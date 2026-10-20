using System.Text.Json;
using System.Security.Cryptography;
using Hms.Server.Application.Licensing;
using Hms.Server.Application.Provisioning;
using Hms.Server.Domain.Devices;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;

namespace Hms.Server.Infrastructure.Persistence;

public sealed class EfProvisioningBundleStore(
    ApplicationDbContext db,
    IDataProtectionProvider dataProtection,
    ISecretRootPolicy secretRoot) : IProvisioningBundleStore
{
    private const string ProtectorPurpose = "Hms.Server.ProvisioningBundle.v1";
    private static readonly object InMemoryConsumeGate = new();
    private readonly IDataProtector _protector = dataProtection.CreateProtector(ProtectorPurpose);

    public ProvisioningBundleMetadata Save(ProvisioningBundle bundle)
    {
        secretRoot.EnsureReady();
        using var lifecycleFence = BeginLifecycleFence(bundle.DeviceId.Value);
        var state = db.Devices.AsNoTracking()
            .Where(x => x.Id == bundle.DeviceId.Value)
            .Select(x => x.State)
            .SingleOrDefault();
        if (state is null)
            throw new InvalidOperationException("DEVICE_NOT_FOUND");
        if (StringComparer.Ordinal.Equals(state, nameof(DeviceLifecycleState.Revoked)))
            throw new InvalidOperationException("DEVICE_REVOKED");

        if (db.ProvisioningBundles.Any(x => x.BundleId == bundle.BundleId))
            throw new InvalidOperationException("PROVISIONING_BUNDLE_ALREADY_EXISTS");

        var protectedPayload = _protector.Protect(JsonSerializer.SerializeToUtf8Bytes(bundle));
        db.ProvisioningBundles.Add(new ProvisioningBundleRecord
        {
            BundleId = bundle.BundleId,
            DeviceId = bundle.DeviceId.Value,
            ExpiresAt = bundle.ExpiresAt,
            Used = false,
            CreatedAt = DateTimeOffset.UtcNow,
            ProtectedPayload = protectedPayload
        });
        db.SaveChanges();
        lifecycleFence?.Commit();
        return new(bundle.BundleId, bundle.DeviceId, bundle.ExpiresAt, false);
    }

    private IDbContextTransaction? BeginLifecycleFence(Guid deviceId)
    {
        // The development InMemory provider has no transaction or row-lock
        // surface. Production PostgreSQL serializes this device row with the
        // revoke transaction before the bundle is inserted.
        if (!db.Database.IsRelational() ||
            !(db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ?? false))
            return null;

        var transaction = db.Database.BeginTransaction(IsolationLevel.Serializable);
        db.Database.ExecuteSqlInterpolated(
            $"SELECT \"Id\" FROM \"devices\" WHERE \"Id\" = {deviceId} FOR UPDATE");
        return transaction;
    }

    public ProvisioningBundle? Consume(Guid bundleId) => ConsumeCore(null, bundleId);

    public ProvisioningBundle? Consume(DeviceId expectedDeviceId, Guid bundleId) =>
        ConsumeCore(expectedDeviceId.Value, bundleId);

    private ProvisioningBundle? ConsumeCore(Guid? expectedDeviceId, Guid bundleId)
    {
        secretRoot.EnsureReady();
        var now = DateTimeOffset.UtcNow;
        var query = db.ProvisioningBundles
            .Where(x => x.BundleId == bundleId && !x.Used && x.ExpiresAt > now);
        if (expectedDeviceId.HasValue)
            query = query.Where(x => x.DeviceId == expectedDeviceId.Value);

        var consumedAt = now;
        var updated = db.Database.IsInMemory()
            ? ConsumeInMemory(query, consumedAt)
            : query.ExecuteUpdate(setters => setters
                .SetProperty(x => x.Used, true)
                .SetProperty(x => x.ConsumedAt, consumedAt));
        if (updated != 1)
            return null;

        var record = db.ProvisioningBundles.AsNoTracking().SingleOrDefault(x => x.BundleId == bundleId);
        if (record is null)
            return null;

        try
        {
            var bytes = _protector.Unprotect(record.ProtectedPayload);
            var bundle = JsonSerializer.Deserialize<ProvisioningBundle>(bytes)
                ?? throw new InvalidOperationException("PROVISIONING_BUNDLE_PAYLOAD_INVALID");
            if (!ProvisioningBundleIntegrity.IsValid(bundle) ||
                bundle.BundleId != record.BundleId ||
                bundle.DeviceId.Value != record.DeviceId ||
                bundle.ExpiresAt != record.ExpiresAt)
                throw new InvalidOperationException("PROVISIONING_BUNDLE_INTEGRITY_INVALID");
            return bundle;
        }
        catch (Exception ex) when (ex is JsonException or CryptographicException or InvalidOperationException)
        {
            throw new InvalidOperationException("PROVISIONING_BUNDLE_PAYLOAD_INVALID", ex);
        }
    }

    private int ConsumeInMemory(IQueryable<ProvisioningBundleRecord> query, DateTimeOffset consumedAt)
    {
        lock (InMemoryConsumeGate)
        {
            var record = query.SingleOrDefault();
            if (record is null)
                return 0;

            record.Used = true;
            record.ConsumedAt = consumedAt;
            db.SaveChanges();
            return 1;
        }
    }
}
