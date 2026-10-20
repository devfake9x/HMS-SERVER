---
title: "HMS-SERVER contract security, database and UI review"
type: review-report
date: 2026-09-25
status: pre-cook-review
---

# Pre-cook Contract Review

This review applies the CK security, security-scan, database, backend,
frontend, and testing checklists to the current plan and firmware evidence.
There is no HMS-SERVER production source to audit yet, so implementation
findings are recorded as phase-owned gates rather than falsely marked pass.

## Security findings

| ID | Severity | STRIDE/OWASP | Finding | Required treatment |
|---|---|---|---|---|
| SEC-01 | P0 | Spoofing / A01 | Current HMS-IDF proves human permission middleware for license routes, not a device-bound importer bootstrap proof or local-only trust boundary. | Keep importer bootstrap as `NEW_FIRMWARE_CONTRACT`; Phase 5 must implement and test challenge/token, nonce, expiry, locality, CSRF and remote rejection. |
| SEC-02 | P0 | Tampering / Elevation | Active identity exposes getters and license evaluation rejects an empty/mismatched active device ID, creating a first-enrollment candidate identity seam requirement. | Phase 5 must stage candidate identity without exposing it through active getters and prove ordering before license apply. |
| SEC-03 | P1 | Spoofing / Tampering | Backend cannot trust MQTT topic/payload alone; current firmware does not prove a trusted EMQX session envelope. | Phase 1/3/8 must define and negative-test authenticated broker metadata, replay, mismatch and fail-closed behavior. |
| SEC-04 | P1 | Tampering / Replay | Command signature is raw-byte HMAC with strict field allowlist and key version fencing; reserialization or guessed fields would break authenticity. | Use golden vectors, exact-byte retry and reject `kid`/`generation` in the firmware command JSON. |
| SEC-05 | P1 | Information disclosure | Bundle/license bytes could leak through browser state, caches, logs, backups or proxy access logs. | Phase 4/7/8 must enforce no-store, redaction, transient Blob handling and KMS/backup lifecycle tests. |
| SEC-06 | P1 | Denial of service | Firmware accepts raw commands below `CMD_SIGNABLE_MAX` and has bounded queues/dedup; server retry/size policy must respect those limits. | Reject oversized commands before publish and bound server queues/inflight work. |
| SEC-07 | P1 | Elevation | Device capabilities are availability metadata and do not prove server authorization. | Server catalog plus feature/resource/action grants remain authoritative. |
| SEC-08 | P1 | Repudiation | License, provisioning, key rotation, webhook, revoke and command decisions require durable audit with redacted secrets. | Each owning phase must define audit event, actor, correlation, policy epoch and retention. |

## Database/backend review

- Keep only management/control-plane state: organization tree, memberships,
  nullable classifications, devices, enrollment/keysets, credentials, license
  workflow, catalog mapping, latest status/capabilities, commands, response
  deduplication, audit and authorization policy versions.
- Do not add telemetry tables, historical charts, TimescaleDB, Redis Streams or
  replay ingestion.
- Every device/classification/grant query needs tenant and organization-bound
  constraints; explicit deny must override inherited grants.
- Use unique constraints for device identity, active enrollment generation,
  credential version, catalog mapping and idempotency keys.
- Use optimistic concurrency/version fences for organization grants, device
  revocation, key rotation, license drafts and one-time downloads.
- Keep transaction boundaries inside PostgreSQL/EF Core. Never describe them as
  atomic across firmware NVS partitions.
- Separate vendor license, enrollment, credentials and keysets as aggregates;
  do not make a license update mutate signed bytes in place.
- API contracts must return stable error codes and never return private key,
  password, raw bundle or raw license bytes through ordinary JSON/query caches.

No database or backend implementation exists in HMS-SERVER at this stage;
these are `DEFERRED_TO_PHASE_N — REQUIRED` implementation gates.

## Frontend/web review

- Vue is a consumer of server authorization, catalog and policy epochs; it is
  never the final authority.
- DeviceRevoked must remove SignalR groups, cancel cancellable pending commands,
  and make future command attempts fail through the backend.
- Stale `authorizationEpoch` or `catalogHash` events disable actions and force
  resync; the client must not apply stale status/capability events.
- Raw bundle/license bytes must avoid Pinia, query cache, URL, logs, analytics,
  service-worker cache and ordinary error telemetry.
- Blob download must verify `Cache-Control: no-store`, content type, content
  disposition, size/hash and safe filename; object URLs are revoked on success,
  failure and unmount as appropriate.
- The UI must document that an operator-controlled filesystem copy cannot be
  erased by the server.

No frontend source exists in HMS-SERVER at this stage; these are
`DEFERRED_TO_PHASE_6/7 — REQUIRED` implementation/browser gates.

## Test strategy review

- Contract/model fixtures are valid pre-cook evidence only when their source,
  expected result and limitation are recorded.
- Actual firmware NVS crash recovery, real KMS, real EMQX ACL/session identity,
  backend integration, browser tests and 1,000-device load remain later gates.
- A test is not PASS if it was skipped because Python/ESP-IDF/.NET/frontend
  runtime was unavailable; record exact command and dependency instead.
- Every phase follows Tests Before -> smallest seam -> implementation ->
  negative/regression -> phase regression gate.
