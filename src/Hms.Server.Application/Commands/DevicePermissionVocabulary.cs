using System.Collections.Frozen;
using Hms.Server.Domain.Authorization;

namespace Hms.Server.Application.Commands;

// The one device-permission vocabulary. Role validation and the permission-code listing read
// it only through All, so the two can never disagree.
public static class DevicePermissionVocabulary
{
    // Computed per call: the vocabulary follows whichever catalog is passed in.
    public static FrozenSet<string> All(ICommandCatalog catalog) =>
        catalog.FirmwarePermissions.Concat(DevicePermissionCodes.ServerCodes).ToFrozenSet(StringComparer.Ordinal);
}
