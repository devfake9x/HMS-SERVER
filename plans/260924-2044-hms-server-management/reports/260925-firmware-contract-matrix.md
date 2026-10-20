---
title: "HMS-IDF firmware contract matrix"
type: firmware-evidence-matrix
date: 2026-09-25
freshness: "verified: 2026-09-25@workspace-no-git"
status: source-proven-with-new-contract-gates
---

# Firmware Contract Matrix

## Proven facts

| Contract | Source anchor | Proven behavior | Status | Does not prove |
|---|---|---|---|---|
| Command HMAC | `components/command-dispatcher/cmd_security.c > constants/load_command_key` | Direct HMAC-SHA256 using 32-byte `crypto/cmd_key`; old key version is fenced | `SOURCE_PROVEN` | Server key provisioning or KMS |
| Wire fields | `cmd_security.c > security_strict_envelope` | Allowlist: `type,id,ts,data,key_ver,ttl_ms,dry_run,confirm,response_topic,trace_id,sig`; unknown fields reject | `SOURCE_PROVEN` | Any future server metadata field |
| Signature bytes | `cmd_security.c > json_extract_sig` | `sig` is final field; HMAC covers raw JSON before the final signature field | `SOURCE_PROVEN` | Language serializer compatibility until golden vector is used |
| Signed header size | `cmd_security.c > cmd_security_admit_raw`; `mqtt_service.c > CMD_DATA_MAX` | `1..1023` signed JSON header bytes accepted; 1024 rejected on the normal path | `SOURCE_PROVEN` | Server must measure the signed header, not silently truncate |
| Framed command size | `cmd_frame.h/c`; `mqtt_service.c > frame_fragment` | Header line max 1023 bytes; whole framed message max 131,072 bytes; 15,000 ms idle deadline; body is dispatched only when descriptor `accepts_body=true` | `SOURCE_PROVEN + CONTRACT_TESTED` | `framed-command-fixture.mjs`; no on-device fragmentation/broker-buffer test yet |
| TTL | `command_dispatcher.c > TTL enforcement` | `ts` required; max 24h; future skew 60s; default 120s if no descriptor/frame TTL | `SOURCE_PROVEN` | Clock synchronization quality |
| Dedup | `cmd_security.c > cmd_security_verify_parsed` | `id` is dedup identity; duplicate is rejected; current implementation documents 128-id RAM cache | `SOURCE_PROVEN` | Durable dedup across reboot |
| Topic subscription | `services/mqtt-service/mqtt_service.c > mqtt_service_build_topics/rebuild_topics_and_subscribe` | `<prefix>/<device_id>/cmd/#`, QoS 1 subscription | `SOURCE_PROVEN` | Server publish leaf |
| Command ingress filtering | `services/mqtt-service/mqtt_service.c > mqtt_topic_is_response/mqtt_data_callback` | The callback excludes topics ending in `/cmd/res` and forwards every other received payload to the command queue; it does not compare against a fixed command leaf | `SOURCE_PROVEN` | A server-owned canonical leaf, broker ACL policy, or end-to-end delivery for a chosen leaf |
| Response | `mqtt_service.c > mqtt_service_build_topics` | `<prefix>/<device_id>/cmd/res`; response topic suffix is recognized and not dispatched as a command | `SOURCE_PROVEN` | Response payload signature/version beyond existing response code |
| Capabilities | `mqtt_service.c > publish_capabilities_locked` | Capabilities retained, QoS 1 | `SOURCE_PROVEN` | Capabilities as authority (they are availability only) |
| Status | `mqtt-status-plane/src/status_plane.c > connected state publisher` | Status non-retained; connectivity/LWT retained, QoS 1 | `SOURCE_PROVEN` | Server state coalescing |
| Session | `mqtt-manager/mqtt_config.c > mqtt_config_get_defaults` plus `mqtt-manager/mqtt_manager.c > mqtt_reconnect_task/create_client` | Primary-only default, clean session, keepalive 30s, QoS 1, retain false, LWT QoS 1 retained; active manager disables ESP-MQTT auto-reconnect and uses a 1s wake debounce with 2,4,8,16,30s backoff capped at 30s and 20 attempts per cycle | `SOURCE_PROVEN` | Random jitter is not source-proven; runtime timing/soak behavior and server reconnect compatibility |
| Outbound publish path | `mqtt-message-publish/src/mqtt_message_publish.c`; `command-dispatcher/cmd_response.c`; `services/mqtt-service/mqtt_service.c` | Generic publisher accepts an explicit primary/secondary topic, rejects wildcard topics and retained messages in v1; command response publisher uses generated `<prefix>/<device_id>/cmd/res`, QoS 1, non-retained | `SOURCE_PROVEN + CONTRACT_TESTED` | Generic transport does not choose or prove a canonical server command leaf; server Release 1 remains `PRIMARY_ONLY` |
| Identity | `util-device-identity/src/device_identity.c > device_identity_get_id` | Active device ID is read from `enroll/device_info`; vendor identity is separate | `SOURCE_PROVEN` | Combined importer/setter |
| License binding | `hms-license/src/license_evaluator.c > device binding` | Nonempty signed device ID must match active NVS identity | `SOURCE_PROVEN` | Candidate identity first-enrollment path |
| License journal | `hms-license/src/license_api.c > license_apply` | Exact pending envelope may resume; durable post-marker failure cannot promise rollback | `SOURCE_PROVEN` | Full power-loss matrix on hardware |
| HTTP auth | `hms-http-api/auth/auth_manager.c > auth_middleware_handler` | Human token + permission middleware exists | `SOURCE_PROVEN` | Device-bound/local-only importer trust |
| HTTP/importer boundary | `hms-http-api/auth/{auth_manager,auth_handler}.c`; `hms-http-api/license/license_api.c`; `util-device-identity/src/device_identity.c` | Human Bearer/X-Auth-Token session auth, permission-protected `/api/license/apply`, 2,048-byte license body limit, and getter-only logical identity surface are source-proven | `SOURCE_PROVEN + CONTRACT_TESTED` | Device-bound bootstrap, import nonce/replay/locality/CSRF policy and identity setter are not present in the selected current firmware surface |

## New-contract boundaries

## Handler registration evidence

`fixtures/handler-registration-fixture.mjs` maps all 94 descriptor rows to the
12 current handler namespaces (`system`, `config`, `adc`, `network`, `mqtt`,
`broker`, `status`, `battery`, `modbus`, `ota`, `sms`, and `control`). The
fixture found a registered namespace and an action literal for all 94 rows.
`control` is registered by `services/do-service/do_service.c`; the other 11
namespaces are registered by `mqtt_cmd_handlers_register_all()`.

This closes only the namespace/action-presence part of the mapping. Payload and
response schemas, build/runtime conditions, actual readiness/capability, side
effects, and server policy remain separate evidence gates.

`fixtures/command-contract-fixture.mjs` now joins the same 94 rows with the
descriptor policy that is actually present in `cmd_descriptor.c`: command
class, literal permission or domain-policy flag, confirmation template,
framed-body acceptance, descriptor TTL, dry-run support, audit publication,
and deferred-outcome flag. It reports 94/94 descriptor rows and 94/94 mapped
handler rows. It intentionally leaves payload/response schemas,
build/runtime conditions, side effects, runtime readiness and server policy as
deferred rather than inferring them from a descriptor or action string.

`fixtures/action-source-evidence-fixture.mjs` then follows the registered
handler source for each of the 94 rows. The resulting
`fixtures/action-source-evidence.json` records the handler source digest,
action branch line, direct `data` field references, response field references,
and helper-call references. This is `CONTRACT_TESTED` static source evidence;
it does not prove requiredness, types, nesting, ranges, complete responses,
helper side effects, build conditions, or runtime readiness.

### Identity and license source evidence

`fixtures/identity-license-source-evidence-fixture.mjs` pins the current
identity/license boundary. The exported identity API contains getters only;
`device_identity_get_id()` reads `enroll/device_info/device_id`, while vendor
identity values are separate. The selected source tree has no exported or
implemented `device_identity_set/write/put` function and no direct literal
`nvs_set_*` write for `device_id`. The firmware comment explicitly leaves the
factory provisioning console write path out of scope for the current identity
component.

The same fixture records that `license_apply()` accepts an exact byte-for-byte
pending envelope, validates replay state, commits the journal record before
clock-clear finalization/publication, and that the storage reader requires
matching slot markers and sequence numbers. This is `CONTRACT_TESTED` source
evidence only. Candidate identity activation, importer bootstrap, power-loss
interruption, boot recovery, and post-marker behavior still require firmware
implementation/simulator/hardware tests.

### Build artifact drift evidence

`fixtures/catalog-build-artifact-fixture.mjs` inspected the available older
`HMS-IDF/build` object at revision
`b774170ff46c393eeb5e495ea37936038d3f4f4f` with object SHA-256
`0543c46dae94186df8c9972abff136f5d1c33f691184548ac3df1eef3c2d6590`. The
current 94-row catalog has 89 action strings present in that object and five
missing strings: `modbus.check_rtu_config`, `modbus.replace_rtu_config`,
`modbus.restart_rtu`, `system.get_operations`, and `system.set_ntp_servers`.
The generated result is `fixtures/catalog-build-artifact-report.json` with
status `BUILD_ARTIFACT_STRING_SET_DRIFT_DETECTED`.

This is useful drift evidence, not a build catalog proof: object string
presence cannot prove descriptor pair/order, payload schema, build condition,
or runtime behavior. The server must deny cross-revision catalog compatibility
until a reproducible build manifest or exported catalog is available and the
94 action rows have static branch/field-reference mapping, but complete
action-level schema, build-condition, side-effect and runtime mapping remains
required.

| Contract | Decision | Owner | Required proof | Release treatment |
|---|---|---|---|---|
| Command publish leaf | Firmware accepts any non-response topic delivered through the wildcard subscription, but it does not define a canonical server leaf. Do not invent one; add a fixture/ADR that selects and ACL-binds a server leaf, then proves end-to-end delivery. Until pinned, server returns `CONTRACT_UNAVAILABLE`. | Phase 1/3/5 | HMS-IDF integration fixture with broker trace and selected-leaf ACL test | Commands disabled until server leaf contract passes |
| Catalog binding | Pin a canonical descriptor snapshot with `catalogVersion`, source hash and catalog hash; compare a second build/source artifact; unknown hash denies. The available older object detects 89/94 string-set drift and cannot establish compatibility. | Phase 1/2/3 | `catalog-drift-fixture.mjs` + `catalog-build-artifact-fixture.mjs` + reproducible build manifest | No silent catalog fallback; cross-revision execution denied |
| Broker identity envelope | EMQX trusted rule/mTLS bridge supplies authenticated `clientId`, username, original topic, payload, receivedAt and messageId; backend fails closed. | Phase 1/3/8 | negative envelope/replay/rotation fixture | No state ingestion from raw callbacks |
| Import bootstrap | Device-bound one-time proof, nonce, expiry, bounded body, local-only authenticated route, CSRF policy and redacted response. | Phase 1/5 | actual HMS-IDF importer tests | Feature denied until implementation |
| Candidate identity | Stage candidate identity privately before license evaluation; active getter never exposes staged state; commit only as one staged-generation recovery protocol. | Phase 4/5 | NVS/evaluator fixture | First enrollment denied if seam absent |

## Command evidence matrix policy

The complete 94-row descriptor export is pinned in `fixtures/catalog-baseline.json`.
The server may enrich each row with permissions, dangerous policy and idempotency,
but it cannot add a firmware action. A row is executable only when all of these
are present: descriptor row, registered handler/readiness, build/runtime condition,
payload schema, server policy, keyset, and compatible catalog hash. Unknown or
stale rows are visible as unavailable and denied at the backend boundary.

This matrix is a pre-cook contract artifact. It does not claim an on-device build,
EMQX integration, importer implementation, or production signer.
