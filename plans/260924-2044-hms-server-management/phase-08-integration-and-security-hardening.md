---
phase: 8
title: "Integration and security hardening"
status: in-progress
priority: P1
dependencies: ["7"]
---
# Phase 8: Integration and security hardening

## Overview
Prove the complete management/control plane against PostgreSQL and the Vue frontend, reconcile documentation, run adversarial checks, and measure the latest-state/control-plane workload without adding telemetry ingestion. Core integration and security hardening is required in this goal. Live EMQX/HMS-IDF integration is a separately enabled later profile and remains disabled until the canonical publish leaf and broker identity contracts are proven.

## Requirements
- Functional: Exercise create → provision → import contract → deferred command lifecycle → audit end to end. A live connect/capabilities/status/ACK path is a later broker-enabled profile.
- Functional: Exercise license request → draft → external/managed sign → apply → lifecycle update and invalid/replay paths.
- Functional: Verify parent/child tenant isolation, action grants, explicit deny, bundle one-time behavior, rotation, and revocation.
- Deferred profile: Prove authenticated MQTT session identity is bound to the device credential, client ID, and topic namespace, including reconnect and stale-session rejection, only when the later broker-enabled profile is explicitly enabled.
- Functional: Prove EMQX webhook replay protection, secret rotation overlap, signer trust-boundary failures, SignalR scope revocation, and crash-consistent importer recovery.
- Functional: Prove primary-only provisioning and explicit rejection of unsupported `SECONDARY_ONLY`, `BOTH`, and `FAILOVER` broker modes; no secondary field may be silently ignored.
- Non-functional: Run deterministic CI, security scanning, dependency checks, database migration checks, broker ACL checks, and bounded 1,000-device load tests.
- Non-functional: Update superseded HMS-SERVER documents so future implementation work does not reintroduce telemetry-centric or HKDF assumptions.

## Architecture
Use a pinned Docker Compose profile with one application instance, PostgreSQL, and a pinned EMQX image for CI; a separate repeatable two-replica soak profile may add broker/app replicas. The harness records image digests, CPU/RAM limits, client count, database pool size, broker inflight/keepalive/session-expiry settings, reconnect jitter/backoff, webhook auth QPS/cache behavior, worker/channel bounds, SignalR queue bounds, and test seed. Seed organizations, users, devices, credentials, capabilities, keysets, and grants. A simulator or test firmware client publishes status/capabilities/responses and validates command signatures.

The load test models exactly 1,000 devices with one MQTT client per device, a documented reconnect wave of 100 clients/second with jitter, and a bounded command rate of 0.1 command/device/second (100 commands/second). Retained-message count, QoS, and retain flags are taken from the firmware-proven topic fixture; the test must measure retained churn on reconnect/readiness rather than assume three messages/device. Command responses and audit messages are non-retained unless the fixture proves otherwise, and no telemetry is generated or stored. The capacity model records EMQX webhook authentication QPS and cache hit ratio, broker inflight/keepalive/session expiry, PostgreSQL latest-state upsert latency and pool utilization, application worker/channel depth, SignalR per-connection queue depth/coalescing/drop counts, and reconnect authorization latency. Acceptance budgets are: initial connection/auth success >=99.9% within 120 seconds, status/capability processing p95 <=2 seconds and p99 <=5 seconds, command ACK round-trip p95 <=3 seconds and p99 <=5 seconds, application error/retry rate <1%, broker message loss <0.1%, application CPU <70% and RSS <1 GiB at steady state, database pool utilization <80%, queue memory bounded, and all reconnecting clients authorized and state-republished within 60 seconds. Security tests include malformed MQTT auth, client/topic identity mismatch, topic escape attempts, replayed webhook requests, oversized commands, stale generations, key-rotation races, replayed bundles, invalid licenses, signer/KMS outages, and unauthorized SignalR group joins/revocations.

Production HA is not claimed until the two-replica soak passes with shared PostgreSQL authority, compatible SignalR/session behavior, EMQX persistence/session-expiry settings, TLS certificate rotation, and documented RTO/RPO. The release must prove PostgreSQL backup/restore and server version N+1 reading version N bundles/contracts; multi-region active-active remains out of scope.

## Related Code Files
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/tests/Hms.Server.IntegrationTests/Infrastructure/HmsWebApplicationFactory.cs`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/tests/Hms.Server.IntegrationTests/Mqtt/EmqxAclIntegrationTests.cs`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/tests/Hms.Server.IntegrationTests/Flows/DeviceLifecycleFlowTests.cs`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/tests/Hms.Server.IntegrationTests/Flows/LicenseLifecycleFlowTests.cs`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/tests/Hms.Server.LoadTests/DeviceFanInScenario.cs`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/docker-compose.yml`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/infra/emqx/*`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/scripts/verify-contract-drift.ps1`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/scripts/run-integration-tests.ps1`
- Modify: `D:/WORKSPACE/PROJECT/HMS-SERVER/ALIGNMENT.md`
- Modify: `D:/WORKSPACE/PROJECT/HMS-SERVER/docs/architecture-hms-server.md`
- Modify: `D:/WORKSPACE/PROJECT/HMS-SERVER/research-report.md`
- Modify: `D:/WORKSPACE/PROJECT/HMS-SERVER/brainstorm-report.md`
- Reference: `D:/WORKSPACE/PROJECT/concrete-stress-monitoring/sources/backend/tests/ConcreteStress.IntegrationTests/ConcreteStressWebAppFactory.cs`
- Reference: `D:/WORKSPACE/PROJECT/HMS-IDF/test-suite/tests/test_mqtt_command_coverage.py`
- Reference: `D:/WORKSPACE/PROJECT/HMS-IDF/tools/license-tool/build_fixtures.py`

## Implementation Steps
1. **Tests Before**: Write end-to-end scenarios and failure injections before wiring the final stack. Include tenant isolation, ACL, primary-only mode rejection, authenticated-session identity binding, webhook replay/rotation, lifecycle, license commit recovery, bundle replay/bootstrap trust, key rotation fencing, command ACK/timeout/dedup, SignalR authorization epoch/revocation, importer power-loss recovery, catalog drift, backup/restore, and N-to-N+1 compatibility.
2. Build the isolated PostgreSQL/EMQX test stack with disposable credentials and health checks.
3. Implement the lifecycle flow harness with a simulator/test fixture that validates the command HMAC and emits realistic status/capability/response messages.
4. Run integration tests for normal and failure paths, including broker reconnect, database transaction rollback, license commit ordering/recovery, and crash-consistent importer generation recovery.
5. Run the reproducible 1,000-device scenario with the topology, firmware-derived retained/QoS contract, reconnect wave/jitter, command rate, webhook QPS, broker inflight, queue bounds, and budgets above; capture connection count, message latency, command latency, webhook QPS/cache hit ratio, memory, CPU, broker drops, DB query timings, pool utilization, queue depth/drop counts, and failed authorization counts. Fail the gate when any budget is exceeded.
6. Run static security checks, dependency audit, secret scan, migration drift check, contract drift check, signer/KMS boundary checks, bundle cache/proxy-log/backup checks, and frontend/backend build gates.
7. Reconcile all old documentation with the approved control-plane scope. Explicitly mark old telemetry/HKDF claims as superseded and fail the drift script on `atomic` cross-partition claims, capabilities-as-authority wording, unverified `/cmd/execute` claims, production private-PEM signer configuration, unsupported secondary-broker assumptions, or missing importer bootstrap trust.
8. **Tests After**: Repeat the full suite from a clean checkout and record evidence before recommending implementation handoff.

## Success Criteria
- [ ] Clean-stack end-to-end lifecycle passes with audit records at every sensitive transition.
- [ ] EMQX ACL isolation and webhook failure behavior are verified against a real broker.
- [ ] Invalid/replayed/oversized commands and bundles are rejected without side effects.
- [ ] License invalid signature, wrong binding, replay, expiry, remove, and signer failure paths are covered.
- [ ] MQTT session identity, webhook replay/rotation, command dedup/TTL/key-generation fencing, and SignalR revocation are verified with negative tests.
- [ ] Primary-only broker provisioning rejects unsupported secondary modes and never silently discards secondary configuration.
- [ ] License commit ordering and importer crash recovery preserve the last valid authority before a durable target marker, resume only exact-pending firmware-supported operations after that marker, and otherwise enter `recovery-required` without claiming cross-partition atomicity.
- [ ] 1,000-device status fan-in and bounded command traffic meet the documented latency, error, broker-loss, CPU, memory, pool-utilization, webhook-QPS, broker-inflight, queue-depth, and reconnect-jitter budgets using firmware-derived retained/QoS behavior.
- [ ] Two-replica soak, PostgreSQL backup/restore, TLS rotation, EMQX session expiry, RTO/RPO evidence, and N-to-N+1 bundle/contract compatibility pass before HA is claimed.
- [ ] Bundle downloads are `no-store`, absent from proxy/access logs and backups, and protected by the documented KMS lifecycle.
- [ ] Production managed signing fails closed without an external signer or KMS/HSM; raw private PEM configuration is rejected.
- [ ] No telemetry table, worker, or dashboard is accidentally introduced.
- [ ] Documentation no longer recommends HKDF/master-secret or telemetry infrastructure for this scope.

## Risk Assessment
- **Risk**: integration test passes with a permissive broker setup. **Mitigation**: assert negative ACL operations against a real EMQX instance.
- **Risk**: load test hides database/query leaks. **Mitigation**: inspect query count, indexes, pool utilization, and latest-state upsert timings.
- **Risk**: documentation drift reintroduces rejected assumptions. **Mitigation**: contract drift script and explicit document reconciliation checklist.
- **Risk**: cross-repository releases become unsynchronized. **Mitigation**: record compatible bundle/contract versions and require importer/server version checks.
- **Risk**: a realistic-looking load test passes while topology or resource limits differ between runs. **Mitigation**: pin images/digests, client behavior, reconnect wave, retained cardinality, resource limits, seed, and pass/fail budgets in the load profile and archive raw metrics.
- **Risk**: revocation or signer failures are only checked through happy-path UI flows. **Mitigation**: inject server-side authorization revalidation, KMS outage, webhook replay, and backup/log inspection failures in integration tests.
- **Risk**: one-instance load passes while webhook, broker, database, or SignalR bottlenecks fail under reconnect bursts or horizontal scale. **Mitigation**: record a capacity model, bound queues, coalesce latest state, add reconnect jitter, and run a two-replica soak before claiming HA.

## File inventory and test matrix

| Gate | Evidence |
| --- | --- |
| Clean lifecycle | integration test report and audit assertions |
| Broker security | EMQX ACL allow/deny matrix |
| Authorization | API/SignalR denial tests for every layer |
| Provisioning | one-time/replay/rotate/revoke report |
| License | protobuf/signature/binding/lifecycle report |
| Firmware | importer fixture and device/simulator result |
| Load | 1,000-device latency/error/resource report |
| Identity binding | credential/client/topic mismatch and stale-session denial report |
| Webhook hardening | replay rejection, rotation overlap, and secret/header stripping report |
| Import recovery | power-loss matrix, generation journal, and last-valid-generation report |
| Signer/bundle | external/KMS-only signer and no-store/cache/log/backup evidence |
| Capacity | webhook QPS/cache, broker inflight/session, DB pool, worker/channel depth, SignalR queues/drops, reconnect jitter |
| Recovery/compatibility | RTO/RPO, backup/restore, TLS rotation, EMQX session expiry, N-to-N+1 bundle/contract read |
| Documentation | drift scan and updated links |

## Dependency map

- Blocked by every preceding phase and by a runnable firmware importer/simulator path.
- Final gate for the whole plan.
- Produces evidence for the implementation handoff; it does not expand scope into telemetry.

## Function/interface checklist

- [ ] `HmsWebApplicationFactory` seeds isolated test tenants and disposable secrets.
- [ ] `EmqxAclIntegrationTests` proves both positive and negative topic operations.
- [ ] `DeviceLifecycleFlowTests` validates the complete status/capability/command/audit path.
- [ ] `LicenseLifecycleFlowTests` validates signer adapter and firmware-compatible bytes.
- [ ] `DeviceFanInScenario` uses one client/device, fixed reconnect jitter and command rates, firmware-derived retained/QoS behavior, and asserts all documented budgets.
- [ ] `MqttIdentityBindingTests` rejects valid credentials on another client ID/topic namespace and stale sessions after rotation.
- [ ] `EmqxWebhookReplayTests` rejects duplicate/expired requests and verifies key rotation plus proxy header stripping.
- [ ] `CommandReliabilityTests` verifies correlation deduplication, TTL expiry, and key-generation fencing.
- [ ] `SignalRRevocationTests` force a scope revocation and prove existing connections lose access on the next server-side authorization check.
- [ ] `ImporterCrashRecoveryTests` inject power loss after each partition write and marker, proves previous authority before a durable target license marker, exact-pending resume after the marker, and `recovery-required` for unsupported post-marker failure; it never asserts unconditional rollback of a committed license.
- [ ] `SignerAndBundleHardeningTests` reject raw private PEM production configuration and inspect cache, proxy-log, backup, and KMS lifecycle controls.
- [ ] `PrimaryOnlyProvisioningTests` reject unsupported secondary broker modes without dropping configuration.
- [ ] `CapacityModelTests` assert webhook QPS/cache, broker inflight/session expiry, DB pool, worker/channel bounds, SignalR queue/drop metrics, and reconnect jitter.
- [ ] `TwoReplicaSoakTests` verify shared authority, rolling compatibility, backup/restore, TLS rotation, and RTO/RPO evidence before HA is claimed.
- [ ] Contract/document drift scripts fail on stale topic, key derivation, or telemetry claims.

## Explicit phase gate

### Tests Before
Define the integrated acceptance suite before wiring services: firmware fixtures, backend/frontend contracts, live EMQX identity/ACL, importer crash recovery, signer/KMS, load, backup/restore, rolling upgrade, and documentation drift.

### Smallest seam
Use one-instance CI acceptance plus a separate two-replica soak harness; version topology, budgets, RTO/RPO, and compatibility inputs in the load profile.

### Implementation scope
Integrate phase outputs, harden trust boundaries, run reproducible 1,000-device load, verify recovery, and document the exact HA claim supported by evidence.

### Negative/regression tests
Exercise identity mismatch, replay/rotation rollback, stale epochs/catalogs, importer power loss, signer outage, DB/broker backpressure, reconnect storms, dropped events, backup restore, and rolling N-to-N+1 compatibility.

### Phase regression gate
Integration suite, load profile, resource/error budgets, RTO/RPO evidence, two-replica soak if HA is claimed, security scan, and documentation drift scan pass together.

### Input/output contract
Inputs are earlier phase contracts and implementation artifacts plus pinned firmware/build manifests; outputs are release evidence, runbooks, measured budgets, and a release decision.

### Dependencies
Requires all earlier phases and their implementation tests; missing firmware/infrastructure evidence keeps the capability disabled.

### Ownership
Phase 8 owns cross-system acceptance, security hardening, load/soak, recovery, upgrade compatibility, and final documentation reconciliation.

### Risk
A passing single-instance test can hide HA, reconnect, queue, or recovery failure; no HA claim is allowed without two-replica evidence.

### Evidence classification
Pre-cook fixtures are `CONTRACT_TESTED`; load, HA, recovery, and deployed security are `DEFERRED_TO_PHASE_8 - REQUIRED` until measured.

### Release gate
Release requires all P0/P1 findings resolved or explicitly documented as denied/unsupported release limitations, with RTO/RPO and rolling-upgrade evidence.

### Definition of done
Integrated tests, measured budgets, recovery/upgrade evidence, security results, and documentation drift checks support the exact claimed scope.

## Implementation evidence appendix (2026-09-28)

Core hardening completed in this iteration includes reserved-role protection,
target-user/resource scope checks, wildcard device-action deny precedence,
CSRF protection for command mutations and bundle downloads, atomic command
reservation at the application/EF seam with request fingerprints, persistent
device authorization epochs for PostgreSQL, stale-event delivery filtering,
and fail-closed production Data Protection provider registration.

The one-instance backend/frontend suites pass (35 unit, 17 integration, 23
frontend tests). Live EMQX identity/ACL, firmware importer crash recovery,
real KMS/HSM and external signer, browser SignalR delivery, 1,000-device load,
two-replica soak, RTO/RPO, and rolling-upgrade evidence remain explicit release
limitations. Phase 8 stays `in-progress` and must not be marked ready based on
these contract tests alone. The SignalR bridge is best-effort and bounded: it
revalidates epochs at drain time, but durable delivery, connection registry,
revoke-driven group removal, reconnect integration, and two-replica behavior
remain open gates. Stale `IN_FLIGHT` command reservations use a five-minute
lease and fail closed as `IDEMPOTENCY_OUTCOME_UNKNOWN`; they are never
automatically reclaimed because the transport outcome is unknowable after a
process crash.

### Continuation evidence — pending command terminalization on revoke (2026-09-29)

The revoke lifecycle now closes unfinished command audit rows in the same
application operation that changes device state and advances the authorization
epoch. One unit test and one EF persistence test cover the invariant. The full
solution regression is 72 unit tests and 28 integration tests. This does not
prove distributed cancellation or transport rollback; a future live MQTT
profile must reconcile any possible external side effect separately.

### Historical continuation evidence — P1 hardening verification (2026-09-30; superseded)

The command revoke fence now has a relational conditional-write path, an
in-memory fail-closed fallback, and a no-reservation regression test. The trust
store rejects same-`kid` key/issuer mutation and invalid status, interval, and
transition requests; audit ordering is deterministic. Identity principal refresh
has fail-closed exception coverage and DI wiring coverage. Verification passes
75 unit tests, 30 integration tests, 38 frontend tests, typecheck, production
build, plan validation/status, 11 plan helper tests, and stored contract
fixtures. PostgreSQL race/concurrency, live EMQX, firmware importer runtime,
KMS/HSM, browser SignalR, 1,000-device load, two-replica soak, RTO/RPO, and
rolling upgrade remain release limitations.

### Historical continuation evidence — current-source regression gate (2026-09-30; superseded)

The current source rebuild passes 80 backend unit tests and 30 backend
integration tests. The frontend passes 40 tests, typecheck, production build,
and offline npm audit with no reported vulnerabilities. Plan validation/status,
11 helper tests, all eight phase gates, importer/license/broker model fixtures,
catalog/source evidence fixtures, and the EF pending-model check also pass.

The command actor fence and HTTP body/field limits are covered by regression
tests. These checks prove server boundary behavior only; they do not prove
live broker, firmware, KMS/HSM, PostgreSQL two-context, browser SignalR,
1,000-device, two-replica, RTO/RPO, or rolling-upgrade behavior. The current
catalog source/build comparison remains `89/94` string-set drift, so unknown
or changed catalog compatibility stays denied.

### Current continuation evidence - 2026-09-30 core closure

The current core implementation closes the HTTP authorization, session-fence,
per-connection SignalR, browser SignalR, and classification CRUD integration
findings. Fresh evidence is 96 backend unit tests, 36 backend integration
tests, and 74 frontend tests, plus frontend typecheck/build, solution build,
offline npm audit with zero findings, plan validation/status, phase-gate
fixtures, importer/license/broker model fixtures, catalog/source fixtures, and
the EF pending-model check. SignalR queue/retry/resynchronization counters now
emit runtime `System.Diagnostics.Metrics` instruments; production exporter
deployment remains an external gate.

This phase remains `in-progress` because release evidence is intentionally
deferred: live EMQX/ACL and canonical publish leaf, firmware importer runtime
and power-loss behavior, PostgreSQL two-context concurrency, external
signer/KMS/HSM, 1,000-device load, two-replica soak, RTO/RPO, rolling upgrade,
and production metrics. None of these limitations authorizes invented MQTT or
firmware behavior.

### Current continuation evidence - request/realtime race closure (2026-09-30)

Code review identified and regression-tested five core state races: stale
reconnect rejection after logout, old disconnect clobbering a replacement hub,
whole-connection backlog loss after an explicit leave, sibling resynchronization
fence loss, and same-session stale section/inventory responses. The fixes are
limited to current hub/lifecycle guards, per-device backlog fencing/removal and
request revision checks. They add no MQTT or firmware behavior.

The current source passes 96 backend unit tests, 36 backend integration tests,
and 74 frontend tests, with isolated solution build, frontend typecheck/build, offline
npm audit, plan validation/status, helper tests, documentation drift and EF
pending-model checks passing. Live broker/device, importer, KMS/HSM,
PostgreSQL concurrency, load/HA, RTO/RPO and rolling-upgrade evidence remains
deferred as documented release limitation.

### Current continuation evidence - revoked provisioning and runtime metrics (2026-09-30)

Provisioning rejects terminal `Revoked` devices before secret generation and
bundle persistence. Unit coverage proves the generator is not called; the HTTP
integration test proves the CSRF-protected endpoint returns `DEVICE_REVOKED`.
The same boundary test corrected constructor-parameter validation metadata on
the provisioning request record, preventing an ASP.NET Core validation
exception. SignalR backlog pressure now emits bounded drop/retry/resync metrics
with a `MeterListener` regression. No MQTT or firmware behavior was added.

### Current continuation evidence - provisioning concurrency fences (2026-09-30)

The development singleton bundle store now protects one-time consumption with
one lock across save and consume operations; the concurrent regression passes
with exactly one successful consumer. EF persistence rechecks the device
lifecycle at the insert boundary and, for PostgreSQL, locks the device row in a
serializable transaction before saving, and rejects unknown device IDs at the
store boundary. This closes the source-level revoke /
provisioning ordering gap; a real two-context PostgreSQL race, production
database metrics, and HA behavior remain release gates.
