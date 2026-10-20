---
phase: 6
title: "Kiểm Thử Tích Hợp Phần Cứng Thực & HIL End-to-End (Hardware-in-the-Loop Verification)"
status: pending
priority: P1
dependencies: [5]
---

# Phase 06: Kiểm Thử Tích Hợp Phần Cứng Thực & HIL End-to-End

## Overview
Thực hiện chu trình kiểm định khép kín (End-to-End Hardware-in-the-Loop) trên thiết bị phần cứng thật ESP32-S3 RTU (`gateway_870c`) đang kết nối trực tiếp vào broker EMQX qua topic `hms_toan_lab`. Kiểm chứng từ giao diện Web UI -> Backend API C# -> MQTT Broker -> Phần cứng ESP-IDF -> Phản hồi Telemetry & SignalR -> Hiển thị trên UI. Tuyệt đối duy trì tính chất READ-ONLY trên firmware repository `D:\WORKSPACE\PROJECT\HMS-IDF`.

## Requirements
- **Functional**:
  - Phát lệnh `control.set_output` (Channel 1 ON) từ Web UI: Tiếp điểm relay trên bo mạch thật đóng lại, LED chỉ thị sáng; gói tin ACK trả về `status: "ok"`, switch trên Web UI chuyển sang màu cam active.
  - Phát lệnh `control.set_output` (Channel 1 OFF): Tiếp điểm relay ngắt, switch chuyển sang màu xám.
  - Phát lệnh `system.ping`: Nhận kết quả RTT và timestamp đồng bộ từ phần cứng.
  - Phát lệnh `system.get_info`: Hiển thị chính xác phiên bản IDF, ESP32-S3 rev, và build timestamp.
  - Kiểm tra an toàn: Lệnh `system.reboot` với token hợp lệ làm thiết bị reboot và tự động reconnect broker trong 15s.
- **Non-functional**:
  - Giữ nguyên tắc bất biến: **0 file sửa đổi** trong `D:\WORKSPACE\PROJECT\HMS-IDF`.
  - Giữ nguyên màu cam thương hiệu trên các nút thao tác.
  - Tỷ lệ kiểm thử tự động HIL đạt 100% tỷ lệ thành công.

## Architecture
- Hardware Gateway: ESP32-S3 RTU (`gateway_870c`) kết nối `broker.emqx.io:1883`.
- Backend C2: `Hms.Server.Api` (port 5099) kết nối MQTT qua `MqttDeviceCommandTransport`.
- Frontend SPA: Vue 3 (port 5173).

## Related Code Files
- Create: `tests/Hms.Server.IntegrationTests/HilHardwareE2ETests.cs`
- Run: `tools/mqtt-broker/hms-telemetry-bridge.js`
- Create: `plans/reports/hil-hardware-e2e-report.md`

## Implementation Steps (TDD First)
1. **Viết Test Đỏ (Red Tests)**:
   - `HilHardwareE2ETests.cs`:
     - Test kết nối broker và gửi lệnh ping tới `hms_toan_lab/gateway_870c/cmd`.
     - Test chờ phản hồi trên `hms_toan_lab/gateway_870c/cmd/res` trong vòng 5 giây.
2. **Thực Thi Kiểm Chứng Thực Tế (Green)**:
   - Chạy test suite tương tác với thiết bị thật đang online.
   - Ghi nhận logs, độ trễ và payload phản hồi.
3. **Chụp Ảnh Màn Hình UI & Xuất Báo Cáo**:
   - Kiểm tra visual parity và chụp ảnh UI xác nhận nút cam hoạt động tốt.

## Success Criteria
- [ ] Thiết bị phần cứng thật phản hồi 100% các lệnh điều khiển DO và System.
- [ ] Không có bất kỳ dòng code nào bị can thiệp vào `HMS-IDF`.
- [ ] Báo cáo HIL được phê duyệt và lưu trữ đầy đủ.
