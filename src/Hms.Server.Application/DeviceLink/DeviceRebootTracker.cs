using System.Globalization;
using Hms.Server.Application.Commands;
using Hms.Server.Domain.Devices;
using Microsoft.Extensions.Logging;

namespace Hms.Server.Application.DeviceLink;

/// <summary>
/// Audit codes and timings of <c>system.reboot</c> (MQTT spec §6.1: the device schedules the restart
/// and replies first; §6.4: the relays it drives restart with it). The audit row carries the watch
/// result in <c>Code</c>; no status or column is added for it.
/// </summary>
public static class DeviceRebootCodes
{
    /// <summary>A reboot is already held or watched on this device (real or dry-run).</summary>
    public const string InProgress = "DEVICE_REBOOT_IN_PROGRESS";

    /// <summary>A real reboot was accepted for this device less than a minute ago (the device
    /// throttles at 60 s, spec §3).</summary>
    public const string TooSoon = "DEVICE_REBOOT_TOO_SOON";

    /// <summary>The device accepted the reboot; the server now watches for it coming back.</summary>
    public const string Scheduled = "DEVICE_REBOOT_SCHEDULED";

    /// <summary>An unsigned heartbeat showed an uptime lower than the one before (the signed
    /// <c>system.get_info</c> read is what proves it).</summary>
    public const string Observed = "DEVICE_REBOOT_OBSERVED";

    /// <summary>No lower uptime was heard within the window; the device may still have restarted.</summary>
    public const string NotObserved = "DEVICE_REBOOT_NOT_OBSERVED";

    /// <summary>The watch ended without a result: the device was rebound or revoked, or the server restarted.</summary>
    public const string OutcomeUnknown = "DEVICE_REBOOT_OUTCOME_UNKNOWN";

    /// <summary>The device waits this long before it restarts when no delay is sent (§6.1).</summary>
    public static readonly TimeSpan DeviceDelay = TimeSpan.FromSeconds(2);

    /// <summary>How long after the delay the server listens for the device to come back.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(120);

    /// <summary>Least time between two real reboots of a device.</summary>
    public static readonly TimeSpan MinGap = TimeSpan.FromSeconds(60);

    /// <summary>How long after "not observed" a lower uptime still raises the row.</summary>
    public static readonly TimeSpan Tail = TimeSpan.FromMinutes(10);

    /// <summary>Extra listening time once the server's own broker link is back.</summary>
    public static readonly TimeSpan LinkBackGrace = TimeSpan.FromSeconds(30);

    public static bool IsReboot(string type, string action) =>
        StringComparer.Ordinal.Equals(type, "system") && StringComparer.Ordinal.Equals(action, "reboot");
}

/// <summary>A real reboot the device accepted and replied to; <see cref="UptimeBefore"/> is the last
/// heartbeat uptime heard before the reply.</summary>
public sealed record RebootTracking(CommandExecutionPlan Plan, long? UptimeBefore, DateTimeOffset Deadline);

/// <summary>An audit write the tracker decided on: set <paramref name="Code"/> only while the row's
/// code is still one of <paramref name="FromCodes"/>.</summary>
public sealed record RebootCompletion(
    CommandExecutionPlan Plan,
    string Code,
    IReadOnlySet<string> FromCodes,
    string Detail)
{
    /// <summary>How many times the audit write failed and was handed back.</summary>
    public int Attempt { get; init; }
}

/// <summary>
/// Keeps a device's write lane closed to further reboots from the publish until the reboot is
/// settled, and watches heartbeats for the device coming back. State lives in memory only: a server
/// restart forgets it (the device's own 60 s throttle is the second layer, spec §3).
/// </summary>
public interface IDeviceRebootTracker
{
    /// <summary>Called by the registry under its lane lock for every <c>system.reboot</c>. Null admits
    /// it (a real reboot then holds the device); otherwise the refusal code.</summary>
    string? Admit(CommandExecutionPlan plan, DateTimeOffset now, DateTimeOffset? replyDeadline = null);

    /// <summary>The device refused the command, or it certainly never left: drop its hold. The
    /// one-minute mark stays.</summary>
    void Disarm(DeviceId deviceId, string correlationId);

    /// <summary>The device replied that it will restart: the hold of that command becomes a watch.
    /// Null when that command holds nothing.</summary>
    RebootTracking? Upgrade(CommandExecutionPlan plan, DateTimeOffset repliedAt);

    /// <summary>The device answered "accepted, still running": the hold stays, but the row it may
    /// later be raised on must not say that no reply came.</summary>
    void NoteReplyAccepted(DeviceId deviceId, string correlationId);

    /// <summary>Feed every heartbeat; <paramref name="uptimeSeconds"/> must come from the message.</summary>
    RebootCompletion? ObserveHeartbeat(DeviceId deviceId, long? uptimeSeconds, DateTimeOffset at);

    void ObserveConnectivity(DeviceId deviceId, string state, DateTimeOffset at);

    /// <summary>The device was rebound to another topic or revoked: forget what was heard and end the watch.</summary>
    void Abandon(DeviceId deviceId, string reason);

    /// <summary>Records that ran out of time, abandoned ones and audit writes to retry. While the
    /// server's own broker link is down nothing is concluded.</summary>
    IReadOnlyList<RebootCompletion> TakeDue(DateTimeOffset now, bool brokerConnected);

    /// <summary>The audit write for <paramref name="completion"/> failed: offer it to the next sweep.</summary>
    void Retry(RebootCompletion completion);
}

public sealed class InMemoryDeviceRebootTracker(ILogger<InMemoryDeviceRebootTracker>? logger = null) : IDeviceRebootTracker
{
    private const int MaxAuditAttempts = 3;
    private const int MaxDetailLength = 1024;

    // Rows that carry no device outcome yet: a reply that never came, was too late, was "still
    // running", or whose publish the broker never confirmed. Anything else is a result to keep.
    private static readonly IReadOnlySet<string> UnsettledCodes = new HashSet<string>(StringComparer.Ordinal)
    {
        "MQTT_PUBLISHED", "DEVICE_RESPONSE_TIMEOUT", CommandTransportOutcome.PublishUnconfirmed, "DEVICE_ACCEPTED"
    };

    private static readonly IReadOnlySet<string> FromScheduled = new HashSet<string>(StringComparer.Ordinal) { DeviceRebootCodes.Scheduled };
    private static readonly IReadOnlySet<string> FromNotObserved = new HashSet<string>(StringComparer.Ordinal) { DeviceRebootCodes.NotObserved };

    private enum Phase { Held, Tracking, Tail }

    private sealed record Evidence(long Before, long After, DateTimeOffset At);

    private sealed class Record(CommandExecutionPlan plan, DateTimeOffset acceptedAt)
    {
        public CommandExecutionPlan Plan { get; } = plan;
        public DateTimeOffset AcceptedAt { get; } = acceptedAt;
        public Phase Phase { get; set; } = Phase.Held;
        public DateTimeOffset? RepliedAt { get; set; }

        /// <summary>End of the hold or the watch; for a tail, the end of the tail.</summary>
        public DateTimeOffset Deadline { get; set; } = acceptedAt + DeviceRebootCodes.DeviceDelay + DeviceRebootCodes.Window;

        /// <summary>When the watch itself ended, kept for the tail's <c>late_s</c>.</summary>
        public DateTimeOffset WatchDeadline { get; set; }

        /// <summary>The device answered "accepted, still running" (no result yet).</summary>
        public bool ReplyAccepted { get; set; }

        public bool LinkWasDown { get; set; }
        public bool SawOffline { get; set; }
        public bool OfflineThenOnline { get; set; }
        public Evidence? Evidence { get; set; }
    }

    private sealed class DeviceState
    {
        public long? LastUptime { get; set; }
        public DateTimeOffset? LastRealRebootAt { get; set; }
        public Record? Active { get; set; }
    }

    // One lock for everything: every operation is short and a record ends exactly once, whether
    // the heartbeat path or the sweep gets to it first. The registry calls Admit under its own
    // lane lock, so the order is lane then tracker; nothing here calls back into the registry.
    private readonly object _gate = new();
    private readonly Dictionary<DeviceId, DeviceState> _devices = [];
    private readonly Queue<RebootCompletion> _ready = new();
    private bool _linkWasDown;

    public string? Admit(CommandExecutionPlan plan, DateTimeOffset now, DateTimeOffset? replyDeadline = null)
    {
        if (!DeviceRebootCodes.IsReboot(plan.Request.Type, plan.Request.Action))
            return null;

        lock (_gate)
        {
            var state = StateOf(plan.Request.DeviceId);
            if (state.Active is { Phase: Phase.Held or Phase.Tracking })
                return DeviceRebootCodes.InProgress;
            // A dry-run runs before the device's throttle (spec §4.2): it neither waits for the
            // minute nor starts one.
            if (plan.Request.DryRun)
                return null;
            if (state.LastRealRebootAt is { } last && now - last < DeviceRebootCodes.MinGap)
                return DeviceRebootCodes.TooSoon;

            // The hold also covers the time the server waits for a reply: a reply that arrives
            // inside it must still find the hold (the pending timeout may be set above 120 s).
            state.Active = new Record(plan, now);
            if (replyDeadline is { } wait && wait + DeviceRebootCodes.DeviceDelay > state.Active.Deadline)
                state.Active.Deadline = wait + DeviceRebootCodes.DeviceDelay;
            state.LastRealRebootAt = now;
            return null;
        }
    }

    public void Disarm(DeviceId deviceId, string correlationId)
    {
        lock (_gate)
        {
            if (_devices.TryGetValue(deviceId, out var state) &&
                state.Active is { Phase: Phase.Held } held && IsCommand(held, correlationId))
                state.Active = null;
        }
    }

    public RebootTracking? Upgrade(CommandExecutionPlan plan, DateTimeOffset repliedAt)
    {
        lock (_gate)
        {
            if (!_devices.TryGetValue(plan.Request.DeviceId, out var state) ||
                state.Active is not { Phase: Phase.Held } held || !IsCommand(held, plan.Request.CorrelationId))
                return null;

            held.Phase = Phase.Tracking;
            held.RepliedAt = repliedAt;
            held.Deadline = repliedAt + DeviceRebootCodes.DeviceDelay + DeviceRebootCodes.Window;

            // The drop was heard before the reply got here: the uptime before the reboot is the one
            // from before that drop, and the watch is over at once (the row is raised from SCHEDULED
            // by the next sweep, after the reply has recorded it).
            if (held.Evidence is { } early)
            {
                state.Active = null;
                _ready.Enqueue(new RebootCompletion(held.Plan, DeviceRebootCodes.Observed, FromScheduled,
                    Detail(held, early, reply: true)));
                return new RebootTracking(held.Plan, early.Before, held.Deadline);
            }

            return new RebootTracking(held.Plan, state.LastUptime, held.Deadline);
        }
    }

    public void NoteReplyAccepted(DeviceId deviceId, string correlationId)
    {
        lock (_gate)
        {
            if (_devices.TryGetValue(deviceId, out var state) && state.Active is { Phase: Phase.Held } held &&
                IsCommand(held, correlationId))
                held.ReplyAccepted = true;
        }
    }

    public RebootCompletion? ObserveHeartbeat(DeviceId deviceId, long? uptimeSeconds, DateTimeOffset at)
    {
        // No uptime says nothing: it must not reset the baseline either.
        if (uptimeSeconds is not { } uptime)
            return null;

        lock (_gate)
        {
            var state = StateOf(deviceId);
            var previous = state.LastUptime;
            state.LastUptime = uptime;
            if (previous is not { } before || uptime >= before || state.Active is not { } record || at <= record.AcceptedAt)
                return null;

            switch (record.Phase)
            {
                case Phase.Tracking:
                    state.Active = null;
                    return new RebootCompletion(record.Plan, DeviceRebootCodes.Observed, FromScheduled,
                        Detail(record, new Evidence(before, uptime, at), reply: true));

                case Phase.Held:
                    // Keep the first sign; the row is written when the hold ends, after the
                    // timeout sweep has had its turn.
                    record.Evidence ??= new Evidence(before, uptime, at);
                    return null;

                default:
                    state.Active = null;
                    return new RebootCompletion(record.Plan, DeviceRebootCodes.Observed, FromNotObserved,
                        Detail(record, new Evidence(before, uptime, at), reply: true, lateBy: at - record.WatchDeadline));
            }
        }
    }

    public void ObserveConnectivity(DeviceId deviceId, string state, DateTimeOffset at)
    {
        lock (_gate)
        {
            if (!_devices.TryGetValue(deviceId, out var device) || device.Active is not { } record || at < record.AcceptedAt)
                return;
            if (StringComparer.Ordinal.Equals(state, "offline"))
                record.SawOffline = true;
            else if (StringComparer.Ordinal.Equals(state, "online") && record.SawOffline)
                record.OfflineThenOnline = true;
        }
    }

    public void Abandon(DeviceId deviceId, string reason)
    {
        lock (_gate)
        {
            if (!_devices.TryGetValue(deviceId, out var state))
                return;

            // Another physical device may sit behind this id from now on.
            state.LastUptime = null;
            if (state.Active is not { } record)
                return;
            state.Active = null;
            if (record.Phase == Phase.Tracking)
            {
                _ready.Enqueue(new RebootCompletion(record.Plan, DeviceRebootCodes.OutcomeUnknown, FromScheduled,
                    Truncate($"abandoned={reason}")));
            }
        }
    }

    public IReadOnlyList<RebootCompletion> TakeDue(DateTimeOffset now, bool brokerConnected)
    {
        lock (_gate)
        {
            var due = new List<RebootCompletion>();
            while (_ready.TryDequeue(out var queued))
                due.Add(queued);

            // While the server's own link is down it hears no heartbeat, whatever the device does.
            if (!brokerConnected)
            {
                _linkWasDown = true;
                return due;
            }

            if (_linkWasDown)
            {
                _linkWasDown = false;
                foreach (var record in _devices.Values.Select(x => x.Active).OfType<Record>().Where(x => x.Phase != Phase.Tail))
                {
                    record.LinkWasDown = true;
                    var floor = now + DeviceRebootCodes.LinkBackGrace;
                    if (record.Deadline < floor)
                        record.Deadline = floor;
                }
            }

            foreach (var state in _devices.Values)
            {
                if (state.Active is not { } record || now < record.Deadline)
                    continue;

                switch (record.Phase)
                {
                    case Phase.Held:
                        state.Active = null;
                        if (record.Evidence is { } evidence)
                        {
                            due.Add(new RebootCompletion(record.Plan, DeviceRebootCodes.Observed, UnsettledCodes,
                                Detail(record, evidence, reply: false)));
                        }
                        break;

                    case Phase.Tracking:
                        due.Add(new RebootCompletion(record.Plan, DeviceRebootCodes.NotObserved, FromScheduled,
                            Detail(record, null, reply: true)));
                        record.WatchDeadline = record.Deadline;
                        record.Phase = Phase.Tail;
                        record.Deadline = now + DeviceRebootCodes.Tail;
                        break;

                    default:
                        state.Active = null;
                        break;
                }
            }

            return due;
        }
    }

    public void Retry(RebootCompletion completion)
    {
        lock (_gate)
        {
            var next = completion with { Attempt = completion.Attempt + 1 };
            if (next.Attempt > MaxAuditAttempts)
            {
                logger?.LogError("Audit write of reboot {CorrelationId} ({Code}) failed {Attempts} times; giving up",
                    completion.Plan.Request.CorrelationId, completion.Code, completion.Attempt);
                return;
            }

            _ready.Enqueue(next);
        }
    }

    private DeviceState StateOf(DeviceId deviceId)
    {
        if (!_devices.TryGetValue(deviceId, out var state))
            _devices[deviceId] = state = new DeviceState();
        return state;
    }

    private static bool IsCommand(Record record, string correlationId) =>
        StringComparer.Ordinal.Equals(record.Plan.Request.CorrelationId, correlationId);

    // Only fields that have a value, so a reader can tell what was and was not heard.
    private static string Detail(Record record, Evidence? evidence, bool reply, TimeSpan? lateBy = null)
    {
        var parts = new List<string>();
        if (evidence is not null)
        {
            parts.Add("evidence=heartbeat");
            parts.Add($"uptime_before={evidence.Before.ToString(CultureInfo.InvariantCulture)}");
            parts.Add($"uptime_after={evidence.After.ToString(CultureInfo.InvariantCulture)}");
            if (lateBy is null)
            {
                var since = evidence.At - (record.RepliedAt ?? record.AcceptedAt);
                parts.Add($"after_s={Math.Max(0, (long)Math.Round(since.TotalSeconds)).ToString(CultureInfo.InvariantCulture)}");
            }
        }

        if (record.OfflineThenOnline)
            parts.Add("connectivity=offline>online");
        if (!reply)
            parts.Add(record.ReplyAccepted ? "reply=accepted" : "reply=none");
        if (lateBy is { } late)
            parts.Add($"late_s={Math.Max(0, (long)Math.Round(late.TotalSeconds)).ToString(CultureInfo.InvariantCulture)}");
        if (record.LinkWasDown)
            parts.Add("link_down=true");
        if (parts.Count == 0)
            parts.Add("evidence=none");
        return Truncate(string.Join("; ", parts));
    }

    private static string Truncate(string value) =>
        value.Length <= MaxDetailLength ? value : value[..MaxDetailLength];
}
