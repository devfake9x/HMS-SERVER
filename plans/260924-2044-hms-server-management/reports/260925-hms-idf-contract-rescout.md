---
title: "HMS-IDF contract re-scout"
type: firmware-evidence
date: 2026-09-25
freshness: "verified: 2026-09-25@workspace-no-git"
status: source-evidence-collected
---

# HMS-IDF Contract Re-scout

## Scope and provenance

The current `D:/WORKSPACE/PROJECT/HMS-IDF` workspace was read before the HMS-SERVER plan update. The directory exposes `.git`, but Git cannot resolve it as a repository in the current sandbox, so no commit SHA is claimed. SHA-256 digests below pin the inspected source files.

The scout is read-only. It does not prove that a server, importer, EMQX rule, or KMS integration exists; those remain separate implementation or new-firmware-contract questions.

## File digests

| Source file | SHA-256 |
|---|---|
| `components/command-dispatcher/cmd_security.c` | `CD87B907DAD8C5D45C8478A4A45F9DE9B3E0CA5E31DFBDA58524278840801A8F` |
| `components/command-dispatcher/command_dispatcher.c` | `0C054112AB03CAD8BC3D514C0AE732510E353F9A5CCA55F9A0A3B3D8BD2E42F4` |
| `components/command-dispatcher/cmd_descriptor.c` | `15FE0604DFC9B32933BB634431C5B51DFE8B649C98DA753A671963F231280C36` |
| `services/mqtt-service/mqtt_service.c` | `DC749168574D072274E644BBFB77C8A0DBE90ECA1EF76EE1AF985FBB6B8A8AEC` |
| `components/mqtt-manager/mqtt_config.c` | `83AD50347056BB4396DBE4F2DA7760EC2709180A69801374688FE17025340BE4` |
| `components/mqtt-status-plane/src/status_plane.c` | `6971223A3EEC2685D1319FA6F8FE5E455A6EBC3C3CDD10DF67BC4E07C817031A` |
| `components/util-device-identity/src/device_identity.c` | `805AC0D310014900278787948FCD052589AA1C74542EEAB0457934D4AE68E28F` |
| `components/hms-license/src/license_api.c` | `704C614D81E90DD1FDE38D74DFECCFF66B24AB10DED8BADD18153BE2E9F26422` |
| `components/hms-license/src/license_storage.c` | `4117F2767A3798CF7567B77B026FD9B8384A0343C096713ECB18E43F02623F2E` |
| `components/hms-license/src/license_evaluator.c` | `CDAED7B071CB363008237505DFA8AFA30FB3957BB6B28B408278D7B2773ECE98` |
| `components/hms-http-api/auth/auth_manager.c` | `EB05B59EC21E1323FEE54079F87E91F3D09915D0D6005762C8FC4B5C1B0540A0` |
| `components/hms-http-api/license/license_api.c` | `76B72350F7D20CCC13E8C221570EDC03F2EF3E49864C8125C6FC17400C8DFB6F` |

## Evidence anchors

### Command security

- `components/command-dispatcher/cmd_security.c > constants > command signing limits`: `#define CMD_SIGNING_KEY_SIZE 32`, `#define CMD_SIG_MARKER ",\"sig\":\""`, and `#define CMD_SIGNABLE_MAX 1024`.
- `components/command-dispatcher/cmd_security.c > security_strict_envelope > top-level allowlist`: the allowlist is `type`, `id`, `ts`, `data`, `key_ver`, `ttl_ms`, `dry_run`, `confirm`, `response_topic`, `trace_id`, `sig`.
- `components/command-dispatcher/cmd_security.c > json_extract_sig > raw signature rule`: `sig` must be the final field and the verifier reconstructs the signable JSON by removing the trailing signature field.
- `components/command-dispatcher/cmd_security.c > cmd_security_admit_raw > size admission`: raw length is rejected when `raw_len == 0 || raw_len >= CMD_SIGNABLE_MAX`, so the accepted upper boundary is 1,023 bytes.
- `components/command-dispatcher/cmd_security.h > key lifecycle > rotation`: the command key version increments on set/clear and invalidates envelopes carrying the previous `key_ver`.
- `components/command-dispatcher/command_dispatcher.c > cmd_dispatcher_dispatch_json > envelope fields`: `id` is mandatory, `data.action` is resolved through the descriptor manifest, and a root-level `action` is rejected.
- `components/command-dispatcher/command_dispatcher.c > TTL enforcement > constants`: `CMD_TTL_MAX_MS` is 24 hours, `CMD_CLOCK_SKEW_SEC` is 60 seconds, and the default TTL is 120 seconds when neither the frame nor descriptor supplies one.
- `services/mqtt-service/cmd_frame.h/c` and `mqtt_service.c > framed command path`: normal signed headers remain bounded to 1..1023 bytes, while a framed message has a 1,023-byte header line, a 131,072-byte whole-message limit, a 15,000 ms idle deadline, and a separate body lent only to descriptor rows with `accepts_body=true`. Current rows are `modbus.check_rtu_config` and `modbus.replace_rtu_config`; this does not yet prove an on-device fragmentation/broker-buffer test.

### Command catalog

- `components/command-dispatcher/cmd_descriptor.c > s_manifest > manifest rows`: actions carry type, action, class, required permission, dangerous confirmation, descriptor TTL, body support, and audit metadata.
- `components/command-dispatcher/cmd_descriptor.c > cmd_descriptor_export_capabilities_json`: firmware exports descriptor readiness/capability JSON. This proves availability metadata, not server authorization.

### MQTT topics and session defaults

- `services/mqtt-service/mqtt_service.c > mqtt_service_build_topics`: firmware builds `<prefix>/<device_id>/cmd/#`, `/cmd/res`, `/audit`, and `/capabilities`.
- `services/mqtt-service/mqtt_service.c > publish_capabilities_locked`: capabilities are published to the capabilities topic with QoS 1 and `retain=true`.
- `components/mqtt-status-plane/src/status_plane.c > connected state publisher`: connectivity online state is published with QoS 1 and retained semantics; the status plane publishes the current status separately.
- `components/mqtt-manager/mqtt_config.c > mqtt_config_get_defaults`: default mode is primary-only, clean session is true, keepalive is 30 seconds, session QoS is 1, session retain is false, and LWT uses QoS 1/retain true. The config also stores reconnect interval fields (5 seconds/60 seconds), but `mqtt_manager.c` disables ESP-MQTT auto-reconnect and the active manager task uses the separately source-proven 1/2/4/8/16/30-second timing path described below.

The session defaults do not define every topic's behavior. The active manager path disables ESP-MQTT auto-reconnect and uses its own reconnect task: a one-second wake debounce, 2/4/8/16/30-second exponential backoff capped at 30 seconds, and up to 20 attempts per cycle. There is no random-jitter implementation in the inspected path. On each connected callback, firmware updates status topics, rewires response/audit/descriptor identity, subscribes to the command wildcard, publishes the retained capabilities snapshot for the primary broker, and then runs `status_plane_on_connect`. This ordering is source-proven; runtime soak and server compatibility remain separate tests.

The command ingress call path was re-read after the initial topic pass. `mqtt_data_callback()` first excludes topics whose suffix is `/cmd/res`; every other payload received through the subscribed wildcard is copied to the command queue. The callback does not compare the incoming topic with a single command leaf before dispatch. This is stronger evidence than a bare wildcard string: firmware ingress is wildcard-plus-response-exclusion, while a canonical server publish leaf remains a server/ACL contract that needs an end-to-end fixture. The generic `mqtt-message-publish` service is a caller-supplied outbound message service and does not define the command-ingress leaf.

### Device identity

- `components/util-device-identity/src/device_identity.c > device identity partition constants`: `device_id` is stored in `enroll`, while product model and board serial are stored in `vendor`.
- `components/util-device-identity/src/device_identity.c > device_identity_get_id`: the active public API reads the enrollment identity; this re-scout found no complete combined setter/importer for device ID, broker credentials, command keys, and license bytes.

### License and recovery

- `components/hms-license/include/hms_license.h > license_apply contract`: failures before journal commit leave the prior durable generation authoritative; a committed new generation whose clock-clear step fails remains durably staged and policy-restricted, and retrying the exact envelope resumes the idempotent clear operation.
- `components/hms-license/src/license_api.c > license_apply > exact_pending`: exact-pending resume requires `clock_clear_pending`, a present record, equal envelope length, and byte-for-byte equal envelope content.
- `components/hms-license/src/license_api.c > license_apply > replay and commit`: replay validation occurs before `prv_commit_record_reconciled`; the durable nonce slot is removed as part of the committed record, and a later clock-clear/publication failure can return an error after the new record is durable.
- `components/hms-license/src/license_storage.c > journal validation`: journal slots, commit markers, sequence numbers, digest, and initialization metadata are validated; an unverifiable committed generation fails closed rather than silently rolling back.
- `components/hms-license/src/license_evaluator.c > device binding`: a nonempty signed `device_id` is rejected when active NVS identity is empty or does not match.

### HTTP authentication/importer boundary

- `components/hms-http-api/license/license_api.c > license route registration`: license info/request/apply/remove routes are protected by human permissions `PERM_LICENSE_VIEW` and `PERM_LICENSE_MANAGE`.
- `components/hms-http-api/license/license_api.c > LIC_BODY_MAX/read_body`: the current license route has a bounded request body and raw envelope path.
- `components/hms-http-api/auth/auth_manager.c > auth_middleware_handler`: the current middleware proves permission-based human authentication. This source does not prove a device-bound bootstrap challenge, local-only importer, one-time proof, CSRF contract, or importer nonce replay protection.

## New-contract boundaries

The following are not source-proven by this re-scout and must remain
`NEW_FIRMWARE_CONTRACT` or an explicitly denied release feature:

1. Combined identity, MQTT credential, command-key, and license importer.
2. Candidate identity validation for first enrollment before active identity exists.
3. Device-bound bootstrap challenge/token and proof-of-locality.
4. Import nonce replay protection and one-time consumption semantics.
5. CSRF and remote caller rejection for an importer route.
6. Trusted EMQX broker-session envelope propagation to the backend.
7. Firmware-provided command catalog hash/version binding.
8. Canonical server command publish leaf and ACL binding; firmware connected
   wiring and reconnect backoff are source-proven, while runtime soak/jitter
   compatibility remains a release test.

## Readiness impact

- Existing command envelope/topic facts are `SOURCE_PROVEN` at the source paths above.
- The proposed importer state machine is `MODEL_TESTED`/`CONTRACT_REQUIRED` until implemented in HMS-IDF and exercised against NVS/journal behavior.
- Backend EMQX identity propagation, KMS signer, SignalR authorization epoch, 1,000-device load and two-replica HA are implementation/release gates, not pre-cook source facts.
- The current HMS-SERVER plan must reconcile stale normative documents before `DESIGN_CONTRACT_READY`.
