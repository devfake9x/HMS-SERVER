using System.Collections.Frozen;
using System.Security.Claims;
using Hms.Server.Application.Authorization;
using Hms.Server.Application.Management;
using Hms.Server.Domain.Authorization;
using Hms.Server.Domain.Organizations;

namespace Hms.Server.Api.Authorization;

// The one place that turns a principal into an AccessActor and decides
// organization scope (HTTP and realtime both use it). A master-admin gets every
// organization before any grant or deny is read.
public static class AccessActorFactory
{
    // No HttpContext needed. A missing or non-Guid NameIdentifier yields no actor (fail closed).
    public static AccessActor? FromPrincipal(
        ClaimsPrincipal principal,
        IOrganizationStore organizations,
        IPermissionStore permissions)
    {
        if (!Guid.TryParse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
            return null;

        return new AccessActor(
            new UserId(userId),
            IsMasterAdmin(principal),
            EffectiveOrganizations(principal, organizations, permissions),
            PermissionGrantResolver.Features(principal, permissions).Allowed);
    }

    public static IReadOnlySet<OrganizationId> EffectiveOrganizations(
        ClaimsPrincipal principal,
        IOrganizationStore organizations,
        IPermissionStore? permissions)
    {
        var all = organizations.List();
        if (IsMasterAdmin(principal))
            return all.Select(x => x.Id).ToFrozenSet();

        return OrganizationScopeResolver.Effective(PermissionGrantResolver.Organizations(principal, permissions), all)
            .ToFrozenSet();
    }

    private static bool IsMasterAdmin(ClaimsPrincipal principal) =>
        principal.IsInRole(RoleAuthorizationPolicy.MasterAdminRole);
}
