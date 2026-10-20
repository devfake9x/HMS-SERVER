---
title: "Goal prompt for HMS-SERVER pre-cook readiness"
type: brainstorm-report
date: 2026-09-25
status: recommended
scope: HMS-SERVER management/control-plane planning
---

# Goal Prompt Brainstorm

## Scout findings

- `plans/260924-2044-hms-server-management/plan.md` is the current planning source and remains `status: pending`; all eight phases are pending and production implementation has not started.
- HMS-IDF contains the relevant command path: command descriptors, command security/parser, deduplication, dispatcher, MQTT command handlers, MQTT service, MQTT manager, and status plane.
- The firmware MQTT ingress and reconnect ordering can be source-pinned, but the canonical server publish leaf/ACL, importer bootstrap authentication, candidate identity setter, and several license recovery boundaries require an explicitly labelled new contract or integration fixture.
- The current 94-row source catalog was compared with an older build object at revision `b774170ff46c393eeb5e495ea37936038d3f4f4f`; only 89/94 action strings were present. The five missing strings are `modbus.check_rtu_config`, `modbus.replace_rtu_config`, `modbus.restart_rtu`, `system.get_operations`, and `system.set_ntp_servers`. This detects drift but cannot prove descriptor schema or runtime compatibility.
- `docs/architecture-hms-server.md` contains stale normative-looking claims for Node/Fastify, React, TimescaleDB, Redis Streams, SCADA, and HKDF. Leaving it unchanged would allow two contradictory architectures to remain discoverable.
- The user wants an autonomous `goal` that prepares the system before cook, while preserving the approved .NET/Vue control-plane scope and all accepted red-team findings.

## 1. Solution-jumping diagnosis

The proposed solution is a large master prompt for an autonomous goal. The signal behind it is not a need for prose; it is the risk that an implementation agent will invent firmware behavior, cook against contradictory documents, claim mocked tests as production evidence, or stop after updating checkboxes.

## 2. Underlying problem

HMS-SERVER needs a trustworthy handoff boundary between firmware evidence, architecture contracts, implementation planning, and later production verification. The boundary must make unsupported behavior visible, preserve security invariants, and prevent premature implementation without making pre-cook readiness depend on code that has not been written.

## 3. Assumptions to challenge

| Assumption | Risk if wrong | Validation |
|---|---|---|
| A longer prompt guarantees better execution | Agent may still skip evidence or silently broaden scope | Require a readiness ledger with file/line evidence and command output |
| Existing reports are enough to define MQTT | Firmware may have changed or reports may overstate proof | Re-scout HMS-IDF command and MQTT call paths before every plan change |
| A model test proves the firmware importer | Actual NVS/journal ordering may differ | Label model evidence separately and defer actual crash tests to Phase 5 |
| A plan can be ready while conflicting normative docs remain | Future agents may follow the stale architecture | Reconcile or mark stale docs historical and run drift scan |
| All release tests must pass before cook | Creates a circular gate because implementation is prohibited | Use separate design, implementation, and release gates |
| Any unresolved issue can be called a release limitation | Core trust/enrollment risks may be weakened without approval | Require explicit user acceptance for new limitations that change approved scope/security |

## 4. Problem statement

- **Users/context:** architecture, backend, frontend, firmware, security, and operations contributors working across HMS-SERVER and HMS-IDF.
- **Struggle:** they need to implement device management and MQTT control safely, but the two repositories have different maturity and some proposed contracts are not yet present in firmware.
- **Cause:** protocol assumptions, stale architecture documents, importer gaps, and runtime-only acceptance criteria are mixed into one planning boundary.
- **Consequence:** invalid commands, cross-device authorization, broken license recovery, unsafe provisioning, or false readiness claims.
- **Success:** the goal produces a consistent, evidence-backed plan and executable pre-cook contract checks, with every remaining implementation or release check assigned to an owner phase.

## 5. Alternative problem framings

### Frame A — Protocol compatibility gate

Treat the goal primarily as a firmware/server compatibility audit. The central output is an evidence matrix for topics, envelopes, keys, catalog, identity, license, and importer behavior.

**Strength:** lowest risk of invented MQTT behavior.  
**Weakness:** can under-specify database, authorization, frontend, and operations work.

### Frame B — Plan consistency gate

Treat the goal primarily as a whole-plan reconciliation and documentation drift pass. The central output is a conflict-free plan, decision record set, phase matrix, and readiness ledger.

**Strength:** prevents agents from cooking against contradictory documents.  
**Weakness:** can produce polished documents without enough executable contract evidence.

### Frame C — Pre-cook evidence gate (recommended)

Combine the protocol audit and plan consistency pass, while allowing deterministic fixtures, golden vectors, catalog extraction, and model tests before implementation. Separate these from later implementation and release verification.

**Strength:** preserves autonomy and safety without creating a circular “implement before cook” requirement.  
**Weakness:** requires disciplined evidence labels and an explicit handoff ledger.

## 6. Evidence status

Current evidence is **medium to strong for existing MQTT envelope/topic facts**, because relevant HMS-IDF source exists and has been searched, but is **incomplete for importer bootstrap trust, candidate identity activation, exact license recovery, trusted EMQX identity propagation, and production KMS integration**. These gaps must become explicit contracts and phase-owned tests rather than assumptions.

Catalog evidence is deliberately split: current-source descriptor extraction,
synthetic drift detection, and namespace/action registration are
`CONTRACT_TESTED`; the older object is `CONTRACT_TESTED` drift evidence only;
cross-revision compatibility remains denied until a reproducible build manifest
or exported catalog is available.

## 7. Validation plan

1. Re-scout the firmware source and capture source revision/digests.
2. Build the command/topic/catalog/identity/license evidence matrices.
3. Reconcile the plan, all phases, reports, and stale normative docs.
4. Add deterministic contract fixtures and model tests where they can run without production implementation.
5. Run plan validation, helper tests, drift scans, and available contract tests.
6. Assign all implementation-dependent checks to the exact owning phase.
7. Produce a final readiness ledger and stop at `READY_TO_COOK` or `NOT_READY_TO_COOK`; do not invoke cook.
8. Run the build-artifact fixture and carry its 89/94 result into the plan,
   firmware matrix, readiness baseline, and journal without upgrading the
   evidence to runtime compatibility.
9. Generate the 94-row command evidence matrix from the firmware descriptor
   and handler-registration source, while keeping action-level schemas,
   build conditions, side effects and server policy explicitly unresolved.

## Recommended decision

Use Frame C. The `/goal` prompt must define three states:

- `DESIGN_CONTRACT_READY`: this goal's completion state.
- `IMPLEMENTATION_VERIFIED`: achieved later by the owning cook phases.
- `RELEASE_READY`: achieved only after deployment, recovery, security, load, and applicable HA checks.

The prompt must explicitly require the CK workflow: `ck-brainstorm`, `ck-scout`, `ck-plan`, `ck-ask`, `ck-security`, `ck-security-scan`, `ck-databases`, `ck-backend-development`, `ck-frontend-development`, `ck-test`, `ck-web-testing`, `ck-project-organization`, and `ck-journal`. `ck-cook` is a later handoff and must not be invoked in this goal.

## Stakeholder message

The next agent will not start by building endpoints or UI. It will first prove what HMS-IDF actually supports, reconcile the plan and normative docs, encode unsupported behavior as explicit new firmware contracts, and leave a testable implementation handoff. This prevents protocol drift and false security confidence while keeping the work moving.

## Prompt acceptance criteria

- Starts with a fresh scout of HMS-IDF and current HMS-SERVER documents.
- Treats every MQTT topic, QoS, retain flag, reconnect rule, envelope field, key rule, license limit, identity setter, and importer behavior as source-proven or fixture-gated.
- Prohibits invented `/cmd/execute`, `kid`, or `generation` fields in the current command envelope.
- Requires all 14 accepted findings to map to decisions, phase owners, tests, and evidence status.
- Requires stale architecture documentation to be reconciled or marked historical/superseded.
- Allows only plan/docs/fixture/model-test work before cook; prohibits production API, frontend, and firmware implementation.
- Distinguishes `SOURCE_PROVEN`, `CONTRACT_TESTED`, `MODEL_TESTED`, `IMPLEMENTATION_TESTED`, `NOT_RUN`, and `DEFERRED_TO_PHASE_N — REQUIRED`.
- Ends with a readiness ledger and exactly one of `READY_TO_COOK` or `NOT_READY_TO_COOK`.
