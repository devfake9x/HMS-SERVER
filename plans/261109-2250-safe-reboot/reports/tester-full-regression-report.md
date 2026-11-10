# Tester full regression report: safe reboot (`system.reboot`)

Date: 2026-11-10. Branch: `claude/mqtt-device-connection-36f705`. Uncommitted change set. Nothing committed, no broker/device/lab contact, no process stopped.

Status: DONE_WITH_CONCERNS

## Results overview

| Gate | Command | Result |
|---|---|---|
| .NET build | `dotnet build Hms.Server.slnx --no-incremental` (scratch copy) | exit 0, 0 errors, 3 warnings (all pre-existing) |
| .NET build in place | same, in repo worktree | 6 errors, all file-copy locks on `src/Hms.Server.Api/bin` held by PID 2644 (running API). Compile of Application/Domain/Infrastructure succeeded. Not a code failure |
| Unit, run 1 | `dotnet test tests/Hms.Server.UnitTests` (scratch, with build) | 640 passed, 0 failed, 0 skipped (1 s) |
| Unit, run 2 | `dotnet test ... --no-build` | 640 passed, 0 failed, 0 skipped (976 ms) |
| Unit, run 3 | `dotnet test ... --no-build` | 640 passed, 0 failed, 0 skipped (1 s) |
| Integration | `Mqtt__Enabled=false dotnet test tests/Hms.Server.IntegrationTests` (scratch) | 123 passed, 0 failed, 0 skipped (3 s), exit 0 |
| EF model | `dotnet ef migrations has-pending-model-changes --project src/Hms.Server.Infrastructure/Hms.Server.Infrastructure.csproj --startup-project src/Hms.Server.Infrastructure/Hms.Server.Infrastructure.csproj` (scratch) | "No changes have been made to the model since the last migration." exit 0 |
| Vitest, run 1 | `npx vitest run` in `frontend/` | 40 files, 475 passed, 0 failed (12.39 s) |
| Vitest, run 2 | `npx vitest run` in `frontend/` | 40 files, 475 passed, 0 failed (9.47 s) |
| Type check | `npx vue-tsc --noEmit` | exit 0, no output |
| Prod build | `npx vite build --outDir <scratch>/dist-check --emptyOutDir` | exit 0, built in 5.94 s, no warnings |
| Reboot tool plan | `node tools/device-reboot-check.js --plan` | exit 0, prints steps, sends nothing (the only `fetch` at line 89 sits behind the `--confirm-live` gate, after the `--plan`/`--self-check` returns) |
| Reboot tool self-check | `node tools/device-reboot-check.js --self-check` | exit 0, "every probe is a dry run; live probes carry no confirm and an empty payload" |
| Simulator syntax | `node --check tools/mqtt-broker/spec-device-simulator.js` | exit 0 |

Totals vs baseline (no decrease):

| Suite | Baseline | Phase 1 report | Phase 2 report | This gate |
|---|---|---|---|---|
| .NET unit | 571 | 640 | 640 | 640 (x3) |
| .NET integration | 117 | 123 | 123 | 123 |
| vitest | 325 | 458 (ph2), 475 (ph2 final) | 475 | 475 (x2) |

Counts match the latest reported numbers (unit 640, integration 123, vitest 475).

## Environment and workarounds

- **Lock on `src/Hms.Server.Api/bin`.** The running API (PID 2644, port 5099) locks the Api output. The in-place build failed at the copy step (MSB3021/MSB3027, retries exhausted). Per instructions, I copied the tree (excluding `.git`, `bin`, `obj`, `node_modules`, `frontend/dist`) to:
  `C:/Users/toann/AppData/Local/Temp/claude/4159b4ee1fa27905/1d0ec659-3a4f-4354-a062-009a27bce72c/scratchpad/tree`
  All .NET build, test, and EF runs used this copy. The API was not stopped.
- **Side effect in repo.** The in-place build wrote `bin/`/`obj/` under `src/Hms.Server.Application`, `Domain`, and `Infrastructure` before the lock failed. These are gitignored build outputs. The Api `bin` was not modified.
- **Scratch copy completeness.** 16,547 repo files vs 16,511 in the copy. The 36-file difference equals the 36 files in `frontend/dist`, which were excluded on purpose.
- **Integration env var.** Needed `Mqtt__Enabled=false` (stale CA path in Development user secrets). Per instructions, user secrets were not read or listed.
- **EF args.** The reports do not record the exact args. I used the form documented in `plans/261005-0556-device-permissions-what-where/phase-02-device-roles-memberships-persistence.md` (`--project` and `--startup-project` both `src/Hms.Server.Infrastructure/Hms.Server.Infrastructure.csproj`).
- **Frontend.** Run in place in `frontend/`. `frontend/dist` was not touched: 0 files newer than the build log, 36 files, unchanged. The Vite dev server on 5173 was not stopped.

## Flaky tests

- **None observed.** Unit suite: 3/3 runs identical (640/640). Vitest: 2/2 runs identical (475/475).
- Known pre-existing flaky `SignalRConnectionBacklogTests.Emits_runtime_metrics_for_drop_retry_and_resynchronization` passed in all 3 unit runs. It fails about 1 in 35 runs per the phase 1 report, so 3 clean runs are consistent with that and do not clear it.

## Build warnings

- .NET (`--no-incremental`, scratch): 3 warnings, same as baseline, none new:
  - CS8631 `tests/Hms.Server.IntegrationTests/ManagementApiTests.cs(357,9)`
  - xUnit1031 `tests/Hms.Server.UnitTests/GrantSerializationTests.cs(33,37)`
  - xUnit1031 `tests/Hms.Server.UnitTests/GrantSerializationTests.cs(68,37)`
- Vitest: no stderr output in run 2. The 10 "warn" hits in run 1 are test titles (for example "warns in general words ..."), not console warnings.
- vue-tsc: clean.
- Vite build: no warnings.

## Acceptance coverage (plan.md "Acceptance (toàn plan)")

Excludes the deferred `gateway_870c` items, as instructed.

Covered by automated tests:

- **Catalog:** `system.reboot` opened, 34 enabled (`CatalogTests.cs:157`), `FlashActions` is exactly `[("system","reboot")]` (`:170`). `factory_reset`, `ota.apply`, `ota.remote_start` denied (`CatalogTests.cs:171-177`). Integration `factory_reset` HTTP refusal (`ManagementApiTests.cs:457`).
- **Permission:** master-admin only (`DeviceCommandAuthorizationTests.cs:240-259`, asserts holder gets `HIGH_RISK_REQUIRES_MASTER_ADMIN`, no transport record, no audit row at `:262-272`). Missing `sys.reboot` gives `DEVICE_PERMISSION_REQUIRED` (`:253`). `allowed-commands` master-admin only (`ManagementApiTests.cs:440-442`). Non-master-admin cannot assign `sys.reboot` (`DeviceAccessHandlerTests.cs:180, 201`).
- **Before signing:** `CONFIRM_REQUIRED` (`ManagementApiTests.cs:454`), `CONFIRM_MISMATCH` (`:455`), `COMMAND_PAYLOAD_INVALID` for `delay_ms` (`:456`), and in unit `DeviceLaneSafetyTests`/`ConfigCommandPolicyTests` for the `{}` rule.
- **Gate:** every `system.reboot` during hold gives `DEVICE_REBOOT_IN_PROGRESS` (`DeviceRebootTrackerTests.cs:38`). `DEVICE_REBOOT_TOO_SOON` for real reboots within 60 s (`DeviceRebootTrackerTests.cs`, "Two_real_reboots_are_a_minute_apart"). Lost, late, `Acknowledged`, and unconfirmed-publish replies keep the gate (`DeviceRebootFlowTests.cs`, names "A_lost_reply...", "A_publish_the_broker_did_not_confirm...", "A_reply_that_says_still_running..."). Refused probes do not consume slots: 11 refusals then `set_hostname` publishes (`DeviceRebootFlowTests.cs:168-184`). Config writes and relay still publish during hold (`:187-197`).
- **Dry-run:** success, no hold, no tracking (`DeviceRebootFlowTests.cs:211`). `PayloadSummary` `{"dry_run":true,"payload":{}}` at reservation (`PersistenceModelTests.cs:332-352`, EF in-memory).
- **Real reboot:** `SCHEDULED` with `uptime_before` and DO outputs marked `device_reboot_scheduled` (`DeviceRebootFlowTests.cs`, "A_reply_that_the_device_will_reboot_is_scheduled..."). `OBSERVED` from heartbeat uptime, from the message and not the shared store (`DeviceRebootTrackerTests.cs`, "The_uptime_comes_from_the_heartbeat_message..."). `NOT_OBSERVED` with no resend. Late tail upgrade. Link-down sweep concludes nothing. Rebind and revoke give `OUTCOME_UNKNOWN`. Finished rows not overwritten (`TryCompleteAsync` compare-and-set, "A_finished_row_is_not_overwritten...").
- **Command results:** `command_result` on each code change (`DeviceRebootFlowTests.cs:244, 266, 348`).
- **TTL:** class F signed `ttl_ms` cap 10 000 (`DeviceCommandAuthorizationTests.cs:236`). `/me` still advertises 60000 (`ManagementApiTests.cs:229`).
- **Wiring:** DI with live transport gives one shared tracker (`DeviceLinkWiringTests.cs`, 2 tests).
- **UI, dialog and gate:** button hidden without `system.reboot`, locked when offline or unverified (`DeviceRebootPanel.test.ts:95-128`). Confirm only for exact `REBOOT-<mqttDeviceId>` (`:195`). Relay change closes dialog (`:237, 267`). Recheck on confirm (`:299, 324, 341`). Forced and unforced relay wording (`:140`, `rebootPlan.test.ts`).
- **UI, run:** dry-run then real then result, `Acknowledged` no poll (`useDeviceReboot.test.ts:221`), poll errors swallowed (`:240`), 404 gives server restarted (`:248`), no resend (`:271`), busy and `LANE_FULL` retries (`:286-330`).
- **UI, result:** "Đã khởi động lại" only when signed uptime dropped (`DeviceRebootPanel.test.ts:431`, `rebootPlan.test.ts:104`). Otherwise "chưa xác minh" (`:443`). "Chưa kiểm được trạng thái relay" path (`:457, 464`).
- **Modal C2:** no class F action, no `confirm_token` (`DeviceCommandModal.test.ts:166, 200-232`).
- **Page:** reboot event routing, link refresh and stale marking (`DeviceDetailView.test.ts:252-361`). Dialog slot (`DangerConfirmDialog.test.ts:48`).

Partial or not automated:

1. **`ota.apply` and `ota.remote_start` sent by master-admin.** Acceptance says "kể cả khi master-admin gửi". Only the catalog availability is asserted (`CatalogTests.cs:173-177`). The HTTP integration test checks `factory_reset` only (`ManagementApiTests.cs:457`). The shared availability gate should refuse them, but no per-row send test proves it.
2. **Postgres relational branch** of `EfCommandAuditStore` `TryCompleteAsync` and `FailUnfinished`. Not tested, by plan decision (review-only, no Postgres in dev, red-team #6). Live scenario 9 also did not run the Postgres `OUTCOME_UNKNOWN` path.
3. **Live simulator scenarios** (`reports/live-verification.md`): 2a, 2b, 2c, 2d, 3, 4, 5, 6, 7, 9 recorded PASS by the phase 3 run. They are not in any automated suite and I did not re-run them (they need a live API and simulator).
4. **UI walkthrough, scenario 8.** Not run. No browser-level evidence against the real API. The vitest coverage uses fakes.
5. **Outcome-level `dry_run` in audit.** `"dry_run"` is written at reservation, so it holds for every outcome by construction. There is no per-outcome test (timeout, failure, observed). Low risk.
6. **Server-side "never resent".** Asserted per scenario in tracker and flow tests (for example "No_drop_by_the_deadline_is_not_observed_and_nothing_is_sent_again"). No transport-level redelivery test. Redelivery is an accepted residual risk (R10, Quyết định 5).
7. **`tools/device-reboot-check.js`** has no automated test beyond `--self-check` (probe table and the `refused()` classifier, 9 cases). Its live modes are untested by automation.

Excluded by instruction: gateway `gateway_870c` (deferred, Validation Session 1).

## Critical issues

None blocking. Every gate is green on both suites and the build.

## Recommendations

1. Add a send-time test for each closed row as master-admin: `ota.apply`, `ota.remote_start`, `system.factory_reset` (HTTP or handler loop). Closes gap 1.
2. Run the Postgres relational path once with a real Postgres instance before production (scenario 9 `OUTCOME_UNKNOWN`). Closes gap 2.
3. Schedule scenario 8 (UI walkthrough against the real API and simulator) as the next manual step.
4. Document the `Mqtt__Enabled=false` requirement in the integration README, or refresh the Development user secret (out of scope here; do not read secrets in this gate).
5. Keep the SignalR flaky test on the watch list. Three clean runs do not clear a roughly 1-in-35 failure.

## Next steps (priority)

1. Close gap 1 (`ota.*` master-admin send-time tests). Small, unit-level.
2. Real-Postgres run of the relational audit branch (gap 2).
3. Gateway `gateway_870c` live run, only when someone is at the lab and the user says "go" (deferred).
4. UI walkthrough (scenario 8).
5. Optional: automated test for the reboot tool's live-mode classification.

## Unresolved questions

- Is the EF args form I used (Infrastructure as both project and startup) the project's standard? The reports did not record exact args; the result was clean, but if a different startup project is standard, rerun.
- Who runs scenario 8 (UI walkthrough) and when?
- Should the 6 in-place Api copy errors be documented as a known local limitation whenever the API is running? Future gates need the scratch-copy workaround while the API runs.

Files:
- Report: `D:/WORKSPACE/PROJECT/HMS-SERVER/.claude/worktrees/ck-project-management-review-dd19d6/plans/261109-2250-safe-reboot/reports/tester-full-regression-report.md`
- Scratch logs: `C:/Users/toann/AppData/Local/Temp/claude/4159b4ee1fa27905/1d0ec659-3a4f-4354-a062-009a27bce72c/scratchpad/` (`build-scratch.log`, `unit-run{1,2,3}.log`, `integration.log`, `ef-pending.log`, `vitest-run{1,2}.log`, `vuetsc.log`, `vite-build.log`, `reboot-plan.log`, `reboot-selfcheck.log`)

Status: DONE_WITH_CONCERNS
Summary: All gates green: .NET build 0 errors (3 pre-existing warnings), unit 640/640 x3, integration 123/123, EF no pending changes, vitest 475/475 x2, vue-tsc and prod build clean, tool checks pass. Concerns are coverage only: `ota.*` master-admin send-time refusal lacks a per-row test, the Postgres audit branch and UI walkthrough are not automated, and the in-place build was blocked by the running API so the gate ran in a scratch copy.
Concerns/Blockers: In-place build blocked by API lock (worked around in scratch copy). Integration needs `Mqtt__Enabled=false`. EF args assumed. Gateway deferred.
