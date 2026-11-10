# Code review: per-action behaviour flags on `CommandDefinition`

Scope: uncommitted diff on `claude/mqtt-device-connection-36f705` (9 source files, 7 test files, `docs/architecture-mqtt-device-link.md`). Read-only review.

Result: no critical or high findings. One medium (a gap the change leaves open, not a regression) and three low.

## Findings

### M1. Opening a W/C row still turns on payload storage, and the test that guards it mirrors the implementation

- `src/Hms.Server.Infrastructure/Commands/PinnedCommandCatalog.cs:281`: `KeepsAuditPayload: isRelay || ConfigWriteActions.Contains(key) || FlashActions.Contains(key)`.
- `tests/Hms.Server.UnitTests/CatalogTests.cs:260-272` (`Opened_config_writes_keep_their_audit_payload_and_use_the_write_lane`) iterates `ConfigWriteActions` itself, so it passes for whatever is in the list.
- The change removes the "list opens the row and also grants behaviour" coupling for class B (the `type == "control"` condition) and pins every B/D/F row in a literal table. For W/C rows the coupling remains.
- Failure scenario: a later change adds `mqtt.set_config`, `network.wifi_known_add` or `network.wifi_ap_config_set` to `ConfigWriteActions`. `KeepsAuditPayload` becomes true, the password goes into `command_audits.PayloadSummary`, and no catalog test turns red. Only `network.wifi_sta_config_set` is covered, by `PersistenceModelTests.cs:514`.
- Not a regression: HEAD stored the payload for any `ConfigWriteActions` entry too. It does contradict the new record doc ("False until the action's payload is known to hold no secret or personal data", `CommandDefinition.cs:27-28`).
- Flash rows are covered: adding `ota.apply` or `system.factory_reset` to `FlashActions` fails the literal table at `CatalogTests.cs:233-249`.
- Suggested fix (test only): pin the exact set of actions with `KeepsAuditPayload` as eight literal names (`control.set_output`, `control.pulse`, `system.set_hostname`, `system.set_timezone`, `system.sync_ntp`, `adc.set_channel`, `adc.toggle_channel`, `system.reboot`). Alternative: a separate `AuditPayloadActions` set in the catalog, so opening and storing are two decisions.

### L1. `AffectsOutputs = false` is called "the safe side", but for staleness it is the permissive side

- `src/Hms.Server.Domain/Commands/CommandDefinition.cs:22-30`.
- Marking stale too often only costs a read-back; not marking shows an outdated relay state as current.
- `system.reboot` and `system.factory_reset` do change relays (the handler comment at `DeviceLinkHandlers.cs:109` says so) but carry `AffectsOutputs = false`. Reboot is handled by a separate path keyed on the `DeviceRebootCodes.Scheduled` result (`DeviceLinkHandlers.cs:110-111`) and by the reconnect path (`:351`). An unanswered reboot is not marked stale by the sweep (`:139`), and `factory_reset` has no path at all for when it opens.
- Behaviour is identical to HEAD (class F was never marked stale), so this is wording plus a note for the plan that opens `factory_reset`.
- Suggested fix: reword the param doc to "a digital-output command; restarts are covered by reboot tracking", and drop "safe side" for this flag.

### L2. Docs line 83 contradicts line 157

- `docs/architecture-mqtt-device-link.md:83` says unopened class B/D rows by default do not mark DO stale.
- Line 157 and the code say every class B `control.*` row marks stale, including the closed `set_all`, `force_*` and `set_config`.
- Suggested fix: on line 83, limit "không đánh stale DO" to rows outside `control.*`.

### L3. Hand-built definitions for real catalog actions remain in tests (harmless today)

- `tests/Hms.Server.UnitTests/PhysicalOutputSafetyTests.cs:24-27`: `Pulse` and `SetAll` are class Physical with no flags. I checked their uses (lines 59, 73, 82): only `OutputCommandPolicy.Check`, which reads the class. If `Pulse` is later sent through the transport it will take the write lane, unlike production.
- Hand-built `system.reboot` (`DeviceLinkTests.cs:123,165`, `DeviceRebootFlowTests.cs:27`, `DeviceRebootTrackerTests.cs:16`, `ConfigCommandPolicyTests.cs:183`) and W/C rows (`DeviceLaneSafetyTests.cs:31-34`, `DeviceLinkTests.cs:252,266,284`, `DeviceRebootFlowTests.cs:29`) differ from the catalog only in `KeepsAuditPayload`. None reaches `EfCommandAuditStore`; lane and stale results are the same as with catalog definitions.
- Suggested fix: take `Pulse` from the catalog like `SetOutput`. The rest can stay.

## Per-criterion verdict

| # | Criterion | Verdict | Evidence |
|---|---|---|---|
| 1 | 34 enabled actions unchanged | Pass | 26 reads + 2 relay + 5 config + 1 flash = 34. Old rule vs new flag per group: reads (no payload, read lane, no stale) = all false; `set_output`/`pulse` (payload, relay lane, stale) = (true, true, true); 5 config writes (payload, write lane, no stale) = (true, false, false); `system.reboot` (payload wrapped with `dry_run`, write lane, no stale) = (true, false, false). The Flash wrapper at `EfCommandAuditStore.cs:286` is unchanged. |
| 2 | Unopened B/D/F rows get the conservative side | Pass, see L1 | All 15 B/D/F rows pinned at `CatalogTests.cs:233-249`. `sms.send`, the 4 D rows, `factory_reset`, `ota.*` = (false, false, false); `set_all`, `force_*`, `set_config` = (false, true, false). The new consumer tests would fail on HEAD code, so they prove behaviour. |
| 3 | Unchanged parts | Pass | TTL cap (`SendDeviceCommandHandler.cs:143`), `OutputCommandPolicy.cs:27`, `ConfigCommandPolicy.cs:33` still class-based and untouched. `command-catalog.json` not in the diff, hash `658e191a…`. No migration. `MeController.cs:61-71` maps the DTO field by field; nothing in `src` serializes or logs a `CommandDefinition` or a plan; the frontend has no reference to the new names. |
| 4 | No weakened assertions | Pass | Every removed test line is a definition construction. No existing `Assert` was edited. `PersistenceModelTests.cs:513-514` now runs against catalog definitions, which is stronger. |
| 5 | No leftover class-based relay inference | Pass, see L3 | `CommandClass.Physical` in `src` remains only in the catalog, `OutputCommandPolicy`, `ConfigCommandPolicy` and the TTL cap. The `IsWrite(CommandClass)` overload is gone; all 5 callers pass the definition. `InMemoryCommandCatalog` is used only by tests, and `ManagementApiTests.cs:1582` rebuilds rows with `with`, so flags survive. |
| 6 | Comments and docs | Pass, see L1 and L2 | No plan IDs, phase numbers or finding codes in code comments or test names. The docs reference the plan path, which is allowed there. |

## Checks run

- Targeted unit tests (`CatalogTests`, `DeviceLaneSafetyTests`, `DeviceOutputStateTests`, `PhysicalOutputSafetyTests`, `DeviceRebootFlowTests`): 246 passed.
- Targeted integration tests (`PersistenceModelTests`, `DeviceLinkWiringTests`, `Mqtt__Enabled=false`): 67 passed.
- Both ran with `--no-build` against the existing binaries.
- Not run: mutation testing, the full suites, the frontend gate.

## Ordering check for M1

- `EfCommandAuditStore.ReserveAsync` writes `PayloadSummary` (`EfCommandAuditStore.cs:92-118`).
- Both policies run later, in `HmsIdfCommandProtocolAdapter.PrepareAsync` (`HmsIdfCommandProtocolAdapter.cs:59` and `:63`), which the handler calls after the reservation.
- So in the M1 scenario the payload is stored even when `ConfigCommandPolicy` refuses the command.

## Unresolved questions

None.
