using System.Collections.Frozen;
using Hms.Server.Domain.Authorization;
using Hms.Server.Domain.Devices;
using Hms.Server.Domain.Organizations;

namespace Hms.Server.Application.Authorization;

public interface IDeviceAccessResolver
{
    DeviceAccess Resolve(AccessActor actor, Device device);
}

// One membership read and one role read per device. A membership opens scope on
// that one device only; the home organization grants no scope.
public sealed class DeviceAccessResolver(IDeviceMembershipStore memberships, IDeviceRoleStore roles) : IDeviceAccessResolver
{
    private const string DevicesManage = "devices.manage";
    private const string PermissionsManage = "permissions.manage";

    public DeviceAccess Resolve(AccessActor actor, Device device)
    {
        var organizationInScope = actor.EffectiveOrganizations.Contains(device.OwnerOrganizationId);
        var role = memberships.Find(actor.UserId, device.Id) is { } membership ? roles.Find(membership.RoleId) : null;
        // A role of another organization grants nothing and opens no scope.
        if (role is not null && role.OrganizationId != device.OwnerOrganizationId)
            role = null;

        return new DeviceAccess(
            actor.IsMasterAdmin,
            organizationInScope || role is not null,
            actor.IsMasterAdmin || (organizationInScope &&
                (actor.FeaturePermissions.Contains(DevicesManage) || actor.FeaturePermissions.Contains(PermissionsManage))),
            CanManageAccess(actor, device.OwnerOrganizationId),
            role?.Id,
            role?.Permissions ?? FrozenSet<string>.Empty);
    }

    // Also used by the role handlers, which act on an organization rather than a device.
    public static bool CanManageAccess(AccessActor actor, OrganizationId organizationId) =>
        actor.IsMasterAdmin ||
        (actor.EffectiveOrganizations.Contains(organizationId) && actor.FeaturePermissions.Contains(PermissionsManage));
}
