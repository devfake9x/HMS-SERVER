using Hms.Server.Domain.Organizations;

namespace Hms.Server.Domain.Devices;

public readonly record struct DeviceId(Guid Value)
{
    public static DeviceId New() => new(Guid.NewGuid());
}

public enum DeviceLifecycleState
{
    Provisioning,
    Active,
    Revoked
}

public sealed class Device
{
    private Device(DeviceId id, string name, OrganizationId ownerOrganizationId)
    {
        Id = id;
        Name = name;
        OwnerOrganizationId = ownerOrganizationId;
        State = DeviceLifecycleState.Provisioning;
    }

    public DeviceId Id { get; }
    public string Name { get; private set; }
    public OrganizationId OwnerOrganizationId { get; }
    public Guid? ProjectId { get; private set; }
    public Guid? PackageId { get; private set; }
    public Guid? AreaId { get; private set; }
    public DeviceLifecycleState State { get; private set; }
    public int KeyGeneration { get; private set; } = 1;

    public static Device Create(string name, OrganizationId ownerOrganizationId)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Device name is required.", nameof(name));
        }

        return new(DeviceId.New(), name.Trim(), ownerOrganizationId);
    }

    public static Device Rehydrate(
        Guid id,
        string name,
        OrganizationId ownerOrganizationId,
        Guid? projectId,
        Guid? packageId,
        Guid? areaId,
        DeviceLifecycleState state,
        int keyGeneration)
    {
        var device = new Device(new DeviceId(id), name, ownerOrganizationId)
        {
            ProjectId = projectId,
            PackageId = packageId,
            AreaId = areaId,
            State = state,
            KeyGeneration = keyGeneration
        };
        return device;
    }

    public void SetClassification(Guid? projectId, Guid? packageId, Guid? areaId)
    {
        ProjectId = projectId;
        PackageId = packageId;
        AreaId = areaId;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Device name is required.", nameof(name));
        Name = name.Trim();
    }

    public void Activate()
    {
        if (State == DeviceLifecycleState.Revoked)
        {
            throw new InvalidOperationException("A revoked device cannot be activated.");
        }

        State = DeviceLifecycleState.Active;
    }

    public void Revoke() => State = DeviceLifecycleState.Revoked;

    public void RotateKeyGeneration()
    {
        if (State == DeviceLifecycleState.Revoked)
        {
            throw new InvalidOperationException("A revoked device cannot rotate keys.");
        }

        checked
        {
            KeyGeneration++;
        }
    }
}
