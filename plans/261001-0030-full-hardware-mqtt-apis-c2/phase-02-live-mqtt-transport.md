---
phase: 2
title: "Tầng Vận Chuyển Lệnh MQTT & Lắng Nghe Phản Hồi Hai Chiều (Live MQTT Transport & Ingestion Loop)"
status: pending
priority: P1
dependencies: [1]
---

# Phase 02: Tầng Vận Chuyển Lệnh MQTT & Lắng Nghe Phản Hồi Hai Chiều

## Overview
Xây dựng triển khai `IDeviceCommandTransport` thực thụ kết nối tới MQTT Broker (`mqtt://broker.emqx.io:1883`) để publish gói tin lệnh chuẩn hoá xuống topic `hms_toan_lab/{deviceId}/cmd`, đồng thời xây dựng bộ lắng nghe phản hồi (Response Consumer) trên topic `hms_toan_lab/{deviceId}/cmd/res` để cập nhật trạng thái thực thi lệnh trong `ICommandAuditStore` và thông báo tới Web UI qua SignalR.

## Requirements
- **Functional**:
  - `MqttDeviceCommandTransport`: Triển khai `IDeviceCommandTransport` sử dụng MQTT client bất đồng bộ.
  - Publish lệnh với QoS 1 tới `hms_toan_lab/{deviceId}/cmd`.
  - Đóng gói chuẩn theo envelope đã được kiểm định:
    ```json
    {
      "req_id": "c2-870c-xxxxxx",
      "client_id": "hms_server_c2",
      "ts": 1727715600,
      "data": {
        "action": "control.set_output",
        "params": { "channel": 1, "value": 1 }
      }
    }
    ```
  - `DeviceCommandResponseConsumer`: Lắng nghe trên `hms_toan_lab/+/cmd/res`, đọc `id` (khớp với correlation `req_id`), `success` (boolean), `data`, `error`, cập nhật trạng thái lệnh thành `Executed` hoặc `Failed`.
  - Phát sự kiện SignalR `DeviceCommandExecuted(deviceId, correlationId, result)` tới các client đang theo dõi thiết bị.
- **Non-functional**:
  - Chống block I/O: Kết nối MQTT duy trì connection pool hoặc singleton client kết nối nền.
  - Xử lý timeout: Nếu sau 10s thiết bị không phản hồi, server cập nhật trạng thái `CommandExecutionStatus.Failed` với mã `DEVICE_TIMEOUT`.

## Architecture
- `MqttDeviceCommandTransport`: Thực thi `IDeviceCommandTransport`.
- `DeviceCommandResponseConsumer`: Background Hosted Service (`IHostedService`).
- `IDeviceOperationsHub`: Gửi sự kiện realtime về Vue frontend qua WebSocket.

## Related Code Files
- Create: `src/Hms.Server.Infrastructure/Commands/MqttDeviceCommandTransport.cs`
- Create: `src/Hms.Server.Infrastructure/Commands/DeviceCommandResponseConsumer.cs`
- Modify: `src/Hms.Server.Infrastructure/DependencyInjection.cs`
- Modify: `src/Hms.Server.Api/Program.cs`
- Create: `tests/Hms.Server.UnitTests/MqttCommandTransportTests.cs`

## Implementation Steps (TDD First)
1. **Viết Test Đỏ (Red Tests)**:
   - `MqttCommandTransportTests.cs`:
     - Test đóng gói wire payload và gửi qua mock MQTT client.
     - Test xử lý phản hồi từ thiết bị (Status `Executed` khi payload có `success: true`).
     - Test xử lý lỗi từ thiết bị khi `success: false` kèm `error`.
     - Test fallback `MqttDeferred` khi broker ngắt kết nối.
2. **Hiện Thực Mã Nguồn (Green)**:
   - Hiện thực `MqttDeviceCommandTransport` và `DeviceCommandResponseConsumer`.
   - Đăng ký hosted service trong DI.
3. **Refactor & Verify**:
   - Chạy test kiểm thử tích hợp mô phỏng round-trip dispatch và ACK.

## Success Criteria
- [ ] Gói tin lệnh được publish chính xác xuống topic MQTT của thiết bị.
- [ ] Phản hồi từ thiết bị được tiêu thụ và cập nhật vào cơ sở dữ liệu/audit store.
- [ ] Sự kiện SignalR được bắn về frontend đúng theo `correlationId`.
