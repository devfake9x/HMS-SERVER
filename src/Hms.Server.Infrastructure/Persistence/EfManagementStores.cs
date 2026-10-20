using Hms.Server.Application.Management;
using Hms.Server.Domain.Devices;
using Hms.Server.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Hms.Server.Infrastructure.Persistence;

public sealed class EfOrganizationStore(ApplicationDbContext db) : IOrganizationStore
{
    public Organization? Find(OrganizationId id)
    {
        var record = db.Organizations.AsNoTracking().SingleOrDefault(x => x.Id == id.Value);
        return record is null ? null : Organization.Rehydrate(record.Id, record.Name, record.ParentId);
    }

    public void Add(Organization organization)
    {
        db.Organizations.Add(new OrganizationRecord
        {
            Id = organization.Id.Value,
            Name = organization.Name,
            ParentId = organization.ParentId?.Value
        });
        db.SaveChanges();
    }

    public void Update(Organization organization)
    {
        var record = db.Organizations.SingleOrDefault(x => x.Id == organization.Id.Value)
            ?? throw new InvalidOperationException("ORGANIZATION_NOT_FOUND");
        record.Name = organization.Name;
        db.SaveChanges();
    }

    public IReadOnlyList<Organization> List() => db.Organizations.AsNoTracking()
        .OrderBy(x => x.Name)
        .AsEnumerable()
        .Select(x => Organization.Rehydrate(x.Id, x.Name, x.ParentId))
        .ToArray();
}

public sealed class EfDeviceStore(ApplicationDbContext db) : IDeviceStore
{
    public Device? Find(DeviceId id)
    {
        var record = db.Devices.AsNoTracking().SingleOrDefault(x => x.Id == id.Value);
        return record is null ? null : ToDomain(record);
    }

    public void Add(Device device)
    {
        db.Devices.Add(new DeviceRecord
        {
            Id = device.Id.Value,
            Name = device.Name,
            OwnerOrganizationId = device.OwnerOrganizationId.Value,
            ProjectId = device.ProjectId,
            PackageId = device.PackageId,
            AreaId = device.AreaId,
            State = device.State.ToString(),
            KeyGeneration = device.KeyGeneration
        });
        db.SaveChanges();
    }

    public void Update(Device device)
    {
        var record = db.Devices.SingleOrDefault(x => x.Id == device.Id.Value)
            ?? throw new InvalidOperationException("DEVICE_NOT_FOUND");

        record.Name = device.Name;
        record.ProjectId = device.ProjectId;
        record.PackageId = device.PackageId;
        record.AreaId = device.AreaId;
        record.State = device.State.ToString();
        record.KeyGeneration = device.KeyGeneration;
        db.SaveChanges();
    }

    public IReadOnlyList<Device> List() => db.Devices.AsNoTracking()
        .OrderBy(x => x.Name)
        .AsEnumerable()
        .Select(ToDomain)
        .ToArray();

    private static Device ToDomain(DeviceRecord record) => Device.Rehydrate(
        record.Id,
        record.Name,
        new OrganizationId(record.OwnerOrganizationId),
        record.ProjectId,
        record.PackageId,
        record.AreaId,
        Enum.TryParse<DeviceLifecycleState>(record.State, out var state)
            ? state
            : DeviceLifecycleState.Provisioning,
        record.KeyGeneration);
}

