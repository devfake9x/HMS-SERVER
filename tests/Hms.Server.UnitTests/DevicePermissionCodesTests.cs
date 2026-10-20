using Hms.Server.Domain.Authorization;
using Hms.Server.Infrastructure.Commands;

namespace Hms.Server.UnitTests;

public sealed class DevicePermissionCodesTests
{
    [Fact]
    public void Server_codes_are_the_four_device_codes()
    {
        Assert.Equal("device.view", DevicePermissionCodes.DeviceView);
        Assert.Equal("device.edit_basics", DevicePermissionCodes.DeviceEditBasics);
        Assert.Equal("device.edit_product", DevicePermissionCodes.DeviceEditProduct);
        Assert.Equal("device.record_measurements", DevicePermissionCodes.DeviceRecordMeasurements);
        Assert.Equal(
            new[] { "device.edit_basics", "device.edit_product", "device.record_measurements", "device.view" },
            DevicePermissionCodes.ServerCodes.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void High_risk_set_is_a_subset_of_firmware_vocabulary()
    {
        Assert.Equal(
            new[]
            {
                "eth.edit", "mqtt.config_edit", "net.priority_edit", "sys.factory_reset", "sys.ota", "sys.reboot",
                "wifi.sta_connect", "wifi.sta_edit"
            },
            DevicePermissionCodes.HighRisk.Order(StringComparer.Ordinal).ToArray());
        Assert.Subset(
            new PinnedCommandCatalog().FirmwarePermissions.ToHashSet(StringComparer.Ordinal),
            DevicePermissionCodes.HighRisk.ToHashSet(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("sys.reboot", true)]
    [InlineData("wifi.sta_connect", true)]
    [InlineData("SYS.REBOOT", false)]
    [InlineData(" sys.reboot", false)]
    [InlineData("sys.config_import", false)]
    [InlineData("device.view", false)]
    [InlineData(null, false)]
    public void IsHighRisk_is_exact_and_null_safe(string? code, bool expected) =>
        Assert.Equal(expected, DevicePermissionCodes.IsHighRisk(code));

    [Fact]
    public void Permission_sets_are_immutable()
    {
        Assert.Throws<NotSupportedException>(() =>
            ((ICollection<string>)DevicePermissionCodes.HighRisk).Add("sys.info_view"));
        Assert.Throws<NotSupportedException>(() =>
            ((ICollection<string>)DevicePermissionCodes.ServerCodes).Add("device.delete"));
    }
}
