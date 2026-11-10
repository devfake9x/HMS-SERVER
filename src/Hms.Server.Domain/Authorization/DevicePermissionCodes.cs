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

    // Only a master-admin may put these into a role or assign a role that contains them. Checked when
    // a grant is written (role create, update, delete; membership assign, remove), not when a command
    // is sent. Changing this set does not revisit grants that already exist.
    public static IReadOnlySet<string> HighRisk { get; } =
        new[]
        {
            "sys.reboot", "sys.factory_reset", "sys.ota", "mqtt.config_edit", "eth.edit", "net.priority_edit",
            "wifi.sta_edit", "wifi.sta_connect",
            // Drives relay outputs wired to physical equipment.
            "gpio.do_write",
            // Forces a relay output, or changes the output configuration the device applies at boot.
            "gpio.do_force", "gpio.do_config",
            // Sends or deletes SMS: costs money, reaches people outside the system, or destroys messages.
            "sms.send", "sms.delete"
        }.ToFrozenSet(StringComparer.Ordinal);

    public static bool IsHighRisk(string? code) => code is not null && HighRisk.Contains(code);
}
