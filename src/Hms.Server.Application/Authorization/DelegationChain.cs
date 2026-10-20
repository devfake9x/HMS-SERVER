using System.Collections.Frozen;
using Hms.Server.Domain.Authorization;
using Hms.Server.Domain.Devices;
using Hms.Server.Domain.Organizations;

namespace Hms.Server.Application.Authorization;

// Delegated authority never exceeds the authority it came from. A non-master
// admin's appointer is whoever granted their latest permissions.manage; the
// chain follows appointers upward and stops at a master-admin, at a row without
// a grantor, or at a repeat. An admin never acts on anyone in their chain, and
// hands out only what every appointer in it also holds.
internal static class DelegationChain
{
    private const string PermissionsManage = "permissions.manage";
    private const int MaxDepth = 32;

    public static IReadOnlyList<UserId> Of(AccessActor actor, IPermissionStore permissions, IUserScopeStore users)
    {
        if (actor.IsMasterAdmin)
            return [];

        var chain = new List<UserId>();
        var seen = new HashSet<UserId> { actor.UserId };
        var current = actor.UserId;
        while (chain.Count < MaxDepth)
        {
            var appointment = PermissionGrantResolver
                .LatestFeatureGrants(permissions.FeatureGrants(current).Where(x => x.Permission == PermissionsManage))
                .FirstOrDefault();
            if (appointment is not { Deny: false, GrantedByMasterAdmin: false, GrantedBy: { } appointer } ||
                !seen.Add(appointer) ||
                users.IsMasterAdmin(appointer))
                break;
            chain.Add(appointer);
            current = appointer;
        }

        return chain;
    }

    public static string? CheckTarget(IReadOnlyList<UserId> chain, UserId target) =>
        chain.Contains(target) ? DeviceAccessOutcomeCodes.GrantToDelegatorForbidden : null;

    // Features the actor may hand out: their own, cut to what each appointer holds.
    public static IReadOnlySet<string> Features(
        AccessActor actor,
        IReadOnlyList<UserId> chain,
        IPermissionStore permissions) =>
        chain.Aggregate((IEnumerable<string>)actor.FeaturePermissions, (held, appointer) =>
                held.Intersect(PermissionGrantResolver.Features([], [], permissions.FeatureGrants(appointer)).Allowed))
            .ToFrozenSet(StringComparer.Ordinal);

    // Organizations the actor may grant or deny: their own scope, cut to each appointer's.
    public static IReadOnlySet<OrganizationId> Organizations(
        AccessActor actor,
        IReadOnlyList<UserId> chain,
        IPermissionStore permissions,
        IReadOnlyList<Organization> organizations) =>
        chain.Aggregate((IEnumerable<OrganizationId>)actor.EffectiveOrganizations, (held, appointer) =>
                held.Intersect(OrganizationScopeResolver.Effective(
                    PermissionGrantResolver.Organizations([], [], permissions.ResourceGrants(appointer)),
                    organizations)))
            .ToFrozenSet();

    // Device permissions the actor may hand out: their role on the device, cut to each
    // appointer's role there (an appointer without a role there leaves nothing).
    public static IReadOnlySet<string> DevicePermissions(
        IReadOnlySet<string> own,
        IReadOnlyList<UserId> chain,
        Device device,
        IDeviceAccessResolver resolver) =>
        chain.Aggregate((IEnumerable<string>)own, (held, appointer) =>
                held.Intersect(resolver.Resolve(MembershipOnly(appointer), device).Permissions))
            .ToFrozenSet(StringComparer.Ordinal);

    private static AccessActor MembershipOnly(UserId user) =>
        new(user, false, FrozenSet<OrganizationId>.Empty, FrozenSet<string>.Empty);
}
