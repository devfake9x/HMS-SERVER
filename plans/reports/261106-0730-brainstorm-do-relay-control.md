---
type: brainstorm-report
date: 2026-11-06
topic: Điều khiển DO relay qua MQTT (lát điều khiển đầu tiên)
decision: DO relay trước; gateway thật chỉ DO1 (loopback DI1), DO2–DO6 chỉ trên simulator
contract: HMS-IDF engineering-docs/api/mqtt-api.md §6.4 (control), §7, §11
---

# Brainstorm — điều khiển DO relay qua MQTT

> **Đã được thay thế một phần bởi red-team ngày 2026-11-06.** Xem `plans/261106-0735-do-relay-control/plan.md` § Red Team Review. Ba điểm của báo cáo này không còn đúng:
> - Danh sách kênh để trống giờ là **đóng**, không phải "cho phép tất cả".
> - Trạng thái DO lưu trong store riêng và xem cần `gpio.do_view`; không đi qua telemetry `digital_outputs`.
> - Đọc lại sau **mọi** kết quả không chắc chắn, không chỉ sau `Executed`.

## Bối cảnh

- **Đã có:** link MQTT chạy live, 11 lệnh đọc đã ký đều `Executed` trên `gateway_870c` (xem `docs/architecture-mqtt-device-link.md`).
- **Chưa có:** 45 lệnh ghi/điều khiển đều đang `DENIED_UNTIL_ACTION_CONTRACT`. `PinnedCommandCatalog.EnabledActions` hiện chỉ mở class `R`.

## Phương án đã xét

| | Phương án | Ưu | Nhược |
|---|---|---|---|
| **A** | DO relay trước (`set_output`, `pulse`) — **chọn** | Lát nhỏ nhất, thấy ngay trên panel DO; `pulse` đã `SLICE_PASS` trong spec | Đóng/cắt relay thật nên cần chặn an toàn |
| B | Khung ghi chung trước | Dùng lại được cho mọi lệnh ghi | Nhiều việc trước khi có kết quả nhìn thấy |
| C | Mở rộng lệnh đọc trước | Rủi ro thấp | Chưa phải điều khiển |

## Sự thật từ spec và quan sát live (ràng buộc thiết kế)

- `control.set_output`: input `{channel:int>=1, value:bool}` → `channel,state,mode,source`. Permission `gpio.do_write`, class `B`, TTL 120 s, không confirm.
- `control.pulse`: input `{channel, duration_ms}`. **Kênh phải ở pulse mode.** `get_status` ngày 2026-11-06 cho thấy DO1 đang ở `mode:"toggle"`, nên trên thiết bị thật pulse DO1 sẽ bị handler từ chối.
- `control.set_all`: không atomic (HIL được `applied=1/4`), `SLICE_BLOCKED_ENV` → **vẫn tắt**.
- `control.get_status`: `channels[{channel,state,mode,totalCommands,pulseCount,pulseActive}]`. Đây là nguồn đọc lại duy nhất cho DO; heartbeat không chứa trạng thái DO.
- DI không có action MQTT (C12 `SLICE_REJECTED_SCOPE`), nên không xác nhận được đường vòng DO1→DI1 qua MQTT.
- Spec R4/§10: "đã publish" hay "đã nhận" chưa phải bằng chứng; phải đọc lại `get_status`.

## Thiết kế đề xuất

1. **Catalog:** mở `control.set_output` và `control.pulse`, với ngoại lệ có tên rõ ràng cho class `B`. Mọi lệnh ghi khác vẫn bị chặn.
2. **Chặn an toàn theo kênh (server):** cấu hình `AllowedOutputChannels` cho từng thiết bị (seed dev cho gateway: `[1]`). Kênh ngoài danh sách bị từ chối với `DO_CHANNEL_NOT_ALLOWED` trước khi ký. Không cấu hình nghĩa là cho phép tất cả các kênh mà `get_status` đã báo.
3. **Kiểm tra input trước khi ký:** `channel` là số nguyên ≥1, `value` là bool, `duration_ms` là số nguyên dương. Sai thì trả `COMMAND_PAYLOAD_INVALID` mà không publish.
4. **Đọc lại:**
   - Mỗi reply `control.get_status` (bất kể ai gửi lệnh) được chiếu vào telemetry `digital_outputs` và đẩy event `status` qua SignalR, nên panel DO của mọi người xem đều cập nhật.
   - Khi lệnh ghi DO chạy xong (`Executed`), UI tự gửi `control.get_status`.
5. **UI panel DO:**
   - Mỗi kênh có nút Bật/Tắt và nút Pulse (nếu kênh ở pulse mode).
   - Nút ẩn khi thiếu `gpio.do_write` hoặc khi kênh không nằm trong danh sách cho phép.
   - Trạng thái hiển thị đi qua các bước: đang gửi → thiết bị xác nhận → đã đọc lại.
6. **Confirm** chưa cần cho DO (spec không yêu cầu). Phần sửa `confirm` gốc §7 để cho lát reboot/OTA.

## Tiêu chí nghiệm thu

- Gateway thật:
  - `set_output` DO1 ON → `Executed` → `get_status` cho `state:true` → panel hiện ON. Sau đó OFF tương tự.
  - DO2–DO6 bị server từ chối (`DO_CHANNEL_NOT_ALLOWED`), không có gì được publish.
  - `pulse` DO1 khi đang ở mode toggle → `DEVICE_REJECTED` có lý do hiện trên UI.
- Simulator: `set_output` và `pulse` trên 6 kênh chạy trọn vòng, có đọc lại.
- Người dùng thiếu `gpio.do_write` bị chặn. Audit ghi đủ.
- Test unit/integration cho kiểm tra input, danh sách kênh, chiếu `get_status` vào telemetry. Test FE cho panel.

## Ngoài phạm vi

`set_all`; reboot/factory_reset/OTA; network/mqtt/modbus/broker/adc/battery/config ghi; khung ghi chung đầy đủ (dry_run, poll job); các phase 3–6 của plan quyền thiết bị; DI qua MQTT.

## Rủi ro

- Relay thật: chỉ DO1, được chặn ở server bằng danh sách kênh, không chỉ dựa vào UI.
- Hai bên (server và tool HIL của firmware) cùng điều khiển DO1 có thể gây nhiễu kết quả của nhau, nên cần báo team firmware.
- `pulse` cần mode của kênh. Việc đổi mode nằm ngoài MQTT catalog (không có action đổi mode), nên phải làm trên web thiết bị nếu muốn thử pulse thật.

## Điểm chạm

- `src/Hms.Server.Infrastructure/Commands/PinnedCommandCatalog.cs`
- `HmsIdfCommandProtocolAdapter.cs`
- `src/Hms.Server.Application/DeviceLink/*`: chiếu reply `get_status`, chính sách kênh
- `MqttLinkOptions` (seed)
- `frontend/src/features/devices/views/DeviceDetailView.vue` (panel DO)
- Test tương ứng
