---
phase: 1
title: "Contract and solution skeleton"
status: in-progress
priority: P1
dependencies: []
---
# Phase 1: Contract and solution skeleton

## Overview
Freeze the cross-system contracts before domain implementation and create the empty ASP.NET Core/Vue solution with runnable health checks and test harnesses. This phase does not implement business behavior; it creates seams that later phases can test against.

## Requirements
- Functional: Define OpenAPI resources, MQTT topics/ACL, command envelope, server command catalog, capabilities snapshot, provisioning bundle, license adapter boundary, and SignalR event names.
- Functional: Pin catalog canonicalization/hash, trusted broker-session envelope, importer bootstrap schema, primary-only rejection, and `CONTRACT_UNAVAILABLE` behavior until the command publish-leaf fixture passes.
- Functional: Scaffold separate backend and frontend applications plus unit/integration/contract test projects.
- Non-functional: Contracts must be versioned, deterministic, redacted-safe, and usable without a live device for unit tests.
- Non-functional: Pin the .NET SDK, package versions, PostgreSQL/EMQX images, OpenAPI generation command, and license schema hash.
- Non-functional: CI must run format/type/compile/test checks without requiring production secrets.

## Architecture
The backend solution is a new four-project ASP.NET Core solution. The frontend is a separate Vue application. Contract fixtures live outside either runtime so C# and TypeScript tests can consume the same JSON examples.

```text
contracts/
  mqtt/topics.md
  mqtt/command-envelope.schema.json
  mqtt/capabilities.schema.json
  provisioning/device-bundle-v1.schema.json
  license/license.proto             # pinned copy/hash of HMS-IDF source
  fixtures/                          # redacted golden messages only
src/
  Hms.Server.Api
  Hms.Server.Application
  Hms.Server.Domain
  Hms.Server.Infrastructure
frontend/
tests/
  Hms.Server.UnitTests
  Hms.Server.IntegrationTests
```

The importer consumes one canonical JSON bundle envelope. Optional raw `LicenseRequest`/`LicenseEnvelope` files are retained for vendor exchange, not treated as a second device import protocol. The active firmware command envelope is a compact raw JSON object with `type`, `id`, `ts`, nested `data.action`/`data.data`, optional `key_ver`, `ttl_ms`, `dry_run`, `confirm`, `response_topic`, `trace_id`, and final `sig`; unknown top-level fields such as `kid` or `generation` are rejected. Firmware subscribes to `<prefix>/<device_id>/cmd/#`, excludes `/cmd/res`, and forwards every other received payload to the command queue, but it does not define a canonical server publish leaf. Until a fixture pins the server leaf, ACL and reconnect ordering, the server returns `CONTRACT_UNAVAILABLE` and never publishes. The topic contract also records prefix source, QoS, retain flags, response ordering, reconnect rebuild, and retained-message freshness.

The current firmware topic fixture proves `<prefix>/<device_id>/cmd/#` command subscription at QoS 1, `/cmd/res` response at QoS 1 non-retained, `/audit` at QoS 1 non-retained, `/capabilities` at QoS 1 retained, `/status` at QoS 1 non-retained, and `/status/connectivity` at QoS 1 retained for online/LWT state. The command callback's wildcard-plus-response-exclusion rule, connected wiring order, and active reconnect backoff are source-pinned; the canonical server publish leaf and ACL remain fixture-gated.

The current pre-cook contract fixtures are kept outside runtime source:

- `fixtures/catalog-fixture.mjs` extracts 94 descriptor actions and computes a
  canonical catalog hash from the current `cmd_descriptor.c` source.
- `fixtures/mqtt-topic-fixture.mjs` checks the source-proven topic/session
  baseline and the command callback's wildcard-plus-response-exclusion rule;
  it records the server command publish leaf as `NEW_FIRMWARE_CONTRACT` because
  firmware does not select a canonical server leaf.
- `fixtures/license-recovery-model.test.mjs` exercises the proposed
  pre-commit/post-commit recovery state machine. It is `MODEL_TESTED` only and
  does not replace actual NVS/importer crash testing in Phase 5.
- `fixtures/command-golden-vector.mjs` pins direct 32-byte HMAC/raw-byte signing,
  strict fields, final `sig`, and size/TTL source anchors; it is not an on-device test.
- `fixtures/catalog-drift-fixture.mjs` compares the dated `catalog-baseline.json`
  independently and proves synthetic drift detection. A second firmware build
  manifest remains required for release compatibility.
- `fixtures/handler-registration-fixture.mjs` maps all 94 descriptor rows to a
  registered namespace handler and verifies action literal presence. It does
  not claim payload schema, build-condition, side-effect, readiness, or
  end-to-end proof.
- `fixtures/action-source-evidence-fixture.mjs` extracts a deterministic
  source-line/digest record for all 94 handler branches, including direct input
  and response field references plus helper calls. It is source evidence only;
  requiredness, types, ranges, complete schemas, build conditions, side effects,
  and runtime readiness remain deferred.
- `fixtures/identity-license-source-evidence-fixture.mjs` pins the getter-only
  identity API, active NVS location, license journal marker/sequence checks,
  exact-pending comparison, and commit/finalize/publication ordering. It does
  not claim candidate setter, importer bootstrap, or power-loss execution proof.
- `fixtures/mqtt-publish-path-evidence-fixture.mjs` records the generic
  outbound publisher constraints and proves that the current firmware evidence
  still does not select a canonical server command publish leaf. Until the
  server/ACL end-to-end fixture passes, command submission remains
  `CONTRACT_UNAVAILABLE` with zero publish calls.
- `fixtures/http-importer-trust-source-evidence-fixture.mjs` pins the current
  human HTTP auth/license-route limits and the getter-only identity surface. It
  explicitly classifies device-bound bootstrap, one-time nonce replay
  protection, local-only enforcement, and an identity setter as new
  firmware-gated contracts; it is not importer implementation proof.
- `fixtures/command-security-source-evidence-fixture.mjs` pins the volatile
  128-entry firmware `id`-only dedup cache, SNTP-dependent expiry, TTL/skew
  constants, dry-run ordering, response `res:` signing behavior, plaintext
  fallback without a command key, and the response-topic validator's lack of
  device-prefix binding. It is source evidence, not a broker or on-device
  runtime test.
- `fixtures/contract-artifacts-fixture.mjs` parses and checks the shared JSON
  contract set, catalog hash/rows, primary-only bundle rule, forbidden secret
  fields, load-profile target marker and byte-exact pinned license proto. It is
  a contract-artifact check, not a full schema-validator or runtime test.
- `fixtures/broker-session-envelope.test.mjs` and the broker schema define a new
  trusted EMQX-to-application boundary with fail-closed negative cases.
- `fixtures/importer-contract.test.mjs` and `deterministic-keyset.json` are
  contract/model seams only; they do not claim a current combined importer or
  production secrets.

## Contract decisions and evidence gates

- **Broker provisioning:** Release 1 provisions `PRIMARY_ONLY`. The bundle may carry a versioned secondary-broker section for forward compatibility, but `SECONDARY_ONLY`, `BOTH`, and `FAILOVER` are rejected with a stable unsupported-mode error until a firmware fixture and server ACL contract prove them. The server never silently drops secondary fields.
- **Importer trust:** The firmware importer is a new device-side contract, not an existing human-login reuse. The bundle carries a device-bound, one-time bootstrap challenge/token reference and import nonce with expiry. The device accepts the request only over its local authenticated route, rejects remote/untrusted callers and replayed nonces, and returns a redacted result. The exact bootstrap proof, local-interface check, CSRF behavior, and response codes are fixture-gated against HMS-IDF HTTP middleware.
- **MQTT publisher identity:** The application does not infer publisher identity from a topic or payload field. A trusted EMQX bridge/rule output must wrap `client_id`, authenticated username, original topic, payload, receive timestamp, and broker message identity. A `BrokerSessionIdentityProvider` validates this envelope and fails closed when metadata is absent, expired, or inconsistent. This is an infrastructure contract and does not claim current firmware support.
- **Command security boundary:** Firmware deduplication is a volatile 128-entry exact-`id` LRU in RAM, with expiry sweeps gated by completed SNTP; it is not durable, device-scoped, or key-generation-scoped. The server owns durable idempotency/retry fencing. Firmware evaluates `dry_run` before `confirm` and before post-parse dedup/security verification, so the server treats dry-run as a separate non-mutating operation. Responses are HMAC-domain-separated with `res:` when a command key exists and may be plaintext when no key exists; the server never assumes every response is signed. `response_topic` syntax validation does not bind a device prefix, so overrides require server-side namespace binding and ACL proof.
- **MQTT resolved configuration:** The source initializes a 30-second keepalive in `mqtt_config.c` but passes a 120-second client fallback when no protobuf session configuration exists. The plan therefore records both values and requires a resolved-config fixture before selecting an operational keepalive/LWT contract. A subscribe API return is not treated as SUBACK proof, and reconnect jitter is not source-proven.
- **Command catalog:** The server catalog is generated or pinned from the HMS-IDF command descriptor/build manifest. It has `catalogVersion` and `catalogHash`; CI fails on descriptor drift, and unknown version/hash denies command execution. Device capabilities only intersect availability. Catalog entries include action, payload schema, feature permission, dangerous/idempotency policy, and firmware compatibility.
- **Secret root and signing:** Production envelope encryption and ASP.NET Data Protection keys use a KMS-backed secret root with an explicit app identity, rotation/recovery policy, and backup/PITR treatment. Production signer configuration must point to an external signer or KMS/HSM; raw private PEM is rejected. License trust is an allowlist of `kid`, issuer, public key, status, effective interval, and audit history.
- **Realtime authorization:** SignalR events carry `authorizationEpoch`/`policyVersion`. The server revalidates on subscribe, reconnect, revocation, and event delivery; the client discards stale epochs. Latest-state events are coalesced per device and sent through bounded per-connection queues with dropped-event metrics.
- **Capacity and operations:** The load contract measures webhook QPS, auth-cache hit ratio, broker inflight/keepalive, reconnect jitter/backoff, DB pool/query time, worker/channel depth, SignalR queue depth, CPU, RSS, error rate, and broker loss. CI uses one app instance; a two-replica soak is required before claiming HA. Production deployment records RTO/RPO, PostgreSQL restore evidence, EMQX persistence/session expiry, TLS rotation, and N-to-N+1 bundle/contract compatibility.
- **Phase 2/3 key seam:** Phase 2 owns repository interfaces plus deterministic enrollment/keyset fixtures with fake 32-byte keys. Phase 3 may use those fixtures for EMQX auth and signing tests; Phase 4 owns production credential/key generation.

## Related Code Files
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.sln`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Api/Hms.Server.Api.csproj`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Application/Hms.Server.Application.csproj`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Domain/Hms.Server.Domain.csproj`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Infrastructure/Hms.Server.Infrastructure.csproj`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/frontend/package.json`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/contracts/mqtt/topics.md`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/contracts/mqtt/command-envelope.schema.json`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/contracts/mqtt/capabilities.schema.json`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/contracts/provisioning/device-bundle-v1.schema.json`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/contracts/license/license.proto`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/tests/Hms.Server.UnitTests/*`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/tests/Hms.Server.IntegrationTests/*`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/.github/workflows/ci.yml`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/global.json`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/Directory.Build.props`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/contracts/mqtt/command-catalog.json`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/contracts/mqtt/broker-session-envelope.schema.json`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/contracts/mqtt/command-catalog-manifest.json`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/contracts/provisioning/import-bootstrap.schema.json`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/contracts/security/license-trust-store.schema.json`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/contracts/operations/load-profile-v1.json`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/contracts/operations/availability-and-recovery.md`
- Create: `D:/WORKSPACE/PROJECT/HMS-SERVER/scripts/verify-license-schema.ps1`
- Reference: `D:/WORKSPACE/PROJECT/concrete-stress-monitoring/sources/backend/ConcreteStress.slnx`
- Reference: `D:/WORKSPACE/PROJECT/concrete-stress-monitoring/sources/backend/src/ConcreteStress.Api/Program.cs`
- Reference: `D:/WORKSPACE/PROJECT/HMS-IDF/protobuf/proto/license.proto`

## Implementation Steps
1. **Tests Before**: Add failing contract tests for topic rendering, ACL fixtures, the exact command field allowlist and required `id`/`ts`, optional `key_ver`/`ttl_ms` semantics, final `sig` marker, the 1,023-byte signed-header boundary, the 131,072-byte framed-message/body boundary and `accepts_body` allowlist, bundle schema, license adapter interface behavior, health endpoints, primary-only rejection, importer bootstrap/replay semantics, broker-session envelope validation, catalog version/hash drift, KMS/signer configuration fail-closed behavior, SignalR epoch ordering, bounded queue/coalescing behavior, and a fixture proving the chosen command publish subtopic reaches the firmware dispatcher. Until the publish-leaf fixture passes, assert `CONTRACT_UNAVAILABLE` and zero publish calls. Run the pre-cook fixtures first; they must remain independent from production implementation.
2. Freeze field names, enums, error codes, trust boundaries, event names, and versioning rules in the contract files. Add hash/check scripts that detect drift from `HMS-IDF/protobuf/proto/license.proto` and the firmware command descriptor/build manifest.
3. Record separate limits: nested license payload max 1,536 bytes, encoded envelope/HTTP/journal max 2,048 bytes, DER signature max 72 bytes, and signer key ID max 32 bytes.
4. Create the solution, project references, test projects, analyzers, nullable/implicit-usings settings, pinned SDK/package/image versions, and local developer commands.
5. Scaffold ASP.NET Core health/readiness endpoints and a Vue shell with a generated API client placeholder.
6. Add Docker Compose development services for PostgreSQL and EMQX without embedding credentials in the repository.
7. Add OpenAPI generation and frontend type generation; make contract fixtures available to backend, frontend, EMQX bridge, and HMS-IDF test suites.
8. **Tests After**: Make all phase contract tests pass and verify a clean build from an empty database.

## Success Criteria
- [ ] Contract files define exact topic suffixes, ACL direction, command envelope, bundle version, and event names.
- [ ] Topic fixtures prove prefix source, publish subtopic acceptance, QoS/retain, reconnect rebuild, and retained freshness rules.
- [ ] Server command catalog is versioned and capabilities are explicitly availability metadata only.
- [ ] Catalog drift compares a pinned source snapshot independently and denies unknown/changed hashes; the 94-row descriptor snapshot is joined to registration and static handler branch evidence, while complete schemas and runtime compatibility remain deferred.
- [ ] Primary-only provisioning and explicit rejection of unsupported secondary modes are contract-tested.
- [ ] Import bootstrap token/challenge, nonce, expiry, replay, locality, CSRF, and redacted response semantics are contract-tested; unsupported firmware behavior is marked as a new fixture-gated contract.
- [ ] Broker-session envelope identity propagation fails closed when publisher metadata is absent or inconsistent.
- [ ] The connected-wiring order and reconnect backoff are source-pinned; the canonical server command publish leaf and ACL are fixture-gated; no guessed `/cmd/execute` path is enabled.
- [ ] Command catalog version/hash is generated or pinned from the firmware manifest and drift detection denies unknown versions.
- [ ] KMS-backed secret root, Data Protection key-ring, license `kid` trust store, signer boundary, and rotation/recovery rules are documented and fail-closed in production configuration tests.
- [ ] SignalR authorization epoch/event ordering and bounded queue/coalescing semantics are contract-tested.
- [ ] Load profile records webhook QPS, auth cache, broker inflight, DB pool, reconnect jitter, queue depth, resource budgets, RTO/RPO, and rolling compatibility.
- [ ] `dotnet build` and backend unit tests run from the new solution.
- [ ] Frontend typecheck/test commands run from the new frontend.
- [ ] A CI job validates contract fixtures without production keys or broker secrets.
- [ ] A license protobuf drift check fails when the pinned schema/hash changes unexpectedly.

## Risk Assessment
- **Risk**: copying the old telemetry contracts into the new solution. **Mitigation**: reject telemetry schemas in this phase and keep the scope list explicit.
- **Risk**: bundle format becomes hard to parse on ESP32. **Mitigation**: use one small canonical JSON envelope and keep raw protobuf sidecars optional.
- **Risk**: C# and TypeScript contract drift. **Mitigation**: OpenAPI generation plus shared JSON fixtures and CI drift checks.
- **Risk**: SDK/package/image drift makes the empty skeleton unreproducible. **Mitigation**: pin `global.json`, `Directory.Build.props`, package versions, and container image tags in CI.
- **Risk**: a new importer or broker bridge contract is mistaken for current firmware behavior. **Mitigation**: label each new field/endpoint as fixture-gated and block Phase 1 completion until the corresponding HMS-IDF fixture or explicit release limitation exists.

## File inventory and test matrix

| Area | Files to create | Tests before implementation |
| --- | --- | --- |
| Contracts | `contracts/mqtt/*`, `contracts/provisioning/*`, `contracts/license/*` | schema valid/invalid, topic/ACL examples, byte boundaries, firmware topic fixture |
| Backend skeleton | `src/Hms.Server.*` | health/readiness, DI composition, no-secret startup |
| Frontend skeleton | `frontend/*` | typecheck, router shell, API client generation |
| CI | `.github/workflows/ci.yml` | clean checkout contract/build job |
| Trust and operations | `contracts/security/*`, `contracts/operations/*`, broker/import schemas | bootstrap replay, signer/KMS, epoch ordering, queue bounds, load/RTO/RPO contract tests |

## Dependency map

- Requires approved design report and current firmware source.
- Blocks every later phase because all later data and message shapes depend on these contracts.
- Must not depend on a running production broker, database, vendor key, or physical device.

## Function/interface checklist

- [ ] `TopicBuilder` renders all device topics from prefix/device ID.
- [ ] `TopicContract` records QoS, retain, reconnect, and freshness semantics.
- [ ] `CommandCatalog` is the authority for action schema/permission/danger policy; device capabilities only intersect availability.
- [ ] `CommandEnvelopeSerializer` exposes pre-sign and post-sign byte forms.
- [ ] `ICommandSigner`, `ILicenseIssuer`, and `IProvisioningBundleWriter` interfaces are declared without implementation coupling.
- [ ] `IEmqxWebhookAuthenticator` contract includes replay protection, rotation, and proxy/header-stripping behavior.
- [ ] `IBrokerSessionIdentityProvider` validates the trusted broker-session envelope and fails closed on missing/inconsistent identity metadata.
- [ ] `ICommandCatalogManifest` records firmware source/version/hash and denies unknown catalog versions.
- [ ] `IImporterBootstrapAuthenticator` validates device-bound one-time challenge/token, nonce, expiry, local caller, and CSRF contract.
- [ ] `ISecretRootPolicy` and `ILicenseTrustStore` define KMS/Data Protection roots, `kid` allowlist, rotation, recovery, and audit semantics.
- [ ] `IRealtimeAuthorizationEpoch` and `IEventBackpressurePolicy` define epoch ordering, coalescing, queue bounds, drop metrics, and fail-closed behavior.
- [ ] `IEventPublisher` names status, capabilities, command, license, and provisioning events.
- [ ] OpenAPI error envelope includes stable code, message, correlation ID, and validation details.

## Explicit phase gate

### Tests Before
Run all source-anchored firmware fixtures, schema parsing, catalog/handler mapping, and plan validation before creating solution code.

### Smallest seam
Add only contract schemas, fixture runners, deterministic keyset inputs, and test composition.

### Implementation scope
Create the ASP.NET/Vue skeleton and shared adapters after the contract gate; do not add uncontracted domain behavior.

### Negative/regression tests
Reject unknown fields, 1,024-byte commands, changed catalog hashes, unsupported broker modes, missing broker identity, replayed bootstrap data, and invalid licenses.

### Phase regression gate
Plan validation, all pre-cook fixtures, schema parsing, and an empty-solution build pass.

### Input/output contract
Inputs are approved architecture and current HMS-IDF evidence; outputs are versioned schemas, hashes, fixture commands, and reproducible build/test commands.

### Dependencies
Requires current HMS-IDF source and approved design; blocks all later phases.

### Ownership
Phase 1 owns cross-cutting contracts, fixture provenance, and solution/test harness boundaries.

### Risk
Guessed protocol fields or stale documents can become shared dependencies; unsupported behavior stays denied.

### Evidence classification
Use `SOURCE_PROVEN`, `CONTRACT_TESTED`, and `DEFERRED_TO_PHASE_N - REQUIRED`; do not claim runtime proof.

### Release gate
Command publish, importer activation, and production signing remain disabled without their integration evidence.

### Definition of done
Every contract has an owner/test, unsupported behavior fails closed, validation is clean, and no production phase is complete.

### Continuation evidence — HTTP boundary limits (2026-09-30)

The host accepts at most 1 MiB request bodies, while command, license-apply,
and provisioning-create mutations are limited to 64 KiB. License and
provisioning DTOs also validate bounded string, byte-array, port, and generation
values. Reflection and endpoint metadata tests cover these limits; they do not
change any firmware bundle or MQTT wire contract.
