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
    bool DryRunSupported = false)
{
    // Spec §3: the TTL a command gets when it carries no ttl_ms and its row states no default.
    // Catalog rows always state one; this covers definitions built without a TTL.
    public const int DispatcherDefaultTtlMs = 120_000;
}
