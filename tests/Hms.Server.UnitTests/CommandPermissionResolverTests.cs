using System.Text.Json;
using Hms.Server.Application.Commands;
using Hms.Server.Domain.Commands;
using Hms.Server.Infrastructure.Commands;

namespace Hms.Server.UnitTests;

public sealed class CommandPermissionResolverTests
{
    // JSON escapes below are assembled from this constant so the source holds no escape sequence.
    private const string Backslash = "\\";
    private static readonly PinnedCommandCatalog Catalog = new();

    [Fact]
    public void Config_get_resolves_view_permission_from_domain() =>
        Assert.Equal("wifi.ap_view", CommandPermissionResolver.Resolve(Row("config", "get"), Payload(("domain", "wifi_ap"))));

    [Fact]
    public void Config_set_resolves_edit_permission_from_domain() =>
        Assert.Equal("mqtt.config_edit", CommandPermissionResolver.Resolve(Row("config", "set"), Payload(("domain", "mqtt"))));

    [Fact]
    public void Json_element_string_domain_is_accepted()
    {
        var payload = Json("""{"domain":"wifi_sta"}""");

        Assert.IsType<JsonElement>(payload["domain"]);
        Assert.Equal("wifi.sta_edit", CommandPermissionResolver.Resolve(Row("config", "set"), payload));
    }

    [Fact]
    public void Json_escaped_domain_is_compared_on_its_unescaped_value()
    {
        // The underscore written as a JSON unicode escape: an escape is encoding, not value.
        var payload = Json("{\"domain\":\"wifi" + Backslash + "u005fap\"}");

        Assert.Equal("wifi.ap_view", CommandPermissionResolver.Resolve(Row("config", "get"), payload));
    }

    [Theory]
    [InlineData("uD800")]
    [InlineData("uDC00")]
    [InlineData("uD800x")]
    public void Domain_holding_a_lone_surrogate_escape_resolves_to_null(string escape)
    {
        var payload = Json("{\"domain\":\"" + Backslash + escape + "\"}");

        Assert.Null(CommandPermissionResolver.Resolve(Row("config", "get"), payload));
        Assert.Null(CommandPermissionResolver.Resolve(Row("config", "set"), payload));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("number")]
    [InlineData("json_number")]
    [InlineData("json_object")]
    [InlineData("json_null")]
    [InlineData("unknown")]
    [InlineData("upper_case")]
    [InlineData("mixed_case")]
    [InlineData("leading_space")]
    [InlineData("empty")]
    [InlineData("case_insensitive_key")]
    public void Domain_row_without_a_valid_string_domain_resolves_to_null(string variant)
    {
        IReadOnlyDictionary<string, object?> payload = variant switch
        {
            "missing" => Payload(),
            "null" => Payload(("domain", null)),
            "number" => Payload(("domain", 42)),
            "json_number" => Json("""{"domain":42}"""),
            "json_object" => Json("""{"domain":{"name":"wifi_ap"}}"""),
            "json_null" => Json("""{"domain":null}"""),
            "unknown" => Payload(("domain", "bogus")),
            "upper_case" => Payload(("domain", "WIFI_AP")),
            "mixed_case" => Payload(("domain", "Wifi_ap")),
            "leading_space" => Payload(("domain", " wifi_ap")),
            "empty" => Payload(("domain", "")),
            // Server policy: the key match is exact, whatever comparer the dictionary carries.
            "case_insensitive_key" => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Domain"] = "wifi_ap"
            },
            _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, null)
        };

        Assert.Null(CommandPermissionResolver.Resolve(Row("config", "get"), payload));
        Assert.Null(CommandPermissionResolver.Resolve(Row("config", "set"), payload));
    }

    [Theory]
    [InlineData("adc", "get_status", "adc.view")]
    [InlineData("battery", "get_status", "sys.battery_view")]
    [InlineData("mqtt", "get_status", "mqtt.status_view")]
    [InlineData("ota", "get_status", "sys.info_view")]
    [InlineData("sms", "get_status", "sms.view")]
    [InlineData("control", "get_status", "gpio.do_view")]
    [InlineData("adc", "get_config", "adc.view")]
    [InlineData("mqtt", "get_config", "mqtt.status_view")]
    public void Same_action_name_resolves_per_namespace(string type, string action, string expected) =>
        Assert.Equal(expected, CommandPermissionResolver.Resolve(Row(type, action), Payload()));

    [Fact]
    public void Fixed_permission_rows_ignore_payload_domain() =>
        Assert.Equal("adc.view", CommandPermissionResolver.Resolve(Row("adc", "get_status"), Payload(("domain", "mqtt"))));

    [Fact]
    public void Row_without_permission_source_resolves_to_null()
    {
        var row = new CommandDefinition("synthetic", "noop", null, CommandAvailability.Enabled, false, true, null);

        Assert.Null(CommandPermissionResolver.Resolve(row, Payload(("domain", "adc"))));
        Assert.Null(CommandPermissionResolver.DomainPermissions(row));
        Assert.False(CommandPermissionResolver.IsHighRisk(row));
    }

    [Fact]
    public void Domain_permissions_follow_row_class()
    {
        var get = CommandPermissionResolver.DomainPermissions(Row("config", "get"))!;
        var set = CommandPermissionResolver.DomainPermissions(Row("config", "set"))!;

        Assert.Equal(6, get.Count);
        Assert.Equal(6, set.Count);
        Assert.Equal("sys.config_export", get["system"]);
        Assert.Equal("sys.config_import", set["system"]);
        Assert.Equal("wifi.ap_view", get["wifi_ap"]);
        Assert.Equal("wifi.ap_edit", set["wifi_ap"]);
        Assert.Null(CommandPermissionResolver.DomainPermissions(Row("adc", "get_status")));
    }

    [Fact]
    public void High_risk_rows_match_policy()
    {
        Assert.Equal(
            new[]
            {
                "config.set", "mqtt.set_config", "network.configure_ethernet", "network.set_priority",
                "network.wifi_known_connect", "network.wifi_sta_config_set", "network.wifi_sta_connect",
                "network.wifi_sta_disable", "network.wifi_sta_disconnect", "network.wifi_sta_enable",
                "ota.apply", "ota.remote_config_get", "ota.remote_start", "system.factory_reset", "system.reboot"
            },
            Catalog.List()
                .Where(CommandPermissionResolver.IsHighRisk)
                .Select(x => $"{x.Type}.{x.Action}")
                .Order(StringComparer.Ordinal)
                .ToArray());
        Assert.False(CommandPermissionResolver.IsHighRisk(Row("config", "get")));
    }

    private static CommandDefinition Row(string type, string action) => Catalog.Find(type, action)!;

    private static Dictionary<string, object?> Payload(params (string Key, object? Value)[] entries) =>
        entries.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);

    private static Dictionary<string, object?> Json(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, object?>>(json)!;
}
