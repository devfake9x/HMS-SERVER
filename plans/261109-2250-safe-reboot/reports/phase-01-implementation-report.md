# Phase 1 implementation report: safe reboot, server

Plan: `plans/261109-2250-safe-reboot/` | Phase: `phase-01-server-reboot.md` | Branch: `claude/mqtt-device-connection-36f705` | Status: DONE_WITH_CONCERNS (see Concerns)

## Baseline (step 0, before any change)

- Build: 0 errors, 3 warnings (CS8631 in `ManagementApiTests.cs`, 2x xUnit1031 in `GrantSerializationTests.cs`).
- Unit: 571 passed, 0 failed.
- Integration: 117 passed, 0 failed, but only with `Mqtt__Enabled=false`. Without it 55 of 117 fail at baseline with
  `FileNotFoundException ... hms-idf-mosquitto-<guid>\ca.crt`: the Development user secrets of `Hms.Server.Api`
  enable the live MQTT link with a CA path in `%TEMP%` that no longer exists, and `WebApplicationFactory` picks them up.
  Environment issue, not code. I did not read the secrets; I overrode the one flag with an environment variable.
  All integration runs below use `Mqtt__Enabled=false`.

## Final

- Build `Hms.Server.slnx --no-incremental`: 0 errors, 3 warnings (same three as baseline).
- Unit: 633 passed (571 + 62 new), 0 failed.
- Integration: 123 passed (117 + 6 new), 0 failed.
- `dotnet ef migrations has-pending-model-changes`: "No changes have been made to the model since the last migration."
- `PhysicalOutputSafetyTests` `FailUnfinished` and `PersistenceModelTests` `FailUnfinished` still count 2 (green, unchanged).

## Tests Before: red, then green

Red runs were taken against a no-op skeleton (new types and signatures present, behavior absent), so every failure is an
assertion, not a compile error. Green after implementation, all in the final counts above.

| Item | Test | Red result |
|---|---|---|
| 1 | `CatalogTests.Pinned_catalog_opens_only_the_listed_reads_relay_outputs_config_writes_and_reboot` (renamed) | `Expected: 34 / Actual: 33` |
| 1 | `CatalogTests.Only_reboot_opens_among_flash_rows` (new) | `Expected: [("system","reboot")] / Actual: []` |
| 1 | `Only_the_system_and_adc_config_writes_...` (reboot removed from `closed`, D/F loop excludes reboot) | stays green before and after, by design |
| 2 | `ConfigCommandPolicyTests.Reboot_takes_an_empty_payload_only` | `Expected null / Actual "CONFIG_ACTION_UNSUPPORTED"` |
| 2 | `ConfigCommandPolicyTests.A_reboot_with_a_payload_is_refused_before_the_key_is_read` (new, adapter + counting secrets) | `Expected COMMAND_PAYLOAD_INVALID / Actual CONFIG_ACTION_UNSUPPORTED` |
| 2 | `Writes_without_a_payload_rule_fail_closed` now uses factory_reset, ota.apply, ota.remote_start; `Every_opened_config_write_has_a_payload_rule` also walks `FlashActions` | green throughout |
| 3 | `DeviceLinkTests.Confirm_token_is_built_from_the_mqtt_device_id` (now `CanPublish`, signed `"confirm":"REBOOT-gateway_870c"`) | `Assert.True() Failure` |
| 3 | `DeviceLinkTests.A_reboot_dry_run_is_signed_with_dry_run_and_confirm_at_the_root` (new) | `Assert.True() Failure` |
| 3 | `DeviceLinkTests.A_reboot_with_a_payload_is_refused_without_reading_the_key` (new) | `Expected COMMAND_PAYLOAD_INVALID / Actual CONFIG_ACTION_UNSUPPORTED` |
| 4 | `DeviceCommandAuthorizationTests.A_reboot_is_signed_for_ten_seconds_on_server_time_whatever_the_client_sends` (new) | `Expected 00:00:10 / Actual 00:00:20` |
| 4 | `...Catalog_ttl_60000_rows_are_capped_to_the_command_ttl_limit` (class F expects `MaxPhysicalCommandTtlMs`; count 24 unchanged) | 3 of 24 items: `Expected 00:00:10 / Actual 00:00:20` |
| 5 | `...A_flash_command_needs_a_master_admin_even_with_the_permission` (new) | `Expected: Failed / Actual: Recorded` |
| 5 | `...The_master_admin_rule_runs_before_the_confirm_check_and_leaves_no_audit_row` (new) | `Expected HIGH_RISK_REQUIRES_MASTER_ADMIN / Actual CONFIRM_REQUIRED` |
| 5 | `...Only_flash_rows_require_a_master_admin` (new) | 4 of 94 rows `Expected True / Actual False` |
| 6 | `DeviceRebootTrackerTests` (most of the file; the few that already passed against the no-op, such as a dry-run on an idle device, are the no-op-correct cases) | e.g. `Expected DEVICE_REBOOT_IN_PROGRESS / Actual null`; `Assert.NotNull() Failure`; `Assert.Single() Failure: collection was empty`; race test `Expected 0 / Actual 1000` |
| 7 | gate tests in `DeviceRebootFlowTests` (interleaved, lost reply, Acknowledged, unconfirmed publish, NeverSent, rejected, observed at 15 s, 11 refusals, other writes, lane busy, dry-run) | red: second reboot published / wrong code (e.g. `A_second_reboot_is_refused_in_the_window...`, `Refused_reboots_do_not_use_up_the_write_slots`) |
| 8 | correlator and inbound tests in `DeviceRebootFlowTests` (scheduled, observed, uptime from message, not observed, link down, late reply, race with sweep, CAS, rebind, revoke, audit retry) | red: code stayed `MQTT_PUBLISHED`/`Scheduled` instead of the new code |
| 9 | `DeviceRebootFlowTests` in-memory `TryCompleteAsync` and `FailUnfinished`; `PersistenceModelTests` x3 (EF in-memory provider: conditional complete, `PayloadSummary` dry_run true/false after a timeout, restart) | red against stub (`TryCompleteAsync` returned false) |
| 10 | `ManagementApiTests.Reboot_is_for_the_master_admin_only_and_is_checked_before_anything_is_signed` (new) + `/me` catalog `available` set now includes `FlashActions` | `Assert.Contains() Failure: Item not found in collection` (admin `allowed-commands` lacked `system.reboot`) |
| 11 | `DeviceLinkWiringTests` x2 (new) | red before DI registration of the tracker (host/DI could not build the correlator) |

Mutation spot-check: moving `admit` after `recent.Enqueue` in `TryRegisterWrite` fails `Refused_reboots_do_not_use_up_the_write_slots` (reverted).

## Files changed

Source:
- `src/Hms.Server.Application/DeviceLink/DeviceRebootTracker.cs` (new): `DeviceRebootCodes`, `RebootTracking`, `RebootCompletion`, `IDeviceRebootTracker`, `InMemoryDeviceRebootTracker`.
- `src/Hms.Server.Infrastructure/Commands/PinnedCommandCatalog.cs`: `FlashActions = {system.reboot}`, `CommandClass.Flash` branch in the open switch, comment with the reasons the other three F rows stay closed.
- `src/Hms.Server.Application/Commands/CommandPermissionResolver.cs`: `RequiresMasterAdmin(definition)`.
- `src/Hms.Server.Application/Commands/SendDeviceCommandHandler.cs`: master-admin check right after the permission check; class F gets the 10 s TTL cap; doc of the cap.
- `src/Hms.Server.Api/Controllers/DeviceCommandsController.cs`: `allowed-commands` hides class F from non-master-admins.
- `src/Hms.Server.Application/DeviceLink/ConfigCommandPolicy.cs`: `("system","reboot") => payload.Count == 0`.
- `src/Hms.Server.Application/DeviceLink/DeviceLinkContracts.cs`: `TryRegisterWrite(..., Func<string?>? admit = null)`, `admit` runs after busy and rate checks, before the slot is counted.
- `src/Hms.Server.Infrastructure/Mqtt/MqttDeviceCommandTransport.cs`: required `IDeviceRebootTracker`, reboot passes `admit`, `Disarm` on NeverSent, refusal texts, optional `TimeProvider`.
- `src/Hms.Server.Application/DeviceLink/DeviceLinkHandlers.cs`: correlator (required tracker, reboot reply handling after `Remove` true, `CompleteRebootAsync`, sweep with `brokerConnected`, DO stale mark), inbound (required tracker, uptime from the message, connectivity), binding handler (required tracker, `Abandon("rebind")`).
- `src/Hms.Server.Application/Authorization/PermissionContracts.cs`: `RevokeDeviceHandler` required tracker, `Abandon("revoked")`.
- `src/Hms.Server.Application/Commands/CommandContracts.cs`: `ICommandAuditStore.TryCompleteAsync`, in-memory implementation, `FailUnfinished` for `Scheduled` rows, `RebootWatchLostDetail`.
- `src/Hms.Server.Infrastructure/Persistence/EfCommandAuditStore.cs`: `TryCompleteAsync` (relational `ExecuteUpdateAsync` with the code condition; in-memory provider branch), class F `PayloadSummary` = `{"dry_run":..,"payload":{..}}`, `FailUnfinished` for `Scheduled` rows.
- `src/Hms.Server.Application/DeviceLink/DeviceOutputState.cs`: `OutputStaleReasons.DeviceRebootScheduled`.
- `src/Hms.Server.Application/Realtime/DeviceTelemetryContracts.cs`: `DeviceTelemetryParsing.UptimeSeconds` extracted, store reuses it (same behavior).
- `src/Hms.Server.Infrastructure/DependencyInjection.cs`: singleton tracker next to the registry, outside every branch.
- `src/Hms.Server.Infrastructure/Mqtt/MqttDeviceLinkService.cs`: sweep passes `_client.IsConnected`.

Tests:
- New: `DeviceRebootTrackerTests.cs`, `DeviceRebootFlowTests.cs`, `tests/Hms.Server.IntegrationTests/DeviceLinkWiringTests.cs`.
- Edited: `CatalogTests`, `ConfigCommandPolicyTests`, `DeviceLinkTests`, `DeviceCommandAuthorizationTests`, `DeviceLaneSafetyTests` (11 transport ctors), `PhysicalOutputSafetyTests` (2 transports, 2 binding handlers), `DeviceOutputStateTests` (correlator, inbound, `BreakableAuditStore.TryCompleteAsync`), `PermissionTests` (2 revoke handlers), `PersistenceModelTests` (2 revoke handlers, 3 new EF tests), `ManagementApiTests` (`/me` set, new HTTP test).

No EF migration. Nothing outside `src/` and `tests/` was edited (this report aside).

## As built (deviations from the phase file)

1. **New test file for items 7 and 8.** Gate, reply and heartbeat tests are in `DeviceRebootFlowTests.cs` (one rig: real transport + real registry wrapped for the interleaving test + tracker + correlator + inbound), not split across `DeviceLaneSafetyTests` and `DeviceLinkTests`. Those two only got the mechanical constructor changes. The in-memory audit tests (item 9 unit part) are in the same file.
2. **Injectable clock in the transport.** `MqttDeviceCommandTransport` takes an optional last parameter `TimeProvider? clock = null` (default `TimeProvider.System`), so the 60 s and 120 s marks are exact in tests. The tracker gets `now` = the pending entry's `PublishedAt`. Not in the plan.
3. **DI wiring test cannot reach the gate with the real link.** `SendAsync` returns `MQTT_NOT_CONNECTED` before the registry when the (never started) real `MqttDeviceLinkService` is not connected. So `DeviceLinkWiringTests` does two things: (a) real live registration resolves the transport (`MqttDeviceCommandTransport`), correlator, inbound, binding handler, revoke handler in two scopes, same tracker instance, and the transport answers `MQTT_NOT_CONNECTED`; (b) a second provider registers a connected fake `IMqttCommandPublisher` over the real one; an `Admit` through another scope's tracker makes the resolved transport answer `DEVICE_REBOOT_IN_PROGRESS`.
4. **Tracker internals.** No `Abandoned` phase: abandoning a tracked reboot queues an `OUTCOME_UNKNOWN` completion that the next `TakeDue` returns (retries share that queue; both are returned even while the broker link is down, since they conclude nothing about the device). `InMemoryDeviceRebootTracker` takes an optional `ILogger` (used only when a retry is given up after 3 attempts). `RebootTracking` carries `Deadline` in addition to `UptimeBefore`. Detail text: `evidence=heartbeat; uptime_before=; uptime_after=; after_s=; connectivity=offline>online; reply=none; late_s=; link_down=true` (only fields with a value; `evidence=none` when a NOT_OBSERVED has nothing else to say).
5. **Known limit:** a drop in uptime seen while a reboot is only `Held` is kept as evidence only for the hold-end path. If the reply then arrives and the reboot is upgraded to tracking, that earlier drop is not carried over (the plan does not say; the device replies before restarting, so this needs a reply later than the restart).
6. **"Old contradictory revoke test":** I found no existing revoke-with-reboot test in `DeviceOutputStateTests` or `DeviceLinkTests`; nothing was replaced. Rebind and revoke are covered by new tests in `DeviceRebootFlowTests`.
7. **Parameter order:** `reboots` is placed before the first optional parameter in each constructor (correlator after `permissions`; inbound after `correlator`; binding handler after `bindings`; revoke handler after `epochs`). Tests were updated accordingly.
8. **Assertion changes outside the listed ones:** none. The listed ones were all applied (`CatalogTests` x2, `ConfigCommandPolicyTests` x2, `Confirm_token_...`, TTL test, `/me` catalog set).
9. **DO stale mark** at reboot schedule only publishes an `outputs` event when relay state is already known (`MarkStale` returns false otherwise, existing behavior).

## Not done / not testable here

- Relational (Postgres) branches of `EfCommandAuditStore.TryCompleteAsync` (`ExecuteUpdateAsync` with `codes.Contains(x.Code)`) and `FailUnfinished` (second `ExecuteUpdate` on `Code == DEVICE_REBOOT_SCHEDULED`) are review-only: no Postgres in dev. Only the EF in-memory provider branches have tests.
- No network, broker or device contact; no live run.

## Concerns

- **Environment:** integration suite needs `Mqtt__Enabled=false` in this environment (stale CA path in Development user secrets). Without it 55 baseline tests fail before any of this work. Worth fixing outside this phase (refresh the secret or make `ManagementApiFactory` force `Mqtt:Enabled=false`).
- **Pre-existing flaky test:** `SignalRConnectionBacklogTests.Emits_runtime_metrics_for_drop_retry_and_resynchronization` failed once in about 35 unit runs (`Expected: 1 / Actual: 2` at `SignalRConnectionBacklogTests.cs:124`, a metrics listener sharing state with parallel tests). Unrelated to reboot code; it did not fail in the other runs.

## Fixes after code review (server findings)

Source: `reports/code-review-server-tools-report.md`. Each fix test-first; the red run was against the unfixed code.

| Item | Red | Fix |
|---|---|---|
| M1 hold vs long reply timeout | `With_a_long_reply_timeout_a_reply_inside_it_still_finds_the_hold`: `Expected SCHEDULED / Actual OUTCOME_UNKNOWN`; `..._hold_ends_after_the_timeout_row_...`: `Expected MQTT_PUBLISHED / Actual OBSERVED` (timeout 180); tracker `The_hold_lasts_until_the_reply_deadline...`: `Expected IN_PROGRESS / Actual null` | `Admit(plan, now, replyDeadline)`; hold ends at max(accept + 122 s, reply deadline + 2 s); the transport passes the pending entry's deadline |
| L1 SCHEDULED left forever by rebind/revoke + sweep before the SCHEDULED write | `A_revoke_and_a_sweep_between_the_upgrade_and_the_scheduled_write_...`: `Expected OUTCOME_UNKNOWN / Actual SCHEDULED` (interleaving via an audit hook just before the SCHEDULED write) | `CompleteRebootAsync`: when the conditional write fails and the row is still `MQTT_PUBLISHED` (or not yet readable), hand the completion back to the tracker (existing 3-attempt retry) |
| L2 drop before reply lost at `Upgrade` | `A_drop_seen_before_the_reply_is_kept_...`: `Assert.Contains` for `uptime_before=500` failed | `Upgrade` with held evidence reports the pre-drop uptime and queues an `OBSERVED` completion from `{SCHEDULED}` (returned by the next sweep, after the reply recorded SCHEDULED) |
| L3 `reply=none` after Acknowledged | `A_reboot_the_device_only_acknowledged_...`: `reply=accepted` not found | new `IDeviceRebootTracker.NoteReplyAccepted`, called by the correlator on `Acknowledged`; detail says `reply=accepted` |
| L4 guard test | `A_lower_uptime_stamped_before_the_reboot_was_admitted_is_not_evidence_even_while_tracking` passes on the code; with the `at <= AcceptedAt` guard removed it fails (mutation run, reverted) | test only |
| L10 | n/a (log text) | `Program.cs` startup warning now says "left unfinished (published, or a reboot still being watched)" |
| L11 | wiring test: `ValidateScopes = true` added and passes. `ValidateOnBuild` fails on unrelated ASP.NET services (`AuthorizationPolicyCache` needs `EndpointDataSource`), so it is left off, with a comment | `SweepExpiredAsync(now, bool brokerConnected = true, CancellationToken ct = default)`; caller in `MqttDeviceLinkService` updated |

Files: `src/Hms.Server.Application/DeviceLink/DeviceRebootTracker.cs`, `DeviceLinkHandlers.cs`, `src/Hms.Server.Infrastructure/Mqtt/MqttDeviceCommandTransport.cs`, `MqttDeviceLinkService.cs`, `src/Hms.Server.Api/Program.cs`; tests `DeviceRebootFlowTests.cs` (+5, rig takes a reply timeout, audit hook), `DeviceRebootTrackerTests.cs` (+2), `DeviceLinkWiringTests.cs`.

Gate: build 0 errors, 1 warning on the incremental build (3 on `--no-incremental`, all pre-existing); unit 640 passed (633 + 7); integration (`Mqtt__Enabled=false`) 123 passed; `has-pending-model-changes`: none.

Accepted as residual, not fixed (as instructed):
- **L5** short broker-link drops are invisible to the tracker: the link service reconnects before it sweeps, so an outage shorter than one loop never reaches `TakeDue(..., brokerConnected:false)`.
- **L6** a new real reboot during the 10-minute tail replaces the first reboot's tail record, so the first `NOT_OBSERVED` can no longer be raised.

Environment: the API (PID 17352) locked `src/Hms.Server.Api/bin`; it was stopped with `taskkill /PID 17352 /F` and restarted with `start-api.sh` (same env as phase 3) as PID noted in the reply. Status endpoint shows `CONNECTED` to `127.0.0.1:8088`; `/link` for device 870c... is online. PIDs 34804, 29528, the simulator and Vite were not touched.
- Closed flash rows: `ManagementApiTests.The_other_flash_rows_stay_closed_even_for_a_master_admin` (3 cases: factory_reset, ota.remote_start, ota.apply; master-admin, confirm where a template exists, dryRun false, payload {}) expects `DENIED_UNTIL_ACTION_CONTRACT` and no audit reservation. Mutation: adding `ota.apply` to `FlashActions` makes the ota.apply case fail (`CONFIG_ACTION_UNSUPPORTED`); reverted. Unit 640, integration 126.
