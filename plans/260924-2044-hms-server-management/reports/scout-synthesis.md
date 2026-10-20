# Plan Scout Synthesis

## Scope

This report supports the approved HMS-SERVER management/control-plane plan. It records evidence used to choose phase files and test gates. No source code was changed.

## HMS-SERVER baseline

- The repository contains design documents only; no solution, source tree, migrations, package manifest, or tests exist.
- The approved target is ASP.NET Core 10 + Vue 3 + PostgreSQL/EF Core + EMQX + SignalR.
- The first release stores latest operational state and capabilities only. It does not persist telemetry.

## Reference backend patterns

The reference backend is split into `ConcreteStress.Api`, `ConcreteStress.Application`, `ConcreteStress.Domain`, and `ConcreteStress.Infrastructure` under `D:/WORKSPACE/PROJECT/concrete-stress-monitoring/sources/backend`.

Reusable patterns:

- `src/ConcreteStress.Api/Program.cs`: DI, CORS, hosted workers, JWT, and policy wiring.
- `src/ConcreteStress.Infrastructure/Persistence/ApplicationDbContext.cs`: EF Core DbSets and relationship configuration.
- `src/ConcreteStress.Infrastructure/Services/ScopeAuthorizationService.cs`: hierarchical scope evaluation.
- `src/ConcreteStress.Infrastructure/Security/ResourceAuthorizationService.cs`: explicit deny and resource checks.
- `src/ConcreteStress.Api/Authorization/PermissionAuthorizationHandler.cs` and `PermissionAttribute.cs`: feature policy checks.
- `src/ConcreteStress.Infrastructure/Mqtt/GatewayMqttService.cs`: hosted MQTT connection and reconnect pattern.
- `src/ConcreteStress.Infrastructure/Hubs/MonitoringHub.cs`: authenticated SignalR pattern.
- `tests/ConcreteStress.UnitTests/Services/ScopeAuthorizationServiceTests.cs` and related controller/integration tests: porting templates.

The reference gateway model is not copied verbatim. HMS needs an owner organization that is mandatory, nullable classification links, separate enrollment/key/license aggregates, and action-level grants.

## Reference frontend patterns

The reference frontend is Vue 3 + TypeScript + Vite with Pinia, Vue Router, TanStack Vue Query, OpenAPI types, Zod, and Vitest.

Reusable patterns:

- `src/features/gateway-device/views/GatewayDeviceListView.vue`: URL-synchronized filters, paging, table actions.
- `src/features/gateway-device/views/GatewayDeviceDetailView.vue` and identity/config tabs: detail layout.
- `src/features/gateway-device/ui/CreateDeviceModal.vue` and `GatewayBootstrapForm.vue`: creation/provisioning flow.
- `src/features/user/views/UserPermissionsView.vue`, `UserDataScopeModal.vue`, `UserGrantsModal.vue`, and `GrantsMatrixPane.vue`: scope/grant editor.
- `src/app/nav.config.ts`, `src/app/router/routes.ts`, `src/core/auth/can.ts`, and navigation guard tests: fail-closed feature navigation.
- `src/shared/ui/StandardListPage.vue`, filter bars, `CopyableInput`, query error handling, and per-feature tests: consistent management UI.

## Firmware contract evidence

- `components/command-dispatcher/include/cmd_security.h` and `cmd_security.c`: three 32-byte key types, direct `crypto/cmd_key` HMAC over raw JSON, strict 1..1023-byte signed-header limit, final `sig`, required `id`/`ts`, optional `key_ver`/`ttl_ms`/`dry_run`/`confirm`/`response_topic`/`trace_id`, key-version fencing, and replay/dedup behavior. Unknown `kid`/`generation` top-level fields are rejected. `cmd_frame.h/c` adds a separate 131,072-byte framed-message path with a 1,023-byte header and body acceptance only for descriptor rows that opt in.
- `components/command-dispatcher/cmd_descriptor.c`: firmware command manifest and descriptor metadata used for generated capability output; server catalog hashing remains a new fixture contract.
- `services/mqtt-service/mqtt_service.c`: `<prefix>/<device_id>/cmd/#` subscription at QoS 1, `/cmd/res` and `/audit` non-retained QoS 1, retained `/capabilities`, and reconnect rebuild from current identity.
- `components/mqtt-status-plane/src/status_plane.c`: `/status` QoS 1 non-retained and `/status/connectivity` QoS 1 retained online/LWT state.
- `components/hms-license/include/hms_license.h` and `components/hms-license/src/*`: request/apply/remove, lifecycle, binding, nonce/counter, journal.
- `components/hms-http-api/license/license_api.c`: binary license request/apply routes and permission expectations.
- `protobuf/proto/license.proto`: protobuf field limits and ECDSA P-256/SHA-256 envelope contract.
- `tools/license-tool/license_tool.py` and `license_manager.py`: vendor signing and request decoding reference.

`components/util-device-identity/src/device_identity.c` exposes active identity getters but no combined setter/importer. `hms-license/src/license_evaluator.c` validates a nonempty signed `device_id` against active NVS identity, so first enrollment needs a candidate-identity validation seam without exposing staged identity through active getters.

No complete combined importer was found for `device_id`, MQTT credentials, `crypto/*` keys, and license bytes. The plan therefore includes an explicit cross-repository firmware phase with pre-marker versus post-marker `license_apply()` recovery states.

## Required TDD additions

- Authorization composition: feature + resource + action, deny precedence, cross-tenant IDs, nullable classification.
- MQTT: exact topics/ACL, retained status/capabilities, reconnect, command response correlation, timeout.
- Signing: C-compatible golden vectors, exact raw JSON, direct key, 1,023-byte boundary, rotation and response domain.
- License: protobuf round trip, binding, counter/nonce/replay, invalid signature/kid, expiry, remove tombstone.
- Provisioning: encrypted-at-rest secrets, one-time bundle, redaction, rotation/revoke, idempotency.
- Firmware importer: crash-consistent staged NVS writes with boot reconciliation (not cross-partition atomicity), malformed/unknown version rejection, no secret logging, idempotent re-import.
- Frontend: route guards, URL filters, grant editor, provisioning/download state, capability-driven command forms, SignalR authorization.
- End-to-end: real EMQX/PostgreSQL, 1,000-device status fan-in, command ACK/timeout, audit completeness.
