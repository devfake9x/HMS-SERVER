using Hms.Server.Application.Commands;
using Hms.Server.Application.DeviceLink;
using Microsoft.Extensions.Options;

namespace Hms.Server.Infrastructure.Mqtt;

/// <summary>
/// Publishes prepared commands on the live broker link. "Published" only means the broker took
/// the message; the device outcome arrives later on <c>B/cmd/res</c> or as a timeout.
/// </summary>
public sealed class MqttDeviceCommandTransport(
    MqttDeviceLinkService link,
    IPendingDeviceCommandRegistry pending,
    IOptions<MqttLinkOptions> options) : IDeviceCommandTransport
{
    public async ValueTask<CommandExecutionResult> SendAsync(
        CommandExecutionPlan plan,
        PreparedDeviceCommand prepared,
        CancellationToken cancellationToken = default)
    {
        var correlationId = plan.Request.CorrelationId;
        if (!prepared.CanPublish || prepared.Topic is null)
            return new(CommandExecutionStatus.ContractUnavailable, prepared.FailureCode ?? "CONTRACT_UNAVAILABLE", correlationId);
        if (!link.IsConnected)
            return new(CommandExecutionStatus.Failed, "MQTT_NOT_CONNECTED", correlationId);

        // Registered before the publish: the reply can arrive before PublishAsync returns.
        var now = DateTimeOffset.UtcNow;
        pending.Register(new PendingDeviceCommand(plan, now, now.AddSeconds(options.Value.ResponseTimeoutSeconds)));

        string? failure;
        try
        {
            failure = await link.PublishCommandAsync(prepared.Topic, prepared.Payload, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failure = "MQTT_PUBLISH_FAILED";
        }

        if (failure is not null)
        {
            pending.Remove(plan.Request.DeviceId, correlationId);
            return new(CommandExecutionStatus.Failed, failure, correlationId);
        }

        return new(CommandExecutionStatus.Published, "MQTT_PUBLISHED", correlationId, KeyGeneration: plan.KeyGeneration);
    }
}
