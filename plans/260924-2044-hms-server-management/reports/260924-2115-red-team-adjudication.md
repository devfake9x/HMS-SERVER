# Red-team adjudication: HMS-SERVER management and control plane

Date: 2026-09-24  
Plan: `plans/260924-2044-hms-server-management/`  
Disposition: 14 findings accepted and applied; no finding rejected.

## Review scope

The review challenged the approved ASP.NET Core 10 + Vue 3 + PostgreSQL/EF Core + EMQX + SignalR control-plane plan against the verified HMS-IDF firmware contract. The review was limited to management, enrollment, licensing, MQTT control, authorization, frontend operations, and integration hardening. Telemetry ingestion, historical sensor storage, SCADA dashboards, and digital-twin behavior remain out of scope.

## Accepted findings and remediation

| # | Severity | Finding | Evidence checked | Applied remediation |
|---:|:---:|---|---|---|
| 1 | P0 | Importer route/auth and missing NVS setter APIs | `HMS-IDF/components/util-device-identity/include/device_identity.h`; `HMS-IDF/components/hms-http-api/license/license_api.c` | Phase 1 freezes importer/auth contract; Phase 5 requires a local authenticated route, CSRF, bounded body, remote rejection, explicit identity/MQTT/key write adapters, and redacted results. |
| 2 | P0 | Cross-partition atomicity was overstated | ESP-IDF NVS partition boundaries and existing storage paths | Plan, Phase 5, and Phase 8 now use staged generations, completion bits, pending state, commit marker, boot reconciliation, and the term crash consistency; they do not promise cross-partition atomicity. |
| 3 | P1 | License commit ordering and recovery were underspecified | `HMS-IDF/components/hms-license/src/license_storage.c` and `license_api.c` | Phase 4 applies license last, persists target/prior hashes and pending state, resumes only exact firmware-supported pending operations, enters `recovery-required` after unsupported post-marker failures, and tests every failure boundary; Phase 5 and Phase 8 verify recovery. |
| 4 | P1 | Firmware does not define a canonical server publish leaf/ACL even though ingress and reconnect behavior are source-pinned | `HMS-IDF/services/mqtt-service/mqtt_service.c`, `components/mqtt-manager/mqtt_manager.c`, `mqtt-status-plane/src/status_plane.c` | Phase 1 records wildcard-plus-response-exclusion ingress, connected wiring order, and active reconnect backoff; a fixture is still required to select and ACL-bind the server leaf. `/cmd/execute` remains unverified until explicitly selected and proven. |
| 5 | P1 | Retry, deduplication, TTL, and key-rotation fencing were incomplete | `HMS-IDF/components/command-dispatcher/cmd_security.c` and `cmd_security.h` | Phase 3 defines wire `id`/`ts`/`ttl_ms`, exact-byte retry, deduplication, expiry, `key_ver` fencing, and old-key rejection; Phase 4 adds the activation fence and in-flight drain tests. |
| 6 | P1 | License payload, envelope, and HTTP limits were conflated | `HMS-IDF/protobuf/proto/license.proto` and license API routes | Phase 1, 4, and 5 separate nested payload (1,536 bytes), encoded envelope/HTTP/journal (2,048 bytes), DER signature (72 bytes), key ID (32 bytes), and importer bundle (16 KiB) limits. |
| 7 | P1 | Signed license binding, nonce, and counter fields could be changed during editing | `HMS-IDF/components/hms-license/src/license_internal.h` and license journal paths | Phase 4 makes device/MAC binding, nonce, counter, signer key ID, and accepted envelope bytes immutable; edits create a new draft/signature and retain the original hash. |
| 8 | P1 | Device capabilities could be mistaken for command authority | `HMS-IDF/components/command-dispatcher/cmd_descriptor.c` | Phases 1-3, 6, and 7 make the versioned server command catalog authoritative. Capabilities only intersect availability; feature, resource, and action grants remain server-side decisions. |
| 9 | P1 | MQTT authenticated session identity was not bound to topic identity | `HMS-IDF/services/mqtt-service/mqtt_service.c` and broker ACL assumptions | Phase 3 binds username, client ID, active enrollment, and topic device ID; Phase 8 adds cross-device, stale-session, and reconnect negative tests. |
| 10 | P1 | EMQX webhook replay and secret rotation were underspecified | EMQX webhook boundary in Phase 1/3 | Phase 1 and 3 require mTLS or timestamped HMAC with nonce replay protection, rotation overlap, expiry, and proxy/header-stripping tests. |
| 11 | P1 | Managed signer trust boundary allowed raw private PEM risk | License signer adapter design and production deployment boundary | Phase 1, 4, and 8 require an external signer or KMS/HSM adapter in production and reject raw private PEM configuration; signer output is verified before persistence. |
| 12 | P1 | Bundle secrets could leak through caches, logs, backups, or key lifecycle gaps | Provisioning download and frontend Blob flows | Phase 4, 7, and 8 require `Cache-Control: no-store`, proxy/access-log redaction, backup/PITR handling, KMS key lifecycle, one-time download, transient browser Blob URLs, and leakage tests. |
| 13 | P1 | SignalR subscriptions could outlive revoked scopes | Reference `ConcreteStress.Infrastructure/Hubs/MonitoringHub.cs` and scope authorization patterns | Phases 2, 3, 6, 7, and 8 add monotonic authorization versions, server-side revalidation, `DeviceRevoked`, group removal, pending-command cancellation, reconnect reauthorization, and integration tests. |
| 14 | P1 | 1,000-device load test lacked reproducibility and budgets | Existing plan load-test outline and MQTT retained-state behavior | Phase 8 fixes one client/device, pinned Docker topology, 100 clients/s reconnect wave, 0.1 command/device/s, firmware-derived retained/QoS topic behavior, and explicit latency, error, broker-loss, CPU, memory, DB-pool, webhook, and queue budgets. |

## Consistency decisions

- The firmware command key remains the direct 32-byte `crypto/cmd_key` contract. No HKDF or `master_secret` behavior is introduced by this plan.
- Vendor license bytes and server enrollment/keysets remain separate aggregates and stores.
- The importer is a crash-consistent staged protocol. A boot reconciliation state may be `pending`, `rolled_back`, `active`, or `recovery-required`; before a durable target license marker the prior authority remains usable, while after that marker only an exact firmware-supported resume or explicit fail-closed recovery is allowed. The plan never labels mixed NVS partitions as atomically committed.
- A server-pinned command catalog is the authority for action names, payload schemas, permission metadata, and dangerous-action policy. Device capabilities are availability evidence only.
- The `/cmd/execute` publish suffix is a pending contract until a firmware fixture demonstrates acceptance. No phase may implement it from assumption alone.
- Production managed signing cannot run with a raw private PEM in the web process. The implementation must use an external signer or KMS/HSM adapter with auditable key IDs and rotation.

## Verification performed

- Re-read `plan.md`, all eight phase files, and `reports/scout-synthesis.md` after the edits.
- Checked that each accepted finding names at least one owning phase and one test or acceptance gate.
- Added the phase 8 topology/resource budget and the missing negative-test gates for identity binding, webhook replay, signer boundary, bundle lifecycle, SignalR revocation, and importer recovery.
- Replaced the stale scout wording that described importer writes as atomic NVS writes.
- Remaining validation is delegated to the plan helper's schema/dependency checks, the stale-term search recorded in the parent plan, and firmware fixture work for candidate identity/license recovery.

## Handoff condition

The plan remains conditionally validated and is not ready to cook. Implementation should begin only after the remaining firmware fixtures prove candidate identity binding, license recovery states, importer bootstrap trust, broker-session identity, command catalog drift, and exact topic behavior, or those features are explicitly denied at the server boundary. When the gates pass, use `$ck-cook` for this plan with the `--tdd` workflow preserved.
