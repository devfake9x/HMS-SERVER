---
name: behaviour-flag-review
description: Checks for reviewing changes to the per-action behaviour flags on CommandDefinition (KeepsAuditPayload, AffectsOutputs, UsesOutputLane) and for plans that open a new command row
metadata:
  type: reference
---

From 2026-11-10 the server reads three per-action flags on `CommandDefinition` instead of `CommandClass.Physical`. Only `PinnedCommandCatalog.ToDefinition` sets them. This replaces the "class B treated as relay" warning in [[catalog-repin-review]].

- Old-vs-new equivalence is quick by group: 26 reads, 2 relay, 5 config writes, 1 flash = 34 enabled. Compare the old class rule with the flag for each group; the literal table in `CatalogTests` pins all 15 B/D/F rows.
- `KeepsAuditPayload` is derived from `ConfigWriteActions` / `FlashActions` membership, so opening a W/C row with a secret (`mqtt.set_config`, `wifi_known_add`) would store its payload. Closed the same day: `CatalogTests.Only_actions_known_to_carry_no_secret_keep_their_audit_payload` names the 8 payload-keeping actions literally. A change that edits that list needs the payload checked for secrets.
- `AffectsOutputs` is `class B && type == "control"`. Reboot staleness is a separate path keyed on `DeviceRebootCodes.Scheduled` in `DeviceLinkHandlers`; `factory_reset` has none. Raise it when a plan opens `factory_reset`.
- Hand-built test definitions default all flags to false. A hand-built class Physical row takes the write lane, unlike production. Grep `CommandClass.Physical` in `tests/` and check each use reaches only the policies.
- Run the integration suite with `Mqtt__Enabled=false` (documented in `docs/architecture-mqtt-device-link.md`). Without it, 76 tests in `ManagementApiTests` and `MaitechRtuDeviceIntegrationTests` fail with a missing mosquitto `ca.crt`; with it, all 147 pass, including the `/me/command-catalog` contract tests (see [[hms-review-workflow]]).
