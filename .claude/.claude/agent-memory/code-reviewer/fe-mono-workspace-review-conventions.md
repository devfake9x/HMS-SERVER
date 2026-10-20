---
name: fe-mono-workspace-review-conventions
description: fe-mono FE workspace facts for review — pnpm workspace, package names, ui-core domain-free invariant, jsdom test env mechanism, vp check toolchain
metadata:
  type: project
---

fe-mono is a pnpm workspace (separate from be-mono which is Bun). React 19 + antd 6 + Vite+ (`vp` CLI = vite-plus).

**Package names (for `pnpm --filter`):**
- `@reecotech/ui-core` (packages/ui-core) — design tokens + shared shell/providers/helpers
- `auth-ui` (apps/auth-ui) — NOT `@reecotech/auth-ui`
- `admin-operator-ui` (apps/admin-operator) — NOT `admin-operator`
- Run typecheck+lint+format in one shot: `pnpm --filter <name> check` (alias for `vp check`).

**ui-core domain-free invariant (load-bearing for acyclic dep graph):**
- ui-core may import ONLY `@reecotech/types` (workspace) + zustand/dayjs (deps) + antd/tanstack/react-router/react (peer/dev).
- It must NOT import `@reecotech/{schemas,constants,services}`, app aliases `@/*`, or auth-kit. Verify with grep on `from "@reecotech/` excluding types, and `from "@/`.
- **Why:** keeps ui-core reusable across apps without pulling app/domain code → no cycles.

**jsdom test env mechanism:** each test file declares `// @vitest-environment jsdom` as its FIRST line (pragma). Test-utils file dedup does NOT affect env — the pragma lives per-test-file, not in the shared helper. Don't flag "lost jsdom env" when test-utils moves.

See also [[be-mono-workspace-review-conventions]] for the BE (Bun) side.
