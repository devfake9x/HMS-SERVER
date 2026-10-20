---
name: auth-ui-sidebar-derive-conventions
description: auth-ui sidebar does NOT derive groups from RouteMeta.group — uses hardcoded SECTIONS array in sidebar-menu-items.ts keyed by ROUTE_PATHS; silent-drop trap for routes not listed
metadata:
  type: project
---

auth-ui sidebar grouping is HARDCODED, not derived from route meta.

`src/layouts/app-layout/sidebar-menu-items.ts`:
- `SECTIONS: SidebarSection[]` = explicit array of `{ label, keys: ROUTE_PATHS[] }`. Section→route mapping is hand-maintained (comment line ~9-14 even says "hard-coded here (no route-meta change)").
- `sidebarMenuEntries` derives flat entries from `appRoutes` (filter `isSidebarMeta`, sort by `order`), but ONLY label/icon/order/permission — NOT any `group` field.
- `buildGroupedMenuItems(visible)` builds antd Menu groups by looking up each section's `keys` in the visible-entry map. **A route whose path is NOT in any SECTIONS.keys is silently dropped from the sidebar** even if it has valid sidebar meta.
- `RouteMeta` (`src/routes/route-meta-types.ts`) = `SidebarRouteMeta & { permission? }`. There is NO `group` field; SidebarRouteMeta (ui-core) = label/icon/order only.

**Why:** matches the admin-operator-ui silent sidebar-drop trap ([[admin-operator-ui-rebuild-review-conventions]]) — adding a route with a new group via meta alone does nothing; you must also edit SECTIONS.

**How to apply:** any plan claiming "sidebar derives from RouteMeta.group, add group?: string and group-by meta.group" is factually wrong for auth-ui as of 2026-06-18 — it describes a refactor, not the current code. Flag as fact-check failure + a real silent-drop risk if the SECTIONS edit is forgotten.

Other verified auth-ui facts (2026-06-18):
- `src/axios/middlewares/` directory does NOT exist (README claims it; it's aspirational). axios composition root = single `src/axios/index.ts` with exported `middlewareChain` array.
- axios request interceptors run LIFO (reverse registration). `middlewareChain` order: traceparent, auth-token, step-up-token, [response:] step-up-retry, auth-refresh-retry, error-normalize. A request interceptor registered AFTER another runs its request phase BEFORE it.
- No `vite-env.d.ts` / no `*.d.ts` anywhere in app; `import.meta.env.VITE_*` used untyped today (main.tsx:13, axios/index.ts:55).
- `API_PREFIX = "/api/v1"` (@reecotech/constants). Real service calls are `/api/v1/...`; auth-refresh SKIP_REFRESH_PATHS + step-up all key off `/api/v1` + `/admin/` substrings.
- Zero `/analytics` endpoints in any be-mono service.
