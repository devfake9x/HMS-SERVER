# Full regression: do-channel-ownership (uncommitted)

Date: 2026-11-10. Worktree: `D:/WORKSPACE/PROJECT/HMS-SERVER/.claude/worktrees/ck-project-management-review-dd19d6`.
Scope: server (`src/`, `tests/`) + frontend. `tools/` and docs/plan files not tested (other agent editing). No tests added. No commits.

## Results

| # | Step | Result | Expected / baseline | Time |
|---|---|---|---|---|
| 1 | `dotnet build Hms.Server.slnx` | 0 errors, 0 warnings | 0 errors | 5.5 s |
| 2 | `dotnet test tests/Hms.Server.UnitTests` | **658 passed**, 0 failed, 0 skipped | ≥658 (baseline 640) | 1 s test, 4.5 s wall |
| 3 | `Mqtt__Enabled=false dotnet test tests/Hms.Server.IntegrationTests` | **127 passed**, 0 failed, 0 skipped | ≥127 (baseline 126) | 4 s test, 6.8 s wall |
| 4 | `git diff --exit-code -- src/Hms.Server.Infrastructure/Commands/command-catalog.json` | empty, exit 0 | empty | - |
| 5a | `npx vitest run` (run 1) | **533 passed** in 41 files, 0 failed | 533 | 16.7 s test, 21.4 s wall |
| 5b | `npx vitest run` (run 2) | **533 passed** in 41 files, 0 failed | 533 | not timed |
| 5c | `npx vue-tsc --noEmit` | exit 0, no output | clean | - |
| 5d | `npx vite build` | built, exit 0 | OK | 6.9 s (vite), 9.6 s wall |

Flakiness: vitest passed on both runs with identical counts. .NET run once each (not repeated).

Warnings:
- Server build: 0 warnings. Git CRLF notices for `DeviceOutputState.cs`, `ManagementApiTests.cs`, `DeviceOutputStateTests.cs` are line-ending messages, not build warnings. None in changed files.
- Vite: `/*#__PURE__*/` annotation warnings from `node_modules/@microsoft/signalr`. Third-party, not in changed files.

Build output: `frontend/dist/` is gitignored (`.gitignore:4`). `git status` before and after the build is identical: 22 modified + 4 untracked, no new entries.

Failed tests: none.

## Coverage vs plan acceptance

Mapped against `plans/261110-1335-do-channel-ownership/plan.md` "Acceptance" and "Không hồi quy".

**Server (covered)**
- Full reply → projection; missing fields → null: `DeviceOutputStateTests` `A_status_reply_keeps_what_the_device_says_about_each_channel`, `Channel_fields_the_device_did_not_send_stay_null`.
- Owner outside list, non-string owner → `unknown`; `null` → null: theory `An_owner_outside_the_spec_list_becomes_unknown`.
- `owner_id` number/string → text; bool → null: `A_rule_id_is_kept_as_text_whether_it_comes_as_a_number_or_a_string`.
- Wrong-type field dropped, channel kept: `A_channel_field_of_another_type_is_dropped_and_the_channel_is_kept`.
- Old 4 fields unchanged: asserted in `Channel_fields…`, `ManagementApiTests.Output_state_adds_the_channel_fields_and_keeps_the_old_ones`.
- Refusal keeps/clears stale state: `Only_a_refusal_before_the_handler_keeps_the_output_state`, `Output_state_turns_stale_even_when_the_audit_write_fails`.

**API (covered)**
- `GET /outputs` exposes exactly 9 channel properties, `null` when unknown, outer shape `channels/readAt/stale/staleReason` unchanged: `ManagementApiTests.Output_state_adds_the_channel_fields_and_keeps_the_old_ones`.

**Catalog (covered)**
- Catalog diff empty (step 4). `CatalogTests.cs` lives in the unit project and passed within the 658 total. Not checked by name.

**Panel (covered)**
- Lock per reason, order, gpio/forced/enabled/rule: `DeviceOutputsPanel.test.ts` `locks each control of a channel the device will refuse and says why` (DO1 open, DO2 GPIO, DO4 force, DO6 rule, pulse and ms also locked, no send).
- Stale keeps lock: `keeps the lock while the state is unknown`, `keeps a rule lock while the state is unknown`.
- No owner → no owner lock; unknown owner → no lock; `event_rule`/`trigger` note only: `locks nothing by owner when the device does not report one`, `does not lock on an unknown owner`, `notes that a rule or trigger may change the channel, without locking it`.
- `owned_by_rule`: message `Luật #7 giữ kênh: thiết bị không đổi relay`, raw code text, exactly `['set_output','get_status']`, lock after read-back: `explains a state refusal in words with the device code, reads back once and does not resend`.
- Pulse refused → no read after pulse end: `reads back once after a refused pulse and skips the read after the pulse end`.
- R4 regression: `locks the channel, reads back after the device acts, and only then shows ON`; `reads back even when the device outcome is unknown`; `keeps the channel locked and reads back after %s` (3 cases).
- Busy wait before 2nd read-back: `waits before the second read-back when the first is turned away as busy`.

**Reboot panel and rebootPlan (covered)**
- DO6 named with rule id; general rule sentence kept: `DeviceRebootPanel.test.ts` `names each channel a level rule holds with its rule id, and keeps the general rule warning`.
- No "no relay on" while rule-held channel on; no rule line without owner: `does not say no relay is on while a rule-held channel is on`, `shows no rule-held line when the device does not report owner`.
- Owner change alone does not close box: `stays open when only the owner of a channel changed`. `relaysMatch` unchanged.
- Result "do luật" not a fault: `shows a relay a level rule turned on after the boot as the rule, not as a fault`.
- Confirmation age ≤ 30 s: `sends nothing and reads again when the readings are older than 30 s at confirmation`.
- `rebootPlan.test.ts`: `ruleHeld` split, `owner:"force"` as forced (`treats owner force as forced`), rule-held listing (`lists the channels a level rule holds…`), post-boot rule attribution.

**Retry regression (covered)**
- Writes not resent: `does not resend` test above; `useDeviceReboot.test.ts` busy retry on `ESP_ERR_INVALID_STATE: busy`.
- `BUSY_DETAIL` regex (`useDeviceReboot.ts:75`) unchanged in diff (only `NOTHING_BEFORE` gained `ruleHeld: []`).

## Gaps

[!] Not run, out of scope of this run:
- G1. `node tools/device-reboot-check.js --self-check` PASS (plan acceptance). Not run: `tools/` excluded by instruction. Must run when the tool agent finishes. Expected cases: `ruleHolders` (no owner → null; none/force → empty; level_rule/event_rule/trigger → listed; unknown → ASK); busy regex vs the 4 new reasons.
- G2. Preflight FAIL / ASK / PASS behaviour. Same tool, same reason.
- G3. Simulator E2E and UI walkthrough S1-S4 (plan: "còn lại"). Not run here; no servers started.

Gaps in frontend/server tests (none blocking):
- G4. [low] No vitest asserts that the 4 new reasons (`gpio_unavailable`, `channel_disabled`, `channel_forced`, `owned_by_rule`) do NOT match `BUSY_DETAIL`, so they are not retried. Existing tests only cover the busy forms. Suggested: in `frontend/src/core/commands/useDeviceReboot.test.ts`, a `it.each` with `ESP_ERR_INVALID_STATE: owned_by_rule` etc. expecting a single command. Not added (would change the 533 count mid-run; trivial, so ask if wanted).
- G5. [low] DO3 (`gpioOk:false`) and DO5 (`forced`, `owner:"force"`) are not asserted individually in the panel test; same code path as DO2/DO4. Simulator layout DO6 uses `Luật #1`; test uses `#7`. Simulator E2E covers the real values.
- G6. [low] Raw device code `do-6-error-code` is checked by text only; mono font (plan acceptance) not asserted. Styling, no test convention for it in repo found.
- G7. [info] `CatalogTests` passed inside the 658 total but not verified by name.

## Critical issues

None blocking. All counts meet or exceed expected. Catalog untouched.

## Concerns

- Process slip (fixed): a pre-build status snapshot wrote `D:/WORKSPACE/tmp_status_before.txt` (bad relative path, outside the worktree). Deleted. Also a stray `/tmp/x` created and deleted. Neither is in the repo; `git status` unchanged.

## Recommendations

1. Run G1/G2 after the tool agent finishes; they are the only plan acceptance items not verified here.
2. Add G4 test (regex guard for the 4 new reasons). Cheap, prevents a future retry regression on a refusal.
3. Keep G5/G6 as optional; simulator E2E covers the values.

## Unresolved questions

- Spec Q17 (plan): does `control.pulse` on a `level_rule` channel get `owned_by_rule`? The panel locks Xung on that channel (decision 4). If firmware answers "no", drop that single condition in `outputWriteLock` and its test.
- Spec Q18/Q19 (plan): does firmware 1.0.108 send `owner`/`owner_id`, and what type is `owner_id`? Affects whether lock UI shows in practice. Only the gateway read-only check answers this.

Status: DONE_WITH_CONCERNS
Summary: Build 0 errors/0 warnings. Unit 658/658, integration 127/127 (Mqtt disabled), vitest 533/533 twice, vue-tsc clean, vite build OK, catalog diff empty. No failures; no tests added. Gaps: tool `--self-check`/preflight not run (tools/ excluded), 1 low-priority test gap (retry regex vs 4 new reasons).
Concerns/Blockers: tool self-check still unverified; see G1-G4 above.
