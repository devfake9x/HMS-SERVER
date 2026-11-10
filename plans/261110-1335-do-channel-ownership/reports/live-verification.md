# Live verification: DO channel ownership (simulator)

Date: 2026-11-10. Scope: simulator `hms_server_selftest/sim_001` only. Gateway `gateway_870c` not contacted; nothing touched `hms_toan_lab/#` or the lab broker.

## Setup

| | |
|---|---|
| Broker | local Mosquitto on 127.0.0.1:8088 (anonymous, loopback), same conf as the reboot verification |
| Server | `Hms.Server.Api` (built from the working tree with phase 1) on 127.0.0.1:5099, Development, in-memory, `Mqtt__*` and `Mqtt__Devices__0__*` from env vars (allowlist `[1..6]`), seed device `870c870c-...` bound to `sim_001` |
| Simulator | `tools/mqtt-broker/spec-device-simulator.js`, config by env only; test key file in the scratchpad, value never printed |
| Account | Development seed master-admin through env vars |
| Driver | `tools/device-reboot-check.js` (preflight, reboot) and a scratchpad script that sends one signed command through `POST /commands` and prints the stored reply and `GET /outputs` |

## Red first (simulator before the change)

Default simulator, old code: `GET /outputs` shows `owner:null, ownerId:null` on all channels (the device sent no owner); `set_output` on DO2 was `Executed` and set back. S1 could not pass: no gateway layout, no `owner`, DO2 accepts a write. (The tool's own red was the `--self-check` failing with `ruleHolders is not defined`, before any tool code.)

## Results

Simulator log lines counted from the simulator log of each run.

| # | Scenario | Result | Evidence |
|---|---|---|---|
| S1 data | `SIM_DO_LAYOUT=gateway`, `get_status` read through the server | PASS | `GET /outputs`: DO1 `gpioOk:true, owner:none`; DO2/DO3 `gpioOk:false`; DO4/DO5 `forced:true, owner:force`; DO6 `state:true, owner:level_rule, ownerId:"1"` (raw `owner_id:1` became the string). All channels `toggle` |
| S1 refusals | `set_output value:false` per channel | PASS | DO1 `Executed`; DO2/DO3 `DEVICE_REJECTED` `ESP_ERR_INVALID_STATE: gpio_unavailable`, reply `{error, channel}`; DO4/DO5 `...: channel_forced`; DO6 `...: owned_by_rule`, reply `{error:"owned_by_rule", channel:6, rule_id:1}`. DO1 on then off `Executed` |
| S2 data | `SIM_DO_LAYOUT=gateway SIM_DO_NO_OWNER=1` | PASS | `GET /outputs`: `owner:null, ownerId:null` on all channels; `gpioOk:false` (DO2/DO3) and `forced:true` (DO4/DO5) still present, so those locks would still apply and DO6 would not lock |
| S2 refusal | DO6 `set_output` with no owner reported | PASS | `DEVICE_REJECTED` `ESP_ERR_INVALID_STATE: owned_by_rule`, reply `rule_id:1`: the device still decides |
| S3 | `SIM_DO_DISABLED=3`, default layout | PASS | DO3 `enabled:false`; `owner:"none"`, `ownerId:null` on all; DO3 `set_output` -> `...: channel_disabled`, reply `channel:3`. `pulse` on DO6 (pulse mode) `Executed`: pulse has no owner check |
| S5a | `--preflight`, gateway layout | PASS (as designed: preflight FAIL, exit 1) | 7 old checks PASS, then `FAIL  no DO channel held by a rule or trigger (control.get_status owner)  held: DO6 level_rule #1; remove the rule on the device (HTTP /api/do-rule/remove) before a reboot test`; `WARN` (L883) printed. `--reboot` on the same device stopped at the preflight: "Preflight failed: nothing was sent", simulator saw 0 `system.reboot` |
| S5b | `--preflight`, gateway + `SIM_DO_NO_OWNER=1` | PASS (exit 0) | 7 old checks PASS, no owner check, `ASK   firmware does not report owner...`, `WARN` (L883) |
| S5c | `--preflight`, default simulator | PASS (exit 0) | 7 old checks PASS plus `PASS  no DO channel held by a rule or trigger (control.get_status owner)`, `WARN` (L883) |
| Boot | real `--reboot --sim` on gateway + no-owner (preflight passes) | PASS | `OBSERVED` at 10 s, uptime 25 -> 0. After boot: DO1-DO3 OFF, DO4/DO5 forced OFF, **DO6 ON** (level rule sets its level again); tool note "DO6 is ON after reboot, may be a DO rule (not counted as a failure)". Simulator log: `REBOOT: dropping`, `REBOOT: booted` |
| Old simulator, new tool | `--preflight` against the unmodified simulator (no owner) | PASS (exit 0) | 7 checks PASS, `ASK`, `WARN` |

Not run here: `--self-check` output is in the phase report; the unit-only paths (`owner:"unknown"` -> `ASK`, `event_rule`/`trigger` held) are covered by `--self-check` (the `ASK` line is printed from `ruleHolders().unknown`; no simulator mode sends `unknown`).

## Not done by this run (controller)

- UI walkthrough of S1-S4 in a browser (panel locks and reasons, `owned_by_rule` through the panel with exactly one `set_output` and one `get_status` in the simulator log, reboot box lines, post-boot chip "DO6 BẬT (luật #1)"). Screenshots in `reports/screens/` are the controller's.
- Panel handling of `gpio_unavailable` / `channel_disabled` refusals: the panel locks those channels first, so they are not clickable; covered by vitest and by the direct replies above.

## Final state

All processes this run started were stopped (simulator, API, local Mosquitto); ports 8088 and 5099 are free. The lab broker (PID 34804) was not touched.

## UI walkthrough (controller, 2026-11-10)

Built-in browser, `http://127.0.0.1:5173`, dev seed master-admin, device `870c870c-…` bound to `hms_server_selftest/sim_001` on the local broker 127.0.0.1:8088. Read from the DOM (`data-test`) after "Đọc trạng thái"; the pane was hidden, so only S1 has a screenshot.

| Kịch bản | Simulator | Kết quả UI | |
|---|---|---|---|
| S1 | `SIM_DO_LAYOUT=gateway` | DO1 mở. DO2, DO3 "GPIO không dùng được"; DO4, DO5 "Đang force"; DO6 "Luật #1 giữ kênh"; nút của 5 kênh này `disabled`. [screens/s1-gateway-layout-locks.png](screens/s1-gateway-layout-locks.png) | PASS |
| S2 | `SIM_DO_LAYOUT=gateway SIM_DO_NO_OWNER=1` | DO6 mở (không owner), DO2-DO5 vẫn khoá. Bấm DO6: "DO6: Luật #1 giữ kênh: thiết bị không đổi relay (DEVICE_REJECTED: ESP_ERR_INVALID_STATE: owned_by_rule)". Log simulator sau khi bấm: đúng một `control.set_output {"channel":6,"value":false}`, rồi một `control.get_status`; không gửi lại | PASS |
| S3 | `SIM_DO_DISABLED=3` | DO3 "Kênh tắt trong cấu hình", `disabled`; các kênh khác mở; Xung DO6 mở | PASS |
| S4 | `SIM_DO_LAYOUT=gateway` | Hộp xác nhận: "DO4, DO5 force TẮT, giữ TẮT"; "DO6 do luật #1 giữ: sau khởi động về TẮT, rồi luật đặt lại mức (có thể BẬT)"; câu chung "Luật DO SET_HIGH/SET_LOW (nếu có)…" vẫn có; không có dòng "sẽ TẮT". Gõ `REBOOT-sim_001`, xác nhận → "Đã khởi động lại", uptime 82 s → 0 s; chip "DO6 BẬT (luật #1)", ghi chú "DO6 BẬT sau khởi động do luật #1", không tính lỗi; DO4/DO5 "TẮT (force)" | PASS |

Ngoài phạm vi, có sẵn trên `develop`: đăng nhập chuyển tới `/overview` → "Không tìm thấy trang" (`frontend/src/features/auth/views/LoginView.vue:49`; route thật là `/monitoring/overview`).

Env stopped after the walkthrough (`stop-all.sh`: simulator, API, Vite, local Mosquitto); ports 8088/5099/5173 free; lab broker PID 34804 untouched.
