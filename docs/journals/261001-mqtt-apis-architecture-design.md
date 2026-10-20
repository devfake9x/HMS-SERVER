# Nhật Ký Kỹ Thuật: Thiết Kế Kiến Trúc & Khảo Sát Toàn Bộ 94 MQTT Actions (HMS-IDF)

> **Mã nhật ký**: `JRN-261001-MQTT-APIS-ARCH-DESIGN`  
> **Quy trình thực thi**: Pipeline Thiết Kế Kiến Trúc & Lên Ý Tưởng v3.0.0 (`goal-design`)  
> **Chủ đề**: Kiểm tra cực sâu toàn bộ các API hiện đang có của thiết bị qua MQTT trong project để triển khai trên Web  
> **Nguyên tắc bất biến**: Tuyệt đối **READ-ONLY** trên `D:\WORKSPACE\PROJECT\HMS-IDF` (không sửa source của thiết bị chỉ đọc)  
> **Ngày ghi nhận**: 2026-10-01  
> **Người thực hiện**: Antigravity Autonomous Agent Pair-Programmer

---

## 1. Mục Tiêu & Phạm Vi Công Việc

1. **Khảo sát thực chứng mã nguồn C/C++ (HMS-IDF)**:
   - Rà soát sâu toàn bộ các component `command-dispatcher`, `mqtt-cmd-handlers`, `services/do-service`, `services/mqtt-service`.
   - Xác định chính xác số lượng hành động, namespace, tham số, kiểu dữ liệu, phân lớp an toàn, và điều kiện xác thực.
2. **Kiểm tra tính tương thích với backend `HMS-SERVER` & `frontend`**:
   - Đối chiếu với `PinnedCommandCatalog.cs`, `HmsIdfProvisionalProtocolAdapter.cs`, `DeviceCommandsController.cs`.
   - Đánh giá khả năng hiển thị và điều khiển trên `DeviceCommandModal.vue` và `DeviceDetailView.vue`.
3. **Phân tích rủi ro ngược & Đấu trường tranh biện 35 Chuyên gia**:
   - Đặt vấn đề từ góc độ thất bại (Problem-First Inversion).
   - So sánh 3 phương án kiến trúc frontend và chọn phương án tối ưu.
   - Biên soạn báo cáo tranh biện độc lập giữa 5 hội đồng (35 AI Agents).
4. **Xuất bản bộ tài liệu kiến trúc chuẩn mực**:
   - `research-report.md`
   - `brainstorm-report.html`
   - `arena-debate-report.md`
   - `docs/architecture-device-mqtt-api-catalog.md`

---

## 2. Kết Quả Khảo Sát Thực Chứng

### 2.1 Con số bất biến từ Firmware Manifest (`cmd_descriptor.c`)
- **94 Actions độc nhất** thuộc **12 Subsystems**:
  - `system`: 14 actions (reboot, factory reset, get/set hostname, time, NTP, ping, get operations...)
  - `config`: 3 actions đa miền cho 6 domains (`system`, `network`, `wifi_sta`, `wifi_ap`, `mqtt`, `adc`)
  - `control`: 4 actions điều khiển relay ngõ ra số DO (`set_output`, `pulse`, `get_status`, `set_all`)
  - `modbus`: 6 actions cấu hình và quản trị bus RS485 RTU (`get_rtu_config`, `set_rtu_config`, `restart_rtu`...)
  - `broker`: 16 actions cho data-plane transformation (signals, pipelines, scales, filters, formulas)
  - `network`: 27 actions (Ethernet, priority, 4G modem, WiFi STA, WiFi AP, WiFi scan, known networks)
  - `adc`: 6 actions (đọc và cấu hình 16 kênh ngõ vào tương tự)
  - `battery`: 4 actions (trạng thái pin, cảnh báo, xóa lỗi, kích hoạt chẩn đoán)
  - `ota`: 4 actions (trạng thái phân vùng, URL remote config, kích hoạt tải, apply firmware mới)
  - `sms`: 3 actions (trạng thái modem di động, dung lượng flash/SD, đọc tin nhắn phân trang)
  - `status`: 3 actions (dump trạng thái sức khỏe, dịch vụ hệ thống, giá trị data tags)
  - `mqtt`: 4 actions (trạng thái kết nối, cấu hình host/port/client, reconnect, ẩn mật khẩu)

### 2.2 Các phát hiện cốt lõi ảnh hưởng trực tiếp đến Web
1. **Timestamp Epoch Seconds**:
   - Firmware ESP32 yêu cầu `ts` bằng **giây** (`time(NULL)`). Trong khi backend `HmsIdfProvisionalProtocolAdapter.cs:21` đang dùng `ToUnixTimeMilliseconds()`. Cần sửa thành `ToUnixTimeSeconds()`.
2. **Cấu trúc JSON Envelope**:
   - Cấm đặt `action` ở root (`schema:top_level_action_invalid`). Bắt buộc đặt trong `data.action`.
3. **Cơ chế Token Xác Nhận (Class F/D)**:
   - Các lệnh nguy hiểm bắt buộc chuỗi token: `REBOOT-<DEVID>` và `OTA-APPLY-<DEVID>`.
4. **Phân trang an toàn cho SMS**:
   - Mỗi tin nhắn SMS tốn ~930 bytes RAM trong cJSON. Giới hạn `limit = 10` để bảo vệ 78KB RAM nội bộ của ESP32.
5. **Cooldown 5 giây cho Modbus Restart**:
   - Lệnh `modbus.restart_rtu` trả về `operation_id` bất đồng bộ và có cooldown tối thiểu 5000ms.

---

## 3. Quyết Nghị Thiết Kế Kiến Trúc (Consensus)

1. **Phương Án Frontend: Dynamic Schema-Driven Form Generation**:
   - Không viết 94 component tĩnh. Xây dựng Schema Registry tập trung trong frontend và render generic input controls linh hoạt cho từng action.
   - Tích hợp Danger Confirm Dialog với màu đỏ cảnh báo, bắt buộc nhập đúng Token cho Class F/D.
   - Giữ nguyên màu cam `#f97316` ở nút Dispatch chính theo yêu cầu của người dùng.
2. **Nâng Cấp Backend**:
   - Kích hoạt toàn bộ 94 actions trong `PinnedCommandCatalog.cs` (`CommandAvailability.Enabled`).
   - Sửa lỗi timestamp seconds tại `HmsIdfProtocolAdapter.cs`.
   - Kết nối `IDeviceCommandTransport` tới MQTT Bridge để dispatch thực tế.
3. **Bảo Tồn Toàn Vẹn Firmware (Zero Firmware Modification)**:
   - Không có bất kỳ thay đổi nào trong `D:\WORKSPACE\PROJECT\HMS-IDF`. Toàn bộ giao thức được đáp ứng bằng việc tuân thủ 100% contract sẵn có của RTU.

---

## 4. Các Tài Liệu Đã Xuất Bản

- `research-report.md` (Artifact ID: `research_report`)
- `brainstorm-report.html` (Artifact ID: `brainstorm_report_html`)
- `arena-debate-report.md` (Artifact ID: `arena_debate_report`)
- `docs/architecture-device-mqtt-api-catalog.md` (Artifact ID: `architecture_device_mqtt_api_catalog`)
