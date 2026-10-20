using System.Collections.Frozen;
using System.Security.Claims;
using Hms.Server.Application.Authorization;
using Hms.Server.Application.Commands;
using Hms.Server.Application.Management;
using Hms.Server.Application.Realtime;
using Hms.Server.Domain.Authorization;
using Hms.Server.Domain.Commands;
using Hms.Server.Domain.Devices;
using Hms.Server.Domain.Organizations;
using Hms.Server.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace Hms.Server.UnitTests;

public sealed class PermissionTests
{
    private readonly DeviceAccessTestFixture _world = new();

    [Fact]
    public void Three_permission_grants_advance_one_shared_policy_version()
    {
        var store = _world.Permissions;
        var user = new UserId(Guid.NewGuid());
        var master = _world.MasterAdmin(new UserId(Guid.NewGuid()));
        var device = DeviceId.New();

        var feature = _world.GrantFeature().Handle(master, user, "device.read");
        var resource = _world.GrantResource().Handle(master, user, _world.Child.Id);
        var action = new GrantDeviceActionHandler(store).Handle(user, device, "get_info");

        Assert.Equal(1, feature.Feature!.PolicyVersion);
        Assert.Equal(2, resource.Resource!.PolicyVersion);
        Assert.Equal(3, action.PolicyVersion);
        Assert.Equal(3, store.PolicyVersion);
    }

    [Fact]
    public void Revocation_advances_device_epoch_and_changes_lifecycle()
    {
        var organizations = new InMemoryOrganizationStore();
        var devices = new InMemoryDeviceStore();
        var organization = Organization.Create("Master");
        organizations.Add(organization);
        var device = Device.Create("ESP-001", organization.Id);
        device.Activate();
        devices.Add(device);
        var epochs = new InMemoryAuthorizationEpochStore();

        new RevokeDeviceHandler(devices, epochs).Handle(device.Id);

        Assert.Equal(DeviceLifecycleState.Revoked, device.State);
        Assert.Equal(DeviceLifecycleState.Revoked, devices.Find(device.Id)!.State);
        Assert.Equal(1, epochs.Current(device.Id));
    }

    [Fact]
    public async Task Revocation_marks_in_flight_commands_as_device_revoked()
    {
        var organizations = new InMemoryOrganizationStore();
        var devices = new InMemoryDeviceStore();
        var organization = Organization.Create("Master");
        organizations.Add(organization);
        var device = Device.Create("ESP-001", organization.Id);
        device.Activate();
        devices.Add(device);
        var audit = new InMemoryCommandAuditStore();
        var request = new DeviceCommandRequest(
            device.Id,
            new UserId(Guid.NewGuid()),
            "system",
            "get_info",
            new Dictionary<string, object?>(),
            "pending-revoke",
            DateTimeOffset.UtcNow);
        var plan = new CommandExecutionPlan(
            request,
            new CommandDefinition("system", "get_info", "device.read", CommandAvailability.Enabled, false, true, 120_000),
            device.KeyGeneration,
            request.RequestedAt.AddMinutes(2));
        var reservation = await audit.ReserveAsync(plan, CommandRequestFingerprint.Compute(request));
        Assert.Equal(CommandAuditReservationStatus.Claimed, reservation.Status);

        new RevokeDeviceHandler(devices, new InMemoryAuthorizationEpochStore(), audit).Handle(device.Id);

        await audit.RecordAsync(
            plan,
            new CommandExecutionResult(CommandExecutionStatus.MqttDeferred, "MQTT_DEFERRED", request.CorrelationId));

        var result = audit.Find(device.Id, request.CorrelationId);
        Assert.NotNull(result);
        Assert.Equal(CommandExecutionStatus.Failed, result!.Status);
        Assert.Equal("DEVICE_REVOKED", result.Code);
    }

    [Fact]
    public void Organization_scope_includes_all_descendants()
    {
        var root = Organization.Create("Root");
        var child = Organization.Create("Child", root.Id);
        var grandchild = Organization.Create("Grandchild", child.Id);

        var expanded = OrganizationScopeResolver.Expand(
            [root.Id],
            [root, child, grandchild]);

        Assert.Contains(root.Id, expanded);
        Assert.Contains(child.Id, expanded);
        Assert.Contains(grandchild.Id, expanded);
    }

    [Fact]
    public void Feature_wildcard_deny_overrides_exact_allow()
    {
        var resolved = PermissionGrantResolver.Features(["device.read"], ["*"], []);

        Assert.False(resolved.Has("device.read", isMasterAdmin: false));
    }

    [Fact]
    public void Reserved_role_can_only_be_managed_by_master_admin()
    {
        Assert.True(RoleAuthorizationPolicy.IsReserved(" MASTER-ADMIN "));
        Assert.False(RoleAuthorizationPolicy.CanManageReservedRole(false));
        Assert.True(RoleAuthorizationPolicy.CanManageReservedRole(true));
    }

    [Fact]
    public void Reserved_role_check_catches_names_identity_folds_onto_master_admin()
    {
        // U+017F (long s) is no "s" to an ordinal ignore-case compare, but Identity's
        // upper-invariant normalizer turns it into S, so these names reach the real role.
        var longS = (char)0x017F;

        Assert.True(RoleAuthorizationPolicy.IsReserved($"ma{longS}ter-admin"));
        Assert.True(RoleAuthorizationPolicy.IsReserved($" MA{longS}TER-ADMIN "));
        Assert.False(RoleAuthorizationPolicy.IsReserved("master-admins"));
        Assert.False(RoleAuthorizationPolicy.IsReserved(null));
    }

    // ---- latest-wins resolution ----

    [Fact]
    public void Feature_regrant_after_deny_is_granted()
    {
        var user = new UserId(Guid.NewGuid());

        var resolved = PermissionGrantResolver.Features([], [],
        [
            Feature(user, "users.manage", false, 1),
            Feature(user, "users.manage", true, 2),
            Feature(user, "users.manage", false, 3)
        ]);

        Assert.True(resolved.Has("users.manage", isMasterAdmin: false));
        Assert.DoesNotContain("users.manage", resolved.Denied);
    }

    [Fact]
    public void Persisted_feature_grant_overrides_claim_baseline()
    {
        var user = new UserId(Guid.NewGuid());

        var resolved = PermissionGrantResolver.Features(["users.manage"], ["devices.manage"],
        [
            Feature(user, "users.manage", true, 1),
            Feature(user, "devices.manage", false, 2)
        ]);

        Assert.False(resolved.Has("users.manage", isMasterAdmin: false));
        Assert.True(resolved.Has("devices.manage", isMasterAdmin: false));
        // Allowed feeds AccessActor.FeaturePermissions, which is read without Has.
        Assert.DoesNotContain("users.manage", resolved.Allowed);
    }

    [Fact]
    public void Claim_deny_wins_over_claim_allow_on_the_same_key()
    {
        var resolved = PermissionGrantResolver.Features(["users.manage"], ["users.manage"], []);

        Assert.False(resolved.Has("users.manage", isMasterAdmin: false));
        Assert.DoesNotContain("users.manage", resolved.Allowed);
    }

    [Fact]
    public void Feature_wildcard_allow_grants_nothing()
    {
        var user = new UserId(Guid.NewGuid());

        var resolved = PermissionGrantResolver.Features(["*"], [], [Feature(user, "*", false, 1)]);

        Assert.Empty(resolved.Allowed);
        Assert.False(resolved.Has("users.manage", isMasterAdmin: false));
        Assert.False(resolved.Has("*", isMasterAdmin: false));
    }

    [Fact]
    public void Master_admin_ignores_feature_and_wildcard_denies()
    {
        var user = new UserId(Guid.NewGuid());

        var resolved = PermissionGrantResolver.Features([], [],
        [
            Feature(user, "users.manage", true, 1),
            Feature(user, "*", true, 2)
        ]);

        Assert.True(resolved.Has("users.manage", isMasterAdmin: true));
        Assert.False(resolved.Has("users.manage", isMasterAdmin: false));
        Assert.Contains("*", resolved.Denied);
        Assert.Contains("users.manage", resolved.Denied);
    }

    [Fact]
    public void Resource_regrant_after_deny_restores_scope()
    {
        var user = new UserId(Guid.NewGuid());
        var organization = OrganizationId.New();

        var resolved = PermissionGrantResolver.Organizations([], [],
        [
            Resource(user, organization, false, 1),
            Resource(user, organization, true, 2),
            Resource(user, organization, false, 3)
        ]);

        Assert.Contains(organization, resolved.Allowed);
        Assert.DoesNotContain(organization, resolved.Denied);
    }

    [Fact]
    public void Latest_feature_grants_keep_the_last_row_per_key()
    {
        var master = _world.MasterAdmin(new UserId(Guid.NewGuid()));
        var target = new UserId(Guid.NewGuid());
        var grants = _world.GrantFeature();
        grants.Handle(master, target, "users.manage");
        grants.Handle(master, target, "users.manage", deny: true);
        grants.Handle(master, target, "devices.manage");
        var last = grants.Handle(master, target, "users.manage").Feature!;
        grants.Handle(master, target, "devices.manage", deny: true);

        var latest = PermissionGrantResolver.LatestFeatureGrants(_world.Permissions.FeatureGrants(target));

        Assert.Equal(2, latest.Count);
        var users = Assert.Single(latest, x => x.Permission == "users.manage");
        Assert.False(users.Deny);
        Assert.Equal(last.PolicyVersion, users.PolicyVersion);
        Assert.True(Assert.Single(latest, x => x.Permission == "devices.manage").Deny);
        Assert.True(latest[0].PolicyVersion < latest[1].PolicyVersion);
    }

    [Fact]
    public void Latest_resource_grants_keep_the_last_row_per_key()
    {
        var master = _world.MasterAdmin(new UserId(Guid.NewGuid()));
        var target = new UserId(Guid.NewGuid());
        var grants = _world.GrantResource();
        grants.Handle(master, target, _world.Child.Id);
        grants.Handle(master, target, _world.Child.Id, deny: true);
        grants.Handle(master, target, _world.Other.Id);
        var last = grants.Handle(master, target, _world.Child.Id).Resource!;
        grants.Handle(master, target, _world.Other.Id, deny: true);

        var latest = PermissionGrantResolver.LatestResourceGrants(_world.Permissions.ResourceGrants(target));

        Assert.Equal(2, latest.Count);
        var child = Assert.Single(latest, x => x.OrganizationId == _world.Child.Id);
        Assert.False(child.Deny);
        Assert.Equal(last.PolicyVersion, child.PolicyVersion);
        Assert.True(Assert.Single(latest, x => x.OrganizationId == _world.Other.Id).Deny);
    }

    [Fact]
    public void Latest_grant_is_chosen_by_policy_version_not_input_order()
    {
        var user = new UserId(Guid.NewGuid());
        FeaturePermissionGrant[] grants =
        [
            Feature(user, "users.manage", false, 3),
            Feature(user, "devices.manage", false, 2),
            Feature(user, "users.manage", true, 1)
        ];

        var forward = PermissionGrantResolver.LatestFeatureGrants(grants);
        var reversed = PermissionGrantResolver.LatestFeatureGrants(grants.Reverse());

        Assert.Equal(forward, reversed);
        Assert.Equal([2L, 3L], forward.Select(x => x.PolicyVersion));
        Assert.False(Assert.Single(forward, x => x.Permission == "users.manage").Deny);
    }

    [Fact]
    public void Latest_grant_with_equal_policy_version_prefers_deny()
    {
        var user = new UserId(Guid.NewGuid());
        var organization = OrganizationId.New();
        FeaturePermissionGrant[] features = [Feature(user, "users.manage", false, 5), Feature(user, "users.manage", true, 5)];
        ResourceScopeGrant[] resources = [Resource(user, organization, false, 5), Resource(user, organization, true, 5)];

        Assert.True(Assert.Single(PermissionGrantResolver.LatestFeatureGrants(features)).Deny);
        Assert.True(Assert.Single(PermissionGrantResolver.LatestFeatureGrants(features.Reverse())).Deny);
        Assert.True(Assert.Single(PermissionGrantResolver.LatestResourceGrants(resources)).Deny);
        Assert.True(Assert.Single(PermissionGrantResolver.LatestResourceGrants(resources.Reverse())).Deny);
    }

    // ---- feature policy handler ----

    [Fact]
    public async Task Feature_policy_handler_ignores_wildcard_allow()
    {
        var user = new UserId(Guid.NewGuid());
        _world.Permissions.GrantFeature(new FeaturePermissionGrant(user, "*", false, _world.Permissions.Advance()));

        Assert.False(await PolicySucceeds(Principal(user, new Claim("perm", "*")), "users.manage"));
    }

    [Fact]
    public async Task Feature_policy_handler_uses_latest_persisted_grant()
    {
        var user = new UserId(Guid.NewGuid());
        var deniedUser = new UserId(Guid.NewGuid());
        Persist(user, "users.manage", deny: false);
        Persist(user, "users.manage", deny: true);
        Persist(user, "users.manage", deny: false);
        Persist(deniedUser, "users.manage", deny: false);
        Persist(deniedUser, "users.manage", deny: true);

        Assert.True(await PolicySucceeds(Principal(user), "users.manage"));
        Assert.False(await PolicySucceeds(Principal(deniedUser), "users.manage"));
    }

    [Fact]
    public async Task Feature_policy_handler_master_admin_passes_despite_persisted_deny()
    {
        var master = new UserId(Guid.NewGuid());
        var member = new UserId(Guid.NewGuid());
        Persist(master, "users.manage", deny: true);
        Persist(member, "users.manage", deny: true);
        var masterRole = new Claim(ClaimTypes.Role, RoleAuthorizationPolicy.MasterAdminRole);

        Assert.True(await PolicySucceeds(Principal(master, masterRole), "users.manage"));
        Assert.True(await PolicySucceeds(Principal(master, masterRole, new Claim("deny-perm", "*")), "users.manage"));
        Assert.False(await PolicySucceeds(Principal(member, new Claim("perm", "users.manage")), "users.manage"));
    }

    // ---- grant handlers ----

    [Fact]
    public void Wildcard_allow_grant_is_rejected_for_non_master()
    {
        var target = NewUser(_world.Child.Id);
        var admin = _world.Admin(NewUser(_world.Child.Id), _world.Child.Id, "permissions.manage");

        Assert.Equal("WILDCARD_GRANT_FORBIDDEN", _world.GrantFeature().Handle(admin, target, "*").Code);
        Assert.Equal(0, _world.Permissions.PolicyVersion);
    }

    [Fact]
    public void Master_admin_lifts_a_wildcard_deny_with_a_wildcard_allow()
    {
        var target = NewUser(_world.Child.Id);
        Assert.Equal("OK", _world.GrantFeature().Handle(Master(), target, "users.manage").Code);
        Assert.Equal("OK", _world.GrantFeature().Handle(Master(), target, "*", deny: true).Code);
        Assert.False(ResolvedFeatures(target).Has("users.manage", isMasterAdmin: false));

        Assert.Equal("OK", _world.GrantFeature().Handle(Master(), target, "*").Code);

        // The lift only cancels the block: it grants nothing of its own.
        var resolved = ResolvedFeatures(target);
        Assert.True(resolved.Has("users.manage", isMasterAdmin: false));
        Assert.False(resolved.Has("devices.manage", isMasterAdmin: false));
        Assert.DoesNotContain("*", resolved.Allowed);
        Assert.DoesNotContain("*", resolved.Denied);
    }

    [Fact]
    public void Wildcard_deny_grant_requires_master_admin()
    {
        var target = NewUser(_world.Child.Id);
        var admin = _world.Admin(NewUser(_world.Child.Id), _world.Child.Id, "permissions.manage");

        Assert.Equal("WILDCARD_GRANT_FORBIDDEN", _world.GrantFeature().Handle(admin, target, "*", deny: true).Code);
        Assert.Equal("OK", _world.GrantFeature().Handle(Master(), target, "*", deny: true).Code);
    }

    [Fact]
    public void Non_master_cannot_self_grant_feature_or_scope()
    {
        var admin = _world.Admin(NewUser(_world.Child.Id), _world.Child.Id, "permissions.manage", "devices.manage");

        Assert.Equal("SELF_GRANT_FORBIDDEN", _world.GrantFeature().Handle(admin, admin.UserId, "devices.manage").Code);
        Assert.Equal("SELF_GRANT_FORBIDDEN", _world.GrantResource().Handle(admin, admin.UserId, _world.Child.Id).Code);
    }

    [Fact]
    public void Grantor_must_hold_feature_permission()
    {
        var target = NewUser(_world.Child.Id);
        var admin = _world.Admin(NewUser(_world.Child.Id), _world.Child.Id, "permissions.manage");

        Assert.Equal("GRANT_EXCEEDS_OWN_PERMISSIONS", _world.GrantFeature().Handle(admin, target, "users.manage").Code);
        Assert.Equal("OK", _world.GrantFeature().Handle(admin, target, "users.manage", deny: true).Code);
    }

    [Fact]
    public void Master_admin_can_grant_any_feature_to_self()
    {
        var master = Master();

        Assert.Equal("OK", _world.GrantFeature().Handle(master, master.UserId, "users.manage").Code);
        Assert.Equal("OK", _world.GrantResource().Handle(master, master.UserId, _world.Other.Id).Code);
    }

    [Fact]
    public void Non_master_cannot_grant_scope_outside_own_scope()
    {
        var target = NewUser(_world.Child.Id);
        var admin = _world.Admin(NewUser(_world.Child.Id), _world.Child.Id, "permissions.manage");

        Assert.Equal("GRANT_EXCEEDS_OWN_PERMISSIONS", _world.GrantResource().Handle(admin, target, _world.Parent.Id).Code);
        Assert.Equal("GRANT_EXCEEDS_OWN_PERMISSIONS", _world.GrantResource().Handle(admin, target, _world.Other.Id, deny: true).Code);
        Assert.Equal("OK", _world.GrantResource().Handle(admin, target, _world.Child.Id).Code);
    }

    [Fact]
    public void Non_master_cannot_grant_or_deny_an_organization_whose_subtree_leaves_their_scope()
    {
        // Scope on Parent with Child carved out: the admin's authority no longer covers
        // everything a grant or deny on Parent reaches.
        var scope = OrganizationScopeResolver.Expand([_world.Parent.Id], _world.Organizations.List())
            .Where(x => x != _world.Child.Id).ToFrozenSet();
        var admin = new AccessActor(NewUser(_world.Parent.Id), false, scope, new[] { "permissions.manage" }.ToFrozenSet());
        var target = NewUser(_world.Parent.Id);

        Assert.Equal("GRANT_EXCEEDS_OWN_PERMISSIONS", _world.GrantResource().Handle(admin, target, _world.Parent.Id).Code);
        Assert.Equal("GRANT_EXCEEDS_OWN_PERMISSIONS",
            _world.GrantResource().Handle(admin, target, _world.Parent.Id, deny: true).Code);
        Assert.Empty(_world.Permissions.ResourceGrants(target));
    }

    [Fact]
    public void Non_master_can_grant_or_deny_an_organization_whose_subtree_is_in_their_scope()
    {
        var admin = _world.Admin(NewUser(_world.Parent.Id), _world.Parent.Id, "permissions.manage");
        var target = NewUser(_world.Parent.Id);

        Assert.Equal("OK", _world.GrantResource().Handle(admin, target, _world.Parent.Id).Code);
        Assert.Equal("OK", _world.GrantResource().Handle(admin, target, _world.Child.Id, deny: true).Code);
    }

    [Fact]
    public void Rejected_grant_does_not_advance_policy_version()
    {
        var admin = _world.Admin(NewUser(_world.Child.Id), _world.Child.Id, "permissions.manage");
        var target = NewUser(_world.Child.Id);

        _world.GrantFeature().Handle(admin, target, "users.manage");
        _world.GrantFeature().Handle(admin, admin.UserId, "permissions.manage");
        _world.GrantResource().Handle(admin, target, _world.Other.Id);

        Assert.Equal(0, _world.Permissions.PolicyVersion);
        Assert.Empty(_world.Permissions.FeatureGrants(target));
        Assert.Empty(_world.Permissions.ResourceGrants(target));
    }

    [Fact]
    public void Feature_grant_rejected_when_target_scope_exceeds_grantor_scope()
    {
        var admin = _world.Admin(NewUser(_world.Child.Id), _world.Child.Id, "permissions.manage", "devices.manage");
        var wideTarget = NewUser(_world.Child.Id);
        var plainTarget = NewUser(_world.Child.Id);
        _world.GrantResource().Handle(Master(), wideTarget, _world.Parent.Id);

        Assert.Equal("GRANT_EXCEEDS_OWN_PERMISSIONS", _world.GrantFeature().Handle(admin, wideTarget, "devices.manage").Code);
        Assert.Equal("OK", _world.GrantFeature().Handle(admin, plainTarget, "devices.manage").Code);
    }

    [Fact]
    public void Resource_grant_rejected_when_target_holds_features_grantor_lacks()
    {
        var admin = _world.Admin(NewUser(_world.Child.Id), _world.Child.Id, "permissions.manage");
        var strongTarget = NewUser(_world.Child.Id);
        var plainTarget = NewUser(_world.Child.Id);
        _world.GrantFeature().Handle(Master(), strongTarget, "users.manage");

        Assert.Equal("GRANT_EXCEEDS_OWN_PERMISSIONS", _world.GrantResource().Handle(admin, strongTarget, _world.Child.Id).Code);
        Assert.Equal("OK", _world.GrantResource().Handle(admin, plainTarget, _world.Child.Id).Code);
    }

    [Fact]
    public void Non_master_cannot_reverse_master_admin_feature_deny()
    {
        var admin = _world.Admin(NewUser(_world.Child.Id), _world.Child.Id, "permissions.manage", "devices.manage");
        var target = NewUser(_world.Child.Id);
        _world.GrantFeature().Handle(Master(), target, "devices.manage", deny: true);

        Assert.Equal("GRANT_OVERRIDES_MASTER_DECISION", _world.GrantFeature().Handle(admin, target, "devices.manage").Code);

        // Writing an own deny first does not launder the reversal.
        Assert.Equal("OK", _world.GrantFeature().Handle(admin, target, "devices.manage", deny: true).Code);
        Assert.Equal("GRANT_OVERRIDES_MASTER_DECISION", _world.GrantFeature().Handle(admin, target, "devices.manage").Code);
    }

    [Fact]
    public void Non_master_cannot_reverse_master_admin_resource_deny()
    {
        var admin = _world.Admin(NewUser(_world.Child.Id), _world.Child.Id, "permissions.manage");
        var target = NewUser(_world.Child.Id);
        _world.GrantResource().Handle(Master(), target, _world.Child.Id, deny: true);

        Assert.Equal("GRANT_OVERRIDES_MASTER_DECISION", _world.GrantResource().Handle(admin, target, _world.Child.Id).Code);

        Assert.Equal("OK", _world.GrantResource().Handle(admin, target, _world.Child.Id, deny: true).Code);
        Assert.Equal("GRANT_OVERRIDES_MASTER_DECISION", _world.GrantResource().Handle(admin, target, _world.Child.Id).Code);
    }

    [Fact]
    public void Master_admin_can_reverse_own_deny()
    {
        var target = NewUser(_world.Child.Id);
        _world.GrantFeature().Handle(Master(), target, "devices.manage", deny: true);
        _world.GrantResource().Handle(Master(), target, _world.Child.Id, deny: true);

        Assert.Equal("OK", _world.GrantFeature().Handle(Master(), target, "devices.manage").Code);
        Assert.Equal("OK", _world.GrantResource().Handle(Master(), target, _world.Child.Id).Code);
    }

    [Fact]
    public void Non_master_may_allow_after_master_reversed_own_deny()
    {
        var admin = _world.Admin(NewUser(_world.Child.Id), _world.Child.Id, "permissions.manage", "devices.manage");
        var featureTarget = NewUser(_world.Child.Id);
        var scopeTarget = NewUser(_world.Child.Id);
        var master = Master();
        _world.GrantFeature().Handle(master, featureTarget, "devices.manage", deny: true);
        _world.GrantFeature().Handle(master, featureTarget, "devices.manage");
        _world.GrantResource().Handle(master, scopeTarget, _world.Child.Id, deny: true);
        _world.GrantResource().Handle(master, scopeTarget, _world.Child.Id);

        // Only the latest master-issued row counts: the master took its own deny back.
        Assert.Equal("OK", _world.GrantFeature().Handle(admin, featureTarget, "devices.manage").Code);
        Assert.Equal("OK", _world.GrantResource().Handle(admin, scopeTarget, _world.Child.Id).Code);
    }

    [Fact]
    public void Non_master_can_reverse_non_master_deny()
    {
        var first = _world.Admin(NewUser(_world.Child.Id), _world.Child.Id, "permissions.manage", "devices.manage");
        var second = _world.Admin(NewUser(_world.Child.Id), _world.Child.Id, "permissions.manage", "devices.manage");
        var target = NewUser(_world.Child.Id);
        _world.GrantFeature().Handle(first, target, "devices.manage", deny: true);

        Assert.Equal("OK", _world.GrantFeature().Handle(second, target, "devices.manage").Code);
    }

    [Fact]
    public void Grants_record_grantor_and_master_flag()
    {
        var master = Master();
        var admin = _world.Admin(NewUser(_world.Child.Id), _world.Child.Id, "permissions.manage", "devices.manage");
        var target = NewUser(_world.Child.Id);

        // Scope first: once the target holds users.manage the admin may no longer widen its scope.
        var scope = _world.GrantResource().Handle(admin, target, _world.Child.Id).Resource!;
        var byMaster = _world.GrantFeature().Handle(master, target, "users.manage").Feature!;
        var byAdmin = _world.GrantFeature().Handle(admin, target, "devices.manage").Feature!;

        Assert.Equal(master.UserId, byMaster.GrantedBy);
        Assert.True(byMaster.GrantedByMasterAdmin);
        Assert.Equal(admin.UserId, byAdmin.GrantedBy);
        Assert.False(byAdmin.GrantedByMasterAdmin);
        Assert.Equal(admin.UserId, scope.GrantedBy);
        Assert.False(scope.GrantedByMasterAdmin);
        Assert.Contains(byMaster, _world.Permissions.FeatureGrants(target));
    }

    [Fact]
    public void Deny_targeting_master_admin_is_rejected()
    {
        var masterTarget = new UserId(Guid.NewGuid());
        _world.Users.AddMasterAdmin(masterTarget, _world.Child.Id);
        var admin = _world.Admin(NewUser(_world.Child.Id), _world.Child.Id, "permissions.manage", "devices.manage");

        Assert.Equal("GRANT_TARGET_IS_MASTER_ADMIN", _world.GrantFeature().Handle(Master(), masterTarget, "users.manage", deny: true).Code);
        Assert.Equal("GRANT_TARGET_IS_MASTER_ADMIN", _world.GrantFeature().Handle(admin, masterTarget, "devices.manage", deny: true).Code);
        Assert.Equal("GRANT_TARGET_IS_MASTER_ADMIN", _world.GrantResource().Handle(Master(), masterTarget, _world.Child.Id, deny: true).Code);
        Assert.Equal("GRANT_TARGET_IS_MASTER_ADMIN", _world.GrantResource().Handle(admin, masterTarget, _world.Child.Id, deny: true).Code);
        Assert.Equal(0, _world.Permissions.PolicyVersion);
        Assert.Equal("OK", _world.GrantFeature().Handle(Master(), masterTarget, "users.manage").Code);
    }

    private UserId NewUser(OrganizationId? organization)
    {
        var user = new UserId(Guid.NewGuid());
        _world.Users.Add(user, organization);
        return user;
    }

    private AccessActor Master() => _world.MasterAdmin(new UserId(Guid.NewGuid()));

    private ResolvedFeaturePermissions ResolvedFeatures(UserId user) =>
        PermissionGrantResolver.Features([], [], _world.Permissions.FeatureGrants(user));

    private void Persist(UserId user, string permission, bool deny) =>
        _world.Permissions.GrantFeature(new FeaturePermissionGrant(user, permission, deny, _world.Permissions.Advance()));

    private async Task<bool> PolicySucceeds(ClaimsPrincipal principal, string permission)
    {
        var requirement = new FeaturePermissionRequirement(permission);
        var context = new AuthorizationHandlerContext([requirement], principal, null);
        await new FeaturePermissionAuthorizationHandler(_world.Permissions).HandleAsync(context);
        return context.HasSucceeded;
    }

    private static ClaimsPrincipal Principal(UserId user, params Claim[] claims) => new(new ClaimsIdentity(
        claims.Prepend(new Claim(ClaimTypes.NameIdentifier, user.Value.ToString())), "Test"));

    private static FeaturePermissionGrant Feature(UserId user, string permission, bool deny, long version) =>
        new(user, permission, deny, version);

    private static ResourceScopeGrant Resource(UserId user, OrganizationId organization, bool deny, long version) =>
        new(user, organization, deny, version);
}
