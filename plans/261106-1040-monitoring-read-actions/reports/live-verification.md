# Phase 3: live verification (2026-11-06)

Environment: `hms-api` (Development, in-memory DB) and `hms-frontend` on this worktree; lab Mosquitto TLS
`192.168.1.101:443`; real gateway `hms_toan_lab/gateway_870c`, firmware `1.0.108` (compiled 2026-11-05),
command key generation 3 (key changed by the user earlier that day, re-imported through the dev API).
Tool: `tools/device-read-check.js` (repo), full replies saved under `tools/device-reads/` (gitignored).

## The 15 reads on the real gateway

All `Executed`, about 1 s each, replies verified ("res:" HMAC) by the server.

| Action | Data | Against spec §6 (pin `83e93d77e`) |
|---|---|---|
| `system.get_time` | 189 B | Matches; extra `ntp_servers`, `ntp_mode` |
| `system.get_hostname` | 26 B | Matches |
| `system.get_device_id` | 54 B | Matches (`device_id`, `mac`) |
| `ota.get_status` | 416 B | **Gap:** `data` is `{success, data:{firmware, partition, ota, rollback}}`, one level deeper than other reads; spec names no fields |
| `network.get_priority` | 78 B | Matches |
| `network.get_ethernet` | 144 B | Matches; extra `gateway_type` |
| `network.get_modem` | 148 B | Same shape as `get_ethernet` (`type, connected, active, ip, gateway, netmask, gateway_type`); spec §6.5 only says "projection" |
| `network.wifi_sta_status` | 35 B | Matches (`enabled`, `connected` only while disabled) |
| `network.wifi_ap_status` | 139 B | **Gap:** with AP off there is no `channel`/`client_count`; extra `password_set`, `autoOffMinutes`, `timerRunning`, `timerRemainingMs` |
| `battery.get_status` | 243 B | Matches (battery present: ~7.76 V, ~60 %) |
| `battery.get_alerts` | 136 B | Matches |
| `adc.get_config` | 122 B | Matches; `hardware` is an object (`num_devices`, `addr0/1`, `shunt0/1`, `gain`) |
| `adc.get_channels` | 433 B | Matches: array of 8 `{ai,name,type,enabled}` |
| `adc.get_status` | 1391 B | **Gap:** channels do carry `ai` (spec lists value/unit/quality/raw only) |
| `adc.get_values` | 593 B | Matches: array of 8 `{ai,value,unit,quality}`; lab has no sensors wired (`WIRE_BREAK`) |

The cards now unwrap the OTA reply and label its fields, show `ntp_mode`, round long fractions to 4
significant digits, and show `last_sync` as a date. Fixture from these replies, anonymised (device id, MAC,
SSID, carrier-assigned IPs replaced), is `frontend/src/features/devices/components/reads/fixtures/lab-gateway-replies.json`.

## Safety checks on the device

- **Read cap:** 6 parallel `system.get_info` from one user → 2 `Executed`, 4 `ContractUnavailable/DEVICE_READ_BUSY` in ~10 ms (one user holds at most half of the 4 slots); none dropped by the device.
- **Three cards at once (UI):** 15 reads finished in 8 s with no error; the busy refusal was retried once after 2 s.
- **TTL and command traffic:** every read on the wire carried `ttl_ms: 20000`; see the sniffer section below.
- **Realtime:** no `command_result` event for reads (unit/integration tested); "Last Heartbeat" moved only with heartbeats during the run.

## Sniffer on `hms_toan_lab/gateway_870c/cmd/#` (10:27–10:35Z)

| Topic | Messages | Accounted for by |
|---|---|---|
| `cmd/system` | 9 | key check `get_info` 1, tool 3, burst 2 published (4 refused never left the server), UI 3 |
| `cmd/control` | 1 | key check `get_status` only: no `set_output`/`pulse` |
| `cmd/ota` | 2 | tool 1, UI 1 |
| `cmd/network` | 10 | tool 5, UI 5 |
| `cmd/battery` | 6 | tool 2, UI 2, UI after reload 2 |
| `cmd/adc` | 10 | tool 4, UI 3, UI after reload 3 |

Every read on the wire carried `ttl_ms: 20000` (reads; relay writes keep 10 000). No write command was sent.

## Screenshots

- `screens/power-adc-card-lab-gateway.jpg` (no device identifiers on this card). Screenshots of the device/time
  and network cards show the real MAC and SSID and are not kept in the repo.
