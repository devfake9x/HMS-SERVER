namespace Hms.Server.Domain.Organizations;

public readonly record struct OrganizationId(Guid Value)
{
    public static OrganizationId New() => new(Guid.NewGuid());
}

public sealed class Organization
{
    private Organization(OrganizationId id, string name, OrganizationId? parentId)
    {
        Id = id;
        Name = name;
        ParentId = parentId;
    }

    public OrganizationId Id { get; }
    public string Name { get; private set; }
    public OrganizationId? ParentId { get; }

    public static Organization Create(string name, OrganizationId? parentId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Organization name is required.", nameof(name));
        }

        return new(OrganizationId.New(), name.Trim(), parentId);
    }

    public static Organization Rehydrate(Guid id, string name, Guid? parentId) =>
        new(new OrganizationId(id), name, parentId is null ? null : new OrganizationId(parentId.Value));

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Organization name is required.", nameof(name));
        }

        Name = name.Trim();
    }
}
