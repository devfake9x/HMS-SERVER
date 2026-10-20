---
title: "Code review - phase 1 catalog v2 and permission vocabulary"
date: 2026-10-05
reviewer: code-reviewer (read-only)
plan: plans/261005-0556-device-permissions-what-where/plan.md
phase: phase-01-catalog-v2-permission-vocabulary.md
score: 9/10
decision: PASS
criticalCount: 0
warningCount: 0
---

> **Superseded 2026-10-05 (design contract Q14):** this report covers the first phase 1 implementation, whose generator read firmware source. Phase 1 is being redone with the catalog sourced from spec §11 of `mqtt-api.md`.


# Code review - phase 1: catalog v2 and permission vocabulary

## Scope

- 13 source/test files named by the controller, the generator and its node test, and 3 doc/plan edits.
- I compared the whole tree with the pre-change snapshot (`diff -rq`, skipping bin/obj/node_modules). Nothing changed outside the listed files. The only other differences are the tester report and some `.tmp-*` build folders dated 2026-09-28/30, which predate this phase.
- Firmware source of truth: `git show 83e93d77e:components/command-dispatcher/cmd_descriptor.c`.

## Checks I ran myself

| Check | Result |
|---|---|
| `node --test scripts/generate-command-catalog.test.mjs` | 6/6 pass |
| `node scripts/generate-command-catalog.mjs --check` | exit 0 |
| Copy of the generator in the scratchpad, run twice | Byte-identical output; sha256 `e946b073...` equals the committed file |
| Same copy: one-byte edit / CRLF copy / missing repo | exit 1 / exit 0 / exit 1 with a clear message |
| My own parser over the firmware blob compared with the JSON (94 rows x 12 fields, 6 domains, row order) | 0 differences |
| Counts from my parser | R49 W18 C17 D3 B3 F4; 46 permissions; 24 rows with TTL 60000; 3 templates = 3 dry_run; 45 audit_publish; accepts_body = check/replace_rtu_config; outcome_deferred = restart_rtu; pair-set `dbda8317...`; 15 high-risk rows |
| My own v2 hash recomputation | `83c801a9...` = `catalog_hash` |
| `git show` bytes sha256 / `rev-parse` blob id | `15fe0604...0c36` / `24b73b6d...2f98` |
| Golden canonical literal, C# vs JS | Byte-identical (913 bytes) |
| `dotnet build Hms.Server.slnx --no-incremental` | 0 errors, 0 CS warnings (NU1900 only, environmental) |
| `dotnet test Hms.Server.slnx` | 196 unit + 43 integration pass |
| frontend `npm run typecheck` / `npm test` | clean / 135 pass (build not re-run; no frontend file changed) |
| Scratchpad probes of `PinnedCommandCatalog.Load` and `CommandPermissionResolver.Resolve` | see S1, S5 |

## Overall assessment

The implementation matches contract sections 1, 2, 3, 5 and 6 and the phase file. The tests check real behavior; none only executes code without asserting. The main claim, that the JS and C# hashes agree, is proven three ways: the shared golden vector, the hash check at load time, and my own recomputation. I found no critical or warning-level defect. The items below are hardening suggestions.

## Critical - none

## Warning - none

## Suggestions

**S1 (suggestion) The loader accepts duplicate JSON keys (the last one wins), although its comment says the catalog is "parsed strictly".**
- Evidence: `src/Hms.Server.Infrastructure/Commands/PinnedCommandCatalog.cs:19-24` (`StrictOptions` does not set `AllowDuplicateProperties = false`); the claim is in the comment at `:9-11`.
- Probe: a row with a second `required_perm` loaded. A second top-level `catalog_version` also loaded, and its value replaced the real one.
- Impact is low:
  - The hash covers the values that end up in force, so changing one without rehashing gives `COMMAND_CATALOG_HASH_MISMATCH`.
  - `CatalogTests` reads the raw JSON through `JsonNode`, which throws `ArgumentException` on a duplicate key.
  - `--check` catches any change to the text.
  - `catalog_version` and the `source_*` fields are not hashed. The loader only checks they are non-blank (`:45-49`); `CatalogTests.cs:42-67` and `--check` cover them.
- Fix: set `AllowDuplicateProperties = false`. The option exists in .NET 10, and my probe shows it throws `JsonException`, which the existing METADATA_INVALID / ROW_INVALID mapping already handles. Alternatively, start from `JsonSerializerOptions.Strict`.

**S2 (suggestion) A load failure cannot be diagnosed from the logs.**
- `PinnedCommandCatalog.cs:104-107` and `:116-119` drop the `JsonException` (no inner exception).
- `:62`, `:130` and `:151` throw `COMMAND_CATALOG_ROW_INVALID` without naming the row or field. The generator does name them.
- Fix: pass the `JsonException` as the inner exception, and put `type.action` or the domain into `Exception.Data`. `Message` stays the code, so the existing tests still hold.

**S3 (suggestion) The C# charset tests only change `required_perm`.**
- `tests/Hms.Server.UnitTests/CatalogTests.cs:287-299`: all three InlineData cases mutate `required_perm`.
- The checks on type/action (`PinnedCommandCatalog.cs:142-143`), confirm_template (`:148`) and domain/view/edit (`:128`) have no C# test. Deleting any of them would leave the suite green.
- My probe confirmed they work today (a domain of "adc" plus a trailing LF gives ROW_INVALID).
- Add one InlineData per field.
- Side note: the tester report says this theory has "7 variants". It has 3; the 7+ variants are in the node test.

**S4 (suggestion) The generator and the loader enforce different invariants.**
- The generator requires every mutation row to set audit_publish (`scripts/generate-command-catalog.mjs:373`). This mirrors the firmware check at L452-483, given that the generator does not allow `audit_exempt`.
- The loader only enforces "a read row sets neither audit_publish nor outcome_deferred" (`PinnedCommandCatalog.cs:149`).
- No effect today: `CatalogTests.cs:198` locks the committed JSON, and audit_publish is not projected.
- Add the same check to the loader before the transport plan starts projecting audit_publish.

**S5 (suggestion) The resolver throws instead of returning null when `domain` holds a lone UTF-16 surrogate.**
- `src/Hms.Server.Application/Commands/CommandPermissionResolver.cs:54`: `element.GetString()` throws `InvalidOperationException` when `domain` is a lone high-surrogate escape (probe).
- Contract section 5 says anything else resolves to null.
- Not reachable today:
  - The resolver is not wired yet.
  - The existing `CommandRequestFingerprint.Compute` call at `SendDeviceCommandHandler.cs:23` already throws `JsonException` on the same payload. That turns it into a 500 and predates this phase.
- In phase 3: catch the exception around `GetString` and return null. Consider answering such payloads with 400 at the controller.
- Other probe results:
  - An escaped underscore resolves to `wifi.ap_view`, consistent with how cJSON unescapes.
  - An escaped NUL resolves to null (fails closed).
  - A repeated exact `domain` key: last wins, and the adapter re-serializes the same dictionary.

**S6 (suggestion; the design contract requires this default) A domain row built without an explicit `Class` resolves to the weaker view permission.**
- `Class = CommandClass.Read` is the default (`src/Hms.Server.Domain/Commands/CommandDefinition.cs:30`), and Read maps to the view permission (`CommandPermissionResolver.cs:39-40`). A hand-built domain row that omits `Class` therefore resolves a mutation to the view permission.
- Production rows always set `Class` (`PinnedCommandCatalog.cs:163`).
- No change needed now. Phase 3 tests must pass `Class` explicitly for domain rows.

**S7 (suggestion) No automated gate runs the node test suite.**
- There is no root `package.json` or CI configuration, and the frontend vitest root does not include `scripts/`.
- Mitigation: if the JS canonicalizer drifts and that reaches the committed JSON, the C# load-time hash fails in `dotnet test`.
- Add `node --test scripts/*.test.mjs` to CI once CI exists.

**S8 (suggestion) Importing the generator can throw ENOENT.**
- `isEntryPoint` (`scripts/generate-command-catalog.mjs:488-493`) calls `realpathSync` on `argv[1]`. If `argv[1]` is not an existing path (for example `node -e "import(...)" foo`), the import throws ENOENT.
- Fix: wrap it in try/catch and return false.

## Deliberate deviations: my verdict

1. **Private constructor takes the parsed `CatalogDocument` (`PinnedCommandCatalog.cs:39-43,74`): ACCEPT.** The public surface (parameterless DI constructor, `Load(Stream)`) is as planned, and parsing is now separate from validation.
2. **Charset check as a char loop (`:171-172`): ACCEPT.** It is equivalent to the JS regex: a JS `$` without the `m` flag anchors only at the end of the input, and `char.IsAsciiLetterOrDigit` is ASCII-only. It also avoids .NET's `$` matching before a trailing LF; my probe confirmed a trailing LF is rejected.
3. **Extra checks: ACCEPT.** Each one fails closed or fails loudly:
   - the macro text is pinned (`.mjs:50-54,173-178`);
   - the blob id of the `git show` bytes is verified (`.mjs:410-414`);
   - confirm_required ⇔ confirm_template (`.mjs:370-372`), which matches the loader (`:147`) and contract section 2;
   - the extra loader tests (metadata, duplicate pair/domain, TTL, no permission source).

   The only gap is the asymmetry in S4.

## Acceptance coverage (criterion → proof)

| # | Proof |
|---|---|
| 1 | 6/6 node tests. Importing the module never calls git; git runs only inside `main` (`.mjs:393-446`, guard `:488-502`) |
| 2 | `--check` exit 0; two scratchpad runs gave identical bytes (= committed file); tampered copy → exit 1; CRLF copy → exit 0 by design |
| 3 | `command-catalog.json:2-11`: top-level order as in contract section 2 (asserted in `CatalogTests.cs:48-54`); 94 rows; `source_hash` / `source_blob` match my `git show`/`rev-parse`; `source_path` is repo-relative |
| 4 | `CatalogTests` loads the catalog; JS hash = C# hash = my recomputation `83c801a9...`; golden literal identical in `CatalogTests.cs:39` and the node test |
| 5 | Generator `assertPolicyCharset` (`.mjs:71-82,160-165`); loader `IsPolicyString` (`:171-172`); no escape table, only the `<`/`>` replacement (`.mjs:86-90`) |
| 6 | `CatalogTests.cs:69-189` (counts, TTL, templates, fingerprint) and `:209-245` (15-field hash theory); every count re-derived from the firmware |
| 7 | `PinnedCommandCatalog.cs:138-167` projects exactly 6 fields; `CatalogTests.cs:301-341` covers both directions of the confirm/template mismatch and a read row with audit_publish or outcome_deferred |
| 8 | `DevicePermissionVocabulary.cs:11-12`; `DevicePermissionVocabularyTests.cs`; grep shows `ServerCodes` is read only there |
| 9 | `CommandPermissionResolver.cs:14-60`; `CommandPermissionResolverTests.cs` (12-variant null theory, JsonElement, 8 per-namespace cases) |
| 10 | `MeController.cs:61-71,89-99` (exactly 10 fields); `ManagementApiTests.cs:201` new test |
| 11 | Every row is Denied (`PinnedCommandCatalog.cs:159`; `CatalogTests.cs:80-88`). Grep of src/tests: the old hash appears only in `CatalogTests.cs:19` (`FirmwarePairSetSha256`); the old version string nowhere |
| 12 | `dotnet test` 196+43 green; frontend typecheck and 135 tests green (re-run by me); frontend build green per the tester report (no frontend file changed) |

## Contract status: OK

- The public changes match the declared list.
- Everything else is additive: `CommandClass`, `DomainPermission`, `CommandDefinition.DispatcherDefaultTtlMs`, `PinnedCommandCatalog.Load(Stream)`, the `FirmwarePermissions` properties, the three new static classes, and the generator-only env var `HMS_IDF_REPO`.
- No DB schema, DI registration, route or server env var changed.
- `ICommandCatalog` has exactly two implementers, both updated. `CommandCatalogActionDto` and `CommandCatalogRow` have no consumers outside their owning files.

## Side effects / regression

None unintended. The only consumer-visible effects are the declared ones:

- **The old frontend reads removed DTO fields.**
  - `can(undefined)` returns false (`frontend/src/core/auth/permissions.ts:62`, `frontend/src/permissions.ts:13`), reached through `DeviceCommandModal.vue:75` and `App.vue:125`.
  - `DeviceCommandModal.vue:250` shows an empty permission label.
  - Every action was already `available=false`. The UI fails closed until phase 6, as planned.
- **The catalog version and hash changed**, so a client holding the old values gets `CATALOG_STALE`. This is intended.
- **Handler line 82 passes `RequiredPermission ?? ""` to the old policy.** This is unreachable for the pinned catalog: the `DENIED_UNTIL_ACTION_CONTRACT` check returns first (`SendDeviceCommandHandler.cs:74-77`), and every row is Denied. Test catalogs pass their own permission strings, so their behavior is unchanged.
- **Nothing persists or sends `CommandDefinition` over the wire.** I checked `EfCommandAuditStore` and the provisional adapter.

## Plan follow-ups (no plan file edited)

- All 12 Todo items appear done. I recommend the lead mark phase 1 complete. TDD red-first order cannot be checked because there is no git history.
- Phase 3 inputs: S5, S6. Transport plan: S4.
- I verified the firmware claim the plan relies on: `cmd_security.c` `security_json_tree_is_strict` rejects keys that differ only in case (strcasecmp, recursive), and `raw_has_escaped_nul` rejects an escaped NUL. The server's case-sensitive `domain` read and the device therefore stay consistent only while the adapter forwards the same dictionary and the device keeps this check.

## Unresolved questions

- Should the loader itself require `catalog_version == "hms-idf-" + source_commit[..9]`? Today only `CatalogTests` and `--check` enforce it. Not blocking.

## Cycle 2 (delta review, 2026-10-05)

**Verdict:** 9/10, PASS_WITH_RISK. 0 critical. One latent warning (W1) to fix before any firmware re-pin.

**Scope:** every file changed since the cycle-1 report (found with `find -newer`):
- Code: `PinnedCommandCatalog.cs`, `CommandPermissionResolver.cs`, `generate-command-catalog.mjs` and its node test, `CatalogTests.cs`, `CommandPermissionResolverTests.cs`.
- Also changed, non-code: plan reports and the harness JSON.
- Unchanged: the embedded JSON (sha256 still `e946b073...`), DI, the frontend and the docs.

**Re-run by me:**
- node:test 9/9; `--check` exit 0.
- `dotnet build --no-incremental`: 0 errors, 0 CS warnings.
- `dotnet test`: 209 unit + 43 integration pass.
- Probes in the scratchpad, results below.

### Cycle-1 items

| Item | Status | Evidence |
|---|---|---|
| S1 duplicate keys | Fixed | See note 1 |
| S3 charset coverage | Fixed | `CatalogTests.cs:290-311` covers all 7 policy-string fields. Each case fails only through its own charset check |
| S4 mutation must be audited | Fixed | `PinnedCommandCatalog.cs:153`; `CatalogTests.cs:343` |
| S5 resolver throws | Fixed | See note 2 |
| S8 isEntryPoint | Fixed | `generate-command-catalog.mjs:524-534`. The node test runs `node -e <import> not-a-real-path`, which would have thrown ENOENT before the fix |
| Adversarial parser cases (#undef, struct field order, octal, `//` splice) | Fixed | `.mjs:196-216, 229-237, 369-371`, plus the 15-case node table. By code inspection (no mutation run), each of these 4 cases would parse silently without its guard |
| S2, S6, S7 | Not done (user decision, accepted) | S2 is slightly worse now: a duplicate key inside a row reports METADATA_INVALID, not ROW_INVALID (documented at `:24`) |

1. **S1:** `PinnedCommandCatalog.cs:25` now sets `AllowDuplicateProperties = false`. My cycle-1 probe now gives METADATA_INVALID for a duplicated row key and for a duplicated `catalog_version`. `CatalogTests.cs:379-393` discriminates: without the option, both variants load.
2. **S5:** `CommandPermissionResolver.cs:56-76`. My probe shows `ValueEquals` throws `InvalidOperationException` for keys short enough to reach the STJ unescape path ("adc", "mqtt"), including for a lone low surrogate. For longer keys it returns false. The catch is therefore load-bearing; the adversarial report's "non-throwing" note was wrong.
   - A disposed document still propagates `ObjectDisposedException`.
   - Tests `CommandPermissionResolverTests.cs:40-50` discriminate.
   - An escaped underscore resolves after unescaping, consistent with cJSON (`:31-38`). An embedded NUL still resolves to null.

### New finding

**W1 (warning, latent) The generator's "fails loudly" guard is still not total.**
- The gap already existed in cycle 1 (I missed it then); it is not a cycle-2 regression.
- Two input classes are still misread silently: preprocessor conditionals, and line splices that move a comment boundary.
- Evidence:
  - `stripComments` rejects a splice only after `//` (`.mjs:229-237`).
  - `initializerBody` takes the first regex match (`.mjs:281-283`).
  - Nothing rejects `#if`/`#ifdef`/`#else`/`#endif` outside the initializer bodies.
- Repro (scratchpad `cycle2/parser-gaps.mjs`, using the exported `parseDescriptor`). I confirmed C semantics with `riscv32-esp-elf-gcc -E -std=gnu17`.

  | Case | Generator reads | gcc compiles |
  |---|---|---|
  | (A) `#if 0` decoy `s_config_domains` before the real table | Decoy table (adc `edit_perm` = `adc.view`) | Only the real table (`adc.config_edit`) |
  | (B) Backslash before the newline inside `*/` in a literal row | Swallows `.default_ttl_ms = 60000` (TTL null) | Keeps it |
  | (C) Same splice inside `//`, hiding a decoy table | Decoy table | Only the real table |

- Impact: none today.
  - The pinned blob has no conditional directive.
  - Its only backslash-newlines are the 4 ROW_* macro lines (verified in cycle 1).
  - After a re-pin, a common idiom (for example `#ifdef CONFIG_X` around two domain tables) would silently change a permission mapping.
  - `--check` cannot see this, and the generator does not pin the domain count.
  - The node test name "fails loudly on input it cannot vouch for" overstates what is covered.
- Fix before the next re-pin (cheap, and complete for these classes):
  1. Reject any `#if`/`#ifdef`/`#ifndef`/`#elif`/`#else`/`#endif` line in the stripped code.
  2. On the raw source, reject any backslash-newline outside the `#define ROW_*` lines.
  3. Require `s_config_domains[] =` and `s_manifest[] =` to each match exactly once.

### Checks a-e (cycle 2)

- **(a) Acceptance criteria:** still all met. New proof for criterion 3: `CatalogTests.cs:60-61` asserts the exact `source_blob` and `source_hash`.
- **(b) Regression:** none. Every cycle-1 resolver case gives the same result. The real catalog still loads (45 audited mutations).
- **(c) Contracts:** OK. The only new public surface is the `parseDescriptor` export in the dev script. Resolver and loader signatures are unchanged.
- **(d) Repo patterns:** followed. No review IDs appear in comments or test names.
- **(e) Build/lint/type:** no new errors.
