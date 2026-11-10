using Hms.Server.Application.Commands;
using Hms.Server.Application.DeviceLink;
using Hms.Server.Domain.Devices;
using Microsoft.EntityFrameworkCore;

namespace Hms.Server.Infrastructure.Persistence;

public sealed class EfCommandAuditStore(
    ApplicationDbContext db,
    TimeSpan? inFlightLease = null,
    Func<DateTimeOffset>? clock = null) : ICommandAuditStore
{
    public static readonly TimeSpan DefaultInFlightLease = TimeSpan.FromMinutes(5);
    private readonly TimeSpan _inFlightLease = inFlightLease ?? DefaultInFlightLease;
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public CommandExecutionResult? Find(DeviceId deviceId, string correlationId) =>
        db.CommandAudits.AsNoTracking()
            .SingleOrDefault(x => x.DeviceId == deviceId.Value && x.CorrelationId == correlationId) is { } record
                ? ToResultOrNull(record)
                : null;

    public CommandAuditPlan? FindPlan(DeviceId deviceId, string correlationId) =>
        db.CommandAudits.AsNoTracking()
            .SingleOrDefault(x => x.DeviceId == deviceId.Value && x.CorrelationId == correlationId) is { } record
                ? ToPlan(record)
                : null;

    public int CancelPendingForDevice(DeviceId deviceId, string code = "DEVICE_REVOKED")
    {
        var pending = db.CommandAudits
            .Where(x => x.DeviceId == deviceId.Value && x.Status == "IN_FLIGHT")
            .ToList();
        foreach (var record in pending)
        {
            record.Status = nameof(CommandExecutionStatus.Failed);
            record.Code = code;
        }

        if (pending.Count > 0)
            db.SaveChanges();
        return pending.Count;
    }

    public int FailUnfinished(string code)
    {
        var published = nameof(CommandExecutionStatus.Published);
        var failed = nameof(CommandExecutionStatus.Failed);
        const string inFlight = "IN_FLIGHT";
        var detail = CommandTransportOutcome.AfterRestartDetail;
        // A reboot the device replied to keeps its status (it did reply); only the watch for the
        // device coming back is lost, so it gets its own code and text.
        var scheduled = DeviceRebootCodes.Scheduled;
        var unknown = DeviceRebootCodes.OutcomeUnknown;
        var watchLost = CommandTransportOutcome.RebootWatchLostDetail;
        if (db.Database.IsRelational())
        {
            var unfinished = db.CommandAudits
                .Where(x => x.Status == published || x.Status == inFlight)
                .ExecuteUpdate(setters => setters
                    .SetProperty(x => x.Status, failed)
                    .SetProperty(x => x.Code, code)
                    .SetProperty(x => x.Detail, detail));
            var watching = db.CommandAudits
                .Where(x => x.Code == scheduled)
                .ExecuteUpdate(setters => setters
                    .SetProperty(x => x.Code, unknown)
                    .SetProperty(x => x.Detail, watchLost));
            return unfinished + watching;
        }

        var rows = db.CommandAudits.Where(x => x.Status == published || x.Status == inFlight).ToList();
        foreach (var row in rows)
        {
            row.Status = failed;
            row.Code = code;
            row.Detail = detail;
        }

        var watched = db.CommandAudits.Where(x => x.Code == scheduled).ToList();
        foreach (var row in watched)
        {
            row.Code = unknown;
            row.Detail = watchLost;
        }

        if (rows.Count + watched.Count > 0)
            db.SaveChanges();
        return rows.Count + watched.Count;
    }

    public async ValueTask<CommandAuditReservation> ReserveAsync(
        CommandExecutionPlan plan,
        string requestFingerprint,
        CancellationToken cancellationToken = default)
    {
        var existing = await db.CommandAudits.AsNoTracking()
            .SingleOrDefaultAsync(x => x.DeviceId == plan.Request.DeviceId.Value &&
                                       x.CorrelationId == plan.Request.CorrelationId,
                cancellationToken);
        if (existing is not null)
            return ToReservation(existing, requestFingerprint);

        var candidate = new CommandAuditRecord
        {
            Id = Guid.NewGuid(),
            DeviceId = plan.Request.DeviceId.Value,
            CorrelationId = plan.Request.CorrelationId,
            Status = "IN_FLIGHT",
            Code = "COMMAND_IN_FLIGHT",
            Type = plan.Request.Type,
            Action = plan.Request.Action,
            RequestFingerprint = requestFingerprint,
            KeyGeneration = plan.KeyGeneration,
            ExpiresAt = plan.ExpiresAt,
            CreatedAt = _clock(),
            IssuedBy = plan.Request.UserId.Value,
            PayloadSummary = SummarizePayload(plan)
        };

        db.CommandAudits.Add(candidate);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return new CommandAuditReservation(
                CommandAuditReservationStatus.Claimed,
                ToPlan(candidate));
        }
        catch (DbUpdateException)
        {
            // The unique device/correlation index is the cross-request claim.
            // Reload the winner so a retry cannot send a second side effect.
            db.ChangeTracker.Clear();
            existing = await db.CommandAudits.AsNoTracking()
                .SingleOrDefaultAsync(x => x.DeviceId == plan.Request.DeviceId.Value &&
                                           x.CorrelationId == plan.Request.CorrelationId,
                    cancellationToken);
            if (existing is null)
                throw;

            return ToReservation(existing, requestFingerprint);
        }
    }

    public async ValueTask RecordAsync(
        CommandExecutionPlan plan,
        CommandExecutionResult result,
        CancellationToken cancellationToken = default)
    {
        var record = await db.CommandAudits.AsNoTracking()
            .SingleOrDefaultAsync(x => x.DeviceId == plan.Request.DeviceId.Value &&
                                       x.CorrelationId == plan.Request.CorrelationId,
                cancellationToken);
        if (record is null)
        {
            // A completion is valid only for a previously claimed reservation.
            // In particular, do not stage an incomplete Added entity: a later
            // SaveChanges on this scope must not manufacture an audit row that
            // bypassed the reservation/idempotency fence.
            return;
        }

        // Revocation is a terminal server-side fence. The predicate is part of
        // the database write, so a completion that races with revoke cannot
        // overwrite DEVICE_REVOKED after the revoke transaction commits.
        // The device can reply before the sender records its publish result; that late transport
        // status only applies while the row is still unfinished, so it never overwrites a device outcome.
        var publishedOnly = CommandTransportOutcome.IsTransportOnly(result);
        if (db.Database.IsRelational())
        {
            var updated = await db.CommandAudits
                .Where(x => x.DeviceId == plan.Request.DeviceId.Value &&
                            x.CorrelationId == plan.Request.CorrelationId &&
                            x.Code != "DEVICE_REVOKED" &&
                            (!publishedOnly || x.Status == "IN_FLIGHT"))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, result.Status.ToString())
                    .SetProperty(x => x.Code, result.Code)
                    .SetProperty(x => x.Detail, Truncate(result.Detail, CommandAuditRecord.MaxDetailLength)), cancellationToken);

            if (updated == 0)
                return;
        }
        else
        {
            // The in-memory provider has no atomic SQL update surface. Keep the
            // development/test fallback fail-closed for an already revoked row. The row is
            // loaded tracked: the reservation in the same scope may still track it.
            var current = await db.CommandAudits
                .SingleAsync(x => x.DeviceId == plan.Request.DeviceId.Value &&
                                  x.CorrelationId == plan.Request.CorrelationId,
                    cancellationToken);
            if (current.Code == "DEVICE_REVOKED" || (publishedOnly && current.Status != "IN_FLIGHT"))
                return;

            current.Status = result.Status.ToString();
            current.Code = result.Code;
            current.Detail = Truncate(result.Detail, CommandAuditRecord.MaxDetailLength);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async ValueTask<bool> TryCompleteAsync(
        CommandExecutionPlan plan,
        CommandExecutionResult result,
        IReadOnlySet<string> fromCodes,
        CancellationToken cancellationToken = default)
    {
        var deviceId = plan.Request.DeviceId.Value;
        var correlationId = plan.Request.CorrelationId;
        var codes = fromCodes.ToArray();
        var status = result.Status.ToString();
        var detail = Truncate(result.Detail, CommandAuditRecord.MaxDetailLength);
        if (db.Database.IsRelational())
        {
            // The expected code is part of the database write, so a completion that raced with
            // another one (or with a revoke) cannot replace the code it did not expect.
            var updated = await db.CommandAudits
                .Where(x => x.DeviceId == deviceId && x.CorrelationId == correlationId &&
                            x.Code != "DEVICE_REVOKED" && codes.Contains(x.Code))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, status)
                    .SetProperty(x => x.Code, result.Code)
                    .SetProperty(x => x.Detail, detail), cancellationToken);
            return updated > 0;
        }

        // The in-memory provider has no atomic SQL update: development and tests only.
        var current = await db.CommandAudits
            .SingleOrDefaultAsync(x => x.DeviceId == deviceId && x.CorrelationId == correlationId, cancellationToken);
        if (current is null || current.Code == "DEVICE_REVOKED" || !codes.Contains(current.Code))
            return false;

        current.Status = status;
        current.Code = result.Code;
        current.Detail = detail;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private CommandAuditReservation ToReservation(
        CommandAuditRecord record,
        string requestedFingerprint)
    {
        var plan = ToPlan(record);
        var status = !StringComparer.Ordinal.Equals(record.RequestFingerprint, requestedFingerprint)
            ? CommandAuditReservationStatus.Conflict
            : StringComparer.Ordinal.Equals(record.Status, "IN_FLIGHT")
                ? _clock() - record.CreatedAt >= _inFlightLease
                    ? CommandAuditReservationStatus.StaleInFlight
                    : CommandAuditReservationStatus.InFlight
                : CommandAuditReservationStatus.Completed;
        return new CommandAuditReservation(status, plan, ToResultOrNull(record));
    }

    private static CommandAuditPlan ToPlan(CommandAuditRecord record) =>
        new(new DeviceId(record.DeviceId), record.Type, record.Action,
            record.KeyGeneration, record.ExpiresAt, record.RequestFingerprint);

    private static CommandExecutionResult? ToResultOrNull(CommandAuditRecord record)
    {
        if (StringComparer.Ordinal.Equals(record.Status, "IN_FLIGHT"))
            return null;

        return new CommandExecutionResult(
            Enum.TryParse<CommandExecutionStatus>(record.Status, out var status)
                ? status
                : CommandExecutionStatus.Failed,
            record.Code,
            record.CorrelationId,
            record.Detail,
            KeyGeneration: record.KeyGeneration);
    }

    // What a state-changing command asked for. Of the config writes only the opened ones are kept:
    // their payloads hold no secret, while other writes (Wi-Fi, MQTT) carry passwords and need a
    // per-action redaction before they may be stored.
    private static string? SummarizePayload(CommandExecutionPlan plan)
    {
        var kept = plan.Definition.Class is Domain.Commands.CommandClass.Physical
                       or Domain.Commands.CommandClass.Destructive
                       or Domain.Commands.CommandClass.Flash ||
                   Commands.PinnedCommandCatalog.ConfigWriteActions.Contains((plan.Request.Type, plan.Request.Action));
        if (!kept)
            return null;
        try
        {
            // A dry-run and a real flash command share a payload ({}), and the table has no column
            // for the difference; without this a timed-out preview reads like a timed-out reboot.
            object summary = plan.Definition.Class == Domain.Commands.CommandClass.Flash
                ? new Dictionary<string, object?> { ["dry_run"] = plan.Request.DryRun, ["payload"] = plan.Request.Payload }
                : plan.Request.Payload;
            return Truncate(System.Text.Json.JsonSerializer.Serialize(summary), CommandAuditRecord.MaxPayloadSummaryLength);
        }
        catch (Exception ex) when (ex is NotSupportedException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
