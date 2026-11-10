# Phase 2 implementation report: safe reboot, frontend

Plan: `plans/261109-2250-safe-reboot/` | Phase: `phase-02-reboot-ui.md` | Branch: `claude/mqtt-device-connection-36f705` | Status: DONE (no commits, plan files untouched)

## Baseline (before any change)

- `npm ci` in `frontend/` (node_modules was missing). Vitest: 37 files, 325 passed, 0 failed. `vue-tsc --noEmit`: clean.

## Final

- Vitest: 40 files, 458 passed (325 + 133 new), 0 failed. Run twice, same result.
- `npx vue-tsc --noEmit`: clean (one error found on the way, fixed: `status` possibly null in `useDeviceReboot.ts`).
- `npm run build` (`vue-tsc --noEmit && vite build`): succeeds. `dist/` and `node_modules/` are gitignored.
- `grep confirm_token frontend/src`: only the test that asserts it is absent.

## Tests Before: red, then green

Red runs for the new modules were taken against stubs (types and signatures present, behavior absent), so failures are assertions.

| Item | Test | Red result |
|---|---|---|
| 1 | `rebootPlan.test.ts` (48 cases: `relaysBeforeReboot` incl. force-ON and pulse, `checkAfterReboot`, `isVerifiedReboot`, `rebootGate`, `rebootPhrase`, `rebootMessage`) | 39 failed / 9 passed (the 9 are by-design no-op-correct, e.g. "not readable for garbage") |
| 1+ | `rebootResultText` (3 cases, added, see deviations) | `rebootResultText is not a function` |
| 2 | `useDeviceReboot.test.ts` (usual path x5, dry run x5, real command x5, server record x9, read-back x4, never-resend x7, composable x5 = 39) | 38 failed / 1 passed (the pass is "dry run rejected" send count against a stub). Typical: `expected [] to have a length of 2 but got +0`, `expected 'not_sent' to be 'observed'` |
| 3 | `useDeviceWrite.test.ts`: `followJobs:false` + Acknowledged | `expected 'JOB_POLL_TIMEOUT' to be 'DEVICE_ACCEPTED'` (default-behavior and confirm/dryRun pass-through tests green throughout, by design) |
| 4 | `DeviceRebootPanel.test.ts` (22) | 20 failed / 2 passed against an empty-template stub: `Cannot call trigger on an empty DOMWrapper`; the 2 passes are the "absent" cases |
| 5 | `DangerConfirmDialog.test.ts` (5 new) | slot, `cancelText`/`promptText` (`expected 'Cancel' to be 'Huỷ'`), `beforeConfirm` x2 (`expected "spy" to be called once, but got 0 times`); existing test at :7 green throughout |
| 6 | `DeviceCommandModal.test.ts` (fixture + 6 new) | `expected [ 'telemetry', 'system', 'ota' ] to not include 'ota'`; `expected [ 'new_flash', 'plain' ] to deeply equal [ 'plain' ]`; 3 x `confirm_token` still produced for reboot/factory_reset/ota.apply |
| 7 | `DeviceDetailView.test.ts` (6 new in "the reboot panel") | `expected false to be true` (no panel), `Cannot call props on an empty VueWrapper`, banners `[false,false,false]` vs `[true,false,true]` |
| 8 | `DeviceReadCards.test.ts` (renamed + new string) | `expected 'Đã ghi và xác nhậnHostname hiện tại: …' to contain 'Thiết bị khuyên khởi động lại; master…'` |
| extra | `DeviceOutputsPanel.test.ts`: `device_reboot_scheduled` | `expected 'Trạng thái có thể đã cũ: device_reboo…' to contain 'thiết bị đang khởi động lại'` |

All green after the matching implementation (final counts above). Only the two intended existing assertions changed: item 6 fixture (class Flash rows added to the catalog fixture; `system.get_info` row added so `system` still has an action) and item 8 test name and string.

## Files changed

New (all under `frontend/src/`):
- `core/commands/useDeviceReboot.ts`: `runReboot` (dry run, one real send, poll by code, read-back with retry), `readWithRetry`, `infoOf`, `useDeviceReboot` (phase, outcome, correlationId, run, cancel, nudge).
- `core/commands/useDeviceReboot.test.ts`: 39 tests.
- `features/devices/components/reboot/rebootPlan.ts` (+ `.test.ts`, 51 tests): pure logic: relay plan, post-reboot check, verification, gate, phrase, messages, `rebootResultText`, `DRY_RUN_NOTE`.
- `features/devices/components/reboot/DeviceRebootPanel.vue` (+ `.test.ts`, 22 tests): button, preflight reads, dialog, status line, result.
- `features/devices/components/reboot/RebootConfirmBody.vue`: dialog slot content (§6.4 lines).
- `features/devices/components/reboot/RebootResult.vue`: uptime before to after, firmware, clock, relay chips, deviations, notes.

Modified:
- `core/api/adminApi.ts`: `sendCommand` body `confirm?`; `CommandCatalogAction.class?`.
- `core/commands/useDeviceCommand.ts`: `DeviceCommandBody` `confirm?`, `dryRun?`.
- `core/commands/useDeviceWrite.ts` (+ test): `followJobs?` option.
- `core/commands/testing/fakeDevice.ts`: `SentCommand.confirm/dryRun`; `Scenario.results` (Error entries fail a poll) and `pollError`.
- `shared/ui/feedback/DangerConfirmDialog.vue` (+ test): default slot, `cancelText`, `promptText`, `promptSuffix`, `inputPlaceholder`, `beforeConfirm`.
- `features/devices/components/DeviceCommandModal.vue` (+ test): hides `class === 'Flash'`; `confirm_token` branches removed (`sms.list` kept).
- `features/devices/components/DeviceOutputsPanel.vue` (+ test): stale reason `device_reboot_scheduled`.
- `features/devices/components/reads/configWriteSpecs.ts` and `DeviceReadCards.test.ts`: reboot_recommended note.
- `features/devices/views/DeviceDetailView.vue` (+ test): panel in column 1, `rebootEvent`, `noteReboot`.

## As built (deviations)

1. **Extra components.** Dialog body and result display are `RebootConfirmBody.vue` and `RebootResult.vue`, so the panel stays near 150 lines. Not in the phase's Create list.
2. **Extra dialog props.** `promptSuffix` ("để xác nhận:") and `inputPlaceholder`, besides the planned `cancelText`/`promptText`. Without them the dialog read "Gõ REBOOT-x to confirm:" with an English placeholder. Defaults unchanged, so other callers are unaffected.
3. **Panel placement:** inside the "MQTT Broker Transport" card (column 1), as a bordered section, not a separate card; the whole section is `v-if` on `rebootGate.visible`.
4. **`RebootOutcome` shape.** `kind` values: `dry_run_failed`, `not_sent`, `rejected`, `observed`, `relay_unchecked`, `not_observed`, `outcome_unknown`, `server_restarted`, `tracking_timeout`, `untracked`. Added `uptimeBefore` and `check` (`{deviations, notes}`) fields; `info` is `{uptimeSeconds, firmware}`, `time` is `{ntpSynced, skewSeconds}`. `runReboot` takes extra options `relaysBefore`, `onRealCommand`, `wait` (so the composable can cut the 2 s poll pause short on `nudge`). `nudge(id?)` ignores a different correlation id.
5. **Server `Failed` refusals.** The shared write classifier calls an HTTP-200 `Failed` result `unknown`. For the real command only, `HIGH_RISK_REQUIRES_MASTER_ADMIN`, `CONFIRM_REQUIRED`, `DEVICE_PERMISSION_REQUIRED` are treated as `not_sent` (refused before signing) in `useDeviceReboot.ts`, so nothing is read back and no polling happens. Shared classification untouched. `ContractUnavailable` refusals (`IN_PROGRESS`, `TOO_SOON`, `CONFIRM_MISMATCH`, payload invalid) were already `not_sent`.
6. **Retry rule for read-back and preflight reads** (`readWithRetry`, 30 s per read): server `DEVICE_READ_BUSY`; `DEVICE_REJECTED` whose detail starts with `LANE_FULL`, `busy` or `UNAUTHORIZED`; wait = reply `retry_after_ms` clamped to 0.5-5 s, else 2 s. Thrown errors and other codes are not retried. `control.get_status` or `system.get_info` not in `allowedCommands` is not sent: status missing gives `relay_unchecked`, uptime missing gives `verified:false`.
7. **Preflight:** if the signed uptime cannot be read, the dialog still opens with a warning line ("kết quả sẽ không được xác minh") rather than blocking. A reboot cannot be verified then, but the operator is a master-admin and the server gate is unchanged.
8. **Known limitation, `outputs` event during preflight.** Dialog closes on any `outputsKey` change while it is open. The preflight `control.get_status` read can itself produce an `outputs` event; if that event arrives after the dialog opened it closes once with the "Trạng thái relay vừa đổi" notice and the operator reopens it (second read changes nothing). Not testable in jsdom; phase 3 simulator run should watch for it.
9. **Tracking edge:** unknown/unrecognised codes during polling end the run as `outcome_unknown` with the code shown, instead of polling the full 180 s. Network or 5xx poll errors are swallowed; only 404 ends it (`server_restarted`).
10. `fakeDevice` `pollError` counts failing polls after the first answer of a command (failed polls do not consume `results`); `results` also accepts `Error` entries (used for the 404 case).
11. `class` is read from `/me/command-catalog` as the string `Flash` (server serializes the enum as a string via `JsonStringEnumConverter`, `Program.cs:27`).

## Not done / not testable here

- No browser run, no broker, device or gateway contact (phase 3 does the UI run on the simulator). Visual layout of the panel and dialog is unchecked beyond typecheck and jsdom text assertions.
- `docs/design-guidelines.md` does not exist in the repo; I matched the DO panel and read-card styling (tokens, `Button` variants, 11px text).

Status: DONE

## Fixes after UI walkthrough

**Bug 1: dialog closed on any `outputs` event.** Red (new tests in `DeviceRebootPanel.test.ts`): `stays open when an outputs event arrives but the relays are as the dialog shows them` (`expected null not to be null`); stale and fetch-failure closing tests (`expected 'Trạng thái relay vừa đổi…' to contain 'không còn đáng tin'`). The "relay changed", mode, pulse, channel added/removed cases were green against the old code by design (it always closed). Green after the fix.
- The panel now keeps the channel table the dialog was built from. On an `outputsKey` change while the dialog is open it fetches `adminApi.deviceOutputs` (the server's verified state) and compares with `relaysMatch` (channel set, state, mode, pulse running; the verified DTO has no forced flag, so a forced level is compared as the state).
- Decision: it closes with "Trạng thái relay vừa đổi; mở lại…" only on a difference. It also closes, with a separate message ("Danh sách relay không còn đáng tin…"), when the state is stale (for example `device_reboot_scheduled`) or when the state cannot be fetched or is not readable, because the list can no longer be trusted. A same-state event, including a late event from the dialog's own preflight read, leaves it open. Overlapping checks: only the newest applies. The earlier test that expected a close on a bare event was replaced by these.

**Bug 2: old result persisted into a new attempt.** Red: `clears the previous result and notice when the next attempt starts` (`expected true to be false` on the status element). Fix: `prepare()` (opening the dialog) calls `reboot.cancel()` when no run is active, clearing the previous outcome and phase, and clears the notice. Green.

Files: `features/devices/components/reboot/DeviceRebootPanel.vue`, `rebootPlan.ts` (`relaysMatch`, `mode` on `RelayChannel`), `DeviceRebootPanel.test.ts`.

Gate: vitest 40 files, 466 passed (458 + 8 new); `vue-tsc --noEmit` clean; `npm run build` see below.

## Fixes after code review

Each fix was test-first; red results are from running the new test against the unfixed code.

| Item | Red | Change |
|---|---|---|
| M1 run survives tab switch | panel remount test: `Cannot call text on an empty DOMWrapper` (new panel had no run) | `useDeviceReboot` instance now lives in `DeviceDetailView` (page scope, disposed with the page; a device change cancels it) and is passed to the panel as prop `reboot`. Panel test remounts a panel mid-run: live phase, locked button, then the result. Page test: same `reboot` instance after leaving and returning to the telemetry tab |
| M2 relay_unchecked | `expected 'Chưa kiểm được trạng thái relay' to be 'Đã khởi động lại. Chưa kiểm được…'` | `relay_unchecked` text is "<Đã khởi động lại \| Thiết bị báo đã lên lại (chưa xác minh)>. Chưa kiểm được trạng thái relay". Two existing expectations changed on purpose (rebootPlan test, panel test) |
| M3 preflight reads after unmount | `expected 11 to be 3` sends (reads kept going 30 s) | Panel aborts its read controller on scope dispose; `readWithRetry` also returns right after an aborted pause instead of sending once more |
| Server M3 busy form | `expected 1 to be 3` | Retry regex is now `/(^\|:\s*)(LANE_FULL\|busy\|UNAUTHORIZED)\b/i`; verified against `DeviceLinkHandlers.cs` (`reason = error[: data.error]`). Test uses the detail `ESP_ERR_INVALID_STATE: busy` |
| L2 lost send then 404 | `expected 'server_restarted' to be 'outcome_unknown'` | 404 means "server restarted" only if the server answered the send or an earlier poll; otherwise `outcome_unknown` / `REBOOT_SEND_UNCONFIRMED`: "Không rõ lệnh có tới server không; chưa rõ kết quả" |
| L3 confirm during comparison | `expected [system.reboot…] to have a length of +0 but got 1` | The panel keeps the in-flight comparison promise; `stillValid` awaits it and refuses if it closed the dialog |
| L9 layering, kind type | (refactor, tests green throughout) | `rebootPlan.ts` and its test moved to `core/commands/`; `RebootKind` lives there and is re-exported by `useDeviceReboot.ts`; `rebootResultText(kind: RebootKind, …)`. No `core` to `features` import remains |
| L10 fail closed | `expected [ 'no_class', 'plain' ] to deeply equal [ 'plain' ]` | C2 modal shows only actions with a class other than `Flash`. Modal test fixtures now carry `class` (intended fixture change) |

Accepted as residual, no change: L1 (empty phrase; the gate re-check and server `CONFIRM_REQUIRED` cover it), L4 (no explanation on stale-read reopen), L5 (no re-check between dry run and real send), L6 (`readWithRetry` duplicates the read classifier), L7 (`nudge` also shortens read retries), L8 (verified state has no `forced` flag), L11 (forced-ON deviation can be early, spec gap).

Gate: vitest 40 files, 475 passed (466 + 9); `vue-tsc --noEmit` clean; `vue-tsc` plus `vite build --outDir <temp>` succeeds (repo `frontend/dist` and the Vite dev server on 5173 untouched).
