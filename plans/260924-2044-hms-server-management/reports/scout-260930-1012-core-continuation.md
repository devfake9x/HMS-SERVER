# Scout Report (historical snapshot - superseded by current-source continuation)

## Scope

Read-only scout for the continuation hardening slice. The workspace has no
usable Git metadata, so current files and command output are the authoritative
state.

## Relevant files

- `src/Hms.Server.Infrastructure/Persistence/EfCommandAuditStore.cs` - EF
  reservation, revoke cancellation and late completion fence.
- `src/Hms.Server.Application/Licensing/LicenseContracts.cs` - trust entry,
  transition validation and in-memory parity seam.
- `src/Hms.Server.Infrastructure/Persistence/EfLicenseStores.cs` - persisted
  trust records and deterministic audit reads.
- `src/Hms.Server.Infrastructure/Identity/IdentityRealtimePrincipalRefresher.cs`
  - Identity-backed principal refresh and fail-closed behavior.
- `src/Hms.Server.Api/Hubs/SignalRDeviceEventDeliveryService.cs` - authorized
  event delivery and refresh/revalidation call path.
- `tests/Hms.Server.UnitTests/LicenseTests.cs` - trust mutation, status,
  interval and transition regressions.
- `tests/Hms.Server.IntegrationTests/PersistenceModelTests.cs` - EF command
  fence, missing reservation and trust persistence regressions.
- `tests/Hms.Server.UnitTests/IdentityRealtimePrincipalRefresherTests.cs` -
  refresh failure and current-identity claims coverage.
- `plans/260924-2044-hms-server-management/plan.md` and phase 02/03/04/08 -
  durable contracts, evidence and release limitations.

## Contracts and patterns

- Device revoke is a server-side terminal audit fence. It does not claim that a
  future live MQTT transport can undo a device-side effect.
- Relational late completion uses a conditional update excluding
  `DEVICE_REVOKED`; the InMemory provider is a development/test fallback.
- `RecordAsync` requires a pre-existing reservation and does not manufacture an
  audit row for an unreserved completion.
- A trust `kid` is an immutable issuer/public-key identity. Rotation introduces
  a new active `kid`; revocation targets an existing active/retired key.
- Statuses are `active`, `retired`, or `revoked`; effective intervals are
  strictly ordered; audit order is timestamp then audit id.
- Realtime principal refresh is fail-closed when Identity lookup or stamp
  validation fails.

## Verification evidence

- `dotnet test Hms.Server.slnx --no-restore -v minimal -m:1 -nodeReuse:false -p:UseSharedCompilation=false`
- 75 unit tests passed; 30 integration tests passed at the time of this
  snapshot.
- `npm test -- --run` - 38 frontend tests passed at the time of this snapshot.
- `npm run typecheck` - passed.
- `npm run build` - passed.
- `plan-ops validate` - valid, no errors/warnings.
- `plan-ops status` - plan remains `in-progress`; no phase falsely completed.
- `plan-ops.test.cjs` - 11 passed.
- Importer, license recovery, broker envelope and phase-gate fixtures passed.
- Catalog source/baseline comparison passed for the recorded snapshot; the
  build-artifact fixture reports 89/94 action strings and remains a denied
  cross-revision compatibility gate.

## Unresolved questions and limits

- PostgreSQL multi-context race and trust-store concurrency are not proven on
  this host; EF InMemory does not substitute for that evidence.
- Live EMQX ACL/session identity, canonical publish leaf, QoS/retain behavior,
  firmware importer runtime, production KMS/HSM, browser SignalR runtime,
  1,000-device load, two-replica soak, RTO/RPO and rolling upgrade remain
  release gates.
- NuGet vulnerability-feed access is blocked (`NU1900`); no .NET dependency
  vulnerability audit is claimed.
