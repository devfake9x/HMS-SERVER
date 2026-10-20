---
phase: 3
title: "Command boundary and deferred transport"
status: in-progress
priority: P1
dependencies: ["2"]
---
# Phase 3: Command boundary and deferred transport

## Overview
Build the transport-independent command application boundary, provisional firmware protocol adapter, recording/deferred transport, command lifecycle, and SignalR event bridge. Do not require live EMQX or a canonical MQTT publish leaf in this phase. No telemetry handler or telemetry storage is introduced.

## Requirements
- Functional: Define broker credential and ACL ports without requiring a live broker.
- Functional: Validate the provisional firmware command contract with direct `crypto/cmd_key`, append `sig` last, enforce `1..1023` bytes for the signed header, permit a bounded framed body only for `accepts_body` actions, and correlate deferred/ACK/timeout lifecycle states.
- Functional: Keep inbound status, connectivity, capabilities, command response, and audit handling behind ports; live topic/QoS/retain/reconnect integration is deferred to Phase 8.
- Functional: Define command correlation, retry eligibility, deduplication, expiry/TTL, and key-generation fencing so rotation cannot replay stale commands.
- Functional: Persist command audit and latest-state updates; publish authorized operational events to SignalR.
- Non-functional: Reconnect safely, rebuild topics from device identity, avoid duplicate response handling, and never log secrets or signed payload keys.
- Non-functional: Bound broker ingress, handler channels, SignalR fan-out queues, and database writes; coalesce latest-state updates per device and expose dropped-event/backpressure metrics.
- Contract gate: firmware proves wildcard command ingress with `/cmd/res` exclusion and source-pins connected wiring/reconnect backoff, but does not define a canonical server publish leaf. In this phase command submission uses a recording/deferred transport; any live publish path returns `CONTRACT_UNAVAILABLE` and performs zero broker calls.

## Architecture
`SendDeviceCommand` receives an authorized request, resolves a server-owned command catalog entry, applies idempotency/expiry/generation policy, and creates a neutral `CommandExecutionPlan`. `ICommandProtocolAdapter` may render the provisional firmware fields and signer bytes. `RecordingDeviceCommandTransport` records the lifecycle for tests and `DeferredDeviceCommandTransport` returns `MQTT_DEFERRED` without broker calls. The future MQTT adapter will own broker connection, topic, QoS, retain, ACL, and trusted session identity. The wire command ID is `id` and is the firmware deduplication key; the server may keep a separate internal command row ID but retries must reuse the same `id` and exact signed bytes. `ts` is mandatory, `ttl_ms` is optional with firmware maximum 24 hours, firmware default TTL is 120 seconds when no descriptor default applies, and `key_ver` must match the active command-key version. `kid` and `generation` are not valid command fields unless a future firmware fixture changes the allowlist.

```text
EMQX -> /internal/emqx/auth -> authenticated webhook + credential hash + active enrollment -> ACL
device status/capabilities/cmd-res/audit -> MQTT handlers -> DB latest state/audit -> SignalR
web command -> three-layer policy -> server catalog + capability availability -> direct HMAC signer -> fixture-approved command publish suffix
trusted EMQX bridge envelope -> identity verifier -> bounded handlers -> coalesced latest-state store/event queues
```

The signer must use exact raw bytes. It must not deserialize/re-serialize after computing the signature. A golden vector must be shared with HMS-IDF tests. Firmware's own deduplication is a volatile 128-entry exact-`id` LRU whose expiry sweep requires completed SNTP; it is not durable, device-scoped or key-generation-scoped. Firmware evaluates `dry_run` before `confirm` and before post-parse dedup/security verification. Responses use the `res:` HMAC domain when a command key exists and can be plaintext when no key exists. The response-topic validator checks syntax and reserved segments but does not prove device-prefix binding, so the server must enforce namespace binding with enrollment identity and ACL. The current firmware wildcard subscription does not authorize inventing `/cmd/execute` or any other leaf.

## Related Code Files
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Application/Commands/ICommandProtocolAdapter.cs`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Application/Commands/IDeviceCommandTransport.cs`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Application/Commands/RecordingDeviceCommandTransport.cs`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Application/Commands/DeferredDeviceCommandTransport.cs`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Infrastructure/Security/CommandEnvelopeSerializer.cs`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Infrastructure/Security/HmacCommandSigner.cs`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Infrastructure/Mqtt/HmsIdfProvisionalProtocolAdapter.cs`
- Deferred to Phase 8: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Infrastructure/Mqtt/MqttHostedService.cs`
- Deferred to Phase 8: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Infrastructure/Mqtt/EmqxAuthService.cs`
- Deferred to Phase 8: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Infrastructure/Mqtt/BrokerSessionIdentityProvider.cs`
- Deferred to Phase 8: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Infrastructure/Mqtt/LatestStateCoalescer.cs`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Infrastructure/Hubs/AuthorizationEpochEventFilter.cs`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Api/Controllers/Internal/EmqxAuthController.cs`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Application/Commands/*`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Infrastructure/Hubs/DeviceOperationsHub.cs`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/tests/Hms.Server.UnitTests/Security/*`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/tests/Hms.Server.UnitTests/Mqtt/*`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/tests/Hms.Server.IntegrationTests/Mqtt/*`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/tests/Hms.Server.IntegrationTests/Security/SignalRRevocationTests.cs`
- Reference: `D:/WORKSPACE/PROJECT/concrete-stress-monitoring/sources/backend/src/ConcreteStress.Infrastructure/Mqtt/GatewayMqttService.cs`
- Reference: `D:/WORKSPACE/PROJECT/concrete-stress-monitoring/sources/backend/src/ConcreteStress.Infrastructure/Hubs/MonitoringHub.cs`
- Reference: `D:/WORKSPACE/PROJECT/HMS-IDF/components/command-dispatcher/include/cmd_security.h`
- Reference: `D:/WORKSPACE/PROJECT/HMS-IDF/services/mqtt-service/mqtt_service.c`

## Implementation Steps
1. **Tests Before**: Add failing golden-vector tests for direct HMAC, compact JSON, exact field allowlist, required `id`/`ts`, optional `key_ver`/`ttl_ms`, signature marker, nested action schema, the 1,023-byte signed-header boundary, framed-message/body limits and body-action allowlist, wrong key, key-version fence, response correlation, and zero broker calls from the deferred transport. Add source-security evidence tests for the volatile 128-entry dedup/LRU/SNTP behavior, dry-run ordering, `res:` response signing/plaintext fallback, and response-topic syntax versus device binding. Add capability-injection tests proving unknown or `DENIED_UNTIL_ACTION_CONTRACT` actions remain denied, deterministic keyset tests from Phase 2, SignalR authorization-epoch ordering, bounded queue/coalescing, dropped-event metrics, and recording/deferred lifecycle states. Keep complete schemas, build conditions, side effects, and runtime readiness denied until their owning contracts/tests exist. Do not add live EMQX or new firmware investigation in this phase.
2. Add `HmsTopicBuilder` and contract fixtures for `/cmd/#`, `/cmd/res`, `/status`, `/status/connectivity`, `/audit`, and `/capabilities`.
3. Implement `EmqxAuthService` with password-hash verification, active enrollment checks, authenticated webhook verification (mTLS or timestamped HMAC with nonce cache and rotation), and deny-all fallback. Add exact ACL response tests and assert the ACL cannot escape the authenticated device namespace. Raw topic/payload callbacks never establish identity; only the trusted broker-session envelope does.
4. Implement recording/deferred command transport and application-side lifecycle handling. Keep the future MQTT hosted service, reconnect/subscription lifecycle, retained/latest status and capabilities handlers, and audit/response broker handlers behind Phase 8 adapters. Consume only contract-defined inbound envelopes when a later adapter exists; ensure duplicate message IDs are idempotent, coalesce latest state per device, and bound event queues with explicit drop behavior.
5. Implement `CommandEnvelopeSerializer` and `HmacCommandSigner` using direct `crypto/cmd_key`; add key generation/version lookup through the keyset repository.
6. Implement `SendCommandUseCase`: feature/resource/action policy against the server command catalog, capability availability intersection, payload validation, canonical provisional serialization, size check, deferred transport, timeout, audit, and SignalR event publication. Never treat a device capability message as a permission grant and never report delivery from the deferred transport.
7. Add controlled retry/timeout semantics. Reuse the same wire `id` and exact signed bytes for an allowed idempotent retry, retain server deduplication state for at least twice the effective firmware expiry, and reject expired commands before publish. Never retry dangerous commands without an explicit idempotency/correlation policy. During key rotation, fence new sends, drain or mark in-flight records, set the firmware-proven `key_ver`, and reject late responses or retries from the old key version. Firmware's volatile 128-entry cache is only a device-side safety net and must not be the server's replay guarantee.
8. **Tests After**: Run unit tests, deferred transport tests, response/timeout lifecycle tests, keyset fixture tests, queue/backpressure tests, and SignalR authorization-epoch/revocation tests. Defer live EMQX integration, reconnect, broker-session identity and ACL tests to Phase 8.

## Success Criteria
- [ ] Broker credential/ACL ports are defined and testable without EMQX; live namespace isolation is deferred to Phase 8.
- [ ] Status/capabilities/cmd-res/audit update the correct device only.
- [ ] Inbound messages are rejected when topic device ID, authenticated MQTT username/client identity, and enrollment do not agree.
- [ ] Inbound messages are rejected when the trusted broker-session envelope is missing, expired, duplicated, or inconsistent with topic/client/enrollment identity.
- [ ] Missing or unpinned command publish leaf returns `CONTRACT_UNAVAILABLE` before signing/publishing; `/cmd/execute` is never enabled from assumption and the deferred transport makes zero broker calls.
- [ ] Direct-key signing matches HMS-IDF golden vectors.
- [ ] Signed command headers at 1,023 bytes pass and 1,024 bytes fail before publish; framed messages are tested separately at the 131,072-byte whole-message boundary and only `modbus.check_rtu_config`/`modbus.replace_rtu_config` accept the body.
- [ ] Commands without feature/resource/action authorization never reach MQTT.
- [ ] ACK, device error, duplicate response, and timeout states are deterministic and audited.
- [ ] Idempotent retries reuse one wire `id` and exact signature bytes, expired commands are rejected before publish, duplicate responses are side-effect free, and old-key-version records cannot be retried after the rotation fence.
- [ ] Any action without a closed request/response schema, build-condition evidence, and side-effect/idempotency policy is `DENIED_UNTIL_ACTION_CONTRACT`; it produces no signature or publish and is disabled in command UX.
- [ ] SignalR subscriptions are checked against the same device resource scope.
- [ ] SignalR group join and event delivery re-check the current authorization version after scope/grant revocation.
- [ ] Recording/deferred transport never returns `PUBLISHED`, `ACKNOWLEDGED`, or `EXECUTED` without a future live adapter evidence path.
- [ ] SignalR events include an authorization epoch; stale events are dropped server-side and client-side, and bounded queue overflow produces metrics without unbounded memory growth.

## Risk Assessment
- **Risk**: MQTT reconnect creates stale topics or duplicate handlers. **Mitigation**: rebuild from current identity on every connection and make handlers idempotent.
- **Risk**: JSON serializer changes field order/escaping and invalidates signatures. **Mitigation**: own raw serializer, golden vectors, and byte-level assertions.
- **Risk**: privileged EMQX webhook is exposed. **Mitigation**: mTLS or timestamped HMAC with nonce replay protection, network restriction, rate limit, rotation overlap, and structured audit; a static internal header alone is invalid.
- **Risk**: webhook request replay or header disclosure authorizes forged broker decisions. **Mitigation**: mTLS or timestamped HMAC with nonce replay cache, rotation overlap, and explicit proxy/header stripping tests.
- **Risk**: a device publishes a valid message under another device topic or advertises a dangerous capability. **Mitigation**: bind topic to authenticated client identity and intersect capabilities with the server-owned command catalog.
- **Risk**: subscriber callbacks lose the broker authentication context. **Mitigation**: require a trusted EMQX bridge/rule envelope, reject raw topic/payload ingestion for authenticated state, and test missing/expired/forged envelope metadata.
- **Risk**: status heartbeat fan-out overwhelms SignalR or the database. **Mitigation**: latest-state coalescing, bounded queues, backpressure/drop metrics, and load tests with heartbeat/reconnect bursts.
- **Risk**: command key possession is full device authority. **Mitigation**: never expose it to browsers, enforce server authorization, rotate generations, and audit.

## File inventory and test matrix

| Contract path | Test scenarios |
| --- | --- |
| Candidate command publish suffix -> device | fixture-approved authorized publish, wrong device, revoked enrollment; no suffix is frozen from assumption |
| `/cmd/res` -> server | ACK, error, duplicate ID, unknown ID, malformed response |
| `/status*` -> server | retained online/offline, stale timestamp, reconnect |
| `/capabilities` -> server | schema version, hash update, unavailable action |
| `/audit` -> server | redaction, device identity check |
| EMQX auth | correct hash, wrong password, inactive device, ACL isolation |
| Webhook auth | invalid signature, stale timestamp, nonce replay, rotation overlap, deny-all on verifier failure |
| Inbound identity | topic/client/enrollment mismatch, cross-device status/capability/audit publish |
| Reliability | same-correlation retry, expiry before publish, duplicate response, late response after rotation fence |
| Signer | exact vector, wrong key, boundary size, generation rotation |
| Backpressure | heartbeat burst, queue bound, coalescing, dropped-event metric, authorization-epoch ordering |

## Dependency map

- Blocked by persistence, keyset abstractions, and policy interfaces from Phase 2.
- Blocks provisioning because provisioning must create credentials and keysets used here.
- Blocks frontend command/status UI because event names and command states originate here.

## Function/interface checklist

- [ ] `IHmsTopicBuilder` renders and validates every topic suffix.
- [ ] `ICommandEnvelopeSerializer` returns pre-sign and final bytes.
- [ ] `ICommandSigner` signs direct raw bytes and reports key generation.
- [ ] `ICommandDispatcher` enforces policy before publish.
- [ ] `IMqttMessageHandler` implementations validate device identity from topic.
- [ ] `IEmqxAuthService` returns fail-closed authentication and ACL decisions.
- [ ] `IDeviceOperationsHub` authorizes subscriptions and event fan-out.
- [ ] `IEmqxWebhookRequestAuthenticator` verifies mTLS or timestamped HMAC, timestamp skew, nonce uniqueness, and key rotation.
- [ ] `IMqttMessageHandler` compares topic device ID with authenticated client identity and active enrollment.
- [ ] `IBrokerSessionIdentityProvider` validates the trusted EMQX bridge envelope and fails closed on missing/expired/inconsistent metadata.
- [ ] `ILatestStateCoalescer` and `IEventBackpressurePolicy` bound queues, coalesce per-device state, and expose dropped-event metrics.
- [ ] `IDeviceOperationsHub` re-checks authorization version on joins, reconnects, and event delivery.

## Explicit phase gate

### Tests Before
Run command golden vectors, catalog/command-contract fixtures, MQTT topic and
publish-path evidence fixtures, broker-envelope negatives, deterministic keyset
tests, and command size/TTL/key-rotation cases.

### Smallest seam
Implement serializer, signer, catalog resolver, trusted broker-session validator, and publisher abstraction before EMQX or device I/O.

### Implementation scope
Add MQTT ingestion, command authorization, direct 32-byte HMAC signing, deduplication, response/audit handling, and bounded realtime events.

### Negative/regression tests
Reject topic/client/enrollment mismatch, cross-device publish, missing identity, replay/expiry, unknown catalog hash, signed headers over 1,023 bytes, framed bodies over 131,072 bytes or attached to non-body actions, stale key generation, duplicate responses, and unpinned publish leaf.

### Phase regression gate
Fixtures, backend command tests, broker replay/rotation tests, catalog drift checks, and selected-leaf/ACL integration pass; otherwise return `CONTRACT_UNAVAILABLE` and publish zero messages.

### Input/output contract
Inputs are Phase 1 contracts and the Phase 2 keyset/evaluator; outputs are signed bytes, authorized broker events, bounded latest state, and audit outcomes.

### Dependencies
Requires Phase 1 and the Phase 2 keyset seam; does not depend on Phase 4 provisioning.

### Ownership
Phase 3 owns MQTT transport, identity propagation, command security, catalog enforcement, deduplication, responses, audits, and realtime admission.

### Risk
Raw callbacks or guessed topics can bypass identity; every identity and publish path fails closed.

### Evidence classification
Firmware facts are `SOURCE_PROVEN`, standalone negatives are `CONTRACT_TESTED`, and live EMQX/backend runtime is deferred.

### Release gate
Commands remain disabled until canonical publish leaf, ACL, trusted envelope, and catalog compatibility evidence pass.

### Definition of done
Exact wire bytes, limits, identity checks, catalog policy, response behavior, and bounded events are tested without telemetry persistence.

## Implementation evidence appendix (2026-09-28)

The core command boundary is implemented against the deferred transport. Audit
reservation uses a unique `(device_id, correlation_id)` claim and a persisted
request fingerprint. A live unfinished claim returns
`IDEMPOTENCY_IN_PROGRESS`; after the five-minute lease it returns
`IDEMPOTENCY_OUTCOME_UNKNOWN` and is not reclaimed automatically because a
crashed process may already have reached a transport side effect. The policy is
covered by unit and EF persistence tests and does not enable live MQTT.

The bounded realtime publisher has a hosted SignalR delivery seam. The
2026-09-29 hardening slice uses explicit connection delivery, current Identity
refresh, and epoch/policy-version checks immediately before send. Group
membership alone never authorizes a send.

### Realtime backpressure hardening contract (2026-09-29)

- Firmware evidence: none needed; this is a server-only operational event
  contract and adds no MQTT or firmware behavior.
- Prior assumption: a global drain followed by best-effort send was sufficient.
  Requeueing globally after one connection fails can duplicate delivery to other
  connections and overwrite newer coalesced state.
- Recommended contract: retain failed delivery only for its connection, preserve
  FIFO for critical events, bound each connection backlog, and retry with fresh
  authorization. Coalesce status/capabilities only; never silently evict a
  critical event. A full critical ingress queue fails explicitly. Exhausted
  delivery retries or a full critical connection backlog remove memberships
  and require an authenticated snapshot/resubscription. Send deadlines bound
  slow clients. Metrics count drops, retries and required resynchronizations.
- Tests Before: saturation preserves critical entries; critical-only ingress
  overflow rejects explicitly; send failure retries only the failed connection;
  policy/epoch changes or principal failure block retry; membership removal
  during principal refresh cannot be undone; group-removal failure remains
  fail-closed; backlog overflow and retry exhaustion force resubscription.
- Smallest seam: one deterministic delivery iteration callable by tests with
  fake hub clients and real application stores; no live broker dependency.
- Risk/limit: queues are in-memory. A successful SignalR send is not a browser
  acknowledgement, and reconnect/snapshot recovery and multi-replica runtime
  still need end-to-end evidence. No durable/exactly-once delivery is claimed.

### Continuation evidence — revoke closes command reservations (2026-09-29)

The command audit store exposes a server-only cancellation seam used by device
revocation. Unfinished reservations become terminal `Failed/DEVICE_REVOKED`,
so a later retry cannot re-enter transport through an authorization-epoch race.
This does not alter the deferred MQTT adapter, topic, QoS, retain or wire
envelope contract. Unit and EF integration regressions pass; live transport
cancellation remains unsupported until its contract exists.

The same terminal fence is applied in `RecordAsync`, so a transport completion
that races after revoke cannot replace `DEVICE_REVOKED` with a deferred or
success result.

### Continuation evidence — late completion and missing reservation behavior (2026-09-30)

The relational implementation enforces the revoke fence in the database update
predicate rather than relying only on a previously read entity. The in-memory
fallback is explicitly a development seam and preserves the same fail-closed
terminal result. `RecordAsync` does not manufacture an audit record when no
reservation exists. Focused and full backend tests pass; live broker delivery,
canonical publish leaf, ACL, QoS, retain, and device-side cancellation remain
deferred by the firmware boundary.

### Continuation evidence — actor binding and deferred transport regression (2026-09-30)

The application command handler now rejects a request whose `UserId` differs
from the authorization context before fingerprinting, audit reservation,
protocol preparation, or transport. The regression proves
`COMMAND_ACTOR_MISMATCH` has no audit or transport side effect. The current
source rebuild passes the full 80-test backend unit suite and 30 integration
tests. The transport remains recording/deferred and does not claim publish,
acknowledgement, or device execution; topic, QoS, retain, ACL and canonical
publish-leaf behavior remain firmware-gated.
