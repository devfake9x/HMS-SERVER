---
name: admin-operator-ui-rebuild-review-conventions
description: admin-operator UI rebuild (plan 260616-admin-operator-ui-rebuild) — engine teardown to plain pages; the architecture-boundary invariant and the silent sidebar-drop trap to check on this app
metadata:
  type: project
---

Plan `260616-admin-operator-ui-rebuild` (branch `refactor/fe-mono-extraction`): removed the config-driven collection engine (`src/admin/**`, `src/collections/`) and rebuilt admin-operator as plain explicit pages. This pattern is now LOCKED — review future changes against it.

**Architecture boundary (the whole point of this refactor — guard it):**
- Allowed: `fetchList(endpoint)` util (explicit endpoint string), per-entity `useXxxList()` react-query hook, per-entity list page with hand-written `columns`, route registry as a hand-written `RouteObject[]`.
- Banned (= the engine they tore out): `renderForm(config.fields)`, `buildRoutes(config)`, `FieldConfig`/`CollectionConfig` discriminated union, any registry that reads data-as-config to generate UI. Flag any agent that re-introduces a disguised generic engine.

**Per-entity invariant (17 entities):** for slug `<s>` — service file `src/services/<s>.ts` with `const ENTITY="<s>"`, `const ENDPOINT="/api/<s>"`, hook `use<Pascal>List`; page `src/pages/<group>/<s>-list-page.tsx`; route `{ path:"<s>", element, handle: meta(label, group, order) }`. ENTITY === slug === endpoint suffix === route path. Query keys come from `entityKeys(ENTITY)` (per-name, no slug-generic) → keys are unique across entities because name differs.

**Silent sidebar-drop trap (the #1 bug class here):** sidebar is DERIVED from `appRoutes` in `sidebar-menu-items.ts` — it `.filter(e => e.group === g)` over `SIDEBAR_GROUPS` (in `src/constants/index.ts`). If a route's `handle.group` is NOT one of SIDEBAR_GROUPS, the route silently vanishes from the sidebar (no error, tsc passes because `group?: string` is widened to plain string in `RouteMeta`). When reviewing added routes, always cross-check `group` literal ∈ SIDEBAR_GROUPS. Group/order pairs must be unique per group or items overlap in sort.

**RouteMeta typing gap (latent):** `RouteMeta.group` is `string` not `SidebarGroup` union (route-meta-types.ts). So a typo in `group` is NOT caught by tsc — only the silent drop above. Tightening to the union would make it compile-time safe; left loose so far.

**DataTable contract (ui-core):** props `{ columns, rows: T[]|undefined, rowKey: string|(r)=>string, loading?, error?: unknown, onRetry?, emptyText? }`. `pagination` defaults to `false` (unpaginated) — fine while BE pending, but high-volume entities (audit-logs, products, inventory) will render every row once data lands. ErrorState only surfaces `error.message` / NormalizedApiError → no stack-trace leak.

**rowKey edge case:** pages use `asText(r.id ?? r.public_id ?? r.code)` (asText coerces non-string/number → ""). If a real row lacks all fallback fields, key="" for every such row → React duplicate-key warning. Latent until BE shape is known; not a Phase-1 blocker.

See also [[fe-mono-workspace-review-conventions]], [[fe-mono-extraction-review-conventions]].
