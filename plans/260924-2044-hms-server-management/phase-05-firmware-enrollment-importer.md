---
phase: 5
title: "Server-side enrollment and importer contract"
status: pending
priority: P1
dependencies: ["4"]
---
# Phase 5: Server-side enrollment and importer contract

## Overview
Implement and verify the HMS-SERVER-side bundle, enrollment contract, validation, and crash-recovery model. Firmware-side importer/NVS implementation is explicitly deferred and HMS-IDF must not be modified in this goal.

All NVS writes, boot activation, and device HTTP importer behavior below are acceptance requirements for a later firmware-enabled profile. In this goal they are represented by server contract fixtures and recovery models only; they must not be described as implemented firmware behavior.

## Requirements
- Functional: Import versioned bundle JSON with device ID, MAC/board identity, MQTT broker/credentials, key generations, and optional raw license envelope.
- Functional: Write `device_id` to `enroll/device_info`, MQTT config to the existing MQTT manager storage, command/telemetry keys to the `crypto` namespace, and license envelope to `vendor/license` through existing license APIs.
- Functional: Validate MAC, board serial/product model, schema version, key lengths, generation monotonicity, license binding, and signature before any destructive write. License binding validation must accept an explicitly supplied staged candidate identity under the importer lock; it must not make active identity getters expose uncommitted values.
- Functional: Support idempotent re-import, key rotation, safe failure, and a structured import result/report. An identical bundle hash plus generation returns the existing terminal/pending status without replaying its nonce; a reused nonce with altered bytes, lower generation, or conflicting license counter is rejected.
- Non-functional: Never log raw passwords, key bytes, or license payload secrets. Preserve fail-closed startup behavior on degraded NVS.
- Transport: importer is a local authenticated HTTP endpoint only; require a device-bound one-time bootstrap challenge/token, import nonce, expiry, replay rejection, CSRF protection, bounded body (16 KiB), and reject non-local/unauthenticated callers. Current HMS-IDF evidence proves human permission middleware for license routes, not a device-bound importer permission, so that importer trust boundary is a new fixture-gated firmware contract. Human admin passwords are never placed in the bundle. The response contains only generation, hash prefix, and redacted state. The bootstrap proof and locality check are new firmware contracts until an HMS-IDF fixture proves the exact middleware behavior.

## Architecture
Create a small `hms-provisioning` component and an authenticated local HTTP route under the existing HMS HTTP API. The server first obtains a device-bound bootstrap challenge/token reference; the device verifies locality, token binding, nonce freshness, expiry and one-time use before parsing the bundle. The parser performs a complete validation pass into an in-memory model, including candidate-aware license binding against the target device ID/MAC, without changing active identity getters. The commit state machine is `PREVALIDATE -> STAGE_NONLICENSE -> COMMIT_NONLICENSE -> LICENSE_PENDING -> candidate-aware license_apply() -> COMMIT_ACTIVE`. Because ESP-IDF NVS commits are partition-local, the importer records `pending_generation`, per-store completion bits, bundle hash, prior license sequence/hash, target envelope hash, import nonce state, and a commit marker. Boot reconciliation activates only a complete generation; before a durable target license marker the previous generation remains authoritative, while after that marker recovery resumes the exact pending operation or enters `recovery-required` fail-closed. License bytes are passed to `license_apply()` as the final activation step rather than rewritten by the importer.

```text
HTTP bundle upload
  -> strict parse/schema/version
  -> hardware/key/license binding validation
  -> staged NVS writes (new generation, inactive)
  -> per-store completion bits + commit marker + generation
  -> boot reconciliation / activation fence
  -> reload identity/MQTT/security/license
  -> redacted import result + status/capabilities publish
```

If the device loses power during import, the next boot must expose a clear incomplete/failed state and never combine stores from different generations. Before the license marker the previous valid generation remains authoritative; after a target license marker the device must complete the target or expose `recovery-required` fail-closed. A failed enrollment must never silently erase or replace vendor license authority.

The pre-cook bundle field matrix is
`../reports/260925-provisioning-bundle-field-matrix.md`. It labels the current
getter-only identity surface and every combined importer field that still needs a
firmware setter/fixture. The pre-cook importer model is evidence for the state
machine only; it is not a substitute for NVS power-loss tests after every
partition write, journal commit, completion bit and activation marker.

## Related Code Files
- Create: `D:/WORKSPACE/PROJECT/HMS-IDF/components/hms-provisioning/CMakeLists.txt`
- Create: `D:/WORKSPACE/PROJECT/HMS-IDF/components/hms-provisioning/include/hms_provisioning.h`
- Create: `D:/WORKSPACE/PROJECT/HMS-IDF/components/hms-provisioning/src/provisioning_bundle.c`
- Create: `D:/WORKSPACE/PROJECT/HMS-IDF/components/hms-provisioning/src/provisioning_commit.c`
- Create: `D:/WORKSPACE/PROJECT/HMS-IDF/components/hms-http-api/provisioning/provisioning_api.c`
- Create: `D:/WORKSPACE/PROJECT/HMS-IDF/components/hms-http-api/provisioning/provisioning_bootstrap.c`
- Modify: `D:/WORKSPACE/PROJECT/HMS-IDF/components/hms-http-api/CMakeLists.txt`
- Modify: `D:/WORKSPACE/PROJECT/HMS-IDF/components/util-device-identity/src/device_identity.c`
- Modify: `D:/WORKSPACE/PROJECT/HMS-IDF/components/mqtt-manager/mqtt_config.c`
- Modify: `D:/WORKSPACE/PROJECT/HMS-IDF/components/command-dispatcher/cmd_security.c`
- Modify: `D:/WORKSPACE/PROJECT/HMS-IDF/components/hms-license/src/license_api.c`
- Create: `D:/WORKSPACE/PROJECT/HMS-IDF/test-suite/tests/test_provisioning_import.py`
- Create: `D:/WORKSPACE/PROJECT/HMS-IDF/components/hms-provisioning/test/test_provisioning_bundle.c`
- Reference: `D:/WORKSPACE/PROJECT/HMS-IDF/components/util-device-identity/include/device_identity.h`
- Reference: `D:/WORKSPACE/PROJECT/HMS-IDF/components/command-dispatcher/include/cmd_security.h`
- Reference: `D:/WORKSPACE/PROJECT/HMS-IDF/components/hms-license/include/hms_license.h`
- Reference: `D:/WORKSPACE/PROJECT/HMS-IDF/components/hms-http-api/license/license_api.c`

## Implementation Steps
1. **Tests Before**: Add host/fixture tests for valid import, malformed JSON, unknown version, duplicate fields, wrong MAC, wrong board identity, wrong key length, stale generation, candidate-identity license binding, invalid license, power-loss at every store commit and marker write, pre-marker versus post-marker license recovery, exact-pending resume, boot reconciliation, same-bundle idempotent re-import, altered-bundle nonce replay, lower generation, conflicting counter, replayed/expired bootstrap token, remote caller, HTTP locality/auth/CSRF, and no-secret logging. Run `identity-license-source-evidence-fixture.mjs` and `http-importer-trust-source-evidence-fixture.mjs` first to pin the current getter-only identity surface, human-only auth middleware, license body limit and license journal ordering. The pre-cook `fixtures/license-recovery-model.test.mjs` is only a model seam; Phase 5 must replace it with actual HMS-IDF NVS/journal crash tests before implementation verification is complete.
2. Freeze the bundle schema and importer endpoint with backend Phase 1 fixtures. Add a bundle hash and import generation to the response.
3. Implement strict parser and complete validation with no NVS writes during validation.
4. Implement staged writes and rollback/commit markers. Preserve old enrollment/key generation until every non-license store is complete; never claim atomicity across NVS partitions. Add explicit writer/reader APIs for device identity plus a candidate-aware license evaluator seam because the current firmware surface is getter-only and active identity cannot validate a first enrollment target.
5. Wire adapters around existing MQTT config, command-security and license APIs rather than duplicating storage rules. Persist `LICENSE_PENDING` before apply, invoke `license_apply()` with the staged candidate identity, and leave a recoverable pending or `recovery-required` state if any post-apply step fails. Resume only the exact firmware-supported pending operation; never blindly reapply a committed envelope.
6. Add status/capabilities publication after successful import and a redacted diagnostic result for the server.
7. Implement and test the importer bootstrap verifier as a separate trust boundary from human license/API authentication. Reject remote/untrusted callers, missing/expired/replayed challenge/token or nonce, CSRF failures, and unsupported auth middleware combinations according to the device's existing security model.
8. **Tests After**: Run host tests, firmware component tests, Python MQTT/provisioning suite, and a hardware or simulator smoke test.

## Success Criteria
- [ ] Valid bundle writes all intended stores and reports the new generation without exposing secrets.
- [ ] Invalid bundle writes nothing and leaves the previous generation usable.
- [ ] `device_id` is stored in `enroll/device_info`; vendor license remains in `vendor/license`.
- [ ] Command key rotation invalidates the previous key version and keeps signed mode fail-closed.
- [ ] License bytes are applied through the existing verification/journal path.
- [ ] Re-import of the same bundle hash and generation is idempotent without reusing its import nonce; altered bytes with a replayed nonce, lower generation, or conflicting counter are rejected.
- [ ] A simulated power loss cannot produce a half-active credential/keyset.
- [ ] Every interrupted step is classified as `pending`, `rolled_back`, `active`, or `recovery-required`; boot never combines stores from different generations.
- [ ] Candidate identity binding is tested for first enrollment without exposing uncommitted identity through active getters.
- [ ] License recovery proves prior authority before a durable target marker, exact-pending resume after a post-commit clock-clear failure, and fail-closed `recovery-required` after an unsupported post-marker failure; it never silently destroys the prior license or blindly replays a committed envelope.
- [ ] Import route rejects remote/untrusted callers and never returns secret material.
- [ ] Import route rejects missing/expired/replayed device-bound bootstrap proof and import nonce; human admin passwords never appear in the bundle.
- [ ] Bootstrap and locality behavior is either proven by an HMS-IDF fixture or explicitly recorded as a release-blocking new firmware contract.

## Risk Assessment
- **Risk**: ESP32 NVS cannot atomically span all partitions. **Mitigation**: per-store inactive generations, completion bits, commit marker, boot reconciliation and an activation fence; test power loss after each commit.
- **Risk**: importer duplicates security-sensitive storage logic. **Mitigation**: call existing identity/MQTT/command/license APIs and add adapter seams only where missing.
- **Risk**: malformed input causes memory or parser issues. **Mitigation**: bounded body size, strict duplicate-key/depth checks, fixed-size buffers, and fuzz/negative tests.
- **Risk**: vendor license is accidentally erased during enrollment. **Mitigation**: separate partitions and failure tests that assert vendor bytes remain intact.

## File inventory and test matrix

| Boundary | Test scenarios |
| --- | --- |
| Bundle parser | valid, malformed, duplicate keys, unknown version, oversize |
| Hardware binding | MAC mismatch, serial/model mismatch, device ID mismatch |
| Key store | all 32-byte keys, missing key, stale generation, rotation |
| License store | signed apply, candidate identity binding, invalid signature, wrong kid, replay/counter, exact-pending resume, post-marker recovery, remove tombstone |
| NVS commit | interruption after each partition write/commit/marker, boot reconciliation, pre-marker authority, post-marker recovery-required, idempotent retry |
| HTTP route | authenticated local call, remote caller rejection, CSRF/missing permission, body limit, redacted result |
| Bootstrap trust | device-bound challenge/token, nonce replay, expiry, wrong-device binding, local-interface check |
| Runtime reload | MQTT reconnect, capabilities/status republish, command signer reload |

## Dependency map

- Blocked by canonical bundle and license/command contracts from Phases 1-4.
- Blocks the frontend's claim that a downloaded bundle is automatically importable.
- Requires access to HMS-IDF build/test tooling but not a production server.

## Function/interface checklist

- [ ] `hms_provisioning_parse_bundle()` validates bounded input.
- [ ] `hms_provisioning_verify_bootstrap()` validates device-bound challenge/token, import nonce, expiry, locality and one-time replay state before parsing/writing.
- [ ] `hms_provisioning_validate_bindings()` checks hardware, generation, and license using a staged candidate identity for first enrollment without changing active getter results.
- [ ] `hms_provisioning_stage()` writes no active marker before all validation passes.
- [ ] `hms_provisioning_commit()` records a boot-recoverable generation commit marker and never claims cross-partition atomicity.
- [ ] `hms_provisioning_recover()` distinguishes exact-pending license resume from durable target-license commit and returns `recovery-required` when no safe forward operation is proven.
- [ ] `hms_provisioning_result_render_json()` redacts all secrets.
- [ ] Existing `cmd_security_set_key`, MQTT config setters, identity setters, and `license_apply` are exercised through adapters, not reimplemented.

## Explicit phase gate

### Tests Before
Run importer and license recovery models, then add firmware/simulator tests for bootstrap locality, nonce replay, candidate identity, every partition/marker write, boot recovery, and license journal states.

### Smallest seam
Add a staged-generation storage adapter and candidate-identity validation/commit seam; active getters never expose staged identity during validation.

### Implementation scope
Implement local device-bound importer authentication, bounded parsing, staged NVS writes, markers, identity activation, license apply, recovery, and boot selection.

### Negative/regression tests
Invalid bundles write nothing; identical bundles are idempotent; lower/conflicting generations, altered replay, remote callers, expired tokens, mixed generations, and unsafe post-marker rollback are rejected or enter recovery-required state.

### Phase regression gate
Crash matrix passes after every partition and marker write; boot never activates mixed generations; old valid generation remains usable before durable license commit.

### Input/output contract
Inputs are the Phase 4 bundle and import-bootstrap schema; outputs are staged/active generation state, redacted result, recovery marker, and audit outcome.

### Dependencies
Requires Phase 4 and actual HMS-IDF setter/journal seams; server model tests cannot complete this phase.

### Ownership
Phase 5 owns HMS-IDF importer, identity setter, local bootstrap trust, staged recovery, and hardware/simulator crash tests.

### Risk
The current firmware lacks the combined setter/bootstrap contract; first enrollment and post-marker failure can otherwise split identity and license authority.

### Evidence classification
Current getter/license behavior is `SOURCE_PROVEN`, proposed importer behavior is `NEW_FIRMWARE_CONTRACT`, and model tests are not firmware proof.

### Release gate
Enrollment remains denied until local proof, candidate binding, setter, replay, and crash-recovery evidence pass on the target firmware path.

### Definition of done
Import is authenticated, bounded, idempotent, generation-safe, crash-consistent within documented limits, and never silently destroys prior license authority.
