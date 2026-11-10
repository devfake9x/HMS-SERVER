---
phase: 2
title: "UI: khoá kênh DO kèm lý do, dịch lỗi trạng thái, hộp và kết quả reboot"
status: completed
priority: P2
effort: 4h
dependencies: [1]
---

# Phase 2: UI — panel DO, hộp reboot, kết quả reboot

## Overview

Phase này làm 5 việc, chỉ trong `frontend/`:
1. Kiểu `OutputChannelDto` thêm 5 field tuỳ chọn (tên JSON của phase 1).
2. Logic thuần mới `outputLocks.ts`: thứ tự khoá, ghi chú luật/trigger, dịch 4 lỗi trạng thái.
3. `DeviceOutputsPanel.vue`: khoá Bật/Tắt/Xung kèm lý do; lỗi tiếng Việt + mã gốc mono; Xung bị từ chối thì không đọc lại lần hai.
4. `rebootPlan.ts`, `RebootConfirmBody.vue`: kênh `level_rule` có dòng riêng kèm id luật; câu chung về luật giữ nguyên.
5. `RebootResult.vue` (qua `checkAfterReboot`): relay BẬT sau boot trên kênh `level_rule` ghi "do luật #id", không tính lỗi.

Phần có sẵn, không sửa (hồi quy phải xanh):
- R4: `operate` giữ khoá và đọc lại sau mọi kết quả có thể đã tới thiết bị (`DeviceOutputsPanel.vue:172-222`), `readBackTwice` (:150-156), `mayHaveRun`/`certainlyNotSent` (`useDeviceCommand.ts:43-70`).
- Không tự gửi lại lệnh ghi; regex `BUSY_DETAIL` (`useDeviceReboot.ts:75`) không đổi.
- `relaysMatch` (`rebootPlan.ts:72-78`): hộp reboot chỉ đóng khi relay khác (tập kênh, `state`, `mode`, `pulseActive`); owner không tham gia.
- Câu `reboot-line-rules` (`RebootConfirmBody.vue:28`) giữ nguyên chữ; test `DeviceRebootPanel.test.ts:147` không đổi.

Đọc trước khi sửa template: `docs/architecture-ui-components-parity.md` (quy ước component và màu chữ trạng thái).

## Requirements

### Kiểu (`frontend/src/core/api/adminApi.ts:121`)
- `export type OutputOwner = 'none' | 'force' | 'level_rule' | 'event_rule' | 'trigger' | 'unknown'`.
- `OutputChannelDto { channel; state; mode; pulseActive; enabled?: boolean | null; gpioOk?: boolean | null; forced?: boolean | null; owner?: OutputOwner | null; ownerId?: string | null }`.
- Tuỳ chọn (`?`) để fixture có sẵn (`DeviceOutputsPanel.test.ts:30-35`, `DeviceRebootPanel.test.ts:215`, `DeviceDetailView.test.ts:90`) và server chưa có phase 1 vẫn hợp lệ: thiếu field = không khoá.

### Logic thuần (`frontend/src/core/commands/outputLocks.ts`, mới)
- `outputWriteLock(ch: Pick<OutputChannelDto, 'enabled' | 'gpioOk' | 'forced' | 'owner' | 'ownerId'>): string | null`, thứ tự:
  1. `gpioOk === false` → `'GPIO không dùng được'`
  2. `enabled === false` → `'Kênh tắt trong cấu hình'`
  3. `forced === true || owner === 'force'` → `'Đang force'`
  4. `owner === 'level_rule'` → `` `Luật #${ownerId} giữ kênh` ``, thiếu `ownerId` → `'Luật giữ kênh'`
  - Còn lại (`none`, `event_rule`, `trigger`, `unknown`, `null`, thiếu) → `null`.
  - Doc comment: spec `c80797d` L797, L827-831; Xung trên `level_rule` khoá có chủ đích vì luật đặt lại mức ≤ 1 s (L853) và spec chưa nói pulse có bị từ chối không.
- `outputOwnerNote(ch): string | null`: `event_rule` hoặc `trigger` → `'Luật/trigger có thể đổi kênh sau lệnh tay'`.
- `stateRefusal(detail: string | null | undefined, replyData: unknown): string | null`:
  - lý do = khớp `/(?:^|:\s*)(gpio_unavailable|channel_disabled|channel_forced|owned_by_rule)$/` trên `detail` (định dạng `error: data.error` của server);
  - câu = nhãn khoá tương ứng + `': thiết bị không đổi relay'`; `owned_by_rule` lấy `rule_id` (số hoặc chuỗi) từ `replyData` (object `data` của reply, L831) → `'Luật #7 giữ kênh: thiết bị không đổi relay'`, không có → `'Luật giữ kênh: thiết bị không đổi relay'`;
  - không khớp → `null` (gồm `busy`, `LANE_FULL`, `UNAUTHORIZED`, lỗi request).
- Nhãn khoá dùng chung cho `outputWriteLock` và `stateRefusal` (một chỗ khai báo).

### Panel (`frontend/src/features/devices/components/DeviceOutputsPanel.vue`)
- Doc comment đầu file (:22-26): thêm "controls are locked, with the device's reason, on channels the last read says will refuse a manual write".
- **Khoá:** `:disabled="!!channelBusy[ch.channel] || !!outputWriteLock(ch)"` trên Bật (:343), Tắt (:344), nút đảo (:350), Xung (:367) và ô ms (:357). Áp cả khi `unknown` (stale/offline): khoá theo lần đọc cuối.
- **Lý do:** dưới dòng mode (:330): `<span v-if="outputWriteLock(ch)" :data-test="`do-${ch.channel}-lock`">…</span>`, `v-else-if="outputOwnerNote(ch)"` → `do-${ch.channel}-owner-note`. Hiện cho mọi kênh có lý do, kể cả kênh không trong allowlist (thông tin thiết bị, không phải quyền).
- **Lỗi:**
  - `describe()` (:164-170) trả `{ text: string; code: string | null } | null`. `final.code === 'DEVICE_REJECTED'` và `stateRefusal(final.detail, outcome.final?.reply?.data)` khác `null` → `{ text: <câu>, code: `${final.code}: ${final.detail}` }`; còn lại `{ text: <chuỗi như cũ>, code: null }`.
  - `channelErrorCode = reactive<Record<number, string>>({})`, xoá cùng chỗ với `channelError` (:178, :264-265), gán trong `operate` khi `code` khác `null`.
  - Danh sách lỗi (:374-378): sau message, `<span v-if="channelErrorCode[…]" class="font-mono" :data-test="`do-${ch}-error-code`"> ({{ … }})</span>`.
- **Kết thúc ghi:** `operate` (:175-222) trả `'stopped' | 'unchanged' | 'sent'` thay `boolean`: các `return false` (:185, :194, :200, :206, :217) → `'stopped'`; cuối (:221) → `'unchanged'` khi lỗi là lỗi trạng thái đã dịch, không thì `'sent'`. `setOutput` (:224-226) bỏ qua kết quả. `pulse` (:236): `if ((await operate(…)) !== 'sent') return`, nên Xung bị từ chối chỉ có một `get_status`.
- Không đổi: `readBackTwice`, `READ_BACK_RETRY_MS`, `readFailureText`, reset khi đổi thiết bị (ngoài việc xoá thêm `channelErrorCode`).

### Reboot plan (`frontend/src/core/commands/rebootPlan.ts`)
- `RelayChannel` (:41) thêm `owner: string | null; ownerId: string | null`.
- `channelsOf` (:48-65): `owner` từ `item.owner` (chuỗi, không thì `null`); `ownerId` từ `item.owner_id` (chuỗi giữ nguyên, số → `String`, khác → `null`); `forced: item.forced === true || item.owner === 'force'`.
- `RelaysBefore` (:17-25) thêm `ruleHeld: Array<{ channel: number; ruleId: string | null; on: boolean }>` (doc: "Held by a level rule: OFF at boot, then the rule sets its level again; it may turn the relay ON (L853, L883)").
- `relaysBeforeReboot` (:80-89): force trước (như cũ); rồi `owner === 'level_rule'` → `ruleHeld` (`on = state || pulseActive`), **không** vào `turnOff`; còn lại như cũ. Nhánh không đọc được (:82) có `ruleHeld: []`.
- `checkAfterReboot` (:96-109): trước nhánh ghi chú chung (:105-106): kênh không force, đang BẬT, và (`owner === 'level_rule'` theo lần đọc sau, hoặc có trong `before.ruleHeld`) → ghi chú `` `DO${n} BẬT sau khởi động do luật #${id}` `` (id = `ownerId` lần sau, không có thì `ruleId` lần trước; không có cả hai → `do luật theo mức`). Không vào `deviations`.
- `relaysMatch`: không đổi.
- `frontend/src/core/commands/useDeviceReboot.ts:77` `NOTHING_BEFORE` thêm `ruleHeld: []`.

### Hộp xác nhận (`frontend/src/features/devices/components/reboot/RebootConfirmBody.vue`)
- Trong nhánh đọc được (:20-27): mỗi kênh `ruleHeld` một dòng `data-test="reboot-line-rule-held"`: `DO6 do luật #7 giữ: sau khởi động về TẮT, rồi luật đặt lại mức (có thể BẬT)` (không có id → `do luật theo mức`).
- `noneOn` (:11): thêm điều kiện không có kênh `ruleHeld` nào đang BẬT.
- `reboot-line-rules` (:28): giữ nguyên, luôn hiện (L883: luật chưa giữ kênh nào vẫn có thể bắn sau boot).

### Kết quả (`frontend/src/features/devices/components/reboot/RebootResult.vue`)
- Chip (:34): kênh `owner === 'level_rule'` thêm ` (luật #id)` (không có id → ` (luật)`), như ` (force)`. Logic đặt trong một hàm `tag(ch)` trong `<script setup>`.
- Ghi chú đến từ `checkAfterReboot` (:41-43), không đổi template.

## Architecture

```
GET /outputs ─► OutputChannelDto{…, enabled, gpioOk, forced, owner, ownerId}
            ─► DeviceOutputsPanel: outputWriteLock(ch) ─► disabled + do-N-lock
                                   outputOwnerNote(ch) ─► do-N-owner-note
click ─► operate ─► runDeviceCommand ─► final{result.detail, reply.data}
                 ─► describe: stateRefusal ─► do-N-error (câu) + do-N-error-code (mã gốc)
                 ─► readBackTwice (có sẵn) ─► load ─► khoá mới
                 ─► 'unchanged' ─► pulse bỏ lần đọc sau xung
reboot prepare ─► get_status (reply thô: owner, owner_id) ─► channelsOf ─► relaysBeforeReboot{…, ruleHeld}
               ─► RebootConfirmBody: reboot-line-rule-held + reboot-line-rules (giữ)
after boot ─► checkAfterReboot ─► notes "do luật #id" ─► RebootResult (chip "(luật #id)")
```

## Related Code Files

- **Create:**
  - `frontend/src/core/commands/outputLocks.ts`
  - `frontend/src/core/commands/outputLocks.test.ts`
- **Modify:**
  - `frontend/src/core/api/adminApi.ts` (:121)
  - `frontend/src/features/devices/components/DeviceOutputsPanel.vue`
  - `frontend/src/features/devices/components/DeviceOutputsPanel.test.ts`
  - `frontend/src/core/commands/rebootPlan.ts`
  - `frontend/src/core/commands/rebootPlan.test.ts`
  - `frontend/src/core/commands/useDeviceReboot.ts` (:77)
  - `frontend/src/features/devices/components/reboot/RebootConfirmBody.vue`
  - `frontend/src/features/devices/components/reboot/RebootResult.vue`
  - `frontend/src/features/devices/components/reboot/DeviceRebootPanel.test.ts`
- **Không sửa:** `useDeviceCommand.ts`, `useDeviceReads.ts`, `DeviceRebootPanel.vue`, `testing/fakeDevice.ts` (đã trả `reply.data`, :63-66).
- **Delete:** không.

## Implementation Steps

0. **Mốc** (ghi vào `reports/phase-02-implementation-report.md`): `cd frontend`; `npx vitest run` → mong 475; `npx vue-tsc --noEmit` sạch.
1. **Test đỏ, `outputLocks.test.ts`** (mới; đỏ vì module chưa có):
   - `describe('outputWriteLock')`:
     - `it.each` `locks %j with its reason`: `{gpioOk:false}` → GPIO; `{enabled:false}` → cấu hình; `{forced:true}` → force; `{owner:'force'}` → force; `{owner:'level_rule', ownerId:'7'}` → `Luật #7 giữ kênh`; `{owner:'level_rule', ownerId:null}` → `Luật giữ kênh`.
     - `takes the reasons in the order gpio, config, force, rule`: `{gpioOk:false, enabled:false, forced:true, owner:'level_rule'}` → GPIO; `{enabled:false, forced:true}` → cấu hình; `{forced:true, owner:'level_rule'}` → force.
     - `it.each` `does not lock %j`: `{owner:'none'}`, `{owner:'event_rule'}`, `{owner:'trigger'}`, `{owner:'unknown'}`, `{owner:null}`, `{}`, `{enabled:true, gpioOk:true, forced:false}`.
   - `describe('outputOwnerNote')`: `notes a rule or a trigger that may change the channel` (event_rule, trigger); `says nothing for the other owners` (none, force, level_rule, unknown, null).
   - `describe('stateRefusal')`:
     - `it.each` `translates %s`: 4 lý do với detail `ESP_ERR_INVALID_STATE: <lý do>`.
     - `names the rule from the reply data, as a number or a string`: `rule_id: 7` và `'7'` → `Luật #7 giữ kênh: thiết bị không đổi relay`.
     - `says a rule holds the channel when the reply names none`.
     - `reads a bare reason`: `'owned_by_rule'`.
     - `it.each` `leaves %j alone`: `'ESP_ERR_INVALID_STATE: busy'`, `'LANE_FULL'`, `'UNAUTHORIZED'`, `'ESP_ERR_INVALID_ARG: channel must be an integer >= 1'`, `'ESP_ERR_INVALID_STATE: owned_by_rule_x'`, `null`, `''`.
2. **Test đỏ, `DeviceOutputsPanel.test.ts`** (thêm helper `ch(channel, extra)` cho kênh có field mới và `rejected(id, reason, data)` trả `result` `Failed`/`DEVICE_REJECTED`, detail `ESP_ERR_INVALID_STATE: <reason>`, `reply.data` = `data`):
   - `locks each control of a channel the device will refuse and says why`: allowlist `[1,2,4,6]`; DO1 `none`, DO2 `gpioOk:false`, DO4 `forced:true, owner:'force'`, DO6 pulse `owner:'level_rule', ownerId:'7'` → DO1 nút đảo bật được; DO2, DO4, DO6 nút đảo và `do-6-pulse` có `disabled`; `do-2-lock`, `do-4-lock`, `do-6-lock` đúng chữ; click nút bị khoá không gọi `sendCommand`.
   - `keeps the lock while the state is unknown`: stale + DO2 `gpioOk:false` → `do-2-on`, `do-2-off` `disabled`.
   - `locks nothing by owner when the device does not report one`: DO6 toggle không field mới → nút đảo bật được, không có `do-6-lock`.
   - `does not lock on an unknown owner`: DO1 `owner:'unknown'` → bật được.
   - `notes that a rule or trigger may change the channel, without locking it`: DO1 `event_rule`, DO2 `trigger` (allowlist `[1,2]`) → `do-1-owner-note`, `do-2-owner-note`; nút bật được.
   - `explains a state refusal in words with the device code, reads back once and does not resend`: lần tải đầu DO6 toggle không owner; `set_output` → `rejected('w9','owned_by_rule',{error:'owned_by_rule',channel:6,rule_id:7})`; `get_status` → `Executed`; lần tải sau DO6 `owner:'level_rule', ownerId:'7'`. Sau 2,5 s và sau thêm 10 s: action đã gửi đúng `['set_output','get_status']`; `do-6-error` chứa `Luật #7 giữ kênh: thiết bị không đổi relay`; `do-6-error-code` chứa `DEVICE_REJECTED: ESP_ERR_INVALID_STATE: owned_by_rule`; nút đảo `disabled`; `do-6-lock` = `Luật #7 giữ kênh`.
   - `it.each(['gpio_unavailable','channel_disabled','channel_forced'])` `translates %s and keeps the device code`.
   - `reads back once after a refused pulse and skips the read after the pulse end`: DO6 pulse không owner; `pulse` → `rejected(…,'channel_forced',{error:'channel_forced',channel:6})`; sau 5 s action đúng `['pulse','get_status']`.
   - `keeps the raw text for a device refusal it does not translate`: detail `ESP_ERR_INVALID_STATE: busy` → `do-1-error` chứa `DEVICE_REJECTED: ESP_ERR_INVALID_STATE: busy`, không có `do-1-error-code`.
   - Không sửa test cũ; riêng `offers pulse only in pulse mode and reads back again after the pulse ends` (:327-344) phải vẫn thấy 2 lần `get_status` (xung thành công).
3. **Test đỏ, `rebootPlan.test.ts`** (helper `channel` :13-14 đã nhận `extra`; dùng `owner`, `owner_id` dạng thô):
   - `relaysBeforeReboot`:
     - `lists the channels a level rule holds with their rule id, apart from the ones that turn off`: DO1 BẬT, DO6 BẬT `owner:'level_rule', owner_id:7` → `turnOff [1]`, `ruleHeld [{channel:6, ruleId:'7', on:true}]`.
     - `reads a rule id given as a string`.
     - `has no rule-held channel when the device does not report owner or the table is unreadable`.
   - `channelsOf`: `treats owner force as forced`.
   - `checkAfterReboot`:
     - `reports a relay a level rule turned on after the boot as the rule, not as a fault`: note `DO6 BẬT sau khởi động do luật #7`, `deviations` rỗng.
     - `names the rule from the reading before when the reading after has no owner`.
4. **Test đỏ, `DeviceRebootPanel.test.ts`** (helper `channel` :28 thêm tham số `extra`):
   - `before the dialog` › `names each channel a level rule holds with its rule id, and keeps the general rule warning`: `reboot-line-rule-held` = `DO6 do luật #7 giữ: sau khởi động về TẮT, rồi luật đặt lại mức (có thể BẬT)`; `reboot-line-rules` đúng chữ cũ; `reboot-line-turn-off` không chứa `DO6`; không có `reboot-line-none`.
   - `before the dialog` › `shows no rule-held line when the device does not report owner` (`relaysNow`).
   - `the confirmation` › `stays open when only the owner of a channel changed` (mẫu test :225-236): `verified(rows => { rows[0].owner = 'level_rule'; rows[0].ownerId = '9' })`, rồi `outputsEvent` → hộp vẫn mở, không có `reboot-notice`.
   - `the result` › `shows a relay a level rule turned on after the boot as the rule, not as a fault`: `reboot-relays` chứa `DO6 BẬT (luật #7)`; `reboot-notes` chứa `do luật #7`; không có `reboot-deviations`; `reboot-status` = `Đã khởi động lại`.
5. **Code:** kiểu `adminApi.ts` → `outputLocks.ts` (mục 1 xanh) → panel (mục 2) → `rebootPlan.ts` + `NOTHING_BEFORE` (mục 3) → `RebootConfirmBody.vue`, `RebootResult.vue` (mục 4).
6. **Xanh:**
   - `npx vitest run`: mọi test xanh, số ≥ 475 + test mới (ghi số thật).
   - `npx vue-tsc --noEmit` sạch; `npx vite build` OK.
   - Grep không còn `operate(` trả boolean; `BUSY_DETAIL` (`useDeviceReboot.ts:75`) và `relaysMatch` không có diff.

## Success Criteria

- [ ] Kênh `gpioOk:false`, `enabled:false`, force, `level_rule` khoá Bật/Tắt/Xung kèm đúng một lý do, theo thứ tự đã định; kể cả khi trạng thái cũ.
- [ ] Không owner hoặc `unknown`: không khoá theo owner; `event_rule`/`trigger`: chỉ ghi chú.
- [ ] 4 lỗi trạng thái hiện tiếng Việt + mã gốc mono; lỗi khác giữ chữ cũ.
- [ ] Sau lỗi trạng thái: đúng một lệnh ghi, một `get_status` (khi đọc được); Xung bị từ chối không đọc lần hai.
- [ ] Hộp reboot nêu kênh `level_rule` kèm id luật; câu chung về luật còn; owner đổi một mình không đóng hộp.
- [ ] Kết quả reboot: relay BẬT do luật là ghi chú, không phải lỗi.
- [ ] Test R4, không gửi lại, regex busy cũ xanh không sửa; vitest ≥ 475 + mới; `vue-tsc` sạch.

## Risk Assessment

| Rủi ro | Khả năng × Ảnh hưởng | Giảm thiểu |
|---|---|---|
| Khoá sai từ lần đọc cũ | Trung bình × Thấp | Lý do hiện rõ; "Đọc trạng thái" luôn có; mọi lệnh ghi có đọc lại |
| Đổi kiểu trả về của `operate` làm hỏng R4 | Thấp × Cao | Test R4 có sẵn không sửa; chỉ `pulse` đọc kết quả |
| Dịch lỗi che mã gốc | Thấp × Trung bình | Mã gốc luôn hiện (`do-N-error-code`); test |
| Khoá Xung trên `level_rule` khi thiết bị cho phép | Trung bình × Thấp | Câu hỏi spec 17; một điều kiện trong `outputWriteLock` |
| Kênh `level_rule` rời dòng "sẽ TẮT" làm hộp nói thiếu | Thấp × Trung bình | Dòng riêng nói rõ "về TẮT, rồi luật đặt lại mức"; `noneOn` tính cả kênh này |
| `vue-tsc` lỗi do `RelaysBefore` thêm field bắt buộc | Thấp × Thấp | Chỗ dựng duy nhất ngoài hàm: `NOTHING_BEFORE` (`useDeviceReboot.ts:77`); test dựng qua `relaysBeforeReboot` |

Rollback: revert commit frontend; server phase 1 vẫn trả field thừa, vô hại.

## As built

Khớp plan. Khác nhỏ: thêm 2 test (`channelsOf` đọc owner/ownerId; hộp reboot không nói "không relay nào BẬT" khi kênh luật đang BẬT); dòng khoá nằm ngay dưới dòng mode, trên nhãn trạng thái. Mốc: vitest 475 (có 1-3 test timeout 5 s lúc chạy lạnh, không cố định), sau: 528 xanh; `vue-tsc` sạch; `vite build` OK. Chi tiết: `reports/phase-02-implementation-report.md`.

