using Hms.Server.Application.Authorization;
using Hms.Server.Domain.Authorization;
using Hms.Server.Domain.Devices;
using Hms.Server.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Hms.Server.Infrastructure.Persistence;

public sealed class EfDeviceRoleStore(ApplicationDbContext db) : IDeviceRoleStore
{
    public DeviceRole? Find(DeviceRoleId id)
    {
        var record = db.DeviceRoles.AsNoTracking()
            .Include(x => x.Permissions)
            .SingleOrDefault(x => x.Id == id.Value);
        return record is null ? null : ToDomain(record);
    }

    public IReadOnlyList<DeviceRole> ListByOrganization(OrganizationId organizationId) => db.DeviceRoles.AsNoTracking()
        .Include(x => x.Permissions)
        .Where(x => x.OrganizationId == organizationId.Value)
        .OrderBy(x => x.Name)
        .AsEnumerable()
        .Select(ToDomain)
        .ToArray();

    public void Add(DeviceRole role, DeviceAccessAuditEntry audit)
    {
        audit.EnsureValid();
        ThrowIfNameTaken(role.OrganizationId.Value, role, cause: null);

        DeviceAccessWrites.Save(db, audit,
            stage: () => db.DeviceRoles.Add(new DeviceRoleRecord
            {
                Id = role.Id.Value,
                OrganizationId = role.OrganizationId.Value,
                Name = role.Name,
                NormalizedName = role.NormalizedName,
                Description = role.Description,
                Version = role.Version,
                CreatedBy = audit.Actor.Value,
                CreatedAt = audit.OccurredAt,
                UpdatedAt = audit.OccurredAt,
                Permissions = role.Permissions
                    .Select(permission => new DeviceRolePermissionRecord { RoleId = role.Id.Value, Permission = permission })
                    .ToList()
            }),
            recheck: error => ThrowIfNameTaken(role.OrganizationId.Value, role, error));
    }

    public void Update(DeviceRole role, DeviceAccessAuditEntry audit)
    {
        audit.EnsureValid();
        var record = db.DeviceRoles.Include(x => x.Permissions).SingleOrDefault(x => x.Id == role.Id.Value);
        ThrowIfUpdateRejected(role, record?.Version, record?.OrganizationId, cause: null);
        var stored = record!;

        DeviceAccessWrites.Save(db, audit,
            stage: () =>
            {
                // OrganizationId is never written: a role does not move between organizations.
                stored.Name = role.Name;
                stored.NormalizedName = role.NormalizedName;
                stored.Description = role.Description;
                stored.Version = role.Version;
                stored.UpdatedAt = audit.OccurredAt;
                // Diff the permission rows: deleting and re-adding the same key in one
                // SaveChanges would track two entities with one key.
                var kept = stored.Permissions.Select(x => x.Permission).ToHashSet(StringComparer.Ordinal);
                db.DeviceRolePermissions.RemoveRange(
                    stored.Permissions.Where(x => !role.Permissions.Contains(x.Permission)).ToArray());
                db.DeviceRolePermissions.AddRange(role.Permissions
                    .Where(permission => !kept.Contains(permission))
                    .Select(permission => new DeviceRolePermissionRecord { RoleId = stored.Id, Permission = permission }));
            },
            recheck: error =>
            {
                var current = db.DeviceRoles.AsNoTracking()
                    .Where(x => x.Id == role.Id.Value)
                    .Select(x => new { x.Version, x.OrganizationId })
                    .SingleOrDefault();
                ThrowIfUpdateRejected(role, current?.Version, current?.OrganizationId, error);
            });
    }

    public void Remove(DeviceRoleId id, int expectedVersion, DeviceAccessAuditEntry audit)
    {
        audit.EnsureValid();
        var record = db.DeviceRoles.Include(x => x.Permissions).SingleOrDefault(x => x.Id == id.Value);
        ThrowIfRemoveRejected(id, expectedVersion, record?.Version, cause: null);
        var stored = record!;

        DeviceAccessWrites.Save(db, audit,
            stage: () =>
            {
                // Delete the permission rows explicitly: EF InMemory cascades only to
                // tracked rows. PostgreSQL cascades them anyway.
                db.DeviceRolePermissions.RemoveRange(stored.Permissions.ToArray());
                db.DeviceRoles.Remove(stored);
            },
            recheck: error =>
            {
                var current = db.DeviceRoles.AsNoTracking()
                    .Where(x => x.Id == id.Value)
                    .Select(x => (int?)x.Version)
                    .SingleOrDefault();
                ThrowIfRemoveRejected(id, expectedVersion, current, error);
            });
    }

    // The update checks in their order. They run on the tracked row before the
    // save and again on fresh data when the database rejects the save.
    private void ThrowIfUpdateRejected(DeviceRole role, int? storedVersion, Guid? organizationId, Exception? cause)
    {
        if (storedVersion is null || organizationId is null)
            throw new InvalidOperationException("DEVICE_ROLE_NOT_FOUND", cause);
        if (storedVersion != role.Version - 1)
            throw new InvalidOperationException("DEVICE_ROLE_VERSION_STALE", cause);
        ThrowIfNameTaken(organizationId.Value, role, cause);
    }

    private void ThrowIfRemoveRejected(DeviceRoleId id, int expectedVersion, int? storedVersion, Exception? cause)
    {
        if (storedVersion is null)
            throw new InvalidOperationException("DEVICE_ROLE_NOT_FOUND", cause);
        if (storedVersion != expectedVersion)
            throw new InvalidOperationException("DEVICE_ROLE_VERSION_STALE", cause);
        if (db.DeviceMemberships.Any(x => x.RoleId == id.Value))
            throw new InvalidOperationException("DEVICE_ROLE_IN_USE", cause);
    }

    private void ThrowIfNameTaken(Guid organizationId, DeviceRole role, Exception? cause)
    {
        var normalizedName = role.NormalizedName;
        if (db.DeviceRoles.Any(x =>
                x.OrganizationId == organizationId && x.NormalizedName == normalizedName && x.Id != role.Id.Value))
        {
            throw new InvalidOperationException("DEVICE_ROLE_NAME_TAKEN", cause);
        }
    }

    private static DeviceRole ToDomain(DeviceRoleRecord record) => DeviceRole.Rehydrate(
        record.Id,
        record.OrganizationId,
        record.Name,
        record.Description,
        record.Permissions.Select(x => x.Permission),
        record.Version);
}

public sealed class EfDeviceMembershipStore(ApplicationDbContext db) : IDeviceMembershipStore
{
    public DeviceMembership? Find(UserId userId, DeviceId deviceId)
    {
        var record = db.DeviceMemberships.AsNoTracking()
            .SingleOrDefault(x => x.UserId == userId.Value && x.DeviceId == deviceId.Value);
        return record is null ? null : ToDomain(record);
    }

    public IReadOnlyList<DeviceMembership> ListByDevice(DeviceId deviceId) => db.DeviceMemberships.AsNoTracking()
        .Where(x => x.DeviceId == deviceId.Value)
        .AsEnumerable()
        .Select(ToDomain)
        .ToArray();

    public IReadOnlyList<DeviceMembership> ListByRole(DeviceRoleId roleId) => db.DeviceMemberships.AsNoTracking()
        .Where(x => x.RoleId == roleId.Value)
        .AsEnumerable()
        .Select(ToDomain)
        .ToArray();

    public IReadOnlyList<DeviceMembership> ListByUser(UserId userId) => db.DeviceMemberships.AsNoTracking()
        .Where(x => x.UserId == userId.Value)
        .AsEnumerable()
        .Select(ToDomain)
        .ToArray();

    public void Upsert(DeviceMembership membership, DeviceRoleId? expectedRoleId, DeviceAccessAuditEntry audit)
    {
        audit.EnsureValid();
        ThrowIfRoleMissing(membership.RoleId, cause: null);
        var record = FindTracked(membership.UserId, membership.DeviceId);
        if (RoleOf(record) != expectedRoleId)
            throw new InvalidOperationException("DEVICE_MEMBERSHIP_CHANGED");

        DeviceAccessWrites.Save(db, audit,
            stage: () =>
            {
                if (record is null)
                {
                    db.DeviceMemberships.Add(new DeviceMembershipRecord
                    {
                        Id = Guid.NewGuid(),
                        UserId = membership.UserId.Value,
                        DeviceId = membership.DeviceId.Value,
                        RoleId = membership.RoleId.Value,
                        GrantedBy = membership.GrantedBy.Value,
                        GrantedAt = membership.GrantedAt
                    });
                    return;
                }

                record.RoleId = membership.RoleId.Value;
                record.GrantedBy = membership.GrantedBy.Value;
                record.GrantedAt = membership.GrantedAt;
                // Send the UPDATE even when no value changed, so the RoleId token is
                // still checked against the stored row.
                db.Entry(record).Property(x => x.RoleId).IsModified = true;
            },
            recheck: error =>
            {
                ThrowIfRoleMissing(membership.RoleId, error);
                ThrowIfChanged(membership.UserId, membership.DeviceId, expectedRoleId, error);
            });
    }

    public bool Remove(UserId userId, DeviceId deviceId, DeviceRoleId? expectedRoleId, DeviceAccessAuditEntry audit)
    {
        audit.EnsureValid();
        var record = FindTracked(userId, deviceId);
        if (RoleOf(record) != expectedRoleId)
            throw new InvalidOperationException("DEVICE_MEMBERSHIP_CHANGED");
        if (record is null)
            return false;

        DeviceAccessWrites.Save(db, audit,
            stage: () => db.DeviceMemberships.Remove(record),
            recheck: error => ThrowIfChanged(userId, deviceId, expectedRoleId, error));
        return true;
    }

    private void ThrowIfRoleMissing(DeviceRoleId roleId, Exception? cause)
    {
        if (!db.DeviceRoles.Any(x => x.Id == roleId.Value))
            throw new InvalidOperationException("DEVICE_ROLE_NOT_FOUND", cause);
    }

    private void ThrowIfChanged(UserId userId, DeviceId deviceId, DeviceRoleId? expectedRoleId, DbUpdateException error)
    {
        // A token conflict means the row changed after the check read it, even
        // if it holds the expected role again by now.
        var current = RoleOf(db.DeviceMemberships.AsNoTracking()
            .SingleOrDefault(x => x.UserId == userId.Value && x.DeviceId == deviceId.Value));
        if (error is DbUpdateConcurrencyException || current != expectedRoleId)
            throw new InvalidOperationException("DEVICE_MEMBERSHIP_CHANGED", error);
    }

    private DeviceMembershipRecord? FindTracked(UserId userId, DeviceId deviceId) =>
        db.DeviceMemberships.SingleOrDefault(x => x.UserId == userId.Value && x.DeviceId == deviceId.Value);

    private static DeviceRoleId? RoleOf(DeviceMembershipRecord? record) =>
        record is null ? null : new DeviceRoleId(record.RoleId);

    private static DeviceMembership ToDomain(DeviceMembershipRecord record) => new(
        new UserId(record.UserId),
        new DeviceId(record.DeviceId),
        new DeviceRoleId(record.RoleId),
        new UserId(record.GrantedBy),
        record.GrantedAt);
}

// One write discipline for both stores. The change and its audit row go out in
// one SaveChanges, which PostgreSQL runs in one transaction. Any failure clears
// the change tracker, so nothing is left for a later save in the same scope.
// When the database rejects the save (concurrency token, unique index, foreign
// key), recheck re-runs the store's checks on fresh data and throws the code
// they give; a rejection no check explains is rethrown.
internal static class DeviceAccessWrites
{
    public static void Save(
        ApplicationDbContext db,
        DeviceAccessAuditEntry audit,
        Action stage,
        Action<DbUpdateException> recheck)
    {
        try
        {
            stage();
            db.DeviceAccessAudits.Add(ToRecord(audit));
            db.SaveChanges();
        }
        catch (DbUpdateException error)
        {
            db.ChangeTracker.Clear();
            recheck(error);
            throw;
        }
        catch
        {
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private static DeviceAccessAuditRecord ToRecord(DeviceAccessAuditEntry audit) => new()
    {
        AuditId = audit.AuditId,
        Actor = audit.Actor.Value.ToString("D"),
        Change = audit.Change,
        OrganizationId = audit.OrganizationId?.Value,
        DeviceId = audit.DeviceId?.Value,
        TargetUserId = audit.TargetUserId?.Value,
        RoleId = audit.RoleId?.Value,
        PreviousValue = audit.PreviousValue,
        NewValue = audit.NewValue,
        OccurredAt = audit.OccurredAt
    };
}
