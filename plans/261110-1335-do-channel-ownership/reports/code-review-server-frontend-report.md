# Code review: DO channel ownership, phases 1-2 (server + frontend)

Date: 2026-11-10. Reviewer: code-reviewer. Scope: `git diff -- src tests frontend` plus the untracked
`frontend/src/core/commands/outputLocks.ts` and `outputLocks.test.ts`. Contract: HMS-IDF `mqtt-api.md` at HEAD
(L797, L816-834, L839, L853, L870-885), read through `git show` only.

## Scope

- Server: `DeviceOutputState.cs`, `DeviceLinkController.cs`, `DeviceOutputStateTests.cs`, `ManagementApiTests.cs`.
- Frontend: `adminApi.ts`, `outputLocks.ts` (new), `rebootPlan.ts`, `useDeviceReboot.ts`, `DeviceOutputsPanel.vue`,
  `RebootConfirmBody.vue`, `RebootResult.vue`, and their tests.
- About 570 added lines and 36 removed. No reviewed file changed while the review ran (mtime checked at the end).

## Gates (run by the reviewer)

| Gate | Where | Result |
|---|---|---|
| `dotnet build Hms.Server.slnx --no-incremental` | scratch copy of src/tests | 0 errors, 3 warnings, all from before this change (CS8631 at `ManagementApiTests.cs:357`, 2x xUnit1031) |
| Unit tests | scratch | 658/658 pass (baseline 640) |
| Integration tests, `Mqtt__Enabled=false` | scratch | 127/127 pass (baseline 126) |
| `npx vue-tsc --noEmit` | repo `frontend/` | clean |
| `npx vitest run` | scratch copy with node_modules junction | 41 files, 528/528 pass (baseline 475) |
| `npx vite build` | scratch, output dir outside the repo | OK |
| `command-catalog.json` | `git status` and `git diff --stat src/Hms.Server.Infrastructure/Commands/` | no change; `CatalogTests` green, not edited |

## Overall assessment

The change does what the plan says. The server keeps the 5 fields without inferring anything. `GET /outputs` only
adds fields, and an integration test checks the exact property set. The panel lock only adds to the existing
`channelBusy` lock (an OR), so it can never unlock a channel that the R4 read-back is holding. `ToResult`, `relaysMatch`,
`BUSY_DETAIL` and the existing R4 tests have no diff. I found no Critical or High issue. There is one Medium defect,
a wrong device code shown next to an unrelated message, confirmed by a probe. The four Low items are inference or
robustness edges.

## Critical

None.

## High

None.

## Medium

### M1. A translated refusal's device code stays next to a later, unrelated pulse-duration error
- **Where:** `frontend/src/features/devices/components/DeviceOutputsPanel.vue:249` (the pulse-duration check in
  `pulse()` writes `channelError[channel]` and returns, but does not clear `channelErrorCode[channel]`). The code is
  rendered at `:403`.
- **Failure scenario (probe, scratch vitest):** DO6 is in pulse mode and the device does not report `forced`/`owner`
  (older firmware, so the channel is not locked). Pulse → the device answers `channel_forced` → the panel shows
  `Đang force: thiết bị không đổi relay (DEVICE_REJECTED: ESP_ERR_INVALID_STATE: channel_forced)`. The operator then types `0` ms and
  presses Xung again. The panel now shows
  `DO6: Thời gian xung phải là số nguyên từ 1 đến 60000 ms (DEVICE_REJECTED: ESP_ERR_INVALID_STATE: channel_forced)`.
  The local validation message is displayed as if the device had refused it. This breaks the acceptance item
  "lỗi khác giữ chữ cũ".
- **Why it is reachable:** the plan rates "firmware 1.0.108 does not send `owner`" as High likelihood. Then nothing
  locks the pulse controls, and the path is open.
- **Fix:** keep the two maps paired at every write site. In the validation branch:
  ```ts
  delete channelErrorCode[channel]
  channelError[channel] = `Thời gian xung phải là số nguyên từ 1 đến ${MAX_PULSE_MS} ms`
  ```
  Alternatively, store `{ text, code }` in a single `channelError` record so the two cannot drift apart. Add a vitest:
  refused pulse, then an invalid duration, then expect no `do-6-error-code`.

## Low

### L1. The reboot result names the earlier level rule even when the reading after the reboot names a different owner
- **Where:** `frontend/src/core/commands/rebootPlan.ts:120-124`.
- **Probe output:** before the reboot, DO6 is `level_rule` #7. After the reboot, DO6 is ON with:
  - owner `none`: the note says "DO6 BẬT sau khởi động do luật #7", although the device says no rule holds the channel.
  - owner `event_rule`: the note says "do luật #7", although the device names an event rule.
  - owner `trigger` with `owner_id: 3` (off-spec, since L797 says `owner_id` is null unless `level_rule`): the note says
    "do luật #3", so a trigger id is shown as a rule id.
- **Impact:** the note is informational only, never a deviation. It is still a claim the device's latest reading
  contradicts. Decision 10 says "theo lần đọc sau, hoặc theo danh sách trước", and the code follows that literally,
  so this is a refinement, not a plan breach.
- **Fix:** use the earlier list only when the device did not report an owner after the boot, and read the id only
  from a `level_rule` row:
  ```ts
  const ruled = ch.owner === 'level_rule' || (ch.owner === null && held)
  const id = (ch.owner === 'level_rule' ? ch.ownerId : null) ?? held?.ruleId
  ```

### L2. A malformed string escape in `owner` or `owner_id` drops the whole status projection
- **Where:** `src/Hms.Server.Application/DeviceLink/DeviceOutputState.cs:211` (`GetString()` in `OwnerOf`) and `:221`
  (`RuleIdOf`).
- **Probe (scratch unit test):** a signed reply whose row has `"owner":"\uD800"` or `"owner_id":"\uD800"`.
  `GetString()` throws `InvalidOperationException`, the correlator catches it (`DeviceLinkHandlers.cs:98-105`),
  and no state is stored at all (`Outputs.Get` returned null). The panel's read-back still sees `Executed` and reloads
  the old state, which may be stale.
- **Context:** `mode` already behaves this way (same probe), and the reply is signed by the device. So this is not a
  regression in kind, but it adds two more fields to that failure mode. It also contradicts the phase-1 risk note
  "Hàm parse không ném" ("the parse function does not throw").
- **Fix (optional):** catch the throw per field so one bad field cannot discard the other channels, for example
  `try { text = value.GetString(); } catch (InvalidOperationException) { text = null; }`, which makes `OwnerOf` return
  `unknown` and `RuleIdOf` return `null`. For `OwnerOf`, matching against `Known` with `value.ValueEquals(k)` would also
  avoid materializing the string. Add a theory case with a lone surrogate.

### L3. A numeric `owner_id` can be displayed differently on the panel and in the reboot dialog
- **Where:** the server's `DeviceOutputState.cs:222` (`GetRawText()`) and the reboot path's `rebootPlan.ts:74`
  (`String(n)`).
- **Probe:** `7.0` becomes `"7.0"` on the server (panel: "Luật #7.0 giữ kênh") but `"7"` in the reboot dialog.
  `1e2` becomes `"1e2"` on the server but `"100"` in the reboot dialog. Plain integers (`7`, `-1`) agree.
- **Impact:** cosmetic. The type of `owner_id` is still spec question 19. Leave it as is, or normalize the server side
  to a decimal integer when `TryGetInt64` succeeds. Not blocking.

### L4. Test gaps and one assertion that cannot fail
- `DeviceOutputsPanel.test.ts:383` (`expect(send).not.toHaveBeenCalled()` after clicking locked buttons): Vue Test
  Utils `trigger` does nothing on a disabled element, so this assertion only repeats the `disabled` checks above it.
  It is harmless; the `disabled` assertions are the real proof.
- No panel test covers `enabled:false`, which is covered only in `outputLocks.test.ts`, or a `level_rule` lock while the
  state is stale (`keeps the lock while the state is unknown` uses only `gpioOk:false`).
- No test covers a translated refusal followed by a failed read-back. That path shows
  `<translation>. Chưa đọc lại được… (DEVICE_REJECTED: …)`, and a refused pulse then skips the second read. I traced it
  by hand and it is correct, but it is untested.

## Explicit checks requested

### (a) Acceptance criteria of phases 1-2, each with a test
| Criterion | Proof |
|---|---|
| Reply with every field → full projection | `A_status_reply_keeps_what_the_device_says_about_each_channel` (record equality on DO1, DO6) |
| Missing field → null | `Channel_fields_the_device_did_not_send_stay_null` |
| Unrecognized `owner`, `"LEVEL_RULE"`, number, bool → `"unknown"`; JSON null → null; 6 known values kept | `An_owner_outside_the_spec_list_becomes_unknown` (11 cases) |
| `owner_id` number → text, string kept, null/bool → null | `A_rule_id_is_kept_as_text_…` (4 cases) |
| Wrong type does not break the reply | `A_channel_field_of_another_type_is_dropped_and_the_channel_is_kept` |
| `GET /outputs`: exactly 9 channel properties, nulls, outer shape | `Output_state_adds_the_channel_fields_and_keeps_the_old_ones` |
| Lock order gpio → config → force → rule, plus reasons | `outputLocks.test.ts` (`locks %j`, `takes the reasons in the order …`) |
| No owner or `unknown` → no owner lock; `event_rule`/`trigger` → note only | panel tests `locks nothing by owner…`, `does not lock on an unknown owner`, `notes that a rule or trigger…` |
| Lock applies while the state is stale | `keeps the lock while the state is unknown` |
| Pulse locked on `level_rule` | `locks each control…` (`do-6-pulse`, `do-6-pulse-ms` disabled) |
| 4 refusals translated + raw code in mono; other refusals keep their raw text | `translates %s…`, `explains a state refusal…`, `keeps the raw text…`; M1 is the exception |
| `owned_by_rule`: one write, one read, lock appears afterwards | `explains a state refusal in words…` (action list `['set_output','get_status']` after 12.5 s) |
| Refused pulse skips the read after the pulse | `reads back once after a refused pulse…`; a successful pulse still reads twice (existing test at `:327-344`) |
| Reboot dialog: rule line with id, general line kept, not in "sẽ TẮT", `noneOn` | `names each channel a level rule holds…`, `does not say no relay is on while a rule-held channel is on`, `shows no rule-held line…` |
| Owner change alone does not close the dialog | `stays open when only the owner of a channel changed` |
| Reboot result: "do luật #id", chip "(luật #id)", not a fault | `shows a relay a level rule turned on after the boot…`, plus 2 `checkAfterReboot` tests |

### (b) No regression in the touchpoints
- `ParseChannels`: the whole-reply rejection rule (missing `channel` ≥ 1 or a non-bool `state`) is unchanged, and so
  is its only caller, `ApplyAsync`. Stale marking after `DEVICE_REJECTED` (`DeviceOutputState.cs:131-137`) and
  `ToResult` have no diff. Record equality used by `MarkStale`'s `TryUpdate` compares `Channels` by reference, so the
  new fields do not affect it.
- `GET /outputs`: the permission path is untouched; only the mapping changed.
- R4: `operate` still reads back after every outcome that may have reached the device. `'unchanged'` is returned only
  after `readBackTwice` has run, and it is used only by `pulse` to skip the second read. `setOutput` ignores the
  return value. The existing R4 tests are untouched and green.
- No resend path was added.
- `BUSY_DETAIL` (`useDeviceReboot.ts:75`) and `relaysMatch` (`rebootPlan.ts:85-91`) are byte-for-byte unchanged. The
  only edit in `useDeviceReboot.ts` is `NOTHING_BEFORE` gaining `ruleHeld: []`. `DeviceRebootPanel.vue` is unchanged.

### (c) Public contract
`GET /outputs` only adds fields. The integration test pins the 9 channel properties and the 4 outer properties.
`command-catalog.json` is untouched, and the SignalR `outputs` event payload is unchanged (existing test
`DeviceOutputStateTests.cs:50`).

### (d) Patterns
`Flag` mirrors the existing `pulseActive` parse, `OutputOwners` mirrors `OutputStaleReasons`, and the DTO keeps its
single construction site. On the frontend, `outputLocks.ts` sits next to `rebootPlan.ts` and keeps the labels in one
place. The new UI text uses the amber and hint colours this panel already uses for its warnings.

### (e) Build and type errors
None new (see Gates).

### (f) Correctness edge cases
- Owner null vs `"unknown"` vs missing: the server maps missing and JSON null to `null`, and every other non-listed
  value to `"unknown"`. The frontend locks on neither. The reboot path uses the raw string and acts only on
  `level_rule` and `force`, which is fine.
- Wrong JSON types on the server: covered by tests. On the reboot path, `=== true` and `typeof` guards handle them.
- `owner_id` number vs string: fine; see L3 for unusual numeric forms. An empty-string id falls back to "Luật giữ
  kênh" or "do luật theo mức" everywhere, because the code tests truthiness.
- Lock on stale state: implemented as Decision 6 says, and tested.
- Pulse lock on `level_rule`: implemented and tested; the doc comment explains why.
- `stateRefusal` regex vs `ToResult`: the detail is `"{error}: {data.error}"`, or `": {data.error}"` when the envelope
  `error` is null, or the bare envelope `error`. `/(?:^|:\s*)(…)$/` matches all three and rejects `owned_by_rule_x`,
  `busy`, `LANE_FULL` and `UNAUTHORIZED`. The reply is saved before the audit result (`DeviceLinkHandlers.cs:109`), so
  the poll that sees `Failed` also has `reply.data.rule_id`.
- Reboot dialog lines: a rule-held channel is never in `turnOff` and is listed even when OFF. `noneOn` is false while a
  rule-held channel is ON. A forced row wins over `level_rule`. The general SET_HIGH/SET_LOW line always shows. Correct.
- Result chips: `(force)` first, then `(luật #id)` or `(luật)`. Correct. The chip uses only the reading after the
  reboot, as planned.

## Edge cases found while scouting and probing
- M1 (stale code) and L1 (owner attribution) were confirmed with scratch vitest probes. L2 and L3 were confirmed with
  scratch xUnit probes. The probe files were deleted, and the repo was not modified.

## Recommended actions
1. Fix M1: one `delete channelErrorCode[channel]` in the pulse-duration branch, plus a vitest.
2. Optional: L1 (prefer the owner reported after the reboot), L2 (per-field try/catch), L4 (add the `enabled:false`,
   stale `level_rule`, and refusal-plus-failed-read-back tests).
3. L3: leave it until spec question 19 is answered.

## Plan follow-ups
Phases 1 and 2 are complete apart from M1. Phase 3 (simulator, preflight, docs) is outside this review.

## Unresolved questions
None for this code. Spec questions 17-19 in the plan still stand.

Status: DONE_WITH_CONCERNS
Summary: Phases 1-2 meet the acceptance criteria, the regression set and the contract checks; all gates pass (unit 658, integration 127, vitest 528, vue-tsc clean, build 0 errors and 3 old warnings). Findings: 0 Critical, 0 High, 1 Medium, 4 Low.
Concerns/Blockers: M1, a stale device code next to the pulse-duration error, should be fixed before merge (one line plus a test).
