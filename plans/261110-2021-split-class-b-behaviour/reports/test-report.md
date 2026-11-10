# Test report: split class B behaviour (CommandDefinition flags)

Worktree: `D:\WORKSPACE\PROJECT\HMS-SERVER\.claude\worktrees\ck-project-management-review-dd19d6`
Scope: uncommitted change, 17 files (src + tests + docs). No source/test/doc edits, no commit.

## 1. Build

| Command | Result |
|---|---|
| `dotnet build Hms.Server.slnx` | exit 0, 0 errors, 0 warnings (incremental) |
| `dotnet build Hms.Server.slnx --no-incremental` | exit 0, 0 errors, 3 warnings |

The 3 warnings are pre-existing and in files NOT in `git diff --name-only`:
- `tests/Hms.Server.IntegrationTests/ManagementApiTests.cs(366)` CS8631 (line from commit 45ba9dfd, 2026-11-06)
- `tests/Hms.Server.UnitTests/GrantSerializationTests.cs(33,68)` xUnit1031 x2

New warnings in changed files: none.

## 2. Test totals (`dotnet test Hms.Server.slnx --no-build`)

| Assembly | Passed | Failed | Total | Time |
|---|---|---|---|---|
| Hms.Server.UnitTests | 709 | 0 | 709 | 1 s |
| Hms.Server.IntegrationTests | 71 | 76 | 147 | 4 s |

Matches expectation exactly.

### Integration failures classified (76/76 environmental)
- All 76 failure messages: `System.IO.FileNotFoundException: Could not find file '...\Temp\hms-idf-mosquitto-<guid>\ca.crt'`.
- By class: `ManagementApiTests` 73, `MaitechRtuDeviceIntegrationTests` 3.
- Non-environmental failures: **0**.
- Not fixed, no cert/broker created (per constraint).

## 3. Node checks
- `node --test "scripts/*.test.mjs"`: 33 pass, 0 fail.
- `node scripts/generate-command-catalog.mjs --check --accept-stated-count 98/12`: "up to date" (hash 658e191a...3413b). Warning printed: spec §11 states 98 pairs / 12 namespaces, disagreeing with its action table; accepted by flag. Pre-existing spec inconsistency, not a failure.

## 4. Targeted filter run (`--no-build`, 7 classes)
- Unit: 246 tests executed, 246 passed.
- Integration: 67 tests executed, 67 passed.
- Total 313 passed, 0 failed, 0 "no tests matched". New tests confirmed executing (catalog, consumer, audit theories).

## 5. Diff hygiene (step 6)
- `git diff --stat`: 17 files, +252/-48. Changed: docs, src (Application/Domain/Infrastructure), tests.
- `command-catalog.json`: NOT in diff.
- EF migration files: NOT in diff.

## 6. Coverage gap analysis

Method: read the four named test files and the diff; traced each flag to its consumer.

### Catalog level (which rows carry which flag)
- All 108 pinned rows have all three flags asserted:
  - 15 B/D/F rows: explicit table in `Every_physical_destructive_and_flash_row_declares_its_behaviour`.
  - Read + unopened W/C rows: `Reads_and_unopened_writes_declare_no_behaviour` (all false).
  - Opened config writes: `Opened_config_writes_keep_their_audit_payload_and_use_the_write_lane`.
- Positive and negative cases present for all three flags.
- Consistency invariants: `UsesOutputLane => Physical && AffectsOutputs`; `KeepsAuditPayload => Enabled`.

### Consumer level
- `UsesOutputLane` (`CommandLanes.IsWrite`): theory `Every_command_but_reads_and_relay_outputs_takes_the_write_lane` (positive: sms.send, set_all, force_set, reboot, adc, hostname; negative: set_output, pulse, reads). Plus `A_second_sms_waits...` end-to-end through transport.
- `AffectsOutputs` (reply projection and timeout sweep): theory `Only_a_command_that_can_change_an_output_turns_the_state_stale` (positive: set_all, force_set, set_config on reply and timeout; negative: sms.send, sms.delete). `set_output` stale path covered by existing tests.
- `KeepsAuditPayload` (`EfCommandAuditStore.SummarizePayload`): positive pulse (`"duration_ms"` stored), set_hostname (config write), reboot (existing test). Negative: wifi config write, plus theory sms.send, sms.delete, set_all, factory_reset.

### Concrete missing cases that could let a wrong flag value slip through
- **None found.** Every row's three flags are pinned by the catalog tests, and each consumer has a positive and a negative test. A wrong value on any of the 108 rows fails a catalog test.

### Minor, not blocking
1. `Reads_and_unopened_writes_declare_no_behaviour` hardcodes `58 + 18 + 17 - ConfigWriteActions.Count`. Adding or reclassifying a row fails it for a count reason, not a flag reason. Brittle, not a gap.
2. `CommandDefinition` record defaults (all three false) are not pinned by a test. Production always builds from the catalog, so low risk. Hand-built definitions in `DeviceLaneSafetyTests`/`DeviceOutputStateTests` rely on the default.
3. Enabled-behaviour preservation (old class-based rules vs new flags) is not tested as an oracle. I checked it by reasoning: for every Enabled row, new lane/stale/payload values equal the old class-derived values. Enabled B rows are all control relays; enabled F is only `system.reboot`; no enabled D rows. Covered indirectly by the catalog table.
4. Unopened Flash/Destructive rows lost their audit payload (old: stored for all F/D). Not observable at runtime: `SendDeviceCommandHandler` returns on `DeniedUntilActionContract` (line 101) before `ReserveAsync` (line 171).

## Status
Status: DONE

Summary: build clean (0 new warnings), unit 709/0, node 33/0 and catalog check up to date, targeted filter 313/0 executed. All 76 integration failures are the expected ca.crt FileNotFoundException; no real regressions. The flags are pinned for all 108 catalog rows with consumer positive and negative tests.

Concerns/Blockers: none blocking. Integration suite stays red until the local broker cert exists (environmental).

Unresolved questions:
- Only `SendDeviceCommandHandler` was checked for the denial-before-audit ordering. Other `ReserveAsync` callers were not audited.
- Spec §11 counts (98 pairs / 12 namespaces) disagree with its action table; the generator accepts them via flag. Owner should reconcile the spec.
