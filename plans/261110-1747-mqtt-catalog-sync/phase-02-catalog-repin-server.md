---
phase: 2
title: "Ghim catalog c80797d, server và test"
status: completed
priority: P2
effort: 3.5h
dependencies: [1]
---

# Phase 2: Ghim catalog `c80797d`, high-risk, test server

## Overview

Đổi `SOURCE_COMMIT` sang `c80797d…`, sinh lại `command-catalog.json` bằng `--accept-stated-count 98/12`, kiểm
bằng script độc lập, ghim hash vào test, thêm 4 mã high-risk, sửa comment `PinnedCommandCatalog.cs:44-49`. Code
server khác không đổi: catalog quyết định quyền `status.tags`; 14 row mới đóng vì không nằm trong allowlist nào
(`PinnedCommandCatalog.cs:249-257`).

## Hiện trạng (đã đọc khi lập plan)

- `command-catalog.json`: `mqtt-api-83e93d77e`, hash `b1d47a78…`, 94 row, 6 domain.
- Test ghim số hoặc giá trị của catalog cũ:
  - `CatalogTests.cs`: :15, :18-19, :22-24, :30, :107-109, :217-222, :228-233, :258-262, :271-287, :372.
  - `DevicePermissionVocabularyTests.cs`: :11-15 (50), :21-28 (46).
  - `DevicePermissionCodesTests.cs`: :21-33 (9 mã high-risk), :35-44.
  - `CommandPermissionResolverTests.cs`: :134-151 (18 row high-risk).
  - `DeviceCommandAuthorizationTests.cs`: :189-195 (3 row confirm), :198-221 (24 row TTL 60000; cap chỉ Flash).
  - `ManagementApiTests.cs`: :191 (94 action), :250-252 (18 high-risk).
- Quyền của row mở chỉ được ghim rải rác (`CatalogTests.cs:297,300,301`, `ManagementApiTests.cs:233`, :349-357, :375-392); không gì ghim người giữ `sys.info_view` mở được gì.
- Integration lấy version/hash qua `PinnedCatalogMetadata()` (`ManagementApiTests.cs:1460`), không hard-code.
- `DeviceAccessTestFixture.cs:20` dùng `PinnedCommandCatalog` thật → từ vựng role theo catalog mới.
- Kiểm khi tạo role: tổ chức → text → từ vựng → high-risk (`DeviceAccessHandlers.cs:54-57`); sửa: high-risk sau từ vựng (:94-106); xoá :166; gán/gỡ qua `CheckRoleChange` (:225, :289, :315-327). Mã từ chối từ vựng: `DEVICE_PERMISSION_UNKNOWN` (:31).
- Đường gửi khi đủ quyền trong integration (binding có, không có command key): giữ chỗ audit (`SendDeviceCommandHandler.cs:171`) → `HmsIdfCommandProtocolAdapter` trả `COMMAND_KEY_NOT_PROVISIONED` (:67-69) → `DeferredDeviceCommandTransport.cs:12-15` → kết quả `ContractUnavailable`/`COMMAND_KEY_NOT_PROVISIONED`.
- Device-action grant: `POST /api/v1/permissions/device-actions` (`PermissionGrantsController.cs:81-101`), `PUT /api/v1/devices/{id}/permissions` (`DevicePermissionsController.cs:46-89`, `expectedPolicyVersion` tuỳ chọn) → `GrantDeviceActionHandler` (`PermissionContracts.cs:220-229`, chỉ kiểm rỗng). `/me/permissions` liệt kê dạng `device:{id:N}:action:{action}` (`MeController.cs:30`). Phân quyền lệnh chỉ đọc role membership (`DeviceAccessResolver.cs:23-35`).
- `PinnedCommandCatalog.cs:44-49` ghi nhóm đầu là "what the pinned spec snapshot closed as SLICE_PASS". Ở `c80797d`, bảng "API đã đóng trong campaign hiện tại" (spec L34-51) ghi `status.tags` (C07, L47) với bằng chứng dạng trang `PASS_HOST`, `HIL_PASS`; receipt `SLICE_PASS` dạng mảng giữ làm lịch sử (L47, L2129).

### Sự thật spec (đối chiếu từng row qua `git show` hai commit)

| Row (spec L) | Class | Quyền | TTL | Confirm |
|---|---|---|---|---|
| `tags.list` (1410), `tags.get` (1411) | R | `tag.view` | 120000 | — |
| `modbus.get_rtu_devices` (1418) | R | `mb_rtu.slave_view` | 120000 | — |
| `modbus.get_rtu_tags` (1419), `get_rtu_live` (1420) | R | `mb_rtu.tag_view` | 120000 | — |
| `modbus.get_rtu_diagnostics` (1421) | R | `mb_rtu.config_view` | 120000 | — |
| `status.tags` (1428) **đổi** | R | `tag.view` (cũ `sys.info_view`) | 120000 | — |
| `sms.get` (1434) | R | `sms.view` | 120000 | — |
| `sms.send_status` (1435) | R | `sms.send` | 120000 | — |
| `sms.send` (1436) | B | `sms.send` | 60000 | `SMS-SEND-<DEVID>` |
| `sms.delete` (1437) | D | `sms.delete` | 60000 | `SMS-DELETE-<DEVID>` |
| `control.force_set` (1471), `force_clear` (1472) | B | `gpio.do_force` | 60000 | — |
| `control.set_config` (1473) | B | `gpio.do_config` | 60000 | — |
| `control.get_config` (1474) | R | `gpio.do_view` | 120000 | — |

Dry run: không row mới nào. Bảng domain không đổi. Số kỳ vọng (tính lúc lập plan từ `git show`; đếm row/namespace/pair-set
thêm một lần bằng regex dòng không dùng code bộ sinh):

| Số | Cũ | Mới |
|---|---|---|
| Row / namespace / domain | 94 / 12 / 6 | 108 / 13 / 6 |
| R / W / C / D / B / F | 49 / 18 / 17 / 3 / 3 / 4 | 58 / 18 / 17 / 4 / 7 / 4 |
| TTL 60000 (có confirm; Wi-Fi) / TTL 120000 | 24 (3; 21) / 70 | 29 (5; 21) / 79 |
| Row confirm / row dry run | 3 / 3 | 5 / 3 |
| Quyền firmware / từ vựng | 46 / 50 | 53 / 57 |
| Mã high-risk / row high-risk | 9 / 18 | 13 / 24 |
| Action mở / action `sys.info_view` mở | 34 / 9 | 34 / 8 |
| Pair-set SHA | `dbda8317…` | `4286c13cd55ef43ca344ba98d07f7e99b98f111872d408d2afb97ec6017c2b07` |
| `catalog_hash` | `b1d47a78…` | kỳ vọng `658e191a9e66f14a6885183b6ae9d9ec72a9e25a396c3cd30c80bdcd4df3413b` |

Metadata mới: `source_commit` `c80797d9d5b817174cd2cfbcb6ee62effe503ff2`, `catalog_version` `mqtt-api-c80797d9d`,
`source_blob` `84ff812f68567e07cfd3a2dfeeb862ceb48630f6`, `source_hash` `8cde6ab454b7148bd29ab5a49c5187ec98b1143b4f11f84de76c95d9451c6406`.

### 34 action mở sau ghim (class, quyền, TTL, confirm)

Chỉ dòng `status.tags` khác ghim cũ (cũ: `sys.info_view`). Dạng chuỗi dùng trong test (ordinal):
```
adc.get_channels Read adc.view 120000 -
adc.get_config Read adc.view 120000 -
adc.get_status Read adc.view 120000 -
adc.get_values Read adc.view 120000 -
adc.set_channel Write adc.config_edit 120000 -
adc.toggle_channel Control adc.enable 120000 -
battery.get_alerts Read sys.battery_view 120000 -
battery.get_status Read sys.battery_view 120000 -
control.get_status Read gpio.do_view 120000 -
control.pulse Physical gpio.do_write 120000 -
control.set_output Physical gpio.do_write 120000 -
mqtt.get_config Read mqtt.status_view 120000 -
mqtt.get_status Read mqtt.status_view 120000 -
network.get_all_status Read net.priority_view 120000 -
network.get_ethernet Read eth.view 120000 -
network.get_modem Read modem.view 120000 -
network.get_priority Read net.priority_view 120000 -
network.wifi_ap_status Read wifi.ap_view 60000 -
network.wifi_sta_status Read wifi.sta_view 60000 -
ota.get_status Read sys.info_view 120000 -
status.dump Read sys.info_view 120000 -
status.services Read sys.info_view 120000 -
status.tags Read tag.view 120000 -
system.get_device_id Read sys.info_view 120000 -
system.get_heap Read sys.info_view 120000 -
system.get_hostname Read sys.hostname_view 120000 -
system.get_info Read sys.info_view 120000 -
system.get_operations Read sys.info_view 120000 -
system.get_time Read sys.time_view 120000 -
system.ping Read sys.info_view 120000 -
system.reboot Flash sys.reboot 60000 REBOOT-<DEVID>
system.set_hostname Write sys.hostname_edit 120000 -
system.set_timezone Write sys.time_edit 120000 -
system.sync_ntp Control sys.time_edit 120000 -
```

## Requirements

- `scripts/generate-command-catalog.mjs`, hằng `SOURCE_COMMIT` (L21 lúc lập plan): `'c80797d9d5b817174cd2cfbcb6ee62effe503ff2'`. Không đổi gì khác trong file.
- `command-catalog.json`: chỉ do bộ sinh viết; không sửa tay.
- `DevicePermissionCodes.HighRisk` (`DevicePermissionCodes.cs:21-28`), sau `"gpio.do_write"`:
  ```csharp
  // Forces a relay output, or changes the output configuration the device applies at boot.
  "gpio.do_force", "gpio.do_config",
  // Sends or deletes SMS: costs money, reaches people outside the system, or destroys messages.
  "sms.send", "sms.delete"
  ```
  Comment trên `HighRisk` (:20) thêm: kiểm khi ghi grant (tạo/sửa/xoá role, gán/gỡ membership), không kiểm lúc gửi; đổi tập này không xét lại grant đã có.
- `PinnedCommandCatalog.cs:44-49`, chỉ comment:
  ```csharp
  // Read actions the server signs. The first group is the pinned spec's "API đã đóng trong campaign
  // hiện tại" table (snapshot verified 2026-10-28), plus system.ping (the spec §1 smoke test) and
  // system.get_operations (job read-back, spec §5). In that table status.tags now has paged-reply
  // evidence (PASS_HOST, HIL_PASS) and keeps its array-form SLICE_PASS receipt only as history; it
  // stays open with the permission the catalog gives it.
  ```
  Phần còn lại của comment (nhóm giám sát, đọc nhạy cảm) giữ nguyên. Allowlist không đổi.
- `CommandPermissionResolver.cs`, controller, frontend: không sửa.

## Related Code Files

- **Modify:** `scripts/generate-command-catalog.mjs` (hằng), `src/Hms.Server.Infrastructure/Commands/command-catalog.json` (sinh),
  `src/Hms.Server.Infrastructure/Commands/PinnedCommandCatalog.cs` (comment), `src/Hms.Server.Domain/Authorization/DevicePermissionCodes.cs`,
  `tests/Hms.Server.UnitTests/{CatalogTests,DevicePermissionVocabularyTests,DevicePermissionCodesTests,CommandPermissionResolverTests,DeviceCommandAuthorizationTests,DeviceAccessHandlerTests}.cs`,
  `tests/Hms.Server.IntegrationTests/ManagementApiTests.cs`.
- **Create/Delete:** không.

## Implementation Steps

0. **Mốc và nguồn** (ghi vào `reports/phase-02-implementation-report.md`):
   - `dotnet build Hms.Server.slnx` (ghi số cảnh báo; có sẵn CS8631, 2x xUnit1031); `dotnet test tests/Hms.Server.UnitTests` → mong 658; `Mqtt__Enabled=false dotnet test tests/Hms.Server.IntegrationTests` → mong 127 (không đọc user-secrets); node 2 file test → mong 31.
   - HMS-IDF, chỉ git, luôn `--no-replace-objects`:
     - `for-each-ref refs/replace` → rỗng (không rỗng: dừng, báo controller).
     - `ls-tree c80797d engineering-docs/api/mqtt-api.md` → blob `84ff812f…`; `show c80797d:engineering-docs/api/mqtt-api.md | sha256sum` → `8cde6ab4…`.
     - `branch -r --contains c80797d9d5b8…` và `… 83e93d77e4a4…` → ghi danh sách vào report (lúc lập plan: chỉ `origin/feat/next-work-20261019`, `origin/claude/status-tags-paging`, và với `83e93d77e` thêm `origin/refactor-all-upate-v6`). Không nhánh remote nào chứa `c80797d` → dừng, báo controller.
1. **Test đỏ, unit** (sửa assertion cũ + thêm test; ghi lý do từng chỗ trong report):
   - `CatalogTests.cs`:
     - :15 `PinnedSpecCommit` = `c80797d9d5b817174cd2cfbcb6ee62effe503ff2`; :18 `PinnedSpecBlob` = `84ff812f…`; :19 `PinnedSpecSha256` = `8cde6ab4…` (giá trị đầy đủ ở trên).
     - :22 comment "94" → "108". :24 `ManifestPairSetSha256`, :30 `PinnedCatalogHash`: **chưa đổi ở bước này** (bước 6).
     - :107-109 → 108, 108, 13. :372 → 108.
     - :217-222 → R 58, W 18, C 17, D 4, B 7, F 4 (:224 `Dangerous` 4 giữ).
     - :228 đổi tên `Pinned_catalog_exposes_53_firmware_permissions_and_6_config_domains`; :233 → 53; thêm `Assert.Contains` cho `tag.view`, `mb_rtu.slave_view`, `mb_rtu.tag_view`, `sms.send`, `sms.delete`, `gpio.do_force`, `gpio.do_config`.
     - :258 → 29; :259 → 5; :260 giữ 21; :262 → 79.
     - :271-281 → 5 chuỗi ordinal: `ota.apply=OTA-APPLY-<DEVID>`, `sms.delete=SMS-DELETE-<DEVID>`, `sms.send=SMS-SEND-<DEVID>`, `system.factory_reset=FACTORY-RESET-<DEVID>`, `system.reboot=REBOOT-<DEVID>`. Dry run (:282-287) giữ 3.
     - Theory `Pinned_catalog_row_matches_spec` (:290-311) thêm 15 `InlineData` theo bảng Sự thật spec (`status.tags` + 14 row; class `Read`/`Physical`/`Destructive`, quyền, TTL).
     - Fact mới `Tag_paged_modbus_sms_and_output_force_or_config_rows_stay_closed`: 14 cặp có trong catalog, `DeniedUntilActionContract`, không nằm trong 4 allowlist.
     - Fact mới `Status_tags_is_open_and_needs_tag_view`: `Enabled`, `Read`, `RequiredPermission == "tag.view"`; `EnabledActions` chứa `("status","tags")`.
     - Fact mới `Open_actions_keep_their_reviewed_class_permission_ttl_and_confirm`: hai bước, để thấy lần ghim này đổi đúng một dòng:
       (a) viết với 34 chuỗi của ghim **cũ** (bảng trên, dòng `status.tags` là `Read sys.info_view 120000 -`) → chạy, phải **xanh** trên catalog cũ;
       (b) đổi đúng dòng đó thành `status.tags Read tag.view 120000 -` → đỏ.
       Actual = 4 allowlist nối lại → `catalog.Find` → `$"{Type}.{Action} {Class} {RequiredPermission} {DefaultTtlMs} {ConfirmTemplate ?? "-"}"` → `Order(StringComparer.Ordinal)`; `Assert.Equal(expected, actual)`. Comment trong test: danh sách sửa tay từ catalog đã review; một lần ghim đổi row mở phải đổi dòng ở đây, tách khỏi các literal hash.
     - Giữ nguyên, phải xanh: :135 (26 đọc), :157 (34 mở), :205-208 (mọi D/F trừ reboot đóng: nay phủ cả `sms.delete`).
   - `DevicePermissionVocabularyTests.cs`: :11 đổi tên `Vocabulary_has_fifty_seven_codes`, :15 → 57, thêm `Assert.Contains("tag.view", vocabulary)`; :21 đổi tên `Firmware_part_of_vocabulary_has_53_codes`, :28 → 53.
   - `DevicePermissionCodesTests.cs`: :23-29 kỳ vọng 13 mã ordinal: `eth.edit`, `gpio.do_config`, `gpio.do_force`, `gpio.do_write`, `mqtt.config_edit`, `net.priority_edit`, `sms.delete`, `sms.send`, `sys.factory_reset`, `sys.ota`, `sys.reboot`, `wifi.sta_connect`, `wifi.sta_edit` (Subset :30-32 giữ). `IsHighRisk_is_exact_and_null_safe` thêm `("gpio.do_force", true)`, `("gpio.do_config", true)`, `("sms.send", true)`, `("sms.delete", true)`, `("sms.view", false)`, `("tag.view", false)`.
   - `CommandPermissionResolverTests.cs` :137-143 → 24 action ordinal: `config.set`, `control.force_clear`, `control.force_set`, `control.pulse`, `control.set_all`, `control.set_config`, `control.set_output`, `mqtt.set_config`, `network.configure_ethernet`, `network.set_priority`, `network.wifi_known_connect`, `network.wifi_sta_config_set`, `network.wifi_sta_connect`, `network.wifi_sta_disable`, `network.wifi_sta_disconnect`, `network.wifi_sta_enable`, `ota.apply`, `ota.remote_config_get`, `ota.remote_start`, `sms.delete`, `sms.send`, `sms.send_status`, `system.factory_reset`, `system.reboot`.
   - `DeviceCommandAuthorizationTests.cs`:
     - :189 đổi tên `Only_the_spec_confirm_rows_require_confirmation`; kỳ vọng `["ota.apply", "sms.delete", "sms.send", "system.factory_reset", "system.reboot"]`.
     - :211-212 → 29, 29. :215-220 kỳ vọng cap = `Class is CommandClass.Physical or CommandClass.Flash ? MaxPhysicalCommandTtlMs : MaxCommandTtlMs` (luật `SendDeviceCommandHandler.cs:143-145`); comment :213-214 → "a relay or flash command gets the tighter physical cap, as the handler does". Không sửa code handler.
   - `DeviceAccessHandlerTests.cs`, Theory mới cạnh :566-581:
     `Output_force_output_config_and_sms_send_or_delete_are_granted_by_a_master_admin_only(string code)` với `InlineData` `gpio.do_force`, `gpio.do_config`, `sms.send`, `sms.delete`:
     - Seed: `plain = _world.SeedRole(_world.Child.Id, DevicePermissionCodes.DeviceView)`; `withCode = _world.SeedRole(_world.Child.Id, DevicePermissionCodes.DeviceView, code)`.
     - `admin = _world.Admin(NewUser(_world.Child.Id), _world.Child.Id, "permissions.manage")`: `CreateRole(admin, _world.Child.Id, "Holder", DevicePermissionCodes.DeviceView, code)`, `UpdateRole(admin, plain, [DevicePermissionCodes.DeviceView, code])`, `UpdateRole(admin, withCode, [DevicePermissionCodes.DeviceView])`, `_world.DeleteRole().Handle(admin, _world.Child.Id, withCode.Id, withCode.Version)` → mỗi cái `HIGH_RISK_REQUIRES_MASTER_ADMIN`; `Assert.Empty(_world.Access.Attempts)`.
     - `holder = ManagingAdmin(_world.Device, DevicePermissionCodes.DeviceView, code)`: `Assign(holder, NewUser(_world.Child.Id), withCode)`; với `target` đã `SeedMembership(target, _world.Device, withCode)`: `Assign(holder, target, plain, expectedRoleId: withCode.Id)` và `_world.Remove().Handle(holder, _world.Device.Id, target, withCode.Id)` → mỗi cái `HIGH_RISK_REQUIRES_MASTER_ADMIN`; `Assert.Empty(_world.Access.Writes)`.
     - `CreateRole(Master(), _world.Child.Id, $"Holder {code}", DevicePermissionCodes.DeviceView, code)` → `OK`.
   - Đỏ vì: catalog vẫn 94 row, `status.tags` còn `sys.info_view`, mã mới chưa có trong từ vựng (`DEVICE_PERMISSION_UNKNOWN`), `HighRisk` thiếu 4 mã.
2. **Test đỏ, integration** (`ManagementApiTests.cs`; `IClassFixture` dùng chung: mỗi test dùng thiết bị, user, correlation id riêng):
   - :191 → 108.
   - :250-252: comment → "the rows behind relay output, output force and output config, SMS send and delete, plus the 15 others"; 18 → 24; thêm: `control.force_set`, `sms.send_status`, `sms.delete` có `highRisk` true; `status.tags` có `requiredPermission == "tag.view"`, `highRisk` false.
   - `Allowed_commands_lists_what_the_caller_may_send_on_this_device` (:302-337), khoá (xanh trước và sau): thêm `Assert.Equal(34, adminActions.Length)`, `Assert.Contains("status.tags", adminActions)`, `Assert.DoesNotContain("control.force_set", adminActions)`, `Assert.DoesNotContain("tags.list", adminActions)`.
   - Fact mới `Status_tags_needs_tag_view_and_sys_info_view_alone_is_not_enough`: thiết bị `Activate()` + binding (mẫu :364-373); `infoViewer` = `GrantDeviceRole(…, "sys.info_view")`, `tagViewer` = `GrantDeviceRole(…, "tag.view")`, master-admin; body `{ type:"status", action:"tags", payload:{}, correlationId, catalogVersion, catalogHash }` từ `PinnedCatalogMetadata()`:
     - `allowed-commands` của `infoViewer` `Assert.Equal` đúng `["ota.get_status", "status.dump", "status.services", "system.get_device_id", "system.get_heap", "system.get_info", "system.get_operations", "system.ping"]`; của `tagViewer` `Assert.Equal(["status.tags"])`.
     - `infoViewer` gửi (`tags-info-1`) → HTTP 200, `DEVICE_PERMISSION_REQUIRED`; `FindPlan` null.
     - master-admin gửi (`tags-admin-1`) → code A; `tagViewer` gửi (`tags-tag-1`) → code bằng A và bằng `COMMAND_KEY_NOT_PROVISIONED` (Hiện trạng); `FindPlan(…, "tags-tag-1")` khác null. Nếu code thật khác `COMMAND_KEY_NOT_PROVISIONED`: dừng, đọc lại đường gửi, ghi lý do vào report trước khi ghim.
   - Theory mới `Tag_paged_modbus_sms_and_output_force_or_config_rows_stay_closed_even_for_a_master_admin(string type, string action, string? confirm)`, thân như `The_other_flash_rows_stay_closed_even_for_a_master_admin` (:460-491), binding id `closed_{type}_{action}`, 14 `InlineData` (confirm `SMS-SEND-closed_sms_send` cho `sms.send`, `SMS-DELETE-closed_sms_delete` cho `sms.delete`, `null` cho các row khác) → `DENIED_UNTIL_ACTION_CONTRACT`; `FindPlan` null.
   - Fact mới `Device_action_grants_open_no_command_whatever_the_code`: thiết bị mới active trong Child; `targetId` mới (`SeedIdentityUserAsync(targetId, ChildOrganizationId)`, như :926-927); admin là một user mới khác (không dùng `ScopedUserId`: có deny dùng chung, :925) với claim `org-scope` Child, `perm devices.manage`, `perm permissions.manage` (như :1071-1074), CSRF như :923-937:
     - `POST /api/v1/permissions/device-actions` cho `targetId` với `gpio.do_write`, `gpio.do_force`, `sys.reboot` → 200; `PUT /api/v1/devices/{id}/permissions` grant `gpio.do_write` (không `expectedPolicyVersion`) → 200 (nếu endpoint từ chối: assert mã từ chối, ghi vào report).
     - Client của `targetId` (claim `org-scope` Child): `/api/v1/me/permissions` chứa `device:{id:N}:action:gpio.do_write` (grant có thật); `allowed-commands` trả `[]`; gửi `control.set_output` `{channel:1,value:true}` → `DEVICE_PERMISSION_REQUIRED`, `FindPlan` null.
     - Comment: device-action grant không bao giờ cho phép gửi lệnh; quyền lệnh chỉ đến từ role membership.
   - Không thêm test "danh tính catalog cũ → `CATALOG_STALE`": đã có `Stale_client_catalog_is_rejected_before_transport` (`CommandBoundaryTests.cs:257`) và `ManagementApiTests.cs:923`; một test hard-code hash cũ sẽ không còn ý nghĩa sau lần ghim kế.
   - Đỏ vì: 94 action; 18 high-risk; `infoViewer` vẫn mở `status.tags` (9 action); 14 row → `COMMAND_NOT_IN_CATALOG`. Test device-action grant xanh ngay (khoá hành vi có sẵn).
3. **Ghim lại spec:** hằng → `c80797d…`; chạy
   `node scripts/generate-command-catalog.mjs --idf D:/WORKSPACE/PROJECT/HMS-IDF --accept-stated-count 98/12`.
   Mong: đúng 1 dòng `warning:` (`spec §11 states 98 pairs and 12 namespaces, which disagrees with its action table; accepted by --accept-stated-count 98/12`), summary `108 commands (… R58 … W18 … C17 … D4 … B7 … F4)`, `6 config domains, 53 permission codes`, `catalog_hash 658e191a…`, blob `84ff812f…`, sha256 `8cde6ab4…`. Ghi nguyên output vào report.
   Rồi: `--check --accept-stated-count 98/12` → up to date; không cờ → exit 1 (`states 98 pairs, which disagrees …`; output không chứa `108`); `--accept-stated-count 100/13` và `108/13` → exit 1 (`does not match the count §11 states (98/12)`).
4. **Kiểm độc lập** (script có commit từ phase 1, không dùng code bộ sinh):
   `node scripts/verify-command-catalog-repin.mjs --idf D:/WORKSPACE/PROJECT/HMS-IDF --rev c80797d9d5b817174cd2cfbcb6ee62effe503ff2 --base 83e93d77e4a433efce665d669b567b3acc51b735 --expect-pair-set 4286c13cd55ef43ca344ba98d07f7e99b98f111872d408d2afb97ec6017c2b07`
   → 108 row, 13 namespace; thêm đúng 14 khoá của bảng Sự thật spec; đổi đúng `status.tags` (`required_perm` `sys.info_view` → `tag.view`); xoá 0; 0 lệch với `command-catalog.json` mới; exit 0. Ghi nguyên output vào report. So thêm top-level JSON với `git show HEAD:…/command-catalog.json`: đổi đúng 5 field metadata, `config_domains` bằng nhau.
5. **Pair-set:** chạy `CatalogTests` → `Pinned_catalog_pairs_match_spec_manifest` báo giá trị thật ở assertion pair-set; phải bằng `4286c13c…` (bước 4). Khác: dừng, không ghim.
6. **Ghim:** `ManifestPairSetSha256` (:24) = `4286c13c…`; `PinnedCatalogHash` (:30) = `658e191a…`, chỉ khi bốn nguồn trùng nhau: summary bước 3, JSON, `new PinnedCommandCatalog().Metadata.CatalogHash` (loader tính lại; lệch thì `COMMAND_CATALOG_HASH_MISMATCH`), giá trị kỳ vọng của plan; và bước 4 báo 0 lệch. Comment :26-29 giữ nguyên.
7. **High-risk + comment:** sửa `DevicePermissionCodes.HighRisk` và comment (sau bước 3: `High_risk_set_is_a_subset_of_firmware_vocabulary` cần mã có trong catalog); sửa comment `PinnedCommandCatalog.cs:44-49`.
8. **Xanh:**
   - Unit: 658 + 15 (Theory row) + 3 (Fact catalog) + 6 (`IsHighRisk`) + 4 (Theory role) = dự kiến **686**; integration: 127 + 1 + 14 + 1 = dự kiến **143**. Ghi số thật (Theory đếm theo ca); không ít hơn mốc.
   - Node 2 file vẫn 31.
   - `dotnet build Hms.Server.slnx`: không cảnh báo mới.
   - `git diff --stat` chỉ gồm file ở Related Code Files; `git diff src/Hms.Server.Infrastructure/Commands/PinnedCommandCatalog.cs` chỉ đổi dòng comment.

## Success Criteria

- [ ] `command-catalog.json` do bộ sinh viết từ object git `c80797d`; script độc lập: 14 thêm / 1 đổi / 0 xoá, 0 lệch; `source_blob` = `ls-tree c80797d`.
- [ ] `CatalogTests` ghim commit, blob, sha256, `catalog_hash 658e191a…`, pair-set `4286c13c…`, mỗi giá trị có hai nguồn độc lập.
- [ ] Test 34 action mở xanh; so với ghim cũ chỉ đổi dòng `status.tags`; `sys.info_view` mở đúng 8 action.
- [ ] `status.tags` cần `tag.view`, vẫn mở; người có `tag.view` qua mọi cổng trước khi ký (`FindPlan` khác null, code chính xác); 14 row đóng kể cả master-admin; `allowed-commands` master-admin = 34.
- [ ] 4 mã high-risk mới: admin thường không tạo/sửa/xoá role, không gán/gỡ membership liên quan; master-admin tạo được; mã cũ không đổi hành vi.
- [ ] Device-action grant không mở lệnh nào.
- [ ] Unit/integration xanh, số ≥ mốc + mới; build không cảnh báo mới.

## Risk Assessment

| Rủi ro | Khả năng × Ảnh hưởng | Giảm thiểu |
|---|---|---|
| Ghim hash chép từ output mà không xem nội dung | Thấp × Cao | Bước 4 (script độc lập, so từng field), bước 5-6 (bốn nguồn trùng, giá trị kỳ vọng của plan) |
| HMS-IDF có replace ref hoặc thay đổi chưa commit | Thấp × Cao | `--no-replace-objects`, env sạch (phase 1, có test); bước 0 kiểm `refs/replace` rỗng; chỉ đọc object |
| Commit ghim không còn trên remote | Trung bình × Trung bình | Bước 0 ghi `branch -r --contains`, dừng nếu rỗng; follow-up (a) của plan |
| Đổi `HighRisk` trước khi sinh catalog làm test subset đỏ giả | Thấp × Thấp | Thứ tự bước 3 → 7 |
| Sửa nhầm kỳ vọng TTL thành đổi hành vi handler | Thấp × Trung bình | Chỉ đổi test; kỳ vọng chép đúng `SendDeviceCommandHandler.cs:143-145` |
| `sms.send_status`, `sms.delete` báo high-risk làm UI đổi | Thấp × Thấp | `highRisk` không xuất hiện trong `frontend/src`; row đóng |
| Test integration để lại reservation trong host dùng chung | Thấp × Thấp | Thiết bị và correlation id riêng mỗi test |
| Revert sau khi đã có grant mã mới | Thấp × Cao | Thủ tục Rollback ở `plan.md` (SQL trước/sau, master-admin gỡ trước) |

Rollback: theo mục Rollback của `plan.md` (không chỉ revert code).

## As built

Done. Generator output, independent check, red/green evidence and counts: `reports/phase-02-implementation-report.md`.

- `catalog_hash` `658e191a...`, pair-set `4286c13c...`, 108 rows / 13 namespaces, 14 added / 1 changed / 0 removed, 0 mismatches.
- Unit 658 -> 686, integration 127 -> 143, node 32 pass, build warnings unchanged (3).
- Deviation: xUnit `Assert.Contains` on frozen/read-only sets is ambiguous; tests use a cast or `Assert.True/False(set.Contains(..))`.
