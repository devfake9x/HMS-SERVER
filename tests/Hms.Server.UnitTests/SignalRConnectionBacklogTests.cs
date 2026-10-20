using Hms.Server.Api.Hubs;
using Hms.Server.Application.Realtime;
using Hms.Server.Domain.Devices;
using System.Diagnostics.Metrics;

namespace Hms.Server.UnitTests;

public sealed class SignalRConnectionBacklogTests
{
    [Fact]
    public void Coalesces_latest_state_without_reordering_critical_events()
    {
        var backlog = new SignalRConnectionBacklog(3);
        var device = DeviceId.New();

        Assert.True(backlog.TryEnqueue("c1", Event(device, "status", 1)));
        Assert.True(backlog.TryEnqueue("c1", Event(device, "command", 2)));
        Assert.True(backlog.TryEnqueue("c1", Event(device, "status", 3)));

        var entries = backlog.Snapshot("c1");
        Assert.Equal(["status", "command"], entries.Select(x => x.Event.EventType));
        Assert.Equal(3, entries[0].Event.PolicyVersion);
    }

    [Fact]
    public void Full_critical_backlog_requires_resynchronization_without_evicting_critical_events()
    {
        var backlog = new SignalRConnectionBacklog(2);
        var device = DeviceId.New();

        Assert.True(backlog.TryEnqueue("c1", Event(device, "command", 1)));
        Assert.True(backlog.TryEnqueue("c1", Event(device, "DeviceRevoked", 2)));
        Assert.False(backlog.TryEnqueue("c1", Event(device, "license", 3)));

        Assert.True(backlog.RequiresResynchronization("c1", device));
        Assert.Equal(["command", "DeviceRevoked"], backlog.Snapshot("c1").Select(x => x.Event.EventType));
    }

    [Fact]
    public void Retry_state_belongs_to_one_connection_and_exhaustion_requires_resync()
    {
        var backlog = new SignalRConnectionBacklog();
        var device = DeviceId.New();
        Assert.True(backlog.TryEnqueue("c1", Event(device, "command", 1)));
        Assert.True(backlog.TryEnqueue("c2", Event(device, "command", 2)));

        Assert.True(backlog.MarkRetry("c1", maxAttempts: 3));
        Assert.False(backlog.RequiresResynchronization("c1", device));
        Assert.False(backlog.RequiresResynchronization("c2", device));
        Assert.False(backlog.MarkRetry("c1", maxAttempts: 2));
        Assert.True(backlog.RequiresResynchronization("c1", device));
        Assert.False(backlog.RequiresResynchronization("c2", device));
        Assert.Single(backlog.Snapshot("c2"));
    }

    [Fact]
    public void Removing_one_device_preserves_other_device_events()
    {
        var backlog = new SignalRConnectionBacklog();
        var first = DeviceId.New();
        var second = DeviceId.New();
        backlog.TryEnqueue("c1", Event(first, "command", 1));
        backlog.TryEnqueue("c1", Event(second, "command", 2));

        backlog.RemoveDevice("c1", first);

        var remaining = backlog.Snapshot("c1");
        Assert.Single(remaining);
        Assert.Equal(second, remaining[0].Event.DeviceId);
    }

    [Fact]
    public void Removing_one_device_preserves_a_sibling_resynchronization_fence()
    {
        var backlog = new SignalRConnectionBacklog(1);
        var first = DeviceId.New();
        var second = DeviceId.New();
        Assert.True(backlog.TryEnqueue("c1", Event(first, "command", 1)));
        Assert.False(backlog.TryEnqueue("c1", Event(second, "command", 2)));

        backlog.RemoveDevice("c1", first);

        Assert.False(backlog.RequiresResynchronization("c1", first));
        Assert.True(backlog.RequiresResynchronization("c1", second));
    }

    [Fact]
    public void Emits_runtime_metrics_for_drop_retry_and_resynchronization()
    {
        long dropped = 0;
        long retries = 0;
        long resynchronizations = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (instrument.Meter.Name == SignalRBacklogMetrics.MeterName)
                current.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, _, _) =>
        {
            switch (instrument.Name)
            {
                case SignalRBacklogMetrics.DroppedInstrumentName:
                    dropped += measurement;
                    break;
                case SignalRBacklogMetrics.RetryInstrumentName:
                    retries += measurement;
                    break;
                case SignalRBacklogMetrics.ResynchronizationInstrumentName:
                    resynchronizations += measurement;
                    break;
            }
        });
        listener.Start();

        var backlog = new SignalRConnectionBacklog(1);
        var device = DeviceId.New();
        Assert.True(backlog.TryEnqueue("c1", Event(device, "status", 1)));
        Assert.True(backlog.TryEnqueue("c1", Event(device, "command", 2)));
        Assert.True(backlog.MarkRetry("c1", maxAttempts: 3));
        Assert.False(backlog.TryEnqueue("c1", Event(device, "license", 3)));

        Assert.Equal(1, dropped);
        Assert.Equal(1, retries);
        Assert.Equal(1, resynchronizations);
    }

    private static AuthorizedDeviceEvent Event(DeviceId device, string type, long policy) =>
        new(device, 0, policy, type, new Dictionary<string, object?>());
}
