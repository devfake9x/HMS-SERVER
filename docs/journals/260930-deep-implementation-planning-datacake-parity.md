---
title: "Deep implementation planning for Datacake Parity UI synchronization"
date: 2026-09-30
status: active
---

# Deep Implementation Planning for Datacake Parity UI Synchronization

## Context
Following the approval of the architectural specification (`docs/architecture-frontend-ui-sync.md`) and the completion of the multi-layer security audit (`docs/security-audit-report.md`), a deep 6-phase implementation roadmap was planned and decomposed.

The goal is to migrate the frontend from a monolithic single file (`src/App.vue`, 1,051 lines) to an industrial Feature-Sliced Design (FSD) architecture matching `D:\WORKSPACE\PROJECT\concrete-stress-monitoring\sources\frontend` (Datacake Parity) 1:1, while preserving 100% of verified business logic and test suites.

## Plan Structure (6 Sequential Phases)
1. **Phase 01**: Scaffolding, Design Tokens, Routing, & AppShell Layout (FSD layers, CSS variables, `AppShell.vue` with 64px/260px collapsible rail sidebar via `Ctrl+[`, header breadcrumbs, and zero-FOUC script).
2. **Phase 02**: Shared Atomic UI Primitives & Dialogs (`DataTable.vue`, `TreeDataTable.vue`, `FilterBar.vue`, `Modal.vue`, `DangerConfirmDialog.vue` with 2-step confirmation).
3. **Phase 03**: Feature Slices: Authentication & Monitoring Views (`LoginView.vue` with CSRF & session fence, `OverviewView.vue` with fleet telemetry KPI cards and broker transport status).
4. **Phase 04**: Feature Slices: Device Management & Safe Command Console (`DeviceListView.vue` with dual table/tree view, `DeviceDetailView.vue` with 5 tabs, and `DeviceCommandModal.vue` with catalog SHA-256 hash pre-flight verification).
5. **Phase 05**: Feature Slices: Hierarchy & Access Control (`HierarchyView.vue` multi-level tree, `UserManagementView.vue`, and 3-tier `PermissionGrantMatrix.vue`).
6. **Phase 06**: Full Integration, Regression Gate, & Datacake Parity Verification (`App.vue` simplified to root router container, 100% tests PASS across frontend and backend, visual verification).

## 12-Dimension Risk Saturation
Every phase was audited across 12 dimensions:
- Boundary limits: Clamping page numbers in `DataTable`, trimming search inputs.
- Concurrency: `sessionFence.isCurrent(lease)` cancels stale responses on route change.
- Scale: Bounded SignalR WebSocket subscriptions ($\le 50$ devices) with strict unmount cleanup.
- Data integrity: Pre-flight catalog verification prevents commands on stale schemas (`CATALOG_STALE`).
- Security: 100% state-changing API endpoints retain CSRF token headers and backend antiforgery filters.

## 5-Persona Plan Consensus
- **Architect**: Approved clean FSD phase boundaries and dependency ordering.
- **Security**: Approved fail-closed auth guards and 2-step confirmation for destructive device actions.
- **Performance**: Approved quantitative metrics (render < 20ms, FOUC < 16ms, bounded subscriptions).
- **UX**: Approved Datacake Parity styling (rail sidebar, breadcrumbs, Zinc-900 Dark & Datacake Light themes).
- **Devil's Advocate**: Approved TDD red-first checklists ensuring zero regressions against the 74 vitest tests.

## Scope Boundaries
- **In-Scope**: Frontend presentation layer overhaul, FSD scaffolding, router implementation, and component migration.
- **Out-of-Scope**: Backend business logic modifications (APIs remain untouched); Live MQTT broker streaming (remains deferred until firmware contract is proven).

## Plan Artifacts
- Master Plan: `plans/260930-1915-hms-frontend-ui-parity/plan.md`
- Interactive HTML Plan: `plans/260930-1915-hms-frontend-ui-parity/plan.html`
- Phase Specs: `phase-01-*.md` through `phase-06-*.md`
- Project Roadmap: `docs/project-roadmap.md`

## Verdict
**`VERDICT: PLAN READY FOR COOK`**
