using Hms.Server.Application.Realtime;
using Hms.Server.Domain.Devices;

namespace Hms.Server.UnitTests;

public sealed class RealtimeTests
{
    [Fact]
    public void Authorized_event_uses_numeric_policy_version()
    {
        var device = DeviceId.New();
        var deviceEvent = new AuthorizedDeviceEvent(
            device,
            AuthorizationEpoch: 1,
            PolicyVersion: 7,
            EventType: "status",
            Payload: new Dictionary<string, object?>());

        Assert.Equal(7, deviceEvent.PolicyVersion);
    }

    [Fact]
    public async Task Publisher_coalesces_latest_event_and_bounds_queue()
    {
        var publisher = new BoundedDeviceEventPublisher(capacity: 1);
        var first = DeviceId.New();
        var second = DeviceId.New();

        await publisher.PublishAsync(new AuthorizedDeviceEvent(first, 1, 1, "status", new Dictionary<string, object?>()));
        await publisher.PublishAsync(new AuthorizedDeviceEvent(first, 2, 2, "status", new Dictionary<string, object?>()));
        await publisher.PublishAsync(new AuthorizedDeviceEvent(second, 1, 1, "status", new Dictionary<string, object?>()));

        var snapshot = publisher.Snapshot();
        Assert.Single(snapshot);
        Assert.Equal(second, snapshot[0].DeviceId);
        Assert.Equal(1, publisher.DroppedCount);
    }

    [Fact]
    public void Epoch_store_advances_monotonically()
    {
        var store = new InMemoryAuthorizationEpochStore();
        var device = DeviceId.New();
        Assert.Equal(0, store.Current(device));
        Assert.Equal(1, store.Advance(device));
        Assert.Equal(2, store.Advance(device));
        Assert.Equal(2, store.Current(device));
    }

    [Fact]
    public void Epoch_store_advance_many_moves_each_distinct_device_once()
    {
        var store = new InMemoryAuthorizationEpochStore();
        var first = DeviceId.New();
        var second = DeviceId.New();

        store.AdvanceMany([first, first, second]);

        Assert.Equal(1, store.Current(first));
        Assert.Equal(1, store.Current(second));
    }

    [Fact]
    public async Task Delivery_snapshot_discards_events_from_old_authorization_epochs()
    {
        var publisher = new BoundedDeviceEventPublisher();
        var epochs = new InMemoryAuthorizationEpochStore();
        var device = DeviceId.New();

        await publisher.PublishAsync(new AuthorizedDeviceEvent(
            device, 0, 1, "status", new Dictionary<string, object?>()));
        epochs.Advance(device);

        Assert.Empty(publisher.SnapshotForDelivery(epochs));
    }

    [Fact]
    public void Event_fence_rejects_policy_version_drift_even_when_epoch_is_current()
    {
        var epochs = new InMemoryAuthorizationEpochStore();
        var device = DeviceId.New();
        var deviceEvent = new AuthorizedDeviceEvent(
            device, 0, 4, "status", new Dictionary<string, object?>());

        Assert.True(RealtimeEventFence.IsCurrent(deviceEvent, epochs, 4));
        Assert.False(RealtimeEventFence.IsCurrent(deviceEvent, epochs, 5));
    }

    [Fact]
    public async Task Drain_for_delivery_removes_current_events_but_drops_stale_epochs()
    {
        var publisher = new BoundedDeviceEventPublisher();
        var epochs = new InMemoryAuthorizationEpochStore();
        var current = DeviceId.New();
        var stale = DeviceId.New();

        await publisher.PublishAsync(new AuthorizedDeviceEvent(
            current, 0, 1, "status", new Dictionary<string, object?>()));
        await publisher.PublishAsync(new AuthorizedDeviceEvent(
            stale, 0, 1, "status", new Dictionary<string, object?>()));
        epochs.Advance(stale);

        var drained = publisher.DrainForDelivery(epochs);

        var only = Assert.Single(drained);
        Assert.Equal(current, only.DeviceId);
        Assert.Empty(publisher.Snapshot());
    }

    [Fact]
    public async Task Publisher_does_not_coalesce_security_events_with_status_updates()
    {
        var publisher = new BoundedDeviceEventPublisher();
        var device = DeviceId.New();
        await publisher.PublishAsync(new AuthorizedDeviceEvent(device, 0, 1, "DeviceRevoked", new Dictionary<string, object?>()));
        await publisher.PublishAsync(new AuthorizedDeviceEvent(device, 0, 1, "status", new Dictionary<string, object?>()));

        var events = publisher.Snapshot();
        Assert.Equal(2, events.Count);
        Assert.Equal("DeviceRevoked", events[0].EventType);
        Assert.Equal("status", events[1].EventType);
    }

    [Fact]
    public async Task Full_queue_preserves_critical_event_over_status_update()
    {
        var publisher = new BoundedDeviceEventPublisher(capacity: 1);
        var device = DeviceId.New();
        await publisher.PublishAsync(new AuthorizedDeviceEvent(device, 0, 1, "status", new Dictionary<string, object?>()));
        await publisher.PublishAsync(new AuthorizedDeviceEvent(device, 0, 1, "DeviceRevoked", new Dictionary<string, object?>()));

        var events = publisher.Snapshot();
        var only = Assert.Single(events);
        Assert.Equal("DeviceRevoked", only.EventType);
    }

    [Fact]
    public async Task Failed_delivery_can_be_requeued_without_losing_event()
    {
        var publisher = new BoundedDeviceEventPublisher();
        var epochs = new InMemoryAuthorizationEpochStore();
        var device = DeviceId.New();
        var item = new AuthorizedDeviceEvent(device, 0, 1, "command", new Dictionary<string, object?>());
        await publisher.PublishAsync(item);

        var drained = publisher.DrainForDelivery(epochs);
        Assert.Single(drained);
        publisher.RequeueForDelivery(drained, epochs);

        Assert.Equal(item, Assert.Single(publisher.Snapshot()));
    }

    [Fact]
    public async Task Critical_only_queue_rejects_incoming_critical_event_explicitly()
    {
        var publisher = new BoundedDeviceEventPublisher(capacity: 1);
        var first = new AuthorizedDeviceEvent(DeviceId.New(), 0, 0, "command", new Dictionary<string, object?>());
        await publisher.PublishAsync(first);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await publisher.PublishAsync(first with { EventType = "security" }));

        Assert.Equal("REALTIME_QUEUE_FULL", error.Message);
        Assert.Equal(first, Assert.Single(publisher.Snapshot()));
    }

    [Fact]
    public async Task Incoming_status_cannot_evict_critical_event()
    {
        var publisher = new BoundedDeviceEventPublisher(capacity: 1);
        var first = new AuthorizedDeviceEvent(DeviceId.New(), 0, 0, "command", new Dictionary<string, object?>());
        await publisher.PublishAsync(first);
        await publisher.PublishAsync(first with { EventType = "status" });
        Assert.Equal(first, Assert.Single(publisher.Snapshot()));
        Assert.Equal(1, publisher.DroppedCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Nonpositive_capacity_is_rejected(int capacity) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedDeviceEventPublisher(capacity));

    [Fact]
    public void Device_revoked_notification_carries_current_authorization_context()
    {
        var device = DeviceId.New();
        var epochs = new InMemoryAuthorizationEpochStore();
        epochs.Advance(device);
        var permissions = new Hms.Server.Application.Authorization.InMemoryPermissionStore();
        permissions.Advance();

        var notification = new Hms.Server.Application.Realtime.DeviceRevokedNotification(
            device.Value,
            epochs.Current(device),
            permissions.PolicyVersion,
            "AUTHORIZATION_REVOKED");

        Assert.Equal(device.Value, notification.DeviceId);
        Assert.Equal(1, notification.AuthorizationEpoch);
        Assert.Equal(1, notification.PolicyVersion);
        Assert.Equal("AUTHORIZATION_REVOKED", notification.Reason);
    }
}
