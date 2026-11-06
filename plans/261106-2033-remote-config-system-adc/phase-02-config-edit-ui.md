---
phase: 2
title: "UI: form sửa trên hai thẻ"
status: completed
priority: P1
dependencies: [1]
---

# Phase 2: UI — form sửa trên hai thẻ

## Overview

Thêm luồng ghi cấu hình vào hai thẻ có sẵn, "Thiết bị & thời gian" và "Nguồn & ADC". Mỗi lần ghi theo trình tự:
1. Xác nhận giá trị cũ → mới.
2. Gửi một lần.
3. Nếu thiết bị báo job còn chạy thì chờ job xong.
4. Đọc lại bằng chính các bước đọc của thẻ.

Giá trị hiển thị luôn lấy từ lần đọc lại. Không bao giờ tự gửi lại lệnh ghi. Quy tắc "khi nào phải đọc lại" dùng
chung với DO panel, không viết bản thứ hai.

## Requirements

### Quy tắc chung (`frontend/src/core/commands/useDeviceCommand.ts`)
- **Tách quy tắc của DO panel** (`DeviceOutputsPanel.vue:170-219`) thành hàm thuần `mayHaveRun(outcomeOrError)`:
  - trả `false` chỉ khi `certainlyNotSent(dispatch)` (`ContractUnavailable`, `TransportNotConfigured`, mã NeverSent), hoặc request lỗi với HTTP < 500;
  - mọi trường hợp khác trả `true`: `Failed` với bất kỳ mã nào (`DEVICE_PERMISSION_REQUIRED`, `IDEMPOTENCY_*`, `OUTCOME_UNKNOWN_AFTER_RESTART`, `DEVICE_REJECTED`, `DEVICE_RESPONSE_TIMEOUT`…), `MqttDeferred`, `Acknowledged` không có `operation_id`, HTTP 5xx, lỗi mạng, hết giờ poll.
- **DO panel** chuyển sang dùng hàm này; test DO panel phải giữ nguyên kết quả.

### `frontend/src/core/commands/useDeviceWrite.ts` (mới)
- **`runDeviceWrite(deviceId, body, { csrfToken, signal })`** trả `WriteOutcome`:
  - `kind`: `not_sent` | `executed` | `rejected` | `job_failed` | `unknown`;
  - kèm `dispatch`, `final`, `code`, `detail`, `data`.
- **Gửi:** qua `runDeviceCommand`. Lỗi ném ra (HTTP, mạng) được bắt và phân loại bằng `mayHaveRun`.
- **Phân loại:**
  - `mayHaveRun` sai → `not_sent`; đây là trường hợp duy nhất không đọc lại;
  - `Executed` → `executed`;
  - `Failed` + `DEVICE_REJECTED` → `rejected` (spec R3: vẫn phải đọc lại);
  - mọi trường hợp còn lại → `unknown`.
- **Job còn chạy:** `Acknowledged` với detail `operation_id=<id>` (giữ `<id>` là string):
  - gửi `system.get_operations {operation_id}` mỗi 1.5 s, tối đa 60 s;
  - poll bị `DEVICE_READ_BUSY` thì thử lại sau 2 s, vẫn trong cửa sổ 60 s;
  - `state:"terminal"` + `resultSuccess:true` → `executed`, `resultSuccess:false` → `job_failed` (kèm `resultCode`/`resultMessage`);
  - `state:"unknown"` (job đã mất, ví dụ thiết bị reboot) hoặc hết giờ → `unknown`. Trường camelCase theo spec §5.
- **`useDeviceWrite(deviceId)`:**
  - trả `{ writing, run, cancel }`; mỗi lúc chỉ một lệnh;
  - huỷ bằng `AbortController`; `onScopeDispose(cancel)`;
  - gắn `deviceId` lúc gửi; thiết bị đã đổi thì trả `null`.

### `DeviceReadCard.vue`
- **Slot props mới:**
  - `refresh(steps): Promise<Record<string, ReadState>>`: huỷ lần đọc đang chạy, chờ nó dừng, rồi đọc lại các bước đó (đã lọc theo `allowedCommands`, giống `start()`), và trả state sau khi đọc;
  - `lockReads(on: boolean)`: khoá hoặc mở nút "Đọc"/"Dừng" của thẻ trong lúc ghi.
- **Lý do:** `useDeviceReads.read()` bỏ qua khi thẻ đang đọc (`useDeviceReads.ts:92-93`), nên không gọi thẳng được.
- **Kiểm thời điểm:** state đọc lại chỉ được dùng khi `readAt` sau thời điểm lệnh ghi kết thúc.
- **Prop `configChangedAt: number | null`:** state đọc trước mốc này bị coi là cũ.

### Điều kiện bật một control
Control chỉ bật khi đủ cả 4 điều kiện:
1. `allowedCommands` có:
   - lệnh ghi đó;
   - mọi bước đọc lại của nó;
   - `system.get_operations`.
2. Catalog đã xác minh và link online.
3. Lần đọc tương ứng gần nhất là `ok`, trong vòng 60 s, và không cũ hơn `configChangedAt`.
4. Thẻ không đang ghi.

Khi chưa bật được, hiện lý do:
- "Đọc lại trước khi sửa";
- "Thiết bị offline";
- "Thiếu quyền `<mã>`";
- "Đang ghi…".

### Thẻ "Thiết bị & thời gian" (`DeviceInfoTimeCard.vue`)
- **Hostname:**
  - nút "Sửa"; input kiểm cùng luật với server;
  - đọc lại bằng `system.get_hostname`.
- **Múi giờ:**
  - select từ `TIMEZONES`; giá trị hiện tại không nằm trong danh sách thì hiện nguyên chuỗi;
  - đọc lại bằng `system.get_time`.
- **"Đồng bộ NTP":**
  - gửi `system.sync_ntp`, rồi đọc lại `system.get_time`;
  - hiện `ntp_synced`.
- **NTP server:** chỉ hiển thị (`ntp_servers`, `ntp_mode` nếu có). Không có control sửa (`set_ntp_servers` đóng).
- **Kiểm đồng hồ** sau mỗi lần đọc lại `get_time` do lệnh ghi: |`epoch` thiết bị − giờ trình duyệt| > 30 s → cảnh báo "Đồng hồ thiết bị lệch N s; lệnh sau có thể bị từ chối". Mốc giờ trình duyệt lấy lúc nhận kết quả.
- **Reply `set_hostname`:**
  - `reboot_recommended:true` → "Thiết bị khuyên khởi động lại; reboot chưa mở trên server";
  - `applied_immediately:false` → "Đã lưu, chưa áp dụng". Khi đó đọc lại vẫn có thể ra giá trị cũ: không coi là lệch, mà báo "chưa áp dụng".

### Thẻ "Nguồn & ADC" (`DevicePowerAdcCard.vue`)
- **Mỗi kênh có `ai` hợp lệ:**
  - "Đổi tên" gửi `adc.set_channel {ai, name, type, enabled}`; `type` và `enabled` lấy từ lần đọc `get_channels`, người dùng không sửa;
  - công tắc gửi `adc.toggle_channel {ai, enabled}`.
- **Đọc lại:** `adc.get_channels` và `adc.get_values`. So cả bản ghi kênh: `name`/`enabled` mới, các trường khác (`type`…) không đổi. Lệch thì cảnh báo, nêu rõ trường nào đổi.
- **Ẩn control khi:** `available:false`, hoặc kênh không có `ai` hợp lệ.

### Kết quả hiển thị (dưới control, `aria-live`)
- `executed` + đọc lại khớp → "Đã ghi và xác nhận".
- `executed` + đọc lại khác → cảnh báo kèm giá trị đọc được.
- `rejected` → mã lỗi + `data.error` nếu có + kết quả đọc lại.
- `job_failed` → `resultCode`/`resultMessage` + kết quả đọc lại.
- `unknown` → "Chưa rõ kết quả; đã đọc lại trạng thái" + giá trị đọc lại. Không có nút "Gửi lại".
- `not_sent`, thông báo theo mã:
  - `DEVICE_WRITE_BUSY` → "Thiết bị đang xử lý một lệnh ghi khác, thử lại sau";
  - `DEVICE_WRITE_RATE_LIMITED` → "Đã ghi quá nhiều lần trong một phút, thử lại sau";
  - `COMMAND_PAYLOAD_INVALID` → "Giá trị không hợp lệ";
  - `CATALOG_STALE` → "Catalog lệnh trên server đã đổi, hãy tải lại trang";
  - HTTP 4xx → "Yêu cầu bị từ chối".
- **`Failed`/`DEVICE_PERMISSION_REQUIRED`** (HTTP 200) → "Thiếu quyền". Mã này không thuộc `certainlyNotSent`, nên theo quy tắc chung vẫn đọc lại; việc đọc lại vô hại.

### Trang thiết bị (`DeviceDetailView.vue`)
- **Đặt mốc dữ liệu cũ:** khi nhận event `command_result` của một trong 5 lệnh cho thiết bị đang xem, đặt `configChangedAt = Date.now()` cho thẻ tương ứng. Thẻ báo "Cấu hình vừa đổi, hãy đọc lại".
- **Bỏ qua:** event của chính lệnh mà thẻ vừa gửi (so `correlationId`).
- **Đổi thiết bị:** huỷ lệnh đang chạy, xoá form, kết quả và `configChangedAt`.

### Modal C2 và danh sách múi giờ
- **`DeviceCommandModal.vue`:** thêm 5 lệnh vào `PANEL_ONLY_ACTIONS`.
- **`frontend/src/core/devices/timezones.ts`:** mirror `ConfigCommandPolicy.SupportedTimezones`, kèm comment trỏ về server; server là bên quyết định.

## Tests first

1. **`useDeviceCommand.test.ts`:**
   - `mayHaveRun` trả `false` cho `ContractUnavailable`, `TransportNotConfigured`, mã NeverSent, HTTP 400/403/409;
   - trả `true` cho `Failed` + `DEVICE_PERMISSION_REQUIRED`/`IDEMPOTENCY_OUTCOME_UNKNOWN`/`DEVICE_REJECTED`, `Acknowledged` không có `operation_id`, HTTP 500, lỗi mạng, hết giờ poll.
2. **`useDeviceWrite.test.ts`:**
   - **Phân loại `kind`:** đủ các trường hợp ở trên.
   - **Job:**
     - `terminal` + success → `executed`; `terminal` + fail → `job_failed`;
     - `unknown` hoặc hết 60 s → `unknown`;
     - poll bị `DEVICE_READ_BUSY` → thử lại.
   - **Không gửi lại:** đếm số lần `sendCommand` gửi action ghi, luôn = 1.
   - **Huỷ:** abort → `null`; đổi thiết bị → `null`.
3. **`DeviceReadCard.test.ts`:**
   - `refresh` trong lúc thẻ đang đọc → vẫn đọc lại và trả state mới;
   - `refresh` lọc theo `allowedCommands`;
   - `lockReads(true)` → nút đọc bị khoá.
4. **`DeviceReadCards.test.ts`:**
   - **Điều kiện bật control:**
     - thiếu lệnh ghi, thiếu bước đọc lại, hoặc thiếu `system.get_operations` → control khoá kèm lý do;
     - lần đọc cũ hơn 60 s, hoặc cũ hơn `configChangedAt` → khoá.
   - **Ghi và đọc lại:**
     - hộp xác nhận hiện cũ → mới;
     - `executed` → thẻ đọc lại đúng bước;
     - đọc lại khác giá trị → cảnh báo;
     - `unknown` → vẫn đọc lại, không có nút gửi lại;
     - `not_sent` → không đọc lại;
     - `DEVICE_PERMISSION_REQUIRED` → "Thiếu quyền" và vẫn đọc lại.
   - **ADC:**
     - "Đổi tên" gửi đủ `{ai, name, type, enabled}` lấy từ lần đọc;
     - đọc lại có `type` khác → cảnh báo nêu `type`;
     - `available:false` → không có control.
   - **Reply `set_hostname`:**
     - `applied_immediately:false` → "Đã lưu, chưa áp dụng", không báo lệch;
     - `reboot_recommended:true` → thông báo khởi động lại.
   - **Đồng hồ:** `epoch` lệch 45 s → cảnh báo.
   - **Đổi thiết bị** trong lúc ghi → không hiện kết quả của thiết bị cũ.
5. **`DeviceOutputsPanel.test.ts`:** giữ nguyên, phải xanh sau khi chuyển sang `mayHaveRun`.
6. **`DeviceCommandModal.test.ts`:** 5 lệnh không có trong danh sách chọn.
7. **Validation thuần (`configRules.ts`):** test bảng cùng các trường hợp như test server (hostname, tên ADC gồm U+202E/U+200B, `type`).

## Implementation Steps

1. Viết test 1–7, chạy để thấy đỏ.
2. `mayHaveRun`; chuyển DO panel sang dùng nó; test DO panel phải xanh.
3. `useDeviceWrite.ts`, `configRules.ts`, `timezones.ts`.
4. `refresh`, `lockReads`, `configChangedAt` trong `DeviceReadCard`.
5. Form trong hai thẻ. Tách `ConfigWriteConfirm.vue` dùng chung nếu thẻ vượt khoảng 200 dòng.
6. `DeviceDetailView`: `configChangedAt` từ `command_result`.
7. `PANEL_ONLY_ACTIONS`.
8. Chạy `vitest` và `vue-tsc`.

## Related Code Files

- **Create:**
  - `frontend/src/core/commands/useDeviceWrite.ts` + test
  - `frontend/src/core/devices/timezones.ts`
  - `frontend/src/features/devices/components/reads/configRules.ts` + test
  - có thể thêm `frontend/src/features/devices/components/reads/ConfigWriteConfirm.vue`
- **Modify:**
  - `frontend/src/core/commands/useDeviceCommand.ts` + test
  - `DeviceOutputsPanel.vue`
  - `DeviceReadCard.vue`, `DeviceInfoTimeCard.vue`, `DevicePowerAdcCard.vue` + `DeviceReadCard.test.ts`, `DeviceReadCards.test.ts`
  - `frontend/src/features/devices/views/DeviceDetailView.vue`
  - `DeviceCommandModal.vue` + test

## Regression Gate

- `npx vitest run` xanh, gồm toàn bộ test thẻ đọc, DO panel và modal.
- `npx vue-tsc --noEmit` sạch.

## Success Criteria

- [x] Năm control chạy đúng luồng xác nhận → ghi → (job) → đọc lại, và kết quả lấy từ lần đọc lại sau khi ghi.
- [x] Một quy tắc đọc lại dùng chung cho DO panel và lệnh cấu hình; không có đường nào tự gửi lại.
- [x] Control chỉ bật khi đủ quyền, có dữ liệu mới (≤ 60 s) và không có thay đổi từ người khác; lý do bị khoá hiện rõ.
- [x] Modal C2 không còn gửi được 5 lệnh này.

## Risk Assessment

- **Đổi DO panel làm hỏng luồng relay:** chỉ thay phần tính, không đổi hành vi; `DeviceOutputsPanel.test.ts` là chốt chặn.
- **Thẻ phình to:** tách hộp xác nhận dùng chung.
- **Giờ trình duyệt lệch:** cảnh báo đồng hồ dựa trên giờ trình duyệt, nên nếu chính máy người dùng lệch thì sẽ báo sai. Ghi rõ trong tooltip.
- **TZ ở frontend lệch server:** server trả `COMMAND_PAYLOAD_INVALID`, UI hiện "Giá trị không hợp lệ".

## As built (2026-11-06)

- **Cấu trúc code (subagent làm, đã review):**
  - logic: `useDeviceWrite.ts`, `useConfigWrite.ts`, `configWriteSpecs.ts`, `configGate.ts`, `configRules.ts`, `adcData.ts`;
  - component: `DeviceTimeConfig.vue`, `AdcChannelTable.vue`, `ConfigWriteConfirm.vue`, `ConfigWriteResult.vue`;
  - fake API dùng chung cho test: `core/commands/testing/fakeDevice.ts`.
- **Nhận ra lệnh của chính mình:** `isOwnWrite` giữ 100 correlation id gần nhất của trình duyệt, trong bộ nhớ module.
- **Lý do khoá mới:** thêm "Catalog lệnh chưa được xác minh".
- **Sửa sau code review:**
  - M-1/L-1: `staleReason` kiểm lại khi bấm xác nhận, bằng giờ hiện tại: lần đọc không bị thay, ≤ 60 s, không cũ hơn `configChangedAt`;
  - L-4: realtime kết nối lại thì đánh dấu dữ liệu cả hai thẻ là cũ.
- **Phần chưa có test riêng:** đường reconnect. Mock hub trong `DeviceDetailView.test.ts` không giả được reconnect; nhánh này chỉ được `vue-tsc` kiểm.
- **Đã chạy trên trình duyệt** với simulator và gateway thật (xem phase 3).
