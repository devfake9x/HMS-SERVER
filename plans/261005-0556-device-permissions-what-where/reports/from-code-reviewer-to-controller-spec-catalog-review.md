---
title: "Code review - phase 1 rework: catalog v2 sourced from MQTT spec §11"
date: 2026-10-05
reviewer: code-reviewer (read-only)
plan: plans/261005-0556-device-permissions-what-where/plan.md
phase: phase-01-catalog-v2-permission-vocabulary.md
score: 8/10
decision: PASS_WITH_RISK
criticalCount: 0
warningCount: 2
suggestionCount: 7
---

# Code review - phase 1 rework (catalog from spec §11)

## Scope

- Rework delta: the 8 files in `phase-01-before-spec-rework` (generator and its node test, `CommandCatalogHash.cs`, `PinnedCommandCatalog.cs`, `command-catalog.json`, `CatalogTests.cs`, the architecture doc line 275, and the 260924 plan blockquote).
- Whole phase-1 delta: compared with `backup-before-phase-01` (`diff -rq`, skipping bin/obj/node_modules). Only the files the phase names changed. The frontend and `DependencyInjection.cs` are byte-identical. The `.tmp-*` folders date from 2026-09-28/30, so they predate this plan.
- Device facts were judged only against `git show 83e93d77e:engineering-docs/api/mqtt-api.md`. I re-checked blob `6485e00f…` and SHA-256 `ef41c113…`. I did not open any HMS-IDF source file.

## Checks I ran myself

| Check | Result |
|---|---|
| My own §11 parse vs the committed catalog (9 fields per row, plus domains) | 94/94 rows, 6/6 domains, 0 differences |
| Counts | R49 W18 C17 D3 B3 F4; 46 codes; 12 namespaces; 24 × 60000 + 70 × 120000; 3 templates; 3 dry run; every row has the same 9 keys |
| Pair-set fingerprint computed from the spec | `dbda8317…78d9`, equal to `CatalogTests.ManifestPairSetSha256` |
| Independent STJ-style hash of §11 | `b1d47a78…12c4`, equal to the committed `catalog_hash` |
| Golden literal, JS vs C# | byte-identical (714 chars, backslash-u003C escapes intact) |
| `node --test`, `--check` | 9/9 pass; exit 0 |
| Scratch copy of the generator: one-value edit / unknown argument / bad `--idf` | exit 1 / 1 / 1. Two regenerations give the committed bytes. A CRLF copy passes `--check`. |
| `dotnet build` and `dotnet test` on a scratch copy of src+tests | 0 warnings, 0 errors; 204 unit + 43 integration pass |
| About 30 altered copies of the real spec fed to `parseSpecManifest` | Every layout error in contract §2 fails loudly. Accepted semantic changes are listed in W2, S2, S4 and the edge cases. |
| Loader probes (test that exists only in the scratch copy) | TTL 99999999999 → `ROW_INVALID`; TTL 90000000 → loads; duplicate key in a config domain → `METADATA_INVALID`; empty `config_domains` → loads (44 codes, config.* resolve null) |

I did not re-run the frontend gate. No frontend file differs from the pre-phase backup, so this phase cannot change its result (the controller and the tester ran it green).

## Critical

None.

## Warnings

**W1 (warning): the resolver and its tests justify behaviour by firmware internals that the spec does not define (Q14).**
- Code comments:
  - `src/Hms.Server.Application/Commands/CommandPermissionResolver.cs:7-9`: "Mirrors the firmware's cmd_descriptor_required_permission … Anything the firmware would not match exactly".
  - `:30`: "like the firmware's required_perm_by_domain".
  - `:54-55`: "as cJSON does".
  - `:69-70`: "cJSON fails to parse such a string at all".
- Tests in `tests/Hms.Server.UnitTests/CommandPermissionResolverTests.cs`:
  - `:32`: test name `Json_escaped_domain_is_compared_after_unescaping_like_the_firmware`.
  - `:34`: "cJSON unescapes before its compare".
  - `:80`: "The firmware reads the key case-sensitively".
- Why it matters:
  - The spec names no firmware function, no parser and no matching rule for `domain`. Contract §5 (`design-contract.md:151`) calls exact, string-only matching **server policy** and gap G3.
  - This breaks the phase's own rule "mọi fact về thiết bị dẫn mục đặc tả", yet the phase file marks these files "Không đổi (đã đúng)".
  - Risk: a maintainer who reads "mirrors the firmware" will check parity by reading C, which is the Q14 mistake again.
- Fix (wording only, no behaviour change):
  - Reword the comments to: server policy (contract §5, G3): exact, case-sensitive, string-only match; a JSON string is compared on its unescaped value (RFC 8259); an invalid escape matches nothing (fail closed).
  - Rename the test to `Json_escaped_domain_is_compared_on_its_unescaped_value`.
  - In the same pass, make `src/Hms.Server.Domain/Commands/CommandDefinition.cs:35` cite spec §3 (default 120000) instead of "the firmware dispatcher".

**W2 (warning): §11 binds a domain permission to an action, but the server picks it by class, and the generator does not enforce the binding.**
- The §11 domain-table header is `Domain | config.get | config.set`, so each permission belongs to the action named by its column. The catalog renames the columns `view_perm`/`edit_perm`. `CommandPermissionResolver.cs:39-40` then picks view for class R and edit otherwise. The spec does not state that rule.
- Probes on the real spec:
  - With `config.set` reclassified as `R`, the generator accepts the spec. The class rule would then gate `config.set wifi_sta` with `wifi.sta_view` instead of the column's `wifi.sta_edit`: a write gated by a read permission, on the R11 security boundary.
  - It also accepts a by-domain permission on a non-config action (`adc.get_values`). `scripts/generate-command-catalog.mjs:183-189` only checks that the domain table is not empty.
- Not live at `83e93d77e` (config.get = R, config.set = W). After a re-pin, only `tests/Hms.Server.UnitTests/CatalogTests.cs:135-137` blocks it, and updating that expectation re-opens the hole.
- Fix: in `parseSpecManifest`, require the `domain_policy` rows to be exactly the actions in the domain-table header, and fail unless `config.get` is `R` and `config.set` is not `R`. Alternatively, bind by action in the resolver. Record the rule next to G3 in contract §13.

## Suggestions

- **S1 (suggestion):** `docs/architecture-device-mqtt-api-catalog.md:135` (and the mermaid at `:40-43`) still says the 94-action table was "trích xuất trực tiếp từ bảng manifest `cmd_descriptor.c`". Its table also contradicts §11 (`:142`: `ping` TTL `0` and response `{ pong: true }`, against 120000 and §6.1 `{status:"alive",…}`). Meanwhile `:275`, edited in this phase, says the server uses only the spec. Add a superseded note to that §3 pointing to spec §11 and the generated catalog, following the repo's SUPERSEDED-banner convention (`scripts/documentation-drift-check.mjs`).
- **S2 (suggestion):** the TTL has no range.
  - `scripts/generate-command-catalog.mjs:255` accepts any positive integer.
  - `99999999999` is written, but the loader then throws `ROW_INVALID` on first resolve, so every catalog-dependent endpoint fails. Only `dotnet test` would catch it.
  - `9007199254740993` is silently written as `…992`.
  - `90000000` passes both the generator and the loader (`PinnedCommandCatalog.cs:151` only checks `> 0`), but every send of that row then fails with `TTL_EXCEEDS_FIRMWARE_LIMIT`.
  - Spec §3 bounds `ttl_ms` to `1..86400000`; enforce that range in both places.
- **S3 (suggestion):** the node tests do not cover every case listed in phase step A1 (`scripts/generate-command-catalog.test.mjs:244-273`).
  - Missing cases: TTL `-1` or empty, a missing cell, a changed domain-table header.
  - The branch for `Theo domain` rows with an empty domain table (`generate-command-catalog.mjs:187-189`) is never reached: the "domain table missing" case hits the two-tables error instead.
  - My probes show all of these fail correctly today; adding them keeps them locked.
- **S4 (suggestion):** spec inconsistency not recorded. §11 templates use `<DEVID>`, while §7 says `<device_id>` is replaced by the MQTT identity. Phase 3 (`CONFIRM_REQUIRED`) and the transport plan (exact token) will have to substitute one of them, and the spec never says `<DEVID>` is a placeholder. Add it as G5 in `design-contract.md` §13. Related: the generator accepts any charset string as a template, so a stray `-` becomes `confirm_required: true`. Today the CatalogTests template lock catches that.
- **S5 (suggestion):** binding docs still give firmware-sourced reasons.
  - `design-contract.md:162`: "because the firmware labels misstate what several codes gate". This contradicts §10 at `:231`, which says the spec has no labels.
  - `design-contract.md:162` and `phase-06-frontend-roles-members-gating.md:73`: "mirrors firmware `required_perm_by_domain`". Spec §2 names that field but not its shape.
  - `plan.md:3`: the description still says "catalog quyền v2 từ firmware".
  - `plan.md:135` keeps two firmware source paths with line ranges inside an annotated historical note. Criterion 1 allows this; it could still be trimmed.
- **S6 (suggestion):** `tests/Hms.Server.IntegrationTests/ManagementApiTests.cs:201-249` does not lock the exact 10-key action shape. It checks two absent keys and some present ones, so criterion 5 is proven only by the record `MeController.cs:89-99`. Add an exact key-set assertion.
- **S7 (suggestion):** the loader accepts domain-policy rows with an empty `config_domains` (`PinnedCommandCatalog.cs:124-136`). It then loads 44 codes and config.* resolves to null, which fails closed. The generator already rejects this; mirroring the check in the loader is optional.

Edge cases accepted with no action needed:
- Same hash: a row indented 4 spaces, an NBSP after the class, NFD `Có`, a CRLF document, §11 as the last section, a later decoy §11 heading, alignment colons in the separator.
- `system.get.info` → action `get.info`, which follows the "split at the first dot" rule.

## Acceptance coverage

| # | Criterion | Proof |
|---|---|---|
| 1 | Reads only the spec; no firmware source path | `SOURCE_PATH` at generator `:22`; rev-parse, `git show` and blob-id check at `:336-347`. Grep of generator, tests, contract, plan and phases finds paths only in revision notes (`phase-01:12`, `plan.md:135`, `plan.md:195`). Non-path firmware claims remain (W1, S5). |
| 2 | Catalog = §11 | Own parse: 0 differences. Counts above, pair-set fingerprint, independent hash. |
| 3 | 9 fields per row; removed fields gone | Every row has the same 9 keys. Removed names appear only in negative tests (`CatalogTests.cs:329`, `:363`; node test `:128`, `:175`). |
| 4 | Hash JS ≡ C#; bad layout → error, no catalog | Golden literals byte-identical; independent hash matches. Layout errors throw before `writeFileSync`. Semantic gaps: W2, S2. |
| 5 | `/me/command-catalog` 10 fields | `MeController.cs:89-99`; the rework did not touch `MeController.cs` (S6). |
| 6 | Gates green | Build 0/0, 204 + 43, node 9/9, `--check` 0; frontend unchanged since the backup. |

## Contract status

- The only public-contract changes are the intentional ones. `CommandCatalogRow` follows the intentional v2 JSON shape. The version string changed from `hms-idf-` to `mqtt-api-83e93d77e` together with the new hash, so clients holding the old one get `CATALOG_STALE` (as called out).
- The rework did not change `CommandDefinition`, `ICommandCatalog`, `InMemoryCommandCatalog`, the DTO or DI.
- The legacy frontend still reads `featurePermission` (`frontend/src/adminApi.ts:69`, `App.vue:125`, `DeviceCommandModal.vue:75`). Phase 6 repoints these, and the "release after phase 6" rule covers the gap.

## Side effects and regressions

None found at runtime:
- Every row is still `DeniedUntilActionContract`, and that gate (`SendDeviceCommandHandler.cs:74`) comes before the TTL and permission code.
- `Dangerous` and `Idempotent` have no consumers.
- `DefaultTtlMs` is read only after the gate and by the DTO.
- The empty permission passed for domain rows (`SendDeviceCommandHandler.cs:82`) is unreachable, and phase 3 replaces that code.
- The singleton is still resolved lazily (`DependencyInjection.cs:44`, untouched).

## Plan follow-ups

- Phase 1 tasks look complete. The phase checkboxes are still `[ ]` and plan.md still shows "Pending"; the lead should update them once W1 and W2 are dispositioned.
- Two claims in the tester report are not supported by the code: a frontend test delta of "126 to 135" (frontend unchanged since the backup), and a property named `DryRunSupported` (the row property is `DryRun`).
- Note: the project-scoped reviewer memory `.claude/agent-memory/code-reviewer/reference_firmware_c_oracle.md` still describes compiling firmware C as an oracle, which contradicts Q14. I did not use it, and I did not edit it, as instructed.

## Unresolved questions

1. W2 needs a choice: bind by action in the resolver, or assert the classes in the generator. This is a contract decision.
2. For the transport plan: §2 says capabilities carry `ttl_ms` only "nếu row có default", while §11 gives 120000 for the other 70 rows. A future drift check against capabilities (G2) must treat a missing `ttl_ms` as 120000 (§3), because the catalog no longer records which rows have their own default.

## Cycle 2: delta review of the fix cycle (2026-10-05)

**Verdict: 9/10, PASS.** This replaces the cycle-1 verdict above: 0 criticals, 0 warnings, 2 suggestions (one new, one left over from cycle 1).

### Scope

- Baselines: my cycle-1 scratch copies (`review-build` for src/tests, `gen` for the generator). Doc changes were checked against what I read in cycle 1.
- Changed:
  - C#: `CommandPermissionResolver.cs`, `CommandDefinition.cs`, `PinnedCommandCatalog.cs`.
  - Tests: `CatalogTests.cs`, `CommandPermissionResolverTests.cs`, `ManagementApiTests.cs`.
  - Generator and its node test.
  - Docs: contract §2/§5/§6/§13, `plan.md:3/135/195`, `phase-01:12`, the architecture-doc banner.
- `command-catalog.json` is byte-identical to cycle 1 (hash `b1d47a78…12c4`).

### Checks re-run

- Gates: node 10/10. `--check` exit 0. `dotnet build` 0 warnings, 0 errors, and 215 unit + 43 integration tests pass (fresh scratch copy).
- Catalog vs §11: my own parse still shows 0 differences. Counts, pair-set fingerprint and independent hash are unchanged. The golden literals are still byte-identical.
- Cycle-1 probes, re-run on the new parser. Every W2/S2/S4 case is now rejected loudly:
  - `config.get` as W and `config.set` as R;
  - a by-domain permission on `adc.get_values`;
  - TTL 99999999999, 90000000 and 2^53+1;
  - a template without `<DEVID>`, and `-` in the Confirm column;
  - an indented row.
- Still accepted, and fine:
  - Hash-changing semantic edits (a confirm template or dry run on another row). `Committed_catalog_content_is_pinned` (`CatalogTests.cs:83`) now fails on these.
  - Same-hash variants: NBSP after the class, NFD `Có`, §11 as the last section.

### Cycle-1 findings

| ID | Status | Evidence |
|---|---|---|
| W1 | Fixed | Resolver `:7-10`, `:31`, `:55-56`, `:70`, and test `:32`, `:34`, `:80-81` now cite contract §5 / G3. `CommandDefinition.cs` cites spec §3/§6/§11. The removed `ObjectDisposedException` filter has no effect: payloads come from model binding (`DeviceCommandsController.cs:127`), whose `JsonElement`s are never disposed, and `src` has no `using JsonDocument`. |
| W2 | Fixed | Generator `assertDomainBinding` (`:199`, `:345`). Loader `IsConfigDomainRow` plus a non-empty domain table (`PinnedCommandCatalog.cs:156`, `:181`). The contract §5 rationale is accurate. Tests: node "outside config", "config.set as R"; C# `domain_row_outside_config`, `config_set_as_read`. |
| S1 | Fixed | Superseded banner at `docs/architecture-device-mqtt-api-catalog.md:135`. |
| S2 | Fixed | TTL capped at 86400000: generator `:281`, loader `:161`, tests on both sides. |
| S3 | Fixed | Node cases for TTL `-1`/empty/above the cap, a missing cell, a changed domain header, and the empty-domain-table branch. |
| S4 | Fixed | G5 added to contract §13; `<DEVID>` is required by the generator (`:291`) and the loader (`:160`). |
| S5 | Mostly | Contract `:162`, `plan.md:3/135/195` and `phase-01:12` were reworded. One item remains (N2). |
| S6 | Fixed | Exact ordered 10-key DTO check (`ManagementApiTests.cs:244-250`). |
| S7 | Fixed | The loader rejects domain rows with an empty domain table, and an empty catalog (`:55`). |

The extras from the adversarial pass check out:
- The hash literal pin (`CatalogTests.cs:30`) is documented as a DRY exception (contract `:72`).
- Identifiers are checked raw, which closes the U+212A → `K` NFC rewrite. A node test covers it.
- The layout rules, the mandatory namespace count and the exact lowercase hash (`:62`) work as described.

### New findings

- **N1 (suggestion): §11 HTML blocks are not modeled.**
  - Probes on the real spec are accepted with the **same hash**:
    - the domain table, or the whole action table, wrapped in `<!-- … -->` with a blank line before `-->`;
    - the domain table placed directly under `<div>`.
  - A CommonMark renderer hides the first (a type-2 block ends only at `-->`) and prints the second as raw text (a type-6 block runs to the blank line).
  - This contradicts the claim in `design-contract.md:49` and `scripts/generate-command-catalog.mjs:204-207` that the parser fails on anything a renderer could read differently.
  - The pinned §11 has no line starting with `<`. So reject such lines next to the code-fence rule (`generate-command-catalog.mjs:217`), or soften the claim.
- **N2 (suggestion, left over from S5):** `phase-06-frontend-roles-members-gating.md:73` still says "(mirror firmware required_perm_by_domain)". Reword it to "the §11 config-domain column for that action", as contract `:162` now does.

### Mandatory checks and side effects

- (a) Every acceptance criterion is still met: the catalog is unchanged and all gates are green.
- (b) No regression:
  - The loader is stricter only on off-spec catalogs, and the committed catalog loads.
  - Resolver behaviour and the DTO are unchanged; the DTO is now locked by a test.
- (c) No new public-contract change. `PinnedCommandCatalog.Load` now rejects an upper-case hash, an empty catalog, TTL > 86400000, templates without `<DEVID>`, and domain rows outside config.*. Only tests feed it documents, and they are spec-conformant.
- (d) Repo patterns followed: the contract was updated together with the code, and the banner follows the superseded convention.
- (e) No new lint, type or build errors.

**Side effects/regressions: none.**

## Cycle 3: final delta review (2026-10-05)

**Verdict: 9/10, PASS.** 0 criticals, 0 warnings, 2 non-blocking suggestions. This replaces the cycle-2 verdict.

### Scope

- Diffed against my cycle-2 copy:
  - C#: `PinnedCommandCatalog.cs` (`:73-77`, both-or-neither) and `CatalogTests.cs` (TTL at the cap; `config_get_as_write`, `domain_row_with_get_action_outside_config`, `only_config_get_by_domain`).
  - Generator: `:186`, `:195`, `:222-227`, and the node test.
  - Docs: contract §2 (`:49`, `:71`) and phase-06 `:73`.
- `command-catalog.json` is byte-identical (hash `b1d47a78…12c4`).

### Re-run checks

- Gates: node 11/11, `--check` exit 0. `dotnet build` 0 warnings, 0 errors, and 219 unit + 43 integration tests pass (fresh scratch copy).
- Catalog vs §11: still 0 differences. Hash and golden literals are unchanged.

### Cycle-2 findings

- **N1 fixed.** Probes are now rejected loudly: a table inside `<!-- -->`, under `<div>` or inside `<details>`, a table in a blockquote, an empty `##` or a `### 11.1` sub-heading between the tables, and a setext underline.
- Correctly accepted: `#hashtag` (not a heading in CommonMark), thematic breaks `***` and `- - -` (harmless), and an indented `   ## 12.` (it ends the section).
- **N2 fixed:** phase-06 `:73` now cites the §11 config-domain column.
- The loader's both-or-neither check is sound. It compares only actions, but `IsConfigDomainRow` has already limited domain rows to `config.*`, and a test covers a non-config `get`.

### New findings (suggestions)

- **N3: a GFM table without leading pipes inside §11 is read as prose.**
  - Probe: an extra table after the domain table, written with a `---|---` delimiter and holding a `system.debug` row, is accepted with the same hash. GFM renders it as a table.
  - The effect fails closed: the visible row is missing from the catalog, so the command gets `COMMAND_NOT_IN_CATALOG`.
  - Either of the two required tables written this way already fails the "exactly 2 tables" check.
  - Both the comment at `scripts/generate-command-catalog.mjs:206-209` ("Anything else is an error") and contract §2 ("accepts only the layout §11 uses") slightly over-claim.
  - Fix (one line): in `splitSection`, reject a prose line made only of `|`, `-`, `:` and spaces that contains a `|`, since that is a GFM delimiter row. The pinned §11 has none.
- **N4: stale wording.** `scripts/generate-command-catalog.mjs:175` and `design-contract.md:49` still say §11 runs "up to the next level-2 heading". The code (`:185-186`) and a later sentence of contract §2 end it at any ATX heading.

### Mandatory checks and side effects

- (a) Criteria met.
- (b) No regression: the pinned spec and the committed catalog pass the stricter rules.
- (c) No public-contract change: the stricter rules reject only off-spec inputs.
- (d) Patterns followed: the contract was updated together with the code.
- (e) No new errors.

**Side effects/regressions: none.**
