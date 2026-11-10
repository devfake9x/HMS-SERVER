# 2026-11-10 — Behaviour flags replace the "class B is a relay" assumption

Plan: `plans/261110-2021-split-class-b-behaviour/` (from `plans/reports/brainstorm-261110-2019-split-class-b-behaviour.md`).
Follow-up from the catalog re-pin. Class B holds relays (`control.set_output`, `control.pulse`) and `sms.send`, and the server treated all of class B as relays.

## What was wrong

Four places inferred behaviour from `CommandClass.Physical`: audit payload storage (`EfCommandAuditStore.SummarizePayload`), DO stale marking after a reply (`DeviceOutputState`), DO stale marking on the expiry sweep (`DeviceLinkHandlers`), and lane choice (`CommandLanes.IsWrite`, five callers).

Nothing leaked. The closed rows are refused in `SendDeviceCommandHandler` before `ReserveAsync` writes an audit row. That protection is an ordering accident, not a rule about SMS. Adding `sms.send` to `PhysicalOutputActions` would have stored phone numbers and message text in `command_audits.PayloadSummary`, marked DO stale, and skipped the write lane. No test would have gone red. We found it by reading the code, not from a failing test, and that is the embarrassing part.

## Decision

Three bools on `CommandDefinition`, all default `false`, assigned only in `PinnedCommandCatalog.ToDefinition`:

- `KeepsAuditPayload`, `AffectsOutputs`, `UsesOutputLane`.

`false` is the safe value for payload and lane. It is not the safe value for staleness: `false` means cached relay state is trusted. The record doc says so now.

Rejected:

- Payload allowlist only. It fixes one of four sites and leaves three wrong the next time a row opens.
- A strategy per action family. YAGNI, with two open B rows.

The red-team review caught a flaw in my first derivation. If `UsesOutputLane` came from `PhysicalOutputActions` alone, putting `sms.send` on that list would still give it the relay lane and store its payload, because that list is also what opens class B rows. So `isRelay` now requires class B, `type == "control"`, and list membership. A test asserts every entry of the list has type `control`.

## Tests

- The catalog table pins all three flags for all 15 B/D/F rows.
- Consumer tests: stale after reply and after timeout; lane (`sms.send` takes the write lane, and a second `sms.send` is refused with `DEVICE_WRITE_BUSY` while the relay still publishes); audit (`sms.send`, `sms.delete`, `control.set_all`, `system.factory_reset` store null).
- Tests that built relay definitions by hand now take them from the catalog. A hand-built definition silently defaults to `false`, so a relay test could pass with the wrong lane.

The review found a worse one of my own. The config-write payload test iterated `ConfigWriteActions`, the same list it guarded. Opening `mqtt.set_config` or a Wi-Fi config would have stored a password in the audit with every test green. It now names the eight payload-keeping actions literally. Lesson: a test that derives its expectation from the implementation's list cannot catch a bad entry in that list.

## Results

- Build: 0 errors, no new warnings. The 3 existing warnings are in files this change does not touch.
- Unit 710/710. Integration 147/147 with `Mqtt__Enabled=false`. Node 33/33. `generate-command-catalog.mjs --check` is up to date (`658e191a…`).
- Unchanged: `command-catalog.json` bytes, catalog hash, DB schema, `/me/command-catalog` DTO, the 10 s TTL cap, `OutputCommandPolicy`, `ConfigCommandPolicy`. All 34 enabled actions behave as before.

## The 76 failures I wrote off as environmental

The first integration run gave 76 failures, all `FileNotFoundException … ca.crt` under a temp `hms-idf-mosquitto-<guid>` path. The test report labelled them environmental and moved on. That was wrong. `docs/architecture-mqtt-device-link.md` says integration tests need `Mqtt__Enabled=false` on this machine, because user-secrets point at an old CA path. With that variable set, the suite is 147/147.

The failures were a documented step I had not followed, not a broken machine. Before calling a failure environmental, read the repo's test instructions and run the suite the documented way. "Environmental" should have been a question, not a conclusion.

## Still open

For any plan that opens a B or D row:

- Redaction: `sms.send` payloads (numbers, text) need field-level redaction before `KeepsAuditPayload` can be true for them.
- Lane and TTL: `set_all`, `force_*`, `set_config` take the write lane today; SMS needs its own TTL rule (R13: the table TTL is a ceiling, primary broker only).
- `OutputCommandPolicy` and `ConfigCommandPolicy` still read class.
- `system.factory_reset` has no stale path. The plan that opens it must decide.
- R13, R15, R16 from the catalog-sync plan (Follow-up d) remain.

## Next steps

- Owner: whoever opens the next B or D row. The checklist above is the gate; it should not merge without it.
- The change is not committed. Review the diff on `claude/mqtt-device-connection-36f705` and commit before the next plan starts.
