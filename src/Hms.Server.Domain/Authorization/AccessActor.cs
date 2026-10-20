using Hms.Server.Domain.Organizations;

namespace Hms.Server.Domain.Authorization;

// Who is acting: built once per request from the principal and persisted grants.
// EffectiveOrganizations is every organization for a master-admin; FeaturePermissions
// never contains the wildcard.
public sealed record AccessActor(
    UserId UserId,
    bool IsMasterAdmin,
    IReadOnlySet<OrganizationId> EffectiveOrganizations,
    IReadOnlySet<string> FeaturePermissions);
