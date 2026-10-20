using System.Collections.Frozen;
using System.Security.Claims;
using Hms.Server.Domain.Authorization;
using Hms.Server.Domain.Organizations;

namespace Hms.Server.Application.Authorization;

public sealed record ResolvedFeaturePermissions(IReadOnlySet<string> Allowed, IReadOnlySet<string> Denied)
{
    // A master-admin is decided before any deny is read.
    public bool Has(string permission, bool isMasterAdmin) =>
        isMasterAdmin ||
        (!Denied.Contains(PermissionGrantResolver.Wildcard) &&
         !Denied.Contains(permission) &&
         Allowed.Contains(permission));
}

public sealed record ResolvedOrganizationSeeds(IReadOnlySet<OrganizationId> Allowed, IReadOnlySet<OrganizationId> Denied);

// The only latest-wins implementation for feature and resource grants. Per key
// the row with the highest PolicyVersion decides; on an equal version the deny
// wins. Claims are a test-only baseline: they decide only keys without a
// persisted row, and a claim deny beats a claim allow. "*" never grants;
// Denied keeps it so a deny "*" still blocks a non-master.
public static class PermissionGrantResolver
{
    public const string Wildcard = "*";

    public static IReadOnlyList<FeaturePermissionGrant> LatestFeatureGrants(IEnumerable<FeaturePermissionGrant> grants) =>
        Latest(grants, x => (x.UserId, x.Permission), x => x.PolicyVersion, x => x.Deny);

    public static IReadOnlyList<ResourceScopeGrant> LatestResourceGrants(IEnumerable<ResourceScopeGrant> grants) =>
        Latest(grants, x => (x.UserId, x.OrganizationId), x => x.PolicyVersion, x => x.Deny);

    public static ResolvedFeaturePermissions Features(
        IEnumerable<string> claimAllow,
        IEnumerable<string> claimDeny,
        IEnumerable<FeaturePermissionGrant> grants)
    {
        var (allowed, denied) = Resolve(
            claimAllow,
            claimDeny,
            LatestFeatureGrants(grants).Select(x => (x.Permission, x.Deny)),
            StringComparer.Ordinal);
        allowed.Remove(Wildcard);
        if (denied.Contains(Wildcard))
            allowed.Clear();
        return new(allowed.ToFrozenSet(StringComparer.Ordinal), denied.ToFrozenSet(StringComparer.Ordinal));
    }

    public static ResolvedOrganizationSeeds Organizations(
        IEnumerable<OrganizationId> claimAllow,
        IEnumerable<OrganizationId> claimDeny,
        IEnumerable<ResourceScopeGrant> grants)
    {
        var (allowed, denied) = Resolve(
            claimAllow,
            claimDeny,
            LatestResourceGrants(grants).Select(x => (x.OrganizationId, x.Deny)),
            EqualityComparer<OrganizationId>.Default);
        return new(allowed.ToFrozenSet(), denied.ToFrozenSet());
    }

    // The only place that knows the claim names: perm/deny-perm and org-scope/deny-org-scope.
    public static ResolvedFeaturePermissions Features(ClaimsPrincipal principal, IPermissionStore? permissions) =>
        Features(
            principal.FindAll("perm").Select(x => x.Value),
            principal.FindAll("deny-perm").Select(x => x.Value),
            UserIdOf(principal) is { } userId && permissions is not null ? permissions.FeatureGrants(userId) : []);

    public static ResolvedOrganizationSeeds Organizations(ClaimsPrincipal principal, IPermissionStore? permissions) =>
        Organizations(
            OrganizationClaims(principal, "org-scope"),
            OrganizationClaims(principal, "deny-org-scope"),
            UserIdOf(principal) is { } userId && permissions is not null ? permissions.ResourceGrants(userId) : []);

    private static (HashSet<TKey> Allowed, HashSet<TKey> Denied) Resolve<TKey>(
        IEnumerable<TKey> claimAllow,
        IEnumerable<TKey> claimDeny,
        IEnumerable<(TKey Key, bool Deny)> latestRows,
        IEqualityComparer<TKey> comparer)
    {
        var rows = latestRows.ToArray();
        var persisted = rows.Select(x => x.Key).ToHashSet(comparer);
        var denied = claimDeny.Where(x => !persisted.Contains(x)).ToHashSet(comparer);
        var allowed = claimAllow.Where(x => !persisted.Contains(x) && !denied.Contains(x)).ToHashSet(comparer);
        foreach (var (key, deny) in rows)
            (deny ? denied : allowed).Add(key);
        return (allowed, denied);
    }

    private static IReadOnlyList<TGrant> Latest<TGrant, TKey>(
        IEnumerable<TGrant> grants,
        Func<TGrant, TKey> key,
        Func<TGrant, long> version,
        Func<TGrant, bool> deny) =>
        grants.GroupBy(key)
            .Select(group => group.OrderByDescending(version).ThenByDescending(deny).First())
            .OrderBy(version)
            .ToArray();

    private static UserId? UserIdOf(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? new UserId(id) : null;

    private static IEnumerable<OrganizationId> OrganizationClaims(ClaimsPrincipal principal, string type) =>
        principal.FindAll(type)
            .Select(x => Guid.TryParse(x.Value, out var id) ? new OrganizationId(id) : (OrganizationId?)null)
            .Where(x => x.HasValue)
            .Select(x => x!.Value);
}
