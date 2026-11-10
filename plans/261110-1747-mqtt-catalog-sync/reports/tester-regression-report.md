# Regression report: command-catalog re-pin (94 -> 108 rows)

Date: 2026-11-10. Branch: claude/mqtt-device-connection-36f705. Worktree: ck-project-management-review-dd19d6.
No source, test, script or doc modified. Build output (bin/obj) regenerated only.

## Results

| # | Suite | Command | Passed | Failed | Skipped | Expected | Duration | Status |
|---|---|---|---|---|---|---|---|---|
| 1 | Build | `dotnet build Hms.Server.slnx --nologo --no-incremental` | - | 0 errors | - | 3 warnings | ~11 s | OK |
| 2 | Unit | `dotnet test tests/Hms.Server.UnitTests/Hms.Server.UnitTests.csproj --no-build -v q` | 686 | 0 | 0 | 686 | 3 s (run) / 10 s (wall) | OK |
| 3 | Integration | `Mqtt__Enabled=false dotnet test tests/Hms.Server.IntegrationTests/... --no-build -v q` | 143 | 0 | 0 | 143 | 4 s (run) / 9 s (wall) | OK |
| 4 | Node | `node --test scripts/*.test.mjs` (Node v24.18.0) | 32 | 0 | 0 | 32 | 0.31 s | OK |
| 5a | Frontend tests | `npx vitest run` (in frontend/) | 537 (41 files) | 0 | 0 | 537 | 18.9 s | OK |
| 5b | Frontend typecheck | `npm run typecheck` (`vue-tsc --noEmit`) | - | 0 errors | - | clean | ~10 s | OK |

Totals: 1398 tests run, 1398 passed, 0 failed, 0 skipped.

Counts equal or exceed expected on every suite. No flaky reruns needed.

## Build detail

- 3 warnings, 0 errors. Matches baseline.
- `tests/Hms.Server.IntegrationTests/ManagementApiTests.cs(366,9)`: CS8631 (`string?` vs `Assert.Equal<T>(ReadOnlySpan<T>, T[])` nullability). File is in the uncommitted change set. Warning count unchanged, so likely pre-existing line. Not verified against baseline diff.
- `tests/Hms.Server.UnitTests/GrantSerializationTests.cs(33,37)` and `(68,37)`: xUnit1031 (blocking task op in test). File not modified.

## Failed tests

None.

## Skipped / not run

- Generator `--check` (`scripts/generate-command-catalog.mjs --check`): not run. Requires HMS-IDF repo path, excluded by constraint.
- No API, broker, simulator started. Port 443 untouched. HMS-IDF not opened. No secrets or .env read.

## Observations

- Integration and unit tests ran from prebuilt output (`--no-build`) after a clean build, so results reflect current sources.
- Frontend `vitest` emits no failure. Duration includes environment setup (93 s cumulative across workers).

## Unresolved questions

- Is CS8631 at `ManagementApiTests.cs:366` introduced by this change set or pre-existing? Count matches baseline 3, so likely pre-existing. Confirm with `git diff` on that file if needed.

Status: DONE
Summary: All 5 suites green. Build 0 errors / 3 warnings (baseline). Unit 686/686, integration 143/143, node 32/32, vitest 537/537, typecheck clean.
