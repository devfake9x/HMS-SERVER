---
phase: 3
title: "Simulator bố cục gateway, preflight đọc owner, tài liệu"
status: completed
priority: P2
effort: 3h
dependencies: [1, 2]
---

# Phase 3: Simulator, preflight, tài liệu

## Overview

Phase này làm 5 việc, chỉ trong `tools/`, `docs/` và file plan:
1. Simulator gửi `owner`/`owner_id` và đủ field kênh thật; từ chối `owned_by_rule`, `channel_disabled`, `gpio_unavailable`, `channel_forced` kèm `channel` (+`rule_id`); có bố cục giống gateway (`SIM_DO_LAYOUT=gateway`) và chế độ firmware cũ (`SIM_DO_NO_OWNER=1`).
2. `tools/device-reboot-check.js --preflight` đọc `owner`: kênh do luật/trigger giữ → FAIL kèm danh sách; thiếu `owner` hoặc `unknown` → dòng `ASK` (xác nhận tay giữ nguyên).
3. Kiểm E2E trên simulator `hms_server_selftest/sim_001` (panel, hộp reboot, một reboot thật trên simulator, preflight).
4. Tài liệu: `docs/architecture-mqtt-device-link.md`, `docs/architecture-device-mqtt-api-catalog.md`.
5. File plan: điều kiện gateway của plan reboot, danh sách câu hỏi spec, kết quả plan này.

Gateway thật ngoài phạm vi (bàn thử đang chạy HIL). Follow-up (a) của `plan.md`: một `control.get_status` read-only.

Phần có sẵn, không sửa:
- `BUSY_DETAIL` (`device-reboot-check.js:156`), `refusedResult`, `PROBES`, mọi chế độ khác của tool; `compareAfter` (:371-397) không đổi (preflight đã chặn thiết bị có kênh do luật giữ).
- Simulator: chữ ký, dry-run, throttle, reboot (trừ việc đặt lại mức luật lúc boot), heartbeat, các handler không phải `control.*`.

## Requirements

### Simulator (`tools/mqtt-broker/spec-device-simulator.js`; cấu hình chỉ qua env, :8)
- **Header** (:1-43): mục mới "DO channel fields (spec `c80797d` L797, L822, L827-831, L839, L883; HIL L1054-1057)" mô tả 3 biến dưới. Comment `channel_forced` (:135) đổi tham chiếu sang `c80797d` L827.
- **`SIM_DO_LAYOUT=gateway`** (giá trị khác → `fail('SIM_DO_LAYOUT must be "gateway"')`, thoát mã 2). Bỏ qua `SIM_CHANNELS`, `SIM_PULSE_CHANNELS`, `SIM_FORCED` (in một dòng log nếu có đặt):

  | Kênh | mode | gpioOk | forced | owner | owner_id | trạng thái đầu |
  |---|---|---|---|---|---|---|
  | DO1 | toggle | true | false | `none` | `null` | TẮT |
  | DO2, DO3 | toggle | **false** | false | `none` | `null` | TẮT |
  | DO4, DO5 | toggle | true | **true** (TẮT) | `force` | `null` | TẮT |
  | DO6 | toggle | true | false | **`level_rule`** | **`1`** (số) | BẬT (mức của luật) |

  Nguồn: HIL L1054 (DO2/DO3 `gpioOk:false`, DO4/DO5 `force`, DO6 `level_rule`, DO1 `none`). Mức BẬT của luật DO6 là lựa chọn của simulator (spec không nêu), để kịch bản reboot thấy "BẬT do luật".
- **`SIM_DO_NO_OWNER=1`:** `get_status` không có `owner`, `owner_id` (firmware trước `c80797d`); `enabled`, `gpioOk`, `forced` vẫn gửi; mọi từ chối không đổi (thiết bị vẫn quyết).
- **`SIM_DO_DISABLED=<list>`:** kênh trong danh sách `enabled:false` (dùng được cùng layout).
- **Mô hình kênh** (:67-76): thêm `enabled`, `gpioOk`, `owner` (`'none'|'level_rule'`), `ruleId`, `ruleLevel`.
- **`channelView`** (:98-102): `enabled`, `gpioOk` thật (thay hằng `true`); `owner: o.forced ? 'force' : o.owner`; `owner_id: o.owner === 'level_rule' ? o.ruleId : null`; bỏ 2 field này khi `SIM_DO_NO_OWNER=1`.
- **`control.set_output`** (:128-140), sau kiểm `value`: `gpio_unavailable` → `channel_disabled` → `channel_forced` → `owned_by_rule`, mỗi lỗi `{ error: 'ESP_ERR_INVALID_STATE', data: { error, channel[, rule_id] } }` (L822; `rule_id` chỉ với `owned_by_rule`, L831). Spec không định thứ tự cho `set_output`; dùng đúng thứ tự khoá của panel (comment ghi vậy).
- **`control.pulse`** (:141-157): không kiểm owner. Comment: L818 ghi pulse không đổi trong đợt `owned_by_rule`; câu hỏi spec chưa trả lời.
- **Boot** (:329-337): cùng chỗ đặt lại mức force (:336), kênh `level_rule` lấy `ruleLevel` (L883: luật SET_HIGH/SET_LOW bắn ở lần đánh giá đầu).
- **Log khởi động** (:279): thêm `layout=<gateway|default> owner=<on|off>`.

### Preflight (`tools/device-reboot-check.js`)
- **`relaysOf`** (:204-208): thêm `owner: typeof c.owner === 'string' ? c.owner : null`, `ownerId: c.owner_id == null ? null : String(c.owner_id)`.
- **`ruleHolders(relays)`** (mới, cạnh `relaysOf`): `null` khi không phải mảng hoặc không kênh nào có `owner` (không kiểm được); không thì `{ held, unknown }`: `held` = kênh có owner `level_rule`, `event_rule`, `trigger`; `unknown` = kênh owner `unknown`. Comment: spec `c80797d` L797.
- **`preflightVerdict`** (:246-257): khi `ruleHolders(s.relays)` khác `null`, thêm `{ name: 'no DO channel held by a rule or trigger (control.get_status owner)', pass: held.length === 0, detail }`. Các mục có thêm `detail` tuỳ chọn; `runPreflight` (:273) in `report(v.name, v.pass, v.detail)`.
- **Thông điệp** (`runPreflight`, :271-280):
  - FAIL: `FAIL  no DO channel held by a rule or trigger (control.get_status owner)  held: DO6 level_rule #1; remove the rule on the device (HTTP /api/do-rule/remove) before a reboot test` (danh sách dạng `DO<n> <owner>[ #<id>]`, nối bằng `, `).
  - Thiếu owner: `ASK   firmware does not report owner on control.get_status: confirm by hand that no DO rule (SET_HIGH/SET_LOW) or trigger drives a relay on this device; this does not fail the preflight`.
  - Có `unknown`: `ASK   owner "unknown" on DO3: the device cannot tell who holds it; confirm by hand that no DO rule or trigger drives it`.
  - Luôn in: `WARN  owner names who holds a channel at read time; a DO rule that holds none can still fire SET_HIGH/SET_LOW at the first evaluation after boot (spec L883)`.
  - `--reboot` (:416-417) đã dừng khi preflight không PASS: FAIL mới chặn reboot, không cần sửa.
- **`--plan`** (:512-513): thêm "PASS also needs no DO channel held by a rule or trigger when the device reports owner; ASK when it does not".
- **`selfCheck`** (:52-79): thêm ca cho `ruleHolders`, `relaysOf`, `preflightVerdict`, `BUSY_DETAIL` (ở bước 1).

### Tài liệu
- `docs/architecture-mqtt-device-link.md`:
  - "Trạng thái DO đã xác minh" (:155-160): `channels[{channel, state, mode, pulseActive, enabled, gpioOk, forced, owner, ownerId}]`; `null` khi thiết bị không gửi; `owner` ngoài 6 giá trị → `unknown`; `owner_id` → chuỗi `ownerId`; spec `c80797d` L797, L839.
  - "UI (`DeviceOutputsPanel.vue`)" (:182-195): bảng thứ tự khoá (4 mức, kể cả Xung trên `level_rule`, lý do); khoá theo lần đọc cuối kể cả khi cũ; ghi chú `event_rule`/`trigger`; không khoá theo owner khi thiếu/`unknown`; dịch 4 lỗi + mã gốc; đọc lại có sẵn (một `get_status` khi đọc được), Xung bị từ chối không đọc lần hai.
  - "Simulator" (:197-214): `SIM_DO_LAYOUT=gateway` (bảng bố cục), `SIM_DO_NO_OWNER=1`, `SIM_DO_DISABLED`; mặc định nay gửi `owner` (`none`/`force`).
  - "Khởi động lại an toàn" › "UI" (:580): dòng kênh `level_rule` kèm id luật; câu chung về luật giữ nguyên; kết quả "do luật #id" không tính lỗi.
  - "Công cụ kiểm và simulator" (:587-604): kiểm owner của `--preflight`, `ASK`, `WARN`.
  - "Gateway thật: hoãn" (:621-623): "user xác nhận không có luật DO" chỉ còn khi preflight in `ASK`; có `owner` thì preflight kiểm; `gateway_870c` có DO6 `level_rule` (HIL L1054) nên preflight sẽ FAIL tới khi luật được xoá qua HTTP thiết bị.
  - DO section: một gạch "Spec gap" cho câu 17-19 (không gửi).
- `docs/architecture-device-mqtt-api-catalog.md`:
  - §3.2 row `get_status` (:170): phản hồi thêm `enabled, gpioOk, forced, owner, owner_id`, giá trị `owner`.
  - Dưới bảng §3.2 (:173): "Spec HEAD `c80797d` (2026-11-10) thêm field kênh và `owned_by_rule`; đó là shape reply, không phải row §11. Catalog vẫn ghim `83e93d77e`, hash không đổi; đồng bộ catalog vẫn chặn bởi số đếm §11."

### File plan
- `plans/261109-2250-safe-reboot/plan.md:125`: thay bằng "User xác nhận gateway không có luật DO `SET_HIGH`/`SET_LOW` khi `--preflight` in `ASK` (firmware không gửi `owner`); khi có `owner`, preflight FAIL nếu kênh do luật/trigger giữ (plan `261110-1335-do-channel-ownership`)". Không đổi dòng khác.
- `plans/reports/brainstorm-261108-1700-mqtt-spec-coverage-and-requests.md` §3: mục mới "### D. DO ownership (spec `c80797d`, 2026-11-10)" với câu 17, 18, 19 như `plan.md` § Spec gap. Ghi "chưa gửi".
- `plan.md` của plan này: status từng phase, mục "Kết quả", follow-up còn mở.

## Architecture

```
SIM_DO_LAYOUT=gateway ─► outputs[6]{enabled,gpioOk,forced,owner,ruleId,ruleLevel}
  get_status ─► channelView ─► {…, enabled, gpioOk, forced, owner, owner_id}   (SIM_DO_NO_OWNER=1: bỏ owner, owner_id)
  set_output ─► gpio_unavailable | channel_disabled | channel_forced | owned_by_rule(+rule_id) ─► ESP_ERR_INVALID_STATE
  boot ─► forced → mức force; level_rule → ruleLevel

device-reboot-check --preflight ─► snapshot ─► relaysOf(+owner) ─► ruleHolders
   null ─► ASK (tay) ; held ─► FAIL + danh sách ; unknown ─► ASK ; luôn WARN (L883)
```

## Related Code Files

- **Create:** `plans/261110-1335-do-channel-ownership/reports/live-verification.md` (kết quả E2E), `reports/phase-03-implementation-report.md`.
- **Modify:**
  - `tools/mqtt-broker/spec-device-simulator.js`
  - `tools/device-reboot-check.js`
  - `docs/architecture-mqtt-device-link.md`
  - `docs/architecture-device-mqtt-api-catalog.md`
  - `plans/261109-2250-safe-reboot/plan.md` (đúng dòng :125)
  - `plans/reports/brainstorm-261108-1700-mqtt-spec-coverage-and-requests.md` (§3)
  - `plans/261110-1335-do-channel-ownership/plan.md`
- **Delete:** không.

## Implementation Steps

0. **Mốc:** `node tools/device-reboot-check.js --self-check` → PASS (ghi số dòng PASS/FAIL); `node --check tools/mqtt-broker/spec-device-simulator.js`. Ghi vào `reports/phase-03-implementation-report.md`.
1. **Đỏ, tool** (`selfCheck` trong `tools/device-reboot-check.js`; đỏ vì `ruleHolders` chưa có và `relaysOf` chưa đọc owner):
   - `ruleHolders`: `[{channel:1,owner:null},{channel:2,owner:null}]` → `null`; `[{channel:1,owner:'none'},{channel:4,owner:'force'}]` → `held` rỗng; `[{channel:6,owner:'level_rule',ownerId:'1'}]` → `held` có DO6; `[{channel:2,owner:'event_rule'},{channel:3,owner:'trigger'}]` → `held` 2 kênh; `[{channel:3,owner:'unknown'}]` → `held` rỗng, `unknown` có DO3.
   - `relaysOf({channels:[{channel:6,state:true,owner:'level_rule',owner_id:1}]})[0]` → `owner 'level_rule'`, `ownerId '1'`; reply không có owner → `owner null`.
   - `preflightVerdict` với snapshot có DO6 `level_rule` → mục owner `pass:false`, `detail` chứa `DO6 level_rule #1`; snapshot không có owner → không có mục owner.
   - `BUSY_DETAIL`: `ESP_ERR_INVALID_STATE: owned_by_rule`, `…: gpio_unavailable`, `…: channel_disabled` → `false` (cùng bảng :76).
   - Chạy `--self-check` → phải FAIL (hoặc ném lỗi) trước khi code.
2. **Đỏ, E2E** (ghi bản "trước" vào `reports/live-verification.md`): với simulator hiện tại, kịch bản S1 trượt (không có lý do khoá, DO2 nhận lệnh).
3. **Code tool** → `--self-check` PASS.
4. **Code simulator** → `node --check` sạch.
5. **E2E trên simulator** (Mosquitto loopback như plan reboot, vì Aedes chỉ nói MQTT 3.1.1; server dev với `Mqtt` trỏ broker đó; device `sim_001` đã bind, command key đã import, allowlist `[1,2,3,4,5,6]` qua `PUT /link/output-channels`; tài khoản master-admin; không ghi key, mật khẩu vào log hay repo):
   - **S1** `SIM_DO_LAYOUT=gateway`: "Đọc trạng thái" → chỉ DO1 bấm được; DO2/DO3 "GPIO không dùng được"; DO4/DO5 "Đang force"; DO6 "Luật #1 giữ kênh". DO1 Bật rồi Tắt: đọc lại đúng, mở khoá.
   - **S2** `SIM_DO_LAYOUT=gateway SIM_DO_NO_OWNER=1`: DO6 không khoá; DO2-DO5 vẫn khoá. Bấm DO6 Tắt → lỗi "Luật #1 giữ kênh: thiết bị không đổi relay" + `(DEVICE_REJECTED: ESP_ERR_INVALID_STATE: owned_by_rule)`; log simulator: đúng một `CMD control.set_output`, rồi một `CMD control.get_status`; DO6 vẫn không khoá (không owner).
   - **S3** `SIM_DO_DISABLED=3` (không layout): DO3 "Kênh tắt trong cấu hình"; các kênh khác như cũ; `get_status` có `owner:"none"`.
   - **S4** `SIM_DO_LAYOUT=gateway`, master-admin mở hộp reboot: dòng "DO6 do luật #1 giữ: …"; dòng "DO4, DO5 force TẮT, giữ TẮT"; câu chung về luật còn; DO6 không ở dòng "sẽ TẮT". Huỷ (không gửi). Mở lại và reboot thật trên simulator: "Đã khởi động lại"; chip "DO6 BẬT (luật #1)"; ghi chú "DO6 BẬT sau khởi động do luật #1"; không có cảnh báo lệch.
   - **S5** preflight: `node tools/device-reboot-check.js --device <guid sim> --confirm-live --preflight` với (a) `SIM_DO_LAYOUT=gateway` → dòng FAIL owner kèm `held: DO6 level_rule #1`; (b) thêm `SIM_DO_NO_OWNER=1` → dòng `ASK`, không có mục owner, 7 mục cũ PASS; (c) mặc định → mục owner PASS. Mọi lần: dòng `WARN` L883.
   - Ảnh chụp panel S1, S2 và hộp S4 vào `reports/screens/` (không có dữ liệu bí mật trên màn hình).
6. **Tài liệu và file plan** (theo Requirements); kiểm lại ngày, link, số dòng và tên field (`enabled`, `gpioOk`, `forced`, `owner`, `ownerId`; thô `owner_id`) khớp code đã làm ở phase 1, 2.
7. **Xanh toàn plan:** chạy lại test phase 1, 2 (unit, integration với `Mqtt__Enabled=false`, vitest, `vue-tsc`) nếu có commit mới sau phase 2; `git diff src/Hms.Server.Infrastructure/Commands/command-catalog.json` rỗng.

## Success Criteria

- [ ] Simulator: `get_status` có `enabled`, `gpioOk`, `forced`, `owner`, `owner_id`; bố cục gateway đúng bảng; `SIM_DO_NO_OWNER=1` bỏ owner; 4 từ chối kèm `channel` (+`rule_id`); pulse không kiểm owner; luật đặt lại mức sau boot.
- [ ] `--self-check` PASS với các ca mới; preflight FAIL kèm danh sách khi kênh do luật/trigger giữ; `ASK` khi thiếu owner hoặc `unknown`; `WARN` L883 luôn in.
- [ ] E2E S1-S5 PASS, ghi ở `reports/live-verification.md`.
- [ ] Docs DO, simulator, reboot, catalog cập nhật; catalog ghi spec HEAD `c80797d`, vẫn ghim `83e93d77e`, hash không đổi.
- [ ] Dòng điều kiện gateway của plan reboot sửa đúng một dòng; câu hỏi 17-19 nằm trong danh sách spec, chưa gửi.
- [ ] Gateway thật không chạy; follow-up (a) ghi trong `plan.md`.

## Risk Assessment

| Rủi ro | Khả năng × Ảnh hưởng | Giảm thiểu |
|---|---|---|
| Bố cục simulator khác gateway thật (mode DO6, mức luật, thứ tự lỗi) | Trung bình × Thấp | Bảng theo HIL L1054; chỗ simulator tự chọn ghi trong header; follow-up đọc gateway |
| Preflight PASS khi có luật chưa giữ kênh nào | Thấp × Cao | `WARN` L883 luôn in; hộp reboot giữ câu chung |
| `gateway_870c` FAIL preflight vì DO6 `level_rule` | Cao × Thấp | Chủ đích; docs và điều kiện gateway nói rõ phải xoá luật trước |
| Từ chối `gpio_unavailable`/`channel_disabled` của simulator không có E2E (panel đã khoá) | Trung bình × Thấp | Review code; vitest dịch lỗi; ghi trong test matrix |
| Sửa nhầm plan reboot ngoài dòng :125 | Thấp × Thấp | Chỉ sửa đúng một dòng; diff kiểm trước commit |

Rollback: revert commit tool/docs/plan. Simulator không chạy production; bỏ kiểm owner thì điều kiện tay cũ áp lại.

## As built

- **Tool** (`tools/device-reboot-check.js`): `relaysOf` reads `owner`/`ownerId`; `ruleHolders` (null / `{held, unknown}`); `preflightVerdict` adds the owner check with `detail`; `runPreflight` prints `detail`, `ASK` (no owner, or `unknown`) and the L883 `WARN`; `--plan` line added. `--self-check` has new cases for `ruleHolders`, `relaysOf`, `preflightVerdict` and three more `BUSY_DETAIL` details. Red first: `ruleHolders is not defined`.
- **Simulator** (`tools/mqtt-broker/spec-device-simulator.js`): channel model with `enabled`, `gpioOk`, `owner`, `ruleId`, `ruleLevel`, `forcedLevel`; `SIM_DO_LAYOUT=gateway` (bad value exits 2), `SIM_DO_NO_OWNER=1`, `SIM_DO_DISABLED`; `set_output` refusals with `channel` (+`rule_id`) in the panel's order; pulse unchecked; boot re-applies force and rule levels; startup log shows `layout`, `owner`. Header documents the three variables.
- **Reboot gate**: `rebootBlockedByAsk({ pass, ask, rulesConfirmed })` (pure, in `--self-check`); `--reboot` sends nothing when the preflight printed an `ASK` unless `--rules-confirmed`; the preflight verdict stays PASS. The manual no-DO-rule confirmation is unconditional in the gateway precondition (user decision 2026-11-10).
- **Docs and plan files** as in Requirements. One extra edit: the Simulator section of `architecture-mqtt-device-link.md` also mentions where the DO6 level comes from.
- **E2E**: `reports/live-verification.md` (S1/S2/S3 data and refusals, S5a-c, real reboot with boot behavior). Not done here: UI walkthrough S1-S4 and screenshots (controller). `gpio_unavailable`/`channel_disabled` panel path is unclickable (locked first): vitest plus the direct replies.
- **Deviations**: the `owner:"unknown"` `ASK` line has no simulator mode (self-check only). Step 7 re-run of phase 1-2 suites skipped: no new commits since phase 2; `git diff` of `command-catalog.json` is empty.

