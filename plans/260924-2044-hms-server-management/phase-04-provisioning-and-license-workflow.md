---
phase: 4
title: "Provisioning and license workflow"
status: in-progress
priority: P1
dependencies: ["3"]
---
# Phase 4: Provisioning and license workflow

## Overview
Implement database-transactional device provisioning, encrypted credential/key storage, one-time identity bundle issuance, key rotation/revocation, protobuf license request parsing, draft/sign/apply/remove lifecycle, and signer adapters. This phase makes the backend capable of producing the material that the firmware importer will consume; it does not claim atomicity across firmware NVS partitions.

## Requirements
- Functional: Device creation generates `device_id`, MQTT credentials, keyset, and enrollment record in one database transaction with an explicit activation state.
- Functional: Store `crypto/cmd_key`, `crypto/tlm_sign`, and `crypto/tlm_data` as separate key types with generation and revocation.
- Functional: Produce a versioned canonical identity bundle with one-time download and audit.
- Functional: Decode/validate raw `LicenseRequest`, preserve raw bytes, create editable drafts, import or produce signed `LicenseEnvelope`, and expose lifecycle state.
- Functional: Support external vendor signing and managed signer through one adapter interface.
- Non-functional: Secrets are encrypted at rest and redacted from logs, DTOs, audit payloads, exceptions, and frontend caches.
- Non-functional: Bundle and license downloads stream with `Cache-Control: no-store`/`Pragma: no-cache`; access/proxy logs, tracing, crash dumps, temporary files, and analytics exclude tokens and secret bytes; encrypted backups/PITR and KMS-wrapped data keys follow explicit retention, rotation, revocation, and destruction rules.
- Safety: Production managed signing fails closed unless an external signer or KMS/HSM adapter is configured; raw private PEM in the web process is rejected.
- Safety: The application fails startup in production when the KMS-backed secret root, Data Protection key-ring, or signer trust store is missing, ephemeral, unreadable, or configured for raw private PEM.
- Safety: provisioning uses a server-side transaction plus an explicit activation state; no bundle is downloadable until device, enrollment, credential and key rows are committed.
- Limits: MQTT passwords are at most 128 UTF-8 bytes; each key is exactly 32 bytes; bundle JSON is at most 16 KiB; encoded LicenseEnvelope is at most 2,048 bytes, while its protobuf `payload` field is at most 1,536 bytes.

## Architecture
Provisioning is an application transaction across device, credential, keyset and enrollment records. A bundle is generated only after commit and references a bundle record with expiry, one-time token and hash. Key generations are immutable rows: rotation inserts a new generation and marks the previous one revoked with an effective-at timestamp; existing signed commands retain their generation for deterministic stale-key handling.

The canonical device import payload is a small JSON envelope (`hms-device-identity`, version 1) containing device identity, broker configuration, credential, base64 key values, generation metadata, and base64 license envelope. Raw protobuf request/envelope bytes are retained separately for vendor exchange and verification. The device importer must consume the canonical envelope defined in Phase 1.

License payload editing is draft-only. A signed envelope is immutable evidence and is never rewritten in place. The accepted binding tuple (`device_id`, hardware MAC, nonce, counter, signer key ID, and envelope bytes/hash) is immutable; any change creates a new draft and signature. The license trust store maps `kid` to issuer/public key/status/effective interval and rejects unknown, retired, not-yet-effective, or rollback keys. `ExternalVendorIssuer` exports request/draft data; `ManagedSignerIssuer` calls a protected signer and validates the returned ECDSA envelope against the configured public key/kid. Applying a license is the final device activation step. The firmware journal may durably commit the target license before a later clock-clear/publication step fails, so recovery stores the exact envelope hash, target sequence, prior sequence/hash, and pending state. It may resume only the firmware-proven exact-pending clock-clear path; it must never blindly replay an already committed envelope or promise restoration of the old license after the new durable marker. Recovery either completes the target generation or enters an explicit `recovery-required`/fail-closed state without mixing generations or silently destroying license authority.

The field-by-field bundle contract is recorded in
`reports/260925-provisioning-bundle-field-matrix.md`. Current firmware proves
getters, license binding and key rules, but not a combined importer setter;
unknown bundle fields and unsupported secondary broker modes are rejected rather
than silently ignored.

## Related Code Files
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Domain/Provisioning/*`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Domain/Licensing/*`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Application/Provisioning/*`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Application/Licensing/*`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Infrastructure/Secrets/EnvelopeEncryptionService.cs`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Infrastructure/Provisioning/ProvisioningBundleWriter.cs`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Infrastructure/Licensing/NanopbLicenseCodec.cs`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Infrastructure/Licensing/ExternalVendorIssuer.cs`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Infrastructure/Licensing/ManagedSignerIssuer.cs`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Infrastructure/Security/KmsSecretRootPolicy.cs`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Infrastructure/Licensing/LicenseTrustStore.cs`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Infrastructure/Licensing/SignerConfigurationValidator.cs`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Api/Controllers/ProvisioningController.cs`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Api/Controllers/DeviceLicenseController.cs`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/tests/Hms.Server.UnitTests/Provisioning/*`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/tests/Hms.Server.UnitTests/Licensing/*`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/tests/Hms.Server.IntegrationTests/Provisioning/*`
- Reference: `D:/WORKSPACE/PROJECT/HMS-IDF/protobuf/proto/license.proto`
- Reference: `D:/WORKSPACE/PROJECT/HMS-IDF/components/hms-license/include/hms_license.h`
- Reference: `D:/WORKSPACE/PROJECT/HMS-IDF/components/hms-http-api/license/license_api.c`
- Reference: `D:/WORKSPACE/PROJECT/HMS-IDF/tools/license-tool/license_tool.py`

## Implementation Steps
1. **Tests Before**: Add failing tests for database transaction success/rollback (not cross-partition firmware atomicity), secret redaction, one-time bundle download, key rotation/revoke, protobuf request decoding, license binding/counter/nonce validation, unknown/retired/not-yet-effective/rollback `kid`, missing or ephemeral KMS/Data Protection roots, invalid signature, and signer adapter behavior.
2. Implement CSPRNG credential/key generation and envelope encryption. Persist password hash plus encrypted password only when re-export is required by the approved workflow. Require a KMS-backed secret root and non-ephemeral Data Protection key-ring in production; record app identity, rotation, recovery, backup/PITR and destruction policy.
3. Implement `CreateDeviceProvisioningUseCase` with idempotency key and transaction boundary. Do not return raw key material from ordinary device CRUD.
4. Implement bundle writer, manifest hash, one-time token, expiry, content disposition, `no-store` headers, streaming response, and download audit. Scrub tokens and bytes from reverse-proxy/access logs, tracing, temp files, and analytics; define encrypted backup/PITR retention and KMS data-key rotation/revocation/destruction. Add strict maximum size and schema validation.
5. Generate or bind the logical `device_id` before final license request when the license binds device ID and MAC.
6. Implement protobuf codec/validator using the pinned HMS-IDF schema limits: nested payload max 1,536 bytes, encoded envelope max 2,048 bytes, DER signature max 72 bytes, signer key ID max 32 bytes, ECDSA P-256/SHA-256, and request nonce/counter fields. Preserve the exact input bytes and reject non-canonical re-encoding where byte identity is required.
7. Implement license draft, external vendor handoff, managed signer adapter, signed envelope import, apply/remove state transitions, immutable envelope hashes, and signer key/kid rotation with an auditable trust-store effective interval. Reject unknown, retired, not-yet-effective, or rollback signer keys before persistence. Model firmware `exact_pending` resume separately from a durable target-license commit; never treat a replay rejection as permission to restore the old license.
8. Implement rotation/revocation with an activation fence: stop new commands, drain or mark in-flight commands, switch generation, then resume. Old generations must fail authentication after the fence and must never be retried as new commands.
9. **Tests After**: Run unit and PostgreSQL integration tests, including transaction rollback and one-time download replay.

## Success Criteria
- [ ] Device creation produces a complete enrollment record exactly once under an idempotency key.
- [ ] Raw credentials/key bytes never appear in list endpoints, logs, exception messages, or audit payloads.
- [ ] Bundle download succeeds once, then returns a deterministic expired/used response.
- [ ] Rotation creates a new generation and revokes the old credential/keyset.
- [ ] License request fields are decoded correctly and raw bytes are preserved.
- [ ] Signed envelope edits require a new signature; original hash remains auditable.
- [ ] Managed signer output is verified for signature, kid, binding, counter, and schema before persistence.
- [ ] Production startup rejects missing/ephemeral secret roots, unprotected Data Protection keys, raw private PEM, and unknown/retired/rollback signer trust anchors.
- [ ] External vendor mode never requires the server to possess the vendor private key.
- [ ] A crash before a durable license marker leaves the prior active generation or a recoverable pending activation; a crash after the target license marker leaves a recoverable target or explicit `recovery-required` state, never a mixed generation or silent rollback claim.
- [ ] License envelope bytes and signer metadata are immutable after acceptance; changes create a new draft/signature and retain the original hash.

## Risk Assessment
- **Risk**: duplicate device creation creates multiple active credentials. **Mitigation**: unique MAC/device ID, idempotency key, transaction, and active enrollment constraint.
- **Risk**: bundle token leaks. **Mitigation**: one-time short-lived token, HTTPS, audit, no token in ordinary logs, immediate revoke endpoint.
- **Risk**: license data is altered without a valid signature. **Mitigation**: immutable original bytes/hash and signer verification before apply.
- **Risk**: bundle material leaks through caches, logs, backups, or stale encryption keys. **Mitigation**: no-store streaming, redaction tests for every logging/proxy path, encrypted backup/PITR policy, and KMS key lifecycle controls.
- **Risk**: private signer key is accidentally exposed to web runtime. **Mitigation**: production configuration rejects raw private PEM and requires an external signer or KMS/HSM adapter with auditable key IDs.

## File inventory and test matrix

| Workflow | Tests before implementation |
| --- | --- |
| Device provisioning | atomic success/rollback, duplicate MAC, idempotent retry |
| Secrets | encryption/decryption, redaction, wrong key, no raw logs |
| Bundle | schema, hash, one-time download, expiry, replay, size limit |
| Key rotation | generation increment, old key rejection, revoke audit |
| License request | protobuf round trip, missing fields, malformed bytes |
| License envelope | signature/kid/binding/counter/nonce/expiry, immutable binding tuple/hash, 1,536-byte payload and 2,048-byte encoded limits |
| Secret lifecycle | no-store headers, proxy/access-log redaction, temp-file/tracing redaction, encrypted backup/PITR retention, KMS rotation/revocation/destruction |
| Signer adapters | external handoff and managed signer success/failure |
| Trust roots | KMS/Data Protection startup failure, key rotation/recovery, backup restore, `kid` allowlist/effective interval |

## Dependency map

- Blocked by domain/keyset repositories and command signer contracts from Phases 1-3.
- Blocks firmware importer and frontend provisioning/license screens.
- Does not require a physical device for most unit/integration tests, but requires a golden protobuf fixture.

## Function/interface checklist

- [ ] `IDeviceProvisioningService` creates and rotates enrollment material transactionally.
- [ ] `ISecretProtector` encrypts/decrypts only inside infrastructure boundaries.
- [ ] `IProvisioningBundleWriter` emits canonical versioned bytes and redacts diagnostics.
- [ ] `ILicenseCodec` decodes/encodes pinned protobuf fields without mutation of original bytes.
- [ ] `ILicenseIssuer` supports external and managed implementations.
- [ ] `ILicenseLifecycleService` models draft, signed, applied, removed, and rejected states.
- [ ] `IOneTimeDownloadService` prevents replay and records audit.
- [ ] `IBundleSecretLifecyclePolicy` defines no-store delivery, log/cache/temp-file redaction, encrypted backup/PITR retention, and KMS key lifecycle.
- [ ] `IManagedSignerConfigurationValidator` rejects raw private PEM and requires an external signer or KMS/HSM in production.

## Explicit phase gate

### Tests Before
Add tests for bundle limits, primary-only rejection, immutable license binding, nonce/counter replay, signer/KMS boundaries, trust-store rotation, Blob no-store, and redaction.

### Smallest seam
Define bundle and signer/trust-store interfaces with an external-signer test double; keep raw private PEM outside the web process.

### Implementation scope
Implement device creation, keyset generation, bundle export, license workflow, trust-store audit, revocation, and download lifecycle.

### Negative/regression tests
Reject human passwords, secondary/both/failover modes, oversize fields, changed signed binding, stale/lower generation, replayed nonce/counter, retired keys, and cache leakage.

### Phase regression gate
Provisioning/license tests, signer/KMS trust tests, redaction/no-store tests, and recovery model tests pass; production signing remains unavailable without its boundary.

### Input/output contract
Inputs are Phase 1 schemas and the Phase 2 device model; outputs are versioned provisioning artifacts, immutable license requests, trust records, and audit events.

### Dependencies
Requires Phase 1/2; Phase 5 consumes this contract but is not a prerequisite for server validation tests.

### Ownership
Phase 4 owns server bundle/license lifecycle, signer adapter, trust store, rotation, and secret download controls.

### Risk
A signer or cache boundary can expose private material or destroy the previous valid license; fail closed and preserve recovery states.

### Evidence classification
Firmware license ordering is `SOURCE_PROVEN`, recovery model is `MODEL_TESTED`, and server/KMS/hardware behavior is deferred.

### Release gate
No production signing or bundle download is enabled until signer/KMS, trust-store, lifecycle, and recovery tests pass.

### Definition of done
Bundle fields, signer boundary, trust records, immutable bindings, limits, redaction, and recovery outcomes have executable tests and owners.

## Implementation evidence appendix (2026-09-28)

The server-side slice implements primary-only bundle generation, protected
storage, hash/expiry/import-nonce metadata, one-time consumption, explicit
secondary/both-mode rejection, license trust-store and apply-journal seams,
and no-store delivery. Bundle consumption is now a CSRF-protected POST so a
link preview or cross-site GET cannot burn the one-time artifact. Production
registration fails closed unless a non-ephemeral Data Protection provider is
injected at the KMS boundary; raw private PEM and live signing remain
unavailable.

Backend provisioning/license tests and integration checks pass. A real KMS/HSM,
external signer, encrypted backup/PITR lifecycle, and firmware-side importer
remain deferred evidence gates; the plan does not claim those capabilities are
production-ready.

### Continuation evidence — license trust rotation/revocation audit (2026-09-29)

The trust store now records immutable audit rows for `Created`, `Rotated`, and
`Revoked` changes, including `kid`, issuer, previous status, new status, actor,
and timestamp. The in-memory development seam and EF implementation expose the
same read/write contract, and migration
`20260929090323_AddLicenseTrustAudits` creates `license_trust_audits` with a
`(Kid, OccurredAt)` index. License verification remains read-only and
fail-closed; no raw private key or signer behavior was added.

### Continuation evidence — trust-anchor immutability and transition validation (2026-09-30)

Trust-store mutations now share a validation helper across the in-memory and EF
implementations. Existing `kid` issuer/public-key bindings cannot be changed;
status and effective interval values are validated; rotation creates a new active
`kid`; revocation requires an existing active/retired key and a revoked target;
and audit ordering is deterministic when timestamps collide. Regression tests
cover same-kid mutation, invalid status/interval, invalid creation and
revocation transitions, and EF persistence. Production KMS/HSM, external signer,
firmware parsing/binding, and PostgreSQL concurrent trust updates remain open
release gates.

### Continuation evidence — bounded mutation inputs (2026-09-30)

License apply and provisioning creation now reject oversized or malformed
metadata at the API boundary: each mutation is capped at 64 KiB, license
envelopes are capped at 2,048 bytes, identifiers are capped at 128 characters,
broker hosts at 253 characters, broker ports are range-checked, and generation
is positive. These checks protect the server boundary only; they do not assert a
firmware license size or provisioning importer limit until HMS-IDF proves it.

### Continuation evidence - revoked-device provisioning fence (2026-09-30)

The provisioning handler rejects terminal `Revoked` devices before calling the
secret generator or persisting a bundle. Unit coverage proves no secret
generation occurs, while the management API integration test proves the
CSRF-protected endpoint returns `DEVICE_REVOKED`. The request record's
validation metadata is attached to constructor parameters so invalid requests
produce normal model-validation responses rather than a runtime exception.

### Continuation evidence - provisioning concurrency fences (2026-09-30)

`InMemoryProvisioningBundleStore` now serializes `Save` and both one-time
consumption paths, and a 64-consumer regression proves that only one caller can
consume a bundle. The EF store rechecks the persisted device state immediately
before insertion; the PostgreSQL path uses a serializable transaction and a
`FOR UPDATE` device-row fence so a revoke that commits first rejects the bundle
with `DEVICE_REVOKED`. The source-level fence and InMemory regression pass, but
a real PostgreSQL two-context race remains an external release gate. Unknown
device IDs are also rejected at this store boundary, preventing orphaned
provisioning records when the persistence port is called directly.
