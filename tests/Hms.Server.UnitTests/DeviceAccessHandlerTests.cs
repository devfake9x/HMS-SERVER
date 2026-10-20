using System.Text.Json;
using Hms.Server.Application.Authorization;
using Hms.Server.Domain.Authorization;
using Hms.Server.Domain.Devices;
using Hms.Server.Domain.Organizations;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hms.Server.UnitTests;

public sealed class DeviceAccessHandlerTests
{
    private readonly DeviceAccessTestFixture _world = new();

    // ---- memberships: who may manage, which device, which target ----

    [Fact]
    public void Devices_manage_without_permissions_manage_cannot_assign_membership()
    {
        var admin = _world.Admin(NewUser(_world.Child.Id), _world.Child.Id, "devices.manage");
        var target = NewUser(_world.Child.Id);
        var role = _world.SeedRole(_world.Child.Id, DevicePermissionCodes.DeviceView);

        var outcome = Assign(admin, target, role);

        Assert.Equal("PERMISSION_MANAGEMENT_REQUIRED", outcome.Code);
        Assert.Empty(_world.Access.Writes);
    }

    [Fact]
    public void Out_of_scope_device_membership_change_is_not_found()
    {
        var admin = ManagingAdmin(_world.Device, DevicePermissionCodes.DeviceView);
        var target = NewUser(_world.Child.Id);
        var role = _world.SeedRole(_world.Other.Id, DevicePermissionCodes.DeviceView);

        Assert.Equal("DEVICE_NOT_FOUND",
            _world.Assign().Handle(admin, _world.OtherDevice.Id, target, role.Id, null, role.Version).Code);
        Assert.Equal("DEVICE_NOT_FOUND", _world.Remove().Handle(admin, _world.OtherDevice.Id, target, null).Code);
        Assert.Equal("DEVICE_NOT_FOUND",
            _world.Assign().Handle(admin, DeviceId.New(), target, role.Id, null, role.Version).Code);
        Assert.Empty(_world.Access.Writes);
    }

    [Theory]
    [InlineData("missing", false)]
    [InlineData("other-organization", false)]
    [InlineData("no-organization", false)]
    [InlineData("missing", true)]
    [InlineData("other-organization", true)]
    [InlineData("no-organization", true)]
    public void Target_user_missing_outside_scope_or_without_organization_is_not_found(string kind, bool remove)
    {
        var admin = ManagingAdmin(_world.Device, DevicePermissionCodes.DeviceView);
        var target = new UserId(Guid.NewGuid());
        if (kind == "other-organization")
            _world.Users.Add(target, _world.Other.Id);
        else if (kind == "no-organization")
            _world.Users.Add(target, null);
        var role = _world.SeedRole(_world.Child.Id, DevicePermissionCodes.DeviceView);

        var outcome = remove
            ? _world.Remove().Handle(admin, _world.Device.Id, target, null)
            : Assign(admin, target, role);

        Assert.Equal("USER_NOT_FOUND", outcome.Code);
        Assert.Empty(_world.Access.Writes);
    }

    [Fact]
    public void Master_admin_can_assign_a_target_without_organization()
    {
        var target = NewUser(null);
        var role = _world.SeedRole(_world.Child.Id, DevicePermissionCodes.DeviceView);

        Assert.Equal("OK", Assign(Master(), target, role).Code);
    }

    [Fact]
    public void Non_master_admin_cannot_assign_self()
    {
        var admin = ManagingAdmin(_world.Device, DevicePermissionCodes.DeviceView, "adc.view");
        var role = _world.SeedRole(_world.Child.Id, DevicePermissionCodes.DeviceView);

        Assert.Equal("SELF_GRANT_FORBIDDEN", Assign(admin, admin.UserId, role).Code);
        Assert.Equal("SELF_GRANT_FORBIDDEN", _world.Remove().Handle(admin, _world.Device.Id, admin.UserId,
            _world.Inner.Find(admin.UserId, _world.Device.Id)!.RoleId).Code);
        Assert.Empty(_world.Access.Writes);
    }

    [Fact]
    public void Master_admin_can_assign_self()
    {
        var master = Master();
        _world.Users.AddMasterAdmin(master.UserId);
        var role = _world.SeedRole(_world.Child.Id, "sys.reboot");

        var outcome = Assign(master, master.UserId, role);

        Assert.Equal("OK", outcome.Code);
        Assert.Equal(role.Id, _world.Inner.Find(master.UserId, _world.Device.Id)?.RoleId);
    }

    [Fact]
    public void Role_must_belong_to_device_owner_organization()
    {
        var foreignRole = _world.SeedRole(_world.Other.Id, DevicePermissionCodes.DeviceView);
        var admin = ManagingAdmin(_world.Device, DevicePermissionCodes.DeviceView);

        Assert.Equal("DEVICE_ROLE_ORGANIZATION_MISMATCH", Assign(Master(), NewUser(_world.Child.Id), foreignRole).Code);
        Assert.Equal("DEVICE_ROLE_ORGANIZATION_MISMATCH", Assign(admin, NewUser(_world.Child.Id), foreignRole).Code);
        Assert.Empty(_world.Access.Writes);
    }

    [Fact]
    public void Missing_role_is_not_found()
    {
        var outcome = _world.Assign().Handle(Master(), _world.Device.Id, NewUser(_world.Child.Id), DeviceRoleId.New(), null, 1);

        Assert.Equal("DEVICE_ROLE_NOT_FOUND", outcome.Code);
    }

    [Fact]
    public void Stale_expected_role_is_rejected_on_assign_and_remove()
    {
        var target = NewUser(_world.Child.Id);
        var current = _world.SeedRole(_world.Child.Id, DevicePermissionCodes.DeviceView);
        var other = _world.SeedRole(_world.Child.Id, "adc.view");
        _world.SeedMembership(target, _world.Device, current);

        Assert.Equal("DEVICE_MEMBERSHIP_CHANGED", Assign(Master(), target, other, expectedRoleId: null).Code);
        Assert.Equal("DEVICE_MEMBERSHIP_CHANGED", _world.Remove().Handle(Master(), _world.Device.Id, target, other.Id).Code);
        Assert.Equal("DEVICE_MEMBERSHIP_CHANGED", _world.Remove().Handle(Master(), _world.Device.Id, target, null).Code);
        Assert.Empty(_world.Access.Writes);
        Assert.Equal(current.Id, _world.Inner.Find(target, _world.Device.Id)?.RoleId);
    }

    [Fact]
    public void Store_level_membership_conflict_surfaces_membership_changed()
    {
        var target = NewUser(_world.Child.Id);
        var current = _world.SeedRole(_world.Child.Id, DevicePermissionCodes.DeviceView);
        var replacement = _world.SeedRole(_world.Child.Id, "adc.view");
        var concurrent = _world.SeedRole(_world.Child.Id, "adc.config_edit");
        _world.SeedMembership(target, _world.Device, current);
        var policyVersion = _world.Permissions.PolicyVersion;
        _world.Access.BeforeWrite = () =>
        {
            // Another admin changes the role after this handler's pre-checks.
            _world.Access.BeforeWrite = null;
            _world.SeedMembership(target, _world.Device, concurrent);
        };

        var outcome = Assign(Master(), target, replacement, expectedRoleId: current.Id);

        Assert.Equal("DEVICE_MEMBERSHIP_CHANGED", outcome.Code);
        Assert.Equal(concurrent.Id, _world.Inner.Find(target, _world.Device.Id)?.RoleId);
        Assert.Equal(policyVersion, _world.Permissions.PolicyVersion);
        Assert.Empty(_world.Epochs.Advances);
    }

    // ---- memberships: what an admin may delegate ----

    [Fact]
    public void Non_master_admin_cannot_assign_role_exceeding_own_device_permissions()
    {
        var admin = ManagingAdmin(_world.Device, DevicePermissionCodes.DeviceView, "adc.view");
        var exceeding = _world.SeedRole(_world.Child.Id, "adc.view", "adc.config_edit");
        var subset = _world.SeedRole(_world.Child.Id, "adc.view");

        Assert.Equal("GRANT_EXCEEDS_OWN_PERMISSIONS", Assign(admin, NewUser(_world.Child.Id), exceeding).Code);
        Assert.Equal("OK", Assign(admin, NewUser(_world.Child.Id), subset).Code);
    }

    [Fact]
    public void Non_master_admin_cannot_assign_high_risk_role_even_when_holding_it()
    {
        var admin = ManagingAdmin(_world.Device, DevicePermissionCodes.DeviceView, "sys.reboot");
        var highRisk = _world.SeedRole(_world.Child.Id, "sys.reboot");

        Assert.Equal("HIGH_RISK_REQUIRES_MASTER_ADMIN", Assign(admin, NewUser(_world.Child.Id), highRisk).Code);
        Assert.Empty(_world.Access.Writes);
    }

    [Fact]
    public void Master_admin_can_assign_high_risk_role()
    {
        var highRisk = _world.SeedRole(_world.Child.Id, "sys.reboot", "sys.factory_reset");

        Assert.Equal("OK", Assign(Master(), NewUser(_world.Child.Id), highRisk).Code);
    }

    [Fact]
    public void Non_master_admin_cannot_replace_or_remove_high_risk_current_role()
    {
        var admin = ManagingAdmin(_world.Device, DevicePermissionCodes.DeviceView, "sys.reboot");
        var target = NewUser(_world.Child.Id);
        var highRisk = _world.SeedRole(_world.Child.Id, "sys.reboot");
        var plain = _world.SeedRole(_world.Child.Id, DevicePermissionCodes.DeviceView);
        _world.SeedMembership(target, _world.Device, highRisk);

        Assert.Equal("HIGH_RISK_REQUIRES_MASTER_ADMIN", Assign(admin, target, plain, expectedRoleId: highRisk.Id).Code);
        Assert.Equal("HIGH_RISK_REQUIRES_MASTER_ADMIN",
            _world.Remove().Handle(admin, _world.Device.Id, target, highRisk.Id).Code);
        Assert.Empty(_world.Access.Writes);
    }

    [Fact]
    public void Non_master_admin_cannot_replace_or_remove_current_role_exceeding_own_permissions()
    {
        var admin = ManagingAdmin(_world.Device, DevicePermissionCodes.DeviceView, "adc.view");
        var target = NewUser(_world.Child.Id);
        var stronger = _world.SeedRole(_world.Child.Id, "adc.view", "adc.config_edit");
        var weaker = _world.SeedRole(_world.Child.Id, "adc.view");
        _world.SeedMembership(target, _world.Device, stronger);

        Assert.Equal("GRANT_EXCEEDS_OWN_PERMISSIONS", Assign(admin, target, weaker, expectedRoleId: stronger.Id).Code);
        Assert.Equal("GRANT_EXCEEDS_OWN_PERMISSIONS",
            _world.Remove().Handle(admin, _world.Device.Id, target, stronger.Id).Code);
        Assert.Empty(_world.Access.Writes);
    }

    [Fact]
    public void Scope_admin_without_own_membership_cannot_delegate_on_device()
    {
        var admin = _world.Admin(NewUser(_world.Child.Id), _world.Child.Id, "permissions.manage");
        var viewer = _world.SeedRole(_world.Child.Id, DevicePermissionCodes.DeviceView);

        Assert.Equal("GRANT_EXCEEDS_OWN_PERMISSIONS", Assign(admin, NewUser(_world.Child.Id), viewer).Code);
    }

    [Fact]
    public void Assign_with_stale_role_version_is_rejected()
    {
        var admin = ManagingAdmin(_world.Device, DevicePermissionCodes.DeviceView, "adc.view");
        var role = _world.SeedRole(_world.Child.Id, "adc.view");
        var seen = role.Version;
        var edited = _world.Inner.Find(role.Id)!;
        edited.Update(edited.Name, "edited after the admin looked", edited.Permissions);
        _world.Inner.Update(edited, new DeviceAccessAuditEntry(Guid.NewGuid(), admin.UserId,
            DeviceAccessChanges.RoleUpdated, null, null, null, role.Id, null, null, DateTimeOffset.UtcNow));
        var policyVersion = _world.Permissions.PolicyVersion;

        Assert.Equal("DEVICE_ROLE_VERSION_STALE",
            _world.Assign().Handle(Master(), _world.Device.Id, NewUser(_world.Child.Id), role.Id, null, seen).Code);
        Assert.Equal("DEVICE_ROLE_VERSION_STALE",
            _world.Assign().Handle(admin, _world.Device.Id, NewUser(_world.Child.Id), role.Id, null, seen).Code);
        Assert.Empty(_world.Access.Writes);
        Assert.Equal(policyVersion, _world.Permissions.PolicyVersion);
    }

    // ---- memberships: removing members of other organizations ----

    [Fact]
    public void Org_admin_can_remove_foreign_member_from_own_device()
    {
        var admin = ManagingAdmin(_world.Device, DevicePermissionCodes.DeviceView);
        var foreigner = NewUser(_world.Other.Id);
        var role = _world.SeedRole(_world.Child.Id, DevicePermissionCodes.DeviceView);
        _world.SeedMembership(foreigner, _world.Device, role);

        var outcome = _world.Remove().Handle(admin, _world.Device.Id, foreigner, role.Id);

        Assert.Equal("OK", outcome.Code);
        Assert.Null(_world.Inner.Find(foreigner, _world.Device.Id));
        Assert.Equal(DeviceAccessChanges.MembershipRemoved, Assert.Single(_world.Access.Writes).Audit.Change);
    }

    [Fact]
    public void Org_admin_can_remove_orgless_member()
    {
        var admin = ManagingAdmin(_world.Device, DevicePermissionCodes.DeviceView);
        var orgless = NewUser(null);
        var role = _world.SeedRole(_world.Child.Id, DevicePermissionCodes.DeviceView);
        _world.SeedMembership(orgless, _world.Device, role);

        Assert.Equal("OK", _world.Remove().Handle(admin, _world.Device.Id, orgless, role.Id).Code);
    }

    [Fact]
    public void Org_admin_can_remove_orphaned_membership_of_deleted_user()
    {
        var admin = ManagingAdmin(_world.Device, DevicePermissionCodes.DeviceView);
        var deleted = NewUser(_world.Child.Id);
        var role = _world.SeedRole(_world.Child.Id, DevicePermissionCodes.DeviceView);
        _world.SeedMembership(deleted, _world.Device, role);
        _world.Users.Delete(deleted);

        Assert.Equal("OK", _world.Remove().Handle(admin, _world.Device.Id, deleted, role.Id).Code);
    }

    [Fact]
    public void Org_admin_cannot_replace_role_of_foreign_member()
    {
        var admin = ManagingAdmin(_world.Device, DevicePermissionCodes.DeviceView, "adc.view");
        var foreigner = NewUser(_world.Other.Id);
        var role = _world.SeedRole(_world.Child.Id, DevicePermissionCodes.DeviceView);
        var replacement = _world.SeedRole(_world.Child.Id, "adc.view");
        _world.SeedMembership(foreigner, _world.Device, role);

        Assert.Equal("USER_NOT_FOUND", Assign(admin, foreigner, replacement, expectedRoleId: role.Id).Code);
    }

    [Fact]
    public void Removing_foreign_member_still_obeys_high_risk_and_own_permission_rules()
    {
        var admin = ManagingAdmin(_world.Device, DevicePermissionCodes.DeviceView, "sys.reboot");
        var highRiskHolder = NewUser(_world.Other.Id);
        var strongerHolder = NewUser(_world.Other.Id);
        var highRisk = _world.SeedRole(_world.Child.Id, "sys.reboot");
        var stronger = _world.SeedRole(_world.Child.Id, DevicePermissionCodes.DeviceView, "adc.config_edit");
        _world.SeedMembership(highRiskHolder, _world.Device, highRisk);
        _world.SeedMembership(strongerHolder, _world.Device, stronger);

        Assert.Equal("HIGH_RISK_REQUIRES_MASTER_ADMIN",
            _world.Remove().Handle(admin, _world.Device.Id, highRiskHolder, highRisk.Id).Code);
        Assert.Equal("GRANT_EXCEEDS_OWN_PERMISSIONS",
            _world.Remove().Handle(admin, _world.Device.Id, strongerHolder, stronger.Id).Code);
        Assert.Empty(_world.Access.Writes);
    }

    // ---- memberships: write ordering and audit ----

    [Fact]
    public void Membership_write_precedes_policy_version_and_epoch_advance()
    {
        var target = NewUser(_world.Child.Id);
        var first = _world.SeedRole(_world.Child.Id, DevicePermissionCodes.DeviceView);
        var second = _world.SeedRole(_world.Child.Id, "adc.view");
        _world.SeedMembership(target, _world.Device, first);
        var policyVersion = _world.Permissions.PolicyVersion;

        Assert.Equal("OK", Assign(Master(), target, second, expectedRoleId: first.Id).Code);
        Assert.Equal("OK", _world.Remove().Handle(Master(), _world.Device.Id, target, second.Id).Code);

        Assert.Equal(2, _world.Access.Writes.Count);
        Assert.Equal(policyVersion, _world.Access.Writes[0].PolicyVersionAtWrite);
        Assert.Equal(0, _world.Access.Writes[0].EpochsAtWrite[_world.Device.Id]);
        Assert.Equal(policyVersion + 1, _world.Access.Writes[1].PolicyVersionAtWrite);
        Assert.Equal(1, _world.Access.Writes[1].EpochsAtWrite[_world.Device.Id]);
        Assert.Collection(_world.Epochs.Advances,
            advance =>
            {
                Assert.Equal([_world.Device.Id], advance.Devices);
                Assert.Equal(policyVersion + 1, advance.PolicyVersionAtAdvance);
            },
            advance =>
            {
                Assert.Equal([_world.Device.Id], advance.Devices);
                Assert.Equal(policyVersion + 2, advance.PolicyVersionAtAdvance);
            });
        Assert.Equal(2, _world.Epochs.Current(_world.Device.Id));
        Assert.Equal(0, _world.Epochs.Current(_world.SecondDevice.Id));
        Assert.Equal(0, _world.Epochs.Current(_world.OtherDevice.Id));
    }

    [Fact]
    public void Fresh_assign_does_not_advance_device_epoch()
    {
        var role = _world.SeedRole(_world.Child.Id, DevicePermissionCodes.DeviceView);
        var policyVersion = _world.Permissions.PolicyVersion;

        Assert.Equal("OK", Assign(Master(), NewUser(_world.Child.Id), role).Code);

        Assert.Equal(policyVersion + 1, _world.Permissions.PolicyVersion);
        Assert.Empty(_world.Epochs.Advances);
        Assert.Equal(0, _world.Epochs.Current(_world.Device.Id));
    }

    [Fact]
    public void Post_commit_signal_failure_returns_ok_and_keeps_the_write()
    {
        var target = NewUser(_world.Child.Id);
        var first = _world.SeedRole(_world.Child.Id, DevicePermissionCodes.DeviceView);
        var second = _world.SeedRole(_world.Child.Id, "adc.view");
        _world.SeedMembership(target, _world.Device, first);
        _world.Epochs.ThrowOnAdvance = true;

        var outcome = Assign(Master(), target, second, expectedRoleId: first.Id);

        Assert.Equal("OK", outcome.Code);
        Assert.Equal(second.Id, _world.Inner.Find(target, _world.Device.Id)?.RoleId);
        Assert.Equal(DeviceAccessChanges.MembershipChanged, Assert.Single(_world.Access.Writes).Audit.Change);
    }

    [Fact]
    public void Epoch_still_advances_when_the_policy_version_signal_fails()
    {
        var (permissions, control) = AdvanceFailingPermissionStore.Wrap(_world.Permissions);
        control.ThrowOnAdvance = true;
        var assign = new AssignDeviceMembershipHandler(_world.Access, _world.Access, _world.Devices, _world.Resolver,
            _world.Users, permissions, _world.Epochs, NullLogger<AssignDeviceMembershipHandler>.Instance);
        var target = NewUser(_world.Child.Id);
        var first = _world.SeedRole(_world.Child.Id, DevicePermissionCodes.DeviceView);
        var second = _world.SeedRole(_world.Child.Id, "adc.view");
        _world.SeedMembership(target, _world.Device, first);

        var outcome = assign.Handle(Master(), _world.Device.Id, target, second.Id, first.Id, second.Version);

        Assert.Equal("OK", outcome.Code);
        Assert.Equal([_world.Device.Id], Assert.Single(_world.Epochs.Advances).Devices);
    }

    [Fact]
    public void Assigning_the_current_role_again_is_a_no_op()
    {
        var target = NewUser(_world.Child.Id);
        var role = _world.SeedRole(_world.Child.Id, DevicePermissionCodes.DeviceView);
        _world.SeedMembership(target, _world.Device, role);
        var policyVersion = _world.Permissions.PolicyVersion;

        var outcome = Assign(Master(), target, role, expectedRoleId: role.Id);

        Assert.Equal("OK", outcome.Code);
        Assert.Empty(_world.Access.Attempts);
        Assert.Equal(policyVersion, _world.Permissions.PolicyVersion);
        Assert.Empty(_world.Epochs.Advances);
    }

    [Fact]
    public void Unreadable_current_role_counts_as_stronger_than_a_non_master()
    {
        var admin = ManagingAdmin(_world.Device, DevicePermissionCodes.DeviceView, "adc.view");
        var target = NewUser(_world.Child.Id);
        var current = _world.SeedRole(_world.Child.Id, "adc.view");
        var replacement = _world.SeedRole(_world.Child.Id, "adc.view");
        _world.SeedMembership(target, _world.Device, current);
        _world.Access.Unreadable.Add(current.Id);

        Assert.Equal("GRANT_EXCEEDS_OWN_PERMISSIONS",
            Assign(admin, target, replacement, expectedRoleId: current.Id).Code);
        Assert.Equal("GRANT_EXCEEDS_OWN_PERMISSIONS",
            _world.Remove().Handle(admin, _world.Device.Id, target, current.Id).Code);
        Assert.Empty(_world.Access.Writes);
    }

    [Fact]
    public void Removing_absent_membership_without_expected_role_is_a_no_op()
    {
        var policyVersion = _world.Permissions.PolicyVersion;

        var outcome = _world.Remove().Handle(Master(), _world.Device.Id, NewUser(_world.Child.Id), null);

        Assert.Equal("OK", outcome.Code);
        Assert.Empty(_world.Access.Writes);
        Assert.Equal(policyVersion, _world.Permissions.PolicyVersion);
        Assert.Empty(_world.Epochs.Advances);
    }

    [Fact]
    public void Membership_writes_emit_assigned_changed_removed_audit()
    {
        var target = NewUser(_world.Child.Id);
        var first = _world.SeedRole(_world.Child.Id, DevicePermissionCodes.DeviceView);
        var second = _world.SeedRole(_world.Child.Id, "adc.view");
        var master = Master();

        Assert.Equal("OK", Assign(master, target, first).Code);
        Assert.Equal("OK", Assign(master, target, second, expectedRoleId: first.Id).Code);
        Assert.Equal("OK", _world.Remove().Handle(master, _world.Device.Id, target, second.Id).Code);

        var audits = _world.Access.Writes.Select(x => x.Audit).ToArray();
        Assert.Equal(
            [DeviceAccessChanges.MembershipAssigned, DeviceAccessChanges.MembershipChanged, DeviceAccessChanges.MembershipRemoved],
            audits.Select(x => x.Change));
        Assert.All(audits, audit =>
        {
            Assert.Equal(master.UserId, audit.Actor);
            Assert.Equal(_world.Device.Id, audit.DeviceId);
            Assert.Equal(target, audit.TargetUserId);
            Assert.Equal(_world.Child.Id, audit.OrganizationId);
        });
        Assert.Null(audits[0].PreviousValue);
        Assert.Equal(first.Id.Value, RoleIdOf(audits[0].NewValue));
        Assert.Equal(first.Id.Value, RoleIdOf(audits[1].PreviousValue));
        Assert.Equal(second.Id.Value, RoleIdOf(audits[1].NewValue));
        Assert.Equal(second.Id.Value, RoleIdOf(audits[2].PreviousValue));
        Assert.Null(audits[2].NewValue);
    }

    // ---- roles ----

    [Fact]
    public void Role_create_requires_permissions_manage_in_scope()
    {
        var withoutPermission = _world.Admin(NewUser(_world.Child.Id), _world.Child.Id, "devices.manage");
        var withPermission = _world.Admin(NewUser(_world.Child.Id), _world.Child.Id, "permissions.manage");

        Assert.Equal("PERMISSION_MANAGEMENT_REQUIRED", CreateRole(withoutPermission, _world.Child.Id, "Operator").Code);
        Assert.Equal("ORGANIZATION_NOT_FOUND", CreateRole(withPermission, _world.Other.Id, "Operator").Code);
        Assert.Equal("ORGANIZATION_NOT_FOUND", CreateRole(withPermission, OrganizationId.New(), "Operator").Code);
        Assert.Equal("ORGANIZATION_NOT_FOUND", CreateRole(Master(), OrganizationId.New(), "Operator").Code);
        Assert.Empty(_world.Access.Writes);
    }

    public static TheoryData<string, string?> InvalidRoleTexts => new()
    {
        { "", null },
        { "  ", null },
        { new string('n', DeviceRole.NameMaxLength + 1), null },
        { "Operator", new string('d', DeviceRole.DescriptionMaxLength + 1) }
    };

    [Theory]
    [MemberData(nameof(InvalidRoleTexts))]
    public void Role_create_rejects_invalid_name_or_description(string name, string? description)
    {
        var outcome = _world.CreateRole().Handle(Master(), _world.Child.Id, name, description, [DevicePermissionCodes.DeviceView]);

        Assert.Equal("DEVICE_ROLE_INVALID", outcome.Code);
        Assert.Empty(_world.Access.Attempts);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("*")]
    [InlineData("ADC.VIEW")]
    public void Role_create_rejects_unknown_or_wildcard_permission(string permission)
    {
        var outcome = _world.CreateRole().Handle(Master(), _world.Child.Id, "Operator", null, ["adc.view", permission]);

        Assert.Equal("DEVICE_PERMISSION_UNKNOWN", outcome.Code);
        Assert.Empty(_world.Access.Attempts);
    }

    [Fact]
    public void Role_create_accepts_server_and_config_domain_codes()
    {
        var admin = _world.Admin(NewUser(_world.Child.Id), _world.Child.Id, "permissions.manage");
        var policyVersion = _world.Permissions.PolicyVersion;

        var outcome = _world.CreateRole().Handle(admin, _world.Child.Id, " Operator ", " Runs the line ",
            [DevicePermissionCodes.DeviceView, "sys.config_import"]);

        Assert.Equal("OK", outcome.Code);
        Assert.Equal(policyVersion + 1, _world.Permissions.PolicyVersion);
        Assert.NotNull(outcome.Role);
        Assert.Equal("Operator", outcome.Role!.Name);
        Assert.Equal(_world.Child.Id, outcome.Role.OrganizationId);
        Assert.True(outcome.Role.Permissions.SetEquals([DevicePermissionCodes.DeviceView, "sys.config_import"]));
        var write = Assert.Single(_world.Access.Writes);
        Assert.Equal(DeviceAccessChanges.RoleCreated, write.Audit.Change);
        Assert.Equal(outcome.Role.Id, write.Audit.RoleId);
        Assert.Null(write.Audit.PreviousValue);
        using var snapshot = JsonDocument.Parse(write.Audit.NewValue!);
        Assert.Equal("Operator", snapshot.RootElement.GetProperty("name").GetString());
        Assert.Equal(1, snapshot.RootElement.GetProperty("version").GetInt32());
        Assert.Equal(
            [DevicePermissionCodes.DeviceView, "sys.config_import"],
            snapshot.RootElement.GetProperty("permissions").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public void Role_name_is_unique_per_organization_ignoring_case()
    {
        Assert.Equal("OK", CreateRole(Master(), _world.Child.Id, "Operator").Code);

        var duplicate = CreateRole(Master(), _world.Child.Id, " operator ");

        Assert.Equal("DEVICE_ROLE_NAME_TAKEN", duplicate.Code);
        // The store decided: the second Add reached it.
        Assert.Equal(["Add", "Add"], _world.Access.Attempts);
        Assert.Equal("OK", CreateRole(Master(), _world.Other.Id, "operator").Code);
    }

    [Fact]
    public void Non_master_admin_cannot_add_or_remove_high_risk_permission()
    {
        var admin = _world.Admin(NewUser(_world.Child.Id), _world.Child.Id, "permissions.manage");
        var withHighRisk = _world.SeedRole(_world.Child.Id, DevicePermissionCodes.DeviceView, "sys.reboot");
        var plain = _world.SeedRole(_world.Child.Id, DevicePermissionCodes.DeviceView);

        Assert.Equal("HIGH_RISK_REQUIRES_MASTER_ADMIN",
            CreateRole(admin, _world.Child.Id, "Rebooter", DevicePermissionCodes.DeviceView, "sys.reboot").Code);
        Assert.Equal("HIGH_RISK_REQUIRES_MASTER_ADMIN",
            UpdateRole(admin, withHighRisk, [DevicePermissionCodes.DeviceView]).Code);
        Assert.Equal("HIGH_RISK_REQUIRES_MASTER_ADMIN",
            UpdateRole(admin, plain, [DevicePermissionCodes.DeviceView, "wifi.sta_edit"]).Code);
        Assert.Equal("HIGH_RISK_REQUIRES_MASTER_ADMIN",
            _world.DeleteRole().Handle(admin, _world.Child.Id, withHighRisk.Id, withHighRisk.Version).Code);
        Assert.Empty(_world.Access.Attempts);
    }

    [Fact]
    public void Role_update_with_stale_version_is_rejected()
    {
        var role = _world.SeedRole(_world.Child.Id, DevicePermissionCodes.DeviceView);

        var outcome = _world.UpdateRole().Handle(Master(), _world.Child.Id, role.Id, role.Name, null,
            ["adc.view"], role.Version + 1);

        Assert.Equal("DEVICE_ROLE_VERSION_STALE", outcome.Code);
        Assert.Empty(_world.Access.Attempts);
    }

    [Fact]
    public void Role_update_must_stay_within_admin_permissions_on_every_assigned_device()
    {
        var adminUser = NewUser(_world.Child.Id);
        _world.SeedMembership(adminUser, _world.Device,
            _world.SeedRole(_world.Child.Id, DevicePermissionCodes.DeviceView, "adc.view", "adc.config_edit"));
        _world.SeedMembership(adminUser, _world.SecondDevice,
            _world.SeedRole(_world.Child.Id, DevicePermissionCodes.DeviceView, "adc.view"));
        var admin = _world.Admin(adminUser, _world.Child.Id, "permissions.manage");
        var role = _world.SeedRole(_world.Child.Id, "adc.view");
        _world.SeedMembership(NewUser(_world.Child.Id), _world.Device, role);
        _world.SeedMembership(NewUser(_world.Child.Id), _world.SecondDevice, role);

        Assert.Equal("GRANT_EXCEEDS_OWN_PERMISSIONS", UpdateRole(admin, role, ["adc.view", "adc.config_edit"]).Code);
        Assert.Empty(_world.Access.Attempts);
        Assert.Equal("OK", UpdateRole(admin, role, ["adc.view", DevicePermissionCodes.DeviceView]).Code);
    }

    [Fact]
    public void Non_master_cannot_shrink_role_exceeding_own_permissions_on_assigned_devices()
    {
        var adminUser = NewUser(_world.Child.Id);
        _world.SeedMembership(adminUser, _world.Device, _world.SeedRole(_world.Child.Id, DevicePermissionCodes.DeviceView));
        var admin = _world.Admin(adminUser, _world.Child.Id, "permissions.manage");
        var role = _world.SeedRole(_world.Child.Id, DevicePermissionCodes.DeviceView, "adc.config_edit");
        _world.SeedMembership(NewUser(_world.Child.Id), _world.Device, role);
        _world.SeedMembership(NewUser(_world.Child.Id), _world.SecondDevice, role);
        var policyVersion = _world.Permissions.PolicyVersion;

        Assert.Equal("GRANT_EXCEEDS_OWN_PERMISSIONS", UpdateRole(admin, role, []).Code);
        Assert.Equal("GRANT_EXCEEDS_OWN_PERMISSIONS", UpdateRole(admin, role, [DevicePermissionCodes.DeviceView]).Code);
        Assert.Empty(_world.Access.Attempts);
        Assert.Equal(policyVersion, _world.Permissions.PolicyVersion);
        Assert.Empty(_world.Epochs.Advances);
    }

    [Fact]
    public void Role_update_advances_all_affected_devices_in_one_call()
    {
        var thirdDevice = _world.AddDevice("line-3", _world.Child.Id);
        var role = _world.SeedRole(_world.Child.Id, DevicePermissionCodes.DeviceView);
        var otherRole = _world.SeedRole(_world.Child.Id, DevicePermissionCodes.DeviceView);
        _world.SeedMembership(NewUser(_world.Child.Id), _world.Device, role);
        _world.SeedMembership(NewUser(_world.Child.Id), _world.Device, role);
        _world.SeedMembership(NewUser(_world.Child.Id), _world.SecondDevice, role);
        _world.SeedMembership(NewUser(_world.Child.Id), thirdDevice, otherRole);
        var policyVersion = _world.Permissions.PolicyVersion;

        Assert.Equal("OK", UpdateRole(Master(), role, ["adc.view"]).Code);

        var advance = Assert.Single(_world.Epochs.Advances);
        Assert.True(advance.Batch);
        Assert.True(advance.Devices.ToHashSet().SetEquals([_world.Device.Id, _world.SecondDevice.Id]));
        Assert.Equal(2, advance.Devices.Count);
        Assert.Equal(policyVersion + 1, advance.PolicyVersionAtAdvance);
        Assert.Equal(0, _world.Epochs.Current(thirdDevice.Id));
        var write = Assert.Single(_world.Access.Writes);
        Assert.Equal(DeviceAccessChanges.RoleUpdated, write.Audit.Change);
        Assert.Equal(policyVersion, write.PolicyVersionAtWrite);
    }

    [Fact]
    public void Role_of_another_organization_is_not_found()
    {
        var role = _world.SeedRole(_world.Other.Id, DevicePermissionCodes.DeviceView);

        Assert.Equal("DEVICE_ROLE_NOT_FOUND", _world.UpdateRole().Handle(Master(), _world.Child.Id, role.Id, role.Name, null,
            [DevicePermissionCodes.DeviceView], role.Version).Code);
        Assert.Equal("DEVICE_ROLE_NOT_FOUND", _world.DeleteRole().Handle(Master(), _world.Child.Id, role.Id, role.Version).Code);
        Assert.Equal("DEVICE_ROLE_NOT_FOUND", _world.DeleteRole().Handle(Master(), _world.Child.Id, DeviceRoleId.New(), 1).Code);
        Assert.Empty(_world.Access.Attempts);
    }

    [Fact]
    public void Role_delete_reports_stale_version_before_in_use()
    {
        var holder = NewUser(_world.Child.Id);
        var role = _world.SeedRole(_world.Child.Id, DevicePermissionCodes.DeviceView);
        _world.SeedMembership(holder, _world.Device, role);
        var master = Master();

        var policyVersion = _world.Permissions.PolicyVersion;

        Assert.Equal("DEVICE_ROLE_VERSION_STALE", _world.DeleteRole().Handle(master, _world.Child.Id, role.Id, role.Version + 1).Code);
        Assert.Equal("DEVICE_ROLE_IN_USE", _world.DeleteRole().Handle(master, _world.Child.Id, role.Id, role.Version).Code);
        Assert.Equal(policyVersion, _world.Permissions.PolicyVersion);
        Assert.Equal("OK", _world.Remove().Handle(master, _world.Device.Id, holder, role.Id).Code);
        policyVersion = _world.Permissions.PolicyVersion;
        Assert.Equal("OK", _world.DeleteRole().Handle(master, _world.Child.Id, role.Id, role.Version).Code);
        Assert.Null(_world.Inner.Find(role.Id));
        Assert.Equal(policyVersion + 1, _world.Permissions.PolicyVersion);
    }

    [Fact]
    public void Role_create_accepts_name_and_description_at_their_maximum_length()
    {
        var outcome = _world.CreateRole().Handle(Master(), _world.Child.Id,
            new string('n', DeviceRole.NameMaxLength), new string('d', DeviceRole.DescriptionMaxLength),
            [DevicePermissionCodes.DeviceView]);

        Assert.Equal("OK", outcome.Code);
    }

    [Fact]
    public void Unexpected_store_failure_propagates_from_role_create()
    {
        _world.Access.BeforeWrite = () => throw new InvalidOperationException("STORE_UNAVAILABLE");

        Assert.Throws<InvalidOperationException>(() => CreateRole(Master(), _world.Child.Id, "Operator"));
    }

    // ---- helpers ----

    private UserId NewUser(OrganizationId? organization)
    {
        var user = new UserId(Guid.NewGuid());
        _world.Users.Add(user, organization);
        return user;
    }

    private AccessActor Master() => _world.MasterAdmin(new UserId(Guid.NewGuid()));

    // A non-master admin with permissions.manage over Child and, when own is
    // not empty, a membership on the device whose role holds own.
    private AccessActor ManagingAdmin(Device device, params string[] own)
    {
        var user = NewUser(_world.Child.Id);
        if (own.Length > 0)
            _world.SeedMembership(user, device, _world.SeedRole(device.OwnerOrganizationId, own));
        return _world.Admin(user, _world.Child.Id, "permissions.manage");
    }

    private DeviceAccessOutcome Assign(AccessActor actor, UserId target, DeviceRole role, DeviceRoleId? expectedRoleId = null) =>
        _world.Assign().Handle(actor, _world.Device.Id, target, role.Id, expectedRoleId, role.Version);

    private DeviceAccessOutcome CreateRole(AccessActor actor, OrganizationId organization, string name, params string[] permissions) =>
        _world.CreateRole().Handle(actor, organization, name, null,
            permissions.Length == 0 ? [DevicePermissionCodes.DeviceView] : permissions);

    private DeviceAccessOutcome UpdateRole(AccessActor actor, DeviceRole role, string[] permissions)
    {
        var current = _world.Inner.Find(role.Id)!;
        return _world.UpdateRole().Handle(actor, current.OrganizationId, current.Id, current.Name, current.Description,
            permissions, current.Version);
    }

    private static Guid RoleIdOf(string? json)
    {
        Assert.NotNull(json);
        using var document = JsonDocument.Parse(json!);
        return document.RootElement.GetProperty("roleId").GetGuid();
    }
}
