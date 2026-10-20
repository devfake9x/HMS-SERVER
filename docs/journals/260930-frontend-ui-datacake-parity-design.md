---
title: "Frontend UI synchronization to concrete-stress-monitoring (Datacake Parity)"
date: 2026-09-30
status: active
---

# Frontend UI Synchronization to Concrete-Stress-Monitoring (Datacake Parity)

## Context
HMS-SERVER has achieved high reliability on its backend and communication logic, including 3-tier authorization, CSRF token handling, session fences, command catalog verification, and bounded SignalR real-time subscriptions (passing 96 unit tests, 36 integration tests, and 74 vitest frontend tests).
However, the user interface remained a single-file test console (`src/App.vue`, 1,051 lines) without routing or industrial dashboard design.

The objective of this architectural session is to formulate a 100% UI synchronization RFC adhering to the structure, aesthetics, and interaction patterns of `D:\WORKSPACE\PROJECT\concrete-stress-monitoring\sources\frontend` (Datacake Parity, FSD structure, Tailwind CSS v4, Zinc-900 Dark & Datacake Light themes, Reka UI) while strictly preserving all existing security boundaries and business logic.

## Key Architectural Decisions

1. **Feature-Sliced Design (FSD) Architectural Parity**:
   - `src/app/`: Vue Router (`router/index.ts`), Navigation Configuration (`nav.config.ts`), and design tokens (`tokens.css`).
   - `src/core/`: 100% preservation of verified logic (`adminApi.ts`, `sessionFence.ts`, `catalogVerification.ts`, `secureDownload.ts`, `useDeviceOperationsHub.ts`).
   - `src/shared/`: Atomic UI primitives (`DataTable`, `TreeDataTable`, `Modal`, `DangerConfirmDialog`, `FilterBar`, `AlarmBell`, `Badge`).
   - `src/features/`: Isolated business slices (`auth/views/LoginView.vue`, `devices/views/DeviceListView.vue`, `devices/views/DeviceDetailView.vue`, `hierarchy/views/HierarchyView.vue`, `security/views/UserManagementView.vue`).

2. **Datacake Industrial Layout (`AppShell.vue`)**:
   - Collapsible sidebar rail (64px collapsed, 260px expanded, `Ctrl+[` shortcut).
   - Dynamic header with breadcrumb trail, history back button (`canGoBack`), AlarmBell popover, locale switcher, and user dropdown.

3. **Design Tokens & Zero-FOUC Guarantee**:
   - Styling entirely controlled via CSS variables (`--bg-surface`, `--border-subtle`, `--accent-primary`).
   - Inline theme script in `<head>` preventing Flash of Unstyled Content (FOUC).

4. **Security & State Preservation Invariants**:
   - CSRF token header (`X-CSRF-TOKEN`) injection and session cleansing on logout/401 preserved fail-closed.
   - Command issuance requires catalog SHA-256 verification and 2-step destructive confirmation (`DangerConfirmDialog.vue`).
   - SignalR hub connection restricted to bounded subscriptions ($\le 50$ devices) with strict unmount cleanup.

## Artifacts Produced
- `research-report.md`: Deep comparative analysis of the reference frontend, Datacake/ThingsBoard/Grafana benchmarks, and feature mapping matrix.
- `brainstorm-report.md`: Problem-First Inversion (4 failure modes) and 5-Persona Debate.
- `brainstorm-report.html`: Self-contained interactive report with Mermaid diagrams and styled showcases.
- `docs/architecture-frontend-ui-sync.md`: Full architectural specification RFC for execution.

## Next Steps
Following approval of this RFC, execution will proceed through the 4-phase non-breaking migration pipeline to implement the views, verify 100% test pass rate across all 74 vitest tests and backend suites, and conduct live browser validation.
