using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hms.Server.Application.Authorization;
using Hms.Server.Application.Commands;
using Hms.Server.Application.DeviceLink;
using Hms.Server.Application.Management;
using Hms.Server.Application.Provisioning;
using Hms.Server.Domain.Authorization;
using Hms.Server.Domain.Commands;
using Hms.Server.Domain.Devices;
using Hms.Server.Domain.Organizations;
using Hms.Server.Infrastructure.Commands;
using Hms.Server.Infrastructure.Mqtt;
using Microsoft.Extensions.Options;

namespace Hms.Server.UnitTests;

/// <summary>Relay outputs move physical equipment: every layer that can stop a wrong or
/// duplicated output command is pinned here.</summary>
public sealed class PhysicalOutputSafetyTests
{
    private static readonly CommandDefinition SetOutput = new(
        "control", "set_output", "gpio.do_write", CommandAvailability.Enabled, false, true, 120_000, CommandClass.Physical);
    private static readonly CommandDefinition Pulse = new(
        "control", "pulse", "gpio.do_write", CommandAvailability.Enabled, false, true, 120_000, CommandClass.Physical);
    private static readonly CommandDefinition SetAll = new(
        "control", "set_all", "gpio.do_write", CommandAvailability.Enabled, false, true, 120_000, CommandClass.Physical);
    private static readonly IReadOnlyList<int> OnlyDo1 = [1];

    public static TheoryData<string, string> RejectedSetOutputPayloads => new()
    {
        { """{"channel":0,"value":true}""", OutputCommandPolicy.PayloadInvalid },
        { """{"channel":-1,"value":true}""", OutputCommandPolicy.PayloadInvalid },
        { """{"channel":"1","value":true}""", OutputCommandPolicy.PayloadInvalid },
        { """{"channel":1.5,"value":true}""", OutputCommandPolicy.PayloadInvalid },
        { """{"value":true}""", OutputCommandPolicy.PayloadInvalid },
        { """{"channel":1,"value":1}""", OutputCommandPolicy.PayloadInvalid },
        { """{"channel":1,"value":"on"}""", OutputCommandPolicy.PayloadInvalid },
        { """{"channel":1}""", OutputCommandPolicy.PayloadInvalid },
        { """{"channel":1,"value":true,"force":true}""", OutputCommandPolicy.PayloadInvalid },
        { """{"channel":2,"value":true}""", OutputCommandPolicy.ChannelNotAllowed }
    };

    [Theory]
    [MemberData(nameof(RejectedSetOutputPayloads))]
    public void Set_output_payloads_the_device_should_never_see_are_refused(string json, string code)
    {
        Assert.Equal(code, OutputCommandPolicy.Check(SetOutput, Payload(json), OnlyDo1, 60_000));
    }

    [Theory]
    [InlineData("""{"channel":1,"duration_ms":0}""")]
    [InlineData("""{"channel":1,"duration_ms":60001}""")]
    [InlineData("""{"channel":1,"duration_ms":2147483647}""")]
    [InlineData("""{"channel":1,"duration_ms":"1000"}""")]
    [InlineData("""{"channel":1}""")]
    public void Pulse_durations_outside_the_server_cap_are_refused(string json)
    {
        Assert.Equal(OutputCommandPolicy.PayloadInvalid, OutputCommandPolicy.Check(Pulse, Payload(json), OnlyDo1, 60_000));
    }

    [Fact]
    public void A_missing_or_empty_channel_allowlist_blocks_every_channel()
    {
        Assert.Equal(OutputCommandPolicy.ChannelNotAllowed, OutputCommandPolicy.Check(SetOutput, Payload("""{"channel":1,"value":true}"""), null, 60_000));
        Assert.Equal(OutputCommandPolicy.ChannelNotAllowed, OutputCommandPolicy.Check(SetOutput, Payload("""{"channel":1,"value":true}"""), [], 60_000));
    }

    [Fact]
    public void Valid_output_commands_pass_from_api_json_and_from_plain_values()
    {
        Assert.Null(OutputCommandPolicy.Check(SetOutput, Payload("""{"channel":1,"value":true}"""), OnlyDo1, 60_000));
        Assert.Null(OutputCommandPolicy.Check(Pulse, Payload("""{"channel":1,"duration_ms":60000}"""), OnlyDo1, 60_000));
        Assert.Null(OutputCommandPolicy.Check(SetOutput,
            new Dictionary<string, object?> { ["channel"] = 1, ["value"] = false }, OnlyDo1, 60_000));
    }

    [Fact]
    public void A_physical_action_without_a_known_payload_contract_fails_closed()
    {
        Assert.Equal(OutputCommandPolicy.PhysicalActionUnsupported,
            OutputCommandPolicy.Check(SetAll, Payload("""{"values":[true]}"""), OnlyDo1, 60_000));
    }

    [Fact]
    public void Non_physical_commands_are_not_touched_by_the_output_policy()
    {
        var read = new CommandDefinition("system", "get_info", "sys.info_view", CommandAvailability.Enabled, false, true, 120_000);
        Assert.Null(OutputCommandPolicy.Check(read, new Dictionary<string, object?>(), null, 60_000));
    }

    [Fact]
    public async Task Adapter_never_signs_a_refused_output_command()
    {
        var setup = new Setup(allowed: OnlyDo1);

        var refused = await setup.Adapter.PrepareAsync(setup.Plan(SetOutput, """{"channel":2,"value":true}"""));
        var allowed = await setup.Adapter.PrepareAsync(setup.Plan(SetOutput, """{"channel":1,"value":true}"""));

        Assert.False(refused.CanPublish);
        Assert.Equal(OutputCommandPolicy.ChannelNotAllowed, refused.FailureCode);
        Assert.True(refused.Payload.IsEmpty);
        Assert.True(allowed.CanPublish);
        using var json = JsonDocument.Parse(Body(allowed.Payload.ToArray()));
        var parameters = json.RootElement.GetProperty("data").GetProperty("data");
        Assert.Equal(JsonValueKind.Number, parameters.GetProperty("channel").ValueKind);
        Assert.Equal(JsonValueKind.True, parameters.GetProperty("value").ValueKind);
    }

    [Fact]
    public async Task Physical_commands_expire_before_the_server_stops_waiting()
    {
        var setup = new Setup(allowed: OnlyDo1);
        var handler = setup.Handler(SetOutput);

        var result = await handler.HandleAsync(setup.Request("set_output", """{"channel":1,"value":true}"""), setup.Actor("gpio.do_write"));

        Assert.Equal(CommandExecutionStatus.Recorded, result.Status);
        var (plan, prepared) = Assert.Single(setup.Transport.Records);
        Assert.True(plan.ExpiresAt - plan.Request.RequestedAt <= TimeSpan.FromMilliseconds(SendDeviceCommandHandler.MaxPhysicalCommandTtlMs));
        using var json = JsonDocument.Parse(Body(prepared.Payload.ToArray()));
        Assert.True(json.RootElement.GetProperty("ttl_ms").GetInt64() <= SendDeviceCommandHandler.MaxPhysicalCommandTtlMs);
        Assert.True(SendDeviceCommandHandler.MaxPhysicalCommandTtlMs < new MqttLinkOptions().ResponseTimeoutSeconds * 1000);
    }

    [Theory]
    [InlineData(10, 60_000, "MQTT_TIMING_INVALID")]   // reply timeout must outlast every command's TTL
    [InlineData(20, 60_000, "MQTT_TIMING_INVALID")]   // 20 s is the TTL of reads; equal is not enough
    [InlineData(0, 60_000, "MQTT_TIMING_INVALID")]
    [InlineData(30, 0, "MQTT_MAX_PULSE_INVALID")]
    [InlineData(30, 3_600_001, "MQTT_MAX_PULSE_INVALID")]
    public void Mqtt_options_reject_unsafe_timing_and_pulse_caps(int responseTimeoutSeconds, int maxPulseMs, string code)
    {
        var options = new MqttLinkOptions
        {
            Enabled = true, Host = "broker.lab", ResponseTimeoutSeconds = responseTimeoutSeconds, MaxPulseMs = maxPulseMs,
        };

        Assert.Equal(code, Assert.Throws<InvalidOperationException>(options.Validate).Message);
    }

    [Fact]
    public void Mqtt_options_accept_the_defaults()
    {
        new MqttLinkOptions { Enabled = true, Host = "broker.lab" }.Validate();
        new MqttLinkOptions { Enabled = true, Host = "broker.lab", ResponseTimeoutSeconds = 21, MaxPulseMs = 3_600_000 }.Validate();
        new MqttLinkOptions { Enabled = true, Host = "broker.lab", MaxPendingReadsPerDevice = 1 }.Validate();
        new MqttLinkOptions { Enabled = true, Host = "broker.lab", MaxPendingReadsPerDevice = 16 }.Validate();
    }

    [Fact]
    public void Rebinding_a_device_keeps_its_channel_allowlist()
    {
        var setup = new Setup(allowed: [1, 3]);

        new ConfigureDeviceMqttBindingHandler(setup.Devices, setup.Bindings)
            .Handle(setup.Device.Id, "hms_toan_lab", "gateway_870c", 12);

        Assert.Equal([1, 3], setup.Bindings.Find(setup.Device.Id)!.AllowedOutputChannels);
    }

    [Fact]
    public void Binding_another_physical_device_closes_its_outputs_and_forgets_their_state()
    {
        var setup = new Setup(allowed: [1, 3]);
        var outputs = new InMemoryDeviceOutputStateStore();
        outputs.Set(setup.Device.Id, [new(1, true, "toggle", false)], DateTimeOffset.UtcNow);

        new ConfigureDeviceMqttBindingHandler(setup.Devices, setup.Bindings, outputs)
            .Handle(setup.Device.Id, "hms_toan_lab", "gateway_other", null);

        Assert.Null(setup.Bindings.Find(setup.Device.Id)!.AllowedOutputChannels);
        Assert.Null(outputs.Get(setup.Device.Id));
    }

    [Fact]
    public void A_rebind_never_writes_back_an_allowlist_read_before_it_changed()
    {
        var setup = new Setup(allowed: [1, 3]);
        var stale = setup.Bindings.Find(setup.Device.Id)!;

        new SetDeviceOutputChannelsHandler(setup.Bindings).Handle(setup.Device.Id, [1]);
        // A rebind that had read the old row before the change completes afterwards.
        setup.Bindings.Upsert(stale with { KeyVersion = 13 });

        Assert.Equal([1], setup.Bindings.Find(setup.Device.Id)!.AllowedOutputChannels);
    }

    [Theory]
    [InlineData(256)]
    [InlineData(int.MaxValue)]
    public void Channel_numbers_are_bounded(int channel) =>
        Assert.Throws<ArgumentException>(() => DeviceMqttBinding.ValidateOutputChannels([channel]));

    [Theory]
    [InlineData(new[] { 0 })]
    [InlineData(new[] { 1, 1 })]
    [InlineData(new[] { -2 })]
    public void Channel_allowlists_with_invalid_entries_are_rejected(int[] channels)
    {
        var setup = new Setup(allowed: null);

        Assert.Throws<ArgumentException>(() =>
            new SetDeviceOutputChannelsHandler(setup.Bindings).Handle(setup.Device.Id, channels));
    }

    [Fact]
    public void Setting_the_channel_allowlist_reports_before_and_after()
    {
        var setup = new Setup(allowed: [1]);

        var change = new SetDeviceOutputChannelsHandler(setup.Bindings).Handle(setup.Device.Id, [1, 3]);

        Assert.Equal([1], change.Before);
        Assert.Equal([1, 3], change.After);
        Assert.Equal([1, 3], setup.Bindings.Find(setup.Device.Id)!.AllowedOutputChannels);
        Assert.Empty(new SetDeviceOutputChannelsHandler(setup.Bindings).Handle(setup.Device.Id, []).After!);
    }

    [Fact]
    public async Task A_publish_that_may_have_reached_the_broker_stays_pending()
    {
        var pending = new InMemoryPendingDeviceCommandRegistry();
        var setup = new Setup(allowed: OnlyDo1);
        var plan = setup.Plan(SetOutput, """{"channel":1,"value":true}""");
        var prepared = new PreparedDeviceCommand(plan.Request.DeviceId, "hms_toan_lab/gateway_870c/cmd/control", "{}"u8.ToArray(), 1, true);

        var unconfirmed = await new MqttDeviceCommandTransport(new FakePublisher(throws: true), pending, Options())
            .SendAsync(plan, prepared);
        var notSent = await new MqttDeviceCommandTransport(new FakePublisher(result: "MQTT_NOT_CONNECTED"), pending, Options())
            .SendAsync(setup.Plan(SetOutput, """{"channel":1,"value":false}"""), prepared);

        Assert.Equal("MQTT_PUBLISH_UNCONFIRMED", unconfirmed.Code);
        Assert.NotNull(pending.Find(plan.Request.DeviceId, plan.Request.CorrelationId));
        Assert.Equal("MQTT_NOT_CONNECTED", notSent.Code);
        Assert.Single(pending.TakeExpired(DateTimeOffset.UtcNow.AddHours(1)));
    }

    [Fact]
    public async Task Commands_a_previous_run_left_unfinished_become_outcome_unknown()
    {
        var audit = new InMemoryCommandAuditStore();
        var setup = new Setup(allowed: OnlyDo1);
        var published = setup.Plan(SetOutput, """{"channel":1,"value":true}""");
        var midPublish = setup.Plan(SetOutput, """{"channel":1,"value":false}""");
        var finished = setup.Plan(SetOutput, """{"channel":1,"value":true}""");
        foreach (var plan in new[] { published, midPublish, finished })
            await audit.ReserveAsync(plan, CommandRequestFingerprint.Compute(plan.Request));
        await audit.RecordAsync(published, new CommandExecutionResult(CommandExecutionStatus.Published, "MQTT_PUBLISHED", published.Request.CorrelationId));
        await audit.RecordAsync(finished, new CommandExecutionResult(CommandExecutionStatus.Executed, "DEVICE_EXECUTED", finished.Request.CorrelationId));

        Assert.Equal(2, audit.FailUnfinished("OUTCOME_UNKNOWN_AFTER_RESTART"));

        foreach (var plan in new[] { published, midPublish })
        {
            var result = audit.Find(plan.Request.DeviceId, plan.Request.CorrelationId)!;
            Assert.Equal(CommandExecutionStatus.Failed, result.Status);
            Assert.Equal("OUTCOME_UNKNOWN_AFTER_RESTART", result.Code);
        }
        Assert.Equal(CommandExecutionStatus.Executed, audit.Find(finished.Request.DeviceId, finished.Request.CorrelationId)!.Status);
    }

    [Fact]
    public async Task A_late_unconfirmed_publish_never_replaces_the_device_reply()
    {
        var audit = new InMemoryCommandAuditStore();
        var setup = new Setup(allowed: OnlyDo1);
        var plan = setup.Plan(SetOutput, """{"channel":1,"value":true}""");
        await audit.ReserveAsync(plan, CommandRequestFingerprint.Compute(plan.Request));

        await audit.RecordAsync(plan, new CommandExecutionResult(CommandExecutionStatus.Executed, "DEVICE_EXECUTED", plan.Request.CorrelationId));
        await audit.RecordAsync(plan, new CommandExecutionResult(CommandExecutionStatus.Failed, CommandTransportOutcome.PublishUnconfirmed, plan.Request.CorrelationId));

        Assert.Equal("DEVICE_EXECUTED", audit.Find(plan.Request.DeviceId, plan.Request.CorrelationId)!.Code);
    }

    [Fact]
    public async Task A_command_refused_before_its_audit_reservation_still_leaves_a_trace()
    {
        var setup = new Setup(allowed: OnlyDo1);
        var logger = new CapturingLogger();
        var handler = setup.Handler(SetOutput, logger);

        var result = await handler.HandleAsync(setup.Request("set_output", """{"channel":1,"value":true}"""), setup.Actor("gpio.do_view"));

        Assert.Equal(DeviceAccessCodes.PermissionRequired, result.Code);
        Assert.Contains(logger.Messages, x => x.Contains(DeviceAccessCodes.PermissionRequired) && x.Contains("set_output"));
    }

    [Fact]
    public void Relay_write_permission_is_high_risk()
    {
        Assert.True(DevicePermissionCodes.IsHighRisk("gpio.do_write"));
    }

    private static IReadOnlyDictionary<string, object?> Payload(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject().ToDictionary(x => x.Name, x => (object?)x.Value.Clone());
    }

    private static string Body(byte[] wire) => Encoding.UTF8.GetString(wire[..^(8 + 64 + 2)]) + "}";

    private static IOptions<MqttLinkOptions> Options() => Microsoft.Extensions.Options.Options.Create(new MqttLinkOptions());

    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger<SendDeviceCommandHandler>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }

    private sealed class FakePublisher(string? result = null, bool throws = false) : IMqttCommandPublisher
    {
        public bool IsConnected => true;

        public ValueTask<string?> PublishCommandAsync(string commandTopic, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
            throws ? throw new InvalidOperationException("broker went away mid-publish") : ValueTask.FromResult(result);
    }

    private sealed class Setup
    {
        private readonly InMemoryDeviceAccessStore _access = new();

        public Setup(IReadOnlyList<int>? allowed)
        {
            Device = Domain.Devices.Device.Create("RTU", Organization.Create("Lab").Id);
            Device.Activate();
            Devices.Add(Device);
            Bindings.Upsert(DeviceMqttBinding.Create(Device.Id, "hms_toan_lab", "gateway_870c", null) with { AllowedOutputChannels = allowed });
            Secrets.Import(Device.Id, Device.KeyGeneration, RandomNumberGenerator.GetBytes(32));
            Adapter = new HmsIdfCommandProtocolAdapter(Bindings, Secrets, new InMemoryDeviceLinkStateStore());
        }

        public Device Device { get; }
        public InMemoryDeviceStore Devices { get; } = new();
        public InMemoryDeviceMqttBindingStore Bindings { get; } = new();
        public RandomProvisioningSecretGenerator Secrets { get; } = new();
        public HmsIdfCommandProtocolAdapter Adapter { get; }
        public RecordingDeviceCommandTransport Transport { get; } = new();

        public CommandExecutionPlan Plan(CommandDefinition definition, string json)
        {
            var now = DateTimeOffset.UtcNow;
            var request = new DeviceCommandRequest(Device.Id, new UserId(Guid.NewGuid()), definition.Type, definition.Action,
                Payload(json), Guid.NewGuid().ToString("N"), now);
            return new CommandExecutionPlan(request, definition, Device.KeyGeneration, now.AddSeconds(10));
        }

        public DeviceCommandRequest Request(string action, string json) =>
            new(Device.Id, TestUser, "control", action, Payload(json), Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow,
                CatalogVersion: "test", CatalogHash: "hash");

        public SendDeviceCommandHandler Handler(CommandDefinition definition, CapturingLogger? logger = null) => new(
            new InMemoryCommandCatalog([definition], new CommandCatalogMetadata("test", "hash")),
            Adapter, Transport, new InMemoryCommandAuditStore(), id => id == Device.Id ? Device : null,
            new DeviceAccessResolver(_access, _access), logger);

        // A member whose device role holds the permissions, and no other scope.
        public AccessActor Actor(params string[] permissions)
        {
            var role = DeviceRole.Create(Device.OwnerOrganizationId, $"role-{Guid.NewGuid():N}", null, permissions);
            _access.Add(role, Audit(DeviceAccessChanges.RoleCreated));
            _access.Upsert(new DeviceMembership(TestUser, Device.Id, role.Id, TestUser, DateTimeOffset.UtcNow), null,
                Audit(DeviceAccessChanges.MembershipAssigned));
            return new AccessActor(TestUser, false, System.Collections.Frozen.FrozenSet<OrganizationId>.Empty,
                System.Collections.Frozen.FrozenSet<string>.Empty);
        }

        private static DeviceAccessAuditEntry Audit(string change) =>
            new(Guid.NewGuid(), TestUser, change, null, null, null, null, null, null, DateTimeOffset.UtcNow);

        private static readonly UserId TestUser = new(Guid.Parse("00000000-0000-0000-0000-0000000000aa"));
    }
}
