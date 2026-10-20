---
name: auth-service-review-conventions
description: Quy tắc review cho plan 260518-1629-init-auth-service — adjustment đã duyệt và risk đã accept KHÔNG được flag lại
metadata:
  type: feedback
---

Khi review code vs plan `plans/260518-1629-init-auth-service/`:

- **KHÔNG flag lại** các "Điều chỉnh so với plan" đã ghi trong plan.md § Validation Log (Session 8-13) — chúng là ĐÃ DUYỆT. Ví dụ: sessions table nằm trong `schema/sessions.ts`, `AuthedUser.sid` optional, JWT bỏ `permissions[]` (Phase 7), logout revoke-by-sid (Phase 15), bulk `PUT /rbac-matrix` (Phase 16).
- **KHÔNG flag** risk đã accept trong plan: in-memory/single-instance rate-limit (nay đã lên Redis), RS256 private key plaintext trong DB dev, audit fail-silent.
- Phase 10 (alert email, cleanup cron, CSRF doc) + Phase 11 (tests/docs/README đầy đủ) CHƯA cook — thiếu các thứ đó là EXPECTED, không phải finding.
- Mọi finding phải trace tới failure mode THẬT (threat model), không phải lo ngại trừu tượng.

**Why:** user yêu cầu rõ trong task review 2026-06-11; tránh noise khi audit lại quyết định đã verify (khớp rule `review-audit-self-decision.md` toàn cục).
**How to apply:** đọc plan.md Validation Log TRƯỚC khi phân loại finding; mọi deviation tìm thấy → check log/phase-file § Điều chỉnh trước khi flag.

Trạng thái mở đáng nhớ (đến 2026-06-11): carry-over Phase 15 M1 — `password-reset-service.reset()` + `changePassword` phải đổi `revokeAllForAccount` → `revokeAllSessions`. Review p1-3 xác nhận VẪN MỞ (changePassword thậm chí không revoke gì) — báo cáo tại `reports/review-260611-core-auth-p123.md` (H1). Verify lại code trước khi nhắc lại — có thể đã fix sau thời điểm này.

Cập nhật từ review P15/16/4a/4b/12 (2026-06-11):
- Carry-over EXPECTED thêm: test hành vi Phase 15 (tombstone NX race…) → Phase 11; `email.setup_account` template/route trong background-service (Phase 8 carry-over) — xác nhận CHƯA làm; E2E reload xuyên Redpanda/Sequin thật → Phase 11.
- H2 Phase 16 (consumer không reconnect) = mitigation ĐÃ CHỐT là poll fallback — KHÔNG đề xuất reconnect.
- Bug pattern lặp lại trong repo: `z.coerce.boolean()` cho env flag coi chuỗi "false" là `true` (Boolean("false")===true) — soi mọi boolean env mới ở `auth/src/config/env.ts` (AUTHZ_RELOAD_ENABLED, EMAIL_VERIFY_REQUIRED, COOKIE_SECURE).
