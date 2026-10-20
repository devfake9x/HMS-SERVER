---
phase: 3
title: "Tích Hợp Các Phân Hệ Cốt Lõi: Relay Control, ADC Sensor & System Diagnostics"
status: pending
priority: P1
dependencies: [2]
---

# Phase 03: Tích Hợp Các Phân Hệ Cốt Lõi: Relay Control, ADC Sensor & System Diagnostics

## Overview
Triển khai tích hợp các subsystem có tần suất sử dụng cao nhất: `control` (điều khiển 4 ngõ ra số rơ-le DO), `adc` (giám sát 8 kênh tương tự 4-20mA / 0-10V), và `system` (chẩn đoán hệ điều hành, uptime, free heap, ping, khởi động lại an toàn).

## Requirements
- **Functional**:
  - `control`:
    - `control.set_output`: Bật/tắt relay theo kênh (`channel: 1..4`, `value: 0|1`).
    - `control.pulse`: Kích xung relay (`channel: 1..4`, `duration_ms: 100..60000`).
    - `control.get_status`: Đọc trạng thái hiện tại của 4 relay.
  - `adc`:
    - `adc.get_values`: Đọc điện áp/dòng điện tức thời trên 8 kênh ADC.
    - `adc.get_channels`: Liệt kê cấu hình và trạng thái bật/tắt từng kênh.
    - `adc.toggle_channel`: Bật/tắt một kênh đo analog.
  - `system`:
    - `system.ping`: Đo thời gian đáp ứng round-trip (RTT).
    - `system.get_info`: Đọc phiên bản firmware, build target, chip ID.
    - `system.get_heap`: Đọc dung lượng internal RAM và PSRAM còn trống.
    - `system.reboot`: Yêu cầu tham số `confirm_token: "REBOOT-<DEVID>"`.
- **Non-functional**:
  - Thời gian phản hồi điều khiển rơ-le DO: < 300ms (end-to-end từ Web UI đến tiếp điểm relay vật lý).
  - Tự động đồng bộ trạng thái rơ-le trên UI sau khi nhận được telemetry phản hồi.

## Architecture
- `DeviceControlEndpoints.cs` / `DeviceCommandsController`: Hỗ trợ endpoints chuyên biệt cho điều khiển rơ-le nhanh và đọc cảm biến ADC.
- SignalR Telemetry Bridge: Cập nhật dữ liệu vào `DeviceTelemetryDto`.

## Related Code Files
- Create: `src/Hms.Server.Api/Controllers/DeviceControlController.cs`
- Create: `src/Hms.Server.Application/Devices/DeviceControlService.cs`
- Modify: `src/Hms.Server.Api/Controllers/DeviceCommandsController.cs`
- Create: `tests/Hms.Server.IntegrationTests/DeviceControlApiTests.cs`

## Implementation Steps (TDD First)
1. **Viết Test Đỏ (Red Tests)**:
   - `DeviceControlApiTests.cs`:
     - Test gửi lệnh `control.set_output` với channel 1, value 1 -> Thành công.
     - Test gửi lệnh `control.set_output` với channel 5 (vượt ngưỡng) -> Lỗi 400 Bad Request / Validation Failure.
     - Test gửi lệnh `control.pulse` với duration 0ms -> Lỗi Validation.
     - Test gửi lệnh `system.reboot` không có `confirm_token` -> Lỗi `CONFIRM_TOKEN_REQUIRED`.
2. **Hiện Thực Mã Nguồn (Green)**:
   - Viết `DeviceControlService` và validator tham số cho các lệnh DO, ADC, System.
   - Kết nối với pipeline `SendDeviceCommandHandler`.
3. **Refactor & Verify**:
   - Kiểm tra toàn bộ tests chạy thành công.

## Success Criteria
- [ ] Điều khiển bật/tắt rơ-le 4 kênh hoàn toàn chuẩn xác.
- [ ] Đọc dữ liệu 8 kênh ADC trả về số đo điện áp/dòng điện hợp lệ.
- [ ] Lệnh reboot yêu cầu bắt buộc token xác thực và được ghi nhận vào audit log.
