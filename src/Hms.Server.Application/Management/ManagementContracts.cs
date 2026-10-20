using Hms.Server.Domain.Devices;
using Hms.Server.Domain.Organizations;

namespace Hms.Server.Application.Management;

public sealed record CreateOrganizationRequest(string Name, OrganizationId? ParentId);
public sealed record UpdateOrganizationRequest(string Name);
public sealed record OrganizationDto(Guid Id, string Name, Guid? ParentId);

public sealed record CreateDeviceRequest(
    string Name,
    OrganizationId OwnerOrganizationId,
    Guid? ProjectId,
    Guid? PackageId,
    Guid? AreaId);
public sealed record UpdateDeviceRequest(string Name, Guid? ProjectId, Guid? PackageId, Guid? AreaId);

public sealed record CreateProjectRequest(string Name, OrganizationId OrganizationId);
public sealed record UpdateProjectRequest(string Name);
public sealed record ProjectDto(Guid Id, string Name, Guid OrganizationId);
public sealed record CreatePackageRequest(string Name, ProjectId ProjectId);
public sealed record UpdatePackageRequest(string Name);
public sealed record PackageDto(Guid Id, string Name, Guid ProjectId);
public sealed record CreateAreaRequest(string Name, OrganizationId OrganizationId);
public sealed record UpdateAreaRequest(string Name);
public sealed record AreaDto(Guid Id, string Name, Guid OrganizationId);

public sealed record DeviceDto(
    Guid Id,
    string Name,
    Guid OwnerOrganizationId,
    Guid? ProjectId,
    Guid? PackageId,
    Guid? AreaId,
    DeviceLifecycleState State,
    int KeyGeneration);

public interface IOrganizationStore
{
    Organization? Find(OrganizationId id);
    void Add(Organization organization);
    void Update(Organization organization);
    IReadOnlyList<Organization> List();
}

public interface IDeviceStore
{
    Device? Find(DeviceId id);
    void Add(Device device);
    void Update(Device device);
    IReadOnlyList<Device> List();
}

public interface IProjectStore
{
    Project? Find(ProjectId id);
    void Add(Project project);
    void Update(Project project);
    IReadOnlyList<Project> List();
}

public interface IPackageStore
{
    Package? Find(PackageId id);
    void Add(Package package);
    void Update(Package package);
    IReadOnlyList<Package> List();
}

public interface IAreaStore
{
    Area? Find(AreaId id);
    void Add(Area area);
    void Update(Area area);
    IReadOnlyList<Area> List();
}

public sealed class InMemoryOrganizationStore : IOrganizationStore
{
    private readonly Dictionary<OrganizationId, Organization> _items = [];

    public Organization? Find(OrganizationId id) => _items.GetValueOrDefault(id);
    public void Add(Organization organization) => _items.Add(organization.Id, organization);
    public void Update(Organization organization) => _items[organization.Id] = organization;
    public IReadOnlyList<Organization> List() => _items.Values.ToArray();
}

public sealed class InMemoryDeviceStore : IDeviceStore
{
    private readonly Dictionary<DeviceId, Device> _items = [];

    public Device? Find(DeviceId id) => _items.GetValueOrDefault(id);
    public void Add(Device device) => _items.Add(device.Id, device);
    public void Update(Device device) => _items[device.Id] = device;
    public IReadOnlyList<Device> List() => _items.Values.ToArray();
}

public sealed class InMemoryProjectStore : IProjectStore
{
    private readonly Dictionary<ProjectId, Project> _items = [];
    public Project? Find(ProjectId id) => _items.GetValueOrDefault(id);
    public void Add(Project project) => _items.Add(project.Id, project);
    public void Update(Project project) => _items[project.Id] = project;
    public IReadOnlyList<Project> List() => _items.Values.ToArray();
}

public sealed class InMemoryPackageStore : IPackageStore
{
    private readonly Dictionary<PackageId, Package> _items = [];
    public Package? Find(PackageId id) => _items.GetValueOrDefault(id);
    public void Add(Package package) => _items.Add(package.Id, package);
    public void Update(Package package) => _items[package.Id] = package;
    public IReadOnlyList<Package> List() => _items.Values.ToArray();
}

public sealed class InMemoryAreaStore : IAreaStore
{
    private readonly Dictionary<AreaId, Area> _items = [];
    public Area? Find(AreaId id) => _items.GetValueOrDefault(id);
    public void Add(Area area) => _items.Add(area.Id, area);
    public void Update(Area area) => _items[area.Id] = area;
    public IReadOnlyList<Area> List() => _items.Values.ToArray();
}

public sealed class CreateOrganizationHandler(IOrganizationStore organizations)
{
    public OrganizationDto Handle(CreateOrganizationRequest request)
    {
        if (request.ParentId is { } parentId && organizations.Find(parentId) is null)
        {
            throw new InvalidOperationException("PARENT_ORGANIZATION_NOT_FOUND");
        }

        var organization = Organization.Create(request.Name, request.ParentId);
        organizations.Add(organization);
        return new(organization.Id.Value, organization.Name, organization.ParentId?.Value);
    }
}

public sealed class CreateProjectHandler(IOrganizationStore organizations, IProjectStore projects)
{
    public ProjectDto Handle(CreateProjectRequest request)
    {
        if (organizations.Find(request.OrganizationId) is null) throw new InvalidOperationException("ORGANIZATION_NOT_FOUND");
        var project = Project.Create(request.Name, request.OrganizationId);
        projects.Add(project);
        return new(project.Id.Value, project.Name, project.OrganizationId.Value);
    }
}

public sealed class CreatePackageHandler(IProjectStore projects, IPackageStore packages)
{
    public PackageDto Handle(CreatePackageRequest request)
    {
        if (projects.Find(request.ProjectId) is null) throw new InvalidOperationException("PROJECT_NOT_FOUND");
        var package = Package.Create(request.Name, request.ProjectId);
        packages.Add(package);
        return new(package.Id.Value, package.Name, package.ProjectId.Value);
    }
}

public sealed class CreateAreaHandler(IOrganizationStore organizations, IAreaStore areas)
{
    public AreaDto Handle(CreateAreaRequest request)
    {
        if (organizations.Find(request.OrganizationId) is null) throw new InvalidOperationException("ORGANIZATION_NOT_FOUND");
        var area = Area.Create(request.Name, request.OrganizationId);
        areas.Add(area);
        return new(area.Id.Value, area.Name, area.OrganizationId.Value);
    }
}

public sealed class CreateDeviceHandler
{
    private readonly IOrganizationStore _organizations;
    private readonly IDeviceStore _devices;
    private readonly IProjectStore? _projects;
    private readonly IPackageStore? _packages;
    private readonly IAreaStore? _areas;

    public CreateDeviceHandler(IOrganizationStore organizations, IDeviceStore devices)
        : this(organizations, devices, null, null, null) { }

    public CreateDeviceHandler(
        IOrganizationStore organizations,
        IDeviceStore devices,
        IProjectStore? projects,
        IPackageStore? packages,
        IAreaStore? areas)
    {
        _organizations = organizations;
        _devices = devices;
        _projects = projects;
        _packages = packages;
        _areas = areas;
    }

    public DeviceDto Handle(CreateDeviceRequest request)
    {
        if (_organizations.Find(request.OwnerOrganizationId) is null)
        {
            throw new InvalidOperationException("OWNER_ORGANIZATION_NOT_FOUND");
        }

        Project? project = null;
        if (request.ProjectId is { } projectId)
        {
            project = _projects?.Find(new ProjectId(projectId)) ?? throw new InvalidOperationException("PROJECT_NOT_FOUND");
            if (project.OrganizationId != request.OwnerOrganizationId)
                throw new InvalidOperationException("PROJECT_ORGANIZATION_MISMATCH");
        }

        if (request.PackageId is { } packageId)
        {
            var package = _packages?.Find(new PackageId(packageId)) ?? throw new InvalidOperationException("PACKAGE_NOT_FOUND");
            if (project is not null && package.ProjectId != project.Id)
                throw new InvalidOperationException("PACKAGE_PROJECT_MISMATCH");
            if (project is null && _projects?.Find(package.ProjectId)?.OrganizationId != request.OwnerOrganizationId)
                throw new InvalidOperationException("PACKAGE_ORGANIZATION_MISMATCH");
        }

        if (request.AreaId is { } areaId)
        {
            var area = _areas?.Find(new AreaId(areaId)) ?? throw new InvalidOperationException("AREA_NOT_FOUND");
            if (area.OrganizationId != request.OwnerOrganizationId)
                throw new InvalidOperationException("AREA_ORGANIZATION_MISMATCH");
        }

        var device = Device.Create(request.Name, request.OwnerOrganizationId);
        device.SetClassification(request.ProjectId, request.PackageId, request.AreaId);
        _devices.Add(device);
        return ToDto(device);
    }

    public static DeviceDto ToDto(Device device) => new(
        device.Id.Value,
        device.Name,
        device.OwnerOrganizationId.Value,
        device.ProjectId,
        device.PackageId,
        device.AreaId,
        device.State,
        device.KeyGeneration);
}

public sealed class UpdateOrganizationHandler(IOrganizationStore organizations)
{
    public OrganizationDto Handle(OrganizationId id, UpdateOrganizationRequest request)
    {
        var item = organizations.Find(id) ?? throw new InvalidOperationException("ORGANIZATION_NOT_FOUND");
        item.Rename(request.Name); organizations.Update(item);
        return new(item.Id.Value, item.Name, item.ParentId?.Value);
    }
}

public sealed class UpdateProjectHandler(IProjectStore projects)
{
    public ProjectDto Handle(ProjectId id, UpdateProjectRequest request)
    {
        var item = projects.Find(id) ?? throw new InvalidOperationException("PROJECT_NOT_FOUND");
        item.Rename(request.Name); projects.Update(item);
        return new(item.Id.Value, item.Name, item.OrganizationId.Value);
    }
}

public sealed class UpdatePackageHandler(IPackageStore packages)
{
    public PackageDto Handle(PackageId id, UpdatePackageRequest request)
    {
        var item = packages.Find(id) ?? throw new InvalidOperationException("PACKAGE_NOT_FOUND");
        item.Rename(request.Name); packages.Update(item);
        return new(item.Id.Value, item.Name, item.ProjectId.Value);
    }
}

public sealed class UpdateAreaHandler(IAreaStore areas)
{
    public AreaDto Handle(AreaId id, UpdateAreaRequest request)
    {
        var item = areas.Find(id) ?? throw new InvalidOperationException("AREA_NOT_FOUND");
        item.Rename(request.Name); areas.Update(item);
        return new(item.Id.Value, item.Name, item.OrganizationId.Value);
    }
}

public sealed class UpdateDeviceHandler(
    IDeviceStore devices, IProjectStore projects, IPackageStore packages, IAreaStore areas)
{
    public DeviceDto Handle(DeviceId id, UpdateDeviceRequest request)
    {
        var device = devices.Find(id) ?? throw new InvalidOperationException("DEVICE_NOT_FOUND");
        Project? project = null;
        if (request.ProjectId is { } projectId)
        {
            project = projects.Find(new ProjectId(projectId)) ?? throw new InvalidOperationException("PROJECT_NOT_FOUND");
            if (project.OrganizationId != device.OwnerOrganizationId) throw new InvalidOperationException("PROJECT_ORGANIZATION_MISMATCH");
        }
        if (request.PackageId is { } packageId)
        {
            var package = packages.Find(new PackageId(packageId)) ?? throw new InvalidOperationException("PACKAGE_NOT_FOUND");
            var packageProject = projects.Find(package.ProjectId);
            if (project is not null && package.ProjectId != project.Id) throw new InvalidOperationException("PACKAGE_PROJECT_MISMATCH");
            if (project is null && (packageProject is null || packageProject.OrganizationId != device.OwnerOrganizationId)) throw new InvalidOperationException("PACKAGE_ORGANIZATION_MISMATCH");
        }
        if (request.AreaId is { } areaId)
        {
            var area = areas.Find(new AreaId(areaId)) ?? throw new InvalidOperationException("AREA_NOT_FOUND");
            if (area.OrganizationId != device.OwnerOrganizationId) throw new InvalidOperationException("AREA_ORGANIZATION_MISMATCH");
        }
        device.Rename(request.Name); device.SetClassification(request.ProjectId, request.PackageId, request.AreaId); devices.Update(device);
        return CreateDeviceHandler.ToDto(device);
    }
}
