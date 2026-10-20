---
phase: 7
title: "Frontend operations and command UX"
status: in-progress
priority: P1
dependencies: ["6"]
---
# Phase 7: Frontend operations and command UX

## Overview
Implement provisioning, license, latest device status/capabilities, SignalR events, and capability-driven command UI. Secret-bearing workflows are deliberately explicit, one-time, and tied to backend audit records.

## Requirements
- Functional: Run the provisioning wizard, generate/download a one-time identity bundle, rotate/revoke credentials, and show redacted history.
- Functional: Upload/view license requests, edit drafts, submit to signer, apply signed envelopes, remove/revoke license, and show binding/lifecycle state.
- Functional: Display latest status/connectivity/capabilities and command ACK/error/timeout events through authorized SignalR subscriptions.
- Functional: Render command forms from capability availability metadata, require confirmation for dangerous actions, and show pending state/correlation ID.
- Non-functional: Never cache raw credentials/keys in frontend state; support reconnect and stale-event handling; preserve keyboard accessibility. Document that the operator's browser/OS may persist an explicitly downloaded file outside application control.

## Architecture
Use the existing typed `adminApi` and a small SignalR composable that
subscribes only after the backend returns a current authorization epoch.
Capabilities are availability metadata only. The UI never treats capabilities
as permission grants or signs commands; it requests the backend command
endpoint.

Provisioning download is a deliberate action: display device identity and bundle hash, reveal raw credential/key material only in the generated download, and never echo it into the page after completion. License envelope bytes are treated as opaque files; parsed metadata comes from backend DTOs.

## UI/API and authority contracts
- `POST /api/v1/devices/{id}/provisioning-bundles` returns redacted metadata and
  `GET /api/v1/devices/{id}/provisioning-bundles/{bundleId}/download` returns
  the one-time `application/octet-stream` response. The frontend uses
  `cache: 'no-store'`, validates no-store headers and size/content type,
  creates a transient object URL, then revokes it; raw bytes never enter app
  state, query state, URL parameters, error text, or analytics.
- License upload/download endpoints use opaque `Blob` responses and redacted metadata DTOs. The frontend must not parse or persist private material; it may display server-returned hash, binding, signer state, and lifecycle only.
- `GET /api/v1/devices/{id}/capabilities` returns `{ deviceId, catalogVersion, catalogHash, actions[] }`. Each action has an ID and payload schema; confirmation text/danger level is advisory UI metadata. The backend re-fetches/validates the current catalog, action grant, license feature, and device capability at command time. A stale `catalogVersion` disables submission and forces refresh.
- `POST /api/v1/devices/{id}/commands` returns `{ commandId, correlationId, actionId, acceptedAt, state }`; only SignalR or a later status endpoint changes the state to `acknowledged`, `device_error`, `timed_out`, `rejected`, or `revoked`.
- `DeviceOperationsHub` exposes `SubscribeDevice(deviceId)` and `UnsubscribeDevice(deviceId)` only. Every event carries `authorizationEpoch`/`policyVersion`; the server checks feature/resource/action authorization on subscribe, reconnect, revocation and event delivery, and sends `DeviceRevoked` before removing the connection from the device group. The client discards stale epochs, clears device state, cancels pending commands, and refuses reconnect resubscription until a fresh authorization succeeds. Group membership is never treated as permanent authorization.
- Download UX is deliberately operator-controlled: no preview/open action is triggered, filenames contain no token or secret, the response content type and size/hash are checked, the one-time download audit is shown after completion, and the app only claims no persistence in app state/cache/URL/log/telemetry. It cannot claim control over the operator's download folder or OS history.

## Related Code Files
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/frontend/src/features/provisioning/*`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/frontend/src/features/licenses/*`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/frontend/src/features/device-status/*`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/frontend/src/features/commands/*`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/frontend/src/composables/useDeviceOperationsHub.ts`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/frontend/src/shared/security/redact.ts`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/tests/features/provisioning/*`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/tests/features/licenses/*`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/tests/features/commands/*`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/tests/composables/useDeviceOperationsHub.test.ts`
- Reference: `D:/WORKSPACE/PROJECT/concrete-stress-monitoring/sources/frontend/src/features/gateway-device/ui/CreateDeviceModal.vue`
- Reference: `D:/WORKSPACE/PROJECT/concrete-stress-monitoring/sources/frontend/src/features/gateway-device/views/GatewayDeviceDetailView.vue`
- Reference: `D:/WORKSPACE/PROJECT/concrete-stress-monitoring/sources/frontend/src/shared/ui/CopyableInput.vue`
- Reference: `D:/WORKSPACE/PROJECT/concrete-stress-monitoring/sources/frontend/src/features/user/ui/GrantsMatrixPane.vue`

## Implementation Steps
1. **Tests Before**: Add failing component/composable tests for one-time bundle download, redaction, rotate/revoke state, license draft/sign/apply transitions, capability allowlist, dangerous-command confirmation, ACK/error/timeout, SignalR reconnect, unauthorized subscription, `DeviceRevoked` teardown, stale authorization epoch, stale catalog hash/version, and server-authoritative action rejection. Add browser tests asserting `Cache-Control`/`no-store`, content type/size/hash validation, transient Blob URL revocation, no raw bytes in query cache/Pinia/URL/console, no secret-bearing error telemetry, safe filename, and post-download audit state.
2. Implement status/capabilities detail tabs and an authorized SignalR composable with stale-event/version handling. Handle `DeviceRevoked` by leaving the group, clearing pending state, discarding events with an older authorization epoch, and requiring a new authorization check after reconnect.
3. Implement provisioning wizard using backend-generated IDs and bundle hashes; use a `no-store` Blob download response rather than storing secrets in query state or a query cache. Validate content type/size/hash, use a safe non-secret filename, revoke object URLs immediately, show only redacted metadata after completion, and state honestly that operator filesystem persistence is outside app control.
4. Implement license workflow screens for request metadata, draft changes, signer state, envelope hash, binding, lifecycle, and apply/remove confirmation.
5. Implement capability-driven command forms and confirmation templates. Treat the backend catalog/grant/capability result as authoritative, require catalog hash/version freshness, send only typed payloads to backend, and display server policy errors without attempting local signing.
6. Add command timeline and audit links; map backend command states to accessible pending/success/error/timeout UI.
7. **Tests After**: Run Vue typecheck, lint, component tests, API contract tests, and browser smoke tests with mocked SignalR/REST.

## Success Criteria
- [ ] Provisioning bundle can be issued/downloaded once and UI never logs or caches raw secrets.
- [ ] License states and signer errors are visible without exposing private material.
- [ ] Device status/capabilities refresh after reconnect and ignore stale device events.
- [ ] Unsupported or unauthorized actions are not rendered as executable controls.
- [ ] Dangerous actions require an explicit UI confirmation based on advisory availability metadata and server authorization; capabilities never grant authority.
- [ ] ACK, device error, timeout, and reconnect states are distinguishable and auditable.
- [ ] Bundle/license downloads are `no-store`, transient Blob-only, and leave no raw secret in browser state, URLs, logs, or telemetry.
- [ ] Revocation removes SignalR membership, clears pending command state, and blocks automatic resubscription until reauthorized.
- [ ] Every realtime event carries an authorization epoch; stale epochs are ignored and revoke races cannot render post-revocation state.
- [ ] Download UX validates response metadata, uses a safe filename, records post-download audit state, and documents operator-controlled filesystem persistence.
- [ ] A stale or mismatched capability catalog cannot enable a command; backend rejection remains authoritative.

## Risk Assessment
- **Risk**: secret appears in devtools/query cache or browser caches. **Mitigation**: `no-store`/`Pragma: no-cache`, download-only Blob response, transient object URL revocation, redaction utility, no Pinia/TanStack storage, and browser tests scanning serialized state, network metadata, and console/error spies.
- **Risk**: UI assumes capabilities are stable. **Mitigation**: version/hash capability catalog and invalidate command forms on update.
- **Risk**: SignalR event from an unauthorized or revoked device is displayed. **Mitigation**: server-authorized per-device groups, explicit `DeviceRevoked` event, membership removal, client teardown, reauthorization on reconnect, and tests for each transition.
- **Risk**: UI capability metadata is mistaken for authority. **Mitigation**: catalog version/hash handshake and backend command-time validation of feature, resource, action, license, and device capability.

## File inventory and test matrix

| Flow | Tests before implementation |
| --- | --- |
| Provisioning | create, preview, one-time download, used token, rotate/revoke |
| License | request upload, draft edit, signer pending/error, apply/remove |
| Status | online/offline, stale event, capability version update |
| Commands | available action, unavailable action, confirmation, server 403 |
| Realtime | authorized subscription, reconnect, duplicate/stale event |
| Redaction | no raw password/key in state, errors, audit links, console spy |
| Download safety | no-store headers, one-time token, Blob URL revocation, browser cache/network checks |
| Revocation | DeviceRevoked teardown, pending-command cancellation, reconnect reauthorization |
| Epoch ordering | revoke race, queued old event, reconnect, stale-event discard |
| Authority | stale catalog, grant/license/device-capability mismatch, backend 403/409 |

## Dependency map

- Blocked by backend endpoints/events from Phases 3-4 and administrative session/device modules from Phase 6.
- Blocks final browser and end-to-end acceptance in Phase 8.
- Does not require the device importer for mocked UI tests, but automatic import messaging must remain honest until Phase 5 passes.

## Function/interface checklist

- [ ] `useDeviceOperationsHub()` enforces authorized group subscription and reconnect handling.
- [ ] `useProvisioningBundle()` exposes only redacted metadata plus a one-time download action.
- [ ] `useLicenseWorkflow()` models request/draft/signed/applied/removed states.
- [ ] `useCapabilityActions()` validates action availability and payload schemas.
- [ ] `useCommandTimeline()` correlates command IDs and maps ACK/error/timeout.
- [ ] `redactSecrets()` covers password, key, token, envelope, and bundle fields.
- [ ] `useProvisioningBundle()` requests `no-store` Blob downloads and revokes object URLs after save.
- [ ] `useDeviceOperationsHub()` handles `DeviceRevoked`, cancels pending commands, and requires reauthorization after reconnect.
- [ ] `useDeviceOperationsHub()` carries and validates authorization epochs on every event.
- [ ] `useCapabilityActions()` blocks stale catalog hashes/versions; backend responses remain the final authority.

## Explicit phase gate

### Tests Before
Add tests for device operations, capability availability, catalog gating, SignalR authorization epochs, stale event discard, revoke cancellation, idempotent retry, and dangerous-action confirmation.

### Smallest seam
Build an API/SignalR adapter carrying authorizationEpoch/policyVersion and catalog metadata; keep signing and authorization on the backend.

### Implementation scope
Build operational views for latest status/capabilities, command/result state, audit visibility, stale events, and pending-command cancellation.

### Negative/regression tests
Discard stale epochs/catalogs, cancel pending commands on DeviceRevoked, reject client signing and over-limit commands, handle dropped queues, duplicate responses, expired commands, and backend denial.

### Phase regression gate
Typecheck, SignalR/browser contract tests, command UX tests, and backend integration tests pass through revoke/reconnect/order races.

### Input/output contract
Inputs are Phase 3 command/event contracts and Phase 6 permission context; outputs are authorized operational views and backend-originated requests.

### Dependencies
Requires Phase 3/6; live operations remain unavailable until canonical MQTT leaf/ACL evidence passes.

### Ownership
Phase 7 owns operational Vue modules, SignalR lifecycle, stale-event policy, queue/drop presentation, and command UX.

### Risk
A stale realtime event can restore revoked authority or show a false result; epoch and backend revalidation are mandatory.

### Evidence classification
Event/catalog contracts are `CONTRACT_TESTED`; frontend/runtime behavior is deferred until cook.

### Release gate
Operational commands stay disabled when catalog, authorization epoch, backend authority, or MQTT contract is stale/unavailable.

### Definition of done
The UI is revocation-aware, catalog-aware, bounded, backend-authoritative, and never opens a browser MQTT connection.

## Historical implementation evidence appendix (2026-09-28; superseded)

### Catalog verification hardening contract (2026-09-29)

The displayed catalog and independently verified server catalog identity must
match before command submission. Verification becomes unavailable while a
request is pending or failed. An older asynchronous verification must not
restore readiness after catalog replacement or logout. A changed catalog
requires explicit reload and selection reset. Tests cover matching, mismatched,
untrusted and stale asynchronous responses; backend checks remain authoritative.
This is a frontend contract and requires no firmware rescout.

The current Vue slice provides typed same-origin administration calls, login and
logout CSRF refresh, permission/catalog snapshots, truthful deferred-MQTT
status, device revoke handling, stale authorization-epoch filtering, secure
Blob download helpers, user/role/three-layer permission forms, and a real
SignalR browser adapter with reconnect revalidation. Commands remain
backend-authorized and catalog-gated; no browser MQTT or client signing is
present.

Validation: the current full frontend suite, typecheck, and production build
pass. SignalR delivery is tested through fake hub clients and bounded
per-connection queues; live broker/device delivery, firmware importer runtime,
and operator filesystem guarantees remain explicitly deferred. The deferred
transport does not claim device delivery or execution.

## Implementation evidence appendix (2026-09-30 current slice)

The browser adapter now fences connection lifecycle, reconnect revalidation,
revocation, pending-command cancellation, delayed session responses, and
shutdown failures. A per-connection SignalR backlog coalesces only status and
capabilities, retries delivery in isolation, preserves critical events, and
removes one revoked device without discarding sibling queues. Stale epochs and
catalog identities remain disabled at the client while the backend stays the
final authority.
