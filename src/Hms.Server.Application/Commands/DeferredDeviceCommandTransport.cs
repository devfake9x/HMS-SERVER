namespace Hms.Server.Application.Commands;

public sealed class DeferredDeviceCommandTransport : IDeviceCommandTransport
{
    public ValueTask<CommandExecutionResult> SendAsync(
        CommandExecutionPlan plan,
        PreparedDeviceCommand prepared,
        CancellationToken cancellationToken = default)
    {
        var deferred = prepared.CanPublish || prepared.FailureCode == "DEFERRED_MQTT_CONTRACT";
        var status = deferred
            ? CommandExecutionStatus.MqttDeferred
            : CommandExecutionStatus.ContractUnavailable;
        var code = deferred ? "MQTT_DEFERRED" : prepared.FailureCode ?? "CONTRACT_UNAVAILABLE";
        return ValueTask.FromResult(new CommandExecutionResult(status, code, plan.Request.CorrelationId));
    }
}
