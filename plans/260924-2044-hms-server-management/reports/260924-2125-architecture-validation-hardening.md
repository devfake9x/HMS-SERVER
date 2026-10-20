# Architecture validation hardening

Date: 2026-09-24  
Plan: `plans/260924-2044-hms-server-management/`  
Status: conditionally ready for implementation planning; production code is not started.

## Decision

Adopt the Production Control Plane v1 boundary:

- ASP.NET Core 10 modular monolith, Vue 3, PostgreSQL/EF Core, EMQX, and SignalR remain the approved stack.
- Release 1 provisions `PRIMARY_ONLY`; unsupported `SECONDARY_ONLY`, `BOTH`, and `FAILOVER` modes fail explicitly.
- No telemetry ingestion, historical sensor storage, Redis Streams, Kafka, SCADA, digital twin, OTA artifacts, browser MQTT, client-side signing, or multi-region active-active is added.
- Firmware-derived facts remain authoritative. Any importer bootstrap, broker-session envelope, command catalog manifest, or topic behavior not proven in HMS-IDF is a new contract gated by a fixture.

## Firmware evidence and contract status

| Boundary | Firmware evidence | Plan treatment | Proof required | Risk if unresolved |
|---|---|---|---|---|
| MQTT broker modes | `components/mqtt-manager/mqtt_config.c:34` defines `PRIMARY_ONLY`, `SECONDARY_ONLY`, `BOTH`, and `FAILOVER`; defaults select primary-only. | Release 1 provisions primary-only and rejects other modes explicitly. | Primary-only bundle/ACL fixture and unsupported-mode negative test. | Silent secondary-field loss could strand a device on an unintended broker. |
| MQTT session defaults | `components/mqtt-manager/mqtt_config.c:49-51,62-72` defaults keepalive 30s, session QoS 1, retain false, clean session true, and LWT QoS 1/retain true; `mqtt_manager.c:1045-1072` validates LWT. | Defaults are evidence only; per-topic command/status behavior remains fixture-gated. | Topic/QoS/retain/reconnect fixture from `services/mqtt-service/mqtt_service.c` and status-plane. | Wrong topic flags can lose readiness state or cause duplicate command effects. |
| Topic identity | `components/mqtt-manager/mqtt_config.c:62-72` and `mqtt_manager.c:1370-1405` store/build prefix and device ID topics. | Server validates topic namespace, but does not infer authenticated identity from topic alone. | Trusted EMQX broker-session envelope with client ID, username, topic, timestamp, message ID. | Topic spoofing could make one device's state or response appear to belong to another. |
| Command envelope | `components/command-dispatcher/cmd_security.c:52-56,329-350,431-443,678-690` proves direct HMAC-SHA256 with a 32-byte key, raw JSON 1..1023, final `sig` marker, strict fields (`type`, `id`, `ts`, `data`, optional `key_ver`, `ttl_ms`, `dry_run`, `confirm`, `response_topic`, `trace_id`), required timestamp, and active key-version check; unknown `kid`/`generation` fields are rejected. `command_dispatcher.c:433-447,652-680` proves max TTL 24h, default 120s, and 60s future skew. `cmd_frame.h/c` and `mqtt_service.c` additionally prove a separate framed body path with a 1,023-byte header, 131,072-byte whole-message limit and `accepts_body` gating. | Server preserves exact signed header bytes, maps wire `id` to device dedup/correlation, emits `key_ver` when rotating keys, keeps server metadata outside firmware JSON, and only uses the framed body contract for the two source-proven body actions. | Shared golden/framed vectors, field allowlist, signed-header boundary, framed whole-message/body boundary, TTL, retry/dedup, expiry, and key-version fence tests. | Treating 1,023 as the whole MQTT message would reject valid framed Modbus operations; treating every large message as framed would bypass body/action gating. |
| Command catalog | `components/command-dispatcher/cmd_descriptor.c` contains the firmware command manifest and descriptor metadata; generated capability JSON is emitted around descriptor generation. | Server catalog is generated/pinned with version/hash and unknown hash denies execution. | Descriptor extraction/build-manifest drift test. | Catalog drift can expose actions the running firmware cannot safely parse. |
| Device identity writes | `components/util-device-identity/src/device_identity.c:19-116` exposes stored identity getters; no complete combined importer/setter was found. | Add explicit write/prepare/activate adapters in a new `hms-provisioning` component. | HMS-IDF setter API and importer fixture. | Partial writes could activate mismatched identity, credentials, and keys. |
| License verification | `components/hms-license/include/hms_license.h:13,110-135`, `components/hms-license/src/license_api.c:567-588,642-677`, `components/hms-license/src/license_evaluator.c:97-109`, and `components/hms-license/src/license_storage.c:126-176,315+` prove two-slot journaling, durable nonce/counter checks, candidate binding against the active `device_id`, a durable commit before a possible later clock-clear failure, exact-pending resume, and fail-closed conflict handling. | Server/importer uses candidate-aware validation for first enrollment, records `LICENSE_PENDING` with target/prior hashes, resumes only exact pending, and enters `recovery-required` after an unsupported post-marker failure; it never promises unconditional rollback or blindly replays a committed envelope. | Power-loss matrix around every write/marker, candidate identity fixture, exact-pending resume, post-marker recovery, and `license_apply()`. | A bad recovery rule could destroy the last valid license, replay a consumed nonce, or activate a restricted generation silently. |
| Device identity writes | `components/util-device-identity/src/device_identity.c:19-116` and `include/device_identity.h` expose active getters for `enroll/device_info` and vendor identity only; no combined setter/importer exists. `license_evaluator.c:97-109` rejects a signed nonempty device ID when active NVS identity is empty/mismatched. | Add a candidate-identity validation seam under the importer lock, stage identity before license evaluation, and never expose staged values through active getters during dry-run. | First-enrollment candidate binding fixture, staged/active getter separation, and boot-recovery ordering test. | Without the seam, the first signed license cannot validate its target device ID, or the firmware may accidentally expose uncommitted identity. |
| HTTP importer auth | `components/hms-http-api/auth/auth_manager.c:1928-1935` and `components/hms-http-api/license/license_api.c:311-355` prove human permission middleware (`auth_require`) for license routes; they do not prove local-only transport, device bootstrap, CSRF, or replay protection. | Use a separate device-bound one-time bootstrap proof, nonce, expiry, locality and CSRF contract; human admin passwords never enter bundles. | New firmware auth/import fixture; do not claim current support until it passes. | Reusing human auth or trusting a remote caller could turn provisioning into a device takeover path. |

## Required plan changes applied

- Phase 1 now owns the broker-session envelope, importer bootstrap schema, command catalog manifest, license trust-store schema, load profile, KMS/Data Protection boundary, SignalR epoch ordering, backpressure contract, and Phase 2/3 keyset seam.
- Phase 2 now owns deterministic test enrollment/keyset fixtures with fake 32-byte keys and catalog source/hash drift detection.
- Phase 3 now consumes only trusted broker-session envelopes, fails closed on missing identity metadata, coalesces latest state, bounds queues, and tests authorization epochs.
- Phase 4 now rejects missing/ephemeral secret roots, raw PEM, unknown/retired/rollback `kid`, and documents KMS/Data Protection/backup recovery.
- Phase 5 now separates importer bootstrap trust from human auth, requires challenge/token plus nonce/expiry/replay/locality checks, and marks unsupported firmware behavior as a new contract.
- Phase 7 now documents operator-controlled filesystem persistence and tests response metadata, safe filenames, audit completion, and stale authorization epochs.
- Phase 8 now uses firmware-derived retained/QoS behavior, records webhook QPS/cache/broker/DB/queue capacity, tests primary-only rejection, adds RTO/RPO/backup/restore and N-to-N+1 compatibility, and requires a two-replica soak before claiming HA.

## Firmware-proven MQTT topic map

The active firmware source proves the following current topic behavior: `services/mqtt-service/mqtt_service.c:80-96,380-381,563-564,612` builds `<prefix>/<device_id>/cmd/#`, `/cmd/res`, `/audit`, and `/capabilities`; command subscription and command responses use QoS 1, responses are non-retained, audit is non-retained, and capabilities are retained. `components/mqtt-status-plane/src/status_plane.c:62-63,178-180` publishes `/status` QoS 1 non-retained and `/status/connectivity` QoS 1 retained for online state; `mqtt_manager.c:1059-1067` validates the offline LWT on the same connectivity suffix. The plan treats these values as the current fixture baseline while retaining a drift test for future firmware changes.

## Handoff gate

The plan is not ready to cook until these P0/P1 gates have evidence:

1. Importer bootstrap proof/locality and replay fixture.
2. Candidate-identity license binding and active/staged getter separation fixture.
3. Exact `license_apply()` journal recovery fixture, including exact-pending resume and post-marker `recovery-required` behavior.
4. Trusted EMQX broker-session identity propagation fixture.
5. Firmware command catalog extraction/hash drift fixture.
6. Exact command topic/QoS/retain/reconnect fixture.
7. KMS/Data Protection and signer trust-store fail-closed tests.
8. SignalR authorization epoch, revoke race, coalescing and bounded queue tests.
9. Firmware-derived retained-state load profile and capacity model.

If a firmware fixture cannot be produced in this release, the corresponding feature must be explicitly marked unsupported and denied at the server boundary; it must not be silently approximated.
