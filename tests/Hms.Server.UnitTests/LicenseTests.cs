using Hms.Server.Application.Licensing;
using Hms.Server.Domain.Devices;

namespace Hms.Server.UnitTests;

public sealed class LicenseTests
{
    [Fact]
    public void License_apply_preserves_raw_bytes_and_is_idempotent()
    {
        var device = DeviceId.New();
        var trust = new InMemoryLicenseTrustStore([
            new LicenseTrustEntry("kid-1", "vendor", "public-key", "active", DateTimeOffset.UtcNow.AddMinutes(-1), null)]);
        var store = new InMemoryLicenseStore();
        var handler = new ApplyLicenseHandler(trust, store, new FailClosedSecretRootPolicy(true), verifier: new AcceptingLicenseVerifier());
        var raw = new byte[] { 0, 1, 2, 255 };
        var request = new LicenseApplyRequest(device, "kid-1", "vendor", 1, raw);

        var first = handler.Handle(request);
        var second = handler.Handle(request);

        Assert.Equal("LICENSE_APPLIED", first.Code);
        Assert.Equal("LICENSE_REIMPORT_IDEMPOTENT", second.Code);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(raw)).ToLowerInvariant(), first.RawSha256);
        Assert.Equal(raw, store.FindActive(device)!.RawEnvelope);
    }

    [Fact]
    public void Unconfigured_secret_root_fails_closed_before_apply()
    {
        var handler = new ApplyLicenseHandler(
            new InMemoryLicenseTrustStore([]),
            new InMemoryLicenseStore(),
            new FailClosedSecretRootPolicy(false));

        Assert.Throws<InvalidOperationException>(() => handler.Handle(new LicenseApplyRequest(
            DeviceId.New(), "kid", "issuer", 1, new byte[] { 1 })));
    }

    [Fact]
    public void Pending_journal_blocks_a_different_target_until_recovery_is_resolved()
    {
        var device = DeviceId.New();
        var journal = new InMemoryLicenseApplyJournalStore();
        var oldHash = new string('a', 64);
        journal.Save(new LicenseApplyJournal(
            Guid.NewGuid(), device, 1, oldHash, 2, new string('b', 64),
            LicenseJournalState.RecoveryRequired, DateTimeOffset.UtcNow));
        var handler = new ApplyLicenseHandler(
            new InMemoryLicenseTrustStore([
                new LicenseTrustEntry("kid-1", "vendor", "public-key", "active", DateTimeOffset.UtcNow.AddMinutes(-1), null)]),
            new InMemoryLicenseStore(),
            new FailClosedSecretRootPolicy(true),
            journal,
            new AcceptingLicenseVerifier());

        var result = handler.Handle(new LicenseApplyRequest(device, "kid-1", "vendor", 3, new byte[] { 1, 2, 3 }));

        Assert.Equal(LicenseLifecycleState.RecoveryRequired, result.State);
        Assert.Equal("LICENSE_APPLY_RECOVERY_REQUIRED", result.Code);
    }

    [Fact]
    public void Persistence_failure_records_recovery_required_instead_of_claiming_applied()
    {
        var device = DeviceId.New();
        var journal = new InMemoryLicenseApplyJournalStore();
        var handler = new ApplyLicenseHandler(
            new InMemoryLicenseTrustStore([
                new LicenseTrustEntry("kid-1", "vendor", "public-key", "active", DateTimeOffset.UtcNow.AddMinutes(-1), null)]),
            new ThrowingLicenseStore(),
            new FailClosedSecretRootPolicy(true),
            journal,
            new AcceptingLicenseVerifier());

        var result = handler.Handle(new LicenseApplyRequest(device, "kid-1", "vendor", 1, new byte[] { 9 }));

        Assert.Equal(LicenseLifecycleState.RecoveryRequired, result.State);
        Assert.Equal(LicenseJournalState.RecoveryRequired, journal.FindPending(device)!.State);
    }

    [Fact]
    public void Different_bytes_cannot_replace_an_active_license_at_the_same_generation()
    {
        var device = DeviceId.New();
        var trust = new InMemoryLicenseTrustStore([
            new LicenseTrustEntry("kid-1", "vendor", "public-key", "active", DateTimeOffset.UtcNow.AddMinutes(-1), null)]);
        var store = new InMemoryLicenseStore();
        var handler = new ApplyLicenseHandler(trust, store, new FailClosedSecretRootPolicy(true), verifier: new AcceptingLicenseVerifier());

        Assert.Equal("LICENSE_APPLIED", handler.Handle(
            new LicenseApplyRequest(device, "kid-1", "vendor", 4, new byte[] { 1 })).Code);
        var conflict = handler.Handle(
            new LicenseApplyRequest(device, "kid-1", "vendor", 4, new byte[] { 2 }));

        Assert.Equal(LicenseLifecycleState.Rejected, conflict.State);
        Assert.Equal("LICENSE_BINDING_CONFLICT", conflict.Code);
        Assert.Equal(new byte[] { 1 }, store.FindActive(device)!.RawEnvelope);
    }

    [Fact]
    public void Unsigned_input_is_rejected_until_a_signer_contract_exists()
    {
        var handler = new ApplyLicenseHandler(
            new InMemoryLicenseTrustStore([
                new LicenseTrustEntry("kid-1", "vendor", "public-key", "active", DateTimeOffset.UtcNow.AddMinutes(-1), null)]),
            new InMemoryLicenseStore(),
            new FailClosedSecretRootPolicy(true),
            verifier: new AcceptingLicenseVerifier());

        var result = handler.Handle(new LicenseApplyRequest(
            DeviceId.New(), "kid-1", "vendor", 1, new byte[] { 1 }, AlreadySigned: false));

        Assert.Equal("LICENSE_UNSIGNED_NOT_SUPPORTED", result.Code);
    }

    [Fact]
    public void License_apply_fails_closed_when_signature_verifier_is_unavailable()
    {
        var handler = new ApplyLicenseHandler(
            new InMemoryLicenseTrustStore([
                new LicenseTrustEntry("kid-1", "vendor", "public-key", "active", DateTimeOffset.UtcNow.AddMinutes(-1), null)]),
            new InMemoryLicenseStore(),
            new FailClosedSecretRootPolicy(true));

        var result = handler.Handle(new LicenseApplyRequest(
            DeviceId.New(), "kid-1", "vendor", 1, new byte[] { 1 }));

        Assert.Equal("LICENSE_VERIFIER_UNAVAILABLE", result.Code);
        Assert.Equal(LicenseLifecycleState.Rejected, result.State);
    }

    [Fact]
    public void Oversized_license_is_rejected_before_verification_or_persistence()
    {
        var device = DeviceId.New();
        var verifier = new RecordingLicenseVerifier();
        var store = new InMemoryLicenseStore();
        var handler = new ApplyLicenseHandler(
            new InMemoryLicenseTrustStore([
                new LicenseTrustEntry("kid-1", "vendor", "public-key", "active", DateTimeOffset.UtcNow.AddMinutes(-1), null)]),
            store,
            new FailClosedSecretRootPolicy(true),
            verifier: verifier);

        var result = handler.Handle(new LicenseApplyRequest(
            device, "kid-1", "vendor", 1, new byte[2049]));

        Assert.Equal(LicenseLifecycleState.Rejected, result.State);
        Assert.Equal("LICENSE_BODY_TOO_LARGE", result.Code);
        Assert.False(verifier.Called);
        Assert.Null(store.FindActive(device));
    }

    [Fact]
    public void Trust_store_records_rotation_and_revocation_audit()
    {
        var store = new InMemoryLicenseTrustStore([]);
        var now = DateTimeOffset.UtcNow;
        var active = new LicenseTrustEntry("kid-1", "vendor", "public-key-1", "active", now, null);
        var rotated = new LicenseTrustEntry("kid-2", "vendor", "public-key-2", "active", now.AddMinutes(1), null);
        var revoked = active with { Status = "revoked", EffectiveFrom = now.AddMinutes(2) };

        store.Upsert(active, LicenseTrustChange.Created, "bootstrap", now);
        store.Upsert(rotated, LicenseTrustChange.Rotated, "operator-1", now.AddMinutes(1));
        store.Upsert(revoked, LicenseTrustChange.Revoked, "operator-2", now.AddMinutes(2));

        var audit = store.Audit("kid-1");
        Assert.Equal(2, audit.Count);
        Assert.Equal(LicenseTrustChange.Created, audit[0].Change);
        Assert.Equal(LicenseTrustChange.Revoked, audit[1].Change);
        Assert.Equal("active", audit[1].PreviousStatus);
        Assert.Equal("operator-2", audit[1].Actor);
        Assert.False(store.IsTrusted("kid-1", "vendor", now.AddMinutes(3)));
    }

    [Fact]
    public void Trust_store_rejects_same_kid_key_or_issuer_mutation()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new InMemoryLicenseTrustStore([]);
        store.Upsert(new LicenseTrustEntry("kid-1", "vendor", "key-1", "active", now, null),
            LicenseTrustChange.Created, "bootstrap", now);

        Assert.Throws<InvalidOperationException>(() => store.Upsert(
            new LicenseTrustEntry("kid-1", "vendor", "key-2", "active", now, null),
            LicenseTrustChange.Rotated, "operator", now.AddSeconds(1)));
        Assert.Throws<InvalidOperationException>(() => store.Upsert(
            new LicenseTrustEntry("kid-1", "other", "key-1", "active", now, null),
            LicenseTrustChange.Rotated, "operator", now.AddSeconds(2)));
    }

    [Fact]
    public void Trust_store_rejects_invalid_status_interval_and_transition()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new InMemoryLicenseTrustStore([]);
        Assert.Throws<InvalidOperationException>(() => store.Upsert(
            new LicenseTrustEntry("kid-1", "vendor", "key", "unknown", now, null),
            LicenseTrustChange.Created, "bootstrap", now));
        Assert.Throws<InvalidOperationException>(() => store.Upsert(
            new LicenseTrustEntry("kid-2", "vendor", "key", "active", now, now),
            LicenseTrustChange.Created, "bootstrap", now));
        Assert.Throws<InvalidOperationException>(() => store.Upsert(
            new LicenseTrustEntry("kid-3", "vendor", "key", "revoked", now, null),
            LicenseTrustChange.Created, "bootstrap", now));

        store.Upsert(new LicenseTrustEntry("kid-4", "vendor", "key", "active", now, null),
            LicenseTrustChange.Created, "bootstrap", now);
        Assert.Throws<InvalidOperationException>(() => store.Upsert(
            new LicenseTrustEntry("kid-4", "vendor", "key", "active", now, null),
            LicenseTrustChange.Revoked, "operator", now.AddSeconds(1)));
    }

    private sealed class AcceptingLicenseVerifier : ILicenseVerifier
    {
        public LicenseVerificationResult Verify(
            LicenseApplyRequest request,
            LicenseTrustEntry trust,
            DateTimeOffset verifiedAt) => new(true, "LICENSE_SIGNATURE_VERIFIED");
    }

    private sealed class RecordingLicenseVerifier : ILicenseVerifier
    {
        public bool Called { get; private set; }

        public LicenseVerificationResult Verify(
            LicenseApplyRequest request,
            LicenseTrustEntry trust,
            DateTimeOffset verifiedAt)
        {
            Called = true;
            return new(true, "LICENSE_SIGNATURE_VERIFIED");
        }
    }

    private sealed class ThrowingLicenseStore : ILicenseStore
    {
        public LicenseDocument? FindActive(DeviceId deviceId) => null;
        public LicenseDocument? FindByHash(DeviceId deviceId, string rawSha256) => null;
        public void Save(LicenseDocument document) => throw new InvalidOperationException("STORE_FAILURE");
    }
}
