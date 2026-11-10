using Hms.Server.Domain.Commands;

namespace Hms.Server.Application.Commands;

/// <summary>Which per-device lane a command uses on the server. Reads share a capped read lane,
/// the actions the catalog marks as relay outputs are serialized by their panel, and every other
/// command goes through the write lane: one waiting at a time per device. The lane is chosen per
/// action, not per class, so a physical command that is not a relay output still waits its turn.</summary>
public static class CommandLanes
{
    public static bool IsWrite(CommandDefinition definition) =>
        definition.Class != CommandClass.Read && !definition.UsesOutputLane;
}
