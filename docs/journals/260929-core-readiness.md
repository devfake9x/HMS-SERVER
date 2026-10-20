---
title: "Core control-plane readiness continuation"
date: 2026-09-29
status: active
---

# Core control-plane readiness continuation

## Context

The approved goal keeps live MQTT/EMQX and firmware importer execution
deferred while the server completes the management/control-plane core. This
continuation closed several gaps that did not require new HMS-IDF evidence.

## Changes

- Device command requests now resolve the actor organization from the persisted
  Identity user scope when the session has no `org` claim, and reject
  conflicting claims fail-closed.
- Device listing gained a server-side text filter while retaining organization,
  classification and lifecycle scope checks.
- SignalR reconciliation now removes revoked or stale memberships even when no
  event is queued; registry cleanup remains fail-closed if group removal fails,
  and critical events are not coalesced with status/capability updates.
- Provisioning keysets are persisted in encrypted `device_keysets` rows keyed by
  `(device_id, generation)`, with a race-safe reload of the winning row.
- Permission policy versions use a persistent singleton counter with an atomic
  PostgreSQL upsert, so concurrent grants do not reuse a version derived from
  `MAX(permission_grants)`.
- Identity user/role mutations advance the same policy version, and the API
  regression test proves a role creation invalidates older authorization
  snapshots.
- License apply now has an explicit `ILicenseVerifier` boundary. The default
  adapter returns `LICENSE_VERIFIER_UNAVAILABLE` until a real parser/signature/
  device-binding implementation is configured; raw bytes alone cannot become
  an applied license.
- Frontend license file handling no longer enforces an unsupported fixed byte
  limit, clears the file input after processing, reloads inventory after login,
  and clears sensitive session state on logout.
- Frontend command choices come only from the server catalog; all current
  unresolved actions remain disabled and MQTT delivery remains deferred.
- Role creation and assignment now re-check the persisted `users.manage`
  feature permission, so an explicit persisted deny cannot be bypassed by a
  stale or forged HTTP claim. Wildcard feature denies also override exact
  allows at the domain command boundary.
- SignalR subscriptions now refresh the Identity principal before each
  reconciliation and event authorization check. Missing, malformed, deleted,
  or refresh-failing users are removed fail-closed, and refreshed claims
  replace the registry snapshot.
- Hub join and revalidation use the same principal refresh seam before adding
  or renewing membership, closing the stale-claim gap on reconnect.
- Command submissions now carry the catalog version/hash used by the frontend.
  Missing, blank, partial, or mismatched metadata returns `CATALOG_STALE` before
  protocol preparation or transport; the frontend revalidates with a no-store
  request on focus and before submit, then disables the command form until it
  reloads the trusted catalog.
- SignalR revoke notifications use an explicit DTO containing device id,
  authorization epoch, numeric policy version, and reason on both delivery-deny
  and reconciliation paths. Frontend realtime state now fences events per
  device and cancels pending work on an accepted revoke.
- SignalR delivery now fences queued events by both authorization epoch and
  current policy version. Bounded event admission preserves critical
  lifecycle/security/command events, drops only coalescible latest-state work
  when saturated, rejects a full critical queue explicitly, and supports
  requeue after a failed send. This remains an in-memory retry seam; it does not
  claim durable or exactly-once browser delivery.
- Identity realtime refresh now rejects locked users and tickets with a stale
  security stamp, while successful refresh rebuilds claims from current
  Identity rather than reusing a stale principal.
- Frontend inventory filters are URL-backed and server-side (`q`, organization,
  project, package, and area); nullable classification fields are exposed in
  device creation and transmitted as `null` when unassigned.
- Frontend catalog readiness now uses an independently verified catalog identity
  with request revision fencing. Changed or malformed catalogs clear the proof
  and require an explicit reload; logout invalidates pending verification.
- EF license persistence now enforces one envelope per `(device_id, generation)`
  and detaches a losing concurrent candidate before recording recovery. This
  is a database concurrency fence, not proof of production PostgreSQL failover
  or a cross-store atomic license commit.
- The action-source evidence report was marked `INVALIDATED` after a bad fixture
  invocation wrote it with the plan directory as the root. The original output
  is retained for forensic reference only; no new HMS-IDF verification is
  claimed. The contract-artifact report is marked `STORED_NOT_REVALIDATED` for
  the same reason.

## Evidence

- Backend unit tests: 70 passed.
- Backend integration tests: 26 passed.
- Frontend tests: 38 passed.
- Frontend typecheck and production build passed.
- `plan-ops validate` passed.
- `plan-ops status` reports the plan remains in progress with no phase falsely
  marked complete.
- Plan helper tests: 11/11 passed.
- Stored model-only contract fixtures passed: 16/16 across importer contract,
  license-recovery model and broker-session envelope; all eight phase gates
  have 12/12 required sections. The historical documentation-banner
  drift check passed; its documented limitation remains that it does not prove
  every current implementation document is drift-free.
- The contract-artifact fixture was intentionally not counted as new firmware
  evidence: its report is marked `INVALIDATED` because the invocation reads the
  external HMS-IDF root, which is outside this continuation's allowed
  verification boundary.

## Remaining boundaries

The core is not release-ready. A production license parser/verifier, firmware
importer bootstrap and crash-recovery fixture, trusted EMQX session identity,
canonical publish topic/ACL, firmware-derived QoS/retain behavior, real
SignalR client delivery tests, and broker/load/HA evidence remain explicit
release gates. The workspace has no Git repository, so changes are verified by
current files and test output rather than a commit diff. No new HMS-IDF
behavior was inferred or implemented.

Production startup now also requires host-supplied `ISecretRootPolicy` and
non-ephemeral Data Protection implementations; the boolean configuration flag
is no longer accepted as proof of a KMS boundary.

## Continuation — 2026-09-29

Device revocation now transitions every unfinished command audit for that
device to terminal `Failed/DEVICE_REVOKED` in both in-memory and EF stores.
This prevents a revoked reservation from remaining retryable after the
authorization epoch fence. Unit and EF integration regressions cover the
behavior, and the full solution passes 72 unit tests and 28 integration tests.
The transition is intentionally an audit/lifecycle fence; because MQTT remains
deferred, it does not claim to cancel an already executed device-side effect.

The audit stores also reject late completion writes after this fence, preserving
the terminal revoke outcome even when a pre-revoke command owner returns later.

The license trust boundary now persists rotation/revocation audit entries with
the key id, issuer, previous/new status, actor and timestamp. The EF model and
migration `20260929090323_AddLicenseTrustAudits` add the
`license_trust_audits` table; verification remains fail-closed and no signer
private material enters the web process.

## Continuation — 2026-09-30 P1 hardening

The command audit revoke fence was tightened at the persistence boundary.
Relational `RecordAsync` uses a conditional update that excludes
`DEVICE_REVOKED`, so a late completion cannot overwrite the terminal result
after revoke commits. The InMemory provider has a separate fail-closed readback
path, and an unreserved completion is ignored without staging an incomplete
entity. These semantics are covered by the revoke/late-completion and
no-reservation integration regressions. A real PostgreSQL two-context race is
still unproven because the workspace has no PostgreSQL/Testcontainers runtime.

The trust store now enforces immutable issuer/public-key bindings per `kid`, an
allowlist of `active`/`retired`/`revoked` statuses, ordered effective intervals,
new-active-key semantics for creation/rotation, and revoked-target semantics for
revocation. Audit reads order by timestamp and audit id. Unit and EF tests cover
same-key mutation, invalid transitions, intervals, status and persistence.

Identity realtime principal refresh has an exception fail-closed test and DI
wiring coverage. Full verification is 75 unit tests, 30 integration tests, 38
frontend tests, successful frontend typecheck/build, plan validation/status,
11 plan helper tests, and stored contract fixtures. `NU1900` remains because
NuGet vulnerability-feed access is unavailable; no .NET vulnerability audit is
claimed. Live EMQX, firmware importer runtime, KMS/HSM, browser SignalR,
PostgreSQL concurrency, load/HA, RTO/RPO and rolling-upgrade gates remain open.

## Historical continuation — 2026-09-30 current-source verification (superseded)

The reflection assertions in `RequestLimitTests` were updated to use the
non-generic `MemberInfo.GetCustomAttributes(Type, inherit)` API supported by
the target framework. The source was rebuilt after clearing only stale test
processes created by previous verification attempts; no production behavior
was loosened to accommodate old test binaries.

The final current-source gate passes 80 backend unit tests, 30 backend
integration tests, 40 frontend tests, frontend typecheck, production build,
and offline npm audit with zero findings. `plan-ops validate` and `status`
remain consistent, 11 plan helper tests pass, all eight phase gates pass, the
importer/license/broker model fixtures pass, catalog/source evidence matches
the pinned 94-row catalog, and EF reports no pending model changes. The older
firmware build artifact still has 89/94 action strings; cross-revision catalog
compatibility remains denied until a reproducible manifest is available.

The initial inspection did not prove full objective compliance. A subsequent
audit found P1 gaps in persisted-grant HTTP policies, browser session response
fencing, browser SignalR wiring, and per-connection delivery backpressure.
These remain core implementation work, not firmware limitations. Live
EMQX and firmware importer execution, real PostgreSQL concurrency, KMS/HSM and
external signer, browser SignalR, 1,000-device load, two-replica soak,
RTO/RPO, rolling upgrade, and the NuGet vulnerability audit remain open.

## Continuation - 2026-09-30 core integration closure

The management control plane now supports scoped rename/edit flows for
organizations, projects, packages, areas, and devices. Device ownership and
key generation are immutable; nullable classification edits are checked for
existence, organization ownership, and package/project consistency. The
frontend uses CSRF-protected `PUT` calls and a session fence so a delayed edit
cannot write into a later session. The update DTO uses nullable GUID strings;
create DTOs continue to use their existing strong-ID value objects.

SignalR delivery now removes only the revoked device from a connection's
backlog, preserving sibling subscriptions. Browser hub handlers are fenced to
the active connection/revision, reconnects revalidate authorization, revoke
events cancel pending command work, and disconnect forces local cleanup even if
transport shutdown rejects. Regression coverage includes fake-client delivery,
multi-device revoke, late join/revalidate, old-hub callbacks, and stop failure.

Current verification: 91 backend unit tests, 35 backend integration tests, and
74 frontend tests pass; frontend typecheck and production build pass; offline
`npm audit` reports zero findings; solution build passes. The NuGet `NU1900`
warning is an unavailable online vulnerability feed and is not reported as a
successful .NET audit. The core P1 HTTP authorization, session fencing,
per-connection SignalR, and browser SignalR integration findings are resolved
in tested code.

Release gates remain explicit: live EMQX/ACL and canonical publish leaf,
firmware importer runtime/power-loss behavior, real PostgreSQL concurrency,
external signer/KMS/HSM, 1,000-device load, two-replica soak, RTO/RPO, rolling
upgrade compatibility, and production metrics/observability. MQTT remains a
deferred/recording transport and no device delivery or execution is claimed.

## Continuation - 2026-09-30 realtime/request-order race closure

The final bounded code review found five concrete availability/state races in
the browser SignalR and delivery paths. Tests were added before the fixes. A
stale reconnect rejection can no longer restore reauthorization state after
logout; an old hub shutdown cannot overwrite a replacement connected hub; and
the delivery service removes only the left or missing device's backlog entry.
Resynchronization fences are now per device, so removing one subscription does
not clear a sibling's required resynchronization state.

`App.vue` section and inventory requests now carry request revisions in addition
to the session fence. Responses from an older section or filter are ignored in
the same authenticated session. New regression coverage passes together with
91 backend unit tests, 35 backend integration tests, and 74 frontend tests.
Typecheck, production build, solution build, offline npm audit, plan checks,
documentation drift check and the EF pending-model check also pass. The
review found no additional P0/P1/P2 command, provisioning, license or secret
handling defect; live broker, firmware, KMS/HSM, load, HA and upgrade evidence
remain release limitations.

## Continuation - 2026-09-30 revoked provisioning and runtime metrics

A bounded security review found that provisioning could still generate a fresh
credential/key bundle for a terminal `Revoked` device. The handler now rejects
that state before secret generation, with a unit test proving the generator is
not called and an HTTP integration test proving `DEVICE_REVOKED` at the CSRF
and scope-protected boundary. The integration test also exposed invalid record
validation metadata; constructor-parameter attributes now produce normal
ASP.NET Core model validation instead of a runtime exception.

SignalR backlog drop, retry, and resynchronization counters are now exposed as
bounded `System.Diagnostics.Metrics` instruments. A `MeterListener` regression
proves all three signals while leaving exporter configuration to the host.
Fresh verification is 93 backend unit tests, 36 backend integration tests, and
74 frontend tests; build, typecheck, offline npm audit, plan checks, model
fixtures, documentation drift, and EF pending-model checks pass. PostgreSQL
two-context, browser-E2E, live EMQX, firmware importer, KMS/HSM, load/HA,
RTO/RPO, and rolling-upgrade evidence remain unproven.

## Continuation - 2026-09-30 provisioning concurrency fences

The in-memory provisioning bundle store was a singleton development seam with
an unsynchronized dictionary. `Save` and both one-time `Consume` overloads now
share a lock, and a 64-consumer regression proves that exactly one caller can
consume a bundle. This removes a development/test race without changing the
production EF contract.

The EF bundle store now rechecks the device lifecycle immediately before the
insert. When PostgreSQL is the configured provider, the save runs inside a
serializable transaction and locks the device row with `FOR UPDATE`; a revoke
that commits first therefore rejects the bundle, while a save that owns the row
finishes before the revoke commit. An InMemory revoked-state regression covers
the application-visible error. A real PostgreSQL two-context race remains an
external release gate because no PostgreSQL runtime is available here.

The persistence port also rejects unknown device IDs before inserting an
orphaned bundle; the regression leaves the store empty after the rejection.

Isolated verification passes 96 backend unit tests, 36 backend integration
tests, and 74 frontend tests; infrastructure compilation, frontend checks,
plan checks, fixtures, documentation drift and EF model checks remain green.
The NuGet `NU1900` warning is still environment-related because the online
vulnerability feed is unreachable.
