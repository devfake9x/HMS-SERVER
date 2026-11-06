---
phase: 1
title: "Server: mở lệnh đọc và chốt an toàn"
status: completed
priority: P1
dependencies: []
---

# Phase 1: Server — open the reads, protect the device lane

## Overview

Mở 15 lệnh đọc trong catalog đang ghim. Thêm các chốt để lệnh đọc không làm hại lệnh relay hay làm rò dữ liệu:
- giới hạn số lệnh đọc đang chờ trên mỗi thiết bị;
- TTL ngắn cho mọi lệnh;
- không phát realtime cho kết quả lệnh đọc;
- kiểm lại quyền khi đọc reply;
- endpoint `allowed-commands` rẻ và đúng scope.

## Requirements

### Tập lệnh mở
- `PinnedCommandCatalog.EnabledActions` thêm đúng 15 lệnh trong `plan.md`.
- Sửa comment của tập này cho đúng thực tế: spec đóng 9 lệnh `SLICE_PASS`, ngoài ra mở `system.ping`, `system.get_operations` và 15 lệnh đọc này đi trước campaign. Lý do: chúng chỉ đọc, đã có trong manifest đang ghim, và phase 3 kiểm reply thật của từng lệnh.
- Catalog JSON, `PinnedCatalogHash`, `FirmwarePermissions` và `HighRisk` **không đổi**.

### Giới hạn lệnh đọc đang chờ trên mỗi thiết bị [RT F2]
- **Lý do:** thiết bị có một hàng đợi lệnh 16 slot và một rate bucket dùng chung cho mọi client (spec §2/§3); khi đầy thì bỏ lệnh không trả lời. Server hiện không giới hạn gì (`InMemoryPendingDeviceCommandRegistry`).
- **Chốt:**
  - trước khi ký và gửi một lệnh class R, nếu thiết bị đã có ≥ `Mqtt:MaxPendingReadsPerDevice` (mặc định 4) lệnh đọc đang chờ, trả `ContractUnavailable` với mã `DEVICE_READ_BUSY`;
  - lệnh bị từ chối không được ký, không được publish, và được ghi log có cấu trúc;
  - lệnh class B (relay) không bị giới hạn này.
- `MqttLinkOptions.Validate`: giá trị phải trong 1..16.

### TTL ngắn cho mọi lệnh [RT F10]
- Đổi `SendDeviceCommandHandler.PhysicalCommandTtlMs` thành `MaxCommandTtlMs` (10000), áp cho **mọi** class, không chỉ class B.
- Lý do: lệnh đọc đang sống 120 s trên thiết bị trong khi server bỏ sau 30 s. Lệnh bị bỏ dở vẫn chiếm slot, và broker có thể dội lại (spec R10 `clean_session=false`).
- Catalog vẫn giữ TTL đúng theo spec (`CatalogTests` khoá); chỉ handler kẹp lại.
- Ràng buộc `ResponseTimeoutSeconds*1000 > MaxCommandTtlMs` giữ nguyên.

### Không phát `command_result` realtime cho lệnh đọc [RT F11]
- **Lý do:** mỗi lệnh đọc đẩy một event tới mọi người xem. Backlog SignalR 64 event mỗi kết nối, xả 100 ms/event; khi đầy, event `outputs` bị rớt và subscription bị thu hồi.
- **Đổi:**
  - `DeviceCommandResponseCorrelator` (reply và sweep) không publish `command_result` khi plan là class R. Người gửi vẫn có kết quả qua `GET .../commands/{id}` (thẻ và modal đều poll).
  - Trước khi đổi, grep mọi consumer của `command_result` và ghi lại. Hiện frontend không đọc tên event này; `DeviceDetailView.onEvent` chỉ coi mọi event là heartbeat.

### `GET .../commands/{correlationId}` kiểm lại quyền [RT F13]
- Ngoài kiểm "chính người gửi" và scope, kiểm thêm quyền của action (`CommandPermissionResolver.Resolve` + chính sách quyền thiết bị) tại lúc đọc. Mất quyền thì trả 404 như không tồn tại.
- Audit thêm index trên `Status` (migration `AddCommandAuditStatusIndex`) cho bước dọn lúc khởi động, vì mỗi lệnh đọc tạo một dòng audit. Retention: ngoài plan.

### `GET /api/v1/devices/{deviceId}/allowed-commands` [RT F12]
- **Trả về** `{ "actions": [...] }`: các action đang mở (`available`) có quyền tĩnh (`RequiredPermission` khác null) mà người gọi được gửi trên thiết bị này. Thứ tự sắp xếp ổn định; `no-store`.
- **Một lần mỗi request:** dựng actor và grant/membership một lần (`AccessActorFactory` + resolver), rồi đánh giá `DeviceAccessPolicy` cho từng quyền trong bộ nhớ. Không gọi `Allows` theo vòng lặp, vì mỗi lần gọi nạp lại tổ chức, grant và membership.
- **Scope:** dùng đúng quy tắc mà `SendDeviceCommandHandler` dùng khi nhận `POST .../commands`. Đọc code để xác định, rồi ghi vào báo cáo phase: người chỉ có membership trên thiết bị có được gửi không. Không tự đặt quy tắc riêng.
- **Thiết bị không thấy được** (không tồn tại, revoke, ngoài scope theo quy tắc trên) → 404 `DEVICE_NOT_FOUND`.
- Tách phần dựng actor và đánh giá của `DeviceLinkController.Allows` thành helper dùng chung (`CanControlOutputs`/`CanViewOutputs` cũng đi qua nó); không chép logic.
- **Không phải ranh giới bảo mật.** Server vẫn kiểm khi nhận lệnh; endpoint chỉ để UI ẩn nút.

## Architecture

```
POST /commands (class R)
  → quyền + scope (như cũ)
  → pending.CountReads(device) ≥ N ? ContractUnavailable/DEVICE_READ_BUSY : tiếp
  → ttl = min(row TTL, 10 s) → ký → publish
reply / sweep
  → audit, response store
  → command_result realtime chỉ khi class ≠ R
GET /commands/{id}  → người gửi + scope + quyền action hiện tại
GET /allowed-commands → actor một lần → policy cho ~30 quyền trong bộ nhớ
```

## Related Code Files

- **Modify:**
  - `src/Hms.Server.Infrastructure/Commands/PinnedCommandCatalog.cs` (`EnabledActions` và comment)
  - `src/Hms.Server.Application/Commands/SendDeviceCommandHandler.cs` (TTL cho mọi lệnh; giới hạn lệnh đọc đang chờ)
  - `src/Hms.Server.Application/DeviceLink/DeviceLinkContracts.cs` (registry: đếm lệnh đọc đang chờ theo thiết bị)
  - `src/Hms.Server.Application/DeviceLink/DeviceLinkHandlers.cs` (không publish `command_result` cho class R)
  - `src/Hms.Server.Infrastructure/Mqtt/MqttLinkOptions.cs` (`MaxPendingReadsPerDevice` + validate)
  - `src/Hms.Server.Api/Controllers/DeviceCommandsController.cs` (`allowed-commands`; kiểm lại quyền ở `GET {id}`)
  - `src/Hms.Server.Api/Controllers/DeviceLinkController.cs` (dùng helper chung)
  - `src/Hms.Server.Infrastructure/Persistence/ApplicationDbContext.cs` + migration `AddCommandAuditStatusIndex`
  - `frontend/src/core/api/adminApi.ts` (`allowedCommands(deviceId)`)
- **Create:** helper dựng actor và đánh giá quyền thiết bị dùng chung cho các controller.
- **Test có assertion đổi có chủ đích:**
  - `CatalogTests` (tập lệnh mở mong đợi);
  - `PhysicalOutputSafetyTests` (tên hằng TTL);
  - test nào khẳng định `ttl_ms` 120000 trong envelope lệnh đọc (grep `ttl_ms` trong tests);
  - test nào khẳng định có event `command_result` cho lệnh đọc.

## Tests first (viết trước, phải đỏ)

1. **`CatalogTests`:**
   - tập mở = 11 đọc cũ + 15 + 2 DO;
   - mọi lệnh trong `EnabledActions` là class R;
   - `sms.list`, `config.get`, `ota.remote_config_get`, `broker.list_pipelines`, `network.wifi_ap_clients`, mọi `modbus.*` đều `available=false`.
2. **Giới hạn lệnh đọc:**
   - đủ 4 lệnh đọc đang chờ trên thiết bị A thì lệnh đọc thứ 5 trả `DEVICE_READ_BUSY`, không ký, không publish;
   - thiết bị B vẫn gửi được;
   - `control.set_output` trên A vẫn gửi được;
   - khi một lệnh đọc xong (reply hoặc timeout) thì gửi tiếp được.
3. **TTL:** envelope của lệnh đọc (`system.get_info`) có `ttl_ms ≤ 10000`; `ExpiresAt` của plan cũng vậy.
4. **Realtime:** reply của lệnh đọc không sinh event `command_result`; reply của `set_output` vẫn sinh. Timeout của lệnh đọc cũng không sinh event.
5. **`GET .../commands/{id}`:** người gửi mất quyền action sau khi gửi thì nhận 404.
6. **`allowed-commands`:**
   - master-admin thấy `adc.get_values`, `network.get_modem`, `control.set_output`, không thấy `sms.list`;
   - người trong scope không có device role không thấy lệnh cần quyền thiết bị;
   - thiết bị ngoài scope → 404;
   - `no-store`;
   - người chỉ có membership: hành vi đúng với quy tắc của `POST .../commands`.
7. **Không hồi quy:** `CanControlOutputs`/`CanViewOutputs` vẫn đúng sau khi tách helper. Các test DO (`PhysicalOutputSafetyTests`, `DeviceOutputStateTests`) vẫn xanh.
8. **`MqttLinkOptions`:** `MaxPendingReadsPerDevice` 0 hoặc 17 → `MQTT_READ_LIMIT_INVALID`.

## Implementation Steps

1. Viết test 1–8, chạy để xác nhận đỏ.
2. Thêm 15 lệnh vào `EnabledActions`, sửa comment.
3. TTL cho mọi lệnh (đổi tên hằng, cập nhật nơi dùng: `MqttLinkOptions`, test).
4. Đếm lệnh đọc đang chờ trong registry; giới hạn trong handler, kèm option và validate.
5. Bỏ `command_result` realtime cho class R (sau khi grep consumer).
6. Helper quyền dùng chung; `allowed-commands`; kiểm lại quyền ở `GET {id}`.
7. Index audit + migration; `adminApi.allowedCommands`.
8. `dotnet build` và `dotnet test` toàn bộ.

## Success Criteria

- [x] Đúng 15 lệnh mới mở; catalog và hash không đổi; lệnh nhạy cảm và Modbus vẫn đóng.
- [x] Lệnh đọc thứ N+1 trên một thiết bị bị từ chối trước khi gửi; relay không bị ảnh hưởng.
- [x] Mọi envelope có `ttl_ms ≤ 10000`.
- [x] Lệnh đọc không phát `command_result`.
- [x] `allowed-commands` đúng vai trò, đúng scope, dựng actor một lần.
- [x] Toàn bộ test xanh.

## Risk Assessment

- **Giới hạn 4 lệnh đọc dùng chung cho mọi người xem:** nhiều người cùng bấm thì có người nhận `DEVICE_READ_BUSY`. Đó là chủ ý (bảo vệ lane relay); thẻ thử lại sau backoff (phase 2).
- **Đổi TTL của lệnh đọc đã mở:** thiết bị chưa đồng bộ giờ thì bỏ qua kiểm tuổi (spec §3), nên không có tác dụng phụ. Thiết bị đã đồng bộ thì lệnh trễ hơn 10 s bị loại, đúng ý.
- **Bỏ event `command_result` cho lệnh đọc:** nếu có màn hình nào dựa vào nó thì bước grep sẽ phát hiện. Kết quả vẫn lấy được qua API.
- **Rollback:** bỏ các dòng `EnabledActions`. Option giới hạn và TTL độc lập; migration index gỡ được.

## As built (2026-11-06)

Where the code differs from the text above (code review fixes, decided under the MQTT self-approval):

- **TTL:** `MaxCommandTtlMs` is **20 s** for reads and every other class; relay writes keep `MaxPhysicalCommandTtlMs` 10 s. 10 s for all reads would make a device whose clock runs ~9 s ahead reject every read, including the ones used to diagnose it. Startup still requires `ResponseTimeoutSeconds*1000 > 20000`.
- **Read cap** (`TryRegisterRead(entry, perDevice, perUser)`):
  - each sender holds at most half the slots (2 of 4);
  - `control.get_status` (relay read-back) gets one slot beyond the cap, so a relay write can always be verified;
  - a read stops counting once past its `ExpiresAt` + 2 s, because the device has dropped it by then.
- **Signing:** a refused read is signed and then discarded, not "not signed": the cap sits in the transport, after the adapter. Nothing is published, so there is no security impact.
- **Reply order:** the correlator saves the reply data before writing the audit row, so a poll that sees `Executed` also finds the data.
- **Scope:** `GET .../commands/{id}` now uses the send rule (`DeviceAccessChecks.CanSendCommands`), so a member who can send can also follow the result. `allowed-commands` resolves permissions with `CommandPermissionResolver`, like `POST`.
- **Tests:** `DeviceLaneSafetyTests` (cap per device and per user, reserved read-back slot, expired reads, busy refusal in the audit), plus integration tests for a device-role member, a membership-only member and a revoked device.
