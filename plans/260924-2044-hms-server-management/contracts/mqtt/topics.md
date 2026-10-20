---
title: "HMS-SERVER MQTT topic contract"
type: contract-spec
date: 2026-09-25
status: source-proven-topics-plus-new-publish-leaf-gate
---

# MQTT Topic Contract

## Purpose

This file separates topic behavior proven by the current HMS-IDF source from the
server-owned publish-leaf and ACL contract that firmware does not choose.

## Source-proven topic facts

Source anchors:

- `D:/WORKSPACE/PROJECT/HMS-IDF/services/mqtt-service/mqtt_service.c`
- `D:/WORKSPACE/PROJECT/HMS-IDF/components/mqtt-status-plane/src/status_plane.c`
- `D:/WORKSPACE/PROJECT/HMS-IDF/components/mqtt-manager/mqtt_config.c`
- `D:/WORKSPACE/PROJECT/HMS-IDF/components/mqtt-manager/mqtt_manager.c`

| Topic | Direction | QoS | Retain | Evidence |
|---|---|---:|---:|---|
| `<prefix>/<device_id>/cmd/#` | device subscription filter | 1 | n/a | `SOURCE_PROVEN` |
| `<prefix>/<device_id>/cmd/res` | device response | 1 | false | `SOURCE_PROVEN` |
| `<prefix>/<device_id>/audit` | device publication | 1 | false | `SOURCE_PROVEN` |
| `<prefix>/<device_id>/capabilities` | device publication | 1 | true | `SOURCE_PROVEN` |
| `<prefix>/<device_id>/status` | device publication | 1 | false | `SOURCE_PROVEN` |
| `<prefix>/<device_id>/status/connectivity` | LWT/online state | 1 | true | `SOURCE_PROVEN` |

The firmware callback excludes the response suffix `/cmd/res` and forwards every
other received payload under the wildcard command filter to the command queue.
It does not select a canonical server publish leaf.

## Session and reconnect facts

- Release 1 contract is `PRIMARY_ONLY`.
- `mqtt_config.c` initializes a 30-second keepalive, but
  `mqtt_manager.c` passes 120 seconds when no protobuf session configuration is
  present. The effective value is therefore unresolved until a resolved-config
  fixture proves precedence; the server must not hard-code 30 seconds.
- Default session QoS is 1.
- Default retain is false.
- Clean session is true.
- LWT QoS is 1 and LWT retain is true.
- Firmware disables ESP-MQTT auto-reconnect.
- The active reconnect task contains a one-second wake debounce and an
  exponential 2/4/8/16/30-second backoff sequence capped at 30 seconds, with 20
  attempts per cycle. This is source evidence for the task path, not proof of
  broker timing, jitter, SUBACK acceptance or resolved-config precedence.
- Random jitter is not source-proven and must not be treated as a measured fact.
- The inspected source does not prove a broker `SUBACK` acceptance path; a
  subscribe API return is not equivalent to a broker acknowledgment.
- LWT topic, payload, QoS and retain are loaded from resolved protobuf topic
  configuration. The source default is not a canonical deployed LWT contract;
  a resolved-config fixture is required.
- Connected ordering is status-topic update, topic/response/audit/descriptor
  rebuild and subscription, retained capabilities publication, then status-plane
  connect.

## Server publish leaf gate

`<prefix>/<device_id>/cmd/#` is an ingress filter, not a server publish leaf.
The server must not guess `/cmd/execute` or any other leaf. A selected leaf is a
new server/firmware integration contract and requires:

1. exact rendering from the configured prefix and device ID;
2. EMQX ACL proof that only the authenticated device namespace can receive it;
3. broker trace proving the leaf reaches the firmware dispatcher;
4. rejection of `/cmd/res`, wildcard, empty and cross-device variants;
5. negative topic/client/enrollment identity tests.

Until that fixture passes, command submission returns `CONTRACT_UNAVAILABLE` and
performs zero MQTT publish calls.

## Non-goals and limitations

- This file does not claim a live EMQX broker, ACL, webhook or bridge result.
- Per-topic behavior is not inferred from session defaults.
- No retained-message count or device-count constant is introduced.
- The configured `<prefix>` remains a firmware/server configuration value; `hms`
  is not forced by this contract.

## Verification

- Pre-cook: `fixtures/mqtt-topic-fixture.mjs` and
  `fixtures/mqtt-publish-path-evidence-fixture.mjs`.
- The publish-path fixture separately proves that the firmware generic
  outbound publisher accepts an explicit topic, rejects wildcard topics and
  retained messages in its v1 API, while command responses are wired to the
  generated `/cmd/res` topic. The firmware API exposes primary/secondary
  targets, but the server Release 1 policy remains `PRIMARY_ONLY`; this is
  transport evidence only and does not select a server command leaf.
- Required before command enablement: selected-leaf/ACL integration fixture in
  Phase 3 and live broker negative tests in Phase 8.
