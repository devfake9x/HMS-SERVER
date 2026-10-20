---
title: "Phân quyền lệnh thiết bị theo what + where"
description: "Vai trò theo tổ chức + membership theo thiết bị, catalog quyền v2 từ đặc tả MQTT §11, kiểm quyền lệnh theo (type, action), uỷ quyền tập con, 404/403, thu hồi realtime, UI vai trò và thành viên."
status: in-progress
priority: P1
branch: ""
mode: hard
tdd: true
scope: hold
tags: [feature, backend, frontend, database, api, auth, critical]
blockedBy: []
blocks: [261001-0030-full-hardware-mqtt-apis-c2]
supersedes: [260930-2200-device-permissions-datacake-parity]
sourceReport: plans/reports/261004-2234-brainstorm-device-permissions-what-where.md
created: 2026-10-05
---

# Phân quyền lệnh thiết bị theo what + where

## Overview

Thiết bị không tự kiểm quyền (mqtt-api.md R11) nên server là cửa duy nhất. Plan này thay mô hình `DeviceActionGrant` (khoá theo tên action trần, append-only, cho tự cấp `*`) bằng: catalog quyền v2 lấy từ §11 của đặc tả MQTT `HMS-IDF/engineering-docs/api/mqtt-api.md` @ `83e93d77e` (46 mã firmware + 4 mã server; không đọc code firmware — Q14), vai trò do admin từng tổ chức tự tạo, membership một vai trò/người/thiết bị, uỷ quyền tập con, quyền rủi ro cao chỉ master-admin. Không bật transport MQTT.

Hợp đồng bắt buộc: [design-contract.md](./reports/design-contract.md). Bản đồ code: `reports/scout-*-surface-map.md`.

## Phases

| Phase | Name | Status | Depends |
|-------|------|--------|---------|
| 1 | [Catalog v2 và từ vựng quyền](./phase-01-catalog-v2-permission-vocabulary.md) | Completed (2026-10-05) | — |
| 2 | [Vai trò, membership và persistence](./phase-02-device-roles-memberships-persistence.md) | Completed (2026-10-05) | — |
| 3 | [Kiểm quyền thiết bị và uỷ quyền](./phase-03-access-evaluation-and-delegation.md) | Pending | 1, 2 |
| 4 | [Chặn HTTP và endpoint mới](./phase-04-http-enforcement-and-endpoints.md) | Pending | 3 |
| 5 | [Chặn realtime và thu hồi tức thì](./phase-05-realtime-access-enforcement.md) | Pending | 3 |
| 6 | [Frontend: vai trò, thành viên, gate theo thiết bị](./phase-06-frontend-roles-members-gating.md) | Pending | 4, 5 |

Phụ thuộc thật: 1, 2 → 3 → 4, 5 → 6. Các phase dùng chung 2 project test .NET và bộ test frontend, nên trong một working tree chạy tuần tự 1 → 2 → 3 → 4 → 5 → 6; chỉ song song khi tách worktree. Chỉ phát hành sau phase 6 (phase 4–6 cùng một release): trước phase 5 hàng rào PolicyVersion cũ còn làm rớt sự kiện realtime, trước phase 6 frontend cũ còn gọi endpoint đã xoá.

Trước mọi lần deploy lên DB đã có dữ liệu [RT1]: chạy truy vấn read-only dưới đây để liệt kê deny feature/resource thuộc tài khoản master-admin. Sau release các deny này không còn tác dụng (Q4), nên owner phải xử lý từng tài khoản: trước deploy bằng SQL (xoá dòng `AspNetUserRoles` của role `master-admin` và đổi `SecurityStamp` của tài khoản đó), hoặc ngay sau deploy bằng endpoint gỡ role (Q11) — endpoint này ra cùng release nên không dùng được trước deploy.

```sql
SELECT g."UserId", g."Kind", g."Permission", g."OrganizationId", g."PolicyVersion"
FROM permission_grants g
JOIN "AspNetUserRoles" ur ON ur."UserId" = g."UserId"
JOIN "AspNetRoles" r ON r."Id" = ur."RoleId" AND r."NormalizedName" = 'MASTER-ADMIN'
WHERE g."Deny" AND g."Kind" IN ('feature', 'resource');
```

## Cross-Plan Dependencies

| Relationship | Plan | Ghi chú |
|---|---|---|
| Blocks | `261001-0030-full-hardware-mqtt-apis-c2` | Phase 01 (catalog/RBAC) và 02 (transport) phải dùng mô hình quyền này |
| Supersedes | `260930-2200-device-permissions-datacake-parity` | Ma trận 4 quyền + `DevicePermissionsController` bị thay bằng membership |

## Acceptance Criteria

- [x] Catalog v2 khớp §11 đặc tả `mqtt-api.md` @ `83e93d77e`: 94 dòng, 46 mã quyền, 6 domain, 24 dòng TTL 60000, 3 confirm template; hash v2 phủ mọi trường của §11; generator chỉ đọc tài liệu đặc tả, không đọc code firmware (Q14).
- [ ] Vai trò `adc.view` gọi `sms.get_status`/`mqtt.get_status` → 403 `DEVICE_PERMISSION_REQUIRED` (lỗi 1).
- [ ] Gỡ rồi gán lại membership → lệnh chạy lại được (lỗi 2); feature grant latest-wins, cùng version thì deny thắng [RT9].
- [ ] `devices.manage` không còn cấp quyền; admin con không tự cấp, không cấp vượt quyền mình (kể cả qua phạm vi org hay feature của người nhận [RT4]), không cấp vai trò có quyền rủi ro cao, không thu nhỏ vai trò vượt quyền mình [RT2] (lỗi 3); gán vai trò phải khớp phiên bản vai trò đã xem [RT3]; `PermissionGrantsController` không còn tự cấp `*`.
- [ ] Master-admin gỡ được role `master-admin` của người khác, không gỡ được master-admin cuối cùng; cookie người bị gỡ bị từ chối trong ≤ 1 phút, realtime rớt trong một tick; deny nhắm master-admin bị từ chối (Q11) [RT1].
- [ ] Non-master không đảo được deny do master-admin ghi; mọi grant mới ghi người cấp (Q12) [RT13].
- [ ] Admin của org sở hữu thiết bị gỡ được membership của user org khác hoặc không org trên thiết bị đó (Q13) [RT14].
- [ ] Ingest telemetry chỉ master-admin ở v1; member có `device.record_measurements` không giả được trạng thái thiết bị [RT5].
- [ ] Thiết bị ngoài phạm vi org → 404 ở devices, telemetry, realtime-authorization, memberships, commands, provisioning, licenses, hub.
- [ ] Member chỉ có membership (không có resource grant) vẫn thấy và dùng đúng thiết bị đó theo vai trò; không thấy thiết bị khác.
- [ ] Tên vai trò trùng không phân biệt hoa/thường trong cùng org → 409; xoá vai trò cần `expectedVersion`; mọi id trong DTO mới là Guid trần.
- [ ] Gỡ membership hoặc sửa vai trò → SignalR bị ngắt trong một tick, client nhận đúng reason; người còn quyền tự join lại (thử lại có giới hạn, rồi tải lại telemetry); gán mới không ngắt ai [RT7]; lỗi refresh phiên không hiện màn "mất quyền" [RT6].
- [ ] Thay đổi quyền ở thiết bị A không làm rớt sự kiện của thiết bị B (bỏ hàng rào PolicyVersion toàn cục).
- [ ] `dotnet test Hms.Server.slnx` xanh; `npm run build` và `npm test` (frontend) xanh.

## Dependencies

- Đặc tả `HMS-IDF/engineering-docs/api/mqtt-api.md` @ `83e93d77e` chỉ đọc (generator dùng `git show` tài liệu, không đụng working tree, không đọc code firmware — Q14). Mảng MQTT ngoài §11 tạm dừng; khoảng trống đặc tả G1–G4 ở design contract §13, chưa gửi bên firmware.
- Câu hỏi validation V1–V6 đã chốt ở Validation Session 1 (2026-10-05) — design contract §11 và mục Validation Log bên dưới.
- Quyết định red team Q11–Q13 (owner chốt 2026-10-05), design contract §0.
- master-admin toàn quyền vô điều kiện (deny không áp lên master-admin) — quyết định từ Q4, ghi ở design contract §3; hạn chế bằng endpoint gỡ role (Q11).

## Red Team Review

### Session — 2026-10-05

**Findings:** 30 thô → 19 sau gộp trùng; 14 được phân xử (11 Accept, 3 owner quyết → cả 3 Accept theo khuyến nghị), 5 Reject.
**Severity breakdown:** 0 Critical, 1 High, 13 Medium; 5 bị skeptic hạ xuống None.
**Report:** [from-code-reviewer-to-planner-red-team-adjudicated-findings-report.md](./reports/from-code-reviewer-to-planner-red-team-adjudicated-findings-report.md). Trong file plan, `[RTn]` đánh dấu chỗ sửa theo finding RTn.

| # | Finding | Severity | Disposition | Applied To |
|---|---------|----------|-------------|------------|
| RT1 | Không còn cách chặn master-admin; lối "gỡ role" plan viện dẫn không tồn tại | High | Accept — owner Q11: endpoint gỡ role chỉ master-admin | Contract §0/§3/§4/§6/§9/§10; phase 3, 4, 5, 6; plan.md |
| RT2 | Sửa role vượt rào V4 (thu nhỏ/làm rỗng role vượt quyền mình) | Medium | Accept | Contract §4/§11; phase 3, 4 |
| RT3 | Gán role không ràng buộc phiên bản role | Medium | Accept | Contract §4/§6/§9; phase 3, 4, 6 |
| RT4 | Feature/resource grant bỏ qua phạm vi và feature của người nhận | Medium | Accept | Contract §4; phase 3; plan.md |
| RT5 | Ingest telemetry giao được cho người (giả trạng thái thiết bị) | Medium | Accept — v1 chỉ master-admin | Contract §1/§6/§10; phase 1, 4; plan.md |
| RT6 | Lỗi refresh principal tạm thời → `ACCESS_REMOVED` kết thúc | Medium | Accept — đổi sang `REALTIME_RESYNC_REQUIRED` | Contract §7/§9; phase 5, 6 |
| RT7 | Tín hiệu epoch: evict mọi viewer, join lại một phát, lỗi sau commit, ghi epoch tuần tự | Medium | Accept | Contract §4/§7/§9; phase 2, 3, 5, 6; plan.md |
| RT8 | Memberships GET: query lồng lỗi trên Npgsql + 2N+1 | Medium | Accept (nửa "lộ email" bị bác) | Contract §4/§6; phase 4 |
| RT9 | Bộ đếm PolicyVersion Postgres lệch, không tie-break | Medium | Accept — hardening rẻ | Contract §4/§8; phase 2, 3 |
| RT10 | Generator sao chép bảng escape STJ cho đầu vào không tồn tại | Medium | Accept — guard charset (Option 2) | Contract §2; phase 1 |
| RT11 | Cache DeviceAccess theo tick | Medium | Accept — bỏ cache | Contract §7; phase 5 |
| RT12 | Endpoint mã quyền thiếu nhãn, có `source` không ai đọc | Medium | Accept — thêm `label`, hiển thị `commands`, bỏ `source` | Contract §6/§9/§10; phase 1, 4, 6 |
| RT13 | Admin ngang hàng đảo được deny của master-admin; grant không ghi người cấp | Medium | Accept — owner Q12 | Contract §0/§4/§6/§8; phase 2, 3, 4, 6 |
| RT14 | Admin org sở hữu không gỡ được member org khác | Medium | Accept — owner Q13 | Contract §0/§4/§6/§9; phase 3, 4, 6 |
| — | "Vực phát hành" cho operator chỉ có org scope | None | Reject — chưa có deployment production; mất quyền xem là chủ ý Q9 | — |
| — | Thứ tự migration/binary, Down xoá audit | None | Reject — runbook thuộc plan cha 260924-2044 phase 8 | — |
| — | Trang chi tiết tải cả danh sách thiết bị | None | Reject — lời gọi có sẵn, là nguồn duy nhất của header | — |
| — | Sửa thiết bị cho member chưa có client | None | Reject — mã đã được owner duyệt cần điểm chặn | — |
| — | Idempotency precheck chạy trước phân quyền | None | Reject — không chạm được ở release này; chuyển cho plan 261001 | Phase 3 Next Steps |

Claim check (sửa sự thật, không mức độ): số call site `CommandDefinition` (30), số site `new SendDeviceCommandHandler` (5), cơ chế firmware chống lệch `domain` và vị trí payload của adapter, hash kiểm ở lần resolve đầu chứ không lúc khởi động, PolicyVersion chỉ duy nhất sau khi căn bộ đếm, chi phí dựng actor.

### Whole-Plan Consistency Sweep

Delta quyết định đã áp: Q11–Q13 vào contract §0; gỡ role + đổi stamp + `ValidationInterval` 1 phút; luật grant mới (phạm vi người nhận, deny master-admin bền, deny nhắm master-admin bị từ chối, ghi người cấp); `expectedRoleVersion`; `canRemove` + gỡ member ngoài phạm vi; luật sửa role `(old ∪ new)`; ingest chỉ master-admin; refresh lỗi → `REALTIME_RESYNC_REQUIRED`; gán mới không advance epoch, `AdvanceMany`, tín hiệu sau commit best-effort, join lại có thử lại; `ListDirectoryAsync`; migration căn bộ đếm + tie-break deny; guard charset thay emulator STJ; bỏ cache theo tick; DTO mã quyền `{code, group, highRisk, label, commands}`.

Đã quét plan.md, design-contract.md và phase 1–6 tìm thuật ngữ cũ (`stjStringify`, bảng escape, `source`, `record_measurements` làm cổng ingest, `ACCESS_REMOVED` cho refresh lỗi, cache/`reread`, "20 mã", "9 method", "không tie-break", "31 call site", "lúc khởi động", `ListAsync` cho memberships, chữ ký DTO/handler cũ, `{roleId, expectedRoleId}`, `new ⊆ perms`, "gỡ role" không kèm endpoint). Lần quét sửa thêm: câu golden vector và risk "hash lúc khởi động" ở phase 1, chữ ký handler trong Key Insights phase 4, "20 mã" ở risk phase 6, và cách seed deny trên tài khoản master-admin ở test phase 3/4 (ghi thẳng store, vì handler nay từ chối).

Vòng 2 — một reviewer độc lập đọc trọn 8 file, tìm thêm 15 mâu thuẫn (1 chặn, 14 nhỏ), đã sửa hết:
- Chặn: contract §0 Q11 ghi endpoint gỡ role "chỉ master-admin" nhưng §6 ghi "cùng kiểm như assign" (non-master có `users.manage` gỡ được role thường). Đã chốt theo lựa chọn owner duyệt ("chỉ master-admin dùng"): chỉ master-admin, với mọi role; thêm test `Non_master_cannot_remove_any_role`.
- Nhỏ: mapping status grant ở bảng Modify phase 3; danh sách 5 mã grant phase 3 sở hữu; phase 5 nói `RemoveDeviceMembershipHandler` luôn cần `IUserScopeStore` (trái Q13); "9 endpoint" → 10; phase 1 Next Steps coi `DeviceRecordMeasurements` là mã chặn route; truy vấn audit trước deploy gợi ý dùng endpoint chưa tồn tại lúc đó; claim "mọi test khởi động app ép load catalog"; tên test catalog còn "startup"; câu "evict mọi subscriber với `AUTHORIZATION_CHANGED`" (người mất quyền nhận `ACCESS_REMOVED`); nghĩa của `ACCESS_REMOVED` nới thành "đánh giá truy cập bị từ chối sau refresh thành công"; "thử lại 3 lần" → "tối đa 3 lần thử, tính cả lần đầu"; dòng membership mồ côi trong memberships GET; `StatusFor` thiếu 3 mã grant; báo cáo red team ghi 409 cho `LAST_MASTER_ADMIN` (áp dụng là 400 `{errors}`).

Vòng 3 — cùng reviewer kiểm lại: cả 15 đã giải quyết; phát hiện thêm 1 lệch nhỏ do bản sửa (công thức `canEdit` của memberships GET thiếu vế "user có trong directory") và 1 tên test chưa đồng bộ — đã sửa.

Kết quả: 0 mâu thuẫn chưa giải quyết. V1–V6 đã chốt sau đó ở Validation Session 1 (xem Validation Log).

## Validation Log

### Session 1 — 2026-10-05
**Trigger:** `/ck:plan validate` sau red team; chốt 6 câu hỏi mở V1–V6 (contract §11) và xác nhận lại hệ quả của Q11.
**Questions asked:** 7

#### Verification Results
- Bỏ bước kiểm chứng lại theo guard: `## Red Team Review` đã có bằng chứng kiểm chứng đầy đủ (4 vai trò, mỗi finding có skeptic).
- Tag `[UNVERIFIED]` trong plan: 0.
- Kiểm thêm để trả lời V1/Q11: `config.set` domain `system` chỉ ghi `hostname` (lúc đó kiểm bằng cách đọc code firmware); trong `src` chỉ role `master-admin` được kiểm (`IsInRole`). Failed: 0. *(Đã thay ở Session 2: V1 nay dựa trên đặc tả §6.1, không dựa trên code firmware.)*

#### Questions & Answers

1. **[Assumptions]** V1: Có thêm quyền `sys.config_import` (chặn `config.set` domain `system`, firmware hiện chỉ đổi hostname, giống `system.set_hostname`/`sys.hostname_edit` không high-risk) vào nhóm high-risk chỉ master-admin cấp được không?
   - Options: Không thêm (Recommended) | Thêm vào high-risk
   - **Answer:** Không thêm
   - **Rationale:** Giữ nhóm high-risk 8 mã; hai quyền cùng tác dụng không bị xếp khác nhau. Firmware mở rộng domain `system` thì catalog sinh lại đổi hash và xét lại.
2. **[Architecture]** V2: Khi lệnh thiết bị bị từ chối vì thiếu quyền (`DEVICE_PERMISSION_REQUIRED`) hoặc thiết bị đã thu hồi (`DEVICE_REVOKED`), API lệnh trả HTTP gì?
   - Options: 403 kèm {code} (Recommended) | 200 kèm code
   - **Answer:** 403 kèm {code}
   - **Rationale:** Khớp quy tắc 404/403 (Q6) của mọi route thiết bị; các mã khác của lệnh vẫn 200 kèm code.
3. **[Risks]** V3: Migration của plan có xoá các dòng grant cũ `permission_grants` loại `device-action` (gồm cả grant `*` cũ, không còn code nào đọc sau phase 4) không?
   - Options: Xoá trong migration (Recommended) | Giữ lại
   - **Answer:** Xoá trong migration
   - **Rationale:** Dọn dữ liệu chết và grant `*` cũ; không đảo ngược nên backup trước khi chạy trên DB có dữ liệu.
4. **[Architecture]** V4: Admin không phải master-admin khi thay/gỡ membership hoặc sửa role: có kiểm cả role hiện tại (phải không high-risk và nằm trong quyền của chính admin trên thiết bị) không?
   - Options: Kiểm cả role hiện tại (Recommended) | Chỉ kiểm role mới
   - **Answer:** Kiểm cả role hiện tại
   - **Rationale:** Cấp dưới không hạ được quyền cấp trên cấp; Q13 (gỡ member org khác) và luật sửa role `(old ∪ new)` (RT2) dựa vào luật này.
5. **[Tradeoffs]** V5: Khi admin xoá một vai trò thiết bị đang được gán cho thành viên trên thiết bị nào đó, hệ thống làm gì?
   - Options: Chặn, trả 409 (Recommended) | Xoá kèm membership
   - **Answer:** Chặn, trả 409
   - **Rationale:** Không ai mất quyền ngoài ý muốn; giữ FK Restrict và `DEVICE_ROLE_IN_USE`.
6. **[Scope]** V6: Admin công ty con (có `permissions.manage` trong phạm vi) nhưng chưa có membership trên một thiết bị thì uỷ quyền trên thiết bị đó thế nào?
   - Options: v1 strict (Recommended) | Thêm role cấp tổ chức ngay | Bỏ luật tập con
   - **Answer:** v1 strict
   - **Rationale:** Đúng Q4 ("chỉ cấp quyền mình có") và phạm vi HOLD; đổi lại master-admin phải gán role cho admin trên từng thiết bị (chưa có API hàng loạt). Role cấp tổ chức để v2.
7. **[Tradeoffs]** Q11: Endpoint mới `DELETE /api/v1/users/{userId}/roles/{role}` hiện chỉ master-admin dùng với mọi role, trong khi người có `users.manage` vẫn gán được role thường (chỉ role `master-admin` mang quyền trong code). Giữ hay làm đối xứng với gán?
   - Options: Giữ chỉ master-admin (Recommended) | Đối xứng với gán
   - **Answer:** Giữ chỉ master-admin
   - **Rationale:** Đúng lựa chọn đã duyệt ở red team; lệch gán/gỡ chỉ với role thường, không ảnh hưởng bảo mật.

#### Confirmed Decisions
- V1: `sys.config_import` không high-risk — cùng tác dụng với `sys.hostname_edit` trên firmware ghim.
- V2: lệnh thiếu quyền/thu hồi → 403 kèm `{code}` — nhất quán Q6.
- V3: xoá dòng `device-action` trong migration — dọn grant `*` cũ.
- V4: luật role hiện tại đối xứng, gồm cả sửa role — bảo vệ quyền cấp trên cấp.
- V5: chặn xoá role đang dùng (409) — tránh mất quyền hàng loạt.
- V6: v1 strict — đúng Q4, role cấp tổ chức để v2.
- Q11: endpoint gỡ role chỉ master-admin, mọi role — giữ như đã duyệt.

#### Action Items
- [x] Đổi mọi chỗ "chờ validate" thành "đã chốt" ở contract (§1, §4, §11), plan.md và Open Questions của phase 1–6. Plan đã viết đúng các lựa chọn này nên không đổi hành vi, test hay file cần sửa.

#### Impact on Phases
- Phase 1: Open Questions — V1 đã chốt, test khoá `IsHighRisk("sys.config_import") == false`.
- Phase 2: Open Questions + risk row — V3 (xoá), V5 (chặn) đã chốt.
- Phase 3: Open Questions — V1, V4, V5, V6 đã chốt.
- Phase 4: Open Questions + Key Insights — V2 (403), V4–V6, Q11 đã chốt.
- Phase 5: Open Questions — V5 đã chốt, không cần test delivery thêm.
- Phase 6: Open Questions — V2, V6, Q11 đã chốt.

#### Whole-Plan Consistency Sweep
- Files checked: plan.md, reports/design-contract.md, phase-01 … phase-06 (tìm mọi chỗ nhắc V1–V6 và mọi cách viết "chưa chốt / còn mở / pending / chờ V / nếu V").
- Decision deltas checked: 7
- Reconciled stale references: 13 (contract 3, plan.md 2, phase 1: 1, phase 2: 2, phase 3: 1, phase 4: 2, phase 5: 1, phase 6: 1)
- Unresolved contradictions: 0

### Session 2 — 2026-10-05 (chỉ giao tiếp qua đặc tả)
**Trigger:** chủ dự án chỉ ra phase 1 đọc thẳng code firmware trong khi server và thiết bị giao tiếp qua MQTT; brainstorm `plans/reports/261005-1930-brainstorm-spec-sourced-command-catalog.md`.

#### Findings
- Đặc tả server-facing là `HMS-IDF/engineering-docs/api/mqtt-api.md`, phát hành ở chính commit ghim `83e93d77e`. §11 có bảng 94 cặp (class, permission, TTL, dry run, confirm) và bảng 6 domain; so với catalog đã sinh từ code: 0 khác biệt.
- Ba trường `audit_publish`, `accepts_body`, `outcome_deferred` chỉ có trong code, server không dùng, §11 không có.

#### Confirmed Decisions
- Q14 (design contract §0): server chỉ tích hợp qua đặc tả; catalog lấy từ §11; chỉ làm phần đặc tả đã có; tạm dừng mảng MQTT; khoảng trống G1–G4 ghi ở contract §13, chưa gửi.
- Làm tiếp phase 2–6 theo design contract.

#### Impact on Phases
- Phase 1: viết lại generator (đọc §11 thay vì C), catalog 9 trường, loader bỏ luật của 3 trường đã xoá, test theo đặc tả (xem phase file).
- Phase 4: nhãn mã quyền firmware dựa trên đặc tả (không có nhãn → dùng chính code, G4).
- Các phase khác: không đổi.
