---
phase: 6
title: "Frontend administration and permissions"
status: in-progress
priority: P1
dependencies: ["5"]
---
# Phase 6: Frontend administration and permissions

## Overview
Implement the Vue management shell for organization/project/package/area/device administration, user/role management, resource scopes, and device action grants. The UI follows the reference feature patterns but uses HMS ownership and nullable classification rules.

## Requirements
- Functional: Create/edit/list organization hierarchy, projects, packages, and areas with scoped filters.
- Functional: Create/edit/list devices with required owner organization and nullable classification fields.
- Functional: Manage users, roles, feature permissions, resource scopes, and device action grants.
- Functional: Hide/disable unauthorized navigation and actions while relying on backend denial as the security boundary.
- Non-functional: URL-synchronized filters, keyboard-accessible forms,
  predictable loading/error states, and no secret material in query/cache
  state. Pagination is a later scale optimization and is not a hidden DTO
  contract for this goal.

## Architecture
The current goal uses a bounded Vue 3 + TypeScript shell with a typed
same-origin `adminApi`, session-fenced refs, and a fail-closed `can()` helper.
Pinia, TanStack Query, generated OpenAPI clients, and a virtualized tree remain
optional future presentation layers; they are not introduced merely to satisfy
this control-plane slice. Authorization and scope decisions remain backend
owned.

The first UI pass is administrative. Provisioning/license/command operation screens are Phase 7. This keeps hierarchy and authorization data contracts stable before secret-bearing workflows are exposed.

## UI/API contracts
- Current list endpoints return the server DTO arrays and accept the proven
  filters (`q`, `organizationId`, `projectId`, `packageId`, `areaId`, `state`).
  Device filters are serialized into the URL and sent back to the backend;
  pagination/continuation is a later scale optimization, not a hidden contract.
- `GET /api/v1/me/permissions` returns a versioned feature-permission snapshot. Unknown, expired, or missing snapshots fail closed; a `401/403` from any mutation invalidates the relevant query and never gets converted into a successful optimistic update.
- Organization/project/package/area lists use the existing `/api/v1` DTO
  endpoints. The UI never infers inherited access from labels or local ancestry;
  it renders only server responses and backend permission results.
- `GET /api/v1/devices/{id}/action-grants` and `PUT .../action-grants` use action IDs plus allow/deny state. The server remains authoritative for the catalog and grant evaluation; the frontend only edits values returned by the server.
- `GET /api/v1/devices/{id}/capabilities` is a read-only device report. It is not a permission source and cannot grant an action; command availability is confirmed again by the command endpoint.

## Related Code Files
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/frontend/src/app/router/routes.ts`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/frontend/src/app/nav.config.ts`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/frontend/src/core/auth/permissions.ts`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/frontend/src/core/auth/can.ts`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/frontend/src/features/organizations/*`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/frontend/src/features/projects/*`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/frontend/src/features/packages/*`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/frontend/src/features/areas/*`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/frontend/src/features/devices/*`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/frontend/src/features/users/*`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/frontend/src/features/roles/*`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/frontend/src/features/device-permissions/*`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/frontend/src/shared/ui/*`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/frontend/tests/*`
- Reference: `D:/WORKSPACE/PROJECT/concrete-stress-monitoring/sources/frontend/src/features/gateway-device/views/GatewayDeviceListView.vue`
- Reference: `D:/WORKSPACE/PROJECT/concrete-stress-monitoring/sources/frontend/src/features/user/ui/UserDataScopeModal.vue`
- Reference: `D:/WORKSPACE/PROJECT/concrete-stress-monitoring/sources/frontend/src/app/nav.config.ts`
- Reference: `D:/WORKSPACE/PROJECT/concrete-stress-monitoring/sources/frontend/src/core/auth/can.ts`

## Implementation Steps
1. **Tests Before**: Add failing Vitest/component tests for route guard denial, organization tree inheritance, device `Unassigned` filters, owner-required forms, cross-field classification validation, grant matrices, and empty/error/loading states. Add contract tests for permission snapshot expiry/unknown keys, effective-scope DTO shape, server 403 rollback, action-catalog version mismatch, and no local inference of inherited scopes.
2. Scaffold frontend routes, navigation, session bootstrap, generated API client, query error handling, and permission snapshot store.
3. Implement organization/project/package/area feature modules using URL filter/query patterns from the reference frontend.
4. Implement device inventory/detail/basic CRUD with owner organization required and project/package/area optional. Add server-side pagination and filter serialization.
5. Implement users/roles/feature permissions and resource scope editor. Add explicit device-action grant editor as a separate `WHAT` dimension.
6. Add route metadata and component action guards. Ensure null/unknown permission state fails closed.
7. **Tests After**: Run Vue typecheck, lint, component tests, route tests, API schema tests, and accessibility smoke tests.

## Success Criteria
- [ ] All administrative routes are permission-gated and fail closed.
- [ ] Parent/child organization tree and scope editor reflect backend effective scopes.
- [ ] Device form supports unassigned project/package/area but requires owner organization.
- [ ] Inventory filters synchronize to URL and preserve paging/search state.
- [ ] Resource grants and device-action grants are visibly separate and save independently.
- [x] No password/key/license raw bytes are stored in client refs, URL, query
  cache, console logs, or analytics.
- [ ] The UI treats backend permission/action/capability responses as authoritative and rejects stale catalog versions before rendering executable mutations.

## Risk Assessment
- **Risk**: UI permission checks create a false security boundary. **Mitigation**: every mutation is checked by backend; UI tests include direct API denial.
- **Risk**: scope tree becomes slow with nested organizations. **Mitigation**: paginated/searchable nodes and virtualized tree; test 1,000 devices and deep organization chains.
- **Risk**: reference UI assumes mandatory station/project links. **Mitigation**: add explicit unassigned states and cross-field validation for HMS rules.

## File inventory and test matrix

| Feature | Tests before implementation |
| --- | --- |
| Navigation | permission present/absent/null, route redirect |
| Organization tree | parent/child, suspended node, inherited scope |
| Device list | unassigned, filter combination, paging, API error |
| Device form | owner required, nullable classification, cross-tenant option rejection |
| User/role | feature permission matrix, empty role, revoke |
| Resource scope | allow/deny inheritance, direct device scope |
| Device actions | separate WHAT grant, action catalog refresh, deny |
| API contracts | DTO/schema drift, stale permission snapshot, 401/403 rollback, unknown permission/action |

## Dependency map

- Blocked by backend OpenAPI, authorization, and hierarchy endpoints from Phases 1-2.
- Uses current status/capability API contracts but does not require live MQTT.
- Blocks Phase 7 because provisioning and command screens reuse the session, device detail, and permission primitives.

## Function/interface checklist

- [ ] `can()` fails closed for missing/unknown permission state.
- [ ] `useOrganizationTree()` supports descendant scopes and paging.
- [ ] `useDeviceFilters()` serializes nullable classification filters.
- [ ] `useDeviceActionGrants()` keeps action grants distinct from resource access.
- [ ] `usePermissionSnapshot()` expires and fails closed; it never grants access from cached unknown keys.
- [ ] `useActionCatalog()` requires a server version/hash match before enabling an action editor.
- [ ] Route metadata maps to stable backend feature permission keys.
- [ ] Generated API types are checked against the backend OpenAPI document.

## Explicit phase gate

### Tests Before
Add tests for organization/device CRUD, permission matrix rendering, revocation, no-store downloads, object URL cleanup, raw-byte redaction, and backend error mapping.

### Smallest seam
Generate typed API contracts and use mock handlers; keep authorization decisions and bundle/license bytes out of Pinia, query cache, URLs, logs, and analytics.

### Implementation scope
Build administration screens for hierarchy, nullable classifications, devices, users, feature/resource/device-action permissions, downloads, and revocation.

### Negative/regression tests
Reject unauthorized mutations, stale policy responses, revoked membership, unsupported broker modes, leaked bytes, missing no-store headers, unreleased object URLs, and misleading persistence claims.

### Phase regression gate
Typecheck, component/contract tests, browser privacy/download tests, and backend authority checks pass; UI checks never count as authorization proof.

### Input/output contract
Inputs are Phase 1 OpenAPI/error contracts and the Phase 2 authorization API; outputs are typed admin flows and redacted backend-authoritative requests.

### Dependencies
Requires Phase 1/2; provisioning actions consume Phase 4 APIs but do not require importer completion.

### Ownership
Phase 6 owns administrative Vue modules, typed API integration, permission UX, and safe artifact downloads.

### Risk
Browser state or analytics can retain secrets, or UI can appear authorized after revocation; browser and backend tests are required.

### Evidence classification
Pre-cook UI contracts are `CONTRACT_TESTED`; frontend/browser behavior is `DEFERRED_TO_PHASE_6 - REQUIRED` until implementation.

### Release gate
No administrative release until raw bytes stay out of client state/telemetry, revocation updates membership, and backend remains final authority.

### Definition of done
Admin flows are typed, redacted, revocation-aware, permission-scoped, and tested without a client-side authority model.

## Implementation evidence appendix (2026-09-30 current slice)

Backend rename/update endpoints and frontend edit forms now cover
organization, project, package, area, and device metadata. Device ownership is
immutable; nullable classification updates are validated against the owning
organization and package/project relationship. `adminApi.updateDevice` sends
nullable GUID strings matching `UpdateDeviceRequest`; create DTOs retain their
strong-ID shape. Session-fenced edit responses cannot write after logout or a
target replacement. Integration tests cover successful device edits and
cross-scope denial; frontend tests cover the edit API contract and the session
fence. Pagination and generated-client claims remain future work.

## Historical implementation evidence appendix (2026-09-28; superseded)

The current Vue control-plane shell now has a bounded, backend-authoritative slice:

- `frontend/src/adminApi.ts` centralizes same-origin requests, CSRF headers, authentication, classification CRUD, device CRUD/revoke, permission snapshots, and catalog metadata. Strong ID request fields are serialized as `{ value: guid }`, matching the current ASP.NET contracts; response DTO IDs remain plain GUIDs.
- `frontend/src/App.vue` exposes Overview, Organizations, Projects, Packages, Areas, Devices, Permissions, Provisioning, and Licenses navigation. Login refreshes the CSRF token and permission/catalog snapshots; logout clears permission/catalog state. Organization/project/package/area/device create forms are disabled when the current non-expired snapshot lacks the corresponding `hms.*.manage` key or contains an explicit deny. The backend remains authoritative on every mutation.
- Provisioning metadata typing reflects the current API (`bundleId`, `deviceId.value`, `expiresAt`, `used`). Raw bundle/license bytes are not placed in UI state. Device revoke messaging describes server enforcement only and does not claim pending-command cancellation.
- `frontend/src/adminApi.test.ts` covers non-2xx propagation, same-origin/CSRF behavior, strong-ID serialization, login/logout requests, permission/catalog snapshots, and the absence of fabricated secret fields.

Validation evidence at that snapshot: `npm test -- --run` (23 tests passed),
`npm run typecheck` (passed), and `npm run build` (passed). The later
2026-09-30 appendix supersedes its statement that complete CRUD editing and
SignalR revocation delivery were outstanding.
