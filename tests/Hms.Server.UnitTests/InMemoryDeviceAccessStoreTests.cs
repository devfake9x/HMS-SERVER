using Hms.Server.Application.Authorization;
using Hms.Server.Domain.Authorization;
using Hms.Server.Domain.Devices;
using Hms.Server.Domain.Organizations;

namespace Hms.Server.UnitTests;

public sealed class InMemoryDeviceAccessStoreTests
{
    private static readonly UserId Grantor = new(Guid.NewGuid());
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private readonly InMemoryDeviceAccessStore _store = new();
    private readonly OrganizationId _organization = OrganizationId.New();

    [Fact]
    public void Membership_upsert_with_matching_expected_role_replaces_state()
    {
        var viewer = AddRole("Viewer", "device.view");
        var editor = AddRole("Editor", "device.view", "device.edit_basics");
        var user = NewUser();
        var device = DeviceId.New();
        var auditBefore = _store.Audit().Count;

        _store.Upsert(Membership(user, device, viewer), null, Audit(DeviceAccessChanges.MembershipAssigned, viewer.Id, device, user));
        _store.Upsert(Membership(user, device, editor), viewer.Id, Audit(DeviceAccessChanges.MembershipChanged, editor.Id, device, user));

        Assert.Equal(editor.Id, _store.Find(user, device)?.RoleId);
        Assert.Single(_store.ListByDevice(device));
        Assert.Equal(
            [DeviceAccessChanges.MembershipAssigned, DeviceAccessChanges.MembershipChanged],
            _store.Audit().Skip(auditBefore).Select(x => x.Change));
    }

    [Fact]
    public void Membership_upsert_rejects_expected_role_mismatch()
    {
        var viewer = AddRole("Viewer", "device.view");
        var editor = AddRole("Editor", "device.edit_basics");
        var user = NewUser();
        var other = NewUser();
        var device = DeviceId.New();
        _store.Upsert(Membership(user, device, viewer), null, Audit(DeviceAccessChanges.MembershipAssigned));
        var auditCount = _store.Audit().Count;

        AssertCode("DEVICE_MEMBERSHIP_CHANGED", () =>
            _store.Upsert(Membership(user, device, editor), null, Audit(DeviceAccessChanges.MembershipChanged)));
        AssertCode("DEVICE_MEMBERSHIP_CHANGED", () =>
            _store.Upsert(Membership(user, device, editor), editor.Id, Audit(DeviceAccessChanges.MembershipChanged)));
        AssertCode("DEVICE_MEMBERSHIP_CHANGED", () =>
            _store.Upsert(Membership(other, device, editor), viewer.Id, Audit(DeviceAccessChanges.MembershipAssigned)));

        Assert.Equal(viewer.Id, _store.Find(user, device)?.RoleId);
        Assert.Null(_store.Find(other, device));
        Assert.Equal(auditCount, _store.Audit().Count);
    }

    [Fact]
    public void Membership_remove_rejects_expected_role_mismatch()
    {
        var viewer = AddRole("Viewer", "device.view");
        var editor = AddRole("Editor", "device.edit_basics");
        var user = NewUser();
        var other = NewUser();
        var device = DeviceId.New();
        _store.Upsert(Membership(user, device, viewer), null, Audit(DeviceAccessChanges.MembershipAssigned));
        var auditCount = _store.Audit().Count;

        AssertCode("DEVICE_MEMBERSHIP_CHANGED", () =>
            _store.Remove(user, device, editor.Id, Audit(DeviceAccessChanges.MembershipRemoved)));
        AssertCode("DEVICE_MEMBERSHIP_CHANGED", () =>
            _store.Remove(user, device, null, Audit(DeviceAccessChanges.MembershipRemoved)));
        AssertCode("DEVICE_MEMBERSHIP_CHANGED", () =>
            _store.Remove(other, device, viewer.Id, Audit(DeviceAccessChanges.MembershipRemoved)));

        Assert.Equal(viewer.Id, _store.Find(user, device)?.RoleId);
        Assert.Equal(auditCount, _store.Audit().Count);
    }

    [Fact]
    public void Membership_remove_absent_with_null_expected_role_returns_false_without_audit()
    {
        var auditCount = _store.Audit().Count;

        var removed = _store.Remove(NewUser(), DeviceId.New(), null, Audit(DeviceAccessChanges.MembershipRemoved));

        Assert.False(removed);
        Assert.Equal(auditCount, _store.Audit().Count);
    }

    [Fact]
    public void Membership_remove_then_regrant_restores_access()
    {
        var viewer = AddRole("Viewer", "device.view");
        var user = NewUser();
        var device = DeviceId.New();
        _store.Upsert(Membership(user, device, viewer), null, Audit(DeviceAccessChanges.MembershipAssigned));

        Assert.True(_store.Remove(user, device, viewer.Id, Audit(DeviceAccessChanges.MembershipRemoved)));
        Assert.Null(_store.Find(user, device));
        Assert.Equal(DeviceAccessChanges.MembershipRemoved, _store.Audit()[^1].Change);

        _store.Upsert(Membership(user, device, viewer), null, Audit(DeviceAccessChanges.MembershipAssigned));

        Assert.Equal(viewer.Id, _store.Find(user, device)?.RoleId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Concurrent_upserts_with_same_expected_role_have_exactly_one_winner(bool currentlyAssigned)
    {
        var viewer = AddRole("Viewer", "device.view");
        var editor = AddRole("Editor", "device.edit_basics");
        var user = NewUser();
        var device = DeviceId.New();
        DeviceRoleId? expected = null;
        if (currentlyAssigned)
        {
            _store.Upsert(Membership(user, device, viewer), null, Audit(DeviceAccessChanges.MembershipAssigned));
            expected = viewer.Id;
        }

        var others = Enumerable.Range(0, 50).Select(_ => NewUser()).ToArray();
        var auditBefore = _store.Audit().Count;
        var wins = 0;
        var conflicts = 0;

        // Every fifth iteration assigns a different user on the same device;
        // the other 200 race for the same (user, device) pair.
        Parallel.For(0, 250, i =>
        {
            if (i % 5 == 4)
            {
                var otherUser = others[i / 5];
                _store.Upsert(Membership(otherUser, device, editor), null,
                    Audit(DeviceAccessChanges.MembershipAssigned, editor.Id, device, otherUser));
                return;
            }

            try
            {
                _store.Upsert(Membership(user, device, editor), expected,
                    Audit(DeviceAccessChanges.MembershipChanged, editor.Id, device, user));
                Interlocked.Increment(ref wins);
            }
            catch (InvalidOperationException error) when (error.Message == "DEVICE_MEMBERSHIP_CHANGED")
            {
                Interlocked.Increment(ref conflicts);
            }
        });

        Assert.Equal(1, wins);
        Assert.Equal(199, conflicts);
        Assert.Equal(editor.Id, _store.Find(user, device)?.RoleId);
        Assert.Equal(51, _store.ListByDevice(device).Count);
        Assert.Single(_store.ListByDevice(device), x => x.UserId == user);
        var newAudit = _store.Audit().Skip(auditBefore).ToArray();
        Assert.Equal(51, newAudit.Length);
        Assert.Single(newAudit, x => x.TargetUserId == user);
    }

    [Fact]
    public void Membership_upsert_rejects_unknown_role()
    {
        var unknown = DeviceRole.Create(_organization, "Unknown", null, []);
        var user = NewUser();
        var device = DeviceId.New();
        var auditCount = _store.Audit().Count;

        AssertCode("DEVICE_ROLE_NOT_FOUND", () =>
            _store.Upsert(Membership(user, device, unknown), null, Audit(DeviceAccessChanges.MembershipAssigned)));
        // The role check runs before the compare-and-set.
        AssertCode("DEVICE_ROLE_NOT_FOUND", () =>
            _store.Upsert(Membership(user, device, unknown), DeviceRoleId.New(), Audit(DeviceAccessChanges.MembershipChanged)));

        Assert.Null(_store.Find(user, device));
        Assert.Equal(auditCount, _store.Audit().Count);
    }

    [Fact]
    public void List_by_role_returns_only_memberships_of_that_role()
    {
        var viewer = AddRole("Viewer", "device.view");
        var editor = AddRole("Editor", "device.edit_basics");
        var first = NewUser();
        var second = NewUser();
        var line = DeviceId.New();
        var pump = DeviceId.New();
        _store.Upsert(Membership(first, line, viewer), null, Audit(DeviceAccessChanges.MembershipAssigned));
        _store.Upsert(Membership(first, pump, viewer), null, Audit(DeviceAccessChanges.MembershipAssigned));
        _store.Upsert(Membership(second, line, editor), null, Audit(DeviceAccessChanges.MembershipAssigned));

        Assert.Equal(
            new HashSet<(UserId, DeviceId)> { (first, line), (first, pump) },
            _store.ListByRole(viewer.Id).Select(x => (x.UserId, x.DeviceId)).ToHashSet());
        Assert.Equal(second, Assert.Single(_store.ListByRole(editor.Id)).UserId);
        Assert.Empty(_store.ListByRole(DeviceRoleId.New()));
        Assert.Equal(
            new HashSet<DeviceId> { line, pump },
            _store.ListByUser(first).Select(x => x.DeviceId).ToHashSet());
    }

    [Fact]
    public void Role_store_rejects_case_insensitive_duplicate_name_in_same_organization()
    {
        var operatorRole = AddRole("Operator");
        var technician = AddRole("Technician");
        var auditCount = _store.Audit().Count;

        AssertCode("DEVICE_ROLE_NAME_TAKEN", () =>
            _store.Add(DeviceRole.Create(_organization, " operator ", null, []), Audit(DeviceAccessChanges.RoleCreated)));
        var renamed = _store.Find(technician.Id)!;
        renamed.Update("OPERATOR", null, []);
        AssertCode("DEVICE_ROLE_NAME_TAKEN", () => _store.Update(renamed, Audit(DeviceAccessChanges.RoleUpdated)));

        Assert.Equal(2, _store.ListByOrganization(_organization).Count);
        Assert.Equal("Technician", _store.Find(technician.Id)!.Name);
        Assert.Equal(1, _store.Find(technician.Id)!.Version);
        Assert.Equal(auditCount, _store.Audit().Count);

        _store.Add(DeviceRole.Create(OrganizationId.New(), "Operator", null, []), Audit(DeviceAccessChanges.RoleCreated));
        var recased = _store.Find(operatorRole.Id)!;
        recased.Update("OPERATOR", null, []);
        _store.Update(recased, Audit(DeviceAccessChanges.RoleUpdated));

        Assert.Equal("OPERATOR", _store.Find(operatorRole.Id)!.Name);
    }

    [Fact]
    public void Role_update_keeps_the_stored_organization()
    {
        AddRole("Operator");
        var technician = AddRole("Technician");
        var otherOrganization = OrganizationId.New();
        _store.Add(DeviceRole.Create(otherOrganization, "Supervisor", null, []), Audit(DeviceAccessChanges.RoleCreated));

        // The name check runs in the stored organization, not in the one a copy claims.
        var claimsOther = DeviceRole.Rehydrate(technician.Id.Value, otherOrganization.Value, "Technician", null, [], 1);
        claimsOther.Update("Operator", null, []);
        AssertCode("DEVICE_ROLE_NAME_TAKEN", () => _store.Update(claimsOther, Audit(DeviceAccessChanges.RoleUpdated)));

        var moved = DeviceRole.Rehydrate(technician.Id.Value, otherOrganization.Value, "Technician", null, [], 1);
        moved.Update("Supervisor", null, []);
        _store.Update(moved, Audit(DeviceAccessChanges.RoleUpdated));

        var stored = _store.Find(technician.Id)!;
        Assert.Equal(_organization, stored.OrganizationId);
        Assert.Equal("Supervisor", stored.Name);
        Assert.Contains(technician.Id, _store.ListByOrganization(_organization).Select(x => x.Id));
        Assert.DoesNotContain(technician.Id, _store.ListByOrganization(otherOrganization).Select(x => x.Id));
    }

    [Fact]
    public void Role_update_requires_next_version()
    {
        var role = AddRole("Operator", "device.view");
        var first = _store.Find(role.Id)!;
        var second = _store.Find(role.Id)!;
        first.Update("Operator", null, ["device.view", "device.edit_basics"]);
        _store.Update(first, Audit(DeviceAccessChanges.RoleUpdated));
        var auditCount = _store.Audit().Count;

        second.Update("Operator", null, []);
        AssertCode("DEVICE_ROLE_VERSION_STALE", () => _store.Update(second, Audit(DeviceAccessChanges.RoleUpdated)));
        var unchanged = _store.Find(role.Id)!;
        AssertCode("DEVICE_ROLE_VERSION_STALE", () => _store.Update(unchanged, Audit(DeviceAccessChanges.RoleUpdated)));
        AssertCode("DEVICE_ROLE_NOT_FOUND", () =>
            _store.Update(DeviceRole.Create(_organization, "Missing", null, []), Audit(DeviceAccessChanges.RoleUpdated)));

        var stored = _store.Find(role.Id)!;
        Assert.Equal(2, stored.Version);
        Assert.Equal(["device.edit_basics", "device.view"], stored.Permissions.Order(StringComparer.Ordinal));
        Assert.Equal(auditCount, _store.Audit().Count);
    }

    [Fact]
    public void Role_update_reports_stale_before_name_taken()
    {
        var role = AddRole("Operator");
        AddRole("Supervisor");
        var current = _store.Find(role.Id)!;
        current.Update("Operator", "Second version", []);
        _store.Update(current, Audit(DeviceAccessChanges.RoleUpdated));

        // A copy of version 1 that also renames onto a taken name.
        var stale = DeviceRole.Rehydrate(role.Id.Value, _organization.Value, "Operator", null, [], 1);
        stale.Update("Supervisor", null, []);

        AssertCode("DEVICE_ROLE_VERSION_STALE", () => _store.Update(stale, Audit(DeviceAccessChanges.RoleUpdated)));
    }

    [Fact]
    public void Role_store_returns_copies()
    {
        var role = AddRole("Operator", "device.view");

        role.Update("Changed by caller", null, []);
        _store.Find(role.Id)!.Update("Changed after find", null, []);
        _store.ListByOrganization(_organization)[0].Update("Changed after list", null, []);

        var stored = _store.Find(role.Id)!;
        Assert.Equal("Operator", stored.Name);
        Assert.Equal(1, stored.Version);
        Assert.Equal(["device.view"], stored.Permissions);
    }

    [Fact]
    public void Role_remove_checks_missing_then_version_then_in_use()
    {
        var role = AddRole("Operator", "device.view");
        _store.Upsert(Membership(NewUser(), DeviceId.New(), role), null, Audit(DeviceAccessChanges.MembershipAssigned));
        var auditCount = _store.Audit().Count;

        AssertCode("DEVICE_ROLE_NOT_FOUND", () => _store.Remove(DeviceRoleId.New(), 1, Audit(DeviceAccessChanges.RoleDeleted)));
        AssertCode("DEVICE_ROLE_VERSION_STALE", () => _store.Remove(role.Id, 2, Audit(DeviceAccessChanges.RoleDeleted)));
        AssertCode("DEVICE_ROLE_IN_USE", () => _store.Remove(role.Id, 1, Audit(DeviceAccessChanges.RoleDeleted)));

        Assert.NotNull(_store.Find(role.Id));
        Assert.Equal(auditCount, _store.Audit().Count);
    }

    [Fact]
    public void Role_remove_with_current_version_deletes_role_and_writes_audit()
    {
        var role = AddRole("Operator", "device.view");

        _store.Remove(role.Id, 1, Audit(DeviceAccessChanges.RoleDeleted, role.Id));

        Assert.Null(_store.Find(role.Id));
        Assert.Empty(_store.ListByOrganization(_organization));
        var deleted = Assert.Single(_store.Audit(), x => x.Change == DeviceAccessChanges.RoleDeleted);
        Assert.Equal(role.Id, deleted.RoleId);
    }

    [Fact]
    public void Audit_entry_validates_change_only()
    {
        Assert.Equal(
            ["ROLE_CREATED", "ROLE_UPDATED", "ROLE_DELETED", "MEMBERSHIP_ASSIGNED", "MEMBERSHIP_CHANGED", "MEMBERSHIP_REMOVED"],
            new[]
            {
                DeviceAccessChanges.RoleCreated, DeviceAccessChanges.RoleUpdated, DeviceAccessChanges.RoleDeleted,
                DeviceAccessChanges.MembershipAssigned, DeviceAccessChanges.MembershipChanged,
                DeviceAccessChanges.MembershipRemoved
            });
        var unknown = Audit("ROLE_RENAMED");

        Assert.False(DeviceAccessChanges.IsKnown("ROLE_RENAMED"));
        Assert.False(DeviceAccessChanges.IsKnown("role_created"));
        Assert.Throws<ArgumentException>(unknown.EnsureValid);
        Assert.Throws<ArgumentException>(() => _store.Add(DeviceRole.Create(_organization, "Operator", null, []), unknown));
        Assert.Empty(_store.ListByOrganization(_organization));
        Assert.Empty(_store.Audit());

        var large = Audit(DeviceAccessChanges.RoleUpdated) with
        {
            PreviousValue = new string('p', 4096),
            NewValue = new string('n', 4096)
        };
        large.EnsureValid();
    }

    [Fact]
    public void Every_write_rejects_an_unknown_audit_change_without_writing()
    {
        var viewer = AddRole("Viewer", "device.view");
        var user = NewUser();
        var device = DeviceId.New();
        _store.Upsert(Membership(user, device, viewer), null, Audit(DeviceAccessChanges.MembershipAssigned));
        var auditCount = _store.Audit().Count;
        var unknown = Audit("ROLE_RENAMED");
        var renamed = _store.Find(viewer.Id)!;
        renamed.Update("Renamed", null, ["device.view"]);

        Assert.Throws<ArgumentException>(() => _store.Update(renamed, unknown));
        Assert.Throws<ArgumentException>(() => _store.Remove(viewer.Id, 1, unknown));
        Assert.Throws<ArgumentException>(() => _store.Upsert(Membership(NewUser(), device, viewer), null, unknown));
        Assert.Throws<ArgumentException>(() => _store.Remove(user, device, viewer.Id, unknown));

        Assert.Equal("Viewer", _store.Find(viewer.Id)!.Name);
        Assert.Single(_store.ListByDevice(device));
        Assert.Equal(auditCount, _store.Audit().Count);
    }

    private DeviceRole AddRole(string name, params string[] permissions)
    {
        var role = DeviceRole.Create(_organization, name, null, permissions);
        _store.Add(role, Audit(DeviceAccessChanges.RoleCreated, role.Id));
        return role;
    }

    private static UserId NewUser() => new(Guid.NewGuid());

    private static DeviceMembership Membership(UserId user, DeviceId device, DeviceRole role) =>
        new(user, device, role.Id, Grantor, Now);

    private static DeviceAccessAuditEntry Audit(
        string change,
        DeviceRoleId? roleId = null,
        DeviceId? deviceId = null,
        UserId? targetUserId = null) =>
        new(Guid.NewGuid(), Grantor, change, null, deviceId, targetUserId, roleId, null, null, Now);

    private static void AssertCode(string code, Action action) =>
        Assert.Equal(code, Assert.Throws<InvalidOperationException>(action).Message);
}
