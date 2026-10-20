---
name: be-mono-workspace-review-conventions
description: be-mono Bun workspace — dep-completeness checklist, shared exports-map ./* greedy resolution, catalog pins, bunfig relative preload, git-rename-as-add gotcha
metadata:
  type: project
---

BE consolidated into `be-mono/` Bun workspace (commit 647d267 era, plan 260616-1434): 3 services (`services/{auth,admin-operator,background-service}`) + `packages/shared` (`@reecotech/shared`, real `workspace:*` dep replacing old `@shared/*`/`@admin-shared/*`/`@events/*` tsconfig path aliases).

**Why:** workspace isolation surfaces phantom deps — a package can import a bare module it never declared, and typecheck still passes (TS resolves types transitively) while Bun runtime cannot find the value module.

**How to apply when reviewing be-mono dep changes:**
- Dependency-completeness is the #1 risk. For each member, extract every bare import in `src` (`grep -rhoE "(from|import) ['\"][^'\"]+['\"]"` → strip relative/`@reecotech/shared`/`node:`/`bun:`) and cross-check against that member's own package.json deps+devDeps. typecheck passing ≠ runtime-resolvable.
- Watch the catalog: `be-mono/package.json` `catalog:` block is the single version source; services use `"dep": "catalog:"`. When a dep is catalog-ized, confirm catalog value == the original per-service pin (no silent major/minor drift).
- `@reecotech/shared` exports map uses `"./*": "./*.ts"` — greedy `*` captures multi-segment paths, so `@reecotech/shared/events/write-event` → `./events/write-event.ts`. Deep imports (e.g. `telemetry/trace-postgres` internal `_activeSpans`) resolve this way and ARE empirically exercised by bg tests. Map is permissive (exposes all `.ts`); explicit `./events`/`./authz`/`./telemetry` barrels are partly redundant but correct.
- bg has TWO local `src/shared/{queue-names,job-schemas}` files imported RELATIVE (`../shared/...`) — these are bg-local, must NOT be rewritten to the package. Distinct from `@reecotech/shared`.
- OTel version matrix: SDK pkgs pinned exact (api 1.9.0, *-otlp-http + sdk-* 0.57.0, core/resources/sdk-metrics/semantic-conventions/sdk-trace-base 1.30.0). Keep consistent across shared + bg.
- bunfig.toml preload paths are RELATIVE to the bunfig location (`./src/telemetry/instrumentation.ts`); valid because bunfig moves with its service `src/`. `[test]` preload is separate (top-level preload does NOT apply to `bun test`).
- tsconfig: services `extends ../../tsconfig.base.json`; base holds the 9 shared compiler opts incl `jsx: react-jsx` + `jsxImportSource: hono/jsx`. auth+bg keep `exclude: ["src/tests"]`; admin-operator has none (matches pre-migration); shared adds `noEmit: true`.

**git gotcha:** the move was add+delete, NOT a rename git detects (similarity dropped). `git diff` shows moved files as full `+` additions. To isolate the REAL edit, diff content directly: `diff <(git show OLD:auth/src/X) <(git show HEAD:be-mono/services/auth/src/X)` — the only changes should be import-path + matching doc-comment rewrites.
