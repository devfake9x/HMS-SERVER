using Hms.Server.Domain.Authorization;
using Hms.Server.Domain.Commands;
using Hms.Server.Domain.Devices;
using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Hms.Server.Application.Commands;

public sealed record DeviceCommandRequest(
    DeviceId DeviceId,
    UserId UserId,
    string Type,
    string Action,
    IReadOnlyDictionary<string, object?> Payload,
    string CorrelationId,
    DateTimeOffset RequestedAt,
    bool DryRun = false,
    string? CatalogVersion = null,
    string? CatalogHash = null,
    string? Confirm = null);

public sealed record CommandExecutionPlan(
    DeviceCommandRequest Request,
    CommandDefinition Definition,
    int KeyGeneration,
    DateTimeOffset ExpiresAt);

public enum CommandExecutionStatus
{
    Validated,
    Recorded,
    MqttDeferred,
    TransportNotConfigured,
    ContractUnavailable,
    Published,
    Acknowledged,
    Executed,
    Failed
}

public sealed record CommandExecutionResult(
    CommandExecutionStatus Status,
    string Code,
    string CorrelationId,
    string? Detail = null,
    int? KeyGeneration = null)
{
    public bool IsSuccess => Status is CommandExecutionStatus.Validated
        or CommandExecutionStatus.Recorded
        or CommandExecutionStatus.MqttDeferred;
}

public sealed record PreparedDeviceCommand(
    DeviceId DeviceId,
    string? Topic,
    ReadOnlyMemory<byte> Payload,
    int KeyGeneration,
    bool CanPublish,
    string? FailureCode = null);

public interface ICommandCatalog
{
    CommandDefinition? Find(string type, string action);
    IReadOnlyList<CommandDefinition> List();

    /// <summary>Every firmware permission the catalog uses: fixed row permissions plus each
    /// config domain's view and edit permission.</summary>
    IReadOnlySet<string> FirmwarePermissions { get; }
}

public sealed record CommandCatalogMetadata(
    string CatalogVersion,
    string CatalogHash,
    bool IsTrusted = true);

public interface ICommandCatalogMetadata
{
    CommandCatalogMetadata Metadata { get; }
}

public interface ICommandProtocolAdapter
{
    ValueTask<PreparedDeviceCommand> PrepareAsync(
        CommandExecutionPlan plan,
        CancellationToken cancellationToken = default);
}

public interface IDeviceCommandTransport
{
    ValueTask<CommandExecutionResult> SendAsync(
        CommandExecutionPlan plan,
        PreparedDeviceCommand prepared,
        CancellationToken cancellationToken = default);
}

public interface ICommandAuditStore
{
    CommandExecutionResult? Find(DeviceId deviceId, string correlationId);
    CommandAuditPlan? FindPlan(DeviceId deviceId, string correlationId);

    /// <summary>Moves unfinished commands to a terminal, non-retryable revoke outcome.</summary>
    int CancelPendingForDevice(DeviceId deviceId, string code = "DEVICE_REVOKED");

    ValueTask<CommandAuditReservation> ReserveAsync(
        CommandExecutionPlan plan,
        string requestFingerprint,
        CancellationToken cancellationToken = default);

    ValueTask RecordAsync(
        CommandExecutionPlan plan,
        CommandExecutionResult result,
        CancellationToken cancellationToken = default);
}

public sealed record CommandAuditPlan(
    DeviceId DeviceId,
    string Type,
    string Action,
    int KeyGeneration,
    DateTimeOffset ExpiresAt,
    string RequestFingerprint);

public enum CommandAuditReservationStatus
{
    Claimed,
    Completed,
    InFlight,
    StaleInFlight,
    Conflict
}

public sealed record CommandAuditReservation(
    CommandAuditReservationStatus Status,
    CommandAuditPlan Plan,
    CommandExecutionResult? ExistingResult = null);

public static class CommandRequestFingerprint
{
    public static string Compute(DeviceCommandRequest request)
    {
        // Correlation identifies the command intent; retries may receive a
        // fresh server timestamp but must reuse the original execution plan.
        var payloadNode = JsonNode.Parse(JsonSerializer.Serialize(request.Payload));
        var canonicalPayload = SortObjects(payloadNode);
        var canonical = JsonSerializer.Serialize(new
        {
            request.Type,
            request.Action,
            Payload = canonicalPayload,
            request.DryRun
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static JsonNode? SortObjects(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(
            obj.OrderBy(x => x.Key, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => SortObjects(x.Value), StringComparer.Ordinal)),
        JsonArray array => new JsonArray(array.Select(SortObjects).ToArray()),
        _ => node?.DeepClone()
    };
}

public sealed class InMemoryCommandCatalog : ICommandCatalog, ICommandCatalogMetadata
{
    public const string ProvisionalCatalogVersion = "in-memory-test-catalog";
    public const string ProvisionalCatalogHash = "in-memory-test-catalog-hash";

    private readonly IReadOnlyDictionary<(string Type, string Action), CommandDefinition> _definitions;

    public InMemoryCommandCatalog(
        IEnumerable<CommandDefinition> definitions,
        CommandCatalogMetadata? metadata = null)
    {
        _definitions = definitions.ToDictionary(x => (x.Type, x.Action));
        Metadata = metadata ?? new CommandCatalogMetadata(ProvisionalCatalogVersion, ProvisionalCatalogHash);
        FirmwarePermissions = _definitions.Values.SelectMany(PermissionsOf).ToFrozenSet(StringComparer.Ordinal);
    }

    public CommandCatalogMetadata Metadata { get; }

    public IReadOnlySet<string> FirmwarePermissions { get; }

    public CommandDefinition? Find(string type, string action) =>
        _definitions.TryGetValue((type, action), out var definition) ? definition : null;

    public IReadOnlyList<CommandDefinition> List() => _definitions.Values
        .OrderBy(x => x.Type, StringComparer.Ordinal)
        .ThenBy(x => x.Action, StringComparer.Ordinal)
        .ToArray();

    private static IEnumerable<string> PermissionsOf(CommandDefinition definition)
    {
        if (definition.RequiredPermission is not null)
            yield return definition.RequiredPermission;

        foreach (var permission in definition.PermissionByDomain?.Values ?? [])
        {
            yield return permission.ViewPermission;
            yield return permission.EditPermission;
        }
    }
}

public sealed class InMemoryCommandAuditStore : ICommandAuditStore
{
    private sealed record Entry(
        CommandExecutionPlan Plan,
        string Fingerprint,
        CommandExecutionResult? Result,
        DateTimeOffset ClaimedAt);

    public static readonly TimeSpan DefaultInFlightLease = TimeSpan.FromMinutes(5);
    private readonly TimeSpan _inFlightLease;
    private readonly Func<DateTimeOffset> _clock;

    private readonly object _gate = new();
    private readonly Dictionary<(DeviceId DeviceId, string CorrelationId), Entry> _records = [];

    public InMemoryCommandAuditStore(
        TimeSpan? inFlightLease = null,
        Func<DateTimeOffset>? clock = null)
    {
        _inFlightLease = inFlightLease ?? DefaultInFlightLease;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public IReadOnlyList<(CommandExecutionPlan Plan, CommandExecutionResult Result)> Records
    {
        get
        {
            lock (_gate)
            {
                return _records.Values
                    .Where(x => x.Result is not null)
                    .Select(x => (x.Plan, x.Result!))
                    .ToArray();
            }
        }
    }

    public CommandExecutionResult? Find(DeviceId deviceId, string correlationId)
    {
        lock (_gate)
        {
            return _records.TryGetValue((deviceId, correlationId), out var entry)
                ? entry.Result
                : null;
        }
    }

    public CommandAuditPlan? FindPlan(DeviceId deviceId, string correlationId)
    {
        lock (_gate)
        {
            return _records.TryGetValue((deviceId, correlationId), out var entry)
                ? ToAuditPlan(entry.Plan, entry.Fingerprint)
                : null;
        }
    }

    public int CancelPendingForDevice(DeviceId deviceId, string code = "DEVICE_REVOKED")
    {
        lock (_gate)
        {
            var cancelled = 0;
            foreach (var key in _records.Keys.Where(key => key.DeviceId == deviceId).ToArray())
            {
                var entry = _records[key];
                if (entry.Result is not null)
                    continue;

                var result = new CommandExecutionResult(
                    CommandExecutionStatus.Failed,
                    code,
                    entry.Plan.Request.CorrelationId,
                    KeyGeneration: entry.Plan.KeyGeneration);
                _records[key] = entry with { Result = result };
                cancelled++;
            }

            return cancelled;
        }
    }

    public ValueTask<CommandAuditReservation> ReserveAsync(
        CommandExecutionPlan plan,
        string requestFingerprint,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var key = (plan.Request.DeviceId, plan.Request.CorrelationId);
            if (!_records.TryGetValue(key, out var existing))
            {
                _records[key] = new Entry(plan, requestFingerprint, null, _clock());
                return ValueTask.FromResult(new CommandAuditReservation(
                    CommandAuditReservationStatus.Claimed,
                    ToAuditPlan(plan, requestFingerprint)));
            }

            var status = !StringComparer.Ordinal.Equals(existing.Fingerprint, requestFingerprint)
                ? CommandAuditReservationStatus.Conflict
                : existing.Result is null
                    ? _clock() - existing.ClaimedAt >= _inFlightLease
                        ? CommandAuditReservationStatus.StaleInFlight
                        : CommandAuditReservationStatus.InFlight
                    : CommandAuditReservationStatus.Completed;
            return ValueTask.FromResult(new CommandAuditReservation(
                status,
                ToAuditPlan(existing.Plan, existing.Fingerprint),
                existing.Result));
        }
    }

    public ValueTask RecordAsync(
        CommandExecutionPlan plan,
        CommandExecutionResult result,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var key = (plan.Request.DeviceId, plan.Request.CorrelationId);
            if (_records.TryGetValue(key, out var existing))
            {
                // A revoke fence is terminal. A late transport completion must
                // not overwrite the explicit non-retryable outcome.
                if (StringComparer.Ordinal.Equals(existing.Result?.Code, "DEVICE_REVOKED"))
                    return ValueTask.CompletedTask;
                _records[key] = existing with { Result = result };
            }
            else
                _records[key] = new Entry(plan, CommandRequestFingerprint.Compute(plan.Request), result, _clock());
        }
        return ValueTask.CompletedTask;
    }

    private static CommandAuditPlan ToAuditPlan(CommandExecutionPlan plan, string fingerprint) =>
        new(plan.Request.DeviceId, plan.Request.Type, plan.Request.Action,
            plan.KeyGeneration, plan.ExpiresAt, fingerprint);
}
