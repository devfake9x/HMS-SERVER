using System.Security.Claims;
using Hms.Server.Api.Authorization;
using Hms.Server.Application.Authorization;
using Hms.Server.Application.Management;
using Hms.Server.Domain.Authorization;
using Hms.Server.Domain.Devices;
using Hms.Server.Domain.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hms.Server.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/devices/{deviceId:guid}/permissions")]
public sealed class DevicePermissionsController(
    IDeviceStore devices,
    IOrganizationStore organizations,
    IUserManagement users,
    IUserScopeStore userScopes,
    IPermissionStore permissions,
    GrantDeviceActionHandler actions) : ControllerBase
{
    private static readonly IReadOnlyList<DevicePermissionDefinitionDto> DefaultDefinitions =
    [
        new("view", "View", "Can view the device and its data"),
        new("edit_basics", "Metadata", "Can edit device metadata"),
        new("edit_product", "Definition", "Can edit the device definition"),
        new("record_measurements", "Measurements", "Can record measurements")
    ];

    [HttpGet]
    public async Task<ActionResult<DevicePermissionMatrixDto>> Get(Guid deviceId, CancellationToken cancellationToken)
    {
        var device = devices.Find(new DeviceId(deviceId));
        if (device is null)
            return NotFound(new { code = "DEVICE_NOT_FOUND" });

        if (!ScopeAuthorization.CanAccessDevice(User, device, organizations, permissions))
            return Forbid();

        var matrix = await BuildMatrixAsync(device, cancellationToken);
        return Ok(matrix);
    }

    [HttpPut]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<DevicePermissionMatrixDto>> Update(
        Guid deviceId,
        UpdateDevicePermissionsRequest request,
        CancellationToken cancellationToken)
    {
        var device = devices.Find(new DeviceId(deviceId));
        if (device is null)
            return NotFound(new { code = "DEVICE_NOT_FOUND" });

        if (!ScopeAuthorization.CanAccessDevice(User, device, organizations, permissions))
            return Forbid();

        if (!ScopeAuthorization.HasFeaturePermission(User, "permissions.manage", permissions) &&
            !ScopeAuthorization.HasFeaturePermission(User, "devices.manage", permissions))
            return Forbid();

        if (request.ExpectedPolicyVersion.HasValue && request.ExpectedPolicyVersion.Value != permissions.PolicyVersion)
        {
            return Conflict(new
            {
                code = "POLICY_VERSION_STALE",
                currentVersion = permissions.PolicyVersion
            });
        }

        if (request.Grants is not null)
        {
            foreach (var grant in request.Grants)
            {
                if (grant.UserId == Guid.Empty || string.IsNullOrWhiteSpace(grant.Action))
                    continue;

                var targetUserId = new UserId(grant.UserId);
                if (!userScopes.Exists(targetUserId))
                    continue;

                if (!CanManageTarget(targetUserId))
                    return Forbid();

                actions.Handle(targetUserId, device.Id, grant.Action.Trim(), deny: !grant.Granted);
            }
        }

        var matrix = await BuildMatrixAsync(device, cancellationToken);
        return Ok(matrix);
    }

    private async Task<DevicePermissionMatrixDto> BuildMatrixAsync(Device device, CancellationToken cancellationToken)
    {
        var allUsers = await users.ListAsync(cancellationToken);
        var userDtos = new List<DeviceUserPermissionRowDto>();

        foreach (var user in allUsers)
        {
            if (!Guid.TryParse(user.Id, out var parsedGuid))
                continue;

            var targetUserId = new UserId(parsedGuid);
            var isMasterAdmin = user.Roles.Contains("master-admin", StringComparer.OrdinalIgnoreCase);
            var userOrg = userScopes.Organization(targetUserId);

            // Filter users relevant to this device:
            // 1. Is master-admin
            // 2. Belongs to the device's organization
            // 3. Or has explicit device action grants on this device
            var isOrgMember = userOrg.HasValue && userOrg.Value == device.OwnerOrganizationId;
            var hasAccess = isMasterAdmin || isOrgMember;

            var existingGrants = permissions.DeviceActionGrants(targetUserId)
                .Where(g => g.DeviceId == device.Id)
                .ToArray();

            if (!hasAccess && existingGrants.Length == 0)
                continue;

            var grantsMap = new Dictionary<string, DevicePermissionGrantDetailDto>(StringComparer.OrdinalIgnoreCase);

            foreach (var def in DefaultDefinitions)
            {
                bool inherited = false;
                string? inheritedReason = null;
                bool granted = false;

                if (string.Equals(def.Key, "view", StringComparison.OrdinalIgnoreCase))
                {
                    if (isMasterAdmin || isOrgMember)
                    {
                        inherited = true;
                        inheritedReason = "The user has set this permission for all devices in this workspace.";
                        granted = true;
                    }
                }

                var grantOverride = existingGrants
                    .Where(g => string.Equals(g.Action, def.Key, StringComparison.OrdinalIgnoreCase) || g.Action == "*")
                    .OrderByDescending(g => g.PolicyVersion)
                    .FirstOrDefault();

                if (grantOverride is not null)
                {
                    if (!inherited)
                    {
                        granted = !grantOverride.Deny;
                    }
                }

                grantsMap[def.Key] = new DevicePermissionGrantDetailDto(granted, inherited, inheritedReason);
            }

            var displayName = ResolveDisplayName(user.Email);
            var primaryRole = user.Roles.FirstOrDefault() ?? "member";

            userDtos.Add(new DeviceUserPermissionRowDto(
                parsedGuid,
                displayName,
                user.Email,
                primaryRole,
                grantsMap
            ));
        }

        return new DevicePermissionMatrixDto(
            device.Id.Value,
            device.Name,
            permissions.PolicyVersion,
            DefaultDefinitions,
            userDtos
        );
    }

    private static string ResolveDisplayName(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "Unknown";
        if (email.Equals("toannm.work@gmail.com", StringComparison.OrdinalIgnoreCase))
            return "Toan Nguyen";

        var prefix = email.Split('@')[0];
        if (prefix.Contains('.'))
        {
            var parts = prefix.Split('.').Select(p => p.Length > 0 ? char.ToUpperInvariant(p[0]) + (p.Length > 1 ? p[1..] : "") : "");
            return string.Join(" ", parts);
        }
        return char.ToUpperInvariant(prefix[0]) + (prefix.Length > 1 ? prefix[1..] : "");
    }

    private bool CanManageTarget(UserId targetUser)
    {
        if (!userScopes.Exists(targetUser))
            return false;

        if (User.IsInRole("master-admin"))
            return true;

        var targetOrganization = userScopes.Organization(targetUser);
        return targetOrganization.HasValue &&
               ScopeAuthorization.CanAccessOrganization(User, targetOrganization.Value, organizations, permissions);
    }
}

public sealed record DevicePermissionDefinitionDto(
    string Key,
    string Title,
    string Description);

public sealed record DevicePermissionGrantDetailDto(
    bool Granted,
    bool Inherited,
    string? InheritedReason = null);

public sealed record DeviceUserPermissionRowDto(
    Guid UserId,
    string DisplayName,
    string Email,
    string Role,
    IReadOnlyDictionary<string, DevicePermissionGrantDetailDto> Grants);

public sealed record DevicePermissionMatrixDto(
    Guid DeviceId,
    string DeviceName,
    long PolicyVersion,
    IReadOnlyList<DevicePermissionDefinitionDto> Permissions,
    IReadOnlyList<DeviceUserPermissionRowDto> Users);

public sealed record UpdateDevicePermissionsRequest(
    long? ExpectedPolicyVersion,
    IReadOnlyList<DevicePermissionOverrideDto> Grants);

public sealed record DevicePermissionOverrideDto(
    Guid UserId,
    string Action,
    bool Granted);
