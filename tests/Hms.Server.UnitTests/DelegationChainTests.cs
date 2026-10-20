using Hms.Server.Application.Authorization;
using Hms.Server.Domain.Authorization;
using Hms.Server.Domain.Organizations;

namespace Hms.Server.UnitTests;

// An admin appointed by another non-master admin (the grantor of their latest
// permissions.manage) acts within what the appointer holds: they never act on
// anyone up their appointment chain, and never hand out a device permission,
// feature or organization that the appointers above them lack.
public sealed class DelegationChainTests
{
    private readonly DeviceAccessTestFixture _world = new();
    private readonly AccessActor _master;
    private readonly DeviceRole _viewer;
    private readonly DeviceRole _engineer;

    public DelegationChainTests()
    {
        _master = _world.MasterAdmin(new UserId(Guid.NewGuid()));
        _viewer = _world.SeedRole(_world.Child.Id, DevicePermissionCodes.DeviceView);
        _engineer = _world.SeedRole(_world.Child.Id, DevicePermissionCodes.DeviceView, "adc.config_edit");
    }

    // ---- no action on anyone up the chain ----

    [Fact]
    public void Appointed_admin_cannot_assign_or_remove_the_membership_of_its_appointer()
    {
        var appointer = AppointedByMaster();
        _world.SeedMembership(appointer.UserId, _world.Device, _viewer);
        var appointed = AppointedBy(appointer);
        _world.SeedMembership(appointed.UserId, _world.Device, _engineer);

        Assert.Equal("GRANT_TO_DELEGATOR_FORBIDDEN",
            Assign(appointed, appointer.UserId, _engineer, expectedRoleId: _viewer.Id).Code);
        Assert.Equal("GRANT_TO_DELEGATOR_FORBIDDEN",
            _world.Remove().Handle(appointed, _world.Device.Id, appointer.UserId, _viewer.Id).Code);
        Assert.Empty(_world.Access.Writes);
    }

    [Fact]
    public void Appointed_admin_cannot_grant_or_deny_anything_to_its_appointer()
    {
        var appointer = AppointedByMaster();
        var appointed = AppointedBy(appointer, "devices.manage");
        var policyVersion = _world.Permissions.PolicyVersion;

        Assert.Equal("GRANT_TO_DELEGATOR_FORBIDDEN",
            _world.GrantFeature().Handle(appointed, appointer.UserId, "devices.manage").Code);
        Assert.Equal("GRANT_TO_DELEGATOR_FORBIDDEN",
            _world.GrantFeature().Handle(appointed, appointer.UserId, "permissions.manage", deny: true).Code);
        Assert.Equal("GRANT_TO_DELEGATOR_FORBIDDEN",
            _world.GrantResource().Handle(appointed, appointer.UserId, _world.Child.Id).Code);
        Assert.Equal("GRANT_TO_DELEGATOR_FORBIDDEN",
            _world.GrantResource().Handle(appointed, appointer.UserId, _world.Child.Id, deny: true).Code);
        Assert.Equal(policyVersion, _world.Permissions.PolicyVersion);
    }

    [Fact]
    public void Appointed_admin_cannot_change_a_role_its_appointer_holds()
    {
        var appointer = AppointedByMaster();
        var held = _world.SeedRole(_world.Child.Id, DevicePermissionCodes.DeviceView);
        _world.SeedMembership(appointer.UserId, _world.Device, held);
        var appointed = AppointedBy(appointer);
        _world.SeedMembership(appointed.UserId, _world.Device, _engineer);

        // Editing a role changes the access of everyone holding it, the appointer included.
        var outcome = _world.UpdateRole().Handle(appointed, _world.Child.Id, held.Id, held.Name, "renamed",
            [DevicePermissionCodes.DeviceView], held.Version);

        Assert.Equal("GRANT_TO_DELEGATOR_FORBIDDEN", outcome.Code);
        Assert.Empty(_world.Access.Writes);
    }

    [Fact]
    public void Admin_two_appointments_down_cannot_act_on_either_appointer()
    {
        var top = AppointedByMaster();
        var middle = AppointedBy(top);
        var bottom = AppointedBy(middle);

        Assert.Equal("GRANT_TO_DELEGATOR_FORBIDDEN",
            _world.GrantFeature().Handle(bottom, middle.UserId, "permissions.manage", deny: true).Code);
        Assert.Equal("GRANT_TO_DELEGATOR_FORBIDDEN",
            _world.GrantFeature().Handle(bottom, top.UserId, "permissions.manage", deny: true).Code);
    }

    [Fact]
    public void Appointer_can_still_manage_the_admin_it_appointed()
    {
        var appointer = AppointedByMaster();
        var appointed = AppointedBy(appointer);

        Assert.Equal("OK",
            _world.GrantFeature().Handle(appointer, appointed.UserId, "permissions.manage", deny: true).Code);
    }

    [Fact]
    public void Reappointment_by_master_admin_ends_the_chain()
    {
        var appointer = AppointedByMaster();
        var appointed = AppointedBy(appointer);
        Assert.Equal("OK", _world.GrantFeature().Handle(_master, appointed.UserId, "permissions.manage").Code);

        Assert.Equal("OK",
            _world.GrantFeature().Handle(appointed, appointer.UserId, "permissions.manage", deny: true).Code);
    }

    [Fact]
    public void Appointment_rows_forming_a_cycle_do_not_loop()
    {
        var first = NewUser();
        var second = NewUser();
        // Written straight to the store, as older data could hold them.
        _world.Permissions.GrantFeature(new FeaturePermissionGrant(
            first, "permissions.manage", false, _world.Permissions.Advance(), second));
        _world.Permissions.GrantFeature(new FeaturePermissionGrant(
            second, "permissions.manage", false, _world.Permissions.Advance(), first));
        var actor = _world.Admin(first, _world.Child.Id, "permissions.manage");

        Assert.Equal("GRANT_TO_DELEGATOR_FORBIDDEN",
            _world.GrantFeature().Handle(actor, second, "permissions.manage", deny: true).Code);
    }

    // ---- no more than the appointers hold ----

    [Fact]
    public void Appointed_admin_hands_out_no_device_permission_its_appointer_lacks_there()
    {
        var appointer = AppointedByMaster();
        _world.SeedMembership(appointer.UserId, _world.Device, _viewer);
        var appointed = AppointedBy(appointer);
        _world.SeedMembership(appointed.UserId, _world.Device, _engineer);

        Assert.Equal("GRANT_EXCEEDS_OWN_PERMISSIONS", Assign(appointed, NewUser(), _engineer).Code);
        Assert.Equal("OK", Assign(appointed, NewUser(), _viewer).Code);
    }

    [Fact]
    public void Appointed_admin_cannot_delegate_on_a_device_where_its_appointer_holds_no_role()
    {
        var appointer = AppointedByMaster();
        var appointed = AppointedBy(appointer);
        _world.SeedMembership(appointed.UserId, _world.Device, _engineer);

        Assert.Equal("GRANT_EXCEEDS_OWN_PERMISSIONS", Assign(appointed, NewUser(), _viewer).Code);
    }

    [Fact]
    public void Appointed_admin_cannot_widen_a_role_beyond_its_appointer_on_devices_using_it()
    {
        var appointer = AppointedByMaster();
        _world.SeedMembership(appointer.UserId, _world.Device, _viewer);
        var appointed = AppointedBy(appointer);
        _world.SeedMembership(appointed.UserId, _world.Device, _engineer);
        var role = _world.SeedRole(_world.Child.Id, DevicePermissionCodes.DeviceView);
        _world.SeedMembership(NewUser(), _world.Device, role);

        var outcome = _world.UpdateRole().Handle(appointed, _world.Child.Id, role.Id, role.Name, null,
            [DevicePermissionCodes.DeviceView, "adc.config_edit"], role.Version);

        Assert.Equal("GRANT_EXCEEDS_OWN_PERMISSIONS", outcome.Code);
    }

    [Fact]
    public void Reappointment_by_master_admin_lifts_the_device_bound()
    {
        var appointer = AppointedByMaster();
        _world.SeedMembership(appointer.UserId, _world.Device, _viewer);
        var appointed = AppointedBy(appointer);
        _world.SeedMembership(appointed.UserId, _world.Device, _engineer);
        Assert.Equal("OK", _world.GrantFeature().Handle(_master, appointed.UserId, "permissions.manage").Code);

        Assert.Equal("OK", Assign(appointed, NewUser(), _engineer).Code);
    }

    [Fact]
    public void Appointed_admin_grants_no_feature_its_appointer_lacks()
    {
        var appointer = AppointedByMaster();
        var appointed = AppointedBy(appointer, "devices.manage");

        Assert.Equal("GRANT_EXCEEDS_OWN_PERMISSIONS",
            _world.GrantFeature().Handle(appointed, NewUser(), "devices.manage").Code);
        Assert.Equal("OK", _world.GrantFeature().Handle(appointed, NewUser(), "permissions.manage").Code);
    }

    [Fact]
    public void Appointed_admin_grants_no_organization_its_appointer_lacks()
    {
        var appointer = AppointedByMaster();
        var appointed = AppointedBy(appointer);
        // The master later widens the appointed admin's own scope to Parent.
        Assert.Equal("OK", _world.GrantResource().Handle(_master, appointed.UserId, _world.Parent.Id).Code);
        var widened = _world.Admin(appointed.UserId, _world.Parent.Id, "permissions.manage");

        Assert.Equal("GRANT_EXCEEDS_OWN_PERMISSIONS",
            _world.GrantResource().Handle(widened, NewUser(), _world.Parent.Id).Code);
        Assert.Equal("OK", _world.GrantResource().Handle(widened, NewUser(), _world.Child.Id).Code);
    }

    // ---- helpers ----

    private UserId NewUser()
    {
        var user = new UserId(Guid.NewGuid());
        _world.Users.Add(user, _world.Child.Id);
        return user;
    }

    // An access admin over Child appointed by the master: every row is master-issued.
    private AccessActor AppointedByMaster(params string[] features)
    {
        var user = NewUser();
        Assert.Equal("OK", _world.GrantResource().Handle(_master, user, _world.Child.Id).Code);
        foreach (var feature in features.Prepend("permissions.manage"))
            Assert.Equal("OK", _world.GrantFeature().Handle(_master, user, feature).Code);
        return _world.Admin(user, _world.Child.Id, features.Prepend("permissions.manage").ToArray());
    }

    // An access admin over Child whose permissions.manage comes from the appointer;
    // scope and the other features come from the master.
    private AccessActor AppointedBy(AccessActor appointer, params string[] features)
    {
        var user = NewUser();
        Assert.Equal("OK", _world.GrantResource().Handle(_master, user, _world.Child.Id).Code);
        Assert.Equal("OK", _world.GrantFeature().Handle(appointer, user, "permissions.manage").Code);
        foreach (var feature in features)
            Assert.Equal("OK", _world.GrantFeature().Handle(_master, user, feature).Code);
        return _world.Admin(user, _world.Child.Id, features.Prepend("permissions.manage").ToArray());
    }

    private DeviceAccessOutcome Assign(AccessActor actor, UserId target, DeviceRole role, DeviceRoleId? expectedRoleId = null) =>
        _world.Assign().Handle(actor, _world.Device.Id, target, role.Id, expectedRoleId, role.Version);
}
