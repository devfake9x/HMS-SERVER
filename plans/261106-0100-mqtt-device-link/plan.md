---
status: done (signed round trip with the real gateway waits for its command key)
created: 2026-11-06
contract: HMS-IDF/engineering-docs/api/mqtt-api.md (snapshot 2026-10-28) + http-api.md /api/security/keys
supersedes: plans/261001-0030-full-hardware-mqtt-apis-c2/phase-02-live-mqtt-transport.md (its envelope is not the spec's)
---

# MQTT device link — live transport, signed commands, inbound status

Scout: `plans/reports/261106-0033-scout-mqtt-device-connection.md`. The user lifted the Q14 MQTT pause on 2026-11-06 and delegated design choices ("tự duyệt theo khuyến nghị").

## Decisions (self-approved, with reason)

| # | Decision | Why |
|---|---|---|
| D1 | MQTTnet 5 client lives only in `Infrastructure/Mqtt`; Application gets ports (binding store, link state, pending registry, response codec) | ADR 260927 modular monolith: no MQTT types outside Infrastructure |
| D2 | Topic identity per device = `DeviceMqttBinding(TopicPrefix, MqttDeviceId, KeyVersion?)`, set by admin API or dev config; never assumed | Spec §1/§2: `B = <topic_prefix>/<device_id>`, "do not assume `hms`" |
| D3 | Command key = the existing per-`KeyGeneration` keyset. New: import an existing device key (rotates `KeyGeneration`, fences pending commands). The lab device key is **not** rotated | Spec says key comes from admin/HTTP channel; the lab gateway key is shared with the firmware team's HIL oracles, rotating it would break them |
| D4 | Envelope: `type,id,ts,ttl_ms[,key_ver][,confirm][,dry_run],data{action[,data]}` + `sig` last, HMAC-SHA256 raw UTF-8, ≤1023 B; topic `B/cmd/<type>`, QoS1, retain=false | Spec §3/§4 |
| D5 | `confirm` must equal the §7 template with the MQTT device id (`<DEVID>` → `gateway_870c`), else rejected before publish | Device checks the exact token; GUID would never match |
| D6 | `cmd/res` correlated by `id` against server-pending commands only (other clients share the topic); verify `res:` HMAC with the pending command's key; unknown ids ignored | Spec R2, §4.1; firmware HIL tools publish on the same base |
| D7 | Outcome mapping: success→`Executed`; `accepted+pending`→`Acknowledged`; else `Failed/DEVICE_REJECTED`; no reply in 30 s→`Failed/DEVICE_RESPONSE_TIMEOUT` (outcome unknown). Audit never lets `Published` overwrite a device outcome | Spec §5, R4 |
| D8 | Enabled actions = spec snapshot `SLICE_PASS` reads + `system.ping` + `system.get_operations`; all others stay `DeniedUntilActionContract`. Runtime cross-check with retained `B/capabilities.available` | Spec "API đã đóng"; capabilities only advertise handlers |
| D9 | Inbound: `B/status` → telemetry store + SignalR `status`; `B/status/connectivity` → link state + `connectivity`; `B/capabilities` → link state; `B/status/events` → `device_event`. No lifecycle change from MQTT (lab broker is anonymous) | Spec §2/§10 |
| D10 | Telemetry SIGN/ENCRYPT (§9) out of scope: device has no `tlm_*` keys today | `GET /api/security/keys` 2026-11-06 |

## Phases

1. Ports + codec + adapter + correlator (Application/Infrastructure) — tests first for the codec.
2. MQTTnet connection service, transport, inbound routing, config, DI, API endpoints.
3. Live check on the lab broker (`192.168.1.101:443`, `hms_toan_lab/gateway_870c`).
4. Docs: architecture note + spec gaps list.

## Acceptance

- Unit tests: envelope bytes + HMAC match an independent implementation; response HMAC with `res:`; size bound; confirm templating; correlator outcomes + timeout; inbound status → telemetry.
- Live: server subscribed on lab broker shows the real heartbeat via `GET /api/v1/devices/{id}/telemetry` and link state online.
- Live signed command round-trip requires the gateway's command key (import endpoint); without it the API reports `COMMAND_KEY_NOT_PROVISIONED` before publishing.

## Spec gaps found (keep for the user; do not send to firmware unless asked)

- G-a: key `fingerprint` algorithm in `GET /api/security/keys` not specified → server cannot verify an imported key matches the device.
- G-b: no MQTT-side way to learn `key_version`; only HTTP `GET /api/security/keys`.
- G-c: server broker identity/ACL template not specified (lab broker is anonymous).
