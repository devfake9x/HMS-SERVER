---
phase: 1
title: "Server: 5 field kênh của control.get_status"
status: completed
priority: P2
effort: 2h
dependencies: []
---

# Phase 1: Server — giữ `enabled`, `gpioOk`, `forced`, `owner`, `owner_id`

## Overview

Projection đọc reply `control.get_status` đã ký nhưng chỉ giữ `channel`, `state`, `mode`, `pulseActive`
(`DeviceOutputState.cs:13`, :151-172). Phase này thêm 5 field vào record và vào DTO của `GET /outputs`, theo đúng
những gì thiết bị gửi (spec `c80797d` L797, L839), không suy diễn. Chỉ thêm: client cũ không vỡ, catalog không đổi.

Phần có sẵn, không sửa:
- `ToResult` (`DeviceLinkHandlers.cs:245-268`): Detail `error: data.error`; UI phase 2 dựa vào định dạng này.
- Đánh stale sau lệnh class B không `Executed` (`DeviceOutputState.cs:102-111`) và tập từ chối trước handler (:116-122).
- Event `outputs` chỉ có `readAt`, `stale`, `staleReason` (:135-142; test `DeviceOutputStateTests.cs:50`).
- `command-catalog.json`, `PinnedCommandCatalog`, `CatalogTests` (hash `b1d47a78…`, `CatalogTests.cs:30`).

## Requirements

### Record (`src/Hms.Server.Application/DeviceLink/DeviceOutputState.cs`)
- :13 thành
  `OutputChannelState(int Channel, bool State, string? Mode, bool? PulseActive, bool? Enabled = null, bool? GpioOk = null, bool? Forced = null, string? Owner = null, string? OwnerId = null)`.
  Tham số mặc định giữ 3 chỗ dựng cũ biên dịch được: `tests/Hms.Server.UnitTests/DeviceRebootFlowTests.cs:230`, `tests/Hms.Server.UnitTests/PhysicalOutputSafetyTests.cs:168`, `tests/Hms.Server.IntegrationTests/ManagementApiTests.cs:649`. Chỗ dựng trong code: chỉ `DeviceOutputState.cs:168`.
- `public static class OutputOwners` (mới, đặt sau `OutputStaleReasons` :23-30): `None = "none"`, `Force = "force"`, `LevelRule = "level_rule"`, `EventRule = "event_rule"`, `Trigger = "trigger"`, `Unknown = "unknown"`; `FrozenSet<string> Known` (ordinal). Doc: giá trị theo spec L797.
- Lifetime: record bất biến, nằm trong `InMemoryDeviceOutputStateStore` (singleton có sẵn); không thêm state mới.

### Parse (`ParseChannels`, :150-172)
- Comment :150 đổi thành field list của L797 (`c80797d`), thay `§6.4`.
- Điều kiện loại cả reply giữ nguyên (:160-163): thiếu `channel` số ≥ 1 hoặc `state` bool.
- 3 hàm tĩnh nhỏ trong class:
  - `bool? Flag(JsonElement row, string name)`: chỉ JSON `true`/`false` → bool; thiếu, `null`, kiểu khác → `null`.
  - `string? OwnerOf(JsonElement row)`: thiếu hoặc JSON `null` → `null`; chuỗi trong `OutputOwners.Known` → chuỗi đó; mọi giá trị khác (chuỗi lạ, `"LEVEL_RULE"`, số, bool, object) → `OutputOwners.Unknown`.
  - `string? RuleIdOf(JsonElement row)` (đọc `owner_id`): chuỗi → `GetString()`; số → `GetRawText()`; kiểu khác → `null`. Không ràng buộc với `owner` (giữ như thiết bị báo).
- `parsed.Add(new OutputChannelState(number, state.GetBoolean(), mode, pulseActive, Flag(row,"enabled"), Flag(row,"gpioOk"), Flag(row,"forced"), OwnerOf(row), RuleIdOf(row)))`.
- Không suy diễn: `forced:true` không đặt `Owner`, `owner:"force"` không đặt `Forced`.

### DTO (`src/Hms.Server.Api/Controllers/DeviceLinkController.cs`)
- :214 `OutputChannelDto(int Channel, bool State, string? Mode, bool? PulseActive, bool? Enabled, bool? GpioOk, bool? Forced, string? Owner, string? OwnerId)`. Một chỗ dựng duy nhất (:53), không cần mặc định.
- :53 map thêm `x.Enabled, x.GpioOk, x.Forced, x.Owner, x.OwnerId`.
- Doc comment `Outputs` (:36-37): thêm "channel fields are what the device reported; null when it did not".
- JSON: camelCase mặc định của ASP.NET (`Program.cs:27-29` chỉ thêm `JsonStringEnumConverter`), nên ra `enabled`, `gpioOk`, `forced`, `owner`, `ownerId`; `null` vẫn được ghi (không có `DefaultIgnoreCondition`).

## Architecture

```
B/cmd/res get_status ─► Correlator ─► DeviceReplyProjection.ParseChannels
                                        row: channel,state,mode,pulseActive (cũ)
                                             + Flag(enabled|gpioOk|forced), OwnerOf(owner), RuleIdOf(owner_id)
                                    ─► IDeviceOutputStateStore.Set (singleton, in-memory)
GET /outputs ─► DeviceLinkController.Outputs ─► OutputChannelDto (9 property, camelCase) ─► FE phase 2
```

## Related Code Files

- **Create:** không.
- **Modify:**
  - `src/Hms.Server.Application/DeviceLink/DeviceOutputState.cs` (record :13, `OutputOwners`, `ParseChannels` :150-172)
  - `src/Hms.Server.Api/Controllers/DeviceLinkController.cs` (DTO :214, map :53, doc :36-37)
  - `tests/Hms.Server.UnitTests/DeviceOutputStateTests.cs` (test mới)
  - `tests/Hms.Server.IntegrationTests/ManagementApiTests.cs` (test mới)
- **Không sửa, phải biên dịch nguyên:** `DeviceRebootFlowTests.cs:230`, `PhysicalOutputSafetyTests.cs:168`, `ManagementApiTests.cs:649`.
- **Delete:** không.

## Implementation Steps

0. **Mốc** (ghi vào `reports/phase-01-implementation-report.md`):
   - `dotnet build Hms.Server.slnx` (ghi số cảnh báo; có sẵn: CS8631, 2x xUnit1031).
   - `dotnet test tests/Hms.Server.UnitTests` → mong 640.
   - `Mqtt__Enabled=false dotnet test tests/Hms.Server.IntegrationTests` → mong 126 (user-secrets Development trỏ CA path đã mất; không đọc secret).
   - `git status --short src/Hms.Server.Infrastructure/Commands/command-catalog.json` rỗng.
1. **Test đỏ, unit** (`tests/Hms.Server.UnitTests/DeviceOutputStateTests.cs`; dùng `Link`, `PublishedAsync(GetStatus)`, `SignedReply` có sẵn :230-286). Thêm hằng `GatewayChannels`: 6 kênh, DO1 `owner:"none"`; DO2, DO3 `gpioOk:false`; DO4, DO5 `forced:true,"owner":"force"`; DO6 `state:true,"owner":"level_rule","owner_id":7`; mọi kênh có `enabled`, `gpioOk`, `forced`, `owner`, `owner_id` (`null` khi không phải luật).
   - `A_status_reply_keeps_what_the_device_says_about_each_channel` (Fact): DO1 `Enabled true, GpioOk true, Forced false, Owner "none", OwnerId null`; DO2 `GpioOk false`; DO4 `Forced true, Owner "force"`; DO6 `Owner "level_rule", OwnerId "7"`; `State`, `Mode` như reply.
   - `Channel_fields_the_device_did_not_send_stay_null` (Fact): reply `SixChannels` (:27-28) → cả 5 field `null` trên mọi kênh; `Mode`, `PulseActive` như test :39-43.
   - `An_owner_outside_the_spec_list_becomes_unknown` (Theory): `"manual"` → `"unknown"`; `"LEVEL_RULE"` → `"unknown"`; `3` → `"unknown"`; `true` → `"unknown"`; `null` → `null`; và 6 giá trị đã biết giữ nguyên (`none`, `force`, `level_rule`, `event_rule`, `trigger`, `unknown`).
   - `A_rule_id_is_kept_as_text_whether_it_comes_as_a_number_or_a_string` (Theory): `7` → `"7"`; `"r-7"` → `"r-7"`; `null` → `null`; `true` → `null`.
   - `A_channel_field_of_another_type_is_dropped_and_the_channel_is_kept` (Fact): một kênh `"enabled":"yes","gpioOk":1,"forced":null` → 3 field `null`, kênh vẫn có, `State` đúng.
   - Đỏ vì: `OutputChannelState` chưa có property (lỗi biên dịch).
2. **Test đỏ, integration** (`tests/Hms.Server.IntegrationTests/ManagementApiTests.cs`, cạnh `Output_state_needs_the_view_permission_and_link_reports_what_the_caller_may_do` :625-665, cùng cách seed device + store + client master-admin :629-650):
   - `Output_state_adds_the_channel_fields_and_keeps_the_old_ones`: seed `[new(1, false, "toggle", false), new OutputChannelState(6, true, "toggle", null, Enabled: true, GpioOk: true, Forced: false, Owner: "level_rule", OwnerId: "7")]`; `GET /outputs` bằng master-admin:
     - mỗi kênh có đúng tập property `{channel, state, mode, pulseActive, enabled, gpioOk, forced, owner, ownerId}`;
     - kênh 1: 5 property mới là JSON `null`; kênh 6: `owner == "level_rule"`, `ownerId == "7"`, `gpioOk == true`, `forced == false`;
     - object ngoài đúng `{channels, readAt, stale, staleReason}`.
   - Đỏ vì: `OutputChannelState` chưa có tham số đặt tên (lỗi biên dịch), rồi DTO thiếu property.
3. **Code:**
   - Record :13 + `OutputOwners` (theo Requirements).
   - `Flag`, `OwnerOf`, `RuleIdOf` + `parsed.Add` (:168).
   - DTO :214 + map :53 + doc :36-37.
4. **Xanh:**
   - Test mục 1, 2 xanh; `DeviceOutputStateTests`, `DeviceRebootFlowTests`, `PhysicalOutputSafetyTests`, `ManagementApiTests` cũ xanh, không sửa assertion nào.
   - Toàn bộ unit và integration (lệnh ở bước 0): số ≥ mốc + 5 Fact/Theory mới (unit) + 1 (integration). Ghi số thật (Theory đếm theo ca).
   - `dotnet build Hms.Server.slnx`: không cảnh báo mới.
   - `git diff --stat src/Hms.Server.Infrastructure/Commands/` rỗng; `CatalogTests` xanh, không sửa.

## Success Criteria

- [ ] Reply đủ field → `OutputChannelState` đủ 5 field đúng giá trị thiết bị gửi.
- [ ] Thiếu field → `null`; field sai kiểu → `null`, kênh và reply vẫn được nhận.
- [ ] `owner` ngoài 6 giá trị (kể cả khác hoa/thường, kiểu khác chuỗi) → `"unknown"`; `owner_id` số → chuỗi.
- [ ] `GET /outputs` có `enabled`, `gpioOk`, `forced`, `owner`, `ownerId` trên mọi kênh, `null` khi không biết; 4 field cũ và shape ngoài không đổi.
- [ ] Catalog JSON, hash, `CatalogTests` không đổi.
- [ ] Unit ≥ 640 + mới, integration ≥ 126 + mới, 0 lỗi; không cảnh báo build mới.

## Risk Assessment

| Rủi ro | Khả năng × Ảnh hưởng | Giảm thiểu |
|---|---|---|
| Field tuỳ chọn ném lỗi làm cả reply không đọc được (mất state DO) | Trung bình × Trung bình | Hàm parse không ném; test field sai kiểu |
| Tên JSON khác FE mong đợi | Thấp × Trung bình | Integration test so tên property chính xác |
| `owner_id` số ra dạng lạ (`7.0`) | Thấp × Thấp | `GetRawText()` giữ nguyên chữ thiết bị gửi; test số |
| Đổi chữ ký record làm vỡ chỗ dựng | Thấp × Thấp | Tham số mặc định; 3 chỗ dựng đã liệt kê |
| So sánh bằng của record đổi (thêm field) | Thấp × Thấp | Không có chỗ nào so sánh `OutputChannelState` (grep `OutputChannelState` chỉ ra các chỗ dựng ở trên) |

Rollback: revert commit; không migration, không state mới.

## As built

Khop plan. Khong lech: record + OutputOwners + Flag/OwnerOf/RuleIdOf trong DeviceOutputState.cs; DTO + map + doc trong DeviceLinkController.cs.
Test do (compile error CS1061/CS1739) roi xanh. Moc 640/126 -> 658/127 (unit +18 ca, integration +1). Build 0 error, 3 warning nhu cu. Catalog diff rong. has-pending-model-changes: bo qua (khong co DB/EF project; khong doi storage).
