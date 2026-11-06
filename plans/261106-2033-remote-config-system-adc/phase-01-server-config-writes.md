---
phase: 1
title: "Server: mở 5 lệnh ghi và khung an toàn"
status: completed
priority: P1
dependencies: []
---

# Phase 1: Server — mở 5 lệnh ghi và khung an toàn

## Overview

Phase này làm 5 việc:
1. Mở 5 lệnh W/C qua một danh sách riêng trong `PinnedCommandCatalog`.
2. Kiểm payload từng lệnh trước khi ký (`ConfigCommandPolicy`, cùng kiểu với `OutputCommandPolicy`).
3. Thêm lane ghi theo thiết bị cho mọi class trừ R và B: 1 lệnh đang chờ, 10 lệnh mỗi phút, và giữ lane khi job còn chạy.
4. Lệnh W/C luôn dùng giờ server.
5. Audit lưu payload của 5 lệnh mở.

Phần dùng lại được, có sẵn:
- reply job còn chạy đã thành `Acknowledged` + `operation_id=` (`DeviceLinkHandlers.ToResult`, :146-152);
- `command_result` phát cho các class khác R.

## Requirements

### Mở lệnh
- **`ConfigWriteActions`** trong `PinnedCommandCatalog`: `system.set_hostname`, `system.set_timezone`, `system.sync_ntp`, `adc.set_channel`, `adc.toggle_channel`.
- **`ToDefinition`:** class `Write` và `Control` mở qua `ConfigWriteActions.Contains(...)`; `Destructive` và `Flash` vẫn đóng.
- **Comment:** ghi quyết định owner ngày 2026-11-06, và lý do `set_ntp_servers`/`set_time` vẫn đóng (đồng hồ thiết bị; spec §2, §3).

### Kiểm payload (`src/Hms.Server.Application/DeviceLink/ConfigCommandPolicy.cs`)
- **API:** `string? Check(CommandDefinition definition, IReadOnlyDictionary<string, object?> payload)`.
  - Class R và B → null.
  - Class W/C/D/F: chỉ cho qua 5 lệnh trên; lệnh khác trả `CONFIG_ACTION_UNSUPPORTED`.
- **Khoá:** khoá bắt buộc phải có; ngoài khoá tuỳ chọn đã nêu thì không được có khoá nào khác. Không đổi kiểu: chuỗi số không phải số, số không phải bool.

| Lệnh | Luật |
|---|---|
| `system.set_hostname` | `{hostname}` string, 1..63 byte UTF-8, khớp `^[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?$` |
| `system.set_timezone` | `{timezone}` string, nằm trong `ConfigCommandPolicy.SupportedTimezones` (so ordinal) |
| `system.sync_ntp` | payload rỗng |
| `adc.set_channel` | Bắt buộc `ai` và `name`; tuỳ chọn `type` và `enabled`. `ai` là số nguyên 1..16. `name` là string 1..32 byte UTF-8, không chỉ toàn khoảng trắng, không có ký tự thuộc Unicode category Cc, Cf, Zl, Zp, Co, Cs (chặn U+202E, U+200B…). `type` là string 1..32 byte khớp `^[A-Za-z0-9_.\-]+$`. `enabled` là bool |
| `adc.toggle_channel` | `{ai, enabled}`; `ai` là số nguyên 1..16; `enabled` là bool |

- **`SupportedTimezones`:** danh sách POSIX TZ nhỏ, gồm `ICT-7` (giá trị gateway đang dùng) và `UTC0`. Thêm vùng khác khi có nhu cầu thật.
- **Helper chung:** `TryInteger`, `IsBoolean`, `HasExactly` đang `private` trong `OutputCommandPolicy`. Tách ra `internal static class CommandPayloadRules`, không đổi hành vi. Thêm vào đó `TryString` (chỉ `JsonElement` String hoặc `string`) và `HasKeys(required, optional)`.
- **Điểm gọi:** `HmsIdfCommandProtocolAdapter.PrepareAsync`, ngay sau `OutputCommandPolicy` (:59). Bị từ chối thì `Fail(plan, code)`: không đọc khoá, không ký, không publish.

### Giờ server cho lệnh ghi
- **Ở đâu:** `SendDeviceCommandHandler`, trước khi dựng `CommandExecutionPlan`.
- **Làm gì:** lệnh class khác R và B lấy `RequestedAt = DateTimeOffset.UtcNow`, bỏ qua giá trị client gửi. Hiện API nhận `RequestedAt` client tới now+60 s (`DeviceCommandsController.cs:41`, handler :131). Với giá trị đó, lệnh ghi có thể chạy trên thiết bị khoảng 80 s sau khi publish, tức sau khi slot đã nhả và UI đã đọc lại.

### Lane ghi (mọi class trừ R và B)
- **`IPendingDeviceCommandRegistry.TryRegisterWrite(PendingDeviceCommand pending, int maxPerMinute)`**, trả `null` khi đăng ký được, ngược lại trả mã lỗi. Đếm và chèn là một bước atomic, dùng chung lock với lane đọc.
  - **`DEVICE_WRITE_BUSY`:** thiết bị đang có lệnh ghi chờ, hoặc đang trong thời gian giữ lane cho job.
    - Lệnh ghi đang chờ là lệnh còn trong registry, hoặc chưa qua max(`Deadline`, `ExpiresAt` + 2 s).
    - Giữ lane cho job: thiết bị còn hạn `HoldWriteLane`.
  - **`DEVICE_WRITE_RATE_LIMITED`:** thiết bị đã có ≥ `maxPerMinute` lệnh ghi được đăng ký trong 60 s gần nhất (cửa sổ trượt, giữ trong registry).
- **Nhả slot:**
  - khi có reply (`Remove`);
  - khi transport thất bại trước khi gửi (`Remove` ở `MqttDeviceCommandTransport.cs:88`);
  - khi sweep (`TakeExpired`).
  - Một lệnh đã khỏi registry vẫn chặn lane tới max(`Deadline`, `ExpiresAt` + 2 s). Registry giữ mốc này theo thiết bị.
- **`HoldWriteLane(DeviceId, DateTimeOffset until)`:** `DeviceLinkHandlers` gọi khi reply của lệnh W/C/D/F là `Acknowledged` (job còn chạy), với `until` = lúc nhận + 60 s.
- **`MqttDeviceCommandTransport`:**
  - `WriteBusy = "DEVICE_WRITE_BUSY"`, `WriteRateLimited = "DEVICE_WRITE_RATE_LIMITED"`;
  - class W/C/D/F gọi `TryRegisterWrite(entry, 10)`; bị từ chối thì trả `ContractUnavailable` + mã + detail tiếng Việt;
  - class B giữ `Register`; class R giữ lane đọc;
  - đường ghi không bao giờ gọi `Register` trực tiếp, vì `Register` không lấy lock.
- **Hằng số:** `10/phút` và `60 s` là hằng số trong code, không thêm option (YAGNI).

### Audit
- **`EfCommandAuditStore.SummarizePayload`** (:218-223): ngoài B/D/F, lưu thêm payload của W/C khi `(type, action)` thuộc `PinnedCommandCatalog.ConfigWriteActions`. Payload 5 lệnh này không chứa secret.
- **Các lệnh W/C khác:** vẫn `null` cho tới khi có che theo từng action. Đây là điều kiện để mở Wi-Fi hay MQTT.

### Giữ nguyên
- **TTL:** trần 20 s cho lệnh không phải physical.
- **Reply:** `DeviceLinkHandlers.ToResult`.
- **Đọc reply:** `GET .../commands/{id}` kiểm lại quyền khi đọc (`MayReadReply`).
- **`allowed-commands`:** tự liệt kê lệnh mới cho người có quyền.
- **Realtime:** `command_result` vẫn phát cho class khác R. Event có `detail` (gồm `operation_id`, mã lỗi thiết bị) nhưng không có payload.
- **Từ chối quyền:** trả HTTP 200 với `Failed`/`DEVICE_PERMISSION_REQUIRED` (`SendDeviceCommandHandler.cs:103-107`), không đổi.

## Tests first (viết trước, phải đỏ trước khi sửa code)

1. **`CatalogTests`:**
   - **Assertion có chủ đích phải sửa:** test `Pinned_catalog_opens_only_the_closed_reads_and_the_two_relay_outputs` (`CatalogTests.cs:138-150`). Tập `expected` thêm `ConfigWriteActions`; thêm assertion class W/C cho từng phần tử; đổi tên test theo nội dung mới.
   - **Test mới:**
     - đúng 5 cặp mở; tổng số lệnh mở = 33;
     - `system.set_ntp_servers`, `system.set_time`, `system.reboot`, `system.factory_reset`, `config.set`, `network.set_priority`, `network.wifi_sta_config_set`, `mqtt.set_config`, `broker.create_signal`, `modbus.set_rtu_config`, `ota.remote_start` vẫn `DeniedUntilActionContract`;
     - mọi row class D/F đóng; hash không đổi.
2. **`ConfigCommandPolicyTests` (mới):**
   - **Hợp lệ → null.**
   - **Không hợp lệ → `COMMAND_PAYLOAD_INVALID`:**
     - thiếu khoá, thừa khoá, sai kiểu;
     - `ai` là 0, 17, hoặc kiểu string;
     - hostname có `_`, có `.`, dài 64 byte, hoặc bắt đầu bằng `-`;
     - TZ ngoài danh sách;
     - `name` rỗng, chỉ khoảng trắng, có `\n`, U+202E hoặc U+200B; `name` 33 byte;
     - `type` có khoảng trắng; `enabled` là `1`;
     - `sync_ntp` có payload.
   - **Class R/B → null.**
   - **`CONFIG_ACTION_UNSUPPORTED`:**
     - lệnh W/C ngoài danh sách: `system.set_ntp_servers`, `system.set_time`, `network.set_priority`;
     - lệnh D/F.
3. **Adapter (dùng `HmsIdfCommandProtocolAdapter` thật):**
   - không dùng `PublishableTestProtocolAdapter`, vì adapter đó bỏ qua policy;
   - payload sai → `CanPublish == false`, đúng mã, và `secrets.Find` không được gọi.
4. **`DeviceLaneSafetyTests`:**
   - **Bị chặn:**
     - lệnh ghi thứ hai trên cùng thiết bị → `DEVICE_WRITE_BUSY`; publisher không nhận message; audit ghi đúng mã;
     - lệnh đã `Remove` nhưng chưa qua `ExpiresAt` + 2 s → lệnh sau vẫn bị chặn;
     - `HoldWriteLane` còn hạn → bị chặn;
     - lệnh ghi thứ 11 trong 60 s → `DEVICE_WRITE_RATE_LIMITED`.
   - **Được qua:**
     - thiết bị khác;
     - sau `Remove` và đã qua `ExpiresAt` + 2 s;
     - sau `TakeExpired`;
     - sau khi publish thất bại với mã NeverSent;
     - `HoldWriteLane` đã hết hạn;
     - qua 60 s → giới hạn tần suất reset.
   - **Không ảnh hưởng chéo:** lệnh ghi không chặn lệnh đọc hay `control.set_output`, và ngược lại.
   - **Burst** 10 lệnh ghi song song → đúng 1 lệnh được publish.
5. **Giờ server:**
   - `set_hostname` với `RequestedAt` client = now+50 s → `ExpiresAt` tính theo giờ server;
   - lệnh đọc vẫn dùng `RequestedAt` client (hành vi cũ).
6. **`DeviceLinkTests`:**
   - **Đỏ trước:** reply `Acknowledged` cho `adc.set_channel` → lane bị giữ (`TryRegisterWrite` ngay sau đó → `DEVICE_WRITE_BUSY`).
   - **Không cần đỏ trước:** trường hợp `Acknowledged` + `operation_id` đã có test (:194-209), chỉ dùng làm hồi quy.
   - **Ghi chú:** fixture ở `DeviceLinkTests.cs:356` và `DeviceOutputStateTests.cs:272` gọi `Register` trực tiếp, đi vòng qua lane. Chấp nhận được vì đó là test của correlator, không phải của lane.
7. **Audit:** `set_hostname` lưu payload summary; `network.set_priority` (giả lập mở trong test) vẫn `null`.
8. **`ManagementApiTests` (integration, seed binding MQTT cho thiết bị để không bị `DEVICE_MQTT_BINDING_MISSING`):**
   - **`allowed-commands`:**
     - role có `sys.time_edit` → có `system.set_timezone`, `system.sync_ntp`; không có `system.set_time`, `system.set_ntp_servers`;
     - role chỉ có quyền xem → không có lệnh ghi nào.
   - **Lệnh đóng:** role có `sys.time_edit` gửi `system.set_ntp_servers` → HTTP 200, `ContractUnavailable`/`DENIED_UNTIL_ACTION_CONTRACT`.
   - **Thiếu quyền:** role chỉ có quyền xem gửi `set_hostname` → HTTP 200, `Failed`/`DEVICE_PERMISSION_REQUIRED`.
   - **Payload sai** qua HTTP → `COMMAND_PAYLOAD_INVALID`. Kiểm bằng mã lỗi, vì host test không publish.
   - **Thu hồi quyền:** reply của lệnh ghi bị ẩn sau khi role bị thu hồi.

## Implementation Steps

1. Viết test 1–8, chạy để thấy đỏ.
2. Tách `CommandPayloadRules` (refactor thuần); `PhysicalOutputSafetyTests` phải xanh.
3. `ConfigCommandPolicy` + gọi trong adapter.
4. `ConfigWriteActions` + `ToDefinition`.
5. Giờ server cho W/C trong handler.
6. `TryRegisterWrite`, `HoldWriteLane`, nhánh W/C/D/F trong transport; gọi `HoldWriteLane` trong `DeviceLinkHandlers`.
7. `SummarizePayload`.
8. Sửa assertion có chủ đích ở mục 1; chạy toàn bộ test.

## Related Code Files

- **Create:**
  - `src/Hms.Server.Application/DeviceLink/ConfigCommandPolicy.cs`
  - `src/Hms.Server.Application/DeviceLink/CommandPayloadRules.cs`
  - `tests/Hms.Server.UnitTests/ConfigCommandPolicyTests.cs`
- **Modify:**
  - `src/Hms.Server.Application/DeviceLink/OutputCommandPolicy.cs`
  - `src/Hms.Server.Infrastructure/Commands/PinnedCommandCatalog.cs`
  - `src/Hms.Server.Infrastructure/Commands/HmsIdfCommandProtocolAdapter.cs`
  - `src/Hms.Server.Application/Commands/SendDeviceCommandHandler.cs`
  - `src/Hms.Server.Application/DeviceLink/DeviceLinkContracts.cs` (interface + `InMemoryPendingDeviceCommandRegistry`, implementation duy nhất, không có test double)
  - `src/Hms.Server.Application/DeviceLink/DeviceLinkHandlers.cs`
  - `src/Hms.Server.Infrastructure/Mqtt/MqttDeviceCommandTransport.cs`
  - `src/Hms.Server.Infrastructure/Persistence/EfCommandAuditStore.cs`
  - test: `CatalogTests.cs`, `DeviceLaneSafetyTests.cs`, `DeviceLinkTests.cs`, `PhysicalOutputSafetyTests.cs`, `ManagementApiTests.cs`

## Regression Gate

- `dotnet build` không có warning mới.
- `dotnet test tests/Hms.Server.UnitTests` và `dotnet test tests/Hms.Server.IntegrationTests` xanh.
- `dotnet ef migrations has-pending-model-changes` báo không có thay đổi.

## Success Criteria

- [x] 5 lệnh mở, tổng 33; danh sách lệnh vẫn đóng có test, kể cả qua role có quyền tương ứng.
- [x] Mọi payload sai bị chặn trước khi ký; lệnh không có luật kiểm riêng bị `CONFIG_ACTION_UNSUPPORTED`.
- [x] Lane ghi đúng: 1 lệnh, 10 lệnh/phút, giữ tới max(`Deadline`, `ExpiresAt` + 2 s), giữ thêm 60 s khi job còn chạy; lane đọc và DO không đổi.
- [x] W/C dùng giờ server; audit có payload của 5 lệnh mở.

## Risk Assessment

- **Tách helper làm đổi hành vi DO:** `PhysicalOutputSafetyTests` phải xanh trước và sau bước 2.
- **Thiết bị bị khoá ghi:** mặc định 30 s, kéo dài nếu `ResponseTimeoutSeconds` lớn hơn; cộng tới 2 s chờ sweep và 60 s giữ lane cho job. Đây là chủ đích (spec R4). UI báo rõ thiết bị đang bận.
- **Regex hostname hoặc `type` chặt hơn firmware:** server chặt hơn là an toàn; ghi spec gap.
- **Danh sách `SupportedTimezones` hẹp:** chỉ cần nới danh sách, không ảnh hưởng an toàn.

## As built (2026-11-06)

- **Nhả lane ghi:** khi có reply thì nhả ngay. Đoạn "Một lệnh đã khỏi registry vẫn chặn lane…" ở trên chỉ áp dụng cho lệnh bị sweep, không phải lệnh đã có reply. Đúng với `plan.md` ("giữ tới khi có reply, hoặc tới max(`Deadline`, `ExpiresAt` + 2 s)"), và test giữ đúng như vậy.
- **Giữ lane cho job:** `HoldWriteLane` được gọi **trước** `Remove` trong `DeviceCommandResponseCorrelator`, để không lệnh ghi nào chen vào giữa. Hằng số `RunningJobWriteHold` = 60 s.
- **Phạm vi lane ghi:** mọi class trừ R và B (`CommandLanes.IsWrite`), nên D/F nếu sau này được mở cũng đi qua lane này.
- **Detail của mã từ chối:** viết bằng tiếng Anh, giống `DEVICE_READ_BUSY` đã có. UI dịch theo mã.
- **Lệnh chưa gửi vẫn tính vào giới hạn:** lệnh bị chặn ở `NeverSent` vẫn chiếm một slot trong giới hạn 10 lệnh/phút. Tính dư theo hướng an toàn.
- **Assertion có chủ đích phải sửa, ngoài danh sách trong plan:**
  - `ManagementApiTests` `/me` catalog: tập `available` thêm `ConfigWriteActions`;
  - `DeviceLinkTests.Confirm_token_is_built_from_the_mqtt_device_id`: `reboot` (class F) nay bị `CONFIG_ACTION_UNSUPPORTED`. Phần kiểm chuỗi `confirm` chuyển sang test codec, cộng thêm test confirm token cho một lệnh W.
- **Sửa sau review:**
  - surrogate lẻ trong tên kênh (`IsWellFormedUtf16`);
  - escape `�` từ API làm `GetString()` throw: `TryString` nay trả false;
  - lỗi có từ trước: `CommandRequestFingerprint.Compute` throw với payload như vậy, gây HTTP 500 cho mọi lệnh. Handler nay trả `ContractUnavailable`/`COMMAND_PAYLOAD_INVALID`;
  - chặn ký tự vô hình trong tên kênh (U+3164, U+115F, U+2800, U+034F, các variation selector…).
- **Test thêm:** reply của lệnh ghi bị ẩn sau khi role bị thu hồi; audit ghi `DEVICE_WRITE_BUSY`.
