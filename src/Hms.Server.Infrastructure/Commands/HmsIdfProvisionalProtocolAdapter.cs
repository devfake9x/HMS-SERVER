using Hms.Server.Application.Commands;
using System.Text.Json;

namespace Hms.Server.Infrastructure.Commands;

public sealed class HmsIdfProvisionalProtocolAdapter : ICommandProtocolAdapter
{
    public ValueTask<PreparedDeviceCommand> PrepareAsync(
        CommandExecutionPlan plan,
        CancellationToken cancellationToken = default)
    {
        if (plan.Request.Payload is null)
        {
            return ValueTask.FromResult(Failure(plan, "COMMAND_PAYLOAD_REQUIRED"));
        }

        var actionName = plan.Request.Action.Contains('.')
            ? plan.Request.Action
            : $"{plan.Request.Type}.{plan.Request.Action}";

        var provisionalEnvelope = new
        {
            req_id = plan.Request.CorrelationId,
            client_id = "hms_server_c2",
            ts = plan.Request.RequestedAt.ToUnixTimeSeconds(),
            data = new
            {
                action = actionName,
                @params = plan.Request.Payload ?? new Dictionary<string, object?>()
            },
            ttl_ms = (long)(plan.ExpiresAt - plan.Request.RequestedAt).TotalMilliseconds,
            dry_run = plan.Request.DryRun
        };

        byte[] candidate;
        try
        {
            candidate = JsonSerializer.SerializeToUtf8Bytes(provisionalEnvelope);
        }
        catch (Exception)
        {
            return ValueTask.FromResult(Failure(plan, "COMMAND_PAYLOAD_INVALID"));
        }

        if (candidate.Length >= 1024)
        {
            return ValueTask.FromResult(Failure(plan, "COMMAND_HEADER_TOO_LARGE"));
        }

        var deviceToken = plan.Request.DeviceId.Value == new Guid("870c870c-870c-870c-870c-870c870c870c")
            ? "gateway_870c"
            : plan.Request.DeviceId.Value.ToString("N");
        var topic = $"hms_toan_lab/{deviceToken}/cmd";
        return ValueTask.FromResult(new PreparedDeviceCommand(
            plan.Request.DeviceId,
            Topic: topic,
            Payload: candidate,
            plan.KeyGeneration,
            CanPublish: false,
            FailureCode: "DEFERRED_MQTT_CONTRACT"));
    }

    private static PreparedDeviceCommand Failure(CommandExecutionPlan plan, string code) =>
        new(plan.Request.DeviceId, null, ReadOnlyMemory<byte>.Empty, plan.KeyGeneration, false, code);
}
