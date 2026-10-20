---
title: "Core control-plane implementation"
date: 2026-09-28
status: active
---

# Core control-plane implementation

## Context

The active goal permits implementation of the HMS-SERVER control plane while
live MQTT/EMQX and firmware-side importer behavior remain deferred. HMS-IDF was
not re-scanned or modified in this session.

## What Happened

- Standardized API routes under `/api/v1` and made the deferred transport status
  explicit for the frontend.
- Added project, package, and area classification entities, stores, EF model
  tables, APIs, owner consistency checks, and device filters.
- Added ASP.NET Identity EF foundation, cookie sessions, CSRF token endpoint,
  protected management/provisioning/license routes, and production PostgreSQL
  fail-closed configuration.
- Added the pinned 94-row command catalog as an embedded server artifact. Every
  current action remains `DENIED_UNTIL_ACTION_CONTRACT` until a closed action
  contract exists; unknown catalog metadata fails closed.
- Added provisional command timestamp/TTL/size validation, correlation replay
  reuse, explicit permission denies, permission-grant seams, and device revoke
  epoch advancement.
- Added primary-only provisioning validation, import nonce/hash metadata,
  device-bound one-time consumption, license trust/KMS/signer seams, and a
  bounded SignalR authorization-epoch event publisher.
- Connected the Vue shell to real inventory/resource endpoints and kept empty or
  unavailable states truthful without fabricated device/telemetry data.
- Added a typed same-origin frontend API client with CSRF handling, no-store
  secret-download helpers, truthful deferred MQTT state, and revoke actions.
- Fixed EF device revocation persistence, added organization-scope checks to
  revoke/provision/license paths, and made persisted resource grants (including
  explicit denies) participate in effective scope filtering.
- Registered MVC antiforgery filters and configured lifecycle enums as strings so
  the API contract matches the TypeScript frontend model.

## Verification

- .NET: 28 unit tests and 11 integration tests pass.
- Frontend: 20 tests, typecheck, and production build pass.
- Plan validation, status, helper tests, phase-gate fixture, and documentation
  drift scan pass.
- Broker-envelope, importer-contract, and license-recovery fixtures pass.
- Development API smoke test passes for `/health`, `/api/v1/command-transport/status`,
  and `/api/v1/auth/csrf`; protected management endpoints return `401` without a
  session.
- `npm audit` could not reach the registry audit endpoint; no dependency update
  was applied blindly.

## Decisions

- Development may use an explicit in-memory fallback; production requires a
  PostgreSQL connection and does not silently start with in-memory persistence.
- The server owns command catalog and authorization. Capabilities cannot grant
  command permission.
- Live MQTT, EMQX identity/ACL, canonical publish leaf, production KMS, and
  firmware importer execution remain release limitations.

## Next

Finish persistent user-role/target-scope administration, complete the remaining
frontend CRUD and operational views, and add SignalR delivery/reconnect tests.
Keep the deferred MQTT adapter behind the existing Application ports until a
separate firmware/broker contract is approved; do not infer a publish leaf or
claim device delivery.
