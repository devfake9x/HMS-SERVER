# Ai reboot được

Ngày: 2026-11-10.

## Quy tắc sau plan này

`system.reboot` là class F. Server chỉ cho **master-admin** gửi (kiểm lúc gửi, `HIGH_RISK_REQUIRES_MASTER_ADMIN`, trước khi ký và trước khi giữ chỗ audit). `allowed-commands` chỉ liệt kê `system.reboot` cho master-admin.

Hệ quả: mã quyền `sys.reboot` (có trong `DevicePermissionCodes.HighRisk`) **không** cho ai reboot được. Role hay quyền nào đang giữ `sys.reboot` không reboot được sau plan này; danh sách đó chỉ có ý nghĩa nếu sau này nới quy tắc (cần quyết định riêng). Admin không phải master-admin vẫn không gán được `sys.reboot` (`HIGH_RISK_REQUIRES_MASTER_ADMIN`, có sẵn).

## Người reboot được

### Môi trường dev (lab)

Truy vấn qua API dev (`GET /api/v1/users`, `GET /api/v1/permissions/{userId}`), 2026-11-10:

| | Số lượng |
|---|---|
| Tài khoản master-admin (reboot được) | 1 (tài khoản seed của dev) |
| Tài khoản khác | 0 |
| Cấp quyền trực tiếp (feature, resource-scope, device-action) của tài khoản đó | 0 |
| Role cùng tổ chức đang giữ `sys.reboot` | 0 |

- Dev dùng DB in-memory (`DependencyInjection.cs`, nhánh `UseInMemoryDatabase`; user-secrets không có chuỗi kết nối Postgres), nên mọi dữ liệu mất khi API khởi động lại và chỉ còn tài khoản seed.
- Chưa có endpoint HTTP tạo hay liệt kê device role (handler chỉ đăng ký ở `DependencyInjection.cs`), nên không role nào có thể đã được gán qua UI.
- Không in email hay tên đăng nhập.

### Môi trường có Postgres

Chạy truy vấn chỉ đọc trên mọi môi trường có Postgres trước khi triển khai, rồi ghi kết quả (số tài khoản, tên role, tổ chức, số membership; không email). Phần 1 cho biết ai reboot được; phần 2 liệt kê role đang giữ `sys.reboot` (không reboot được, chỉ để biết nếu sau này nới quy tắc).

```sql
-- 1. Master-admin: những người reboot được
SELECT count(*) AS master_admins
FROM "AspNetUserRoles" ur
JOIN "AspNetRoles" r ON r."Id" = ur."RoleId"
WHERE r."Name" = 'master-admin';

-- 2. Role giữ sys.reboot (không reboot được sau plan này)
SELECT r."Name" AS role, r."OrganizationId" AS organization,
       count(DISTINCT m."Id") AS memberships
FROM device_roles r
JOIN device_role_permissions p ON p."RoleId" = r."Id"
LEFT JOIN device_memberships m ON m."RoleId" = r."Id"
WHERE p."Permission" = 'sys.reboot'
GROUP BY r."Id", r."Name", r."OrganizationId"
ORDER BY memberships DESC;
```

Tên bảng ASP.NET Identity ở mục 1 là mặc định; kiểm lại theo migration nếu schema đã đổi tên. Chưa chạy được ở đây (không có Postgres).
