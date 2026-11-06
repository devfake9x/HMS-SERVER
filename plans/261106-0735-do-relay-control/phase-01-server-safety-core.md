---
phase: 1
title: "Server: lõi an toàn cho lệnh vật lý"
status: completed
priority: P1
dependencies: []
---

# Phase 1: Server — physical command safety core

## Overview

Mở `control.set_output` và `control.pulse`. Lệnh chỉ được ký khi qua đủ các lớp: kiểm tra input, danh sách kênh cho phép (mặc định **đóng**), TTL ngắn hơn timeout. Ngoài ra sửa các lỗ hổng vòng đời lệnh làm một hành động vật lý có thể xảy ra 2 lần hoặc bị treo.

## Context

- Brainstorm: `plans/reports/261106-0730-brainstorm-do-relay-control.md`.
- Red-team: `reports/from-code-reviewer-to-planner-red-team-*-plan-review-report.md`.
- Spec:
  - §6.4: `set_output {channel:int>=1, value:bool}`; `pulse {channel, duration_ms}`, kênh phải ở pulse mode.
  - §11: `gpio.do_write`, class `B`, TTL 120000, không confirm.
  - §3 L478: TTL được kiểm khi lệnh rời hàng đợi; đồng hồ chưa hợp lệ thì bỏ qua kiểm tuổi.
  - R4: timeout không chứng minh lệnh chưa chạy.
- `set_output` được mở **đi trước campaign firmware** (user quyết 2026-11-06). Spec §6.4 đã có contract input/output; trạng thái đóng chỉ có cho `pulse` (L882). Kết quả luôn được đọc lại để xác nhận.

## Requirements

### Catalog
- `control.set_output` và `control.pulse` là `Enabled` qua tập riêng `PhysicalOutputActions`.
- `control.set_all` và mọi action ghi khác vẫn `DeniedUntilActionContract`.
- `EnabledActions` vẫn chỉ chứa action class `R`.
- [RT#3] Hai action vật lý có TTL hiệu lực `min(row TTL, 10000)` ms. Envelope gửi `ttl_ms ≤ 10000`, nhỏ hơn server timeout 30 s.

### Kiểm tra input (`OutputCommandPolicy`)
- `channel`: số nguyên ≥ 1. `value`: bool thật (không nhận `1`/`"on"`). `duration_ms`: số nguyên từ 1 tới `MaxPulseMs`. Không có field lạ.
- [RT#11] `MaxPulseMs` mặc định 60000, cấu hình qua `Mqtt:MaxPulseMs`. Đây là chính sách an toàn của server; spec để giới hạn thật cho HTTP.
- Sai thì trả `ContractUnavailable/COMMAND_PAYLOAD_INVALID`, không publish.

### Danh sách kênh cho phép [RT#1]
- Lưu trên binding: `AllowedOutputChannels`, các số nguyên ≥ 1, không trùng, tối đa 32 phần tử.
- **Mặc định đóng:** null hoặc rỗng nghĩa là không kênh nào được điều khiển → `DO_CHANNEL_NOT_ALLOWED`. Kênh ngoài danh sách cũng trả `DO_CHANNEL_NOT_ALLOWED`. Cả hai trường hợp đều không publish.
- Chỉ đổi qua `PUT /api/v1/devices/{id}/link/output-channels` `{channels:[..]}`: master-admin, có CSRF, ghi log có cấu trúc (ai, trước, sau).
- `PUT /link` không đụng tới danh sách. Upsert binding giữ nguyên giá trị đã lưu.
- EF: cột `allowed_output_channels` kiểu `text` (ví dụ `"1,3"`) qua value converter, để in-memory và Npgsql chạy giống nhau. Migration `AddAllowedOutputChannels`.
- Seed dev: `MqttDeviceSeedOptions.AllowedOutputChannels` (`Mqtt:Devices:0:AllowedOutputChannels:0 = 1`) đi qua cùng handler. Thiếu seed nghĩa là đóng, vẫn an toàn.

### Vòng đời lệnh
- [RT#2] `MqttDeviceCommandTransport` chỉ bỏ pending với các mã "chắc chắn chưa gửi": `MQTT_NOT_CONNECTED`, `MQTT_RESPONSE_SUBSCRIPTION_MISSING`, `MQTT_COMMAND_TOPIC_INVALID`. Publish lỗi hoặc exception sau khi đã gửi thì vẫn ghi `Failed/MQTT_PUBLISH_UNCONFIRMED` nhưng **giữ pending** cho reply hoặc sweep.
- [RT#7] `MqttDeviceLinkService`: sweep timeout chạy trong `try` riêng, nên vẫn chạy khi broker mất kết nối.
- [RT#7] Lúc khởi động, các audit row ở `Published` chuyển sang `Failed/OUTCOME_UNKNOWN_AFTER_RESTART`. Thêm `ICommandAuditStore.FailUnfinishedPublished(code)`.

### Quyền
- [RT#13] Thêm `gpio.do_write` vào `DevicePermissionCodes.HighRisk`: chỉ master-admin được đưa quyền này vào role.
- [RT#14] `POST /devices/{id}/telemetry`: chỉ master-admin và có `[ValidateAntiForgeryToken]`. MQTT ingest đã thay bridge Node.

### Chung
- Logic theo action nằm ở Application/adapter, không nằm trong service MQTT (ADR `docs/decisions/260927-be-modular-monolith-mqtt-business-boundary.md`).
- Không đổi wire format đã kiểm chứng.

## Architecture

```
SendDeviceCommandHandler
  └─ adapter.PrepareAsync
       ├─ binding / capabilities (như cũ)
       ├─ OutputCommandPolicy.Check(type, action, payload, binding.AllowedOutputChannels, maxPulseMs)  [mới]
       └─ key → sign (ttl_ms đã ≤ 10000 cho lệnh vật lý)
transport.SendAsync ── giữ pending trừ mã "chưa gửi"
MqttDeviceLinkService loop: { connect/subscribe } try riêng; { sweep } try riêng
Startup: auditStore.FailUnfinishedPublished("OUTCOME_UNKNOWN_AFTER_RESTART")
```

## Related Code Files

- **Create:**
  - `src/Hms.Server.Application/DeviceLink/OutputCommandPolicy.cs`
  - `tests/Hms.Server.UnitTests/OutputCommandPolicyTests.cs`
  - migration `AddAllowedOutputChannels`
- **Modify:**
  - `src/Hms.Server.Infrastructure/Commands/PinnedCommandCatalog.cs`
  - `HmsIdfCommandProtocolAdapter.cs`
  - `src/Hms.Server.Application/DeviceLink/DeviceLinkContracts.cs` (record + Create + in-memory Upsert giữ danh sách)
  - `DeviceLinkHandlers.cs` (handler cho output-channels)
  - `EfDeviceMqttBindingStore.cs`, `PersistenceEntities.cs`, `ApplicationDbContext.cs`, `ApplicationDbContextModelSnapshot.cs`
  - `DeviceLinkController.cs` (endpoint mới; DTO thêm `allowedOutputChannels` ở **gốc** `DeviceLinkDto`)
  - `src/Hms.Server.Infrastructure/Mqtt/MqttLinkOptions.cs` (`MaxPulseMs`, `MqttDeviceSeedOptions.AllowedOutputChannels`)
  - `MqttDeviceCommandTransport.cs`, `MqttDeviceLinkService.cs`
  - `src/Hms.Server.Application/Commands/CommandContracts.cs` (`ICommandAuditStore`, in-memory store)
  - `src/Hms.Server.Infrastructure/Persistence/EfCommandAuditStore.cs`
  - `src/Hms.Server.Api/Program.cs` (seed, reconciliation lúc khởi động)
  - `src/Hms.Server.Domain/Authorization/DevicePermissionCodes.cs`
  - `src/Hms.Server.Api/Controllers/DevicesController.cs`
- **Test có assertion phải đổi theo hành vi mới** [RT#15]:
  - `tests/Hms.Server.UnitTests/CatalogTests.cs:120-126`
  - `tests/Hms.Server.IntegrationTests/ManagementApiTests.cs:195-199`
  - `tests/Hms.Server.IntegrationTests/MaitechRtuDeviceIntegrationTests.cs` (`:30` binding; POST telemetry giờ cần CSRF)
  - `tests/Hms.Server.UnitTests/DeviceLinkTests.cs` (`:295, :311, :314, :315` dựng binding/adapter/correlator)
  - `tests/Hms.Server.UnitTests/DevicePermissionCodesTests.cs` (danh sách HighRisk)

## Tests first (viết trước, phải đỏ)

1. **Catalog:**
   - `set_output` và `pulse` là `Enabled`, TTL ≤ 10000.
   - `set_all`, `system.reboot` là `Denied`.
   - `EnabledActions` chỉ chứa class `R`.
2. **Policy, bị từ chối:**
   - channel 0, -1, `"1"`, `1.5`, thiếu → `COMMAND_PAYLOAD_INVALID`.
   - value `1`, `"on"`, thiếu → `COMMAND_PAYLOAD_INVALID`.
   - duration 0, 60001 (với max 60000) → `COMMAND_PAYLOAD_INVALID`.
   - field lạ → `COMMAND_PAYLOAD_INVALID`.
   - allowlist null, `[]`, hoặc `[1]` mà channel 2 → `DO_CHANNEL_NOT_ALLOWED`.
3. **Policy, hợp lệ:** `{channel:1,value:true}` với allowlist `[1]`. Payload dạng `JsonElement` cũng hợp lệ.
4. **Adapter:**
   - Bị từ chối thì không ký, `CanPublish=false`.
   - Hợp lệ thì `ttl_ms ≤ 10000`, và `channel`, `value` mang đúng kiểu JSON.
5. **Binding:**
   - `PUT /link` không chứa field danh sách → danh sách đã lưu giữ nguyên.
   - `PUT /link/output-channels` bởi user không phải master-admin → 403. Thiếu CSRF → 400.
   - `[0]` → 400. `[1,3]` → `GET /link` trả `[1,3]`.
   - EF in-memory lưu và đọc lại được qua converter.
6. **Transport:**
   - `MQTT_NOT_CONNECTED` → pending bị xoá.
   - Publish ném exception sau khi đã gửi → pending còn, kết quả `MQTT_PUBLISH_UNCONFIRMED`.
7. **Sweep:** broker không kết nối thì pending quá hạn vẫn thành `DEVICE_RESPONSE_TIMEOUT` (test qua một sweep tách riêng có thể gọi trực tiếp).
8. **Restart:** audit có row `Published` → `FailUnfinishedPublished` đổi thành `Failed/OUTCOME_UNKNOWN_AFTER_RESTART`, cho cả in-memory và EF.
9. **Quyền:**
   - `IsHighRisk("gpio.do_write")` là true.
   - `POST /telemetry` bởi user không phải master-admin → 403. Thiếu CSRF → 400.

## Implementation Steps

1. Viết các test ở mục "Tests first", chạy để xác nhận đỏ.
2. Catalog với `PhysicalOutputActions` và TTL vật lý.
3. `OutputCommandPolicy` và gọi nó từ adapter.
4. Danh sách kênh: record, store, converter, migration, endpoint, seed.
5. Transport giữ pending; tách sweep; reconciliation lúc khởi động.
6. Thêm `gpio.do_write` vào HighRisk; khoá `POST /telemetry`.
7. Sửa các test cũ có assertion phải đổi (danh sách ở trên), rồi chạy toàn bộ test .NET.

## Success Criteria

- [x] Test mới chuyển đỏ sang xanh. Test cũ xanh, trừ những assertion đã liệt kê và được đổi có chủ đích.
- [x] `GET /me/command-catalog`: `available=true` cho đúng 11 action đọc cộng 2 action DO.
- [x] Không có kênh nào điều khiển được khi chưa đặt danh sách cho phép.

## Risk Assessment

- **Đồng hồ thiết bị chưa sync:** thiết bị bỏ qua kiểm tuổi lệnh, nên lệnh đã ký vẫn có thể chạy trễ hoặc bị replay sau reboot (dedup mất). Rủi ro còn lại: broker lab cho anonymous đọc và ghi topic. Ghi vào tài liệu; cách khắc phục thật là ACL/xác thực trên broker, nằm ngoài plan này.
- **Migration:** chỉ thêm cột nullable.
- **Rollback:** bỏ hai action khỏi `PhysicalOutputActions`.

## As built (2026-11-06)

Where the code differs from the text above:

- **Physical TTL clamp** lives in `SendDeviceCommandHandler.PhysicalCommandTtlMs` (10000), not in the catalog: `CatalogTests` keep the catalog's TTL equal to the spec row (120000). Envelope and plan expiry are both ≤ 10 s. `MqttLinkOptions.Validate` rejects `ResponseTimeoutSeconds*1000 <= 10000` (`MQTT_TIMING_INVALID`).
- **Allowlist storage:** column `AllowedOutputChannels` `varchar(200)` (EF default naming), not a `text` value converter. Channel numbers are capped at 255 so 32 entries always fit.
- **Re-bind (code review M6):** `IDeviceMqttBindingStore.Upsert` writes the allowlist only for a new row; changes go through `SetAllowedOutputChannels` (one-column update), so a re-bind cannot write back a list read before a master-admin changed it. Binding a **different topic base** resets the allowlist to closed and clears the cached DO state ("`PUT /link` không đụng tới danh sách" still holds for the same topic base).
- **Restart (review M7):** `ICommandAuditStore.FailUnfinished(code)` (renamed from `FailUnfinishedPublished`) also fails rows left `IN_FLIGHT` by a crash during publish.
- **Late transport result (review M1):** `CommandTransportOutcome.IsTransportOnly` (`Published` or `MQTT_PUBLISH_UNCONFIRMED`) never overwrites a device outcome already recorded.
- Policy tests live in `tests/Hms.Server.UnitTests/PhysicalOutputSafetyTests.cs` (no separate `OutputCommandPolicyTests.cs`); MQTT option validation tests added there too.
