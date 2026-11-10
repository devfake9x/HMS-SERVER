# 2026-11-10 — Command catalog re-pinned to spec `c80797d`

Plan: `plans/261110-1747-mqtt-catalog-sync/` (from `plans/reports/brainstorm-261110-1745-mqtt-api-completeness-and-catalog-sync.md`).
The firmware spec moved from `83e93d77e` (94 rows) to `c80797d` (108 rows). Two things mattered for the server:
`status.tags` now needs `tag.view` instead of `sys.info_view`, and the spec added 14 rows that the server did not know.
A role that held `sys.info_view` but not `tag.view` lost `status.tags` on the next catalog load.

## Why the generator needed a flag

The spec's first count sentence in §11 (L1351) still says "98 pairs, 12 namespaces". A later sentence says 100/13, and
the table has 108 rows in 13 namespaces. The generator read the first sentence and refused the new commit. The fix is
`--accept-stated-count 98/12`: it accepts exactly those two prose numbers and nothing else. It does not check the table.
Any other number is an error, and an error never prints the table count, so the flag cannot be copied from output.
When the sentence changes, the generator asks for the flag to be updated or removed.

Because the flag does not check the table, the table has its own checks:

- `scripts/verify-command-catalog-repin.mjs` reads §11 with a line regex. It does not import the generator. It prints the
  row count, namespace count and pair-set SHA, diffs the rows of two commits field by field, and compares each field with
  `command-catalog.json`.
- `CatalogTests` pins the blob, the sha256 of the spec bytes, the `catalog_hash` and the pair-set. It also pins the 34 open
  actions with their class, permission, TTL and confirm flag, so a later re-pin cannot quietly open a row.

## What changed

- **Catalog:** 108 rows, 13 namespaces, 53 firmware permissions (57 with the 4 server codes). `catalog_hash`
  `658e191a…`. Exactly 14 rows were added and one row changed: `status.tags` `required_perm` `sys.info_view` → `tag.view`.
  Still 34 open actions. `sys.info_view` now opens exactly 8 actions.
- **High-risk:** `DevicePermissionCodes.HighRisk` grew from 9 to 13 codes (`gpio.do_force`, `gpio.do_config`, `sms.send`,
  `sms.delete`). `/me/command-catalog` reports `highRisk: true` for 24 actions.
- **Write-time only:** high-risk is checked when a grant is written (role create, update, delete; membership assign and
  remove). It is not checked when a command is sent, and existing grants are not revisited. Changing the set later needs a
  query for current holders first; the query is in `reports/affected-roles.md`.
- **Closed rows:** the 14 new rows return `DENIED_UNTIL_ACTION_CONTRACT`, including for the master-admin. No audit record,
  no signature, no publish. The allowlists in `PinnedCommandCatalog.cs` are unchanged.
- **Tests:** unit 686 (was 658), integration 143 (was 127), node 33 (was 11). One expectation changed: a TTL cap test now
  follows the handler rule (physical or flash commands cap at 10 s), since the four new class-B rows carry TTL 60000.
- **Frontend:** no code change. Vitest 537 and `vue-tsc --noEmit` pass. The UI reads version and hash from the server and
  has no firmware permission list.

## Found while reviewing the spec diff

- Seven open actions have different text in §6 at `c80797d`. Only `status.tags` breaks the reply contract: it is now a
  paged object, `{schema:"status.tags.v1", …, items[], truncated}`, with a default `limit` of 10. The gateway on firmware
  1.0.108 still sends an array, and the server and UI do not read `status.tags` at all. Paging is a follow-up, not part of
  this plan.
- Another spec sentence (§11 L1359) labels the SMS rule as "(R14)". The SMS rule is R13 (L417); R14 is the
  `mqtt.set_config` trial. This is an unsent question for the spec team.

## Before any of the 14 rows opens

The catalog carries only the nine table fields. It has no TTL cap, no primary-only flag and no posture, so the server must
add these itself first: R13 for SMS, R15 for force DO, R16 for `control.set_config`. Class-B rows must also stop being
treated as relays: the audit keeps their payload (including phone numbers and SMS text), output state is marked stale,
the write lane is bypassed, and the TTL is capped at 10 s. Only `PhysicalOutputActions` is the class-B allowlist. These
steps are separate plans.

## Cut-over

Deploy means a build and an API restart. The restart drops stored replies, DO state, lanes and reboot tracking, and marks
unfinished commands `OUTCOME_UNKNOWN_AFTER_RESTART`. Deploy only when no DO write, pulse or reboot is in flight. A tab
opened before the deploy gets `CATALOG_STALE` on its next send and must reload. A write with an unknown outcome is checked
with a status read before any retry.

## Open items

- Spec questions 8 (one count sentence) and 20 (the R13/R14 label): the first is answered by the flag, the second is unsent.
- Tag the pinned commits (`c80797d`, `83e93d77e`). They exist only on feature branches of HMS-IDF.
- Paged `status.tags`: decide between keeping it open with paging or closing it until the gateway upgrades.
- The end-to-end check of a stale tab (`CATALOG_STALE`, then reload and read) is recorded in the plan's
  `reports/live-verification.md`.
