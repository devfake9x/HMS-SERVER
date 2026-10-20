using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Hms.Server.Domain.Commands;
using Hms.Server.Infrastructure.Commands;

namespace Hms.Server.UnitTests;

// The catalog is generated from §11 of the MQTT spec (HMS-IDF engineering-docs/api/mqtt-api.md);
// the expectations below restate that table.
public sealed class CatalogTests
{
    private const string PinnedSpecCommit = "83e93d77e4a433efce665d669b567b3acc51b735";
    private const string PinnedSpecPath = "engineering-docs/api/mqtt-api.md";
    // Blob id and SHA-256 of the LF bytes of the spec document at that commit.
    private const string PinnedSpecBlob = "6485e00f4c609a1ba57063cfd7e7aef75955c58b";
    private const string PinnedSpecSha256 = "ef41c113b001459b2cc0d0e6bb8c3fd00f8008c22bd583af8a2e72d6f33e5274";
    private const string CatalogResourceName = "Hms.Server.Infrastructure.Commands.command-catalog.json";

    // SHA-256 of the 94 (type, action) pairs of spec §11, computed with the v1 pair-set
    // algorithm in PairSetSha256. It fingerprints the pair set; it is not the catalog hash.
    private const string ManifestPairSetSha256 = "dbda8317758e5f25c5a4dee8456841ef3689fea4707cf5a27c30ffad656f78d9";

    // Catalog hash of the committed catalog. CI cannot run the generator's --check (no HMS-IDF),
    // so this literal is what makes any content change, even one with a recomputed hash, fail
    // the build. Re-pinning the spec changes it on purpose: update it in the same change, after
    // checking the regenerated catalog against §11.
    private const string PinnedCatalogHash = "b1d47a786879f8be749fb89f38250c73a37b6e72297412e40c9e71a96aeb12c4";

    private static readonly string[] RowFields =
        ["type", "action", "class", "required_perm", "domain_policy", "ttl_ms", "confirm_required", "confirm_template", "dry_run"];

    private static readonly CommandCatalogRow BaselineRow = new(
        "system", "reboot", "F", "sys.reboot", false, 60000, true, "REBOOT-<DEVID>", true);
    private static readonly CommandCatalogDomainRow BaselineDomain = new("adc", "adc.view", "adc.config_edit");

    // Shared with scripts/generate-command-catalog.test.mjs: the same input and the same
    // canonical text. Rows and domains are listed out of order; "Zeta" sorts before "config"
    // only under ordinal comparison; the template exercises the angle-bracket escape.
    private static readonly CommandCatalogRow[] GoldenRows =
    [
        new("system", "reboot", "F", "sys.reboot", false, 60000, true, "REBOOT-<DEVID>", true),
        new("config", "set", "W", null, true, 120000, false, null, false),
        new("Zeta", "b", "R", "z.view", false, 120000, false, null, false),
    ];
    private static readonly CommandCatalogDomainRow[] GoldenDomains =
    [
        new("wifi_ap", "wifi.ap_view", "wifi.ap_edit"),
        new("adc", "adc.view", "adc.config_edit"),
    ];
    private const string GoldenCanonical = """{"commands":[{"type":"Zeta","action":"b","class":"R","required_perm":"z.view","domain_policy":false,"ttl_ms":120000,"confirm_required":false,"confirm_template":null,"dry_run":false},{"type":"config","action":"set","class":"W","required_perm":null,"domain_policy":true,"ttl_ms":120000,"confirm_required":false,"confirm_template":null,"dry_run":false},{"type":"system","action":"reboot","class":"F","required_perm":"sys.reboot","domain_policy":false,"ttl_ms":60000,"confirm_required":true,"confirm_template":"REBOOT-\u003CDEVID\u003E","dry_run":true}],"config_domains":[{"domain":"adc","view_perm":"adc.view","edit_perm":"adc.config_edit"},{"domain":"wifi_ap","view_perm":"wifi.ap_view","edit_perm":"wifi.ap_edit"}]}""";

    [Fact]
    public void Pinned_catalog_metadata_points_at_the_pinned_spec()
    {
        var raw = ReadCommittedCatalog();
        var commit = raw["source_commit"]!.GetValue<string>();

        Assert.Equal(
            new[]
            {
                "catalog_version", "catalog_hash", "canonicalization", "source_repo", "source_commit", "source_path",
                "source_blob", "source_hash", "config_domains", "commands"
            },
            raw.Select(x => x.Key).ToArray());
        Assert.Equal(PinnedSpecCommit, commit);
        Assert.Equal("mqtt-api-" + commit[..9], raw["catalog_version"]!.GetValue<string>());
        Assert.Equal("HMS-IDF", raw["source_repo"]!.GetValue<string>());
        Assert.Equal(PinnedSpecPath, raw["source_path"]!.GetValue<string>());
        Assert.Equal(PinnedSpecBlob, raw["source_blob"]!.GetValue<string>());
        Assert.Equal(PinnedSpecSha256, raw["source_hash"]!.GetValue<string>());
        Assert.Matches("^[0-9a-f]{64}$", raw["catalog_hash"]!.GetValue<string>());
        Assert.Equal(CommandCatalogHash.Canonicalization, raw["canonicalization"]!.GetValue<string>());

        var catalog = new PinnedCommandCatalog();
        Assert.Equal(raw["catalog_version"]!.GetValue<string>(), catalog.Metadata.CatalogVersion);
        Assert.Equal(raw["catalog_hash"]!.GetValue<string>(), catalog.Metadata.CatalogHash);
    }

    [Fact]
    public void Committed_catalog_content_is_pinned()
    {
        Assert.Equal(PinnedCatalogHash, ReadCommittedCatalog()["catalog_hash"]!.GetValue<string>());
        Assert.Equal(PinnedCatalogHash, new PinnedCommandCatalog().Metadata.CatalogHash);
    }

    [Fact]
    public void Committed_catalog_rows_hold_only_spec_fields()
    {
        var raw = ReadCommittedCatalog();

        Assert.All(raw["commands"]!.AsArray(), row => Assert.Equal(RowFields, row!.AsObject().Select(x => x.Key).ToArray()));
        Assert.All(raw["config_domains"]!.AsArray(), row => Assert.Equal(
            new[] { "domain", "view_perm", "edit_perm" }, row!.AsObject().Select(x => x.Key).ToArray()));
        Assert.All(raw["commands"]!.AsArray(), row => Assert.Equal(
            row!["confirm_required"]!.GetValue<bool>(), row["confirm_template"] is not null));
    }

    [Fact]
    public void Pinned_catalog_pairs_match_spec_manifest()
    {
        var catalog = new PinnedCommandCatalog();
        var pairs = catalog.List().Select(x => (x.Type, x.Action)).ToArray();

        Assert.Equal(94, catalog.Count);
        Assert.Equal(94, pairs.Distinct().Count());
        Assert.Equal(12, pairs.Select(x => x.Type).Distinct().Count());
        Assert.Equal(ManifestPairSetSha256, PairSetSha256(pairs));
    }

    [Fact]
    public void Pinned_catalog_denies_every_row_until_action_contract()
    {
        var catalog = new PinnedCommandCatalog();

        Assert.Equal(CommandAvailability.DeniedUntilActionContract, catalog.Find("system", "get_info")!.Availability);
        Assert.Contains(catalog.List(), x => x.Type == "system" && x.Action == "get_info");
        Assert.All(catalog.List(), x => Assert.Equal(CommandAvailability.DeniedUntilActionContract, x.Availability));
    }

    [Fact]
    public void Pinned_catalog_class_counts_match_spec()
    {
        var rows = new PinnedCommandCatalog().List();
        var counts = rows.GroupBy(x => x.Class).ToDictionary(x => x.Key, x => x.Count());

        Assert.Equal(49, counts[CommandClass.Read]);
        Assert.Equal(18, counts[CommandClass.Write]);
        Assert.Equal(17, counts[CommandClass.Control]);
        Assert.Equal(3, counts[CommandClass.Destructive]);
        Assert.Equal(3, counts[CommandClass.Physical]);
        Assert.Equal(4, counts[CommandClass.Flash]);
        Assert.All(rows, x => Assert.Equal(x.Class == CommandClass.Flash, x.Dangerous));
        Assert.Equal(4, rows.Count(x => x.Dangerous));
    }

    [Fact]
    public void Pinned_catalog_exposes_46_firmware_permissions_and_6_config_domains()
    {
        var catalog = new PinnedCommandCatalog();
        var domainRows = catalog.List().Where(x => x.PermissionByDomain is not null).ToArray();

        Assert.Equal(46, catalog.FirmwarePermissions.Count);
        Assert.Contains("sys.config_export", catalog.FirmwarePermissions);
        Assert.Contains("sys.config_import", catalog.FirmwarePermissions);
        Assert.Equal(
            new[] { ("config", "get", CommandClass.Read), ("config", "set", CommandClass.Write) },
            domainRows.Select(x => (x.Type, x.Action, x.Class)).ToArray());
        Assert.All(domainRows, row => Assert.Equal(
            new[] { "adc", "mqtt", "network", "system", "wifi_ap", "wifi_sta" },
            row.PermissionByDomain!.Keys.Order(StringComparer.Ordinal).ToArray()));
    }

    [Fact]
    public void Every_row_has_exactly_one_permission_source()
    {
        Assert.All(new PinnedCommandCatalog().List(), row => Assert.True(
            row.RequiredPermission is not null ^ row.PermissionByDomain is not null,
            $"{row.Type}.{row.Action}"));
    }

    [Fact]
    public void Pinned_catalog_ttl_matches_spec_defaults()
    {
        var rows = new PinnedCommandCatalog().List();
        var sixtySeconds = rows.Where(x => x.DefaultTtlMs == 60_000).ToArray();

        Assert.Equal(24, sixtySeconds.Length);
        Assert.Equal(3, sixtySeconds.Count(x => x.ConfirmTemplate is not null));
        Assert.Equal(21, sixtySeconds.Count(x =>
            x.Type == "network" && x.Action.StartsWith("wifi_", StringComparison.Ordinal)));
        Assert.Equal(70, rows.Count(x => x.DefaultTtlMs == CommandDefinition.DispatcherDefaultTtlMs));
        Assert.DoesNotContain(rows, x => x.DefaultTtlMs is null);
    }

    [Fact]
    public void Pinned_catalog_confirm_rows_match_spec_templates()
    {
        var rows = new PinnedCommandCatalog().List();

        Assert.Equal(
            new[]
            {
                "ota.apply=OTA-APPLY-<DEVID>",
                "system.factory_reset=FACTORY-RESET-<DEVID>",
                "system.reboot=REBOOT-<DEVID>"
            },
            rows.Where(x => x.ConfirmTemplate is not null)
                .Select(x => $"{x.Type}.{x.Action}={x.ConfirmTemplate}")
                .Order(StringComparer.Ordinal)
                .ToArray());
        Assert.Equal(
            new[] { "ota.apply", "system.factory_reset", "system.reboot" },
            rows.Where(x => x.DryRunSupported)
                .Select(x => $"{x.Type}.{x.Action}")
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    [Theory]
    [InlineData("ota", "remote_start", CommandClass.Flash, "sys.ota", 120000)]
    [InlineData("ota", "remote_config_get", CommandClass.Read, "sys.ota", 120000)]
    [InlineData("modbus", "restart_rtu", CommandClass.Control, "mb_rtu.config_edit", 120000)]
    [InlineData("modbus", "check_rtu_config", CommandClass.Read, "mb_rtu.config_view", 120000)]
    [InlineData("network", "wifi_known_remove", CommandClass.Destructive, "wifi.known_edit", 60000)]
    [InlineData("network", "wifi_scan_results", CommandClass.Read, "wifi.scan", 60000)]
    [InlineData("control", "set_output", CommandClass.Physical, "gpio.do_write", 120000)]
    [InlineData("system", "set_time", CommandClass.Control, "sys.time_edit", 120000)]
    [InlineData("broker", "delete_signal", CommandClass.Destructive, "broker.signal_edit", 120000)]
    [InlineData("adc", "get_status", CommandClass.Read, "adc.view", 120000)]
    [InlineData("mqtt", "get_status", CommandClass.Read, "mqtt.status_view", 120000)]
    public void Pinned_catalog_row_matches_spec(
        string type, string action, CommandClass commandClass, string permission, int ttlMs)
    {
        var row = new PinnedCommandCatalog().Find(type, action);

        Assert.NotNull(row);
        Assert.Equal(commandClass, row.Class);
        Assert.Equal(permission, row.RequiredPermission);
        Assert.Equal(ttlMs, row.DefaultTtlMs);
    }

    public static TheoryData<string> PolicyFields()
    {
        // Every serialized field of a row and of a config domain is a hash input.
        var data = new TheoryData<string>();
        foreach (var field in JsonSerializer.SerializeToNode(BaselineRow)!.AsObject().Select(x => x.Key)
                     .Concat(JsonSerializer.SerializeToNode(BaselineDomain)!.AsObject().Select(x => x.Key)))
            data.Add(field);
        return data;
    }

    [Theory]
    [MemberData(nameof(PolicyFields))]
    public void Catalog_hash_changes_when_any_policy_field_changes(string field)
    {
        var baseline = CommandCatalogHash.Compute([BaselineRow], [BaselineDomain]);
        var (row, domain) = field switch
        {
            "type" => (BaselineRow with { Type = "system2" }, BaselineDomain),
            "action" => (BaselineRow with { Action = "reboot2" }, BaselineDomain),
            "class" => (BaselineRow with { Class = "C" }, BaselineDomain),
            "required_perm" => (BaselineRow with { RequiredPerm = "sys.info_view" }, BaselineDomain),
            "domain_policy" => (BaselineRow with { DomainPolicy = true }, BaselineDomain),
            "ttl_ms" => (BaselineRow with { TtlMs = 120000 }, BaselineDomain),
            "confirm_required" => (BaselineRow with { ConfirmRequired = false }, BaselineDomain),
            "confirm_template" => (BaselineRow with { ConfirmTemplate = "OTA-APPLY-<DEVID>" }, BaselineDomain),
            "dry_run" => (BaselineRow with { DryRun = false }, BaselineDomain),
            "domain" => (BaselineRow, BaselineDomain with { Domain = "mqtt" }),
            "view_perm" => (BaselineRow, BaselineDomain with { ViewPerm = "mqtt.status_view" }),
            "edit_perm" => (BaselineRow, BaselineDomain with { EditPerm = "mqtt.config_edit" }),
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Uncovered policy field.")
        };

        Assert.NotEqual(baseline, CommandCatalogHash.Compute([row], [domain]));
    }

    [Fact]
    public void Catalog_hash_ignores_input_order()
    {
        var read = new CommandCatalogRow("adc", "get_status", "R", "adc.view", false, 120000, false, null, false);
        var mqtt = new CommandCatalogDomainRow("mqtt", "mqtt.status_view", "mqtt.config_edit");

        Assert.Equal(
            CommandCatalogHash.Compute([read, BaselineRow], [BaselineDomain, mqtt]),
            CommandCatalogHash.Compute([BaselineRow, read], [mqtt, BaselineDomain]));
    }

    [Fact]
    public void Catalog_hash_matches_generator_golden_vector()
    {
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(GoldenCanonical))).ToLowerInvariant();

        Assert.Equal(expected, CommandCatalogHash.Compute(GoldenRows, GoldenDomains));
    }

    [Fact]
    public void Committed_catalog_loads_through_the_stream_entry_point()
    {
        var catalog = Load(ReadCommittedCatalog());

        Assert.Equal(94, catalog.Count);
        Assert.Equal(new PinnedCommandCatalog().Metadata, catalog.Metadata);
    }

    [Fact]
    public void Tampered_catalog_fails_to_load_with_hash_mismatch()
    {
        // CI does not run the generator's --check, so the embedded hash is the only guard
        // against a hand-edited catalog.
        var raw = ReadCommittedCatalog();
        FindRow(raw, "system", "get_info")["required_perm"] = "sys.reboot";

        var error = Assert.Throws<InvalidOperationException>(() => Load(raw));
        Assert.Equal("COMMAND_CATALOG_HASH_MISMATCH", error.Message);
    }

    [Fact]
    public void Catalog_hash_must_be_lowercase_hex()
    {
        var raw = ReadCommittedCatalog();
        raw["catalog_hash"] = raw["catalog_hash"]!.GetValue<string>().ToUpperInvariant();

        var error = Assert.Throws<InvalidOperationException>(() => Load(raw));
        Assert.Equal("COMMAND_CATALOG_HASH_MISMATCH", error.Message);
    }

    [Fact]
    public void Ttl_at_the_spec_cap_loads()
    {
        var raw = ReadCommittedCatalog();
        FindRow(raw, "system", "get_info")["ttl_ms"] = 86_400_000;
        Rehash(raw);

        Assert.Equal(86_400_000, Load(raw).Find("system", "get_info")!.DefaultTtlMs);
    }

    [Fact]
    public void Dry_run_is_projected_from_its_own_column()
    {
        var raw = ReadCommittedCatalog();
        FindRow(raw, "system", "get_info")["dry_run"] = true;
        Rehash(raw);

        var row = Load(raw).Find("system", "get_info")!;
        Assert.True(row.DryRunSupported);
        Assert.Null(row.ConfirmTemplate);
    }

    [Theory]
    [InlineData("required_perm", "sys+reboot")]
    [InlineData("required_perm", "sys reboot")]
    [InlineData("required_perm", "sys.réboot")]
    [InlineData("type", "sys+tem")]
    [InlineData("action", "re boot")]
    [InlineData("confirm_template", "REBOOT <DEVID>")]
    [InlineData("domain", "ad c")]
    [InlineData("view_perm", "adc+view")]
    [InlineData("edit_perm", "adc.config_édit")]
    public void Policy_string_outside_charset_fails_to_load(string field, string value)
    {
        var raw = ReadCommittedCatalog();
        var target = field is "domain" or "view_perm" or "edit_perm"
            ? FindConfigDomain(raw, "adc")
            : FindRow(raw, "system", "reboot");
        target[field] = value;
        Rehash(raw);

        var error = Assert.Throws<InvalidOperationException>(() => Load(raw));
        Assert.Equal("COMMAND_CATALOG_ROW_INVALID", error.Message);
    }

    [Theory]
    [InlineData("missing_field")]
    [InlineData("unknown_field")]
    [InlineData("both_permission_sources")]
    [InlineData("no_permission_source")]
    [InlineData("unknown_class")]
    [InlineData("confirm_required_without_template")]
    [InlineData("template_without_confirm_required")]
    [InlineData("non_positive_ttl")]
    [InlineData("null_ttl")]
    [InlineData("ttl_above_spec_cap")]
    [InlineData("null_type")]
    [InlineData("lowercase_class")]
    [InlineData("template_without_devid")]
    [InlineData("domain_row_outside_config")]
    [InlineData("config_set_as_read")]
    [InlineData("config_get_as_write")]
    [InlineData("domain_row_with_get_action_outside_config")]
    [InlineData("only_config_get_by_domain")]
    [InlineData("domain_rows_without_config_domains")]
    [InlineData("duplicate_pair")]
    [InlineData("duplicate_domain")]
    public void Invalid_catalog_row_fails_to_load(string mutation)
    {
        var raw = ReadCommittedCatalog();
        var row = FindRow(raw, "system", "get_info");
        switch (mutation)
        {
            case "missing_field": row.Remove("dry_run"); break;
            case "unknown_field": row["audit_publish"] = false; break;
            case "both_permission_sources": row["domain_policy"] = true; break;
            case "no_permission_source": row["required_perm"] = null; break;
            case "unknown_class": row["class"] = "X"; break;
            case "confirm_required_without_template": row["confirm_required"] = true; break;
            case "template_without_confirm_required": row["confirm_template"] = "INFO-<DEVID>"; break;
            case "non_positive_ttl": row["ttl_ms"] = 0; break;
            case "null_ttl": row["ttl_ms"] = null; break;
            // Spec §3 bounds ttl_ms to 1..86400000.
            case "ttl_above_spec_cap": row["ttl_ms"] = 86_400_001; break;
            case "null_type": row["type"] = null; break;
            case "lowercase_class": row["class"] = "r"; break;
            case "template_without_devid":
                FindRow(raw, "system", "reboot")["confirm_template"] = "REBOOT-NOW";
                break;
            // Spec §11 takes a permission from the config-domain table only for its two columns.
            case "domain_row_outside_config":
                row["required_perm"] = null;
                row["domain_policy"] = true;
                break;
            case "config_set_as_read": FindRow(raw, "config", "set")["class"] = "R"; break;
            case "config_get_as_write": FindRow(raw, "config", "get")["class"] = "W"; break;
            case "domain_row_with_get_action_outside_config":
                var adcRow = FindRow(raw, "adc", "get_status");
                adcRow["action"] = "get";
                adcRow["required_perm"] = null;
                adcRow["domain_policy"] = true;
                break;
            // The domain table has two columns, so both of its actions must use it or neither.
            case "only_config_get_by_domain":
                var setRow = FindRow(raw, "config", "set");
                setRow["required_perm"] = "sys.config_import";
                setRow["domain_policy"] = false;
                break;
            case "domain_rows_without_config_domains": raw["config_domains"] = new JsonArray(); break;
            case "duplicate_pair": raw["commands"]!.AsArray().Add(row.DeepClone()); break;
            case "duplicate_domain":
                var domains = raw["config_domains"]!.AsArray();
                domains.Add(domains[0]!.DeepClone());
                break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null);
        }
        Rehash(raw);

        var error = Assert.Throws<InvalidOperationException>(() => Load(raw));
        Assert.Equal("COMMAND_CATALOG_ROW_INVALID", error.Message);
    }

    [Theory]
    [InlineData("canonicalization")]
    [InlineData("blank_hash")]
    [InlineData("missing_version")]
    [InlineData("unknown_field")]
    [InlineData("empty_catalog")]
    public void Invalid_catalog_metadata_fails_to_load(string mutation)
    {
        var raw = ReadCommittedCatalog();
        switch (mutation)
        {
            case "canonicalization": raw["canonicalization"] = "v1"; break;
            case "blank_hash": raw["catalog_hash"] = " "; break;
            case "empty_catalog": raw["commands"] = new JsonArray(); break;
            case "missing_version": raw.Remove("catalog_version"); break;
            case "unknown_field": raw["dispatcher_default_ttl_ms"] = 120000; break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null);
        }

        var error = Assert.Throws<InvalidOperationException>(() => Load(raw));
        Assert.Equal("COMMAND_CATALOG_METADATA_INVALID", error.Message);
    }

    [Theory]
    [InlineData("catalog_version")]
    [InlineData("required_perm")]
    public void Duplicate_json_property_fails_to_load(string property)
    {
        // A JsonObject cannot hold a duplicate key, so the duplicate is spliced into the text in
        // front of the first occurrence. A duplicate anywhere fails the strict document parse.
        var json = ReadCommittedCatalog().ToJsonString();
        var first = json.IndexOf($"\"{property}\":", StringComparison.Ordinal);
        Assert.True(first >= 0, property);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json.Insert(first, $"\"{property}\":\"duplicate\",")));

        var error = Assert.Throws<InvalidOperationException>(() => PinnedCommandCatalog.Load(stream));
        Assert.Equal("COMMAND_CATALOG_METADATA_INVALID", error.Message);
    }

    // The v1 catalog hash, kept only to fingerprint the pair set:
    // sorted unique [{type, action}] rows, compact System.Text.Json, UTF-8, SHA-256.
    private static string PairSetSha256(IEnumerable<(string Type, string Action)> pairs)
    {
        var canonical = pairs.Distinct()
            .OrderBy(x => x.Type, StringComparer.Ordinal)
            .ThenBy(x => x.Action, StringComparer.Ordinal)
            .Select(x => new PairRow(x.Type, x.Action))
            .ToArray();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical))))
            .ToLowerInvariant();
    }

    private static JsonObject ReadCommittedCatalog()
    {
        using var stream = typeof(PinnedCommandCatalog).Assembly.GetManifestResourceStream(CatalogResourceName)!;
        return JsonNode.Parse(stream)!.AsObject();
    }

    private static JsonObject FindRow(JsonObject raw, string type, string action) =>
        raw["commands"]!.AsArray().Select(x => x!.AsObject()).Single(x =>
            x["type"]!.GetValue<string>() == type && x["action"]!.GetValue<string>() == action);

    private static JsonObject FindConfigDomain(JsonObject raw, string domain) =>
        raw["config_domains"]!.AsArray().Select(x => x!.AsObject()).Single(x =>
            x["domain"]!.GetValue<string>() == domain);

    // Recomputes the embedded hash so a test reaches the row validation behind the hash check.
    // Default options tolerate the missing or unknown fields some mutations introduce; a row
    // that cannot be typed at all fails the strict row parse before the hash, so it is skipped.
    private static void Rehash(JsonObject raw)
    {
        try
        {
            var commands = raw["commands"]!.AsArray().Select(x => x.Deserialize<CommandCatalogRow>()!).ToArray();
            var domains = raw["config_domains"]!.AsArray().Select(x => x.Deserialize<CommandCatalogDomainRow>()!).ToArray();
            raw["catalog_hash"] = CommandCatalogHash.Compute(commands, domains);
        }
        catch (JsonException)
        {
        }
    }

    private static PinnedCommandCatalog Load(JsonObject raw)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(raw.ToJsonString()));
        return PinnedCommandCatalog.Load(stream);
    }

    private sealed record PairRow(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("action")] string Action);
}
