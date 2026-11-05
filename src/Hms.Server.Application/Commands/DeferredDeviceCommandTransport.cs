namespace Hms.Server.Application.Commands;

/// <summary>Used while the broker link is disabled: a command that could be published is
/// reported as deferred, never as published or executed.</summary>
public sealed class DeferredDeviceCommandTransport : IDeviceCommandTransport
{
    public ValueTask<CommandExecutionResult> SendAsync(
        CommandExecutionPlan plan,
        PreparedDeviceCommand prepared,
        CancellationToken cancellationToken = default)
    {
        var result = prepared.CanPublish
            ? new CommandExecutionResult(CommandExecutionStatus.MqttDeferred, "MQTT_DEFERRED", plan.Request.CorrelationId)
            : new CommandExecutionResult(CommandExecutionStatus.ContractUnavailable,
                prepared.FailureCode ?? "CONTRACT_UNAVAILABLE", plan.Request.CorrelationId);
        return ValueTask.FromResult(result);
    }
}
