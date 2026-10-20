---
title: "Brainstorm — Phân quyền lệnh thiết bị theo what + where"
date: 2026-10-04
status: approved-decisions
scope: HMS-SERVER (backend + frontend); hợp đồng thiết bị lấy từ HMS-IDF
device_contract: HMS-IDF refactor-all-upate-v6 @ 83e93d77e (engineering-docs/api/mqtt-api.md §0–§7, §11)
reference: concrete-stress-monitoring/docs/architecture-thiet-ke-phan-quyen.md (chỉ tham khảo)
modes: markdown
---

# Brainstorm — Phân quyền lệnh thiết bị theo what + where

## 1. Vấn đề

- Thiết bị **không tự kiểm quyền**: ai có command key là có toàn quyền (mqtt-api.md R11). Server là cửa duy nhất.
- Server chưa có mô hình quyền cho lệnh: catalog gán 1 quyền chung `device.command` cho cả 94 action, mọi action đang `DeniedUntilActionContract` (`src/Hms.Server.Infrastructure/Commands/PinnedCommandCatalog.cs:36-43`).
- Yêu cầu: tính năng (quyền) phân theo **từng thiết bị** — cần **what + where**.
- Phải xong trước khi bật transport MQTT.

## 2. Hiện trạng (scout)

| Có sẵn | Vị trí |
|---|---|
| 3 lớp: Feature (global), Org scope, `DeviceActionGrant(user, device, action)` + deny + `PolicyVersion` | `Domain/Authorization/AuthorizationPolicy.cs`, `Application/Authorization/PermissionContracts.cs` |
| Bảng chung `permission_grants(Kind, Permission, OrganizationId, DeviceId, Action, Deny, PolicyVersion)` | `Infrastructure/Persistence/PersistenceEntities.cs:81` |
| Ma trận 4 quyền Datacake theo thiết bị: `view`, `edit_basics`, `edit_product`, `record_measurements` | `Api/Controllers/DevicePermissionsController.cs:24-30` |
| Epoch theo thiết bị, refresh principal realtime, SignalR subscription registry | `Application/Realtime/*`, `Api/Hubs/*` |
| Vai trò tài khoản duy nhất: `master-admin`; còn lại là feature permission (`users.manage`, `permissions.manage`, `devices.manage`) | `RoleAuthorizationPolicy.cs:6` |
| Tổ chức phân cấp (ParentId), `OrganizationScopeResolver.Expand` mở rộng xuống org con | `OrganizationsController.cs`, `OrganizationScopeResolver.cs` |

Thiết bị (manifest §11, @83e93d77e): 94 action → **46 chuỗi quyền** (44 trên dòng lệnh + `sys.config_export`, `sys.config_import` qua domain `system`; đính chính 2026-10-05, bản đầu ghi 45); class R 49 / W 18 / C 17 / D 3 / B 3 (DO) / F 4 (reboot, factory_reset, ota.remote_start, ota.apply). `config.get/set` lấy quyền theo domain (6 domain).

### Lỗi đang có (phải sửa trong đợt này)

1. **Grant khoá theo tên action trần** — `get_status` có ở 6 namespace (adc, battery, control, mqtt, ota, sms), `get_config` ở 2. Cấp `get_status` mở luôn `sms.get_status`. (`SendDeviceCommandHandler.cs:79-83`)
2. **Bỏ tích rồi tích lại → chặn vĩnh viễn** — store chỉ append (`EfPermissionStore.cs:84-97`); UI lấy bản mới nhất (granted) nhưng lệnh gom mọi deny từng có (`DeviceCommandsController.cs:95-99`).
3. **Tự nâng quyền** — ai có `devices.manage` cấp được action bất kỳ kể cả `*`, kể cả cho chính mình (`DevicePermissionsController.cs:60-87`).
4. Allow từ cookie claims (`perm`, `device-action`) cộng vào → thu hồi trong DB chỉ hiệu lực khi refresh phiên (nhẹ).

## 3. Tham khảo concrete-stress-monitoring

| Lấy | Bỏ (lý do) |
|---|---|
| What ∧ Where trực giao | Bitmask `AccessFlags` — bản cuối của chính hệ đó đã bỏ khỏi trục 2, frontend vẫn còn → lệch |
| Membership + vai trò, 1 trạng thái hiện hành, FK + CHECK, không bảng grant đa hình | Denormalized tree / LTREE / Redis đa pod — quy mô 500k tín hiệu; HMS vài chục–vài trăm thiết bị |
| Ràng buộc tập con khi uỷ quyền | `isSuperAdmin` coi role `admin` thường là super admin — rủi ro |
| Thu hồi tức thì (epoch + ngắt SignalR) — HMS đã có epoch/PolicyVersion | "Không cấp theo lá" — với HMS, thiết bị là đơn vị chấp hành, cấp theo thiết bị là đúng |
| 404 khi ngoài phạm vi; `can()` fail-closed; catalog mã quyền có kiểu | |

## 4. Quyết định đã chốt (chủ dự án, 2026-10-04)

| # | Quyết định |
|---|---|
| Q1 | Hợp đồng thiết bị: bản mới nhất nhánh `refactor-all-upate-v6` (HEAD `83e93d77e`) |
| Q2 | **Không có vai trò cố định.** Admin tự tạo vai trò theo nhu cầu; hệ thống không seed vai trò |
| Q3 | Thư viện vai trò **theo từng tổ chức** |
| Q4 | Admin công ty mẹ (master-admin) toàn quyền; admin công ty con chỉ cấp cho member **tập con** quyền mình đang có |
| Q5 | Where = **chỉ thiết bị** ở v1; schema chừa chỗ thêm Area/Project |
| Q6 | Ngoài phạm vi org → **404**; trong phạm vi nhưng thiếu quyền → 403 |
| Q7 | Quyền **rủi ro cao** chỉ master-admin được đưa vào vai trò / gán: class F + nhóm làm mất kết nối (`mqtt.config_edit`, `eth.edit`, `net.priority_edit`, `wifi.sta_edit`, `wifi.sta_connect`) |

## 5. Thiết kế

### 5.1 Quy tắc quyết định

```
cho_phép(user, device, lệnh) ⇔
    master-admin
  ∨ ( (device.org ∈ phạm_vi_org(user) ∨ có membership(user, device))   -- sai → 404
    ∧ device chưa revoked
    ∧ quyền_cần(type, action, data.domain) ∈ quyền(role của membership(user, device))   -- sai → 403
    ∧ (class F ⇒ confirm token đúng ở request) )
```

- `quyền_cần` tra từ catalog; không tra được (domain lạ, cặp lạ) → từ chối (fail-closed).
- Không còn wildcard `*` cho device action (trừ bypass master-admin).

### 5.2 What — catalog quyền

- Nguồn duy nhất: catalog ghim từ `components/command-dispatcher/cmd_descriptor.c` @ `83e93d77e`; mở rộng `command-catalog.json` thêm `permission`, `class`, `ttl_ms`, `confirm`, `dry_run`, `risk` (`high` cho Q7); bảng domain cho `config.get/set`; hash tính lại.
- Thêm 4 quyền thiết bị phía server: `device.view`, `device.edit_basics`, `device.edit_product`, `device.record_measurements`.
- Test: catalog khớp manifest (94 cặp, 46 quyền firmware gồm cả quyền qua domain; cộng 4 quyền server = 50), mọi action có `permission` hoặc domain policy.
- Kế hoạch triển khai: `plans/261005-0556-device-permissions-what-where/` (hợp đồng thiết kế ở `reports/design-contract.md` là bản chuẩn khi khác báo cáo này).

### 5.3 Vai trò theo tổ chức

```
device_roles(id, organization_id FK, name, description, created_by, updated_at, version)
    UNIQUE(organization_id, name)
device_role_permissions(role_id FK ON DELETE CASCADE, permission_code)
    UNIQUE(role_id, permission_code); permission_code ∈ catalog (kiểm ở app)
```

- Không seed. Master-admin tạo/sửa ở mọi org. Admin org con (có `permissions.manage` trong phạm vi) tạo/sửa vai trò của org mình.
- Quyền rủi ro cao: chỉ master-admin thêm/bỏ khỏi vai trò; vai trò chứa quyền rủi ro cao chỉ master-admin sửa.
- Sửa vai trò → tăng `PolicyVersion` + epoch mọi thiết bị đang gán vai trò đó → ngắt SignalR liên quan.

### 5.4 Where — membership theo thiết bị

```
device_memberships(id, user_id FK, device_id FK, role_id FK, granted_by, granted_at)
    UNIQUE(user_id, device_id)
    -- v2: thêm area_id / project_id + scope_type CHECK (theo mẫu ProjectMember)
```

- 1 user × 1 thiết bị = 1 vai trò hiện hành (upsert); lịch sử ghi audit → sửa lỗi 2.
- Thay `permission_grants` loại `device-action`. Feature/resource grant giữ nguyên.
- Vai trò gán phải thuộc org sở hữu thiết bị (v1; xem câu hỏi mở 1).

### 5.5 Uỷ quyền (chống nâng quyền)

| Người làm | Được |
|---|---|
| master-admin | Mọi thứ |
| Admin org con (`permissions.manage`, thiết bị trong phạm vi) | Gán vai trò R cho user U trên thiết bị D chỉ khi `quyền(R) ⊆ quyền(admin, D)` và R không chứa quyền rủi ro cao |
| Admin org con sửa vai trò R | Quyền mới phải ⊆ quyền admin trên **mọi** thiết bị đang gán R; không đụng quyền rủi ro cao |
| Có `devices.manage` | Không còn quyền phân quyền (sửa lỗi 3) |

### 5.6 Điểm chặn

| Kênh | Quyền cần |
|---|---|
| Danh sách/chi tiết thiết bị | `device.view` trên thiết bị (admin quản lý thấy toàn bộ trong phạm vi) — xem câu hỏi mở 2 |
| GET telemetry | `device.view` |
| SignalR subscribe | `device.view`; thu hồi → epoch + ngắt |
| Gửi lệnh | `quyền_cần` từ catalog; F → confirm |
| Phân quyền / vai trò | §5.5 |
| POST telemetry (ingest) | Định danh dịch vụ, không phải user (việc riêng, đã báo trong review) |

### 5.7 UI

- Trang org: **Vai trò thiết bị** — danh sách; form tạo/sửa chọn quyền gom theo phân hệ (DO, ADC, Hệ thống, Mạng, Wi-Fi, MQTT, Modbus, SMS, OTA, Broker, Pin), badge class, quyền rủi ro cao khoá với người không phải master-admin; nút "nhân bản vai trò".
- Tab Permissions thiết bị: mỗi user một dropdown vai trò; vai trò vượt quyền người cấp bị disable kèm lý do; xem quyền mở rộng (chỉ đọc).
- `GET /api/v1/devices/{id}/my-permissions` → frontend `can(device, code)` fail-closed; nút lệnh ẩn/khoá theo quyền.

## 6. Rủi ro

| Rủi ro | Giảm thiểu |
|---|---|
| Không có vai trò mặc định → org mới chưa dùng được | Master-admin tạo; nút nhân bản vai trò giữa org (chỉ master-admin) |
| Sửa vai trò ảnh hưởng hàng loạt | Hiện số thiết bị/user bị ảnh hưởng trước khi lưu; ghi audit |
| Catalog lệch firmware | Ghim + hash; test khớp manifest; (sau) so với topic `capabilities` lúc chạy, lệch → fail-closed |
| Master-admin là điểm duy nhất cho quyền rủi ro cao | Chấp nhận (Q7); ghi audit mọi thao tác |
| Đổi 403 → 404 phá test hiện có | Cập nhật test có chủ đích trong cùng phase |
| Dữ liệu grant `device-action` cũ không map được sang vai trò | Chỉ là dữ liệu dev; bỏ, ghi rõ trong migration |

## 7. Tiêu chí chấp nhận

- [ ] 94 action đều có `permission`; `config.get/set` tra theo domain; domain lạ → từ chối.
- [ ] Cấp vai trò chỉ có `adc.view` → gọi `sms.get_status` bị 403 (hồi quy lỗi 1).
- [ ] Bỏ vai trò rồi gán lại → lệnh chạy lại được (hồi quy lỗi 2).
- [ ] User chỉ có `devices.manage` không cấp được quyền nào; admin org con không gán được vai trò vượt quyền mình, không gán được vai trò có quyền rủi ro cao (hồi quy lỗi 3).
- [ ] Thiết bị ngoài phạm vi org → 404 ở chi tiết, telemetry, permissions, commands.
- [ ] Thu hồi membership → SignalR thiết bị đó bị ngắt; lệnh tiếp theo 403.
- [ ] Lệnh class F thiếu/sai confirm → từ chối trước khi tới transport.
- [ ] Toàn bộ test backend/frontend hiện có xanh (trừ test đổi 403→404 có chủ đích).

## 8. Hướng tiếp theo (thứ tự)

1. **Phân quyền what + where** (báo cáo này) — catalog, vai trò theo org, membership, uỷ quyền, điểm chặn, UI.
2. **Transport MQTT** theo mqtt-api.md §0–§5: ký HMAC, subscribe `cmd/res` trước, verify `res:`, `operation_id`, timeout 30 s, kiểm cỡ. Cần chốt nguồn command key cho RTU 870c. Viết lại `plans/261001-0030-full-hardware-mqtt-apis-c2/phase-02-live-mqtt-transport.md` (envelope cũ sai).
3. **UI điều khiển** theo thiết bị, gate bằng `can()`.
4. **HIL** trên RTU thật qua server.

Dọn song song: gỡ status MQTT giả (`CommandTransportController.cs`), bỏ mật khẩu cứng trong `tools/mqtt-broker/hms-telemetry-bridge.js`, ingest telemetry bằng định danh dịch vụ.

## 9. Quyết định bổ sung và câu hỏi còn mở

- **Q8 (chốt):** vai trò gán trên thiết bị phải thuộc **chính org sở hữu thiết bị**; vai trò org mẹ không dùng cho org con; master-admin nhân bản khi cần.
- **Q9 (chốt):** member chỉ thấy thiết bị có membership với `device.view`; admin quản lý thấy toàn bộ trong phạm vi. Bỏ "view kế thừa cho mọi member org" của ma trận Datacake hiện tại.
- **Q10 (chốt):** lập plan bằng `/ck:plan --tdd`.
- **Còn mở:** nguồn command key cho RTU 870c (nạp khoá server sinh vào thiết bị, hay nhập khoá hiện có vào server) — chốt trước bước 2 (transport).
