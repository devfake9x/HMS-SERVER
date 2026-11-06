# Red-team review (security adversary): DO relay control plan

Scope: `plan.md`, `phase-01-server-do-control.md`, `phase-02-do-panel-ui.md`, `phase-03-live-verification.md`, checked against the worktree code and `HMS-IDF/engineering-docs/api/mqtt-api.md` (spec only, no firmware source).
Roles: Fact Checker and Contract Verifier (Standard tier).

## Finding 1: The read-back channel the plan trusts can be forged by three unauthenticated or under-privileged writers
- **Severity:** High
- **Location:** Phase 1, Requirements "Chiếu trạng thái" (lines 36-37) and Tests first #5 (lines 102-105). Phase 2, Requirements "Trạng thái ON/OFF" (line 28) and Architecture "Read-back" (line 43).
- **Flaw:** The verified `get_status` projection writes the same telemetry key (`digital_outputs`) and the same event type (`status`) that three unverified paths already write verbatim. The UI cannot tell signed state from forged state. The test "Reply get_status có chữ ký sai → không chiếu" covers the only path that was already authenticated and leaves the forgeable paths open.
- **Failure scenario:**
  1. An attacker publishes `{"digital_outputs":[{"channel":1,"state":false,"mode":"toggle"}]}` on `hms_toan_lab/gateway_870c/status` (the broker is anonymous) or on `.../status/events`. Alternatively, any in-scope user with no device permission sends `POST /api/v1/devices/{id}/telemetry` with that body.
  2. Every viewer's DO panel shows DO1 OFF while the relay is ON.
  3. The Phase 2 toggle picks Bật/Tắt "tuỳ trạng thái hiện tại", so the forged state also chooses which command the operator sends.
  4. The telemetry merge keeps the forged value until the next real `get_status`. A forged heartbeat every 3 s overrides any real read-back.
- **Evidence:**
  - Heartbeat → `telemetry.Record(deviceId, status)` + `PublishAsync(..., "status", status)`: `src/Hms.Server.Application/DeviceLink/DeviceLinkHandlers.cs:195-205`.
  - Device events are forwarded raw: `DeviceLinkHandlers.cs:219-226`.
  - HTTP ingest checks scope only and has no `[ValidateAntiForgeryToken]`. It records the data and publishes `"status"`: `src/Hms.Server.Api/Controllers/DevicesController.cs:96-126`.
  - The merge keeps the last `digital_outputs` whatever its source: `src/Hms.Server.Application/Realtime/DeviceTelemetryContracts.cs:116-120`.
  - The UI applies **every** event type's payload (no event-type filter): `frontend/src/features/devices/views/DeviceDetailView.vue:266-273`. It also parses `digital_outputs` with loose coercion: `DeviceDetailView.vue:210-217`.
  - The broker is anonymous: `docs/architecture-mqtt-device-link.md:138, :194`.
- **Suggested fix:**
  - Project to a server-owned key and event, for example telemetry `verified_digital_outputs` and event `do_state`, carrying `{correlationId, receivedAt, channels}`.
  - Strip that key from heartbeat, device_event and HTTP-ingest inputs.
  - In the UI, read DO state only from that event or key.
  - Add tests: a forged heartbeat with `digital_outputs` does not change the verified state, and a forged HTTP ingest does not change it either.

## Finding 2: Any `devices.manage` holder can silently remove the interlock, and a routine re-bind does it by accident
- **Severity:** Critical
- **Location:** Phase 1, Requirements "API" (lines 38-40), Implementation step 4 (line 113), Tests first #6 (line 106).
- **Flaw:**
  - The interlock is edited through `PUT /link`. That route requires only the org-level feature permission `devices.manage` plus scope. It does not require master-admin and does not require `gpio.do_write`. The command key, which is equally safety-critical, is master-admin only.
  - The request record pattern (`KeyVersion = null` default) means a PUT that omits `allowedOutputChannels` stores `null`, and `null` means "unrestricted".
  - `ConfigureDeviceMqttBindingHandler` writes no audit, so widening the interlock leaves no trace.
- **Failure scenario:**
  1. An operator fixes a topic prefix with the documented body `{topicPrefix, mqttDeviceId, keyVersion?}`.
  2. The EF upsert overwrites every column, so `allowed_output_channels` becomes NULL.
  3. DO2–DO6 on the real gateway are now drivable by any `gpio.do_write` holder.
  4. Separately, a lower-privileged org admin who holds `devices.manage` but not `gpio.do_write` can PUT `null`, or `[1..6]`, to widen another user's reach.
  5. Neither change is audited.
- **Evidence:**
  - Gate: `src/Hms.Server.Api/Controllers/DeviceLinkController.cs:64`, compared with the master-admin-only key import at `:96`.
  - Request record with default-null optional field: `DeviceLinkController.cs:145`.
  - Handler has no audit: `src/Hms.Server.Application/DeviceLink/DeviceLinkHandlers.cs:268-278`.
  - Upsert overwrites all fields: `src/Hms.Server.Infrastructure/Persistence/EfDeviceMqttBindingStore.cs:32-35`.
  - Documented PUT body without the field: `docs/architecture-mqtt-device-link.md:146`.
  - Plan test #6 checks only `[0]`→400 and `[1,3]` round-trip, not omission.
- **Suggested fix:**
  - Use a separate endpoint `PUT /link/output-allowlist` that is master-admin-only (or requires `gpio.do_write` and is high-risk), validates CSRF and writes an audit entry.
  - Keep `PUT /link` from touching the allowlist: omission preserves the stored value.
  - Make a widening change (including to `null`) require master-admin.
  - Add tests: omitted field preserves the value; a non-master cannot set `null`; every change writes an audit row.

## Finding 3: The interlock fails open by default and in the lab depends on an uncommitted secret
- **Severity:** High
- **Location:** Phase 1, Requirements "Danh sách kênh cho phép" (lines 33-35), "Seed dev" (line 41), Risk "Allowlist chỉ để an toàn cho lab" (line 128). Phase 3, Implementation step 1.
- **Flaw:**
  - `null` = no server restriction. That is the value every existing row gets from the nullable-column migration and every new binding gets.
  - The only place the lab gateway gets `[1]` is `Mqtt:Devices:0:AllowedOutputChannels`. No `Mqtt` section exists in any committed appsettings file; it lives in user-secrets (`UserSecretsId hms-server-api-dev`). It is applied only when `IsDevelopment() && Development:AutoSeed` (default true).
  - It is keyed by array index 0, not by device id.
  - In .NET configuration an empty JSON array binds as absent, so `[]` meaning "deny all" becomes `null` meaning "allow all".
  - The plan also contradicts the brainstorm: brainstorm line 36 says unset means "only channels `get_status` reported"; Phase 1 line 35 says unrestricted.
- **Failure scenario:** Any of these runs the real gateway with all six relays open:
  - a second developer machine without that secret;
  - a run with `Development__AutoSeed=false`;
  - a staging host with live MQTT;
  - a secret where index 0 is the simulator.

  Phase 3 step 4 ("DO2 → DO_CHANNEL_NOT_ALLOWED") passes only on the one machine that has the secret.
- **Evidence:**
  - Seed gating: `src/Hms.Server.Api/Program.cs:58, 127-135`.
  - Seed options: `src/Hms.Server.Infrastructure/Mqtt/MqttLinkOptions.cs:29-31, 46-56`.
  - `grep "Mqtt" src/Hms.Server.Api/appsettings*.json` finds nothing.
  - `Hms.Server.Api.csproj:8` uses user-secrets.
- **Suggested fix:**
  - Fail closed for Physical-class actions: `null` means no output channel is allowed until an allowlist is set explicitly. An explicit `"*"` or a full list opens all channels.
  - Commit the lab allowlist keyed by `DeviceId` in `appsettings.Development.json`. It is not a secret.
  - In Phase 3, check `GET /link` returns `[1]` before the first relay command.

## Finding 4: The projection broadcasts data gated by `gpio.do_view` to viewers who lack it, breaking an explicit code invariant
- **Severity:** High
- **Location:** Phase 1, Requirements "Chiếu trạng thái … từ bất kỳ người gửi nào" (line 36), Architecture (line 64).
- **Flaw:**
  - `control.get_status` needs `gpio.do_view`.
  - The code deliberately keeps reply data away from other viewers. Realtime `command_result` carries the outcome only, and `GET /commands/{id}` hides `reply` from anyone but the issuer and master-admin, because "reply data was released by a permission the sender holds, which a viewer may not".
  - The plan copies that same reply data into telemetry and into the `status` event. Event delivery and `GET /telemetry` check only org scope (`CanAccessDevice` → `CanAccessOrganization`). There is no `gpio.do_view` check and no device-role check.
- **Failure scenario:** A user scoped to the organization with no device role (or a role without `gpio.do_view`) subscribes to the hub. After any operator sends `get_status`, that user receives channel states and modes for all DOs. `GET /devices/{id}/telemetry` returns the same data. The brainstorm notes that heartbeat carries no DO state (`brainstorm:29`), so this is a new disclosure path.
- **Evidence:**
  - Invariant comments: `DeviceLinkHandlers.cs:141-142` and `src/Hms.Server.Api/Controllers/DeviceCommandsController.cs:68-70`.
  - Delivery checks scope only: `src/Hms.Server.Api/Hubs/SignalRDeviceEventDeliveryService.cs:77-80`.
  - `src/Hms.Server.Api/Authorization/ScopeAuthorization.cs:34-39`.
  - Telemetry read: `DevicesController.cs:79-94`.
  - Permission: `src/Hms.Server.Infrastructure/Commands/command-catalog.json:363-366`.
- **Suggested fix:**
  - Deliver the DO projection only to subscribers whose device access (`DeviceAccessResolver`) holds `gpio.do_view`.
  - Return it from a dedicated endpoint gated the same way.
  - If broadcasting to all viewers is wanted, record that product decision and get the user's sign-off. It reverses an existing security invariant.

## Finding 5: Physical commands stay executable for 90 s after the server reports "timeout", and signed envelopes can be replayed
- **Severity:** High
- **Location:** Phase 1, Requirements (TTL accepted as "TTL 120000"), Phase 2 "Tiến trình mỗi kênh" (line 26), Phase 3 Risk "Mất kết nối giữa chừng" (line 62).
- **Flaw:**
  - The envelope `ttl_ms` is 120000 (catalog row). The server marks the command `DEVICE_RESPONSE_TIMEOUT` and drops it from `pending` after 30 s.
  - Spec §3 checks TTL when the command leaves the device queue, so a queued `set_output` or `pulse` can still run up to 90 s after the UI said "outcome unknown".
  - The late reply is then `Ignored` (no pending entry), so the audit and UI never learn of it.
  - Phase 2 auto-reads back only after `Executed`, never after a timeout. The panel keeps the old state, and heartbeats carry no DO data.
  - Spec R4 dedup is 128 IDs in RAM, and reboot forgets it. While the device clock is unsynced (every boot until SNTP), the age check is skipped entirely. The broker is anonymous and command topics are readable: Phase 3 itself sniffs `.../cmd/#`. A captured signed `set_output ON` can therefore be replayed after a device reboot.
- **Failure scenario:**
  1. The operator presses ON while the link is congested. The UI shows timeout and the operator presses OFF.
  2. The OFF is processed, then the queued ON runs. The relay ends ON while the panel shows OFF.
  3. Separately, an attacker who recorded `set_output {1,true}` replays it right after the gateway reboots.
- **Evidence:**
  - TTL: `command-catalog.json:374-404` (`ttl_ms: 120000`).
  - Deadline = now + `ResponseTimeoutSeconds` (30): `src/Hms.Server.Infrastructure/Mqtt/MqttDeviceCommandTransport.cs:29` and `MqttLinkOptions.cs:27`.
  - Late reply ignored: `DeviceLinkHandlers.cs:48-50`.
  - Spec: `mqtt-api.md:452` (dedup 128 IDs, lost on reboot), `:478` (TTL at dequeue; age check skipped when clock not valid), `:1162`.
  - Sniffer on the command topic: `phase-03-live-verification.md:26`.
- **Suggested fix:**
  - For Physical-class actions, send `ttl_ms` ≤ the server response timeout, for example 20 s TTL with a 30 s timeout, so that a timeout means "expired on device" once the clock is synced. The spec allows `ttl_ms` 1..86400000.
  - Auto-send `get_status` on `DEVICE_RESPONSE_TIMEOUT` and keep the channel locked until the read-back arrives.
  - Record "replay after reboot / unsynced clock on anonymous broker" as a spec gap. Make a broker ACL a precondition for any non-lab use.

## Finding 6: "Mọi lệnh đều có audit" is false: the audit records no issuer and no channel/value, and skips denials
- **Severity:** High
- **Location:** `plan.md` Acceptance (line 38). Phase 1 has no audit requirement at all.
- **Flaw:**
  - The persisted command audit row has `DeviceId, CorrelationId, Status, Code, Type, Action, RequestFingerprint, KeyGeneration, ExpiresAt, CreatedAt`. It has no `UserId` and no payload. The fingerprint is a SHA-256 over type, action, payload and dryRun, with no user.
  - The issuer exists only in the in-memory `ICommandResponseStore` (capacity 2048, lost on restart, written only when a signed reply arrives).
  - Denials before reservation (`DEVICE_PERMISSION_REQUIRED`, `DENIED_UNTIL_ACTION_CONTRACT`, `DEVICE_NOT_FOUND`) go through `RecordAsync(request, null, …)`, which writes nothing when `plan` is null.
  - Allowlist changes are unaudited (Finding 2).
- **Failure scenario:** A relay is switched ON at 02:00. After a restart, or after a timeout with no reply, the audit cannot say who did it, which channel, or ON vs OFF. A user probing `set_output` without permission leaves no record.
- **Evidence:**
  - `src/Hms.Server.Infrastructure/Persistence/PersistenceEntities.cs:75-88`.
  - `src/Hms.Server.Application/Commands/CommandContracts.cs:121-127, 145-158`.
  - `src/Hms.Server.Application/Commands/SendDeviceCommandHandler.cs:50-53, 93-97, 155-167`.
  - `DeviceLinkContracts.cs:294-311`.
- **Suggested fix:**
  - For Physical-class actions, persist `IssuedBy`, a canonical payload (channel/value/duration), and the allowlist in force at sign time.
  - Persist denials (a permission-denied row without a plan).
  - Audit binding and allowlist changes.
  - Add acceptance tests that query these fields.

## Finding 7: `pulse.duration_ms` has no server bound, although the spec says to read the limits before driving real hardware
- **Severity:** Medium
- **Location:** `plan.md` Spec gaps (line 48). Phase 1 "Kiểm tra input" (lines 29-32). Phase 3 step 5.
- **Flaw:**
  - The server accepts any positive integer and defers to an undocumented device gate.
  - The spec says: "Pulse duration còn chịu gate của DO primitive; lấy cấu hình/giới hạn DO qua HTTP trước khi điều khiển thiết bị thật".
  - The plan does not fix the integer width (Int32 or Int64) or an upper bound.
- **Failure scenario:** A channel is switched to pulse mode through the device web UI (out of band). A `pulse` with `duration_ms: 2147483647` sent from `DeviceCommandModal` holds the relay ~24.8 days, which is effectively a latched ON. The operator believes "pulse" means momentary. With allowlist `null` (Finding 3) this reaches every channel.
- **Evidence:**
  - Spec: `mqtt-api.md:780, :795`.
  - The modal sends free-form JSON payloads: `frontend/src/features/devices/components/DeviceCommandModal.vue:170, 207-216`.
- **Suggested fix:**
  - Cap `duration_ms` server-side (for example 1..60000), configurable per device, until the HTTP limit is known.
  - Reject values outside Int32.
  - Test `duration_ms` = cap+1 → `COMMAND_PAYLOAD_INVALID`.

## Finding 8: UI permission gating reads a snapshot that never contains device-role permissions, so Phase 2 tests prove nothing
- **Severity:** Medium
- **Location:** Phase 2, Requirements (line 23 "đọc từ catalog requiredPermission và snapshot quyền"), Tests first #3 and #4 (lines 66-67), Architecture (line 44).
- **Flaw:**
  - The server authorizes `gpio.do_write` only from the actor's device membership role.
  - `/me/permissions` returns feature grants, org scopes, legacy `device:{id}:action:*` grants and claims. It never returns device-role permissions.
  - `can()` matches the plain string.
  - Result: non-master users with the right role never see the buttons, while a stray feature grant named `gpio.do_write` shows buttons the server will deny. Tests #3 and #4 will pass because they mock the snapshot, so they are phantom tests.
  - Contract gap: Phase 1 says "`GET /link` trả thêm `allowedOutputChannels`" without saying where. `binding` (`DeviceMqttBindingDto`) and the top level (`DeviceLinkDto`) are both possible, and PUT returns `DeviceMqttBindingDto`. Phase 2 assumes top-level. If the field lands in `binding`, `deviceLink.allowedOutputChannels` is `undefined`, and a `!= null` check treats that as unrestricted.
- **Evidence:**
  - `src/Hms.Server.Api/Controllers/MeController.cs:24-36`.
  - `src/Hms.Server.Application/Authorization/DeviceAccessResolver.cs:23-35`.
  - `src/Hms.Server.Domain/Authorization/DeviceAccessPolicy.cs:118`.
  - `frontend/src/core/auth/permissions.ts:57-70`.
  - `DeviceLinkController.cs:41-42, 70, 131-143`.
  - `frontend/src/core/api/adminApi.ts:102-113`.
- **Suggested fix:**
  - Expose the caller's effective device permissions (for example `GET /devices/{id}/me` → `DeviceAccess.Permissions`) and gate on that.
  - Pin the DTO shape: one location, with the same name in GET and PUT.
  - Add a test with a role-holder whose global snapshot lacks `gpio.do_write` but who has it on the device.

## Finding 9: The projection mechanics would erase heartbeat telemetry, lose read-back events, and risk dropping reply delivery
- **Severity:** Medium
- **Location:** Phase 1, Implementation step 6 (line 115 "`IDeviceTelemetryStore.Record` (merge) … correlator gọi nó sau khi ghi audit"), Tests first #5.
- **Flaw:**
  1. `Record` is not a generic merge. It rebuilds the DTO from the new dictionary only, carries over 7 whitelisted keys, and sets `Timestamp = now`. A call with `{digital_outputs}` sets uptime, heap, MQTT, network and tag fields to null and drops `network`, `services` and the rest from `Data` until the next heartbeat. It also bumps the timestamp as if a heartbeat arrived.
  2. `status` events are coalescible per connection. A queued projection `status` is replaced by the next heartbeat `status`, which has no `digital_outputs`, before a slow client drains it. Phase 2's "Đã đọc lại" then never fires.
  3. "Sau khi ghi audit" places the projection between `audit.RecordAsync` and `responses.Save` / `PublishResultAsync`. A throw on a device-shaped reply (for example `channels` not an array) skips both, and the issuer loses reply data and the `command_result` event. The inbound loop only logs it.
- **Evidence:**
  - `DeviceTelemetryContracts.cs:116-162`.
  - `src/Hms.Server.Api/Hubs/SignalRConnectionBacklog.cs:39-50, 191-194`.
  - `DeviceLinkHandlers.cs:77-87`.
  - `src/Hms.Server.Infrastructure/Mqtt/MqttDeviceLinkService.cs:313-322`.
- **Suggested fix:**
  - Add a store method that updates one key without touching other fields or the timestamp.
  - Use a non-coalescible event type for read-back (same fix as Finding 1).
  - Run the projection after `PublishResultAsync`, wrapped in try/catch, with a test for a malformed `channels`.
  - Add a test that the projection does not null heartbeat fields.

## Finding 10: A Physical-class action is opened without the closure evidence the catalog gate requires, and without high-risk governance
- **Severity:** Medium
- **Location:** Phase 1, Implementation step 2 (`ControlOutputActions`), Tests first #1. Brainstorm option A.
- **Flaw:**
  - The catalog gate opens only actions the spec closed as `SLICE_PASS`. `control.pulse` has `SLICE_PASS` (spec :882). `control.set_output` has no disposition at all: it is absent from the closed list (spec :38-52) and from the remote-control disposition table (:880-883).
  - `gpio.do_write` is not in `DevicePermissionCodes.HighRisk`. Any non-master admin who holds it on a device can delegate it, and roles can be created with it without master-admin.
  - The modal's danger-confirm is a name heuristic (`reboot|reset|wipe|format|revoke`), so relay commands get no confirm.
  - `OutputCommandPolicy` is registered per `(type, action)`. If another Physical row is enabled later, for example `set_all`, the adapter passes it with no channel check, because there is no "Physical without a validator → fail closed" rule.
  - Plan line 29 forbids editing existing tests, but `CatalogTests.cs:126` asserts `pulse` is Denied and `ManagementApiTests.cs:195-199` asserts "only closed read actions" are available. Both must be inverted.
- **Evidence:**
  - `src/Hms.Server.Infrastructure/Commands/PinnedCommandCatalog.cs:43-46, 189-194`.
  - `mqtt-api.md:38-52, 779, 880-883`.
  - `src/Hms.Server.Domain/Authorization/DevicePermissionCodes.cs:21-26`.
  - `src/Hms.Server.Application/Authorization/DeviceAccessHandlers.cs:54-57, 317-327`.
  - `DeviceCommandModal.vue:113-118`.
  - `tests/Hms.Server.UnitTests/CatalogTests.cs:126`.
  - `tests/Hms.Server.IntegrationTests/ManagementApiTests.cs:195-199`.
- **Suggested fix:**
  - Either open only `pulse` (closed), or record that `set_output` is opened under an explicit user waiver and list it as a spec gap.
  - Add `gpio.do_write` to `HighRisk`, or state why not.
  - In the adapter: `Class == Physical && no registered validator` → `COMMAND_PAYLOAD_INVALID`.
  - Name the existing tests that must change, and why.

---

## Fact check (plan claims)

| Claim | Result |
|---|---|
| `PinnedCommandCatalog.EnabledActions` opens only class R, 11 actions | VERIFIED `PinnedCommandCatalog.cs:48-61, 192` |
| §11: set_output/pulse = `gpio.do_write`, B, 120000, no confirm | VERIFIED `mqtt-api.md:1280-1281`; `command-catalog.json:374-404` |
| `pulse` already `SLICE_PASS` (brainstorm) | VERIFIED `mqtt-api.md:882` |
| `set_output` has a closed contract | FAILED: no disposition in `mqtt-api.md:38-52, 880-883` |
| Heartbeat does not erase `digital_outputs` (store merge) | VERIFIED `DeviceTelemetryContracts.cs:116-120` |
| `Record` is a merge usable for a partial projection | FAILED: typed fields and non-whitelisted keys are reset, `DeviceTelemetryContracts.cs:149-162` |
| Correlator only handles ids it is waiting for | VERIFIED `DeviceLinkHandlers.cs:48-50` |
| `applyTelemetryPayload` updates only present fields | VERIFIED `DeviceDetailView.vue:210-211` (and it applies every event type, :266-273) |
| `data-test="do-N-state"`, 6 fake channels | VERIFIED `DeviceDetailView.vue:857-872` |
| Poll logic in `DeviceCommandModal` (40 s) | VERIFIED `DeviceCommandModal.vue:42, 61` |
| Correlation id ≤ 39 bytes | VERIFIED `HmsIdfCommandProtocolAdapter.cs:21, 34` |
| Policy placement "after binding + capabilities, before key" | VERIFIED feasible, `HmsIdfCommandProtocolAdapter.cs:28-58`; single adapter, `DependencyInjection.cs:35` |
| `EfDeviceMqttBindingStore.cs`, `PersistenceEntities.cs`, `ApplicationDbContext.cs` | VERIFIED (exist; `PersistenceEntities.cs:39`, `ApplicationDbContext.cs:24, 91-101`) |
| `MqttLinkOptions.cs` + seed in `Program.cs` | VERIFIED; the field belongs on `MqttDeviceSeedOptions` (`MqttLinkOptions.cs:46-56`); seed at `Program.cs:127-135` |
| Seed value `Mqtt:Devices:0:…` is reviewable config | FAILED: no `Mqtt` in committed appsettings; user-secrets only (`Hms.Server.Api.csproj:8`) |
| `CatalogTests.cs`, `ManagementApiTests.cs`, `DeviceDetailView.test.ts`, `DeviceCommandModal.test.ts` exist | VERIFIED |
| `frontend/src/core/commands/` for the new composable | VERIFIED dir exists; `commandAvailability.ts` already has `canRenderCommand`, so reuse it rather than re-implementing "available" gating |
| ADR 260927 | UNVERIFIED: referenced in `docs/architecture-mqtt-device-link.md:111`, no ADR file found under `docs/` |
| 427 unit + 103 integration + 139 FE tests | UNVERIFIED (counts not run; plan-review mode) |
| `sim-device.js` | UNVERIFIED: not in repo (scratchpad, uncommitted), so Phase 3 simulator evidence is not reproducible |

## Contract consumers (changes the plan touches)

**`DeviceMqttBinding` record and `Create`** (`DeviceLinkContracts.cs:14-36`):
- `DeviceLinkHandlers.cs:275` (bind handler)
- `Program.cs:134-135` (seed calls `Handle(deviceId, prefix, id, keyVersion)`, so the signature changes)
- `EfDeviceMqttBindingStore.cs:32-35, 48-50`
- `InMemoryDeviceMqttBindingStore` `DeviceLinkContracts.cs:51-76`
- `HmsIdfCommandProtocolAdapter.cs:28`
- `MqttDeviceLinkService.cs:212` (List)
- `DeviceLinkController.cs:37-42, 69-70`
- tests `MaitechRtuDeviceIntegrationTests.cs:30`, `DeviceLinkTests.cs:295, 311`

Missing from plan: `MaitechRtuDeviceIntegrationTests.cs`, `DeviceLinkTests.cs`, the `Program.cs` call-site signature, and `MqttDeviceSeedOptions` (named as `MqttLinkOptions`).

**`DeviceLinkDto` / `DeviceMqttBindingDto` / `ConfigureDeviceMqttBindingRequest`:**
- server `DeviceLinkController.cs:41-51, 70, 131-145`
- client `frontend/src/core/api/adminApi.ts:102-113, 238-239`
- consumer `DeviceDetailView.vue:94, 297`

No frontend PUT caller exists. The plan does not pin which DTO carries the field (Finding 8) or what an omitted field means on PUT (Finding 2).

**Catalog availability (`GET /me/command-catalog`):**
- `MeController.cs:48-72`
- `ManagementApiTests.cs:180-199`
- `CatalogTests.cs:121-139`
- FE `commandAvailability.ts:13-20`
- `DeviceCommandModal.vue:88-127`

Missing from plan: the modal will now list `set_output`/`pulse` with a free-form payload and no confirm (Finding 10).

**`IDeviceTelemetryStore.Record` and the `status` event:**
- `DeviceLinkHandlers.cs:203-204`
- `DevicesController.cs:115-125`
- delivery `SignalRDeviceEventDeliveryService.cs:77-87`
- coalescing `SignalRConnectionBacklog.cs:191-194`

Not addressed (Findings 1, 4, 9).

Status: DONE_WITH_CONCERNS
Summary: The plan's two safety anchors can both be defeated with code paths already in the repo. The channel allowlist fails open, is editable by `devices.manage`, and is wiped when a PUT omits the field. The DO read-back shares its key and event with unauthenticated writers. Separately, the 120 s TTL against the 30 s timeout, the issuer-less audit, and the broadcast of data gated by `gpio.do_view` need fixing before any real relay is switched.
