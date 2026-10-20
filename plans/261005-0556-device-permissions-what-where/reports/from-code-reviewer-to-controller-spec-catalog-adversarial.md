---
title: Adversarial validation - phase 1 rework, catalog sourced from MQTT spec section 11
date: 2026-10-05
reviewer: code-reviewer (adversarial, read-only on project)
decision: PASS_WITH_RISK
---

# Verdict: PASS_WITH_RISK

No reachable regression and no exploitable defect at the pinned spec. The committed catalog matches spec §11 row for row, and JS hash = C# hash on every catalog I tried. Claims 2, 4 and 6 are disproven as worded, and claim 1 is disproven in part (namespace count). Each disproof below has a repro. Repro scripts are in `C:\Users\toann\AppData\Local\Temp\claude\D--WORKSPACE-PROJECT-HMS-SERVER\daebfbef-46ca-4e9f-b506-cf6e26aef926\scratchpad\adversarial-spec\` (spec obtained only through `git show 83e93d77e:engineering-docs/api/mqtt-api.md`: blob 6485e00f, sha256 ef41c113, both match; no HMS-IDF source opened). All mutations were made on a scratch copy of the solution (`sln/`), and that copy matched the project byte for byte afterwards.

## Claim 1: catalog = §11 tables (holds, except the namespace count)
- Independent re-parse (`verify-independent.mjs`, a different technique from the generator): 94/94 rows and 6/6 domains, 0 field differences. Rows have 9 fields; R49 W18 C17 D3 B3 F4; TTL 60000×24 and 120000×70; 46 codes; 12 types. `--check` exits 0.
- An action-header decoy table in §10 is ignored (`attack-parser.mjs`).
- DISPROVEN (namespace count): the namespace count is checked only when the regex matches (`generate-command-catalog.mjs:163`, `:317`). Changing the spec to `94 cặp, 13 namespaces` (plural) is ACCEPTED, and so is removing the namespace phrase. Only the pair count is mandatory.

## Claim 2: parser never fails silently (DISPROVEN)
Inputs that are accepted but read differently from the table (`attack-parser.mjs` against the real spec):
1. **Silent row loss.** If the last domain row has no leading pipe (`` `adc` | `adc.view` | `adc.config_edit` |``), the result is ACCEPTED with 5 domains and `adc` gone. GFM still renders that line as a table row: leading pipes are optional, and GFM spec Ex. 202 shows a pipe-less line directly under a table becoming a row. Cause: `isTableLine` (`:199`) ends the table, and the line goes to `prose` (`:203`). The domain table has no count check. The same edit on the last action row is caught only because of the "94 cặp" check. Runtime effect fails closed (`config.*` for adc gives `PERMISSION_UNMAPPED`). At this pin, CatalogTests (6 domains, 46 codes) would catch it, but on a re-pin the generator emits a wrong catalog without error.
2. **NFC rewrites policy strings.** The whole document is normalized (`:170`), not just the literals the parser matches. U+212A KELVIN SIGN inside a code cell becomes ASCII `K`: `` `sys.info_view\u212A` `` is read as `sys.info_viewK`, and an action becomes `system.get_infoK`. U+1FEF becomes a backtick, so a cell with no backticks is accepted as a code span.
3. **TTL accepted but changed or unloadable.** `9007199254740993` is emitted as `9007199254740992` (precision loss). `2147483648` and `86400001` are accepted by the generator, but spec §3 caps `ttl_ms` at `1..86400000`. The loader rejects `2147483648` (`ROW_INVALID`, probe), so this fails late, not at generation.
4. **Layouts that are not a table or heading are accepted.** Delimiter row with fewer cells than the header (GFM Ex. 203: not a table). Domain table inside a code fence or indented 4 spaces. A §11 heading indented 4 spaces (a code block, not a heading). A second `## 11.` section (the first one wins silently). In each case the data is identical to the baseline, so the impact is low.
5. A hyphen-minus is inside the charset. Confirm `` `-` `` is read as `confirm_required=true`, template `-`. Permission `` `-` `` becomes the code `-`.

Loud as claimed: renamed or reordered headers (action and domain), missing or extra cells, missing backticks, class `X`/`r`, permission or confirm outside the charset, TTL `0`/`-1`/empty/`1.2e5`/`0120000`, Dry run `Yes`/`Khong`, duplicate action or domain, missing domain table, empty domain table with by-domain rows, a third table, stray lines, renamed heading, escaped pipe, lone CR. CRLF and NFD documents give output identical to the baseline.

## Claim 3: JS hash = C# hash; loader error codes (holds)
- 400 random loader-valid catalogs (strings over `[A-Za-z0-9_.<>-]`, TTL 1..2^31-1, nulls, ordinal edge types) hashed by JS `catalogHash`: 400/400 load through `PinnedCommandCatalog.Load` (`make-cases.mjs`, `probe/probe.cs`). The golden literals are byte-identical in JS and C# (714 B, 2 escapes; `golden-compare.mjs`).
- 22 crafted or raw-byte cases all fail with only `METADATA_INVALID`, `HASH_MISMATCH` or `ROW_INVALID`. They include: lone surrogates, invalid UTF-8 in a row or in metadata, escaped duplicate keys (`t\u0079pe`), `commands:null` and `[null]`, null type/action/class with a matching hash, TTL `120000.0`/`1.2e5`/`2147483648`, depth-70 nesting, an empty document, `null`, `[]`, and a trailing comma.
- Not violations, but notes on how far tamper detection reaches. These load: an uppercase `catalog_hash` (`PinnedCommandCatalog.cs:56` uses OrdinalIgnoreCase, while the canonicalization string says lowercase); edited `catalog_version` / `source_*` (not covered by the hash; only CatalogTests pin them); 0 commands; by-domain rows with no `config_domains` (the generator rejects this, the loader does not).

## Claim 4: nothing outside the spec, no reliance on firmware source (first half holds, second half DISPROVEN)
- Catalog and loader: no audit/body/deferred/dispatcher fields or rules (diff against the pre-rework backup). `dispatcher_default_ttl_ms` now fails as unknown. 120000 is backed by spec §3 ("bỏ hoặc 0 dùng default 120000 ms").
- Server source still justifies behaviour with firmware facts the spec does not state (the spec gives no matching rule; that is gap G3):
  - `src/Hms.Server.Application/Commands/CommandPermissionResolver.cs:7-9` ("Mirrors the firmware's cmd_descriptor_required_permission"), `:55` ("as cJSON does") and `:70` ("cJSON fails to parse such a string").
  - `tests/Hms.Server.UnitTests/CommandPermissionResolverTests.cs:32-34` (`..._like_the_firmware`, "cJSON unescapes before its compare") and `:80` ("The firmware reads the key case-sensitively").
- Binding docs:
  - `design-contract.md:162` gives "because the firmware labels misstate what several codes gate" as the reason. The spec has no labels; §10 of the contract already gives the spec-based reason.
  - The phase-01 success criterion at line 130 ("no firmware code file path in ... contract, plan, phase") is not met literally: `plan.md:135` cites `components/device-ops/src/ops_config.c:48-66` and `cmd_config.c:30-38` (marked as superseded), and `plan.md:195` and `phase-01...md:12` name `cmd_descriptor.c`.

## Claim 5: no reachable regression (holds)
- DTO `ttlMs`: before the rework, 70 rows had `null` and fell back to 120000; now they carry an explicit 120000. That is 0 differences over 94 rows (`compare-old-new.mjs`).
- Handler TTL: `DefaultTtlMs is > 0` falls back to 120 s, so it is unchanged. `HmsIdfProvisionalProtocolAdapter` derives `ttl_ms` from the plan, also unchanged.
- `DefaultTtlMs` is still `int?`. The resolver, MeController, handler, CommandDefinition and CommandContracts all have mtimes before the rework backup (19:37); the resolver's is 18:27.
- Full suite on the scratch copy: 204 unit + 43 integration + 9 node tests green.

## Claim 6: tests lock claims 1-3 (DISPROVEN, surviving mutants)
- **Row content is not locked by any CI test.** Changing `control.pulse` `required_perm` from `gpio.do_write` to `gpio.do_view` (physical output gated by a view code), with a recomputed hash, passes all 204 + 43 + 9 tests. Only `--check` exits 1, and it needs HMS-IDF; CatalogTests.cs:278-279 says CI does not run it. (`content-mutant.mjs K4`. K1, K6 and K7 were killed by the high-risk and domain tests.)
- Node suite survivors, from `mutate-js.mjs` and `mutate-js2.mjs` (the behaviour is correct today; it is just not tested):
  - J1: domain-table header check deleted.
  - J2: `tables.length < 2`, so a third table is ignored.
  - J3: empty-domain-table check deleted. The phase's planned case "thiếu bảng domain khi có dòng Theo domain" goes through the "2 tables" error instead.
  - J4: NFC normalization dropped. The NFD claim is untested; there are no CRLF or NFD tests at all.
  - J7/J8: domain cells need no backticks or charset check.
  - J10/J11: `confirm_required` and `dry_run` cross-wired. The fixture's only confirm row is also its only dry-run row.
  - J12: stray-line guard deleted. The test still passes because `/table/` matches the header error.
  - J5 (CRLF replace) is an equivalent mutant.
- Loader survivors (`mutate-cs.mjs`, `probe2/probe2.cs`):
  - C18 `RespectNullableAnnotations=false`: 204/204 still green, while null `type`/`action`/`class` with a matching hash now throws `NullReferenceException` / `ArgumentNullException`, which are not documented codes.
  - C23: case-insensitive class letters.
  - C6: `DryRunSupported` taken from ConfirmRequired (equivalent on the pinned data).
  - C3: Ordinal hash compare (harmless).
- Planned node cases missing from the suite: header missing a column, row missing a cell, TTL `-1`/empty. The behaviour is loud today (`planned-cases.mjs`).

## Recommended actions (by priority)
1. Pin one full-content fingerprint in CatalogTests (all 9 fields + domains; the v2 `catalog_hash` itself would do). This kills K4 without HMS-IDF. Trade-off: it goes against the contract's DRY note, but it is the same pattern as `ManifestPairSetSha256`.
2. Parser: fail when a non-blank, non-heading line follows a table row with no blank line between them (kills the GFM Ex. 202 loss). Check the charset on the raw cell before NFC, or reject cells that change under NFC. Cap TTL at spec §3's 86400000 in both the generator and the loader. Make the namespace phrase mandatory, or drop it from the claim.
3. Add node tests for the J1-J4 and J7-J12 shapes, plus the planned cases. Add a C# case for null non-nullable strings with a matching hash.
4. Re-word the resolver comments and test names to say "server policy (spec gap G3)". Remove the firmware-label rationale at contract:162 and the firmware paths in plan.md:135/195 and phase-01:12 (or relax criterion line 130).

# Cycle 2 (fix cycle re-run, 2026-10-05): PASS_WITH_RISK (all residual risks low)

I re-ran every cycle-1 tool against the current code, plus new attacks on the new layout rules. The solution was copied to scratch `sln-c2/`, and every mutated file was confirmed byte-identical to the project afterwards. Baseline:
- node 10/10.
- `--check` exits 0.
- Independent §11 diff: 0 differences. The catalog is unchanged (`b1d47a78…12c4`).
- Golden literals identical (714 B).
- dotnet: 215 unit + 43 integration tests pass.

## Claim by claim
- **Claim 1 HOLDS.** The pair and namespace counts are now mandatory. "13 namespaces" and a removed namespace phrase both fail loudly (`attack-parser.mjs`).
- **Claim 2 holds for every cycle-1 case; three residual low-severity cases remain.** Every silent case reported in cycle 1 now fails loudly: pipe-less row, Kelvin sign in action/permission/domain/template, U+1FEF, TTL above 2^53 / Int32 / 86400000, hyphen Confirm, short delimiter, fence or indented table, indented or duplicated heading, glued prose. Re-run with `attack-parser.mjs` and `kelvin-c2.mjs`. The three residual cases (`attack-parser-c2.mjs`, `empty-actions.mjs`):
  - HTML-level layouts are not modelled and are accepted silently. The worst case: the domain table wrapped in a multi-line `<!-- … -->` with a blank line before `-->`. GitHub renders no domain table, but the parser reads the hidden table (6 domains). The parser treats `<!--`/`-->` as prose (`generate-command-catalog.mjs:218-221`). The same applies to a third table inside a blockquote or as raw `<table>` (ignored, while GitHub renders it), to an unclosed fence or `<!--` in §10, to a `<div>` directly above the action table, and to a setext or `##<TAB>` heading. All of these were data-equivalent in my probes. A rendered third table with extra rows would still trip the pair count, provided the count statement includes those rows.
  - Permission `` `-` `` is still accepted as a code (fails closed).
  - "0 cặp, 0 namespace" with an empty action table is accepted. The generator then emits a catalog that the loader rejects (`METADATA_INVALID`).
- **Claim 3 HOLDS.**
  - 400/400 random catalogs that are valid under the new rules and hashed by JS load in C# (`make-cases-c2.mjs`, `probe-c2/`).
  - Uppercase hash gives `HASH_MISMATCH`. TTL 86400001, `<devid>`, config.get as W, by-domain rows without domains and a null type/action/class with a matching hash all give `ROW_INVALID`. Empty commands give `METADATA_INVALID`. Every other malformed case maps to the 3 codes.
  - Residual: the loader's binding check is laxer than the generator's. A catalog where only config.get is by-domain (config.set fixed) loads; the generator requires both. Edited `catalog_version` / `source_*` fields load, but CatalogTests pin them.
- **Claim 4 HOLDS.**
  - Binding docs: no firmware file paths remain. Contract :162 now cites the §11 column. plan.md:135 is a history note that V1 now rests on spec §6.1.
  - Server source and tests: the resolver, its tests and CommandDefinition cite spec §3/§6/§11 and G3. No cJSON or `cmd_descriptor` remains.
- **Claim 5 HOLDS.**
  - Unchanged since cycle 1: MeController, the send handler, CommandContracts, DevicePermissionVocabulary/DevicePermissionCodes, the provisional protocol adapter and the catalog JSON. CommandDefinition changed in comments only.
  - The 93 cycle-1 resolver and catalog tests, run against the new code, all pass.
  - The integration test now pins the 10 DTO keys.
  - The only resolver change is that `catch (InvalidOperationException)` now also swallows `ObjectDisposedException`. That path is unreachable: payload JsonElements come from MVC System.Text.Json binding and are not disposable. It also fails closed.
- **Claim 6 MOSTLY HOLDS.**
  - The content mutant (control.pulse with a recomputed hash) is now killed by `Committed_catalog_content_is_pinned`.
  - Cycle-1 survivors J1-J4, J7, J10-J12 and C3/C6/C18/C23 are all killed.
  - New C# rule mutants C24-C28 and C30 are killed.
  - Remaining survivors, all low:
    - N16a/b/c: if NFC normalization were re-added to the action, domain or template identifiers, nothing fails. Only the permission column has a Kelvin-sign test, so "identifiers as written" is locked for one of four identifier columns.
    - N7: the `~~~` fence is not tested.
    - N9 and C32: the TTL boundary 86400000 is not tested.
    - N13 and C29: config.get as a non-R class is not tested on either side.
    - C31: the loader's `Type == "config"` check is not tested.
    - J8a/b: the parser-level domain charset checks are untested, but `assertPolicyCharset` on the CLI path still catches them.
    - N18 is equivalent on the fixture.

## Suggested follow-ups (optional, low)
1. Add a node case with a Kelvin sign in an action, a domain and a template.
2. Add tests for the `~~~` fence, TTL 86400000 accepted, config.get as W, and a non-config `x.get` by-domain row in the loader.
3. Optionally reject `<!--`, `-->`, `>`-prefixed table lines and HTML block starts inside §11. Reject an empty action table.

# Cycle 3 (final bounded cycle, 2026-10-05): PASS_WITH_RISK (low, non-blocking)

Judged against the narrowed contract §2. Items the contract lists as not modelled (constructs outside §11) and the `-` permission cell are not re-raised.

**Baseline:**
- node 11/11.
- `--check` exits 0.
- Independent §11 diff: 0 differences.
- Golden literals identical.
- 219 unit + 43 integration tests pass on scratch copy `sln-c3/`. Every file I mutated was compared byte for byte with the project afterwards.

**Re-runs:**
- Cycle-1 and cycle-2 attacks: all fail loudly, except the items that are now in contract scope.
- The content mutant is killed by `Committed_catalog_content_is_pinned`.
- 400/400 JS-hashed catalogs load.
- The crafted loader cases all map to the 3 codes. "Only config.get by-domain" now gives `ROW_INVALID`.

**Claims:** 1, 3, 4 and 5 hold. Claims 2 and 6 hold except for the following.

**Disproofs** (reproducible, inside §11, low; `attack-parser-c3.mjs`; GitHub rendering checked with `gfm-probe.mjs` against api.github.com/markdown, synthetic text only):
1. **The counts can come from hidden text.** Replace the count line with `Snapshot <!-- 94 cặp, 12 namespace --> 95 cặp, 13 namespace trong snapshot.` GitHub renders only "95 cặp, 13 namespace", but the parser accepts the 94-row table: `PAIR_COUNT` takes the first match, which is inside the inline comment. The contract only forbids HTML *blocks* (lines starting with `<`). Policy data cannot be hidden this way, because inline HTML in a cell fails `codeOf`.
2. **A list item glued to a table.** A list item directly above the action or domain table (no blank line) is accepted. GitHub renders those lines as text of the list item, not as a table. The data is the same and the text stays visible, so this is very low.

**Doc nit:** contract line 49 says "takes §11 up to the next level-2 heading", while the same paragraph and the code end §11 at any ATX heading (a `###` inside §11 fails with "found 1"). The behaviour is safe; the two sentences disagree.

**Surviving mutants** (`mutate-js-c3.mjs`, `mutate-cs-c3.mjs`). Cycle-2 survivors N7, N9, N13, N16a-c, J8a, C29 and C32 are now killed, as are 7 of the 9 new rule mutants (P1-P4, P7, P8, C33). Remaining:
- **J8b:** dropping the config.set permission charset check in the parser. The CLI's `assertPolicyCharset` still catches it.
- **P5/P6:** an empty `##` line or an indented ` ## ` not ending §11. The current code is correct; neither case is tested.
- **C31:** the loader's per-row `Type == "config"` check. The new test is killed by the catalog-level get/set check instead. The current loader correctly rejects `adc.get` + `config.set` by-domain with `config.get` fixed; no test covers that input.
