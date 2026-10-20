---
title: "Tích Hợp Toàn Diện 94 MQTT Actions & Bảng Điều Khiển C2 Phần Cứng Thực Tế (HMS-SERVER & HMS-IDF)"
status: in-progress
blockedBy: [261005-0556-device-permissions-what-where]
version: "1.0.0"
author: "Antigravity Autonomous Pair-Programmer"
created: "2026-10-01"
phases:
  - id: 1
    title: "Kích Hoạt Danh Mục Lệnh & Phân Quyền Đa Phân Hệ"
    file: "phase-01-catalog-activation.md"
    status: pending
  - id: 2
    title: "Tầng Vận Chuyển Lệnh MQTT & Lắng Nghe Phản Hồi Hai Chiều"
    file: "phase-02-live-mqtt-transport.md"
    status: pending
  - id: 3
    title: "Tích Hợp Các Phân Hệ Cốt Lõi: Relay Control, ADC Sensor & System Diagnostics"
    file: "phase-03-control-telemetry-subsystems.md"
    status: pending
  - id: 4
    title: "Tích Hợp Các Phân Hệ Nâng Cao: Network, Modbus RS485, SMS & OTA Firmware"
    file: "phase-04-advanced-subsystems.md"
    status: pending
  - id: 5
    title: "Trung Tâm Điều Khiển C2 Web UI & Bảng Điều Khiển Chuyên Biệt"
    file: "phase-05-frontend-c2-console.md"
    status: pending
  - id: 6
    title: "Kiểm Thử Tích Hợp Phần Cứng Thực & HIL End-to-End"
    file: "phase-06-e2e-hil-verification.md"
    status: pending
---

# Kế Hoạch Triển Khai: Tích Hợp Toàn Diện 94 MQTT Actions & Bảng Điều Khiển C2 Phần Cứng Thực Tế

> **Mã kế hoạch:** `PLN-261001-MQTT-C2-INTEGRATION`  
> **Kiến trúc tham chiếu:** [`docs/architecture-device-mqtt-api-catalog.md`](file:///d:/WORKSPACE/PROJECT/HMS-SERVER/docs/architecture-device-mqtt-api-catalog.md)  
> **Mã nguồn phần cứng mục tiêu:** `D:\WORKSPACE\PROJECT\HMS-IDF` (Nguyên tắc bất biến: **READ-ONLY 100%**)  
> **Quy chuẩn giao diện:** Phong cách Datacake UI; **bảo lưu tuyệt đối màu cam thương hiệu (`#f97316` / `#ea580c`) trên các nút nhấn**.

---

## 1. Tóm Tắt Mục Tiêu Chiến Lược

Hiện thực hóa toàn bộ kết quả khảo sát 94 actions và kiến trúc wire protocol đã được xác minh để biến Web UI của `HMS-SERVER` thành một **Trung tâm chỉ huy & điều khiển thiết bị công nghiệp (Industrial IoT C2 Console)** đầy đủ tính năng:
1. **Kích hoạt danh mục lệnh**: Kích hoạt 94 actions qua phân quyền Subsystem RBAC, bảo vệ lệnh nguy hiểm bằng token xác thực 2 bước.
2. **Kênh truyền thông hai chiều thời gian thực**: Sử dụng MQTT QoS 1 với topic `hms_toan_lab/{deviceId}/cmd` và `hms_toan_lab/{deviceId}/cmd/res`, đồng bộ tức thời lên Web UI qua SignalR.
3. **Bảng điều khiển chuyên dụng**:
   - Tab Relay Outputs: 4 công tắc rơ-le DO1-DO4 + nút kích xung.
   - Tab ADC Sensors: Giám sát 8 kênh analog trực quan.
   - Command Console Modal: Soạn thảo tham số JSON linh hoạt với auto-complete mẫu.
4. **Kiểm thử trên phần cứng thực (ESP32-S3 RTU `gateway_870c`)**: Kiểm tra round-trip thực tế trên broker `broker.emqx.io:1883` mà không sửa đổi bất kỳ dòng code nào của firmware.

---

## 2. Lộ Trình 6 Giai Đoạn (6-Phase TDD Roadmap)

```mermaid
flowchart TD
    P1["Phase 01: Catalog Activation & Subsystem RBAC"] --> P2["Phase 02: Live MQTT Transport & Ingestion Loop"]
    P2 --> P3["Phase 03: Core Subsystems (DO Relay, ADC, System)"]
    P2 --> P4["Phase 04: Advanced Subsystems (Network, Modbus, SMS, OTA)"]
    P3 --> P5["Phase 05: Frontend C2 Console & Specialized Tabs"]
    P4 --> P5
    P5 --> P6["Phase 06: E2E Hardware-in-the-Loop (HIL) Verification"]
```

| Phase | Trọng tâm bàn giao | File chính | Test Suite |
| :---: | :--- | :--- | :--- |
| **01** | Kích hoạt 94 actions, RBAC 12 subsystems, phân loại Dangerous | [`phase-01-catalog-activation.md`](file:///d:/WORKSPACE/PROJECT/HMS-SERVER/plans/261001-0030-full-hardware-mqtt-apis-c2/phase-01-catalog-activation.md) | `SubsystemCatalogActivationTests.cs` |
| **02** | Live MQTT Transport, Response Consumer, SignalR Event Hub | [`phase-02-live-mqtt-transport.md`](file:///d:/WORKSPACE/PROJECT/HMS-SERVER/plans/261001-0030-full-hardware-mqtt-apis-c2/phase-02-live-mqtt-transport.md) | `MqttCommandTransportTests.cs` |
| **03** | Endpoints điều khiển rơ-le DO, đọc cảm biến ADC, system health | [`phase-03-control-telemetry-subsystems.md`](file:///d:/WORKSPACE/PROJECT/HMS-SERVER/plans/261001-0030-full-hardware-mqtt-apis-c2/phase-03-control-telemetry-subsystems.md) | `DeviceControlApiTests.cs` |
| **04** | Cấu hình mạng Ethernet/Wi-Fi/4G, Modbus 5s cooldown, SMS paging, OTA | [`phase-04-advanced-subsystems.md`](file:///d:/WORKSPACE/PROJECT/HMS-SERVER/plans/261001-0030-full-hardware-mqtt-apis-c2/phase-04-advanced-subsystems.md) | `AdvancedSubsystemsTests.cs` |
| **05** | UI Tab Outputs, Tab ADC, Modal tham số thông minh, Nút cam | [`phase-05-frontend-c2-console.md`](file:///d:/WORKSPACE/PROJECT/HMS-SERVER/plans/261001-0030-full-hardware-mqtt-apis-c2/phase-05-frontend-c2-console.md) | Vitest Component Tests |
| **06** | HIL test thực tế trên ESP32-S3 RTU qua EMQX broker | [`phase-06-e2e-hil-verification.md`](file:///d:/WORKSPACE/PROJECT/HMS-SERVER/plans/261001-0030-full-hardware-mqtt-apis-c2/phase-06-e2e-hil-verification.md) | `HilHardwareE2ETests.cs` |

---

## 3. Ma Trận Rủi Ro 12 Chiều & Tiêu Chuẩn Chấp Nhận
Toàn bộ 12 chiều kiểm thử rủi ro (Null/Undefined, Boundary Limits, Heap Overflow, Concurrency, Cold Start, Auth Bypass, Extreme Scale, Network Flakiness, Error Cascade, Data Integrity, Security Injections, Business State) đã được bóc tách chi tiết tại:
👉 [`plans/261001-0030-full-hardware-mqtt-apis-c2/scenario-matrix.md`](file:///d:/WORKSPACE/PROJECT/HMS-SERVER/plans/261001-0030-full-hardware-mqtt-apis-c2/scenario-matrix.md).

---

## 4. Kỷ Luật Triển Khai (Execution Rules)
1. **TDD Test-First**: Mỗi phase bắt buộc viết Unit/Integration Tests đỏ trước khi hiện thực logic nghiệp vụ.
2. **Không làm gãy Baseline cũ**: 137 test backend và 128 test frontend hiện tại phải duy trì tỷ lệ đỗ 100%.
3. **READ-ONLY trên `HMS-IDF`**: Tuyệt đối không chạm vào code firmware trong `D:\WORKSPACE\PROJECT\HMS-IDF`.
4. **Bảo lưu màu cam**: Mọi nút nhấn thực thi/điều khiển chính trên Web UI phải mang mã màu cam (`#f97316` / `#ea580c`).
5. **Cấm tự ý commit/push**: Phải xin ý kiến người dùng trước khi tạo bất kỳ commit nào.
