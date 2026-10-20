using Hms.Server.Application.Licensing;
using Hms.Server.Domain.Devices;
using Microsoft.EntityFrameworkCore;

namespace Hms.Server.Infrastructure.Persistence;

public sealed class EfLicenseApplyJournalStore(ApplicationDbContext db) : ILicenseApplyJournalStore
{
    public LicenseApplyJournal? FindPending(DeviceId deviceId) => db.LicenseApplyJournals
        .AsNoTracking()
        .Where(x => x.DeviceId == deviceId.Value && x.State != LicenseJournalState.Applied.ToString())
        .OrderByDescending(x => x.UpdatedAt)
        .AsEnumerable()
        .Select(ToDomain)
        .FirstOrDefault();

    public void Save(LicenseApplyJournal journal)
    {
        var existing = db.LicenseApplyJournals.SingleOrDefault(x => x.JournalId == journal.JournalId);
        var record = existing ?? new LicenseApplyJournalRecord { JournalId = journal.JournalId };
        record.DeviceId = journal.DeviceId.Value;
        record.PriorGeneration = journal.PriorGeneration;
        record.PriorRawSha256 = journal.PriorRawSha256;
        record.TargetGeneration = journal.TargetGeneration;
        record.TargetRawSha256 = journal.TargetRawSha256;
        record.State = journal.State.ToString();
        record.UpdatedAt = journal.UpdatedAt;
        if (existing is null) db.LicenseApplyJournals.Add(record);
        db.SaveChanges();
    }

    private static LicenseApplyJournal ToDomain(LicenseApplyJournalRecord record) => new(
        record.JournalId,
        new DeviceId(record.DeviceId),
        record.PriorGeneration,
        record.PriorRawSha256,
        record.TargetGeneration,
        record.TargetRawSha256,
        Enum.TryParse<LicenseJournalState>(record.State, out var state)
            ? state
            : LicenseJournalState.RecoveryRequired,
        record.UpdatedAt);
}
