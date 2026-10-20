using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hms.Server.Application.Commands;
using Hms.Server.Domain.Commands;

namespace Hms.Server.Infrastructure.Commands;

// The device does not check permissions itself (MQTT spec R11), so this mapping is a security
// boundary: the committed catalog, generated from spec §11, is parsed strictly, hash-checked and
// validated row by row, failing on the first resolve of the singleton rather than serving a
// partial catalog.
public sealed class PinnedCommandCatalog : ICommandCatalog, ICommandCatalogMetadata
{
    private const string ResourceName = "Hms.Server.Infrastructure.Commands.command-catalog.json";
    private const string MetadataInvalid = "COMMAND_CATALOG_METADATA_INVALID";
    private const string HashMismatch = "COMMAND_CATALOG_HASH_MISMATCH";
    private const string RowInvalid = "COMMAND_CATALOG_ROW_INVALID";
    // Spec §3: ttl_ms is 1..86400000.
    private const int MaxTtlMs = 86_400_000;
    // Every confirm token is a template around the device id (spec §7, §11).
    private const string DeviceIdPlaceholder = "<DEVID>";

    private static readonly JsonSerializerOptions StrictOptions = new()
    {
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        // A duplicate key anywhere, rows included, fails the document parse instead of last-wins.
        AllowDuplicateProperties = false
    };

    private static readonly FrozenDictionary<string, CommandClass> ClassByLetter =
        new Dictionary<string, CommandClass>
        {
            ["R"] = CommandClass.Read,
            ["W"] = CommandClass.Write,
            ["C"] = CommandClass.Control,
            ["D"] = CommandClass.Destructive,
            ["B"] = CommandClass.Physical,
            ["F"] = CommandClass.Flash
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private readonly IReadOnlyDictionary<(string Type, string Action), CommandDefinition> _definitions;

    public PinnedCommandCatalog() : this(ReadEmbeddedCatalog())
    {
    }

    private PinnedCommandCatalog(CatalogDocument document)
    {
        if (string.IsNullOrWhiteSpace(document.CatalogVersion) ||
            string.IsNullOrWhiteSpace(document.CatalogHash) ||
            !StringComparer.Ordinal.Equals(document.Canonicalization, CommandCatalogHash.Canonicalization) ||
            document.Commands.Length == 0)
            throw new InvalidOperationException(MetadataInvalid);

        var commands = document.Commands.Select(ParseRow<CommandCatalogRow>).ToArray();
        var configDomains = document.ConfigDomains.Select(ParseRow<CommandCatalogDomainRow>).ToArray();
        // The canonicalization fixes lowercase hex, so the stored hash must match exactly.
        var computedHash = CommandCatalogHash.Compute(commands, configDomains);
        if (!StringComparer.Ordinal.Equals(computedHash, document.CatalogHash))
            throw new InvalidOperationException(HashMismatch);

        var permissionByDomain = ValidateConfigDomains(configDomains);
        var definitions = new Dictionary<(string Type, string Action), CommandDefinition>();
        foreach (var row in commands)
        {
            if (!definitions.TryAdd((row.Type, row.Action), ToDefinition(row, permissionByDomain)))
                throw new InvalidOperationException(RowInvalid);
        }

        // The config-domain table has two columns, so config.get and config.set use it together or
        // not at all, as the generator requires.
        var domainActions = commands.Where(x => x.DomainPolicy).Select(x => x.Action).Order(StringComparer.Ordinal).ToArray();
        if (domainActions.Length > 0 && !domainActions.SequenceEqual(["get", "set"]))
            throw new InvalidOperationException(RowInvalid);

        Metadata = new CommandCatalogMetadata(document.CatalogVersion, computedHash);
        FirmwarePermissions = commands
            .Select(x => x.RequiredPerm)
            .OfType<string>()
            .Concat(configDomains.SelectMany(x => new[] { x.ViewPerm, x.EditPerm }))
            .ToFrozenSet(StringComparer.Ordinal);
        _definitions = definitions;
    }

    public static PinnedCommandCatalog Load(Stream catalogJson) => new(ParseDocument(catalogJson));

    public CommandCatalogMetadata Metadata { get; }

    public IReadOnlySet<string> FirmwarePermissions { get; }

    public CommandDefinition? Find(string type, string action) =>
        _definitions.TryGetValue((type, action), out var definition) ? definition : null;

    public IReadOnlyList<CommandDefinition> List() => _definitions.Values
        .OrderBy(x => x.Type, StringComparer.Ordinal)
        .ThenBy(x => x.Action, StringComparer.Ordinal)
        .ToArray();

    public int Count => _definitions.Count;

    private static CatalogDocument ReadEmbeddedCatalog()
    {
        using var stream = typeof(PinnedCommandCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("COMMAND_CATALOG_ARTIFACT_MISSING");
        return ParseDocument(stream);
    }

    private static CatalogDocument ParseDocument(Stream catalogJson)
    {
        try
        {
            return JsonSerializer.Deserialize<CatalogDocument>(catalogJson, StrictOptions)
                ?? throw new InvalidOperationException(MetadataInvalid);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(MetadataInvalid);
        }
    }

    private static T ParseRow<T>(JsonElement element)
    {
        try
        {
            return element.Deserialize<T>(StrictOptions) ?? throw new InvalidOperationException(RowInvalid);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(RowInvalid);
        }
    }

    private static FrozenDictionary<string, DomainPermission> ValidateConfigDomains(
        IEnumerable<CommandCatalogDomainRow> configDomains)
    {
        var byDomain = new Dictionary<string, DomainPermission>(StringComparer.Ordinal);
        foreach (var row in configDomains)
        {
            if (!IsPolicyString(row.Domain) || !IsPolicyString(row.ViewPerm) || !IsPolicyString(row.EditPerm) ||
                !byDomain.TryAdd(row.Domain, new DomainPermission(row.ViewPerm, row.EditPerm)))
                throw new InvalidOperationException(RowInvalid);
        }

        return byDomain.ToFrozenDictionary(StringComparer.Ordinal);
    }

    // What a §11 row states, checked the same way the generator checks it: a known class, exactly
    // one permission source (a code, or the config-domain table for config.get/config.set only),
    // a device-id template for every confirm gate, a TTL within spec §3, the policy charset.
    private static CommandDefinition ToDefinition(
        CommandCatalogRow row,
        IReadOnlyDictionary<string, DomainPermission> permissionByDomain)
    {
        if (!IsPolicyString(row.Type) ||
            !IsPolicyString(row.Action) ||
            !ClassByLetter.TryGetValue(row.Class, out var commandClass) ||
            row.DomainPolicy != (row.RequiredPerm is null) ||
            (row.RequiredPerm is not null && !IsPolicyString(row.RequiredPerm)) ||
            (row.DomainPolicy && (!IsConfigDomainRow(row, commandClass) || permissionByDomain.Count == 0)) ||
            row.ConfirmRequired != (row.ConfirmTemplate is not null) ||
            (row.ConfirmTemplate is not null &&
             (!IsPolicyString(row.ConfirmTemplate) ||
              !row.ConfirmTemplate.Contains(DeviceIdPlaceholder, StringComparison.Ordinal))) ||
            row.TtlMs is <= 0 or > MaxTtlMs)
            throw new InvalidOperationException(RowInvalid);

        return new CommandDefinition(
            row.Type,
            row.Action,
            row.RequiredPerm,
            CommandAvailability.DeniedUntilActionContract,
            Dangerous: commandClass == CommandClass.Flash,
            Idempotent: true,
            DefaultTtlMs: row.TtlMs,
            Class: commandClass,
            PermissionByDomain: row.DomainPolicy ? permissionByDomain : null,
            ConfirmTemplate: row.ConfirmTemplate,
            DryRunSupported: row.DryRun);
    }

    // Spec §11 takes a permission from the config-domain table only for config.get and
    // config.set, its two columns. The resolver picks view or edit by class, which matches that
    // binding only while config.get is a read and config.set is not.
    private static bool IsConfigDomainRow(CommandCatalogRow row, CommandClass commandClass) =>
        row.Type == "config" &&
        ((row.Action == "get" && commandClass == CommandClass.Read) ||
         (row.Action == "set" && commandClass != CommandClass.Read));

    // The generator's charset ^[A-Za-z0-9_.<>-]+$: inside it, compact System.Text.Json and the
    // generator's JSON.stringify differ only in the escaping of < and >.
    private static bool IsPolicyString(string value) =>
        value.Length > 0 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '<' or '>' or '-');

    private sealed record CatalogDocument(
        [property: JsonPropertyName("catalog_version")] string CatalogVersion,
        [property: JsonPropertyName("catalog_hash")] string CatalogHash,
        [property: JsonPropertyName("canonicalization")] string Canonicalization,
        [property: JsonPropertyName("source_repo")] string SourceRepo,
        [property: JsonPropertyName("source_commit")] string SourceCommit,
        [property: JsonPropertyName("source_path")] string SourcePath,
        [property: JsonPropertyName("source_blob")] string SourceBlob,
        [property: JsonPropertyName("source_hash")] string SourceHash,
        [property: JsonPropertyName("config_domains")] JsonElement[] ConfigDomains,
        [property: JsonPropertyName("commands")] JsonElement[] Commands);
}
