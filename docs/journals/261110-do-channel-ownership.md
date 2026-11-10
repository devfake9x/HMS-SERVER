# 2026-11-10 — DO panel says which channels can be written

Plan: `plans/261110-1335-do-channel-ownership/` (TDD, from `plans/reports/brainstorm-261110-1329-do-channel-ownership.md`).
The firmware spec changed the same day (HMS-IDF `c80797d`): `control.get_status` now reports `owner` and `owner_id`
per channel, and writes on a channel a level rule holds are refused with `owned_by_rule`. The server already received
`enabled`, `gpioOk` and `forced` but dropped them, so the DO panel offered On/Off/Pulse on every channel. On the lab
gateway, by the HIL evidence in the spec, only DO1 is writable: DO2/DO3 have no GPIO, DO4/DO5 are forced, DO6 is held
by a level rule.

## What changed

- **Server:** `OutputChannelState` and `OutputChannelDto` keep the five fields, null when the device does not send
  them. Any `owner` outside the six spec values becomes `unknown`; a numeric `owner_id` is kept as its text. The
  `GET /outputs` change is additive and the pinned catalog (`83e93d77e`) and its hash are unchanged: this is reply
  shape, not a §11 row.
- **Panel:** one lock reason per channel, in the order GPIO, configuration, force, level rule. Pulse is also locked
  on a rule-held channel on purpose, since the spec does not say whether a pulse is refused there and the rule sets
  the level again (spec question 17). `event_rule`/`trigger` only add a note. No `owner` (older firmware) means no
  lock by owner: the device decides, and the four state refusals are translated with the raw code kept.
- **Reboot:** the confirm dialog names the rule-held channels; the result credits a relay that is ON after boot to
  the rule. The generic "rules may turn a relay on" sentence stays, because `owner` only says who holds a channel
  at read time.
- **Tools:** simulator layouts (`SIM_DO_LAYOUT=gateway`, `SIM_DO_NO_OWNER=1`, `SIM_DO_DISABLED`); the reboot
  preflight FAILs on rule-held channels and prints `ASK` when it cannot tell.

## Decisions that changed during the work

- **Owner does not replace the manual rule check.** The approved design let the owner check replace "the user
  confirms the gateway has no DO rule". Review showed why that is weaker: an event rule with a SET_HIGH action that
  has not fired yet reports `owner: none`, and it still fires at the first evaluation after boot (spec L883). The
  user chose: the manual confirmation is unconditional again, the owner check is an extra guard, and `--reboot`
  refuses after an `ASK` unless `--rules-confirmed` is given.
- **Fail closed on owner values the tool does not know.** The first tool version only knew the literal `unknown`,
  so a misspelled or new owner value passed. It now whitelists `none`/`force` as free and asks on everything else,
  matching how the server normalises the field.
- **Phases 1 and 2 ran in parallel.** The plan said sequential; the files were disjoint and the JSON contract was
  fixed in the plan, so the frontend did not need the server to exist. Running vitest while `dotnet test` was busy
  caused 1-3 random 5 s timeouts; the same suite on an idle machine was green twice.

## What went well, what did not

- Reading the real code before planning paid off: the planner found the outputs endpoint maps to a separate DTO,
  and that the panel already re-reads after every device outcome, so no new read path was needed.
- Two small UI bugs came only from review probes, not from the tests written first: a stale device code shown next
  to an unrelated pulse-duration error, and the reboot note crediting the earlier rule when the reading after names
  another owner. Both are now covered.
- The browser pane was hidden for most of the walkthrough, so only S1 has a screenshot; S2-S4 evidence is the DOM
  and the simulator log.

## Numbers

Unit 640 → 658, integration 126 → 127 (`Mqtt__Enabled=false`), vitest 475 → 537, `vue-tsc` clean, `--self-check`
PASS. UI walkthrough S1-S4 and preflight S5 PASS on the simulator.

## Open

- One read-only `control.get_status` on `gateway_870c` when the bench is free: does firmware 1.0.108 send `owner`?
- Spec questions 17-19 (pulse on a rule-held channel, firmware version of `owner`, type of `owner_id`) are written
  down, not sent.
- Pre-existing on `develop`: the login redirect goes to `/overview`, which is not a route.
