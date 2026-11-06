# Role được quyền ghi cấu hình ngay khi mở 5 lệnh

Ngày: 2026-11-06.

## Mã quyền bị ảnh hưởng

Từ trước đến nay, các mã quyền dưới đây không mở được lệnh nào. Từ plan này, role nào đang giữ chúng sẽ ghi được
ngay. Không mã nào thuộc `DevicePermissionCodes.HighRisk`, nên admin không phải master-admin cũng gán được (nếu họ
đang có mã đó).

| Mã quyền | Lệnh mở thêm | Lệnh vẫn đóng dùng chung mã |
|---|---|---|
| `sys.hostname_edit` | `system.set_hostname` | — |
| `sys.time_edit` | `system.set_timezone`, `system.sync_ntp` | `system.set_time`, `system.set_ntp_servers` (đóng nhờ catalog, không nhờ quyền) |
| `adc.config_edit` | `adc.set_channel` | — |
| `adc.enable` | `adc.toggle_channel` | — |

## Role thật đang giữ các mã này

### Môi trường dev (lab)

**Không có role nào.**
- User-secrets của API dev không có chuỗi kết nối `Postgres`, nên API dùng DB in-memory (`DependencyInjection.cs`, nhánh `UseInMemoryDatabase`). Mọi role mất khi API khởi động lại.
- Không có seed device role: `DeviceRole.Create` chỉ được gọi trong các handler của `DeviceAccessHandlers`.
- Chưa có endpoint HTTP nào để tạo hay liệt kê device role. Không role nào có thể đã được gán qua UI.

### Môi trường có Postgres

Chạy truy vấn chỉ đọc dưới đây trước khi triển khai, rồi ghi kết quả (tên role, tổ chức, số membership) vào báo cáo
triển khai. Không ghi email người dùng.

```sql
SELECT r."Name" AS role, r."OrganizationId" AS organization,
       string_agg(DISTINCT p."Permission", ', ' ORDER BY p."Permission") AS codes,
       count(DISTINCT m."Id") AS memberships
FROM device_roles r
JOIN device_role_permissions p ON p."RoleId" = r."Id"
LEFT JOIN device_memberships m ON m."RoleId" = r."Id"
WHERE p."Permission" IN ('sys.hostname_edit', 'sys.time_edit', 'adc.config_edit', 'adc.enable')
GROUP BY r."Id", r."Name", r."OrganizationId"
ORDER BY memberships DESC;
```

Master-admin luôn có mọi quyền, không cần role.
