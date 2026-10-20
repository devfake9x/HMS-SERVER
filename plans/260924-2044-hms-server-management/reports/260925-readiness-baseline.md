---
title: "HMS-SERVER readiness baseline"
type: readiness-ledger
date: 2026-09-25
status: not-ready-to-cook
---

# Readiness Baseline

The requirement-group audit for this baseline is maintained in
`reports/260925-final-readiness-audit.md`; this file remains the command/test
ledger and Gate A blocker inventory.

## Current status

`NOT_READY_TO_COOK` remains the correct status. This goal has not invoked
`ck-cook`, and no production backend, frontend, or firmware source has been
created by this goal.

## CK activation and evidence

| Skill/workflow | Scope | Result |
|---|---|---|
| `ck-brainstorm` | Problem-first prompt and pre-cook gate design | Applied; report at `plans/reports/260925-ck-brainstorm-goal-readiness-prompt.md` |
| `ck-scout` | HMS-IDF call paths and HMS-SERVER document inventory | Applied read-only; current source report at `reports/260925-hms-idf-contract-rescout.md` |
| `ck-plan` | Plan validation, status, helper tests, and plan update | Applied; validation passed, status remains pending, helper tests passed |
| `ck-project-organization` | Report and artifact placement | Applied by using plan-scoped reports, `contracts/`, `plans/reports/`, `docs/decisions/`, and `docs/journals/` |
| `ck-journal` | Closing technical record | Applied; `docs/journals/260925-hms-server-readiness.md` |
| `ck-security`, `ck-databases`, `ck-backend-development`, `ck-frontend-development`, `ck-test`, `ck-web-testing` | Review gates | Contract requirements are in the plan; production implementation tests are deferred to owning phases |

## Commands executed

| Command | Result |
|---|---|
| `node C:/Users/toann/.codex/skills/ck-plan/scripts/plan-ops.cjs validate D:/WORKSPACE/PROJECT/HMS-SERVER/plans/260924-2044-hms-server-management --json` | PASS; valid true; no errors/warnings |
| `node C:/Users/toann/.codex/skills/ck-plan/scripts/plan-ops.cjs status D:/WORKSPACE/PROJECT/HMS-SERVER/plans/260924-2044-hms-server-management/plan.md --json` | PASS; 8 pending, 0 completed, 0 in progress |
| `node --test C:/Users/toann/.codex/skills/ck-plan/scripts/plan-ops.test.cjs` | PASS; 11 passed, 0 failed |
| `node C:/Users/toann/.codex/skills/ck-plan/scripts/plan-ops.cjs red-team D:/WORKSPACE/PROJECT/HMS-SERVER/plans/260924-2044-hms-server-management --json` | PASS validation; static warning about machine-specific paths/secret-like values. Paths are required by the user-provided local plan and contain no credentials; redact before external sharing. |
| `Get-Command node,python,py,idf.py,clangd` | Only `node.exe` was available in the current PATH |
| `git -C D:/WORKSPACE/PROJECT/HMS-IDF rev-parse HEAD` | NOT_RUN; Git refused with dubious ownership and suggested a global safe-directory exception. No global config was changed and no commit is claimed. |
| `node fixtures/catalog-fixture.mjs D:/WORKSPACE/PROJECT/HMS-IDF` | PASS; 94 descriptor actions extracted; catalog hash `dbda8317758e5f25c5a4dee8456841ef3689fea4707cf5a27c30ffad656f78d9` |
| `node --test fixtures/license-recovery-model.test.mjs` | PASS; 5 model tests; model evidence only, not actual NVS/importer proof |
| `node fixtures/command-golden-vector.mjs D:/WORKSPACE/PROJECT/HMS-IDF` | PASS; direct HMAC vector, strict field/size/TTL source anchors; not an on-device test |
| `node fixtures/catalog-drift-fixture.mjs fixtures/catalog-baseline.json D:/WORKSPACE/PROJECT/HMS-IDF/components/command-dispatcher/cmd_descriptor.c` | PASS; current snapshot matches and synthetic drift is detected; second build manifest still required |
| `node fixtures/handler-registration-fixture.mjs D:/WORKSPACE/PROJECT/HMS-IDF` | PASS; all 94 rows map to a registered namespace handler and contain an action literal; payload/build/runtime/side-effect mapping remains required |
| `node fixtures/action-source-evidence-fixture.mjs D:/WORKSPACE/PROJECT/HMS-IDF` | PASS; all 94 rows have a source handler branch, handler digest, direct field-reference and helper-call record; types/requiredness/complete schemas/build conditions/side effects/runtime remain deferred |
| `node fixtures/catalog-build-artifact-fixture.mjs D:/WORKSPACE/PROJECT/HMS-IDF build` | PASS as drift evidence; build revision `b774170ff46c393eeb5e495ea37936038d3f4f4f`, object SHA-256 `0543c46dae94186df8c9972abff136f5d1c33f691184548ac3df1eef3c2d6590`, 89/94 action strings present, 5 missing; not a reproducible manifest |
| `node fixtures/command-contract-fixture.mjs D:/WORKSPACE/PROJECT/HMS-IDF fixtures/command-evidence-matrix.json` | PASS; 94 descriptor rows, 94/94 handler mappings, descriptor class/permission/confirmation/body/TTL/dry-run/audit/deferred-outcome policy extracted; action payload/response/build/side-effect mapping remains deferred |
| `node fixtures/identity-license-source-evidence-fixture.mjs D:/WORKSPACE/PROJECT/HMS-IDF` | PASS; getter-only identity API, active NVS location, exact-pending license comparison, replay validation, journal marker/sequence checks, and commit/finalize/publication ordering are source-pinned; candidate setter/importer and power-loss execution remain deferred |
| `node fixtures/contract-artifacts-fixture.mjs plans/260924-2044-hms-server-management D:/WORKSPACE/PROJECT/HMS-IDF` | PASS; shared JSON contract invariants, 94-row catalog hash, primary-only bundle rule, forbidden secret fields, load target marker and byte-exact license proto pin validated |
| `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify-license-schema.ps1` | PASS; pinned license proto is byte-exact with current HMS-IDF source; source hash `ea936071b96e62194ca91906ea29075b85812e9fedcb20d26c5f61f575ca682c`; no released-build claim |
| `node fixtures/framed-command-fixture.mjs D:/WORKSPACE/PROJECT/HMS-IDF` | PASS; signed header 1..1023, framed whole message 131,072, header 1,023, idle deadline 15,000 ms, body actions limited to `modbus.check_rtu_config` and `modbus.replace_rtu_config`; source evidence only |
| `node fixtures/phase-gate-fixture.mjs D:/WORKSPACE/PROJECT/HMS-SERVER/plans/260924-2044-hms-server-management` | PASS; all 8 phases contain the 12 required explicit TDD/ownership/evidence/release sections |
| `node fixtures/mqtt-topic-fixture.mjs D:/WORKSPACE/PROJECT/HMS-IDF` | PASS; proven topic/session baseline and wildcard-plus-response-exclusion ingress; canonical server publish leaf remains `NEW_FIRMWARE_CONTRACT` |
| `node --test fixtures/broker-session-envelope.test.mjs` | PASS; 5 negative/rotation model tests; no live EMQX proof |
| `node --test fixtures/importer-contract.test.mjs` | PASS; 5 importer trust/recovery model tests; no actual NVS/importer proof |
| `node scripts/documentation-drift-check.mjs` | PASS; all legacy entrypoints have historical banners and no misleading current banner markers |
| JSON contract parse (`contracts/**/*.json`) | PASS; all four contract/profile files parse successfully |

## Documentation drift result

The following historical documents were explicitly marked as superseded or
historical so they cannot be mistaken for the current architecture:

- `ALIGNMENT.md`
- `docs/journal.md`
- `docs/architecture-hms-server.md`
- `brainstorm-report.md`
- `research-report.md`

The current plan and approved design journal remain the normative management/
control-plane sources. The pre-cook drift script now checks legacy banners;
Phase 8 must extend it to generated/current implementation docs.

## Gate A ledger

| Gate | Owner | Status | Evidence/risk |
|---|---|---|---|
| Firmware command envelope and size/security limits | Phase 1/3 | `SOURCE_PROVEN + CONTRACT_TESTED` | Current source anchors, shared envelope schema and golden vector pass; no on-device test |
| Firmware topic/QoS/retain/reconnect baseline | Phase 1/3 | `SOURCE_PROVEN` | Topic map, wildcard-plus-response-exclusion ingress, connected-wiring order, and active reconnect backoff are source-pinned; canonical server publish leaf/ACL and runtime soak remain open |
| Command catalog version/hash drift | Phase 1/2/3 | `CONTRACT_TESTED + DEFERRED_TO_PHASE_2/3 — REQUIRED` | Pinned 94-row snapshot, synthetic drift, descriptor policy extraction, namespace/action registration mapping, static handler branch/field-reference extraction, and older-object drift scan pass; 89/94 string presence with five missing actions is evidence of incompatibility, not a manifest; reproducible build manifest and action-level schema/build/side-effect mapping remain release gates |
| Candidate identity/importer bootstrap | Phase 1/5 | `CONTRACT_TESTED + NEW_FIRMWARE_CONTRACT` | Source fixture confirms getter-only identity and no current setter; model/schema covers proof, nonce, expiry, locality, redaction and candidate staging; current firmware lacks combined setter/importer |
| License journal/recovery | Phase 4/5/8 | `SOURCE_PROVEN + CONTRACT_TESTED + MODEL_TESTED + DEFERRED_TO_PHASE_5 — REQUIRED` | Source fixture pins exact-pending, replay, marker/sequence and commit/finalize/publication ordering; model covers invalid/lower/pre/post-marker/exact-pending; hardware power-loss matrix remains required |
| EMQX trusted publisher identity | Phase 1/3/8 | `CONTRACT_TESTED + NEW_CONTRACT_REQUIRED` | Negative envelope/rotation model passes; live EMQX bridge/ACL tests deferred |
| KMS/signer/Data Protection | Phase 1/4/8 | `DEFERRED_TO_PHASE_4/8 — REQUIRED` | No production implementation exists |
| SignalR epoch/backpressure | Phase 3/7/8 | `DEFERRED_TO_PHASE_3/7/8 — REQUIRED` | No production implementation exists |
| PostgreSQL/EF authorization schema | Phase 2 | `DEFERRED_TO_PHASE_2 — REQUIRED` | HMS-SERVER currently contains planning documents only |
| Vue frontend download/command UX | Phase 6/7 | `DEFERRED_TO_PHASE_6/7 — REQUIRED` | HMS-SERVER frontend source does not yet exist |
| 1,000-device load and two-replica soak | Phase 8 | `DEFERRED_TO_PHASE_8 — REQUIRED` | Test profile is specified in plan; no implementation/runtime result |

## Remaining blockers

Gate A is not yet complete because the following source/fixture gates remain:

1. A reproducible firmware build/source manifest and complete action-level 94-row
   payload/response/build-condition/side-effect mapping are required before
   catalog compatibility is claimed. The available older object at revision
   `b774170ff46c393eeb5e495ea37936038d3f4f4f` contains only 89/94 current
   action strings, so cross-revision execution remains denied. Namespace
   registration/action presence is already fixture-tested.
2. A server/ACL integration fixture must select and pin the canonical command
   publish leaf. Until then commands are explicitly disabled with
   `CONTRACT_UNAVAILABLE`; firmware connected-wiring and reconnect ordering are
   already source-proven, while runtime timing/jitter remains a soak concern.
3. Actual EMQX bridge/ACL/webhook integration must prove the new broker envelope.
4. Actual HMS-IDF importer/NVS setter and candidate-identity tests remain required; the current source fixture proves the setter is absent from the identity component and therefore keeps this as a new firmware contract.
5. The model recovery fixture must be replaced by hardware/simulator power-loss
   tests after every partition write and marker in Phase 5.
6. Backend, frontend, real signer/KMS, 1,000-device load and HA tests remain
   implementation/release gates and are not pre-cook passes.

These blockers are actionable without production implementation. The goal must
continue working on them rather than mark itself complete.
