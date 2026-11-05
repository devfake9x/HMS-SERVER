using System.Text;
using Hms.Server.Application.Commands;
using Hms.Server.Application.DeviceLink;
using Hms.Server.Application.Provisioning;

namespace Hms.Server.Infrastructure.Commands;

/// <summary>
/// Turns an authorized command plan into the signed bytes and topic the HMS-IDF firmware accepts
/// (MQTT spec §2-§4, §7). Anything the device would drop silently or refuse is caught here, so a
/// command is only published when it can be admitted.
/// </summary>
public sealed class HmsIdfCommandProtocolAdapter(
    IDeviceMqttBindingStore bindings,
    IProvisioningSecretGenerator secrets,
    IDeviceLinkStateStore linkState) : ICommandProtocolAdapter
{
    // Spec §3 field limits, in UTF-8 bytes.
    private const int MaxTypeBytes = 31;
    private const int MaxActionBytes = 31;
    private const int MaxIdBytes = 39;

    public ValueTask<PreparedDeviceCommand> PrepareAsync(
        CommandExecutionPlan plan,
        CancellationToken cancellationToken = default)
    {
        var request = plan.Request;
        var binding = bindings.Find(request.DeviceId);
        if (binding is null)
            return Fail(plan, "DEVICE_MQTT_BINDING_MISSING");

        if (!WithinBytes(request.Type, MaxTypeBytes) || !WithinBytes(request.Action, MaxActionBytes))
            return Fail(plan, "COMMAND_NAME_INVALID");
        if (!WithinBytes(request.CorrelationId, MaxIdBytes) || request.CorrelationId.Any(char.IsControl))
            return Fail(plan, "CORRELATION_ID_INVALID");

        // The retained capabilities say which handlers this firmware registered (spec §2).
        // Not yet received means unknown, not unavailable.
        var capabilities = linkState.GetCapabilities(request.DeviceId);
        if (capabilities is not null && capabilities.IsAvailable(request.Type, request.Action) is not true)
            return Fail(plan, "DEVICE_ACTION_UNAVAILABLE");

        // The device compares the exact token built from its own MQTT device id (spec §7).
        string? confirm = null;
        if (plan.Definition.ConfirmTemplate is { } template)
        {
            confirm = template.Replace(PinnedCommandCatalog.DeviceIdPlaceholder, binding.MqttDeviceId, StringComparison.Ordinal);
            if (!StringComparer.Ordinal.Equals(request.Confirm, confirm))
                return Fail(plan, "CONFIRM_MISMATCH");
        }

        if (request.DryRun && !plan.Definition.DryRunSupported)
            return Fail(plan, "DRY_RUN_UNSUPPORTED");

        // Find, never Generate: a freshly generated key is one the device has never seen.
        var keyset = secrets.Find(request.DeviceId, plan.KeyGeneration);
        if (keyset is null)
            return Fail(plan, "COMMAND_KEY_NOT_PROVISIONED");

        byte[] wire;
        try
        {
            var unsigned = HmsIdfCommandCodec.Serialize(new HmsIdfCommandCodec.CommandEnvelope(
                request.Type,
                request.CorrelationId,
                request.RequestedAt.ToUnixTimeSeconds(),
                (long)(plan.ExpiresAt - request.RequestedAt).TotalMilliseconds,
                request.Action,
                request.Payload,
                binding.KeyVersion,
                confirm,
                request.DryRun));
            wire = HmsIdfCommandCodec.Sign(unsigned, keyset.CommandKey);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException or System.Text.Json.JsonException)
        {
            return Fail(plan, "COMMAND_PAYLOAD_INVALID");
        }

        // Oversized commands get no reply at all (spec R1).
        if (wire.Length > HmsIdfCommandCodec.MaxCommandBytes)
            return Fail(plan, "COMMAND_TOO_LARGE");

        return ValueTask.FromResult(new PreparedDeviceCommand(
            request.DeviceId,
            DeviceLinkTopics.Command(binding, request.Type),
            wire,
            plan.KeyGeneration,
            CanPublish: true));
    }

    private static bool WithinBytes(string value, int maxBytes) =>
        !string.IsNullOrEmpty(value) && Encoding.UTF8.GetByteCount(value) <= maxBytes;

    private static ValueTask<PreparedDeviceCommand> Fail(CommandExecutionPlan plan, string code) =>
        ValueTask.FromResult(new PreparedDeviceCommand(
            plan.Request.DeviceId, null, ReadOnlyMemory<byte>.Empty, plan.KeyGeneration, CanPublish: false, FailureCode: code));
}
