using System.Collections.Concurrent;
using Hms.Server.Domain.Devices;

namespace Hms.Server.Application.Realtime;

public sealed record DeviceTelemetryDto(
    Guid DeviceId,
    DateTimeOffset Timestamp,
    long? UptimeSeconds,
    long? FreeHeap,
    long? MinFreeHeap,
    long? PsramFree,
    long? PsramTotal,
    bool? MqttConnected,
    bool? NetworkConnected,
    int? TagCount,
    int? ServiceCount,
    string? ActiveUplink,
    IReadOnlyDictionary<string, object?> Data);

public interface IDeviceTelemetryStore
{
    void Record(DeviceId deviceId, IReadOnlyDictionary<string, object?> data);
    DeviceTelemetryDto? GetLatest(DeviceId deviceId);
}

public sealed class InMemoryDeviceTelemetryStore : IDeviceTelemetryStore
{
    private readonly ConcurrentDictionary<DeviceId, DeviceTelemetryDto> _latest = new();

    public void Record(DeviceId deviceId, IReadOnlyDictionary<string, object?> data)
    {
        long? uptime = null;
        if (data.TryGetValue("uptime_seconds", out var u) && u is not null)
        {
            if (u is long l) uptime = l;
            else if (u is int i) uptime = i;
            else if (long.TryParse(u.ToString(), out var parsed)) uptime = parsed;
        }

        long? freeHeap = null;
        if (data.TryGetValue("free_heap", out var fh) && fh is not null)
        {
            if (fh is long l) freeHeap = l;
            else if (fh is int i) freeHeap = i;
            else if (long.TryParse(fh.ToString(), out var parsed)) freeHeap = parsed;
        }

        long? minFreeHeap = null;
        if (data.TryGetValue("min_free_heap", out var mfh) && mfh is not null)
        {
            if (mfh is long l) minFreeHeap = l;
            else if (mfh is int i) minFreeHeap = i;
            else if (long.TryParse(mfh.ToString(), out var parsed)) minFreeHeap = parsed;
        }

        long? psramFree = null;
        if (data.TryGetValue("psram_free", out var pf) && pf is not null)
        {
            if (pf is long l) psramFree = l;
            else if (pf is int i) psramFree = i;
            else if (long.TryParse(pf.ToString(), out var parsed)) psramFree = parsed;
        }

        long? psramTotal = null;
        if (data.TryGetValue("psram_total", out var pt) && pt is not null)
        {
            if (pt is long l) psramTotal = l;
            else if (pt is int i) psramTotal = i;
            else if (long.TryParse(pt.ToString(), out var parsed)) psramTotal = parsed;
        }

        bool? mqttConnected = null;
        if (data.TryGetValue("mqtt_connected", out var mc) && mc is not null)
        {
            if (mc is bool b) mqttConnected = b;
            else if (bool.TryParse(mc.ToString(), out var parsed)) mqttConnected = parsed;
        }

        bool? netConnected = null;
        if (data.TryGetValue("network_connected", out var nc) && nc is not null)
        {
            if (nc is bool b) netConnected = b;
            else if (bool.TryParse(nc.ToString(), out var parsed)) netConnected = parsed;
        }

        int? tagCount = null;
        if (data.TryGetValue("tag_count", out var tc) && tc is not null)
        {
            if (tc is int i) tagCount = i;
            else if (tc is long l) tagCount = (int)l;
            else if (int.TryParse(tc.ToString(), out var parsed)) tagCount = parsed;
        }

        int? svcCount = null;
        if (data.TryGetValue("service_count", out var sc) && sc is not null)
        {
            if (sc is int i) svcCount = i;
            else if (sc is long l) svcCount = (int)l;
            else if (int.TryParse(sc.ToString(), out var parsed)) svcCount = parsed;
        }

        string? activeUplink = null;
        if (data.TryGetValue("network", out var netObj) && netObj is IReadOnlyDictionary<string, object?> netDict)
        {
            if (netDict.TryGetValue("active_uplink", out var au) && au is not null)
            {
                activeUplink = au.ToString();
            }
        }
        else if (data.TryGetValue("active_uplink", out var au2) && au2 is not null)
        {
            activeUplink = au2.ToString();
        }

        var mergedData = new Dictionary<string, object?>(data);
        if (_latest.TryGetValue(deviceId, out var existing) && existing.Data is not null)
        {
            if (!mergedData.ContainsKey("digital_outputs") && existing.Data.TryGetValue("digital_outputs", out var existingDo))
            {
                mergedData["digital_outputs"] = existingDo;
            }
            if (!mergedData.ContainsKey("digital_inputs") && existing.Data.TryGetValue("digital_inputs", out var existingDi))
            {
                mergedData["digital_inputs"] = existingDi;
            }
            if (!mergedData.ContainsKey("mqtt") && existing.Data.TryGetValue("mqtt", out var existingMqtt))
            {
                mergedData["mqtt"] = existingMqtt;
            }
            if (!mergedData.ContainsKey("hostname") && existing.Data.TryGetValue("hostname", out var existingHost))
            {
                mergedData["hostname"] = existingHost;
            }
            if (!mergedData.ContainsKey("voltage") && existing.Data.TryGetValue("voltage", out var existingV))
            {
                mergedData["voltage"] = existingV;
            }
            if (!mergedData.ContainsKey("temperature") && existing.Data.TryGetValue("temperature", out var existingTemp))
            {
                mergedData["temperature"] = existingTemp;
            }
            if (!mergedData.ContainsKey("batteryLevel") && existing.Data.TryGetValue("batteryLevel", out var existingBat))
            {
                mergedData["batteryLevel"] = existingBat;
            }
        }

        _latest[deviceId] = new DeviceTelemetryDto(
            deviceId.Value,
            DateTimeOffset.UtcNow,
            uptime,
            freeHeap,
            minFreeHeap,
            psramFree,
            psramTotal,
            mqttConnected,
            netConnected,
            tagCount,
            svcCount,
            activeUplink,
            mergedData);
    }

    public DeviceTelemetryDto? GetLatest(DeviceId deviceId) => _latest.GetValueOrDefault(deviceId);
}
