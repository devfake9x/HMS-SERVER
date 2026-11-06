---
phase: 3
title: "UI: điều khiển trên panel DO, đọc lại có khoá kênh"
status: completed
priority: P1
dependencies: [1, 2]
---

# Phase 3: UI — DO panel controls

## Overview

Panel DO trở thành **đường điều khiển DO duy nhất**. Mỗi lệnh ghi đều tự đọc lại; kênh bị khoá cho tới khi biết trạng thái thật. Bỏ dữ liệu DO và DI giả.

## Requirements

### Nguồn trạng thái [RT#4, RT#6]
- Lấy từ `GET /devices/{id}/outputs`. Tải lại khi có event `outputs` hoặc khi trang mở.
- Hiện "đọc lúc …" (tuổi của `readAt`). Khi `stale` thì hiện "Trạng thái có thể đã cũ: <lý do>" và kênh chuyển sang **Không rõ**.
- Không bao giờ lấy trạng thái DO từ telemetry hay `status`.

### Chưa đọc hoặc không có quyền
- `OUTPUTS_NOT_READ` → "Chưa đọc trạng thái" kèm nút **Đọc trạng thái** (`control.get_status`).
- Thiếu `canViewOutputs` → "Không có quyền xem DO".
- Bỏ 6 kênh mặc định. [RT#15] DI panel cũng bỏ dữ liệu giả và hiện "DI không đọc được qua MQTT (spec C12)".

### Gating [RT#9]
- Nút điều khiển chỉ hiện khi đủ cả 5 điều kiện:
  - `canControlOutputs` (server tính);
  - kênh nằm trong `allowedOutputChannels`;
  - action `available`;
  - catalog đã qua kiểm tin cậy (giống modal);
  - `canViewOutputs` (cần để đọc lại).

### Điều khiển
- Mỗi kênh: nút **Bật** hoặc **Tắt** tuỳ trạng thái đã đọc. Khi trạng thái là "Không rõ", hiện **cả hai** nút để luôn có đường OFF khẩn cấp.
- **Pulse** chỉ hiện khi `mode === 'pulse'`, có ô `duration_ms` (1..`MaxPulseMs`, mặc định 1000).

### Khoá kênh và đọc lại [RT#2]
- **Kết quả "chắc chắn chưa gửi":** `ContractUnavailable` (`COMMAND_PAYLOAD_INVALID`, `DO_CHANNEL_NOT_ALLOWED`, `DEVICE_ACTION_UNAVAILABLE`…), `MQTT_NOT_CONNECTED`, 4xx từ API. Hiện lỗi và mở khoá ngay.
- **Mọi kết quả khác:** `Executed`, `Acknowledged`, `DEVICE_REJECTED`, `DEVICE_RESPONSE_TIMEOUT`, `MQTT_PUBLISH_UNCONFIRMED`, client poll hết 40 s. Khi đó:
  - giữ khoá kênh;
  - tự gửi `control.get_status`;
  - chỉ mở khoá khi đọc lại xong (hoặc hết 2 lần thử đọc, rồi hiện "Không rõ, hãy đọc lại").
- [RT#6] **Pulse:** đọc lại lần đầu ngay sau khi kết quả về; đọc lại lần hai sau `duration_ms + 1000` ms để thấy relay đã nhả.

### Modal C2 [RT#12]
- Lọc bỏ `control.set_output`, `control.pulse` và `control.set_all` khỏi danh sách action, kèm ghi chú "Điều khiển DO trên panel DO".

### Chung
- Composable `useDeviceCommand` dùng chung cho modal và panel; poll 1 s, tối đa 40 s.
- Correlation id ≤ 39 byte.

## Related Code Files

- **Create:**
  - `frontend/src/core/commands/useDeviceCommand.ts`
  - `frontend/src/core/commands/useDeviceCommand.test.ts`
- **Modify:**
  - `frontend/src/features/devices/views/DeviceDetailView.vue`
  - `DeviceDetailView.test.ts`
  - `frontend/src/features/devices/components/DeviceCommandModal.vue`
  - `DeviceCommandModal.test.ts`
  - `frontend/src/core/api/adminApi.ts` (`deviceOutputs`, các field mới của `DeviceLinkDto`)
- **Test có assertion phải đổi:** `DeviceDetailView.test.ts` (đang khẳng định có "Supply Voltage"; bỏ các khối giả có thể đổi text).

## Tests first (viết trước, phải đỏ)

1. **Composable:**
   - `Published` → poll → `Executed`.
   - Kết quả "chắc chắn chưa gửi" thì dừng ngay.
   - Hết 40 s thì phase `timeout`.
   - `cancel()` thì dừng.
2. **Panel khi chưa đọc:** không có `do-N-state`, có `do-read-status`. DI hiện thông báo, không có trạng thái giả.
3. **Gating:** `allowedOutputChannels:[1]` và đủ quyền → chỉ có `do-1-toggle`. Thiếu `canControlOutputs` hoặc `canViewOutputs` → không có nút nào.
4. **Bấm `do-1-toggle` khi DO1 OFF:**
   - gửi `{channel:1,value:true}`;
   - kênh khoá;
   - sau `Executed` thì tự gửi `get_status`;
   - ON chỉ hiện sau khi `GET /outputs` trả `state:true`;
   - lúc đó mới mở khoá.
5. **`DEVICE_RESPONSE_TIMEOUT`:** kênh vẫn khoá, tự đọc lại, hiện kết quả đọc lại.
6. **`DO_CHANNEL_NOT_ALLOWED`:** mở khoá ngay, không đọc lại, hiện lỗi tại kênh.
7. **Trạng thái stale hoặc không rõ:** hiện cả Bật và Tắt, kèm cảnh báo.
8. **Pulse:** chỉ khi `mode:'pulse'`. Có hai lần đọc lại (lần hai sau `duration_ms+1000`, dùng fake timers).
9. **Modal:** danh sách action không có `control.set_output`/`pulse`/`set_all`. Các test cũ của modal vẫn xanh.

## Implementation Steps

1. Viết các test ở mục "Tests first", chạy để xác nhận đỏ.
2. Composable; modal chuyển sang dùng composable và lọc action DO.
3. Panel DO: nguồn `/outputs`, gating, nút, khoá và đọc lại, pulse, stale.
4. Bỏ DO/DI giả.
5. Chạy `vitest run` và `vue-tsc --noEmit`.

## Success Criteria

- [x] Test mới chuyển đỏ sang xanh; test cũ xanh (trừ chỗ đã liệt kê); `vue-tsc` sạch.
- [x] Không còn dữ liệu DO/DI giả.
- [x] Không có đường nào gửi lệnh DO ghi từ UI mà không khoá kênh và đọc lại.

## Risk Assessment

- **Hai người cùng điều khiển một kênh:** khoá chỉ có tác dụng trong phiên. Trạng thái cuối luôn lấy từ lần đọc lại, và mọi người xem đều nhận event `outputs`.
- **Rollback:** ẩn khối nút, chỉ giữ phần hiển thị trạng thái.

## As built (2026-11-06)

- **Unlock rule (code review H1):** the panel unlocks without a read-back only when `certainlyNotSent` holds (`ContractUnavailable`, `TransportNotConfigured`, `MQTT_NOT_CONNECTED`/`MQTT_RESPONSE_SUBSCRIPTION_MISSING`/`MQTT_COMMAND_TOPIC_INVALID`) or the API answered 4xx. A 5xx, a network error or any other `Failed` dispatch (e.g. `IDEMPOTENCY_OUTCOME_UNKNOWN`) keeps the channel locked and reads back.
- **Offline (review M4):** device offline or server broker link down → every channel shows **Không rõ** with both Bật/Tắt and a warning; "Đọc lúc" shows the reading's age.
- **Loading (review M5):** newest load wins; a failed load clears the state and the controls; a SignalR reconnect and every `outputs` event re-read the state and the link flags.
- **Pulse:** duration must be an integer 1..60000 ms; out of range is refused without sending.
- **Layout:** the hardware card that holds the panel now also shows for any device with an MQTT binding (a bound device without telemetry had no relay control). Channel errors list under the grid.
