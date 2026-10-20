using System.Security.Claims;
using Hms.Server.Application.Authorization;
using Hms.Server.Application.Management;
using Hms.Server.Domain.Authorization;
using Hms.Server.Domain.Devices;
using Hms.Server.Domain.Organizations;
using Hms.Server.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hms.Server.Api.Controllers;

[ApiController]
[Authorize(Policy = "hms.permissions.manage")]
[Route("api/v1/permissions")]
public sealed class PermissionGrantsController(
    GrantFeaturePermissionHandler features,
    GrantResourceScopeHandler resources,
    GrantDeviceActionHandler actions,
    IUserScopeStore users,
    IOrganizationStore organizations,
    IDeviceStore devices,
    IPermissionStore permissions) : ControllerBase
{
    [HttpGet("{userId:guid}")]
    public ActionResult<PermissionGrantSnapshot> Get(Guid userId)
    {
        if (!CanManagePermissions())
            return Forbid();

        var target = new UserId(userId);
        if (!CanManageTarget(target))
            return Forbid();

        return Ok(new PermissionGrantSnapshot(
            target,
            permissions.FeatureGrants(target),
            permissions.ResourceGrants(target),
            permissions.DeviceActionGrants(target),
            permissions.PolicyVersion));
    }

    [HttpPost("features")]
    [ValidateAntiForgeryToken]
    public ActionResult<FeaturePermissionGrant> GrantFeature(GrantFeatureRequest request)
    {
        if (!CanManagePermissions())
            return Forbid();
        if (request.UserId == Guid.Empty || string.IsNullOrWhiteSpace(request.Permission))
            return BadRequest(new { code = "PERMISSION_REQUEST_INVALID" });
        if (!CanManageTarget(new UserId(request.UserId)))
            return Forbid();
        if (AccessActorFactory.FromPrincipal(User, organizations, permissions) is not { } actor)
            return Forbid();

        var outcome = features.Handle(actor, new UserId(request.UserId), request.Permission, request.Deny);
        return outcome.Feature is { } grant ? Ok(grant) : Rejected(outcome.Code);
    }

    [HttpPost("resource-scopes")]
    [ValidateAntiForgeryToken]
    public ActionResult<ResourceScopeGrant> GrantResource(GrantResourceRequest request)
    {
        if (!CanManagePermissions())
            return Forbid();
        if (request.UserId == Guid.Empty || request.OrganizationId == Guid.Empty)
            return BadRequest(new { code = "PERMISSION_REQUEST_INVALID" });

        var targetOrganization = new OrganizationId(request.OrganizationId);
        if (organizations.Find(targetOrganization) is null ||
            !ScopeAuthorization.CanAccessOrganization(User, targetOrganization, organizations, permissions) ||
            !CanManageTarget(new UserId(request.UserId)))
            return Forbid();
        if (AccessActorFactory.FromPrincipal(User, organizations, permissions) is not { } actor)
            return Forbid();

        var outcome = resources.Handle(actor, new UserId(request.UserId), targetOrganization, request.Deny);
        return outcome.Resource is { } grant ? Ok(grant) : Rejected(outcome.Code);
    }

    [HttpPost("device-actions")]
    [ValidateAntiForgeryToken]
    public ActionResult<DeviceActionGrant> GrantDeviceAction(GrantDeviceActionRequest request)
    {
        if (!CanManagePermissions())
            return Forbid();
        if (request.UserId == Guid.Empty || request.DeviceId == Guid.Empty || string.IsNullOrWhiteSpace(request.Action))
            return BadRequest(new { code = "PERMISSION_REQUEST_INVALID" });

        var device = devices.Find(new DeviceId(request.DeviceId));
        if (device is null ||
            !ScopeAuthorization.CanAccessDevice(User, device, organizations, permissions) ||
            !CanManageTarget(new UserId(request.UserId)))
            return Forbid();

        return Ok(actions.Handle(
            new UserId(request.UserId),
            new DeviceId(request.DeviceId),
            request.Action,
            request.Deny));
    }

    private bool CanManagePermissions() =>
        ScopeAuthorization.HasFeaturePermission(User, "permissions.manage", permissions);

    // A malformed grant is 400; a grant beyond what the caller may decide is 403. Both carry the code.
    private ObjectResult Rejected(string code) => code switch
    {
        DeviceAccessOutcomeCodes.WildcardGrantForbidden or DeviceAccessOutcomeCodes.GrantTargetIsMasterAdmin =>
            BadRequest(new { code }),
        _ => StatusCode(StatusCodes.Status403Forbidden, new { code })
    };

    private bool CanManageTarget(UserId targetUser)
    {
        if (!users.Exists(targetUser))
            return false;

        if (User.IsInRole("master-admin"))
            return true;

        var targetOrganization = users.Organization(targetUser);
        return targetOrganization.HasValue &&
               ScopeAuthorization.CanAccessOrganization(User, targetOrganization.Value, organizations, permissions);
    }
}

public sealed record GrantFeatureRequest(Guid UserId, string Permission, bool Deny = false);
public sealed record GrantResourceRequest(Guid UserId, Guid OrganizationId, bool Deny = false);
public sealed record GrantDeviceActionRequest(Guid UserId, Guid DeviceId, string Action, bool Deny = false);

public sealed record PermissionGrantSnapshot(
    UserId UserId,
    IReadOnlyList<FeaturePermissionGrant> FeatureGrants,
    IReadOnlyList<ResourceScopeGrant> ResourceGrants,
    IReadOnlyList<DeviceActionGrant> DeviceActionGrants,
    long PolicyVersion);
