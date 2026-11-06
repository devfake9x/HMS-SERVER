# Code review: monitoring read actions (uncommitted change set)

Reviewer: code-reviewer. Date: 2026-11-06. Mode: read-only (no build/test run; tester runs them in parallel).

## Scope

- Server: `PinnedCommandCatalog.cs`, `SendDeviceCommandHandler.cs`, `DeviceLinkContracts.cs` (registry), `DeviceLinkHandlers.cs` (correlator), `MqttDeviceCommandTransport.cs`, `MqttLinkOptions.cs`, `DeviceCommandsController.cs`, `DeviceLinkController.cs`, new `Api/Authorization/DeviceAccessChecks.cs`, `ApplicationDbContext.cs` + migration `20261106095440_AddCommandAuditStatusIndex`.
- Frontend: `core/commands/useDeviceReads.ts`, `core/time/readAge.ts`, `core/api/adminApi.ts`, `features/devices/components/reads/*`, `DeviceDetailView.vue`, `DeviceOutputsPanel.vue`.
- Tests: `DeviceLaneSafetyTests` (new), `CatalogTests`, `DeviceLinkTests`, `DeviceOutputStateTests`, `DeviceCommandAuthorizationTests`, `PhysicalOutputSafetyTests`, `ManagementApiTests`, frontend composable/card/view/panel tests.
- ~390 changed lines in tracked files + ~1,150 new lines.
- Spec checked: `HMS-IDF@83e93d77e:engineering-docs/api/mqtt-api.md` §2, §3, §5, §6.1-§6.3.

## Overall

The server pieces are small and mostly correct. `TryRegisterRead` is atomic, the `command_result` suppression covers both the reply path and the sweep, `allowed-commands` resolves access once per request, and the `DeviceLinkDto` flags keep their meaning after the refactor. The frontend composable is careful about overlap, abort and late results. There are no Critical findings.

One High finding. The per-device read cap also covers `control.get_status`, which is the relay panel's verification read, so monitoring reads can now block relay read-back. That contradicts the plan's own "relay không bị ảnh hưởng". The other findings are about device-switch leftovers, scope consistency, the TTL/clock-skew trade-off and test gaps.

## Critical

None.

## High

### H1. Read cap blocks the relay verification read (`control.get_status` is class R)

- **Where:**
  - `src/Hms.Server.Infrastructure/Mqtt/MqttDeviceCommandTransport.cs:54-59`: the cap applies to every class R command.
  - `command-catalog.json:363-366`: `control.get_status` is class `R`.
  - `frontend/src/features/devices/components/DeviceOutputsPanel.vue:127-136,184`: `readBack()` sends `control.get_status`. A `not_sent` outcome returns `false`, and the second attempt `readBack() || readBack()` runs immediately, with no backoff.
- **Scenario:**
  1. Two viewers press the three cards: six reads want slots and the cap is 4. Alternatively, one read is silently dropped by the device (spec §3: full queue drops without reply), and the server keeps its slot for `ResponseTimeoutSeconds` = 30 s, even though the device TTL is now 10 s.
  2. Meanwhile an operator toggles a relay. `set_output` is not capped, so it publishes and executes, and the projection marks the outputs stale (`output_command_executed`).
  3. Both read-backs get `DEVICE_READ_BUSY` within milliseconds. The channel ends with "Chưa đọc lại được trạng thái" and the relays show "Không rõ" until someone reads again.
  4. This fails safe, but it is a regression in the DO flow that just shipped. It is also a lever: any user holding one read permission (e.g. `sys.time_view`) can keep all four slots busy. There is no rate limit on `POST /commands`, so this blocks relay verification for everyone.
- **Fix (pick one):**
  - **(a)** Exempt the physical-output read-back from the cap: count and cap only class R reads other than `control.get_status`. Put that predicate next to `OutputCommandPolicy` so it has a domain anchor.
  - **(b)** Reserve headroom: monitoring reads are capped at N, and `control.get_status` may use slot N+1.
- **With either fix:**
  - In `DeviceOutputsPanel.readBack`, treat `DEVICE_READ_BUSY` as transient and wait about 1-2 s before the second attempt.
  - Add a lane test: four pending monitoring reads on device A, and `control.get_status` on A is still `Published`.

## Medium

### M1. One user can starve every other viewer of the shared read cap; dropped reads hold a slot for 30 s

- **Where:** `DeviceLinkContracts.cs:281-293` (no per-user share); `MqttDeviceCommandTransport.cs:53` (the pending deadline is `ResponseTimeoutSeconds`, not `plan.ExpiresAt`).
- **Scenario:**
  - A user scripts reads in a loop, which the API allows.
  - Each refused attempt still writes an audit row (`ContractUnavailable/DEVICE_READ_BUSY`).
  - Every other viewer's cards end in `DEVICE_READ_BUSY` after the single retry.
  - Separately, a read the device dropped keeps its server slot for 30 s, although the device discarded it at TTL (10 s).
- **Fix:**
  - Add a per-user sub-cap, for example at most 2 per user per device, and/or rate-limit `POST /commands`.
  - For cap counting, treat a read as finished at `plan.ExpiresAt` plus a small grace period. The pending entry can stay registered for reply correlation until the 30 s timeout.

### M2. A 10 s TTL on every read removes clock-skew tolerance, including for the diagnostic reads

- **Where:** `SendDeviceCommandHandler.cs:114-119`. Spec §3: the device checks the age at dequeue and skips the check only when `time(NULL) <= 1577836800`. `ts` is floored to whole seconds (`HmsIdfCommandProtocolAdapter.cs:73`), so the real window is about 9-10 s minus transit time.
- **Scenario:**
  - Take a device whose clock is valid but runs more than about 9 s ahead, for example an RTC with no NTP reachable on a local-only uplink.
  - Before this change it rejected only the relay commands, which already had a 10 s TTL.
  - Now it also rejects all 26 open reads with `expired`, including `system.get_time` and `system.ping`, the reads an operator would use to diagnose the skew.
  - The plan's risk note covers unsynced clocks only.
- **Not a defect against the accepted plan.** It needs a user decision, with these options:
  - **(a)** Keep 10 s. In the cards, map `DEVICE_REJECTED`/`expired` to "Đồng hồ thiết bị lệch, kiểm tra NTP". Add a skewed-clock case to the phase 3 live check.
  - **(b)** Cap reads at `ResponseTimeoutSeconds*1000 - margin` (about 20 s). This still meets "expire before the server gives up".
  - **(c)** Exempt `system.get_time`/`system.ping`.

### M3. After a route switch, data and in-flight work of device A remain under device B

- **Where:** `DeviceDetailView.vue:319-332` resets `device`, `deviceLink`, `allowedCommands`, `hardwareTelemetry` and `lastHeartbeat`. It leaves:
  - `telemetryData` (voltage/temperature/rssi/firmware/battery, rendered at `:797-821`);
  - `commandHistory` (the C2 history of A shows on B's commands tab);
  - the `DeviceOutputsPanel` state.
- **Relay panel detail:** `DeviceOutputsPanel` keeps `channelBusy`/`channelError` across the switch. An `operate()` in flight for A keeps running, and `send()` reads `props.deviceId` at call time (`DeviceOutputsPanel.vue:120`). So A's verification `control.get_status` (and the post-pulse read) goes to device B, and B's outputs are loaded as the result of A's command.
- **Scenario:** the operator toggles a relay on A, then presses browser Back to B during the read-back. B's panel shows A's channel error or busy lock, and a read is spent on B.
- **Pre-existing, but now in scope:** the panel behaviour existed before, but the new watch makes route reuse an explicitly supported path, and the comment at `:319-320` claims "nothing of device A shows under device B".
- **Fix:**
  - Reset `telemetryData` and `commandHistory` in the watch.
  - In the panel, capture `deviceId` at the start of `operate()`/`pulse()`, use it for every send and read-back, and drop the results if `props.deviceId` changed.
  - Clear `channelBusy`/`channelError`/`pulseMs` on `deviceId` change.

### M4. "Last Heartbeat" still shows a made-up value

- **Where:** `DeviceDetailView.vue:372-375`: `lastHeartbeat.value = '2 moments ago'` when telemetry has no data.
- **Why it matters:** the acceptance says Last Heartbeat changes only with real heartbeats. This placeholder claims a heartbeat that never happened, and the new route watch now applies it to every device the page switches to.
- **Fix:** show `deviceLink.lastHeartbeatAt` when present, otherwise keep `'Never'`.

### M5. Scope rules differ between "may send" and "may read the result" for membership-only users

- **Where:**
  - `allowed-commands` uses `DeviceAccessChecks.CanSendCommands` = `IsMasterAdmin || InScope`, where InScope means organization or membership (`DeviceCommandsController.cs:64`). That matches POST (`SendDeviceCommandHandler.cs:55`).
  - `GET .../commands/{id}` (`DeviceCommandsController.cs:91-93`), `/link` and `/outputs` use `ScopeAuthorization.CanAccessDevice`, which is organization only.
- **Scenario:**
  - A user who only has a membership on the device gets the action list, and their POST publishes.
  - Every poll returns 404, so the C2 modal or any client ends in `CLIENT_TIMEOUT` after 40 s, after using a read slot.
  - Today the cards are hidden for this user because `/link` returns 404, so the visible impact is the C2 modal.
- **Plan items not met:**
  - Phase-1 asked to record the membership-only rule in the phase report. The reports folder has no phase report.
  - Phase-1 test 6, last bullet (membership-only behaviour), is not written.
- **Fix:** let the sender read back what they may send. Use `CanSendCommands(access)` for the scope gate of `GET commands/{id}`, or explicitly decide and document the split, and add the test.

### M6. Tests that would pass on broken code

- **`ManagementApiTests.Reply_data_needs_the_permission_at_read_time_not_only_at_send_time`:** there is no positive control. A sender who still holds `adc.view` must still see `reply.data`. Without that case, a `MayReadReply` that always returns `false` passes.
- **`ManagementApiTests.Allowed_commands_lists_...`:** only master-admin (sees everything) and a no-role viewer (sees nothing) are covered. A non-admin with a device role should get exactly role ∩ enabled. The membership-only case is also missing.
- **Deleted security assertion:** the check that the realtime `command_result` carries no reply `data` was removed (old `DeviceLinkTests.cs:172-173`). It was not re-added for write results; the new `DeviceOutputStateTests.Read_results_stay_off_...` only counts the event. Assert the `set_output` event payload keys (`status`, `code`, no `data`).
- **Handler path:** `DEVICE_READ_BUSY` is tested only at the transport. Add one handler-level case: an audit row with `ContractUnavailable/DEVICE_READ_BUSY`, and no reservation left `IN_FLIGHT`.

## Low

- **L1. The cap check runs after signing.**
  - The handler calls `protocolAdapter.PrepareAsync` (HMAC sign) before `transport.SendAsync` (`SendDeviceCommandHandler.cs:160-161`), so a refused read is signed and then discarded.
  - There is no security impact: nothing is published and there is no nonce. But phase-1 says "lệnh bị từ chối không được ký", which is not literally true.
  - Fix: reword the plan, or add a cheap pre-check before `PrepareAsync`, with `TryRegisterRead` staying the authority.
- **L2. No structured log for `DEVICE_READ_BUSY`.** `RecordAsync` logs only when the plan is null, so the audit row is the only trace. The plan asked for a structured log line.
- **L3. Race that shows a false "no data" message.**
  - The correlator writes the audit row (`Executed`) before it saves the reply (`DeviceLinkHandlers.cs:94-95`). A poll landing in between sees `Executed` with `reply: null`, and the card shows "Thiết bị không trả dữ liệu".
  - The ordering is pre-existing; it is now visible to users.
  - Fix: call `responses.Save` before `audit.RecordAsync`. Polling continues while the audit row still says `Published`.
- **L4. Comment claims a check that has not happened.** `PinnedCommandCatalog.cs:47-48` says the monitoring replies "were checked against §6 on the lab gateway". Phase 3 (live verification) has not run. Fix the comment, or commit after phase 3.
- **L5. Scope rule is copied, not shared.** `DeviceAccessChecks.CanSendCommands` repeats the condition at `SendDeviceCommandHandler.cs:55`, so the two can drift. Move it to `Domain.Authorization.DeviceAccessPolicy` (e.g. `CanReach(access)`) and use it in both places.
- **L6. ADC join can put a bad row on channel 0.** In `DevicePowerAdcCard.vue:64-66`, `Number(null)` and `Number('')` are 0, so a channel with `ai: null` lands on row 0. Accept only integer `ai >= 1` (spec range 1..16).
- **L7. ADC sections drop unexpected shapes silently.** The `bodyless` ADC sections show nothing when `get_channels`/`get_values` data is neither an array nor an object wrapping an array, and extra row fields are dropped. Phase-2 says unknown shapes go to "Khác" as raw JSON. Fall back to `ReadFieldList` when `list()` is empty but data is not.
- **L8. Relay panel handles `CATALOG_STALE` only on writes.** `readAll()`/`readBack()` ignore the outcome (`DeviceOutputsPanel.vue:127-136,210-216`), so "Đọc trạng thái" with a stale catalog shows nothing.
- **L9. Accessibility and wording:**
  - Three identical "Đọc"/"Dừng" button names: add `aria-label` with the card title.
  - The per-read status lines are not in an `aria-live` region.
  - The ADC `<th>` cells have no `scope`.
  - The `DEVICE_READ_BUSY` detail is English text inside the Vietnamese UI.
- **L10. Realtime status can show the wrong state after a fast route switch.**
  - `rotate()` at `DeviceDetailView.vue:330` makes an unfinished first `joinRealtime` fail with `SESSION_CHANGED`.
  - Its `catch` can set `realtimeStatus = 'disconnected'` after the second join succeeded, so the header shows "SignalR: disconnected" while subscribed. Cosmetic.
  - Fix: guard the status writes with a local sequence number.
- **L11. Global lock scans all devices.** `TryRegisterRead` scans the pending commands of all devices (`_pending.Values` locks and copies) under one global lock. Fine at lab scale; use a per-device counter if the fleet grows.
- **L12. Docs are stale (phase 3 must fix):**
  - `docs/architecture-mqtt-device-link.md:80` still says `command_result` goes out for every command.
  - `:133` still names `PhysicalCommandTtlMs` (class B only).
  - Document `Mqtt:MaxPendingReadsPerDevice`, `MQTT_READ_LIMIT_INVALID`, `DEVICE_READ_BUSY` and `allowed-commands`.
  - Note that `/catalog` still reports `ttlMs` 120000 while the envelope uses 10000.
- **L13. Connectivity no longer updates Last Heartbeat.** It refreshes the link instead (`DeviceDetailView.vue:275-283`). plan.md's acceptance reads "chỉ đổi theo heartbeat và connectivity". The implemented behaviour is the better one (an `offline` event must not bump the heartbeat); update the plan wording.
- **L14. Test isolation.** `DeviceDetailView.test.ts` "reloads everything" changes the shared `mockRoute` and restores it only on the happy path. A failed assertion leaks `dev-102` into later tests. Restore it in `afterEach`.

## Explicit checks

### (a) Acceptance criteria (plan.md)

| Criterion | Status | Evidence |
|---|---|---|
| 15 reads opened; 28 total; catalog JSON/hash/permission vocabulary unchanged | Met | `PinnedCommandCatalog.cs:64-79`; `command-catalog.json` unmodified; permissions match plan (checked in JSON); `CatalogTests.cs:113-136,138-153` |
| At most N reads pending per device, refused before sending | Met, with the signing deviation (L1) | `MqttDeviceCommandTransport.cs:54-59`; `DeviceLaneSafetyTests.cs:27-62` |
| Physical commands not capped | Met for B, **but the relay read-back is capped (H1)** | `MqttDeviceCommandTransport.cs:61-63` |
| Every command `ttl_ms <= 10000` | Met | `SendDeviceCommandHandler.cs:114-119`; `DeviceLaneSafetyTests.cs:64-77` |
| Reads emit no `command_result` (reply + sweep) | Met | `DeviceLinkHandlers.cs:169-172`; `DeviceOutputStateTests` new test |
| Last Heartbeat only from heartbeat (/connectivity) | Partly: realtime path met (`DeviceDetailView.vue:283`); fabricated value remains (M4); connectivity wording (L13) |
| `allowed-commands`: open + permitted, resolves once, POST scope rule, 404, no-store | Met; tests thin (M6); membership-only rule not recorded or tested (M5) | `DeviceCommandsController.cs:52-75`; `DeviceAccessChecks.cs` |
| `GET commands/{id}` re-checks action permission | Met | `DeviceCommandsController.cs:100-102,117-129` |
| Cards: on click only, cancellable, no overlap | Met | `DeviceReadCard.vue:45-75`; `useDeviceReads.ts:92-131` |
| Cards: data of the device being viewed | Met for the cards (reset at `DeviceReadCard.vue:43`, cards unmount when `deviceLink` is null); not for the rest of the page (M3) |
| Per-command read time; failed read drops the old value | Met | `ReadSection.vue:15-31`; `useDeviceReads.ts:102-110` |
| CATALOG_STALE / permission / busy / offline shown | Met (busy and permission shown as code + detail) | `DeviceReadCard.vue:79-90`, `ReadSection.vue:34-36` |
| Live gateway (15 executed, fixtures redacted) | Not done (phase 3); the code comment already claims it (L4) |
| All tests green; vue-tsc clean | Not verified here (tester) |

### (b) Regressions

- **DO relay panel:**
  - It polls and never used `command_result`.
  - `outputs` is still published by `DeviceReplyProjection.ApplyAsync` for `control.get_status`, so the panel still refreshes.
  - Regression: H1 (read-back capped), plus the device-switch leak (M3).
- **C2 modal:** `DeviceCommandModal.vue:37` uses `useDeviceCommand`, which polls `GET commands/{id}`. Losing `command_result` for reads changes nothing there. The modal now lists the 15 new reads and gates them on feature permission, as the plan says.
- **`App.vue:142-143`:** consumes only `status`. Unaffected.
- **Realtime consumers of `command_result`:** grep finds none in production frontend code. The only server references are the publisher and tests; the doc at `architecture-mqtt-device-link.md:80` needs an update (L12).
- **`DeviceLinkDto` flags:** same semantics (null actor → false; otherwise `Evaluate`), now with one resolve instead of two.
- **Existing reads with a 10 s TTL:**
  - TTL is checked when the command leaves the device queue, not against reply size, so `status.dump` and `system.get_operations` are unaffected while the clock is synced.
  - Risk: valid-but-skewed clocks (M2).
  - `/catalog` still advertises `ttlMs` 120000; the frontend does not use it.
- **Telemetry/heartbeat display:** `status` events are still applied. `command_result`, `device_event` and `capabilities` no longer bump the heartbeat or feed telemetry. Fabricated value: M4.

### (c) Contract changes

| Change | Intentional / listed | Notes |
|---|---|---|
| `GET /api/v1/devices/{id}/allowed-commands` → `{ actions: string[] }` (`AllowedCommandsDto`) | Yes | camelCase `actions` matches `adminApi.ts:254-255`; not in docs yet (L12) |
| Config `Mqtt:MaxPendingReadsPerDevice` (default 4, 1..16, `MQTT_READ_LIMIT_INVALID`) | Yes | `Validate` runs only when `Enabled` (consistent with the other keys) |
| Error code `DEVICE_READ_BUSY` (status `ContractUnavailable`) | Yes | Frontend `certainlyNotSent` → true, which is correct (never published); an audit row is written for each refusal |
| TTL ≤ 10 s for every command (was B only); const renamed `PhysicalCommandTtlMs` → `MaxCommandTtlMs` | Yes | Public const rename inside the solution; doc `:133` stale; skew risk M2 |
| No `command_result` for class R (reply and timeout) | Yes | Breaking for any external SignalR client; none in repo |
| Reply data hidden when the sender has lost the action permission, or the device is revoked (non-admin) | Yes (revoked-device effect not called out) | A domain-mapped action (`config.*`) resolves to null with the empty payload, so its reply is always hidden for non-admins. Harmless while those stay closed; revisit when they open |
| Index `IX_command_audits_Status` | Yes | Matches the startup sweep (`EfCommandAuditStore.cs:53,60`); designer and snapshot consistent; `Down` drops it |

### (d) Races

- **Registry:**
  - `TryRegisterRead` counts and inserts under `_readGate`.
  - `Register` (non-read), `Remove` and `TakeExpired` run outside the lock but cannot add reads, so a concurrent removal can only make the count too high: safe.
  - `PublishUnconfirmed` keeps the slot (correct per R4); NeverSent codes release it.
  - Fairness and starvation: M1/H1.
- **`useDeviceReads`:**
  - The `reading` guard prevents overlap.
  - `cancel` aborts both the poll sleep and the retry sleep.
  - The `finally` only touches `reading` states, which belong to this run because a new run cannot start until `reading` is cleared.
  - `onScopeDispose(cancel)` covers unmount; `deviceId()` comparisons drop late results.
  - The in-flight POST itself is not abortable (`runDeviceCommand` does not pass the signal to `sendCommand`), which is acceptable.
- **Route watch:**
  - `rotate()` aborts the old load.
  - `leaveDevice(previous)` is fire-and-forget; if the hub is still connecting it throws `SIGNALR_NOT_CONNECTED`, which is swallowed.
  - A second `joinRealtime` while the first is connecting starts a new connection (the first is stopped). Both chains then join the current `deviceId.value`, so the old device is not left joined.
  - Remaining: realtimeStatus cosmetic issue (L10); relay panel leak (M3).

### (e) Frontend versus spec, XSS, leaks

- **Spec shapes:**
  - `adc.get_channels`/`get_values` are arrays of `{ai,...}` (§6.2). The card accepts an array or an object wrapping an array, joins on `ai`, and keeps channels present on one side only.
  - It uses `get_values` + `get_channels`, not `get_status` (which has no `ai`). Correct.
  - Edge cases: L6/L7.
- **Arrays versus objects:** `ReadFieldList` prints non-object data as raw JSON; objects get labelled fields plus "Khác".
- **`available:false`:** handled in `ReadSection.vue:15-20`, ahead of the body, including the bodyless ADC sections.
- **XSS:** no `v-html` or `innerHTML` in the new or changed code; all values go through `{{ }}`.
- **Leaks:** each card's 15 s interval is cleared in `onUnmounted`. Abort listeners added per sleep are never removed, but they die with the per-run controller (same pattern as `useDeviceCommand`).
- **Accessibility:** L9.

### (f) Patterns, dead code, comments

- Follows the repo's patterns: controller DTO records, `[FromServices]`, `no-store`, `runDeviceCommand`, the shared `Button` primitive.
- No dead code found. `formatReadTime` is shared correctly with the relay panel.
- No plan IDs or finding codes in code, comments or test names.
- Comments explain invariants, except the premature "checked on the lab gateway" claim (L4).

## Positive observations (risk calibration only)

- `TryRegisterRead` keeps the count and the insert atomic, and only reads take the lock, so relay registration has no new contention.
- The `command_result` suppression sits in `PublishResultAsync`, so reply and sweep cannot diverge.

## Recommended actions (priority order)

1. H1: exempt or reserve a slot for `control.get_status`; back off in the relay panel's read-back on `DEVICE_READ_BUSY`; add a lane test.
2. M3/M4: complete the device-switch reset (telemetryData, commandHistory, relay panel device capture); drop `'2 moments ago'`.
3. M6: add positive controls to the reply re-check and `allowed-commands` tests; restore the realtime "no data" assertion for write results.
4. M5: align the `GET commands/{id}` scope with the send rule (or document it), and record the membership-only rule as phase-1 required.
5. M1/M2: decide on a per-user share or rate limit and on skew handling (user decision for M2).
6. L1-L14 as convenient; L4 and L12 before commit and in phase 3.

## Metrics

- Type coverage / test coverage / lint: not measured (read-only review; tester agent runs `dotnet test`, `vitest`, `vue-tsc`).

## Unresolved questions

- M2: keep 10 s for reads, accepting the loss of skew tolerance, or allow up to about 20 s for reads?
- M5: should a membership-only user be able to read back their own command results (align GET with POST), or should POST become organization-only?

Status: DONE_WITH_CONCERNS
Summary: No Critical issues. The read cap also throttles the relay panel's `control.get_status` verification read (High), and the device-switch reset, membership-only scope split, TTL clock-skew trade-off and several thin tests need follow-up before commit.
