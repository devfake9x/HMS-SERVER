---
title: "HMS-SERVER final pre-cook readiness audit"
type: readiness-audit
date: 2026-09-25
status: NOT_READY_TO_COOK
gateA: incomplete
gateB: deferred
---

# Final Pre-Cook Readiness Audit

This audit evaluates the current workspace after the firmware re-scout,
contract fixture pass, plan edits, and consistency validation. It does not treat
plan wording, a self-authored report, a model test, or a static search as
production/runtime proof.

## Decision

`NOT_READY_TO_COOK`

The design is internally consistent enough to continue contract work, but Gate
A is not complete. P0/P1 trust and compatibility boundaries still depend on
firmware/EMQX/runtime evidence or an explicit product-owner acceptance of a
release limitation. No new limitation is self-accepted here.

## Latest CK verification

The current CK brainstorm re-activation updated Phase 1's evidence inventory
and reran the complete pre-cook sweep. All 17 runnable fixture/model checks,
the 11 `plan-ops` helper tests, `plan-ops validate/status`, and the
documentation drift check passed. This confirms plan/fixture consistency only;
it does not close the firmware, broker, importer, runtime, or implementation
gates listed below.

## Gate A audit

| Requirement group | Current evidence | Test/command | Status | Release consequence |
|---|---|---|---|---|
| CK workflow | Activation log records deliberate CK skill application; visible problem-first report exists | `260925-ck-activation-log.md`; `260925-ck-brainstorm-visible-pre-cook.md` | `CONTRACT_TESTED` | No cook in this goal |
| Plan structure | `plan.md` and 8 phases are internally valid; all phases have 12 required sections | `plan-ops validate`; `phase-gate-fixture.mjs` | `CONTRACT_TESTED` | Phases remain pending until implementation |
| Shared contract artifacts | MQTT topics/envelope/capabilities, device bundle, catalog, importer bootstrap, trust store, load/recovery contract and pinned license proto exist with recorded hashes | `contract-artifacts-fixture.mjs`; `verify-license-schema.ps1` | `CONTRACT_TESTED` | Full JSON Schema/runtime validation remains phase-owned |
| Firmware re-scout | Current HMS-IDF paths, source anchors and file digests recorded; repository commit unavailable in sandbox | `260925-hms-idf-contract-rescout.md`; source fixtures | `SOURCE_PROVEN` with freshness limitation | Re-scout before any future contract change |
| Command wire security | Direct 32-byte HMAC, allowlist, final `sig`, required `id`/`ts`, TTL, skew, dedup and key fence | `command-golden-vector.mjs` | `SOURCE_PROVEN + CONTRACT_TESTED` | Backend must reject unsupported envelope fields |
| Framed command path | Header 1,023 bytes; whole frame 131,072 bytes; 15s idle; only two body actions | `framed-command-fixture.mjs` | `SOURCE_PROVEN + CONTRACT_TESTED` | No body on other actions; device fragmentation still deferred |
| Firmware MQTT topics | Subscription and response/status/capabilities/audit QoS/retain map, wildcard exclusion, reconnect order/backoff, and separation of generic outbound transport from command response wiring | `mqtt-topic-fixture.mjs`; `mqtt-publish-path-evidence-fixture.mjs` | `SOURCE_PROVEN + CONTRACT_TESTED` | Runtime timing/jitter, broker soak and canonical server leaf/ACL remain open |
| Canonical server publish leaf/ACL | Firmware accepts wildcard ingress but selects no canonical server leaf | `mqtt-topic-fixture.mjs`; selected-leaf integration fixture missing | `NEW_FIRMWARE_CONTRACT` | Commands fail closed as `CONTRACT_UNAVAILABLE` |
| Command catalog | 94 descriptor rows, hash, registration, descriptor policy, static handler branch/field references | catalog/registration/command/action-source fixtures | `CONTRACT_TESTED` but compatibility incomplete | Unknown or cross-revision hashes deny commands |
| Catalog cross-revision proof | Older object has 89/94 action strings and lacks five current actions | `catalog-build-artifact-fixture.mjs` | `DRIFT_DETECTED` | No cross-revision execution until reproducible manifest exists |
| Broker publisher identity | Schema/model rejects missing, expired, replayed and inconsistent metadata | `broker-session-envelope.test.mjs` | `CONTRACT_TESTED`; live EMQX deferred | Raw topic/payload callbacks cannot establish identity |
| Webhook auth/rotation | Model covers expiry, replay, overlap and retired-key denial | broker envelope model tests | `CONTRACT_TESTED`; live EMQX deferred | No production webhook decision until integration proof |
| Identity API | Getter-only exported identity surface; active ID is `enroll/device_info/device_id`; no setter in selected source | `identity-license-source-evidence-fixture.mjs` | `SOURCE_PROVEN + CONTRACT_TESTED` | First enrollment denied until candidate setter/importer exists |
| License journal | Exact-pending bytes, replay validation, two-slot marker/sequence checks, commit-before-finalize/publication ordering | identity/license source fixture; license model | `SOURCE_PROVEN + CONTRACT_TESTED + MODEL_TESTED` | Hardware/simulator crash proof still required |
| Importer trust | Device-bound challenge, nonce, expiry, locality, CSRF, bounded body and redaction are specified as new contract; current firmware only proves human session auth and license-route body bounds | `importer-contract.test.mjs`; `http-importer-trust-source-evidence-fixture.mjs` | `SOURCE_PROVEN + MODEL_TESTED/CONTRACT_TESTED`; firmware implementation missing | Enrollment feature remains denied |
| Database/authorization | Scope, hierarchy, three permission layers and no-telemetry boundary are specified | API/DB/UI contract report | `DESIGN_CONTRACTED`; no production implementation | Backend must remain final authority |
| Frontend/download/realtime | Blob/no-store, object URL revocation, stale epoch/catalog behavior and DeviceRevoked rules are specified | TDD acceptance matrix; browser tests not present | `DESIGN_CONTRACTED`; implementation deferred | No frontend readiness claim |
| SignalR/backpressure | Epoch/policyVersion, revalidation, coalescing and bounded queues are specified | phase contract/tests not runtime | `DESIGN_CONTRACTED`; implementation deferred | No HA/realtime claim |
| KMS/signer/trust store | External signer/KMS boundary and trust-store fields are specified; raw PEM forbidden | security review/report; no live KMS | `DESIGN_CONTRACTED`; implementation deferred | Signing fails closed until production adapter tests |
| Load/operations | 1,000-device profile, budgets, RTO/RPO and two-replica conditions are specified | load/recovery contract; no runtime load | `DESIGN_CONTRACTED`; measured result missing | No HA or capacity claim |
| Documentation drift | Legacy entry points are explicitly historical/superseded | `documentation-drift-check.mjs` | `CONTRACT_TESTED` | Phase 8 must scan generated docs after cook |
| Shared contract artifact set | MQTT topics/envelope/capabilities, device bundle, catalog, importer bootstrap, trust store, load/recovery contract and byte-exact license proto are present | `contract-artifacts-fixture.mjs`; `verify-license-schema.ps1` | `CONTRACT_TESTED` | Full external schema validation and runtime integration remain deferred |
| Red-team findings | All 14 accepted findings have owner, artifact/test, status and consequence | `260925-14-finding-readiness-ledger.md`; `plan-ops red-team` | `MAPPED`; P0/P1 gates remain open | Cannot call findings resolved from checkboxes |

## Open P0/P1 gates

1. Canonical server command publish leaf and EMQX ACL integration trace.
2. Trusted EMQX session envelope propagation in a live broker path.
3. Device-bound importer bootstrap and local-only trust on firmware.
4. Candidate identity setter/validation/activation seam in HMS-IDF.
5. Power-loss tests after every partition write and activation/marker write.
6. Exact `license_apply()` recovery on firmware/simulator/hardware, including
   post-marker failure and recovery-required behavior.
7. Reproducible firmware build manifest/catalog across revisions.
8. Complete 94-row action payload/response/type/requiredness/build-condition/
   side-effect mapping; static branch references are not sufficient.
9. Production KMS/signer/Data Protection integration and rotation/recovery.
10. Backend and frontend implementation plus authorization regression tests.
11. Reproducible 1,000-device load run with measured budgets.
12. Two-replica soak, failover and rolling-upgrade proof if HA is claimed.
13. Runtime reconnect/jitter/retained-message soak against the selected broker.

## Gate B classification

The following are intentionally `DEFERRED_TO_PHASE_N - REQUIRED` and are not
pre-cook passes: actual firmware importer/NVS tests, backend integration tests,
frontend/browser tests, negative MQTT ACL tests, live EMQX envelope tests, live
KMS/signer tests, load/HA tests, backup/restore, rolling upgrade and final
documentation drift after implementation.

## Scope audit

No telemetry tables, TimescaleDB, Redis Streams, Kafka, SCADA dashboard, digital
twin, historical charts, OTA artifact distribution, browser-direct MQTT,
client-side command signing, or multi-region active-active behavior was added to
the current plan or new fixtures. Legacy documents containing those terms are
marked historical/superseded.

## Handoff decision

Do not invoke `ck-cook`. Continue closing the open gates or obtain an explicit
product-owner decision accepting a documented release limitation. Re-run this
audit, `plan-ops validate/status/red-team`, all pre-cook fixtures, and the final
scope/drift scan before any implementation handoff.
