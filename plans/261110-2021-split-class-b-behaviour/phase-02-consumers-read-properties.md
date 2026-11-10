---
phase: 2
title: "Nơi dùng đọc thuộc tính thay vì class; docs"
status: completed
priority: P1
dependencies: [1]
---

# Phase 2: Nơi dùng đọc thuộc tính

## Overview

Audit store, hai chỗ đánh stale DO và `CommandLanes` chuyển sang đọc thuộc tính của definition. Hành vi của 34 action đang mở không đổi; `sms.send` và các row B/D chưa mở có hành vi an toàn.

## Requirements

- Functional:
  - `SummarizePayload` trả `null` khi `!Definition.KeepsAuditPayload`. Class F vẫn bọc `{dry_run, payload}`.
  - `DeviceOutputState` và sweep hết hạn chỉ đánh stale khi `Definition.AffectsOutputs`.
  - `CommandLanes.IsWrite(CommandDefinition d)` = `d.Class != Read && !d.UsesOutputLane`. Xoá overload nhận `CommandClass`.
- Non-functional: không đổi TTL, policy, schema, API.

## Architecture

Luồng lane trong `MqttDeviceCommandTransport` (dòng 70-98) giữ nguyên ba nhánh: Read → lane đọc; `IsWrite` → lane ghi (`TryRegisterWrite`); còn lại → `Register` (relay). Sau thay đổi, nhánh cuối chỉ nhận definition có `UsesOutputLane`.

## Related Code Files

- Modify: `src/Hms.Server.Application/Commands/CommandLanes.cs`
- Modify: `src/Hms.Server.Application/Commands/SendDeviceCommandHandler.cs` (dòng 156, chỉ lời gọi `IsWrite`; dòng 143 TTL giữ nguyên)
- Modify: `src/Hms.Server.Application/DeviceLink/DeviceLinkContracts.cs` (269 doc, 335, 382)
- Modify: `src/Hms.Server.Application/DeviceLink/DeviceLinkHandlers.cs` (84, 139)
- Modify: `src/Hms.Server.Application/DeviceLink/DeviceOutputState.cs` (128)
- Modify: `src/Hms.Server.Infrastructure/Mqtt/MqttDeviceCommandTransport.cs` (81)
- Modify: `src/Hms.Server.Infrastructure/Persistence/EfCommandAuditStore.cs` (275-299)
- Modify tests (chỉ cách dựng definition, không đổi kỳ vọng):
  - Relay `SetOutput` dựng tay → thay bằng `new PinnedCommandCatalog().Find("control","set_output")!` (row catalog khớp bản dựng tay; cần đủ cả ba cờ, sửa một cờ là chưa đủ dù test xanh):
    - `tests/Hms.Server.UnitTests/DeviceOutputStateTests.cs:24-25`
    - `tests/Hms.Server.UnitTests/DeviceLaneSafetyTests.cs:24-25`
    - `tests/Hms.Server.UnitTests/DeviceRebootFlowTests.cs:31-32`
    - `tests/Hms.Server.UnitTests/PhysicalOutputSafetyTests.cs:23-24` (chỉ `SetOutput`; `Pulse`/`SetAll` ở 25-28 chỉ dùng cho policy, không đụng; `SetAll` không được có cờ relay)
  - Audit:
    - `tests/Hms.Server.IntegrationTests/PersistenceModelTests.cs:284-286` (`RebootDefinition`) → từ catalog `system.reboot`
    - `PersistenceModelTests.cs:253-254` và `tests/Hms.Server.UnitTests/DeviceLinkWiringTests.cs:19-20` → từ catalog (xanh sẵn nhưng payload lưu sẽ lặng lẽ thành null)
    - `PersistenceModelTests.cs:473` và test `:488-515` (dòng 502): cả hai definition lấy từ `Find(...)! with { Availability = Enabled }`, để `Assert.Null(other.PayloadSummary)` cho `network.wifi_sta_config_set` vẫn chứng minh luật production chứ không phải giá trị mặc định. Không thêm tham số bool vào helper `WriteAsync`.
  - Không cần đổi: `PersistenceModelTests.cs:230,397` (Read), `DeviceLinkTests.cs:122,251` (chỉ adapter)
  - mọi file khác đỏ sau khi đổi
- Modify docs: `docs/architecture-mqtt-device-link.md` (dòng 83, 162, 164, 201, 303, 337, 453, 476)

## Implementation Steps (tests first)

1. Viết test đỏ cho hành vi mới (definition lấy từ `new PinnedCommandCatalog().Find(...)`, đổi `Availability` bằng `with` khi cần):
   - Audit (integration, `PersistenceModelTests`): plan `sms.send` với payload có số điện thoại và nội dung → `PayloadSummary == null`. Thêm một row D (`sms.delete`) → `null`.
   - Stale sau reply (`DeviceOutputStateTests`): reply Executed cho `sms.send` → projection không stale. `control.set_all` → stale.
   - Stale khi hết hạn (test sweep hiện có trong `DeviceLinkTests` hoặc file gần nhất): `sms.send` hết hạn → không stale.
   - Lane (`DeviceLaneSafetyTests`): `CommandLanes.IsWrite` = true cho `sms.send`, `control.set_all`, `system.reboot`, `adc.set_channel`; false cho `control.set_output`, `control.pulse`, mọi Read. Transport: `sms.send` thứ hai khi cái đầu còn chờ → bị từ chối bằng mã write-busy.
2. Đổi `CommandLanes.IsWrite` sang nhận `CommandDefinition`, xoá overload cũ, sửa 5 nơi gọi cho biên dịch được. Sửa summary của `CommandLanes` và doc dòng 269.
3. Đổi `DeviceOutputState:128` và `DeviceLinkHandlers:139` sang `AffectsOutputs`.
4. Đổi `SummarizePayload`: `if (!plan.Definition.KeepsAuditPayload) return null;`. Bỏ tham chiếu `ConfigWriteActions` ở đây. Sửa comment phía trên: payload chỉ lưu cho action catalog đã khai báo; row khác cần redaction theo action trước khi bật.
5. Sửa các definition liệt kê ở "Related Code Files" (cả cái xanh lẫn đỏ), rồi chạy test. Với test đỏ khác:
   - action có trong catalog ghim → bắt buộc lấy definition từ catalog thật;
   - definition tổng hợp (action không có thật) → truyền cờ bằng named argument, đủ bộ cờ nhất quán.
   Không sửa assertion. Nếu một assertion phải đổi mới xanh → dừng, đó là thay đổi hành vi, báo lại.
6. Grep xác nhận: `CommandClass.Physical` không còn trong 4 file nơi dùng; còn lại chỉ ở `PinnedCommandCatalog`, `OutputCommandPolicy`, `ConfigCommandPolicy`, `SendDeviceCommandHandler` (TTL).
7. Docs `architecture-mqtt-device-link.md`: thay ghi chú "class B = relay" bằng bảng ba thuộc tính; ghi rõ row D và `sms.send` mặc định không lưu payload, plan mở chúng phải quyết định redaction, lane, TTL.
8. Chạy toàn bộ: `dotnet build Hms.Server.slnx`, `dotnet test Hms.Server.slnx`, `node --test scripts/`, `node scripts/generate-command-catalog.mjs --check`.

## Success Criteria

- [x] Test mới ở bước 1 xanh.
- [x] Toàn bộ test cũ xanh, không assertion nào bị sửa (diff test chỉ chạm dòng dựng definition + test mới).
- [x] Grep ở bước 6 đúng.
- [x] `git diff --stat`: không có `command-catalog.json`, không có migration.
- [x] Docs khớp mã.

## Risk Assessment

- **Definition dựng tay trong test im lặng đổi nghĩa**: ví dụ test lane relay dựng `control.set_output` class B không cờ → sau thay đổi đi lane ghi. Phần lớn sẽ đỏ; một số vẫn xanh (vd `DeviceOutputStateTests` publish thẳng, bỏ qua lane). Giảm: bước 5 sửa mọi definition đã liệt kê dù xanh hay đỏ, lấy từ catalog thật.
- **Registry pending (`DeviceLinkContracts` 335, 382)** đếm slot ghi theo `IsWrite`: relay vẫn `false` nên không đổi. Test lane hiện có phủ.
- **Rollback**: revert commit; không có dữ liệu hay schema cần hoàn tác.
