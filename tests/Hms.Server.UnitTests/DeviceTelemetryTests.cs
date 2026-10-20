using Hms.Server.Application.Management;
using Hms.Server.Application.Realtime;
using Hms.Server.Domain.Devices;
using Hms.Server.Domain.Organizations;
using Xunit;

namespace Hms.Server.UnitTests;

public sealed class DeviceTelemetryTests
{
    [Fact]
    public void InMemoryDeviceTelemetryStore_RecordsAndRetrievesLatestTelemetry()
    {
        var store = new InMemoryDeviceTelemetryStore();
        var deviceId = DeviceId.New();

        var telemetryData = new Dictionary<string, object?>
        {
            ["uptime_seconds"] = 438798L,
            ["free_heap"] = 1466571L,
            ["min_free_heap"] = 681248L,
            ["psram_free"] = 1373856L,
            ["psram_total"] = 3840256L,
            ["mqtt_connected"] = true,
            ["network_connected"] = true,
            ["tag_count"] = 62,
            ["service_count"] = 23,
            ["network"] = new Dictionary<string, object?>
            {
                ["active_uplink"] = "Ethernet",
                ["ethernet"] = new Dictionary<string, object?>
                {
                    ["ip"] = "192.168.1.127",
                    ["connected"] = true
                }
            }
        };

        store.Record(deviceId, telemetryData);

        var retrieved = store.GetLatest(deviceId);
        Assert.NotNull(retrieved);
        Assert.Equal(deviceId.Value, retrieved.DeviceId);
        Assert.Equal(438798L, retrieved.UptimeSeconds);
        Assert.Equal(1466571L, retrieved.FreeHeap);
        Assert.Equal(681248L, retrieved.MinFreeHeap);
        Assert.Equal(1373856L, retrieved.PsramFree);
        Assert.Equal(3840256L, retrieved.PsramTotal);
        Assert.True(retrieved.MqttConnected);
        Assert.True(retrieved.NetworkConnected);
        Assert.Equal(62, retrieved.TagCount);
        Assert.Equal(23, retrieved.ServiceCount);
        Assert.Equal("Ethernet", retrieved.ActiveUplink);
    }

    [Fact]
    public void InMemoryDeviceTelemetryStore_ReturnsNullForUnknownDevice()
    {
        var store = new InMemoryDeviceTelemetryStore();
        var unknownId = DeviceId.New();

        Assert.Null(store.GetLatest(unknownId));
    }

    [Fact]
    public void Device_ActivatesFromProvisioningOnTelemetry()
    {
        var orgId = OrganizationId.New();
        var device = Device.Create("ESP32-S3 RTU", orgId);
        Assert.Equal(DeviceLifecycleState.Provisioning, device.State);

        device.Activate();
        Assert.Equal(DeviceLifecycleState.Active, device.State);
    }
}
