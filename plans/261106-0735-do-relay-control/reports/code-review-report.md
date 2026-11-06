# Code review: DO relay control (uncommitted working tree)

Date: 2026-11-06. Reviewer: code-reviewer (read-only; no build or test run, the tester agent owns those).
Scope: 34 modified + 14 untracked files (`git status --porcelain`), about 770 added and 160 removed lines. Spec used: `HMS-IDF/engineering-docs/api/mqtt-api.md` §2, §3 L478, §5, §6.4, §10, §11, R3, R4, R10.

## Overall assessment

The server core is solid. Payload and allowlist checks run before signing, the allowlist fails closed (null, empty, or unparseable all mean "none"), physical TTL is clamped below the reply timeout, a publish that may have gone out stays pending, and the sweep now runs while the broker is down. DO state can only be written by a verified `get_status` reply, and reading it needs `gpio.do_view`. I found no authorization bypass and no Critical issue.

The weak spots are at the edges of the "outcome unknown" model:
- The panel treats anything it cannot positively classify as "not sent", so it unlocks without a read-back (High).
- Several paths let a fresh-looking relay state stay on screen after it stopped being true (Medium).
- An unconfirmed publish can overwrite a device outcome the correlator already recorded in the audit row (Medium).

## Critical

None.

## High

### H1. The panel unlocks without a read-back on 5xx, network errors and unexpected statuses

- `frontend/src/features/devices/components/DeviceOutputsPanel.vue:129-136`: the `catch` comment says "The API refused the request (4xx/5xx): nothing reached the broker". That is false for 5xx and for network errors.
- `frontend/src/core/commands/useDeviceCommand.ts:30-32,62`: `mayHaveReachedDevice` is an allowlist of "sent" outcomes (`Published`, `MQTT_PUBLISH_UNCONFIRMED`). Every other status is treated as `not_sent`. The plan does the opposite: it names the outcomes that are certainly not sent (`ContractUnavailable`, `MQTT_NOT_CONNECTED`, 4xx) and requires lock plus read-back for everything else.

**Failure scenario.** `SendDeviceCommandHandler` publishes `set_output {channel:1,value:true}`. Then `auditStore.RecordAsync(plan, Published)` throws (DB timeout), so the handler returns 500 (`SendDeviceCommandHandler.cs:161-163`). The same happens if the browser loses the connection after the POST reached the server. The panel shows "Request failed (500)", unlocks DO1 and skips the read-back, while the relay switched. Server-side stale marking may not save it, because the correlator's own audit write hits the same DB fault before the projection runs (see M2). The page then keeps showing the old state as fresh. This breaks the plan acceptance "Không đường UI nào gửi lệnh DO ghi mà không khoá kênh và không đọc lại".

**Fix.**
- In `operate()`, unlock immediately only on an HTTP status in 400–499. For a thrown error with no status (network) or with 5xx, keep the lock and run the two read-backs.
- In `runDeviceCommand`, compute `not_sent` from an explicit deny-list (`status === 'ContractUnavailable'`, or code in {`MQTT_NOT_CONNECTED`, `MQTT_RESPONSE_SUBSCRIPTION_MISSING`, `MQTT_COMMAND_TOPIC_INVALID`}, or a pre-reservation refusal code). Treat any other status (`Executed`, `Acknowledged`, `IDEMPOTENCY_OUTCOME_UNKNOWN`, `AUDIT_RESULT_MISSING`…) as "may have reached the device".
- Add a panel test for the 500 path and one for a network error.

## Medium

### M1. An unconfirmed publish overwrites a device outcome already in the audit row

- `SendDeviceCommandHandler.cs:162-163` records the transport result after the publish returns.
- `EfCommandAuditStore.cs:145-152` only guards `Published` (`publishedOnly`). `Failed/MQTT_PUBLISH_UNCONFIRMED` is written unconditionally.

**Scenario.** The PUBACK times out (MQTTnet `WithTimeout(10s)`), but the broker had forwarded the command. The device replies `Executed` within that window. The correlator removes the pending entry, records `Executed` and marks the state stale. Then the handler records `Failed/MQTT_PUBLISH_UNCONFIRMED` over it. The pending entry is already gone, so no sweep will fix the row. The audit permanently loses the device's real outcome, and the UI polls the full 40 s before reading back.

**Fix.** Apply the same guard as `Published` (update only while `Status == "IN_FLIGHT"`) to any transport-produced result: both `Published` and `MQTT_PUBLISH_UNCONFIRMED`. Do this in the EF store and the in-memory store.

### M2. Stale marking depends on a successful audit write, and the sweep drops the rest of a batch on one failure

- `DeviceLinkHandlers.cs:75-101`: `pending.Remove` runs, then `audit.RecordAsync`, `responses.Save` and the event publish, and only then the projection. If the audit write throws, the reply is lost and an executed `set_output` never marks the state stale.
- `DeviceLinkHandlers.cs:106-122`: `TakeExpired` removes every expired entry first. One `RecordAsync` exception aborts the loop. The remaining commands are never audited, never published and never mark the state stale, and they cannot be retried because they left the registry.

**Scenario.** A Postgres hiccup during a timeout sweep. Several physical commands lose their `DEVICE_RESPONSE_TIMEOUT` and their stale mark. Other viewers keep seeing the pre-command state as fresh.

**Fix.** For physical commands, call `projection.MarkStaleAsync` (in-memory, cannot fail) before the audit write in both paths. In the sweep, wrap each command in its own try/catch and log the failure.

### M3. The projection treats device errors as "did not run", contrary to spec R3

`DeviceOutputState.cs:98-107` marks stale only for `Executed`, `Acknowledged` and `UNAUTHORIZED`. Its comment reads "Any other refusal means the handler did not act". Spec R3 says the opposite: "lỗi response không chứng minh lệnh ghi chưa chạy". From C04, an `ESP_ERR_NO_MEM` reply can come after the handler has already run, when attaching the reply data fails.

**Scenario.** `set_output` runs, and response building fails with `ESP_ERR_NO_MEM`. The store keeps the old state as not stale. The issuing panel reads back, but if that read-back fails (M4/M5), every viewer sees the old state as fresh.

**Fix.** For physical commands, mark `output_command_outcome_unknown` on every failed reply, except the pre-handler admission codes in R3(a) (`expired`, `future_timestamp`, `LANE_FULL`, `unknown_action`, `confirm_required`, `schema:*`, `permission_unmapped`). The cost is one extra "Không rõ" until the next read.

### M4. No staleness on heartbeat loss or when the server's broker link drops; read time shown instead of age

- Stale triggers are only connectivity `offline`, uptime going backwards, and command outcomes (`DeviceLinkHandlers.cs:220-236`). Per spec §2 and §10, connectivity/LWT is optional ("khi connectivity/LWT được bật"). If the server's own broker session drops, it hears nothing at all.
- The panel ignores `link.online` and `link.brokerConnected`. It shows `toLocaleTimeString()` (`DeviceOutputsPanel.vue:62-63`), while the plan asked for the age of `readAt`.

**Scenario.** The gateway loses power, with no LWT configured. The relay is de-energized, but the panel keeps showing "DO1 ON" and "Đọc lúc 14:03:12" with no stale banner, for hours. The in-memory store lives as long as the process does.

**Fix.**
- Server: mark `device_offline` when the heartbeat goes stale. A cheap option is to treat `IsStale` as true in `GET /outputs` when the last heartbeat is older than `HeartbeatFreshness` or the broker is disconnected.
- UI: show the age ("đọc 3 giờ trước") and show "Không rõ" when `!link.online`.

### M5. The panel can keep showing a state that is no longer current

All in `DeviceOutputsPanel.vue`:
- **Lines 72-84.** On `not_read`, `forbidden` or `error`, `load()` changes `loadState` but keeps the old `outputs.value`. The old channels and toggle buttons stay on screen under a red "Không tải được" line.
- **Lines 101-107.** `readBack()` returns `true` ("confirmed") after `load()` even when `load()` failed, so the operator gets no "chưa đọc lại được" message.
- **Lines 183-184.** Concurrent `load()` calls have no latest-wins guard. A slower, older response can overwrite a newer stale flag.
- **No reload when SignalR reconnects.** `DeviceDetailView.vue` only bumps `outputsRefreshKey` on an `outputs` event.
- **Outputs events can be dropped.** `outputs` is now coalescible, and `SignalRConnectionBacklog.cs:55-76` drops coalescible events first under pressure, with no resynchronization. Unlike `status`, an `outputs` event is a "re-read now" signal: dropping it is not replaced by a later snapshot.
- **Ordering in the correlator** (`DeviceLinkHandlers.cs:79-95`). The audit row becomes `Executed` before `outputs.Set` runs, so a poll that lands in that gap fetches the pre-read state. It usually self-heals through the event, which brings back the event-delivery dependency above.

**Fix.**
- Set `outputs.value = null` on any non-`ok` load.
- Have `readBack` return `load()`'s success, and check that `readAt` is not older than `final.reply.receivedAt`.
- Add a request sequence number to `load()`.
- Reload on hub reconnect.
- Never evict `outputs` without resynchronization: coalesce it, but count it as non-evictable.

### M6. The allowlist and output state survive a re-bind, and PUT /link can resurrect an old allowlist

- `DeviceLinkHandlers.cs:300-306` copies the stored allowlist into the new binding (read-modify-write). `EfDeviceMqttBindingStore.cs:35-37` then writes that column on every upsert.
- **Lost-update race.** A master-admin narrows `[1,2]` to `[1]` between the handler's `Find` and `Upsert`. The `devices.manage` PUT then writes `[1,2]` back. This is the exact write path RT#1 meant to close.
- **Re-bind to another gateway.** A `devices.manage` user re-binds the device to a different `MqttDeviceId`, which is a different physical gateway. The allowlist (and the in-memory DO state, `IDeviceOutputStateStore`) carry over. A master-admin approved channel 1 on gateway A, not on gateway B. This matters wherever two gateways share a command key; per spec R11, the key holder owns the device.

**Fix.**
- Give the store a column-scoped `SetAllowedOutputChannels`, and stop `Upsert` from touching the column on update. This removes the race.
- When the topic base changes, reset the allowlist to empty and clear the output state. This only ever narrows, so it keeps RT#1's intent, but it changes the plan's literal "PUT /link không đụng tới danh sách". The lead should confirm.

### M7. The restart sweep misses commands whose audit row is still IN_FLIGHT

- `EfCommandAuditStore.cs:44-67` and `Program.cs:157-165` fail only rows in `Published`.
- A crash between `ReserveAsync` and `RecordAsync(Published)` leaves the row `IN_FLIGHT` forever. That window includes the MQTT publish and its 10 s PUBACK wait. `Find` maps `IN_FLIGHT` to `null`, so the UI never gets an outcome and a same-id retry gets `IDEMPOTENCY_OUTCOME_UNKNOWN`.
- Plan acceptance "Restart API khi đang có lệnh → OUTCOME_UNKNOWN_AFTER_RESTART" is therefore only partly met.

**Fix.** At startup, also move `IN_FLIGHT` to `Failed/OUTCOME_UNKNOWN_AFTER_RESTART`. This is valid only for a single instance: the pending registry and output store are per-process, so record "single instance only" in the docs.
**Tests.** The EF variant of `FailUnfinishedPublished` has no test; the plan asked for one.

## Low

- **L1. Client-supplied `RequestedAt` reopens RT#3.** `DeviceCommandsController.cs:41` accepts a client `RequestedAt`, and `SendDeviceCommandHandler.cs:126` allows up to +60 s. The envelope `ts` comes from it (`HmsIdfCommandProtocolAdapter.cs:73-74`), and the device accepts a `ts` up to 60 s in the future (§3 L478). So a signed `set_output` stays valid until about +70 s, past the 30 s timeout. The UI does not send it; API scripts can. Fix: for class B, ignore the client value and use server `UtcNow`, or cap the future skew at a few seconds.
- **L2. A typed 0 becomes a 1 s pulse.** `DeviceOutputsPanel.vue:160`: `pulseMs[channel] || DEFAULT_PULSE_MS` turns a typed `0` into a 1000 ms pulse, so the relay pulses although the operator entered an invalid value. Fix: validate 1..MAX and refuse otherwise, so the server's `COMMAND_PAYLOAD_INVALID` path shows.
- **L3. The dev seed reopens channels on every restart.** `Program.cs:140-142` re-applies the seed allowlist on each restart, undoing a runtime "close all" made through the API. This is dev-only, but dev drives the real lab relay. Fix: seed only when the stored value is null.
- **L4. Large channel numbers overflow the column and get the wrong error code.** `ValidateOutputChannels` has no upper bound on channel numbers, but the column is `varchar(200)` (`ApplicationDbContext.cs:97`). 32 ten-digit channels overflow it on Postgres, and the resulting `DbUpdateException` is remapped to `MQTT_TOPIC_BASE_IN_USE` (`EfDeviceMqttBindingStore.cs:43-47`), so the client gets a 409 with a wrong code. The in-memory provider does not enforce the length, so tests pass. Fix: cap channels (e.g. ≤ 64).
- **L5. Optional `projection` parameter.** `DeviceLinkHandlers.cs:36,198` make `DeviceReplyProjection? projection = null` optional. A missing registration or an old-style construction silently turns off stale marking. Make it required; the DI registration exists already.
- **L6. Not every early refusal is logged.** `SendDeviceCommandHandler.cs:26-43,139-154`: `COMMAND_ACTOR_MISMATCH`, `CORRELATION_ID_REUSED`, `KEY_GENERATION_FENCE` and `IDEMPOTENCY_*` return through `Failure` without going through `RecordAsync`, so they skip the new structured log.
- **L7. The telemetry store still keeps `digital_outputs` and `digital_inputs`, and the DI panel still shows them.** `DeviceTelemetryContracts.cs:119-125` carries them forward from earlier ingests, and `GET /telemetry` returns them to any viewer. If any heartbeat or master-admin POST carries them, DO data reaches users without `gpio.do_view`. `DeviceDetailView.vue:865-890` still renders DI from that unverified data, while the plan asked for the C12 message only.
- **L8. Read-back control.** `readBack` retries at once (`DeviceOutputsPanel.vue:148`), but spec §5 asks for backoff on `LANE_FULL`. Also, `canControl` does not require `canRead` (`get_status` available), although the read-back depends on it.
- **L9. Possible secrets in the payload summary.** `EfCommandAuditStore.cs:214-227` stores the payload JSON for class D/F actions. Those actions are denied today, but an OTA URL or token would land in plain text once they open. Add a redaction allowlist per action.
- **L10. Missing tests.**
  - The MqttDeviceLinkService loop (sweep while disconnected); only the correlator sweep is tested.
  - The EF `FailUnfinishedPublished`.
  - The panel's 5xx and network paths.
  - The modal now clears a user-typed correlation id even when the POST fails (`DeviceCommandModal.vue:181`). This is a minor UX change.

## Plan acceptance (plan.md)

| Criterion | Status | Evidence |
|---|---|---|
| Gateway thật: DO1 ON/OFF round trip, DO2 refused with nothing published, pulse on toggle → DEVICE_REJECTED, empty list blocks | Code path present, live check pending (phase 4) | `OutputCommandPolicy.cs:54`; `HmsIdfCommandProtocolAdapter.cs:58-60` (refused before signing; transport returns at `MqttDeviceCommandTransport.cs:42-43`) |
| Simulator DO1–DO6 with read-back | Code present, live pending | `DeviceOutputsPanel.vue:125-167` |
| No reply → DEVICE_RESPONSE_TIMEOUT, channel stays locked, auto read-back, state stale | Met in code | `DeviceLinkHandlers.cs:106-122`; panel `:145-152` |
| Restart while a command is pending → OUTCOME_UNKNOWN_AFTER_RESTART | Partly met: `Published` rows only (M7) | `Program.cs:157-165`; `EfCommandAuditStore.cs:44-67` |
| Physical `ttl_ms ≤ 10000` | Met (caveat L1) | `SendDeviceCommandHandler.cs:118-120`; `MqttLinkOptions.cs:48` |
| No UI path sends a DO write without lock and read-back | **Not met** (H1); modal filter met | `DeviceOutputsPanel.vue:131-136`; `DeviceCommandModal.vue:67-71`; legacy `App.vue:772` sends no payload, so the policy refuses it |
| DO state cannot be written from heartbeat or POST /telemetry | Met | `DeviceOutputState.cs:87-93` is the only writer |
| DO state not exposed without `gpio.do_view` | Met for `/outputs` and the event (`DeviceLinkController.cs:45-61`; event has no channels, `DeviceOutputState.cs:123-130`); residual through telemetry (L7) | |
| `gpio.do_write` is high-risk | Met | `DevicePermissionCodes.cs:27` |
| Every reserved command's audit row has issuer, detail and payload summary (physical) | Met | `EfCommandAuditStore.cs:94-95,156,175` |
| Early refusals logged | Mostly met (L6) | `SendDeviceCommandHandler.cs:176-182` |
| Tests green, vue-tsc clean | Not verified here (tester agent) | — |
| Phase 1: `/me/command-catalog` = 11 reads + 2 DO available | Met by test | `ManagementApiTests.cs` catalog assertion; `PinnedCommandCatalog.cs:68-73,203-209` |

Phases 1–3 look implemented; phase 4 (live) remains. The leftovers are the deviations above: H1, M3, M4, M7, and the age display.

## Touchpoint regressions (b)

- **The 11 reads.** Dispatch is unchanged. The policy returns null for non-physical commands (`OutputCommandPolicy.cs:28-29`), the TTL clamp applies to class B only, and `EnabledActions` is untouched. One intentional change: a publish exception on a read now returns `Failed/MQTT_PUBLISH_UNCONFIRMED` and stays pending, where it used to return `MQTT_PUBLISH_FAILED` and drop the pending entry. Nothing in the repo references the old code.
- **Audit for non-physical commands.** `PayloadSummary` is null and `IssuedBy` and `Detail` are now stored. `Find` now returns `Detail` to any device viewer (previously EF dropped it); the device error text is not sensitive. The in-memory EF path now uses tracked loading, which affects only that provider.
- **Telemetry ingestion.** Unchanged apart from the uptime comparison and the stale mark before the `status` publish.
- **SignalR.** Other event types are unchanged; `outputs` only adds a coalescing key (eviction caveat in M5).
- **C2 modal.** Behaviour for non-DO commands is the same, now through the composable. It also polls on `MQTT_PUBLISH_UNCONFIRMED` and shows a client-timeout message.

## Contract changes (c)

All of these look intentional; the ones marked * are not in the requested list:

- `DeviceMqttBinding` gains a fifth optional `AllowedOutputChannels` (Deconstruct arity changes; record equality is now by list reference), plus `MaxOutputChannels` and `ValidateOutputChannels`.
- `DeviceLinkDto` gains `allowedOutputChannels`, `canControlOutputs` and `canViewOutputs` at the root.
- New endpoints: `GET /api/v1/devices/{id}/outputs` and `PUT /api/v1/devices/{id}/link/output-channels` (master-admin, CSRF, warning log).
- `POST /devices/{id}/telemetry` is now master-admin + CSRF. This breaks the superseded Node bridge.
- Config `Mqtt:MaxPulseMs` (1..3,600,000), and `Mqtt:Devices:N:AllowedOutputChannels`.
- \* `Mqtt:ResponseTimeoutSeconds` must now be > 10 s, otherwise startup fails with `MQTT_TIMING_INVALID`.
- DB columns `device_mqtt_bindings.AllowedOutputChannels varchar(200)` (the plan said `text` through a value converter; the implementation joins strings in the store, which is equivalent apart from L4), and `command_audits.Detail varchar(1024)`, `IssuedBy uuid`, `PayloadSummary varchar(512)`. All nullable.
- \* `ICommandAuditStore.FailUnfinishedPublished`.
- \* `IMqttCommandPublisher`.
- \* Transport code `MQTT_PUBLISH_FAILED` becomes `MQTT_PUBLISH_UNCONFIRMED`.
- \* New realtime event `outputs {readAt, stale, staleReason}`.
- \* Catalog now marks `control.set_output` and `control.pulse` available, and three control rows high-risk.
- \* Existing device roles holding `gpio.do_write` become master-admin-only to edit, delete or assign.

## Patterns (d)

- Controllers follow the existing order: 404 when out of scope, then `Forbid`, plus `[ValidateAntiForgeryToken]`. `/outputs` sits on `DeviceLinkController` with a `~/` route, which is unusual but works.
- DI follows the existing style.
- The EF store follows the existing find-then-upsert style.
- The Vue code uses `Button`/`Badge`. A raw number input is fine, since no Input primitive exists.

## Concurrency (e)

- **Two commands on the same channel.** Within one tab, `channelBusy` is set synchronously, so a double click is blocked. Across tabs or users there is no server lock; this is an accepted risk, and the final truth is the read-back.
- **Store Set/MarkStale.** The CAS loop is correct.
- **Sweep vs. late reply.** Exactly one side wins, through atomic `TryRemove`/`TakeExpired`. The sweep thread and the inbound thread can interleave Set and MarkStale; the result is conservative unless the device clock is unsynced (residual risk already documented).
- **Startup reconciliation vs. in-flight commands.** It runs before `app.Run()`, so no command from this process is in flight. It is unsafe with more than one instance (see M7).

## Fail-closed checks (f)

- Allowlist null or empty → `DO_CHANNEL_NOT_ALLOWED` (`OutputCommandPolicy.cs:54`).
- Unparseable stored value → `[]` (`EfDeviceMqttBindingStore.cs:57-69`).
- Missing permission → refused before reservation, and the UI hides the controls.
- Catalog untrusted or stale → `CATALOG_UNTRUSTED`/`CATALOG_STALE`, and the UI gates on it too.
- Broker down → `MQTT_NOT_CONNECTED`, pending removed, sweep still runs.
- Unknown class B action → `PHYSICAL_ACTION_UNSUPPORTED`.
- Exception: capabilities not yet received means "allowed" (pre-existing). The device refuses unknown actions anyway.

## Unresolved questions

1. Does the real `status.dump` heartbeat contain `digital_outputs`? If it does, L7 is a live exposure.
2. Should a topic-base change reset the allowlist? This is the M6 fix, and it touches the plan's wording.

Status: DONE_WITH_CONCERNS
Summary: The server safety core holds (fail-closed allowlist, check before signing, TTL clamp, pending kept, verified-only DO state); there is no Critical issue. One High: the DO panel unlocks without a read-back on 5xx and network errors. Several Medium issues let a stale relay state look fresh, or lose the device's outcome in the audit row.
Concerns/Blockers: H1 breaks a plan acceptance criterion and should be fixed before the phase-4 live test. M1–M3 are small server changes worth doing in the same pass.
