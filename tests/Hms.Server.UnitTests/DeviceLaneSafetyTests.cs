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

namespace Hms.Server.UnitTests;

/// <summary>A device has one small command queue and one rate bucket for every client (spec §2/§3).
/// Monitoring reads must not crowd out relay commands or outlive the server's reply timeout.</summary>
public sealed class DeviceLaneSafetyTests
{
    private static readonly CommandDefinition GetInfo = new(
        "system", "get_info", "sys.info_view", CommandAvailability.Enabled, false, true, 120_000, CommandClass.Read);
    private static readonly CommandDefinition SetOutput = new(
        "control", "set_output", "gpio.do_write", CommandAvailability.Enabled, false, true, 120_000, CommandClass.Physical);
    private static readonly CommandDefinition GetStatus = new(
        "control", "get_status", "gpio.do_view", CommandAvailability.Enabled, false, true, 120_000, CommandClass.Read);
    private static readonly CommandDefinition SetHostname = new(
        "system", "set_hostname", "sys.hostname_edit", CommandAvailability.Enabled, false, true, 120_000, CommandClass.Write);
    private static readonly CommandDefinition SyncNtp = new(
        "system", "sync_ntp", "sys.time_edit", CommandAvailability.Enabled, false, true, 120_000, CommandClass.Control);

    [Fact]
    public async Task A_device_has_at_most_one_config_write_waiting()
    {
        var pending = new InMemoryPendingDeviceCommandRegistry();
        var publisher = new CountingPublisher();
        var transport = new MqttDeviceCommandTransport(publisher, pending, Options(maxReads: 4), new InMemoryDeviceRebootTracker());
        var deviceA = DeviceId.New();
        var first = Plan(deviceA, SetHostname);

        Assert.Equal(CommandExecutionStatus.Published, (await transport.SendAsync(first, Prepared(first))).Status);
        var second = Plan(deviceA, SyncNtp);
        var refused = await transport.SendAsync(second, Prepared(second));

        Assert.Equal(CommandExecutionStatus.ContractUnavailable, refused.Status);
        Assert.Equal(MqttDeviceCommandTransport.WriteBusy, refused.Code);
        Assert.Equal(1, publisher.Count);
        Assert.Null(pending.Find(deviceA, second.Request.CorrelationId));

        // Another device, a read and a relay command on the same device are not held back by the write.
        var other = Plan(DeviceId.New(), SetHostname);
        Assert.Equal(CommandExecutionStatus.Published, (await transport.SendAsync(other, Prepared(other))).Status);
        var read = Plan(deviceA, GetInfo);
        Assert.Equal(CommandExecutionStatus.Published, (await transport.SendAsync(read, Prepared(read))).Status);
        var relay = Plan(deviceA, SetOutput);
        Assert.Equal(CommandExecutionStatus.Published, (await transport.SendAsync(relay, Prepared(relay))).Status);

        // The device answered: the next write may go at once.
        pending.Remove(deviceA, first.Request.CorrelationId);
        var next = Plan(deviceA, SyncNtp);
        Assert.Equal(CommandExecutionStatus.Published, (await transport.SendAsync(next, Prepared(next))).Status);
    }

    [Fact]
    public async Task Waiting_reads_and_relay_commands_do_not_block_a_config_write()
    {
        var pending = new InMemoryPendingDeviceCommandRegistry();
        var transport = new MqttDeviceCommandTransport(new CountingPublisher(), pending, Options(maxReads: 4), new InMemoryDeviceRebootTracker());
        var device = DeviceId.New();
        foreach (var definition in new[] { GetInfo, GetInfo, SetOutput })
        {
            var plan = Plan(device, definition);
            await transport.SendAsync(plan, Prepared(plan));
        }

        var write = Plan(device, SetHostname);

        Assert.Equal(CommandExecutionStatus.Published, (await transport.SendAsync(write, Prepared(write))).Status);
    }

    [Fact]
    public void A_write_swept_without_a_reply_holds_the_lane_until_it_has_expired_on_the_device()
    {
        var pending = new InMemoryPendingDeviceCommandRegistry();
        var device = DeviceId.New();
        var now = DateTimeOffset.UtcNow;
        // No reply by the server deadline, but the device may still dequeue it until its expiry (spec R4).
        var silent = Plan(device, SetHostname, expiresAt: now.AddSeconds(20));
        Assert.Null(pending.TryRegisterWrite(new PendingDeviceCommand(silent, now.AddSeconds(-1), now), 10));

        Assert.Single(pending.TakeExpired(now.AddSeconds(1)));

        var tooSoon = new PendingDeviceCommand(Plan(device, SyncNtp), now.AddSeconds(21), now.AddSeconds(51));
        Assert.Equal(MqttDeviceCommandTransport.WriteBusy, pending.TryRegisterWrite(tooSoon, 10));
        var afterExpiry = new PendingDeviceCommand(Plan(device, SyncNtp), now.AddSeconds(23), now.AddSeconds(53));
        Assert.Null(pending.TryRegisterWrite(afterExpiry, 10));
    }

    [Fact]
    public void A_job_the_device_is_still_running_holds_the_write_lane()
    {
        var pending = new InMemoryPendingDeviceCommandRegistry();
        var device = DeviceId.New();
        var now = DateTimeOffset.UtcNow;

        pending.HoldWriteLane(device, now.AddSeconds(60));

        Assert.Equal(MqttDeviceCommandTransport.WriteBusy,
            pending.TryRegisterWrite(new PendingDeviceCommand(Plan(device, SyncNtp), now.AddSeconds(59), now.AddSeconds(89)), 10));
        Assert.Null(pending.TryRegisterWrite(new PendingDeviceCommand(Plan(device, SyncNtp), now.AddSeconds(61), now.AddSeconds(91)), 10));
    }

    [Fact]
    public void A_device_takes_at_most_ten_config_writes_a_minute()
    {
        var pending = new InMemoryPendingDeviceCommandRegistry();
        var device = DeviceId.New();
        var start = DateTimeOffset.UtcNow;
        for (var i = 0; i < 10; i++)
        {
            var write = Plan(device, SyncNtp);
            Assert.Null(pending.TryRegisterWrite(new PendingDeviceCommand(write, start.AddSeconds(i), start.AddSeconds(i + 30)), 10));
            pending.Remove(device, write.Request.CorrelationId);
        }

        var eleventh = Plan(device, SyncNtp);
        Assert.Equal(MqttDeviceCommandTransport.WriteRateLimited,
            pending.TryRegisterWrite(new PendingDeviceCommand(eleventh, start.AddSeconds(30), start.AddSeconds(60)), 10));
        Assert.Null(pending.Find(device, eleventh.Request.CorrelationId));

        // The first write left the one-minute window.
        Assert.Null(pending.TryRegisterWrite(new PendingDeviceCommand(eleventh, start.AddSeconds(60.5), start.AddSeconds(90)), 10));
    }

    [Fact]
    public async Task A_write_that_certainly_never_left_the_server_frees_the_lane()
    {
        var pending = new InMemoryPendingDeviceCommandRegistry();
        var device = DeviceId.New();
        var notSent = Plan(device, SetHostname);

        var result = await new MqttDeviceCommandTransport(new CountingPublisher("MQTT_NOT_CONNECTED"), pending, Options(maxReads: 4), new InMemoryDeviceRebootTracker())
            .SendAsync(notSent, Prepared(notSent));

        Assert.Equal("MQTT_NOT_CONNECTED", result.Code);
        var next = Plan(device, SetHostname);
        Assert.Equal(CommandExecutionStatus.Published,
            (await new MqttDeviceCommandTransport(new CountingPublisher(), pending, Options(maxReads: 4), new InMemoryDeviceRebootTracker()).SendAsync(next, Prepared(next))).Status);
    }

    [Fact]
    public async Task A_burst_of_config_writes_publishes_exactly_one()
    {
        var pending = new InMemoryPendingDeviceCommandRegistry();
        var publisher = new CountingPublisher();
        var transport = new MqttDeviceCommandTransport(publisher, pending, Options(maxReads: 4), new InMemoryDeviceRebootTracker());
        var device = DeviceId.New();

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
        {
            var plan = Plan(device, SetHostname);
            return await transport.SendAsync(plan, Prepared(plan));
        })));

        Assert.Single(results, x => x.Status == CommandExecutionStatus.Published);
        Assert.Equal(9, results.Count(x => x.Code == MqttDeviceCommandTransport.WriteBusy));
        Assert.Equal(1, publisher.Count);
    }

    [Theory]
    [InlineData("system", "set_hostname", """{"hostname":"x\uD800"}""")]
    [InlineData("system", "get_info", """{"note":"\uDC00"}""")]
    public async Task A_payload_string_that_is_not_valid_utf16_is_refused_not_thrown_on(string type, string action, string json)
    {
        var setup = new Setup();
        var definition = type == SetHostname.Type && action == SetHostname.Action ? SetHostname : GetInfo;
        using var document = JsonDocument.Parse(json);
        var payload = document.RootElement.EnumerateObject().ToDictionary(x => x.Name, x => (object?)x.Value.Clone());

        var result = await setup.Handler(definition).HandleAsync(
            setup.Request(definition, payload, DateTimeOffset.UtcNow), setup.Actor("sys.hostname_edit", "sys.info_view"));

        Assert.Equal(CommandExecutionStatus.ContractUnavailable, result.Status);
        Assert.Equal("COMMAND_PAYLOAD_INVALID", result.Code);
        Assert.Empty(setup.Transport.Records);
    }

    [Fact]
    public async Task A_write_refused_by_the_busy_lane_is_recorded_in_the_audit_without_a_publish()
    {
        var setup = new Setup();
        var pending = new InMemoryPendingDeviceCommandRegistry();
        var publisher = new CountingPublisher();
        var audit = new InMemoryCommandAuditStore();
        var handler = setup.Handler(SyncNtp, new MqttDeviceCommandTransport(publisher, pending, Options(maxReads: 4), new InMemoryDeviceRebootTracker()), audit);
        var actor = setup.Actor("sys.time_edit");

        var first = await handler.HandleAsync(setup.Request(SyncNtp), actor);
        var request = setup.Request(SyncNtp);
        var refused = await handler.HandleAsync(request, actor);

        Assert.Equal(CommandExecutionStatus.Published, first.Status);
        Assert.Equal(MqttDeviceCommandTransport.WriteBusy, refused.Code);
        Assert.Equal(MqttDeviceCommandTransport.WriteBusy, audit.Find(setup.Device.Id, request.CorrelationId)!.Code);
        Assert.Equal(1, publisher.Count);
    }

    [Fact]
    public async Task Config_writes_are_signed_with_server_time_whatever_the_client_sent()
    {
        var setup = new Setup();
        var actor = setup.Actor("sys.hostname_edit", "sys.info_view");
        var clientTime = DateTimeOffset.UtcNow.AddSeconds(50);

        await setup.Handler(SetHostname).HandleAsync(
            setup.Request(SetHostname, new Dictionary<string, object?> { ["hostname"] = "gw-t" }, clientTime), actor);
        await setup.Handler(GetInfo).HandleAsync(setup.Request(GetInfo, null, clientTime), actor);

        var write = setup.Transport.Records[0].Plan;
        Assert.InRange(write.Request.RequestedAt, DateTimeOffset.UtcNow.AddSeconds(-5), DateTimeOffset.UtcNow);
        Assert.True(write.ExpiresAt <= DateTimeOffset.UtcNow.AddMilliseconds(SendDeviceCommandHandler.MaxCommandTtlMs));
        // Reads keep the time the client asked for (unchanged behaviour).
        Assert.Equal(clientTime, setup.Transport.Records[1].Plan.Request.RequestedAt);
    }

    [Fact]
    public async Task A_device_takes_at_most_the_configured_number_of_pending_reads()
    {
        var pending = new InMemoryPendingDeviceCommandRegistry();
        var publisher = new CountingPublisher();
        var transport = new MqttDeviceCommandTransport(publisher, pending, Options(maxReads: 4), new InMemoryDeviceRebootTracker());
        var deviceA = DeviceId.New();
        var deviceB = DeviceId.New();
        var first = Plan(deviceA, GetInfo);

        Assert.Equal(CommandExecutionStatus.Published, (await transport.SendAsync(first, Prepared(first))).Status);
        for (var i = 0; i < 3; i++)
        {
            var read = Plan(deviceA, GetInfo);
            Assert.Equal(CommandExecutionStatus.Published, (await transport.SendAsync(read, Prepared(read))).Status);
        }

        var fifth = Plan(deviceA, GetInfo);
        var refused = await transport.SendAsync(fifth, Prepared(fifth));

        Assert.Equal(CommandExecutionStatus.ContractUnavailable, refused.Status);
        Assert.Equal(MqttDeviceCommandTransport.ReadBusy, refused.Code);
        Assert.Equal(4, publisher.Count);
        Assert.Null(pending.Find(deviceA, fifth.Request.CorrelationId));

        // Another device, and a relay command on the same device, are not held back by reads.
        var other = Plan(deviceB, GetInfo);
        Assert.Equal(CommandExecutionStatus.Published, (await transport.SendAsync(other, Prepared(other))).Status);
        var relay = Plan(deviceA, SetOutput);
        Assert.Equal(CommandExecutionStatus.Published, (await transport.SendAsync(relay, Prepared(relay))).Status);

        // A read that finished (reply or timeout) frees its slot.
        pending.Remove(deviceA, first.Request.CorrelationId);
        var again = Plan(deviceA, GetInfo);
        Assert.Equal(CommandExecutionStatus.Published, (await transport.SendAsync(again, Prepared(again))).Status);
    }

    [Fact]
    public async Task One_user_gets_half_of_the_read_slots()
    {
        var pending = new InMemoryPendingDeviceCommandRegistry();
        var transport = new MqttDeviceCommandTransport(new CountingPublisher(), pending, Options(maxReads: 4), new InMemoryDeviceRebootTracker());
        var device = DeviceId.New();
        var user = new UserId(Guid.NewGuid());

        var results = new List<CommandExecutionResult>();
        for (var i = 0; i < 3; i++)
        {
            var read = Plan(device, GetInfo, user);
            results.Add(await transport.SendAsync(read, Prepared(read)));
        }

        Assert.Equal([CommandExecutionStatus.Published, CommandExecutionStatus.Published, CommandExecutionStatus.ContractUnavailable],
            results.Select(x => x.Status));
        var other = Plan(device, GetInfo);
        Assert.Equal(CommandExecutionStatus.Published, (await transport.SendAsync(other, Prepared(other))).Status);
    }

    [Fact]
    public async Task A_relay_read_back_still_fits_when_reads_fill_the_cap()
    {
        var pending = new InMemoryPendingDeviceCommandRegistry();
        var transport = new MqttDeviceCommandTransport(new CountingPublisher(), pending, Options(maxReads: 4), new InMemoryDeviceRebootTracker());
        var device = DeviceId.New();
        for (var i = 0; i < 4; i++)
        {
            var read = Plan(device, GetInfo);
            await transport.SendAsync(read, Prepared(read));
        }

        var readBack = Plan(device, GetStatus);
        var second = Plan(device, GetStatus);

        Assert.Equal(CommandExecutionStatus.Published, (await transport.SendAsync(readBack, Prepared(readBack))).Status);
        Assert.Equal(MqttDeviceCommandTransport.ReadBusy, (await transport.SendAsync(second, Prepared(second))).Code);
    }

    [Fact]
    public async Task A_read_past_its_expiry_no_longer_holds_a_slot()
    {
        var pending = new InMemoryPendingDeviceCommandRegistry();
        var transport = new MqttDeviceCommandTransport(new CountingPublisher(), pending, Options(maxReads: 4), new InMemoryDeviceRebootTracker());
        var device = DeviceId.New();
        for (var i = 0; i < 4; i++)
        {
            // Published long ago and never answered: the device has dropped them (spec §3 TTL).
            var stale = Plan(device, GetInfo, expiresAt: DateTimeOffset.UtcNow.AddSeconds(-30));
            pending.Register(new PendingDeviceCommand(stale, DateTimeOffset.UtcNow.AddSeconds(-40), DateTimeOffset.UtcNow.AddSeconds(5)));
        }

        var fresh = Plan(device, GetInfo);

        Assert.Equal(CommandExecutionStatus.Published, (await transport.SendAsync(fresh, Prepared(fresh))).Status);
    }

    [Fact]
    public async Task A_busy_refusal_is_recorded_in_the_audit_without_a_publish()
    {
        var setup = new Setup();
        var pending = new InMemoryPendingDeviceCommandRegistry();
        var publisher = new CountingPublisher();
        var audit = new InMemoryCommandAuditStore();
        var handler = setup.Handler(GetInfo, new MqttDeviceCommandTransport(publisher, pending, Options(maxReads: 1), new InMemoryDeviceRebootTracker()), audit);
        var actor = setup.Actor("sys.info_view");

        var first = await handler.HandleAsync(setup.Request(GetInfo), actor);
        var request = setup.Request(GetInfo);
        var refused = await handler.HandleAsync(request, actor);

        Assert.Equal(CommandExecutionStatus.Published, first.Status);
        Assert.Equal(MqttDeviceCommandTransport.ReadBusy, refused.Code);
        Assert.Equal(MqttDeviceCommandTransport.ReadBusy, audit.Find(setup.Device.Id, request.CorrelationId)!.Code);
        Assert.Equal(1, publisher.Count);
    }

    [Fact]
    public async Task Every_command_expires_before_the_server_stops_waiting()
    {
        var setup = new Setup();
        var handler = setup.Handler(GetInfo);

        var result = await handler.HandleAsync(setup.Request(GetInfo), setup.Actor("sys.info_view"));

        Assert.Equal(CommandExecutionStatus.Recorded, result.Status);
        var (plan, prepared) = Assert.Single(setup.Transport.Records);
        Assert.True(plan.ExpiresAt - plan.Request.RequestedAt <= TimeSpan.FromMilliseconds(SendDeviceCommandHandler.MaxCommandTtlMs));
        using var json = JsonDocument.Parse(Body(prepared.Payload.ToArray()));
        Assert.True(json.RootElement.GetProperty("ttl_ms").GetInt64() <= SendDeviceCommandHandler.MaxCommandTtlMs);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public void The_read_limit_must_fit_the_device_queue(int maxReads)
    {
        var options = new MqttLinkOptions { Enabled = true, Host = "broker.lab", MaxPendingReadsPerDevice = maxReads };

        Assert.Equal("MQTT_READ_LIMIT_INVALID", Assert.Throws<InvalidOperationException>(options.Validate).Message);
    }

    private static Microsoft.Extensions.Options.IOptions<MqttLinkOptions> Options(int maxReads) =>
        Microsoft.Extensions.Options.Options.Create(new MqttLinkOptions { MaxPendingReadsPerDevice = maxReads });

    private static CommandExecutionPlan Plan(
        DeviceId deviceId, CommandDefinition definition, UserId? user = null, DateTimeOffset? expiresAt = null)
    {
        var now = DateTimeOffset.UtcNow;
        var request = new DeviceCommandRequest(deviceId, user ?? new UserId(Guid.NewGuid()), definition.Type, definition.Action,
            new Dictionary<string, object?>(), Guid.NewGuid().ToString("N"), now);
        return new CommandExecutionPlan(request, definition, 1, expiresAt ?? now.AddSeconds(10));
    }

    private static PreparedDeviceCommand Prepared(CommandExecutionPlan plan) =>
        new(plan.Request.DeviceId, $"lab/dev/cmd/{plan.Request.Type}", "{}"u8.ToArray(), 1, true);

    private static string Body(byte[] wire) => Encoding.UTF8.GetString(wire[..^(8 + 64 + 2)]) + "}";

    private sealed class CountingPublisher(string? failure = null) : IMqttCommandPublisher
    {
        private int _count;
        public int Count => _count;
        public bool IsConnected => true;

        public ValueTask<string?> PublishCommandAsync(string commandTopic, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _count);
            return ValueTask.FromResult(failure);
        }
    }

    private sealed class Setup
    {
        private static readonly UserId TestUser = new(Guid.Parse("00000000-0000-0000-0000-0000000000bb"));
        private readonly InMemoryDeviceAccessStore _access = new();

        public Setup()
        {
            Device = Domain.Devices.Device.Create("RTU", Organization.Create("Lab").Id);
            Device.Activate();
            Devices.Add(Device);
            Bindings.Upsert(DeviceMqttBinding.Create(Device.Id, "hms_toan_lab", "gateway_870c", null));
            Secrets.Import(Device.Id, Device.KeyGeneration, RandomNumberGenerator.GetBytes(32));
            Adapter = new HmsIdfCommandProtocolAdapter(Bindings, Secrets, new InMemoryDeviceLinkStateStore());
        }

        public Device Device { get; }
        public InMemoryDeviceStore Devices { get; } = new();
        public InMemoryDeviceMqttBindingStore Bindings { get; } = new();
        public RandomProvisioningSecretGenerator Secrets { get; } = new();
        public HmsIdfCommandProtocolAdapter Adapter { get; }
        public RecordingDeviceCommandTransport Transport { get; } = new();

        public DeviceCommandRequest Request(CommandDefinition definition) => Request(definition, null, DateTimeOffset.UtcNow);

        public DeviceCommandRequest Request(
            CommandDefinition definition, IReadOnlyDictionary<string, object?>? payload, DateTimeOffset requestedAt) =>
            new(Device.Id, TestUser, definition.Type, definition.Action, payload ?? new Dictionary<string, object?>(),
                Guid.NewGuid().ToString("N"), requestedAt, CatalogVersion: "test", CatalogHash: "hash");

        public SendDeviceCommandHandler Handler(
            CommandDefinition definition, IDeviceCommandTransport? transport = null, ICommandAuditStore? audit = null) => new(
            new InMemoryCommandCatalog([definition], new CommandCatalogMetadata("test", "hash")),
            Adapter, transport ?? Transport, audit ?? new InMemoryCommandAuditStore(), id => id == Device.Id ? Device : null,
            new DeviceAccessResolver(_access, _access));

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
    }
}
