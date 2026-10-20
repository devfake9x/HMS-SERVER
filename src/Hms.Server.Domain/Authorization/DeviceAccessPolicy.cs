using Hms.Server.Domain.Devices;

namespace Hms.Server.Domain.Authorization;

// What an actor may do on one device. InScope: the device's organization is in
// the actor's scope, or the actor holds a membership on the device. Permissions
// come only from the actor's own membership role on this device.
public sealed record DeviceAccess(
    bool IsMasterAdmin,
    bool InScope,
    bool SeesAllInScope,
    bool CanManageAccess,
    DeviceRoleId? RoleId,
    IReadOnlySet<string> Permissions);

public sealed record DeviceAccessDecision(bool Allowed, string Code);

public static class DeviceAccessCodes
{
    public const string Allowed = "ALLOWED";
    public const string DeviceNotFound = "DEVICE_NOT_FOUND";
    public const string DeviceRevoked = "DEVICE_REVOKED";
    public const string PermissionRequired = "DEVICE_PERMISSION_REQUIRED";
}

public static class DeviceAccessPolicy
{
    // Order: scope, then revocation, then master-admin, then permission. Out of
    // scope and missing look the same, so a device of another organization never
    // reveals its state.
    public static DeviceAccessDecision Evaluate(DeviceAccess access, Device device, string requiredPermission)
    {
        if (!access.IsMasterAdmin && !access.InScope)
            return Deny(DeviceAccessCodes.DeviceNotFound);
        if (device.State == DeviceLifecycleState.Revoked)
            return Deny(DeviceAccessCodes.DeviceRevoked);
        if (access.IsMasterAdmin)
            return Allow();
        if (requiredPermission == DevicePermissionCodes.DeviceView && access.SeesAllInScope)
            return Allow();
        return access.Permissions.Contains(requiredPermission)
            ? Allow()
            : Deny(DeviceAccessCodes.PermissionRequired);
    }

    private static DeviceAccessDecision Allow() => new(true, DeviceAccessCodes.Allowed);

    private static DeviceAccessDecision Deny(string code) => new(false, code);
}
