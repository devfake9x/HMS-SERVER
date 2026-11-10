using Hms.Server.Application.Commands;
using Hms.Server.Application.DeviceLink;
using Hms.Server.Domain.Authorization;
using Hms.Server.Domain.Commands;
using Hms.Server.Domain.Devices;

namespace Hms.Server.UnitTests;

/// <summary>The in-memory record of a reboot the server sent: it holds the lane from the publish,
/// then watches heartbeats for the device coming back, and ends each record exactly once.</summary>
public sealed class DeviceRebootTrackerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 10, 8, 0, 0, TimeSpan.Zero);

    private static readonly CommandDefinition Reboot = new("system", "reboot", "sys.reboot", CommandAvailability.Enabled,
        true, false, 60_000, CommandClass.Flash, ConfirmTemplate: "REBOOT-<DEVID>", DryRunSupported: true);

    private readonly InMemoryDeviceRebootTracker _tracker = new();
    private readonly DeviceId _device = DeviceId.New();

    private CommandExecutionPlan Plan(bool dryRun = false, DeviceId? device = null)
    {
        var request = new DeviceCommandRequest(device ?? _device, new UserId(Guid.NewGuid()), "system", "reboot",
            new Dictionary<string, object?>(), Guid.NewGuid().ToString("N"), T0, DryRun: dryRun);
        return new CommandExecutionPlan(request, Reboot, 1, T0.AddSeconds(10));
    }

    /// <summary>A real reboot admitted at <paramref name="at"/> and answered one second later.</summary>
    private CommandExecutionPlan Tracked(DateTimeOffset at)
    {
        var plan = Plan();
        Assert.Null(_tracker.Admit(plan, at));
        Assert.NotNull(_tracker.Upgrade(plan, at.AddSeconds(1)));
        return plan;
    }

    [Fact]
    public void A_real_reboot_holds_the_gate_for_every_later_reboot_real_or_dry_run()
    {
        Assert.Null(_tracker.Admit(Plan(), T0));

        Assert.Equal(DeviceRebootCodes.InProgress, _tracker.Admit(Plan(), T0.AddSeconds(1)));
        Assert.Equal(DeviceRebootCodes.InProgress, _tracker.Admit(Plan(dryRun: true), T0.AddSeconds(1)));
        // Another device is not held.
        Assert.Null(_tracker.Admit(Plan(device: DeviceId.New()), T0.AddSeconds(1)));
    }

    [Fact]
    public void A_dry_run_on_an_idle_device_is_admitted_and_holds_nothing()
    {
        Assert.Null(_tracker.Admit(Plan(dryRun: true), T0));

        Assert.Null(_tracker.Admit(Plan(dryRun: true), T0.AddSeconds(1)));
        // The device runs its preview before its throttle (spec §4.2), so a dry-run leaves no 60 s mark.
        Assert.Null(_tracker.Admit(Plan(), T0.AddSeconds(2)));
    }

    [Fact]
    public void Two_real_reboots_are_a_minute_apart_whatever_became_of_the_first()
    {
        var first = Plan();
        Assert.Null(_tracker.Admit(first, T0));
        _tracker.Disarm(_device, first.Request.CorrelationId);

        Assert.Equal(DeviceRebootCodes.TooSoon, _tracker.Admit(Plan(), T0.AddSeconds(59)));
        Assert.Null(_tracker.Admit(Plan(dryRun: true), T0.AddSeconds(59)));
        Assert.Null(_tracker.Admit(Plan(), T0.AddSeconds(60)));
    }

    [Fact]
    public void A_refused_reboot_does_not_move_the_one_minute_mark()
    {
        var first = Plan();
        Assert.Null(_tracker.Admit(first, T0));
        _tracker.Disarm(_device, first.Request.CorrelationId);
        Assert.Equal(DeviceRebootCodes.TooSoon, _tracker.Admit(Plan(), T0.AddSeconds(50)));

        Assert.Null(_tracker.Admit(Plan(), T0.AddSeconds(61)));
    }

    [Fact]
    public void Disarm_frees_only_the_hold_of_that_exact_command()
    {
        var plan = Plan();
        Assert.Null(_tracker.Admit(plan, T0));

        _tracker.Disarm(_device, "someone-else");
        Assert.Equal(DeviceRebootCodes.InProgress, _tracker.Admit(Plan(dryRun: true), T0.AddSeconds(1)));

        _tracker.Disarm(_device, plan.Request.CorrelationId);
        Assert.Null(_tracker.Admit(Plan(dryRun: true), T0.AddSeconds(2)));
    }

    [Fact]
    public void A_reply_turns_the_hold_into_tracking_with_the_uptime_seen_before()
    {
        Assert.Null(_tracker.ObserveHeartbeat(_device, 500, T0.AddSeconds(-5)));
        var plan = Plan();
        Assert.Null(_tracker.Admit(plan, T0));

        var tracking = _tracker.Upgrade(plan, T0.AddSeconds(1));

        Assert.NotNull(tracking);
        Assert.Equal(500, tracking.UptimeBefore);
        Assert.Equal(T0.AddSeconds(1) + DeviceRebootCodes.DeviceDelay + DeviceRebootCodes.Window, tracking.Deadline);
        // Tracking keeps the gate closed past the one-minute mark.
        Assert.Equal(DeviceRebootCodes.InProgress, _tracker.Admit(Plan(), T0.AddSeconds(90)));
    }

    [Fact]
    public void Upgrade_needs_the_hold_of_that_exact_command()
    {
        var plan = Plan();
        Assert.Null(_tracker.Upgrade(plan, T0));

        Assert.Null(_tracker.Admit(plan, T0));
        Assert.Null(_tracker.Upgrade(Plan(), T0.AddSeconds(1)));
        Assert.NotNull(_tracker.Upgrade(plan, T0.AddSeconds(1)));
        // Already tracking: a second reply for it does not start another watch.
        Assert.Null(_tracker.Upgrade(plan, T0.AddSeconds(2)));
    }

    [Fact]
    public void A_heartbeat_with_a_lower_uptime_after_the_mark_ends_tracking_as_observed()
    {
        _tracker.ObserveHeartbeat(_device, 500, T0.AddSeconds(-5));
        var plan = Tracked(T0);

        Assert.Null(_tracker.ObserveHeartbeat(_device, 503, T0.AddSeconds(3)));
        var done = _tracker.ObserveHeartbeat(_device, 4, T0.AddSeconds(20));

        Assert.NotNull(done);
        Assert.Equal(DeviceRebootCodes.Observed, done.Code);
        Assert.Equal([DeviceRebootCodes.Scheduled], done.FromCodes);
        Assert.Equal(plan.Request.CorrelationId, done.Plan.Request.CorrelationId);
        Assert.Contains("evidence=heartbeat", done.Detail);
        Assert.Contains("uptime_before=503", done.Detail);
        Assert.Contains("uptime_after=4", done.Detail);
        Assert.Contains("after_s=19", done.Detail);
        // Ended once: the next heartbeat finds nothing to end, and the gate is open again.
        Assert.Null(_tracker.ObserveHeartbeat(_device, 2, T0.AddSeconds(30)));
        Assert.Null(_tracker.Admit(Plan(dryRun: true), T0.AddSeconds(31)));
    }

    [Fact]
    public void Heartbeats_that_prove_nothing_end_nothing()
    {
        _tracker.ObserveHeartbeat(_device, 500, T0.AddSeconds(-10));
        // Lower, but sent before the reboot was admitted: not evidence of it.
        Assert.Null(_tracker.ObserveHeartbeat(_device, 4, T0.AddSeconds(-5)));
        Tracked(T0);

        Assert.Null(_tracker.ObserveHeartbeat(_device, 6, T0.AddSeconds(10)));
        Assert.Null(_tracker.ObserveHeartbeat(_device, null, T0.AddSeconds(11)));
        Assert.Null(_tracker.ObserveHeartbeat(_device, 8, T0.AddSeconds(12)));
        // The missing uptime did not reset the baseline: 8 -> 7 is still a drop.
        Assert.NotNull(_tracker.ObserveHeartbeat(_device, 7, T0.AddSeconds(13)));
    }

    [Fact]
    public void A_lower_uptime_stamped_before_the_reboot_was_admitted_is_not_evidence_even_while_tracking()
    {
        _tracker.ObserveHeartbeat(_device, 500, T0.AddSeconds(-5));
        Tracked(T0);

        // A heartbeat from an inbound backlog: received before the mark, processed after the reply.
        Assert.Null(_tracker.ObserveHeartbeat(_device, 4, T0.AddSeconds(-1)));
        Assert.Empty(_tracker.TakeDue(T0.AddSeconds(2), brokerConnected: true));
    }

    [Fact]
    public void The_hold_lasts_until_the_reply_deadline_has_passed_when_that_is_later()
    {
        var plan = Plan();
        Assert.Null(_tracker.Admit(plan, T0, T0.AddSeconds(180)));

        Assert.Empty(_tracker.TakeDue(T0.AddSeconds(150), brokerConnected: true));
        Assert.Equal(DeviceRebootCodes.InProgress, _tracker.Admit(Plan(dryRun: true), T0.AddSeconds(150)));
        Assert.NotNull(_tracker.Upgrade(plan, T0.AddSeconds(150)));
    }

    [Fact]
    public void The_first_heartbeat_ever_only_sets_the_baseline()
    {
        Tracked(T0);

        Assert.Null(_tracker.ObserveHeartbeat(_device, 4, T0.AddSeconds(10)));
    }

    [Fact]
    public void A_drop_seen_while_only_held_is_kept_until_the_hold_ends()
    {
        _tracker.ObserveHeartbeat(_device, 500, T0.AddSeconds(-5));
        Assert.Null(_tracker.Admit(Plan(), T0));

        Assert.Null(_tracker.ObserveHeartbeat(_device, 4, T0.AddSeconds(10)));
        Assert.Empty(_tracker.TakeDue(T0.AddSeconds(121), brokerConnected: true));
        var done = Assert.Single(_tracker.TakeDue(T0.AddSeconds(123), brokerConnected: true));

        Assert.Equal(DeviceRebootCodes.Observed, done.Code);
        Assert.Contains("reply=none", done.Detail);
        Assert.Contains("evidence=heartbeat", done.Detail);
        // Only a row with no device outcome is raised: never a rejection or a success.
        Assert.Equivalent(new[]
        {
            "MQTT_PUBLISHED", "DEVICE_RESPONSE_TIMEOUT", "MQTT_PUBLISH_UNCONFIRMED", "DEVICE_ACCEPTED"
        }, done.FromCodes);
        Assert.Null(_tracker.Admit(Plan(dryRun: true), T0.AddSeconds(124)));
    }

    [Fact]
    public void A_hold_that_ends_without_evidence_writes_nothing_and_frees_the_gate()
    {
        Assert.Null(_tracker.Admit(Plan(), T0));

        Assert.Empty(_tracker.TakeDue(T0.AddSeconds(123), brokerConnected: true));

        Assert.Null(_tracker.Admit(Plan(dryRun: true), T0.AddSeconds(124)));
        Assert.Null(_tracker.Admit(Plan(), T0.AddSeconds(124)));
    }

    [Fact]
    public void A_hold_keeps_the_gate_closed_until_its_end_with_or_without_a_reply()
    {
        _tracker.ObserveHeartbeat(_device, 500, T0.AddSeconds(-5));
        Assert.Null(_tracker.Admit(Plan(), T0));

        Assert.Equal(DeviceRebootCodes.InProgress, _tracker.Admit(Plan(dryRun: true), T0.AddSeconds(100)));
        Assert.Empty(_tracker.TakeDue(T0.AddSeconds(100), brokerConnected: true));
        Assert.Equal(DeviceRebootCodes.InProgress, _tracker.Admit(Plan(dryRun: true), T0.AddSeconds(100)));
    }

    [Fact]
    public void Tracking_that_sees_no_drop_ends_as_not_observed_and_a_late_drop_raises_it()
    {
        _tracker.ObserveHeartbeat(_device, 500, T0.AddSeconds(-5));
        var plan = Tracked(T0);

        Assert.Empty(_tracker.TakeDue(T0.AddSeconds(122), brokerConnected: true));
        var ended = Assert.Single(_tracker.TakeDue(T0.AddSeconds(124), brokerConnected: true));
        Assert.Equal(DeviceRebootCodes.NotObserved, ended.Code);
        Assert.Equal([DeviceRebootCodes.Scheduled], ended.FromCodes);
        Assert.Equal(plan.Request.CorrelationId, ended.Plan.Request.CorrelationId);
        // Not observed does not keep the gate closed.
        Assert.Null(_tracker.Admit(Plan(dryRun: true), T0.AddSeconds(125)));

        var late = _tracker.ObserveHeartbeat(_device, 10, T0.AddSeconds(130));

        Assert.NotNull(late);
        Assert.Equal(DeviceRebootCodes.Observed, late.Code);
        Assert.Equal([DeviceRebootCodes.NotObserved], late.FromCodes);
        Assert.Contains("late_s=7", late.Detail);
        Assert.Null(_tracker.ObserveHeartbeat(_device, 3, T0.AddSeconds(140)));
    }

    [Fact]
    public void The_late_tail_ends_ten_minutes_after_not_observed()
    {
        _tracker.ObserveHeartbeat(_device, 500, T0.AddSeconds(-5));
        Tracked(T0);
        Assert.Single(_tracker.TakeDue(T0.AddSeconds(124), brokerConnected: true));

        Assert.Empty(_tracker.TakeDue(T0.AddSeconds(124 + 601), brokerConnected: true));

        Assert.Null(_tracker.ObserveHeartbeat(_device, 10, T0.AddSeconds(124 + 602)));
    }

    [Fact]
    public void While_the_servers_own_broker_link_is_down_nothing_is_concluded()
    {
        _tracker.ObserveHeartbeat(_device, 500, T0.AddSeconds(-5));
        Tracked(T0);

        Assert.Empty(_tracker.TakeDue(T0.AddSeconds(124), brokerConnected: false));
        // The link is back: the device gets at least 30 s more to be heard.
        Assert.Empty(_tracker.TakeDue(T0.AddSeconds(125), brokerConnected: true));
        Assert.Empty(_tracker.TakeDue(T0.AddSeconds(154), brokerConnected: true));
        var ended = Assert.Single(_tracker.TakeDue(T0.AddSeconds(156), brokerConnected: true));

        Assert.Equal(DeviceRebootCodes.NotObserved, ended.Code);
        Assert.Contains("link_down=true", ended.Detail);
    }

    [Fact]
    public void A_link_that_never_dropped_adds_no_grace()
    {
        Tracked(T0);

        var ended = Assert.Single(_tracker.TakeDue(T0.AddSeconds(124), brokerConnected: true));

        Assert.DoesNotContain("link_down", ended.Detail);
    }

    [Fact]
    public void Abandoning_a_tracked_reboot_ends_it_as_outcome_unknown_and_forgets_the_uptime()
    {
        _tracker.ObserveHeartbeat(_device, 500, T0.AddSeconds(-5));
        var plan = Tracked(T0);

        _tracker.Abandon(_device, "rebind");

        var ended = Assert.Single(_tracker.TakeDue(T0.AddSeconds(5), brokerConnected: true));
        Assert.Equal(DeviceRebootCodes.OutcomeUnknown, ended.Code);
        Assert.Equal([DeviceRebootCodes.Scheduled], ended.FromCodes);
        Assert.Equal(plan.Request.CorrelationId, ended.Plan.Request.CorrelationId);
        Assert.Contains("abandoned=rebind", ended.Detail);
        Assert.Empty(_tracker.TakeDue(T0.AddSeconds(6), brokerConnected: true));
        // Another physical device may sit behind the id now: the old uptime is not a baseline.
        var next = Plan();
        Assert.Null(_tracker.Admit(next, T0.AddSeconds(70)));
        Assert.Null(_tracker.Upgrade(next, T0.AddSeconds(71))!.UptimeBefore);
    }

    [Fact]
    public void Abandoning_a_hold_frees_the_gate_without_an_audit_write()
    {
        Assert.Null(_tracker.Admit(Plan(), T0));

        _tracker.Abandon(_device, "revoked");

        Assert.Empty(_tracker.TakeDue(T0.AddSeconds(1), brokerConnected: true));
        Assert.Null(_tracker.Admit(Plan(dryRun: true), T0.AddSeconds(2)));
    }

    [Fact]
    public void A_completion_whose_audit_write_failed_comes_back_up_to_three_times()
    {
        _tracker.ObserveHeartbeat(_device, 500, T0.AddSeconds(-5));
        Tracked(T0);
        var done = _tracker.ObserveHeartbeat(_device, 4, T0.AddSeconds(10))!;

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            _tracker.Retry(done);
            var again = Assert.Single(_tracker.TakeDue(T0.AddSeconds(11 + attempt), brokerConnected: true));
            Assert.Equal(done.Plan.Request.CorrelationId, again.Plan.Request.CorrelationId);
            done = again;
        }

        _tracker.Retry(done);
        Assert.Empty(_tracker.TakeDue(T0.AddSeconds(20), brokerConnected: true));
    }

    [Fact]
    public void Connectivity_offline_then_online_after_the_mark_is_noted_in_the_detail_only()
    {
        _tracker.ObserveHeartbeat(_device, 500, T0.AddSeconds(-5));
        Tracked(T0);
        _tracker.ObserveConnectivity(_device, "offline", T0.AddSeconds(4));
        _tracker.ObserveConnectivity(_device, "online", T0.AddSeconds(18));

        // On its own it ends nothing.
        Assert.Empty(_tracker.TakeDue(T0.AddSeconds(30), brokerConnected: true));
        var done = _tracker.ObserveHeartbeat(_device, 4, T0.AddSeconds(20));

        Assert.NotNull(done);
        Assert.Contains("connectivity=offline>online", done.Detail);
    }

    [Fact]
    public void An_online_message_with_no_offline_after_the_mark_is_not_a_reboot_sign()
    {
        _tracker.ObserveHeartbeat(_device, 500, T0.AddSeconds(-5));
        Tracked(T0);
        // An offline from before the mark, then a retained "online" replayed after a subscribe.
        _tracker.ObserveConnectivity(_device, "offline", T0.AddSeconds(-3));
        _tracker.ObserveConnectivity(_device, "online", T0.AddSeconds(5));

        var done = _tracker.ObserveHeartbeat(_device, 4, T0.AddSeconds(20));

        Assert.NotNull(done);
        Assert.DoesNotContain("connectivity=", done.Detail);
    }

    [Fact]
    public void A_record_ends_exactly_once_when_a_heartbeat_and_the_sweep_race()
    {
        var multiple = 0;
        var none = 0;
        Parallel.For(0, 1000, new ParallelOptions { MaxDegreeOfParallelism = 8 }, _ =>
        {
            var tracker = new InMemoryDeviceRebootTracker();
            var device = DeviceId.New();
            tracker.ObserveHeartbeat(device, 500, T0.AddSeconds(-5));
            var plan = Plan(device: device);
            tracker.Admit(plan, T0);
            tracker.Upgrade(plan, T0.AddSeconds(1));
            using var start = new ManualResetEventSlim();
            RebootCompletion? fromHeartbeat = null;
            IReadOnlyList<RebootCompletion> fromSweep = [];
            var heartbeat = Task.Run(() => { start.Wait(); fromHeartbeat = tracker.ObserveHeartbeat(device, 4, T0.AddSeconds(122)); });
            var sweep = Task.Run(() => { start.Wait(); fromSweep = tracker.TakeDue(T0.AddSeconds(124), brokerConnected: true); });
            start.Set();
            Task.WaitAll(heartbeat, sweep);

            // Whichever won, the Scheduled row is ended once: Observed, or NotObserved (which a
            // heartbeat that came after may only raise from the NotObserved code, never from Scheduled).
            var endsScheduled = fromSweep.Concat(fromHeartbeat is null ? [] : [fromHeartbeat])
                .Count(x => x.FromCodes.Contains(DeviceRebootCodes.Scheduled));
            if (endsScheduled > 1) Interlocked.Increment(ref multiple);
            if (endsScheduled == 0) Interlocked.Increment(ref none);
        });

        Assert.Equal(0, multiple);
        Assert.Equal(0, none);
    }

    [Theory]
    [InlineData("system", "reboot", true)]
    [InlineData("system", "factory_reset", false)]
    [InlineData("control", "set_output", false)]
    public void Only_system_reboot_is_a_reboot(string type, string action, bool expected)
    {
        Assert.Equal(expected, DeviceRebootCodes.IsReboot(type, action));
    }
}
