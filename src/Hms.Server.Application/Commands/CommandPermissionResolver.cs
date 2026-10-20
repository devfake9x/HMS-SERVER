using System.Text.Json;
using Hms.Server.Domain.Authorization;
using Hms.Server.Domain.Commands;

namespace Hms.Server.Application.Commands;

// The permission a command needs, from the catalog (MQTT spec §11): the row's fixed permission,
// or for a config-domain row the view/edit permission of the payload's "domain". The domain match
// is server policy, since the spec states no rule: exact and string-only.
// Anything else resolves to null (fail closed).
public static class CommandPermissionResolver
{
    private const string DomainKey = "domain";

    public static string? Resolve(CommandDefinition definition, IReadOnlyDictionary<string, object?> payload)
    {
        if (definition.RequiredPermission is not null)
            return definition.RequiredPermission;
        if (definition.PermissionByDomain is null || FindDomainValue(payload) is not { } domain)
            return null;

        foreach (var (key, permission) in definition.PermissionByDomain)
        {
            if (DomainEquals(domain, key))
                return ForClass(definition.Class, permission);
        }

        return null;
    }

    // Domain → permission for this row's class: the §11 config-domain column the action uses.
    public static IReadOnlyDictionary<string, string>? DomainPermissions(CommandDefinition definition) =>
        definition.PermissionByDomain?.ToDictionary(
            x => x.Key, x => ForClass(definition.Class, x.Value), StringComparer.Ordinal);

    public static bool IsHighRisk(CommandDefinition definition) =>
        DevicePermissionCodes.IsHighRisk(definition.RequiredPermission) ||
        DomainPermissions(definition)?.Values.Any(DevicePermissionCodes.IsHighRisk) == true;

    private static string ForClass(CommandClass commandClass, DomainPermission permission) =>
        commandClass == CommandClass.Read ? permission.ViewPermission : permission.EditPermission;

    // The key is compared ordinally whatever comparer the dictionary carries.
    private static object? FindDomainValue(IReadOnlyDictionary<string, object?> payload)
    {
        foreach (var (key, value) in payload)
        {
            if (StringComparer.Ordinal.Equals(key, DomainKey))
                return value;
        }

        return null;
    }

    // Only a JSON string counts, and nothing is converted with ToString(). A JsonElement is
    // compared on its unescaped value: an escape is encoding, not value.
    private static bool DomainEquals(object value, string domain)
    {
        switch (value)
        {
            case string text:
                return StringComparer.Ordinal.Equals(text, domain);
            case JsonElement { ValueKind: JsonValueKind.String } element:
                try
                {
                    return element.ValueEquals(domain);
                }
                catch (InvalidOperationException)
                {
                    // An escape that is not valid UTF-16 (a lone surrogate) equals no domain.
                    return false;
                }
            default:
                return false;
        }
    }
}
