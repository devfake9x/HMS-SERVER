using Hms.Server.Application.Commands;
using Hms.Server.Domain.Authorization;
using Hms.Server.Domain.Commands;
using Hms.Server.Infrastructure.Commands;

namespace Hms.Server.UnitTests;

public sealed class DevicePermissionVocabularyTests
{
    [Fact]
    public void Vocabulary_has_fifty_codes()
    {
        IReadOnlySet<string> vocabulary = DevicePermissionVocabulary.All(new PinnedCommandCatalog());

        Assert.Equal(50, vocabulary.Count);
        Assert.Contains("device.view", vocabulary);
        Assert.Contains("sys.config_import", vocabulary);
    }

    [Fact]
    public void Firmware_part_of_vocabulary_has_46_codes()
    {
        var catalog = new PinnedCommandCatalog();
        var firmwarePart = DevicePermissionVocabulary.All(catalog)
            .Except(DevicePermissionCodes.ServerCodes)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(46, firmwarePart.Count);
        Assert.True(firmwarePart.SetEquals(catalog.FirmwarePermissions));
        Assert.Empty(DevicePermissionCodes.ServerCodes.Intersect(catalog.FirmwarePermissions));
    }

    [Fact]
    public void Vocabulary_follows_the_given_catalog()
    {
        var catalog = new InMemoryCommandCatalog(
        [
            new CommandDefinition("adc", "get_status", "adc.view", CommandAvailability.Enabled, false, true, null),
            new CommandDefinition("config", "get", null, CommandAvailability.Enabled, false, true, null,
                PermissionByDomain: new Dictionary<string, DomainPermission>
                {
                    ["mqtt"] = new("mqtt.status_view", "mqtt.config_edit")
                })
        ]);

        Assert.Equal(
            new[]
            {
                "adc.view", "device.edit_basics", "device.edit_product", "device.record_measurements", "device.view",
                "mqtt.config_edit", "mqtt.status_view"
            },
            DevicePermissionVocabulary.All(catalog).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Vocabulary_is_immutable() =>
        Assert.Throws<NotSupportedException>(() =>
            ((ICollection<string>)DevicePermissionVocabulary.All(new PinnedCommandCatalog())).Add("x.y"));
}
