using Hms.Server.Application.Authorization;
using Hms.Server.Domain.Authorization;
using Hms.Server.Domain.Devices;
using Hms.Server.Domain.Organizations;
using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Hms.Server.Infrastructure.Persistence;

public sealed class EfPermissionStore(ApplicationDbContext db) : IPermissionStore
{
    // Other providers serve development and tests, which run in one process.
    private static readonly object ProcessGate = new();
    // First key of the two-key advisory lock: keeps grant locks apart from any other.
    private const int GrantLockNamespace = 0x48474C4B;

    public long PolicyVersion
    {
        get
        {
            var counter = db.PolicyVersions.AsNoTracking()
                .Where(x => x.Id == 1)
                .Select(x => (long?)x.Version)
                .SingleOrDefault() ?? 0;
            var grants = db.PermissionGrants.Select(x => (long?)x.PolicyVersion).Max() ?? 0;
            return Math.Max(counter, grants);
        }
    }

    public long Advance()
    {
        if (IsPostgres)
        {
            return AdvancePostgres();
        }

        var record = db.PolicyVersions.Find(1);
        if (record is null)
        {
            record = new PolicyVersionRecord
            {
                Id = 1,
                Version = checked((db.PermissionGrants.Select(x => (long?)x.PolicyVersion).Max() ?? 0) + 1)
            };
            db.PolicyVersions.Add(record);
        }
        else
        {
            var grantVersion = db.PermissionGrants.Select(x => (long?)x.PolicyVersion).Max() ?? 0;
            record.Version = checked(Math.Max(record.Version, grantVersion) + 1);
        }

        db.SaveChanges();
        return record.Version;
    }

    public void GrantFeature(FeaturePermissionGrant grant)
    {
        db.PermissionGrants.Add(new PermissionGrantRecord
        {
            Id = Guid.NewGuid(),
            UserId = grant.UserId.Value,
            Kind = "feature",
            Permission = grant.Permission,
            Deny = grant.Deny,
            PolicyVersion = grant.PolicyVersion,
            GrantedBy = grant.GrantedBy?.Value,
            GrantedByMasterAdmin = grant.GrantedByMasterAdmin
        });
        db.SaveChanges();
    }

    public void GrantResource(ResourceScopeGrant grant)
    {
        db.PermissionGrants.Add(new PermissionGrantRecord
        {
            Id = Guid.NewGuid(),
            UserId = grant.UserId.Value,
            Kind = "resource",
            OrganizationId = grant.OrganizationId.Value,
            Deny = grant.Deny,
            PolicyVersion = grant.PolicyVersion,
            GrantedBy = grant.GrantedBy?.Value,
            GrantedByMasterAdmin = grant.GrantedByMasterAdmin
        });
        db.SaveChanges();
    }

    public void GrantDeviceAction(DeviceActionGrant grant)
    {
        db.PermissionGrants.Add(new PermissionGrantRecord
        {
            Id = Guid.NewGuid(),
            UserId = grant.UserId.Value,
            Kind = "device-action",
            DeviceId = grant.DeviceId.Value,
            Action = grant.Action,
            Deny = grant.Deny,
            PolicyVersion = grant.PolicyVersion
        });
        db.SaveChanges();
    }

    public IReadOnlyList<FeaturePermissionGrant> FeatureGrants(UserId userId) => db.PermissionGrants.AsNoTracking()
        .Where(x => x.UserId == userId.Value && x.Kind == "feature")
        .AsEnumerable()
        .Select(x => new FeaturePermissionGrant(
            userId, x.Permission ?? string.Empty, x.Deny, x.PolicyVersion, Grantor(x), x.GrantedByMasterAdmin))
        .ToArray();

    public IReadOnlyList<ResourceScopeGrant> ResourceGrants(UserId userId) => db.PermissionGrants.AsNoTracking()
        .Where(x => x.UserId == userId.Value && x.Kind == "resource" && x.OrganizationId.HasValue)
        .AsEnumerable()
        .Select(x => new ResourceScopeGrant(
            userId, new OrganizationId(x.OrganizationId!.Value), x.Deny, x.PolicyVersion, Grantor(x), x.GrantedByMasterAdmin))
        .ToArray();

    private static UserId? Grantor(PermissionGrantRecord record) =>
        record.GrantedBy is { } grantedBy ? new UserId(grantedBy) : null;

    public T WithUserLock<T>(UserId userId, Func<T> decide)
    {
        if (!IsPostgres)
        {
            lock (ProcessGate)
                return decide();
        }

        // The transaction-scoped advisory lock holds from the decision's first read to
        // its write on every replica, and is released at commit or rollback.
        using var transaction = db.Database.BeginTransaction();
        db.Database.ExecuteSql($"SELECT pg_advisory_xact_lock({GrantLockNamespace}, {LockKey(userId)})");
        var result = decide();
        transaction.Commit();
        return result;
    }

    private bool IsPostgres => string.Equals(
        db.Database.ProviderName,
        "Npgsql.EntityFrameworkCore.PostgreSQL",
        StringComparison.Ordinal);

    // Folds the user id into the lock's second key; a collision only serializes two users.
    private static int LockKey(UserId userId)
    {
        Span<byte> bytes = stackalloc byte[16];
        userId.Value.TryWriteBytes(bytes);
        return BitConverter.ToInt32(bytes[..4]) ^ BitConverter.ToInt32(bytes[4..8]) ^
               BitConverter.ToInt32(bytes[8..12]) ^ BitConverter.ToInt32(bytes[12..]);
    }

    public IReadOnlyList<DeviceActionGrant> DeviceActionGrants(UserId userId) => db.PermissionGrants.AsNoTracking()
        .Where(x => x.UserId == userId.Value && x.Kind == "device-action" && x.DeviceId.HasValue)
        .AsEnumerable()
        .Select(x => new DeviceActionGrant(userId, new DeviceId(x.DeviceId!.Value), x.Action ?? string.Empty, x.Deny, x.PolicyVersion))
        .ToArray();

    private long AdvancePostgres()
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State == ConnectionState.Closed;
        if (shouldClose)
        {
            connection.Open();
        }

        try
        {
            using var command = connection.CreateCommand();
            // Inside a grant decision the counter moves in the decision's transaction.
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = """
                INSERT INTO "policy_versions" ("Id", "Version")
                VALUES (1, 1)
                ON CONFLICT ("Id") DO UPDATE
                SET "Version" = "policy_versions"."Version" + 1
                RETURNING "Version";
                """;
            var result = command.ExecuteScalar();
            if (result is null || result is DBNull)
                throw new InvalidOperationException("POLICY_VERSION_ADVANCE_FAILED");
            return Convert.ToInt64(result, CultureInfo.InvariantCulture);
        }
        finally
        {
            if (shouldClose)
                connection.Close();
        }
    }
}
