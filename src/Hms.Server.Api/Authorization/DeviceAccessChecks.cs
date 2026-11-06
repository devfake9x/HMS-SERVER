using System.Security.Claims;
using Hms.Server.Application.Authorization;
using Hms.Server.Application.Management;
using Hms.Server.Domain.Authorization;
using Hms.Server.Domain.Devices;

namespace Hms.Server.Api.Authorization;

/// <summary>
/// The device-role check the command handler applies, for endpoints that tell the UI what will
/// pass. Resolve once per request (it reads organizations, grants, membership and role), then
/// evaluate any number of permissions against the result in memory.
/// </summary>
public static class DeviceAccessChecks
{
    public static DeviceAccess? Resolve(
        ClaimsPrincipal user,
        Device device,
        IOrganizationStore organizations,
        IPermissionStore permissions,
        IDeviceAccessResolver resolver)
    {
        var actor = AccessActorFactory.FromPrincipal(user, organizations, permissions);
        return actor is null ? null : resolver.Resolve(actor, device);
    }

    /// <summary>The scope rule of <c>POST .../commands</c>: master-admin, or in scope through an
    /// organization or a membership on this device.</summary>
    public static bool CanSendCommands(DeviceAccess? access) =>
        access is not null && (access.IsMasterAdmin || access.InScope);

    public static bool Allows(DeviceAccess? access, Device device, string permission) =>
        access is not null && DeviceAccessPolicy.Evaluate(access, device, permission).Allowed;
}
