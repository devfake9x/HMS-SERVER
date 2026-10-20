using System.Security.Claims;
using Hms.Server.Api.Hubs;
using Hms.Server.Domain.Devices;

namespace Hms.Server.UnitTests;

public sealed class SignalRSubscriptionRegistryTests
{
    [Fact]
    public void Revalidation_registry_keeps_current_connection_once_and_removes_revoked_membership()
    {
        var registry = new SignalRSubscriptionRegistry();
        var device = DeviceId.New();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "test"));

        registry.Register("connection-1", principal, device, 3);
        registry.Register("connection-1", principal, device, 3);
        registry.Register("connection-2", principal, device, 3);

        Assert.Single(registry.ForDevice(device), x => x.ConnectionId == "connection-1");
        Assert.Equal(2, registry.Count);

        registry.Remove("connection-1", device);

        Assert.DoesNotContain(registry.ForDevice(device), x => x.ConnectionId == "connection-1");
        Assert.Single(registry.ForDevice(device));
    }

    [Fact]
    public void Disconnect_cleans_all_device_memberships_for_connection()
    {
        var registry = new SignalRSubscriptionRegistry();
        var principal = new ClaimsPrincipal(new ClaimsIdentity("test"));
        registry.Register("connection-1", principal, DeviceId.New(), 0);
        registry.Register("connection-1", principal, DeviceId.New(), 0);

        registry.RemoveConnection("connection-1");

        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void RemoveWhere_returns_and_removes_all_revoked_device_memberships()
    {
        var registry = new SignalRSubscriptionRegistry();
        var principal = new ClaimsPrincipal(new ClaimsIdentity("test"));
        var revoked = DeviceId.New();
        var active = DeviceId.New();
        registry.Register("connection-1", principal, revoked, 4);
        registry.Register("connection-2", principal, revoked, 4);
        registry.Register("connection-3", principal, active, 4);

        var removed = registry.RemoveWhere(x => x.DeviceId == revoked);

        Assert.Equal(2, removed.Count);
        Assert.Empty(registry.ForDevice(revoked));
        Assert.Single(registry.ForDevice(active));
    }

    [Fact]
    public void Snapshot_is_safe_for_cleanup_while_memberships_are_removed()
    {
        var registry = new SignalRSubscriptionRegistry();
        var principal = new ClaimsPrincipal(new ClaimsIdentity("test"));
        var device = DeviceId.New();
        registry.Register("connection-1", principal, device, 0);

        var snapshot = registry.Snapshot();
        registry.Remove(snapshot[0].ConnectionId, snapshot[0].DeviceId);

        Assert.Single(snapshot);
        Assert.Equal(0, registry.Count);
    }
}
