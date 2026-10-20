using System.Collections.Frozen;
using Hms.Server.Domain.Authorization;
using Hms.Server.Domain.Devices;

namespace Hms.Server.UnitTests;

public sealed class DeviceAccessPolicyTests
{
    private readonly DeviceAccessTestFixture _world = new();

    [Fact]
    public void Out_of_scope_device_is_not_found_even_when_revoked()
    {
        var actor = _world.Admin(NewUser(), _world.Child.Id, "devices.manage");
        _world.OtherDevice.Revoke();

        var decision = Evaluate(actor, _world.OtherDevice, DevicePermissionCodes.DeviceView);

        Assert.False(decision.Allowed);
        Assert.Equal(DeviceAccessCodes.DeviceNotFound, decision.Code);
    }

    [Fact]
    public void Revoked_device_is_denied_before_master_admin_bypass()
    {
        var actor = _world.MasterAdmin(NewUser());
        _world.Device.Revoke();

        var decision = Evaluate(actor, _world.Device, DevicePermissionCodes.DeviceView);

        Assert.Equal(DeviceAccessCodes.DeviceRevoked, decision.Code);
    }

    [Fact]
    public void Master_admin_is_allowed_without_membership_or_scope()
    {
        var actor = _world.MasterAdmin(NewUser()) with { EffectiveOrganizations = FrozenSet<Hms.Server.Domain.Organizations.OrganizationId>.Empty };

        var decision = Evaluate(actor, _world.OtherDevice, "sys.reboot");

        Assert.True(decision.Allowed);
        Assert.Equal(DeviceAccessCodes.Allowed, decision.Code);
    }

    [Fact]
    public void Member_with_only_a_membership_is_in_scope_and_allowed_per_role()
    {
        var actor = _world.Member(NewUser(), _world.Device, "adc.view");

        var access = _world.Resolver.Resolve(actor, _world.Device);

        Assert.True(access.InScope);
        Assert.False(access.SeesAllInScope);
        Assert.False(access.CanManageAccess);
        Assert.NotNull(access.RoleId);
        Assert.True(DeviceAccessPolicy.Evaluate(access, _world.Device, "adc.view").Allowed);
        Assert.Equal(DeviceAccessCodes.PermissionRequired,
            DeviceAccessPolicy.Evaluate(access, _world.Device, "sys.reboot").Code);
    }

    [Fact]
    public void Membership_does_not_extend_scope_to_other_devices_of_the_organization()
    {
        var actor = _world.Member(NewUser(), _world.Device, "adc.view", DevicePermissionCodes.DeviceView);

        var decision = Evaluate(actor, _world.SecondDevice, DevicePermissionCodes.DeviceView);

        Assert.Equal(DeviceAccessCodes.DeviceNotFound, decision.Code);
    }

    [Fact]
    public void Scope_admin_without_membership_sees_device_view_only()
    {
        var actor = _world.Admin(NewUser(), _world.Parent.Id, "devices.manage");

        Assert.True(Evaluate(actor, _world.Device, DevicePermissionCodes.DeviceView).Allowed);
        Assert.Equal(DeviceAccessCodes.PermissionRequired, Evaluate(actor, _world.Device, "adc.view").Code);
    }

    [Fact]
    public void Membership_role_allows_exact_code_only()
    {
        var actor = _world.Member(NewUser(), _world.Device, "adc.view");

        Assert.True(Evaluate(actor, _world.Device, "adc.view").Allowed);
        Assert.Equal(DeviceAccessCodes.PermissionRequired, Evaluate(actor, _world.Device, "adc.config_edit").Code);
        Assert.Equal(DeviceAccessCodes.PermissionRequired, Evaluate(actor, _world.Device, "ADC.VIEW").Code);
        Assert.Equal(DeviceAccessCodes.PermissionRequired, Evaluate(actor, _world.Device, DevicePermissionCodes.DeviceView).Code);
    }

    [Fact]
    public void Wildcard_in_role_permissions_grants_nothing()
    {
        // The handlers reject "*" as an unknown code; even a role that holds it grants nothing.
        var actor = _world.Member(NewUser(), _world.Device, "*");

        Assert.Equal(DeviceAccessCodes.PermissionRequired, Evaluate(actor, _world.Device, "adc.view").Code);
        Assert.Equal(DeviceAccessCodes.PermissionRequired, Evaluate(actor, _world.Device, DevicePermissionCodes.DeviceView).Code);
    }

    [Fact]
    public void Resolver_ignores_role_from_another_organization()
    {
        var user = NewUser();
        var foreignRole = _world.SeedRole(_world.Other.Id, "adc.view");
        _world.SeedMembership(user, _world.Device, foreignRole);
        var actor = new AccessActor(user, false, FrozenSet<Hms.Server.Domain.Organizations.OrganizationId>.Empty, FrozenSet<string>.Empty);

        var access = _world.Resolver.Resolve(actor, _world.Device);

        Assert.False(access.InScope);
        Assert.Null(access.RoleId);
        Assert.Empty(access.Permissions);
        Assert.Equal(DeviceAccessCodes.DeviceNotFound, DeviceAccessPolicy.Evaluate(access, _world.Device, "adc.view").Code);
    }

    [Fact]
    public void Resolver_sets_flags_only_for_in_scope_features()
    {
        var devicesManager = _world.Resolver.Resolve(_world.Admin(NewUser(), _world.Child.Id, "devices.manage"), _world.Device);
        var permissionsManager = _world.Resolver.Resolve(_world.Admin(NewUser(), _world.Child.Id, "permissions.manage"), _world.Device);

        var outOfScopeUser = NewUser();
        _world.SeedMembership(outOfScopeUser, _world.OtherDevice, _world.SeedRole(_world.Other.Id, DevicePermissionCodes.DeviceView));
        var outOfScope = _world.Resolver.Resolve(
            _world.Admin(outOfScopeUser, _world.Child.Id, "devices.manage", "permissions.manage"), _world.OtherDevice);

        Assert.True(devicesManager.SeesAllInScope);
        Assert.False(devicesManager.CanManageAccess);
        Assert.True(permissionsManager.SeesAllInScope);
        Assert.True(permissionsManager.CanManageAccess);
        Assert.True(outOfScope.InScope);
        Assert.False(outOfScope.SeesAllInScope);
        Assert.False(outOfScope.CanManageAccess);
    }

    [Fact]
    public void In_scope_actor_without_a_management_feature_does_not_see_every_device()
    {
        var access = _world.Resolver.Resolve(_world.Admin(NewUser(), _world.Child.Id, "users.manage"), _world.Device);

        Assert.True(access.InScope);
        Assert.False(access.SeesAllInScope);
        Assert.Equal(DeviceAccessCodes.PermissionRequired,
            DeviceAccessPolicy.Evaluate(access, _world.Device, DevicePermissionCodes.DeviceView).Code);
    }

    private DeviceAccessDecision Evaluate(AccessActor actor, Device device, string permission) =>
        DeviceAccessPolicy.Evaluate(_world.Resolver.Resolve(actor, device), device, permission);

    private static UserId NewUser() => new(Guid.NewGuid());
}
