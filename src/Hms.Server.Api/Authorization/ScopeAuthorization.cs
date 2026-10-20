using System.Security.Claims;
using Hms.Server.Application.Authorization;
using Hms.Server.Application.Management;
using Hms.Server.Domain.Devices;
using Hms.Server.Domain.Organizations;

namespace Hms.Server.Api.Authorization;

// Organization scope and feature checks for HTTP routes. Both delegate to the
// shared resolution: latest grant wins, a wildcard allow grants nothing, and a
// master-admin is decided before any deny.
internal static class ScopeAuthorization
{
    public static IReadOnlySet<OrganizationId> EffectiveOrganizations(
        ClaimsPrincipal user,
        IOrganizationStore organizations,
        IPermissionStore? permissions = null) =>
        AccessActorFactory.EffectiveOrganizations(user, organizations, permissions);

    public static bool HasFeaturePermission(
        ClaimsPrincipal user,
        string permission,
        IPermissionStore? permissions = null) =>
        PermissionGrantResolver.Features(user, permissions)
            .Has(permission, user.IsInRole(RoleAuthorizationPolicy.MasterAdminRole));

    public static bool CanAccessOrganization(
        ClaimsPrincipal user,
        OrganizationId organizationId,
        IOrganizationStore organizations,
        IPermissionStore? permissions = null) =>
        EffectiveOrganizations(user, organizations, permissions).Contains(organizationId);

    public static bool CanAccessDevice(
        ClaimsPrincipal user,
        Device device,
        IOrganizationStore organizations,
        IPermissionStore? permissions = null) =>
        CanAccessOrganization(user, device.OwnerOrganizationId, organizations, permissions);
}
