# Live verification: safe reboot (`system.reboot`)

Date: 2026-11-10. Scope: **simulator only**. The gateway part is deferred (see the last section).

## Setup

| | |
|---|---|
| Device | spec simulator `hms_server_selftest/sim_001`, bound to the seed device `870c870c-870c-870c-870c-870c870c870c` |
| Broker | **local Mosquitto 2.1.2 on 127.0.0.1:8088**, anonymous, loopback only. The lab broker (192.168.1.101) was not touched; nothing connected to or subscribed on `hms_toan_lab/#` |
| Server | `Hms.Server.Api` on 127.0.0.1:5099, Development, in-memory store, `Mqtt__Host=127.0.0.1`, `Mqtt__Port=8088`, `Mqtt__UseTls=false`, all MQTT settings overridden by environment variables; `GET /command-transport/status` showed `broker: 127.0.0.1:8088`, `CONNECTED`; `/link` showed `hms_server_selftest/sim_001` before the first command |
| Key | fresh random 64-hex test key in the session scratchpad, used for both the simulator and the server binding; never in the repo |
| Account | the Development seed master-admin (tracked in `Program.cs`), through `HMS_ADMIN_EMAIL` / `HMS_ADMIN_PASSWORD` |
| Tool | `tools/device-reboot-check.js` (probes, dry run, reboot, follow, `--after`) |
| Reports | `tools/device-reads/*-reboot-report-*.json` and `*-reboot-before.json` (gitignored) |

**Broker change.** `tools/mqtt-broker/broker.js` (Aedes) cannot be used here: it speaks MQTT 3.1.1 only and the server's MQTTnet 5 client connects with 5.0, so Aedes answers `unacceptable protocol version` and the server logs `MQTT connect refused: 1` in a loop (broker log: `Connection Error [null]: unacceptable protocol version`). I ran a loopback-only Mosquitto instead (`listener 8088 127.0.0.1`, `allow_anonymous true`, no persistence). Not fixed in `src/`; the repo's dev broker is unusable with the live server until that is decided (open issue in the phase report).

## Red first (unmodified simulator)

`node tools/device-reboot-check.js --device <guid> --confirm-live --dry-run` against the simulator before any change:

- `FAIL  dry run executed (would_apply:true)  rejected DEVICE_REJECTED`; the simulator logged `RES system.reboot success=false error=unknown_action` (one dry run, harmless).
- `--reboot` on the same simulator stopped earlier, at `--preflight` (no `uptime_seconds`, no `clean_session`, no `config_trial`): nothing was sent. The phase file expected the failure at the dry run; with the preflight in front, `--dry-run` is the mode that reaches it.

## Scenarios (simulator)

Timings are seconds after the real reboot was published, from the tool's poll of `GET /commands/{id}` (2 s granularity) and `GET /link` (1 s).

| # | Scenario | Result | Evidence |
|---|---|---|---|
| 2a | `--preflight` | PASS | all 7 checks: link online, signed `uptime_seconds`, relay table, `clean_session`, clock within 5 s, `config_trial.state="none"`, OTA idle |
| 2b | `--checks --sim` | PASS | 6 probes, all `dryRun:true`, refused before signing: `CONFIRM_REQUIRED`, `DENIED_UNTIL_ACTION_CONTRACT` x3 (factory_reset, ota.apply, ota.remote_start), `CONFIRM_MISMATCH`, `COMMAND_PAYLOAD_INVALID`. Simulator log: 0 `system.reboot` commands from them |
| 2c | `--dry-run` | PASS | `Executed` with `would_apply:true`; signed uptime over 20 s: `[17,25,31,37,43]` (never down); link never offline |
| 2d | `--reboot` + `--probe-in-progress` (parallel) | PASS | `SCHEDULED` 0 s (`uptime_before=60`), `OBSERVED` 10 s (`evidence=heartbeat; uptime_before=60; uptime_after=0; after_s=10; connectivity=offline>online`). Probe 3 s after the row appeared: `DEVICE_REBOOT_IN_PROGRESS` (watched code `SCHEDULED`); it never reached the simulator. `--after`: signed uptime 53 -> 0, firmware same, all unforced channels OFF |
| 3 | `SIM_REBOOT_NO_RETURN=1`, `--expect not_observed` | PASS | `SCHEDULED` 0 s, `NOT_OBSERVED` at **124 s** (`evidence=none`). Simulator saw 2 reboot commands (1 dry run, 1 real): none resent. Then I started a normal simulator: the row went to `OBSERVED` with `late_s=48` (tail upgrade) |
| 4 | `SIM_REBOOT_NO_REPLY=1`, `--expect observed_reply_none` | PASS (2 runs) | `MQTT_PUBLISHED` 0 s, `DEVICE_RESPONSE_TIMEOUT` **31 s**; probe sent 35 s after the row appeared: `DEVICE_REBOOT_IN_PROGRESS` (watched code `DEVICE_RESPONSE_TIMEOUT`); `OBSERVED` at **124 s** with `reply=none` (`uptime_before=10; uptime_after=0`) |
| 5 | `SIM_SILENT_REAL=system.reboot` (real command silent, nothing reboots), `--expect open`, 140 s | PASS | `DEVICE_RESPONSE_TIMEOUT` at 33 s and the row stayed so past the 122 s hold; signed uptime at the end 151 s (no reset); simulator log `SILENT system.reboot` |
| 6 | `SIM_FORCED=2:on,4:off`, DO1 ON before | PASS | before: DO1 ON, DO2 ON (forced), DO4 OFF (forced). After: DO1 OFF, DO2 ON, DO4 OFF, DO3/5/6 OFF. `OBSERVED` at 10 s, signed uptime 12 -> 0 (strictly smaller) |
| 7 | `SIM_THROTTLE_S=90 SIM_THROTTLE_SURVIVES=1` | PASS | first reboot `OBSERVED`. Second real reboot 81 s later: `DEVICE_REJECTED` with detail `UNAUTHORIZED` (simulator throttle survived the boot; server gate was open after 60 s). A dry run right after was `Executed`: the rejected reboot released the hold |
| 9 | server stopped during tracking, restarted (in-memory audit) | PASS | API killed 15 s after the reboot was published, back after 6 s. `GET /commands/{id}` -> 404 `COMMAND_NOT_FOUND`; the tool ended `server_restarted` at 23 s. The dev session cookie was invalid after the restart (ephemeral data-protection keys); the tool signed in again. Postgres branch (`OUTCOME_UNKNOWN`) not run: no Postgres in dev (review only) |
| 8 | UI walkthrough (controller, built-in browser, master-admin dev seed) | PASS after 2 UI fixes | see "Scenario 8" below |

### Extra observations

- **`DEVICE_REBOOT_TOO_SOON` was exercised twice by accident** (my driver started a second real reboot 49 s and 56 s after the previous one). Both were refused in the server (`ContractUnavailable`), nothing reached the simulator; that is the 60 s gate working. Logs: `s7-toosoon-evidence` (scratchpad).
- **Simulator "boot" timing does not transfer to the gateway.** 2 s reboot delay plus `SIM_BOOT_SECONDS=8` give `OBSERVED` at 10 s. The simulator closes its socket, so Mosquitto published the last will immediately (`offline` seen about 2 s after the reply). A real chip can leave the TCP session half open, and the broker then publishes the will after 1.5 x keepalive. Gateway `offline` timing and boot time are **not measured**.
- **Reply to first heartbeat:** 10 s on the simulator (not a gateway figure).
- **Late observation and the UI rule.** In scenario 4 the reboot is only seen at 124 s, and the before-read was 4 s into the previous boot, so the signed uptime after (113 s) is larger than before (4 s). The tool's first version reported FAIL for "uptime smaller than before"; this is the "uptime does not drop" residual risk of the plan, not a server fault. The UI would say "Thiết bị báo đã lên lại (chưa xác minh)". The tool now reports both: a clock-adjusted reset check (`after < before + elapsed - 5`) as the verdict and whether the strict rule (the UI's) holds, in the detail.
- **Preflight clock check.** The first version measured device clock minus tool time after the poll, which added 1-4 s of read delay to the skew (reported `-4 s` on a clock that is exact). It now uses the server's `reply.receivedAt`; later runs show `-1 s`.

## Scenario 8: UI walkthrough (controller, 2026-11-10)

Setup: the API, frontend and simulator from the setup above. The simulator ran with `SIM_FORCED=2:on,4:off`, `SIM_BOOT_SECONDS=20` and DO1 ON. Login was the dev seed master-admin. Page: `/devices/870c870c-…`. Screenshots are in `screens/`.

| Step | Result |
|---|---|
| Panel | "Khởi động lại thiết bị" sits in the MQTT column; the button is a danger solid button |
| Confirm dialog ([reboot-confirm-dialog-simulator.jpg](screens/reboot-confirm-dialog-simulator.jpg)) | Lists, per §6.4: "DO1 đang BẬT (không force) sẽ TẮT"; "DO4 force TẮT, giữ TẮT"; "DO2 force BẬT: có thể TẮT trong lúc khởi động, BẬT lại khi thiết bị lên"; the DO rule warning; the read time; the dry-run note. The preflight read did not close the dialog |
| Wrong phrase `REBOOT-<server guid>` | Confirm button stays disabled |
| `REBOOT-sim_001` → confirm ([reboot-in-progress-simulator.jpg](screens/reboot-in-progress-simulator.jpg)) | Panel shows "Đang khởi động lại…" and the button is locked. The DO panel switches to "Trạng thái có thể đã cũ: thiết bị đang khởi động lại", with channels "Không rõ" |
| Result ([reboot-observed-simulator.jpg](screens/reboot-observed-simulator.jpg)) | "Đã khởi động lại", uptime 218 s → 0 s (signed), NTP synced −2 s; relay table DO1 TẮT, DO2 BẬT (force), DO3 TẮT, DO4 TẮT (force), DO5/DO6 TẮT; the DO panel re-read itself; config cards show "Cấu hình vừa đổi, hãy đọc lại" |
| Immediate second reboot ([reboot-too-soon-simulator.jpg](screens/reboot-too-soon-simulator.jpg)) | "Chưa đủ 60 s từ lần khởi động lại trước; thử lại sau" (`DEVICE_REBOOT_TOO_SOON`); nothing sent |
| Second tab during an ongoing reboot | That tab's panel says "Thiết bị offline" and sends nothing. The server-side `IN_PROGRESS` refusal is covered by scenario 2 (tool probe) |
| C2 modal | The `system` action list has no `reboot` or `factory_reset`; no `confirm_token` text |
| Tab switch mid-run (after the review fix) | Switched to Audit History during "Đang khởi động lại…" and came back: "Đang đọc lại thiết bị…" with the button still locked, then "Đã khởi động lại" (87 s → 0 s) with the relay table. The post-boot spec-shaped `busy` was retried |

Bugs found and fixed test-first in phase 2 (see `phase-02-implementation-report.md`, "Fixes after UI walkthrough"):
1. **The dialog closed on any `outputs` event.** A plain relay read in another tab closed it with the false text "Trạng thái relay vừa đổi". After the fix, a same-state read leaves it open. A real change (DO3 turned ON in another tab) closes it with "Danh sách relay không còn đáng tin…", because the write marks the state stale first. DO3 was set back OFF.
2. **The previous attempt's "Chưa đủ 60 s…" line stayed visible after reopening.** It is now cleared when a new attempt starts.

Not covered here: a second user hitting `DEVICE_REBOOT_IN_PROGRESS` through the UI. While the simulator boots, its link is offline, so the UI gate stops the attempt first. The server code is covered by unit tests and the tool probe.

## Final state of the simulator device

Not applicable (simulated relays). The environment left for the controller has DO1 ON, DO2 forced ON, DO4 forced OFF.

## Gateway `gateway_870c`: deferred

Not run, not even `--preflight` (read-only). Reason (Validation Session 1, 2026-11-10): nobody is at the lab, and another session is running HIL on the bench. The plan's own risk table does not accept a gateway that does not come back with nobody on site.

To run later, only when someone is at the lab, the bench is free and the user says "go":

1. `--preflight` PASS (`clean_session`, clock within 5 s, no MQTT trial, no staged image; the `ota.get_status` shape is a spec gap, so expect FAIL until it is known).
2. One HMS-SERVER only; `hms-telemetry-bridge.js` stopped; no DO `SET_HIGH`/`SET_LOW` rule (user confirms); firmware team told; redacted sniffer on `hms_toan_lab/gateway_870c/#` (only `id`, `type.action`, `ttl_ms`, `dry_run`, whether `confirm` is present; log deleted after counting).
3. `--checks`, `--dry-run`, UI reboot with `--probe-in-progress --watch`, `--after`, `--restore-do`; measure reply to first heartbeat; T+180 s limit; no resend.

Expected sniffer count: `system.reboot` = 2 dry runs + 1 real; 0 or 1 `control.set_output` (DO1 restore); `ttl_ms` of the reboot 10000.
