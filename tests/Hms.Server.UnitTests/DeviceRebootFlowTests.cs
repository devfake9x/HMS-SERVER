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
using Hms.Server.Infrastructure.Mqtt;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hms.Server.UnitTests;

/// <summary>A reboot from publish to the audit row that says whether the device came back: the
/// lane gate (transport + registry + tracker), the reply and heartbeat paths (correlator +
/// inbound), and the conditional audit writes between them. The clock is injected, so the
/// 60 s and 120 s marks are exact.</summary>
public sealed class DeviceRebootFlowTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 10, 8, 0, 0, TimeSpan.Zero);

    private static readonly CommandDefinition Reboot = new("system", "reboot", "sys.reboot", CommandAvailability.Enabled,
        true, false, 60_000, CommandClass.Flash, ConfirmTemplate: "REBOOT-<DEVID>", DryRunSupported: true);
    private static readonly CommandDefinition SetHostname = new(
        "system", "set_hostname", "sys.hostname_edit", CommandAvailability.Enabled, false, true, 120_000, CommandClass.Write);
    private static readonly CommandDefinition SetOutput = new(
        "control", "set_output", "gpio.do_write", CommandAvailability.Enabled, false, true, 120_000, CommandClass.Physical);

    private const string Executed = """{"success":true,"data":{"rebooting":true}}""";

    // ---- the gate -----------------------------------------------------------------------------

    [Fact]
    public async Task A_second_reboot_is_refused_in_the_window_between_the_lane_freeing_and_the_reply_being_recorded()
    {
        var rig = new Rig();
        rig.At(0);
        var (first, sent) = await rig.SendAsync(Reboot);
        Assert.Equal(CommandExecutionStatus.Published, sent.Status);
        CommandExecutionResult? inside = null;
        // Runs after the registry removed the command (the lane is free) and before the reply was
        // acted on: the hold must already be there, not be armed by the reply.
        rig.Registry.AfterRemove = () => inside = rig.SendAsync(Reboot, dryRun: true).GetAwaiter().GetResult().Result;

        await rig.ReplyAsync(first, Executed, at: 1);

        Assert.NotNull(inside);
        Assert.Equal(DeviceRebootCodes.InProgress, inside.Code);
        Assert.Equal(CommandExecutionStatus.ContractUnavailable, inside.Status);
        Assert.Equal(DeviceRebootCodes.Scheduled, rig.CodeOf(first));
        rig.At(2);
        Assert.Equal(DeviceRebootCodes.InProgress, (await rig.SendAsync(Reboot)).Result.Code);
        Assert.Equal(1, rig.Publisher.Count);
    }

    [Fact]
    public async Task A_lost_reply_keeps_the_gate_closed_until_the_hold_ends()
    {
        var rig = new Rig();
        rig.At(0);
        var (first, _) = await rig.SendAsync(Reboot);

        await rig.Correlator.SweepExpiredAsync(T0.AddSeconds(31));

        Assert.Equal("DEVICE_RESPONSE_TIMEOUT", rig.CodeOf(first));
        rig.At(40);
        Assert.Equal(DeviceRebootCodes.InProgress, (await rig.SendAsync(Reboot, dryRun: true)).Result.Code);
        rig.At(121);
        Assert.Equal(DeviceRebootCodes.InProgress, (await rig.SendAsync(Reboot)).Result.Code);

        await rig.Correlator.SweepExpiredAsync(T0.AddSeconds(123));
        rig.At(124);
        Assert.Equal(CommandExecutionStatus.Published, (await rig.SendAsync(Reboot)).Result.Status);
        Assert.Equal(2, rig.Publisher.Count);
    }

    [Fact]
    public async Task A_reply_that_says_still_running_keeps_the_gate_closed()
    {
        var rig = new Rig();
        rig.At(0);
        var (first, _) = await rig.SendAsync(Reboot);

        await rig.ReplyAsync(first,
            """{"success":false,"error":"ESP_ERR_TIMEOUT","data":{"accepted":true,"pending":true,"operation_id":"9"}}""", at: 2);

        Assert.Equal(CommandExecutionStatus.Acknowledged, rig.Audit.Find(rig.Device.Id, first.Request.CorrelationId)!.Status);
        rig.At(70);
        Assert.Equal(DeviceRebootCodes.InProgress, (await rig.SendAsync(Reboot)).Result.Code);
        Assert.Equal(DeviceRebootCodes.InProgress, (await rig.SendAsync(Reboot, dryRun: true)).Result.Code);
    }

    [Fact]
    public async Task A_publish_the_broker_did_not_confirm_keeps_the_gate_closed()
    {
        var rig = new Rig(publishFailure: "MQTT_PUBLISH_UNSPECIFIED");
        rig.At(0);
        var (first, sent) = await rig.SendAsync(Reboot);
        Assert.Equal(MqttDeviceCommandTransport.PublishUnconfirmed, sent.Code);

        await rig.Correlator.SweepExpiredAsync(T0.AddSeconds(31));

        rig.At(40);
        Assert.Equal(DeviceRebootCodes.InProgress, (await rig.SendAsync(Reboot)).Result.Code);
        Assert.Equal("DEVICE_RESPONSE_TIMEOUT", rig.CodeOf(first));
    }

    [Fact]
    public async Task A_reboot_that_certainly_never_left_the_server_frees_the_gate_but_keeps_the_minute()
    {
        var rig = new Rig(publishFailure: "MQTT_RESPONSE_SUBSCRIPTION_MISSING");
        rig.At(0);
        var (_, sent) = await rig.SendAsync(Reboot);
        Assert.Equal("MQTT_RESPONSE_SUBSCRIPTION_MISSING", sent.Code);

        rig.At(5);
        Assert.Equal(DeviceRebootCodes.TooSoon, (await rig.SendAsync(Reboot)).Result.Code);
        rig.Publisher.Failure = null;
        Assert.Equal(CommandExecutionStatus.Published, (await rig.SendAsync(Reboot, dryRun: true)).Result.Status);
    }

    [Fact]
    public async Task A_reboot_the_device_refused_frees_the_gate_but_keeps_the_minute()
    {
        var rig = new Rig();
        rig.At(0);
        var (first, _) = await rig.SendAsync(Reboot);

        await rig.ReplyAsync(first, """{"success":false,"error":"UNAUTHORIZED"}""", at: 1);

        Assert.Equal("DEVICE_REJECTED", rig.CodeOf(first));
        rig.At(10);
        Assert.Equal(DeviceRebootCodes.TooSoon, (await rig.SendAsync(Reboot)).Result.Code);
        var (dry, dryResult) = await rig.SendAsync(Reboot, dryRun: true);
        Assert.Equal(CommandExecutionStatus.Published, dryResult.Status);
        await rig.ReplyAsync(dry, """{"success":true,"data":{"would_apply":true}}""", at: 11);
        rig.At(61);
        Assert.Equal(CommandExecutionStatus.Published, (await rig.SendAsync(Reboot)).Result.Status);
    }

    [Fact]
    public async Task Once_observed_a_dry_run_passes_and_a_real_reboot_waits_for_the_minute()
    {
        var rig = new Rig();
        await rig.HeartbeatAsync(500, -5);
        rig.At(0);
        var (first, _) = await rig.SendAsync(Reboot);
        await rig.ReplyAsync(first, Executed, at: 1);
        await rig.HeartbeatAsync(4, 15);
        Assert.Equal(DeviceRebootCodes.Observed, rig.CodeOf(first));

        rig.At(16);
        var (dry, dryResult) = await rig.SendAsync(Reboot, dryRun: true);
        Assert.Equal(CommandExecutionStatus.Published, dryResult.Status);
        await rig.ReplyAsync(dry, """{"success":true,"data":{"would_apply":true}}""", at: 17);
        rig.At(18);
        Assert.Equal(DeviceRebootCodes.TooSoon, (await rig.SendAsync(Reboot)).Result.Code);
        rig.At(60);
        Assert.Equal(CommandExecutionStatus.Published, (await rig.SendAsync(Reboot)).Result.Status);
    }

    [Fact]
    public async Task Refused_reboots_do_not_use_up_the_write_slots()
    {
        var rig = new Rig();
        rig.At(0);
        var (first, _) = await rig.SendAsync(Reboot);
        await rig.ReplyAsync(first, Executed, at: 1);

        for (var i = 0; i < 11; i++)
        {
            rig.At(2 + i);
            Assert.Equal(DeviceRebootCodes.InProgress, (await rig.SendAsync(Reboot)).Result.Code);
        }

        rig.At(14);
        var hostname = await rig.SendAsync(SetHostname, payload: new Dictionary<string, object?> { ["hostname"] = "gw" });
        Assert.Equal(CommandExecutionStatus.Published, hostname.Result.Status);
    }

    [Fact]
    public async Task While_a_reboot_is_held_other_writes_and_relay_commands_still_publish()
    {
        var rig = new Rig();
        rig.At(0);
        var (first, _) = await rig.SendAsync(Reboot);
        await rig.ReplyAsync(first, Executed, at: 1);

        rig.At(2);
        Assert.Equal(CommandExecutionStatus.Published, (await rig.SendAsync(SetHostname)).Result.Status);
        Assert.Equal(CommandExecutionStatus.Published, (await rig.SendAsync(SetOutput)).Result.Status);
    }

    [Fact]
    public async Task A_reboot_sent_while_another_write_waits_for_its_reply_meets_the_lane_first()
    {
        var rig = new Rig();
        rig.At(0);
        await rig.SendAsync(Reboot);

        rig.At(1);
        Assert.Equal(DeviceLaneCodes.WriteBusy, (await rig.SendAsync(Reboot)).Result.Code);
    }

    [Fact]
    public async Task A_dry_run_reboot_is_answered_without_holding_or_tracking()
    {
        var rig = new Rig();
        rig.At(0);
        var (dry, _) = await rig.SendAsync(Reboot, dryRun: true);

        await rig.ReplyAsync(dry, """{"success":true,"data":{"would_apply":true}}""", at: 1);

        Assert.Equal("DEVICE_EXECUTED", rig.CodeOf(dry));
        rig.At(2);
        Assert.Equal(CommandExecutionStatus.Published, (await rig.SendAsync(Reboot)).Result.Status);
    }

    // ---- reply and heartbeat -------------------------------------------------------------------

    [Fact]
    public async Task A_reply_that_the_device_will_reboot_is_scheduled_with_the_uptime_before_and_the_outputs_marked_stale()
    {
        var rig = new Rig();
        rig.Outputs.Set(rig.Device.Id, [new OutputChannelState(1, false, "toggle", false)], T0.AddSeconds(-10));
        await rig.HeartbeatAsync(500, -5);
        rig.At(0);
        var (plan, _) = await rig.SendAsync(Reboot);

        await rig.ReplyAsync(plan, Executed, at: 1);

        var row = rig.Audit.Find(rig.Device.Id, plan.Request.CorrelationId)!;
        Assert.Equal(CommandExecutionStatus.Executed, row.Status);
        Assert.Equal(DeviceRebootCodes.Scheduled, row.Code);
        Assert.Contains("uptime_before=500", row.Detail);
        Assert.Equal(OutputStaleReasons.DeviceRebootScheduled, rig.Outputs.Get(rig.Device.Id)!.StaleReason);
        Assert.Contains(rig.Events.Snapshot(), x => x.EventType == DeviceReplyProjection.OutputsEvent &&
            (string?)x.Payload["staleReason"] == "device_reboot_scheduled");
        var result = Assert.Single(rig.Events.Snapshot(), x => x.EventType == DeviceCommandResponseCorrelator.CommandResultEvent);
        Assert.Equal(DeviceRebootCodes.Scheduled, result.Payload["code"]);
        Assert.Equal(plan.Request.CorrelationId, result.Payload["correlationId"]);
    }

    [Fact]
    public async Task A_lower_uptime_in_a_heartbeat_ends_the_row_as_observed_and_pushes_the_change()
    {
        var rig = new Rig();
        await rig.HeartbeatAsync(500, -5);
        rig.At(0);
        var (plan, _) = await rig.SendAsync(Reboot);
        await rig.ReplyAsync(plan, Executed, at: 1);

        await rig.HeartbeatAsync(7, 20);

        var row = rig.Audit.Find(rig.Device.Id, plan.Request.CorrelationId)!;
        Assert.Equal(DeviceRebootCodes.Observed, row.Code);
        Assert.Equal(CommandExecutionStatus.Executed, row.Status);
        Assert.Contains("evidence=heartbeat", row.Detail);
        Assert.Contains("uptime_before=500", row.Detail);
        Assert.Contains("uptime_after=7", row.Detail);
        var codes = rig.Events.Snapshot().Where(x => x.EventType == DeviceCommandResponseCorrelator.CommandResultEvent)
            .Select(x => (string)x.Payload["code"]!).ToArray();
        Assert.Equal([DeviceRebootCodes.Scheduled, DeviceRebootCodes.Observed], codes);
    }

    [Fact]
    public async Task The_uptime_comes_from_the_heartbeat_message_not_from_the_shared_telemetry_store()
    {
        var rig = new Rig();
        await rig.HeartbeatAsync(500, -5);
        rig.At(0);
        var (plan, _) = await rig.SendAsync(Reboot);
        await rig.ReplyAsync(plan, Executed, at: 1);

        // POST /telemetry and the bridge write the same store: a huge uptime planted there makes
        // the next real heartbeat (600, higher than 500) look like a drop to a store-based check.
        rig.Telemetry.Record(rig.Device.Id, new Dictionary<string, object?> { ["uptime_seconds"] = 99_999L });
        await rig.HeartbeatAsync(600, 10);

        Assert.Equal(DeviceRebootCodes.Scheduled, rig.CodeOf(plan));
    }

    [Fact]
    public async Task No_drop_by_the_deadline_is_not_observed_and_nothing_is_sent_again()
    {
        var rig = new Rig();
        await rig.HeartbeatAsync(500, -5);
        rig.At(0);
        var (plan, _) = await rig.SendAsync(Reboot);
        await rig.ReplyAsync(plan, Executed, at: 1);
        await rig.HeartbeatAsync(530, 30);

        await rig.Correlator.SweepExpiredAsync(T0.AddSeconds(122));
        Assert.Equal(DeviceRebootCodes.Scheduled, rig.CodeOf(plan));
        await rig.Correlator.SweepExpiredAsync(T0.AddSeconds(124));

        Assert.Equal(DeviceRebootCodes.NotObserved, rig.CodeOf(plan));
        Assert.Equal(1, rig.Publisher.Count);
        Assert.Null(rig.Pending.Find(rig.Device.Id, plan.Request.CorrelationId));

        // A drop in the ten minutes after still raises the row once.
        await rig.HeartbeatAsync(9, 200);
        Assert.Equal(DeviceRebootCodes.Observed, rig.CodeOf(plan));
        Assert.Contains("late_s=", rig.Audit.Find(rig.Device.Id, plan.Request.CorrelationId)!.Detail);
    }

    [Fact]
    public async Task While_the_servers_broker_link_is_down_the_sweep_concludes_nothing()
    {
        var rig = new Rig();
        await rig.HeartbeatAsync(500, -5);
        rig.At(0);
        var (plan, _) = await rig.SendAsync(Reboot);
        await rig.ReplyAsync(plan, Executed, at: 1);

        await rig.Correlator.SweepExpiredAsync(T0.AddSeconds(124), brokerConnected: false);
        await rig.Correlator.SweepExpiredAsync(T0.AddSeconds(125));

        Assert.Equal(DeviceRebootCodes.Scheduled, rig.CodeOf(plan));
    }

    [Fact]
    public async Task A_late_reply_after_the_timeout_is_ignored_and_a_drop_seen_meanwhile_raises_the_row()
    {
        var rig = new Rig();
        await rig.HeartbeatAsync(500, -5);
        rig.At(0);
        var (plan, _) = await rig.SendAsync(Reboot);
        await rig.HeartbeatAsync(4, 20);
        await rig.Correlator.SweepExpiredAsync(T0.AddSeconds(31));
        Assert.Equal("DEVICE_RESPONSE_TIMEOUT", rig.CodeOf(plan));

        var late = await rig.ReplyAsync(plan, Executed, at: 40);
        Assert.Equal(DeviceResponseOutcome.Ignored, late);
        Assert.Equal("DEVICE_RESPONSE_TIMEOUT", rig.CodeOf(plan));

        await rig.Correlator.SweepExpiredAsync(T0.AddSeconds(123));

        var row = rig.Audit.Find(rig.Device.Id, plan.Request.CorrelationId)!;
        Assert.Equal(CommandExecutionStatus.Executed, row.Status);
        Assert.Equal(DeviceRebootCodes.Observed, row.Code);
        Assert.Contains("reply=none", row.Detail);
        Assert.Contains(rig.Events.Snapshot(), x => x.EventType == DeviceCommandResponseCorrelator.CommandResultEvent &&
            Equals(x.Payload["code"], DeviceRebootCodes.Observed));
    }

    [Fact]
    public async Task A_lost_reply_with_no_drop_leaves_the_timeout_row_alone()
    {
        var rig = new Rig();
        await rig.HeartbeatAsync(500, -5);
        rig.At(0);
        var (plan, _) = await rig.SendAsync(Reboot);
        await rig.Correlator.SweepExpiredAsync(T0.AddSeconds(31));

        await rig.Correlator.SweepExpiredAsync(T0.AddSeconds(123));

        Assert.Equal("DEVICE_RESPONSE_TIMEOUT", rig.CodeOf(plan));
    }

    [Fact]
    public async Task A_reply_that_lost_the_race_with_the_sweep_neither_schedules_nor_starts_tracking()
    {
        var rig = new Rig();
        rig.At(0);
        var (plan, _) = await rig.SendAsync(Reboot);
        rig.Registry.StealOnRemove = true;

        var outcome = await rig.ReplyAsync(plan, Executed, at: 1);

        Assert.Equal(DeviceResponseOutcome.Ignored, outcome);
        Assert.NotEqual(DeviceRebootCodes.Scheduled, rig.CodeOf(plan));
        // Still held, not tracked: a later reply for the same command could still upgrade it.
        Assert.NotNull(rig.Tracker.Upgrade(plan, T0.AddSeconds(2)));
    }

    [Fact]
    public async Task A_finished_row_is_not_overwritten_by_a_later_completion()
    {
        var rig = new Rig();
        await rig.HeartbeatAsync(500, -5);
        rig.At(0);
        var (plan, _) = await rig.SendAsync(Reboot);
        await rig.ReplyAsync(plan, Executed, at: 1);
        await rig.HeartbeatAsync(4, 20);
        Assert.Equal(DeviceRebootCodes.Observed, rig.CodeOf(plan));

        await rig.Correlator.CompleteRebootAsync(new RebootCompletion(plan, DeviceRebootCodes.NotObserved,
            new HashSet<string> { DeviceRebootCodes.Scheduled }, "late sweep"));

        Assert.Equal(DeviceRebootCodes.Observed, rig.CodeOf(plan));
    }

    [Fact]
    public async Task Rebinding_to_another_topic_ends_a_tracked_reboot_as_outcome_unknown_and_the_same_topic_does_not()
    {
        var rig = new Rig();
        rig.At(0);
        var (plan, _) = await rig.SendAsync(Reboot);
        await rig.ReplyAsync(plan, Executed, at: 1);
        var handler = new ConfigureDeviceMqttBindingHandler(rig.Devices, rig.Bindings, rig.Tracker);

        handler.Handle(rig.Device.Id, "hms_toan_lab", "gateway_870c", 11);
        await rig.Correlator.SweepExpiredAsync(T0.AddSeconds(5));
        Assert.Equal(DeviceRebootCodes.Scheduled, rig.CodeOf(plan));

        handler.Handle(rig.Device.Id, "hms_toan_lab", "other_device", 11);
        await rig.Correlator.SweepExpiredAsync(T0.AddSeconds(6));

        var row = rig.Audit.Find(rig.Device.Id, plan.Request.CorrelationId)!;
        Assert.Equal(DeviceRebootCodes.OutcomeUnknown, row.Code);
        Assert.Contains("abandoned=rebind", row.Detail);
    }

    [Fact]
    public async Task Revoking_the_device_ends_a_tracked_reboot_as_outcome_unknown()
    {
        var rig = new Rig();
        rig.At(0);
        var (plan, _) = await rig.SendAsync(Reboot);
        await rig.ReplyAsync(plan, Executed, at: 1);

        new RevokeDeviceHandler(rig.Devices, rig.Epochs, rig.Tracker, rig.Audit).Handle(rig.Device.Id);
        await rig.Correlator.SweepExpiredAsync(T0.AddSeconds(5));

        var row = rig.Audit.Find(rig.Device.Id, plan.Request.CorrelationId)!;
        Assert.Equal(DeviceRebootCodes.OutcomeUnknown, row.Code);
        Assert.Contains("abandoned=revoked", row.Detail);
    }

    [Fact]
    public async Task An_audit_write_that_fails_is_tried_again_by_the_next_sweep()
    {
        var rig = new Rig();
        await rig.HeartbeatAsync(500, -5);
        rig.At(0);
        var (plan, _) = await rig.SendAsync(Reboot);
        await rig.ReplyAsync(plan, Executed, at: 1);
        rig.Audit.TryCompleteFailures = 1;

        await rig.HeartbeatAsync(4, 20);
        Assert.Equal(DeviceRebootCodes.Scheduled, rig.CodeOf(plan));
        await rig.Correlator.SweepExpiredAsync(T0.AddSeconds(22));

        Assert.Equal(DeviceRebootCodes.Observed, rig.CodeOf(plan));
    }

    // ---- fixes after review --------------------------------------------------------------------

    [Fact]
    public async Task With_a_long_reply_timeout_a_reply_inside_it_still_finds_the_hold()
    {
        var rig = new Rig(responseTimeoutSeconds: 180);
        await rig.HeartbeatAsync(500, -5);
        rig.At(0);
        var (plan, _) = await rig.SendAsync(Reboot);

        // Past the old fixed hold (122 s) and still inside the 180 s the server waits for a reply.
        await rig.Correlator.SweepExpiredAsync(T0.AddSeconds(125));
        await rig.ReplyAsync(plan, Executed, at: 150);

        Assert.Equal(DeviceRebootCodes.Scheduled, rig.CodeOf(plan));
        await rig.Correlator.SweepExpiredAsync(T0.AddSeconds(185));
        Assert.Equal(DeviceRebootCodes.Scheduled, rig.CodeOf(plan));
    }

    [Fact]
    public async Task With_a_long_reply_timeout_the_hold_ends_after_the_timeout_row_so_it_is_never_overwritten()
    {
        var rig = new Rig(responseTimeoutSeconds: 180);
        await rig.HeartbeatAsync(500, -5);
        rig.At(0);
        var (plan, _) = await rig.SendAsync(Reboot);
        await rig.HeartbeatAsync(4, 20);

        await rig.Correlator.SweepExpiredAsync(T0.AddSeconds(123));
        Assert.Equal("MQTT_PUBLISHED", rig.CodeOf(plan));
        await rig.Correlator.SweepExpiredAsync(T0.AddSeconds(181));
        Assert.Equal("DEVICE_RESPONSE_TIMEOUT", rig.CodeOf(plan));
        await rig.Correlator.SweepExpiredAsync(T0.AddSeconds(184));
        Assert.Equal(DeviceRebootCodes.Observed, rig.CodeOf(plan));
        await rig.Correlator.SweepExpiredAsync(T0.AddSeconds(200));
        Assert.Equal(DeviceRebootCodes.Observed, rig.CodeOf(plan));
    }

    [Fact]
    public async Task A_revoke_and_a_sweep_between_the_upgrade_and_the_scheduled_write_do_not_leave_the_row_scheduled()
    {
        var rig = new Rig();
        await rig.HeartbeatAsync(500, -5);
        rig.At(0);
        var (plan, _) = await rig.SendAsync(Reboot);
        // Just before the reply's SCHEDULED write (the tracker is already tracking): the device is
        // revoked and a sweep tries to write the abandon result against a row still MQTT_PUBLISHED.
        rig.Audit.BeforeRecord = (DeviceRebootCodes.Scheduled, async () =>
        {
            rig.Tracker.Abandon(rig.Device.Id, "revoked");
            await rig.Correlator.SweepExpiredAsync(T0.AddSeconds(2));
        });

        await rig.ReplyAsync(plan, Executed, at: 1);
        Assert.Equal(DeviceRebootCodes.Scheduled, rig.CodeOf(plan));
        await rig.Correlator.SweepExpiredAsync(T0.AddSeconds(4));

        var row = rig.Audit.Find(rig.Device.Id, plan.Request.CorrelationId)!;
        Assert.Equal(DeviceRebootCodes.OutcomeUnknown, row.Code);
        Assert.Contains("abandoned=revoked", row.Detail);
    }

    [Fact]
    public async Task A_drop_seen_before_the_reply_is_kept_and_the_row_ends_observed_with_the_old_uptime()
    {
        var rig = new Rig();
        await rig.HeartbeatAsync(500, -5);
        rig.At(0);
        var (plan, _) = await rig.SendAsync(Reboot);
        await rig.HeartbeatAsync(4, 5);

        await rig.ReplyAsync(plan, Executed, at: 6);
        Assert.Contains("uptime_before=500", rig.Audit.Find(rig.Device.Id, plan.Request.CorrelationId)!.Detail);
        await rig.Correlator.SweepExpiredAsync(T0.AddSeconds(8));

        var row = rig.Audit.Find(rig.Device.Id, plan.Request.CorrelationId)!;
        Assert.Equal(DeviceRebootCodes.Observed, row.Code);
        Assert.Contains("uptime_before=500", row.Detail);
        Assert.Contains("uptime_after=4", row.Detail);
        Assert.DoesNotContain("reply=none", row.Detail);
    }

    [Fact]
    public async Task A_reboot_the_device_only_acknowledged_is_not_reported_as_having_no_reply()
    {
        var rig = new Rig();
        await rig.HeartbeatAsync(500, -5);
        rig.At(0);
        var (plan, _) = await rig.SendAsync(Reboot);
        await rig.HeartbeatAsync(4, 20);
        await rig.ReplyAsync(plan,
            """{"success":false,"error":"ESP_ERR_TIMEOUT","data":{"accepted":true,"pending":true,"operation_id":"9"}}""", at: 25);

        await rig.Correlator.SweepExpiredAsync(T0.AddSeconds(124));

        var row = rig.Audit.Find(rig.Device.Id, plan.Request.CorrelationId)!;
        Assert.Equal(DeviceRebootCodes.Observed, row.Code);
        Assert.Contains("reply=accepted", row.Detail);
        Assert.DoesNotContain("reply=none", row.Detail);
    }

    // ---- audit store ---------------------------------------------------------------------------

    [Fact]
    public async Task The_in_memory_audit_store_completes_only_from_the_codes_it_is_given()
    {
        var store = new InMemoryCommandAuditStore();
        var rig = new Rig();
        rig.At(0);
        var (plan, _) = await rig.SendAsync(Reboot);
        await store.ReserveAsync(plan, CommandRequestFingerprint.Compute(plan.Request));
        await store.RecordAsync(plan, new CommandExecutionResult(CommandExecutionStatus.Executed, DeviceRebootCodes.Scheduled, plan.Request.CorrelationId));
        CommandExecutionResult Observed() => new(CommandExecutionStatus.Executed, DeviceRebootCodes.Observed, plan.Request.CorrelationId, "d");

        Assert.False(await store.TryCompleteAsync(plan, Observed(), new HashSet<string> { DeviceRebootCodes.NotObserved }));
        Assert.Equal(DeviceRebootCodes.Scheduled, store.Find(plan.Request.DeviceId, plan.Request.CorrelationId)!.Code);
        Assert.True(await store.TryCompleteAsync(plan, Observed(), new HashSet<string> { DeviceRebootCodes.Scheduled }));
        Assert.Equal("d", store.Find(plan.Request.DeviceId, plan.Request.CorrelationId)!.Detail);
        Assert.False(await store.TryCompleteAsync(plan, Observed(), new HashSet<string> { DeviceRebootCodes.Scheduled }));
    }

    [Fact]
    public async Task A_restart_turns_a_scheduled_reboot_into_outcome_unknown_and_counts_it()
    {
        var store = new InMemoryCommandAuditStore();
        var rig = new Rig();
        rig.At(0);
        var (plan, _) = await rig.SendAsync(Reboot);
        await store.ReserveAsync(plan, CommandRequestFingerprint.Compute(plan.Request));
        await store.RecordAsync(plan, new CommandExecutionResult(CommandExecutionStatus.Executed, DeviceRebootCodes.Scheduled, plan.Request.CorrelationId));
        var (observed, _) = await rig.SendAsync(Reboot, dryRun: true);
        await store.ReserveAsync(observed, CommandRequestFingerprint.Compute(observed.Request));
        await store.RecordAsync(observed, new CommandExecutionResult(CommandExecutionStatus.Executed, DeviceRebootCodes.Observed, observed.Request.CorrelationId));

        var failed = store.FailUnfinished("COMMAND_OUTCOME_UNKNOWN");

        Assert.Equal(1, failed);
        var row = store.Find(plan.Request.DeviceId, plan.Request.CorrelationId)!;
        Assert.Equal(DeviceRebootCodes.OutcomeUnknown, row.Code);
        Assert.Equal(CommandExecutionStatus.Executed, row.Status);
        Assert.Equal(CommandTransportOutcome.RebootWatchLostDetail, row.Detail);
        Assert.Equal(DeviceRebootCodes.Observed, store.Find(observed.Request.DeviceId, observed.Request.CorrelationId)!.Code);
    }

    // ---- rig -----------------------------------------------------------------------------------

    private sealed class Rig
    {
        private static readonly byte[] CommandKey = RandomNumberGenerator.GetBytes(32);

        public Rig(string? publishFailure = null, int responseTimeoutSeconds = 30)
        {
            Device = Domain.Devices.Device.Create("RTU", Organization.Create("Lab").Id);
            Device.Activate();
            Devices.Add(Device);
            Bindings.Upsert(DeviceMqttBinding.Create(Device.Id, "hms_toan_lab", "gateway_870c", 11));
            Secrets.Import(Device.Id, Device.KeyGeneration, CommandKey);
            Publisher = new CountingPublisher { Failure = publishFailure };
            Registry = new HookedRegistry(Pending);
            Transport = new MqttDeviceCommandTransport(Publisher, Registry,
                Microsoft.Extensions.Options.Options.Create(new MqttLinkOptions { ResponseTimeoutSeconds = responseTimeoutSeconds }), Tracker, Clock);
            Projection = new DeviceReplyProjection(Outputs, Events, Epochs, Permissions, NullLogger<DeviceReplyProjection>.Instance);
            Correlator = new DeviceCommandResponseCorrelator(Registry, new HmsIdfResponseCodec(), Secrets, Audit,
                Responses, Events, Epochs, Permissions, Tracker, NullLogger<DeviceCommandResponseCorrelator>.Instance, Projection);
            Inbound = new DeviceInboundMessageHandler(Devices, Telemetry, new InMemoryDeviceLinkStateStore(), Events, Epochs,
                Permissions, Correlator, Tracker, NullLogger<DeviceInboundMessageHandler>.Instance, Projection);
        }

        public Device Device { get; }
        public TestClock Clock { get; } = new() { Now = T0 };
        public InMemoryDeviceStore Devices { get; } = new();
        public InMemoryDeviceMqttBindingStore Bindings { get; } = new();
        public RandomProvisioningSecretGenerator Secrets { get; } = new();
        public InMemoryPendingDeviceCommandRegistry Pending { get; } = new();
        public HookedRegistry Registry { get; }
        public InMemoryDeviceRebootTracker Tracker { get; } = new();
        public FlakyAuditStore Audit { get; } = new();
        public InMemoryCommandResponseStore Responses { get; } = new();
        public BoundedDeviceEventPublisher Events { get; } = new();
        public InMemoryAuthorizationEpochStore Epochs { get; } = new();
        public InMemoryPermissionStore Permissions { get; } = new();
        public InMemoryDeviceTelemetryStore Telemetry { get; } = new();
        public InMemoryDeviceOutputStateStore Outputs { get; } = new();
        public CountingPublisher Publisher { get; }
        public DeviceReplyProjection Projection { get; }
        public MqttDeviceCommandTransport Transport { get; }
        public DeviceCommandResponseCorrelator Correlator { get; }
        public DeviceInboundMessageHandler Inbound { get; }

        public void At(double seconds) => Clock.Now = T0.AddSeconds(seconds);

        public string CodeOf(CommandExecutionPlan plan) => Audit.Find(Device.Id, plan.Request.CorrelationId)!.Code;

        /// <summary>What the handler does around the transport: reserve the audit row, send, record the result.</summary>
        public async Task<(CommandExecutionPlan Plan, CommandExecutionResult Result)> SendAsync(
            CommandDefinition definition, bool dryRun = false, IReadOnlyDictionary<string, object?>? payload = null)
        {
            var now = Clock.GetUtcNow();
            var request = new DeviceCommandRequest(Device.Id, new UserId(Guid.NewGuid()), definition.Type, definition.Action,
                payload ?? new Dictionary<string, object?>(), Guid.NewGuid().ToString("N"), now, DryRun: dryRun);
            var plan = new CommandExecutionPlan(request, definition, Device.KeyGeneration, now.AddSeconds(10));
            await Audit.ReserveAsync(plan, CommandRequestFingerprint.Compute(request));
            var prepared = new PreparedDeviceCommand(Device.Id, "lab/dev/cmd/" + definition.Type, "{}"u8.ToArray(), plan.KeyGeneration, true);
            var result = await Transport.SendAsync(plan, prepared);
            await Audit.RecordAsync(plan, result);
            return (plan, result);
        }

        public async Task<DeviceResponseOutcome> ReplyAsync(CommandExecutionPlan plan, string fields, double at)
        {
            var body = $"{{\"id\":\"{plan.Request.CorrelationId}\",\"type\":\"{plan.Request.Type}\",\"action\":\"{plan.Request.Action}\",\"ts\":{T0.ToUnixTimeSeconds()}," + fields[1..];
            var mac = Convert.ToHexStringLower(HMACSHA256.HashData(CommandKey, Encoding.UTF8.GetBytes("res:" + body)));
            var wire = Encoding.UTF8.GetBytes(body[..^1] + ",\"sig\":\"" + mac + "\"}");
            return await Correlator.HandleAsync(Device.Id, wire, T0.AddSeconds(at));
        }

        public ValueTask HeartbeatAsync(long uptime, double at) => Inbound.HandleAsync(Device.Id, DeviceInboundKind.Status,
            Encoding.UTF8.GetBytes($"{{\"uptime_seconds\":{uptime}}}"), T0.AddSeconds(at));
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class CountingPublisher : IMqttCommandPublisher
    {
        private int _count;
        public int Count => _count;
        public string? Failure { get; set; }
        public bool IsConnected => true;

        public ValueTask<string?> PublishCommandAsync(string commandTopic, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _count);
            return ValueTask.FromResult(Failure);
        }
    }

    /// <summary>Wraps the real registry so a test can act in the middle of a reply's removal.</summary>
    private sealed class HookedRegistry(IPendingDeviceCommandRegistry inner) : IPendingDeviceCommandRegistry
    {
        public Action? AfterRemove { get; set; }

        /// <summary>The sweep took the command between the reply's Find and its Remove.</summary>
        public bool StealOnRemove { get; set; }

        public void Register(PendingDeviceCommand pending) => inner.Register(pending);
        public bool TryRegisterRead(PendingDeviceCommand pending, int maxPerDevice, int maxPerUser) =>
            inner.TryRegisterRead(pending, maxPerDevice, maxPerUser);
        public string? TryRegisterWrite(PendingDeviceCommand pending, int maxPerMinute, Func<string?>? admit = null) =>
            inner.TryRegisterWrite(pending, maxPerMinute, admit);
        public void HoldWriteLane(DeviceId deviceId, DateTimeOffset until) => inner.HoldWriteLane(deviceId, until);
        public PendingDeviceCommand? Find(DeviceId deviceId, string correlationId) => inner.Find(deviceId, correlationId);
        public IReadOnlyList<PendingDeviceCommand> TakeExpired(DateTimeOffset now) => inner.TakeExpired(now);

        public bool Remove(DeviceId deviceId, string correlationId)
        {
            if (StealOnRemove)
            {
                inner.Remove(deviceId, correlationId);
                return false;
            }

            var removed = inner.Remove(deviceId, correlationId);
            if (removed)
                AfterRemove?.Invoke();
            return removed;
        }
    }

    /// <summary>The in-memory store, except that a set number of conditional completions throw.</summary>
    private sealed class FlakyAuditStore : ICommandAuditStore
    {
        private readonly InMemoryCommandAuditStore _inner = new();
        public int TryCompleteFailures { get; set; }

        /// <summary>Runs just before a record with this code is written (an interleaving point).</summary>
        public (string Code, Func<Task> Action)? BeforeRecord { get; set; }

        public CommandExecutionResult? Find(DeviceId deviceId, string correlationId) => _inner.Find(deviceId, correlationId);
        public CommandAuditPlan? FindPlan(DeviceId deviceId, string correlationId) => _inner.FindPlan(deviceId, correlationId);
        public int CancelPendingForDevice(DeviceId deviceId, string code = "DEVICE_REVOKED") => _inner.CancelPendingForDevice(deviceId, code);
        public int FailUnfinished(string code) => _inner.FailUnfinished(code);

        public ValueTask<CommandAuditReservation> ReserveAsync(CommandExecutionPlan plan, string requestFingerprint, CancellationToken cancellationToken = default) =>
            _inner.ReserveAsync(plan, requestFingerprint, cancellationToken);

        public async ValueTask RecordAsync(CommandExecutionPlan plan, CommandExecutionResult result, CancellationToken cancellationToken = default)
        {
            if (BeforeRecord is { } hook && hook.Code == result.Code)
            {
                BeforeRecord = null;
                await hook.Action();
            }

            await _inner.RecordAsync(plan, result, cancellationToken);
        }

        public ValueTask<bool> TryCompleteAsync(CommandExecutionPlan plan, CommandExecutionResult result,
            IReadOnlySet<string> fromCodes, CancellationToken cancellationToken = default)
        {
            if (TryCompleteFailures > 0)
            {
                TryCompleteFailures--;
                throw new InvalidOperationException("AUDIT_DOWN");
            }

            return _inner.TryCompleteAsync(plan, result, fromCodes, cancellationToken);
        }
    }
}
