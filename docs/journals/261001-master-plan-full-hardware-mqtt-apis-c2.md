# Nhật Ký Kỹ Thuật: Lập Kế Hoạch & Bóc Tách Roadmap TDD (Milestone 4 Plan)

> **Mã nhật ký**: `JRN-261001-MASTER-PLAN-FULL-HARDWARE-MQTT-APIS-C2`  
> **Quy trình thực thi**: Pipeline Lập Kế Hoạch & Bóc Tách Roadmap v3.0.0 (`goal-plan`)  
> **Kế hoạch mục tiêu**: Tích hợp toàn diện 94 MQTT Actions & Bảng điều khiển C2 phần cứng thực tế (HMS-SERVER & HMS-IDF)  
> **Ngày ghi nhận**: 2026-10-01  
> **Người thực hiện**: Antigravity Autonomous Agent Pair-Programmer  
> **Trạng thái**: 🟢 **PLAN READY FOR COOK (Đã qua thẩm định Arena Debate 35 Agents)**

---

## 1. Mục Tiêu & Phạm Vi Bóc Tách (Scope Boundaries)

Dựa trên bản thiết kế kiến trúc chuẩn mực [`docs/architecture-device-mqtt-api-catalog.md`](file:///d:/WORKSPACE/PROJECT/HMS-SERVER/docs/architecture-device-mqtt-api-catalog.md) và kết quả kiểm định mã nguồn đa tầng, kế hoạch này thiết lập lộ trình TDD test-first khép kín gồm **6 Phases** độc lập:
1. **Phase 01**: Kích hoạt 94 actions, RBAC 12 subsystems, phân loại Dangerous.
2. **Phase 02**: Tầng vận chuyển Live MQTT Transport, Response Consumer, SignalR Event Hub.
3. **Phase 03**: Tích hợp phân hệ cốt lõi: Relay DO, ADC Sensor & System Diagnostics.
4. **Phase 04**: Tích hợp phân hệ nâng cao: Network, Modbus RS485, SMS & OTA Firmware.
5. **Phase 05**: Trung tâm điều khiển C2 Web UI & Bảng điều khiển chuyên biệt (Outputs, ADC, Command Modal, nút cam `#f97316`).
6. **Phase 06**: Kiểm thử tích hợp phần cứng thực & HIL End-to-End trên bo mạch ESP32-S3 RTU (`gateway_870c`).

---

## 2. Các Đánh Đổi Thiết Kế Quan Trọng (Key Technical Trade-Offs)

| Khía cạnh | Quyết định đã chốt | Giải pháp thay thế bị loại bỏ | Lý do đánh đổi |
| :--- | :--- | :--- | :--- |
| **Catalog Activation** | Ánh xạ linh hoạt qua DI, giữ an toàn mặc định cho test harness cũ | Xóa cờ `DeniedUntilActionContract` trong toàn bộ codebase | Bảo đảm không làm gãy bất kỳ bài kiểm thử nào trong bộ 137 test backend hiện có. |
| **Transport Seam** | Triển khai `MqttDeviceCommandTransport` trực tiếp với MQTTnet | Tiếp tục dùng `DeferredDeviceCommandTransport` | Cần điều khiển phần cứng thực tế và nhận phản hồi tức thời từ gateway. |
| **Rate Limiting** | Áp dụng token bucket và cooldown 5000ms phía server | Đẩy toàn bộ trách nhiệm rate limit cho vi điều khiển | Bảo vệ vi điều khiển ESP32 với bộ nhớ RAM tự do 78KB khỏi nguy cơ nghẽn hàng đợi hoặc crash heap. |
| **UI Parameters** | Form tham số động kết hợp JSON editor chuyên sâu | Form tĩnh cố định cho từng lệnh | Phục vụ đầy đủ 94 actions mà không làm phình to mã nguồn giao diện (chống code bloat). |
| **Nút Thao Tác** | Cố định mã màu cam thương hiệu (`#f97316` / `#ea580c`) | Dùng màu tím hoặc xanh ngọc của framework mặc định | Tuân thủ tuyệt đối quy tắc người dùng và đồng bộ hoàn hảo với hệ thống Datacake UI theme. |

---

## 3. Hệ Thống Tài Liệu Bàn Giao

1. **Master Plan Markdown**: [`plans/261001-0030-full-hardware-mqtt-apis-c2/plan.md`](file:///d:/WORKSPACE/PROJECT/HMS-SERVER/plans/261001-0030-full-hardware-mqtt-apis-c2/plan.md)
2. **Master Plan Interactive HTML**: [`plans/261001-0030-full-hardware-mqtt-apis-c2/plan.html`](file:///d:/WORKSPACE/PROJECT/HMS-SERVER/plans/261001-0030-full-hardware-mqtt-apis-c2/plan.html)
3. **Scenario Matrix 12 Chiều**: [`plans/261001-0030-full-hardware-mqtt-apis-c2/scenario-matrix.md`](file:///d:/WORKSPACE/PROJECT/HMS-SERVER/plans/261001-0030-full-hardware-mqtt-apis-c2/scenario-matrix.md)
4. **Hồ Sơ 6 Phases**:
   - `phase-01-catalog-activation.md`
   - `phase-02-live-mqtt-transport.md`
   - `phase-03-control-telemetry-subsystems.md`
   - `phase-04-advanced-subsystems.md`
   - `phase-05-frontend-c2-console.md`
   - `phase-06-e2e-hil-verification.md`
5. **Biên Bản Tranh Biện Hội Đồng 35 Agents**: [`plans/261001-0030-full-hardware-mqtt-apis-c2/arena-debate-report.md`](file:///d:/WORKSPACE/PROJECT/HMS-SERVER/plans/261001-0030-full-hardware-mqtt-apis-c2/arena-debate-report.md)
6. **Lộ Trình Cập Nhật**: [`docs/project-roadmap.md`](file:///d:/WORKSPACE/PROJECT/HMS-SERVER/docs/project-roadmap.md)
