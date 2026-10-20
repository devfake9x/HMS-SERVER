---
phase: 2
title: "Persistence and authorization foundation"
status: in-progress
priority: P1
dependencies: ["1"]
---
# Phase 2: Persistence and authorization foundation

## Overview
Implement the PostgreSQL domain model, ASP.NET Core Identity integration, organization hierarchy, device registry, latest-state records, and the three-layer authorization engine. This phase establishes safe query scoping before any MQTT command can be published.

## Requirements
- Functional: Persist master/child organizations, projects, packages, areas, devices, latest status/capabilities, roles, feature permissions, resource scopes, device-action grants, and audit primitives.
- Functional: Require `owner_organization_id` while allowing nullable project/package/area classifications.
- Functional: Enforce hierarchy consistency and cross-tenant isolation in commands and queries.
- Non-functional: EF migrations are deterministic, indexes support 1,000-device filtering, and secrets never enter ordinary entity logs.

## Architecture
Use `Domain` entities/value objects and `Application` interfaces. `Infrastructure` owns EF Core, Identity stores, query specifications, and authorization implementations. `Api` owns endpoint policy attributes and request-to-application mapping.

Authorization is composed, not collapsed:

```text
feature policy -> resource scope evaluator -> device action evaluator -> application use case
```

Explicit denies win. An organization scope may fold to descendants, but direct device scope is more specific. Classification links are checked for tenant and parent consistency. No telemetry tables are created. Authorization decisions carry a monotonic policy version so long-lived SignalR connections and cached scopes can be invalidated after a grant, role, or organization change. Device capabilities never grant authority: action grants resolve against a server-owned command catalog; capabilities only report device availability.

The command catalog is a versioned, server-owned artifact generated or pinned from the HMS-IDF command descriptor/build manifest. Each accepted catalog records `catalogVersion`, `catalogHash`, firmware source revision, action schemas, feature permission, dangerous/idempotency policy, and compatibility range. Unknown or drifted catalog hashes deny command execution. This phase also owns deterministic enrollment/keyset fixtures with fake but correctly sized 32-byte keys (`../fixtures/deterministic-keyset.json`) so Phase 3 can test broker authentication, ACL construction, and direct-HMAC signing without depending on Phase 4's production credential generator. The fixture is test-only and carries no production entropy claim.

## Related Code Files
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Domain/Organizations/*`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Domain/Devices/*`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Domain/Authorization/*`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Application/Authorization/*`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Application/Devices/*`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Infrastructure/Persistence/ApplicationDbContext.cs`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Infrastructure/Persistence/Configurations/*`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Infrastructure/Security/*`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Infrastructure/Security/AuthorizationVersionStore.cs`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Infrastructure/Security/CommandCatalogManifestStore.cs`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/tests/Hms.Server.UnitTests/Fixtures/DeterministicEnrollmentFixture.cs`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/tests/Hms.Server.IntegrationTests/Fixtures/EnrollmentKeysetFixture.cs`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Domain/Authorization/CommandCatalog/*`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Api/Authorization/*`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Api/Controllers/OrganizationsController.cs`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Api/Controllers/ProjectsController.cs`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Api/Controllers/PackagesController.cs`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Api/Controllers/AreasController.cs`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Api/Controllers/DevicesController.cs`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/tests/Hms.Server.UnitTests/Authorization/*`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/tests/Hms.Server.IntegrationTests/Persistence/*`
- Reference: `D:/WORKSPACE/PROJECT/concrete-stress-monitoring/sources/backend/src/ConcreteStress.Infrastructure/Persistence/ApplicationDbContext.cs`
- Reference: `D:/WORKSPACE/PROJECT/concrete-stress-monitoring/sources/backend/src/ConcreteStress.Infrastructure/Services/ScopeAuthorizationService.cs`
- Reference: `D:/WORKSPACE/PROJECT/concrete-stress-monitoring/sources/backend/src/ConcreteStress.Infrastructure/Security/ResourceAuthorizationService.cs`
- Reference: `D:/WORKSPACE/PROJECT/concrete-stress-monitoring/sources/backend/src/ConcreteStress.Api/Authorization/PermissionAuthorizationHandler.cs`

## Implementation Steps
1. **Tests Before**: Port the reference scope tests and add failing HMS cases for parent/child organizations, cross-tenant IDs, nullable classifications, owner requirement, explicit deny precedence, and the three-way command decision. Add tests that a capability message cannot create an action grant, that policy versions advance on grant/revocation, and that a stale authorization snapshot is rejected.
2. Add domain entities/value objects and validation rules. Keep `VendorLicense`, `DeviceEnrollment`, credentials, and keysets as separate types even if their tables are introduced later. Add a deterministic enrollment/keyset fixture with fake 32-byte command/telemetry keys and explicit generation metadata; mark it test-only and never use it for production secrets.
3. Add Identity user/session/role persistence and stable feature permission seed data. Use the reference policy shape but deny by default when permission data is missing.
4. Implement EF Core configurations, indexes, foreign keys, uniqueness constraints, and the initial migration. Add query filters/specifications for owner organization and resource scopes.
5. Implement `IScopeAuthorizationService`, `IResourceAuthorizationService`, and `IDeviceActionAuthorizationService` with cache invalidation hooks.
6. Add a server-owned command catalog containing type/action, required feature permission, dangerous/idempotency policy, payload schema, firmware compatibility, `catalogVersion`, and `catalogHash`. Generate or pin it from the HMS-IDF descriptor/build manifest; fail CI on drift and deny unknown hashes at runtime. Device capability snapshots may intersect this catalog for availability but cannot add actions or permissions. Increment the authorization version on every grant, role, organization, device, and enrollment revocation mutation.
7. Add hierarchy/device CRUD application services and controllers. Reject inconsistent project/package/area assignments before persistence.
8. Add audit primitives for security-sensitive mutations; do not store raw credential/key payloads.
9. **Tests After**: Run unit, EF integration, authorization, and controller tests against PostgreSQL or Testcontainers, including revocation while a SignalR connection remains open.

## Success Criteria
- [ ] Initial migration creates only management/control-plane tables, not telemetry tables.
- [ ] Device creation requires owner organization and accepts null project/package/area.
- [ ] Queries cannot return devices outside effective resource scope.
- [ ] A user missing any one of feature/resource/action permission receives a deterministic denial.
- [ ] Explicit deny wins over inherited allow.
- [ ] Parent/child and package/project consistency constraints are tested.
- [ ] Feature permission seed keys are stable and documented.
- [ ] Device capabilities cannot grant an action absent from the server command catalog.
- [ ] Authorization version changes invalidate cached scopes and active event subscriptions after revocation.
- [ ] Catalog generation records the firmware source revision/version/hash and unknown or drifted catalog versions deny commands.
- [ ] Deterministic enrollment/keyset fixtures expose fake 32-byte keys and generations for Phase 3 without calling Phase 4 provisioning code.

## Risk Assessment
- **Risk**: copying reference `Gateway` relationships creates a mandatory station dependency. **Mitigation**: define the HMS device aggregate from the approved model; do not copy `StationId`.
- **Risk**: scope evaluation leaks descendants through unscoped EF queries. **Mitigation**: require application query services to accept an authorization context and test raw endpoint bypasses.
- **Risk**: Identity roles and domain roles diverge. **Mitigation**: one permission catalog and explicit mapping tests.

## File inventory and test matrix

| Scenario | Expected test |
| --- | --- |
| Create device with no classification | succeeds with owner organization |
| Create device without owner | validation failure |
| Package belongs to another project | validation failure |
| Child organization scope | sees allowed descendants only |
| Explicit device deny | denies despite ancestor allow |
| Feature permission missing | endpoint returns 403 |
| Action grant missing | command use case returns policy denial |
| Cross-tenant ID in request | not found/forbidden without data disclosure |
| 1,000-device filtered list | indexed query and bounded page response |
| Capability adds unknown action | command remains denied |
| Grant revoked during live session | subsequent event/subscription is denied |
| Catalog drift | unknown version/hash denies command and fails contract CI |
| Phase 3 key seam | deterministic enrollment fixture authenticates and signs with test-only 32-byte keys |

## Dependency map

- Blocked by Phase 1 contract and solution skeleton.
- Blocks command, provisioning, license, and frontend phases.
- Does not depend on EMQX or physical firmware.

## Function/interface checklist

- [ ] `OrganizationHierarchyService` validates ancestor relationships.
- [ ] `DeviceAssignmentValidator` enforces owner and nullable classification rules.
- [ ] `IScopeAuthorizationService` returns effective resource IDs.
- [ ] `IDeviceActionAuthorizationService` evaluates action grants with deny precedence.
- [ ] `IPermissionCatalog` seeds and resolves feature permissions.
- [ ] `IAuditWriter` redacts secrets before persistence.
- [ ] `ICommandCatalog` is server-owned and maps actions to permissions/schemas.
- [ ] `ICommandCatalogManifestStore` records firmware source revision, catalog version/hash, compatibility and drift status.
- [ ] `IAuthorizationVersionStore` invalidates scopes and event subscriptions on mutation.
- [ ] `IEnrollmentKeysetFixture` supplies deterministic test-only device credentials, 32-byte keys, and generations to Phase 3.

## Explicit phase gate

### Tests Before
Write failing tests for organization hierarchy, nullable classifications, device uniqueness, permission scope, concurrency, audit ownership, and deterministic keyset/enrollment fixtures.

### Smallest seam
Start with aggregates, database constraints, authorization evaluator interfaces, and deterministic in-memory keysets; do not depend on MQTT or provisioning.

### Implementation scope
Implement persistence and server-side feature/resource/device-action authorization without telemetry or history tables.

### Negative/regression tests
Reject cross-organization access, invalid hierarchy moves, capability-based authority escalation, stale authorization epochs, duplicate identities, and tenant-filter bypasses.

### Phase regression gate
Domain/unit tests, migration checks, authorization matrix tests, and deterministic keyset tests pass with PostgreSQL constraints enabled.

### Input/output contract
Inputs are Phase 1 contracts and deterministic keysets; outputs are EF models/migrations, authorization policies, audit records, and the Phase 3 keyset seam.

### Dependencies
Requires Phase 1 and must not require Phase 3 MQTT or Phase 4 bundle generation.

### Ownership
Phase 2 owns tenant boundaries, classifications, grants, persistence concurrency, and authorization versioning.

### Risk
A missing organization predicate can expose another company's device; every read/write path needs a negative test.

### Evidence classification
Pre-cook schemas/fixtures are `CONTRACT_TESTED`; EF and domain behavior becomes `IMPLEMENTATION_TESTED` after cook.

### Release gate
No authorization or device endpoint is release-eligible until cross-tenant and stale-policy tests pass against PostgreSQL.

### Definition of done
Constraints, indexes, policies, keyset seam, audit ownership, and negative tests are documented and phase-owned; telemetry tables are absent.

## Implementation evidence appendix (2026-09-28)

Implemented evidence includes nullable device classifications with required
owner organization, hierarchy-filtered management reads, persisted feature/
resource/device-action grants with explicit deny precedence, target-user and
resource scope checks, reserved-role protection, device revoke persistence,
and a PostgreSQL-backed authorization-epoch store (the in-memory store remains
the development/test seam). Command audit correlation reservations now carry a
request fingerprint and reject collisions before transport.

Validation: 33 backend unit tests and 17 backend integration tests pass. The
PostgreSQL migration list includes the command fingerprint and device epoch
migrations. A live PostgreSQL run, distributed concurrency soak, and full user
membership administration remain open evidence gates; they are not replaced by
the in-memory tests.

### Continuation evidence — device revoke command fence (2026-09-29)

Revoke now closes every unfinished command audit for the device with terminal
`Failed/DEVICE_REVOKED`. `RevokeDeviceHandler` invokes the audit-store seam;
both in-memory and EF stores implement it. Unit and EF integration tests cover
the transition. This is a server-side lifecycle fence and makes no claim about
rollback of a side effect already performed by a future live transport.

Late command completion is also fenced: both audit stores refuse to overwrite a
terminal `DEVICE_REVOKED` result with a transport result that arrives after the
revoke operation.

### Continuation evidence — relational revoke fence and principal refresh (2026-09-30)

`EfCommandAuditStore.RecordAsync` now uses a provider-aware path. Relational
providers execute a conditional update with `Code <> DEVICE_REVOKED`; the
in-memory path re-reads and preserves the terminal code. A missing reservation
is ignored without creating a row, so a later scoped `SaveChanges` cannot bypass
the idempotency claim. Tests cover both late completion and the unreserved case.
The current tests use EF InMemory; a two-context PostgreSQL race remains a
release gate rather than an implied proof.

The Identity-backed realtime principal refresher now has fail-closed coverage
for lookup exceptions and development DI registration. Existing SignalR tests
continue to cover stale policy/epoch and revoke membership behavior; browser and
durable multi-replica delivery are still deferred.
