namespace Hms.Server.Application.Commands;

public sealed class RecordingDeviceCommandTransport : IDeviceCommandTransport
{
    private readonly List<(CommandExecutionPlan Plan, PreparedDeviceCommand Prepared)> _records = [];

    public IReadOnlyList<(CommandExecutionPlan Plan, PreparedDeviceCommand Prepared)> Records => _records;

    public ValueTask<CommandExecutionResult> SendAsync(
        CommandExecutionPlan plan,
        PreparedDeviceCommand prepared,
        CancellationToken cancellationToken = default)
    {
        if (!prepared.CanPublish)
        {
            return ValueTask.FromResult(new CommandExecutionResult(
                CommandExecutionStatus.ContractUnavailable,
                prepared.FailureCode ?? "CONTRACT_UNAVAILABLE",
                plan.Request.CorrelationId));
        }

        _records.Add((plan, prepared));
        return ValueTask.FromResult(new CommandExecutionResult(
            CommandExecutionStatus.Recorded,
            "COMMAND_RECORDED",
            plan.Request.CorrelationId));
    }
}
