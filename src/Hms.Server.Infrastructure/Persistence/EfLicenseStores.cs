using Hms.Server.Application.Licensing;
using Hms.Server.Domain.Devices;
using Microsoft.EntityFrameworkCore;

namespace Hms.Server.Infrastructure.Persistence;

public sealed class EfLicenseTrustStore(ApplicationDbContext db) : ILicenseTrustStore
{
    public LicenseTrustEntry? Find(string kid)
    {
        var entry = db.LicenseTrustEntries.AsNoTracking().SingleOrDefault(x => x.Kid == kid);
        return entry is null ? null : new LicenseTrustEntry(
            entry.Kid,
            entry.Issuer,
            entry.PublicKey,
            entry.Status,
            entry.EffectiveFrom,
            entry.EffectiveUntil);
    }

    public bool IsTrusted(string kid, string issuer, DateTimeOffset at) =>
        Find(kid) is { } entry &&
        entry.Issuer == issuer &&
        entry.Status.Equals("active", StringComparison.OrdinalIgnoreCase) &&
        entry.EffectiveFrom <= at &&
        (entry.EffectiveUntil is null || entry.EffectiveUntil > at);

    public void Upsert(LicenseTrustEntry entry, LicenseTrustChange change, string actor, DateTimeOffset occurredAt)
    {
        if (string.IsNullOrWhiteSpace(entry.Kid) || string.IsNullOrWhiteSpace(entry.Issuer) ||
            string.IsNullOrWhiteSpace(entry.PublicKey) || string.IsNullOrWhiteSpace(actor))
            throw new ArgumentException("Trust entry and actor are required.");

        var record = db.LicenseTrustEntries.SingleOrDefault(x => x.Kid == entry.Kid);
        var previous = record is null ? null : new LicenseTrustEntry(
            record.Kid, record.Issuer, record.PublicKey, record.Status,
            record.EffectiveFrom, record.EffectiveUntil);
        LicenseTrustValidation.ValidateTransition(entry, change, previous);
        var previousStatus = record?.Status;
        record ??= new LicenseTrustRecord { Kid = entry.Kid };
        record.Issuer = entry.Issuer;
        record.PublicKey = entry.PublicKey;
        record.Status = entry.Status;
        record.EffectiveFrom = entry.EffectiveFrom;
        record.EffectiveUntil = entry.EffectiveUntil;
        if (db.Entry(record).State == EntityState.Detached)
            db.LicenseTrustEntries.Add(record);

        db.LicenseTrustAudits.Add(new LicenseTrustAuditRecord
        {
            AuditId = Guid.NewGuid(),
            Kid = entry.Kid,
            Issuer = entry.Issuer,
            Change = change.ToString(),
            PreviousStatus = previousStatus,
            NewStatus = entry.Status,
            Actor = actor,
            OccurredAt = occurredAt
        });
        db.SaveChanges();
    }

    public IReadOnlyList<LicenseTrustAuditEntry> Audit(string kid) =>
        db.LicenseTrustAudits.AsNoTracking()
            .Where(x => x.Kid == kid)
            .OrderBy(x => x.OccurredAt).ThenBy(x => x.AuditId)
            .AsEnumerable()
            .Select(x => new LicenseTrustAuditEntry(
                x.AuditId,
                x.Kid,
                x.Issuer,
                Enum.TryParse<LicenseTrustChange>(x.Change, out var change)
                    ? change
                    : throw new InvalidOperationException("UNKNOWN_LICENSE_TRUST_CHANGE"),
                x.PreviousStatus,
                x.NewStatus,
                x.Actor,
                x.OccurredAt))
            .ToArray();
}

public sealed class EfLicenseStore(ApplicationDbContext db) : ILicenseStore
{
    public LicenseDocument? FindActive(DeviceId deviceId) => db.Licenses.AsNoTracking()
        .Where(x => x.DeviceId == deviceId.Value && x.State == LicenseLifecycleState.Applied.ToString())
        .OrderByDescending(x => x.Generation)
        .AsEnumerable()
        .Select(ToDomain)
        .FirstOrDefault();

    public LicenseDocument? FindByHash(DeviceId deviceId, string rawSha256) => db.Licenses.AsNoTracking()
        .Where(x => x.DeviceId == deviceId.Value && x.RawSha256 == rawSha256)
        .AsEnumerable()
        .Select(ToDomain)
        .SingleOrDefault();

    public void Save(LicenseDocument document)
    {
        var existing = db.Licenses.SingleOrDefault(x => x.LicenseId == document.LicenseId);
        var record = existing ?? new LicenseRecord { LicenseId = document.LicenseId };
        record.DeviceId = document.DeviceId.Value;
        record.Kid = document.Kid;
        record.Issuer = document.Issuer;
        record.Generation = document.Generation;
        record.RawEnvelope = document.RawEnvelope;
        record.RawSha256 = document.RawSha256;
        record.State = document.State.ToString();
        record.UpdatedAt = document.UpdatedAt;
        if (existing is null) db.Licenses.Add(record);
        try
        {
            db.SaveChanges();
        }
        catch (DbUpdateException)
        {
            // A concurrent apply may have won the unique device/generation
            // fence. Detach only this candidate so the caller can persist its
            // recovery journal with the same scoped DbContext.
            if (existing is null)
                db.Entry(record).State = EntityState.Detached;
            throw;
        }
    }

    private static LicenseDocument ToDomain(LicenseRecord record) => new(
        record.LicenseId,
        new DeviceId(record.DeviceId),
        record.Kid,
        record.Issuer,
        record.Generation,
        record.RawEnvelope,
        record.RawSha256,
        Enum.TryParse<LicenseLifecycleState>(record.State, out var state) ? state : LicenseLifecycleState.Rejected,
        record.UpdatedAt);
}
