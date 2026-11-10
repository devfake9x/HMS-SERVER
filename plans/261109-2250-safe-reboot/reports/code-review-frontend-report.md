# Code review: safe reboot, frontend

Plan: `plans/261109-2250-safe-reboot/` (phase 2) | Branch: `claude/mqtt-device-connection-36f705` | Date: 2026-11-10 | Scope: `frontend/` only, uncommitted changes. Read-only review: no repo file changed except this report.

## Scope

- Modified (15): `core/api/adminApi.ts`, `core/commands/{useDeviceCommand,useDeviceWrite}.ts` (+ test), `core/commands/testing/fakeDevice.ts`, `shared/ui/feedback/DangerConfirmDialog.vue` (+ test), `features/devices/components/{DeviceCommandModal,DeviceOutputsPanel}.vue` (+ tests), `reads/configWriteSpecs.ts`, `reads/DeviceReadCards.test.ts`, `views/DeviceDetailView.vue` (+ test). Diff: +368 / -25.
- New: `core/commands/useDeviceReboot.ts` (320) + test (426); `features/devices/components/reboot/{rebootPlan.ts (163), DeviceRebootPanel.vue (168), RebootConfirmBody.vue (39), RebootResult.vue (45)}` + `rebootPlan.test.ts` (220), `DeviceRebootPanel.test.ts` (425).
- No frontend file changed during the review (newest mtime 08:50, review 08:52-09:05).
- Probes: a scratch copy of `frontend/` (node_modules by junction) ran 5 probe tests against the repo code; nothing was added to the repo.

## Overall assessment

The flow is built correctly. The realtime event only shortens the poll pause and never sets a result, so the event and the poll cannot both handle the same result. The flow sends `system.reboot` from exactly two call sites, and `request()` does not retry. Aborting after the dry run prevents the real send. The flow branches on the code, including the first answer. The shared-code changes are additive, and their defaults keep existing behaviour. All gates are green.

No Critical or High findings. Three Medium findings concern what the operator sees and leftover device traffic when the panel goes away or the relay read fails. The Low findings are wording, defence-in-depth and maintainability.

## Critical

None.

## High

None.

## Medium

**M1. Switching tab on the device page silently cancels a reboot in progress and loses its result.**
- Where: `DeviceDetailView.vue:704` (`<div v-if="activeTab === 'telemetry'">` wraps the panel at :950); `useDeviceReboot.ts:316-317` (`watch(deviceId, cancel)`, `onScopeDispose(cancel)`); state is local to `DeviceRebootPanel.vue:19`.
- Scenario: the operator confirms, then opens the "Audit" or "Commands" tab to watch the row. The panel unmounts and `cancel()` aborts the run. Probe P2: when the operator returns, the status shows nothing and the button is enabled again. If the tab switch happens after the real POST, the device reboots, but the panel never shows "Đã khởi động lại" or the relay table. The next click reaches the dry run and gets `DEVICE_REBOOT_IN_PROGRESS`/`TOO_SOON` from the server, so this is safe but confusing. The 180 s window makes this much more likely than for the config cards, which have the same pattern.
- Fix (pick one):
  - (a) Create `useDeviceReboot` in `DeviceDetailView` (page scope) and pass `phase`/`outcome`/`run`/`nudge` to the panel.
  - (b) Use `v-show` for the telemetry tab.
  - (c) At minimum, keep the last `{correlationId, startedAt}` in page state. On remount, show "Đang theo dõi bị dừng; xem kết quả ở tab Audit" and poll that correlation id read-only. Never send again.

**M2. `relay_unchecked` hides whether the reboot was verified.**
- Where: `rebootPlan.ts:156` returns only "Chưa kiểm được trạng thái relay"; the panel status is `DeviceRebootPanel.vue:41`.
- Scenario: the server reports OBSERVED, the signed uptime did not drop (`verified:false`), and `control.get_status` stays `LANE_FULL` for 30 s. Probe P4: `kind:'relay_unchecked', verified:false`. The status line says only "Chưa kiểm được trạng thái relay". It never says "Thiết bị báo đã lên lại (chưa xác minh)", which Acceptance UI requires whenever the signed uptime did not drop. The same text appears when the reboot was verified. RebootResult shows the raw uptime numbers only when `info` was read.
- Fix: for `relay_unchecked`, show `${rebootMessage('DEVICE_REBOOT_OBSERVED', null, verified)}. Chưa kiểm được trạng thái relay`. Add a panel test with `uptimeAfter > uptimeBefore` and an unreadable table.

**M3. The pre-dialog reads keep running after the panel unmounts.**
- Where: `DeviceRebootPanel.vue:28`, `:56-75`. `readController` is aborted only by the `deviceId` watcher (`:128-135`); the panel has no `onScopeDispose`/`onUnmounted`.
- Scenario: the operator clicks "Khởi động lại" and the device answers `LANE_FULL`. Then the operator leaves the tab or page. Probe P1: 2 reads were sent before unmount and 11 after it (9 more `control.get_status` retries over 30 s). Each read takes a slot in the device's small command queue, which relay commands share. Each read can also take up to 40 s (`POLL_LIMIT_MS`), so this can last about 80 s.
- Fix: `onScopeDispose(() => { readController?.abort(); readController = null })` in the panel, plus a test that unmounts mid-preflight and asserts no further sends.

## Low

- **L1. An empty confirm phrase removes the typed-phrase step.**
  - Where: `DeviceRebootPanel.vue:161` passes `rebootPhrase(link)`, which is `''` without `mqttDeviceId`. `DangerConfirmDialog.vue:54-57` treats an empty phrase as "no phrase needed".
  - Probe P3: after the binding id was cleared while the dialog was open, the input disappeared and the confirm button was enabled without typing.
  - Still safe: `stillValid` re-runs `rebootGate` ("Chưa có định danh MQTT"), and the server answers `CONFIRM_REQUIRED`.
  - Fix: close the dialog, or keep confirm disabled, when the phrase becomes empty. Do not rely on the dialog's "no phrase" default for a class F command.
- **L2. A failed real send can be reported as "server restarted".**
  - Where: `useDeviceReboot.ts:223`.
  - Scenario: the real POST fails in transit (`NETWORK_ERROR`, so `isOpen`), and the server never got it, so the first poll is 404. Probe P5: `server_restarted`, "Server đã khởi động lại và mất bản ghi lệnh". The server did not restart.
  - Fix: when `real.dispatch === null` and the first poll is 404, use `outcome_unknown` with "Không rõ lệnh có tới server không; chưa rõ kết quả".
- **L3. Confirming during an in-flight relay comparison skips the comparison.**
  - Where: `DeviceRebootPanel.vue:78-91` vs `:111-126`.
  - Scenario: an `outputs` event arrives and the `/outputs` comparison is in flight. If the operator confirms inside that window (one HTTP round trip), `stillValid` passes and the reboot is sent from a list that might have just changed.
  - Fix: keep the pending check promise and have `stillValid` await it.
- **L4. The stale-reading reopen has no explanation.**
  - Where: `DeviceRebootPanel.vue:85-88`.
  - The dialog closes, re-reads and reopens with an empty input. Nothing tells the operator why the phrase must be typed again. With a 30 s limit, a slow reader hits this often.
  - Fix: set a notice such as "Dữ liệu cũ hơn 30 s; đã đọc lại, gõ lại mã".
- **L5. The relay list is not re-checked between the dry run and the real send.**
  - Where: `useDeviceReboot.ts:189-197`.
  - A dry run can take up to 40 s (`CLIENT_TIMEOUT` makes it `unknown`, so it stops). An `executed` reply after 35 s still leads to a real send based on a list about 65 s old.
  - The plan does not require this check. Option: re-check `preflight.readAt` age via an `onPhase('sending')` hook, and stop with "dữ liệu cũ" if it is too old.
- **L6. `readWithRetry` reimplements the read logic of `useDeviceReads`.**
  - Where: `useDeviceReboot.ts:107-148` re-implements `useDeviceReads.ts:30-79` (`readOnce`, `isBusy`) with a wider busy rule (`busy`, `UNAUTHORIZED`, `retry_after_ms`).
  - Two classifiers for the same reply shapes will drift. Fix: export `readOnce`/`isBusy` and build the 30 s retry on top.
- **L7. `nudge` also shortens read retries.**
  - Where: `useDeviceReboot.ts:181`, `:262-271`.
  - The `wait` that `nudge()` can cut short is also passed to `readWithRetry` in the read-back phase. A duplicate `command_result` event during read-back makes a retry fire before `retry_after_ms`. The `nudged` flag can also be consumed by a read pause instead of a poll pause. The effect is bounded by the 30 s window.
  - Fix: pass `sleep` to the read-back.
- **L8. The dialog's change check cannot see `forced`.**
  - Where: `rebootPlan.ts:61-67`.
  - The server DTO has no `forced` flag. A force set or cleared at the same level leaves the dialog open with the old wording. For example, it still says "DO1 đang BẬT (không force) sẽ TẮT" for a channel that is now force-ON. Known server limitation; record it in the phase report, or add `forced` to `OutputChannelDto`.
- **L9. The layering is inverted and `kind` is loosely typed.**
  - Where: `useDeviceReboot.ts:3-10` imports from `@/features/devices/components/reboot/rebootPlan`. This is the only `core → features` import in `src` (grep), against the FSD layering in `docs/architecture-frontend-ui-sync.md:17`.
  - `rebootResultText(kind: string, …)` (`rebootPlan.ts:153`) takes a plain string, so a typo falls to the default branch.
  - Fix: move the pure relay/uptime helpers and types next to `useDeviceReboot` (or under `entities/`), and type `kind` as `RebootKind`.
- **L10. The C2 modal's class F filter fails open.**
  - Where: `DeviceCommandModal.vue:77-78`. A catalog snapshot without `class` (older server) shows `system.reboot` again.
  - Still safe: the modal never sends `confirm`, so the server answers `CONFIRM_REQUIRED`.
  - Optional: also list `system.reboot` in `PANEL_ONLY_ACTIONS`.
- **L11. A forced-ON deviation can be a false alarm.**
  - Where: `rebootPlan.ts:90-91`. The read-back runs as soon as OBSERVED arrives. Per §6.4 L876, a forced-ON line comes back only at the first line write, so an early read can report "force BẬT nhưng sau khởi động đang TẮT" by mistake. This is the open spec gap. Consider wording such as "(có thể chưa ghi lại line)", or one delayed re-read.
- **Informational: legacy command form.** The fallback shell in `App.vue` (`v-else`, `:958-959`, `sendDeferredCommand` `:760-779`) still lists class F actions. It sends without `confirm` or payload, so the server refuses with `CONFIRM_REQUIRED` before signing. It is not a second real send path.

## Edge cases found by scouting

- Tab switch, page reload, and losing the binding during a run or a preflight (M1, M3, L1).
- Server answers mapped through `runDeviceWrite`:
  - lane `DEVICE_WRITE_BUSY`/`RATE_LIMITED` and the reboot gate codes are `ContractUnavailable`, so they become `not_sent` (correct);
  - handler `Failed` refusals other than the three listed (for example `COMMAND_TIMESTAMP_IN_FUTURE`) become `outcome_unknown` without polling (conservative; no resend);
  - `MQTT_NOT_CONNECTED` is `not_sent`.
- The reply handler removes the pending entry (`DeviceLinkHandlers.cs:87`) before it records (`:123`), so the real send after a dry run does not race into `DEVICE_WRITE_BUSY`.
- The panel's own `control.get_status` read creates an `outputs` event. The dialog stays open when the state matches, which is the walkthrough fix, and is covered.
- The server-side DO state (`/outputs`) does not filter channels, and `pulseActive` null equals false. `relaysMatch` compares like with like. Only `forced` is missing (L8).

## (a) UI acceptance criteria

| Criterion | Result | Evidence |
|---|---|---|
| Button only with `system.reboot` in allowed-commands | Pass | `rebootGate` `rebootPlan.ts:106`; panel `v-if="gate.visible"` |
| Locks: catalog verified, link offline (`online`/`brokerConnected`/null link), no `mqttDeviceId`, running | Pass | `rebootPlan.ts:109-112`; `catalogVerified` = page `outputsCatalogVerified` (implies `trusted`, `catalogVerification.ts:33-37`); button also disabled while reading |
| Dialog wording per §6.4 (unforced ON goes OFF, forced OFF stays, forced ON may drop then return, DO rules may turn ON, unreadable, none ON) | Pass | `RebootConfirmBody.vue:16-37`; screenshot `screens/reboot-confirm-dialog-simulator.jpg` matches |
| Confirm phrase only `REBOOT-<mqttDeviceId>` | Pass (L1 caveat) | `rebootPhrase` `rebootPlan.ts:117-120`; server id refused (panel test) |
| Closes only on a real difference or stale/uncomparable state; re-checks gate and 30 s read age on confirm | Pass (L3 window) | `DeviceRebootPanel.vue:78-91`, `:111-126` |
| Dry run first, one real send | Pass | `useDeviceReboot.ts:190-197`; abort between them returns before the real send (`:192`) |
| Poll by code, any code on first answer | Pass | loop `:207-226` checks `code` before the first pause |
| Acknowledged without get_operations | Pass | `followJobs:false` `:183`, `useDeviceWrite.ts:175` |
| Poll errors swallowed; 404 → server restarted; 180 s cap | Pass (L2 wording) | `:219-225`, `:215` |
| Read-back with bounded retry (busy, LANE_FULL, read-busy, UNAUTHORIZED, `retry_after_ms` clamped 0.5-5 s) | Pass | `readWithRetry` `:107-148` |
| "Đã khởi động lại" only when signed uptime after < before, else unverified | Pass for `observed`; **gap for `relay_unchecked`** (M2) | `isVerifiedReboot` `rebootPlan.ts:101-103`, `rebootMessage` :141 |
| "Chưa kiểm được trạng thái relay" | Pass | `rebootPlan.ts:156` |
| Never resends | Pass | two `runDeviceWrite` calls only; `request()` has no retry (`adminApi.ts:184-194`); `run` guarded by `controller`; tests assert ≤ 2 sends in 7 scenarios plus 400 s idle |
| C2 modal hides class F, no `confirm_token` | Pass | `DeviceCommandModal.vue:77-78`; grep `confirm_token` in `src`: only the asserting test |
| DO panel stale reason `device_reboot_scheduled` | Pass | `DeviceOutputsPanel.vue:87` |
| Page event forwarding (rebootEvent, OBSERVED → link refresh, both config cards stale; cleared on device change) | Pass | `DeviceDetailView.vue:287`, `:330-340`, `:383` |

## (b) Regression check of shared code (callers by grep, non-test)

- `DangerConfirmDialog.vue`: callers `DeviceListView.vue:680` (revoke) and `DeviceCommandModal.vue:428`, plus the new panel. The new props default to the old English text, and the empty default slot renders nothing. Without `beforeConfirm`, `onConfirm` has no `await`, so `confirm` is still emitted synchronously. No change for the old callers.
- `useDeviceCommand.ts`: type-only (`confirm?`, `dryRun?`). `runDeviceCommand` already spread the body; undefined fields drop out in `JSON.stringify`. Callers `useDeviceReads.ts:50`, `DeviceOutputsPanel.vue:125`, `useDeviceWrite.ts:107/150`, `DeviceCommandModal.vue:37` are unaffected.
- `useDeviceWrite.ts`: `followJobs` is only consulted when explicitly `false`. Config cards go through `useConfigWrite.ts:137` → `useDeviceWrite` → `runDeviceWrite` without the option, so job following is unchanged (default-behaviour test added).
- `DeviceOutputsPanel.vue`: one map entry. `DeviceCommandModal.vue`: intentional behaviour change (class F hidden, token prefill removed; `sms.list` kept).
- `DeviceDetailView.vue`: additive; existing tests green.
- `adminApi.ts`: two optional fields. `configWriteSpecs.ts`: note text only (intended assertion change in `DeviceReadCards.test.ts`).
- `fakeDevice.ts`: `results`/`pollError` are optional, and without them every poll still gets `result` (backward compatible).

## (c) Public contract changes

| Contract | Change | Justified |
|---|---|---|
| `adminApi.sendCommand` body | `+confirm?: string` | Yes, the server reads root `confirm` for class F |
| `CommandCatalogAction` | `+class?: string` | Yes, the server already sends it (`MeController.cs:65`, `JsonStringEnumConverter` `Program.cs:29`). Could be the union `'Read'\|…\|'Flash'` |
| `DeviceCommandBody` | `+confirm?`, `+dryRun?` | Yes |
| `runDeviceWrite` options | `+followJobs?: boolean` (default follows) | Yes (red-team #15) |
| `DangerConfirmDialog` | props `cancelText`, `promptText`, `promptSuffix`, `inputPlaceholder`, `beforeConfirm`; default slot | Yes. `promptSuffix`/`inputPlaceholder` are beyond the plan, deviation 2 in the phase report |
| `fakeDevice` `Scenario`/`SentCommand` | `+results`, `+pollError`, `+confirm`, `+dryRun` | Test helper |
| New exports | `useDeviceReboot.ts`: `runReboot`, `useDeviceReboot`, `readWithRetry`, `infoOf`, types and constants. `rebootPlan.ts`: gate, phrase, relay helpers, messages, `DRY_RUN_NOTE`, `relaysMatch` | Yes; see L9 on placement |
| `DeviceRebootPanel` | props `ReadCardContext & {commandEvent, outputsKey}`, emit `refresh-link` | As planned |

## (d) Patterns, wording, leaks

- Vietnamese wording matches the plan text and the rest of the device page: "Catalog lệnh chưa được xác minh" and "Thiết bị offline" are the same strings as `configGate`. "Huỷ" is spelled the same everywhere. Tokens and 11px text match the DO panel.
- No `console.*` in the new code. Nothing logs `confirm`, ids or reply data. `mqttDeviceId` is already shown on the page (`DeviceDetailView.vue` "Device ID" row). For unmapped codes the status shows raw `CODE: detail`, which is server text that only master-admins see.
- `DeviceRebootPanel.vue` is 168 lines (target about 150); the logic lives in `rebootPlan.ts`/`useDeviceReboot.ts` as planned.

## (e) Gates (run 2026-11-10 in `frontend/`)

- `npx vitest run`: 40 files, **466 passed**, 0 failed (11.0 s). Matches the phase report (325 baseline + 141).
- `npx vue-tsc --noEmit`: **clean** (exit 0).
- Build: `npx vite build --outDir <scratchpad>/dist-check --emptyOutDir` after the `vue-tsc` above. This is `npm run build` (`vue-tsc --noEmit && vite build`) with the output redirected, so the repo's `frontend/dist` stayed untouched. **Success** in 5.6 s; `DeviceDetailView` chunk 144.9 kB (41.4 kB gzip).
- Probes (scratch copy, not in the repo): P1 (M3), P2 (M1), P3 (L1), P4 (M2), P5 (L2) all reproduced as described.

## Race / leak checklist

- SignalR vs polling: the event only calls `nudge()`, which shortens a pause. The result always comes from the poll, so it cannot be handled twice. Dry-run events are ignored because `correlationId` is null until `onRealCommand`. Double `refreshOutputs` (page on OBSERVED + panel `refresh-link`) is harmless.
- Stale closures: `stillValid` and `start` read props/refs at call time. A double click on confirm is guarded by `checking` and by `run`'s `controller` guard.
- Timers: the run's waits remove their abort listeners, and `cancel` on scope dispose aborts them. Preflight reads are not cancelled on unmount (M3). The `/outputs` compare fetch is not abortable, but its result is ignored once the dialog is closed.
- Second real send path: none (L10, informational note on `App.vue`).

## Recommended actions

1. M1: hoist the reboot state to the page, or keep the telemetry tab mounted. At least tell the operator that tracking stopped.
2. M2: include the verified/unverified sentence in the `relay_unchecked` result, and add a test.
3. M3: abort preflight reads on unmount, and add a test.
4. L1-L4: guard against an empty phrase, fix the 404 wording after a failed real send, await the in-flight compare in `stillValid`, add a notice on the stale-read reopen.
5. L6/L9: reuse the read classifier and fix the `core → features` import when next in this area.

## Plan follow-ups (no plan file edited)

- Phase 2 items 1-8 appear implemented; the 466 tests are green. Success criterion "Đã khởi động lại chỉ khi uptime đã ký xác nhận; bảng relay có, hoặc 'Chưa kiểm được trạng thái relay'" is met. The criterion's spirit (always say verified or not) is missed in the `relay_unchecked` branch (M2).
- Phase 3 UI walkthrough: add a check that switching tab mid-run behaves as decided after M1.

## Unresolved questions

- M1: should a run survive a tab switch (page-scoped state), or is "cancel and tell the operator" enough?
- L8: should `OutputChannelDto` carry `forced`, so the dialog can detect force changes?

Status: DONE_WITH_CONCERNS
Summary: The frontend reboot flow meets the plan's UI criteria. It never resends, keeps shared-code callers compatible, and passes vitest (466), vue-tsc and the build. There are no Critical or High findings. Three Medium findings, all reproduced by probes: a tab switch silently cancels a run in progress; `relay_unchecked` hides whether the reboot was verified; preflight reads outlive the panel.
Concerns/Blockers: M1-M3 should be fixed or explicitly accepted before the phase is marked complete; nothing blocks the server side.
