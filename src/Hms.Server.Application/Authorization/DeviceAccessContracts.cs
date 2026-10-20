using Hms.Server.Domain.Authorization;
using Hms.Server.Domain.Devices;
using Hms.Server.Domain.Organizations;

namespace Hms.Server.Application.Authorization;

// Stores do not check permissions; handlers do. A store is the last check for
// its own invariants and reports a violation as InvalidOperationException(code)
// without writing anything, audit included.
public interface IDeviceRoleStore
{
    DeviceRole? Find(DeviceRoleId id);
    IReadOnlyList<DeviceRole> ListByOrganization(OrganizationId organizationId);

    // DEVICE_ROLE_NAME_TAKEN when another role of the organization has the same NormalizedName.
    void Add(DeviceRole role, DeviceAccessAuditEntry audit);

    // Compare-and-set: the stored Version must be role.Version - 1.
    // DEVICE_ROLE_NOT_FOUND, DEVICE_ROLE_VERSION_STALE, DEVICE_ROLE_NAME_TAKEN.
    void Update(DeviceRole role, DeviceAccessAuditEntry audit);

    // Checked in order: DEVICE_ROLE_NOT_FOUND, DEVICE_ROLE_VERSION_STALE, DEVICE_ROLE_IN_USE.
    void Remove(DeviceRoleId id, int expectedVersion, DeviceAccessAuditEntry audit);
}

public interface IDeviceMembershipStore
{
    DeviceMembership? Find(UserId userId, DeviceId deviceId);
    IReadOnlyList<DeviceMembership> ListByDevice(DeviceId deviceId);
    IReadOnlyList<DeviceMembership> ListByRole(DeviceRoleId roleId);
    IReadOnlyList<DeviceMembership> ListByUser(UserId userId);

    // Compare-and-set on the current role; expectedRoleId null means no membership yet.
    // DEVICE_ROLE_NOT_FOUND (checked first), then DEVICE_MEMBERSHIP_CHANGED.
    void Upsert(DeviceMembership membership, DeviceRoleId? expectedRoleId, DeviceAccessAuditEntry audit);

    // Same compare-and-set. Returns false, writing nothing, when there is no
    // membership and none was expected.
    bool Remove(UserId userId, DeviceId deviceId, DeviceRoleId? expectedRoleId, DeviceAccessAuditEntry audit);
}

// PreviousValue/NewValue are compact JSON snapshots of unbounded length.
public sealed record DeviceAccessAuditEntry(
    Guid AuditId,
    UserId Actor,
    string Change,
    OrganizationId? OrganizationId,
    DeviceId? DeviceId,
    UserId? TargetUserId,
    DeviceRoleId? RoleId,
    string? PreviousValue,
    string? NewValue,
    DateTimeOffset OccurredAt)
{
    public void EnsureValid()
    {
        if (!DeviceAccessChanges.IsKnown(Change))
        {
            throw new ArgumentException($"Unknown device access change '{Change}'.", nameof(Change));
        }
    }
}

public static class DeviceAccessChanges
{
    public const string RoleCreated = "ROLE_CREATED";
    public const string RoleUpdated = "ROLE_UPDATED";
    public const string RoleDeleted = "ROLE_DELETED";
    public const string MembershipAssigned = "MEMBERSHIP_ASSIGNED";
    public const string MembershipChanged = "MEMBERSHIP_CHANGED";
    public const string MembershipRemoved = "MEMBERSHIP_REMOVED";

    public static bool IsKnown(string? change) => change is
        RoleCreated or RoleUpdated or RoleDeleted or
        MembershipAssigned or MembershipChanged or MembershipRemoved;
}

// One instance backs both interfaces so the role in-use and exists checks see
// the memberships, as the foreign keys do in PostgreSQL. Roles are stored and
// returned as copies: a caller that edits a found role changes nothing until
// it calls Update.
public sealed class InMemoryDeviceAccessStore : IDeviceRoleStore, IDeviceMembershipStore
{
    private readonly object _gate = new();
    private readonly Dictionary<DeviceRoleId, DeviceRole> _roles = [];
    private readonly Dictionary<(UserId UserId, DeviceId DeviceId), DeviceMembership> _memberships = [];
    private readonly List<DeviceAccessAuditEntry> _audit = [];

    public DeviceRole? Find(DeviceRoleId id)
    {
        lock (_gate)
            return _roles.TryGetValue(id, out var role) ? Copy(role) : null;
    }

    public IReadOnlyList<DeviceRole> ListByOrganization(OrganizationId organizationId)
    {
        lock (_gate)
            return _roles.Values
                .Where(x => x.OrganizationId == organizationId)
                .OrderBy(x => x.Name, StringComparer.Ordinal)
                .Select(Copy)
                .ToArray();
    }

    public void Add(DeviceRole role, DeviceAccessAuditEntry audit)
    {
        audit.EnsureValid();
        lock (_gate)
        {
            EnsureNameAvailable(role.Id, role.OrganizationId, role.NormalizedName);
            _roles.Add(role.Id, Copy(role));
            _audit.Add(audit);
        }
    }

    public void Update(DeviceRole role, DeviceAccessAuditEntry audit)
    {
        audit.EnsureValid();
        lock (_gate)
        {
            if (!_roles.TryGetValue(role.Id, out var stored))
                throw new InvalidOperationException("DEVICE_ROLE_NOT_FOUND");
            if (stored.Version != role.Version - 1)
                throw new InvalidOperationException("DEVICE_ROLE_VERSION_STALE");
            EnsureNameAvailable(role.Id, stored.OrganizationId, role.NormalizedName);

            // The organization never changes, whatever the caller's copy says.
            _roles[role.Id] = DeviceRole.Rehydrate(
                role.Id.Value, stored.OrganizationId.Value, role.Name, role.Description, role.Permissions, role.Version);
            _audit.Add(audit);
        }
    }

    public void Remove(DeviceRoleId id, int expectedVersion, DeviceAccessAuditEntry audit)
    {
        audit.EnsureValid();
        lock (_gate)
        {
            if (!_roles.TryGetValue(id, out var stored))
                throw new InvalidOperationException("DEVICE_ROLE_NOT_FOUND");
            if (stored.Version != expectedVersion)
                throw new InvalidOperationException("DEVICE_ROLE_VERSION_STALE");
            if (_memberships.Values.Any(x => x.RoleId == id))
                throw new InvalidOperationException("DEVICE_ROLE_IN_USE");

            _roles.Remove(id);
            _audit.Add(audit);
        }
    }

    public DeviceMembership? Find(UserId userId, DeviceId deviceId)
    {
        lock (_gate)
            return _memberships.GetValueOrDefault((userId, deviceId));
    }

    public IReadOnlyList<DeviceMembership> ListByDevice(DeviceId deviceId)
    {
        lock (_gate)
            return _memberships.Values.Where(x => x.DeviceId == deviceId).ToArray();
    }

    public IReadOnlyList<DeviceMembership> ListByRole(DeviceRoleId roleId)
    {
        lock (_gate)
            return _memberships.Values.Where(x => x.RoleId == roleId).ToArray();
    }

    public IReadOnlyList<DeviceMembership> ListByUser(UserId userId)
    {
        lock (_gate)
            return _memberships.Values.Where(x => x.UserId == userId).ToArray();
    }

    public void Upsert(DeviceMembership membership, DeviceRoleId? expectedRoleId, DeviceAccessAuditEntry audit)
    {
        audit.EnsureValid();
        lock (_gate)
        {
            if (!_roles.ContainsKey(membership.RoleId))
                throw new InvalidOperationException("DEVICE_ROLE_NOT_FOUND");
            var key = (membership.UserId, membership.DeviceId);
            if (CurrentRole(key) != expectedRoleId)
                throw new InvalidOperationException("DEVICE_MEMBERSHIP_CHANGED");

            _memberships[key] = membership;
            _audit.Add(audit);
        }
    }

    public bool Remove(UserId userId, DeviceId deviceId, DeviceRoleId? expectedRoleId, DeviceAccessAuditEntry audit)
    {
        audit.EnsureValid();
        lock (_gate)
        {
            var key = (userId, deviceId);
            var current = CurrentRole(key);
            if (current != expectedRoleId)
                throw new InvalidOperationException("DEVICE_MEMBERSHIP_CHANGED");
            if (current is null)
                return false;

            _memberships.Remove(key);
            _audit.Add(audit);
            return true;
        }
    }

    public IReadOnlyList<DeviceAccessAuditEntry> Audit()
    {
        lock (_gate)
            return _audit.ToArray();
    }

    private DeviceRoleId? CurrentRole((UserId UserId, DeviceId DeviceId) key) =>
        _memberships.TryGetValue(key, out var membership) ? membership.RoleId : null;

    private void EnsureNameAvailable(DeviceRoleId id, OrganizationId organizationId, string normalizedName)
    {
        if (_roles.Values.Any(x =>
                x.Id != id &&
                x.OrganizationId == organizationId &&
                string.Equals(x.NormalizedName, normalizedName, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("DEVICE_ROLE_NAME_TAKEN");
        }
    }

    private static DeviceRole Copy(DeviceRole role) => DeviceRole.Rehydrate(
        role.Id.Value, role.OrganizationId.Value, role.Name, role.Description, role.Permissions, role.Version);
}
