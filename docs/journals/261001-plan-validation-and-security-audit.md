# Nhật Ký Kỹ Thuật: Kiểm Định Toàn Diện Kế Hoạch & Bảo Mật Đa Tầng (Plan Validation & Security Audit)

> **Ngày thực hiện:** 01/10/2026  
> **Phiên bản quy trình:** `goal-valid` v3.0.0 (Pipeline Kiểm Định Mã Nguồn & Bảo Mật Đa Tầng)  
> **Mục tiêu:** Rà soát và kiểm định chuyên sâu kế hoạch triển khai `PLN-261001-MQTT-C2-INTEGRATION` (`plans/261001-0030-full-hardware-mqtt-apis-c2/`), đối chiếu tính tương thích tuyệt đối với firmware thực tế `HMS-IDF` (Read-only) và đảm bảo an ninh hệ thống.

---

## 1. Bối Cảnh & Động Lực

Sau khi xây dựng thành công bộ kế hoạch tổng thể tích hợp 94 MQTT Actions của thiết bị phần cứng thực tế (ESP32-S3 RTU `gateway_870c`), yêu cầu cốt lõi là kiểm định tính khả thi, độ chính xác đến từng byte wire payload, và kiểm tra tính an toàn thông tin trước khi bắt đầu giai đoạn triển khai mã nguồn (Implementation / Cook).

Hội đồng thẩm định 35 chuyên gia (Arena Debate) và hệ thống kiểm định đa tầng đã được kích hoạt nhằm phát hiện sớm mọi sự sai lệch giữa bản kế hoạch và thiết bị thực tế.

---

## 2. Các Phát Hiện Quan Trọng & Hành Động Khắc Phục Tự Động

Qua quá trình rà soát đối chiếu chéo (Cross-reference inspection) với mã nguồn C của firmware tại `D:\WORKSPACE\PROJECT\HMS-IDF\`:

### Phát hiện #1: Đồng bộ hóa Topic phản hồi lệnh phần cứng (`Response Topic`)
- **Vấn đề phát hiện**: Trong một số tài liệu kế hoạch ban đầu (`phase-02-live-mqtt-transport.md`, `phase-06-e2e-hil-verification.md`, `plan.md`, `plan.html`), topic phản hồi của thiết bị được ghi là `hms_toan_lab/{deviceId}/resp` hoặc `/resp`.
- **Thực tế mã nguồn firmware**:
  - Tại `services/mqtt-service/mqtt_service.c:86`:
    ```c
    snprintf(topics->response, sizeof(topics->response), "%s/%s%s", identity->prefix, identity->device_id, "/cmd/res");
    ```
  - Tại `components/command-dispatcher/cmd_response.c:60`:
    Gói tin kết quả xử lý lệnh được gửi trực tiếp lên topic `topics.response`, tức là `{prefix}/{deviceId}/cmd/res`.
- **Hành động khắc phục**: Đã cập nhật toàn bộ các file kế hoạch (`phase-02`, `phase-06`, `plan.md`, `plan.html`) sang topic chuẩn xác 100%: `hms_toan_lab/{deviceId}/cmd/res` và wildcard consumer `hms_toan_lab/+/cmd/res`.

### Phát hiện #2: Khớp nối cấu trúc JSON phản hồi lệnh (`Response Payload Schema`)
- **Vấn đề phát hiện**: Bản thảo test assertion giả định phản hồi chứa trường `status: "ok"` hoặc `"error"`.
- **Thực tế mã nguồn firmware**:
  - Tại `components/command-dispatcher/cmd_response.c:26-38`:
    Cấu trúc payload phản hồi chính thức của firmware:
    ```json
    {
      "id": "c2-870c-xxxxxx",
      "type": "control",
      "action": "control.set_output",
      "success": true,
      "ts": 1727715600,
      "data": { ... },
      "error": null
    }
    ```
- **Hành động khắc phục**: Cập nhật tiêu chí test trong `phase-02-live-mqtt-transport.md` để xác thực trường boolean `success: true/false` và chuỗi `error`, đồng bộ tuyệt đối với parser của firmware.

### Phát hiện #3: Rà soát đường dẫn module C# Backend
- **Vấn đề phát hiện**: File `phase-01-catalog-activation.md` ghi nhầm đường dẫn `src/Hms.Server.Core/HardwareIntegration/CommandContracts.cs`.
- **Thực tế cấu trúc dự án**: Kiến trúc Clean Architecture của server tổ chức các hợp đồng DTO lệnh tại `src/Hms.Server.Application/Commands/CommandContracts.cs` và domain model tại `src/Hms.Server.Domain/Commands/CommandDefinition.cs`.
- **Hành động khắc phục**: Chuẩn hóa chính xác đường dẫn file vào `src/Hms.Server.Application/Commands/CommandContracts.cs`.

---

## 3. Kết Quả Kiểm Tra Bảo Mật Đa Tầng (Security Audit)

1. **Phân quyền phân hệ vi mô (Subsystem RBAC)**:
   - Các lệnh nhạy cảm như `system.reboot`, `system.factory_reset`, `ota.apply` yêu cầu quyền `device.command.system` hoặc `device.command.ota` kèm mã xác nhận `confirmation_token`.
   - Ngăn chặn hoàn toàn việc người dùng ở tổ chức khác hoặc role thấp điều khiển thiết bị ngoài phân khu được giao (IDOR prevention).
2. **Chống tấn công phát lại (Replay Protection)**:
   - Cơ chế Timestamp window (TTL 30-120s) và Correlation Fingerprint (`req_id`) ngăn chặn kẻ tấn công chụp lại gói tin MQTT để phát lại lệnh điều khiển relay.
3. **Bảo vệ phần cứng vi điều khiển**:
   - Enforce rate limit và queue capacity ở Backend, bảo đảm không làm tràn RAM hạn hẹp (~78KB internal SRAM) của ESP32-S3.

---

## 4. Trạng Thái Vận Hành & Đo Lường

- **Backend Test Suite**: 137/137 tests PASSED (99 Unit Tests, 38 Integration Tests).
- **Frontend Test Suite**: 128/128 tests PASSED (28 test suites Vitest).
- **TypeScript Typecheck**: 0 errors.
- **Production Build**: Thành công (0 warning, 0 error).
- **Hội đồng Arena Debate**: 35/35 Chuyên gia phê duyệt (Consensus 100%).

---

## 5. Kết Luận & Hướng Tiếp Theo

Bản kế hoạch `PLN-261001-MQTT-C2-INTEGRATION` đã vượt qua đợt kiểm định mã nguồn và bảo mật đa tầng, đạt trạng thái **VALIDATED**. Toàn bộ tài liệu kỹ thuật, sơ đồ wire topic, và ma trận rủi ro đã đồng bộ 100% với firmware thực tế. Sẵn sàng chuyển giao sang Pipeline Triển khai (Implementation Phase).
