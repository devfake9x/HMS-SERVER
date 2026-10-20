using System.Security.Cryptography;
using Hms.Server.Domain.Devices;

namespace Hms.Server.Application.Licensing;

public enum LicenseLifecycleState
{
    Draft,
    Signed,
    Applied,
    RecoveryRequired,
    Rejected
}

public enum LicenseJournalState
{
    Prepared,
    Applied,
    RecoveryRequired
}

public sealed record LicenseTrustEntry(
    string Kid,
    string Issuer,
    string PublicKey,
    string Status,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveUntil);

public enum LicenseTrustChange
{
    Created,
    Rotated,
    Revoked
}

public static class LicenseTrustValidation
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
        { "active", "retired", "revoked" };

    public static void ValidateEntry(LicenseTrustEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Kid) || string.IsNullOrWhiteSpace(entry.Issuer) ||
            string.IsNullOrWhiteSpace(entry.PublicKey))
            throw new ArgumentException("TRUST_ENTRY_REQUIRED");
        if (!AllowedStatuses.Contains(entry.Status))
            throw new InvalidOperationException("TRUST_STATUS_INVALID");
        if (entry.EffectiveUntil is not null && entry.EffectiveUntil <= entry.EffectiveFrom)
            throw new InvalidOperationException("TRUST_INTERVAL_INVALID");
    }

    public static void ValidateTransition(
        LicenseTrustEntry entry, LicenseTrustChange change, LicenseTrustEntry? previous)
    {
        ValidateEntry(entry);
        if (change == LicenseTrustChange.Created && previous is not null)
            throw new InvalidOperationException("TRUST_KID_ALREADY_EXISTS");
        if (change == LicenseTrustChange.Created &&
            !entry.Status.Equals("active", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("TRUST_CREATION_REQUIRES_ACTIVE");
        if (change == LicenseTrustChange.Rotated && previous is not null)
            throw new InvalidOperationException("TRUST_ROTATION_REQUIRES_NEW_KID");
        if (change == LicenseTrustChange.Rotated &&
            !entry.Status.Equals("active", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("TRUST_ROTATION_REQUIRES_ACTIVE");
        if (change == LicenseTrustChange.Revoked &&
            (previous is null || !previous.Status.Equals("active", StringComparison.OrdinalIgnoreCase) &&
             !previous.Status.Equals("retired", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("TRUST_REVOCATION_INVALID");
        if (change == LicenseTrustChange.Revoked &&
            !entry.Status.Equals("revoked", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("TRUST_REVOCATION_REQUIRES_REVOKED");
        if (previous is not null &&
            (!string.Equals(previous.Issuer, entry.Issuer, StringComparison.Ordinal) ||
             !string.Equals(previous.PublicKey, entry.PublicKey, StringComparison.Ordinal)))
            throw new InvalidOperationException("TRUST_BINDING_IMMUTABLE");
    }
}

public sealed record LicenseTrustAuditEntry(
    Guid AuditId,
    string Kid,
    string Issuer,
    LicenseTrustChange Change,
    string? PreviousStatus,
    string NewStatus,
    string Actor,
    DateTimeOffset OccurredAt);

public sealed record LicenseDocument(
    Guid LicenseId,
    DeviceId DeviceId,
    string Kid,
    string Issuer,
    int Generation,
    byte[] RawEnvelope,
    string RawSha256,
    LicenseLifecycleState State,
    DateTimeOffset UpdatedAt);

public sealed record LicenseApplyRequest(
    DeviceId DeviceId,
    string Kid,
    string Issuer,
    int Generation,
    ReadOnlyMemory<byte> RawEnvelope,
    bool AlreadySigned = true);

public sealed record LicenseApplyResult(
    LicenseLifecycleState State,
    string Code,
    Guid? LicenseId,
    string? RawSha256);

public sealed record LicenseApplyJournal(
    Guid JournalId,
    DeviceId DeviceId,
    int PriorGeneration,
    string? PriorRawSha256,
    int TargetGeneration,
    string TargetRawSha256,
    LicenseJournalState State,
    DateTimeOffset UpdatedAt);

public interface ILicenseTrustStore
{
    LicenseTrustEntry? Find(string kid);
    bool IsTrusted(string kid, string issuer, DateTimeOffset at);
    void Upsert(LicenseTrustEntry entry, LicenseTrustChange change, string actor, DateTimeOffset occurredAt);
    IReadOnlyList<LicenseTrustAuditEntry> Audit(string kid);
}

public interface ILicenseStore
{
    LicenseDocument? FindActive(DeviceId deviceId);
    LicenseDocument? FindByHash(DeviceId deviceId, string rawSha256);
    void Save(LicenseDocument document);
}

public interface ILicenseApplyJournalStore
{
    LicenseApplyJournal? FindPending(DeviceId deviceId);
    void Save(LicenseApplyJournal journal);
}

public interface ILicenseSigner
{
    ValueTask<ReadOnlyMemory<byte>> SignAsync(
        DeviceId deviceId,
        ReadOnlyMemory<byte> unsignedEnvelope,
        CancellationToken cancellationToken = default);
}

public sealed record LicenseVerificationResult(bool IsValid, string Code);

public interface ILicenseVerifier
{
    LicenseVerificationResult Verify(
        LicenseApplyRequest request,
        LicenseTrustEntry trust,
        DateTimeOffset verifiedAt);
}

public sealed class UnavailableLicenseVerifier : ILicenseVerifier
{
    public LicenseVerificationResult Verify(
        LicenseApplyRequest request,
        LicenseTrustEntry trust,
        DateTimeOffset verifiedAt) =>
        new(false, "LICENSE_VERIFIER_UNAVAILABLE");
}

public interface ISecretRootPolicy
{
    bool IsConfigured { get; }
    void EnsureReady();
}

public sealed class InMemoryLicenseTrustStore(IEnumerable<LicenseTrustEntry> entries) : ILicenseTrustStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, LicenseTrustEntry> _entries = entries
        .ToDictionary(x => x.Kid, StringComparer.Ordinal);
    private readonly List<LicenseTrustAuditEntry> _audit = [];

    public LicenseTrustEntry? Find(string kid)
    {
        lock (_gate)
            return _entries.GetValueOrDefault(kid);
    }

    public bool IsTrusted(string kid, string issuer, DateTimeOffset at) =>
        Find(kid) is { } entry &&
        entry.Issuer == issuer &&
        entry.Status.Equals("active", StringComparison.OrdinalIgnoreCase) &&
        entry.EffectiveFrom <= at &&
        (entry.EffectiveUntil is null || entry.EffectiveUntil > at);

    public void Upsert(LicenseTrustEntry entry, LicenseTrustChange change, string actor, DateTimeOffset occurredAt)
    {
        if (string.IsNullOrWhiteSpace(actor))
            throw new ArgumentException("Trust entry and actor are required.");

        lock (_gate)
        {
            _entries.TryGetValue(entry.Kid, out var previous);
            LicenseTrustValidation.ValidateTransition(entry, change, previous);
            _entries[entry.Kid] = entry;
            _audit.Add(new LicenseTrustAuditEntry(
                Guid.NewGuid(), entry.Kid, entry.Issuer, change,
                previous?.Status, entry.Status, actor, occurredAt));
        }
    }

    public IReadOnlyList<LicenseTrustAuditEntry> Audit(string kid)
    {
        lock (_gate)
            return _audit.Where(x => x.Kid == kid)
                .OrderBy(x => x.OccurredAt).ThenBy(x => x.AuditId).ToArray();
    }
}

public sealed class InMemoryLicenseStore : ILicenseStore
{
    private readonly Dictionary<Guid, LicenseDocument> _documents = [];

    public LicenseDocument? FindActive(DeviceId deviceId) => _documents.Values
        .Where(x => x.DeviceId == deviceId && x.State == LicenseLifecycleState.Applied)
        .OrderByDescending(x => x.Generation)
        .FirstOrDefault();

    public LicenseDocument? FindByHash(DeviceId deviceId, string rawSha256) => _documents.Values
        .SingleOrDefault(x => x.DeviceId == deviceId && x.RawSha256 == rawSha256);

    public void Save(LicenseDocument document) => _documents[document.LicenseId] = document;
}

public sealed class InMemoryLicenseApplyJournalStore : ILicenseApplyJournalStore
{
    private readonly Dictionary<Guid, LicenseApplyJournal> _journals = [];

    public LicenseApplyJournal? FindPending(DeviceId deviceId) => _journals.Values
        .Where(x => x.DeviceId == deviceId && x.State != LicenseJournalState.Applied)
        .OrderByDescending(x => x.UpdatedAt)
        .FirstOrDefault();

    public void Save(LicenseApplyJournal journal) => _journals[journal.JournalId] = journal;
}

public sealed class FailClosedSecretRootPolicy(bool configured) : ISecretRootPolicy
{
    public bool IsConfigured => configured;

    public void EnsureReady()
    {
        if (!configured) throw new InvalidOperationException("KMS_SECRET_ROOT_NOT_CONFIGURED");
    }
}

public sealed class ExternalLicenseSignerUnavailable : ILicenseSigner
{
    public ValueTask<ReadOnlyMemory<byte>> SignAsync(
        DeviceId deviceId,
        ReadOnlyMemory<byte> unsignedEnvelope,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("EXTERNAL_SIGNER_NOT_CONFIGURED");
}

public sealed class ApplyLicenseHandler(
    ILicenseTrustStore trustStore,
    ILicenseStore licenses,
    ISecretRootPolicy secretRoot,
    ILicenseApplyJournalStore? journal = null,
    ILicenseVerifier? verifier = null)
{
    public const int MaxRawEnvelopeBytes = 2048;

    public LicenseApplyResult Handle(LicenseApplyRequest request)
    {
        if (request.RawEnvelope.IsEmpty || request.Generation < 1 ||
            string.IsNullOrWhiteSpace(request.Kid) || string.IsNullOrWhiteSpace(request.Issuer))
            return Rejected("LICENSE_BODY_INVALID");
        if (request.RawEnvelope.Length > MaxRawEnvelopeBytes)
            return Rejected("LICENSE_BODY_TOO_LARGE");
        if (!request.AlreadySigned)
            return Rejected("LICENSE_UNSIGNED_NOT_SUPPORTED");

        secretRoot.EnsureReady();
        var now = DateTimeOffset.UtcNow;
        if (!trustStore.IsTrusted(request.Kid, request.Issuer, now))
            return Rejected("LICENSE_TRUST_NOT_FOUND");
        var trust = trustStore.Find(request.Kid);
        if (trust is null)
            return Rejected("LICENSE_TRUST_NOT_FOUND");
        var verification = (verifier ?? new UnavailableLicenseVerifier())
            .Verify(request, trust, now);
        if (!verification.IsValid)
            return Rejected(verification.Code);

        var raw = request.RawEnvelope.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant();
        var existing = licenses.FindByHash(request.DeviceId, hash);
        if (existing is not null)
            return new(existing.State, "LICENSE_REIMPORT_IDEMPOTENT", existing.LicenseId, hash);

        var pending = journal?.FindPending(request.DeviceId);
        if (pending is { } pendingJournal)
        {
            if (!pendingJournal.TargetRawSha256.Equals(hash, StringComparison.OrdinalIgnoreCase))
                return new(LicenseLifecycleState.RecoveryRequired,
                    "LICENSE_APPLY_RECOVERY_REQUIRED", null, pendingJournal.TargetRawSha256);
        }

        var active = licenses.FindActive(request.DeviceId);
        if (active is not null && request.Generation < active.Generation)
            return Rejected("LICENSE_GENERATION_ROLLBACK");
        if (active is not null && request.Generation == active.Generation &&
            !active.RawSha256.Equals(hash, StringComparison.OrdinalIgnoreCase))
            return Rejected("LICENSE_BINDING_CONFLICT");

        var journalEntry = pending ?? new LicenseApplyJournal(
            Guid.NewGuid(),
            request.DeviceId,
            active?.Generation ?? 0,
            active?.RawSha256,
            request.Generation,
            hash,
            LicenseJournalState.Prepared,
            now);
        journal?.Save(journalEntry);

        var document = new LicenseDocument(
            Guid.NewGuid(),
            request.DeviceId,
            request.Kid,
            request.Issuer,
            request.Generation,
            raw,
            hash,
            LicenseLifecycleState.Applied,
            now);
        try
        {
            licenses.Save(document);
            journal?.Save(journalEntry with { State = LicenseJournalState.Applied, UpdatedAt = DateTimeOffset.UtcNow });
            return new(LicenseLifecycleState.Applied, "LICENSE_APPLIED", document.LicenseId, hash);
        }
        catch
        {
            journal?.Save(journalEntry with { State = LicenseJournalState.RecoveryRequired, UpdatedAt = DateTimeOffset.UtcNow });
            return new(LicenseLifecycleState.RecoveryRequired,
                "LICENSE_APPLY_RECOVERY_REQUIRED", document.LicenseId, hash);
        }
    }

    private static LicenseApplyResult Rejected(string code) =>
        new(LicenseLifecycleState.Rejected, code, null, null);
}
