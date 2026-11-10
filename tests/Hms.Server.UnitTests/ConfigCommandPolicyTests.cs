using System.Security.Cryptography;
using System.Text.Json;
using Hms.Server.Application.Commands;
using Hms.Server.Application.DeviceLink;
using Hms.Server.Application.Provisioning;
using Hms.Server.Domain.Authorization;
using Hms.Server.Domain.Commands;
using Hms.Server.Domain.Devices;
using Hms.Server.Domain.Organizations;
using Hms.Server.Infrastructure.Commands;

namespace Hms.Server.UnitTests;

/// <summary>The device takes any signed config write as owner (spec R11), so the server checks the
/// exact payload of every opened write before signing, and refuses writes it has no rule for.</summary>
public sealed class ConfigCommandPolicyTests
{
    private static readonly CommandDefinition SetHostname = Definition("system", "set_hostname", "sys.hostname_edit", CommandClass.Write);
    private static readonly CommandDefinition SetTimezone = Definition("system", "set_timezone", "sys.time_edit", CommandClass.Write);
    private static readonly CommandDefinition SyncNtp = Definition("system", "sync_ntp", "sys.time_edit", CommandClass.Control);
    private static readonly CommandDefinition SetChannel = Definition("adc", "set_channel", "adc.config_edit", CommandClass.Write);
    private static readonly CommandDefinition ToggleChannel = Definition("adc", "toggle_channel", "adc.enable", CommandClass.Control);

    public static TheoryData<string, string, string> ValidPayloads => new()
    {
        { "system", "set_hostname", """{"hostname":"gateway-870c"}""" },
        { "system", "set_hostname", """{"hostname":"a"}""" },
        { "system", "set_hostname", $$"""{"hostname":"{{new string('h', 63)}}"}""" },
        { "system", "set_timezone", """{"timezone":"ICT-7"}""" },
        { "system", "set_timezone", """{"timezone":"UTC0"}""" },
        { "system", "sync_ntp", "{}" },
        { "adc", "set_channel", """{"ai":1,"name":"Pressure in"}""" },
        { "adc", "set_channel", """{"ai":16,"name":"Nhiệt độ","type":"4-20mA","enabled":true}""" },
        { "adc", "toggle_channel", """{"ai":3,"enabled":false}""" }
    };

    public static TheoryData<string, string, string> InvalidPayloads => new()
    {
        { "system", "set_hostname", "{}" },
        { "system", "set_hostname", """{"hostname":""}""" },
        { "system", "set_hostname", """{"hostname":"gate_way"}""" },
        { "system", "set_hostname", """{"hostname":"gate.way"}""" },
        { "system", "set_hostname", """{"hostname":"-gateway"}""" },
        { "system", "set_hostname", """{"hostname":"gateway-"}""" },
        { "system", "set_hostname", """{"hostname":"gateway\n"}""" },
        { "system", "set_hostname", $$"""{"hostname":"{{new string('h', 64)}}"}""" },
        { "system", "set_hostname", """{"hostname":42}""" },
        { "system", "set_hostname", """{"hostname":"gateway","extra":1}""" },
        { "system", "set_timezone", """{"timezone":"Asia/Ho_Chi_Minh"}""" },
        { "system", "set_timezone", """{"timezone":"ict-7"}""" },
        { "system", "set_timezone", """{"timezone":null}""" },
        { "system", "sync_ntp", """{"force":true}""" },
        { "adc", "set_channel", """{"ai":0,"name":"x"}""" },
        { "adc", "set_channel", """{"ai":17,"name":"x"}""" },
        { "adc", "set_channel", """{"ai":"1","name":"x"}""" },
        { "adc", "set_channel", """{"ai":1.5,"name":"x"}""" },
        { "adc", "set_channel", """{"ai":1}""" },
        { "adc", "set_channel", """{"ai":1,"name":""}""" },
        { "adc", "set_channel", """{"ai":1,"name":"   "}""" },
        { "adc", "set_channel", """{"ai":1,"name":"line\nbreak"}""" },
        { "adc", "set_channel", """{"ai":1,"name":"evil‮txt"}""" },
        { "adc", "set_channel", """{"ai":1,"name":"zero​width"}""" },
        { "adc", "set_channel", """{"ai":1,"name":"ㅤ"}""" },
        { "adc", "set_channel", """{"ai":1,"name":"AIᅟ1"}""" },
        { "adc", "set_channel", """{"ai":1,"name":"⠀⠀"}""" },
        { "adc", "set_channel", """{"ai":1,"name":"A͏I"}""" },
        { "adc", "set_channel", """{"ai":1,"name":"AI️"}""" },
        { "adc", "set_channel", """{"ai":1,"name":"ﾠx"}""" },
        { "adc", "set_channel", $$"""{"ai":1,"name":"{{new string('n', 33)}}"}""" },
        { "adc", "set_channel", """{"ai":1,"name":"x","type":"4 20mA"}""" },
        { "adc", "set_channel", """{"ai":1,"name":"x","type":""}""" },
        { "adc", "set_channel", """{"ai":1,"name":"x","enabled":1}""" },
        { "adc", "set_channel", """{"ai":1,"name":"x","unit":"bar"}""" },
        { "adc", "toggle_channel", """{"ai":1}""" },
        { "adc", "toggle_channel", """{"ai":1,"enabled":"true"}""" },
        { "adc", "toggle_channel", """{"ai":0,"enabled":true}""" },
        { "adc", "toggle_channel", """{"ai":1,"enabled":true,"name":"x"}""" }
    };

    [Theory]
    [MemberData(nameof(ValidPayloads))]
    public void Valid_config_writes_pass(string type, string action, string json)
    {
        Assert.Null(ConfigCommandPolicy.Check(Opened(type, action), Payload(json)));
    }

    [Theory]
    [MemberData(nameof(InvalidPayloads))]
    public void Config_write_payloads_the_device_should_never_see_are_refused(string type, string action, string json)
    {
        Assert.Equal(ConfigCommandPolicy.PayloadInvalid, ConfigCommandPolicy.Check(Opened(type, action), Payload(json)));
    }

    [Fact]
    public void Channel_names_with_a_lone_surrogate_are_refused()
    {
        // Built here, not in InlineData: xUnit serializes theory data and would replace the surrogate.
        string[] names = ["x" + '\uD800', "x" + '\uDC00' + "y", new string(['\uDC00', '\uD800'])];

        Assert.All(names, name => Assert.Equal(ConfigCommandPolicy.PayloadInvalid,
            ConfigCommandPolicy.Check(SetChannel, new Dictionary<string, object?> { ["ai"] = 1, ["name"] = name })));
        Assert.Null(ConfigCommandPolicy.Check(SetChannel,
            new Dictionary<string, object?> { ["ai"] = 1, ["name"] = "x" + "😀" }));
    }

    [Theory]
    [InlineData("""{"ai":1,"name":"x\uD800"}""")]
    [InlineData("""{"ai":1,"name":"\uDC00"}""")]
    public async Task A_lone_surrogate_escape_from_the_api_is_refused_not_thrown_on(string apiJson)
    {
        // System.Text.Json throws when it unescapes "\uD800" to a string. That must end as a refused
        // payload, not an exception that leaves the command's audit row half written.
        Assert.Equal(ConfigCommandPolicy.PayloadInvalid, ConfigCommandPolicy.Check(SetChannel, Payload(apiJson)));

        var device = Device.Create("RTU", Organization.Create("Lab").Id);
        var bindings = new InMemoryDeviceMqttBindingStore();
        bindings.Upsert(DeviceMqttBinding.Create(device.Id, "hms_toan_lab", "gateway_870c", null));
        var secrets = new RandomProvisioningSecretGenerator();
        secrets.Import(device.Id, device.KeyGeneration, RandomNumberGenerator.GetBytes(32));
        var adapter = new HmsIdfCommandProtocolAdapter(bindings, secrets, new InMemoryDeviceLinkStateStore());

        var prepared = await adapter.PrepareAsync(Plan(device, SetChannel, apiJson));

        Assert.False(prepared.CanPublish);
        Assert.Equal(ConfigCommandPolicy.PayloadInvalid, prepared.FailureCode);
    }

    [Fact]
    public void Plain_values_from_server_code_are_accepted_like_api_json()
    {
        Assert.Null(ConfigCommandPolicy.Check(ToggleChannel, new Dictionary<string, object?> { ["ai"] = 2, ["enabled"] = true }));
        Assert.Null(ConfigCommandPolicy.Check(SetHostname, new Dictionary<string, object?> { ["hostname"] = "gw-1" }));
    }

    [Theory]
    [InlineData("system", "set_ntp_servers", CommandClass.Control)]
    [InlineData("system", "set_time", CommandClass.Control)]
    [InlineData("network", "set_priority", CommandClass.Write)]
    [InlineData("config", "set", CommandClass.Write)]
    [InlineData("network", "wifi_known_remove", CommandClass.Destructive)]
    [InlineData("system", "factory_reset", CommandClass.Flash)]
    [InlineData("ota", "apply", CommandClass.Flash)]
    [InlineData("ota", "remote_start", CommandClass.Flash)]
    public void Writes_without_a_payload_rule_fail_closed(string type, string action, CommandClass commandClass)
    {
        Assert.Equal(ConfigCommandPolicy.ActionUnsupported,
            ConfigCommandPolicy.Check(Definition(type, action, "x.edit", commandClass), new Dictionary<string, object?>()));
    }

    [Fact]
    public void Reads_and_relay_outputs_are_left_to_their_own_rules()
    {
        Assert.Null(ConfigCommandPolicy.Check(Definition("system", "get_time", "sys.time_view", CommandClass.Read), new Dictionary<string, object?> { ["x"] = 1 }));
        Assert.Null(ConfigCommandPolicy.Check(Definition("control", "set_output", "gpio.do_write", CommandClass.Physical), new Dictionary<string, object?>()));
    }

    [Fact]
    public void Reboot_takes_an_empty_payload_only()
    {
        var reboot = Definition("system", "reboot", "sys.reboot", CommandClass.Flash);

        Assert.Null(ConfigCommandPolicy.Check(reboot, Payload("{}")));
        // delay_ms is left out: the device default (2000 ms) gives the server a known deadline, and
        // delay_ms:0 could restart before the reply leaves the device.
        foreach (var json in new[]
                 {
                     """{"delay_ms":2000}""", """{"delay_ms":0}""", """{"confirm":"REBOOT-gateway_870c"}""",
                     """{"confirm_token":"REBOOT-gateway_870c"}"""
                 })
            Assert.Equal(ConfigCommandPolicy.PayloadInvalid, ConfigCommandPolicy.Check(reboot, Payload(json)));
    }

    [Fact]
    public async Task A_reboot_with_a_payload_is_refused_before_the_key_is_read()
    {
        var device = Device.Create("RTU", Organization.Create("Lab").Id);
        var bindings = new InMemoryDeviceMqttBindingStore();
        bindings.Upsert(DeviceMqttBinding.Create(device.Id, "hms_toan_lab", "gateway_870c", null));
        var secrets = new CountingSecrets();
        secrets.Inner.Import(device.Id, device.KeyGeneration, RandomNumberGenerator.GetBytes(32));
        var adapter = new HmsIdfCommandProtocolAdapter(bindings, secrets, new InMemoryDeviceLinkStateStore());
        var reboot = new CommandDefinition("system", "reboot", "sys.reboot", CommandAvailability.Enabled, true, false, 60_000,
            CommandClass.Flash, ConfirmTemplate: "REBOOT-<DEVID>", DryRunSupported: true);
        var plan = Plan(device, reboot, """{"delay_ms":2000}""");
        plan = plan with { Request = plan.Request with { Confirm = "REBOOT-gateway_870c" } };

        var prepared = await adapter.PrepareAsync(plan);

        Assert.False(prepared.CanPublish);
        Assert.Equal(ConfigCommandPolicy.PayloadInvalid, prepared.FailureCode);
        Assert.Equal(0, secrets.FindCalls);
    }

    [Fact]
    public void Every_opened_config_write_has_a_payload_rule()
    {
        var catalog = new PinnedCommandCatalog();
        var valid = ValidPayloads.Select(x => ((string)x[0], (string)x[1])).ToHashSet();

        // The opened flash row (reboot) has its own rule and test above; it must not fall to
        // the fail-closed default.
        Assert.All(PinnedCommandCatalog.FlashActions, pair =>
            Assert.NotEqual(ConfigCommandPolicy.ActionUnsupported,
                ConfigCommandPolicy.Check(catalog.Find(pair.Type, pair.Action)!, new Dictionary<string, object?>())));

        Assert.All(PinnedCommandCatalog.ConfigWriteActions, pair =>
        {
            Assert.Contains(pair, valid);
            Assert.NotEqual(ConfigCommandPolicy.ActionUnsupported,
                ConfigCommandPolicy.Check(catalog.Find(pair.Type, pair.Action)!, new Dictionary<string, object?>()));
        });
    }

    [Fact]
    public void The_lab_gateway_timezone_is_supported()
    {
        Assert.Contains("ICT-7", ConfigCommandPolicy.SupportedTimezones);
        Assert.Contains("UTC0", ConfigCommandPolicy.SupportedTimezones);
    }

    [Fact]
    public async Task Adapter_never_reads_the_key_or_signs_a_refused_config_write()
    {
        var device = Device.Create("RTU", Organization.Create("Lab").Id);
        var bindings = new InMemoryDeviceMqttBindingStore();
        bindings.Upsert(DeviceMqttBinding.Create(device.Id, "hms_toan_lab", "gateway_870c", null));
        var secrets = new CountingSecrets();
        secrets.Inner.Import(device.Id, device.KeyGeneration, RandomNumberGenerator.GetBytes(32));
        var adapter = new HmsIdfCommandProtocolAdapter(bindings, secrets, new InMemoryDeviceLinkStateStore());

        var refused = await adapter.PrepareAsync(Plan(device, SetHostname, """{"hostname":"bad_name"}"""));

        Assert.False(refused.CanPublish);
        Assert.Equal(ConfigCommandPolicy.PayloadInvalid, refused.FailureCode);
        Assert.True(refused.Payload.IsEmpty);
        Assert.Equal(0, secrets.FindCalls);

        var allowed = await adapter.PrepareAsync(Plan(device, SetHostname, """{"hostname":"gateway-t"}"""));
        Assert.True(allowed.CanPublish);
        Assert.Equal(1, secrets.FindCalls);
    }

    private static CommandDefinition Opened(string type, string action) =>
        new[] { SetHostname, SetTimezone, SyncNtp, SetChannel, ToggleChannel }.Single(x => x.Type == type && x.Action == action);

    private static CommandDefinition Definition(string type, string action, string permission, CommandClass commandClass) =>
        new(type, action, permission, CommandAvailability.Enabled, false, true, 120_000, commandClass);

    private static CommandExecutionPlan Plan(Device device, CommandDefinition definition, string json)
    {
        var now = DateTimeOffset.UtcNow;
        var request = new DeviceCommandRequest(device.Id, new UserId(Guid.NewGuid()), definition.Type, definition.Action,
            Payload(json), Guid.NewGuid().ToString("N"), now);
        return new CommandExecutionPlan(request, definition, device.KeyGeneration, now.AddSeconds(10));
    }

    private static IReadOnlyDictionary<string, object?> Payload(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject().ToDictionary(x => x.Name, x => (object?)x.Value.Clone());
    }

    private sealed class CountingSecrets : IProvisioningSecretGenerator
    {
        public RandomProvisioningSecretGenerator Inner { get; } = new();
        public int FindCalls { get; private set; }

        public ProvisioningSecrets Generate(DeviceId deviceId, int generation) => Inner.Generate(deviceId, generation);

        public ProvisioningSecrets? Find(DeviceId deviceId, int generation)
        {
            FindCalls++;
            return Inner.Find(deviceId, generation);
        }

        public ProvisioningSecrets Import(DeviceId deviceId, int generation, byte[] commandKey) =>
            Inner.Import(deviceId, generation, commandKey);
    }
}
