using Hms.Server.Application.Management;
using Hms.Server.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Hms.Server.Infrastructure.Persistence;

public sealed class EfProjectStore(ApplicationDbContext db) : IProjectStore
{
    public Project? Find(ProjectId id) => db.Projects.AsNoTracking().Where(x => x.Id == id.Value)
        .AsEnumerable().Select(x => Project.Rehydrate(x.Id, x.Name, x.OrganizationId)).SingleOrDefault();
    public void Add(Project project)
    {
        db.Projects.Add(new ProjectRecord { Id = project.Id.Value, Name = project.Name, OrganizationId = project.OrganizationId.Value });
        db.SaveChanges();
    }
    public void Update(Project project)
    {
        var record = db.Projects.SingleOrDefault(x => x.Id == project.Id.Value) ?? throw new InvalidOperationException("PROJECT_NOT_FOUND");
        record.Name = project.Name;
        db.SaveChanges();
    }
    public IReadOnlyList<Project> List() => db.Projects.AsNoTracking().OrderBy(x => x.Name).AsEnumerable()
        .Select(x => Project.Rehydrate(x.Id, x.Name, x.OrganizationId)).ToArray();
}

public sealed class EfPackageStore(ApplicationDbContext db) : IPackageStore
{
    public Package? Find(PackageId id) => db.Packages.AsNoTracking().Where(x => x.Id == id.Value)
        .AsEnumerable().Select(x => Package.Rehydrate(x.Id, x.Name, x.ProjectId)).SingleOrDefault();
    public void Add(Package package)
    {
        db.Packages.Add(new PackageRecord { Id = package.Id.Value, Name = package.Name, ProjectId = package.ProjectId.Value });
        db.SaveChanges();
    }
    public void Update(Package package)
    {
        var record = db.Packages.SingleOrDefault(x => x.Id == package.Id.Value) ?? throw new InvalidOperationException("PACKAGE_NOT_FOUND");
        record.Name = package.Name;
        db.SaveChanges();
    }
    public IReadOnlyList<Package> List() => db.Packages.AsNoTracking().OrderBy(x => x.Name).AsEnumerable()
        .Select(x => Package.Rehydrate(x.Id, x.Name, x.ProjectId)).ToArray();
}

public sealed class EfAreaStore(ApplicationDbContext db) : IAreaStore
{
    public Area? Find(AreaId id) => db.Areas.AsNoTracking().Where(x => x.Id == id.Value)
        .AsEnumerable().Select(x => Area.Rehydrate(x.Id, x.Name, x.OrganizationId)).SingleOrDefault();
    public void Add(Area area)
    {
        db.Areas.Add(new AreaRecord { Id = area.Id.Value, Name = area.Name, OrganizationId = area.OrganizationId.Value });
        db.SaveChanges();
    }
    public void Update(Area area)
    {
        var record = db.Areas.SingleOrDefault(x => x.Id == area.Id.Value) ?? throw new InvalidOperationException("AREA_NOT_FOUND");
        record.Name = area.Name;
        db.SaveChanges();
    }
    public IReadOnlyList<Area> List() => db.Areas.AsNoTracking().OrderBy(x => x.Name).AsEnumerable()
        .Select(x => Area.Rehydrate(x.Id, x.Name, x.OrganizationId)).ToArray();
}
