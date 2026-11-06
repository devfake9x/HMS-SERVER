# Phase 4: live verification (2026-11-06)

Environment: `hms-api` (Development, EF in-memory DB) + `hms-frontend` on this worktree; lab Mosquitto TLS
`192.168.1.101:443`. Real device `hms_toan_lab/gateway_870c` (key gen 2, firmware key_ver 12). Simulator
`tools/mqtt-broker/spec-device-simulator.js` bound as `hms_server_selftest/sim_001`. The firmware team was told
before DO1 was switched (user confirmation, 2026-11-06).

## Real gateway (only DO1 allowed)

| # | Step | Result |
|---|------|--------|
| — | Allowlist empty after start (no seed value) | `set_output` DO1 → `ContractUnavailable DO_CHANNEL_NOT_ALLOWED` |
| 1 | Panel "Đọc trạng thái" | "Chưa đọc trạng thái" → 6 channels, all `toggle`, all OFF. Only DO1 has a button |
| 2 | Panel DO1 Bật | Chờ thiết bị → Executed → stale "vừa có lệnh đổi relay" → read back → **ON** (≈2 s), unlocked after read-back |
| 3 | Panel DO1 Tắt | → stale → read back → **OFF** (≈2 s) |
| 4 | API `set_output` DO2 | `DO_CHANNEL_NOT_ALLOWED`, refused before signing |
| 5 | API `pulse` DO1 (toggle mode) | `Failed DEVICE_REJECTED`, device error `ESP_ERR_INVALID_ARG` "Channel is in toggle mode, use set_output". State → stale `output_command_outcome_unknown`; `get_status` read back → fresh, DO1 OFF, `pulseCount 0` |
| 6 | Allowlist `[]` then `set_output` DO1 | `DO_CHANNEL_NOT_ALLOWED`; allowlist reset to `[1]` |
| end | DO1 | **OFF**, read back at 02:14:45Z; allowlist closed again (`[]`) |

Sniffer on `hms_toan_lab/gateway_870c/cmd/#` for the whole run (02:13:30–02:18:30Z): **9** commands on `cmd/control` and 9
replies on `cmd/res`, exactly the 5 `get_status`, 2 `set_output` (DO1) and 2 rejected `pulse` (DO1) sent. None of the
refused commands (DO2, and DO1 with an empty allowlist) was published; an earlier read-only pass refused DO2 and
DO6 the same way, also with nothing on the wire. A final `get_status` confirms it from the
device side: DO1 `totalCommands 2`, `pulseCount 0`; DO2–DO6 `totalCommands 0`; all six OFF.

Note for the firmware docs: §6.4 says pulse needs pulse mode but does not name the error code; the device
answers `ESP_ERR_INVALID_ARG` with `data.error` text. The simulator now does the same.

## Simulator (6 channels, DO6 in pulse mode)

| Step | Result |
|------|--------|
| Fail-closed allowlist | `set_output` before any allowlist → `DO_CHANNEL_NOT_ALLOWED` |
| ON / read back / OFF | DO3 ON → stale `output_command_executed` → `get_status` → ON; OFF the same |
| Pulse DO6 1500 ms | Executed, read inside the pulse shows ON · đang xung, second read after duration + 1 s shows OFF |
| Pulse on a toggle channel | `DEVICE_REJECTED` |
| Bad payloads | `value:"1"`, `duration_ms:70000` → `COMMAND_PAYLOAD_INVALID`; channel 7 → `DO_CHANNEL_NOT_ALLOWED`; none reached the simulator |
| 7: silent device | `SIM_SILENT=control.set_output`: DO2 stays locked ("Chờ thiết bị…") for the 30 s server timeout, `DEVICE_RESPONSE_TIMEOUT`, automatic read-back, unlock, DO2 OFF |
| 8: restart mid-command | Not live: dev runs on the in-memory DB, so a restart loses the audit rows. Covered by `PhysicalOutputSafetyTests.Commands_a_previous_run_left_unfinished_become_outcome_unknown` and `PersistenceModelTests.Ef_command_audit_fails_what_a_previous_run_left_unfinished_and_keeps_device_outcomes` |

## Found while testing, fixed

- The DO panel sat inside a card shown only after telemetry arrived, so a bound device without a heartbeat had
  no relay control. The card now also shows for any device with an MQTT binding.
- Long channel errors broke letter by letter inside the narrow cell; errors now list under the grid.
- The simulator sent no heartbeat, so the server (correctly) treated it as offline; it now publishes
  `<base>/status` every 5 s.

## Screenshots (`screens/`)

- `gateway-step1-read.jpg`, `gateway-step2-do1-on.png`, `gateway-step3-do1-off-final.png`
- `sim-step7-timeout-readback.png` (before the error-list layout fix), `sim-do3-on-do6-pulse.jpg`,
  `sim-panel-after-review-fixes.png`
