# Brainstorm — Cấu hình thiết bị từ xa: nhóm Hệ thống & ADC

> **Cập nhật sau red-team (2026-11-06):** user quyết định đóng `system.set_ntp_servers`, vì trỏ thiết bị tới NTP sai
> giờ có thể khoá thiết bị hoặc mở đường phát lại lệnh cũ. Plan `261106-2033-remote-config-system-adc` mở 5 lệnh.
> Thiết kế bên dưới giữ nguyên làm bản ghi; bản có hiệu lực là plan.

- Ngày: 2026-11-06
- Branch: `claude/mqtt-device-connection-36f705`
- Câu hỏi của user: "đã xong cấu hình chưa? có thể cấu hình từ xa được chưa?"
- Hợp đồng: `HMS-IDF/engineering-docs/api/mqtt-api.md` ở bản ghim `83e93d77e` (§0 R3/R4, §3, §5, §6.1, §6.2)
- Dựa trên: `261106-0100-mqtt-device-link`, `261106-0735-do-relay-control`, `261106-1040-monitoring-read-actions`

## Trả lời câu hỏi

- **Phía server, cấu hình liên kết thiết bị: xong.**
  - Gắn topic, nạp khoá lệnh, danh sách DO được phép điều khiển, giới hạn lệnh đọc.
- **Cấu hình thiết bị từ xa: chưa làm được.** Catalog ghim có 42 lệnh thay đổi thiết bị, server chưa mở lệnh nào trong số đó:
  - 18 lệnh class W (ghi cấu hình);
  - 17 lệnh class C (điều khiển);
  - 3 lệnh class D (xoá);
  - 4 lệnh class F (reboot/OTA/factory reset).
- Hiện server mở 26 lệnh đọc và 2 lệnh relay (`control.set_output`, `control.pulse`).

## Hiện trạng (scout)

- **Phần nền sẵn có:** `HmsIdfCommandProtocolAdapter` đã hỗ trợ token confirm và `dry_run`. `DeviceLinkHandlers` đã lấy `operation_id`. Audit đã tách các class W/C/D/F.
- **Khoá mở lệnh:** `PinnedCommandCatalog.ToDefinition` chỉ mở class R (`EnabledActions`) và class B (`PhysicalOutputActions`). Class W/C/D/F đều ở `DeniedUntilActionContract`.
- **Quyền:** mã quyền firmware cho lệnh ghi có sẵn trong catalog. `DevicePermissionCodes.HighRisk` đã gồm `mqtt.config_edit`, `eth.edit`, `net.priority_edit`, `wifi.sta_edit`, `wifi.sta_connect`, `sys.reboot`, `sys.factory_reset`, `sys.ota`; các mã này chỉ master-admin được gán.
- **Spec §6.1, `config.set` không đồng nhất:** domain `mqtt` bỏ qua nội dung; domain `adc` trả `ESP_ERR_NOT_SUPPORTED`; Wi-Fi chỉ lưu mà không áp dụng.
- **Spec §6.3:** các lệnh disconnect/disable có thể cắt chính kênh MQTT đang dùng.
- **Spec R4:** không có trả lời không chứng minh lệnh chưa chạy; phải đọc lại trước khi gửi lại.
- **Gateway lab:**
  - internet đi bằng 4G (`internet_uplink: modem`);
  - Ethernet có kết nối nhưng không phải đường ra internet (`gateway_type: AUTO`), broker LAN đi đường này;
  - đổi priority, Ethernet hay MQTT sai là mất đường điều khiển. Spec không có cơ chế commit-confirm hay tự hoàn tác.
- **Audit:** `PayloadSummary` lưu nguyên payload. Mở các lệnh Wi-Fi/MQTT (có password) thì phải ẩn trường đó trước.

## Các hướng đã cân nhắc

| # | Hướng | Ưu | Nhược | Kết luận |
|---|---|---|---|---|
| 1 | Mở nhóm Hệ thống & ADC (6 lệnh), có khung an toàn cho lệnh ghi | Không đụng kênh mạng; dựng nền read-back và lane ghi cho các đợt sau | Chưa phải "cấu hình từ xa" đầy đủ | **Chọn** |
| 2 | Thêm mạng/Wi-Fi/MQTT | Đúng nghĩa cấu hình từ xa | Ghi sai là mất gateway, phải ra hiện trường; spec chưa có rollback; cần ẩn password | Để sau, ghi thành spec gap |
| 3 | Mở `config.set` kèm ô JSON trong modal C2 | Nhanh nhất | Không read-back, không kiểm field, lộ secret, spec nói mỗi domain một kiểu | Loại |

## Quyết định (user, 2026-11-06)

- **Phạm vi:** 6 lệnh "Hệ thống & ADC".
- **Thử live:** ghi trên `hms_toan_lab/gateway_870c`, đọc lại, rồi đặt về giá trị cũ. User báo team firmware trước khi chạy.
- **Thiết kế dưới đây đã được duyệt.**
- **Claude tự quyết theo uỷ quyền:**
  - form đặt trên các thẻ đọc có sẵn;
  - quyền dùng mã firmware có sẵn và không đưa vào HighRisk. Lý do: tách quyền xem và quyền sửa như AWS IAM (Get/Put) và K8s RBAC (get vs update); đây là thay đổi tác động thấp;
  - tài liệu liệt kê các role được ghi ngay sau khi mở.

## Thiết kế đã duyệt

### 6 lệnh

| Lệnh | Class | Input server nhận | Đọc lại | Quyền |
|---|---|---|---|---|
| `system.set_hostname` | W | `{hostname}`: `[A-Za-z0-9-]`, 1..63 byte, không bắt đầu hay kết thúc bằng `-` | `system.get_hostname` | `sys.hostname_edit` |
| `system.set_timezone` | W | `{timezone}`: chuỗi TZ POSIX, chọn từ danh sách server biết | `system.get_time` | `sys.time_edit` |
| `system.set_ntp_servers` | C | `{servers}`: 0..3 hostname hoặc IP; rỗng = auto | `system.get_time` | `sys.time_edit` |
| `system.sync_ntp` | C | không có | `system.get_time` (`ntp_synced`) | `sys.time_edit` |
| `adc.set_channel` | W | `{ai: 1..16, name}`, chỉ sửa tên | `adc.get_channels` | `adc.config_edit` |
| `adc.toggle_channel` | C | `{ai: 1..16, enabled: bool}` | `adc.get_channels` | `adc.enable` |

### Server

- **Mở lệnh:** thêm `ConfigWriteActions` (class W/C) trong `PinnedCommandCatalog`, mở qua `ToDefinition` giống `PhysicalOutputActions`. Catalog JSON, hash và từ vựng quyền giữ nguyên.
- **Kiểm payload:** policy riêng cho từng action (tương tự `OutputCommandPolicy`), từ chối mặc định:
  - trường lạ hoặc sai kiểu → 400 trước khi ký;
  - mọi action class W/C ngoài danh sách vẫn đóng.
- **Lane ghi:** mỗi thiết bị có tối đa 1 lệnh ghi đang chờ. Lệnh thứ hai bị từ chối `DEVICE_WRITE_BUSY` và không được publish. Lệnh hết tính sau `ExpiresAt` cộng một khoảng nhỏ, giống lane đọc.
- **Không tự gửi lại:** không có reply → outcome "chưa rõ"; R3 error → vẫn phải đọc lại.
- **Job pending:** reply `ESP_ERR_TIMEOUT` kèm `accepted:true, pending:true, operation_id` → client poll `system.get_operations` tới `terminal`, rồi mới đọc lại.
- **TTL:** giữ mức trần 20 s cho lệnh không phải physical.
- **Audit:** payload của 6 lệnh không chứa secret, chưa cần redaction (vẫn là điều kiện trước khi mở Wi-Fi/MQTT).
- **Lệnh vẫn đóng, có test khẳng định:** `system.set_time`, `system.reboot`, `config.set`, mọi `network.*`/`mqtt.*`, `broker.*`, `modbus.*`, `ota.*`, `system.factory_reset`.

### UI

- **Thẻ "Thiết bị & thời gian":**
  - sửa hostname;
  - chọn múi giờ;
  - danh sách NTP server, có nút "Về tự động";
  - nút "Đồng bộ NTP".
- **Thẻ "Nguồn & ADC":** sửa tên kênh và công tắc bật/tắt cho từng kênh.
- **Hiện nút:** chỉ khi `GET .../allowed-commands` có action đó.
- **Luồng:** form → hộp xác nhận cũ → mới → gửi → đọc lại. Giá trị hiển thị luôn lấy từ lần đọc lại.
- **Khi chạy:**
  - khoá form trong lúc ghi;
  - gắn lệnh với thiết bị lúc gửi; đổi thiết bị thì bỏ kết quả.
- **Kết quả:**
  - không reply → "Chưa rõ kết quả", vẫn đọc lại, không gửi lại;
  - `reboot_recommended:true` → báo cần khởi động lại (reboot vẫn đóng).
- **Modal C2:** ẩn 6 lệnh này (thêm vào `PANEL_ONLY_ACTIONS`).

## Tiêu chí nghiệm thu

- **Mở đúng 6 lệnh:** tổng số lệnh mở = 26 đọc + 2 DO + 6 cấu hình. Catalog, hash và quyền không đổi.
- **Payload sai bị chặn trước khi ký (400):** hostname sai ký tự hoặc quá 63 byte; TZ ngoài danh sách; quá 3 NTP server; `ai` ngoài 1..16; trường lạ.
- **Lane ghi:** hai lệnh ghi cùng lúc trên một thiết bị → lệnh thứ hai `DEVICE_WRITE_BUSY`, không publish.
- **Không quyền:** người không có quyền thì không thấy nút, và `POST` trả 403.
- **Live trên `gateway_870c`:** cả 6 lệnh `Executed`; đọc lại thấy giá trị mới; đặt lại giá trị cũ thì đọc lại thấy giá trị cũ.
- **Sniffer:** chỉ thấy các lệnh trong danh sách, không có lệnh ghi nào khác.
- **Test:** .NET và frontend xanh; `vue-tsc` sạch.

## Ngoài phạm vi

- **Lệnh vẫn đóng:** `system.set_time`, reboot, mạng/Wi-Fi/MQTT, `config.set`, `broker.*`, Modbus, OTA, factory reset.
- **ADC:** sửa `type` của kênh (spec không liệt kê giá trị hợp lệ).
- **Phía server:** lưu lịch sử cấu hình; cấu hình hàng loạt nhiều thiết bị.

## Spec gap (đưa user chuyển cho team firmware, không tự gửi)

- **TZ:** định dạng chuỗi TZ mà `system.set_timezone` chấp nhận chưa được định nghĩa.
- **ADC:** các giá trị `type` hợp lệ của kênh chưa được liệt kê.
- **`system.get_time`:** trả `ntp_servers` và `ntp_mode`, nhưng §6.1 không liệt kê.
- **Hostname:** spec không nói việc đổi hostname có ảnh hưởng MQTT `client_id`/session hay không.
- **Rollback:** chưa có commit-confirm hay rollback cho lệnh đổi mạng và MQTT. Đây là điều kiện để mở nhóm mạng an toàn.

## Rủi ro

- **NTP server sai:** đồng hồ trôi dần, về lâu dài làm lệnh bị `expired`. Giảm bằng cách luôn đọc lại `ntp_synced` và có nút "Về tự động".
- **Tắt kênh ADC:** telemetry của kênh đó dừng trong lúc thử; bật lại ngay sau khi kiểm.
- **Đổi hostname:** nếu firmware lấy `client_id` từ hostname (spec không nói), session MQTT có thể đổi. Giảm bằng cách thử live rồi trả lại ngay, theo dõi heartbeat.
- **Role cũ được ghi ngay:** role đang giữ `sys.hostname_edit`, `sys.time_edit`, `adc.config_edit`, `adc.enable` có quyền ghi ngay khi mở. Liệt kê trong tài liệu.

## Bước tiếp theo

- **Lập kế hoạch:** `/ck:plan --tdd` với báo cáo này. TDD vì thay đổi đường gửi lệnh đã có nhiều test.
- **Trước khi chạy live:** user báo team firmware.
