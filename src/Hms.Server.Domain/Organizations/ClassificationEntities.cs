namespace Hms.Server.Domain.Organizations;

public readonly record struct ProjectId(Guid Value)
{
    public static ProjectId New() => new(Guid.NewGuid());
}

public readonly record struct PackageId(Guid Value)
{
    public static PackageId New() => new(Guid.NewGuid());
}

public readonly record struct AreaId(Guid Value)
{
    public static AreaId New() => new(Guid.NewGuid());
}

public sealed class Project
{
    private Project(ProjectId id, string name, OrganizationId organizationId)
    {
        Id = id;
        Name = name;
        OrganizationId = organizationId;
    }

    public ProjectId Id { get; }
    public string Name { get; private set; }
    public OrganizationId OrganizationId { get; }

    public static Project Create(string name, OrganizationId organizationId)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Project name is required.", nameof(name));
        return new(ProjectId.New(), name.Trim(), organizationId);
    }

    public static Project Rehydrate(Guid id, string name, Guid organizationId) =>
        new(new ProjectId(id), name, new OrganizationId(organizationId));

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Project name is required.", nameof(name));
        Name = name.Trim();
    }
}

public sealed class Package
{
    private Package(PackageId id, string name, ProjectId projectId)
    {
        Id = id;
        Name = name;
        ProjectId = projectId;
    }

    public PackageId Id { get; }
    public string Name { get; private set; }
    public ProjectId ProjectId { get; }

    public static Package Create(string name, ProjectId projectId)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Package name is required.", nameof(name));
        return new(PackageId.New(), name.Trim(), projectId);
    }

    public static Package Rehydrate(Guid id, string name, Guid projectId) =>
        new(new PackageId(id), name, new ProjectId(projectId));

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Package name is required.", nameof(name));
        Name = name.Trim();
    }
}

public sealed class Area
{
    private Area(AreaId id, string name, OrganizationId organizationId)
    {
        Id = id;
        Name = name;
        OrganizationId = organizationId;
    }

    public AreaId Id { get; }
    public string Name { get; private set; }
    public OrganizationId OrganizationId { get; }

    public static Area Create(string name, OrganizationId organizationId)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Area name is required.", nameof(name));
        return new(AreaId.New(), name.Trim(), organizationId);
    }

    public static Area Rehydrate(Guid id, string name, Guid organizationId) =>
        new(new AreaId(id), name, new OrganizationId(organizationId));

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Area name is required.", nameof(name));
        Name = name.Trim();
    }
}
