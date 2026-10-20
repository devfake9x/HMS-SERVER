using Hms.Server.Application.Authorization;
using Hms.Server.Domain.Authorization;
using Hms.Server.Domain.Organizations;
using Hms.Server.Application.Management;
using Hms.Server.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hms.Server.Api.Controllers;

[ApiController]
[Authorize(Policy = "hms.user.manage")]
[Route("api/v1/users")]
public sealed class UsersController(
    IUserManagement users,
    IUserScopeStore userScopes,
    IOrganizationStore organizations,
    IPermissionStore permissions) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ManagedUserDto>>> List(CancellationToken cancellationToken)
    {
        if (!ScopeAuthorization.HasFeaturePermission(User, "users.manage", permissions))
            return Forbid();

        var managedUsers = await users.ListAsync(cancellationToken);
        if (User.IsInRole(RoleAuthorizationPolicy.MasterAdminRole))
            return Ok(managedUsers);

        return Ok(managedUsers.Where(user =>
        {
            if (!Guid.TryParse(user.Id, out var id))
                return false;
            var organization = userScopes.Organization(new UserId(id));
            return organization.HasValue &&
                   ScopeAuthorization.CanAccessOrganization(User, organization.Value, organizations, permissions);
        }).ToArray());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateManagedUserRequest request, CancellationToken cancellationToken)
    {
        if (!ScopeAuthorization.HasFeaturePermission(User, "users.manage", permissions))
            return Forbid();
        if (request.OrganizationId is null && !User.IsInRole(RoleAuthorizationPolicy.MasterAdminRole))
            return Forbid();
        if (request.OrganizationId is { } organizationId &&
            (organizations.Find(organizationId) is null ||
             !ScopeAuthorization.CanAccessOrganization(User, organizationId, organizations, permissions)))
            return Forbid();

        var (user, errors) = await users.CreateAsync(request, cancellationToken);
        return user is null ? BadRequest(new { errors }) : Created($"/api/v1/users/{user.Id}", user);
    }

    [HttpPost("roles")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateRole(CreateRoleRequest request, CancellationToken cancellationToken)
    {
        if (!ScopeAuthorization.HasFeaturePermission(User, "users.manage", permissions))
            return Forbid();
        if (RoleAuthorizationPolicy.IsReserved(request.Name) &&
            !RoleAuthorizationPolicy.CanManageReservedRole(User.IsInRole(RoleAuthorizationPolicy.MasterAdminRole)))
            return Forbid();

        var errors = await users.CreateRoleAsync(
            request.Name,
            new UserManagementActor(User.IsInRole(RoleAuthorizationPolicy.MasterAdminRole)),
            cancellationToken);
        return errors.Count == 0 ? NoContent() : BadRequest(new { errors });
    }

    [HttpPut("{userId}/roles/{role}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignRole(string userId, string role, CancellationToken cancellationToken)
    {
        if (!ScopeAuthorization.HasFeaturePermission(User, "users.manage", permissions))
            return Forbid();
        if (!Guid.TryParse(userId, out var targetId) ||
            !CanAccessTarget(new UserId(targetId)))
            return Forbid();
        if (RoleAuthorizationPolicy.IsReserved(role) &&
            !RoleAuthorizationPolicy.CanManageReservedRole(User.IsInRole(RoleAuthorizationPolicy.MasterAdminRole)))
            return Forbid();

        var errors = await users.AssignRoleAsync(
            userId,
            role,
            new UserManagementActor(User.IsInRole(RoleAuthorizationPolicy.MasterAdminRole)),
            cancellationToken);
        return errors.Count == 0 ? NoContent() : BadRequest(new { errors });
    }

    private bool CanAccessTarget(UserId targetUser)
    {
        if (!userScopes.Exists(targetUser))
            return false;
        if (User.IsInRole(RoleAuthorizationPolicy.MasterAdminRole))
            return true;

        var targetOrganization = userScopes.Organization(targetUser);
        return targetOrganization.HasValue &&
               ScopeAuthorization.CanAccessOrganization(User, targetOrganization.Value, organizations, permissions);
    }
}
