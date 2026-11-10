# Red-team review: plan 261110-2021-split-class-b-behaviour

- Date: 2026-11-10
- Scope: `plan.md`, `phase-01-…`, `phase-02-…`, brainstorm report; checked against the source and tests on branch `claude/mqtt-device-connection-36f705`.
- Method: static reading and grep only. No build, no test run, no source edits.

## Verdict

The plan can be executed and its line references are correct. The equivalence claim for the 34 enabled actions holds. One design gap (F1) leaves the plan's own threat scenario open in production code for two of the three behaviours. Two test-migration gaps (F2, F3) let tests stay green while proving less than before. The rest is low severity.

## Findings

### F1 (High): the behaviour flags are keyed on the same list that opens a class B row

- `PinnedCommandCatalog.cs:254`: a class B row opens only through `PhysicalOutputActions`. There is no other path for `sms.send`.
- Planned derivation: `KeepsAuditPayload` and `UsesOutputLane` are both `PhysicalOutputActions.Contains(key)`, with no type check. Only `AffectsOutputs` has `type == "control"`.
- So the scenario in plan.md "Vì sao" (one line added to the allowlist) still gives `sms.send` `KeepsAuditPayload = true` and `UsesOutputLane = true` after the plan.
- The payload is written at reservation (`SendDeviceCommandHandler.cs:171` → `EfCommandAuditStore.cs:118`), before `OutputCommandPolicy` runs at `SendDeviceCommandHandler.cs:194`. The policy refusing the action does not stop the payload being stored.
- The phase 2 tests use `Find("sms","send") with { Availability = Enabled }`. Production cannot reach that state, so those tests do not cover the real opening path. Only the phase 1 expectation table would go red.

Plan change:
- In `ToDefinition`, compute `isRelay = commandClass == Physical && row.Type == "control" && PhysicalOutputActions.Contains(key)`.
- Set `UsesOutputLane = isRelay` and `KeepsAuditPayload = isRelay || ConfigWriteActions.Contains(key) || FlashActions.Contains(key)`.
- Add to phase 1: `Assert.All(PhysicalOutputActions, x => Assert.Equal("control", x.Type))`.
- In the comment on `PhysicalOutputActions` (`PinnedCommandCatalog.cs:84-87`) and in the docs, state that a non-relay class B row needs its own allowlist and switch arm.
- Reword plan.md acceptance bullet 2 to say which path it covers.

### F2 (Medium): `PersistenceModelTests.cs:502` loses its meaning

- Test `Ef_command_audit_keeps_what_an_opened_config_write_asked_and_nothing_of_other_writes` (`:488-515`) builds both definitions by hand as `CommandClass.Write`.
- Today `Assert.Null(other.PayloadSummary)` at `:514` proves the store filters a secret-bearing write (`network.wifi_sta_config_set`).
- After phase 2 the hostname half goes red (`:513`). Whatever fix is applied, the wifi half passes because of the default `false`; it no longer tests any production rule.

Plan change:
- Name this test in phase 2. Both definitions must come from `new PinnedCommandCatalog().Find(type, action)! with { Availability = Enabled }`. `wifi_sta_config_set` exists in `command-catalog.json:837`.
- Do not add a bool parameter to the local `WriteAsync` helper.

### F3 (Medium): fixing only the flag a red assertion asks for leaves inconsistent relay definitions that stay green

Phase 2 step 5 is driven by red tests. Each file's red tests demand only one flag:

| File:line | Definition | What goes red | Flag the red asks for | Flags it really needs |
|---|---|---|---|---|
| `DeviceOutputStateTests.cs:24-25` | `SetOutput` (B) | `:172-207` (timeout, executed, duplicate), `:222-241`, `:290-309` | `AffectsOutputs` | all three |
| `DeviceLaneSafetyTests.cs:24-25` | `SetOutput` (B) | `:56-57`, `:71-80` | `UsesOutputLane` | all three |
| `DeviceRebootFlowTests.cs:31-32` | `SetOutput` (B) | `:196` | `UsesOutputLane` | all three |
| `PhysicalOutputSafetyTests.cs:23-24` | `SetOutput` (B) | `:222-239` (second send becomes `DEVICE_WRITE_BUSY`) | `UsesOutputLane` | all three |

- In `DeviceOutputStateTests`, `Link.PublishedAsync` (`:372-379`) calls `Pending.Register` directly and bypasses the lane. With only `AffectsOutputs` added, every test is green while the relay is treated as a write at `DeviceLinkHandlers.cs:84` and `DeviceLinkContracts.cs:382`.
- `DeviceLaneSafetyTests.cs:255-256` ("relay not held back by reads") is green with or without the flag; it then exercises the write lane, not the relay lane.

Plan change:
- For these four `SetOutput` fields, require `new PinnedCommandCatalog().Find("control","set_output")!`. The catalog row matches the hand-built one: permission `gpio.do_write`, TTL 120000, Enabled.
- If built by hand, all three flags must be given together.
- Add both rules to the phase 2 success criteria.

### F4 (Low): the phase 2 test list is inaccurate

- Missing: `PersistenceModelTests.cs:284-286` (`RebootDefinition`, class F). It goes red at `:351-352` and needs `KeepsAuditPayload: true`.
- Listed but need nothing:
  - `PersistenceModelTests.cs:230` and `:397` are class Read.
  - `DeviceLinkTests.cs:122` and `:251` only call `Adapter.PrepareAsync`; `IsWrite` is unchanged for F and W.
  - `PhysicalOutputSafetyTests.cs:25-28` (`Pulse`, `SetAll`) are used only with `OutputCommandPolicy`, which reads `Class`.
- `SetAll` (`PhysicalOutputSafetyTests.cs:27-28`) must not get relay flags; the phase 1 table gives `control.set_all` false/true/false.
- Green and unasserted, but the stored payload silently becomes null:
  - `PersistenceModelTests.cs:253-254` (set_output, B) needs `KeepsAuditPayload: true` to stay representative.
  - `DeviceLinkWiringTests.cs:19-20` (reboot, F), same.

Plan change: correct the list as above.

### F5 (Low): doc lines the plan does not list become false

In `docs/architecture-mqtt-device-link.md`:
- `:453` "Lane ghi áp dụng cho mọi class trừ R và B".
- `:337` "Lệnh vật lý (class B) | Không bị giới hạn".
- `:476` repeats the payload rule.

Plan change: add 337, 453 and 476 to phase 2 step 7.

### F6 (Low): two steps do no work

- Phase 1 step 5 and the third risk row in plan.md are already answered. `MeController.cs:60-71` maps to `CommandCatalogActionDto` (`:88-99`) field by field. No other code serialises `CommandDefinition`; `DeviceCommandsController.cs:134` only resolves a permission. Replace the step with this fact.
- The phase 1 test "`new CommandDefinition(...)` without flags gives three false" tests C# default parameters. Drop it; the consumer tests in phase 2 cover the safe default.

## Answers to the six questions

1. **Other class-derived behaviour the plan misses: no finding.**
   - Remaining readers of `Class` in `src` are acknowledged by the plan or only test for Read:
     - `CommandPermissionResolver.cs:25,34,45,48`
     - `ConfigCommandPolicy.cs:33`, `OutputCommandPolicy.cs:27`
     - `SendDeviceCommandHandler.cs:143`
     - `MqttDeviceCommandTransport.cs:70`, `DeviceLinkContracts.cs:318`, `DeviceLinkHandlers.cs:278`
     - `MeController.cs:65`
   - The frontend reads `class` only to hide Flash (`DeviceCommandModal.vue:79`).
   - DI registers only `PinnedCommandCatalog` (`DependencyInjection.cs:72`).

2. **Equivalence for the enabled actions: no finding.**
   - 26 reads + 2 relay + 5 config + 1 flash = 34.
   - Audit: every enabled B or F action is in `PhysicalOutputActions` or `FlashActions`; no D row is enabled; reads are in none of the three sets.
   - The old rule keyed `ConfigWriteActions` on `plan.Request` and the new one on the definition. They are the same in production because of `catalog.Find(request.Type, request.Action)` at `SendDeviceCommandHandler.cs:94`.
   - `AffectsOutputs` and `UsesOutputLane` equal the old `Class == Physical` for the two enabled B rows, both type `control`.

3. **Hand-built definitions that change meaning:** see F2, F3, F4.

4. **Phase 1 invariant and table: no finding.**
   - `KeepsAuditPayload ⇒ Enabled` holds because `CatalogTests.cs:152-156` pins each list to its class, so set membership implies `opened`.
   - `FlashActions` is `{system.reboot}` (`PinnedCommandCatalog.cs:117-121`), so reboot is true and `factory_reset`/`ota.*` are false.
   - 7 B + 4 D + 4 F = 15 rows matches `CatalogTests.cs:220-222`.

5. **Record equality, `with`, deconstruction: no finding.**
   - `with` sites carry the flags from the catalog: `ManagementApiTests.cs:1583`, `DeviceCommandAuthorizationTests.cs:26`.
   - No positional deconstruction and no equality assertion on definitions.
   - Every hand-built call that passes arguments after `Class` uses named arguments.

6. **Over-engineering and executability:** see F6.
   - All cited source line numbers are correct.
   - `CatalogTests.cs` exists; `generate-command-catalog.mjs` supports `--check`.
   - Three bools are justified: `AffectsOutputs` and `UsesOutputLane` differ for the four closed `control.*` rows.

## Unresolved

- Should phase 2 forbid hand-built definitions for actions that exist in the pinned catalog (F3), or only recommend against it? The plan currently only states a preference.
