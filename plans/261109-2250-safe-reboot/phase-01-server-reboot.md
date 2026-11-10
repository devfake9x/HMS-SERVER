---
phase: 1
title: "Server: mở reboot, cổng và theo dõi"
status: completed
priority: P1
dependencies: []
---

# Phase 1: Server — mở `system.reboot`, cổng và theo dõi

## Overview

Phase này làm 8 việc:
1. Mở `system.reboot` qua danh sách class F riêng; 3 row F khác vẫn đóng.
2. Class F chỉ cho master-admin, kiểm lúc gửi (red-team #2).
3. Payload chỉ `{}` (red-team #1).
4. Trần TTL 10 s cho class F.
5. Cổng trong bước atomic của registry: giữ từ lúc publish, cách nhau tối thiểu 60 s, không tốn slot (red-team #1, #12).
6. Tracker: uptime lấy từ chính heartbeat, ghi audit có điều kiện, đuôi 10 phút, không kết luận khi link broker mất, bỏ khi rebind/revoke (red-team #9, #10, #11).
7. Audit và realtime: mã theo dõi, `PayloadSummary` có `dry_run`, DO đánh dấu cũ khi lên lịch, "không rõ" sau khi server khởi động lại (red-team #13, #14).
8. Tracker là tham số bắt buộc ở mọi nơi dùng, có test DI với transport thật (red-team #6).

Phần có sẵn, không sửa:
- confirm dựng từ `binding.MqttDeviceId`, sai → `CONFIRM_MISMATCH` (`HmsIdfCommandProtocolAdapter.cs:46-53`); dry-run trên row không hỗ trợ → `DRY_RUN_UNSUPPORTED` (:55-56);
- thiếu confirm → `CONFIRM_REQUIRED` (`SendDeviceCommandHandler.cs:119-122`);
- root `confirm`/`dry_run` trong envelope (`HmsIdfCommandCodec.cs:58-61`);
- lane ghi cho mọi class trừ R và B (`CommandLanes.cs:10-11`; `MqttDeviceCommandTransport.cs:78-86`);
- heartbeat có uptime giảm đánh dấu DO cũ (`DeviceLinkHandlers.cs:247-253`), connectivity `offline` cũng vậy (:262-263).

## Requirements

### Catalog (`PinnedCommandCatalog.cs`)
- **`FlashActions`** (public static `FrozenSet`, đặt sau `ConfigWriteActions` :98-106): chỉ `("system","reboot")`.
- **Switch** (:236-242): thêm `CommandClass.Flash => FlashActions.Contains(...)`; `Destructive` vẫn rơi vào `_ => false`.
- **Comment:** quyết định owner 2026-11-09; vì sao `factory_reset` (xoá cấu hình, khoá) và `ota.*` (thiếu đặc tả tải/apply/rollback) vẫn đóng.

### Master-admin lúc gửi (red-team #2)
- **`CommandPermissionResolver.RequiresMasterAdmin(CommandDefinition d) => d.Class == CommandClass.Flash`** (hàm tĩnh mới trong `CommandPermissionResolver.cs`), dùng chung cho handler và `allowed-commands`.
- **Handler:** ngay sau khi `DeviceAccessPolicy.Evaluate` cho qua (`SendDeviceCommandHandler.cs:113-117`), trước `CONFIRM_REQUIRED` (:119): `RequiresMasterAdmin && !access.IsMasterAdmin` → `RecordAsync(request, null, Failure(Failed, DeviceAccessOutcomeCodes.HighRiskRequiresMasterAdmin))` (mã ở `DeviceAccessHandlers.cs:26`). Không giữ chỗ audit, không ký; có log như các từ chối trước audit khác (:197-203).
- **`allowed-commands`** (`DeviceCommandsController.cs:67-72`): thêm điều kiện `!RequiresMasterAdmin(x) || access.IsMasterAdmin`.
- **Comment:** quyết định user 2026-11-09 (tiêu chí 2 brainstorm); nới ra phải có quyết định riêng.

### Payload (`ConfigCommandPolicy.cs`) (red-team #1)
- Thêm nhánh vào switch (:36-70): `("system","reboot") => payload.Count == 0`.
- Comment: §6.1 L604; thiết bị dùng mặc định 2000 ms, server cần biết trước hạn và tránh `delay_ms:0`.

### TTL (`SendDeviceCommandHandler.cs`) (red-team #4)
- :133 đổi thành `definition.Class is CommandClass.Physical or CommandClass.Flash ? MaxPhysicalCommandTtlMs : MaxCommandTtlMs`.
- Doc của `MaxPhysicalCommandTtlMs` (:22-24): áp cho lệnh làm thay đổi thiết bị vật lý, gồm reboot; lý do là "lệnh đến trễ tệ hơn lệnh bị từ chối". Không ghi là chống phát lại.
- `/me/command-catalog` vẫn trả `ttlMs` 60000 (`MeController.cs:70`): đó là row của spec, không sửa.

### Tracker (`src/Hms.Server.Application/DeviceLink/DeviceRebootTracker.cs`, mới)
- **`DeviceRebootCodes`:** `InProgress = "DEVICE_REBOOT_IN_PROGRESS"`, `TooSoon = "DEVICE_REBOOT_TOO_SOON"`, `Scheduled`, `Observed`, `NotObserved`, `OutcomeUnknown` (`DEVICE_REBOOT_*`). Hằng: `DeviceDelay = 2 s` (mặc định §6.1), `Window = 120 s`, `MinGap = 60 s`, `Tail = 10 min`, `LinkBackGrace = 30 s`. `static bool IsReboot(type, action)`.
- **Trạng thái mỗi thiết bị:** tối đa một bản ghi, cộng uptime heartbeat cuối và mốc reboot thật được nhận gần nhất.
  - `Held`: reboot thật đã vào lane. Hạn = lúc nhận + 2 s + 120 s.
  - `Tracking`: đã có reply thành công. Hạn = lúc reply + 2 s + 120 s.
  - `Tail`: sau `NOT_OBSERVED`, 10 phút, không chặn cổng.
  - `Abandoned`: sau rebind/revoke, kết thúc ở sweep kế tiếp.
- **`RebootCompletion(Plan, Code, FromCodes, Detail)`**: detail dạng `evidence=heartbeat; uptime_before=…; uptime_after=…; after_s=…; connectivity=offline>online; reply=none; late_s=…; abandoned=…; link_down=true` (chỉ field có giá trị), ≤ 1024 ký tự (`PersistenceEntities.cs:94`).
- **`IDeviceRebootTracker`:**
  - `string? Admit(CommandExecutionPlan plan, DateTimeOffset now)`: registry gọi dưới khoá lane. Có `Held`/`Tracking` → `InProgress` (cả dry-run). Reboot thật trong 60 s kể từ mốc → `TooSoon`. Reboot thật được nhận → `Held` + ghi mốc. Dry-run được nhận → không đổi gì (L551: preview trước throttle).
  - `void Disarm(DeviceId, string correlationId)`: `Held` của đúng lệnh đó → xoá. Mốc 60 s giữ nguyên.
  - `RebootTracking? Upgrade(CommandExecutionPlan plan, DateTimeOffset repliedAt)`: `Held` của đúng lệnh → `Tracking`, trả bản ghi (có `UptimeBefore`); không có → null.
  - `RebootCompletion? ObserveHeartbeat(DeviceId, long? uptime, DateTimeOffset at)`: luôn cập nhật uptime cuối. Uptime nhỏ hơn uptime cuối trước đó và `at` sau mốc của bản ghi:
    - `Tracking` → trả `Observed` từ `{Scheduled}`;
    - `Held` → chỉ ghi bằng chứng (kết thúc lúc hết hạn giữ);
    - `Tail` → trả `Observed` từ `{NotObserved}`, detail `late_s`.
    Uptime null → bỏ qua.
  - `void ObserveConnectivity(DeviceId, string state, DateTimeOffset at)`: chỉ ghi cờ.
  - `void Abandon(DeviceId, string reason)`: xoá uptime cuối; `Held`/`Tail` → xoá; `Tracking` → `Abandoned(reason)`.
  - `IReadOnlyList<RebootCompletion> TakeDue(DateTimeOffset now, bool brokerConnected)`:
    - link mất → đánh dấu `link_down`, trả rỗng; lần đầu có lại link → mọi hạn ≥ now + 30 s;
    - `Tracking` quá hạn → `NotObserved` từ `{Scheduled}`, chuyển sang `Tail`;
    - `Held` quá hạn có bằng chứng → `Observed` từ `{MQTT_PUBLISHED, DEVICE_RESPONSE_TIMEOUT, MQTT_PUBLISH_UNCONFIRMED, DEVICE_ACCEPTED}`, detail `reply=none`; không bằng chứng → xoá, không ghi audit;
    - `Abandoned` → `OutcomeUnknown` từ `{Scheduled}`;
    - `Tail` quá 10 phút → xoá.
  - `void Retry(RebootCompletion c)`: ghi audit lỗi → đưa lại vào sweep kế tiếp, tối đa 3 lần rồi log lỗi.
- **`InMemoryDeviceRebootTracker`:** một `lock`; mỗi bản ghi kết thúc đúng một lần (heartbeat hoặc sweep, bên nào lấy trước).
- **DI** (`DependencyInjection.cs`, cạnh registry :38, ngoài mọi nhánh `if`): `AddSingleton<IDeviceRebootTracker, InMemoryDeviceRebootTracker>()`.

### Registry (`DeviceLinkContracts.cs`) (red-team #12)
- `TryRegisterWrite(PendingDeviceCommand pending, int maxPerMinute, Func<string?>? admit = null)` (:324-348). Thứ tự trong `_laneGate`:
  1. lệnh ghi đang chờ → `WriteBusy` (:332-333);
  2. đang giữ lane → `WriteBusy` (:334-335);
  3. dọn cửa sổ và kiểm tần suất, **chưa** ghi slot (:337-342);
  4. `admit?.Invoke()` khác null → trả mã đó, không ghi slot, không đăng ký;
  5. ghi slot (`recent.Enqueue`) và `Register`.
- Thứ tự khoá: `_laneGate` → khoá tracker; tracker không gọi registry. Tham số `admit` tuỳ chọn để các lệnh ghi khác không đổi.

### Transport (`MqttDeviceCommandTransport.cs`)
- **Ctor:** `IDeviceRebootTracker reboots` là tham số **bắt buộc** (red-team #6). Hằng `RebootInProgress`, `RebootTooSoon`.
- **Nhánh ghi** (:78-86): với `system.reboot`, truyền `admit: () => reboots.Admit(plan, now)` vào `TryRegisterWrite`; mã từ chối trả `ContractUnavailable` + detail tiếng Anh. Không publish.
- **NeverSent** (:105-109): thêm `reboots.Disarm(deviceId, correlationId)`.
- `PublishUnconfirmed` (:111-115): không đổi; cổng vẫn giữ.

### Correlator (`DeviceLinkHandlers.cs`, `DeviceCommandResponseCorrelator`)
- **Ctor:** tracker bắt buộc (red-team #6).
- **`HandleAsync`:** mã kết quả tính trước như cũ. **Sau khi `pending.Remove` trả true** (:86-87) (red-team #10), với `system.reboot` không dry-run:
  - `Executed` → `Upgrade`; có bản ghi → `result = result with { Code = Scheduled, Detail = … }` và `projection?.MarkStaleAsync(deviceId, OutputStaleReasons.DeviceRebootScheduled)` (red-team #14); null → `Code = OutcomeUnknown`, detail `tracking=unavailable`;
  - `Failed` + `DEVICE_REJECTED` → `Disarm`;
  - `Acknowledged` → không đổi gì (cổng vẫn giữ; lane giữ 60 s như :83-84).
  - Sau đó lưu reply, audit (:112), `command_result` (:113) như cũ.
- **`CompleteRebootAsync(RebootCompletion c)`** (public): `audit.TryCompleteAsync(c.Plan, new(Executed, c.Code, correlationId, c.Detail, keyGeneration), c.FromCodes)`; true → `PublishResultAsync` (:174-208); false → log (row đã sang mã khác); exception → `reboots.Retry(c)` và log.
- **`SweepExpiredAsync(DateTimeOffset now, CancellationToken ct = default, bool brokerConnected = true)`** (:118-146): sau vòng lệnh hết hạn, `reboots.TakeDue(now, brokerConnected)` → `CompleteRebootAsync`. `MqttDeviceLinkService.SweepTimeoutsAsync` (:290-295) truyền `_client.IsConnected`.

### Inbound (`DeviceInboundMessageHandler`, `DeviceLinkHandlers.cs:215-317`)
- **Ctor:** tracker bắt buộc.
- **Status** (:239-255) (red-team #9): `var uptime = DeviceTelemetryParsing.UptimeSeconds(status)`, đọc từ chính dictionary của message. `DeviceTelemetryParsing` là hàm tĩnh mới, tách từ `DeviceTelemetryContracts.cs:47-53`; store dùng lại nó, không đổi hành vi. Sau đó `reboots.ObserveHeartbeat(deviceId, uptime, receivedAt)` → có kết quả → `await correlator.CompleteRebootAsync(c)`. Đánh dấu DO cũ (:251-253) giữ nguyên.
- **Connectivity** (:257-269): `reboots.ObserveConnectivity(deviceId, state, receivedAt)` sau `RecordConnectivity` (:261).

### Rebind và revoke (red-team #9, #10)
- **`ConfigureDeviceMqttBindingHandler`** (`DeviceLinkHandlers.cs:320-346`): tracker bắt buộc; khi đổi topic base (:337-342) gọi `reboots.Abandon(deviceId, "rebind")`.
- **`RevokeDeviceHandler`** (`PermissionContracts.cs:231-245`): tracker bắt buộc; gọi `reboots.Abandon(deviceId, "revoked")`.
- Lý do: revoke chỉ huỷ row `IN_FLIGHT` (`EfCommandAuditStore.cs:28-42`), nên row `SCHEDULED` (`Executed`) không bao giờ thành `DEVICE_REVOKED`.

### Audit
- **`ICommandAuditStore.TryCompleteAsync(CommandExecutionPlan plan, CommandExecutionResult result, IReadOnlySet<string> fromCodes)` → `bool`** (red-team #10). Chỉ ghi Status/Code/Detail khi `Code` hiện tại thuộc `fromCodes` và khác `DEVICE_REVOKED`.
  - `EfCommandAuditStore`: nhánh relational `ExecuteUpdateAsync` có điều kiện (theo mẫu :152-160); nhánh in-memory provider (theo mẫu :170-180).
  - `InMemoryCommandAuditStore` (`CommandContracts.cs:233`): kiểm và ghi trong `_gate`.
  - Decorator test `BreakableAuditStore` (`DeviceOutputStateTests.cs:210-218`): chuyển tiếp.
- **`SummarizePayload`** (`EfCommandAuditStore.cs:221-237`) (red-team #13): class F → `{"dry_run":<bool>,"payload":{…}}`, ghi lúc giữ chỗ (:99); ≤ 512 ký tự.
- **`FailUnfinished`** (`EfCommandAuditStore.cs:44-71`; `CommandContracts.cs:314-336`): row `Code == Scheduled` → `Code = OutcomeUnknown`, giữ `Status`, detail mới `CommandTransportOutcome.RebootWatchLostDetail` = "The server restarted while it watched for the device to come back; read the device state." (detail cũ nói "trước khi thiết bị trả lời", sai với row đã có reply; `CommandContracts.cs:50-51`). Số trả về cộng thêm các row này. `Program.cs:157-165` không đổi.
- **Nhánh relational (Postgres):** dev không có Postgres; `TryCompleteAsync` và `FailUnfinished` relational **chỉ được review**, không có test chạy (red-team #6).

### DO đánh dấu cũ (red-team #14)
- `OutputStaleReasons.DeviceRebootScheduled = "device_reboot_scheduled"` (`DeviceOutputState.cs:23-29`).

### Giữ nguyên
- Từ chối quyền vẫn HTTP 200 + `Failed` (`SendDeviceCommandHandler.cs:113-117`).
- `GET /commands/{id}` trả code mới qua `audit.Find` (`DeviceCommandsController.cs:96`).
- Không migration EF; schema `CommandAuditRecord` (`PersistenceEntities.cs:80-106`) không đổi.

## Architecture

```
POST /commands ─► Handler: perm ─► [class F && !master] HIGH_RISK_REQUIRES_MASTER_ADMIN
               ─► CONFIRM_REQUIRED ─► TTL 10 s (F) ─► server time ─► reserve (PayloadSummary dry_run)
               ─► Adapter: CONFIRM_MISMATCH, DRY_RUN, payload {} , sign
               ─► Transport ─► Registry.TryRegisterWrite(lock: busy → rate check → admit → enqueue+register)
                                  admit = tracker.Admit: IN_PROGRESS | TOO_SOON | Held
                            ─► publish (NeverSent → Remove + Disarm)
B/cmd/res ─► Correlator: Remove true ─► Executed: Upgrade → SCHEDULED, DO stale
                                     ─► DEVICE_REJECTED: Disarm ; Acknowledged: keep Held
           ─► audit, command_result
B/status  ─► Inbound: uptime from message ─► ObserveHeartbeat ─► OBSERVED (CAS from SCHEDULED / NOT_OBSERVED)
sweep 2 s ─► TakeDue(now, brokerConnected) ─► NOT_OBSERVED | Held-end OBSERVED (reply=none) | Abandoned UNKNOWN
rebind / revoke ─► Abandon
startup   ─► FailUnfinished: SCHEDULED → OUTCOME_UNKNOWN (Postgres only; in-memory store is empty)
```

Lifetime: tracker singleton (như registry, `DependencyInjection.cs:38`); correlator, inbound (:46-47), binding (:48), revoke (:118) và transport (:57) scoped, cùng nhận một instance.

## Related Code Files

- **Create:**
  - `src/Hms.Server.Application/DeviceLink/DeviceRebootTracker.cs`
  - `tests/Hms.Server.UnitTests/DeviceRebootTrackerTests.cs`
  - `tests/Hms.Server.IntegrationTests/DeviceLinkWiringTests.cs` (test DI)
- **Modify:**
  - `src/Hms.Server.Infrastructure/Commands/PinnedCommandCatalog.cs`
  - `src/Hms.Server.Application/Commands/CommandPermissionResolver.cs`
  - `src/Hms.Server.Application/Commands/SendDeviceCommandHandler.cs`
  - `src/Hms.Server.Api/Controllers/DeviceCommandsController.cs`
  - `src/Hms.Server.Application/DeviceLink/ConfigCommandPolicy.cs`
  - `src/Hms.Server.Application/DeviceLink/DeviceLinkContracts.cs` (registry)
  - `src/Hms.Server.Application/DeviceLink/DeviceLinkHandlers.cs` (correlator, inbound, binding)
  - `src/Hms.Server.Application/DeviceLink/DeviceOutputState.cs` (lý do cũ)
  - `src/Hms.Server.Application/Realtime/DeviceTelemetryContracts.cs` (tách hàm đọc uptime)
  - `src/Hms.Server.Application/Authorization/PermissionContracts.cs` (revoke)
  - `src/Hms.Server.Application/Commands/CommandContracts.cs` (`ICommandAuditStore`, `InMemoryCommandAuditStore`, detail mới)
  - `src/Hms.Server.Infrastructure/Mqtt/MqttDeviceCommandTransport.cs`
  - `src/Hms.Server.Infrastructure/Mqtt/MqttDeviceLinkService.cs` (truyền `IsConnected` cho sweep)
  - `src/Hms.Server.Infrastructure/Persistence/EfCommandAuditStore.cs`
  - `src/Hms.Server.Infrastructure/DependencyInjection.cs`
  - test (red-team #6, danh sách đủ): `CatalogTests.cs`, `ConfigCommandPolicyTests.cs`, `DeviceLinkTests.cs` (fixture :354-371), `DeviceLaneSafetyTests.cs` (11 chỗ dựng transport), `PhysicalOutputSafetyTests.cs` (2 chỗ dựng transport, 2 chỗ dựng handler binding; `FailUnfinished` :253 vẫn đếm 2), `DeviceOutputStateTests.cs` (correlator/inbound :238-241, decorator :210-218), `DeviceCommandAuthorizationTests.cs`, `PermissionTests.cs` (2 chỗ dựng `RevokeDeviceHandler`), `tests/Hms.Server.IntegrationTests/PersistenceModelTests.cs` (2 chỗ dựng `RevokeDeviceHandler`; `FailUnfinished` :276), `tests/Hms.Server.IntegrationTests/ManagementApiTests.cs`
- **Delete:** không.

## Tests Before (viết trước, phải đỏ trước khi sửa code)

0. **Mốc:** chạy toàn bộ test, ghi số unit/integration hiện tại vào báo cáo phase.
1. **`CatalogTests`:**
   - **Có chủ đích:**
     - `Pinned_catalog_opens_only_the_listed_reads_relay_outputs_and_config_writes` (`CatalogTests.cs:138-161`): `expected` thêm `FlashActions`; assert class `Flash` cho từng phần tử; `33` → `34` (:155); `system.reboot` thành `Enabled` (:160); đổi tên test.
     - `Only_the_system_and_adc_config_writes_open_and_clock_or_link_writes_stay_closed` (:164-187): bỏ `system.reboot` khỏi `closed` (:175); "mọi row D/F đóng" (:185-186) thành "mọi row D/F trừ `system.reboot`".
   - **Mới `Only_reboot_opens_among_flash_rows`:** `FlashActions` có đúng 1 phần tử; `system.factory_reset`, `ota.remote_start`, `ota.apply` = `DeniedUntilActionContract`; hash không đổi.
2. **`ConfigCommandPolicyTests`:**
   - **Có chủ đích:** `Writes_without_a_payload_rule_fail_closed` (`ConfigCommandPolicyTests.cs:135-146`): thay `InlineData("system","reboot",Flash)` (:141) bằng `factory_reset`, `ota.apply`, `ota.remote_start`.
   - **Có chủ đích:** `Every_opened_config_write_has_a_payload_rule` (:156-167) duyệt thêm `FlashActions`.
   - **Mới `Reboot_takes_an_empty_payload_only`:** `{}` → null; `{delay_ms:2000}`, `{delay_ms:0}`, `{confirm:…}`, `{confirm_token:…}` → `COMMAND_PAYLOAD_INVALID`.
3. **Adapter thật (`DeviceLinkTests`):**
   - **Có chủ đích:** `Confirm_token_is_built_from_the_mqtt_device_id` (`DeviceLinkTests.cs:119-135`): `withMqttId` nay `CanPublish == true` (:131 đang chờ `CONFIG_ACTION_UNSUPPORTED`); bytes đã ký có `"confirm":"REBOOT-gateway_870c"`; `withGuid` vẫn `CONFIRM_MISMATCH`.
   - **Mới:** reboot `DryRun = true` → bytes có `"dry_run":true` và `confirm`; payload `{delay_ms:2000}` → `COMMAND_PAYLOAD_INVALID`, không đọc khoá (mẫu `ConfigCommandPolicyTests.cs:120`, :184).
4. **TTL:**
   - **Mới:** plan `system.reboot` có `ExpiresAt − RequestedAt` = 10 s; `RequestedAt` là giờ server dù client gửi now+50 s.
   - **Có chủ đích:** `Catalog_ttl_60000_rows_are_capped_to_the_command_ttl_limit` (`DeviceCommandAuthorizationTests.cs:196-214`): row class F mong `MaxPhysicalCommandTtlMs`, row khác giữ `MaxCommandTtlMs`; số 24 không đổi.
5. **Master-admin (`DeviceCommandAuthorizationTests`)** (red-team #2): member có role chứa `sys.reboot` gửi `system.reboot` → `Failed`/`HIGH_RISK_REQUIRES_MASTER_ADMIN`, `_transport.Records` rỗng, không có row audit; member thiếu `sys.reboot` → `DEVICE_PERMISSION_REQUIRED`; master-admin → qua bước này. Các test confirm có sẵn (:121-163) dùng master-admin nên không đổi.
6. **`DeviceRebootTrackerTests` (mới, logic thuần):**
   - `Admit`: reboot thật → `Held`, lần hai → `InProgress`; dry-run lúc `Held` → `InProgress`; dry-run lúc rảnh → nhận, không tạo `Held`; reboot thật trong 60 s sau mốc → `TooSoon`, sau 60 s → nhận.
   - `Disarm` xoá `Held` nhưng giữ mốc (`TooSoon` vẫn áp); `Disarm` sai `correlationId` → không đổi.
   - `Upgrade` → `Tracking` có `UptimeBefore`; không có `Held` → null.
   - heartbeat 500 → 503 → còn theo dõi; → 4 → `Observed` từ `{Scheduled}`, uptime 503 → 4; heartbeat trước mốc → bỏ qua; uptime null → bỏ qua; chưa có heartbeat trước mốc → heartbeat đầu chỉ đặt mốc.
   - `Held` thấy uptime giảm → không trả gì; hết hạn giữ → `Observed` từ tập mã "chưa rõ", detail `reply=none`; `Held` hết hạn không bằng chứng → không trả gì, cổng nhả.
   - `Tracking` hết hạn → `NotObserved`; heartbeat giảm trong 10 phút sau → `Observed` từ `{NotObserved}`, detail `late_s`; quá 10 phút → không gì.
   - `TakeDue(…, brokerConnected:false)` → rỗng; có lại link → hạn ≥ lúc đó + 30 s; kết quả có `link_down=true`.
   - `Abandon` lúc `Tracking` → sweep trả `OutcomeUnknown` từ `{Scheduled}`; mốc uptime bị xoá (heartbeat đầu sau đó không phải reboot).
   - `Retry` đưa lại tối đa 3 lần.
   - connectivity `offline` → `online` sau mốc chỉ bật cờ; `online` retained không có `offline` trước → không cờ.
   - 1000 vòng song song `ObserveHeartbeat` và `TakeDue` → mỗi bản ghi kết thúc đúng một lần.
7. **Registry + transport (`DeviceLaneSafetyTests`)** (red-team #1, #6, #12):
   - **Xen kẽ tất định:** registry bọc trong test; bên trong `Remove` của reply reboot 1 (qua correlator), gọi `SendAsync` reboot 2 → `DEVICE_REBOOT_IN_PROGRESS`. Phải đỏ nếu `Upgrade` làm mất `Held` trước khi đặt `Tracking`, hoặc nếu bỏ cổng.
   - reply mất (sweep ghi timeout) → reboot sau đó `IN_PROGRESS` tới hết hạn giữ; `Acknowledged` → vẫn `IN_PROGRESS`; `PublishUnconfirmed` → vẫn `IN_PROGRESS`.
   - NeverSent → `Held` nhả; reboot thật trong 60 s → `TooSoon`; dry-run → publish.
   - `DEVICE_REJECTED` → nhả; reboot thật trong 60 s → `TooSoon`.
   - `OBSERVED` ở giây 15 → dry-run qua, reboot thật `TooSoon` tới giây 60.
   - **Không tốn slot:** 11 reboot bị từ chối (`IN_PROGRESS`), rồi `system.set_hostname` vẫn publish.
   - đang giữ: `set_hostname` và `control.set_output` vẫn publish.
   - reboot thứ hai khi reboot đầu chưa có reply → `DEVICE_WRITE_BUSY` (lane có sẵn).
   - 13 chỗ dựng transport (11 ở đây, 2 ở `PhysicalOutputSafetyTests`) truyền tracker.
8. **Correlator + inbound (`DeviceLinkTests`, fixture `Link` :354-371; `DeviceOutputStateTests` :238-241)**:
   - reply thành công → audit `SCHEDULED`, có `uptime_before`; event `outputs` với `device_reboot_scheduled`; `command_result`.
   - heartbeat uptime thấp hơn → `OBSERVED`, detail `evidence=heartbeat`. Uptime lấy từ message: trước đó gọi `telemetry.Record` với uptime giả cao hơn (như POST `/telemetry`), heartbeat thật vẫn quyết định.
   - sweep sau hạn → `NOT_OBSERVED`; registry không có lệnh mới (không gửi lại).
   - reply trễ sau timeout → `Ignored`; `Held` hết hạn có bằng chứng → row `DEVICE_RESPONSE_TIMEOUT` thành `Executed`/`OBSERVED`, `reply=none`.
   - `Remove` trả false (sweep lấy trước) → không `Upgrade`, không `SCHEDULED`.
   - `TryCompleteAsync` không ghi khi row đã sang mã khác (ví dụ `OBSERVED` rồi mới tới `NOT_OBSERVED`).
   - rebind sang topic khác lúc theo dõi → `OUTCOME_UNKNOWN`, `abandoned=rebind`; revoke → `OUTCOME_UNKNOWN`, `abandoned=revoked` (thay test revoke cũ, vốn tự mâu thuẫn).
   - audit ghi lỗi một lần → sweep sau ghi được.
   - dry-run thành công → `DEVICE_EXECUTED`, không theo dõi; `success:false` + `UNAUTHORIZED` → `DEVICE_REJECTED`, cổng nhả.
9. **Audit:**
   - unit: `InMemoryCommandAuditStore.TryCompleteAsync` (đúng mã → ghi, sai mã → không); `FailUnfinished` row `SCHEDULED` → `OUTCOME_UNKNOWN`, status `Executed`, detail mới, có trong số đếm.
   - integration (`PersistenceModelTests`, EF in-memory provider, cạnh :249-276): như trên với `EfCommandAuditStore`; `PayloadSummary` của dry-run bị timeout là `{"dry_run":true,"payload":{}}`, của reboot thật bị timeout là `{"dry_run":false,"payload":{}}`; test có sẵn :276 và `PhysicalOutputSafetyTests.cs:253` vẫn đếm 2.
   - **Không phải coverage:** nhánh relational, xem "Audit" ở trên.
10. **`ManagementApiTests` (integration, seed binding như :368-370):**
    - **Có chủ đích:** `/me` catalog (`ManagementApiTests.cs:195-203`): tập `available` thêm `FlashActions`.
    - `allowed-commands`: master-admin → có `system.reboot`; member có role chứa `sys.reboot` → **không** (red-team #2); member chỉ xem → không.
    - HTTP: member có `sys.reboot` → 200 + `HIGH_RISK_REQUIRES_MASTER_ADMIN`; master-admin thiếu confirm → `CONFIRM_REQUIRED`; `REBOOT-<guid server>` → `CONFIRM_MISMATCH`; `{delay_ms:2000}` → `COMMAND_PAYLOAD_INVALID`; `system.factory_reset` → `DENIED_UNTIL_ACTION_CONTRACT`.
    - Hồi quy, không viết mới: gán role có `sys.reboot` bởi admin không phải master → `HIGH_RISK_REQUIRES_MASTER_ADMIN` (`DeviceAccessHandlerTests.cs:569-573`).
11. **DI (`DeviceLinkWiringTests`, mới)** (red-team #6): `new ServiceCollection().AddLogging().AddHmsInfrastructure(enableLiveMqtt: true)` cộng `MqttLinkOptions` hợp lệ; trong một scope resolve `IDeviceCommandTransport` (phải là `MqttDeviceCommandTransport`), `DeviceCommandResponseCorrelator`, `DeviceInboundMessageHandler`, `ConfigureDeviceMqttBindingHandler`, `RevokeDeviceHandler`; tracker từ hai scope là cùng instance; `Admit` một reboot trên tracker rồi gửi reboot qua transport đã resolve → `DEVICE_REBOOT_IN_PROGRESS`. Đã kiểm (Validation Session 1): ctor `MqttDeviceLinkService` (`MqttDeviceLinkService.cs:42-56`) gọi `_options.Validate()`. Hàm này thoát ngay khi `Enabled=false` (`MqttLinkOptions.cs:44-45`), nên để `MqttLinkOptions` mặc định là đủ. Nếu bật `Enabled` thì phải có `Host`, `ClientId` và các mốc thời gian hợp lệ (:46-58). Test chỉ build provider và resolve service, không start hosted service, nên không kết nối broker.

## Implement

1. `DeviceRebootTracker.cs` → test 6.
2. `FlashActions` + switch → test 1.
3. Luật `{}` → test 2, 3.
4. Master-admin lúc gửi + `allowed-commands` → test 5, 10.
5. Trần TTL class F → test 4.
6. `TryRegisterWrite` với `admit`; transport (tracker bắt buộc, `Disarm` khi NeverSent) → test 7.
7. `TryCompleteAsync`, `SummarizePayload`, `FailUnfinished` (hai store, decorator test) → test 9.
8. Correlator (`Upgrade` sau `Remove`, `Disarm`, DO cũ, `CompleteRebootAsync`, sweep có `brokerConnected`), inbound (uptime từ message), binding, revoke; `DeviceTelemetryParsing` → test 8.
9. DI singleton, sửa mọi chỗ dựng → test 11.
10. Sửa các assertion có chủ đích ở mục 1–4, 10.

## Tests After

- Mục 1–11 xanh; test cũ xanh trừ đúng các assertion có chủ đích đã liệt kê.
- Số test unit và integration ≥ mốc ở bước 0, cộng số test mới.
- Ghi vào "As built" mọi chỗ code khác plan và mọi assertion có chủ đích ngoài danh sách.

## Regression Gate

- `dotnet build Hms.Server.slnx` không có warning mới.
- `dotnet test tests/Hms.Server.UnitTests` và `dotnet test tests/Hms.Server.IntegrationTests` xanh.
- `dotnet ef migrations has-pending-model-changes --project src/Hms.Server.Infrastructure/Hms.Server.Infrastructure.csproj --startup-project src/Hms.Server.Infrastructure/Hms.Server.Infrastructure.csproj` báo không có thay đổi.

## Success Criteria

- [x] Đúng `system.reboot` mở, tổng 34; `factory_reset`, `ota.remote_start`, `ota.apply` đóng, có test.
- [x] Chỉ master-admin gửi được; `allowed-commands` khớp.
- [x] Payload khác `{}` bị chặn trước khi đọc khoá; confirm sai hoặc thiếu bị chặn trước khi ký; lệnh reboot ký `ttl_ms` 10000.
- [x] Cổng giữ từ lúc nhận vào lane qua mọi đường R4; 60 s giữa hai reboot thật; lệnh bị từ chối không tốn slot; lệnh khác không bị chặn.
- [ ] Audit `SCHEDULED` → `OBSERVED`/`NOT_OBSERVED` (nâng muộn được), `OUTCOME_UNKNOWN` khi bỏ hoặc sau khởi động lại (Postgres); ghi có điều kiện; `PayloadSummary` có `dry_run`. **Chưa đủ bằng chứng cho nhánh Postgres** (`TryCompleteAsync` và `FailUnfinished` relational chỉ review, không có Postgres ở dev). Phần còn lại có test (in-memory, EF in-memory provider). Mở: follow-up (b) của `plan.md`.
- [x] Tracker bắt buộc ở 5 nơi; test DI với transport thật xanh.
- [x] Không có đường nào gửi lại lệnh reboot.

## Risk Assessment

| Rủi ro | Khả năng × Ảnh hưởng | Giảm thiểu |
|---|---|---|
| `admit` bị gọi ngoài khoá lane hoặc trước kiểm tần suất | Thấp × Cao | Test xen kẽ và test 11 lần từ chối (mục 7) |
| Ghi đè audit sai row | Thấp × Trung bình | `TryCompleteAsync` có điều kiện; test mục 8 |
| Nhánh Postgres sai mà không test nào bắt | Trung bình × Trung bình | Review riêng; ghi "As built"; rủi ro còn lại ở `plan.md` |
| Đổi chữ ký ctor làm vỡ nhiều test | Cao × Thấp | Đã liệt kê từng file; sửa cơ học |
| Đổi trần TTL class F làm hỏng test khác | Trung bình × Thấp | Đã liệt kê assertion có chủ đích; chỉ Flash đổi |
| Heartbeat giả nhả cổng sớm | Trung bình × Trung bình | Mốc 60 s vẫn giữ; UI xác minh bằng uptime đã ký |
| Cổng và theo dõi mất khi server khởi động lại | Thấp × Cao | Rủi ro còn lại; throttle 60 s của thiết bị là lớp thứ hai |

## As built

Nguồn: `reports/phase-01-implementation-report.md` (kèm mục "Fixes after code review").

**Chỗ code khác plan**
- Test gate, reply và heartbeat nằm trong `tests/Hms.Server.UnitTests/DeviceRebootFlowTests.cs` (một bộ dựng: transport thật, registry bọc để test xen kẽ, tracker, correlator, inbound). Không tách vào `DeviceLaneSafetyTests` và `DeviceLinkTests`; hai file đó chỉ đổi constructor.
- `MqttDeviceCommandTransport` có tham số cuối tuỳ chọn `TimeProvider? clock = null` (mặc định hệ thống) để mốc 60 s và 120 s chính xác trong test. Tracker nhận `now` = `PublishedAt` của pending entry. Plan không có.
- Test DI (`DeviceLinkWiringTests.cs`) không chạm được cổng với link thật: link chưa start nên `SendAsync` trả `MQTT_NOT_CONNECTED` trước registry. Test gồm (a) resolve transport, correlator, inbound, binding, revoke ở hai scope, cùng một tracker; (b) provider thứ hai đăng ký `IMqttCommandPublisher` giả đã kết nối; `Admit` qua scope khác làm transport trả `DEVICE_REBOOT_IN_PROGRESS`. `ValidateScopes = true`; `ValidateOnBuild` để tắt vì ASP.NET services không liên quan lỗi.
- Tracker: không có trạng thái `Abandoned`. Bỏ một lệnh đang theo dõi đưa vào hàng đợi completion `OUTCOME_UNKNOWN`, `TakeDue` kế tiếp trả về (kể cả khi link broker mất, vì không kết luận gì về thiết bị). `RebootTracking` có thêm `Deadline`. `InMemoryDeviceRebootTracker` nhận `ILogger` tuỳ chọn (chỉ dùng khi bỏ retry sau 3 lần).
- Detail audit: chỉ các field có giá trị; `evidence=none` khi `NOT_OBSERVED` không có gì thêm.
- Thứ tự tham số: `reboots` đặt trước tham số tuỳ chọn đầu tiên của mỗi constructor (correlator sau `permissions`; inbound sau `correlator`; binding sau `bindings`; revoke sau `epochs`).
- `DO stale mark` khi lên lịch chỉ phát event `outputs` khi đã biết trạng thái relay (hành vi `MarkStale` sẵn có).
- Không tìm thấy test revoke-với-reboot mâu thuẫn cũ; không thay test nào. Rebind và revoke có test mới trong `DeviceRebootFlowTests`.
- `SweepExpiredAsync(now, bool brokerConnected = true, CancellationToken ct = default)` (đổi thứ tự sau review L11).
- Hành vi bổ sung của `CompleteRebootAsync`: khi CAS thất bại và row còn ở mã trước reply, trả completion lại tracker để retry (review L1).

**Sửa sau code review (server)**
- M1: giữ cổng đến `max(accept + 122 s, reply deadline + 2 s)`; `Admit(plan, now, replyDeadline)`; transport truyền deadline của pending entry. Test timeout 180 s.
- L1: CAS thua khi row chưa ghi reply được retry, không bị bỏ.
- L2: `Upgrade` giữ bằng chứng uptime giảm đã thấy khi còn Held; báo `uptime_before` trước khi giảm và kết luận `OBSERVED` qua completion từ `{SCHEDULED}`.
- L3: `NoteReplyAccepted` khi `Acknowledged`; detail ghi `reply=accepted`.
- L4: test guard heartbeat trước mốc (đã kiểm bằng mutation, khôi phục).
- L10: text cảnh báo khởi động trong `Program.cs`.
- L11: `ValidateScopes = true`; đổi thứ tự tham số `SweepExpiredAsync`.
- Thêm test từ chối đóng khác (`ManagementApiTests.The_other_flash_rows_stay_closed_even_for_a_master_admin`, 3 ca: factory_reset, ota.remote_start, ota.apply, master-admin). Mutation (thêm `ota.apply` vào `FlashActions`) làm ca đó đỏ.

**Residual đã chấp nhận (không sửa):** L5 (drop link ngắn hơn một vòng sweep không tới `TakeDue(brokerConnected:false)`), L6 (reboot mới trong đuôi 10 phút thay bản ghi đuôi cũ). Đã vào "Rủi ro còn lại" của `plan.md`.

**Số đo**
- Build: 0 lỗi, 3 cảnh báo (đều có sẵn: CS8631, 2x xUnit1031).
- Unit: 571 → 640 (0 lỗi).
- Integration: 117 → 126 (0 lỗi). Chạy với `Mqtt__Enabled=false`: user secrets Development trỏ CA path đã mất, không phải lỗi code. Báo cáo tester chạy 123 trước khi thêm 3 ca trên.
- `dotnet ef migrations has-pending-model-changes`: không thay đổi. Không migration.
- Test flaky đã có: `SignalRConnectionBacklogTests.Emits_runtime_metrics_for_drop_retry_and_resynchronization`, khoảng 1/35 lần, không liên quan reboot.

**Chưa làm được:** nhánh Postgres của `EfCommandAuditStore` (`TryCompleteAsync`, `FailUnfinished` relational): chỉ review.
