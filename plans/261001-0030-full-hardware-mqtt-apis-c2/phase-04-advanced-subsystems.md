---
phase: 4
title: "Tích Hợp Các Phân Hệ Nâng Cao: Network, Modbus RS485, SMS & OTA Firmware"
status: pending
priority: P2
dependencies: [3]
---

# Phase 04: Tích Hợp Các Phân Hệ Nâng Cao: Network, Modbus RS485, SMS & OTA Firmware

## Overview
Triển khai tích hợp các subsystem kỹ thuật chuyên sâu: `network` (quản trị đa kênh Ethernet W5500, Wi-Fi Station & SoftAP, LTE 4G), `modbus` (cấu hình master RS485 RTU, mapping thanh ghi), `sms` (đọc tin nhắn phân trang chống tràn RAM), và `ota` (nâng cấp firmware từ xa có kiểm tra chữ ký số phần cứng).

## Requirements
- **Functional**:
  - `network`:
    - Quản trị Ethernet (`get_ethernet`, `configure_ethernet`).
    - Quản trị Wi-Fi Station (quét mạng, kết nối SSID/password, ngắt kết nối).
    - Quản trị SoftAP (bật/tắt AP cứu hộ, đổi mật khẩu).
    - Đặt độ ưu tiên giao diện mạng (`set_priority`: Ethernet > Wi-Fi > LTE 4G).
  - `modbus`:
    - `modbus.get_rtu_config` & `modbus.set_rtu_config`: Đọc/ghi thông số baudrate, parity, data bits, slave mapping.
    - `modbus.restart_rtu`: Khởi động lại bus RS485 có cơ chế cooldown 5000ms.
  - `sms`:
    - `sms.list`: Đọc tin nhắn modem LTE SIMCom/Quectel có phân trang (`page`, `limit <= 10`).
    - `sms.storage_stats`: Đọc dung lượng bộ nhớ SIM / NVRAM.
  - `ota`:
    - `ota.get_status`: Đọc phân vùng đang chạy (ota_0 / ota_1) và trạng thái rollback.
    - `ota.remote_start`: Chỉ định URL tải firmware binary (.bin).
    - `ota.apply`: Xác nhận nạp với token `OTA-APPLY-<DEVID>`.
- **Non-functional**:
  - Bảo vệ bộ nhớ: Chặn cứng truy vấn SMS quá 10 tin/lần để bảo toàn 78KB RAM ESP32.
  - An toàn mạng: Input sanitization cho SSID và password Wi-Fi, chặn chèn ký tự điều khiển.

## Architecture
- `DeviceNetworkService.cs`: Điều phối các lệnh cấu hình mạng.
- `DeviceModbusService.cs`: Điều phối cấu hình RS485 và enforcing cooldown timer.
- `DeviceOtaService.cs`: Quản lý quy trình 3 bước OTA an toàn.

## Related Code Files
- Create: `src/Hms.Server.Application/Devices/DeviceNetworkService.cs`
- Create: `src/Hms.Server.Application/Devices/DeviceModbusService.cs`
- Create: `src/Hms.Server.Application/Devices/DeviceOtaService.cs`
- Create: `tests/Hms.Server.UnitTests/AdvancedSubsystemsTests.cs`

## Implementation Steps (TDD First)
1. **Viết Test Đỏ (Red Tests)**:
   - `AdvancedSubsystemsTests.cs`:
     - Test lệnh `modbus.restart_rtu` gọi 2 lần trong vòng 2 giây bị chặn với lỗi `COOLDOWN_ACTIVE`.
     - Test lệnh `sms.list` với `limit = 50` tự động bị ép về `10` hoặc báo lỗi tham số.
     - Test lệnh `ota.apply` yêu cầu đúng định dạng token `OTA-APPLY-{deviceId}`.
2. **Hiện Thực Mã Nguồn (Green)**:
   - Cài đặt các service xử lý tham số và rate limiting cho các subsystem nâng cao.
3. **Refactor & Verify**:
   - Chạy test kiểm thử đạt 100% tỷ lệ vượt qua.

## Success Criteria
- [ ] Cooldown 5s cho Modbus RS485 hoạt động chính xác.
- [ ] Phân trang SMS giới hạn an toàn <= 10 tin.
- [ ] Lệnh nạp OTA được bảo vệ 3 tầng an ninh.
