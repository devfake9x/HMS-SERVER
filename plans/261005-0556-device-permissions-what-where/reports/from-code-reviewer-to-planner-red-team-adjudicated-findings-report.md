---
type: report
date: 2026-10-05
plan: 261005-0556-device-permissions-what-where
stage: red-team adjudication
reviewers: [security-adversary, failure-mode-analyst, assumption-destroyer, scope-complexity-critic]
verification: Full tier (Fact Checker, Flow Tracer, Scope Auditor, Contract Verifier) + 1 skeptic mỗi finding
workflow_run: wf_27755e2a-37c
status: applied
owner_decisions: {V7: "endpoint gỡ role (a)", V8: "deny của master-admin không bị non-master đảo (a)", V9: "cho gỡ member org khác (a)"}
---

> **Note 2026-10-05 (design contract Q14):** evidence below that cites firmware source files is historical; firmware facts now come from `mqtt-api.md` only.


# Red team — kết luận phân xử

## Tóm tắt

- 30 finding thô: security 8, failure-mode 8, assumption 6, scope 8. Skeptic xác minh: 6 confirmed, 19 partially confirmed, 5 refuted.
- Gộp trùng → **14 finding được phân xử** (1 High, 13 Medium). 5 finding bị skeptic bác (mức sau xác minh = None) nằm dưới Medium nên ra khỏi cap; vẫn liệt kê ở cuối, không bỏ im lặng.
- Phân xử: **11 Accept**, **3 cần owner chọn** (V7, V8, V9), **5 Reject**.
- Evidence filter: cả 14 finding đều có trích dẫn `file:line` trong code thật.
- Ngoài finding: 6 câu khẳng định sai sự thật nhỏ trong plan (claim check) — sửa trong consistency sweep.

## Bảng gộp trùng

| RT | Gộp từ | Mức sau xác minh | Phân xử |
|----|--------|------------------|---------|
| RT1 | security #3, failure #1, assumption #1, scope #7 | High | Owner (V7) + phần chung Accept |
| RT2 | security #6 | Medium | Accept |
| RT3 | security #5 | Medium | Accept |
| RT4 | security #1 | Medium | Accept |
| RT5 | security #7 | Medium | Accept |
| RT6 | failure #2, assumption #2, scope #8 | Medium | Accept |
| RT7 | failure #5, assumption #5, failure #7 | Medium | Accept |
| RT8 | assumption #3, failure #8, scope #2 | Medium | Accept (nửa "lộ email" bị bác) |
| RT9 | failure #4, assumption #4 | Medium (thấp) | Accept — hardening rẻ |
| RT10 | scope #1 | Medium | Accept (Option 2) |
| RT11 | scope #3 | Medium | Accept |
| RT12 | scope #5 | Medium | Accept (giữ `commands`, thêm `label`) |
| RT13 | security #2 | Medium | Owner (V8) |
| RT14 | security #4 | Medium | Owner (V9) |
| — | failure #3 | None | Reject |
| — | failure #6, assumption #6 | None | Reject |
| — | scope #4 | None | Reject |
| — | scope #6 | None | Reject |
| — | security #8 | None | Reject (chuyển cho plan 261001) |

## Findings

### RT1 — Không còn cách chặn master-admin trong sản phẩm; lối "gỡ role" plan viện dẫn không tồn tại — HIGH

**Reviewer:** security #3, failure #1, assumption #1, scope #7
**Location:** contract §3 (`design-contract.md:100`); phase-03 :28, :342, :350; phase-04 :39, :284, :288; phase-05 :35, :46, :112, :275, :282; phase-06 :23, :356
**Flaw:** Q4 bỏ "deny thắng master-admin" ở mọi điểm (feature route, effective orgs, device gate, realtime). Plan ghi lối thay thế là "gỡ role master-admin (chỉ master-admin gỡ được)". Code không có gỡ role, lockout hay đổi security stamp; không phase nào thêm. `RoleAuthorizationPolicy` chỉ canh tạo/gán role, không có thao tác gỡ.
**Failure scenario:** Master-admin lộ mật khẩu/cookie. Hôm nay master khác persist deny `*` → request kế tiếp bị chặn. Sau phase 3 deny bị bỏ qua, không endpoint gỡ role → phải sửa SQL `AspNetUserRoles`; cookie HTTP còn giữ role tới 30 phút (SecurityStampValidator mặc định) và trong khoảng đó tự gán lại role được. Thêm: form "Explicit deny" vẫn trả 200 cho deny nhắm master-admin nhưng vô tác dụng → tưởng đã cô lập.
**Evidence:** `UserManagementContracts.cs:15-30`; `UsersController.cs:74-93`; `IdentityUserManagement.cs:71-87`; `FeaturePermissionAuthorizationHandler.cs:25-29`; `ScopeAuthorization.cs:33-38`, `:56-62`; `DependencyInjection.cs:169-180`; `IdentityRealtimePrincipalRefresher.cs:28-46`.
**Disposition:** Owner decision (V7). Phần không phụ thuộc lựa chọn: Accept.
**Rationale:** Contract ràng buộc ghi một biện pháp không tồn tại ở ~10 chỗ để biện minh việc bỏ biện pháp duy nhất đang có cho role mạnh nhất. Đây là hệ quả của Q4 → không tự quyết.
**Lựa chọn:**
- (a) **Khuyến nghị.** Endpoint chỉ master-admin `DELETE /api/v1/users/{userId}/roles/{role}` → `IUserManagement.RemoveRoleAsync` (RemoveFromRoleAsync → UpdateSecurityStampAsync → `permissions.Advance()`); 409 khi gỡ master-admin cuối cùng; `AssignRoleAsync` cũng đổi stamp; `SecurityStampValidatorOptions.ValidationInterval` = 1 phút. Thuộc phase 4 (cùng release). Đánh đổi: master-admin xấu cũng gỡ được master khác (rủi ro này có sẵn với deny hôm nay).
- (b) Break-glass: deny `*` do master-admin ghi vẫn chặn master-admin đích. Ngoại lệ trong `ResolvedFeaturePermissions.Has` + `AccessActorFactory`. Nhỏ hơn, nhưng "vô điều kiện" có một ngoại lệ.
- (c) Chỉ cô lập ở DB: runbook xoá dòng `AspNetUserRoles`, đổi `SecurityStamp`, xoay DataProtection key ring nếu lộ cookie. HTTP trễ tới 30 phút.
**Áp dụng bất kể lựa chọn:** sửa mọi câu "gỡ role" theo lựa chọn; grant handler trả lỗi `GRANT_TARGET_IS_MASTER_ADMIN` cho deny nhắm master-admin (trừ deny `*` nếu chọn b); docs phase-04 C.9 + ghi chú phát hành: "deny feature/resource không còn chặn master-admin"; truy vấn read-only trước mọi lần deploy lên DB có dữ liệu: dòng deny feature/resource thuộc tài khoản master-admin; sửa tham chiếu "§10" sai ở phase-04:288.

### RT2 — Sửa role vượt rào V4: non-master thu nhỏ (kể cả làm rỗng) role vượt quyền mình, hạ quyền mọi holder — MEDIUM

**Reviewer:** security #6 (confirmed)
**Location:** contract §4 `:123` vs `:125`; phase-03 Architecture `:154` vs `:156`, test `:250`
**Flaw:** V4 cấm non-master thay/gỡ membership có role hiện tại ⊄ perms(actor, D). Role update chỉ kiểm `new ⊆ perms(actor, D)`. ∅ ⊆ mọi tập → làm rỗng role chung là hạ quyền mọi holder trên mọi thiết bị. Plan tự mâu thuẫn: :154 nói `CheckRoleChange` dùng chung cho role-update (có kiểm old), :156 và contract bỏ kiểm old.
**Failure scenario:** Role R = {device.view, adc.config_edit, gpio.do_write} gán cho operator trên D1, D2. Sub-admin A (permissions.manage; membership {device.view} trên D1, không có trên D2) không gỡ được operator (V4) nhưng `PUT /device-roles/R` với `[]` lọt → mọi operator mất quyền, bị evict `ACCESS_REMOVED`.
**Evidence:** `SignalRDeviceEventDeliveryService.cs:173-179`; cmd_descriptor.c @83e93d77e (gpio.do_write, adc.config_edit không thuộc high-risk).
**Disposition:** Accept.
**Rationale:** Bypass thật của bất biến đã nêu; một request hạ quyền toàn org. Theo V4 (đang mở) — nếu owner bỏ V4 thì finding hết hiệu lực.
**Fix:** contract §4 + phase-03 :156: non-master update → `(old ∪ new) ⊆ perms(actor, D)` ∀ D ∈ distinct ListByRole, cài bằng `DelegationRules.CheckRoleChange(actor, perms(actor, D), new, old)` mỗi D; giữ kiểm `H ∩ (old ∪ new)` ngoài vòng lặp. §11 V4 mở rộng "gỡ/thay **và sửa role**". Test `Non_master_cannot_shrink_role_exceeding_own_permissions_on_assigned_devices` (`[]` và `[device.view]` đều `GRANT_EXCEEDS_OWN_PERMISSIONS`, không ghi store, không advance).

### RT3 — Gán role không ràng buộc phiên bản role: sub-admin sửa role chưa gán, master-admin gán theo tên — MEDIUM

**Reviewer:** security #5 (confirmed)
**Location:** contract §4 `:122`, `:124`, `:125`, §6 `:163`; phase-04 `:85-86`, `:110`; phase-06 `:126`
**Flaw:** CAS khi gán chỉ phủ role hiện tại của target (`expectedRoleId`). Role sắp gán không kiểm version/nội dung. Role chưa gán: kiểm subset khi sửa là rỗng nghĩa. Picker chỉ hiện tên role.
**Failure scenario:** A (thiếu gpio.do_write trên D) đổi role chưa gán thành "Viewer (read-only)" = {device.view, gpio.do_write, mb_rtu.config_edit}. Master-admin chọn "Viewer (read-only)" cho U → U điều khiển ngõ ra vật lý. A còn có thể nhờ master gán role đó cho chính A.
**Evidence:** `DevicePermissionsController.cs:64-71` (hôm nay có `POLICY_VERSION_STALE`, plan bỏ mà không thay); `DevicePermissionsTab.vue:184`.
**Disposition:** Accept.
**Rationale:** Confused-deputy làm hỏng ý "chỉ cấp tập con quyền mình" của Q4 mà không cần race.
**Fix:** `DeviceRoleOptionDto` thêm `int Version`; `AssignDeviceMembershipRequest(Guid RoleId, Guid? ExpectedRoleId, int ExpectedRoleVersion)` bắt buộc mọi actor; handler sau `DEVICE_ROLE_NOT_FOUND`/`DEVICE_ROLE_ORGANIZATION_MISMATCH`, trước ghi → `DEVICE_ROLE_VERSION_STALE` (409). Khe vài ms giữa đọc role và ghi membership ghi vào risk (cạnh phase-03 :337). Phase 6: hiện danh sách quyền của role đang chọn (nhóm + badge high-risk) trước khi lưu; gửi version; 409 → reload + banner. Test phase 3 (master + non-master), phase 4 integration, phase 6 render + gửi version.

### RT4 — Feature grant không gắn phạm vi: sub-org admin đẩy quyền quản trị sang org ngoài phạm vi mình — MEDIUM

**Reviewer:** security #1
**Location:** phase-03 F6 `:57`, `:167-170`, Security `:353`; contract §4 `:114`, §3 `:98`, `:124`; plan.md `:51`
**Flaw:** Feature allow chỉ kiểm grantor đang giữ feature + `CanManageTarget` (home org của target). Feature không có chiều org, kết hợp với resource scope target có từ người khác. Ngược lại, resource allow không kiểm feature của target.
**Failure scenario:** Master cấp cho U (home Y) scope trên org X. Sub-admin A (scope Y, giữ permissions.manage + devices.manage) cấp U permissions.manage + devices.manage — mọi kiểm F6 qua. U giờ tạo/revoke thiết bị X, sửa/xoá role của X.
**Evidence:** `PermissionContracts.cs:10-11`, `:45-54`; `PermissionGrantsController.cs:57-76`, `:103-114`; `ScopeAuthorization.cs:42-63`; `DevicesController.cs:138-140`.
**Disposition:** Accept.
**Rationale:** Hở có sẵn hôm nay (và tệ hơn), plan thu hẹp nhưng câu "không cấp vượt quyền mình" (:353, plan.md:51) chưa đúng. Fix nhỏ, dùng helper đã có.
**Fix:** non-master allow, master-admin miễn: (a) feature allow → effective orgs của target (từ resource grant persisted latest-wins, expand, trừ deny) ⊆ `actor.EffectiveOrganizations`; (b) resource allow → feature allowed của target ⊆ `actor.FeaturePermissions`. Vi phạm → `GRANT_EXCEEDS_OWN_PERMISSIONS`. Test unit `Feature_grant_rejected_when_target_scope_exceeds_grantor_scope`, `Resource_grant_rejected_when_target_holds_features_grantor_lacks` + 1 integration.

### RT5 — Ingest telemetry thành quyền giao cho người được (giả trạng thái thiết bị + phát event status), trái brainstorm "định danh dịch vụ" — MEDIUM

**Reviewer:** security #7
**Location:** contract §6 `:152`; phase-04 `:51`, `:105`, test `:182`
**Flaw:** `POST /devices/{id}/telemetry` là đường máy (RTU/bridge). Plan mở cho mọi member có `device.record_measurements` (không high-risk, sub-admin tự đưa vào role được). Dữ liệu người gửi lưu như dữ liệu thiết bị và phát `status` tới mọi viewer. Brainstorm đã duyệt ghi ingest = "định danh dịch vụ, không phải user" (brainstorm :128, :165); contract đổi mà không ghi lý do.
**Failure scenario:** Operator có record_measurements gửi `{"digital_outputs":[{"channel":1,"state":false}]}` → mọi console thấy ngõ ra OFF trong khi đang ON.
**Evidence:** `DevicesController.cs:96-129`; `DeviceTelemetryContracts.cs:31-60`, `:116-122`; `DeviceDetailView.vue:202-210`, `:260-266`; `MaitechRtuDeviceIntegrationTests.cs:74-126`; `tools/mqtt-broker/hms-telemetry-bridge.js:87-98` (bridge đăng nhập master-admin).
**Disposition:** Accept.
**Rationale:** Đưa contract về đúng quyết định brainstorm; bridge và 2 test RTU dùng master-admin nên vẫn xanh.
**Fix:** v1 ingest chỉ master-admin (ngoài phạm vi → 404; trong phạm vi không phải master → 403; Revoked → 403 `DEVICE_REVOKED`). `device.record_measurements` giữ trong 50 mã, chưa có điểm chặn ở v1, label ghi "chưa có hiệu lực ở v1". §10 thêm: ingest bằng định danh dịch vụ + endpoint nhập số đo thủ công là việc riêng. Test phase-04 :182 → `Telemetry_ingest_requires_master_admin`.

### RT6 — Lỗi refresh principal tạm thời biến thành `ACCESS_REMOVED` kết thúc; mitigation "client join lại" không tồn tại ở phase 6 — MEDIUM

**Reviewer:** failure #2, assumption #2, scope #8
**Location:** phase-05 F6 `:50`, reason row 1 `:104`, Quyết định `:131`, C3 `:217`, tests `:201-202`, SC `:255`, risk `:272`, Next Steps `:293`; phase-06 `:43`, `:96`, `:123`, `:185`
**Flaw:** Refresher nuốt mọi exception thành null (giống user mất/khoá/đổi stamp). Phase 5 map null → `ACCESS_REMOVED`, viện dẫn "client join lại qua hub". Phase 6 coi `ACCESS_REMOVED` là kết thúc, test A0 khoá "không join lại".
**Failure scenario:** Lỗi DB một phần (pool cạn, timeout một câu lệnh) trong một tick → viewer còn quyền thấy màn "mất quyền truy cập" chiếm cả trang, không tự hồi phục.
**Evidence:** `IdentityRealtimePrincipalRefresher.cs:29-43`, `:52-56`; `SignalRDeviceEventDeliveryService.cs:32-39`, `:167-171`, `:183-203`; `useDeviceOperationsHub.ts:110-121`; `DependencyInjection.cs:133-137` (không EnableRetryOnFailure).
**Disposition:** Accept.
**Rationale:** Mâu thuẫn nội bộ plan + màn hình sai nghĩa khi hạ tầng chập chờn. Không ảnh hưởng bảo mật (vẫn fail-closed).
**Fix (không đổi refresher, phase 6 không đổi code):** refresh principal lỗi (null hoặc exception) → `REALTIME_RESYNC_REQUIRED`; `ACCESS_REMOVED` chỉ dành cho mất quyền do đánh giá (row 2-3). Đổi tên test `Principal_refresh_returning_null_notifies_resync_required`, `Principal_refresh_failure_notifies_resync_required_and_removes_membership`. Risk :272: client hiện "realtime disconnected", trang vẫn dùng được; join lại thủ công đi qua `RefreshPrincipalAsync` của hub, phiên khoá/xoá/đổi stamp nhận `AUTHENTICATION_REQUIRED`. Next Steps :293 bỏ "kể cả khi phiên mất hiệu lực". Contract §7 ghi nghĩa mới của hai reason.

### RT7 — Tín hiệu realtime khi ghi membership/role: evict mọi viewer, join lại một phát dễ kẹt, lỗi sau commit trả 500 và không phát lại, sửa role ghi epoch tuần tự N lần — MEDIUM

**Reviewer:** failure #5, assumption #5, failure #7
**Location:** contract §4 write ordering `:113`, §7; phase-03 `:153-157`, risk `:338`; phase-05 `:82-90`, `:107`, tests `:192-200`; phase-06 `:96`, `:124`, A4 `:226`, risk `:360`
**Flaw:**
1. Mọi ghi membership tăng epoch(D) — kể cả gán mới (không ai mất quyền) — nên mọi viewer của D bị evict `AUTHORIZATION_CHANGED`, event mang epoch cũ bị bỏ im lặng.
2. Phase 6 join lại đúng một lần, `.catch(noop)`; `STALE_AUTHORIZATION_EPOCH` khi join lại để device kẹt chờ reauth, view vẫn báo "connected".
3. Store + audit đã commit rồi mới `Advance`; Advance lỗi → 500, client thử lại → 409 (CAS), epoch bỏ lỡ không ai phát lại.
4. Sửa role: N lệnh upsert epoch tuần tự sau commit; lỗi ở thiết bị k bỏ sót k..N.
**Failure scenario:** Admin lưu 5 dòng ở tab Thành viên → 5 đợt evict 20 viewer; viewer join lại trúng lúc epoch vừa đổi → live view chết. Role dùng trên nhiều thiết bị → PUT chạy hàng trăm round trip sau commit.
**Evidence:** `EfAuthorizationEpochStore.cs:42-85`; `EfPermissionStore.cs:117-146`; `RealtimeContracts.cs:140-161`; `SignalRDeviceEventDeliveryService.cs:68-71`, `:236`; `DeviceEventsHub.cs:89-91`; `useDeviceOperationsHub.ts:110-121`, `:228-240`; `App.vue:257`.
**Disposition:** Accept.
**Rationale:** Evict thô là thiết kế có chủ đích, nhưng mitigation (join lại) mong manh, phạm vi ảnh hưởng chưa ghi/test, và lỗi sau commit chưa có ngữ nghĩa HTTP.
**Fix:**
- Contract §4 + phase 3: gán mới (chưa có membership) chỉ advance PolicyVersion, **không** tăng epoch(D); thay/gỡ → epoch(D); sửa role → `IAuthorizationEpochStore.AdvanceMany(devices)` một câu lệnh (Postgres `INSERT … SELECT unnest(@ids) ON CONFLICT DO UPDATE`; InMemory lặp dưới lock). Phase 3 Modify thêm `RealtimeContracts.cs`, `EfAuthorizationEpochStore.cs` (bỏ khỏi "Không chạm" của phase 5).
- Sau khi store ghi OK: advance PolicyVersion và epoch là tín hiệu best-effort — bọc riêng từng bước, lỗi thì log audit id + device ids và vẫn trả OK (an toàn nhờ reconcile đọc quyền live).
- Phase 6: join lại tối đa 3 lần, backoff có jitter 250 ms–2 s khi `STALE_AUTHORIZATION_EPOCH` hoặc lấy epoch lỗi tạm; dừng khi `DEVICE_NOT_FOUND`/`DEVICE_PERMISSION_REQUIRED`/`DEVICE_REVOKED`, đổi hub/phiên, hoặc `leaveDevice`; hết lượt → giữ `requiresReauthorization` + callback `onRejoinFailed(deviceId, code)` → DeviceDetailView `realtimeStatus='disconnected'`. Join lại thành công → tải lại telemetry.
- Test: phase 3 `Fresh_assign_does_not_advance_device_epoch`, `Post_commit_signal_failure_returns_ok_and_keeps_the_write`, `Role_update_advances_all_affected_devices_in_one_call`; AdvanceMany đơn điệu (PersistenceModelTests); phase 5 viewer Y (và scope admin) trên D join lại khi membership của X bị thay/gỡ, gán mới không evict ai; phase 6 thay A4 bằng 2 test (thử lại rồi join được; hết lượt thì báo consumer).
- Risk phase 5: mọi thay/gỡ/sửa role evict mọi subscriber thiết bị liên quan và bỏ event đang chờ (chấp nhận ở v1 vì chỉ có event `status` gộp được). Next Steps cho 261001: event kết quả lệnh cần resync/snapshot sau `AUTHORIZATION_CHANGED`. Ghi chú: admin SeesAllInScope được gán mới không nhận tín hiệu → nút lệnh mới hiện sau khi tải lại (chỉ UX).

### RT8 — Memberships GET dựng trên `IUserManagement.ListAsync`: query lồng trong stream lỗi trên Npgsql, 2N+1 query qua mọi user — MEDIUM

**Reviewer:** assumption #3 (confirmed), failure #8, scope #2
**Location:** phase-04 `:110`, NFR `:62`; phase-06 `:126`
**Flaw:** `ListAsync` stream `AspNetUsers` rồi `await GetRolesAsync` cùng DbContext trong vòng lặp. Npgsql không có MARS, không có retrying strategy → lỗi "command already in progress" ngay dòng đầu. Test chạy EF InMemory nên CI xanh. Thêm `Organization()` mỗi user một query; roles lấy về không dùng.
**Failure scenario:** Trên Postgres, mở tab Thành viên → 500. Nơi chạy được thì mỗi lần mở/lưu tốn 1 + 2N query.
**Evidence:** `IdentityUserManagement.cs:15-25`, `:50-55`; `UserManagementContracts.cs:6`; `DependencyInjection.cs:135-143`; `ManagementApiTests.cs:647`.
**Disposition:** Accept. Nửa "lộ email user ngoài phạm vi" bị bác: liệt kê người đang giữ membership trên D là minh bạch có chủ đích, các dòng đó read-only.
**Rationale:** Endpoint mới hỏng chắc chắn trên Postgres; fix một projection.
**Fix:** `IUserManagement.ListDirectoryAsync(ct)` → (UserId, Email, OrganizationId?) một projection `ToListAsync`, không GetRolesAsync; controller lọc trong bộ nhớ (membership trên D ∪ org ∈ effective orgs; master-admin: mọi user). NFR: số query cố định. Test: member có org ngoài phạm vi vẫn được liệt kê, read-only. Risk: EF InMemory không bắt được lỗi MARS → smoke thủ công trên Postgres trước phát hành. `ListAsync` cũ (GET /users) không sửa trong plan này — báo riêng.

### RT9 — Latest-wins dựa vào PolicyVersion duy nhất và tăng dần, nhưng bộ đếm Postgres seed 0, bỏ qua version của grant cũ; không có tie-break — MEDIUM (thấp)

**Reviewer:** failure #4 (partially), assumption #4 (refuted)
**Location:** phase-03 `:32`, `:101`, `:257`; phase-02 migration `:214`
**Flaw:** Phase 3 lần đầu dùng PolicyVersion từng dòng để quyết allow/deny. `AdvancePostgres` chỉ cộng bộ đếm, không so MAX(grants). Chỉ getter và nhánh InMemory sửa lệch.
**Failure scenario:** Chỉ xảy ra với DB Postgres đã có grant trước migration `20260929032719` (cửa sổ ~23 giờ, repo không có dấu hiệu DB như vậy): deny mới (v7) thua allow cũ (v31).
**Evidence:** `20260929032719_AddPolicyVersionCounter.cs:27-30`; `EfPermissionStore.cs:13-24`, `:36-53`, `:99-109`, `:117-139`; `ApplicationDbContext.cs:142-150`; `PermissionTests.cs:15-29` (chỉ InMemory).
**Disposition:** Accept — hardening rẻ.
**Rationale:** Hai reviewer bất đồng về khả năng xảy ra, nhưng cùng chỉ ra plan dựa vào bất biến chưa được ép. Fix = 1 câu SQL idempotent + 1 so sánh.
**Fix:** phase-02 migration, trước DELETE device-action: `UPDATE policy_versions SET "Version" = GREATEST("Version", COALESCE((SELECT MAX("PolicyVersion") FROM permission_grants), 0)) WHERE "Id" = 1;` + test migration kiểm SqlOperation có mặt và đứng trước DELETE. Resolver: cùng key, cùng PolicyVersion → deny thắng (test cả hai thứ tự). Sửa câu :32/:101 thành "duy nhất, tăng dần sau khi migration căn bộ đếm".

### RT10 — Phase 1 viết bản Node sao chép escape của System.Text.Json cho đầu vào generator không bao giờ tạo ra — MEDIUM

**Reviewer:** scope #1
**Location:** contract §2 `:63-64`; phase-01 `:32`, `:107-113`, `:183`, `:203`, `:206`, `:264`, `:277-278`, `:289`
**Flaw:** Hai bộ canonicalize (JS + C#) phải ra byte giống nhau; JS chép cả bảng escape STJ, golden vector hai ngôn ngữ. 301 literal ở cmd_descriptor.c chỉ gồm chữ, số, `_ . -` và `<` `>` trong 3 template; generator đã cấm literal có `\`.
**Failure scenario:** JS và STJ lệch một ký tự → hash mismatch, phải sửa emulator cho ký tự không bao giờ xuất hiện. Rủi ro số 1 của phase 1 chỉ tồn tại vì có hai bản cài.
**Evidence:** `PinnedCommandCatalog.cs:31-35`; `catalogVerification.ts:27-37`; `CommandCatalogHash.cs:21-22`; cmd_descriptor.c @83e93d77e :71, :81, :233.
**Disposition:** Accept (Option 2, không phải Option 1).
**Rationale:** Đúng YAGNI. Giữ `catalog_hash` vì CI không chạy `--check` (thiếu HMS-IDF) — hash là chốt duy nhất chặn catalog bị sửa tay.
**Fix:** generator kiểm charset `^[A-Za-z0-9_.<>-]+$` cho mọi chuỗi policy (type, action, class, required_perm, confirm_template, domain, view_perm, edit_perm), lỗi nêu dòng + field; canonical = `JSON.stringify` rồi `<` → `<`, `>` → `>` (hex hoa), chỉ hợp lệ dưới guard; node:test thay bảng 13 escape bằng 2 test (guard từ chối `+ & ' \` " \ ` non-ASCII, control; `<DEVID>` escape đúng); golden vector chung JS/C# thu về alphabet thật. Xoá risk :278. Sửa luôn câu "hash kiểm lúc khởi động": singleton dựng ở lần resolve đầu, `CatalogTests` ép load (claim check).

### RT11 — Cache DeviceAccess theo tick thêm trạng thái dễ cũ để tiết kiệm 2 lần đọc có index — MEDIUM

**Reviewer:** scope #3
**Location:** phase-05 `:34`, `:48`, `:54`, `:60-94`, `:139-141`, `:186-188`, `:243`, `:258`, `:269-271`, `:281`; contract §7 `:170`
**Flaw:** Mỗi lần Evaluate vẫn dựng lại AccessActor (FeatureGrants, ResourceGrants, đọc bảng org) — phần tốn nhất. Cache chỉ bỏ qua `Resolve` (membership theo unique index + role theo PK). Tick rảnh không có cache hit. Cache sinh bug sai reason phải vá bằng `reread`, cần decorator đếm + 2 test riêng.
**Failure scenario:** Công sức review/test dồn vào luật invalidation; sửa Evaluate sau này quên `reread` là quay lại bug :94.
**Evidence:** `ScopeAuthorization.cs:26-38`; `EfManagementStores.cs:35-39`; `EfPermissionStore.cs:99-109`; `SignalRDeviceEventDeliveryService.cs:21`, `:71-80`, `:114`, `:167-177`.
**Disposition:** Accept.
**Rationale:** Phức tạp tránh được trong code phân quyền, không có số đo chứng minh cần.
**Fix:** bỏ cache và tham số `reread`; `EvictionReason`: tính epochMoved, đánh giá live → `DEVICE_REVOKED` → `ACCESS_REMOVED` → `AUTHORIZATION_CHANGED` → null. Xoá test :186 + decorator, test :188, SC :258; giữ :187, :189, :198. Contract :170 → "mỗi lần kiểm resolve live; cache actor/principal theo connection chỉ khi đo thấy cần".

### RT12 — Endpoint mã quyền trả `commands[]`/`source` không UI nào đọc và thiếu nhãn; role editor sẽ hiện 50 mã trần — MEDIUM

**Reviewer:** scope #5
**Location:** contract §6 `:143`, `:157`; phase-04 `:115`, `:186`; phase-06 `:39`, `:64`, `:127`, `:241-242`
**Flaw:** Không phase-6 component/test nào đọc `commands` hay `source`. DTO không có label → mất tiêu đề/mô tả 4 quyền device.* mà tab hiện tại đang có.
**Failure scenario:** Admin dựng role nhìn chuỗi `mb_rtu.config_edit`, `net.priority_edit` không giải thích → cấp nhầm.
**Evidence:** `DevicePermissionsTab.vue:33-38`, `:312`, `:315`; `DevicePermissionsController.cs:24-30`; HMS-IDF @83e93d77e `auth_manager.c:41-160`.
**Disposition:** Accept (theo bản sửa của skeptic: giữ `commands` và hiển thị nó).
**Rationale:** Nhãn firmware dễ hiểu sai (`sys.config_export` thực ra chặn `config.get:system`; `sys.diagnostic` chặn `battery.clear_alert`) — `commands[]` là giải thích chính xác duy nhất.
**Fix:** DTO `{code, group, highRisk, label, commands[]}`, bỏ `source`; `label` từ map tĩnh phía server: 4 mã device.* chuyển tiêu đề/mô tả từ `DevicePermissionsController.cs:24-30` trước khi xoá controller, mã firmware dùng chính code ở v1; editor hiện label + danh sách lệnh dạng chữ phụ. Test phase-04 :186 kiểm label device.view khác rỗng; phase-06 `shows the label and the commands each permission gates`. Ghi kết luận câu hỏi mở scout-device-contract :79 vào contract.

### RT13 — Latest-wins cho phép admin ngang hàng lặng lẽ đảo deny của master-admin; dòng grant không ghi người cấp — MEDIUM

**Reviewer:** security #2
**Location:** phase-03 F5/F6 `:56-57`, `:167`, tests `:253`, `:259`; contract §4 `:114`; plan.md `:50`; phase-04 `:57`
**Flaw:** Hôm nay deny persisted là vĩnh viễn với mọi người. Plan đổi sang "dòng mới nhất theo PolicyVersion thắng", không ưu tiên người cấp. Non-master giữ feature và quản lý được target ghi allow mới là xoá deny có mục tiêu của master-admin. Không có cột người cấp → không truy được ai đảo.
**Failure scenario:** Sau sự cố, master-admin deny devices.manage của S. Admin P (cùng phạm vi, giữ devices.manage) cấp lại allow → S revoke thiết bị được tiếp; snapshot chỉ thấy allow version cao hơn, không tác giả.
**Evidence:** `ScopeAuthorization.cs:47-58`; `FeaturePermissionAuthorizationHandler.cs:25-29`; `PermissionContracts.cs:10-11`; `PersistenceEntities.cs:81-92`; `PermissionGrantsController.cs:103-114`.
**Disposition:** Owner decision (V8) — chính sách quản trị, không phải lỗi kỹ thuật thuần.
**Lựa chọn:**
- (a) **Khuyến nghị.** Deny do master-admin ghi không bị non-master đảo: thêm `GrantedBy` (uuid, nullable) + `GrantedByMasterAdmin` (bool) vào `permission_grants`; non-master allow trên key có dòng master-issued mới nhất là deny → 403 `GRANT_OVERRIDES_MASTER_DECISION` (so với dòng master-issued mới nhất để chặn trò "tự deny rồi allow"); snapshot GET `/permissions/{userId}` trả `grantedBy`. Test chặn đảo feature/resource + master tự đảo deny của mình được.
- (b) Ngữ nghĩa phẳng: ai được cấp thì được đảo; vẫn ghi `GrantedBy` để truy vết.
- (c) Giữ như plan (không ghi người cấp); ghi nhận rủi ro ở phase 3.

### RT14 — Admin của org sở hữu không gỡ được membership của user org khác (hoặc không org) trên chính thiết bị mình — MEDIUM

**Reviewer:** security #4
**Location:** contract §4 `:121`, `:123`, `:128`; phase-03 `:153`, `:352`; phase-04 `:110`; phase-06 `:126`
**Flaw:** Luật target-user (home org ∈ effective orgs, null = ngoài phạm vi, không thì `USER_NOT_FOUND`) áp cho cả Remove. Master-admin bỏ qua luật này khi gán → user org khác/không org có membership trên thiết bị org O. Admin O thấy họ trong danh sách nhưng không gỡ được; resource deny không đụng membership.
**Failure scenario:** Master gán contractor C (home X hoặc không org) role Operator trên D của O. Hết hợp đồng, admin O không gỡ được; C còn điều khiển D tới khi tìm được master-admin. Lối khác: revoke D hoặc làm rỗng role chung.
**Evidence:** `IdentityUserManagement.cs:50-55`; `UsersController.cs:46-47`; `PermissionGrantsController.cs:108-109`.
**Disposition:** Owner decision (V9) — hành vi giống controller hiện tại (top-down), skeptic khuyến nghị để owner chọn.
**Lựa chọn:**
- (a) **Khuyến nghị.** Admin có `CanManageAccess(D)` gỡ được membership đang có trên D bất kể home org của người giữ; vẫn cấm tự gỡ mình, giữ luật high-risk (Q7) và V4 (role ⊆ quyền mình). Gán/thay vẫn giữ luật target-org. Remove kiểm membership thay vì `users.Exists` (gỡ được cả membership mồ côi). DTO thêm `canRemove`. Đánh đổi: admin org con có thể gỡ membership của user org cha nếu role đó nằm trong quyền của admin con trên D.
- (b) Giữ top-down như plan: chỉ master-admin hoặc admin có phạm vi phủ home org của người giữ mới gỡ được; dòng của user ngoài phạm vi chỉ đọc.

## Bị bác ở bước xác minh (mức None, ngoài cap)

| Finding | Lý do bác |
|---------|-----------|
| failure #3 — "vực phát hành": operator chỉ có org scope mất quyền xem ngày release | Không có deployment/user production nào: khởi động ngoài Development ném `KMS_SECRET_ROOT_PROVIDER_REQUIRED` (`DependencyInjection.cs:104-107`, khoá bởi `ManagementApiTests.cs:525-551`); dev dùng InMemory xoá khi restart; brainstorm :145 coi grant hiện có là dữ liệu dev. Mất quyền xem của member thường là chủ ý Q9; nút thắt sub-admin đã mở ở V6. |
| failure #6 + assumption #6 — thứ tự migration/binary, Down xoá audit | Như trên, chưa có prod. Runbook, kiểm migration, rolling upgrade thuộc plan cha 260924-2044 phase 8 (đang làm). Phụ thuộc bảng do migration tạo không mới (epochs, policy_versions). Down xoá bảng mình tạo là tiền lệ repo. |
| scope #4 — trang chi tiết tải cả danh sách để đọc `state` | Lời gọi list có sẵn hôm nay và là nguồn duy nhất của header (không có GET /devices/{id}); plan không thêm request. Trạng thái `error` tường minh tốt hơn khung trống im lặng hiện tại. |
| scope #6 — sửa thiết bị cho member chưa có client | Owner đã duyệt `device.edit_basics`/`edit_product` (brainstorm §5.2); bỏ điểm chặn thì hai mã thành vô tác dụng. PUT full-replace đã khoá bởi test hồi quy. |
| security #8 — idempotency precheck chạy trước phân quyền | Không chạm được ở release này: mọi dòng catalog `DeniedUntilActionContract`, không bao giờ tới `ReserveAsync` → `FindPlan` luôn null. Kết quả cache đã trả sau kiểm quyền. Chia idempotency theo user là đổi contract §5. Chuyển thành đầu vào cho plan 261001 (thêm 1 dòng Next Steps). |

## Claim check — sửa sự thật (không có mức độ)

1. Phase 1 :24 — 30 chỗ dựng `CommandDefinition` (1 prod + 29 test), không phải 31. Kết luận (tham số tuỳ chọn ở cuối giữ mọi chỗ biên dịch) vẫn đúng.
2. Phase 3 :205 — 5 chỗ `new SendDeviceCommandHandler` trong `CommandBoundaryTests.cs` (:206, :216, :240, :336 + `new(` trong helper `CreateHandler` :516-525), không phải 4.
3. Phase 1 :30/:35 — firmware: chỉ dispatcher so `domain` phân biệt hoa thường (`cmd_descriptor.c:431-433`); handler dùng `cJSON_GetObjectItem` không phân biệt (`cmd_config.c:17,26,32`). Thứ ngăn lệch là admission strict-parse từ chối key trùng không phân biệt hoa thường (`cmd_security.c:395-407`). Adapter hiện ghi payload ở `data.params` (`HmsIdfProvisionalProtocolAdapter.cs:26-30`), firmware chờ `root.data.data` → ghi chú cho plan 261001: payload gửi đi phải giữ nguyên byte và đúng chữ hoa/thường như lúc server resolve quyền.
4. Contract §2 :64, phase-01 :264, :289 — hash kiểm ở lần resolve đầu, không phải lúc khởi động (gộp vào RT10).
5. Phase 3 :32, :101 — "Postgres upsert nguyên tử nên không trùng PolicyVersion" (gộp vào RT9).
6. Phase 5 :54, :271 — chi phí dựng actor "tương đương CanAccessDevice": gần đúng (thêm 1 query FeatureGrants, bớt 1–2 lần đọc org) → giữ câu, bỏ phần nói về cache (RT11).

## Ngoài phạm vi, nên làm riêng

- `IdentityUserManagement.ListAsync` (GET /users) có cùng lỗi query lồng trên Npgsql (RT8). Sửa 1 dòng (`ToListAsync` trước vòng `GetRolesAsync`), không thuộc plan này.

## Quyết định của owner (2026-10-05)

- Áp dụng toàn bộ 11 finding Accept và phần chung của RT1.
- V7 → (a) endpoint gỡ role chỉ master-admin (với mọi role); ghi thành contract §0 Q11. Khi áp, lỗi "gỡ master-admin cuối cùng" theo đúng mẫu assign: 400 `{errors:["LAST_MASTER_ADMIN"]}`, không phải 409 như phương án (a) ở trên ghi.
- V8 → (a) deny do master-admin ghi không bị non-master đảo, grant ghi người cấp; ghi thành Q12.
- V9 → (a) admin có `CanManageAccess` gỡ được membership của member org khác/không org; ghi thành Q13.

Đã áp vào contract, plan.md và phase 1–6 với dấu `[RTn]`; xem mục Red Team Review trong plan.md.

## Câu hỏi chưa giải quyết

- V4 vẫn mở (validate); RT2 (luật sửa role) đi theo V4.
