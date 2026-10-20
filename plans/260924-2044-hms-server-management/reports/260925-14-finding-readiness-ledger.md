---
title: "HMS-SERVER accepted red-team finding ledger"
type: readiness-ledger
date: 2026-09-25
status: open-gates-mapped
---

# Accepted Finding Ledger

| ID | Sev | Evidence/decision | Owner | Artifact/test | Status | Risk/release consequence |
|---|---:|---|---|---|---|---|
| RT-01 | P0 | Human HTTP auth exists; identity API is getter-only and combined importer/setter does not exist | 1/5 | `importer-contract.test.mjs`; `identity-license-source-evidence-fixture.mjs`; actual importer tests | `SOURCE_PROVEN + CONTRACT_TESTED; DEFERRED_TO_PHASE_5 - REQUIRED` | Enrollment denied until device-bound proof/setter exists |
| RT-02 | P0 | NVS partitions cannot be treated as one transaction | 5/8 | crash matrix; power-loss tests | `MODEL_TESTED; DEFERRED_TO_PHASE_5 - REQUIRED` | No mixed-generation activation; no atomicity claim |
| RT-03 | P1 | `license_apply` can durably commit before later clock/publication failure | 4/5/8 | `identity-license-source-evidence-fixture.mjs`; `license-recovery-model.test.mjs` | `SOURCE_PROVEN + CONTRACT_TESTED + MODEL_TESTED` | Roll forward/exact resume or recovery-required; no unconditional rollback; hardware power-loss proof remains required |
| RT-04 | P1 | Firmware proves wildcard ingress, response exclusion, connected-wiring order and active reconnect backoff, but no canonical server publish leaf/ACL | 1/3 | `mqtt-topic-fixture.mjs`; selected-leaf/ACL integration fixture | `SOURCE_PROVEN + NEW_FIRMWARE_CONTRACT` | Commands disabled until server leaf/ACL fixture; runtime jitter/soak remains unclaimed |
| RT-05 | P1 | Direct raw-byte HMAC, id dedup, TTL and key-version fence | 1/3 | `command-golden-vector.mjs`; backend negative tests | `SOURCE_PROVEN + CONTRACT_TESTED` | Exact bytes required; old key retries denied |
| RT-06 | P1 | Firmware/license source exposes bounded sizes; server must preserve separate limits | 1/4/5 | limits schema and HTTP tests | `SOURCE_PROVEN + DEFERRED_TO_PHASE_4/5 - REQUIRED` | Oversize rejected before write/sign/publish |
| RT-07 | P1 | Binding immutable after signing; nonce/counter replay validation in firmware | 4/5 | license replay and mutation tests | `SOURCE_PROVEN + DEFERRED_TO_PHASE_5 - REQUIRED` | Replay/conflicting generation rejected |
| RT-08 | P1 | Capabilities export descriptor readiness, not server grants | 2/3/7 | catalog/capability denial tests; `action-source-evidence-fixture.mjs` | `SOURCE_PROVEN + CONTRACT_TESTED + DEFERRED_TO_PHASE_2/3/7 - REQUIRED` | UI/device cannot grant itself authority; static handler evidence does not replace server policy or runtime readiness |
| RT-09 | P1 | Raw MQTT callbacks do not carry authenticated publisher proof | 3/8 | broker envelope fixture | `NEW_CONTRACT_REQUIRED` | Missing/inconsistent metadata fails closed |
| RT-10 | P1 | Webhook replay/rotation is server boundary, absent in firmware | 1/3/8 | replay/expiry/overlap/rollback tests | `NEW_CONTRACT_REQUIRED` | No webhook decision on stale/forged request |
| RT-11 | P1 | Production signer must be external/KMS; raw PEM forbidden | 1/4/8 | signer/KMS trust tests | `DEFERRED_TO_PHASE_4/8 - REQUIRED` | Signing unavailable rather than private-key exposure |
| RT-12 | P1 | Bundle/download/cache/backup lifecycle needs server controls | 4/7/8 | Blob/no-store/redaction/backup tests | `DEFERRED_TO_PHASE_4/7/8 - REQUIRED` | Raw material never enters ordinary state/logs |
| RT-13 | P1 | SignalR revocation/epoch/backpressure is not firmware behavior | 3/7/8 | epoch/coalescing/drop tests | `DEFERRED_TO_PHASE_3/7/8 - REQUIRED` | Revoked UI cannot retain authority |
| RT-14 | P1 | 1,000-device target is a load contract, not measured evidence | 8 | pinned load profile + two-replica soak | `DEFERRED_TO_PHASE_8 - REQUIRED` | No HA claim until topology/budgets pass |

No finding is marked `resolved` merely because a checkbox exists. Gate A may
proceed only with explicit denied behavior for unsupported contracts and a phase
owner/test for every implementation-dependent row. Gate B remains deferred.
