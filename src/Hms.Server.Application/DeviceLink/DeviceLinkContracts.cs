using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Text.Json;
using Hms.Server.Application.Commands;
using Hms.Server.Domain.Devices;

namespace Hms.Server.Application.DeviceLink;

/// <summary>
/// Where a device lives on the broker. The firmware publishes under
/// <c>B = TopicPrefix/MqttDeviceId</c> (MQTT spec §1, §2); the prefix is per deployment and is
/// never assumed. <see cref="KeyVersion"/> pins the device's command key version when known.
/// </summary>
/// <remarks><see cref="AllowedOutputChannels"/> lists the output channels the server may drive;
/// null or empty allows none, so physical control is closed until someone sets it.</remarks>
public sealed record DeviceMqttBinding(
    DeviceId DeviceId,
    string TopicPrefix,
    string MqttDeviceId,
    uint? KeyVersion = null,
    IReadOnlyList<int>? AllowedOutputChannels = null)
{
    public const int MaxOutputChannels = 32;

    /// <summary>Highest channel number accepted; keeps the stored list within its column.</summary>
    public const int MaxOutputChannelNumber = 255;

    /// <summary>Validates a channel allowlist: positive, no duplicates, bounded. Empty is valid
    /// and means "none".</summary>
    public static IReadOnlyList<int> ValidateOutputChannels(IEnumerable<int>? channels)
    {
        var list = (channels ?? []).ToArray();
        if (list.Length > MaxOutputChannels || list.Any(c => c is < 1 or > MaxOutputChannelNumber) ||
            list.Distinct().Count() != list.Length)
            throw new ArgumentException("OUTPUT_CHANNELS_INVALID", nameof(channels));
        return list.Order().ToArray();
    }

    public string TopicBase => $"{TopicPrefix}/{MqttDeviceId}";

    public static DeviceMqttBinding Create(DeviceId deviceId, string topicPrefix, string mqttDeviceId, uint? keyVersion)
    {
        var prefix = topicPrefix?.Trim() ?? string.Empty;
        var id = mqttDeviceId?.Trim() ?? string.Empty;
        // Topic segments the firmware builds from configured strings; wildcards, empty segments and
        // control characters would change which messages the server matches or publishes to.
        if (prefix.Length is 0 or > 64 || prefix.StartsWith('/') || prefix.EndsWith('/') ||
            prefix.Split('/').Any(segment => segment.Length == 0 || !segment.All(IsTopicChar)))
            throw new ArgumentException("MQTT_TOPIC_PREFIX_INVALID", nameof(topicPrefix));
        if (id.Length is 0 or > 63 || !id.All(IsTopicChar))
            throw new ArgumentException("MQTT_DEVICE_ID_INVALID", nameof(mqttDeviceId));
        if (keyVersion == 0)
            throw new ArgumentException("MQTT_KEY_VERSION_INVALID", nameof(keyVersion));
        return new DeviceMqttBinding(deviceId, prefix, id, keyVersion);
    }

    private static bool IsTopicChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.';
}

public interface IDeviceMqttBindingStore
{
    DeviceMqttBinding? Find(DeviceId deviceId);
    IReadOnlyList<DeviceMqttBinding> List();

    /// <summary>Adds or replaces a device's topic binding. Throws <c>MQTT_TOPIC_BASE_IN_USE</c> when
    /// another device already owns the same topic base. The output-channel allowlist is taken from
    /// <paramref name="binding"/> only for a new row; an existing row keeps its own, which changes
    /// only through <see cref="SetAllowedOutputChannels"/>, so a re-bind never writes back a list
    /// read before an administrator changed it.</summary>
    void Upsert(DeviceMqttBinding binding);

    /// <summary>Writes only the allowlist (null or empty = none). False when the device has no binding.</summary>
    bool SetAllowedOutputChannels(DeviceId deviceId, IReadOnlyList<int>? channels);
}

public sealed class InMemoryDeviceMqttBindingStore : IDeviceMqttBindingStore
{
    private readonly object _gate = new();
    private readonly Dictionary<DeviceId, DeviceMqttBinding> _items = [];

    public DeviceMqttBinding? Find(DeviceId deviceId)
    {
        lock (_gate) return _items.GetValueOrDefault(deviceId);
    }

    public IReadOnlyList<DeviceMqttBinding> List()
    {
        lock (_gate) return _items.Values.ToArray();
    }

    public void Upsert(DeviceMqttBinding binding)
    {
        lock (_gate)
        {
            if (_items.Values.Any(x => x.DeviceId != binding.DeviceId &&
                                       StringComparer.Ordinal.Equals(x.TopicBase, binding.TopicBase)))
                throw new InvalidOperationException("MQTT_TOPIC_BASE_IN_USE");
            _items[binding.DeviceId] = _items.TryGetValue(binding.DeviceId, out var existing)
                ? binding with { AllowedOutputChannels = existing.AllowedOutputChannels }
                : binding;
        }
    }

    public bool SetAllowedOutputChannels(DeviceId deviceId, IReadOnlyList<int>? channels)
    {
        lock (_gate)
        {
            if (!_items.TryGetValue(deviceId, out var existing))
                return false;
            _items[deviceId] = existing with { AllowedOutputChannels = channels };
            return true;
        }
    }
}

/// <summary>Device topics under the base, as the firmware publishes them (spec §2).</summary>
public enum DeviceInboundKind
{
    Status,
    Connectivity,
    Events,
    Capabilities,
    CommandResponse
}

public static class DeviceLinkTopics
{
    public static readonly FrozenDictionary<string, DeviceInboundKind> InboundSuffixes =
        new Dictionary<string, DeviceInboundKind>(StringComparer.Ordinal)
        {
            ["status"] = DeviceInboundKind.Status,
            ["status/connectivity"] = DeviceInboundKind.Connectivity,
            ["status/events"] = DeviceInboundKind.Events,
            ["capabilities"] = DeviceInboundKind.Capabilities,
            ["cmd/res"] = DeviceInboundKind.CommandResponse
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static string Command(DeviceMqttBinding binding, string type) => $"{binding.TopicBase}/cmd/{type}";

    public static IEnumerable<string> InboundTopics(DeviceMqttBinding binding) =>
        InboundSuffixes.Keys.Select(suffix => $"{binding.TopicBase}/{suffix}");
}

/// <summary>What the broker side of the link knows about one device.</summary>
public sealed record DeviceLinkSnapshot(
    DeviceId DeviceId,
    string? Connectivity,
    DateTimeOffset? ConnectivityAt,
    DateTimeOffset? LastHeartbeatAt,
    DateTimeOffset? CapabilitiesAt,
    int CapabilityCount);

public interface IDeviceLinkStateStore
{
    void RecordHeartbeat(DeviceId deviceId, DateTimeOffset receivedAt);
    void RecordConnectivity(DeviceId deviceId, string state, DateTimeOffset receivedAt);
    void RecordCapabilities(DeviceId deviceId, DeviceCapabilities capabilities, DateTimeOffset receivedAt);
    DeviceLinkSnapshot? Get(DeviceId deviceId);
    DeviceCapabilities? GetCapabilities(DeviceId deviceId);
}

/// <summary>The retained <c>B/capabilities</c> document. <c>available</c> only says a handler is
/// registered, not that hardware is ready (spec §2).</summary>
public sealed record DeviceCapabilities(
    IReadOnlyDictionary<(string Type, string Action), bool> Availability,
    string RawJson)
{
    public bool? IsAvailable(string type, string action) =>
        Availability.TryGetValue((type, action), out var available) ? available : null;

    public static DeviceCapabilities? TryParse(ReadOnlySpan<byte> payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload.ToArray());
            if (!document.RootElement.TryGetProperty("actions", out var actions) ||
                actions.ValueKind != JsonValueKind.Array)
                return null;

            var availability = new Dictionary<(string Type, string Action), bool>();
            foreach (var row in actions.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object ||
                    !row.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String ||
                    !row.TryGetProperty("action", out var action) || action.ValueKind != JsonValueKind.String)
                    continue;
                var available = row.TryGetProperty("available", out var flag) && flag.ValueKind == JsonValueKind.True;
                availability[(type.GetString()!, action.GetString()!)] = available;
            }

            return new DeviceCapabilities(availability, document.RootElement.GetRawText());
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed class InMemoryDeviceLinkStateStore : IDeviceLinkStateStore
{
    private sealed record Entry(
        string? Connectivity,
        DateTimeOffset? ConnectivityAt,
        DateTimeOffset? LastHeartbeatAt,
        DeviceCapabilities? Capabilities,
        DateTimeOffset? CapabilitiesAt);

    private readonly ConcurrentDictionary<DeviceId, Entry> _entries = new();

    public void RecordHeartbeat(DeviceId deviceId, DateTimeOffset receivedAt) =>
        _entries.AddOrUpdate(deviceId,
            _ => new Entry(null, null, receivedAt, null, null),
            (_, current) => current with { LastHeartbeatAt = receivedAt });

    public void RecordConnectivity(DeviceId deviceId, string state, DateTimeOffset receivedAt) =>
        _entries.AddOrUpdate(deviceId,
            _ => new Entry(state, receivedAt, null, null, null),
            (_, current) => current with { Connectivity = state, ConnectivityAt = receivedAt });

    public void RecordCapabilities(DeviceId deviceId, DeviceCapabilities capabilities, DateTimeOffset receivedAt) =>
        _entries.AddOrUpdate(deviceId,
            _ => new Entry(null, null, null, capabilities, receivedAt),
            (_, current) => current with { Capabilities = capabilities, CapabilitiesAt = receivedAt });

    public DeviceLinkSnapshot? Get(DeviceId deviceId) =>
        _entries.TryGetValue(deviceId, out var entry)
            ? new DeviceLinkSnapshot(deviceId, entry.Connectivity, entry.ConnectivityAt, entry.LastHeartbeatAt,
                entry.CapabilitiesAt, entry.Capabilities?.Availability.Count ?? 0)
            : null;

    public DeviceCapabilities? GetCapabilities(DeviceId deviceId) =>
        _entries.TryGetValue(deviceId, out var entry) ? entry.Capabilities : null;
}

/// <summary>State of the server's own broker session, for status endpoints.</summary>
public sealed record DeviceLinkTransportStatus(
    bool Enabled,
    bool Connected,
    string? Broker,
    DateTimeOffset? ConnectedSince,
    string? LastError);

public interface IDeviceLinkTransportStatus
{
    DeviceLinkTransportStatus Current { get; }
}

public sealed class DisabledDeviceLinkTransportStatus : IDeviceLinkTransportStatus
{
    public DeviceLinkTransportStatus Current { get; } = new(false, false, null, null, null);
}

/// <summary>A command this server published and still waits a device reply for.</summary>
public sealed record PendingDeviceCommand(
    CommandExecutionPlan Plan,
    DateTimeOffset PublishedAt,
    DateTimeOffset Deadline);

public interface IPendingDeviceCommandRegistry
{
    void Register(PendingDeviceCommand pending);

    /// <summary>Registers a read command unless its device already has <paramref name="maxPerDevice"/>
    /// reads, or its sender <paramref name="maxPerUser"/> reads, that can still run on the device.
    /// The count and the insert are one atomic step.</summary>
    bool TryRegisterRead(PendingDeviceCommand pending, int maxPerDevice, int maxPerUser);

    /// <summary>Registers a write-lane command (<see cref="CommandLanes.IsWrite"/>) unless its device
    /// already has one waiting, is held for a running job, or took <paramref name="maxPerMinute"/>
    /// writes in the last minute. Returns null when registered, otherwise a
    /// <see cref="DeviceLaneCodes"/> refusal. The checks and the insert are one atomic step.</summary>
    string? TryRegisterWrite(PendingDeviceCommand pending, int maxPerMinute);

    /// <summary>Keeps the device's write lane closed until <paramref name="until"/>: the device
    /// accepted a write as a job that is still running (spec §5).</summary>
    void HoldWriteLane(DeviceId deviceId, DateTimeOffset until);

    PendingDeviceCommand? Find(DeviceId deviceId, string correlationId);
    bool Remove(DeviceId deviceId, string correlationId);
    IReadOnlyList<PendingDeviceCommand> TakeExpired(DateTimeOffset now);
}

public static class DeviceLaneCodes
{
    public const string WriteBusy = "DEVICE_WRITE_BUSY";
    public const string WriteRateLimited = "DEVICE_WRITE_RATE_LIMITED";
}

public sealed class InMemoryPendingDeviceCommandRegistry : IPendingDeviceCommandRegistry
{
    private readonly ConcurrentDictionary<(DeviceId DeviceId, string CorrelationId), PendingDeviceCommand> _pending = new();
    // One gate for every lane decision, so a count and its insert, or a sweep and the hold it
    // leaves, are never interleaved with another command's check.
    private readonly object _laneGate = new();
    private readonly Dictionary<DeviceId, DateTimeOffset> _writeHolds = new();
    private readonly Dictionary<DeviceId, Queue<DateTimeOffset>> _recentWrites = new();

    public void Register(PendingDeviceCommand pending) =>
        _pending[(pending.Plan.Request.DeviceId, pending.Plan.Request.CorrelationId)] = pending;

    // A command the device has not answered by its expiry is dropped by the device (spec §3 TTL);
    // the grace covers clock skew and transit.
    private static readonly TimeSpan ReadSlotGrace = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan WriteRateWindow = TimeSpan.FromMinutes(1);

    public bool TryRegisterRead(PendingDeviceCommand pending, int maxPerDevice, int maxPerUser)
    {
        var deviceId = pending.Plan.Request.DeviceId;
        var userId = pending.Plan.Request.UserId;
        lock (_laneGate)
        {
            var live = _pending.Values.Where(x =>
                x.Plan.Request.DeviceId == deviceId &&
                x.Plan.Definition.Class == Domain.Commands.CommandClass.Read &&
                x.Plan.ExpiresAt + ReadSlotGrace > pending.PublishedAt).ToArray();
            if (live.Length >= maxPerDevice || live.Count(x => x.Plan.Request.UserId == userId) >= maxPerUser)
                return false;
            Register(pending);
            return true;
        }
    }

    public string? TryRegisterWrite(PendingDeviceCommand pending, int maxPerMinute)
    {
        var deviceId = pending.Plan.Request.DeviceId;
        var now = pending.PublishedAt;
        lock (_laneGate)
        {
            // A write waiting for its reply holds the lane however old it is: no reply does not
            // mean it did not run (spec R4). The sweep releases it at the server deadline.
            if (_pending.Values.Any(x => x.Plan.Request.DeviceId == deviceId && CommandLanes.IsWrite(x.Plan.Definition.Class)))
                return DeviceLaneCodes.WriteBusy;
            if (_writeHolds.TryGetValue(deviceId, out var heldUntil) && heldUntil > now)
                return DeviceLaneCodes.WriteBusy;

            if (!_recentWrites.TryGetValue(deviceId, out var recent))
                _recentWrites[deviceId] = recent = new Queue<DateTimeOffset>();
            while (recent.Count > 0 && recent.Peek() <= now - WriteRateWindow)
                recent.Dequeue();
            if (recent.Count >= maxPerMinute)
                return DeviceLaneCodes.WriteRateLimited;

            recent.Enqueue(now);
            Register(pending);
            return null;
        }
    }

    public void HoldWriteLane(DeviceId deviceId, DateTimeOffset until)
    {
        lock (_laneGate)
            Hold(deviceId, until);
    }

    public PendingDeviceCommand? Find(DeviceId deviceId, string correlationId) =>
        _pending.GetValueOrDefault((deviceId, correlationId));

    public bool Remove(DeviceId deviceId, string correlationId) =>
        _pending.TryRemove((deviceId, correlationId), out _);

    public IReadOnlyList<PendingDeviceCommand> TakeExpired(DateTimeOffset now)
    {
        var expired = new List<PendingDeviceCommand>();
        lock (_laneGate)
        {
            foreach (var (key, pending) in _pending)
            {
                if (pending.Deadline > now || !_pending.TryRemove(key, out var removed))
                    continue;
                expired.Add(removed);
                // Unanswered by the deadline, a write may still leave the device queue until it
                // expires there: keep the lane closed until then.
                if (CommandLanes.IsWrite(removed.Plan.Definition.Class))
                    Hold(removed.Plan.Request.DeviceId, removed.Plan.ExpiresAt + ReadSlotGrace);
            }
        }

        return expired;
    }

    private void Hold(DeviceId deviceId, DateTimeOffset until)
    {
        if (!_writeHolds.TryGetValue(deviceId, out var current) || current < until)
            _writeHolds[deviceId] = until;
    }
}

/// <summary>A device reply on <c>B/cmd/res</c> whose HMAC was verified (spec §4.1, §5).</summary>
public sealed record DeviceCommandResponse(
    string Id,
    string Type,
    string? Action,
    bool Success,
    long? Timestamp,
    uint? KeyVersion,
    JsonElement? Data,
    string? Error);

/// <summary>Reads the firmware's response wire format; implemented by the protocol adapter side.</summary>
public interface IDeviceResponseCodec
{
    /// <summary>The root <c>id</c>, read without trusting the payload, to find the pending command.</summary>
    string? PeekId(ReadOnlySpan<byte> wire);

    /// <summary>Verifies the <c>res:</c>-prefixed HMAC and parses the reply; null when the
    /// signature is missing or wrong or the JSON is not a response.</summary>
    DeviceCommandResponse? Verify(ReadOnlySpan<byte> wire, ReadOnlySpan<byte> commandKey);
}

/// <summary>Latest device reply per command, kept for the issuing user to read back.</summary>
public sealed record StoredCommandResponse(
    Domain.Authorization.UserId IssuedBy,
    string Type,
    string Action,
    bool Success,
    string? Error,
    string? DataJson,
    DateTimeOffset ReceivedAt);

public interface ICommandResponseStore
{
    void Save(DeviceId deviceId, string correlationId, StoredCommandResponse response);
    StoredCommandResponse? Find(DeviceId deviceId, string correlationId);
}

public sealed class InMemoryCommandResponseStore(int capacity = 2048) : ICommandResponseStore
{
    private readonly object _gate = new();
    private readonly Dictionary<(DeviceId, string), StoredCommandResponse> _items = [];
    private readonly Queue<(DeviceId, string)> _order = [];

    public void Save(DeviceId deviceId, string correlationId, StoredCommandResponse response)
    {
        lock (_gate)
        {
            var key = (deviceId, correlationId);
            if (!_items.ContainsKey(key))
                _order.Enqueue(key);
            _items[key] = response;
            while (_order.Count > capacity)
                _items.Remove(_order.Dequeue());
        }
    }

    public StoredCommandResponse? Find(DeviceId deviceId, string correlationId)
    {
        lock (_gate) return _items.GetValueOrDefault((deviceId, correlationId));
    }
}
