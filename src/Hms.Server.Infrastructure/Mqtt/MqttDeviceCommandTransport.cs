using Hms.Server.Application.Commands;
using Hms.Server.Application.DeviceLink;
using Microsoft.Extensions.Options;

namespace Hms.Server.Infrastructure.Mqtt;

/// <summary>The publish side of the broker session, as the transport needs it.</summary>
public interface IMqttCommandPublisher
{
    bool IsConnected { get; }

    /// <summary>Null when the broker accepted the message; otherwise a failure code.</summary>
    ValueTask<string?> PublishCommandAsync(string commandTopic, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);
}

/// <summary>
/// Publishes prepared commands on the live broker link. "Published" only means the broker took
/// the message; the device outcome arrives later on <c>B/cmd/res</c> or as a timeout.
/// </summary>
public sealed class MqttDeviceCommandTransport(
    IMqttCommandPublisher link,
    IPendingDeviceCommandRegistry pending,
    IOptions<MqttLinkOptions> options) : IDeviceCommandTransport
{
    /// <summary>Failures raised before anything was handed to the broker: the command certainly
    /// did not leave the server.</summary>
    public static readonly IReadOnlySet<string> NeverSentCodes = new HashSet<string>(StringComparer.Ordinal)
    {
        "MQTT_NOT_CONNECTED",
        "MQTT_RESPONSE_SUBSCRIPTION_MISSING",
        "MQTT_COMMAND_TOPIC_INVALID"
    };

    public const string PublishUnconfirmed = CommandTransportOutcome.PublishUnconfirmed;

    /// <summary>The device already has its share of reads waiting; nothing was published.</summary>
    public const string ReadBusy = "DEVICE_READ_BUSY";

    private static bool IsRelayReadBack(CommandExecutionPlan plan) =>
        plan.Request.Type == "control" && plan.Request.Action == "get_status";

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

        // Registered before the publish: the reply can arrive before PublishAsync returns. Reads
        // are capped per device so they cannot fill the queue a relay command needs.
        var now = DateTimeOffset.UtcNow;
        var entry = new PendingDeviceCommand(plan, now, now.AddSeconds(options.Value.ResponseTimeoutSeconds));
        if (plan.Definition.Class == Domain.Commands.CommandClass.Read)
        {
            // Each sender gets half the slots, so one user cannot hold them all. The relay
            // read-back (control.get_status) has one slot beyond the cap: after a relay write the
            // operator must learn the real state even while other reads fill the queue.
            var reserved = IsRelayReadBack(plan) ? 1 : 0;
            var perDevice = options.Value.MaxPendingReadsPerDevice;
            if (!pending.TryRegisterRead(entry, perDevice + reserved, Math.Max(1, perDevice / 2) + reserved))
                return new(CommandExecutionStatus.ContractUnavailable, ReadBusy, correlationId,
                    "The device already has the maximum number of reads waiting for a reply; retry shortly.");
        }
        else
        {
            pending.Register(entry);
        }

        string? failure;
        try
        {
            failure = await link.PublishCommandAsync(prepared.Topic, prepared.Payload, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failure = PublishUnconfirmed;
        }

        if (failure is null)
            return new(CommandExecutionStatus.Published, "MQTT_PUBLISHED", correlationId, KeyGeneration: plan.KeyGeneration);

        if (NeverSentCodes.Contains(failure))
        {
            pending.Remove(plan.Request.DeviceId, correlationId);
            return new(CommandExecutionStatus.Failed, failure, correlationId);
        }

        // The broker may have taken the message even though we saw no acknowledgement. Keep it
        // pending so a reply still closes it, or the sweep marks it outcome-unknown (spec R4).
        return new(CommandExecutionStatus.Failed, PublishUnconfirmed, correlationId,
            $"Publish not acknowledged ({failure}); the device may still run it. Read state before retrying.",
            plan.KeyGeneration);
    }
}
