# Tester report: DO relay control (uncommitted)

Date: 2026-11-06. Worktree: `D:\WORKSPACE\PROJECT\HMS-SERVER\.claude\worktrees\mqtt-device-connection-36f705`.
Scope: full verification, no test files changed, no production code touched, nothing committed.

## Results

| Step | Result |
|---|---|
| `dotnet build` (solution root) | 0 errors, 0 warnings |
| `dotnet test tests/Hms.Server.UnitTests` | 468 passed, 0 failed, 0 skipped (1 s) |
| `dotnet test tests/Hms.Server.IntegrationTests` | 107 passed, 0 failed, 0 skipped (3 s) |
| `npx vitest run` (frontend) | 31 files, 157 tests passed, 0 failed (9 s) |
| `npx vue-tsc --noEmit` | exit 0 |
| EF migrations present | `20261106012159_AddAllowedOutputChannels` and `20261106013027_AddCommandAuditIssuerAndDetail`, both with `.Designer.cs` |
| Snapshot columns | `AllowedOutputChannels`, `IssuedBy`, `Detail`, `PayloadSummary` all present |
| `dotnet ef migrations has-pending-model-changes` (Infra, startup Api, `--no-build`) | "No changes have been made to the model since the last migration", exit 0. dotnet-ef 10.0.10 is installed globally. |

Single run per suite. Flakiness not checked (no repeat runs).

## Acceptance criteria vs tests

Covered:
- `set_output` payload exact (int channel, bool value, no extra keys): `PhysicalOutputSafetyTests.cs:47` (theory of 10 bad payloads).
- `duration_ms` 1..60000 bounds: `PhysicalOutputSafetyTests.cs:58`. Also 60001, 0, string, missing.
- Allowlist null/empty fail-closed: `PhysicalOutputSafetyTests.cs:64`.
- Refused command never signed or published: `PhysicalOutputSafetyTests.cs:94`.
- TTL clamped to 10 s, `ttl_ms` on the wire: `PhysicalOutputSafetyTests.cs:112`. Also `CatalogTests.cs:175`, `:321`.
- PUT `output-channels` master-admin only (manager gets 403), validation (channel 0 gets 400), survives rebinding: `ManagementApiTests.cs:262`.
- Output state fed only by verified `get_status` reply; heartbeat/forged/other-client replies ignored: `DeviceOutputStateTests.cs:31`, `:53`.
- Stale on offline, reboot, timeout, executed, duplicate: `DeviceOutputStateTests.cs:73` (theory). Fresh read clears stale: `:109`.
- Refused-before-send keeps last known state: `DeviceOutputStateTests.cs:121`.
- Audit records IssuedBy, PayloadSummary, Detail: `PersistenceModelTests.cs:324`.
- Rows left Published at startup become `OUTCOME_UNKNOWN_AFTER_RESTART`: `PhysicalOutputSafetyTests.cs:183` (simulated, in-memory). Expired reservation: `PersistenceModelTests.cs:247`.
- Early refusal leaves a structured log: `PhysicalOutputSafetyTests.cs:199`.
- `GET /outputs` gated by `gpio.do_view`: `ManagementApiTests.cs:298`.
- `outputs` realtime event carries no relay data: `DeviceOutputStateTests.cs:31`. Weak: asserts only that the key `channels` is absent.
- Heartbeat/telemetry cannot write DO state: `DeviceOutputStateTests.cs:53`, `MaitechRtuDeviceIntegrationTests.cs:168`.
- `POST /telemetry` without CSRF rejected: `MaitechRtuDeviceIntegrationTests.cs:59`.
- Relay write permission is high risk: `PhysicalOutputSafetyTests.cs:212`.
- Pinned catalog opens only the two relay outputs: `CatalogTests.cs:114`.
- UI: no invented DO/DI data: `DeviceOutputsPanel.test.ts:56`, `DeviceDetailView.test.ts:140`.
- UI: channel locked, read back after device acts, unlock only then: `DeviceOutputsPanel.test.ts:78`, `:107`.
- UI: refused-before-send unlocks at once, skips read-back: `DeviceOutputsPanel.test.ts:126`.
- UI: pulse reads back again after pulse ends: `DeviceOutputsPanel.test.ts:153`. Exact read-back count not inspected.
- UI: C2 modal offers no relay writes (set_output, pulse, set_all): `DeviceCommandModal.test.ts:163`.

## Gaps (criteria with no test, or only partial)

1. `POST /devices/{id}/telemetry` by a non-master-admin returns 403. No test. Gate is `DevicesController.cs:110`. `TelemetryIngestion_WithoutCsrf_IsRejected` only proves the 400 from antiforgery, not the role gate.
2. `PUT /link/output-channels` without CSRF is rejected. No negative test. `PutJsonAsync` helper always attaches the token.
3. `GET /devices/{id}/outputs` returns 404 `OUTPUTS_NOT_READ` before first read (`DeviceLinkController.cs:56`). No test references this code.
4. `MqttLinkOptions.Validate` has no tests. Untested: `MQTT_TIMING_INVALID` when `ResponseTimeoutSeconds*1000 <= TTL` (`MqttLinkOptions.cs:48`), `MQTT_MAX_PULSE_INVALID`, `MQTT_TIMING_INVALID` range checks. This is the code-level guard for the TTL-vs-timeout rule.
5. Migrations never applied in tests. All integration tests use `UseInMemoryDatabase`. The new migrations' SQL is verified only by the model diff. Not covered: upgrade of existing rows (default for `AllowedOutputChannels`), `IssuedBy`/`Detail` on populated tables, Postgres/Citus DDL.
6. Live acceptance from plan Phase 4 is not automated and was not run (per brief, no real broker or gateway). Not covered: DO1 ON round trip on gateway, DO2 `DO_CHANNEL_NOT_ALLOWED` with sniffer, pulse on a `toggle` channel returning `DEVICE_REJECTED`, DO1–DO6 on simulator, timeout on simulator, restart mid-command with a real process restart. Only unit stand-ins exist.
7. Restart test (gap-list item 5 above, covered in unit) simulates startup; it is not a process restart.

## Concerns

- TTL direction conflicts with the brief. The brief says "MQTT response timeout must be <= that (10 s)". Code and plan say the opposite: response timeout must be greater than TTL. Evidence: `MqttLinkOptions.cs:48` throws if `ResponseTimeoutSeconds*1000 <= PhysicalCommandTtlMs`. Default is 30 s (`MqttLinkOptions.cs:27`). Test `PhysicalOutputSafetyTests.cs:124` asserts `TTL < timeout`. Plan red-team finding #3 (TTL 120 s > timeout 30 s was the bug) supports the code. Code not changed. Needs user confirmation that the brief wording is a typo.

## Test changes

None. No test was edited and nothing was committed or pushed.

## Unresolved questions

1. Is the TTL rule "TTL < response timeout" (code/plan) or "response timeout <= 10 s" (brief)?
2. Should gaps 1-4 get tests before merge? Gap 1 and gap 3 are small and security-relevant.
3. Should the new migrations be verified against a real Postgres/Citus instance (existing rows, FK/distribution DDL)? Repo notes say Citus DDL has fragile rules.

Status: DONE_WITH_CONCERNS
Summary: Build 0 errors/0 warnings; unit 468/468, integration 107/107, vitest 157/157 (31 files), vue-tsc exit 0, EF model has no pending changes. Four criteria have no or partial test coverage (telemetry non-admin 403, outputs 404 OUTPUTS_NOT_READ, MQTT timing/pulse validation, real-DB migration), and the brief's TTL wording conflicts with the code.
