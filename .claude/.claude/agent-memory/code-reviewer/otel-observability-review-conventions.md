---
name: otel-observability-review-conventions
description: Quy ước review cho plan 260613-1452-opentelemetry-infra-adoption (OTel) — kill-switch pattern, test-DB migration gap, porsager span-leak, dead-code seams
metadata:
  type: feedback
---

Review OTel feature (branch `feat/auth-authz-policy-admin`, plan `plans/260613-1452-opentelemetry-infra-adoption/`):

- **Kill-switch dùng `z.stringbool()`** cho `OTEL_ENABLED`, KHÔNG phải `z.coerce.boolean()` — đã đúng, đừng flag. Runtime guard trong telemetry đọc thẳng `process.env['OTEL_ENABLED'] !== 'false' && !== '0'` (preload chạy trước env.ts parse) — hai parser cho cùng flag là CHỦ Ý, không phải bug.
- **auth tests chạy trên LIVE Postgres/Redis** (`auth_db` @ localhost:5432, redis localhost:6379), KHÔNG mock. Test harness KHÔNG tự chạy migration. Khi schema thêm cột mà migration chưa apply → hàng loạt test fail `column ... does not exist`. Đây là regression THẬT của branch, không phải infra flake. Cách verify: `psql ... ALTER TABLE events ADD COLUMN IF NOT EXISTS trace_context jsonb` rồi re-run; nhớ DROP COLUMN khôi phục sau.
- background-service tests dùng fake consumer/dedup/router (in-memory) — chạy không cần infra. shared tests thuần unit.

Bug pattern THẬT đã phát hiện (verify lại trước khi nhắc — có thể đã fix):
- **porsager driver-seam span leak** (`shared/telemetry/trace-postgres.ts`): `debugHook` mở span + `activeSpans.set(connId, span)` nhưng porsager `debug` chỉ fire TRƯỚC query (không có hook kết thúc). `wrapQuerySpan` (closer duy nhất per-query) KHÔNG được gọi ở đâu. `onclose` chỉ fire khi connection đóng. → trên pooled connection (`max:5`/`max:10`) mọi query sau cái đầu ghi đè span cũ không `.end()` = leak + telemetry sai. Ảnh hưởng CẢ auth + background-service db client.
- **Dead seams**: `wrapQuerySpan` và `tracedFetch` export-but-unused. `@opentelemetry/auto-instrumentations-node` là dep khai báo nhưng KHÔNG dùng (chỉ nhắc trong comment) — kéo về cả cây 0.200.0/2.0.0 làm version noise.
- **Code-comment rule**: nhiều ref `RT-10/RT-11/RT-6/RT-7/RT-M` trong comment source (create-telemetry-sdk.ts, instrumentations files, middleware, env.ts) — vi phạm rule cấm plan-artifact trong code.

**Why:** task review 2026-06-16 yêu cầu correctness/regression/maintainability lane (security có reviewer riêng).
**How to apply:** với mọi schema change → check migration đã apply vào test DB chưa TRƯỚC khi kết luận test pass; với driver-seam instrumentation → trace lifecycle close path THẬT, đừng tin test gọi `_closeSpan` thủ công.
