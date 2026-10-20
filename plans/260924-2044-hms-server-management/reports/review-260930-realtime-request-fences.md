---
title: "Realtime and request-order fence review"
date: 2026-09-30
status: current
---

# Realtime and request-order fence review

## Scope

Read-only `$ck-code-review` and `$ck-security` review of the current core
control-plane paths:

- browser SignalR lifecycle and reconnect fencing;
- SignalR per-connection backlog and delivery;
- frontend section/inventory request ordering;
- command authorization/catalog/transport;
- provisioning secret lifecycle;
- license trust store and recovery boundary.

No live EMQX, firmware importer runtime, KMS/HSM, PostgreSQL two-context race,
or HMS-IDF rescan was used. Those remain separate release gates.

## Findings and fixes

| Finding | Severity | Evidence | Resolution |
|---|---:|---|---|
| Delayed reconnect rejection could restore reauthorization after logout | P1 availability | `frontend/src/useDeviceOperationsHub.ts` reconnect callback | Require current hub/lifecycle revision and active membership before applying the failure |
| Old `disconnect()` completion could overwrite a replacement connected hub | P1 availability | `frontend/src/useDeviceOperationsHub.ts` shutdown path | Guard the `finally` state write with the captured lifecycle revision and connection identity |
| Leaving a device could remove sibling queued events | P1 availability | `SignalRDeviceEventDeliveryService` backlog drain | Remove only the pending device queue entries when registry membership is gone |
| Resynchronization was connection-wide and removing one device cleared a sibling fence | P1 integrity/availability | `SignalRConnectionBacklog` | Track resynchronization by connection and device; clear only the removed device |
| Older same-session section/filter responses could overwrite newer UI state | P1 availability | `frontend/src/App.vue` section and inventory loaders | Add independent request revisions in addition to the session fence |
| Revoked devices could receive a fresh provisioning bundle | P2 security lifecycle | `CreateProvisioningBundleHandler.Handle` | Reject `Revoked` before secret generation; add unit and HTTP integration regressions |
| Backpressure counters were only test-visible properties | P2 observability | `SignalRConnectionBacklog` counters | Emit bounded `System.Diagnostics.Metrics` counters for drops, retries, and resynchronizations; host exporters remain optional |
| Development one-time bundle store could race concurrent consumers | P2 consistency | `InMemoryProvisioningBundleStore` singleton dictionary | Serialize `Save` and both `Consume` paths; add a 64-consumer regression |
| Revoke could commit between the handler's state check and bundle insert | P2 lifecycle race | `CreateProvisioningBundleHandler` plus EF bundle persistence | Recheck the device state at persistence and use a PostgreSQL row-lock/serializable fence; retain a two-context runtime gate |

## Regression evidence

- Backend unit tests: 93 passed.
- Backend integration tests: 36 passed.
- Frontend tests: 74 passed across 15 files.
- Frontend typecheck: passed.
- Frontend production build: passed; existing SignalR Rollup annotation warning only.
- Solution build: passed.
- Offline npm audit: zero vulnerabilities reported.
- Plan validation/status: passed; plan remains `in-progress` with no phase falsely completed.
- Plan helper tests: 11 passed.
- Phase gate fixture: all 8 phases, 12 required sections each, passed.
- Importer, license-recovery and broker-session model fixtures: 16 tests passed.
- Documentation drift check: passed historical-banner checks.
- EF pending-model check: no pending model changes.

## Security review result

The bounded security review found no new concrete P0/P1 defect in command
authorization/catalog/transport, license trust store/journal or production
secret-root/Data Protection boundaries. It found and closed the revoked-device
provisioning defect, the in-memory one-time-consumption race, and the
observability gap. The persistence boundary now adds a second revoked-state
check and a PostgreSQL row fence; a live two-context test is still required to
prove the cross-request ordering. This is not
an independent external review and does not replace live broker, KMS/HSM,
PostgreSQL concurrency, load or HA evidence.

## Remaining release limitations

Live EMQX identity/ACL and canonical publish leaf, firmware importer runtime and
power-loss behavior, external signer/KMS/HSM, PostgreSQL two-context runtime
proof,
reproducible 1,000-device load, two-replica soak, RTO/RPO, rolling upgrades and
production exporter/backend wiring remain deferred. MQTT stays recording/deferred and never
claims device delivery or execution without the missing contract evidence.

## Continuation review - 2026-09-30 provisioning and runtime metrics

The provisioning handler now rejects a terminal `Revoked` device before
calling the secret generator or persisting a bundle. The unit regression proves
secret generation is not reached; the management API regression proves the
CSRF-protected endpoint returns `DEVICE_REVOKED`. The same test exposed invalid
ASP.NET Core record validation metadata, which was corrected by attaching
attributes to constructor parameters.

`SignalRConnectionBacklog` retains its bounded queue semantics and now emits
three runtime `System.Diagnostics.Metrics` instruments for coalescible drops,
retry attempts, and resynchronization fences. A `MeterListener` regression
proves all three signals. Export remains host-configured; this does not claim
that a production metrics backend is deployed.

Fresh verification is 96 backend unit tests, 36 backend integration tests, and
74 frontend tests; infrastructure and isolated unit builds, frontend typecheck/build, solution build, offline npm audit,
plan validation/status/helper tests, phase gates, model fixtures, documentation
drift checks, and EF pending-model checks pass. PostgreSQL two-context,
browser-E2E, live EMQX, firmware importer runtime, KMS/HSM, load/HA, RTO/RPO
and rolling-upgrade evidence remain unproven.

The 96-unit-test count includes the concurrent in-memory bundle-consumption
regression, the EF revoked-device persistence-fence regression, and the
store-level unknown-device rejection regression. The
PostgreSQL row fence is source-reviewed but not runtime-proven because no
PostgreSQL service or two-context harness is available in this environment.
