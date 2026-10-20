using Hms.Server.Domain.Authorization;
using Hms.Server.Domain.Organizations;

namespace Hms.Server.UnitTests;

public sealed class DeviceRoleTests
{
    private static readonly OrganizationId Organization = OrganizationId.New();

    public static TheoryData<string> InvalidNames => new()
    {
        "",
        "   ",
        new string('n', DeviceRole.NameMaxLength + 1)
    };

    public static TheoryData<string> InvalidPermissionCodes => new()
    {
        "",
        " ",
        new string('p', DeviceRole.PermissionMaxLength + 1)
    };

    [Fact]
    public void Device_role_create_trims_name_and_starts_at_version_one()
    {
        var role = DeviceRole.Create(Organization, "  Operator  ", null, ["device.view"]);

        Assert.NotEqual(Guid.Empty, role.Id.Value);
        Assert.Equal(Organization, role.OrganizationId);
        Assert.Equal("Operator", role.Name);
        Assert.Null(role.Description);
        Assert.Equal(["device.view"], role.Permissions);
        Assert.Equal(1, role.Version);
    }

    [Fact]
    public void Device_role_normalized_name_is_trimmed_upper_invariant()
    {
        var role = DeviceRole.Create(Organization, " Operator ", null, []);

        Assert.Equal("OPERATOR", role.NormalizedName);

        role.Update("Field technician", null, []);

        Assert.Equal("FIELD TECHNICIAN", role.NormalizedName);
    }

    [Theory]
    [MemberData(nameof(InvalidNames))]
    public void Device_role_rejects_blank_or_overlong_name(string name)
    {
        Assert.Throws<ArgumentException>(() => DeviceRole.Create(Organization, name, null, []));
    }

    [Fact]
    public void Device_role_normalizes_blank_description_and_rejects_overlong()
    {
        Assert.Null(DeviceRole.Create(Organization, "Operator", "   ", []).Description);
        Assert.Equal("Runs the line", DeviceRole.Create(Organization, "Operator", "  Runs the line ", []).Description);
        Assert.Throws<ArgumentException>(() => DeviceRole.Create(
            Organization, "Operator", new string('d', DeviceRole.DescriptionMaxLength + 1), []));
    }

    [Theory]
    [MemberData(nameof(InvalidPermissionCodes))]
    public void Device_role_rejects_blank_or_overlong_permission_code(string code)
    {
        Assert.Throws<ArgumentException>(() => DeviceRole.Create(Organization, "Operator", null, ["device.view", code]));
    }

    [Fact]
    public void Device_role_accepts_values_at_max_length()
    {
        var name = new string('n', DeviceRole.NameMaxLength);
        var description = new string('d', DeviceRole.DescriptionMaxLength);
        var permission = new string('p', DeviceRole.PermissionMaxLength);

        var role = DeviceRole.Create(Organization, name, description, [permission]);

        Assert.Equal(name, role.Name);
        Assert.Equal(description, role.Description);
        Assert.Equal([permission], role.Permissions);
    }

    [Fact]
    public void Device_role_update_increments_version_and_replaces_permissions()
    {
        var role = DeviceRole.Create(Organization, "Operator", "Old", ["device.view", "sys.reboot"]);

        role.Update(" Supervisor ", " New ", ["device.view", "device.edit_basics"]);

        Assert.Equal(2, role.Version);
        Assert.Equal("Supervisor", role.Name);
        Assert.Equal("New", role.Description);
        Assert.Equal(["device.edit_basics", "device.view"], role.Permissions.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Device_role_invalid_update_leaves_state_and_version_unchanged()
    {
        var role = DeviceRole.Create(Organization, "Operator", "Reads status", ["device.view"]);

        Assert.Throws<ArgumentException>(() => role.Update("Supervisor", "Valid", ["device.view", " "]));
        Assert.Throws<ArgumentException>(() => role.Update(" ", "Valid", ["device.edit_basics"]));
        Assert.Throws<ArgumentException>(() => role.Update(
            "Supervisor", new string('d', DeviceRole.DescriptionMaxLength + 1), ["device.edit_basics"]));

        Assert.Equal(1, role.Version);
        Assert.Equal("Operator", role.Name);
        Assert.Equal("Reads status", role.Description);
        Assert.Equal(["device.view"], role.Permissions);
    }

    [Fact]
    public void Device_role_update_at_max_version_leaves_state_unchanged()
    {
        var role = DeviceRole.Rehydrate(Guid.NewGuid(), Guid.NewGuid(), "Operator", null, ["device.view"], int.MaxValue);

        Assert.Throws<OverflowException>(() => role.Update("Supervisor", "Runs the line", []));

        Assert.Equal("Operator", role.Name);
        Assert.Null(role.Description);
        Assert.Equal(["device.view"], role.Permissions);
        Assert.Equal(int.MaxValue, role.Version);
    }

    [Fact]
    public void Device_role_permissions_are_an_immutable_snapshot()
    {
        var source = new HashSet<string>(StringComparer.Ordinal) { "device.view" };
        var role = DeviceRole.Create(Organization, "Operator", null, source);

        source.Add("sys.reboot");

        Assert.Equal(["device.view"], role.Permissions);
        Assert.Throws<NotSupportedException>(() => ((ICollection<string>)role.Permissions).Add("sys.reboot"));
    }

    [Fact]
    public void Device_role_rehydrate_preserves_version()
    {
        var id = Guid.NewGuid();
        var organizationId = Guid.NewGuid();

        var role = DeviceRole.Rehydrate(id, organizationId, "Operator", null, ["device.view"], 7);

        Assert.Equal(new DeviceRoleId(id), role.Id);
        Assert.Equal(new OrganizationId(organizationId), role.OrganizationId);
        Assert.Equal("OPERATOR", role.NormalizedName);
        Assert.Equal(7, role.Version);

        role.Update("Operator", null, ["device.view"]);

        Assert.Equal(8, role.Version);
    }
}
