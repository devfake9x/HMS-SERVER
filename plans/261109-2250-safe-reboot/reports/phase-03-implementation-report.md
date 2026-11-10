# Phase 3 implementation report: safe reboot, simulator, tool, docs

Plan: `plans/261109-2250-safe-reboot/` | Phase: `phase-03-simulator-live-docs.md` | Branch: `claude/mqtt-device-connection-36f705` | Status: DONE_WITH_CONCERNS | No commits, plan and phase files untouched, `src/`, `tests/`, `frontend/`, `.claude/` untouched.

Gateway part: **deferred** (not touched; no connection to the lab broker). Scenario 8 (UI walkthrough, screenshots): the controller's, environment is up (see "Left running").

## Built

- `tools/device-reboot-check.js` (new): `--plan`, `--self-check`, `--preflight`, `--checks [--sim]`, `--dry-run`, `--reboot [--expect ...] [--correlation-id] [--window-s] [--tail-s]`, `--probe-in-progress --watch <id> [--after-s]`, `--after <before.json>`, `--restore-do <before.json>`. Pattern of `device-config-check.js` (cookie, CSRF, `tool-env.js`). Checks `/me/command-catalog` first (the three closed class F rows must be unavailable). Never resends `system.reboot`. Every probe refused before signing is a `dryRun:true`; live probes have no `confirm` and `{}` (`--self-check` asserts it).
- `tools/mqtt-broker/spec-device-simulator.js`: handler gets the envelope; `system.get_info` has `uptime_seconds` and `hostname`; channels have `forced`/`enabled`/`gpioOk`; `SIM_FORCED`; last will `{"state":"offline"}` retained on `status/connectivity` (keepalive 10) and `online` on connect; `control.get_status` returns `busy` + `retry_after_ms` for `SIM_DO_WARMUP_S` after boot; `mqtt.get_status` (with `config_trial`) and `clean_session` in `mqtt.get_config`; `system.reboot` (dry run first, throttle, confirm, `delay_ms` 0..60000, reply, drop like a chip reset with `reconnectPeriod: 0`, boot, reconnect); `SIM_BOOT_SECONDS`, `SIM_THROTTLE_S`, `SIM_THROTTLE_SURVIVES`, `SIM_REBOOT_NO_RETURN`, `SIM_REBOOT_NO_REPLY`, and one addition: `SIM_SILENT_REAL` (see deviations). Header comment lists the new env.
- Docs: `docs/architecture-mqtt-device-link.md` (33 -> 34, `FlashActions`, closed list and "Lệnh vẫn đóng" table, stale-reason row `device_reboot_scheduled`, replay-risk line, simulator pointer, new section "Khởi động lại an toàn"), `docs/architecture-device-mqtt-api-catalog.md` (34 open, reboot row, C2 modal section), `docs/project-roadmap.md` §5 (reboot row marked gateway-deferred, "Còn đóng" without reboot, total 34), `docs/journals/261109-safe-reboot.md`.
- Reports: `reports/live-verification.md`, `reports/affected-roles.md`, this file.

## Red first

Unmodified simulator, `--dry-run`: `FAIL dry run ... rejected DEVICE_REJECTED`, simulator `RES system.reboot success=false error=unknown_action`. `--reboot` on the old simulator stops earlier at `--preflight` (nothing sent). Details in `live-verification.md`.

## Scenarios (simulator 1-7, 9)

| # | Result | Key evidence |
|---|---|---|
| 1 red first | PASS (red as expected) | `unknown_action` at the dry run |
| 2 normal + probes | PASS | preflight 7/7; 6 probes refused before signing, simulator saw 0 of them; dry run `would_apply:true`, uptime `[17,25,31,37,43]`; reboot `SCHEDULED` 0 s -> `OBSERVED` 10 s; probe -> `DEVICE_REBOOT_IN_PROGRESS`; `--after` uptime 53 -> 0 |
| 3 no return | PASS | `NOT_OBSERVED` at 124 s, 2 reboot commands total (1 dry, 1 real); tail upgrade to `OBSERVED` (`late_s=48`) when the simulator came back |
| 4 no reply | PASS | `DEVICE_RESPONSE_TIMEOUT` 31 s; probe at 35 s -> `IN_PROGRESS`; `OBSERVED` (`reply=none`) at 124 s |
| 5 silent, no reboot | PASS | `DEVICE_RESPONSE_TIMEOUT` 33 s; row unchanged through the 122 s hold; uptime 151 (no reset) |
| 6 forced channels | PASS | DO1 ON -> OFF; DO2 forced ON stays ON; DO4 forced OFF stays OFF |
| 7 throttle | PASS | second reboot at +81 s: `DEVICE_REJECTED`/`UNAUTHORIZED`; dry run right after `Executed` (hold released) |
| 8 UI | controller | environment ready |
| 9 server restart | PASS | 404 `COMMAND_NOT_FOUND` after restart (in-memory); Postgres branch review-only |

Regression gate: `--plan` and `--self-check` ran with no login and sent nothing. I did not rerun dotnet or vitest suites: no `src/`, `tests/` or `frontend/` file changed in this phase (phase 1 and 2 counts stand: 633 unit, 123 integration, 458 vitest).

## Deviations

1. **Mosquitto instead of Aedes.** The Aedes broker (`tools/mqtt-broker/broker.js`) refuses the server (MQTT 3.1.1 vs MQTTnet 5.0). I ran the installed Mosquitto 2.1.2 as a separate loopback-only instance on 127.0.0.1:8088 with its own config in the scratchpad. The lab Mosquitto (port 443) was not touched or stopped.
2. **`SIM_SILENT_REAL` added** (not in the phase file). `SIM_SILENT=system.reboot` silences the dry run as well, so the tool stops at the dry run and the real command is never sent. `SIM_SILENT_REAL` silences only real commands, to test the "no reply, no reboot" row. Plain `SIM_SILENT=system.reboot` was not run (the UI's dry run would be its first casualty; expected `dry_run_failed`).
3. **Simulator throttle order:** dry run, then throttle, then confirm, then payload (admission checks before the handler, §3 L483). The phase file lists confirm first.
4. **Forced channels refuse writes** in the simulator (`ESP_ERR_INVALID_STATE`): my assumption, flagged in a comment; not in the spec text I used.
5. **Tool adjustments found during the run:** clock skew from the server's `reply.receivedAt` (poll delay no longer counted); "uptime shows a reset" check is clock-adjusted and prints whether the strict (UI) rule holds; tolerant polling for a server restart (re-login on 401); `--expect rejected|server_restarted`. One early FAIL in scenario 4 (strict uptime rule, 4 -> 113) was the tool, not the server; rerun passed.
6. **`DEVICE_REBOOT_TOO_SOON` was hit twice by my driver** (49 s and 56 s after the previous real reboot). Both refused before sending: extra proof of the 60 s gate.
7. **Scenario 6 dialog wording** (hold-text assertions for "DO1 sẽ TẮT", "DO4 force TẮT", "DO2 force BẬT có thể TẮT") is a UI check; not done here, covered by phase 2 vitest. The controller sees it in scenario 8.
8. **Gateway live and its preflight:** deferred; `live-verification.md` has the later steps.

## Open issues

- **Dev broker vs server:** `tools/mqtt-broker/broker.js` cannot serve the live server (MQTT 5.0). Needs a decision: set the protocol version in `MqttDeviceLinkService` (`src/`), or document Mosquitto as the dev broker, or swap the Aedes broker. Not touched here.
- **Integration suite environment** (from phase 1): needs `Mqtt__Enabled=false` because of the stale CA path in the Development user secrets.
- **`ota.get_status` shape is unknown** (spec gap). The tool's preflight FAILs unless the reply names an OTA state; the simulator replies with `ota.state:"idle"`. A real gateway may need the check adapted once the shape is known.
- **Heartbeat timing on the gateway** and the real `offline` behaviour are not measured.
- **Dev sessions die on API restart** (ephemeral data-protection keys): the UI will show the login page after any restart; relevant to the UI "server restarted" branch (the 404 only appears after signing in again).
- **Phase 2 note** (limitation 8): the preflight `control.get_status` read may close the confirm dialog once if an `outputs` event arrives after it opened; the controller should watch for it.
- Postgres branch of `TryCompleteAsync`/`FailUnfinished` still review-only.

## Left running for the controller (scenario 8)

All four are Windows processes started detached (`Start-Process`), logs and scripts in the scratchpad `C:/Users/toann/AppData/Local/Temp/claude/4159b4ee1fa27905/1d0ec659-3a4f-4354-a062-009a27bce72c/scratchpad/`.

| What | Where | PID | Stop |
|---|---|---|---|
| Local Mosquitto | 127.0.0.1:8088, anonymous, loopback | 29528 | `taskkill /PID 29528 /F` (never PID 34804: the lab broker) |
| API (Development, in-memory) | http://127.0.0.1:5099 | 17352 | `taskkill /PID 17352 /F` |
| Simulator `sim_001` | `hms_server_selftest/sim_001` | 18012 | `taskkill /PID 18012 /F` |
| Frontend dev server | http://127.0.0.1:5173 (proxies `/api`, `/hubs` to 5099) | 31596 | `taskkill /PID 31596 /F` |

Or all at once: `bash <scratchpad>/stop-all.sh` (note: its simulator kill matches any `node ... tools/mqtt-broker/spec-device-simulator.js` with a relative path, including another worktree's).

- **Device:** server GUID `870c870c-870c-870c-870c-870c870c870c` (seed device "MAITECH-RTU-870C", bound to `hms_server_selftest/sim_001` through environment overrides, not the lab gateway). Page: http://127.0.0.1:5173/devices/870c870c-870c-870c-870c-870c870c870c . Confirm phrase: `REBOOT-sim_001`.
- **Login:** the Development seed master-admin defined in `src/Hms.Server.Api/Program.cs` (lines 71-84; the tool reads it from `HMS_ADMIN_EMAIL` / `HMS_ADMIN_PASSWORD`, set in the scratchpad `env.sh`). Non-master accounts cannot see the button.
- **Simulator mode:** normal, `SIM_FORCED=2:on,4:off`, plus `SIM_BOOT_SECONDS=20` (default is 8) so "Đang khởi động lại…" lasts long enough to try the second tab, a relay change and an ADC rename. DO1 was set ON at 01:41 UTC through the API; the server shows outputs once the page reads `control.get_status`. Other modes: restart the simulator with the env from the table in `live-verification.md` (`SIM_REBOOT_NO_RETURN=1`, `SIM_REBOOT_NO_REPLY=1`, `SIM_SILENT_REAL=system.reboot`, `SIM_THROTTLE_S=90 SIM_THROTTLE_SURVIVES=1`).
- **Wait 60 s between real reboots** (`DEVICE_REBOOT_TOO_SOON`). The API is fresh (restarted at 01:40 UTC), so there is no gate state yet.
- **Commands used** (the key is in `sim-test-key.txt` in the scratchpad, never printed):

```bash
# broker (loopback only)
"C:/Program Files/mosquitto/mosquitto.exe" -c <scratchpad>/mosquitto-local.conf
# API: <scratchpad>/start-api.sh sets ASPNETCORE_ENVIRONMENT=Development, Mqtt__Enabled=true, Mqtt__Host=127.0.0.1,
#   Mqtt__Port=8088, Mqtt__UseTls=false, empty CA/username/password, Mqtt__ClientId=hms-server-sim and all fields of
#   Mqtt__Devices__0__* (DeviceId=870c..., TopicPrefix=hms_server_selftest, MqttDeviceId=sim_001, KeyVersion=1,
#   CommandKeyHex=<from sim-test-key.txt>, AllowedOutputChannels 1..6), then:
dotnet run --no-build --no-launch-profile --project src/Hms.Server.Api -- --urls http://127.0.0.1:5099
# simulator: <scratchpad>/start-sim.sh <log> SIM_FORCED=2:on,4:off SIM_BOOT_SECONDS=20
#   (sets SIM_MQTT_URL=mqtt://127.0.0.1:8088, SIM_TOPIC_BASE=hms_server_selftest/sim_001, SIM_KEY_FILE=<scratchpad>/sim-test-key.txt)
# frontend: <scratchpad>/start-vite.sh  (VITE_BACKEND_URL=http://127.0.0.1:5099, port 5173)
```

Verified before handing over: `GET /command-transport/status` -> `CONNECTED`, broker `127.0.0.1:8088`; `/link` -> `hms_server_selftest/sim_001`, online, broker connected; `allowed-commands` has `system.reboot`; the Vite proxy answers `/api/v1/auth/csrf` with 200.

## Files touched

- Modified: `tools/mqtt-broker/spec-device-simulator.js`, `docs/architecture-mqtt-device-link.md`, `docs/architecture-device-mqtt-api-catalog.md`, `docs/project-roadmap.md`.
- Created: `tools/device-reboot-check.js`, `docs/journals/261109-safe-reboot.md`, `plans/261109-2250-safe-reboot/reports/phase-03-implementation-report.md`, `reports/live-verification.md`, `reports/affected-roles.md`.
- Outside the repo: `tools/mqtt-broker/node_modules` (npm ci, gitignored), `tools/device-reads/*` (gitignored), scratchpad scripts and logs.

## Unresolved questions

- Fix the dev broker path (MQTT 5.0 vs Aedes) in `src/`, or document Mosquitto? User call.
- Should plain `SIM_SILENT=system.reboot` (dry run silent) also be run through the UI? The controller can do it in scenario 8 with a simulator restart.

## Fixes after code review

All in `tools/` and `docs/`; no `src/`, `tests/`, `frontend/`.

1. **M2, "not sent" rule.** `refused()` in `tools/device-reboot-check.js` now follows the `certainlyNotSent` precedent: HTTP 4xx, `ContractUnavailable`, `TransportNotConfigured`, the never-sent codes, or `Failed` with a known pre-signing code (master-admin, permission, confirm, payload, gate, busy lane, catalog, closed row). `Failed/MQTT_PUBLISH_UNCONFIRMED` and every other answer keep the row followed (the tool says so) and never resend. `--self-check` has 9 cases for it (incl. `MQTT_PUBLISH_UNCONFIRMED`, `DEVICE_REJECTED`, `DEVICE_RESPONSE_TIMEOUT` = not refused).
2. **M3, busy shape.** Simulator: `{error:'ESP_ERR_INVALID_STATE', data:{error:'busy', retry_after_ms:1000}}`. Server mapping verified in `DeviceLinkHandlers.cs` (`reason = "{error}: {data.error}"`), so the detail is `ESP_ERR_INVALID_STATE: busy`. Tool retry regex is now `/(^|:\s*)(LANE_FULL|busy|UNAUTHORIZED)\b/i` (matches `busy`, `LANE_FULL`, `UNAUTHORIZED` and the spec shape; `--self-check` tests it, and that `ESP_ERR_INVALID_STATE: channel_forced` does not retry). The same anchored regex in `frontend/src/core/commands/useDeviceReboot.ts:135` is for the frontend side (not touched).
3. **L7.** Forced-channel write refusal is `ESP_ERR_INVALID_STATE` with `data.error:"channel_forced"` (spec L828); wrong comment fixed.
4. **L8, L9, contract note, L5, L6** in `docs/architecture-mqtt-device-link.md` (section "Khởi động lại an toàn"): early-release list now includes rebind and revoke; audit-reader note that a `Failed` reboot row can later become `Executed/DEVICE_REBOOT_OBSERVED` (`reply=none`); L5 and L6 added to the risk list; the deferred gateway conditions now include the firmware-version caveat (spec L850, 2026-11-08 change).
5. **L9 in the tool.** `--preflight` prints the firmware version from `system.get_info` and a WARN about the 2026-11-08 force-OFF change; the version is stored in the before-file (`info.firmware`).

Re-run (API restarted by the server agent as PID 2644; I waited for `/command-transport/status` = CONNECTED and `/link` = `sim_001` online before each run):
- Scenario 2 (`--reboot --sim`): PASS. `OBSERVED` at 10 s, signed uptime 163 -> 0. The first `control.get_status` after the boot got the spec-shaped busy (`ESP_ERR_INVALID_STATE`, sim log 02:16:57), the tool retried and read the table at 02:16:59; all channels OFF.
- Scenario 6 (`SIM_FORCED=2:on,4:off`, DO1 ON): PASS. Same busy then retry (02:18:30 -> 02:18:32); DO1 OFF, DO2 ON, DO4 OFF; uptime 56 -> 0.
- One attempt hit `DEVICE_REBOOT_TOO_SOON` (my timing after the previous run), refused before sending; rerun after 60 s.
- `--self-check` PASS, `--plan` unchanged.

Left running: Mosquitto 29528, Vite 31596, API 2644 (restarted by the server agent), simulator normal `SIM_FORCED=2:on,4:off SIM_BOOT_SECONDS=20` (new PID in the hand-back message), DO1 ON.
