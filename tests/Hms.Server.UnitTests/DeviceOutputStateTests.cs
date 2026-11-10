using System.Security.Cryptography;
using System.Text;
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

/// <summary>The relay state an operator acts on must come from a verified device reply, carry its
/// age, and turn "unknown" whenever it may no longer be true.</summary>
public sealed class DeviceOutputStateTests
{
    private static readonly CommandDefinition GetStatus = new(
        "control", "get_status", "gpio.do_view", CommandAvailability.Enabled, false, true, 120_000);
    private static readonly CommandDefinition SetOutput = new(
        "control", "set_output", "gpio.do_write", CommandAvailability.Enabled, false, true, 120_000, CommandClass.Physical);

    private const string SixChannels =
        """{"success":true,"data":{"channels":[{"channel":1,"state":true,"mode":"toggle","totalCommands":3,"pulseCount":0,"pulseActive":false},{"channel":2,"state":false,"mode":"toggle"},{"channel":3,"state":false,"mode":"toggle"},{"channel":4,"state":false,"mode":"toggle"},{"channel":5,"state":false,"mode":"toggle"},{"channel":6,"state":true,"mode":"pulse","pulseActive":true}]}}""";

    [Fact]
    public async Task A_verified_status_reply_becomes_the_output_state_and_leaves_telemetry_alone()
    {
        var link = new Link();
        link.Telemetry.Record(link.Device.Id, new Dictionary<string, object?> { ["uptime_seconds"] = 100L, ["free_heap"] = 5000L });
        var plan = await link.PublishedAsync(GetStatus);

        await link.Correlator.HandleAsync(link.Device.Id, link.SignedReply(plan, SixChannels), DateTimeOffset.UtcNow);

        var state = link.Outputs.Get(link.Device.Id)!;
        Assert.Equal(6, state.Channels.Count);
        Assert.True(state.Channels[0].State);
        Assert.Equal("pulse", state.Channels[5].Mode);
        Assert.True(state.Channels[5].PulseActive);
        Assert.False(state.IsStale);
        var telemetry = link.Telemetry.GetLatest(link.Device.Id)!;
        Assert.Equal(100, telemetry.UptimeSeconds);
        Assert.Equal(5000, telemetry.FreeHeap);
        var pushed = Assert.Single(link.Events.Snapshot(), x => x.EventType == DeviceReplyProjection.OutputsEvent);
        // The event only says "re-read": relay data reaches clients through the access-checked GET.
        Assert.Equal(["readAt", "stale", "staleReason"], pushed.Payload.Keys.Order());
    }

    [Fact]
    public async Task Output_state_is_never_taken_from_heartbeats_forged_replies_or_other_clients()
    {
        var link = new Link();
        var plan = await link.PublishedAsync(GetStatus);
        var other = link.Plan(GetStatus);

        await link.Inbound.HandleAsync(link.Device.Id, DeviceInboundKind.Status,
            """{"uptime_seconds":5,"digital_outputs":[{"channel":1,"state":true}]}"""u8.ToArray(), DateTimeOffset.UtcNow);
        await link.Correlator.HandleAsync(link.Device.Id, link.SignedReply(plan, SixChannels, new byte[32]), DateTimeOffset.UtcNow);
        await link.Correlator.HandleAsync(link.Device.Id, link.SignedReply(other, SixChannels), DateTimeOffset.UtcNow);

        Assert.Null(link.Outputs.Get(link.Device.Id));
    }

    [Theory]
    [InlineData("offline")]
    [InlineData("reboot")]
    [InlineData("timeout")]
    [InlineData("executed")]
    [InlineData("duplicate")]
    public async Task Known_output_state_turns_stale_when_it_may_no_longer_be_true(string trigger)
    {
        var link = new Link();
        link.Telemetry.Record(link.Device.Id, new Dictionary<string, object?> { ["uptime_seconds"] = 1000L });
        await link.Correlator.HandleAsync(link.Device.Id,
            link.SignedReply(await link.PublishedAsync(GetStatus), SixChannels), DateTimeOffset.UtcNow);

        switch (trigger)
        {
            case "offline":
                await link.Inbound.HandleAsync(link.Device.Id, DeviceInboundKind.Connectivity, """{"state":"offline"}"""u8.ToArray(), DateTimeOffset.UtcNow);
                break;
            case "reboot":
                await link.Inbound.HandleAsync(link.Device.Id, DeviceInboundKind.Status, """{"uptime_seconds":4}"""u8.ToArray(), DateTimeOffset.UtcNow);
                break;
            case "timeout":
                await link.PublishedAsync(SetOutput);
                await link.Correlator.SweepExpiredAsync(DateTimeOffset.UtcNow.AddMinutes(5));
                break;
            case "executed":
                var write = await link.PublishedAsync(SetOutput);
                await link.Correlator.HandleAsync(link.Device.Id, link.SignedReply(write, """{"success":true,"data":{"channel":1,"state":false}}"""), DateTimeOffset.UtcNow);
                break;
            case "duplicate":
                var dup = await link.PublishedAsync(SetOutput);
                await link.Correlator.HandleAsync(link.Device.Id, link.SignedReply(dup, """{"success":false,"error":"UNAUTHORIZED"}"""), DateTimeOffset.UtcNow);
                break;
        }

        var state = link.Outputs.Get(link.Device.Id)!;
        Assert.True(state.IsStale);
        Assert.NotNull(state.StaleReason);
        Assert.Equal(6, state.Channels.Count);
    }

    [Fact]
    public async Task A_fresh_status_read_clears_staleness()
    {
        var link = new Link();
        await link.Correlator.HandleAsync(link.Device.Id, link.SignedReply(await link.PublishedAsync(GetStatus), SixChannels), DateTimeOffset.UtcNow);
        await link.Inbound.HandleAsync(link.Device.Id, DeviceInboundKind.Connectivity, """{"state":"offline"}"""u8.ToArray(), DateTimeOffset.UtcNow);

        await link.Correlator.HandleAsync(link.Device.Id, link.SignedReply(await link.PublishedAsync(GetStatus), SixChannels), DateTimeOffset.UtcNow);

        Assert.False(link.Outputs.Get(link.Device.Id)!.IsStale);
    }

    [Theory]
    [InlineData("""{"success":false,"error":"expired"}""", false)]
    [InlineData("""{"success":false,"error":"schema:ttl_ms_invalid"}""", false)]
    [InlineData("""{"success":false,"error":"LANE_FULL"}""", false)]
    // Spec R3: a handler error does not prove the write did not run.
    [InlineData("""{"success":false,"error":"ESP_ERR_INVALID_STATE","data":{"error":"channel not in pulse mode"}}""", true)]
    [InlineData("""{"success":false,"error":"ESP_ERR_NO_MEM"}""", true)]
    public async Task Only_a_refusal_before_the_handler_keeps_the_output_state(string reply, bool stale)
    {
        var link = new Link();
        await link.Correlator.HandleAsync(link.Device.Id, link.SignedReply(await link.PublishedAsync(GetStatus), SixChannels), DateTimeOffset.UtcNow);
        var write = await link.PublishedAsync(SetOutput);

        await link.Correlator.HandleAsync(link.Device.Id, link.SignedReply(write, reply), DateTimeOffset.UtcNow);

        var state = link.Outputs.Get(link.Device.Id)!;
        Assert.Equal(stale, state.IsStale);
        if (stale)
            Assert.Equal(OutputStaleReasons.OutputCommandOutcomeUnknown, state.StaleReason);
    }

    [Fact]
    public async Task An_unreadable_status_reply_still_completes_the_command()
    {
        var link = new Link();
        var plan = await link.PublishedAsync(GetStatus);

        var outcome = await link.Correlator.HandleAsync(link.Device.Id,
            link.SignedReply(plan, """{"success":true,"data":{"unexpected":"shape"}}"""), DateTimeOffset.UtcNow);

        Assert.Equal(DeviceResponseOutcome.Completed, outcome);
        Assert.NotNull(link.Responses.Find(link.Device.Id, plan.Request.CorrelationId));
        Assert.Equal(CommandExecutionStatus.Executed, link.Audit.Find(link.Device.Id, plan.Request.CorrelationId)!.Status);
        Assert.Null(link.Outputs.Get(link.Device.Id));
    }

    [Fact]
    public async Task Output_notifications_are_not_coalesced_away_by_heartbeats()
    {
        var events = new BoundedDeviceEventPublisher();
        var deviceId = DeviceId.New();

        await events.PublishAsync(new AuthorizedDeviceEvent(deviceId, 0, 0, DeviceReplyProjection.OutputsEvent, new Dictionary<string, object?>()));
        await events.PublishAsync(new AuthorizedDeviceEvent(deviceId, 0, 0, "status", new Dictionary<string, object?>()));
        await events.PublishAsync(new AuthorizedDeviceEvent(deviceId, 0, 0, "status", new Dictionary<string, object?>()));

        Assert.Equal(["outputs", "status"], events.Snapshot().Select(x => x.EventType));
    }

    [Fact]
    public async Task Read_results_stay_off_the_realtime_channel_but_write_results_do_not()
    {
        var link = new Link();
        await link.Correlator.HandleAsync(link.Device.Id, link.SignedReply(await link.PublishedAsync(GetStatus), SixChannels), DateTimeOffset.UtcNow);
        await link.PublishedAsync(GetStatus);
        await link.Correlator.SweepExpiredAsync(DateTimeOffset.UtcNow.AddMinutes(5));

        // Every viewer shares the realtime backlog; the reader polls its own result instead.
        Assert.DoesNotContain(link.Events.Snapshot(), x => x.EventType == DeviceCommandResponseCorrelator.CommandResultEvent);

        var write = await link.PublishedAsync(SetOutput);
        await link.Correlator.HandleAsync(link.Device.Id,
            link.SignedReply(write, """{"success":true,"data":{"channel":1,"state":true}}"""), DateTimeOffset.UtcNow);

        var pushed = Assert.Single(link.Events.Snapshot(), x => x.EventType == DeviceCommandResponseCorrelator.CommandResultEvent);
        // The event reaches every viewer: outcome only, never the reply data.
        Assert.False(pushed.Payload.ContainsKey("data"));
    }

    [Fact]
    public async Task Output_state_turns_stale_even_when_the_audit_write_fails()
    {
        var link = new Link();
        await link.Correlator.HandleAsync(link.Device.Id, link.SignedReply(await link.PublishedAsync(GetStatus), SixChannels), DateTimeOffset.UtcNow);
        var write = await link.PublishedAsync(SetOutput);
        var lost = await link.PublishedAsync(SetOutput);
        link.Audit.Broken = true;

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await link.Correlator.HandleAsync(
            link.Device.Id, link.SignedReply(write, """{"success":true,"data":{"channel":1,"state":true}}"""), DateTimeOffset.UtcNow));
        Assert.True(link.Outputs.Get(link.Device.Id)!.IsStale);

        // A sweep still flags the state and leaves no command behind when its audit write fails.
        link.Outputs.Set(link.Device.Id, link.Outputs.Get(link.Device.Id)!.Channels, DateTimeOffset.UtcNow);
        Assert.Equal(1, await link.Correlator.SweepExpiredAsync(DateTimeOffset.UtcNow.AddMinutes(5)));
        Assert.Equal(OutputStaleReasons.OutputCommandOutcomeUnknown, link.Outputs.Get(link.Device.Id)!.StaleReason);
        Assert.Null(link.Pending.Find(link.Device.Id, lost.Request.CorrelationId));
    }

    private sealed class BreakableAuditStore : ICommandAuditStore
    {
        private readonly InMemoryCommandAuditStore _inner = new();
        public bool Broken { get; set; }

        public CommandExecutionResult? Find(DeviceId deviceId, string correlationId) => _inner.Find(deviceId, correlationId);
        public CommandAuditPlan? FindPlan(DeviceId deviceId, string correlationId) => _inner.FindPlan(deviceId, correlationId);
        public int CancelPendingForDevice(DeviceId deviceId, string code = "DEVICE_REVOKED") => _inner.CancelPendingForDevice(deviceId, code);
        public int FailUnfinished(string code) => _inner.FailUnfinished(code);

        public ValueTask<CommandAuditReservation> ReserveAsync(CommandExecutionPlan plan, string requestFingerprint, CancellationToken cancellationToken = default) =>
            _inner.ReserveAsync(plan, requestFingerprint, cancellationToken);

        public ValueTask RecordAsync(CommandExecutionPlan plan, CommandExecutionResult result, CancellationToken cancellationToken = default) =>
            Broken ? throw new InvalidOperationException("AUDIT_DOWN") : _inner.RecordAsync(plan, result, cancellationToken);

        public ValueTask<bool> TryCompleteAsync(CommandExecutionPlan plan, CommandExecutionResult result, IReadOnlySet<string> fromCodes, CancellationToken cancellationToken = default) =>
            Broken ? throw new InvalidOperationException("AUDIT_DOWN") : _inner.TryCompleteAsync(plan, result, fromCodes, cancellationToken);
    }

    private sealed class Link
    {
        private static readonly byte[] CommandKey = RandomNumberGenerator.GetBytes(32);

        public Link()
        {
            Device = Domain.Devices.Device.Create("RTU", Organization.Create("Lab").Id);
            Device.Activate();
            Devices.Add(Device);
            Secrets.Import(Device.Id, Device.KeyGeneration, CommandKey);
            Projection = new DeviceReplyProjection(Outputs, Events, Epochs, Permissions, NullLogger<DeviceReplyProjection>.Instance);
            Correlator = new DeviceCommandResponseCorrelator(Pending, new HmsIdfResponseCodec(), Secrets, Audit,
                Responses, Events, Epochs, Permissions, Tracker, NullLogger<DeviceCommandResponseCorrelator>.Instance, Projection);
            Inbound = new DeviceInboundMessageHandler(Devices, Telemetry, new InMemoryDeviceLinkStateStore(), Events, Epochs,
                Permissions, Correlator, Tracker, NullLogger<DeviceInboundMessageHandler>.Instance, Projection);
        }

        public Device Device { get; }
        public InMemoryDeviceStore Devices { get; } = new();
        public RandomProvisioningSecretGenerator Secrets { get; } = new();
        public InMemoryPendingDeviceCommandRegistry Pending { get; } = new();
        public BreakableAuditStore Audit { get; } = new();
        public InMemoryCommandResponseStore Responses { get; } = new();
        public BoundedDeviceEventPublisher Events { get; } = new();
        public InMemoryAuthorizationEpochStore Epochs { get; } = new();
        public InMemoryPermissionStore Permissions { get; } = new();
        public InMemoryDeviceTelemetryStore Telemetry { get; } = new();
        public InMemoryDeviceRebootTracker Tracker { get; } = new();
        public InMemoryDeviceOutputStateStore Outputs { get; } = new();
        public DeviceReplyProjection Projection { get; }
        public DeviceCommandResponseCorrelator Correlator { get; }
        public DeviceInboundMessageHandler Inbound { get; }

        public CommandExecutionPlan Plan(CommandDefinition definition)
        {
            var now = DateTimeOffset.UtcNow;
            var request = new DeviceCommandRequest(Device.Id, new UserId(Guid.NewGuid()), definition.Type, definition.Action,
                new Dictionary<string, object?>(), Guid.NewGuid().ToString("N"), now);
            return new CommandExecutionPlan(request, definition, Device.KeyGeneration, now.AddSeconds(10));
        }

        public async Task<CommandExecutionPlan> PublishedAsync(CommandDefinition definition)
        {
            var plan = Plan(definition);
            await Audit.ReserveAsync(plan, CommandRequestFingerprint.Compute(plan.Request));
            await Audit.RecordAsync(plan, new CommandExecutionResult(CommandExecutionStatus.Published, "MQTT_PUBLISHED", plan.Request.CorrelationId));
            Pending.Register(new PendingDeviceCommand(plan, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddSeconds(30)));
            return plan;
        }

        public byte[] SignedReply(CommandExecutionPlan plan, string fields, byte[]? key = null)
        {
            var body = $"{{\"id\":\"{plan.Request.CorrelationId}\",\"type\":\"{plan.Request.Type}\",\"action\":\"{plan.Request.Action}\",\"ts\":{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}," + fields[1..];
            var mac = Convert.ToHexStringLower(HMACSHA256.HashData(key ?? CommandKey, Encoding.UTF8.GetBytes("res:" + body)));
            return Encoding.UTF8.GetBytes(body[..^1] + ",\"sig\":\"" + mac + "\"}");
        }
    }
}
