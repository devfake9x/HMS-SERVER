# Red-team review: DO relay control plan (assumption destroyer)

Reviewer: code-reviewer (Fact Checker + Contract Verifier). Date: 2026-11-06.
Scope: `plan.md`, `phase-01..03`, brainstorm `261106-0730`, `docs/architecture-mqtt-device-link.md`, spec `mqtt-api.md` §6.4 (lines 776-795) and §6.4 disposition (lines 876-890). No firmware source opened.

## Finding 1: The read-back `status` event can be overwritten by the next heartbeat, and the UI has no fallback
- **Severity:** High
- **Location:** Phase 1, "Requirements → Chiếu trạng thái"; Phase 2, "Architecture" (read-back) and "Tests first" #5
- **Flaw:** The plan publishes the projected DO state as event type `status` with a payload that contains only `digital_outputs`. Both delivery layers treat `status` as a full latest-state snapshot. A newer `status` event replaces the older one for the same device. The 3 s heartbeat publishes `status` without `digital_outputs`, so if it lands while the projected event is still queued, the DO update is replaced and never delivered. Phase 2 makes this event the only path to the screen ("UI không tự parse reply"), and nothing re-fetches telemetry after read-back.
- **Failure scenario:** DO1 ON → `Executed` → UI sends `get_status` → the correlator publishes `status{digital_outputs}` → within the ≤100 ms drain window, or while that connection's backlog is still sending earlier events (one event per connection per tick, each send waits up to 2 s), a heartbeat publishes `status{uptime,...}`, which replaces it. The panel reaches "Đã đọc lại" but still shows OFF while the relay is ON. Heartbeats do not carry DO state, so the panel stays wrong until another read-back. A full publisher queue also evicts `status` first, on the assumption that "the next snapshot can rebuild it". That is false for DO state.
- **Evidence:**
  - Publisher coalescing: `src/Hms.Server.Application/Realtime/RealtimeContracts.cs:88-92` (replace in `_latest`) and `:179-182` (`status` is coalescible).
  - Eviction comment "Dropping latest state is safe; the next snapshot can rebuild it": `RealtimeContracts.cs:105-110`.
  - Per-connection coalescing: `src/Hms.Server.Api/Hubs/SignalRConnectionBacklog.cs:39-49`.
  - Delivery cadence: `src/Hms.Server.Api/Hubs/SignalRDeviceEventDeliveryService.cs:21-22` (100 ms poll, 2 s deadline) and `:100-148` (sequential, one event per connection per loop).
  - Heartbeat rate 3 s: `src/Hms.Server.Api/Controllers/DeviceLinkController.cs:26`. Heartbeat publishes `status`: `src/Hms.Server.Application/DeviceLink/DeviceLinkHandlers.cs:203-204`.
  - Plan: phase-01 line 36 ("publish event `status` chỉ chứa `digital_outputs`") and phase-02 line 43 ("UI không tự parse reply").
- **Suggested fix:** Publish the projection under its own event type (for example `digital_outputs`), either non-coalescible or coalesced only with itself. Also, when the `get_status` poll reaches `Executed`, have the panel re-fetch `GET /telemetry`, or read `reply.data.channels` (the issuer already has it). Add tests: "projection followed by a heartbeat before drain still delivers DO" and "queue-full eviction does not drop DO".

## Finding 2: The per-device channel allowlist fails open on routine operations
- **Severity:** High
- **Location:** Phase 1, "Requirements → Danh sách kênh cho phép", "API", "Seed dev"; "Risk Assessment → Allowlist chỉ để an toàn cho lab"
- **Flaw:** `AllowedOutputChannels` is the only server-side guard for DO2–DO6 on the real gateway, and `null` means unlimited. Several ordinary paths silently turn it into `null`:
  - (a) `PUT /link` replaces the whole binding through `DeviceMqttBinding.Create` + `Upsert`. A body without the new field (every existing caller sends `{topicPrefix, mqttDeviceId, keyVersion}`) deserializes to `null` and clears the allowlist.
  - (b) The dev seed exists only in user-secrets: no `Mqtt` section is committed. `dotnet user-secrets set "Mqtt:Devices:0:AllowedOutputChannels" "[1]"` writes a scalar string, which does not bind as an array (array keys are `...:AllowedOutputChannels:0`). The result is null or empty, never `[1]`.
  - (c) `[]` has no defined meaning: deny all, invalid, or the same as null.
  - (d) The brainstorm said "unconfigured = allow the channels `get_status` reported". The plan changed this to "device decides" without saying so.
- **Failure scenario:** During Phase 3 an operator re-PUTs the link to change `keyVersion` (key rotation happened today, see architecture doc lines 41-44). The allowlist is cleared, `GET /link` returns `null`, and the panel shows buttons for DO2–DO6 on `gateway_870c`. One click actuates a relay with unknown wiring. The same happens on any dev restart where the user-secret did not bind.
- **Evidence:**
  - `src/Hms.Server.Api/Controllers/DeviceLinkController.cs:69` and `:145` (request record with optional fields).
  - `src/Hms.Server.Application/DeviceLink/DeviceLinkHandlers.cs:270-277` (Create + Upsert replaces the record).
  - `src/Hms.Server.Infrastructure/Persistence/EfDeviceMqttBindingStore.cs:32-34` (field-by-field overwrite).
  - `src/Hms.Server.Api/Program.cs:127-147`: the seed runs on every Development start, and line 143 skips the whole binding on `ArgumentException`.
  - No `Mqtt` key in `src/Hms.Server.Api/appsettings*.json` (grep returned nothing).
  - Brainstorm line 36 vs phase-01 line 35.
- **Suggested fix:** Make the update partial: an absent field keeps the stored value, and clearing needs an explicit `null` or a separate endpoint. Define `[]` as deny-all. Add a test that `PUT /link` without the field keeps `[1]`. Add a startup assertion or log that prints the effective allowlist per seeded device, and document the exact user-secrets keys (`:0`). Present "unconfigured = deny" vs "unconfigured = unlimited" to the user as an explicit choice, because the brainstorm and the plan disagree.

## Finding 3: The projection broadcasts reply data gated by `gpio.do_view` to every in-scope viewer, and the UI ignores `gpio.do_view`
- **Severity:** Medium
- **Location:** Phase 1, "Requirements → Chiếu trạng thái"; Phase 2, "Ẩn hoặc vô hiệu nút"
- **Flaw:** `control.get_status` needs `gpio.do_view`. Today the code deliberately keeps reply data behind the issuer's read-back. Both the realtime event and `GET /commands/{id}` withhold data from other viewers, because "Reply data was released by a permission the sender holds, which a viewer may not". The projection writes that data into telemetry, which anyone with device scope can read, and into a `status` event for the whole device group. The plan does not acknowledge that it reverses this invariant. Separately, Phase 2 gates the buttons on `gpio.do_write` only. The automatic read-back and the "Đọc trạng thái" button both need `gpio.do_view`.
- **Failure scenario:** (1) A user with only device scope and no `gpio.do_view` sees live relay state through `GET /telemetry` and SignalR. (2) A role holding `gpio.do_write` without `gpio.do_view` gets `Executed` on `set_output`, then `DEVICE_PERMISSION_REQUIRED` on the automatic `get_status`. The channel stays at "Chờ đọc lại" forever.
- **Evidence:**
  - `src/Hms.Server.Infrastructure/Commands/command-catalog.json:362-372` (`get_status` → `gpio.do_view`).
  - `src/Hms.Server.Api/Controllers/DeviceCommandsController.cs:68-70`.
  - `src/Hms.Server.Application/DeviceLink/DeviceLinkHandlers.cs:141-142`.
  - `docs/architecture-mqtt-device-link.md:75,77`.
  - Telemetry read is scope-only: `src/Hms.Server.Api/Controllers/DevicesController.cs:85-86`. SignalR delivery is scope-only: `SignalRDeviceEventDeliveryService.cs:77-80`.
  - Brainstorm line 39 wants every viewer updated, so this is a user decision that conflicts with an existing boundary.
- **Suggested fix:** Put the options to the user:
  - (a) Accept the exposure and update the architecture doc and the invariant comments.
  - (b) Filter `digital_outputs` per subscriber or reader by `gpio.do_view`.

  In both cases, gate the read-back controls on `gpio.do_view` and add a test for a role that has write but not view.

## Finding 4: UI permission gating cannot see device-role permissions, so "UI ẩn nút" means "only master-admin"
- **Severity:** High
- **Location:** Phase 2, "Requirements → thiếu `gpio.do_write` (đọc từ catalog `requiredPermission` và snapshot quyền)"; plan.md "Acceptance" line 38
- **Flaw:** The server checks `gpio.do_write` against the role of the actor's device membership (`DeviceAccess.Permissions`). `/me/permissions` returns feature grants, org scopes, `device:{id}:action:{x}` grants and `perm` claims. It never returns device-role permission codes. `can(snapshot, 'gpio.do_write')` is therefore true only for master-admin (`*`), or for someone given a feature grant literally named `gpio.do_write`, which the server then rejects. No API endpoint exposes device roles or memberships, so the non-admin positive path cannot be tested. The plan also drops the catalog trust/proof pre-flight that the modal enforces (`isCatalogVerified`).
- **Failure scenario:** An operator who holds a device role with `gpio.do_write` sees no buttons (false negative). A user given the feature grant `gpio.do_write` sees buttons and every click returns `DEVICE_PERMISSION_REQUIRED` (false positive). The test "Thiếu `gpio.do_write`: không kênh nào có nút" passes while proving nothing about device-role permissions.
- **Evidence:**
  - `src/Hms.Server.Api/Controllers/MeController.cs:25-37`.
  - `src/Hms.Server.Application/Authorization/DeviceAccessResolver.cs:23,35`.
  - `src/Hms.Server.Domain/Authorization/DeviceAccessPolicy.cs:41`.
  - `frontend/src/core/auth/permissions.ts:57-70`.
  - `src/Hms.Server.Application/Authorization/PermissionContracts.cs:116-140` (any string can be granted as a feature by master-admin).
  - No controller wires `AssignDeviceMembershipHandler` (it is registered only at `src/Hms.Server.Infrastructure/DependencyInjection.cs:120`).
  - Modal pre-flight: `frontend/src/features/devices/components/DeviceCommandModal.vue:130-140,177-179`.
- **Suggested fix:** Add a per-device effective-permission read, for example `GET /devices/{id}/link` returning `effectivePermissions` from `IDeviceAccessResolver`, and gate on it. Otherwise state plainly that the panel is master-admin-only in this slice and rewrite the acceptance line. Keep the catalog-proof check that the modal has.

## Finding 5: Pulse read-back captures a transient ON, and the panel never refreshes after that
- **Severity:** High
- **Location:** Phase 1, projection shape `[{channel,state,mode}]`; Phase 2, "Tiến trình mỗi kênh", Pulse default 1000 ms; Phase 2 "Risk Assessment" line 91
- **Flaw:** A pulse turns itself off after `duration_ms`. The plan reads back immediately after `Executed` (round trip about 1 s on the real gateway), so `get_status` lands inside or at the edge of a 1000 ms pulse and reports `state:true`. The projection drops `pulseActive` from spec `{channel,state,mode,totalCommands?,pulseCount?,pulseActive?}`, so the UI cannot tell "pulsing" from "latched ON". Heartbeats carry no DO state, `get_status` replies from the firmware HIL tool are not projected (correlator ignores ids it did not issue), and there is no periodic refresh or timestamp. The Phase 2 claim "Trạng thái cuối luôn lấy từ lần đọc lại nên không bị hiển thị sai lâu" is therefore false.
- **Failure scenario:** On the simulator, DO6 pulses for 1000 ms. The read-back at about 900 ms shows ON. The relay drops at 1000 ms, but the panel shows ON indefinitely. Likewise, after the firmware team's HIL tool toggles DO1 (which Phase 3 itself warns about), the panel keeps the old state with no age indicator.
- **Evidence:**
  - Spec `mqtt-api.md:778,780` (pulse sets `state:true` for its duration; `pulseActive` exists).
  - Plan phase-01 line 36, phase-02 lines 21, 26, 91.
  - Correlator ignores ids it did not issue: `src/Hms.Server.Application/DeviceLink/DeviceLinkHandlers.cs:48-50`.
  - 1 s per command live: `docs/architecture-mqtt-device-link.md:101`.
  - Heartbeat has no DO: brainstorm line 29.
- **Suggested fix:** Project `pulseActive` and a `readAt` timestamp. For pulse, delay the read-back until after `duration_ms`, or poll until `pulseActive:false`. Show the age of the DO state in the panel. Add a simulator scenario that models pulse expiry.

## Finding 6: A partial `Record` call wipes heartbeat-derived telemetry and fakes a heartbeat timestamp
- **Severity:** Medium
- **Location:** Phase 1, "Implementation Steps" #6 (`IDeviceTelemetryStore.Record` (merge)); "Tests first" #5
- **Flaw:** `Record` rebuilds every DTO scalar (`UptimeSeconds`, `FreeHeap`, `MqttConnected`, …) only from the incoming dictionary. The merge keeps just seven whitelisted keys (`digital_outputs`, `digital_inputs`, `mqtt`, `hostname`, `voltage`, `temperature`, `batteryLevel`). Calling `Record(deviceId, {digital_outputs})` therefore nulls all scalars and drops `uptime_seconds`, `free_heap`, `network`, `services` and `health` from `Data`. It also sets `Timestamp = UtcNow`, which the UI shows as "last heartbeat". The plan tests only the opposite direction (a heartbeat keeps DO).
- **Failure scenario:** After any read-back, `GET /telemetry` returns uptime, heap and connectivity as null until the next heartbeat. A user who opens or refreshes the device page in that window sees N/A everywhere, plus a "last heartbeat" time that was never a heartbeat. The same projection path also runs while the device is offline: the reply arrives but `B/status` heartbeats do not.
- **Evidence:**
  - `src/Hms.Server.Application/Realtime/DeviceTelemetryContracts.cs:33-114` (scalars only from `data`), `:116-147` (whitelist merge), `:149-151` (`UtcNow`).
  - UI uses `tel.timestamp` as the heartbeat time: `frontend/src/features/devices/views/DeviceDetailView.vue:305-308`.
- **Suggested fix:** Add a dedicated `RecordDigitalOutputs(deviceId, channels, readAt)` method. It updates only that key and leaves the scalars and timestamp alone. Add a test that a projection keeps uptime and heap.

## Finding 7: The read-back source is forgeable through the existing HTTP telemetry ingest
- **Severity:** Medium
- **Location:** Phase 2, "Trạng thái ON/OFF chỉ lấy từ `digital_outputs`"; brainstorm line 31 (spec R4: read-back is the proof)
- **Flaw:** `POST /devices/{id}/telemetry` accepts any dictionary from any user with device scope. It has no `[ValidateAntiForgeryToken]` and no device permission (`device.record_measurements` is marked "Reserved: no route requires it"). The handler calls `Record` and publishes a `status` event. The plan makes `digital_outputs` the only source of truth for relay state, so this endpoint can overwrite the "verified" read-back for every viewer.
- **Failure scenario:** An in-scope user (or a cross-site POST, since there is no CSRF check) sends `{"digital_outputs":[{"channel":1,"state":false}]}` while DO1 is physically ON. Every open panel shows OFF.
- **Evidence:**
  - `src/Hms.Server.Api/Controllers/DevicesController.cs:96-125` (no antiforgery attribute, unlike `:50,132,154`).
  - `src/Hms.Server.Domain/Authorization/DevicePermissionCodes.cs:12-13`.
- **Suggested fix:** Have the HTTP ingest strip or reject the `digital_outputs` key, or keep projected DO state in a separate store that only the correlator writes. Add a test for it.

## Finding 8: Phase 3 steps 4 and 5 cannot be run from the DO panel, and the modal's default `set_output` template fails the new policy
- **Severity:** Medium
- **Location:** Phase 3, "Gateway thật" steps 4-5 and "Kiểm tra trên UI: panel DO cập nhật theo từng bước"; Phase 2, `DeviceCommandModal.test.ts (giữ nguyên hành vi)`; plan.md Acceptance line 36
- **Flaw:** Phase 2 hides the buttons for channels outside the allowlist and shows Pulse only when `mode === 'pulse'`. So "DO2 ON → `DO_CHANNEL_NOT_ALLOWED`" and "Pulse DO1 while toggle → `DEVICE_REJECTED`, reason shown on UI" are unreachable from the panel. Phase 2 test #6 (`DO_CHANNEL_NOT_ALLOWED` shown at the channel) can only happen with a stale allowlist. The only UI route left is the C2 modal. Its preset for `control.set_output` is `{channel:1, value:1}` (a number), which the new policy rejects as `COMMAND_PAYLOAD_INVALID` ("`value` là bool"). The plan keeps the modal's behaviour unchanged.
- **Failure scenario:** During live verification the operator opens the modal, picks `control.set_output` and gets `COMMAND_PAYLOAD_INVALID` from the default template. The UI screenshots required for steps 4 and 5 cannot be taken from the panel.
- **Evidence:**
  - `frontend/src/features/devices/components/DeviceCommandModal.vue:149-152`.
  - Plan phase-02 lines 21, 24, 55, 72; phase-03 lines 26-28.
  - Spec `mqtt-api.md:795` (numeric accepted by the device; the server is stricter by choice).
- **Suggested fix:** Change the modal preset to `{channel:1, value:true}` and add a test for it. Rewrite Phase 3 steps 4-5 to run through the modal or the API, plus the sniffer. Drop or reword the "lý do hiện trên UI" acceptance for pulse-on-toggle, or show a disabled Pulse with a reason.

## Finding 9: The rule "do not modify existing tests" contradicts the work, and the plan misses callers that must change
- **Severity:** Medium
- **Location:** plan.md line 29 ("Không được sửa test hiện có cho dễ qua"); Phase 1 "Success Criteria" ("427 unit + 103 integration test cũ vẫn xanh"); Phase 1 "Implementation Steps" #2 (separate `ControlOutputActions` set)
- **Flaw:** Several existing assertions must fail once the plan is implemented:
  - `CatalogTests.cs:123` asserts every enabled action is class Read.
  - `CatalogTests.cs:126` asserts `control.pulse` is Denied.
  - `CatalogTests.cs:120-122` asserts enabled == `EnabledActions`.
  - `ManagementApiTests.cs:196-199` asserts `available == EnabledActions.Contains(...)`. A separate `ControlOutputActions` set breaks this for exactly the two new actions.

  Adding the projection's dependencies to the correlator constructor breaks the direct construction in `DeviceLinkTests.cs:315-316`, which is not in the Modify list. The success criterion as written cannot be met.
- **Evidence (contract enumeration):**
  - `PinnedCommandCatalog.EnabledActions`: `tests/Hms.Server.UnitTests/CatalogTests.cs:121`; `tests/Hms.Server.IntegrationTests/ManagementApiTests.cs:197`.
  - `DeviceCommandResponseCorrelator` constructor: `tests/Hms.Server.UnitTests/DeviceLinkTests.cs:315` (missing from plan); DI at `src/Hms.Server.Infrastructure/DependencyInjection.cs:44`.
  - `HmsIdfCommandProtocolAdapter` constructor (breaks if the policy is injected): `tests/Hms.Server.IntegrationTests/MaitechRtuDeviceIntegrationTests.cs:33,48`; `tests/Hms.Server.UnitTests/DeviceLinkTests.cs:314`.
  - `DeviceMqttBinding.Create` (breaks if a required parameter is added): `src/Hms.Server.Application/DeviceLink/DeviceLinkHandlers.cs:275`; `tests/Hms.Server.IntegrationTests/MaitechRtuDeviceIntegrationTests.cs:30`; `tests/Hms.Server.UnitTests/DeviceLinkTests.cs:295,311`.
  - `DeviceMqttBinding` positional constructor: `DeviceLinkContracts.cs:35`; `EfDeviceMqttBindingStore.cs:48-50` (both listed).
  - `ConfigureDeviceMqttBindingHandler.Handle`: `DeviceLinkController.cs:69`; `Program.cs:134-135` (both listed).
  - `DeviceLinkDto`: `DeviceLinkController.cs:41-51,133-143`; `frontend/src/core/api/adminApi.ts:102-113`.
- **Suggested fix:** List each existing assertion that changes on purpose, with the new invariant. For example: "enabled == `EnabledActions ∪ ControlOutputActions`; enabled non-Read == exactly the DO pair". Add `DeviceLinkTests.cs` to the Modify list, or keep the correlator constructor stable (for example by injecting `IEnumerable<IDeviceReplyProjection>` with a default). Replace "427 + 103 cũ vẫn xanh" with "all green after the listed intentional edits".

## Finding 10: The projection runs inside the correlator's commit sequence with no error contract
- **Severity:** Medium
- **Location:** Phase 1, "Implementation Steps" #6 ("correlator gọi nó sau khi ghi audit"); "Tests first" #5
- **Flaw:** The correlator does three things in order: it records the audit (`:78`), saves the reply (`:79-86`), then publishes `command_result` (`:87`). The plan inserts the projection "after audit". It does not say whether the projection runs before the reply is saved or what happens when the reply data has an unexpected shape. That shape is controlled by the device: `channels` not an array, `channel` not an int, a missing `state`. If the projection throws, the reply is never saved and `command_result` is never published. The exception is only logged by `ProcessInboundAsync`. The plan's tests cover only a bad signature, not a bad shape.
- **Failure scenario:** A firmware update returns `channels` as an object. The projection throws after the audit says `Executed`. The issuing user's poll sees `Executed` with `reply: null`, the realtime `command_result` never arrives, and the panel shows "Executed" with no read-back data.
- **Evidence:**
  - `src/Hms.Server.Application/DeviceLink/DeviceLinkHandlers.cs:74-88`.
  - `src/Hms.Server.Infrastructure/Mqtt/MqttDeviceLinkService.cs:313-322` (catch-and-log).
  - Plan phase-01 line 115.
- **Suggested fix:** Run the projection after `PublishResultAsync`, as a non-throwing step. Validate the shape strictly and skip with a warning if it does not match. Add tests for malformed `channels` and malformed `channel`.

---

## Fact check (claims cited by the plan)

| Claim | Result |
|---|---|
| `PinnedCommandCatalog.EnabledActions` opens only class R | VERIFIED `src/Hms.Server.Infrastructure/Commands/PinnedCommandCatalog.cs:48-62,190-194` |
| `set_output`/`pulse`: `gpio.do_write`, class B, TTL 120000, no confirm | VERIFIED `command-catalog.json:373-406` |
| `get_status` permission (not mentioned by plan) | VERIFIED `gpio.do_view`, `command-catalog.json:362-372` |
| Adapter order: binding → names → capabilities → confirm → dry_run → key | VERIFIED `HmsIdfCommandProtocolAdapter.cs:28-58` |
| API payload values are `JsonElement` | VERIFIED `DeviceCommandsController.cs:102` (`IReadOnlyDictionary<string, object?>`) |
| "store đã merge giữ lại sẵn" `digital_outputs` across heartbeats | VERIFIED only in that direction (`DeviceTelemetryContracts.cs:119-122`); FAILED for the reverse (Finding 6) |
| Correlator only handles ids the server is waiting for | VERIFIED `DeviceLinkHandlers.cs:48-50` |
| `applyTelemetryPayload` only updates fields that are present | VERIFIED `DeviceDetailView.vue:162-247` (DO at `:210-218`, ignores empty arrays) |
| Fake 6-channel DO data in the panel | VERIFIED `DeviceDetailView.vue:856-863` |
| Modal poll 1 s / 40 s | VERIFIED `DeviceCommandModal.vue:40-42,55-74` |
| UI can read `gpio.do_write` from the permission snapshot | FAILED (Finding 4) `MeController.cs:25-37` |
| `device_mqtt_bindings` table, EF store, InMemory store | VERIFIED `ApplicationDbContext.cs:91-103`, `EfDeviceMqttBindingStore.cs`, `DependencyInjection.cs:40-43` |
| `int[]` column works under EF InMemory | UNVERIFIED (not built). Dev without a connection string uses `InMemoryDeviceMqttBindingStore`, not EF. `PersistenceModelTests` uses `UseInMemoryDatabase` (`:32` etc.), so the planned "EF store lưu và đọc lại" test runs on InMemory and does not test the Npgsql array mapping. Only `Model_snapshot_has_no_pending_changes` (`:75-86`) touches the Npgsql model. |
| Dev seed `Mqtt:Devices:0:AllowedOutputChannels` | VERIFIED the code path (`MqttLinkOptions.cs:46-56`, `Program.cs:127-147`). The value has no committed location: there is no `Mqtt` section in `appsettings*.json`. |
| ADR 260927 (no per-action switch in the MQTT service) | VERIFIED as currently true (`MqttDeviceLinkService.cs` has no action switch) |
| `frontend/src/core/commands/` exists for the new composable | VERIFIED (`catalogVerification.ts`, `commandAvailability.ts`) |
| `sim-device.js` | VERIFIED it exists only in the current session scratchpad and is not in git (`git ls-files` has no match). A new session would not have it. |
| "427 unit + 103 integration", "139 frontend" | UNVERIFIED (tests not run, as instructed) |

Status: DONE_WITH_CONCERNS
Summary: The plan's read-back path rests on a partial `status` event, but the realtime pipeline coalesces and evicts `status` events as full snapshots. The allowlist that protects the real relays fails open on an ordinary `PUT /link`, and UI permission gating cannot see device-role permissions. Also, the projection silently reverses the existing invariant that reply data stays with the issuer.
