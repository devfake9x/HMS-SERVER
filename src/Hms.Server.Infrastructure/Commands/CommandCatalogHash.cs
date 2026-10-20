using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hms.Server.Infrastructure.Commands;

// One row of MQTT spec §11. Declaration order is the canonical field order of the catalog hash.
public sealed record CommandCatalogRow(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("class")] string Class,
    [property: JsonPropertyName("required_perm")] string? RequiredPerm,
    [property: JsonPropertyName("domain_policy")] bool DomainPolicy,
    [property: JsonPropertyName("ttl_ms")] int TtlMs,
    [property: JsonPropertyName("confirm_required")] bool ConfirmRequired,
    [property: JsonPropertyName("confirm_template")] string? ConfirmTemplate,
    [property: JsonPropertyName("dry_run")] bool DryRun);

public sealed record CommandCatalogDomainRow(
    [property: JsonPropertyName("domain")] string Domain,
    [property: JsonPropertyName("view_perm")] string ViewPerm,
    [property: JsonPropertyName("edit_perm")] string EditPerm);

// The reference implementation of the catalog hash: scripts/generate-command-catalog.mjs must
// produce the same bytes, which the shared golden vector in CatalogTests locks.
public static class CommandCatalogHash
{
    public const string Canonicalization =
        "v2: commands sorted ordinal (type,action) with fixed field order; config_domains sorted ordinal by domain; " +
        "{commands,config_domains} compact System.Text.Json; UTF-8; SHA-256 lowercase hex";

    public static string Compute(
        IEnumerable<CommandCatalogRow> commands,
        IEnumerable<CommandCatalogDomainRow> configDomains)
    {
        // No de-duplication: duplicate pairs and domains are rejected by the generator and the loader.
        var canonical = new CanonicalCatalog(
            commands
                .OrderBy(x => x.Type, StringComparer.Ordinal)
                .ThenBy(x => x.Action, StringComparer.Ordinal)
                .ToArray(),
            configDomains
                .OrderBy(x => x.Domain, StringComparer.Ordinal)
                .ToArray());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical))))
            .ToLowerInvariant();
    }

    private sealed record CanonicalCatalog(
        [property: JsonPropertyName("commands")] CommandCatalogRow[] Commands,
        [property: JsonPropertyName("config_domains")] CommandCatalogDomainRow[] ConfigDomains);
}
