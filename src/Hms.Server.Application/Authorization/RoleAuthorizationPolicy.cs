namespace Hms.Server.Application.Authorization;

/// <summary>Guards the reserved roles that carry server-wide authority.</summary>
public static class RoleAuthorizationPolicy
{
    public const string MasterAdminRole = "master-admin";

    // Compared the way ASP.NET Identity's default normalizer compares role names
    // (NFC, then upper-invariant): any name it folds onto the reserved role is the
    // reserved role, whatever an ordinal ignore-case compare says (U+017F is one).
    public static bool IsReserved(string? role) =>
        role is not null &&
        string.Equals(Normalize(role.Trim()), Normalize(MasterAdminRole), StringComparison.Ordinal);

    public static bool CanManageReservedRole(bool actorIsMasterAdmin) => actorIsMasterAdmin;

    private static string Normalize(string name) => name.Normalize().ToUpperInvariant();
}
