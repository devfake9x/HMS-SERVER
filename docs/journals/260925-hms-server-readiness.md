---
type: journal
date: 2026-09-25
topic: hms-server-readiness
status: not-ready-to-cook
---

# HMS-SERVER Pre-Cook Readiness Journal

## Context

This goal prepares the HMS-SERVER management/control-plane design and its
firmware compatibility boundary. It does not implement the ASP.NET backend,
Vue frontend, or HMS-IDF importer, and it does not invoke `ck-cook`.

## Brainstorm decision

The `ck-brainstorm` problem-first review selected the pre-cook evidence-gate
approach. The goal combines a read-only HMS-IDF protocol audit with whole-plan
consistency checks, deterministic fixtures, and an explicit readiness ledger.
This keeps unsupported behavior visible without creating a circular requirement
to implement production code before the plan can be reviewed.

## Evidence established

- HMS-IDF proves direct SHA-256 HMAC with the 32-byte `crypto/cmd_key`, the
  current allowlisted command fields, required `id` and `ts`, final `sig`,
  1..1023-byte raw JSON, TTL/future-skew checks, and key-version fencing.
- HMS-IDF proves the baseline device topic and session behavior for command
  subscription, command responses, audit, capabilities, status, and retained
  connectivity state.
- The pinned 94-row command descriptor snapshot and synthetic drift fixture
  pass; the catalog hash is
  `dbda8317758e5f25c5a4dee8456841ef3689fea4707cf5a27c30ffad656f78d9`.
- License recovery model tests, broker-envelope model tests, importer contract
  tests, documentation drift checks, plan validation, status, and helper tests
  pass. These are contract/model results, not production runtime proof.
- A deeper MQTT call-path fixture now proves wildcard command ingress with
  `/cmd/res` exclusion, connected-wiring order, and the active reconnect task's
  1/2/4/8/16/30-second backoff with one-second debounce. It does not choose a
  server-owned command leaf or ACL.
- A handler-registration fixture maps all 94 descriptor rows to 12 registered
  namespaces and finds an action literal for every row. It does not prove
  payload/response schema, build conditions, readiness, side effects, or
  end-to-end execution.
- A build-artifact fixture inspected the older object at build revision
  `b774170ff46c393eeb5e495ea37936038d3f4f4f` (object SHA-256
  `0543c46dae94186df8c9972abff136f5d1c33f691184548ac3df1eef3c2d6590`). It
  found 89/94 current action strings, missing
  `modbus.check_rtu_config`, `modbus.replace_rtu_config`,
  `modbus.restart_rtu`, `system.get_operations`, and
  `system.set_ntp_servers`. This is drift evidence only; object strings cannot
  prove descriptor ordering, schema, build conditions, or runtime compatibility.
- A command-contract fixture now extracts the descriptor policy for all 94 rows
  and joins it with the 94/94 handler-registration mapping. Class, permission,
  confirmation, framed-body acceptance, descriptor TTL, dry-run, audit and
  deferred-outcome flags are source-proven. Payload/response schemas,
  build/runtime conditions, side effects, runtime readiness and server policy
  remain explicitly deferred.
- An action-source evidence fixture now follows every registered handler branch
  and records source line, handler digest, direct input/response field
  references and helper-call references for all 94 rows. This strengthens the
  provenance record without upgrading static references into schema, side-effect,
  build-condition or runtime proof.
- An identity/license source fixture confirms that the current identity module
  exports getters only and reads `enroll/device_info/device_id`; it also pins
  exact-pending license matching, replay validation, journal marker/sequence
  checks, and commit-before-finalize/publication ordering. Candidate setters,
  importer bootstrap, and power-loss recovery remain new/implementation
  contracts.
- A final requirement-group audit records `NOT_READY_TO_COOK`, separates Gate A
  from Gate B, lists the 13 open P0/P1/runtime gates, and preserves the rule that
  no new release limitation is self-accepted.
- The shared pre-cook contract set is now present: MQTT topics and envelope,
  capabilities observation, device bundle, command catalog, importer bootstrap,
  trust store, load/recovery contract and a byte-exact pinned firmware license
  proto. The artifact fixture and PowerShell schema-pin check pass; full schema
  validation and runtime integration remain phase-owned.
- A framed-command fixture corrected the size contract: the signed JSON header
  remains 1..1023 bytes, while a separate framed body may make the whole MQTT
  message up to 131,072 bytes with a 15-second idle deadline. Only
  `modbus.check_rtu_config` and `modbus.replace_rtu_config` accept that body;
  this is source-anchored and not yet an on-device fragmentation test.
- A phase-gate fixture checks all eight phase files and confirms each contains
  the twelve required TDD, ownership, evidence, release-gate and definition-of-
  done sections.
- A publish-path fixture now separates the generic explicit-topic MQTT
  transport from the command response wiring: `/cmd/res` is source-proven for
  responses, wildcard/retained generic publishes are rejected by the firmware
  v1 transport, and no canonical server command leaf is implied.
- An HTTP/importer trust fixture now pins the existing human session boundary
  (Bearer/X-Auth-Token, permission middleware, login lockout), the 2,048-byte
  license apply body bound, and the getter-only logical identity API. It leaves
  device-bound bootstrap, import replay/locality/CSRF controls and an identity
  setter as explicit new firmware contracts.

## Decisions recorded

1. Keep the approved ASP.NET Core 10, Vue 3, PostgreSQL/EF Core, EMQX, and
   SignalR architecture and the control-plane-only scope.
2. Keep release 1 `PRIMARY_ONLY`; reject unsupported secondary/both/failover
   modes explicitly.
3. Keep broker-authenticated identity propagation fail-closed and require the
   application to reject missing or inconsistent publisher metadata.
4. Keep the server-owned catalog as the command authority and deny unknown
   catalog versions or hashes.
5. Keep importer recovery staged and generation-based. Do not claim
   cross-partition atomicity or unconditional rollback after a durable license
   marker.
6. Keep raw private PEM outside the web process and require an external
   signer/KMS/HSM boundary with trust-store rotation and audit.

## Readiness result

`NOT_READY_TO_COOK` remains the correct result. The following gates are still
explicitly open:

- A reproducible firmware build/source manifest and action-level 94-row
  payload/response/build-condition/side-effect mapping are required before
  cross-revision catalog compatibility is claimed. The available older object
  has 89/94 action strings and therefore cannot be treated as compatible.
  Namespace registration and action presence for the current source are already
  fixture-tested.
- HMS-IDF still does not define a canonical server command publish leaf or ACL.
  A server/ACL integration fixture must select and prove one; until then the
  server must return `CONTRACT_UNAVAILABLE` and emit no publish. Firmware
  connected-wiring and reconnect ordering are source-proven, while runtime
  timing/jitter remains a soak concern.
- The combined importer bootstrap proof, local-only trust, candidate identity
  setter, and hardware/simulator power-loss matrix are not present in the
  current firmware.
- Live EMQX ACL/webhook identity propagation, production signer/KMS tests,
  backend/frontend implementation tests, load testing, and any HA soak remain
  phase-owned implementation or release gates.

## Impact

The plan is internally consistent and safe to hand to implementation work only
after the remaining source/fixture gates are resolved or explicitly accepted
as release limitations by the product owner. Until then, treating the plan as
ready to cook would allow protocol invention and would overstate importer and
recovery guarantees.

## Next action

Continue with the named firmware fixtures and phase-owned implementation gates.
When Gate A is complete, update the readiness ledger to `READY_TO_COOK`; until
then keep all MQTT publish and importer paths fail-closed and keep the goal
active.

## Validation after catalog drift integration

The post-update sweep passed plan validation, plan status, all 11 plan helper
tests, red-team validation, command golden vector, current catalog drift,
handler registration, command-contract extraction, framed-command extraction,
publish-path evidence, HTTP/importer trust evidence, phase-gate lint,
build-artifact drift, MQTT topic, broker-envelope, importer-model,
license-recovery-model, documentation-drift,
and JSON contract parsing checks. The red-team command still reports only the known warning about
machine-specific local paths/secret-like values; no credential was found. The
plan remains `pending` with eight phases pending and readiness remains
`NOT_READY_TO_COOK` because the source/fixture gaps above are unchanged.

## CK brainstorm visibility verification

On the current turn, `ck-brainstorm` was explicitly re-read and applied before
the Phase 1 edit. The problem-first conclusion is unchanged: this is an
evidence-boundary problem between HMS-IDF and HMS-SERVER, so unsupported
protocol behavior must stay denied rather than being filled in by assumption.
The Phase 1 evidence inventory now names both the MQTT publish-path fixture and
the HTTP/importer trust-source fixture.

The follow-up sweep passed all 17 runnable pre-cook fixture/model checks, all 11
`plan-ops` helper tests, plan validation/status, and documentation drift. The
red-team command still reports only the known machine-specific path warning.
No production source was written and `ck-cook` was not invoked. Readiness
therefore remains `NOT_READY_TO_COOK` until the open firmware, broker, importer,
runtime, and implementation gates are proven or explicitly accepted by the
product owner as release limitations.
