---
phase: 2
title: "UI: nút Khởi động lại, hộp xác nhận, modal C2"
status: completed
priority: P1
dependencies: [1]
---

# Phase 2: UI — nút "Khởi động lại", hộp xác nhận, modal C2

## Overview

Thêm một panel nhỏ "Khởi động lại" vào cột "MQTT Broker Transport" của trang thiết bị (`DeviceDetailView.vue:895-930`).
Chỉ master-admin thấy nút, vì `allowed-commands` chỉ liệt kê `system.reboot` cho master-admin (phase 1, red-team #2).
Mỗi lần reboot theo trình tự:
1. Đọc `system.get_info` (uptime đã ký) và `control.get_status` (relay) mới.
2. Hộp xác nhận: nói đúng relay sẽ ra sao theo §6.4; người dùng gõ `REBOOT-<mqttDeviceId>`.
3. Lúc bấm xác nhận: kiểm lại cổng và tuổi lần đọc; dry-run (một lần) → lệnh thật (một lần).
4. Theo dõi kết quả server ("Đang khởi động lại…") tới kết quả cuối.
5. Đã reboot: đọc lại `system.get_info`, `control.get_status`, `system.get_time`; chỉ báo "Đã khởi động lại" khi uptime đã ký nhỏ hơn lần trước.

Không bao giờ tự gửi lại. Modal C2 ẩn mọi lệnh class F và bỏ đoạn gán `confirm_token` sai.

## Requirements

### API và kiểu (`frontend/src/core/api/adminApi.ts`, `useDeviceCommand.ts`, `useDeviceWrite.ts`)
- `sendCommand` body (`adminApi.ts:234`) thêm `confirm?: string` (đã có `dryRun?`).
- `CommandCatalogAction` (`adminApi.ts:63-74`) thêm `class?: string`. Server đã trả field này dưới dạng chuỗi enum (`MeController.cs:57-71`; `Program.cs:27-29`).
- `DeviceCommandBody` (`useDeviceCommand.ts:8-15`) thêm `confirm?: string`, `dryRun?: boolean`; `runDeviceCommand` đã chuyển nguyên body (:92-94).
- `runDeviceWrite` (`useDeviceWrite.ts:138-176`) nhận thêm option `followJobs?: boolean` (mặc định true). Reboot truyền `false`: `Acknowledged` thành `unknown`, không chạy vòng `system.get_operations` 60 s với thiết bị đang khởi động lại (:169-175, :99-129) (red-team #15).
- `fakeDevice.ts` (`core/commands/testing/fakeDevice.ts:14-40`): `SentCommand` ghi thêm `confirm`, `dryRun`; `Scenario` nhận `results?: Partial<CommandExecutionResult>[]` để `commandResult` trả lần lượt, và `pollError?: number` (số lần poll lỗi trước khi trả).

### Logic thuần (`frontend/src/features/devices/components/reboot/rebootPlan.ts`, mới)
- **`relaysBeforeReboot(data: unknown)`** đọc reply `control.get_status` (§6.4 L797: `channels{channel,state,mode,enabled,gpioOk,forced,pulseActive?}`), trả `{ turnOff, forcedOff, forcedOn, readable }` (red-team #3):
  - `turnOff`: kênh không `forced` đang `state:true`, hoặc kênh pulse đang chạy (§6.4 L874-875);
  - `forcedOff`: kênh force TẮT, giữ TẮT;
  - `forcedOn`: kênh force BẬT. Có thể TẮT trong lúc khởi động (driver dừng thì mọi line về OFF, L866), BẬT lại ở lần ghi line đầu tiên (L876);
  - không đọc được → `readable:false`.
- **`checkAfterReboot(before, after)`** (red-team #3):
  - kênh force không về đúng mức force cũ → lệch (lỗi);
  - kênh không force mà `state:true` → **thông tin** "BẬT sau khởi động, có thể do luật DO SET_HIGH/SET_LOW" (L880), không phải lỗi;
  - trả `{ deviations, notes }`.
- **`isVerifiedReboot(uptimeBefore, uptimeAfter)`**: chỉ true khi cả hai là số và `after < before`. Cả hai lấy từ reply `system.get_info` đã ký (red-team #9).
- **`rebootGate(input)`** (cùng kiểu `configGate`, `reads/configGate.ts:38-56`):
  - ẩn panel khi `allowedCommands` không có `system.reboot`;
  - khoá kèm lý do: "Catalog lệnh chưa được xác minh"; "Thiết bị offline" (`link.online === false` hoặc `brokerConnected === false`); "Chưa có định danh MQTT" (thiếu `link.binding.mqttDeviceId`, `adminApi.ts:102-118`); "Đang khởi động lại…".
- **`rebootPhrase(link)`** = `REBOOT-${link.binding.mqttDeviceId}`. Không bao giờ dùng id server.
- **`rebootMessage(code, detail)`** — chuỗi tiếng Việt theo mã:
  - `DEVICE_REBOOT_SCHEDULED` → "Đang khởi động lại…";
  - `DEVICE_REBOOT_OBSERVED` → "Đã khởi động lại" chỉ khi đã xác minh; nếu không → "Thiết bị báo đã lên lại (chưa xác minh)" (red-team #9);
  - `DEVICE_REBOOT_NOT_OBSERVED` → "Chưa thấy thiết bị lên lại sau 2 phút; server vẫn chờ thêm 10 phút. Không gửi lại" (red-team #11);
  - `DEVICE_REBOOT_OUTCOME_UNKNOWN` → "Chưa rõ kết quả (server mất theo dõi)";
  - `DEVICE_REBOOT_IN_PROGRESS` → "Thiết bị đang khởi động lại, chờ kết quả"; `DEVICE_REBOOT_TOO_SOON` → "Chưa đủ 60 s từ lần khởi động lại trước; thử lại sau" (red-team #1);
  - `HIGH_RISK_REQUIRES_MASTER_ADMIN` → "Chỉ master-admin được khởi động lại thiết bị" (red-team #2);
  - `CONFIRM_MISMATCH` → "Mã xác nhận không khớp định danh MQTT của thiết bị"; `CONFIRM_REQUIRED` → "Thiếu mã xác nhận";
  - `DEVICE_REJECTED` + `UNAUTHORIZED` → "Thiết bị từ chối (UNAUTHORIZED: chống lặp, safe mode hoặc giới hạn 60 s)"; + `confirm_required` → "Thiết bị từ chối mã xác nhận";
  - `COMMAND_NOT_FOUND` lúc theo dõi → "Server đã khởi động lại và mất bản ghi lệnh; chưa rõ kết quả" (red-team #10, #15);
  - `DEVICE_WRITE_BUSY`, `DEVICE_WRITE_RATE_LIMITED`, `COMMAND_PAYLOAD_INVALID`, `CATALOG_STALE`, `DEVICE_PERMISSION_REQUIRED`: cùng câu với thẻ cấu hình (plan 261106-2033 phase 2).

### Luồng (`frontend/src/core/commands/useDeviceReboot.ts`, mới)
- **`runReboot(deviceId, { confirm, uptimeBefore, catalog, allowedCommands, csrfToken, signal, onPhase })`** trả `RebootOutcome { kind, code, detail, verified, info, outputs, time }`.
  1. **Dry-run:** `runDeviceWrite` với `{type:'system', action:'reboot', payload:{}, dryRun:true, confirm}`. Khác `executed` → dừng; **không** gửi lệnh thật. Chữ hiển thị: "Chạy thử chỉ kiểm chữ ký, khoá, đồng hồ và lệnh được thiết bị nhận; lệnh thật vẫn có thể bị từ chối" (spec L551, red-team #5).
  2. **Lệnh thật:** `runDeviceWrite` với `dryRun:false`, `followJobs:false`, `correlationId` mới.
     - `not_sent` → trả mã, không đọc lại.
     - `rejected` → đọc `system.get_info` một lần (spec R3), hiện uptime; không gửi lại.
     - mọi trường hợp khác (`executed` với bất kỳ code nào, `unknown`) → bước 3.
  3. **Theo dõi** (red-team #15): poll `adminApi.commandResult` (`adminApi.ts:248-249`) mỗi 2 s, rẽ nhánh theo **code**, không theo `kind`; kiểm ngay cả ở lần poll đầu.
     - `OBSERVED` → bước 4.
     - `NOT_OBSERVED`, `OUTCOME_UNKNOWN` → kết quả cuối; không đọc relay.
     - `DEVICE_EXECUTED` → "tracking không có": đọc `system.get_info` một lần, dừng.
     - `SCHEDULED`, `MQTT_PUBLISHED`, `DEVICE_RESPONSE_TIMEOUT`, `MQTT_PUBLISH_UNCONFIRMED`, `DEVICE_ACCEPTED` → chờ tiếp: cổng phía server còn giữ, có thể nâng lên `OBSERVED` (phase 1).
     - Lỗi mạng hoặc HTTP 5xx → nuốt, chờ tiếp. HTTP 404 `COMMAND_NOT_FOUND` → kết quả "Server đã khởi động lại…".
     - Tối đa 180 s; hết giờ → `kind:'tracking_timeout'`, "Chưa có kết quả; server vẫn theo dõi, xem lại sau". Nếu code cuối vẫn là một mã "chưa rõ" → đọc `system.get_info` một lần.
     - `nudge()` cho poll ngay khi trang nhận `command_result` cùng `correlationId`.
  4. **Đọc lại sau `OBSERVED`** (red-team #4, #15): `system.get_info`, `control.get_status` (nếu có trong `allowedCommands`), `system.get_time`.
     - Thử lại tối đa 30 s cho mỗi lệnh khi gặp: `DEVICE_READ_BUSY` (2 s, `useDeviceReads.ts:25`); `DEVICE_REJECTED` có `LANE_FULL` (như `useDeviceReads.ts:30-33`); `busy` (chờ `retry_after_ms` nếu có, L822, L831); `UNAUTHORIZED` (có thể safe mode, L483).
     - Không đọc được `control.get_status` sau 30 s → `kind:'relay_unchecked'`, "Chưa kiểm được trạng thái relay" (kết quả riêng).
     - `verified = isVerifiedReboot(uptimeBefore, info.uptime_seconds)`.
     - `time`: `ntp_synced` và độ lệch `epoch` so với giờ trình duyệt, chỉ để hiển thị.
- **`useDeviceReboot(deviceId, context)`**: `{ phase, outcome, run, cancel, nudge }`; mỗi lúc một lần chạy; `AbortController`; `onScopeDispose(cancel)`; đổi thiết bị → kết quả `null`. Huỷ trên trình duyệt không dừng theo dõi phía server.
- **Bất biến:** mỗi lần `run` gửi `system.reboot` tối đa 2 lần (1 dry-run + 1 thật).

### `DangerConfirmDialog.vue` (`shared/ui/feedback`)
- Thêm default slot ngay dưới `message` (hiện không có slot, :61-116).
- Thêm prop tuỳ chọn `cancelText` (mặc định "Cancel"), `promptText` (mặc định "Please type"). Mặc định giữ nguyên, nên `DeviceListView.vue` và modal C2 không đổi.
- Thêm prop tuỳ chọn `beforeConfirm?: () => Promise<boolean> | boolean`: false thì không emit `confirm` (dùng để kiểm lại lúc bấm, red-team #15). Kiểm phrase giữ nguyên (`:44-47`, có `trim`).

### Panel (`frontend/src/features/devices/components/reboot/DeviceRebootPanel.vue`, mới)
- **Props:** `ReadCardContext` (`reads/readCard.ts:5-14`) + `commandEvent: { correlationId: string } | null` + `outputsKey: number`. **Emit:** `refresh-link`.
- **Nút** "Khởi động lại" (`variant="danger"`): `v-if` theo `rebootGate`; khoá kèm lý do; khoá trong mọi phase khác `idle`/`done`.
- **Bấm nút:** đọc `system.get_info` (uptime trước) và, nếu được gửi, `control.get_status`; cả hai thử lại như bước 4. Rồi mở `DangerConfirmDialog`:
  - `title` "Khởi động lại thiết bị", `dangerActionText` "Khởi động lại", `cancelText` "Huỷ", `promptText` "Gõ";
  - `confirmPhrase` = `rebootPhrase(link)`;
  - slot (red-team #3): "DO1, DO3 đang BẬT (không force) sẽ TẮT"; "DO4 force TẮT, giữ TẮT"; "DO2 force BẬT: có thể TẮT trong lúc khởi động, BẬT lại khi thiết bị lên"; "Luật DO SET_HIGH/SET_LOW (nếu có) chạy ngay sau khởi động và có thể BẬT relay"; không có kênh BẬT → "Không có relay nào đang BẬT"; không đọc được → "Không đọc được trạng thái relay; mọi relay không bị force sẽ TẮT";
  - ghi thời điểm đọc; thiết bị mất kết nối trong lúc khởi động; dữ liệu ở các thẻ sẽ cũ.
- **Dialog cũ** (red-team #15):
  - `outputsKey` đổi trong lúc hộp đang mở → đóng hộp, báo "Trạng thái relay vừa đổi; mở lại để xem danh sách mới";
  - `beforeConfirm`: `rebootGate` khoá, hoặc lần đọc cũ hơn 30 s → không gửi, đọc lại và mở lại hộp với danh sách mới.
- **Trạng thái** (`aria-live="polite"`): "Đang chạy thử…", "Đang gửi lệnh…", "Đang khởi động lại…", rồi kết quả cuối theo `rebootMessage`.
  - Sau `OBSERVED`: uptime trước → sau, firmware, `ntp_synced`/độ lệch đồng hồ, bảng DO sau reboot, `deviations` (cảnh báo) và `notes` (thông tin).
  - `relay_unchecked` hiện riêng.
- **Không có nút "Gửi lại".** Sau kết quả cuối, nút chỉ bật lại theo `rebootGate`.
- **`commandEvent`** cùng `correlationId` đang theo dõi → `nudge()`. Sau đọc lại ở bước 4 → emit `refresh-link`.
- Kích thước: logic ở `rebootPlan.ts` và `useDeviceReboot.ts`; `.vue` dưới ~150 dòng.

### Trang thiết bị (`DeviceDetailView.vue`)
- Gắn `<DeviceRebootPanel v-bind="readCardContext" :command-event="rebootEvent" :outputs-key="outputsRefreshKey" @refresh-link="refreshOutputs" />` cuối cột 1 (:895-930). `readCardContext` có sẵn (:344-351); `refreshOutputs` tăng `outputsRefreshKey` và đọc lại `/link` (:323-329).
- `onEvent` (:276-296), nhánh `command_result` (:284-287): `type.action` là `system.reboot` → gán `rebootEvent`. Code `DEVICE_REBOOT_OBSERVED` → `refreshOutputs()` (cờ online được làm mới, red-team #15) và đặt `infoConfigChangedAt`, `adcConfigChangedAt` = `Date.now()`, kể cả lệnh của chính trang. Lý do: hostname đã lưu mà chưa áp dụng có thể có hiệu lực sau reboot.
- Đổi thiết bị (:355-360): xoá `rebootEvent`; panel tự huỷ theo `deviceId`.

### DO panel (`DeviceOutputsPanel.vue`)
- `STALE_REASONS` (:84-89) thêm `device_reboot_scheduled: 'thiết bị đang khởi động lại'` (red-team #14).

### Modal C2 (`DeviceCommandModal.vue`)
- `catalogActions` (:74-75): bỏ thêm mọi action có `class === 'Flash'`. Comment: class F chỉ đi qua panel có dry-run, confirm và theo dõi. Bộ lọc chỉ là UI; chặn thật ở server (`CONFIRM_REQUIRED`, master-admin).
- Xoá các nhánh `reboot`/`factory_reset`/`ota.apply` gán `confirm_token` (:138-143); giữ nhánh `sms.list` (:144-145).
- `isDestructive` (:102-107) giữ nguyên.

### Thẻ cấu hình
- `configWriteSpecs.ts:33-35`: ghi chú `reboot_recommended` đổi thành "Thiết bị khuyên khởi động lại; master-admin dùng nút Khởi động lại trên trang".

## Architecture

```
DeviceDetailView ──readCardContext, rebootEvent, outputsRefreshKey──► DeviceRebootPanel
   onEvent(command_result system.reboot) ─► rebootEvent ─► nudge(); OBSERVED ─► refreshOutputs()
   onEvent(outputs | connectivity) ─► outputsRefreshKey++ ─► panel closes an open dialog
DeviceRebootPanel ─► reads: system.get_info (uptime before), control.get_status
                  ─► rebootPlan.ts (gate, relays, phrase, messages)
                  ─► DangerConfirmDialog(slot, beforeConfirm: gate + read age ≤ 30 s)
                  ─► useDeviceReboot.run ─► runDeviceWrite(dry-run) ─► runDeviceWrite(real, followJobs:false)
                                         ─► poll commandResult (by code, errors swallowed, 404, ≤ 180 s)
                                         ─► reads with retry (get_info, get_status, get_time) ─► verified?
```

## Related Code Files

- **Create:**
  - `frontend/src/core/commands/useDeviceReboot.ts` + `useDeviceReboot.test.ts`
  - `frontend/src/features/devices/components/reboot/rebootPlan.ts` + `rebootPlan.test.ts`
  - `frontend/src/features/devices/components/reboot/DeviceRebootPanel.vue` + `DeviceRebootPanel.test.ts`
- **Modify:**
  - `frontend/src/core/api/adminApi.ts`
  - `frontend/src/core/commands/useDeviceCommand.ts`
  - `frontend/src/core/commands/useDeviceWrite.ts` + `useDeviceWrite.test.ts`
  - `frontend/src/core/commands/testing/fakeDevice.ts`
  - `frontend/src/shared/ui/feedback/DangerConfirmDialog.vue` + `DangerConfirmDialog.test.ts`
  - `frontend/src/features/devices/components/DeviceCommandModal.vue` + `DeviceCommandModal.test.ts`
  - `frontend/src/features/devices/components/DeviceOutputsPanel.vue`
  - `frontend/src/features/devices/views/DeviceDetailView.vue` + `DeviceDetailView.test.ts`
  - `frontend/src/features/devices/components/reads/configWriteSpecs.ts` + `DeviceReadCards.test.ts`
- **Delete:** không (chỉ xoá đoạn code :138-143 trong modal).

## Tests Before (viết trước, phải đỏ trước khi sửa code)

1. **`rebootPlan.test.ts`:**
   - `relaysBeforeReboot`: DO1 `state:true` → `turnOff`; DO4 `forced:true,state:false` → `forcedOff`; **DO2 `forced:true,state:true` → `forcedOn`** (red-team #3); DO6 pulse `pulseActive:true` → `turnOff`; data rỗng hoặc sai kiểu → `readable:false`.
   - `checkAfterReboot`: DO2 force BẬT về BẬT → không lệch; DO2 force BẬT nhưng sau là TẮT → lệch; DO4 force TẮT mà BẬT → lệch; DO1 không force mà BẬT → chỉ `notes` (luật DO), không lệch.
   - `isVerifiedReboot`: 500 → 4 true; 500 → 600 false; thiếu số → false.
   - `rebootGate`: thiếu `system.reboot` → ẩn; catalog chưa xác minh, offline, thiếu `mqttDeviceId`, đang chạy → khoá đúng lý do.
   - `rebootPhrase` dùng `mqttDeviceId` (`REBOOT-gateway_870c`), không dùng `deviceId`.
   - `rebootMessage`: đủ các mã ở trên, gồm `TOO_SOON`, `HIGH_RISK_REQUIRES_MASTER_ADMIN`, `OBSERVED` đã/chưa xác minh.
2. **`useDeviceReboot.test.ts`** (`fakeDevice`):
   - đường thường: lần gửi 1 `dryRun:true` + `confirm:'REBOOT-gateway_870c'` + payload `{}`; lần 2 `dryRun:false`; poll `SCHEDULED` rồi `OBSERVED`; đọc `system.get_info`, `control.get_status`, `system.get_time`; uptime 500 → 4 → `verified:true`.
   - `OBSERVED` mà uptime sau 600 > trước 500 → `verified:false`.
   - dry-run `rejected` / `unknown` / `not_sent` → không có lần gửi thứ hai.
   - lệnh thật `not_sent` (`IN_PROGRESS`, `TOO_SOON`, `HIGH_RISK_REQUIRES_MASTER_ADMIN`) → không đọc lại.
   - lệnh thật `rejected` `UNAUTHORIZED` → một lần đọc `system.get_info`.
   - **mọi mã ở lần poll đầu** (red-team #15): `OBSERVED` → đi thẳng bước 4; `NOT_OBSERVED` → kết quả cuối; `DEVICE_EXECUTED` → đọc `get_info` một lần.
   - `Acknowledged` → không gửi `system.get_operations`; tiếp tục poll; sau đó `OBSERVED` → bước 4.
   - `DEVICE_RESPONSE_TIMEOUT` rồi `OBSERVED` (server nâng) → bước 4.
   - poll lỗi 3 lần rồi `OUTCOME_UNKNOWN` → kết quả đó; poll 404 → "Server đã khởi động lại…".
   - quá 180 s → `tracking_timeout`.
   - bước 4: `control.get_status` trả `busy` có `retry_after_ms:1000` hai lần rồi thành công → bảng relay có; `LANE_FULL` mãi → sau 30 s `relay_unchecked`.
   - `nudge()` → poll ngay.
   - huỷ hoặc đổi thiết bị → `null`; không poll tiếp.
   - **Không gửi lại:** mọi kịch bản trên, số lần gửi `system.reboot` ≤ 2.
3. **`useDeviceWrite.test.ts`:** `followJobs:false` + `Acknowledged` có `operation_id` → `unknown`, không gửi `system.get_operations`; mặc định giữ nguyên hành vi cũ.
4. **`DeviceRebootPanel.test.ts`:**
   - không có `system.reboot` → không có nút; có nhưng offline → nút khoá, "Thiết bị offline".
   - bấm nút → gửi `system.get_info` và `control.get_status` trước; hộp có dòng cho kênh BẬT không force, force TẮT, **force BẬT**, và dòng luật DO.
   - không có `control.get_status` trong `allowedCommands` → không đọc; cảnh báo chung.
   - nút xác nhận khoá tới khi gõ đúng `REBOOT-gateway_870c`; gõ `REBOOT-<deviceId server>` vẫn khoá.
   - **hộp mở, rồi `outputsKey` đổi** → hộp đóng, có thông báo (red-team #15).
   - **bấm xác nhận khi lần đọc đã 31 s** → không gửi `system.reboot`; đọc lại và mở lại hộp.
   - các phase hiện đúng chữ; trong lúc chạy nút khoá; không có nút "Gửi lại".
   - sau `OBSERVED` đã xác minh: hiện uptime trước → sau, `deviations` và `notes`; chưa xác minh → "Thiết bị báo đã lên lại (chưa xác minh)"; `relay_unchecked` → dòng riêng.
   - `commandEvent` cùng `correlationId` → poll ngay; sau bước 4 emit `refresh-link`.
5. **`DangerConfirmDialog.test.ts`:** slot hiện trong hộp; `cancelText`/`promptText` thay chữ; `beforeConfirm` trả false → không emit `confirm`; không truyền thì như cũ (test :7 có sẵn vẫn xanh).
6. **`DeviceCommandModal.test.ts`:**
   - fixture (:14) thêm `class:'Flash'` cho `system.reboot`, thêm row `system.factory_reset`, `ota.apply` class `Flash`;
   - mới `offers no class F actions`: ba action không có trong danh sách type/action;
   - mới: chọn một action `system` còn lại → `params` không chứa `confirm_token`.
7. **`DeviceDetailView.test.ts`:**
   - có binding và `allowed-commands` có `system.reboot` → có panel; không có → không.
   - `command_result` `system.reboot` + `DEVICE_REBOOT_OBSERVED` → hai thẻ đọc báo cũ (cùng cách test :226-250) và `/link` được đọc lại.
   - event `outputs` → `outputsRefreshKey` tăng (panel nhận).
8. **`DeviceReadCards.test.ts`**, **có chủ đích:** test `tells that the device recommends a reboot, which the server does not offer` (:496-508): đổi tên và chuỗi mong đợi theo ghi chú mới.

## Implement

1. Kiểu API, `DeviceCommandBody`, `followJobs`, `fakeDevice` → test 3; test 2 chạy được (vẫn đỏ).
2. `rebootPlan.ts` → test 1.
3. `useDeviceReboot.ts` → test 2.
4. `DangerConfirmDialog` slot, nhãn, `beforeConfirm` → test 5.
5. `DeviceRebootPanel.vue` → test 4.
6. `DeviceDetailView` gắn panel, chuyển event, làm mới link, đánh dấu thẻ cũ; nhãn DO panel → test 7.
7. Modal C2 lọc class F, xoá nhánh `confirm_token` → test 6.
8. Ghi chú `configWriteSpecs.ts` + assertion có chủ đích → test 8.
9. Chạy toàn bộ vitest và `vue-tsc`.

## Tests After

- Mục 1–8 xanh; test cũ xanh trừ fixture ở mục 6 và assertion có chủ đích ở mục 8.
- Số test vitest ≥ mốc đo trước khi bắt đầu phase, cộng số test mới.
- Kiểm tay trên trình duyệt ở phase 3 (simulator), vì `vue-tsc` và jsdom không phủ được hiển thị thật.

## Regression Gate

- `cd frontend && npx vitest run` xanh, gồm DO panel, thẻ đọc, modal, trang thiết bị.
- `cd frontend && npx vue-tsc --noEmit` sạch.

## Success Criteria

- [x] Nút chỉ hiện cho master-admin (theo `allowed-commands`); khoá đúng lý do.
- [x] Hộp xác nhận nói đúng §6.4 (gồm force BẬT và luật DO); đóng khi relay đổi; kiểm lại lúc xác nhận; chỉ mở khoá khi gõ đúng `REBOOT-<mqttDeviceId>`. (Còn residual L8, L11 đã chấp nhận: DTO relay không có `forced`; cảnh báo force BẬT có thể báo sớm. Xem `plan.md`.)
- [x] Luồng xử lý mọi mã, `Acknowledged`, lỗi poll, 404; mỗi lần chạy gửi `system.reboot` tối đa 2 lần; không có nút gửi lại.
- [x] "Đã khởi động lại" chỉ khi uptime đã ký xác nhận; bảng relay có, hoặc "Chưa kiểm được trạng thái relay".
- [x] Modal C2 không còn lệnh class F và không còn `confirm_token`.
- [x] Reboot quan sát được làm mới cờ online và làm hai thẻ đọc báo cũ.

## Risk Assessment

| Rủi ro | Khả năng × Ảnh hưởng | Giảm thiểu |
|---|---|---|
| Danh sách relay cũ so với lúc bấm | Trung bình × Trung bình | Đóng hộp khi có `outputs`; kiểm tuổi ≤ 30 s lúc xác nhận |
| Tab ẩn làm timer chậm, lần poll đầu đã là mã cuối | Trung bình × Thấp | Rẽ nhánh theo code, kể cả lần poll đầu |
| Tab đóng giữa chừng | Cao × Thấp | Server vẫn giữ cổng và ghi audit; reboot sau đó bị `IN_PROGRESS`/`TOO_SOON` |
| Server khởi động lại giữa chừng | Thấp × Trung bình | Nuốt lỗi poll; 404 có thông báo riêng; cổng mất (rủi ro còn lại ở `plan.md`) |
| Driver DO chưa sẵn sàng ngay sau boot | Cao × Thấp | Thử lại `busy`/`LANE_FULL` 30 s; kết quả riêng nếu vẫn không đọc được |
| Sửa `DangerConfirmDialog` làm đổi màn khác | Thấp × Thấp | Prop mới có mặc định; test có sẵn giữ nguyên |
| Event realtime rơi | Trung bình × Thấp | Poll 2 s là đường chính; event chỉ làm nhanh hơn |

## As built

Nguồn: `reports/phase-02-implementation-report.md` (kèm "Fixes after UI walkthrough" và "Fixes after code review").

**Chỗ code khác plan**
- Thêm hai component không có trong danh sách Create: `RebootConfirmBody.vue` (nội dung slot hộp xác nhận, §6.4) và `RebootResult.vue` (kết quả: uptime trước → sau, firmware, đồng hồ, relay, deviations, notes). Giữ `DeviceRebootPanel.vue` gần 150 dòng (thực tế 168).
- `DangerConfirmDialog` thêm hai prop ngoài kế hoạch: `promptSuffix` ("để xác nhận:") và `inputPlaceholder`. Không có chúng, hộp hiện "Gõ REBOOT-x to confirm:" với placeholder tiếng Anh. Mặc định giữ nguyên, caller cũ không đổi.
- Panel nằm trong thẻ "MQTT Broker Transport" (cột 1) như một section có viền, không phải card riêng; toàn section `v-if` theo `rebootGate.visible`.
- Shape `RebootOutcome`: `kind` gồm `dry_run_failed`, `not_sent`, `rejected`, `observed`, `relay_unchecked`, `not_observed`, `outcome_unknown`, `server_restarted`, `tracking_timeout`, `untracked`. Thêm `uptimeBefore`, `check` (`{deviations, notes}`); `info` = `{uptimeSeconds, firmware}`; `time` = `{ntpSynced, skewSeconds}`. `runReboot` nhận thêm `relaysBefore`, `onRealCommand`, `wait`; `nudge(id?)` bỏ qua correlation id khác.
- Refusal `Failed` của server cho lệnh thật (chỉ lệnh thật): `HIGH_RISK_REQUIRES_MASTER_ADMIN`, `CONFIRM_REQUIRED`, `DEVICE_PERMISSION_REQUIRED` coi là `not_sent` (từ chối trước khi ký), không đọc lại, không poll. Phân loại chung không đổi. `ContractUnavailable` (`IN_PROGRESS`, `TOO_SOON`, `CONFIRM_MISMATCH`, payload) vốn đã là `not_sent`.
- Quy tắc retry đọc (`readWithRetry`, 30 s mỗi lần đọc): `DEVICE_READ_BUSY`; `DEVICE_REJECTED` có detail bắt đầu bằng `LANE_FULL`, `busy` hoặc `UNAUTHORIZED`; chờ `retry_after_ms` kẹp 0.5-5 s, không có thì 2 s. Lỗi thrown và mã khác không retry. Thiếu `control.get_status` hoặc `system.get_info` trong `allowedCommands` thì không gửi: thiếu status → `relay_unchecked`; thiếu uptime → `verified:false`.
- Preflight: nếu không đọc được uptime đã ký, hộp vẫn mở với cảnh báo "kết quả sẽ không được xác minh" thay vì chặn.
- Hộp đóng khi có `outputs` (walkthrough bug 1, đã sửa): nếu trạng thái server (đọc `adminApi.deviceOutputs`) khớp danh sách đang hiển thị thì hộp mở; khác thì đóng với "Trạng thái relay vừa đổi"; state stale hoặc không đọc được thì đóng với "Danh sách relay không còn đáng tin".
- `prepare()` khi mở hộp xoá kết quả và thông báo lần trước (walkthrough bug 2).
- `useDeviceReboot` đặt ở page scope (`DeviceDetailView`) và truyền xuống panel qua prop `reboot` (review M1). Đổi thiết bị thì cancel.
- `rebootPlan.ts` và test chuyển sang `core/commands/`; `RebootKind` nằm ở core và được re-export (review L9). Không còn import `core` → `features`.
- Modal C2 chỉ hiện action có class khác `Flash`; không có class thì ẩn (fail closed, review L10).
- Trả lời lỗi poll: lỗi mạng và 5xx bị nuốt; chỉ 404 kết thúc. 404 là "server đã khởi động lại" chỉ khi server đã nhận lệnh gửi hoặc có poll trước đó; nếu không thì `outcome_unknown` với mã `REBOOT_SEND_UNCONFIRMED` ("Không rõ lệnh có tới server không; chưa rõ kết quả") (review L2).
- Mã không nhận ra khi đang poll kết thúc run là `outcome_unknown`, không poll tiếp 180 s.
- `fakeDevice`: `pollError` đếm số poll lỗi sau câu trả lời đầu; `results` nhận cả entry `Error`.
- Class đọc từ `/me/command-catalog` là chuỗi `Flash` (server serialize enum bằng `JsonStringEnumConverter`).

**Sửa sau code review (frontend)**
- M1 (tab switch huỷ lần chạy): state reboot ở page scope.
- M2 (`relay_unchecked` không nói verified): text là "<Đã khởi động lại | Thiết bị báo đã lên lại (chưa xác minh)>. Chưa kiểm được trạng thái relay".
- M3 (preflight đọc sau unmount): panel abort controller khi scope dispose; `readWithRetry` không gửi thêm sau pause bị abort.
- Server M3 (busy shape): regex retry anchored `/(^|:\s*)(LANE_FULL|busy|UNAUTHORIZED)\b/i`.
- L2, L3 (chờ so sánh relay đang chạy trước khi gửi, `stillValid` await promise), L9, L10: xem trên.

**Residual đã chấp nhận (không sửa):** L1 (phrase rỗng; gate kiểm lại và server `CONFIRM_REQUIRED` chặn), L4 (mở lại hộp không giải thích), L5 (không kiểm lại giữa dry-run và lệnh thật), L6 (`readWithRetry` lặp phân loại đọc), L7 (`nudge` cũng rút ngắn pause retry đọc), L8 (DTO không có `forced`), L11 (cảnh báo force BẬT có thể sớm, spec gap). Đã vào "Rủi ro còn lại" của `plan.md`.

**Số đo**
- Vitest: 325 → 475 (40 file, 0 lỗi, chạy 2 lần).
- `vue-tsc --noEmit`: sạch. `vite build`: OK.
- Chưa chạy trình duyệt trong phase này; kiểm UI chạy ở phase 3 (scenario 8, do controller).

**Ghi chú:** `App.vue` (fallback shell, `v-else`) vẫn liệt kê action class F nhưng gửi không có `confirm`/payload nên server trả `CONFIRM_REQUIRED` trước khi ký. Không phải đường gửi thứ hai (review frontend, thông tin).
