---
title: Adversarial validation - phase 1 catalog v2 and permission vocabulary
date: 2026-10-05
reviewer: code-reviewer (adversarial, read-only)
decision: PASS_WITH_RISK
---

> **Superseded 2026-10-05 (design contract Q14):** this report covers the first phase 1 implementation, whose generator read firmware source. Phase 1 is being redone with the catalog sourced from spec §11 of `mqtt-api.md`.


# Adversarial validation: phase 1 (catalog v2, permission vocabulary)

Scratch root for every repro below: `C:/Users/toann/AppData/Local/Temp/claude/D--WORKSPACE-PROJECT-HMS-SERVER/daebfbef-46ca-4e9f-b506-cf6e26aef926/scratchpad/adversarial/` (called `ADV/`).
Probes in `ADV/probe/` reference the built `src/Hms.Server.Infrastructure/bin/Debug/net10.0/*.dll`. To rerun one, copy `Program.<name>.cs.txt` over `Program.cs` and run `dotnet run -c Release`.

## Method

- Compiler oracle for the firmware table. Lines 36-396 of `cmd_descriptor.c` @83e93d77e, copied verbatim, were compiled with the ESP riscv32 gcc (`-S -O0`). `ADV/oracle/decode.mjs` decodes the emitted `.word/.byte/.zero` data. The C compiler, not a second hand-written parser, evaluates comments, macros and literals. A negative control (`decode-neg.mjs`) detects 2 injected diffs.
- Differential fuzz: 3000 random catalogs over the full 67-character policy alphabet (962 with sort ties, plus empty arrays and TTL 1 and int.MaxValue). Each was hashed by JS `catalogHash` and by C# `CommandCatalogHash.Compute` (`ADV/fuzz-gen.mjs` plus the current `probe/Program.cs`).
- Loader probe: `Program.loader.cs.txt` loads 26 malformed or tampered variants through `PinnedCommandCatalog.Load`, with the hash recomputed where needed.
- Resolver probe: `Program.resolver.cs.txt` (25 payload variants), plus `Program.fingerprint.cs.txt` and `Program.valueequals.cs.txt`.
- Parser mutations: `ADV/parser-mutations.mjs` runs on `gen-copy.mjs`, which is the generator plus `export { parseDescriptor }`. `ADV/silent-oracle.mjs` confirms the C semantics with gcc.
- Mutation run on a scratch copy of the solution (`ADV/mut/`, now reverted), plus the full suite in that clean copy.

## Verdict per claim

### Claim 1 (JS catalogHash == C# Compute for every accepted catalog): HOLDS
- Fuzz: 3000/3000 equal, 0 mismatches, including 962 tie cases. Both sorts are stable and ordinal.
- Golden literals are byte-identical: 912 chars, the six-character escape is present and there is no raw `<` (`ADV/golden-compare.mjs`). JS rehash of the committed catalog = embedded `83c801a9...bd60`.
- Accepted JSON variants that a text diff would flag still hash the same. Duplicate keys are last-wins in both STJ and JSON.parse (probe E). An escaped `.` inside `required_perm` loads with an unchanged hash (G). `6e4` and `60000.0` are rejected (F).

### Claim 2 (loader rejects every malformed or tampered catalog; never accepts a firmware-invalid row): DISPROVEN (narrow)
- The firmware rejects a mutation row that is not audited. `cmd_descriptor.c:465` is `klass != R && audit_publish == audit_exempt -> invalid`, and `audit_exempt` cannot be represented in the catalog (the generator rejects that field at mjs:311 and enforces the rule itself at mjs:373). The loader only checks the R direction (`PinnedCommandCatalog.cs:149`).
  Repro (probe A): set `audit_publish=false` on `system.set_hostname` (W) or `system.reboot` (F), recompute the hash with `Compute`, then call `Load`. Result: `LOADED count=94` (hash `2884778a3be5...` and `1420615fafd5...`).
  Mitigation today: the field is not projected, and `Committed_catalog_flags_match_firmware` (CatalogTests.cs:198) catches it for the committed artifact.
- Tampered metadata outside the hash is accepted (probe D): `catalog_version="anything"`, `source_commit="not-a-commit"`, `source_hash="x"`, `source_path="C:/evil"` all give `LOADED`. An uppercase `catalog_hash` is also accepted (`OrdinalIgnoreCase`, PinnedCommandCatalog.cs:54). The hash scope is by design (§2). The CI test covers version, commit, path and hex format, but not the `source_blob`/`source_hash` values.
- Exception types: holds. Null members, a null `commands`, a null array element, invalid UTF-8 (in a row and in metadata), an empty stream, an array/null root, truncated input and 1000-deep nesting all fail with documented codes only.

### Claim 3 (exact parse of the pinned file; fails loudly on unknown fields, escaped literals, changed macros): exact parse HOLDS, fail-loud DISPROVEN (narrow)
- Oracle: 94 rows, 6 domains, 0 `audit_exempt` rows, R49 W18 C17 D3 B3 F4, 24 rows at TTL 60000, 46 permissions. **0 field diffs** across all 12 row fields and the domain table, including the literal blocks with embedded `/* */` comments.
- Fails loudly on: unknown `.audit_exempt`; a hex-escaped literal (in a row and in a macro argument); in-place edits of the `ROW_MUT`/`ROW_DOMAIN` bodies; string concatenation; `#if 0`; a designator set twice; `#ifdef` in the domain table.
- Silently accepted while C reads it differently (each confirmed with gcc):
  1. `#undef ROW_READ` plus a redefinition before `s_manifest`. The generator says `system.get_info` is class R; gcc says W. `macroDefinition` validates only the first `#define` (mjs:230-244), so this is a changed macro definition that goes undetected.
  2. Swapping the `config_domain_policy_t` view/edit fields. The positional mapping (mjs:291) is never checked against the struct.
  3. Octal `060000`. The generator gives 60000 (`Number()`, mjs:334-335); gcc gives 24576.
  4. A `//` comment that ends in a backslash. gcc swallows the next row (93 rows, no `status.dump`); the generator keeps it (mjs:200-202).
  Every case needs a firmware change plus a re-pin. The pinned commit is unaffected.

### Claim 4 (Resolve never returns a permission the firmware would resolve differently; fixed rows ignore the payload): HOLDS, with a contract deviation
- Results match firmware L424-439. `wifi_ap` and JSON-escaped `wifi_ap` give `wifi.ap_view`. An escaped key `domain` gives `mqtt.config_edit`. A duplicate JSON key gives last-wins (`mqtt`), and the adapter-style STJ re-serialization emits only that value. These all resolve to null: `""`, `WIFI_AP`, a leading space, 42, JsonElement number/null/object, a `JsonValue` node, `char[]`, an OrdinalIgnoreCase dictionary keyed `Domain`, nested `data.domain`, and a trailing escaped NUL (the server is stricter than C `strcmp`; firmware admission also rejects that escape, cmd_security.c:385-392). Fixed rows ignore the payload, null payload included.
- Firmware claim check verified: `security_json_tree_is_strict` (cmd_security.c:395-407) recurses through every nested object and rejects case-insensitive duplicate keys across the whole envelope (:421, :433, :668).
- Deviation: an unpaired high-surrogate escape in a JsonElement string makes `Resolve` throw `InvalidOperationException` (CommandPermissionResolver.cs:54, `GetString()`). Contract §5 says "anything else -> null". **Not reachable:** the resolver is not wired in, and `SendDeviceCommandHandler` calls `CommandRequestFingerprint.Compute` first (CommandContracts.cs:145), which already throws `JsonException` on the same payload. That is a pre-existing gap and out of scope. `JsonElement.ValueEquals(key)` is verified non-throwing and escape-aware.

### Claim 5 (no reachable regression): HOLDS
- (a) Full suite in a clean scratch copy: 196 unit + 43 integration passed. Per-file test counts are unchanged outside the planned `CatalogTests` rewrite (2 -> 18). No runtime reader of `Dangerous`/`DefaultTtlMs`/`Idempotent` exists via `plan.Definition`. The handler's TTL and permission paths sit behind `DENIED_UNTIL_ACTION_CONTRACT` (SendDeviceCommandHandler.cs:74-77). `DependencyInjection.cs` and the csproj are byte-identical to the backup. `InMemoryCommandCatalog` is only constructed in tests.
- (b) The frontend fails closed. `can()` returns false on a falsy permission before any `*` check (core/auth/permissions.ts:62, permissions.ts:13), so even a master-admin gets false. `canRenderCommand` also requires `available` (all false). `requiresCommandConfirmation` (`undefined || bool`) cannot crash. `idempotent`/`defaultTtlMs` are only in types and test mocks.
- (c) `contract-artifacts-fixture.mjs` exits 0. `catalog-fixture.mjs` exits 0 both before and after (it pins plan copies, not the embedded JSON). `Hms.Server.Api.http` has no catalog references. `documentation-drift-check.mjs` fails identically before and after: the `brainstorm-report.md` banner problem is pre-existing.

### Claim 6 (tests lock the properties): PARTIAL
- Holds: the golden vector is shared byte-for-byte, and both suites compare against the literal, so either side can fail. The hash-field theory covers all 15 fields with a throwing default. The tamper, metadata and row-invalid theories assert the exact code.
- Missing proof (see below). Note also that the tester report claims "7 variants" for `Policy_string_outside_charset_fails_to_load`. The C# theory has 3 (`+`, space, `e-acute`; CatalogTests.cs:288-290). The 10 variants live in node:test.

## Missing proof
1. Surviving mutant: removing the loader charset checks for `type`, `action`, `confirm_template`, `domain`, `view_perm` and `edit_perm` (the `required_perm` check kept) leaves CatalogTests, Resolver, Codes, Vocabulary and CommandBoundary at **120/120 passed** (`ADV/mut/`). Only `required_perm` is exercised.
2. No test, and no loader check, for a non-R row with `audit_publish=false` (Claim 2).
3. The generator parser has no automated test. node:test imports only the 5 canonicalizer exports, `parseDescriptor` is not exported, and `--check` only runs the happy path on the pinned blob. The fail-loud behaviours are unlocked.
4. CI checks `source_blob`/`source_hash` for format only (CatalogTests.cs:57-58), so the success-criteria values `24b73b6d...` and `15fe0604...` are proven only by the manual `--check`. I verified them against HMS-IDF and they are correct today.
5. Resolver: no test for the unpaired-surrogate JsonElement case, which throws today.

## Reachable regressions
None found.

## Recommended actions (non-blocking for phase 2; items 1-3 before phase 3 wires Resolve or the transport plan projects the flags)
1. `PinnedCommandCatalog.ToDefinition`: add `commandClass != CommandClass.Read && !row.AuditPublish -> ROW_INVALID`, plus InlineData `mutation_row_without_audit_publish` (mutate `system.set_hostname`).
2. Extend `Policy_string_outside_charset_fails_to_load` to mutate each of the 7 policy-string fields. That kills mutant 1.
3. `CommandPermissionResolver.FindDomain` and `Resolve`: for a JsonElement string, match with `element.ValueEquals(key)` over `PermissionByDomain` keys instead of `GetString()`. Add an InlineData with an unpaired-surrogate payload, expecting null.
4. Generator hardening: require exactly one `#define` and no `#undef` per ROW_* macro; assert the whitespace-normalized `config_domain_policy_t` typedef text; reject integers with a leading zero; fail on a `//` line that ends in a backslash. Export `parseDescriptor` and add node:test cases with inline C fixtures for every fail-loud path.
5. Assert the `source_blob`/`source_hash` literals in CatalogTests next to `source_commit`, or state in the plan that only `--check` proves them.

## Unverified
- How the transport layer places the payload: `data.data` vs the provisional `data.params`. That is deferred to the transport plan, so the end-to-end firmware match is proven only at the resolver and admission level.
- Frontend gates (`typecheck`, `test`, `build`) were not rerun. No frontend file changed, and the tester reports them green.

## Cycle 2 (review-fix cycle 2, re-run 2026-10-05)

**Decision: PASS_WITH_RISK.** The cycle-1 findings are fixed and locked by tests. What remains is the generator accepting deliberately obfuscated C that the real compiler reads differently. None of it is present at the pinned commit. Probes ran against assemblies built at 18:29, newer than every cycle-2 edit.

**Corrections to cycle 1 (mine):**
- My claim that `ValueEquals` does not throw was wrong. My probe compared only against `wifi_ap`, which is 7 bytes, longer than the 6-byte raw token, so STJ returned false without unescaping. Against `system`, `mqtt` and `adc` it throws `InvalidOperationException` (`ADV/probe/Program.resolver.cs.txt`, "raw ValueEquals" rows).
- My cycle-1 run of `contract-artifacts-fixture.mjs` rewrote its default output `plans/260924-2044-hms-server-management/fixtures/contract-artifacts-report.json` (fixture lines 8 and 101; mtime 17:40:34). The content is byte-identical to the pre-change backup (same SHA-256), so nothing to revert. Still, it breached the read-only rule; future runs should pass an output path under the scratchpad.

**Re-runs:**
- node:test 9/9 pass and `--check` exits 0.
- Golden literals are still byte-identical (912 chars, with the escape).
- Hash fuzz: 3000/3000 equal, 962 with ties.
- Compiler oracle still shows 0 field diffs.
- Full suite in a clean scratch copy: 209 unit + 43 integration pass.
- The cycle-2 footprint is exactly the 6 named files.

### Claim 2: now HOLDS
- Probe A now gives `ROW_INVALID` for unaudited non-R rows of class W (`system.set_hostname`), F (`system.reboot`), B (`control.set_output`) and the domain-W row `config.set` (`ADV/probe/Program.loader.cs.txt` re-run).
- Duplicate keys: the earlier cycle-2 E-series run had broken JSON, so I rebuilt every mutation and checked it is valid JSON first (`ADV/probe/Program.dupkeys.cs.txt`). Duplicates in a row, a domain, at top level, with identical values or with an escaped key name all give `METADATA_INVALID`. A case-variant `Type` next to `type` gives `ROW_INVALID` (unmapped member).
- TTLs of 3000000000 and 4294967296 give `ROW_INVALID`, so the system fails closed even though the generator does not range-check uint32.
- Metadata outside the hash still loads (probe D). This is not harmful:
  - Policy is hash-covered.
  - `Metadata.CatalogHash` is the recomputed lowercase value, so a letter-case edit is invisible to clients.
  - CI now pins the commit, version, blob, source hash and canonicalization of the committed file.
  - A zero-row catalog fails closed and CI asserts 94.
  - Reaching any of this requires a tampered build artifact.

### Claim 3: exact parse HOLDS; accidental drift now fails loudly; obfuscated C still DISPROVEN (narrow)
- All four cycle-1 silent cases now throw (`ADV/parser-mutations.mjs`).
- These also fail loudly (`ADV/guard-bypass.mjs`): `# define` and `#  define` spellings, ROW_READ built through a helper macro, a star-placement or tagged typedef, and line splices between macro arguments, inside a designator name, inside a domain string, inside the typedef body, and as a `/`-splice-`/` comment in the table.
- Still accepted. Each mutation below makes the generator emit a different catalog from what gcc compiles, and each builds with `xtensa-esp32s3-elf-gcc -Wall -Werror=all -Wextra -Os` (flags taken from HMS-IDF `compile_commands.json`) with exit 0, 0 errors and 0 warnings:
  - **A:** `#un`, backslash-newline, `def ROW_READ`, then `#define`, backslash-newline, `ROW_READ(...CMD_CLASS_W...)`. gcc reads `system.get_info` as class W; the generator reports R. The directive regex requires `#`, `define` and the name on one physical line.
  - **B:** the digraph `%:undef` / `%:define ROW_READ`. Same divergence (W vs R).
  - **C:** `#define CMD_CLASS_C CMD_CLASS_F`. gcc reads `system.set_time` as class F; the generator reports C.
  - **D:** `#define audit_publish audit_exempt`. gcc gives `system.set_hostname` audit_publish 0 / audit_exempt 1; the generator reports audited.
  - **F4:** the original table marked `__attribute__((unused))`, plus `#define s_config_domains s_config_domains_live` pointing at a swapped table. gcc reads adc view_perm as `adc.config_edit`. The F3 variant without the attribute fails the IDF build (`-Werror=unused-const-variable`).
  - **G:** the expected typedef under `#if 0`, with `#else typedef const struct {domain; edit_perm; view_perm}` live. Same divergence for adc.
  - **H:** `#define config_domain_policy_t swapped_policy_t` after the expected typedef. Same divergence for adc.
- Not reachable today: the pinned file has exactly 7 `#include` and 3 `#define` (the ROW_ macros), no other directives, and splices only inside those 3 macros. Exploiting this needs a deliberately obfuscated firmware commit plus a reviewed re-pin of `SOURCE_COMMIT`.

### Claim 4: HOLDS; the resolver is now total for JsonElement strings
- These all resolve to null for both `config.get` and `config.set`, with no throw:
  - lone surrogates: uD800, uDC00, uD800x, wifi_ap+uD800, uD800+wifi_ap, adc+uD800
  - surrogate pairs: uD800 uD800, uDC00 uD800, uDBFF uDFFF, and the valid pair uD83D uDE00
  - `mq`+uD800+`tt`
- Escaped domains still resolve as cJSON would: escaped `_` or `w` gives `wifi.ap_*`, and an escaped key name gives `mqtt.config_edit`.
- `ObjectDisposedException` still propagates; it is thrown at the `ValueKind` pattern, before the `try`. `default(JsonElement)` and a `JsonNode` value give null. No input resolves a permission that an exact unescaped match would not.
- ESP-IDF v5.5.5 `components/json/cJSON/cJSON.c:723-753` rejects lone low and unpaired high surrogates, so the new code comment is accurate.

### Mutation checks
- C# (`ADV/mut/run-mutants.mjs`, focused suites):
  - Killed: dropping the 6-field charset checks (6 failures), dropping the non-R audit check (1), allowing duplicates (2), removing the catch (3).
  - Survived: broadening the catch to `Exception`. The `ObjectDisposedException` filter is unreachable dead code, harmless.
  - Equivalent mutant: GetString inside the same `try`.
- JS (`ADV/jsmut/run-mutants.mjs`):
  - Killed: removing the typedef check, the single-definition check, the `//`-backslash check, or the decimal-only integer check, and ignoring `expectedRowCount`.
  - **Survived:** removing the `static const config_domain_policy_t s_config_domains[]` declaration check. No node test covers it.

### Cycle 2 recommendations (non-blocking)
1. In `parseDescriptor`, before any directive scan:
   - apply C line splicing (drop every backslash-newline) and map a leading `%:` to `#`;
   - then allow-list directives: exactly the `#include` lines plus the three ROW_ `#define`s;
   - reject any other `#define`, `#undef`, `#if*` or `#pragma`.
   This closes A, B, C, D, F4, G and H, and the pinned file already satisfies it.
2. Add a node test for a `s_config_domains` declared with another type (kills the JS survivor).
3. Optional: drop or annotate the unreachable `ObjectDisposedException` filter.
