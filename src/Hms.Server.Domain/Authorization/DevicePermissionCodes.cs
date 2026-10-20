using System.Collections.Frozen;

namespace Hms.Server.Domain.Authorization;

// Device permission codes owned by the server, plus the server's high-risk policy. The firmware
// vocabulary comes from the pinned command catalog at runtime and is never listed here.
public static class DevicePermissionCodes
{
    public const string DeviceView = "device.view";
    public const string DeviceEditBasics = "device.edit_basics";
    public const string DeviceEditProduct = "device.edit_product";

    // Reserved: no route requires it until telemetry ingest by a service identity lands.
    public const string DeviceRecordMeasurements = "device.record_measurements";

    public static IReadOnlySet<string> ServerCodes { get; } =
        new[] { DeviceView, DeviceEditBasics, DeviceEditProduct, DeviceRecordMeasurements }
            .ToFrozenSet(StringComparer.Ordinal);

    // Only a master-admin may put these into a role or assign a role that contains them.
    public static IReadOnlySet<string> HighRisk { get; } =
        new[]
        {
            "sys.reboot", "sys.factory_reset", "sys.ota", "mqtt.config_edit", "eth.edit", "net.priority_edit",
            "wifi.sta_edit", "wifi.sta_connect"
        }.ToFrozenSet(StringComparer.Ordinal);

    public static bool IsHighRisk(string? code) => code is not null && HighRisk.Contains(code);
}
