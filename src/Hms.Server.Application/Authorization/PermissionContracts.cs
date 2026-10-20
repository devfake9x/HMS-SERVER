using Hms.Server.Application.Realtime;
using Hms.Server.Application.Commands;
using Hms.Server.Application.Management;
using Hms.Server.Domain.Authorization;
using Hms.Server.Domain.Devices;
using Hms.Server.Domain.Organizations;

namespace Hms.Server.Application.Authorization;

// GrantedBy/GrantedByMasterAdmin record who wrote the row; rows written before
// they existed read back as null/false.
public sealed record FeaturePermissionGrant(
    UserId UserId,
    string Permission,
    bool Deny,
    long PolicyVersion,
    UserId? GrantedBy = null,
    bool GrantedByMasterAdmin = false);
public sealed record ResourceScopeGrant(
    UserId UserId,
    OrganizationId OrganizationId,
    bool Deny,
    long PolicyVersion,
    UserId? GrantedBy = null,
    bool GrantedByMasterAdmin = false);
public sealed record DeviceActionGrant(UserId UserId, DeviceId DeviceId, string Action, bool Deny, long PolicyVersion);

public interface IPermissionStore
{
    long PolicyVersion { get; }
    long Advance();
    void GrantFeature(FeaturePermissionGrant grant);
    void GrantResource(ResourceScopeGrant grant);
    void GrantDeviceAction(DeviceActionGrant grant);
    IReadOnlyList<FeaturePermissionGrant> FeatureGrants(UserId userId);
    IReadOnlyList<ResourceScopeGrant> ResourceGrants(UserId userId);
    IReadOnlyList<DeviceActionGrant> DeviceActionGrants(UserId userId);

    // Runs one grant decision (its reads and its write) as one step for the user:
    // no other decision for that user runs in between, across API replicas too.
    T WithUserLock<T>(UserId userId, Func<T> decide);
}

// Singleton in development and tests, so every access takes the lock.
public sealed class InMemoryPermissionStore : IPermissionStore
{
    private readonly object _gate = new();
    private readonly List<FeaturePermissionGrant> _features = [];
    private readonly List<ResourceScopeGrant> _resources = [];
    private readonly List<DeviceActionGrant> _actions = [];
    private long _policyVersion;

    public long PolicyVersion => Volatile.Read(ref _policyVersion);

    public void GrantFeature(FeaturePermissionGrant grant)
    {
        lock (_gate)
            _features.Add(grant);
    }

    public void GrantResource(ResourceScopeGrant grant)
    {
        lock (_gate)
            _resources.Add(grant);
    }

    public void GrantDeviceAction(DeviceActionGrant grant)
    {
        lock (_gate)
            _actions.Add(grant);
    }

    public IReadOnlyList<FeaturePermissionGrant> FeatureGrants(UserId userId)
    {
        lock (_gate)
            return _features.Where(x => x.UserId == userId).ToArray();
    }

    public IReadOnlyList<ResourceScopeGrant> ResourceGrants(UserId userId)
    {
        lock (_gate)
            return _resources.Where(x => x.UserId == userId).ToArray();
    }

    public IReadOnlyList<DeviceActionGrant> DeviceActionGrants(UserId userId)
    {
        lock (_gate)
            return _actions.Where(x => x.UserId == userId).ToArray();
    }

    public long Advance() => Interlocked.Increment(ref _policyVersion);

    // One lock for every user is enough in memory; Monitor is reentrant, so the
    // reads and writes inside take it again.
    public T WithUserLock<T>(UserId userId, Func<T> decide)
    {
        lock (_gate)
            return decide();
    }
}

public sealed record PermissionGrantOutcome(
    string Code,
    FeaturePermissionGrant? Feature = null,
    ResourceScopeGrant? Resource = null);

// Rejected grants neither advance the policy version nor write a row. Every
// written row records its grantor and whether the grantor was a master-admin.
// The check and the write run under the target user's lock, so a concurrent
// decision for the same user cannot change what the check saw.
public sealed class GrantFeaturePermissionHandler(
    IPermissionStore permissions,
    IUserScopeStore users,
    IOrganizationStore organizations)
{
    public PermissionGrantOutcome Handle(AccessActor actor, UserId target, string permission, bool deny = false) =>
        permissions.WithUserLock(target, () =>
        {
            var rejected = Check(actor, target, permission, deny);
            if (rejected is not null)
                return new PermissionGrantOutcome(rejected);

            var grant = new FeaturePermissionGrant(
                target, permission, deny, permissions.Advance(), actor.UserId, actor.IsMasterAdmin);
            permissions.GrantFeature(grant);
            return new PermissionGrantOutcome(DeviceAccessOutcomeCodes.Ok, Feature: grant);
        });

    private string? Check(AccessActor actor, UserId target, string permission, bool deny)
    {
        // Only a master-admin writes the wildcard key: its deny blocks every feature of a
        // non-master, and its allow grants nothing of its own but lifts that deny.
        if (permission == PermissionGrantResolver.Wildcard && !actor.IsMasterAdmin)
            return DeviceAccessOutcomeCodes.WildcardGrantForbidden;
        // A deny cannot restrict a master-admin; removing the role is the way.
        if (deny && users.IsMasterAdmin(target))
            return DeviceAccessOutcomeCodes.GrantTargetIsMasterAdmin;
        if (actor.IsMasterAdmin)
            return null;
        if (target == actor.UserId)
            return DeviceAccessOutcomeCodes.SelfGrantForbidden;
        var chain = DelegationChain.Of(actor, permissions, users);
        var rejected = DelegationChain.CheckTarget(chain, target);
        if (rejected is not null || deny)
            return rejected;
        // The grantor must hold the feature, and must not push it beyond their own scope
        // through a target whose organizations reach further; their appointers bound both.
        var all = organizations.List();
        var targetScope = OrganizationScopeResolver.Effective(
            PermissionGrantResolver.Organizations([], [], permissions.ResourceGrants(target)), all);
        if (!DelegationChain.Features(actor, chain, permissions).Contains(permission) ||
            !targetScope.IsSubsetOf(DelegationChain.Organizations(actor, chain, permissions, all)))
            return DeviceAccessOutcomeCodes.GrantExceedsOwnPermissions;
        return MasterDecisions.DenyStands(permissions.FeatureGrants(target).Where(x => x.Permission == permission))
            ? DeviceAccessOutcomeCodes.GrantOverridesMasterDecision
            : null;
    }
}

public sealed class GrantResourceScopeHandler(
    IPermissionStore permissions,
    IUserScopeStore users,
    IOrganizationStore organizations)
{
    public PermissionGrantOutcome Handle(AccessActor actor, UserId target, OrganizationId organizationId, bool deny = false) =>
        permissions.WithUserLock(target, () =>
        {
            var rejected = Check(actor, target, organizationId, deny);
            if (rejected is not null)
                return new PermissionGrantOutcome(rejected);

            var grant = new ResourceScopeGrant(
                target, organizationId, deny, permissions.Advance(), actor.UserId, actor.IsMasterAdmin);
            permissions.GrantResource(grant);
            return new PermissionGrantOutcome(DeviceAccessOutcomeCodes.Ok, Resource: grant);
        });

    private string? Check(AccessActor actor, UserId target, OrganizationId organizationId, bool deny)
    {
        if (deny && users.IsMasterAdmin(target))
            return DeviceAccessOutcomeCodes.GrantTargetIsMasterAdmin;
        if (actor.IsMasterAdmin)
            return null;
        if (target == actor.UserId)
            return DeviceAccessOutcomeCodes.SelfGrantForbidden;
        var chain = DelegationChain.Of(actor, permissions, users);
        var rejected = DelegationChain.CheckTarget(chain, target);
        if (rejected is not null)
            return rejected;
        // A grant or deny on an organization reaches its whole subtree, so the actor's
        // scope (bounded by their appointers) must cover all of it.
        var all = organizations.List();
        if (!OrganizationScopeResolver.Expand([organizationId], all)
                .IsSubsetOf(DelegationChain.Organizations(actor, chain, permissions, all)))
            return DeviceAccessOutcomeCodes.GrantExceedsOwnPermissions;
        if (deny)
            return null;
        // Nobody widens the scope under features someone else granted that they lack.
        var targetFeatures = PermissionGrantResolver.Features([], [], permissions.FeatureGrants(target)).Allowed;
        if (!targetFeatures.IsSubsetOf(DelegationChain.Features(actor, chain, permissions)))
            return DeviceAccessOutcomeCodes.GrantExceedsOwnPermissions;
        return MasterDecisions.DenyStands(permissions.ResourceGrants(target).Where(x => x.OrganizationId == organizationId))
            ? DeviceAccessOutcomeCodes.GrantOverridesMasterDecision
            : null;
    }
}

// A non-master may not allow a key whose latest master-issued row is a deny.
// Comparing with the latest master-issued row, not the latest row overall, stops
// a non-master from writing an own deny first to launder the reversal.
internal static class MasterDecisions
{
    public static bool DenyStands(IEnumerable<FeaturePermissionGrant> rowsOfKey) =>
        PermissionGrantResolver.LatestFeatureGrants(rowsOfKey.Where(x => x.GrantedByMasterAdmin)).Any(x => x.Deny);

    public static bool DenyStands(IEnumerable<ResourceScopeGrant> rowsOfKey) =>
        PermissionGrantResolver.LatestResourceGrants(rowsOfKey.Where(x => x.GrantedByMasterAdmin)).Any(x => x.Deny);
}

public sealed class GrantDeviceActionHandler(IPermissionStore permissions)
{
    public DeviceActionGrant Handle(UserId userId, DeviceId deviceId, string action, bool deny = false)
    {
        if (string.IsNullOrWhiteSpace(action)) throw new ArgumentException("Action is required.", nameof(action));
        var version = permissions.Advance();
        var grant = new DeviceActionGrant(userId, deviceId, action.Trim(), deny, version);
        permissions.GrantDeviceAction(grant);
        return grant;
    }
}

public sealed class RevokeDeviceHandler(
    IDeviceStore devices,
    IAuthorizationEpochStore epochs,
    ICommandAuditStore? commandAudits = null)
{
    public void Handle(DeviceId deviceId)
    {
        var device = devices.Find(deviceId) ?? throw new InvalidOperationException("DEVICE_NOT_FOUND");
        device.Revoke();
        devices.Update(device);
        epochs.Advance(deviceId);
        commandAudits?.CancelPendingForDevice(deviceId);
    }
}
