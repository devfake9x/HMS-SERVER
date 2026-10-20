using System.Collections.Frozen;
using Hms.Server.Domain.Organizations;

namespace Hms.Server.Domain.Authorization;

public readonly record struct DeviceRoleId(Guid Value)
{
    public static DeviceRoleId New() => new(Guid.NewGuid());
}

public sealed class DeviceRole
{
    public const int NameMaxLength = 128;
    public const int DescriptionMaxLength = 512;
    public const int PermissionMaxLength = 160;

    private DeviceRole(
        DeviceRoleId id,
        OrganizationId organizationId,
        string name,
        string? description,
        FrozenSet<string> permissions,
        int version)
    {
        Id = id;
        OrganizationId = organizationId;
        Name = name;
        Description = description;
        Permissions = permissions;
        Version = version;
    }

    public DeviceRoleId Id { get; }
    public OrganizationId OrganizationId { get; }
    public string Name { get; private set; }
    public string? Description { get; private set; }

    // Key of the per-organization name uniqueness. Only the role stores compare
    // it, so one place decides whether two names collide.
    public string NormalizedName => Name.Trim().ToUpperInvariant();

    public IReadOnlySet<string> Permissions { get; private set; }
    public int Version { get; private set; }

    public static DeviceRole Create(
        OrganizationId organizationId,
        string name,
        string? description,
        IEnumerable<string> permissions) =>
        new(
            DeviceRoleId.New(),
            organizationId,
            ValidName(name),
            ValidDescription(description),
            ValidPermissions(permissions),
            1);

    public static DeviceRole Rehydrate(
        Guid id,
        Guid organizationId,
        string name,
        string? description,
        IEnumerable<string> permissions,
        int version) =>
        new(
            new DeviceRoleId(id),
            new OrganizationId(organizationId),
            name,
            description,
            permissions.ToFrozenSet(StringComparer.Ordinal),
            version);

    public void Update(string name, string? description, IEnumerable<string> permissions)
    {
        // Validate everything first so a rejected update leaves the role unchanged.
        var validName = ValidName(name);
        var validDescription = ValidDescription(description);
        var validPermissions = ValidPermissions(permissions);
        var nextVersion = checked(Version + 1);

        Name = validName;
        Description = validDescription;
        Permissions = validPermissions;
        Version = nextVersion;
    }

    private static string ValidName(string name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > NameMaxLength)
        {
            throw new ArgumentException(
                $"Device role name is required and at most {NameMaxLength} characters.", nameof(name));
        }

        return trimmed;
    }

    private static string? ValidDescription(string? description)
    {
        var trimmed = description?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length > DescriptionMaxLength)
        {
            throw new ArgumentException(
                $"Device role description is at most {DescriptionMaxLength} characters.", nameof(description));
        }

        return trimmed;
    }

    // Form only. Whether a code is in the vocabulary or high-risk is decided
    // by the handlers, which know the catalog.
    private static FrozenSet<string> ValidPermissions(IEnumerable<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        var codes = permissions.ToArray();
        foreach (var code in codes)
        {
            if (string.IsNullOrWhiteSpace(code) || code.Length > PermissionMaxLength)
            {
                throw new ArgumentException(
                    $"Each permission code is required and at most {PermissionMaxLength} characters.",
                    nameof(permissions));
            }
        }

        return codes.ToFrozenSet(StringComparer.Ordinal);
    }
}
