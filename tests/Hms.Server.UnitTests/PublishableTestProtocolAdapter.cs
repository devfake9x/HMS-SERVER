using Hms.Server.Application.Commands;

namespace Hms.Server.UnitTests;

/// <summary>A protocol adapter that prepares every plan as publishable, for tests of the command
/// boundary that do not exercise the firmware wire format.</summary>
public sealed class PublishableTestProtocolAdapter : ICommandProtocolAdapter
{
    public ValueTask<PreparedDeviceCommand> PrepareAsync(
        CommandExecutionPlan plan,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new PreparedDeviceCommand(
            plan.Request.DeviceId,
            $"test/{plan.Request.DeviceId.Value:N}/cmd/{plan.Request.Type}",
            "{}"u8.ToArray(),
            plan.KeyGeneration,
            CanPublish: true));
}
