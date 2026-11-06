using Hms.Server.Application.Commands;
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
        if (db.Database.IsRelational())
        {
            return db.CommandAudits
                .Where(x => x.Status == published || x.Status == inFlight)
                .ExecuteUpdate(setters => setters
                    .SetProperty(x => x.Status, failed)
                    .SetProperty(x => x.Code, code)
                    .SetProperty(x => x.Detail, detail));
        }

        var rows = db.CommandAudits.Where(x => x.Status == published || x.Status == inFlight).ToList();
        foreach (var row in rows)
        {
            row.Status = failed;
            row.Code = code;
            row.Detail = detail;
        }

        if (rows.Count > 0)
            db.SaveChanges();
        return rows.Count;
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

    private static string? SummarizePayload(CommandExecutionPlan plan)
    {
        if (plan.Definition.Class is not (Domain.Commands.CommandClass.Physical
            or Domain.Commands.CommandClass.Destructive
            or Domain.Commands.CommandClass.Flash))
            return null;
        try
        {
            return Truncate(System.Text.Json.JsonSerializer.Serialize(plan.Request.Payload), CommandAuditRecord.MaxPayloadSummaryLength);
        }
        catch (Exception ex) when (ex is NotSupportedException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
