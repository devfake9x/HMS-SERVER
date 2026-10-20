using System.Collections.Concurrent;
using System.Security.Claims;
using Hms.Server.Domain.Devices;

namespace Hms.Server.Api.Hubs;

public sealed record DeviceSubscription(string ConnectionId, ClaimsPrincipal Principal, DeviceId DeviceId, long AuthorizationEpoch);

/// <summary>Tracks authorized device subscriptions independently of SignalR groups.</summary>
public sealed class SignalRSubscriptionRegistry
{
    private readonly ConcurrentDictionary<(string ConnectionId, DeviceId DeviceId), DeviceSubscription> _subscriptions = new();

    public void Register(string connectionId, ClaimsPrincipal principal, DeviceId deviceId, long authorizationEpoch) =>
        _subscriptions[(connectionId, deviceId)] = new(connectionId, principal, deviceId, authorizationEpoch);

    public void Remove(string connectionId, DeviceId deviceId) => _subscriptions.TryRemove((connectionId, deviceId), out _);

    public bool RefreshPrincipal(string connectionId, DeviceId deviceId, ClaimsPrincipal principal)
    {
        var key = (connectionId, deviceId);
        if (!_subscriptions.TryGetValue(key, out var current))
            return false;
        return _subscriptions.TryUpdate(key, current with { Principal = principal }, current);
    }

    public void RemoveConnection(string connectionId)
    {
        foreach (var key in _subscriptions.Keys.Where(x => x.ConnectionId == connectionId))
            _subscriptions.TryRemove(key, out _);
    }

    public bool TryGet(string connectionId, DeviceId deviceId, out DeviceSubscription? subscription) =>
        _subscriptions.TryGetValue((connectionId, deviceId), out subscription);

    public IReadOnlyList<DeviceSubscription> ForDevice(DeviceId deviceId) =>
        _subscriptions.Values.Where(x => x.DeviceId == deviceId).ToArray();

    public IReadOnlyList<DeviceSubscription> Snapshot() => _subscriptions.Values.ToArray();

    /// <summary>Removes memberships matching a predicate and returns the removed entries.</summary>
    public IReadOnlyList<DeviceSubscription> RemoveWhere(Func<DeviceSubscription, bool> predicate)
    {
        var removed = new List<DeviceSubscription>();
        foreach (var entry in _subscriptions.Values.ToArray())
        {
            if (!predicate(entry))
                continue;
            if (_subscriptions.TryRemove((entry.ConnectionId, entry.DeviceId), out var value) && value is not null)
                removed.Add(value);
        }
        return removed;
    }

    public int Count => _subscriptions.Count;
}
