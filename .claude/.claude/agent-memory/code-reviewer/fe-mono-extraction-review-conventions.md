---
name: fe-mono-extraction-review-conventions
description: Reviewing FE package-extraction + consumer-wiring refactors (auth-kit) in fe-mono — verbatim-move verification, stale vi.mock trap, consumer-wire checklist
metadata:
  type: feedback
---

Reviewing behavior-preserving package extractions AND consumer-wiring in `fe-mono` (plan 260616: Phase B extract auth-ui → `@reecotech/auth-kit`; Phase C wire admin-operator as CONSUMER).

**Why:** these refactors move many files verbatim + rewrite imports + add re-export barrels, then wire consumers. Type-check (`vp check`) and a green test count do NOT prove behavior preserved — highest-yield bugs are in test mocks, barrel completeness, and contract drift, which typecheck can miss.

**How to apply — extraction (Phase B) checklist that paid off:**
- **Verify "verbatim" claims with `diff <(git show HEAD:OLD) NEW`.** Moved files should differ ONLY in import paths (`@/x`→`../x`). Any logic diff = scrutinize.
- **Stale `vi.mock` after import repoint = silent dead mock (HIGH).** Component import repointed deep→barrel but test still `vi.mock("@/hooks/deep-path")` → mock is no-op, component runs REAL code; tests pass if assertions don't depend on it. Sweep: `grep -rn 'vi.mock("@/...deep-path' apps/*/src`.
- **Barrel re-export completeness:** `export *`→explicit named re-exports → enumerate source exports (`grep -nE '^export (const|function|type)'`) and diff vs explicit list.
- **query-key cache compat:** extracted block must be value-identical (TanStack hashes by value), incl. function-shaped keys.
- **peerDeps split:** stateful/context/singleton libs (react, react-dom, antd, @ant-design/icons, @tanstack/react-query, react-router, @simplewebauthn/browser) MUST be peer in the PACKAGE. In a consuming APP they are regular deps — NOT a violation (auth-ui + admin-operator both list @simplewebauthn/browser as a dep; that's the app pattern).
- **Pre-existing >200-line files** moved verbatim are NOT new violations.

**How to apply — consumer-wire (Phase C admin-operator) checklist that paid off:**
- **Composition root must be byte-faithful to auth-ui** except localized props (brand/title/redirect). `diff` axios/index.ts vs auth-ui — only comment-language should differ. Prior placeholder index.ts often already documents the planned wire-in; the rewrite should match its own prediction (no lost `baseURL`/allowlist/exported symbols).
- **M1 order guard non-tautological check:** test must import the REAL exported `middlewareChain` AND import the middleware fns from their source packages (`@reecotech/services`, `@reecotech/auth-kit`), asserting identity + index ordering. error-normalize MUST be `.at(-1)`; both retry mw indices < normalize index.
- **query-key prefix-match for invalidate:** app's `meKeys.all=["me"]` must prefix-match auth-kit internal `me.all=["me"]`/`me.profile=["me","profile"]` so `<RequireAuth queryKeyMe>` invalidation hits the kit's cache. Verify auth-kit `query-keys.ts`.
- **YAGNI deviation is intentional, not a gap:** admin-operator mounts ONLY `<StepUpGate/>`, NOT `<SelfStepUpGate/>` (auth-ui mounts both) — correct: no self-service flow yet. Confirm via grep that SelfStepUpGate appears ONLY in an explaining comment.
- **Acyclic dep graph:** auth-kit src must have ZERO `from "@/"` and zero real import of the consumer app (comment mentions OK). Consumer → kit only.
- **Contract types:** header binds `me?.account.username/email` — confirm `Me.account` schema (packages/schemas/src/me) actually has those fields; RequireAuth/LoginScreen/StepUpGate prop shapes match auth-kit exports.
- **withCredentials:** lives in `createApiInstance` (default true) — refresh cookie works without app setting it; don't flag "missing withCredentials" in app composition root.
- **Placeholder sweep:** grep changed files for prior placeholders ("Quản trị viên"/"Chưa nối auth"/TODO/FIXME) to confirm they're gone.
