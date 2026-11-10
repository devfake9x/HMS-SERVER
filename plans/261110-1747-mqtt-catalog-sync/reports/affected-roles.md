# Báo cáo role bị ảnh hưởng bởi đồng bộ catalog `c80797d`

Ngày: 2026-11-10. Plan: `plans/261110-1747-mqtt-catalog-sync/`. Phase 3, báo cáo chỉ đọc, không đổi code.

## 1. Ai mất gì

| Đối tượng | Thay đổi | Ghi chú |
|---|---|---|
| Role có `sys.info_view` nhưng không có `tag.view` | Mất `status.tags`: gửi lệnh trả `DEVICE_PERMISSION_REQUIRED`, không giữ chỗ audit; không còn trong `allowed-commands` | Đây là thay đổi quyền duy nhất đối với role hiện có |
| Role có `tag.view` | Không mất gì; `status.tags` nay cần đúng `tag.view` | |
| Role có `sys.info_view` | Mở đúng 8 action: `system.get_info`, `system.get_heap`, `system.ping`, `system.get_device_id`, `system.get_operations`, `ota.get_status`, `status.dump`, `status.services` | `config.list_domains` cũng dùng `sys.info_view` nhưng vẫn đóng |
| Master-admin | Không mất gì | Bỏ qua kiểm quyền thiết bị (`DeviceAccessPolicy.cs:37-38`) |
| Reply đã lưu của `status.tags` | Không còn nếu thiết bị khởi động lại | Store reply là in-memory (`DependencyInjection.cs:42`), mất với mọi người và mọi lệnh khi API khởi động lại; không phải hiệu ứng của quyền |

Không có mã mới nào được cấp tự động. Migration không gán quyền.

## 2. Hiện trạng theo code

- **Dev:** không có device role nào ngoài master-admin. Seed phát triển (`src/Hms.Server.Api/Program.cs:58-87`, `Development:AutoSeed`) chỉ tạo user `admin@hms.local` và role master-admin; không có đoạn nào tạo device role, device membership hay `device_role_permissions`. Không có Postgres thì role và membership là in-memory, rỗng khi khởi động (`DependencyInjection.cs:106-117`).
- **Production:** repo không chứa dữ liệu production. Chưa thể nói từ code có role nào giữ `sys.info_view` mà thiếu `tag.view`; cần chạy SQL A và SQL B (mục 3 và 6) trên DB production.
- **Lưu trữ khi có Postgres:** `device_roles` (`Id`, `OrganizationId`, `Name`), `device_role_permissions` (`RoleId`, `Permission`), `device_memberships` (`UserId`, `DeviceId`, `RoleId`, `GrantedBy`, `GrantedAt`). Migration `src/Hms.Server.Infrastructure/Persistence/Migrations/20261005143949_AddDeviceRolesAndMemberships.cs:34-98`; mapping `ApplicationDbContext.cs:178, 199, 206`.
- **Không có HTTP tạo role hay gán membership:** `CreateDeviceRoleHandler` và `AssignDeviceMembershipHandler` chỉ được đăng ký trong DI (`DependencyInjection.cs:124-128`), không có controller gọi. Ghi role chỉ qua handler hoặc trực tiếp vào DB/store.
- **Lệnh gửi kiểm quyền:** chỉ quyền của role membership (`DeviceAccessResolver.cs:23-35`); role của tổ chức khác không cấp gì (`DeviceAccessResolver.cs:24-26`).

## 3. SQL A (chỉ đọc): role mất quyền đọc tag

Người chạy: user, trên DB của môi trường cần kiểm. Agent không đọc connection string hay user-secrets.

```sql
SELECT r."Id", r."OrganizationId", r."Name",
       (SELECT count(*) FROM device_memberships m WHERE m."RoleId" = r."Id") AS members
FROM device_roles r
WHERE EXISTS (SELECT 1 FROM device_role_permissions p WHERE p."RoleId" = r."Id" AND p."Permission" = 'sys.info_view')
  AND NOT EXISTS (SELECT 1 FROM device_role_permissions p WHERE p."RoleId" = r."Id" AND p."Permission" = 'tag.view');
```

## 4. Cách xử lý

- Kết quả SQL A khác rỗng: master-admin thêm `tag.view` vào role đó, hoặc admin quản lý quyền đã giữ `tag.view` làm việc này. `tag.view` không phải mã high-risk, nên không cần master-admin để thêm nó.
- Không viết migration tự cấp quyền.
- Với role có `members > 0`, báo người giữ role trước khi sửa.

## 5. High-risk chỉ kiểm lúc ghi grant

- `DevicePermissionCodes.HighRisk` có 13 mã: `sys.reboot`, `sys.factory_reset`, `sys.ota`, `mqtt.config_edit`, `eth.edit`, `net.priority_edit`, `wifi.sta_edit`, `wifi.sta_connect`, `gpio.do_write`, `gpio.do_force`, `gpio.do_config`, `sms.send`, `sms.delete` (`src/Hms.Server.Domain/Authorization/DevicePermissionCodes.cs:23-35`). Bốn mã mới ở dòng cuối là `gpio.do_force`, `gpio.do_config`, `sms.send`, `sms.delete`.
- Kiểm chỉ xảy ra lúc **ghi grant**: tạo role (`DeviceAccessHandlers.cs:57`), sửa role (:106), xoá role (:166), gán membership và gỡ membership qua `DelegationRules.CheckRoleChange` (:225, :289, :315-327). Lỗi `HIGH_RISK_REQUIRES_MASTER_ADMIN` (`DeviceAccessHandlers.cs:26`).
- **Lúc gửi** chỉ có luật class F (`SendDeviceCommandHandler.cs:123-127`). Lúc gửi không xét mã high-risk trong role.
- **Grant đã có không bị xét lại.** Đổi phân loại high-risk về sau, hoặc rollback, không thu hồi grant nào.
- Bốn mã mới chưa có trong từ vựng trước lần ghim này, nên hiện không role nào chứa chúng (theo code; kiểm bằng SQL B để xác nhận trên DB).

## 6. SQL B (chỉ đọc): ai đang giữ một tập mã

Thay danh sách `IN` theo tập mã cần kiểm.

```sql
SELECT r."Id" AS role_id, r."OrganizationId", r."Name", p."Permission",
       m."UserId", m."DeviceId", m."GrantedBy", m."GrantedAt"
FROM device_role_permissions p
JOIN device_roles r ON r."Id" = p."RoleId"
LEFT JOIN device_memberships m ON m."RoleId" = r."Id"
WHERE p."Permission" IN ('gpio.do_force', 'gpio.do_config', 'sms.send', 'sms.delete')
ORDER BY r."Id", m."UserId";
```

Dùng SQL B khi:
- trước mọi lần đổi phân loại high-risk (thêm hoặc bớt mã);
- trước rollback (với 7 mã mới, mục 7).

## 7. Thủ tục rollback

Khớp mục Rollback của `plan.md`.

1. **Trước khi revert:** chạy SQL B với 7 mã mới: `tag.view`, `mb_rtu.slave_view`, `mb_rtu.tag_view`, `sms.send`, `sms.delete`, `gpio.do_force`, `gpio.do_config`. Nếu có kết quả, master-admin gỡ mã khỏi role hoặc xoá role trước khi revert.
2. **Revert** một commit (hằng số `SOURCE_COMMIT`, JSON, comment, `HighRisk`, test) về `83e93d77e`. Tab mang hash mới gặp `CATALOG_STALE` và phải tải lại. Commit `83e93d77e` phải còn truy cập được (cũng chỉ nằm trên nhánh feature).
3. **Khi ghim lại:** chạy SQL B với 13 mã high-risk, xem `GrantedBy` và `GrantedAt`. Master-admin rà các grant tạo trong cửa sổ revert.
4. **Mã lạ còn trong role sau revert** không khớp quyền nào của catalog cũ; lần sửa role sau đó trả `DEVICE_PERMISSION_UNKNOWN` cho tới khi bỏ mã đó.

Cửa sổ rollback này chỉ mở qua ghi thẳng vào DB hoặc store, vì hôm nay chưa có HTTP endpoint cho role hay membership.

## 8. Kênh device-action grant

- `POST /api/v1/permissions/device-actions` và `PUT /api/v1/devices/{id}/permissions` nhận chuỗi bất kỳ, kể cả mã high-risk, từ người có `permissions.manage` (`PermissionContracts.cs:220-229`; `GrantDeviceActionHandler`).
- Kênh này **không mở lệnh nào**: `DeviceAccessResolver.cs:23-35` chỉ đọc role membership. Integration test phase 2 khoá điều này: grant `gpio.do_write`, `gpio.do_force`, `sys.reboot` qua HTTP không thêm gì vào `allowed-commands`, và gửi `control.set_output` vẫn trả `DEVICE_PERMISSION_REQUIRED`.
- Nếu sau này nối kênh này vào phân quyền lệnh, phải thêm kiểm từ vựng và kiểm high-risk trước.
- Kênh này không kiểm từ vựng và không kiểm high-risk khi ghi. Đây là rủi ro đã biết, ghi trong plan (Rủi ro: "Device-action grant về sau bị nối vào phân quyền lệnh").
