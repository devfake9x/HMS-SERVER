---
name: fe-auth-kit-extraction
description: fe-mono auth-domain bóc ra @reecotech/auth-kit (headless . + ui ./ui); ui-core generic-only; composition root order giữ ở app
metadata:
  type: project
---

Plan `plans/260616-fe-shared-extraction/` — phần "bóc dần sau" của [[admin-operator-ui-design / 260616-0935]].

Quyết định CHỐT với user (KHÔNG re-litigate):
- Package MỚI `@reecotech/auth-kit`, 2 entry: `.` (headless: stores, 4 axios mw, hooks, runStepUp, useAuthGuard/RequireAuth) + `./ui` (LoginScreen, StepUpMfaModal+Gate, SelfStepUpModal+Gate). ui-core VẪN generic-only (C1 từ plan 0935 — KHÔNG đảo).
- ui-store là GENERIC → factory `createUiStore(persistKey)` đặt ở **ui-core**, KHÔNG auth-kit.
- admin-operator: login riêng (share `<LoginScreen>` parametrize), cần admin step-up (mount StepUpGate Phase C); self-step-up = future.
- Graph PHẢI acyclic: `{types,schemas,services,constants,ui-core}→auth-kit→apps`. auth-kit KHÔNG import `@/*`; inject `queryKeys.me` + redirect path qua props/param.

**Why:** DRY — admin-operator scaffold đang copy gần như nguyên shell auth-ui (layout + login + axios). auth-kit + AppShell adopt cắt duplicate.

**How to apply:** Composition root axios (order invariant: request traceparent→auth-token→step-up-token; response step-up-retry→auth-refresh-retry→error-normalize) GIỮ Ở APP — auth-kit chỉ export 4 mw rời; M1 order-guard test ở app. HTTP services auth ĐÃ ở @reecotech/services → auth-kit KHÔNG ôm HTTP. 3 phase: A (AppShell+ui-core helpers, low risk) → B (tách auth-kit, auth-ui adopt trước, test xanh = tách đúng) → C (admin-operator wire, BLOCKED tới khi be-mono auth ready — KHÔNG mock).
