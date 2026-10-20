---
title: "HMS-SERVER TDD acceptance matrix"
type: test-matrix
date: 2026-09-25
status: pre-cook
---

# TDD Acceptance Matrix

| Phase | Tests Before | Smallest seam | Negative/regression gate | Evidence status before cook |
|---:|---|---|---|---|
| 1 | schemas, golden HMAC, signed-header 1,023-byte boundary, framed-command 131,072-byte/body allowlist, topic/catalog drift, bootstrap envelope, KMS config | contracts and fixtures only | unknown fields/hash, unsupported broker/import mode, body attached to non-body action | `CONTRACT_TESTED` where fixtures run |
| 2 | hierarchy, tenant, deny precedence, nullable classification, keyset fixture | domain/EF test seam | cross-tenant IDs, capability authority, stale policy epoch | `DEFERRED_TO_PHASE_2 - REQUIRED` |
| 3 | ACL, webhook replay/rotation, identity envelope, HMAC/signed-header and framed-body size, SignalR queue | deterministic keyset and broker adapter | mismatch, forged metadata, duplicate response, stale key, oversized/misattached body | `DEFERRED_TO_PHASE_3 - REQUIRED` |
| 4 | bundle field limits, signer/trust store, one-time download, license lifecycle | external signer adapter interface | raw PEM, oversize, binding mutation, replay | `DEFERRED_TO_PHASE_4 - REQUIRED` |
| 5 | importer state machine and crash matrix | candidate identity/importer seam | remote caller, nonce replay, power loss every write/marker | `MODEL_TESTED; DEFERRED_TO_PHASE_5 - REQUIRED` |
| 6 | org/device/user/grant screens and backend denial | Vue REST contract | cross-tenant, explicit deny, stale policy | `DEFERRED_TO_PHASE_6 - REQUIRED` |
| 7 | Blob/no-store, epoch/revoke, stale catalog and command UX | SignalR/REST composables | raw secret in state/logs, revoke race, backend 403 | `DEFERRED_TO_PHASE_7 - REQUIRED` |
| 8 | integration, security, drift, load and recovery scenarios | one-instance compose harness | two replicas, backup restore, N+1 rolling compatibility | `DEFERRED_TO_PHASE_8 - REQUIRED` |

The test order is mandatory: failing test -> smallest seam -> implementation ->
negative/regression -> phase regression gate. A skipped runtime-dependent test is
`NOT_RUN` or `DEFERRED_TO_PHASE_N - REQUIRED`, never pass.
