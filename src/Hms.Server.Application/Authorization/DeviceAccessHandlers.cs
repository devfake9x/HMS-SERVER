using System.Collections.Frozen;
using System.Text.Json;
using Hms.Server.Application.Commands;
using Hms.Server.Application.Management;
using Hms.Server.Application.Realtime;
using Hms.Server.Domain.Authorization;
using Hms.Server.Domain.Devices;
using Hms.Server.Domain.Organizations;
using Microsoft.Extensions.Logging;

namespace Hms.Server.Application.Authorization;

public sealed record DeviceAccessOutcome(string Code, DeviceRole? Role = null, DeviceMembership? Membership = null);

// One code table for the device-access handlers and the grant handlers.
public static class DeviceAccessOutcomeCodes
{
    public const string Ok = "OK";
    public const string PermissionManagementRequired = "PERMISSION_MANAGEMENT_REQUIRED";
    public const string DeviceNotFound = "DEVICE_NOT_FOUND";
    public const string UserNotFound = "USER_NOT_FOUND";
    public const string SelfGrantForbidden = "SELF_GRANT_FORBIDDEN";
    public const string RoleNotFound = "DEVICE_ROLE_NOT_FOUND";
    public const string RoleOrganizationMismatch = "DEVICE_ROLE_ORGANIZATION_MISMATCH";
    public const string MembershipChanged = "DEVICE_MEMBERSHIP_CHANGED";
    public const string HighRiskRequiresMasterAdmin = "HIGH_RISK_REQUIRES_MASTER_ADMIN";
    public const string GrantExceedsOwnPermissions = "GRANT_EXCEEDS_OWN_PERMISSIONS";
    public const string OrganizationNotFound = "ORGANIZATION_NOT_FOUND";
    public const string RoleInvalid = "DEVICE_ROLE_INVALID";
    public const string RoleNameTaken = "DEVICE_ROLE_NAME_TAKEN";
    public const string PermissionUnknown = "DEVICE_PERMISSION_UNKNOWN";
    public const string RoleVersionStale = "DEVICE_ROLE_VERSION_STALE";
    public const string RoleInUse = "DEVICE_ROLE_IN_USE";
    public const string WildcardGrantForbidden = "WILDCARD_GRANT_FORBIDDEN";
    public const string GrantTargetIsMasterAdmin = "GRANT_TARGET_IS_MASTER_ADMIN";
    public const string GrantOverridesMasterDecision = "GRANT_OVERRIDES_MASTER_DECISION";
    public const string GrantToDelegatorForbidden = "GRANT_TO_DELEGATOR_FORBIDDEN";
}

public sealed class CreateDeviceRoleHandler(
    IDeviceRoleStore roles,
    IOrganizationStore organizations,
    ICommandCatalog catalog,
    IPermissionStore permissions,
    ILogger<CreateDeviceRoleHandler> logger)
{
    public DeviceAccessOutcome Handle(
        AccessActor actor,
        OrganizationId organizationId,
        string name,
        string? description,
        IReadOnlyCollection<string> permissionCodes)
    {
        var rejected = RoleRules.CheckOrganization(actor, organizationId, organizations)
            ?? RoleRules.CheckText(name, description)
            ?? RoleRules.CheckVocabulary(permissionCodes, catalog)
            ?? RoleRules.CheckHighRisk(actor, permissionCodes);
        if (rejected is not null)
            return new(rejected);

        var role = DeviceRole.Create(organizationId, name, description, permissionCodes);
        var audit = DeviceAccessAudits.ForRole(actor, DeviceAccessChanges.RoleCreated, role, null, DeviceAccessAudits.Snapshot(role));
        // The store decides name uniqueness: the handler does not pre-check it.
        var storeCode = StoreWrites.Try(() => roles.Add(role, audit), DeviceAccessOutcomeCodes.RoleNameTaken);
        if (storeCode is not null)
            return new(storeCode);

        PostCommitSignals.Run(logger, audit.AuditId, [], () => permissions.Advance());
        return new(DeviceAccessOutcomeCodes.Ok, Role: role);
    }
}

public sealed class UpdateDeviceRoleHandler(
    IDeviceRoleStore roles,
    IDeviceMembershipStore memberships,
    IOrganizationStore organizations,
    IDeviceStore devices,
    IDeviceAccessResolver resolver,
    ICommandCatalog catalog,
    IUserScopeStore users,
    IPermissionStore permissions,
    IAuthorizationEpochStore epochs,
    ILogger<UpdateDeviceRoleHandler> logger)
{
    public DeviceAccessOutcome Handle(
        AccessActor actor,
        OrganizationId organizationId,
        DeviceRoleId roleId,
        string name,
        string? description,
        IReadOnlyCollection<string> permissionCodes,
        int expectedVersion)
    {
        var rejected = RoleRules.CheckOrganization(actor, organizationId, organizations)
            ?? RoleRules.CheckText(name, description)
            ?? RoleRules.CheckVocabulary(permissionCodes, catalog);
        if (rejected is not null)
            return new(rejected);

        var role = roles.Find(roleId);
        if (role is null || role.OrganizationId != organizationId)
            return new(DeviceAccessOutcomeCodes.RoleNotFound);
        if (role.Version != expectedVersion)
            return new(DeviceAccessOutcomeCodes.RoleVersionStale);
        // Checked before the device loop, so a role assigned nowhere is checked too.
        rejected = RoleRules.CheckHighRisk(actor, role.Permissions.Concat(permissionCodes));
        if (rejected is not null)
            return new(rejected);

        // Read before the write: the holders whose access the change reaches, and their devices.
        var holders = memberships.ListByRole(roleId);
        var affected = holders.Select(x => x.DeviceId).Distinct().ToArray();
        if (!actor.IsMasterAdmin)
        {
            // Editing a role acts on everyone holding it, so nobody up the actor's
            // appointment chain may hold it.
            var chain = DelegationChain.Of(actor, permissions, users);
            if (holders.Any(x => chain.Contains(x.UserId)))
                return new(DeviceAccessOutcomeCodes.GrantToDelegatorForbidden);
            // An admin may not change a role beyond their own permissions (cut to their
            // appointers') on any device that uses it, and may not shrink one that already
            // exceeds them (old and new both count).
            foreach (var deviceId in affected)
            {
                var own = devices.Find(deviceId) is { } device
                    ? DelegationChain.DevicePermissions(resolver.Resolve(actor, device).Permissions, chain, device, resolver)
                    : FrozenSet<string>.Empty;
                rejected = DelegationRules.CheckRoleChange(actor, own, permissionCodes, role.Permissions);
                if (rejected is not null)
                    return new(rejected);
            }
        }

        var previous = DeviceAccessAudits.Snapshot(role);
        role.Update(name, description, permissionCodes);
        var audit = DeviceAccessAudits.ForRole(actor, DeviceAccessChanges.RoleUpdated, role, previous, DeviceAccessAudits.Snapshot(role));
        var storeCode = StoreWrites.Try(() => roles.Update(role, audit),
            DeviceAccessOutcomeCodes.RoleNameTaken, DeviceAccessOutcomeCodes.RoleVersionStale, DeviceAccessOutcomeCodes.RoleNotFound);
        if (storeCode is not null)
            return new(storeCode);

        PostCommitSignals.Run(logger, audit.AuditId, affected, () => permissions.Advance());
        if (affected.Length > 0)
            PostCommitSignals.Run(logger, audit.AuditId, affected, () => epochs.AdvanceMany(affected));
        return new(DeviceAccessOutcomeCodes.Ok, Role: role);
    }
}

public sealed class DeleteDeviceRoleHandler(
    IDeviceRoleStore roles,
    IOrganizationStore organizations,
    IPermissionStore permissions,
    ILogger<DeleteDeviceRoleHandler> logger)
{
    public DeviceAccessOutcome Handle(AccessActor actor, OrganizationId organizationId, DeviceRoleId roleId, int expectedVersion)
    {
        var rejected = RoleRules.CheckOrganization(actor, organizationId, organizations);
        if (rejected is not null)
            return new(rejected);

        var role = roles.Find(roleId);
        if (role is null || role.OrganizationId != organizationId)
            return new(DeviceAccessOutcomeCodes.RoleNotFound);
        if (role.Version != expectedVersion)
            return new(DeviceAccessOutcomeCodes.RoleVersionStale);
        rejected = RoleRules.CheckHighRisk(actor, role.Permissions);
        if (rejected is not null)
            return new(rejected);

        var audit = DeviceAccessAudits.ForRole(actor, DeviceAccessChanges.RoleDeleted, role, DeviceAccessAudits.Snapshot(role), null);
        var storeCode = StoreWrites.Try(() => roles.Remove(roleId, expectedVersion, audit),
            DeviceAccessOutcomeCodes.RoleNotFound, DeviceAccessOutcomeCodes.RoleVersionStale, DeviceAccessOutcomeCodes.RoleInUse);
        if (storeCode is not null)
            return new(storeCode);

        // A role in use cannot be deleted, so no device epoch moves.
        PostCommitSignals.Run(logger, audit.AuditId, [], () => permissions.Advance());
        return new(DeviceAccessOutcomeCodes.Ok);
    }
}

public sealed class AssignDeviceMembershipHandler(
    IDeviceMembershipStore memberships,
    IDeviceRoleStore roles,
    IDeviceStore devices,
    IDeviceAccessResolver resolver,
    IUserScopeStore users,
    IPermissionStore permissions,
    IAuthorizationEpochStore epochs,
    ILogger<AssignDeviceMembershipHandler> logger)
{
    public DeviceAccessOutcome Handle(
        AccessActor actor,
        DeviceId deviceId,
        UserId target,
        DeviceRoleId roleId,
        DeviceRoleId? expectedRoleId,
        int expectedRoleVersion)
    {
        var device = devices.Find(deviceId);
        var rejected = MembershipRules.CheckDevice(actor, device, resolver, out var access);
        if (rejected is not null)
            return new(rejected);
        var current = memberships.Find(target, deviceId);
        var chain = DelegationChain.Of(actor, permissions, users);
        rejected = MembershipRules.CheckTarget(actor, target, users)
            ?? MembershipRules.CheckSelf(actor, target)
            ?? DelegationChain.CheckTarget(chain, target);
        if (rejected is not null)
            return new(rejected);

        var role = roles.Find(roleId);
        if (role is null)
            return new(DeviceAccessOutcomeCodes.RoleNotFound);
        if (role.OrganizationId != device!.OwnerOrganizationId)
            return new(DeviceAccessOutcomeCodes.RoleOrganizationMismatch);
        // The admin must assign the role version they looked at, master-admin included.
        if (role.Version != expectedRoleVersion)
            return new(DeviceAccessOutcomeCodes.RoleVersionStale);
        if (current?.RoleId != expectedRoleId)
            return new(DeviceAccessOutcomeCodes.MembershipChanged);

        var currentRole = current is null ? null : roles.Find(current.RoleId);
        rejected = DelegationRules.CheckCurrentRoleReadable(actor, current, currentRole)
            ?? DelegationRules.CheckRoleChange(actor,
                DelegationChain.DevicePermissions(access!.Permissions, chain, device!, resolver),
                role.Permissions,
                currentRole?.Permissions);
        if (rejected is not null)
            return new(rejected);
        if (current?.RoleId == roleId)
            return new(DeviceAccessOutcomeCodes.Ok, Membership: current);

        var membership = new DeviceMembership(target, deviceId, roleId, actor.UserId, DateTimeOffset.UtcNow);
        var audit = DeviceAccessAudits.ForMembership(
            actor,
            current is null ? DeviceAccessChanges.MembershipAssigned : DeviceAccessChanges.MembershipChanged,
            device,
            target,
            roleId,
            current is null ? null : DeviceAccessAudits.Snapshot(current.RoleId),
            DeviceAccessAudits.Snapshot(roleId));
        // The store's compare-and-set is the real check; the pre-check above only fails early.
        var storeCode = StoreWrites.Try(() => memberships.Upsert(membership, expectedRoleId, audit),
            DeviceAccessOutcomeCodes.MembershipChanged, DeviceAccessOutcomeCodes.RoleNotFound);
        if (storeCode is not null)
            return new(storeCode);

        PostCommitSignals.Run(logger, audit.AuditId, [deviceId], () => permissions.Advance());
        // A fresh assignment takes access from nobody, so the device epoch stays.
        if (current is not null)
            PostCommitSignals.Run(logger, audit.AuditId, [deviceId], () => epochs.Advance(deviceId));
        return new(DeviceAccessOutcomeCodes.Ok, Membership: membership);
    }
}

public sealed class RemoveDeviceMembershipHandler(
    IDeviceMembershipStore memberships,
    IDeviceRoleStore roles,
    IDeviceStore devices,
    IDeviceAccessResolver resolver,
    IUserScopeStore users,
    IPermissionStore permissions,
    IAuthorizationEpochStore epochs,
    ILogger<RemoveDeviceMembershipHandler> logger)
{
    public DeviceAccessOutcome Handle(AccessActor actor, DeviceId deviceId, UserId target, DeviceRoleId? expectedRoleId)
    {
        var device = devices.Find(deviceId);
        var rejected = MembershipRules.CheckDevice(actor, device, resolver, out var access);
        if (rejected is not null)
            return new(rejected);
        var current = memberships.Find(target, deviceId);
        var chain = DelegationChain.Of(actor, permissions, users);
        // An existing membership is removable whatever the holder's home organization:
        // the stored membership is what scopes it to this device.
        rejected = (current is null ? MembershipRules.CheckTarget(actor, target, users) : null)
            ?? MembershipRules.CheckSelf(actor, target)
            ?? DelegationChain.CheckTarget(chain, target);
        if (rejected is not null)
            return new(rejected);
        if (current?.RoleId != expectedRoleId)
            return new(DeviceAccessOutcomeCodes.MembershipChanged);
        if (current is null)
            return new(DeviceAccessOutcomeCodes.Ok);

        var currentRole = roles.Find(current.RoleId);
        rejected = DelegationRules.CheckCurrentRoleReadable(actor, current, currentRole)
            ?? DelegationRules.CheckRoleChange(actor,
                DelegationChain.DevicePermissions(access!.Permissions, chain, device!, resolver),
                null,
                currentRole?.Permissions);
        if (rejected is not null)
            return new(rejected);

        var audit = DeviceAccessAudits.ForMembership(
            actor, DeviceAccessChanges.MembershipRemoved, device!, target, current.RoleId,
            DeviceAccessAudits.Snapshot(current.RoleId), null);
        var storeCode = StoreWrites.Try(() => memberships.Remove(target, deviceId, expectedRoleId, audit),
            DeviceAccessOutcomeCodes.MembershipChanged);
        if (storeCode is not null)
            return new(storeCode);

        PostCommitSignals.Run(logger, audit.AuditId, [deviceId], () => permissions.Advance());
        PostCommitSignals.Run(logger, audit.AuditId, [deviceId], () => epochs.Advance(deviceId));
        return new(DeviceAccessOutcomeCodes.Ok);
    }
}

// What a non-master may delegate on a device: no high-risk code, and nothing
// beyond their own role on that device. The current role counts too, so an
// admin can neither replace nor remove (nor shrink) access stronger than theirs.
internal static class DelegationRules
{
    public static string? CheckRoleChange(
        AccessActor actor,
        IReadOnlySet<string> own,
        IEnumerable<string>? newPermissions,
        IEnumerable<string>? currentPermissions)
    {
        if (actor.IsMasterAdmin)
            return null;
        var touched = (newPermissions ?? []).Concat(currentPermissions ?? []).ToArray();
        if (touched.Any(DevicePermissionCodes.IsHighRisk))
            return DeviceAccessOutcomeCodes.HighRiskRequiresMasterAdmin;
        return touched.All(own.Contains) ? null : DeviceAccessOutcomeCodes.GrantExceedsOwnPermissions;
    }

    // A membership whose role cannot be read counts as stronger than any non-master.
    public static string? CheckCurrentRoleReadable(AccessActor actor, DeviceMembership? current, DeviceRole? currentRole) =>
        !actor.IsMasterAdmin && current is not null && currentRole is null
            ? DeviceAccessOutcomeCodes.GrantExceedsOwnPermissions
            : null;
}

internal static class RoleRules
{
    public static string? CheckOrganization(AccessActor actor, OrganizationId organizationId, IOrganizationStore organizations)
    {
        if (organizations.Find(organizationId) is null ||
            (!actor.IsMasterAdmin && !actor.EffectiveOrganizations.Contains(organizationId)))
            return DeviceAccessOutcomeCodes.OrganizationNotFound;
        return DeviceAccessResolver.CanManageAccess(actor, organizationId)
            ? null
            : DeviceAccessOutcomeCodes.PermissionManagementRequired;
    }

    // Checked here so a bad name or description never escapes as an ArgumentException.
    public static string? CheckText(string? name, string? description)
    {
        var trimmedName = name?.Trim();
        if (string.IsNullOrEmpty(trimmedName) || trimmedName.Length > DeviceRole.NameMaxLength)
            return DeviceAccessOutcomeCodes.RoleInvalid;
        return (description?.Trim().Length ?? 0) > DeviceRole.DescriptionMaxLength
            ? DeviceAccessOutcomeCodes.RoleInvalid
            : null;
    }

    public static string? CheckVocabulary(IEnumerable<string> permissionCodes, ICommandCatalog catalog)
    {
        var vocabulary = DevicePermissionVocabulary.All(catalog);
        return permissionCodes.All(code => code is not null && vocabulary.Contains(code))
            ? null
            : DeviceAccessOutcomeCodes.PermissionUnknown;
    }

    public static string? CheckHighRisk(AccessActor actor, IEnumerable<string> permissionCodes) =>
        !actor.IsMasterAdmin && permissionCodes.Any(DevicePermissionCodes.IsHighRisk)
            ? DeviceAccessOutcomeCodes.HighRiskRequiresMasterAdmin
            : null;
}

internal static class MembershipRules
{
    // Out of scope and missing look the same.
    public static string? CheckDevice(
        AccessActor actor,
        Device? device,
        IDeviceAccessResolver resolver,
        out DeviceAccess? access)
    {
        access = device is null ? null : resolver.Resolve(actor, device);
        if (access is null || (!actor.IsMasterAdmin && !access.InScope))
            return DeviceAccessOutcomeCodes.DeviceNotFound;
        return access.CanManageAccess ? null : DeviceAccessOutcomeCodes.PermissionManagementRequired;
    }

    // The target must exist and, for a non-master, belong to an organization in scope.
    public static string? CheckTarget(AccessActor actor, UserId target, IUserScopeStore users)
    {
        if (!users.Exists(target))
            return DeviceAccessOutcomeCodes.UserNotFound;
        if (actor.IsMasterAdmin)
            return null;
        return users.Organization(target) is { } organization && actor.EffectiveOrganizations.Contains(organization)
            ? null
            : DeviceAccessOutcomeCodes.UserNotFound;
    }

    public static string? CheckSelf(AccessActor actor, UserId target) =>
        !actor.IsMasterAdmin && target == actor.UserId ? DeviceAccessOutcomeCodes.SelfGrantForbidden : null;
}

internal static class StoreWrites
{
    // Runs a store write. A store invariant violation (InvalidOperationException
    // with one of the given codes) comes back as its code; anything else propagates.
    public static string? Try(Action write, params string[] codes)
    {
        try
        {
            write();
            return null;
        }
        catch (InvalidOperationException error) when (codes.Contains(error.Message))
        {
            return error.Message;
        }
    }
}

// Signals after a committed write are best effort: the write stands and HTTP and
// realtime re-check access live, so a failure is logged and the handler still
// reports OK. Each signal fails on its own.
internal static class PostCommitSignals
{
    public static void Run(ILogger logger, Guid auditId, IReadOnlyCollection<DeviceId> devices, Action signal)
    {
        try
        {
            signal();
        }
        catch (Exception error)
        {
            logger.LogWarning(error,
                "Post-commit signal failed after device access write {AuditId} (devices {DeviceIds})",
                auditId, string.Join(",", devices.Select(x => x.Value)));
        }
    }
}

// Audit snapshots are compact JSON with ids, codes and role names only: no email
// or display name.
internal static class DeviceAccessAudits
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Snapshot(DeviceRoleId roleId) => JsonSerializer.Serialize(new { roleId = roleId.Value }, Options);

    public static string Snapshot(DeviceRole role) => JsonSerializer.Serialize(new
    {
        name = role.Name,
        description = role.Description,
        permissions = role.Permissions.Order(StringComparer.Ordinal).ToArray(),
        version = role.Version
    }, Options);

    public static DeviceAccessAuditEntry ForRole(
        AccessActor actor, string change, DeviceRole role, string? previousValue, string? newValue) =>
        new(Guid.NewGuid(), actor.UserId, change, role.OrganizationId, null, null, role.Id,
            previousValue, newValue, DateTimeOffset.UtcNow);

    public static DeviceAccessAuditEntry ForMembership(
        AccessActor actor, string change, Device device, UserId target, DeviceRoleId roleId,
        string? previousValue, string? newValue) =>
        new(Guid.NewGuid(), actor.UserId, change, device.OwnerOrganizationId, device.Id, target, roleId,
            previousValue, newValue, DateTimeOffset.UtcNow);
}
