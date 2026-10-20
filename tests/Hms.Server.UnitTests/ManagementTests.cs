using Hms.Server.Application.Management;
using Hms.Server.Domain.Devices;
using Hms.Server.Domain.Organizations;

namespace Hms.Server.UnitTests;

public sealed class ManagementTests
{
    [Fact]
    public void Device_creation_requires_owner_and_keeps_classifications_nullable()
    {
        var organizations = new InMemoryOrganizationStore();
        var devices = new InMemoryDeviceStore();
        var organization = new CreateOrganizationHandler(organizations)
            .Handle(new CreateOrganizationRequest("Master", null));
        var handler = new CreateDeviceHandler(organizations, devices);

        var device = handler.Handle(new CreateDeviceRequest(
            "ESP-001",
            new OrganizationId(organization.Id),
            null,
            null,
            null));

        Assert.Equal(organization.Id, device.OwnerOrganizationId);
        Assert.Null(device.ProjectId);
        Assert.Null(device.PackageId);
        Assert.Null(device.AreaId);
    }

    [Fact]
    public void Child_organization_requires_an_existing_parent()
    {
        var organizations = new InMemoryOrganizationStore();
        var handler = new CreateOrganizationHandler(organizations);

        var error = Assert.Throws<InvalidOperationException>(() => handler.Handle(
            new CreateOrganizationRequest("Child", OrganizationId.New())));

        Assert.Equal("PARENT_ORGANIZATION_NOT_FOUND", error.Message);
    }

    [Fact]
    public void Classifications_must_exist_before_device_assignment()
    {
        var organizations = new InMemoryOrganizationStore();
        var projects = new InMemoryProjectStore();
        var packages = new InMemoryPackageStore();
        var areas = new InMemoryAreaStore();
        var devices = new InMemoryDeviceStore();
        var organization = Organization.Create("Master");
        organizations.Add(organization);
        var handler = new CreateDeviceHandler(organizations, devices, projects, packages, areas);

        var error = Assert.Throws<InvalidOperationException>(() => handler.Handle(new CreateDeviceRequest(
            "ESP-001", organization.Id, Guid.NewGuid(), null, null)));

        Assert.Equal("PROJECT_NOT_FOUND", error.Message);
    }

    [Fact]
    public void Device_classification_must_stay_inside_owner_organization()
    {
        var organizations = new InMemoryOrganizationStore();
        var projects = new InMemoryProjectStore();
        var devices = new InMemoryDeviceStore();
        var first = Organization.Create("First");
        var second = Organization.Create("Second");
        organizations.Add(first);
        organizations.Add(second);
        var project = new CreateProjectHandler(organizations, projects).Handle(new CreateProjectRequest("Project", second.Id));
        var handler = new CreateDeviceHandler(organizations, devices, projects, null, null);

        var error = Assert.Throws<InvalidOperationException>(() => handler.Handle(new CreateDeviceRequest(
            "ESP-001", first.Id, project.Id, null, null)));

        Assert.Equal("PROJECT_ORGANIZATION_MISMATCH", error.Message);
    }

    [Fact]
    public void Device_update_validates_classification_and_preserves_owner()
    {
        var organizations = new InMemoryOrganizationStore();
        var projects = new InMemoryProjectStore();
        var packages = new InMemoryPackageStore();
        var areas = new InMemoryAreaStore();
        var devices = new InMemoryDeviceStore();
        var owner = Organization.Create("Owner");
        var other = Organization.Create("Other");
        organizations.Add(owner);
        organizations.Add(other);
        var ownerProject = new CreateProjectHandler(organizations, projects)
            .Handle(new CreateProjectRequest("Owner project", owner.Id));
        var otherProject = new CreateProjectHandler(organizations, projects)
            .Handle(new CreateProjectRequest("Other project", other.Id));
        var device = Device.Create("ESP-001", owner.Id);
        devices.Add(device);
        var handler = new UpdateDeviceHandler(devices, projects, packages, areas);

        var mismatch = Assert.Throws<InvalidOperationException>(() => handler.Handle(
            device.Id,
            new UpdateDeviceRequest("edited", otherProject.Id, null, null)));
        Assert.Equal("PROJECT_ORGANIZATION_MISMATCH", mismatch.Message);

        var updated = handler.Handle(device.Id,
            new UpdateDeviceRequest("edited", ownerProject.Id, null, null));
        Assert.Equal("edited", updated.Name);
        Assert.Equal(owner.Id.Value, updated.OwnerOrganizationId);
        Assert.Equal(ownerProject.Id, updated.ProjectId);
    }

    [Fact]
    public void Classification_rename_rejects_blank_names()
    {
        var organization = Organization.Create("Owner");

        Assert.Throws<ArgumentException>(() => organization.Rename("  "));
    }
}
