using Hms.Server.Application.Management;
using Hms.Server.Domain.Organizations;

namespace Hms.Server.Application.Authorization;

public static class OrganizationScopeResolver
{
    public static IReadOnlySet<OrganizationId> Expand(
        IEnumerable<OrganizationId> roots,
        IReadOnlyList<Organization> organizations)
    {
        var result = roots.ToHashSet();
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var organization in organizations)
            {
                if (organization.ParentId is { } parent && result.Contains(parent) && result.Add(organization.Id))
                    changed = true;
            }
        }

        return result;
    }

    // Allowed seeds expanded to their descendants, minus the denied seeds expanded the same way.
    public static IReadOnlySet<OrganizationId> Effective(
        ResolvedOrganizationSeeds seeds,
        IReadOnlyList<Organization> organizations)
    {
        var scope = Expand(seeds.Allowed, organizations).ToHashSet();
        scope.ExceptWith(Expand(seeds.Denied, organizations));
        return scope;
    }
}
