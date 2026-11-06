# Red-team review: DO relay control plan (failure mode analyst)

Reviewer role: Fact Checker + Contract Verifier, Murphy's-law lens. Plan: `plans/261106-0735-do-relay-control/`. Evidence is from the worktree at review time. For the device side, only `HMS-IDF/engineering-docs/api/mqtt-api.md` was read.

## Finding 1: Read-back runs only after `Executed`, so every outcome-unknown path unlocks the channel and invites a new-id retry
- **Severity:** Critical
- **Location:** Phase 2, sections "Requirements → Tiến trình mỗi kênh", "Không gửi chồng", "Risk Assessment"; Phase 3, "Risk Assessment" (line 62)
- **Flaw:** The automatic `control.get_status` runs only on `Executed` (phase-02 line 26). Every other outcome is shown as a terminal rejection (line 27), and then the channel lock is released (line 29). In this codebase several of those outcomes do not mean "did not run":
  - `DEVICE_RESPONSE_TIMEOUT`. Spec R4 says a timeout is not proof.
  - `MQTT_PUBLISH_FAILED` after the PUBLISH reached the broker but the PUBACK was lost.
  - `DEVICE_REJECTED` with `error:"UNAUTHORIZED"`, which is the dedup reply to a QoS 1 duplicate (spec §2 Retry, §5 table). The server keeps the first verified reply it receives. If the dedup reply arrives before the handler's success reply, the real success is ignored.
  - The composable's own 40 s client-side `timeout` phase.
- **Failure scenario:**
  1. The operator presses Pulse on DO6 (simulator) or a future pulse-mode DO1.
  2. The TLS link drops after the broker accepted the PUBLISH. The transport catches the exception, removes the pending entry, and returns `Failed/MQTT_PUBLISH_FAILED`.
  3. The device pulses. Its reply is `Ignored` because nothing is pending.
  4. The panel shows the failure, unlocks the channel and does not read back.
  5. The operator presses Pulse again with a new correlation id. The relay pulses twice.

  The same thing happens with set_output when the second press is a different value, for example ON times out, the operator presses OFF, and the late ON then lands.
- **Evidence:**
  - phase-02 lines 26-29: "`Executed` → tự gửi `control.get_status`"; rejections, including `DEVICE_RESPONSE_TIMEOUT`, are only "hiện mã và lý do".
  - `src/Hms.Server.Infrastructure/Mqtt/MqttDeviceCommandTransport.cs:31-45`: any non-cancellation exception becomes `MQTT_PUBLISH_FAILED` plus `pending.Remove(...)`.
  - `src/Hms.Server.Application/DeviceLink/DeviceLinkHandlers.cs:48-50`: an unknown id is `Ignored`. Lines 74-87: the first verified reply closes the command. Line 133: `success:false` becomes `DEVICE_REJECTED`.
  - Spec `mqtt-api.md:448`: "lệnh lặp có thể trả `UNAUTHORIZED` … timeout của client không chứng minh lệnh chưa chạy". Line 409, R3 [M45]: "lỗi response không chứng minh lệnh ghi chưa chạy".
  - phase-02 line 91 claims "Trạng thái cuối luôn lấy từ lần đọc lại". That is false for every non-`Executed` path.
- **Suggested fix:**
  - Split outcomes into two classes.
    - **Definitely not sent:** the synchronous `ContractUnavailable/*`, `DENIED_*`, permission codes and `MQTT_NOT_CONNECTED` before publish.
    - **Outcome unknown:** everything else.
  - For outcome-unknown results, the composable auto-reads back, keeps the channel locked until that read-back finishes, and shows "không rõ kết quả" instead of "bị từ chối".
  - In the transport, keep the pending entry until its deadline when the exception happened after the client accepted the PUBLISH. Do not remove it.
  - Treat `DEVICE_REJECTED: UNAUTHORIZED` on a command published once as outcome-unknown.
  - Add tests for each of these paths.

## Finding 2: The channel allowlist, the only physical safety interlock, fails open in at least four ways
- **Severity:** Critical
- **Location:** Phase 1, sections "Requirements → Danh sách kênh cho phép", "API", "Seed dev", "Implementation Steps" 4, "Risk Assessment"
- **Flaw:** `null` means unrestricted (phase-01 line 35). The plan never separates "field omitted" from "explicit null", and does not define what `[]` means. Four paths silently remove the interlock:
  - **(a) PUT without the field.** `ConfigureDeviceMqttBindingRequest` today is `(TopicPrefix, MqttDeviceId, KeyVersion = null)`. Adding `AllowedOutputChannels = null` the same way means a PUT that only re-targets the prefix wipes the allowlist. The documented contract (`docs/architecture-mqtt-device-link.md:72`) has no such field, so existing scripts and callers will omit it.
  - **(b) Seed rewrite on every start.** The dev seed calls `Handle` and `Upsert` on every start. A user-secrets file without `AllowedOutputChannels` (another dev machine, or a CI host) binds `gateway_870c` with `null`. A value set later through PUT is also overwritten on restart.
  - **(c) EF mapping slip.** `EfDeviceMqttBindingStore.ToBinding` constructs the record positionally and `Upsert` copies fields explicitly. With an optional parameter, forgetting to map the new column still compiles and returns `null` on the Postgres path only.
  - **(d) Weak authority, no audit.** Changing the interlock needs only `devices.manage`, while importing a key needs master-admin. Nothing records who widened it.

  The plan also changes the brainstorm's decision without saying so. The brainstorm said "null = only the channels get_status reported"; the plan says "null = unrestricted".
- **Failure scenario:**
  1. A teammate runs the API with their own user-secrets, or anyone sends `PUT /link {topicPrefix, mqttDeviceId}` to fix the prefix.
  2. The allowlist becomes `null` and nothing reports it.
  3. A `control.set_output {channel:3,value:true}` from the panel or the C2 modal is signed and published to the real gateway.

  The user stated that only DO1 is safe to drive. Phase 3's sniffer check (phase-03 line 26) proves the interlock only during that one session.
- **Evidence:**
  - phase-01 line 35: "`null` nghĩa là không giới hạn ở server".
  - Brainstorm line 36: "Không cấu hình nghĩa là cho phép tất cả các kênh mà `get_status` đã báo".
  - `src/Hms.Server.Api/Controllers/DeviceLinkController.cs:145` (request record with optional trailing param), `:64` (`devices.manage` only).
  - `src/Hms.Server.Api/Program.cs:58,129-135`: AutoSeed defaults to true and re-runs `Handle` every start.
  - `appsettings.Development.json` has no `Mqtt` section. The seed lives only in user-secrets (`Hms.Server.Api.csproj` UserSecretsId `hms-server-api-dev`).
  - `src/Hms.Server.Infrastructure/Persistence/EfDeviceMqttBindingStore.cs:32-35,48-50`.
  - `PersistenceEntities.cs:39-46`: the binding record has no actor or audit column.
- **Suggested fix:**
  - Make the field required on PUT, and return 400 when it is absent.
  - Store "unrestricted" as an explicit sentinel, not as a missing value.
  - Define `[]` as deny-all.
  - Default to deny-all for class `B` when no allowlist is configured. At minimum, fail closed for `gateway_870c`.
  - Have the seed only tighten, never loosen.
  - Gate allowlist changes behind master-admin and audit them.
  - Add an EF round-trip test plus a test that a PUT without the field does not change the allowlist.

## Finding 3: TTL is 120 s but the server declares timeout at 30 s, so a "timed-out" relay command can still fire for 90 s, or with no age bound at all
- **Severity:** High
- **Location:** Phase 1, "Context" (TTL 120000); Phase 3, "Risk Assessment" ("Mất kết nối giữa chừng")
- **Flaw:** The envelope carries `ttl_ms = ExpiresAt - RequestedAt = 120000` for both DO actions. The device checks TTL when it takes the command off its queue. When the device clock is not yet valid, for example after a reboot before SNTP, it skips the age check entirely.

  The server closes the command at 30 s (`ResponseTimeoutSeconds`). `TakeExpired` removes the pending entry, so a later real reply is `Ignored` and the audit stays `DEVICE_RESPONSE_TIMEOUT`.

  The plan's mitigation is to read back before retrying. A read-back taken at T+31 s can come before the delayed command executes at T+31..120 s, so it proves nothing.
- **Failure scenario:**
  1. The device queue is backed up, or the HIL tool is consuming the shared 20/10 s rate bucket. The operator presses DO1 ON.
  2. At 30 s the server reports timeout and the panel offers the controls again.
  3. The operator reads back OFF, decides not to retry, and leaves.
  4. At T+80 s the queued ON executes. The relay is energized with nobody watching. The panel still shows OFF and the audit says timeout.
- **Evidence:**
  - `src/Hms.Server.Infrastructure/Commands/command-catalog.json`: `set_output` / `pulse` have `"ttl_ms": 120000`.
  - `SendDeviceCommandHandler.cs:109-120`: `expiresAt = RequestedAt + ttl`.
  - `HmsIdfCommandProtocolAdapter.cs:67`: `ttl_ms = ExpiresAt - RequestedAt`.
  - `MqttLinkOptions.cs:27`: `ResponseTimeoutSeconds = 30`.
  - `DeviceLinkContracts.cs:243-253`: `TakeExpired` removes the entry.
  - Spec `mqtt-api.md:478`: "TTL xét khi command ra khỏi hàng đợi … Khi đồng hồ chưa hợp lệ, age check bỏ qua". Line 480: the queue has 16 slots and the shared bucket is 20/10 s.
- **Suggested fix:**
  - For class `B` actions, send `ttl_ms` no larger than the response timeout minus a margin (for example 20 s). Spec §3 allows a per-command `ttl_ms`.
  - Or keep the pending entry and the UI channel lock until `ExpiresAt`, not until 30 s.
  - Let a late verified reply after timeout update the audit from `DEVICE_RESPONSE_TIMEOUT` to the device outcome.
  - Phase 3 should first confirm that the device clock is synced.

## Finding 4: A server restart or broker outage leaves physical commands as `Published` forever
- **Severity:** High
- **Location:** Phase 1, "Architecture" and "Risk Assessment" (restart not mentioned anywhere); Phase 3, "Risk Assessment" line 62
- **Flaw:** The pending registry is an in-memory singleton, while the EF audit row survives a restart.
  - **After a restart:** nothing sweeps orphaned `Published` rows. A device reply that arrives after the restart is `Ignored`, and `GET /commands/{id}` returns `Published` indefinitely.
  - **During a broker outage:** the housekeeping loop runs `ConnectAsync` → `RefreshSubscriptions` → `SweepTimeouts` inside one `try`. When `ConnectAsync` throws, the sweep is skipped, so no `DEVICE_RESPONSE_TIMEOUT` is ever produced while the broker is down. That contradicts phase-03 line 62 ("lệnh hết giờ thành `DEVICE_RESPONSE_TIMEOUT`").
- **Failure scenario:**
  1. The operator presses DO1 ON and the API process is restarted (deploy, `dotnet watch` reload, crash) before the reply arrives.
  2. The relay switches. The audit says `Published` forever.
  3. The panel's 40 s client timeout ends with no code, no read-back and an unlocked channel.
  4. The telemetry store, also in memory, is empty, so the panel shows "Chưa đọc trạng thái" while the relay is energized.
- **Evidence:**
  - `src/Hms.Server.Infrastructure/DependencyInjection.cs:38`: `InMemoryPendingDeviceCommandRegistry` singleton.
  - `DependencyInjection.cs:62`: EF audit store when Postgres is configured.
  - `DeviceLinkHandlers.cs:48-50`: no pending entry means `Ignored`.
  - `EfCommandAuditStore.cs:118-128` writes `Published`; `:170-181` returns `Published` as a completed result.
  - `MqttDeviceLinkService.cs:101-116`: the sweep sits in the same `try` after `ConnectAsync`.
- **Suggested fix:**
  - On startup, mark `Published` rows older than the response timeout (or still before their `ExpiresAt`) as `Failed/DEVICE_RESPONSE_TIMEOUT` with the outcome-unknown detail, so the UI and audit show them as unknown.
  - Run `SweepTimeoutsAsync` in its own `try`, independent of connect success.
  - Add a restart test and a broker-down test.

## Finding 5: Event coalescing can silently drop the projected `status` event, so the read-back never reaches the UI
- **Severity:** High
- **Location:** Phase 1, "Requirements → Chiếu trạng thái" (publish event `status`); Phase 2, "Architecture" ("Kết quả về qua event `status` … UI không tự parse reply"), test 5
- **Flaw:** `status` is a coalescible event type at two layers:
  - **Publisher queue:** the latest event replaces the earlier one in place.
  - **Per-connection backlog:** the same replacement happens again for each SignalR connection.

  Heartbeats publish `status` every 3 s with no `digital_outputs`. The delivery loop runs every 100 ms and sends only one event per connection per pass, so a projection event can wait several passes behind the `command_result` events from the same click. If a heartbeat arrives in that window, the DO-only payload is overwritten by a payload without `digital_outputs`.

  The UI marks the channel "Đã đọc lại" from the `get_status` command result, but the ON/OFF badge never changes. The panel then shows a successful read-back next to an old state.
- **Failure scenario:**
  1. The operator presses DO1 ON.
  2. The connection's backlog holds `command_result(set_output)`, then `status(projection)`, then `command_result(get_status)`, each drained at one per 100 ms.
  3. A heartbeat arrives and replaces the projection in place.
  4. The panel shows OFF with "Đã đọc lại" while the relay is ON. With 0.1-0.5 s of queueing against a 3 s heartbeat, this hits a few percent of clicks.
- **Evidence:**
  - `src/Hms.Server.Application/Realtime/RealtimeContracts.cs:88-92` (replace in place), `:179-182` (`status` is coalescible).
  - `src/Hms.Server.Api/Hubs/SignalRConnectionBacklog.cs:39-51`: a second coalescing layer per connection.
  - `src/Hms.Server.Api/Hubs/SignalRDeviceEventDeliveryService.cs:21` (100 ms), `:100-148` (one send per connection per pass).
  - `DeviceLinkController.cs:26` and spec `mqtt-api.md:443`: heartbeat every 3 s.
- **Suggested fix:**
  - Publish the projection under its own event type (for example `do_status`) that carries `correlationId` and `readAt`.
  - Or, after `get_status` reaches `Executed`, have the UI fetch `GET /devices/{id}/telemetry` and require `digital_outputs.readAt` to be at or after the read-back's dispatch time before showing "Đã đọc lại".
  - Add a test where a heartbeat arrives between the projection and delivery.

## Finding 6: The projection's `Record(deviceId, {digital_outputs})` wipes heartbeat telemetry and fakes liveness
- **Severity:** High
- **Location:** Phase 1, "Implementation Steps" 6 ("`IDeviceTelemetryStore.Record` (merge)") and "Tests first" 5
- **Flaw:** `InMemoryDeviceTelemetryStore.Record` rebuilds every typed field (uptime, heap, `mqtt_connected`, `network_connected`, tag and service counts, uplink) from the new dictionary only. It carries forward just 7 whitelisted keys. Every other key is dropped from `Data`, including `uptime_seconds`, `free_heap`, `network`, `services` and `health`. It also stamps `Timestamp = UtcNow`.

  A projection with only `digital_outputs` therefore nulls the device health until the next heartbeat. If the device has just gone offline, there is no next heartbeat and the nulled record stays.

  The UI's "last heartbeat" comes from `tel.timestamp`, so it shows the read-back time as a heartbeat. The plan tests only the opposite direction ("heartbeat không xoá `digital_outputs`").
- **Failure scenario:**
  1. The device sends a heartbeat, then drops off the network right after replying to a `get_status`.
  2. `GET telemetry` now returns a fresh timestamp with null uptime, heap, network and health.
  3. The detail view shows the device as just seen, with empty health, indefinitely.
- **Evidence:**
  - `src/Hms.Server.Application/Realtime/DeviceTelemetryContracts.cs:31-114`: typed fields come only from `data`.
  - `:116-147`: the whitelist merge.
  - `:149-162`: `DateTimeOffset.UtcNow`.
  - `frontend/src/features/devices/views/DeviceDetailView.vue:303-308`: `lastHeartbeat` comes from `tel.timestamp`.
  - Other consumer: `DevicesController.cs:89` (`GetLatest`).
  - Secondary: the projection runs between `audit.RecordAsync` and `responses.Save` / `PublishResultAsync` (`DeviceLinkHandlers.cs:78-87`). If parsing device-supplied `channels` throws, the reply data and the `command_result` event are skipped, and the exception surfaces only as a log line (`MqttDeviceLinkService.cs:319-322`).
- **Suggested fix:**
  - Store DO state separately, with its own `readAt` and source correlation id, or add a partial-merge write that keeps the existing typed fields and `Timestamp`.
  - Add a test: heartbeat, then projection, then `GET telemetry` still has uptime and heap and the heartbeat timestamp.
  - Isolate projection errors with try/log so the command result is still saved and published.

## Finding 7: Old `digital_outputs` are shown as current indefinitely, and the single toggle button built from them can block an emergency OFF
- **Severity:** High
- **Location:** Phase 1, "Requirements" line 37 (locks the preserve-forever merge); Phase 2, "Requirements → Mỗi kênh" line 20 ("Nút **Bật** hoặc **Tắt** tuỳ trạng thái hiện tại")
- **Flaw:** The DO state is kept across heartbeats with no age, no source and no invalidation. Nothing clears or marks it stale on:
  - connectivity `offline`;
  - a device reboot (uptime goes down; relays probably reset, and spec C11 says DO persistence is a separate slice);
  - a timed-out read-back;
  - a change made by the HIL tool, whose `get_status` replies are not projected (phase-01 line 127).

  Phase 1 adds a test that locks the keep-forever behavior in. Phase 2 renders a single button whose meaning comes from that possibly old state, while the command itself is an absolute value.
- **Failure scenario:**
  1. The HIL tool switches DO1 ON, but the panel's last read-back says OFF, so only **Bật** is offered.
  2. The operator needs DO1 OFF now and has no OFF button. They must first press "Đọc trạng thái", which goes through the same degraded link that may time out (Finding 1).
  3. Separately, after the gateway reboots, the panel keeps showing DO1 ON with a fresh "last heartbeat".
- **Evidence:**
  - `DeviceTelemetryContracts.cs:119-122`: `digital_outputs` is carried forward unconditionally, with no timestamp.
  - phase-01 line 37 and test 5, "Heartbeat đến sau không xoá `digital_outputs`".
  - `DeviceDetailView.vue:268-274`: every realtime event, including `command_result` and `connectivity`, bumps `lastHeartbeat` and is fed to `applyTelemetryPayload`.
  - `DeviceDetailView.vue:210-218`: DO is replaced only by a non-empty array.
  - The brainstorm (line 39) promised that every viewer is updated "bất kể ai gửi lệnh". The code projects only the server's own pending ids (`DeviceLinkHandlers.cs:48-50`).
- **Suggested fix:**
  - Store and show the read time per channel.
  - Mark the state stale when there is an `offline` connectivity event, when uptime goes down, after N seconds, or after any outcome-unknown result.
  - Always show both an explicit **Bật** and an explicit **Tắt** button.
  - Replace the "keep forever" test with "kept, but marked stale".

## Finding 8: "Mọi lệnh đều có audit" is false, and the device's rejection reason is lost on the Postgres path
- **Severity:** High
- **Location:** plan.md "Acceptance" lines 36 and 38; Phase 2, "Requirements" line 27
- **Flaw:**
  - **(a) Denials are not audited.** Denials before the reservation (missing `gpio.do_write`, out of scope, `DENIED_UNTIL_ACTION_CONTRACT`) go through `RecordAsync(request, null, …)`, which writes nothing.
  - **(b) The row cannot answer basic questions.** `command_audits` has no actor, payload, channel/value/duration or Detail column. The row cannot answer "who switched DO1 ON, and to what?". The fingerprint is a SHA-256 hash and cannot be reversed.
  - **(c) The reason is dropped.** `EfCommandAuditStore.ToResultOrNull` drops `Detail`, so `GET /commands/{id}` returns `DEVICE_REJECTED` without "ESP_ERR_INVALID_STATE: …". The only other source is `reply`, which is limited to the issuer or master-admin and kept in a 2048-entry in-memory store that a restart clears. Unit and integration tests use `InMemoryCommandAuditStore`, which keeps `Detail`, so they will pass and hide this.
- **Failure scenario:** After an incident (a relay found ON overnight), the audit table shows `control/set_output Executed` with no user, no channel and no value. On Postgres, the acceptance step "pulse DO1 khi toggle → lý do của thiết bị hiện trên UI" shows only the code.
- **Evidence:**
  - `src/Hms.Server.Application/Commands/SendDeviceCommandHandler.cs:93-97,155-166`: a null plan means no audit write.
  - `src/Hms.Server.Infrastructure/Persistence/PersistenceEntities.cs:75-88`: no UserId, Payload or Detail.
  - `EfCommandAuditStore.cs:175-181`: Detail omitted.
  - `DeviceCommandsController.cs:66-70`: reply is gated to the issuer.
  - `DeviceLinkContracts.cs:294`: in-memory response store.
- **Suggested fix:** In the same migration, add `actor_user_id`, `payload_json` and `detail` to `command_audits`. Audit denied class-`B` attempts. Have the UI read the rejection reason from a persisted field. Run the Phase 1 tests against the EF store as well.

## Finding 9: The UI hides or shows controls from a global permission snapshot, but the server grants `gpio.do_write` per device role, and the read-back needs `gpio.do_view`
- **Severity:** Medium
- **Location:** Phase 2, "Requirements → Ẩn hoặc vô hiệu nút" (line 23), test 4
- **Flaw:**
  - **The snapshot misses device-role permissions.** For non-master users, `DeviceAccessPolicy` allows a firmware permission only through `access.Permissions`, which are the device-role permissions from `DeviceMembership`. `/me/permissions` returns feature grants, org grants, `device:<id>:action:<x>` grants and claims, but never device-role permissions. `can(snapshot,'gpio.do_write')` is therefore true only for master-admin (`*`). Every operator who holds the permission through a device role sees no buttons.
  - **The read-back permission is never checked.** The automatic read-back and "Đọc trạng thái" need `gpio.do_view` (catalog `get_status`), which the plan never checks. A role with `do_write` but not `do_view` can switch the relay, after which the read-back is denied and the channel is left on "Executed" with no state.
  - Phase 3 verifies only as master-admin, so it will not show either problem.
- **Evidence:**
  - `src/Hms.Server.Application/Authorization/DeviceAccessResolver.cs:23-35`: role permissions only.
  - `src/Hms.Server.Domain/Authorization/DeviceAccessPolicy.cs:37-43`.
  - `src/Hms.Server.Api/Controllers/MeController.cs:25-37`: the snapshot composition.
  - `frontend/src/core/auth/permissions.ts:57-70`: `can`.
  - `DeviceCommandModal.vue:125-128`: the same pattern exists today.
  - `command-catalog.json`: `get_status` requires `gpio.do_view`.
- **Suggested fix:**
  - Return the caller's effective per-device permissions, for example in `GET /link` or `GET /devices/{id}/permissions`, and gate on both `gpio.do_write` and `gpio.do_view`.
  - Without `do_view`, either hide the controls or block the write with an explicit "cannot verify" message.
  - Add a Phase 3 step with a non-master operator who has a device role.

## Finding 10: Missed consumers and verification gaps (C2 modal, pulse acceptance, DI panel, simulator)
- **Severity:** Medium
- **Location:** Phase 2, "Related Code Files" and test 8; Phase 3, "Gateway thật" step 5 and "Related Code Files"
- **Flaw:**
  - **(a) The modal's default payload will now be rejected.** The C2 modal's default for `set_output` is `{channel:1, value:1}`, an integer. The new policy (phase-01 lines 30-32) rejects that with `COMMAND_PAYLOAD_INVALID`, yet Phase 2 says the modal keeps its behavior and never lists fixing this default. (The device itself accepts numbers, spec line 795, so the policy is stricter than the device.)
  - **(b) The modal is a second DO control path with none of the panel's safeguards.** It has no automatic read-back, no per-channel lock and no allowlist hint. A set_output sent from the modal leaves the panel showing the old state as current.
  - **(c) Step 5 cannot be done from the panel.** Phase 3 step 5 (pulse DO1 while it is in `toggle` mode, and show the reason in the UI, plan.md line 36) is impossible there, because Phase 2 hides Pulse unless `mode === 'pulse'` (phase-02 line 21).
  - **(d) The DI panel still shows made-up data.** DI1 is always rendered as OFF. The DO1→DI1 loopback is the user's only physical cross-check, and the UI shows a fabricated DI1 value next to the real DO1 state.
  - **(e) The simulator is not in the repo.** The extended `sim-device.js` lives only in this session's scratchpad, so the 6-channel results cannot be reproduced.
- **Evidence:**
  - `frontend/src/features/devices/components/DeviceCommandModal.vue:149-152` (default `value: 1`), `:198-230` (no read-back or lock).
  - phase-02 lines 54-55 and 74.
  - `DeviceDetailView.vue:890-896`: fake DI array.
  - The only copy of `sim-device.js` is at `C:/Users/toann/AppData/Local/Temp/claude/dcbd9764dec96551/15092bb7-836b-4b83-9bd2-f9233a8cfebb/scratchpad/sim-device.js`; there is none in the repo.
- **Suggested fix:**
  - Change the modal default to `value: true` and add a test for it.
  - Either send the modal's `control.*` actions through `useDeviceCommand` with read-back, or remove `control.set_output` and `control.pulse` from the generic modal.
  - Run Phase 3 step 5 through the modal or the API, and reword the acceptance to match.
  - Show DI as "không có dữ liệu (không có action MQTT)" instead of fake OFF.
  - Commit the simulator under `tools/`, or record the exact command and spec rows it implements.

---

## Fact check

| Plan claim | Result |
|---|---|
| 11 read actions enabled, only class `R` (phase-01 Success Criteria, brainstorm) | VERIFIED: `PinnedCommandCatalog.cs:51-61,192` |
| §11: set_output/pulse = `gpio.do_write`, `B`, 120000, no confirm | VERIFIED: spec `mqtt-api.md:1280-1281`; `command-catalog.json` |
| Spec input shapes for set_output, pulse, get_status | VERIFIED: spec `mqtt-api.md:778-780` |
| Correlation id ≤ 39 bytes | VERIFIED: `HmsIdfCommandProtocolAdapter.cs:21`; spec line 461 |
| Store merge already keeps `digital_outputs` across heartbeats | VERIFIED: `DeviceTelemetryContracts.cs:119-122` |
| `applyTelemetryPayload` only updates the fields present | VERIFIED: `DeviceDetailView.vue:162-239` |
| `data-test="do-N-state"` and the 6 fake DO channels exist | VERIFIED: `DeviceDetailView.vue:856-872` |
| Modal poll is 1 s / 40 s and reusable | VERIFIED: `DeviceCommandModal.vue:40-74` |
| HIL `get_status` replies are not projected | VERIFIED: `DeviceLinkHandlers.cs:48-50` (contradicts brainstorm line 39) |
| "Trạng thái cuối luôn lấy từ lần đọc lại nên không bị hiển thị sai lâu" (phase-02 line 91) | FAILED: Findings 1, 5, 7 |
| "lệnh hết giờ thành `DEVICE_RESPONSE_TIMEOUT`" (phase-03 line 62) | FAILED during a broker outage or after a restart: `MqttDeviceLinkService.cs:101-116`; Finding 4 |
| "Mọi lệnh đều có audit" (plan.md line 38) | FAILED: `SendDeviceCommandHandler.cs:93-97,155-166` |
| "lý do của thiết bị hiện trên UI" (plan.md line 36) | FAILED on the EF store and unreachable from the panel: Findings 8, 10 |
| Files to modify: `PinnedCommandCatalog.cs`, `HmsIdfCommandProtocolAdapter.cs`, `DeviceLinkContracts.cs`, `DeviceLinkHandlers.cs`, `EfDeviceMqttBindingStore.cs`, `PersistenceEntities.cs`, `ApplicationDbContext.cs`, `DeviceLinkController.cs`, `MqttLinkOptions.cs`, `Program.cs`, `CatalogTests.cs`, `ManagementApiTests.cs`, `adminApi.ts`, `DeviceDetailView(.test).ts`, `DeviceCommandModal(.test).ts`; `frontend/src/core/commands/` | VERIFIED: all exist |
| `GET /me/command-catalog` | VERIFIED: `MeController.cs:48` |
| Launch configs `hms-api` and `hms-frontend` | VERIFIED: `.claude/launch.json` |
| `sim-device.js` | UNVERIFIED as a durable artifact (scratchpad only) |
| 427 unit / 103 integration / 139 FE tests | UNVERIFIED (tests not run, per instructions) |

## Contract consumers the plan misses

- **`DeviceMqttBinding`** (adds `AllowedOutputChannels`):
  - `DeviceLinkContracts.cs:14-36` (record and `Create`)
  - `DeviceLinkContracts.cs:51-76` (in-memory store)
  - `EfDeviceMqttBindingStore.cs:17-50` (explicit mapping, silent-null risk)
  - `HmsIdfCommandProtocolAdapter.cs:28`
  - `MqttDeviceLinkService.cs:208-219`
  - `DeviceLinkController.cs:37-42,69-70`
  - `DeviceLinkHandlers.cs:268-278`
  - `Program.cs:134-135`
  - `tests/Hms.Server.UnitTests/DeviceLinkTests.cs:295,311`
  - `tests/Hms.Server.IntegrationTests/MaitechRtuDeviceIntegrationTests.cs:30` (**not listed**)
  - The EF migration also needs `Persistence/Migrations/ApplicationDbContextModelSnapshot.cs` (**not listed**).
- **PUT `/link` request:** `DeviceLinkController.cs:145`; `docs/architecture-mqtt-device-link.md:72` (**not listed** for update in Phase 1). No frontend caller exists (`adminApi.ts:238` is GET only).
- **New input policy on `control.set_output`:** `DeviceCommandModal.vue:149-152` default `value: 1` (**not listed**).
- **`IDeviceTelemetryStore.Record` (new caller):** readers `DevicesController.cs:89` and `DeviceDetailView.vue:294-308` (side effect in Finding 6 not covered).
- **Realtime `status` event (new producer):** `RealtimeContracts.cs:179-182` and `SignalRConnectionBacklog.cs:39-51` coalescing (Finding 5 not covered).

Status: DONE_WITH_CONCERNS
Summary: Two critical failure modes block approval. First, the UI only reads back after `Executed`, so outcome-unknown results (timeout, PUBACK lost, dedup `UNAUTHORIZED`, restart) unlock a new-id retry that can repeat a physical action. Second, the allowlist fails open through PUT omission, the per-start seed rewrite and EF mapping. Five High findings cover the 120 s TTL against the 30 s timeout, restart/outage orphans, event coalescing dropping the read-back, the projection wiping telemetry, and old DO state shown as current.
