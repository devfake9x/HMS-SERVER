using System.Text.Json;
using Hms.Server.Application.Authorization;
using Hms.Server.Application.Commands;
using Hms.Server.Application.Management;
using Hms.Server.Application.Provisioning;
using Hms.Server.Application.Realtime;
using Hms.Server.Domain.Devices;
using Microsoft.Extensions.Logging;

namespace Hms.Server.Application.DeviceLink;

public enum DeviceResponseOutcome
{
    Completed,
    /// <summary>Not a command this server is waiting for: the response topic is shared with
    /// every client that holds the key, and QoS 1 may deliver a reply twice.</summary>
    Ignored,
    /// <summary>Matched a pending command but failed signature or type/action checks.</summary>
    Rejected
}

/// <summary>
/// Closes a published command with the device's reply on <c>B/cmd/res</c> (spec R2, §4.1, §5),
/// or with an outcome-unknown timeout when no reply arrives (spec R4).
/// </summary>
public sealed class DeviceCommandResponseCorrelator(
    IPendingDeviceCommandRegistry pending,
    IDeviceResponseCodec codec,
    IProvisioningSecretGenerator secrets,
    ICommandAuditStore audit,
    ICommandResponseStore responses,
    IAuthorizedEventPublisher events,
    IAuthorizationEpochStore epochs,
    IPermissionStore permissions,
    IDeviceRebootTracker reboots,
    ILogger<DeviceCommandResponseCorrelator> logger,
    DeviceReplyProjection? projection = null)
{
    public const string CommandResultEvent = "command_result";

    /// <summary>How long a write the device accepted as a running job keeps the write lane closed.
    /// The server does not follow the job; the operator's client polls it (spec §5).</summary>
    public static readonly TimeSpan RunningJobWriteHold = TimeSpan.FromSeconds(60);

    public async ValueTask<DeviceResponseOutcome> HandleAsync(
        DeviceId deviceId,
        ReadOnlyMemory<byte> wire,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken = default)
    {
        var id = codec.PeekId(wire.Span);
        if (id is null)
            return DeviceResponseOutcome.Ignored;
        var command = pending.Find(deviceId, id);
        if (command is null)
            return DeviceResponseOutcome.Ignored;

        var plan = command.Plan;
        var keyset = secrets.Find(deviceId, plan.KeyGeneration);
        var response = keyset is null ? null : codec.Verify(wire.Span, keyset.CommandKey);
        if (response is null)
        {
            // Keep the command pending: a forged or corrupted reply must not close it.
            logger.LogWarning("Device {DeviceId} reply for {CorrelationId} failed signature verification", deviceId.Value, id);
            return DeviceResponseOutcome.Rejected;
        }

        // LANE_FULL admission replies carry type "unknown" (spec §5).
        var typeMatches = StringComparer.Ordinal.Equals(response.Type, plan.Request.Type) ||
                          StringComparer.Ordinal.Equals(response.Type, "unknown");
        var actionMatches = response.Action is null ||
                            StringComparer.Ordinal.Equals(response.Action, plan.Request.Action);
        if (!typeMatches || !actionMatches)
        {
            logger.LogWarning("Device {DeviceId} reply for {CorrelationId} names {Type}.{Action}, expected {ExpectedType}.{ExpectedAction}",
                deviceId.Value, id, response.Type, response.Action, plan.Request.Type, plan.Request.Action);
            return DeviceResponseOutcome.Rejected;
        }

        var result = ToResult(response, plan);

        // A write the device took as a still-running job keeps the lane closed for a while. Held
        // before the removal below, so no other write can slip in between.
        if (result.Status == CommandExecutionStatus.Acknowledged && CommandLanes.IsWrite(plan.Definition.Class))
            pending.HoldWriteLane(deviceId, receivedAt + RunningJobWriteHold);

        if (!pending.Remove(deviceId, id))
            return DeviceResponseOutcome.Ignored;

        // A reboot's hold changes only now, after this reply won the removal: a reply the sweep
        // already took must neither start the watch nor release the hold. The hold itself has been
        // in place since the publish, so the lane freeing above opened no gap.
        if (DeviceRebootCodes.IsReboot(plan.Request.Type, plan.Request.Action) && !plan.Request.DryRun)
            result = ApplyRebootReply(plan, result, receivedAt);

        // First, and guarded: whether the outputs may have changed must not depend on the audit
        // write below succeeding, and an unreadable reply must not stop the steps below.
        if (projection is not null)
        {
            try
            {
                await projection.ApplyAsync(plan, response, result, receivedAt, cancellationToken);
            }
            catch (Exception ex) when (ex is InvalidOperationException or JsonException)
            {
                logger.LogWarning("Reply {CorrelationId} from device {DeviceId} not projected: {Error}", id, deviceId.Value, ex.Message);
            }

            // Relays restart with the device (spec §6.4): the state read before is not true any more.
            if (string.Equals(result.Code, DeviceRebootCodes.Scheduled, StringComparison.Ordinal))
                await projection.MarkStaleAsync(deviceId, OutputStaleReasons.DeviceRebootScheduled, cancellationToken);
        }

        // Reply data first: a poll that sees the final status must also find the data.
        responses.Save(deviceId, id, new StoredCommandResponse(
            plan.Request.UserId,
            plan.Request.Type,
            plan.Request.Action,
            response.Success,
            response.Error,
            response.Data?.GetRawText(),
            receivedAt));
        await audit.RecordAsync(plan, result, cancellationToken);
        await PublishResultAsync(plan, result, cancellationToken);

        return DeviceResponseOutcome.Completed;
    }

    public async ValueTask<int> SweepExpiredAsync(
        DateTimeOffset now, bool brokerConnected = true, CancellationToken cancellationToken = default)
    {
        var expired = pending.TakeExpired(now);
        foreach (var command in expired)
        {
            // Taken from the registry already: a failure here must not drop the other commands.
            try
            {
                // No reply does not mean the command did not run (spec R4).
                if (projection is not null && command.Plan.Definition.Class == Domain.Commands.CommandClass.Physical)
                    await projection.MarkStaleAsync(command.Plan.Request.DeviceId, OutputStaleReasons.OutputCommandOutcomeUnknown, cancellationToken);
                var result = new CommandExecutionResult(
                    CommandExecutionStatus.Failed,
                    "DEVICE_RESPONSE_TIMEOUT",
                    command.Plan.Request.CorrelationId,
                    "No device reply before the deadline; outcome unknown. Read device state before retrying with a new correlation id.",
                    command.Plan.KeyGeneration);
                await audit.RecordAsync(command.Plan, result, cancellationToken);
                await PublishResultAsync(command.Plan, result, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Timeout of command {CorrelationId} on device {DeviceId} not recorded",
                    command.Plan.Request.CorrelationId, command.Plan.Request.DeviceId.Value);
            }
        }

        // After the timeouts: a reboot hold that ends with a drop in uptime raises the timeout row
        // written just above, never the other way round.
        foreach (var completion in reboots.TakeDue(now, brokerConnected))
            await CompleteRebootAsync(completion, cancellationToken);

        return expired.Count;
    }

    /// <summary>
    /// Writes what the reboot tracker decided into the audit row, but only while the row still has
    /// one of the codes the decision was based on, so a settled row is never overwritten. A failed
    /// write is handed back to the tracker for the next sweep.
    /// </summary>
    public async ValueTask CompleteRebootAsync(RebootCompletion completion, CancellationToken cancellationToken = default)
    {
        var plan = completion.Plan;
        var result = new CommandExecutionResult(
            CommandExecutionStatus.Executed, completion.Code, plan.Request.CorrelationId, completion.Detail, plan.KeyGeneration);
        try
        {
            if (!await audit.TryCompleteAsync(plan, result, completion.FromCodes, cancellationToken))
            {
                // The reply that makes the tracker watch is recorded a moment after it starts the
                // watch. A decision that lands in that gap (a rebind, a revoke) finds the row still
                // at its publish code: it is not obsolete, only early, so offer it again.
                var current = audit.Find(plan.Request.DeviceId, plan.Request.CorrelationId);
                if (current is null || current.Code == "MQTT_PUBLISHED")
                {
                    reboots.Retry(completion);
                    return;
                }

                logger.LogInformation("Reboot {CorrelationId} of device {DeviceId} already moved past {FromCodes}; {Code} not written",
                    plan.Request.CorrelationId, plan.Request.DeviceId.Value, string.Join('|', completion.FromCodes), completion.Code);
                return;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Audit write {Code} of reboot {CorrelationId} on device {DeviceId} failed; will retry",
                completion.Code, plan.Request.CorrelationId, plan.Request.DeviceId.Value);
            reboots.Retry(completion);
            return;
        }

        try
        {
            await PublishResultAsync(plan, result, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Result {Code} of reboot {CorrelationId} recorded but not pushed", completion.Code, plan.Request.CorrelationId);
        }
    }

    private CommandExecutionResult ApplyRebootReply(CommandExecutionPlan plan, CommandExecutionResult result, DateTimeOffset repliedAt)
    {
        switch (result.Status)
        {
            case CommandExecutionStatus.Executed:
                // The device will restart: the hold becomes a watch. Without a hold to upgrade the
                // reboot cannot be watched, which the row says rather than claiming a schedule.
                return reboots.Upgrade(plan, repliedAt) is { } tracking
                    ? result with
                    {
                        Code = DeviceRebootCodes.Scheduled,
                        Detail = tracking.UptimeBefore is { } before
                            ? $"uptime_before={before.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
                            : "uptime_before=unknown"
                    }
                    : result with { Code = DeviceRebootCodes.OutcomeUnknown, Detail = "tracking=unavailable" };

            case CommandExecutionStatus.Acknowledged:
                // Still running on the device: the hold stays, and a later "observed" is not "no reply".
                reboots.NoteReplyAccepted(plan.Request.DeviceId, plan.Request.CorrelationId);
                return result;

            case CommandExecutionStatus.Failed when result.Code == "DEVICE_REJECTED":
                // The device refused it outright: it will not restart. (Acknowledged keeps the
                // hold: the device may still be running it.)
                reboots.Disarm(plan.Request.DeviceId, plan.Request.CorrelationId);
                return result;

            default:
                return result;
        }
    }

    public static CommandExecutionResult ToResult(DeviceCommandResponse response, CommandExecutionPlan plan)
    {
        var correlationId = plan.Request.CorrelationId;
        if (response.Success)
            return new(CommandExecutionStatus.Executed, "DEVICE_EXECUTED", correlationId, KeyGeneration: plan.KeyGeneration);

        // An executor mutation that is still running: accepted, not failed (spec §5).
        if (response.Data is { ValueKind: JsonValueKind.Object } data &&
            data.TryGetProperty("accepted", out var accepted) && accepted.ValueKind == JsonValueKind.True &&
            data.TryGetProperty("pending", out var isPending) && isPending.ValueKind == JsonValueKind.True)
        {
            var operationId = data.TryGetProperty("operation_id", out var op) ? op.ToString() : null;
            return new(CommandExecutionStatus.Acknowledged, "DEVICE_ACCEPTED", correlationId,
                operationId is null ? null : $"operation_id={operationId}", plan.KeyGeneration);
        }

        // Admission errors sit in error; handler errors keep the esp_err_t name there and the
        // reason in data.error (spec R3).
        var handlerError = response.Data is { ValueKind: JsonValueKind.Object } body &&
                           body.TryGetProperty("error", out var detail) && detail.ValueKind == JsonValueKind.String
            ? detail.GetString()
            : null;
        var reason = handlerError is null ? response.Error : $"{response.Error}: {handlerError}";
        return new(CommandExecutionStatus.Failed, "DEVICE_REJECTED", correlationId, reason, plan.KeyGeneration);
    }

    private async ValueTask PublishResultAsync(
        CommandExecutionPlan plan,
        CommandExecutionResult result,
        CancellationToken cancellationToken)
    {
        // Reads change nothing other viewers need to hear about, and every viewer shares a small
        // realtime backlog that also carries relay changes; the reader polls its own result.
        if (plan.Definition.Class == Domain.Commands.CommandClass.Read)
            return;

        // The realtime group reaches every viewer of the device, so the event carries the outcome
        // only; the reply data stays behind the issuing user's read-back endpoint.
        var payload = new Dictionary<string, object?>
        {
            ["correlationId"] = result.CorrelationId,
            ["type"] = plan.Request.Type,
            ["action"] = plan.Request.Action,
            ["status"] = result.Status.ToString(),
            ["code"] = result.Code,
            ["detail"] = result.Detail
        };
        try
        {
            await events.PublishAsync(new AuthorizedDeviceEvent(
                plan.Request.DeviceId,
                epochs.Current(plan.Request.DeviceId),
                permissions.PolicyVersion,
                CommandResultEvent,
                payload), cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message == "REALTIME_QUEUE_FULL")
        {
            logger.LogWarning("Realtime queue full; command result {CorrelationId} not pushed (still readable via API)", result.CorrelationId);
        }
    }
}

/// <summary>
/// Applies what a device publishes under its topic base (spec §2, §10) to server state. It never
/// changes lifecycle: the broker identity of a message is not proof of the device's identity.
/// </summary>
public sealed class DeviceInboundMessageHandler(
    IDeviceStore devices,
    IDeviceTelemetryStore telemetry,
    IDeviceLinkStateStore linkState,
    IAuthorizedEventPublisher events,
    IAuthorizationEpochStore epochs,
    IPermissionStore permissions,
    DeviceCommandResponseCorrelator correlator,
    IDeviceRebootTracker reboots,
    ILogger<DeviceInboundMessageHandler> logger,
    DeviceReplyProjection? projection = null)
{
    public async ValueTask HandleAsync(
        DeviceId deviceId,
        DeviceInboundKind kind,
        ReadOnlyMemory<byte> payload,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken = default)
    {
        var device = devices.Find(deviceId);
        if (device is null || device.State == DeviceLifecycleState.Revoked)
            return;

        switch (kind)
        {
            case DeviceInboundKind.Status:
                var status = JsonPayload.ToDictionary(payload);
                if (status is null)
                {
                    logger.LogWarning("Device {DeviceId} heartbeat is not a JSON object", deviceId.Value);
                    return;
                }
                TelemetryRelayStates.Remove(status);
                // Uptime going backwards means the device rebooted: outputs may have reset.
                var previousUptime = telemetry.GetLatest(deviceId)?.UptimeSeconds;
                linkState.RecordHeartbeat(deviceId, receivedAt);
                telemetry.Record(deviceId, status);
                if (projection is not null && previousUptime is { } before &&
                    telemetry.GetLatest(deviceId)?.UptimeSeconds is { } after && after < before)
                    await projection.MarkStaleAsync(deviceId, OutputStaleReasons.DeviceRebooted, cancellationToken);
                // The reboot watch reads the uptime from this message, never from the store above:
                // POST /devices/{id}/telemetry and the broker bridge write that store too.
                if (reboots.ObserveHeartbeat(deviceId, DeviceTelemetryParsing.UptimeSeconds(status), receivedAt) is { } rebootDone)
                    await correlator.CompleteRebootAsync(rebootDone, cancellationToken);
                await PublishAsync(deviceId, "status", status, cancellationToken);
                break;

            case DeviceInboundKind.Connectivity:
                var connectivity = JsonPayload.ToDictionary(payload);
                if (connectivity?.GetValueOrDefault("state") is not string state)
                    return;
                linkState.RecordConnectivity(deviceId, state, receivedAt);
                reboots.ObserveConnectivity(deviceId, state, receivedAt);
                if (projection is not null && state == "offline")
                    await projection.MarkStaleAsync(deviceId, OutputStaleReasons.DeviceOffline, cancellationToken);
                await PublishAsync(deviceId, "connectivity", new Dictionary<string, object?>
                {
                    ["state"] = state,
                    ["receivedAt"] = receivedAt
                }, cancellationToken);
                break;

            case DeviceInboundKind.Events:
                // ts here is device uptime seconds, not Unix time (spec §10).
                var deviceEvent = JsonPayload.ToDictionary(payload);
                if (deviceEvent is null)
                    return;
                deviceEvent["receivedAt"] = receivedAt;
                await PublishAsync(deviceId, "device_event", deviceEvent, cancellationToken);
                break;

            case DeviceInboundKind.Capabilities:
                var capabilities = DeviceCapabilities.TryParse(payload.Span);
                if (capabilities is null)
                {
                    logger.LogWarning("Device {DeviceId} capabilities document is not readable", deviceId.Value);
                    return;
                }
                linkState.RecordCapabilities(deviceId, capabilities, receivedAt);
                await PublishAsync(deviceId, "capabilities", new Dictionary<string, object?>
                {
                    ["actionCount"] = capabilities.Availability.Count,
                    ["receivedAt"] = receivedAt
                }, cancellationToken);
                break;

            case DeviceInboundKind.CommandResponse:
                await correlator.HandleAsync(deviceId, payload, receivedAt, cancellationToken);
                break;
        }
    }

    private async ValueTask PublishAsync(
        DeviceId deviceId,
        string eventType,
        IReadOnlyDictionary<string, object?> payload,
        CancellationToken cancellationToken)
    {
        try
        {
            await events.PublishAsync(new AuthorizedDeviceEvent(
                deviceId, epochs.Current(deviceId), permissions.PolicyVersion, eventType, payload), cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message == "REALTIME_QUEUE_FULL")
        {
            logger.LogWarning("Realtime queue full; dropped {EventType} for device {DeviceId}", eventType, deviceId.Value);
        }
    }
}

/// <summary>Binds a device to its topic base on the broker.</summary>
public sealed class ConfigureDeviceMqttBindingHandler(
    IDeviceStore devices,
    IDeviceMqttBindingStore bindings,
    IDeviceRebootTracker reboots,
    IDeviceOutputStateStore? outputs = null)
{
    public DeviceMqttBinding Handle(DeviceId deviceId, string topicPrefix, string mqttDeviceId, uint? keyVersion)
    {
        var device = devices.Find(deviceId) ?? throw new InvalidOperationException("DEVICE_NOT_FOUND");
        if (device.State == DeviceLifecycleState.Revoked)
            throw new InvalidOperationException("DEVICE_REVOKED");
        var binding = DeviceMqttBinding.Create(device.Id, topicPrefix, mqttDeviceId, keyVersion);
        var previous = bindings.Find(device.Id);
        bindings.Upsert(binding);

        // The allowlist has its own, master-admin endpoint, and a re-bind of the same topic base
        // keeps it. A new topic base is another physical device: the channels a master-admin
        // approved and the relay state read from the old one do not carry over.
        if (previous is not null && !StringComparer.Ordinal.Equals(previous.TopicBase, binding.TopicBase))
        {
            bindings.SetAllowedOutputChannels(device.Id, null);
            outputs?.Clear(device.Id);
            // The uptime heard and any reboot being watched belong to the old topic's device.
            reboots.Abandon(device.Id, "rebind");
            return binding;
        }

        return binding with { AllowedOutputChannels = previous?.AllowedOutputChannels };
    }
}

public sealed record OutputChannelsChange(IReadOnlyList<int>? Before, IReadOnlyList<int> After);

/// <summary>Sets which output channels the server may drive on a device (empty = none). Callers
/// restrict this to master-admins: it is the interlock between a command and a real relay.</summary>
public sealed class SetDeviceOutputChannelsHandler(IDeviceMqttBindingStore bindings)
{
    public OutputChannelsChange Handle(DeviceId deviceId, IEnumerable<int> channels)
    {
        var validated = DeviceMqttBinding.ValidateOutputChannels(channels);
        var before = bindings.Find(deviceId)?.AllowedOutputChannels;
        if (!bindings.SetAllowedOutputChannels(deviceId, validated))
            throw new InvalidOperationException("DEVICE_MQTT_BINDING_MISSING");
        return new OutputChannelsChange(before, validated);
    }
}

/// <summary>
/// Registers a command key that was provisioned onto the device out of band (spec §1: the key
/// comes from the administrator or the device's authorized HTTP channel). The key moves the
/// device to a new key generation, so commands planned under the previous key are fenced.
/// </summary>
public sealed class ImportDeviceCommandKeyHandler(IDeviceStore devices, IProvisioningSecretGenerator secrets)
{
    public int Handle(DeviceId deviceId, string valueHex)
    {
        var hex = valueHex?.Trim() ?? string.Empty;
        // Spec §4.2: a key handed over as 64 hex characters is 32 raw bytes, never the ASCII text.
        if (hex.Length != 64 || !hex.All(char.IsAsciiHexDigit))
            throw new ArgumentException("COMMAND_KEY_INVALID", nameof(valueHex));

        var device = devices.Find(deviceId) ?? throw new InvalidOperationException("DEVICE_NOT_FOUND");
        if (device.State == DeviceLifecycleState.Revoked)
            throw new InvalidOperationException("DEVICE_REVOKED");

        var key = Convert.FromHexString(hex);
        var nextGeneration = checked(device.KeyGeneration + 1);
        // Store the key before the device points at its generation, so a failure cannot leave the
        // device on a generation that has no key.
        secrets.Import(device.Id, nextGeneration, key);
        device.RotateKeyGeneration();
        devices.Update(device);
        return device.KeyGeneration;
    }
}

public static class JsonPayload
{
    public static Dictionary<string, object?>? ToDictionary(ReadOnlyMemory<byte> utf8Json)
    {
        try
        {
            using var document = JsonDocument.Parse(utf8Json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? (Dictionary<string, object?>)ToObject(document.RootElement)!
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static object? ToObject(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject()
            .Aggregate(new Dictionary<string, object?>(StringComparer.Ordinal), (map, property) =>
            {
                map[property.Name] = ToObject(property.Value);
                return map;
            }),
        JsonValueKind.Array => element.EnumerateArray().Select(ToObject).ToList(),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var integer) ? integer : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null
    };
}
