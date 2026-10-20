using System.Collections.Concurrent;
using System.Security.Claims;
using Hms.Server.Domain.Devices;

namespace Hms.Server.Application.Realtime;

public sealed record AuthorizedDeviceEvent(
    DeviceId DeviceId,
    long AuthorizationEpoch,
    long PolicyVersion,
    string EventType,
    IReadOnlyDictionary<string, object?> Payload);

/// <summary>Authorization context attached to a forced SignalR membership removal.</summary>
public sealed record DeviceRevokedNotification(
    Guid DeviceId,
    long AuthorizationEpoch,
    long PolicyVersion,
    string Reason);

public interface IAuthorizationEpochStore
{
    long Current(DeviceId deviceId);
    long Advance(DeviceId deviceId);

    // One call for all devices a role change touches; each distinct device moves by one.
    void AdvanceMany(IReadOnlyCollection<DeviceId> deviceIds);
}

public interface IAuthorizedEventPublisher
{
    ValueTask PublishAsync(AuthorizedDeviceEvent deviceEvent, CancellationToken cancellationToken = default);
    IReadOnlyList<AuthorizedDeviceEvent> SnapshotForDelivery(IAuthorizationEpochStore epochs);
    IReadOnlyList<AuthorizedDeviceEvent> DrainForDelivery(IAuthorizationEpochStore epochs);
    void RequeueForDelivery(IEnumerable<AuthorizedDeviceEvent> events, IAuthorizationEpochStore epochs);
}

public interface IRealtimePrincipalRefresher
{
    ValueTask<ClaimsPrincipal?> RefreshAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default);
}

public static class RealtimeEventFence
{
    public static bool IsCurrent(
        AuthorizedDeviceEvent deviceEvent,
        IAuthorizationEpochStore epochs,
        long currentPolicyVersion) =>
        epochs.Current(deviceEvent.DeviceId) == deviceEvent.AuthorizationEpoch &&
        deviceEvent.PolicyVersion == currentPolicyVersion;
}

public sealed class InMemoryAuthorizationEpochStore : IAuthorizationEpochStore
{
    private readonly ConcurrentDictionary<DeviceId, long> _epochs = [];

    public long Current(DeviceId deviceId) => _epochs.GetValueOrDefault(deviceId, 0);

    public long Advance(DeviceId deviceId) => _epochs.AddOrUpdate(deviceId, 1, (_, current) => checked(current + 1));

    public void AdvanceMany(IReadOnlyCollection<DeviceId> deviceIds)
    {
        foreach (var deviceId in deviceIds.Distinct())
            Advance(deviceId);
    }
}

public sealed class BoundedDeviceEventPublisher(int capacity = 256) : IAuthorizedEventPublisher
{
    private readonly int _capacity = capacity > 0
        ? capacity
        : throw new ArgumentOutOfRangeException(nameof(capacity));
    private readonly object _gate = new();
    private readonly Dictionary<(DeviceId DeviceId, string EventType), AuthorizedDeviceEvent> _latest = [];
    private readonly Queue<EventSlot> _order = [];
    private int _dropped;

    private sealed record EventSlot(DeviceId DeviceId, string? CoalescedType, AuthorizedDeviceEvent? Event);

    public int DroppedCount => Volatile.Read(ref _dropped);

    public ValueTask PublishAsync(AuthorizedDeviceEvent deviceEvent, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var coalescedType = IsCoalescible(deviceEvent.EventType) ? deviceEvent.EventType : null;
            if (coalescedType is not null && _latest.ContainsKey((deviceEvent.DeviceId, coalescedType)))
            {
                _latest[(deviceEvent.DeviceId, coalescedType)] = deviceEvent;
                return ValueTask.CompletedTask;
            }

            while (_order.Count >= _capacity)
            {
                // Preserve security/lifecycle/command events when the queue is
                // full. Coalescible status/capability updates may be evicted.
                var evictIndex = _order
                    .Select((slot, index) => (slot, index))
                    .FirstOrDefault(x => x.slot.CoalescedType is not null).index;
                var evictable = _order.ElementAtOrDefault(evictIndex);
                if (evictable is null || evictable.CoalescedType is null)
                {
                    if (coalescedType is not null)
                    {
                        // Dropping latest state is safe; the next snapshot can
                        // rebuild it without losing a security transition.
                        Interlocked.Increment(ref _dropped);
                        return ValueTask.CompletedTask;
                    }

                    // Never silently discard a command, revoke, license, or
                    // provisioning event. The caller must apply its explicit
                    // backpressure policy (retry/resync/reject).
                    throw new InvalidOperationException("REALTIME_QUEUE_FULL");
                }

                var evicted = evictable;
                var remaining = _order.Where((_, index) => index != evictIndex).ToArray();
                _order.Clear();
                foreach (var slot in remaining) _order.Enqueue(slot);
                if (evicted.CoalescedType is not null)
                    _latest.Remove((evicted.DeviceId, evicted.CoalescedType));
                Interlocked.Increment(ref _dropped);
            }

            if (coalescedType is not null)
                _latest[(deviceEvent.DeviceId, coalescedType)] = deviceEvent;
            _order.Enqueue(new EventSlot(deviceEvent.DeviceId, coalescedType, coalescedType is null ? deviceEvent : null));
        }

        return ValueTask.CompletedTask;
    }

    public IReadOnlyList<AuthorizedDeviceEvent> Snapshot()
    {
        lock (_gate)
        {
            return _order.Select(Resolve).ToArray();
        }
    }

    public IReadOnlyList<AuthorizedDeviceEvent> SnapshotForDelivery(IAuthorizationEpochStore epochs) =>
        Snapshot()
            .Where(x => epochs.Current(x.DeviceId) == x.AuthorizationEpoch)
            .ToArray();

    public IReadOnlyList<AuthorizedDeviceEvent> DrainForDelivery(IAuthorizationEpochStore epochs)
    {
        lock (_gate)
        {
            var current = _order
                .Select(Resolve)
                .Where(x => epochs.Current(x.DeviceId) == x.AuthorizationEpoch)
                .ToArray();
            _latest.Clear();
            _order.Clear();
            return current;
        }
    }

    public void RequeueForDelivery(IEnumerable<AuthorizedDeviceEvent> events, IAuthorizationEpochStore epochs)
    {
        foreach (var deviceEvent in events)
        {
            if (epochs.Current(deviceEvent.DeviceId) != deviceEvent.AuthorizationEpoch)
                continue;
            PublishAsync(deviceEvent).GetAwaiter().GetResult();
        }
    }

    private AuthorizedDeviceEvent Resolve(EventSlot slot) =>
        slot.CoalescedType is null
            ? slot.Event!
            : _latest[(slot.DeviceId, slot.CoalescedType)];

    private static bool IsCoalescible(string eventType) =>
        eventType.Equals("status", StringComparison.OrdinalIgnoreCase) ||
        eventType.Equals("capabilities", StringComparison.OrdinalIgnoreCase);
}
