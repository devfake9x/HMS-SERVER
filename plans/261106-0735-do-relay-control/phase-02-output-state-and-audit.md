---
phase: 2
title: "Server: trạng thái DO đã xác minh, quyền xem, audit"
status: completed
priority: P1
dependencies: [1]
---

# Phase 2: Server — verified output state + audit

## Overview

Trạng thái DO đang có hai vấn đề: có thể bị làm giả, và thường bị ghi đè. Phase này đặt nó vào một store riêng, chỉ ghi được từ reply `control.get_status` đã xác minh chữ ký, có thời điểm đọc và trạng thái "cũ". UI đọc store này qua endpoint yêu cầu quyền `gpio.do_view`. Audit được bổ sung để một lệnh vật lý luôn truy được ai gửi và gửi gì.

## Requirements

### Store trạng thái DO [RT#4, RT#6]
- `IDeviceOutputStateStore` (in-memory, singleton) giữ `DeviceOutputState`:
  - `Channels[{channel,state,mode,pulseActive}]`
  - `ReadAt`
  - `IsStale`, `StaleReason`
- **Chỉ ghi qua `DeviceReplyProjection`**, khi reply `control.get_status` đã xác minh (`Executed`) và là của lệnh server đang chờ.
- Không bao giờ ghi từ heartbeat, `status/events` hay `POST /telemetry`. `IDeviceTelemetryStore.Record` **không** được dùng cho DO.
- **Đánh dấu stale** (giữ dữ liệu cũ, kèm lý do):
  - `B/status/connectivity` = `offline`;
  - heartbeat có `uptime_seconds` giảm so với lần trước (thiết bị đã reboot);
  - một lệnh vật lý kết thúc với kết quả không chắc chắn: `DEVICE_RESPONSE_TIMEOUT`, `MQTT_PUBLISH_UNCONFIRMED`, `OUTCOME_UNKNOWN_AFTER_RESTART`, hoặc `DEVICE_REJECTED` với lỗi `UNAUTHORIZED` (spec §2: dedup).
- **Projection** chạy **sau** khi reply đã lưu và event `command_result` đã gửi, bọc trong try/catch có log [RT#15]. Lỗi ở projection không làm mất reply.

### Realtime [RT#4, RT#5]
- Event mới `outputs`, payload chỉ gồm `{readAt, stale}` (không có kênh nào).
- Thêm `outputs` vào danh sách coalescing ở `RealtimeContracts.cs` và `SignalRConnectionBacklog.cs`. Type riêng nên không bị heartbeat `status` ghi đè.

### API [RT#5, RT#9]
- `GET /api/v1/devices/{id}/outputs` → `DeviceOutputState` hoặc 404 `OUTPUTS_NOT_READ`. Yêu cầu device permission `gpio.do_view`, kiểm bằng `DeviceAccessResolver` + `DeviceAccessPolicy.Evaluate`, giống cách handler lệnh kiểm.
- `GET /link` thêm `canControlOutputs` (`gpio.do_write`) và `canViewOutputs` (`gpio.do_view`), do server tính cho user hiện tại.

### Audit [RT#8]
- `CommandAuditRecord` thêm `IssuedBy` (Guid, nullable cho dữ liệu cũ), `Detail` (nullable), `PayloadSummary` (nullable). `PayloadSummary` lưu JSON gọn của payload cho action class `B`/`D`/`F`, tối đa 512 ký tự.
- Migration `AddCommandAuditIssuerAndDetail`.
- `EfCommandAuditStore` lưu và đọc `Detail`. Ở Postgres, lý do từ chối của thiết bị không còn bị mất.
- `SendDeviceCommandHandler`: lệnh bị từ chối trước bước reservation (quyền, action bị tắt, không thấy thiết bị) được ghi log có cấu trúc (user, device, action, code). Không tạo audit row.
- Acceptance được sửa thành: "mọi lệnh **đã qua bước reservation** đều có audit row kèm người gửi; lệnh bị từ chối sớm được ghi log".

## Architecture

```
B/cmd/res ─► correlator (verify, match pending) ─► audit + reply store + command_result
                                                └─► [try] DeviceReplyProjection
                                                        └─ control.get_status → IDeviceOutputStateStore.Set(...)
                                                                                + event "outputs" {readAt, stale}
B/status (uptime giảm) / connectivity offline / kết quả lệnh vật lý không chắc ─► IDeviceOutputStateStore.MarkStale(reason) + event "outputs"
UI ─ event "outputs" ─► GET /devices/{id}/outputs (gpio.do_view) ─► panel
```

## Related Code Files

- **Create:**
  - `src/Hms.Server.Application/DeviceLink/DeviceOutputState.cs` (store, projection)
  - `tests/Hms.Server.UnitTests/DeviceOutputStateTests.cs`
  - migration `AddCommandAuditIssuerAndDetail`
- **Modify:**
  - `DeviceLinkHandlers.cs` (correlator gọi projection; inbound handler đánh dấu stale)
  - `src/Hms.Server.Application/Realtime/RealtimeContracts.cs`, `src/Hms.Server.Api/Hubs/SignalRConnectionBacklog.cs` (`outputs` coalescing)
  - `DeviceLinkController.cs` (thêm cờ quyền)
  - controller mới hoặc `DevicesController.cs` cho `GET /outputs`
  - `src/Hms.Server.Infrastructure/DependencyInjection.cs`
  - `PersistenceEntities.cs`, `EfCommandAuditStore.cs`, `src/Hms.Server.Application/Commands/CommandContracts.cs` (in-memory audit lưu `IssuedBy`)
  - `SendDeviceCommandHandler.cs` (log từ chối sớm)
  - `ApplicationDbContextModelSnapshot.cs`
- **Test phải đổi:** `DeviceLinkTests.cs:315` (correlator nhận thêm phụ thuộc).

## Tests first (viết trước, phải đỏ)

1. **Ghi trạng thái:**
   - Reply `get_status` 6 kênh, đã xác minh → `GET /outputs` trả 6 kênh có `pulseActive` và `readAt`.
   - Event `outputs` không chứa dữ liệu kênh.
   - Telemetry (uptime, heap) **không đổi**.
2. **Không bao giờ ghi từ:** heartbeat có `digital_outputs`, `POST /telemetry` có `digital_outputs`, reply chữ ký sai, `get_status` của client khác.
3. **Stale:** sau connectivity `offline`, heartbeat có uptime giảm, hoặc lệnh vật lý `DEVICE_RESPONSE_TIMEOUT` → `IsStale=true` với đúng lý do. Một `get_status` mới thì xoá stale.
4. **Projection lỗi** (reply sai dạng) → reply vẫn được lưu và `command_result` vẫn được gửi.
5. **Coalescing:** một `outputs` rồi một `status` trong cùng nhịp → cả hai đều được giao.
6. **Quyền:**
   - `GET /outputs` với user có device scope nhưng thiếu `gpio.do_view` → 403. Có quyền → 200.
   - `GET /link` trả `canControlOutputs`/`canViewOutputs` đúng theo device role.
7. **Audit:**
   - EF lưu `IssuedBy`, `Detail` (lý do `DEVICE_REJECTED`) và `PayloadSummary` cho lệnh `set_output`.
   - Lệnh bị từ chối vì thiếu quyền tạo một dòng log có code.

## Implementation Steps

1. Viết các test ở mục "Tests first", chạy để xác nhận đỏ.
2. Store, projection, đánh dấu stale; nối vào correlator và inbound handler.
3. Event `outputs` và coalescing.
4. Endpoint `GET /outputs` và cờ quyền trong `/link`.
5. Cột audit, migration, store; log từ chối sớm.
6. Chạy toàn bộ test .NET.

## Success Criteria

- [x] Test mới chuyển đỏ sang xanh; test cũ vẫn xanh (trừ chỗ đã liệt kê).
- [x] Trạng thái DO chỉ đến từ reply đã xác minh, có `readAt`, có stale.
- [x] Data DO chỉ lộ cho người có `gpio.do_view`.

## Risk Assessment

- **Store in-memory mất khi restart:** sau restart, UI thấy `OUTPUTS_NOT_READ` và phải đọc lại. Đây là hành vi đúng: an toàn hơn hiển thị dữ liệu cũ.
- **Chỉ chiếu reply của chính server:** `get_status` do tool HIL gửi không làm mới UI. Chấp nhận được, vì UI tự đọc lại sau mỗi lệnh và có nút đọc.

## As built (2026-11-06)

- **Error replies (code review M3, spec R3):** a physical command that does not end `Executed` marks the state `output_command_outcome_unknown` unless the device refused it before the handler ran (`expired`, `future_timestamp`, `LANE_FULL`, `unknown_action`, `confirm_required`, `permission_unmapped`, `schema:*`). `UNAUTHORIZED` (dedup) still counts as unknown. Seen live: the gateway's `ESP_ERR_INVALID_ARG` pulse refusal marks the state stale until the read-back.
- **Ordering (review M2):** the reply projection runs before the audit write; the timeout sweep marks stale first and isolates each command in its own try/catch.
- **Telemetry:** `digital_outputs`/`digitalOutputs` are stripped from stored telemetry and from the realtime `status` event (`TelemetryRelayStates`), so relay state is only readable behind `gpio.do_view`. Contract change: `GET /devices/{id}/telemetry` no longer returns `digital_outputs`.
