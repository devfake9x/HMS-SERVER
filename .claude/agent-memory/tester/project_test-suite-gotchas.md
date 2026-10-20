---
name: test-suite-gotchas
description: HMS-SERVER dotnet test quirks seen during mutation runs - flaky SignalR tests, a hang-prone command test, -v q output format, CRLF sources
metadata:
  type: project
---

Recurring test-suite behaviour (observed 2026-10-06, phase 3 mutation run, 104 `dotnet test` invocations):

- Flaky under machine load (about 9% of runs): `SignalRConnectionBacklogTests.Emits_runtime_metrics_for_drop_retry_and_resynchronization` and `SignalRDeviceEventDeliveryServiceTests.Failed_connection_is_retried_without_replaying_to_a_successful_connection`. They reference no authorization code.
  **Why:** they produce false kills and red baselines.
  **How to apply:** the lead's rule is to rerun a mutant once when SignalR tests are its only failures and report both results. Never let a SignalR-only failure classify a mutant.
- `CommandBoundaryTests.Concurrent_same_correlation_claims_transport_once` awaits `transport.Entered.Task` with no timeout. Any regression that stops the first command before the transport hangs `dotnet test` forever.
  **How to apply:** when mutating `SendDeviceCommandHandler`, run with a watchdog that kills the whole process tree (`taskkill /T`). Killing only dotnet.exe orphans testhost.exe and locks `bin/`. Name the hanging test with `--blame-hang-timeout 30s --blame-hang-dump-type none`.
- `dotnet test <slnx> --nologo -v q` (xUnit 2.9.3 on VSTest) prints failures only as `[xUnit.net hh:mm:ss] <FQN> [FAIL]` plus `Passed!/Failed! - Failed: N, Passed: N, ... - <dll>` summaries. There are no `Failed <name> [ms]` lines.
- All `src/**/*.cs` files are CRLF without BOM. Any harness that edits sources must restore raw bytes.
- Other agents often run tests against the repo's build output, so build and test only in a scratch copy (tar without bin/obj). A full green suite is 372 unit + 94 integration tests in about 9 s.

Related: [[mutation-harness-pattern]]
