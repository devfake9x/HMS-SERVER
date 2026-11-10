---
phase: 3
title: "Tài liệu, báo cáo role, diff §6 của row mở, hồi quy frontend"
status: completed
priority: P2
effort: 2h
dependencies: [2]
---

# Phase 3: Tài liệu, báo cáo role, diff §6, hồi quy frontend

## Overview

Không đổi code. Phase này:
1. chạy hồi quy frontend (không có fixture hay code frontend cần đổi);
2. viết `reports/affected-roles.md` (role mất quyền đọc tag, high-risk chỉ kiểm lúc grant, SQL, kênh device-action grant);
3. viết `reports/open-actions-contract-diff.md` (§6 của 34 action mở giữa hai commit);
4. cập nhật docs, journal, câu hỏi spec 8 và 20, ghi chú plan C2, kết quả plan.

## Hiện trạng (đã đọc khi lập plan)

- **Frontend không cần đổi:** không có danh sách quyền firmware hard-code (`DevicePermissionsTab.vue:33-40` chỉ 4 mã server; `RolePermissionView.vue`, `PermissionGrantEditor.vue` lấy từ API); không đọc `status.tags`; `highRisk` không xuất hiện trong `frontend/src`; version/hash lấy từ server (`catalogVerification.ts`, hai bản giống hệt ở `frontend/src/` và `frontend/src/core/commands/`); `CATALOG_STALE` đã có xử lý và test (`App.vue:778-783`; `DeviceOutputsPanel.vue:162-165` + `DeviceOutputsPanel.test.ts:218-230`; `rebootPlan.ts:168` + `rebootPlan.test.ts:254`; `useDeviceWrite.test.ts:46`; `catalogVerification.test.ts:20`).
- **Role:** chưa có HTTP endpoint tạo/gán device role (handler chỉ đăng ký ở `DependencyInjection.cs:124-128`). Seed dev chỉ tạo `admin@hms.local` master-admin (`Program.cs:58-87`); master-admin bỏ qua kiểm quyền thiết bị (`DeviceAccessPolicy.cs:37-38`). Không Postgres → role in-memory, rỗng khi khởi động (`DependencyInjection.cs:106-117`). Có Postgres → `device_roles` (`Id`, `OrganizationId`, `Name`), `device_role_permissions` (`RoleId`, `Permission`), `device_memberships` (`UserId`, `DeviceId`, `RoleId`, `GrantedBy`, `GrantedAt`) (migration `20261005143949_AddDeviceRolesAndMemberships.cs:35-98`).
- **High-risk chỉ kiểm lúc ghi grant:** tạo role `DeviceAccessHandlers.cs:57`, sửa :106, xoá :166, gán/gỡ membership qua `CheckRoleChange` :225, :289, :315-327. Lúc gửi chỉ có luật class F (`SendDeviceCommandHandler.cs:123-127`). Grant đã có không bị xét lại.
- **Device-action grant:** HTTP `POST /api/v1/permissions/device-actions`, `PUT /api/v1/devices/{id}/permissions` nhận chuỗi bất kỳ (`PermissionContracts.cs:220-229`), hiện trong `/me/permissions` (`MeController.cs:30`), không dùng cho phân quyền lệnh (`DeviceAccessResolver.cs:23-35`; integration test phase 2). `canDeviceAction` ở `frontend/src/core/auth/permissions.ts` chưa có caller.
- **Khởi động lại:** store reply in-memory (`DependencyInjection.cs:42`, `DeviceLinkContracts.cs:435`), lane và theo dõi reboot in-memory (`docs/architecture-mqtt-device-link.md:546`, :675), `FailUnfinished("OUTCOME_UNKNOWN_AFTER_RESTART")` (`Program.cs:157-164`).
- **Spec (`c80797d`, chỉ `git show`):** R11 L415; R13 SMS L417 (+ L719); R14 `mqtt.set_config` L418; R15 force DO L419; R16 `control.set_config` L420; §11 L1359 ghi luật SMS/`mqtt.set_config` là "(R14)" (đúng ra SMS là R13); `status.tags` §6 L612 dạng trang, mặc định `limit=10` (L927-929); bảng "API đã đóng…" L47 và bảng C07 L2129: receipt `SLICE_PASS` dạng mảng là lịch sử. Red-team đã so dòng §6 đầu tiên của từng action mở: 7 action khác (`mqtt.get_status`, `mqtt.get_config`, `system.get_info`, `status.tags`, `control.get_status`, `network.get_modem`, `control.set_output`).
- **Commit ghim:** `c80797d` chỉ trên `feat/next-work-20261019`, `claude/status-tags-paging` (local + origin); không tag; không trong `main`/`develop`. `83e93d77e` cũng chỉ trên nhánh feature.
- **Docs cần sửa** (dòng lúc lập plan):
  - `docs/architecture-device-mqtt-api-catalog.md`: L6, L26 (số 94), L135-143 (hộp "Trạng thái mở"), L173 ("Catalog vẫn ghim `83e93d77e`"), L290 (bộ sinh @ `83e93d77e`, 94 action), L292 (từ vựng 50 = 46 + 4). L43, L82, L145 trở đi là bảng lịch sử: không sửa.
  - `docs/architecture-mqtt-device-link.md`: L67 (danh sách đóng), L123 (`status.tags` trong bảng live), L160 ("catalog vẫn ghim `83e93d77e`"), L287 ("98 cặp, bảng có 104; generator từ chối bản `18a63e8ed`").
  - `docs/project-roadmap.md`: L16 ("94 MQTT Actions"), bảng §5 L106-114, L116.

## Requirements

### `reports/affected-roles.md`
1. **Ai mất gì:** role có `sys.info_view` mà không có `tag.view` mất `status.tags` (gửi → `DEVICE_PERMISSION_REQUIRED`; không còn trong `allowed-commands`); `sys.info_view` nay mở đúng 8 action. Master-admin không mất gì. Reply đã lưu mất khi khởi động lại để deploy, với mọi người (store in-memory), không phải do quyền.
2. **Hiện trạng theo code** (mục Hiện trạng, có file:line): dev = 0 device role ngoài master-admin; production chưa có.
3. **SQL A, chỉ đọc** (user tự chạy; agent không đọc connection string hay user-secrets): role mất quyền đọc tag.
   ```sql
   SELECT r."Id", r."OrganizationId", r."Name",
          (SELECT count(*) FROM device_memberships m WHERE m."RoleId" = r."Id") AS members
   FROM device_roles r
   WHERE EXISTS (SELECT 1 FROM device_role_permissions p WHERE p."RoleId" = r."Id" AND p."Permission" = 'sys.info_view')
     AND NOT EXISTS (SELECT 1 FROM device_role_permissions p WHERE p."RoleId" = r."Id" AND p."Permission" = 'tag.view');
   ```
4. **Cách xử lý:** master-admin (hoặc admin quản lý quyền đang giữ `tag.view`; mã này không high-risk) thêm `tag.view`. Không migration tự cấp.
5. **High-risk chỉ kiểm lúc ghi grant** (tạo/sửa/xoá role, gán/gỡ membership), không kiểm lúc gửi, không xét lại grant cũ. 4 mã mới (`gpio.do_force`, `gpio.do_config`, `sms.send`, `sms.delete`) chưa có trong từ vựng trước lần ghim này nên hiện không role nào chứa chúng. Trước **mọi** lần đổi phân loại về sau (thêm/bớt mã high-risk) và trước rollback: chạy SQL B.
6. **SQL B, chỉ đọc:** ai đang giữ một tập mã (thay danh sách `IN`).
   ```sql
   SELECT r."Id" AS role_id, r."OrganizationId", r."Name", p."Permission",
          m."UserId", m."DeviceId", m."GrantedBy", m."GrantedAt"
   FROM device_role_permissions p
   JOIN device_roles r ON r."Id" = p."RoleId"
   LEFT JOIN device_memberships m ON m."RoleId" = r."Id"
   WHERE p."Permission" IN ('gpio.do_force', 'gpio.do_config', 'sms.send', 'sms.delete')
   ORDER BY r."Id", m."UserId";
   ```
7. **Rollback** (khớp mục Rollback của `plan.md`): trước revert, SQL B với 7 mã mới (`tag.view`, `mb_rtu.slave_view`, `mb_rtu.tag_view`, `sms.send`, `sms.delete`, `gpio.do_force`, `gpio.do_config`); có kết quả → master-admin gỡ mã hoặc xoá role trước. Khi ghim lại: SQL B với 13 mã high-risk, rà `GrantedBy` của grant tạo trong cửa sổ revert. Mã lạ còn lại trong role không khớp quyền nào; sửa role sau đó bị `DEVICE_PERMISSION_UNKNOWN` tới khi bỏ mã.
8. **Kênh device-action grant:** nhận mã bất kỳ, kể cả mã high-risk, từ người có `permissions.manage`; không mở lệnh nào (test phase 2). Nếu sau này nối kênh này vào phân quyền lệnh thì phải thêm kiểm từ vựng và high-risk trước.

### `reports/open-actions-contract-diff.md`
- Cách làm (chỉ `git --no-replace-objects -C D:/WORKSPACE/PROJECT/HMS-IDF show <rev>:engineering-docs/api/mqtt-api.md`, cả `83e93d77e` và `c80797d`): trong §6 (`## 6.` → `## 7.`), lấy mọi dòng bắt đầu bằng `` | `type.action` `` của 34 action mở, và các đoạn con §6 nêu action đó; diff hai bản. Ghi lệnh đã dùng.
- Bảng: action | đổi? | loại (chỉ thêm field / phá hợp đồng / chỉ chữ) | code nào đọc reply (grep `src/`, `frontend/src/`, `tools/`).
- Kỳ vọng (red-team): 7 action đổi; `status.tags` phá hợp đồng (mảng → trang `{schema:"status.tags.v1",…,truncated}`, L612, L927-929; gateway 1.0.108 vẫn trả mảng, `docs/architecture-mqtt-device-link.md:118-126`) → follow-up (c) của `plan.md`, không sửa code. Thêm field: ghi lại. Nếu một thay đổi phá field mà server/UI đang đọc (ví dụ `DeviceOutputState` cho `control.get_status`): dừng, báo controller (plan riêng).

### Docs
- `architecture-device-mqtt-api-catalog.md`:
  - L6, L26: số action theo catalog ghim hiện hành (108 cặp §11 @ `c80797d`).
  - Hộp L135-143: "Catalog ghim `c80797d` (2026-11-10): 108 row, 13 namespace, 53 quyền firmware; `status.tags` cần `tag.view`; 14 row mới đóng (liệt kê); vẫn 34 mở."
  - Đầu §3 (sau L133): "bảng dưới là 94 row lịch sử; danh sách đúng là §11 @ `c80797d` (108 row)".
  - L173: thay bằng "Catalog ghim `c80797d` từ 2026-11-10; `control.force_set/force_clear/get_config/set_config` có trong catalog nhưng đóng."
  - L290: bộ sinh đọc @ `c80797d` qua `git --no-replace-objects` (env không `GIT_*`), 108 action. Lệnh ghim lại: `node scripts/generate-command-catalog.mjs --idf <HMS-IDF> --accept-stated-count 98/12`, rồi `--check --accept-stated-count 98/12`, rồi `node scripts/verify-command-catalog-repin.mjs --rev <sha> --base <sha cũ> --expect-pair-set <hex>`. Vì sao có cờ: câu đếm đầu tiên §11 (L1351, câu lịch sử) "98 cặp, 12 namespace", câu sau "100 cặp, 13 namespace", bảng 108/13 (câu hỏi spec 8). Cờ ghim số prose sai, không kiểm bảng; bảng được kiểm bằng script độc lập, test 34 action mở và hash ghim. Khi L1351 đổi (sửa hoặc xoá), bộ sinh đòi sửa/bỏ cờ. Commit ghim chỉ ở nhánh feature của HMS-IDF; ghi blob `84ff812f…`; follow-up xin tag.
  - L292: 57 mã = 53 từ §11 + 4 mã server; 7 mã mới; `HighRisk` 13 mã (thêm `gpio.do_force`, `gpio.do_config`, `sms.send`, `sms.delete`), chỉ kiểm lúc ghi grant.
- `architecture-mqtt-device-link.md`:
  - Mục mới `## Catalog ghim` ngay trước `## API` (L70):
    - ghim `c80797d` (108/13/53), lệnh ghim và kiểm (trỏ docs catalog);
    - `status.tags` → `tag.view`; `sys.info_view` mở đúng 8 action;
    - 14 row đóng; `highRisk` của `/me/command-catalog` trên 24 action (kể cả lệnh đọc `sms.send_status`);
    - high-risk chỉ kiểm lúc grant, không xét lại; SQL ở báo cáo role của plan;
    - device-action grant không mở lệnh;
    - **trước khi mở row đóng:** R13 (SMS, spec L417, L719), R15 (force DO, L419), R16 (`control.set_config`, L420); catalog không mang TTL trần, chỉ-primary, posture; server coi mọi row class B là relay (audit giữ payload, gồm số điện thoại và nội dung SMS; `sms.delete` class D cũng giữ; đánh stale trạng thái DO; bỏ qua lane ghi; cap TTL 10 s; allowlist B duy nhất `PhysicalOutputActions`; `OutputCommandPolicy` theo payload relay): phải tách trước khi mở `sms.send`, `control.force_*`, `control.set_config`;
    - **cut-over:** catalog nhúng nên deploy = build + khởi động lại; khởi động lại xoá reply, trạng thái DO, lane, theo dõi reboot và đánh `OUTCOME_UNKNOWN_AFTER_RESTART`; chỉ deploy khi không có lệnh ghi DO/xung/reboot đang chạy; tab cũ nhận `CATALOG_STALE` và phải tải lại; lệnh ghi outcome không rõ thì đọc trạng thái trước khi thử lại;
    - `status.tags` dạng trang ở `c80797d`, gateway 1.0.108 vẫn mảng; follow-up.
  - L67: thêm 14 row mới vào danh sách đóng.
  - L123: dòng `status.tags`: thêm "(quyền `tag.view` từ catalog `c80797d`; lúc kiểm live là `sys.info_view`, reply dạng mảng)".
  - L160: "catalog vẫn ghim `83e93d77e`" → "thay đổi này không đụng catalog".
  - L287: thay câu Modbus/tags bằng tình trạng hiện tại (catalog `c80797d`; Modbus theo trang, tags, SMS, force/config DO có trong catalog nhưng đóng).
- `project-roadmap.md`: L16 bỏ số 94 cố định; §5 thêm dòng "Đồng bộ catalog spec `c80797d`" (108 row; `status.tags` → `tag.view`; 4 mã high-risk; 14 row đóng) trỏ `plans/261110-1747-mqtt-catalog-sync/`; L114 "Còn đóng" thêm SMS, force DO, `control.get_config`/`set_config`; L116 ghi catalog ghim `c80797d`.
- Journal mới `docs/journals/261110-mqtt-catalog-sync.md` (tiếng Anh, giống `261110-do-channel-ownership.md`): vì sao (lỗ quyền `status.tags`, catalog cũ), gì đổi, cờ và lý do nó ghim số prose sai, kiểm độc lập, high-risk chỉ kiểm lúc grant, cut-over, số test, việc chờ (câu hỏi 8, 20; tag commit ghim; `status.tags` dạng trang).
- `plans/reports/brainstorm-261108-1700-mqtt-spec-coverage-and-requests.md`:
  - §3.B mục 8 (L78-81): thêm "Server ghim `c80797d` bằng `--accept-stated-count 98/12` (2026-11-10), cờ ghim câu L1351; khi câu đó đổi thì bỏ cờ."
  - §3.B thêm mục **20** (chưa gửi): "§11 L1359 ghi luật `sms.*`/`mqtt.set_config` là '(R14)'; luật SMS là R13 (L417), R14 là phiên thử `mqtt.set_config` (L418). Sửa nhãn."
- `plans/261001-0030-full-hardware-mqtt-apis-c2/plan.md`: ghi chú frontmatter về plan này `(pending)` → `(completed)`.
- `plan.md` của plan này: mục "Kết quả", status phase; As built mỗi phase.

## Related Code Files

- **Create:** `plans/261110-1747-mqtt-catalog-sync/reports/affected-roles.md`, `plans/261110-1747-mqtt-catalog-sync/reports/open-actions-contract-diff.md`, `docs/journals/261110-mqtt-catalog-sync.md`.
- **Modify:** `docs/architecture-device-mqtt-api-catalog.md`, `docs/architecture-mqtt-device-link.md`, `docs/project-roadmap.md`, `plans/reports/brainstorm-261108-1700-mqtt-spec-coverage-and-requests.md`, `plans/261001-0030-full-hardware-mqtt-apis-c2/plan.md`, file của plan này.
- **Không sửa:** `src/`, `tests/`, `frontend/`, `scripts/`, `tools/`.

## Implementation Steps

0. **Mốc** (ghi vào `reports/phase-03-implementation-report.md`): `cd frontend && npx vitest run` → mong 537; `npx vue-tsc --noEmit` → sạch; số unit/integration/node lấy từ report phase 2.
1. **Không có test đỏ:** phase không đổi hành vi. Ghi bằng chứng "frontend không đổi" (Hiện trạng) vào report. Grep lại `frontend/src` (`gpio\.do_|sms\.|mb_rtu\.|tag\.view|status\.tags|highRisk`, không tính test): thấy chỗ hard-code quyền firmware hoặc đọc `status.tags` → dừng, báo controller.
2. **Báo cáo role** theo Requirements; trích file:line; chạy lại grep `CreateDeviceRoleHandler|AssignDeviceMembershipHandler` trong `src` (không tính `bin/obj`).
3. **Diff §6** theo Requirements.
4. **Docs, journal, ghi chú.** Đọc file trước khi sửa; sau khi sửa kiểm ngày, số (108/13/53/57/34/24/13/8) và đường dẫn khớp code.
5. **Hồi quy cuối:**
   - `dotnet test tests/Hms.Server.UnitTests`, `Mqtt__Enabled=false dotnet test tests/Hms.Server.IntegrationTests`: bằng số cuối phase 2 (~686 / ~143).
   - `cd frontend && npx vitest run` → 537; `npx vue-tsc --noEmit` sạch.
   - `node --test scripts/generate-command-catalog.test.mjs scripts/verify-command-catalog-repin.test.mjs` → 31; `node scripts/generate-command-catalog.mjs --idf D:/WORKSPACE/PROJECT/HMS-IDF --check --accept-stated-count 98/12` → up to date.
   - `git diff --stat` của phase này chỉ gồm docs và file plan.
6. **Bắt buộc, E2E tab cũ trên máy local** <!-- Updated: Validation Session 1 - E2E tab cũ thành bắt buộc -->: môi trường local như plan trước (Mosquitto `127.0.0.1:8088`, API có binding `hms_server_selftest/sim_001`, simulator, Vite; KHÔNG chạm broker lab `192.168.1.101:443` hay `hms_toan_lab/#`). Chạy API + frontend ở commit trước phase 2, đăng nhập, mở trang thiết bị simulator; chuyển sang commit cuối, khởi động lại API (không có lệnh ghi đang chạy), bấm một lệnh đọc trên tab cũ → thông báo catalog đã đổi, cần tải lại (`CATALOG_STALE`, không ký, không publish: log simulator không có lệnh đó); tải lại → cùng lệnh đọc chạy (`Executed`). Ghi bằng chứng (DOM/ảnh, dòng log simulator) vào `reports/live-verification.md`; dừng đúng các tiến trình đã mở.

## Success Criteria

- [ ] Vitest 537, `vue-tsc` sạch; không file `frontend/` nào đổi.
- [ ] `affected-roles.md` có: ai mất gì (không còn câu "mất reply đã lưu do quyền"), hiện trạng theo code, SQL A và B, high-risk chỉ kiểm lúc grant, thủ tục rollback, kênh device-action grant.
- [ ] `open-actions-contract-diff.md` có lệnh đã dùng và bảng 34 action; `status.tags` ghi là follow-up.
- [ ] Docs ghi: ghim `c80797d` (chỉ nhánh feature, blob), 108/13, `tag.view`, 4 mã high-risk + grant-time only, 14 row đóng, lệnh có `--accept-stated-count 98/12` + script kiểm, khi nào bỏ cờ, R13/R15/R16, hành vi class B phải tách, cut-over; không còn "catalog vẫn ghim `83e93d77e`" ở phần hiện hành.
- [ ] Câu hỏi 8 có ghi chú; câu hỏi 20 đã thêm (chưa gửi); frontmatter plan C2 đã cập nhật; `plan.md` có Kết quả.
- [ ] E2E tab cũ (bước 6) PASS trên local: tab cũ nhận `CATALOG_STALE` trước khi ký (simulator không nhận lệnh), tải lại thì đọc chạy; bằng chứng trong `reports/live-verification.md`; tiến trình đã dừng, broker lab không bị chạm.

## Risk Assessment

| Rủi ro | Khả năng × Ảnh hưởng | Giảm thiểu |
|---|---|---|
| Docs ghi số sai | Thấp × Thấp | Bước 4 đối chiếu số với test phase 2 |
| Bỏ sót frontend hard-code | Thấp × Trung bình | Bước 1 grep lại; vitest + `vue-tsc` |
| SQL sai tên cột | Thấp × Thấp | Tên cột lấy từ migration; câu lệnh chỉ đọc |
| Diff §6 bỏ sót đoạn con nêu action | Trung bình × Thấp | Ghi rõ cách lấy; chỉ là báo cáo, thay đổi phá hợp đồng đã biết được xử lý ở follow-up |

Rollback: revert commit docs.

## As built

Chi tiết và số liệu: `reports/phase-03-implementation-report.md`.

- Bước 0: vitest 537 pass; `vue-tsc --noEmit` exit 0.
- Bước 1: không có file `frontend/` nào đổi. Grep chỉ còn `gpio.do_view` trong thông báo `DeviceOutputsPanel.vue:315` (chữ hiển thị, không phải danh sách quyền, không dừng).
- Bước 2: `reports/affected-roles.md` (SQL A, B lấy nguyên văn; cột tên đối chiếu migration `20261005143949`). Không có HTTP tạo role/gán membership.
- Bước 3: `reports/open-actions-contract-diff.md`: 7 dòng hàng động đổi; chỉ `status.tags` phá hợp đồng (follow-up c); không có reader server/UI nào đọc field bị đổi.
- Bước 4: docs catalog, device-link, roadmap, journal `docs/journals/261110-mqtt-catalog-sync.md`, brainstorm (§3.B mục 8, mục 20 chưa gửi), frontmatter plan C2 `(completed)`. Số đối chiếu với JSON và code: 108 / 13 / 53 (57) / 34 / 14 / 13 / 24 / 8 / 7.
- Bước 5: node 32 pass (33 sau khi sửa finding L1 của review); generator `--check --accept-stated-count 98/12` up to date (`658e191a…`); unit 686 và integration 143 lấy từ phase 2, không chạy lại (API đang chạy của controller).
- Bước 6 (E2E tab cũ): controller chạy trên local, PASS; bằng chứng ở `reports/live-verification.md`. `Executed` chứng minh bằng `system.ping` vì simulator không có `status.services`.
