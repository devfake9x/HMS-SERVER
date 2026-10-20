---
phase: 1
title: "Kích Hoạt Danh Mục Lệnh & Phân Quyền Đa Phân Hệ (Catalog Activation & Subsystem RBAC)"
status: pending
priority: P1
dependencies: []
---

# Phase 01: Kích Hoạt Danh Mục Lệnh & Phân Quyền Đa Phân Hệ

## Overview
Chuyển đổi trạng thái của 94 MQTT Actions từ chế độ tạm khóa (`DeniedUntilActionContract`) sang chế độ sẵn sàng thực thi (`CommandAvailability.Enabled`) có điều kiện, đồng thời thiết lập hệ thống phân quyền chi tiết (Fine-grained Subsystem RBAC) và cờ kiểm soát rủi ro cho từng nhóm lệnh.

## Requirements
- **Functional**:
  - Hỗ trợ đầy đủ 94 actions trên 12 namespaces: `system`, `control`, `config`, `adc`, `battery`, `broker`, `modbus`, `mqtt`, `network`, `ota`, `sms`, `status`.
  - Phân loại rõ ràng quyền hạn: `device.command.system`, `device.command.control`, `device.command.config`, `device.command.adc`, `device.command.battery`, `device.command.broker`, `device.command.modbus`, `device.command.mqtt`, `device.command.network`, `device.command.ota`, `device.command.sms`, `device.command.status`.
  - Đánh dấu thuộc tính `isDangerous: true` và `requiresConfirmation: true` cho các lệnh phá hủy (`system.reboot`, `system.factory_reset`, `ota.apply`, `modbus.replace_rtu_config`).
- **Non-functional**:
  - Tương thích ngược 100% với các bài kiểm thử kiểm định cũ (giữ nguyên cờ cấu hình mặc định an toàn khi chạy harness tests).
  - Tốc độ tra cứu danh mục lệnh: O(1) qua Hash Index.

## Architecture
- `PinnedCommandCatalog.cs`: Cập nhật logic ánh xạ từ `command-catalog.json` sang `CommandDefinition`.
- `CommandDefinition`: Mở rộng trường metadata lưu trữ schema tham số yêu cầu và phân lớp an toàn.
- `DeviceAuthorizationPolicy`: Đánh giá quyền truy cập của người dùng đối chiếu với `FeaturePermission` của từng subsystem.

## Related Code Files
- Modify: `src/Hms.Server.Infrastructure/Commands/PinnedCommandCatalog.cs`
- Modify: `src/Hms.Server.Application/Commands/CommandContracts.cs`
- Modify: `src/Hms.Server.Infrastructure/DependencyInjection.cs`
- Create: `tests/Hms.Server.UnitTests/SubsystemCatalogActivationTests.cs`

## Implementation Steps (TDD First)
1. **Viết Test Đỏ (Red Tests)**:
   - Tạo `SubsystemCatalogActivationTests.cs`:
     - Test tra cứu toàn bộ 94 actions đều tồn tại.
     - Test các lệnh `control.set_output`, `system.ping` có `Availability == Enabled` trong cấu hình kích hoạt.
     - Test các lệnh nguy hiểm (`reboot`, `factory_reset`, `ota.apply`) có cờ `Dangerous == true`.
     - Test người dùng chỉ có quyền `device.command.system` sẽ bị từ chối khi gọi `control.set_output` với lỗi `FEATURE_PERMISSION_REQUIRED`.
2. **Hiện Thực Mã Nguồn (Green)**:
   - Cập nhật constructor của `PinnedCommandCatalog` hỗ trợ kích hoạt danh mục lệnh theo cấu hình Dependency Injection.
   - Bổ sung bảng ánh xạ namespace permissions và danh sách lệnh dangerous.
3. **Refactor & Verify**:
   - Chạy `dotnet test` bảo đảm toàn bộ 137 test cũ và các test mới đều xanh 100%.

## Success Criteria
- [ ] 94 actions được nhận diện chính xác với đúng subsystem permissions.
- [ ] Các lệnh nguy hiểm được gắn nhãn `Dangerous` chính xác.
- [ ] Toàn bộ unit tests và integration tests liên quan đến phân quyền chạy thành công.
