# Code review: remote config writes (System & ADC)

Date: 2026-11-06. Reviewer: code-reviewer (read-only).
Scope: the uncommitted change set on `claude/mqtt-device-connection-36f705` (`git diff` + untracked), against
`plan.md` and phases 01-03, with the device contract from the pinned spec `mqtt-api-83e93d77e.md` (§0, §2, §3, §5,
§6.1, §6.2). No firmware source was opened.

## Scope

- **Server:** `PinnedCommandCatalog`, `ConfigCommandPolicy`, `CommandPayloadRules`, `OutputCommandPolicy`,
  `HmsIdfCommandProtocolAdapter`, `CommandLanes`, `SendDeviceCommandHandler`, `InMemoryPendingDeviceCommandRegistry`,
  `DeviceCommandResponseCorrelator`, `MqttDeviceCommandTransport`, `EfCommandAuditStore`.
- **Frontend:** `useDeviceCommand`, `useDeviceWrite`, `useDeviceReads`, `DeviceReadCard`, `configGate`,
  `configRules`, `configWriteSpecs`, `useConfigWrite`, `adcData`, `ConfigWriteConfirm`, `ConfigWriteResult`,
  `DeviceTimeConfig`, `AdcChannelTable`, the two cards, `DeviceDetailView`, `DeviceCommandModal`,
  `DeviceOutputsPanel`.
- **Tools:** `tools/mqtt-broker/spec-device-simulator.js`, `tools/device-config-check.js`,
  `reports/affected-roles.md`.
- **Size:** 29 tracked files changed (+1360/-131), 23 untracked files.

**The tree changed during the review.** `ConfigCommandPolicy.cs` (22:15), `CommandPayloadRules.cs` (22:17) and
`ConfigCommandPolicyTests.cs` (22:15, 22:17) were edited by another session while this review ran: a lone-surrogate
guard for channel names and its tests. A scratch build taken mid-edit had 3 failing tests. The gate results below
come from a second snapshot taken after five minutes without changes (22:22).

## Gates (scratch copy, the repo's bin/obj untouched)

| Check | Result |
|---|---|
| `dotnet build Hms.Server.slnx --no-incremental` | 0 errors, 3 warnings, all pre-existing: `GrantSerializationTests.cs(33)` and `(68)` xUnit1031, `ManagementApiTests.cs(356)` CS8631. That assertion was already at line 355 on HEAD. |
| `dotnet test` | Unit 561/561 passed; integration 116/116 passed. |
| `npx vitest run` (frontend/) | 36 files, 314 tests passed. |
| `npx vue-tsc --noEmit` | Exit 0. |

## (a) Plan acceptance ("Acceptance (toàn plan)")

| Criterion | Status | Evidence |
|---|---|---|
| Exactly the 5 writes opened, 33 open in total | MET | `PinnedCommandCatalog.cs:98-106`, `:240`; `CatalogTests.cs:155` (`Assert.Equal(33, ...)`) |
| Every other W/C/D/F row stays closed, with tests for set_ntp_servers, set_time, reboot, factory_reset, config.set, network.\*, mqtt.\*, broker.\*, modbus.\*, ota.\* | MET | `CatalogTests.cs:164-187`; all D/F rows `:185-186` |
| A closed command sent by a role that holds its permission returns `DENIED_UNTIL_ACTION_CONTRACT` | MET | `ManagementApiTests.cs:360-404` (set_ntp_servers, set_time over HTTP) |
| An invalid payload is refused before signing (`COMMAND_PAYLOAD_INVALID`), so nothing is published | MET | `HmsIdfCommandProtocolAdapter.cs:62-64`, before `secrets.Find` at `:67`; `ConfigCommandPolicyTests` adapter test (key never read); `ManagementApiTests.cs:404` |
| A W/C/D/F action without its own rule returns `CONFIG_ACTION_UNSUPPORTED` | MET | `ConfigCommandPolicy.cs` `_ => null` arm; `ConfigCommandPolicyTests` `Writes_without_a_payload_rule_fail_closed` |
| At most 1 pending write per device; the second gets `DEVICE_WRITE_BUSY` | MET | `DeviceLinkContracts.cs:324-347`; `DeviceLaneSafetyTests.cs:34`, `:153` (burst) |
| At most 10 writes per minute per device; the extra one gets `DEVICE_WRITE_RATE_LIMITED` | MET | `DeviceLinkContracts.cs:338-344`; `DeviceLaneSafetyTests.cs:115` |
| The slot is held until a reply arrives, or until max(Deadline, ExpiresAt + 2 s) | MET | Pending until reply or sweep; `TakeExpired` holds until `ExpiresAt+2` (`:375`); `DeviceLaneSafetyTests.cs:83`. The phase-01 text disagrees (see L-8). |
| A job-still-running reply holds the lane 60 s more | MET | `DeviceLinkHandlers.cs:83-84`; `DeviceLinkTests` `A_config_write_the_device_is_still_running_keeps_the_write_lane_closed` |
| The read lane and relay commands are unaffected | MET | `MqttDeviceCommandTransport.cs:67-90`; `DeviceLaneSafetyTests.cs:34-61`, `:66` |
| W/C use server time for RequestedAt | MET | `SendDeviceCommandHandler.cs:134-135`; `DeviceLaneSafetyTests.cs:172` |
| Audit keeps the payload of the 5 opened writes; other W/C keep null | MET | `EfCommandAuditStore.cs:221-235`; `PersistenceModelTests` new test |
| A permission refusal is HTTP 200 + `Failed`/`DEVICE_PERMISSION_REQUIRED` | MET | `ManagementApiTests.cs:402-403` |
| `allowed-commands` lists only what the caller may send | MET | `ManagementApiTests.cs` editor/viewer action lists |
| A reply is hidden once the permission is lost | MET by code only | `DeviceCommandsController.cs:99-102` (generic). No write-reply test; phase-01 test 8 is missing (L-6). |
| UI: edits only when the matching read succeeded within 60 s | PARTLY | `configGate.ts:51`; the 15 s `now` tick allows up to 75 s (L-1); the confirm step does not re-check (M-1) |
| UI: confirm box shows old → new | MET | `ConfigWriteConfirm.vue`; `configWriteSpecs.ts` `confirm` |
| UI: reads and writes of the card are locked during a write | MET | `DeviceReadCard.vue` `lockReads`; `configGate.ts:41`; `DeviceReadCard.test.ts:177` |
| UI: only `certainlyNotSent` or HTTP 4xx count as "not sent"; everything else reads back | MET | `useDeviceCommand.ts:61-66`; `useDeviceWrite.ts:150-163`; `useConfigWrite.ts:145` |
| UI: read-back compares the whole record; clock skew over 30 s warns | MET | `configWriteSpecs.ts` `verifyChannel`; `useConfigWrite.ts:87-93` |
| UI: a config `command_result` from someone else marks the card stale | MET, with gaps | `DeviceDetailView.vue` `noteConfigChange`; it does not stop an open confirm box (M-1) and has no reconnect handling (L-4) |
| UI: the C2 modal hides the 5 writes | MET | `DeviceCommandModal.vue:68-72`; test `:187` |
| Real gateway: 5 writes Executed or job success; read-back shows the new value; restore brings the original back; final state equals the original; sniffer shows only scripted commands | NOT MET | Phase 3 live run not done; no `reports/live-verification.md` |
| Tests: all .NET and frontend green; `vue-tsc` clean | MET | Gates above, on the 22:22 snapshot |
| Phase-3 docs (`docs/architecture-mqtt-device-link.md`, catalog doc, journal) | NOT MET | No `docs/` file changed |

## (b) Regression walk

- **Relay DO flow:**
  - `DeviceOutputsPanel.vue:190` and `:201` (`mayHaveRun`) are equivalent to the old `certainlyNotSent` and
    `status < 500` checks inside the `not_sent` branch and the catch;
  - class Physical still takes `Register` (`MqttDeviceCommandTransport.cs:87-90`) and is outside
    `CommandLanes.IsWrite`;
  - `OutputCommandPolicy` behaviour is unchanged (delegating wrappers).
- **Read lane:** `TryRegisterRead` is unchanged apart from the lock name. It still counts only class Read and keeps the
  reserved `control.get_status` slot (`:72-74`). The write lane does not count reads and the read lane does not count
  writes.
- **Audit:** recording paths are unchanged. `SummarizePayload` widens to the 5 actions and stays truncated at 512 chars.
- **Realtime:** `PublishResultAsync` is unchanged. The page now consumes `command_result` (it was ignored before).
- **allowed-commands and GET reply re-check:** code unchanged; it now covers the writes.
- **Registry callers:**
  - `TakeExpired` is used only by `SweepExpiredAsync`;
  - `Remove` is used by the correlator and by the transport's NeverSent path;
  - `IPendingDeviceCommandRegistry` has one implementation (`DependencyInjection.cs:38`);
  - there is one production protocol adapter (`DependencyInjection.cs:35`), so no adapter skips the policy.

## (c) Contract changes

No unintended change was found. Intended changes, verified:
- catalog availability of the 5 writes;
- three new codes;
- D/F refused by the policy;
- `/me` catalog;
- `RequestedAt` ignored for write-lane classes, so `COMMAND_TIMESTAMP_IN_FUTURE` and `COMMAND_EXPIRED` can no longer
  occur for them.

The idempotency fingerprint excludes `RequestedAt` (`CommandContracts.cs:166-179`), so the server-time override does
not break retries.

Additive public surface: `IPendingDeviceCommandRegistry.TryRegisterWrite`/`HoldWriteLane`, `DeviceLaneCodes`,
`MqttDeviceCommandTransport.WriteBusy`/`WriteRateLimited`/`MaxWritesPerMinute`,
`DeviceCommandResponseCorrelator.RunningJobWriteHold`.

No env or config key was added. No EF model change.

## Critical

None found.

## High

None found.

## Medium

### M-1. The confirm step re-checks nothing, so a stale or superseded edit is sent and reported as confirmed

- **Where:**
  - `frontend/src/features/devices/components/reads/useConfigWrite.ts:133-141`: `confirm()` sends `pending.value`
    with no gate check;
  - `DeviceTimeConfig.vue:170-175` and `AdcChannelTable.vue:147-152`: the confirm box stays mounted whatever the gate
    says;
  - `configWriteSpecs.ts:90-98`: `adcRenameSpec` freezes `before.type`/`before.enabled` at request time and verifies
    against `{ ...before, name }`.
- **What happens:** the gate (`configGate.ts`: 60 s freshness, `configChangedAt`, offline, permissions) runs only when
  the confirm box opens (`submitRename`/`submitHostname`/`toggle`). The box can then wait indefinitely. Reads are not
  locked while it is open (`lockReads(true)` happens only inside `confirm()`).
- **Failure scenario:**
  1. Operator A reads the ADC card, clicks rename on AI-3 and leaves the confirm box open.
  2. Operator B disables AI-3. A's page receives `command_result`, so the "Cấu hình vừa đổi" banner appears and the gate
     closes, but the box stays.
  3. A presses "Đọc"; the table now shows AI-3 disabled while the box still holds the old record. A presses
     "Xác nhận".
  4. The server receives `adc.set_channel {ai:3, name, type, enabled:true}` (`configWriteSpecs.ts:93`) and AI-3 is
     re-enabled. The re-read matches `{...before, name}`, so the UI says "Đã ghi và xác nhận" while it has silently
     undone B's change.
- **Same root, other forms:**
  - confirming 10 minutes after the last read;
  - confirming after the link went offline;
  - confirming after the catalog lost its verification.
- **Fix:** in `confirm()`, re-evaluate the control's gate and drop the pending spec when it is closed. Also rebuild
  `before` from the current `states` and refuse if it changed: compare `channelRecord(...)` with the spec's `before`,
  and the hostname/timezone with `confirm.from`. Simplest form: store the gate getter and the source record in the
  `WriteSpec`, and dismiss the pending spec from a `watch` on the gate going false. Add a test: open the confirm,
  then set `configChangedAt`, then press confirm, and expect no `sendCommand`.

### M-2. The live tool cannot restore after any write the device answers as a job: the server's 60 s hold refuses the restore write

- **Where:**
  - `tools/device-config-check.js:101-113`: `followJob` returns as soon as the job is terminal;
  - `:26`, `:126-131`: only 7 s spacing between writes;
  - `:65-69`, `:77-78`: `DEVICE_WRITE_BUSY` (ContractUnavailable) is classified `not_sent`;
  - `:242-250`: the restore write is sent once, then the run stops with "the original value did not come back";
  - server: `DeviceLinkHandlers.cs:83-84` holds the lane until `receivedAt + 60 s` for every Acknowledged write;
    `DeviceLinkContracts.cs:334` refuses while the hold lasts.
- **Failure scenario (deterministic with the plan's own `SIM_ADC_JOB=1`, and on the real gateway whenever it answers
  `accepted/pending`, spec §5):**
  1. `adc.set_channel` "AI-1" → "AI-1-t" is Acknowledged; the server holds the lane until about t+60 s.
  2. The job is terminal at t+10 s; the read-back shows "AI-1-t".
  3. The restore write goes out at about t+12 s, gets `DEVICE_WRITE_BUSY`, and the read shows "AI-1-t". The run stops.
  4. The final snapshot differs: the device is left on the test value with "NOT RESTORED" and a `--restore` hint.
     Running `--restore` within 60 s fails the same way.
- **Impact on the plan:** phase-3 success criterion "UI và tool chạy đúng với simulator ở cả 4 chế độ" cannot pass,
  and the tool contradicts its own `--plan` text ("any surprise ... undoes what changed").
- **Fix:** when a forward write ended through a job, wait out `RunningJobWriteHold` (60 s from the reply, plus a
  margin) before the restore write. Alternatively, on `DEVICE_WRITE_BUSY` for a restore write only, wait and send it
  once more: a lane refusal proves nothing was published, so this is not a blind resend. Apply the same in
  `restore()`.

## Low

### L-1. The freshness window is 60-75 s, not 60 s

- **Where:** `DeviceReadCard.vue:60` updates `now` every 15 s; `configGate.ts:51` compares `input.now - readAt`
  against 60 s.
- **What happens:** a read finished just after a tick keeps the control enabled until the next tick after T+60 s, so
  up to about T+75 s.
- **Fix:** compute freshness against `Date.now()` in the gate, or tick every 1-5 s while a control is enabled.

### L-2. ADC names may consist of invisible characters that are not Cf

- **Where:** server `ConfigCommandPolicy.cs:85-92`; client `configRules.ts:14`.
- **Probe (.NET 10):** these characters pass both rules and are not whitespace:
  - U+3164 HANGUL FILLER and U+115F: OtherLetter;
  - U+2800 BRAILLE PATTERN BLANK: OtherSymbol;
  - U+034F and U+FE0F: NonSpacingMark.
- **Scenario:** a name of "ㅤ", or "AI-1" with a trailing U+3164, renders blank or identical to another channel.
  The plan's F12 aimed at exactly this.
- **Fix:** also reject the Default_Ignorable_Code_Point set (a short explicit list: U+115F, U+1160, U+3164, U+FFA0,
  U+034F, U+180B-U+180F, U+FE00-U+FE0F, U+2800), and require at least one L or N rune outside it. Keep the client
  mirror in step.

### L-3. The tool does not pre-check that the original values are writable before it changes them

- **Where:** `device-config-check.js:153-203`. Timezone is pre-checked (`:169`); hostname and ADC name are not.
- **Scenario:** if the original channel name is empty, whitespace, over 32 bytes, or holds a character the policy
  refuses, the tool still writes the test value (`:181-182` truncates it to a valid one). The restore is then refused
  `COMMAND_PAYLOAD_INVALID` and the device keeps the test name. The same happens for a hostname over 63 bytes.
- **Fix:** skip a field whose original value fails the server rule, as is already done for timezone. The lab
  fixture's values (`AI-1`, `maitech-rtu`, `ICT-7`) pass, so the impact is on other devices.

### L-4. The hostname safety skip fails open, and a realtime gap leaves config cards unmarked

- **Hostname skip:**
  - Where: `device-config-check.js:147`, `:159`.
  - What happens: `mqttClientIdSet` is null when `mqtt.get_config` is unreadable or omits `client_id`, and only
    `false` skips the step. Phase 3 says to skip when `client_id` is empty, so unknown should skip too.
- **Realtime gap:**
  - Where: `DeviceDetailView.vue:332`.
  - What happens: a reconnect refreshes outputs only. `command_result` events lost during a reconnect never set
    `configChangedAt`, so only the 60-75 s freshness bounds a stale edit.
  - Fix: set both `*ConfigChangedAt` to `Date.now()` on reconnect.

### L-5. The write-lane hold assumes the device clock is within 2 s of the server

- **Where:** `DeviceLinkContracts.cs:375` holds until `ExpiresAt + 2 s` after the sweep.
- **What happens:** spec §3 accepts a `ts` up to 60 s ahead of the device clock and checks the TTL against device time
  at dequeue. A device 40 s behind the server can therefore run a write until about server time ts+60 s, while the
  lane reopens at the 30 s sweep.
- **Why Low:** it needs a backed-up queue plus a skewed clock, and the UI warns about skew only after a time write.
- **Fix:** document it next to the residual risks, or widen the hold to the 60 s future window.

### L-6. Test gaps against phase-01 test 8 and the reboot assertion

- **Write reply after revocation:** no test that a write reply is hidden after the role is revoked. It is covered
  only generically.
- **Confirm token in the signed wire:** `DeviceLinkTests.cs:126-134` now proves the token by calling the codec
  directly. Removing `confirm` from `HmsIdfCommandProtocolAdapter.cs:82` would no longer fail any test.
- **Audit code for a busy write:** no assertion that the audit row carries `DEVICE_WRITE_BUSY` (phase-01 test 4).

### L-7. The tool and the UI read `system.get_operations` differently

- **Where:** `useDeviceWrite.ts:64-67` unwraps a reply nested one level deeper; `device-config-check.js:105-110`
  does not.
- **What happens:** if the gateway nests this reply as it nests `ota.get_status`, the tool never sees `state` and
  reports `JOB_TIMEOUT`.

### L-8. Plan text and code disagree on lane release after a reply, and on the refusal detail language

- **Lane release:**
  - phase-01 says a removed write keeps blocking until max(Deadline, ExpiresAt + 2 s) ("lệnh đã Remove nhưng chưa
    qua ExpiresAt + 2 s → lệnh sau vẫn bị chặn");
  - the code frees the lane on reply (`DeviceLaneSafetyTests.cs:58-61`, `DeviceLinkTests`
    `..._finished_frees_the_write_lane_at_once`);
  - the code matches plan.md acceptance and is the safer reading (a reply proves it ran once), so fix the phase text.
- **Refusal detail:** the plan asks for Vietnamese detail; it is English (`MqttDeviceCommandTransport.cs:82-85`), like
  `ReadBusy`.
- **Rate tokens:** a write that never left the server (NeverSent) still uses a rate token (`DeviceLinkContracts.cs:344`
  enqueues before publish), against "10 lệnh ghi được gửi mỗi phút".

## Edge cases checked and found sound

- **Registry locking:**
  - `TryRegisterRead`, `TryRegisterWrite`, `HoldWriteLane` and `TakeExpired` share `_laneGate`;
  - `Register` (unlocked) is used only for Physical, which the write lane ignores;
  - `Remove` (unlocked) can only free the lane;
  - the correlator holds before `Remove`, so nothing slips in between;
  - a sweep racing a reply leaves the larger hold;
  - the burst test is real concurrency (`Task.Run`).
- **No automatic resend of a write anywhere:**
  - `adminApi.request` has no retry;
  - `runDeviceWrite` sends once;
  - `useDeviceReads` retries only reads;
  - the tool's `write()` sends once.
- **Payload forms:**
  - `1.0`, `1e0`, `"1"` and `-0` are refused for `ai`;
  - `1` and `0` are refused for `enabled`;
  - case-variant duplicate keys are refused by `HasExactly`/`HasKeys`;
  - `\A...\z` anchors are used on the server, and JS `$` without `m` on the client;
  - NUL, CR/LF, bidi and zero-width characters are refused;
  - the codec writes raw UTF-8 (`UnsafeRelaxedJsonEscaping`), so a 32-byte name stays 32 bytes on the wire.
- **Device switch mid-write:**
  - `useConfigWrite` resets on `deviceId`;
  - `useDeviceWrite.run` returns null;
  - `DeviceReadCard` clears `locked`;
  - the server lane keeps the old device protected.
- **Own-write detection:** the correlation id is remembered before the POST; the realtime echo and a later sweep
  timeout are both skipped; another tab of the same user is treated as "someone else", which is correct.
- **Pre-existing, not introduced here:** a payload carrying a lone-surrogate escape (`"\uD800"`) still returns HTTP
  500 at `CommandRequestFingerprint.Compute` (`CommandContracts.cs:170`, STJ JsonException, probed). That happens before
  any reservation, so the concurrent `TryString` guard does not change the HTTP outcome. Its test comment about a
  half-written audit row describes the adapter path only.

## Recommended actions (priority order)

1. M-1: gate re-check and record re-check inside `confirm()`, plus a test.
2. M-2: make the tool wait out the job hold before restore and `--restore` writes; re-run the tool against the
   simulator with `SIM_ADC_JOB=1` before any gateway run.
3. L-3 and L-4 (tool pre-checks) before the live run on `gateway_870c`.
4. L-1, L-2, L-6 (gate tick, name rule, missing tests).
5. Phase 3: live run, `reports/live-verification.md`, docs, journal; then fix the phase-01 text (L-8).

## Plan follow-ups (for the lead; plan files not edited)

- **Phase 1:** functionally complete. Remaining: L-6 tests; fix the phase text on lane release (L-8).
- **Phase 2:** complete apart from M-1 and L-1.
- **Phase 3:** simulator, tool and `affected-roles.md` exist. Still to do:
  - fix M-2, L-3 and L-4 before the gateway run;
  - the gateway run itself, plus the sniffer count and the final-state compare;
  - docs and journal.
