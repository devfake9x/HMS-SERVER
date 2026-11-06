using Hms.Server.Domain.Commands;

namespace Hms.Server.Application.Commands;

/// <summary>Which per-device lane a command uses on the server. Reads share a capped read lane,
/// relay outputs are serialized by their panel, and every other class (configuration, control,
/// destructive, flash) goes through the write lane: one waiting at a time per device.</summary>
public static class CommandLanes
{
    public static bool IsWrite(CommandClass commandClass) =>
        commandClass is not (CommandClass.Read or CommandClass.Physical);
}
