# Phase 2 implementation report: panel DO, hop reboot, ket qua reboot

## Baseline (before)
- vitest: 475 tests / 40 files. 1-3 tests timed out at 5 s on cold runs (OverviewView, first DeviceOutputsPanel test, first DeviceRebootPanel test); different ones each run, so machine-load flakes, not code. Passed on later runs.
- vue-tsc: clean.

## Red
- New tests written first: outputLocks.test.ts (module missing, file failed), rebootPlan.test.ts (13 red), DeviceOutputsPanel.test.ts (8 red), DeviceRebootPanel.test.ts (2 red). Others of the new ones (no-owner, unknown owner, owner-change keeps dialog, untranslated refusal) pass without code, as designed (they pin existing behaviour).

## Green (after)
- vitest: 528 passed / 41 files (475 + 53 new).
- vue-tsc --noEmit: clean. vite build: OK.
- `BUSY_DETAIL` and `relaysMatch` untouched (git diff of useDeviceReboot.ts is one line: `NOTHING_BEFORE`).

## Files
- Created: frontend/src/core/commands/outputLocks.ts, outputLocks.test.ts
- Modified: adminApi.ts (OutputOwner, 5 optional fields), rebootPlan.ts, useDeviceReboot.ts (:77), DeviceOutputsPanel.vue, RebootConfirmBody.vue, RebootResult.vue, tests of panel, rebootPlan, reboot panel.

## Deviations
- Added two small tests beyond the list: `channelsOf` reads owner/ownerId (number/string/absent); reboot dialog does not say "no relay on" while a rule-held channel is ON.
- Lock text placed above the state badge (below the mode line).
- Mode `pulseActive`-only rule-held channel counts as `on` in `ruleHeld`.

Status: DONE
