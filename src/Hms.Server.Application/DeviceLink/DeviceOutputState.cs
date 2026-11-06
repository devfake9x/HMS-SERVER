using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Text.Json;
using Hms.Server.Application.Authorization;
using Hms.Server.Application.Commands;
using Hms.Server.Application.Realtime;
using Hms.Server.Domain.Commands;
using Hms.Server.Domain.Devices;
using Microsoft.Extensions.Logging;

namespace Hms.Server.Application.DeviceLink;

public sealed record OutputChannelState(int Channel, bool State, string? Mode, bool? PulseActive);

/// <summary>Relay outputs as the device last reported them in a verified <c>control.get_status</c>
/// reply. <see cref="IsStale"/> means the state may no longer be true and must be read again.</summary>
public sealed record DeviceOutputState(
    IReadOnlyList<OutputChannelState> Channels,
    DateTimeOffset ReadAt,
    bool IsStale,
    string? StaleReason);

public static class OutputStaleReasons
{
    public const string DeviceOffline = "device_offline";
    public const string DeviceRebooted = "device_rebooted";
    public const string OutputCommandExecuted = "output_command_executed";
    public const string OutputCommandOutcomeUnknown = "output_command_outcome_unknown";
}

/// <summary>Written only by <see cref="DeviceReplyProjection"/>: heartbeats, device events and
/// manual telemetry never reach it.</summary>
public interface IDeviceOutputStateStore
{
    DeviceOutputState? Get(DeviceId deviceId);
    void Set(DeviceId deviceId, IReadOnlyList<OutputChannelState> channels, DateTimeOffset readAt);

    /// <summary>Keeps the last channels but flags them; false when nothing was known or the state
    /// was already stale for the same reason.</summary>
    bool MarkStale(DeviceId deviceId, string reason);

    /// <summary>Forgets the state, e.g. when the device is bound to another physical device.</summary>
    void Clear(DeviceId deviceId);
}

public sealed class InMemoryDeviceOutputStateStore : IDeviceOutputStateStore
{
    private readonly ConcurrentDictionary<DeviceId, DeviceOutputState> _states = new();

    public DeviceOutputState? Get(DeviceId deviceId) => _states.GetValueOrDefault(deviceId);

    public void Set(DeviceId deviceId, IReadOnlyList<OutputChannelState> channels, DateTimeOffset readAt) =>
        _states[deviceId] = new DeviceOutputState(channels, readAt, false, null);

    public void Clear(DeviceId deviceId) => _states.TryRemove(deviceId, out _);

    public bool MarkStale(DeviceId deviceId, string reason)
    {
        while (_states.TryGetValue(deviceId, out var current))
        {
            if (current.IsStale && current.StaleReason == reason)
                return false;
            if (_states.TryUpdate(deviceId, current with { IsStale = true, StaleReason = reason }, current))
                return true;
        }

        return false;
    }
}

/// <summary>
/// Turns verified device replies into output state, and flags that state whenever a command may
/// have changed the outputs without a read-back. The realtime event carries no channel data: the
/// state is read through an endpoint that checks <c>gpio.do_view</c>.
/// </summary>
public sealed class DeviceReplyProjection(
    IDeviceOutputStateStore outputs,
    IAuthorizedEventPublisher events,
    IAuthorizationEpochStore epochs,
    IPermissionStore permissions,
    ILogger<DeviceReplyProjection> logger)
{
    public const string OutputsEvent = "outputs";

    public async ValueTask ApplyAsync(
        CommandExecutionPlan plan,
        DeviceCommandResponse response,
        CommandExecutionResult result,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken = default)
    {
        var deviceId = plan.Request.DeviceId;
        if (plan.Request.Type == "control" && plan.Request.Action == "get_status" && response.Success)
        {
            var channels = ParseChannels(response.Data) ?? throw new InvalidOperationException("OUTPUT_STATUS_UNREADABLE");
            outputs.Set(deviceId, channels, receivedAt);
            await PublishAsync(deviceId, cancellationToken);
            return;
        }

        if (plan.Definition.Class != CommandClass.Physical)
            return;

        // Executed: the outputs changed and have not been read back yet. Only a refusal before the
        // handler ran (spec R3a) proves nothing happened; an error from the handler, the dedup
        // refusal (a repeat may have run) or an accepted-but-running reply leave it unknown (R3).
        if (result.Status == CommandExecutionStatus.Executed)
            await MarkStaleAsync(deviceId, OutputStaleReasons.OutputCommandExecuted, cancellationToken);
        else if (!RefusedBeforeHandler(response.Error))
            await MarkStaleAsync(deviceId, OutputStaleReasons.OutputCommandOutcomeUnknown, cancellationToken);
    }

    // Spec R3a/§5 admission refusals. UNAUTHORIZED is left out on purpose: it is also the
    // duplicate-id refusal, and the first copy may have run.
    private static readonly FrozenSet<string> AdmissionRefusals = new[]
    {
        "expired", "future_timestamp", "LANE_FULL", "unknown_action", "confirm_required", "permission_unmapped",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static bool RefusedBeforeHandler(string? error) =>
        error is not null && (AdmissionRefusals.Contains(error) || error.StartsWith("schema:", StringComparison.Ordinal));

    public async ValueTask MarkStaleAsync(DeviceId deviceId, string reason, CancellationToken cancellationToken = default)
    {
        if (outputs.MarkStale(deviceId, reason))
            await PublishAsync(deviceId, cancellationToken);
    }

    private async ValueTask PublishAsync(DeviceId deviceId, CancellationToken cancellationToken)
    {
        var state = outputs.Get(deviceId);
        try
        {
            await events.PublishAsync(new AuthorizedDeviceEvent(
                deviceId, epochs.Current(deviceId), permissions.PolicyVersion, OutputsEvent,
                new Dictionary<string, object?>
                {
                    ["readAt"] = state?.ReadAt,
                    ["stale"] = state?.IsStale,
                    ["staleReason"] = state?.StaleReason
                }), cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message == "REALTIME_QUEUE_FULL")
        {
            logger.LogWarning("Realtime queue full; outputs change for device {DeviceId} not pushed", deviceId.Value);
        }
    }

    // spec §6.4: channels[{channel,state,mode,totalCommands?,pulseCount?,pulseActive?}]
    private static IReadOnlyList<OutputChannelState>? ParseChannels(JsonElement? data)
    {
        if (data is not { ValueKind: JsonValueKind.Object } body ||
            !body.TryGetProperty("channels", out var channels) || channels.ValueKind != JsonValueKind.Array)
            return null;

        var parsed = new List<OutputChannelState>();
        foreach (var row in channels.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object ||
                !row.TryGetProperty("channel", out var channel) || !channel.TryGetInt32(out var number) || number < 1 ||
                !row.TryGetProperty("state", out var state) || state.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return null;
            var mode = row.TryGetProperty("mode", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
            bool? pulseActive = row.TryGetProperty("pulseActive", out var p) && p.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? p.GetBoolean()
                : null;
            parsed.Add(new OutputChannelState(number, state.GetBoolean(), mode, pulseActive));
        }

        return parsed.OrderBy(x => x.Channel).ToArray();
    }
}
