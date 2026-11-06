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

    [Fact]
    public async Task A_device_takes_at_most_the_configured_number_of_pending_reads()
    {
        var pending = new InMemoryPendingDeviceCommandRegistry();
        var publisher = new CountingPublisher();
        var transport = new MqttDeviceCommandTransport(publisher, pending, Options(maxReads: 4));
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
        var transport = new MqttDeviceCommandTransport(new CountingPublisher(), pending, Options(maxReads: 4));
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
        var transport = new MqttDeviceCommandTransport(new CountingPublisher(), pending, Options(maxReads: 4));
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
        var transport = new MqttDeviceCommandTransport(new CountingPublisher(), pending, Options(maxReads: 4));
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
        var handler = setup.Handler(GetInfo, new MqttDeviceCommandTransport(publisher, pending, Options(maxReads: 1)), audit);
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

    private sealed class CountingPublisher : IMqttCommandPublisher
    {
        public int Count { get; private set; }
        public bool IsConnected => true;

        public ValueTask<string?> PublishCommandAsync(string commandTopic, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
        {
            Count++;
            return ValueTask.FromResult<string?>(null);
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

        public DeviceCommandRequest Request(CommandDefinition definition) =>
            new(Device.Id, TestUser, definition.Type, definition.Action, new Dictionary<string, object?>(),
                Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, CatalogVersion: "test", CatalogHash: "hash");

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
