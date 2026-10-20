using Hms.Server.Application.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace Hms.Server.Infrastructure.Authorization;

/// <summary>
/// Evaluates mutable feature permissions at the authorization boundary so a
/// request without the permission is rejected before anti-forgery filters run.
/// Controllers still perform final target and scope checks.
/// </summary>
public sealed record FeaturePermissionRequirement(string Permission) : IAuthorizationRequirement;

public sealed class FeaturePermissionAuthorizationHandler(IPermissionStore permissions)
    : AuthorizationHandler<FeaturePermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        FeaturePermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
            return Task.CompletedTask;

        // Same resolution as ScopeAuthorization: latest grant wins, a wildcard allow grants
        // nothing, and a master-admin succeeds before any deny is read.
        if (PermissionGrantResolver.Features(context.User, permissions)
            .Has(requirement.Permission, context.User.IsInRole(RoleAuthorizationPolicy.MasterAdminRole)))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
