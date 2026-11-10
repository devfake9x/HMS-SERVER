namespace Hms.Server.Domain.Commands;

public enum CommandAvailability
{
    Enabled,
    DeniedUntilActionContract
}

// Command classes R W C D B F (MQTT spec §6, §11).
public enum CommandClass
{
    Read,
    Write,
    Control,
    Destructive,
    Physical,
    Flash
}

public sealed record DomainPermission(string ViewPermission, string EditPermission);

/// <summary>One command the device accepts, with what the server does when it sends it. The
/// behaviour flags are stated per action and never read off <see cref="Class"/>: one class holds
/// actions that need different handling (class B has relay outputs and sms.send). All three default
/// to false, so an action nobody reviewed stores no payload and waits its turn in the write lane.
/// For <see cref="AffectsOutputs"/> false is not the cautious value: the cached output state stays
/// trusted, so an action that can move a relay must set it or mark the state stale another way
/// (system.reboot does, through the reboot tracker).</summary>
/// <param name="KeepsAuditPayload">The audit store may keep the request payload. False until the
/// action's payload is known to hold no secret or personal data.</param>
/// <param name="AffectsOutputs">The command can change a digital output, so the cached output state
/// is stale after it runs or goes unanswered. Set for every class B control row.</param>
/// <param name="UsesOutputLane">The relay panel serializes this command itself, so it skips the
/// per-device write lane. False sends a non-read command through the write lane.</param>
public sealed record CommandDefinition(
    string Type,
    string Action,
    string? RequiredPermission,
    CommandAvailability Availability,
    bool Dangerous,
    bool Idempotent,
    int? DefaultTtlMs,
    CommandClass Class = CommandClass.Read,
    IReadOnlyDictionary<string, DomainPermission>? PermissionByDomain = null,
    string? ConfirmTemplate = null,
    bool DryRunSupported = false,
    bool KeepsAuditPayload = false,
    bool AffectsOutputs = false,
    bool UsesOutputLane = false)
{
    // Spec §3: the TTL a command gets when it carries no ttl_ms and its row states no default.
    // Catalog rows always state one; this covers definitions built without a TTL.
    public const int DispatcherDefaultTtlMs = 120_000;
}
