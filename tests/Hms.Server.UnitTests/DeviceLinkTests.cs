using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hms.Server.Application.Authorization;
using Hms.Server.Application.Commands;
using Hms.Server.Application.DeviceLink;
using Hms.Server.Application.Management;
using Hms.Server.Application.Provisioning;
using Hms.Server.Application.Realtime;
using Hms.Server.Domain.Authorization;
using Hms.Server.Domain.Commands;
using Hms.Server.Domain.Devices;
using Hms.Server.Domain.Organizations;
using Hms.Server.Infrastructure.Commands;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hms.Server.UnitTests;

public sealed class DeviceLinkTests
{
    // Bytes 0x00..0x1f. The expected signatures below were computed with Node's crypto module
    // straight from the algorithm in mqtt-api.md §4.2, independently of this codebase.
    private static readonly byte[] Key = Enumerable.Range(0, 32).Select(x => (byte)x).ToArray();
    private const string PingJson = "{\"type\":\"system\",\"id\":\"integration-001\",\"ts\":1790899200,\"ttl_ms\":120000,\"data\":{\"action\":\"ping\"}}";
    private const string PingSig = "a73651704b544ab748942d4970147b546fa8472c88978a265725c4894fc7b0ef";
    private const string ReplyJson = "{\"id\":\"integration-001\",\"type\":\"system\",\"action\":\"ping\",\"success\":true,\"ts\":1790899201,\"data\":{\"status\":\"alive\"}}";
    private const string ReplySig = "f8230a48115b9ce01015fe7995e4bffb8452da5a844372f5cc697b2361bdaf88";

    [Fact]
    public void Signed_command_matches_an_independent_hmac_over_the_spec_envelope()
    {
        var unsigned = HmsIdfCommandCodec.Serialize(new HmsIdfCommandCodec.CommandEnvelope(
            "system", "integration-001", 1790899200, 120000, "ping"));
        var wire = HmsIdfCommandCodec.Sign(unsigned, Key);

        Assert.Equal(PingJson, Encoding.UTF8.GetString(unsigned));
        Assert.Equal(PingJson[..^1] + ",\"sig\":\"" + PingSig + "\"}", Encoding.UTF8.GetString(wire));
    }

    [Fact]
    public void Unicode_parameters_are_signed_as_raw_utf8()
    {
        var unsigned = HmsIdfCommandCodec.Serialize(new HmsIdfCommandCodec.CommandEnvelope(
            "sms", "u-1", 1790899200, 60000, "send",
            new Dictionary<string, object?> { ["text"] = "Xin chào" }));
        var wire = Encoding.UTF8.GetString(HmsIdfCommandCodec.Sign(unsigned, Key));

        Assert.Equal(109, unsigned.Length);
        Assert.EndsWith(",\"sig\":\"e47ca3aa4bfcfc9c01b7ce16aa5c906883de5c296be4c9a5a5f092c163a3ce68\"}", wire);
        Assert.Contains("Xin chào", wire);
    }

    [Fact]
    public void Envelope_refuses_keys_the_device_would_reject()
    {
        Assert.Throws<InvalidOperationException>(() => HmsIdfCommandCodec.Serialize(new HmsIdfCommandCodec.CommandEnvelope(
            "config", "dup-1", 1790899200, 120000, "set",
            new Dictionary<string, object?> { ["Mode"] = 1, ["mode"] = 2 })));
        Assert.Throws<InvalidOperationException>(() => HmsIdfCommandCodec.Serialize(new HmsIdfCommandCodec.CommandEnvelope(
            "config", "nul-1", 1790899200, 120000, "set",
            new Dictionary<string, object?> { ["name"] = "a\0b" })));
    }

    [Fact]
    public void Reply_verifies_only_with_the_res_domain_prefix()
    {
        var reply = HmsIdfCommandCodec.VerifyResponse(Wire(ReplyJson, ReplySig), Key);

        Assert.NotNull(reply);
        Assert.Equal("integration-001", reply.Id);
        Assert.Equal("ping", reply.Action);
        Assert.True(reply.Success);
        Assert.Equal("alive", reply.Data!.Value.GetProperty("status").GetString());

        var flipped = ReplySig[..^1] + (ReplySig[^1] == '8' ? '9' : '8');
        Assert.Null(HmsIdfCommandCodec.VerifyResponse(Wire(ReplyJson, flipped), Key));
        // The command-domain HMAC (no "res:") must not verify a reply.
        Assert.Null(HmsIdfCommandCodec.VerifyResponse(Wire(ReplyJson, Hex(HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes(ReplyJson)))), Key));
        Assert.Null(HmsIdfCommandCodec.VerifyResponse(Encoding.UTF8.GetBytes(ReplyJson), Key));
    }

    [Fact]
    public void Reply_id_is_read_from_the_root_only()
    {
        const string nested = "{\"type\":\"system\",\"data\":{\"id\":\"inner\"},\"id\":\"outer\",\"success\":true}";

        Assert.Equal("outer", HmsIdfCommandCodec.PeekId(Encoding.UTF8.GetBytes(nested)));
        Assert.Null(HmsIdfCommandCodec.PeekId("not json"u8));
    }

    [Fact]
    public async Task Adapter_signs_the_spec_envelope_on_the_type_topic()
    {
        var link = new Link();
        var plan = link.Plan("system", "get_info");

        var prepared = await link.Adapter.PrepareAsync(plan);

        Assert.True(prepared.CanPublish);
        Assert.Equal("hms_toan_lab/gateway_870c/cmd/system", prepared.Topic);
        var wire = prepared.Payload.ToArray();
        Assert.True(wire.Length <= HmsIdfCommandCodec.MaxCommandBytes);
        var body = wire[..^(8 + 64 + 2)].Append((byte)'}').ToArray();
        var sig = Encoding.ASCII.GetString(wire[^(64 + 2)..^2]);
        Assert.Equal(Hex(HMACSHA256.HashData(Link.CommandKey, body)), sig);

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal(["type", "id", "ts", "ttl_ms", "key_ver", "data"], root.EnumerateObject().Select(x => x.Name));
        Assert.Equal(plan.Request.CorrelationId, root.GetProperty("id").GetString());
        Assert.Equal(plan.Request.RequestedAt.ToUnixTimeSeconds(), root.GetProperty("ts").GetInt64());
        Assert.Equal(120000, root.GetProperty("ttl_ms").GetInt64());
        Assert.Equal(11, root.GetProperty("key_ver").GetInt32());
        Assert.Equal("get_info", root.GetProperty("data").GetProperty("action").GetString());
        Assert.False(root.GetProperty("data").TryGetProperty("data", out _));
    }

    [Fact]
    public async Task Confirm_token_is_built_from_the_mqtt_device_id()
    {
        var link = new Link();
        var definition = new CommandDefinition("system", "reboot", "sys.reboot", CommandAvailability.Enabled,
            true, false, 60000, CommandClass.Flash, ConfirmTemplate: "REBOOT-<DEVID>", DryRunSupported: true);

        var withGuid = await link.Adapter.PrepareAsync(link.Plan(definition, confirm: $"REBOOT-{link.Device.Id.Value}"));
        var withMqttId = await link.Adapter.PrepareAsync(link.Plan(definition, confirm: "REBOOT-gateway_870c"));

        Assert.Equal("CONFIRM_MISMATCH", withGuid.FailureCode);
        // The right token passes the confirm check and the empty-payload rule, and is signed in.
        Assert.True(withMqttId.CanPublish);
        Assert.Contains("\"confirm\":\"REBOOT-gateway_870c\"", Encoding.UTF8.GetString(withMqttId.Payload.Span));
    }

    [Fact]
    public async Task A_reboot_dry_run_is_signed_with_dry_run_and_confirm_at_the_root()
    {
        var link = new Link();
        var plan = link.Plan(RebootDefinition, confirm: "REBOOT-gateway_870c");
        plan = plan with { Request = plan.Request with { DryRun = true } };

        var prepared = await link.Adapter.PrepareAsync(plan);

        Assert.True(prepared.CanPublish);
        var wire = Encoding.UTF8.GetString(prepared.Payload.Span);
        Assert.Contains("\"dry_run\":true", wire);
        Assert.Contains("\"confirm\":\"REBOOT-gateway_870c\"", wire);
        Assert.Contains("\"data\":{\"action\":\"reboot\"}", wire);
    }

    [Fact]
    public async Task A_reboot_with_a_payload_is_refused_without_reading_the_key()
    {
        var link = new Link(importKey: false);
        var plan = link.Plan(RebootDefinition, confirm: "REBOOT-gateway_870c",
            payload: new Dictionary<string, object?> { ["delay_ms"] = 2000 });

        var prepared = await link.Adapter.PrepareAsync(plan);

        Assert.False(prepared.CanPublish);
        // Not COMMAND_KEY_NOT_PROVISIONED: the payload rule runs before the key is looked up.
        Assert.Equal(ConfigCommandPolicy.PayloadInvalid, prepared.FailureCode);
    }

    private static readonly CommandDefinition RebootDefinition = new("system", "reboot", "sys.reboot",
        CommandAvailability.Enabled, true, false, 60000, CommandClass.Flash, ConfirmTemplate: "REBOOT-<DEVID>", DryRunSupported: true);

    [Fact]
    public async Task Adapter_refuses_what_the_device_would_drop()
    {
        var link = new Link();
        var unkeyed = new Link(importKey: false);

        Assert.Equal("COMMAND_KEY_NOT_PROVISIONED", (await unkeyed.Adapter.PrepareAsync(unkeyed.Plan("system", "ping"))).FailureCode);
        var oversized = link.Plan("system", "ping", new Dictionary<string, object?> { ["pad"] = new string('x', 1100) });
        Assert.Equal("COMMAND_TOO_LARGE", (await link.Adapter.PrepareAsync(oversized)).FailureCode);
        var dryRun = link.Plan("system", "ping") with { Request = link.Plan("system", "ping").Request with { DryRun = true } };
        Assert.Equal("DRY_RUN_UNSUPPORTED", (await link.Adapter.PrepareAsync(dryRun)).FailureCode);
    }

    [Fact]
    public async Task Adapter_follows_the_retained_capabilities_once_received()
    {
        var link = new Link();
        Assert.True((await link.Adapter.PrepareAsync(link.Plan("system", "get_info"))).CanPublish);

        link.LinkState.RecordCapabilities(link.Device.Id, DeviceCapabilities.TryParse(
            """{"version":"1","actions":[{"type":"system","action":"get_info","available":false}]}"""u8)!, DateTimeOffset.UtcNow);

        Assert.Equal("DEVICE_ACTION_UNAVAILABLE", (await link.Adapter.PrepareAsync(link.Plan("system", "get_info"))).FailureCode);
        Assert.Equal("DEVICE_ACTION_UNAVAILABLE", (await link.Adapter.PrepareAsync(link.Plan("system", "get_heap"))).FailureCode);
    }

    [Fact]
    public async Task Signed_reply_closes_the_pending_command_as_executed()
    {
        var link = new Link();
        var plan = await link.PublishedAsync("system", "get_info");

        var outcome = await link.Correlator.HandleAsync(link.Device.Id,
            link.SignedReply(plan, """{"success":true,"data":{"chip":"esp32s3"}}"""), DateTimeOffset.UtcNow);

        Assert.Equal(DeviceResponseOutcome.Completed, outcome);
        Assert.Equal(CommandExecutionStatus.Executed, link.Audit.Find(link.Device.Id, plan.Request.CorrelationId)!.Status);
        Assert.Null(link.Pending.Find(link.Device.Id, plan.Request.CorrelationId));
        Assert.Contains("esp32s3", link.Responses.Find(link.Device.Id, plan.Request.CorrelationId)!.DataJson);
        // A read's result is polled by its sender; it is not broadcast to every viewer.
        Assert.DoesNotContain(link.Events.Snapshot(), x => x.EventType == DeviceCommandResponseCorrelator.CommandResultEvent);
    }

    [Fact]
    public async Task Replies_that_are_not_ours_or_not_authentic_change_nothing()
    {
        var link = new Link();
        var plan = await link.PublishedAsync("system", "get_info");
        var other = link.Plan("system", "get_info");

        Assert.Equal(DeviceResponseOutcome.Ignored, await link.Correlator.HandleAsync(
            link.Device.Id, link.SignedReply(other, """{"success":true}"""), DateTimeOffset.UtcNow));
        Assert.Equal(DeviceResponseOutcome.Rejected, await link.Correlator.HandleAsync(
            link.Device.Id, link.SignedReply(plan, """{"success":true}""", new byte[32]), DateTimeOffset.UtcNow));
        Assert.Equal(DeviceResponseOutcome.Rejected, await link.Correlator.HandleAsync(
            link.Device.Id, link.SignedReply(plan, """{"success":true}""", type: "network"), DateTimeOffset.UtcNow));

        Assert.NotNull(link.Pending.Find(link.Device.Id, plan.Request.CorrelationId));
        Assert.Equal(CommandExecutionStatus.Published, link.Audit.Find(link.Device.Id, plan.Request.CorrelationId)!.Status);
    }

    [Theory]
    [InlineData("""{"success":false,"error":"ESP_ERR_TIMEOUT","data":{"accepted":true,"pending":true,"operation_id":"42"}}""", CommandExecutionStatus.Acknowledged, "DEVICE_ACCEPTED", "operation_id=42")]
    [InlineData("""{"success":false,"error":"ESP_ERR_INVALID_ARG","data":{"error":"channel out of range"}}""", CommandExecutionStatus.Failed, "DEVICE_REJECTED", "ESP_ERR_INVALID_ARG: channel out of range")]
    [InlineData("""{"success":false,"error":"confirm_required"}""", CommandExecutionStatus.Failed, "DEVICE_REJECTED", "confirm_required")]
    public async Task Reply_outcomes_follow_the_spec_error_levels(string fields, CommandExecutionStatus status, string code, string detail)
    {
        var link = new Link();
        var plan = await link.PublishedAsync("system", "get_info");

        await link.Correlator.HandleAsync(link.Device.Id, link.SignedReply(plan, fields), DateTimeOffset.UtcNow);

        var recorded = link.Audit.Find(link.Device.Id, plan.Request.CorrelationId)!;
        Assert.Equal(status, recorded.Status);
        Assert.Equal(code, recorded.Code);
        Assert.Equal(detail, recorded.Detail);
    }

    [Fact]
    public async Task A_confirm_token_is_signed_into_the_command_the_device_receives()
    {
        // A config write with a confirm gate (none of the opened ones has one yet, so this row is
        // made up): the token built from the MQTT device id must be in the signed bytes.
        var link = new Link();
        var definition = new CommandDefinition("system", "set_hostname", "sys.hostname_edit", CommandAvailability.Enabled,
            false, true, 60000, CommandClass.Write, ConfirmTemplate: "HOST-<DEVID>");
        var plan = link.Plan(definition, confirm: "HOST-gateway_870c",
            payload: new Dictionary<string, object?> { ["hostname"] = "gw-t" });

        var prepared = await link.Adapter.PrepareAsync(plan);

        Assert.True(prepared.CanPublish);
        Assert.Contains("\"confirm\":\"HOST-gateway_870c\"", Encoding.UTF8.GetString(prepared.Payload.Span));
    }

    [Fact]
    public async Task A_config_write_the_device_is_still_running_keeps_the_write_lane_closed()
    {
        var link = new Link();
        var setChannel = new CommandDefinition("adc", "set_channel", "adc.config_edit", CommandAvailability.Enabled, false, true, 120_000, CommandClass.Write);
        var plan = await link.PublishedAsync(setChannel);
        var receivedAt = DateTimeOffset.UtcNow;

        await link.Correlator.HandleAsync(link.Device.Id, link.SignedReply(plan,
            """{"success":false,"error":"ESP_ERR_TIMEOUT","data":{"accepted":true,"pending":true,"operation_id":"7"}}"""), receivedAt);

        Assert.Equal(CommandExecutionStatus.Acknowledged, link.Audit.Find(link.Device.Id, plan.Request.CorrelationId)!.Status);
        var next = link.Plan(setChannel);
        Assert.Equal(DeviceLaneCodes.WriteBusy,
            link.Pending.TryRegisterWrite(new PendingDeviceCommand(next, receivedAt.AddSeconds(59), receivedAt.AddSeconds(89)), 10));
        Assert.Null(link.Pending.TryRegisterWrite(new PendingDeviceCommand(next, receivedAt.AddSeconds(61), receivedAt.AddSeconds(91)), 10));
    }

    [Fact]
    public async Task A_config_write_the_device_finished_frees_the_write_lane_at_once()
    {
        var link = new Link();
        var setHostname = new CommandDefinition("system", "set_hostname", "sys.hostname_edit", CommandAvailability.Enabled, false, true, 120_000, CommandClass.Write);
        var plan = await link.PublishedAsync(setHostname);
        var receivedAt = DateTimeOffset.UtcNow;

        await link.Correlator.HandleAsync(link.Device.Id, link.SignedReply(plan, """{"success":true,"data":{"hostname":"gw-t"}}"""), receivedAt);

        var next = link.Plan(setHostname);
        Assert.Null(link.Pending.TryRegisterWrite(new PendingDeviceCommand(next, receivedAt.AddMilliseconds(1), receivedAt.AddSeconds(30)), 10));
    }

    [Fact]
    public async Task No_reply_before_the_deadline_is_recorded_as_outcome_unknown()
    {
        var link = new Link();
        var plan = await link.PublishedAsync("system", "get_info");

        Assert.Equal(0, await link.Correlator.SweepExpiredAsync(DateTimeOffset.UtcNow));
        Assert.Equal(1, await link.Correlator.SweepExpiredAsync(DateTimeOffset.UtcNow.AddMinutes(5)));

        var result = link.Audit.Find(link.Device.Id, plan.Request.CorrelationId)!;
        Assert.Equal(CommandExecutionStatus.Failed, result.Status);
        Assert.Equal("DEVICE_RESPONSE_TIMEOUT", result.Code);
        Assert.Null(link.Pending.Find(link.Device.Id, plan.Request.CorrelationId));
    }

    [Fact]
    public async Task A_late_published_status_never_overwrites_the_device_outcome()
    {
        var link = new Link();
        var plan = link.Plan("system", "get_info");
        await link.Audit.ReserveAsync(plan, CommandRequestFingerprint.Compute(plan.Request));

        await link.Audit.RecordAsync(plan, new CommandExecutionResult(CommandExecutionStatus.Executed, "DEVICE_EXECUTED", plan.Request.CorrelationId));
        await link.Audit.RecordAsync(plan, new CommandExecutionResult(CommandExecutionStatus.Published, "MQTT_PUBLISHED", plan.Request.CorrelationId));

        Assert.Equal(CommandExecutionStatus.Executed, link.Audit.Find(link.Device.Id, plan.Request.CorrelationId)!.Status);
    }

    [Fact]
    public async Task Heartbeat_updates_telemetry_link_state_and_realtime()
    {
        var link = new Link();

        await link.Inbound.HandleAsync(link.Device.Id, DeviceInboundKind.Status,
            """{"uptime_seconds":12109,"free_heap":1306047,"network":{"active_uplink":"4G/Modem"}}"""u8.ToArray(), DateTimeOffset.UtcNow);
        await link.Inbound.HandleAsync(link.Device.Id, DeviceInboundKind.Connectivity,
            """{"state":"online"}"""u8.ToArray(), DateTimeOffset.UtcNow);

        var telemetry = link.Telemetry.GetLatest(link.Device.Id)!;
        Assert.Equal(12109, telemetry.UptimeSeconds);
        Assert.Equal("4G/Modem", telemetry.ActiveUplink);
        var snapshot = link.LinkState.Get(link.Device.Id)!;
        Assert.Equal("online", snapshot.Connectivity);
        Assert.NotNull(snapshot.LastHeartbeatAt);
        Assert.Contains(link.Events.Snapshot(), x => x.EventType == "status");
        Assert.Contains(link.Events.Snapshot(), x => x.EventType == "connectivity");
    }

    [Fact]
    public async Task Messages_for_a_revoked_device_are_dropped()
    {
        var link = new Link();
        link.Device.Revoke();

        await link.Inbound.HandleAsync(link.Device.Id, DeviceInboundKind.Status, """{"uptime_seconds":1}"""u8.ToArray(), DateTimeOffset.UtcNow);

        Assert.Null(link.Telemetry.GetLatest(link.Device.Id));
        Assert.Empty(link.Events.Snapshot());
    }

    [Fact]
    public void Importing_a_device_key_moves_the_device_to_a_new_key_generation()
    {
        var link = new Link(importKey: false);
        var key = RandomNumberGenerator.GetBytes(32);

        var generation = new ImportDeviceCommandKeyHandler(link.Devices, link.Secrets).Handle(link.Device.Id, Convert.ToHexString(key));

        Assert.Equal(2, generation);
        Assert.Equal(2, link.Device.KeyGeneration);
        Assert.Equal(key, link.Secrets.Find(link.Device.Id, 2)!.CommandKey);
        Assert.Throws<ArgumentException>(() => new ImportDeviceCommandKeyHandler(link.Devices, link.Secrets).Handle(link.Device.Id, "abc"));
    }

    [Theory]
    [InlineData("hms/+", "dev")]
    [InlineData("hms/#", "dev")]
    [InlineData("hms//lab", "dev")]
    [InlineData("/hms", "dev")]
    [InlineData("hms", "dev/1")]
    [InlineData("hms", "")]
    public void Binding_rejects_topics_that_would_widen_or_shift_the_match(string prefix, string deviceId)
    {
        Assert.Throws<ArgumentException>(() => DeviceMqttBinding.Create(DeviceId.New(), prefix, deviceId, null));
    }

    private static byte[] Wire(string json, string sigHex) => Encoding.UTF8.GetBytes(json[..^1] + ",\"sig\":\"" + sigHex + "\"}");

    private static string Hex(byte[] bytes) => Convert.ToHexStringLower(bytes);

    private sealed class Link
    {
        public static readonly byte[] CommandKey = RandomNumberGenerator.GetBytes(32);

        public Link(bool importKey = true)
        {
            Device = Domain.Devices.Device.Create("RTU", Organization.Create("Lab").Id);
            Device.Activate();
            Devices.Add(Device);
            Bindings.Upsert(DeviceMqttBinding.Create(Device.Id, "hms_toan_lab", "gateway_870c", 11));
            if (importKey)
                Secrets.Import(Device.Id, Device.KeyGeneration, CommandKey);
            Adapter = new HmsIdfCommandProtocolAdapter(Bindings, Secrets, LinkState);
            Correlator = new DeviceCommandResponseCorrelator(Pending, new HmsIdfResponseCodec(), Secrets, Audit,
                Responses, Events, Epochs, Permissions, Tracker, NullLogger<DeviceCommandResponseCorrelator>.Instance);
            Inbound = new DeviceInboundMessageHandler(Devices, Telemetry, LinkState, Events, Epochs, Permissions,
                Correlator, Tracker, NullLogger<DeviceInboundMessageHandler>.Instance);
        }

        public Device Device { get; }
        public InMemoryDeviceStore Devices { get; } = new();
        public InMemoryDeviceMqttBindingStore Bindings { get; } = new();
        public RandomProvisioningSecretGenerator Secrets { get; } = new();
        public InMemoryDeviceLinkStateStore LinkState { get; } = new();
        public InMemoryPendingDeviceCommandRegistry Pending { get; } = new();
        public InMemoryCommandAuditStore Audit { get; } = new();
        public InMemoryCommandResponseStore Responses { get; } = new();
        public BoundedDeviceEventPublisher Events { get; } = new();
        public InMemoryAuthorizationEpochStore Epochs { get; } = new();
        public InMemoryPermissionStore Permissions { get; } = new();
        public InMemoryDeviceTelemetryStore Telemetry { get; } = new();
        public InMemoryDeviceRebootTracker Tracker { get; } = new();
        public HmsIdfCommandProtocolAdapter Adapter { get; }
        public DeviceCommandResponseCorrelator Correlator { get; }
        public DeviceInboundMessageHandler Inbound { get; }

        public CommandExecutionPlan Plan(string type, string action, IReadOnlyDictionary<string, object?>? payload = null) =>
            Plan(new CommandDefinition(type, action, "sys.info_view", CommandAvailability.Enabled, false, true, 120_000), payload: payload);

        public CommandExecutionPlan Plan(
            CommandDefinition definition,
            string? confirm = null,
            IReadOnlyDictionary<string, object?>? payload = null)
        {
            var now = DateTimeOffset.UtcNow;
            var request = new DeviceCommandRequest(Device.Id, new UserId(Guid.NewGuid()), definition.Type, definition.Action,
                payload ?? new Dictionary<string, object?>(), Guid.NewGuid().ToString(), now, Confirm: confirm);
            return new CommandExecutionPlan(request, definition, Device.KeyGeneration, now.AddMilliseconds(definition.DefaultTtlMs!.Value));
        }

        /// <summary>A plan the transport has published: reserved, recorded as published, pending.</summary>
        public Task<CommandExecutionPlan> PublishedAsync(string type, string action) =>
            PublishedAsync(new CommandDefinition(type, action, "sys.info_view", CommandAvailability.Enabled, false, true, 120_000));

        public async Task<CommandExecutionPlan> PublishedAsync(CommandDefinition definition)
        {
            var plan = Plan(definition);
            await Audit.ReserveAsync(plan, CommandRequestFingerprint.Compute(plan.Request));
            await Audit.RecordAsync(plan, new CommandExecutionResult(CommandExecutionStatus.Published, "MQTT_PUBLISHED", plan.Request.CorrelationId));
            Pending.Register(new PendingDeviceCommand(plan, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddSeconds(30)));
            return plan;
        }

        /// <summary>A device reply signed the way spec §4.1 describes, built without the codec.</summary>
        public byte[] SignedReply(CommandExecutionPlan plan, string fields, byte[]? key = null, string? type = null)
        {
            var head = $"{{\"id\":\"{plan.Request.CorrelationId}\",\"type\":\"{type ?? plan.Request.Type}\",\"action\":\"{plan.Request.Action}\",\"ts\":{DateTimeOffset.UtcNow.ToUnixTimeSeconds()},";
            var body = head + fields[1..];
            var mac = HMACSHA256.HashData(key ?? CommandKey, Encoding.UTF8.GetBytes("res:" + body));
            return Wire(body, Hex(mac));
        }
    }
}
