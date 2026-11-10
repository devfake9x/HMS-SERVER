# Code review: safe reboot, server + tools + docs

Plan: `plans/261109-2250-safe-reboot/` | Branch: `claude/mqtt-device-connection-36f705` (uncommitted) | Reviewed 2026-11-10 08:49-09:15
Scope: `src/`, `tests/`, `tools/device-reboot-check.js`, `tools/mqtt-broker/spec-device-simulator.js`, `docs/`. `frontend/` is out of scope (separate review), but one finding below also hits it.
No in-scope file changed during the review (mtime check against 08:49). Spec read only via `git show HEAD:engineering-docs/api/mqtt-api.md` (HMS-IDF HEAD 3c5350511; the spec file is unchanged since 9bfc16854, the plan's pin).

## Verdict

No Critical or High finding. The gate, tracker, CAS audit writes, master-admin rule and catalog change do what the plan says, and the tests are real (13 of 16 mutants killed; the 3 survivors are listed). Three Medium findings: one config-dependent break of the "hold outlives the pending entry" invariant, and two in the tools (a reboot whose publish was unconfirmed is reported as "refused before sending"; the simulator models the DO `busy` reply in a non-spec shape, which hides that the tool's and the UI's retry regex miss the spec shape).

## How this was checked

- Diff `git diff -- src tests tools docs` plus the 6 untracked in-scope files, read in full: `DeviceRebootTracker.cs`, `DeviceRebootFlowTests.cs`, `DeviceRebootTrackerTests.cs`, `DeviceLinkWiringTests.cs`, `device-reboot-check.js`, `docs/journals/261109-safe-reboot.md`.
- Build and tests in a scratch copy (tar of `Hms.Server.slnx`, `src`, `tests`, no bin/obj), so the running API's locks were never touched and the repo's bin/obj did not change.
- 4 behaviour probes (scratch-only xUnit file) and a 16-mutant campaign (scratch-only, raw-byte restore, scratch `src` diffed equal to the repo afterwards).
- `node --check` on both JS files; `--plan` and `--self-check` run offline (no login, nothing sent). No network, broker or device contact.

## Critical

None.

## High

None.

## Medium

### M1. A `ResponseTimeoutSeconds` above ~120 s lets the reply-timeout overwrite `OBSERVED` and ends the hold while the command is still pending
- Where: `DeviceRebootTracker.cs:132` (hold = accepted + 2 s + 120 s, fixed), `DeviceLinkHandlers.cs:141-147` (timeout write is the unconditional `RecordAsync`), `MqttLinkOptions.cs:51` (allows `ResponseTimeoutSeconds` 21..600). The comment at `DeviceLinkHandlers.cs:157-158` ("raises the timeout row written just above, never the other way round") only holds while the pending deadline is shorter than the hold.
- Failure (probe A, `ResponseTimeoutSeconds = 180`, run in scratch): heartbeat drop at 20 s; at 123 s the hold ends and the row becomes `Executed/DEVICE_REBOOT_OBSERVED (reply=none)`; at 181 s the sweep times out the still-pending entry and writes `Failed/DEVICE_RESPONSE_TIMEOUT` over it. A reply arriving between 122 s and the timeout finds no hold: `Upgrade` returns null, the row says `OUTCOME_UNKNOWN tracking=unavailable`, and the gate is open (the 60 s mark has passed) as soon as the lane frees. A slow 4G gateway is exactly where someone would raise this timeout.
- Fix (any one): (a) in `MqttLinkOptions.Validate()` require `ResponseTimeoutSeconds < (DeviceDelay + Window)` seconds; or (b) pass the pending deadline into `Admit` and set the held deadline to `max(accepted + 122 s, pendingDeadline + 2 s)`; and/or (c) make the sweep's timeout write skip rows already carrying a `DEVICE_REBOOT_*` code. (a) is one line; (b) is the robust one.

### M2. `device-reboot-check.js` reports a possibly-sent reboot as "refused before sending"
- Where: `tools/device-reboot-check.js:124-129` (`refused()` returns true for every `status === 'Failed'`), used at `:397` for the real reboot and at `:138` (which makes the `MQTT_PUBLISH_UNCONFIRMED` branch at `:139` dead code).
- Failure: the server answers a real reboot with `Failed/MQTT_PUBLISH_UNCONFIRMED` when the broker did not acknowledge the publish (`MqttDeviceCommandTransport.cs:125-129`: "the device may still run it"). The tool prints "The server refused the reboot before sending it.", does not follow the audit row, and exits. The device may restart; an operator who trusts the message re-runs `--reboot` later. The server hold covers the first ~122 s only. This also departs from the precedent `certainlyNotSent()` in `tools/device-config-check.js:62-67`, which deliberately does not treat `Failed` as "not sent".
- Fix: keep the precedent rule and add the pre-sign refusals explicitly, e.g. `status === 'Failed' && !['MQTT_PUBLISH_UNCONFIRMED','IDEMPOTENCY_OUTCOME_UNKNOWN','IDEMPOTENCY_IN_PROGRESS'].includes(code)`; in `runReboot`, follow the row (`follow()`) on `MQTT_PUBLISH_UNCONFIRMED` like on `Published`.

### M3. Simulator `busy` reply is not the spec shape, so the `^busy` retry in the tool (and the UI) is unverified and would not fire on a real device
- Spec (§6.4 "DO control: lý do từ chối", L822, L831, L838): a refusal is `success:false`, `error:"ESP_ERR_INVALID_STATE"`, `data.error:"busy"`, `data.retry_after_ms:1000`; `control.get_status` answers `busy` while the driver is not ready.
- Simulator `spec-device-simulator.js:124`: `{ error: 'busy', data: { retry_after_ms: 1000 } }` (busy in the envelope `error`, the R3a admission position). The server turns that into detail `"busy"`; for the spec shape it gives `"ESP_ERR_INVALID_STATE: busy"` (`DeviceLinkHandlers.cs:248-253`).
- Tool `device-reboot-check.js:161-162` retries only on `/^(LANE_FULL|busy|UNAUTHORIZED)/i`, which matches the simulator and not the spec. On the gateway, `--after` right after a boot would not retry and would FAIL "relay table readable before and after"; the simulator run could not show this. The same anchored regex is in `frontend/src/core/commands/useDeviceReboot.ts:135` (pass to the frontend reviewer: the UI's "retry while busy" path, an acceptance item, would end in "Chưa kiểm được trạng thái relay" on real hardware).
- Fix: simulator returns `{ error: 'ESP_ERR_INVALID_STATE', data: { error: 'busy', retry_after_ms: 1000 } }`; tool (and UI) match `busy` in the handler-error part, e.g. `/(^|:\s*)(LANE_FULL|busy|UNAUTHORIZED)\b/i`, with a test on `"ESP_ERR_INVALID_STATE: busy"`.

## Low

- **L1. Rebind/revoke racing a reply leaves the row `SCHEDULED` for good.** `DeviceLinkHandlers.cs:93-123`: `Upgrade` (tracker now `Tracking`) happens before the unconditional `RecordAsync(SCHEDULED)`, with awaits in between. If `Abandon` (HTTP thread) and a sweep (`TryCompleteAsync` from `{SCHEDULED}`) land in that window, the CAS fails because the row is still `MQTT_PUBLISHED`, `CompleteRebootAsync` (:177-182) logs and drops it, then the reply writes `SCHEDULED`. Probe R reproduced it: still `Executed/DEVICE_REBOOT_SCHEDULED` after 15 min of sweeps (in-memory store: forever; Postgres: until the next restart). Narrow window. Fix: in `CompleteRebootAsync`, when the CAS fails and the row is still at a pre-reply code (`COMMAND_IN_FLIGHT`, `MQTT_PUBLISHED`), call `reboots.Retry(completion)` instead of dropping it.
- **L2. A drop seen while only held is lost when the reply then arrives** (as-built note 5). Probe B: drop at 5 s, reply at 6 s gives `SCHEDULED (uptime_before=4)`, the post-boot value, and ends `NOT_OBSERVED (evidence=none)`. Unlikely (the device replies before it restarts), but the detail is wrong when it happens. Fix: in `Upgrade` (`DeviceRebootTracker.cs:199-202`), if `held.Evidence` exists, report the pre-drop uptime as `UptimeBefore` and conclude `OBSERVED` at once (queue a completion from `{SCHEDULED}`).
- **L3. `Acknowledged` then restart is written as `reply=none`.** Probe C: `Executed/DEVICE_REBOOT_OBSERVED [...; reply=none]` although a `DEVICE_ACCEPTED` reply came (`DeviceRebootTracker.cs:308-313` always passes `reply: false`). Fix: record in the held record that a reply arrived (`reply=accepted`).
- **L4. The ObserveHeartbeat "before the mark" guard has no test.** Mutant M13 (drop `|| at <= record.AcceptedAt` at `DeviceRebootTracker.cs:217`) survives unit and integration. `Heartbeats_that_prove_nothing_end_nothing` (`DeviceRebootTrackerTests.cs:146-158`) sends its "before the mark" heartbeat while no record exists, so the guard is never reached. Add: admit at T0, then a lower-uptime heartbeat with `at` = T0-1 s (an inbound backlog), expect no evidence.
- **L5. Short broker-link drops are invisible to the tracker.** `MqttDeviceLinkService.cs:103-121` reconnects before it sweeps, so an outage that ends within one loop never reaches `TakeDue(..., brokerConnected:false)`. The server uses a clean session (`:172`), so heartbeats in the gap are lost. Impact is small (later heartbeats still show the drop); the "no conclusion while the link is down" claim is per sweep, not per outage. Option: have the link service record "disconnected since last sweep" in `OnDisconnectedAsync` and pass that.
- **L6. A real reboot during the 10-minute tail silently replaces the tail record** (`DeviceRebootTracker.cs:175`), so the first reboot's `NOT_OBSERVED` can no longer be raised. Acceptable; say it in the docs or keep the tail on a separate slot.
- **L7. Simulator forced-channel refusal uses free text and a wrong comment.** `spec-device-simulator.js:134-135` says the spec does not define it; spec L828 defines `data.error:"channel_forced"`. Use that code.
- **L8. Docs: early release list incomplete.** `docs/architecture-mqtt-device-link.md:553` says the hold is released early "chỉ khi" `DEVICE_REJECTED` or never-sent; rebind to another topic and revoke also release a held (no-reply-yet) reboot silently (`DeviceRebootTracker.cs:263-265`, by plan). Add them.
- **L9. Docs/UI wording vs firmware age.** Spec L850 (unchanged since the pin): before the 2026-11-08 firmware change, a force-OFF relay could switch ON for about 0.14 s at each restart. `docs/architecture-mqtt-device-link.md:575` (and the dialog) says "kênh force TẮT giữ TẮT" without a firmware condition. For the deferred gateway run, add a preflight check that the firmware includes D-667, or add the caveat.
- **L10. Startup log text.** `Program.cs:164` "left published by a previous run" now also counts watched reboots (`FailUnfinished` adds `SCHEDULED` rows). Cosmetic.
- **L11. Test hygiene.** `DeviceLinkWiringTests.cs:31` builds the provider without `ValidateScopes = true`, so a captive scoped dependency in the singleton tracker would pass; `SweepExpiredAsync(now, ct, bool brokerConnected)` puts a parameter after the `CancellationToken` (CA1068).

## Edge cases from scouting (not defects)

- Lock order verified: `_laneGate` → tracker `_gate` only through `admit`; the tracker never calls the registry; `Upgrade`/`Disarm`/`TakeDue` run outside the lane lock; `Remove` is lock-free. No deadlock path.
- Gaps between `Remove`/NeverSent `Remove` and `Upgrade`/`Disarm` only make a concurrent reboot see `IN_PROGRESS` (conservative). Covered by the interleaving test.
- Reply path and heartbeat path share the single inbound reader (`SingleReader = true`), so `RecordAsync(SCHEDULED)` always lands before a heartbeat's CAS; only the sweep and HTTP threads run concurrently (L1).
- `command_result` is not a coalescible realtime type (`RealtimeContracts.cs:179-183`), so each code change is delivered as its own event.
- Single publish path: only `SendDeviceCommandHandler` reaches `IDeviceCommandTransport`; no other route can send a reboot around the master-admin check or the gate.
- No `/link` unbind path exists, so rebind and revoke are the only identity changes the tracker must see.

## Checklist (a)-(e)

### (a) Acceptance criteria, server and tools

| Criterion | Result | Evidence |
|---|---|---|
| Catalog opens only `system.reboot` (34 open); 3 other F rows `DENIED_UNTIL_ACTION_CONTRACT`, also for master-admin | Met | `PinnedCommandCatalog.cs:115-119`, :254; `CatalogTests` (34), `ManagementApiTests` factory_reset by admin |
| Master-admin at send, before audit reservation and signing; `allowed-commands` master-only | Met | `SendDeviceCommandHandler.cs:123-127` (before `ReserveAsync` :171); controller :71; mutants M18, M19 killed |
| Payload `{}` only, refused before the key is read | Met | `ConfigCommandPolicy.cs` reboot rule; adapter test with counting secrets |
| Confirm / dry-run unchanged; dry-run not held or tracked | Met | adapter tests; `A_dry_run_reboot_is_answered_without_holding_or_tracking`; M12 killed |
| TTL 10 s | Met | `SendDeviceCommandHandler.cs:143-147`; test asserts 00:00:10 |
| Gate inside `TryRegisterWrite`, after busy and rate checks, before the slot | Met | `DeviceLinkContracts.cs:349-353`; 11-refusals test; M1 killed |
| Hold from admission; 60 s `TOO_SOON` | Met (M1 caveat for long timeouts) | `DeviceRebootTracker.cs:158-179`; M2, M17 killed |
| Release only on `DEVICE_REJECTED` / NeverSent | Met; rebind/revoke also release a hold, by plan (L8 docs) | `DeviceLinkHandlers.cs:219-223`, transport :116-123; M4, M15 killed |
| Tracker required everywhere; singleton | Met | 5 non-optional ctor params; `DependencyInjection.cs:41`; M7 killed by `DeviceLinkWiringTests` |
| Uptime from the heartbeat message | Met | `DeviceLinkHandlers.cs:339`; store-poisoning test |
| `TryCompleteAsync` compare-and-set | Met (in-memory and EF in-memory provider tested; relational review-only, see (b)) | M20 survives as expected |
| 10-minute tail; `reply=none` upgrade | Met (L2, L3, L6 edge cases) | tracker and flow tests; M16 killed |
| No conclusion while broker link down | Met per sweep (L5) | M9 killed |
| Rebind/revoke → `OUTCOME_UNKNOWN` | Met for tracked reboots (L1 race) | flow tests; M14 killed |
| Class F `PayloadSummary` dry-run marker | Met (EF store only; the in-memory audit store keeps no summary, as before) | `EfCommandAuditStore.cs:288-293`; `PersistenceModelTests` |
| DO stale reason at schedule | Met (only when relay state is known) | `DeviceLinkHandlers.cs:109-111` |
| `command_result` on every code change | Met | `CompleteRebootAsync` publishes after a successful CAS only |
| Tool: closed-row catalog check first; every pre-sign probe `dryRun:true`, live probes no confirm, `{}`; never resends | Met; M2 is the one wrong "not sent" | `--self-check` PASS offline |

### (b) Regressions in touchpoints and callers

- `SendDeviceCommandHandler`, `ConfigCommandPolicy`, `PinnedCommandCatalog`, lanes/registry, transport, correlator, inbound, sweep, `DeviceOutputState`, DI: no regression found. Non-reboot writes pass `admit: null`; DO relay and config-write tests unchanged and green; mechanical ctor changes only in the 13 transport, 4 binding/correlator/inbound and 4 revoke call sites.
- `EfCommandAuditStore` relational branch (review only, no Postgres):
  - `TryCompleteAsync` (:214-226): one `ExecuteUpdateAsync` with `DeviceId`, `CorrelationId`, `Code != DEVICE_REVOKED` and `codes.Contains(Code)` in the WHERE, so it is atomic per row; it is translated as a parameterised `= ANY(...)` (standard EF/Npgsql).
  - `FailUnfinished` (:56-70): two statements with disjoint predicates (no `SCHEDULED` row is `Published`/`IN_FLIGHT`), no transaction. A crash between them is repaired by the next startup. Status is kept, as the plan asks.
  - Residual: with two instances (rolling deploy), a starting instance flips rows the other is still watching. Single-instance assumption, already documented.
  - Mutants M20 and M21 confirm that no test covers these lines.
- EF in-memory-provider `TryCompleteAsync` is read-check-write and not atomic; it is dev-only, and the tracker emits at most one completion per record from `{SCHEDULED}`.
- Races and leaks: tracker `_devices` grows by one entry per device (bounded); `_ready` is bounded by 3 retries per completion. Sweep-vs-reply race covered by `StealOnRemove`; sweep-vs-HTTP race is L1.

### (c) Public contract changes

C# (all intended unless marked):
- `IPendingDeviceCommandRegistry.TryRegisterWrite(..., Func<string?>? admit = null)`: interface change; implementers must update.
- `ICommandAuditStore.TryCompleteAsync(plan, result, fromCodes, ct)`: new interface member.
- Constructors gain a required `IDeviceRebootTracker`: `MqttDeviceCommandTransport`, `DeviceCommandResponseCorrelator` (inserted before `logger`), `DeviceInboundMessageHandler`, `ConfigureDeviceMqttBindingHandler`, `RevokeDeviceHandler`.
- `MqttDeviceCommandTransport` also gains `TimeProvider? clock = null`. Not in the plan; a test seam. DI resolves a registered `TimeProvider` if one exists, else the system clock. Harmless.
- `DeviceCommandResponseCorrelator.SweepExpiredAsync(now, ct, brokerConnected = true)`; new public `CompleteRebootAsync`.
- New public types: `DeviceRebootCodes`, `RebootTracking`, `RebootCompletion`, `IDeviceRebootTracker`, `InMemoryDeviceRebootTracker`, `DeviceTelemetryParsing`.
- New members: `PinnedCommandCatalog.FlashActions`, `CommandPermissionResolver.RequiresMasterAdmin`, `OutputStaleReasons.DeviceRebootScheduled`, `CommandTransportOutcome.RebootWatchLostDetail`.
- `FailUnfinished` return value now also counts `SCHEDULED` rows (semantic; L10).

HTTP, realtime and data:
- `GET /me/command-catalog`: `system.reboot` now `available:true` for every caller (class F, `ttlMs` 60000 unchanged), including non-master-admins. Intended (Decision 2/5).
- `GET /devices/{id}/allowed-commands`: `system.reboot` for master-admins only.
- `POST /devices/{id}/commands` for `system.reboot`:
  - a non-master holding `sys.reboot` now gets `Failed/HIGH_RISK_REQUIRES_MASTER_ADMIN` (was `ContractUnavailable/DENIED_UNTIL_ACTION_CONTRACT`);
  - new `ContractUnavailable/DEVICE_REBOOT_IN_PROGRESS` and `ContractUnavailable/DEVICE_REBOOT_TOO_SOON`;
  - a non-empty payload gives `COMMAND_PAYLOAD_INVALID`; the signed `ttl_ms` is 10000.
- `GET /commands/{id}`: new codes `DEVICE_REBOOT_SCHEDULED|OBSERVED|NOT_OBSERVED|OUTCOME_UNKNOWN`, all with status `Executed`.
  - Unintended-looking, call out to consumers: a row can move from `Failed/DEVICE_RESPONSE_TIMEOUT`, `Failed/MQTT_PUBLISH_UNCONFIRMED` or `Acknowledged/DEVICE_ACCEPTED` to `Executed/DEVICE_REBOOT_OBSERVED` about 2 min later. Until now a `Failed` result was final. The reboot UI handles it; other readers of the audit (reports, generic pollers) should be told.
- Realtime: several `command_result` events per `correlationId`; `outputs.staleReason = device_reboot_scheduled`.
- Audit data: class F `PayloadSummary` is now `{"dry_run":bool,"payload":{...}}`; class D keeps the raw payload. No migration (`has-pending-model-changes`: none).
- Config keys: none added. `Mqtt:ResponseTimeoutSeconds` now has an unenforced upper coupling (M1).

### (d) Pattern conformance (precedent 261106-* plans)

- Server: follows the established pieces: a payload rule per opened write in `ConfigCommandPolicy`, the atomic lane step, the revoke fence copied from `RecordAsync` into `TryCompleteAsync`, refusals before audit logged like the other pre-audit refusals, comments that state the invariant. No generic helper or parallel reimplementation. Code comments cite user-decision dates and spec sections, not plan IDs or finding codes.
- Tool: copies the `device-config-check.js` login/CSRF/`tool-env.js` pattern. Its "not sent" rule regressed from the precedent (M2).
- Simulator: env-flag style consistent. Two spec-shape deviations (M3, L7). `SIM_REBOOT_NO_REPLY` now models "effect happened, reply lost", which closes the old `SIM_SILENT` blind spot.

### (e) Build and test status (scratch copy of the exact tree)

| Gate | Result |
|---|---|
| `dotnet build Hms.Server.slnx --no-incremental` | 0 errors, 3 warnings, all pre-existing baseline: CS8631 `ManagementApiTests.cs:357`, 2x xUnit1031 `GrantSerializationTests.cs:33,68`. No new warning |
| `dotnet test tests/Hms.Server.UnitTests` | 633 passed, 0 failed |
| `Mqtt__Enabled=false dotnet test tests/Hms.Server.IntegrationTests` | 123 passed, 0 failed |
| `dotnet ef migrations has-pending-model-changes` | "No changes have been made to the model since the last migration." |
| `node --check` both JS files; `--self-check`; `--plan` | OK; nothing sent |
| Locks | None hit: built in a scratch copy; the running API's output and the repo's bin/obj were not touched |

Mutation campaign (16 mutants, scratch):
- Killed (13): M1 `admit` skipped, M2 dry-run bypasses the hold, M3 reply applied before `Remove`, M4 no `Disarm` on NeverSent, M7 scoped tracker, M9 link-down ignored, M12 dry-run tracked, M14 `Abandon` keeps the uptime, M15 `Acknowledged` disarms, M16 hold end ignores the evidence, M17 no `TOO_SOON`, M18 no master-admin check, M19 no `allowed-commands` filter.
- Survived (3): M13 (L4, test gap); M20 and M21 (relational branches, expected and documented as review-only).
- One M21 run first showed a unit failure that did not repeat. This is the known flaky `SignalRConnectionBacklogTests` metrics test from the phase 1 report; on rerun M21 survived and the unit suite was 633/633.

Probes (scratch-only, all reproduced the stated behaviour): A (M1), B (L2), C (L3), R (L1).

Docs fact-check: 34 open actions (26+2+5+1) matches `CatalogTests`; the codes, the master-admin rule, the 10 s TTL, the 60 s/122 s marks, the tail, the CAS and the PayloadSummary shape match the code. Gateway is marked deferred in the architecture doc, roadmap, journal and catalog doc. "ACL broker bắt buộc trước production" is present in the architecture doc, roadmap and journal. Inaccuracies: L8, L9.

## Recommended actions (priority order)

1. M1: add the `ResponseTimeoutSeconds` bound in `MqttLinkOptions.Validate()`, or derive the hold from the pending deadline. Add a test with timeout 180.
2. M2: restore the precedent "certainly not sent" rule in `device-reboot-check.js`; follow the row on `MQTT_PUBLISH_UNCONFIRMED`.
3. M3: fix the simulator `busy` shape. Widen the tool regex and tell the frontend reviewer about `useDeviceReboot.ts:135`. Re-run the busy-retry scenario on the simulator.
4. L1, L4: retry a CAS that lost to a not-yet-recorded reply; add the backlog-heartbeat test.
5. Docs: L8, L9 (+ firmware-version preflight item for the deferred gateway run); L7 simulator code.

## Plan follow-ups (no plan file edited)

Phase 1 success criteria are met except as caveated in M1/L1. The phase 3 simulator and tool criteria are met, but the busy-retry evidence is simulator-shaped (M3), so the gateway step should not rely on it until M3 is fixed. The gateway step is deferred and correctly not counted.

## Unresolved questions

- Is a production `ResponseTimeoutSeconds` above 120 s planned (4G gateways)? If so, M1 rises to High.
- Should non-master-admins see `system.reboot` as `available:true` in `/me/command-catalog`? The plan says yes; confirm the UI does not read it as permission.

Status: DONE_WITH_CONCERNS
Summary: Server gate, tracker, CAS audit and master-admin rule meet the plan and are test-backed (build clean, 633 unit + 123 integration green, 13/16 mutants killed); no Critical/High. Three Medium issues: a config-dependent hold/timeout invariant break, the tool calling an unconfirmed publish "not sent", and a non-spec simulator `busy` shape that masks a retry-regex miss in the tool and UI.
Concerns/Blockers: relational EF branches remain review-only (mutants M20/M21 survive by design); M3 also affects `frontend/src/core/commands/useDeviceReboot.ts:135` (for the frontend review).
