# Nhật Ký Kỹ Thuật: Tích Hợp Thiết Bị Thật MAITECH-RTU-870C Vào HMS-SERVER

- **Ngày thực hiện:** 01/10/2026
- **Mã kế hoạch:** `[PLAN] = Thêm chính con RTU mai tech vào đi, để tôi xem các cấu hình hoạt động`
- **Phiên bản:** v3.0.0 (Production-Ready)
- **Trạng thái:** 🟢 HOÀN THÀNH TOÀN DIỆN (100% IMPLEMENTED & VERIFIED)

---

## 1. Mục Tiêu & Phạm Vi Triển Khai

1. **Khởi tạo và Seed thiết bị vật lý thực tế:**
   - Định danh: `MAITECH-RTU-870C`
   - GUID cố định: `870c870c-870c-870c-870c-870c870c870c`
   - Hardware Device ID: `gateway_870c`
   - MQTT Broker: `broker.emqx.io:1883`
   - MQTT Topic Prefix: `hms_toan_lab`
   - Command Topic: `hms_toan_lab/gateway_870c/cmd`
   - Status Topic: `hms_toan_lab/gateway_870c/status`
   - Web Console: `http://maitech-rtu.local/` (Credentials: `admin / admin123`)

2. **Kỷ luật an toàn tuyệt đối:**
   - Source code firmware tại `D:\WORKSPACE\PROJECT\HMS-IDF` được bảo vệ nghiêm ngặt (0 file sửa đổi).
   - Thao tác cấu hình và kiểm thử thiết bị RTU hoàn toàn thực hiện qua Web Interface (`http://maitech-rtu.local/`) và Chrome Remote Debugging (port 9222).

---

## 2. Các Thành Phần Đã Triển Khai

### 2.1 Backend Core & Database Seeding (`Hms.Server.Api` & `Hms.Server.Infrastructure`)
- **Deterministic Seeding (`src/Hms.Server.Api/Program.cs`):**
  - Tự động kiểm tra và seed thiết bị `MAITECH-RTU-870C` với UUID `870c870c-870c-870c-870c-870c870c870c` thuộc Tổ chức chính (`Default Organization`).
  - Thiết lập trạng thái `DeviceLifecycleState.Active` ngay khi khởi tạo backend.
- **Hardware Command Routing (`src/Hms.Server.Infrastructure/Commands/HmsIdfProvisionalProtocolAdapter.cs`):**
  - Cấu hình mapping đặc biệt cho UUID `870c870c-870c-870c-870c-870c870c870c` xuất topic MQTT trực tiếp theo format phần cứng: `hms_toan_lab/gateway_870c/cmd` thay vì UUID fallback.

### 2.2 Telemetry Bridge & Device Synchronization (`tools/mqtt-broker/hms-telemetry-bridge.js`)
- Nâng cấp bridge chạy daemon nền:
  - Tự động login xác thực CSRF (`admin@hms.local`) vào HMS-SERVER Backend.
  - Tự động xác thực JWT/Session Cookie vào Web Server của RTU (`http://maitech-rtu.local/api/auth/login`).
  - Polling định kỳ dữ liệu phần cứng thời gian thực:
    - `/api/system/info`: Hostname (`maitech-rtu`), Uptime, Free Heap, PSRAM, Internal RAM, Active Services.
    - `/api/system/health`: Trạng thái mạng (Ethernet, 4G LTE Viettel), Pin, Nhiệt độ MCU, Điện áp nguồn.
    - `/api/do/status`: Trạng thái 6 kênh Relay Digital Output (DO1 - DO6 kèm GPIO pinout: 21, 35, 37, 40, 38, 41).
    - `/api/di/status`: Trạng thái 4 kênh Digital Input (DI1 - DI4: GPIO 1, 2, 3, 4).
    - `/api/mqtt/config`: Cấu hình broker vận hành (`broker.emqx.io:1883`, Client ID, Prefix).
  - Tự động đẩy payload đầy đủ vào Endpoint `POST /api/v1/devices/{deviceId}/telemetry` của HMS-SERVER và phát tán qua SignalR.

### 2.3 Giao Diện Vận Hành Web (`frontend/src/features/devices/views/DeviceDetailView.vue`)
- Bổ sung Card chuyên biệt: **"Cấu Hình Hoạt Động & Phân Hệ Phần Cứng MaiTech RTU"**:
  - **MQTT Broker Transport Panel:** Hiển thị trực quan Host, Client ID, Topic Prefix, Command Topic và trạng thái `Connected`.
  - **Relay Digital Outputs (DO) Grid:** Hiển thị 6 kênh Relay (DO1-DO6) với GPIO pinout thực tế, chế độ toggle/pulse và trạng thái ON/OFF động.
  - **Digital Inputs (DI) Grid:** Hiển thị 4 kênh Digital Input với trạng thái HIGH/LOW thực tế.
  - **Nút điều hướng chuẩn Datacake Brand:** Nút "Mở RTU Web Console ↗" với màu cam thương hiệu Datacake (`#ea580c` / `#f97316`) mở trực tiếp bảng điều khiển `http://maitech-rtu.local/`.
  - **Hỗ trợ đa dạng cấu trúc dữ liệu:** Xử lý liền mạch cả `snake_case` từ phần cứng và `camelCase` từ API.

---

## 3. Kết Quả Kiểm Thử & Kiểm Định

| Phân hệ kiểm thử | Công cụ / Framework | Số lượng test | Kết quả | Ghi chú |
|---|---|---|---|---|
| **Backend Integration Tests** | xUnit (.NET 10) | 41 / 41 | 100% PASSED | Bao gồm `MaitechRtuDeviceIntegrationTests.cs` (Seed, Routing, Telemetry) |
| **Backend Unit Tests** | xUnit (.NET 10) | 99 / 99 | 100% PASSED | Toàn bộ domain logic, commands, auth |
| **Frontend Tests** | Vitest v3.2.7 | 135 / 135 (29 files) | 100% PASSED | Toàn bộ components, views, permissions, HIL mocks |
| **Frontend TypeScript** | vue-tsc --noEmit | - | 0 LỖI | 100% Type-safe |
| **Live Hardware E2E Verification** | Chrome CDP (Port 9222) | - | HOÀN HẢO | Dữ liệu sống truyền từ `maitech-rtu.local` hiển thị đầy đủ trên Web UI |

---

## 4. Bằng Chứng Thực Nghiệm Trực Quan (Artifacts)
- **Top Card Live Telemetry:** `maitech_rtu_hms_server_live.png`
  - Hiển thị Uptime: `5d 3h 44m`, Free Heap: `1.40 MB`, Ethernet IP: `192.168.1.127`, 4G Viettel: `5/5 bars`, Điện áp: `3.62 V`, Nhiệt độ MCU: `28.4 °C`, SignalR: `connected`.
- **Operating Configuration & Hardware Subsystems Card:** `maitech_rtu_hms_server_full_card_scrolled.png`
  - Hiển thị đầy đủ MQTT Broker Transport, 6 kênh Relay DO1-DO6, 4 kênh Digital Input DI1-DI4 và nút liên kết "Mở RTU Web Console ↗".
