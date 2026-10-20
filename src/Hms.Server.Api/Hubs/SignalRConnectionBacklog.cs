using Hms.Server.Application.Realtime;
using Hms.Server.Domain.Devices;

namespace Hms.Server.Api.Hubs;

/// <summary>
/// Keeps delivery state per SignalR connection so a slow or failed client does
/// not cause an already-delivered event to be replayed to other clients.
/// </summary>
public sealed class SignalRConnectionBacklog
{
    public sealed record Pending(AuthorizedDeviceEvent Event, int Attempts);

    private readonly int _capacity;
    private readonly object _gate = new();
    private readonly Dictionary<string, LinkedList<Pending>> _queues = [];
    private readonly Dictionary<string, HashSet<DeviceId>> _resyncRequired = [];

    public SignalRConnectionBacklog(int capacity = 64)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public int DroppedCoalescible { get; private set; }
    public int RetryCount { get; private set; }
    public int ResynchronizationCount { get; private set; }

    public bool TryEnqueue(string connectionId, AuthorizedDeviceEvent deviceEvent)
    {
        lock (_gate)
        {
            if (!_queues.TryGetValue(connectionId, out var queue))
            {
                queue = [];
                _queues[connectionId] = queue;
            }

            if (IsCoalescible(deviceEvent.EventType))
            {
                var existing = queue.First;
                while (existing is not null)
                {
                    if (existing.Value.Event.DeviceId == deviceEvent.DeviceId &&
                        existing.Value.Event.EventType.Equals(deviceEvent.EventType, StringComparison.OrdinalIgnoreCase))
                    {
                        existing.Value = new Pending(deviceEvent, existing.Value.Attempts);
                        return true;
                    }
                    existing = existing.Next;
                }
            }

            while (queue.Count >= _capacity)
            {
                var evictable = queue.First;
                while (evictable is not null && !IsCoalescible(evictable.Value.Event.EventType))
                    evictable = evictable.Next;

                if (evictable is null)
                {
                    if (IsCoalescible(deviceEvent.EventType))
                    {
                        DroppedCoalescible++;
                        SignalRBacklogMetrics.RecordDropped();
                        return true;
                    }

                    RequireResynchronization(connectionId, deviceEvent.DeviceId);
                    ResynchronizationCount++;
                    SignalRBacklogMetrics.RecordResynchronization();
                    return false;
                }

                queue.Remove(evictable);
                DroppedCoalescible++;
                SignalRBacklogMetrics.RecordDropped();
            }

            queue.AddLast(new Pending(deviceEvent, 0));
            return true;
        }
    }

    public IReadOnlyList<string> Connections
    {
        get
        {
            lock (_gate) return _queues.Keys.ToArray();
        }
    }

    public bool TryPeek(string connectionId, out Pending? pending)
    {
        lock (_gate)
        {
            if (_queues.TryGetValue(connectionId, out var queue) && queue.First is not null)
            {
                pending = queue.First.Value;
                return true;
            }
            pending = null;
            return false;
        }
    }

    public void MarkDelivered(string connectionId)
    {
        lock (_gate)
        {
            if (!_queues.TryGetValue(connectionId, out var queue) || queue.First is null) return;
            queue.RemoveFirst();
            if (queue.Count == 0) _queues.Remove(connectionId);
        }
    }

    public bool MarkRetry(string connectionId, int maxAttempts)
    {
        lock (_gate)
        {
            if (!_queues.TryGetValue(connectionId, out var queue) || queue.First is null) return false;
            var current = queue.First.Value;
            var attempts = current.Attempts + 1;
            RetryCount++;
            SignalRBacklogMetrics.RecordRetry();
            if (attempts >= maxAttempts)
            {
                RequireResynchronization(connectionId, current.Event.DeviceId);
                ResynchronizationCount++;
                SignalRBacklogMetrics.RecordResynchronization();
                return false;
            }

            queue.First.Value = current with { Attempts = attempts };
            return true;
        }
    }

    public bool RequiresResynchronization(string connectionId)
    {
        lock (_gate) return _resyncRequired.TryGetValue(connectionId, out var devices) && devices.Count > 0;
    }

    public bool RequiresResynchronization(string connectionId, DeviceId deviceId)
    {
        lock (_gate) return _resyncRequired.TryGetValue(connectionId, out var devices) && devices.Contains(deviceId);
    }

    public void Remove(string connectionId)
    {
        lock (_gate)
        {
            _queues.Remove(connectionId);
            _resyncRequired.Remove(connectionId);
        }
    }

    public void RemoveDevice(string connectionId, DeviceId deviceId)
    {
        lock (_gate)
        {
            if (_queues.TryGetValue(connectionId, out var queue))
            {
                var current = queue.First;
                while (current is not null)
                {
                    var next = current.Next;
                    if (current.Value.Event.DeviceId == deviceId) queue.Remove(current);
                    current = next;
                }
                if (queue.Count == 0)
                {
                    _queues.Remove(connectionId);
                }
            }

            if (_resyncRequired.TryGetValue(connectionId, out var devices))
            {
                devices.Remove(deviceId);
                if (devices.Count == 0) _resyncRequired.Remove(connectionId);
            }
        }
    }

    public IReadOnlyList<Pending> Snapshot(string connectionId)
    {
        lock (_gate)
            return _queues.TryGetValue(connectionId, out var queue) ? queue.ToArray() : [];
    }

    private static bool IsCoalescible(string eventType) =>
        eventType.Equals("status", StringComparison.OrdinalIgnoreCase) ||
        eventType.Equals("capabilities", StringComparison.OrdinalIgnoreCase);

    private void RequireResynchronization(string connectionId, DeviceId deviceId)
    {
        if (!_resyncRequired.TryGetValue(connectionId, out var devices))
        {
            devices = [];
            _resyncRequired[connectionId] = devices;
        }
        devices.Add(deviceId);
    }
}
