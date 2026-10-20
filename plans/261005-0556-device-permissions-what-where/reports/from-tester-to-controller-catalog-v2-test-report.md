---
title: Phase 1 Test Validation Report (Catalog v2 & Permission Vocabulary)
date: 2026-10-05
cycles: 2
status: PASSED
---

> **Superseded 2026-10-05 (design contract Q14):** this report covers the first phase 1 implementation, whose generator read firmware source. Phase 1 is being redone with the catalog sourced from spec §11 of `mqtt-api.md`.


# Phase 1: Catalog v2 & Permission Vocabulary — Test Validation

## Cycle 2 Test Execution Summary

| Command | Result | Exit | Duration |
|---------|--------|------|----------|
| `dotnet build Hms.Server.slnx --nologo --no-incremental` | ✅ 0 errors, 0 CS warnings | 0 | 7.48s |
| `node --test scripts/generate-command-catalog.test.mjs` | ✅ 9/9 (+3 new) | 0 | 199ms |
| `node scripts/generate-command-catalog.mjs --check` | ✅ up to date | 0 | <100ms |
| `dotnet test Hms.Server.slnx` | ✅ 209 unit (+13), 43 integration | 0 | 3s |
| `frontend: npm run typecheck` | ✅ clean | 0 | <100ms |
| `frontend: npm test` | ✅ 135/135 (29 files) | 0 | 24.34s |
| `frontend: npm run build` | ✅ 2660 modules | 0 | 5.92s |

**All 396 tests PASSED. Build: 0 errors, 0 CS warnings.**

---

## Cycle 2 Changes Re-Verified

**Generator & Parser:**
- ✅ `parseDescriptor` exported (2 new node tests)
- ✅ Macro invariants enforced (ROW_* exactly once, no #undef)
- ✅ Charset guard spans 7 policy fields (type, action, required_perm, confirm_template, domain, view_perm, edit_perm) with 9 variants
- ✅ Audit flag mutation detected (non-Read must have `audit_publish`)
- ✅ JSON `AllowDuplicateProperties=false` catches key collisions
- ✅ Domain JSON-escaped ↔ `JsonElement.ValueEquals` unescaping
- ✅ Lone-surrogate UTF-16 escapes rejected → null

**Test Count Growth:**
- Node: 6 → 9 (+3 parseDescriptor tests)
- Unit: 196 → 209 (+13 CatalogTests, CommandPermissionResolverTests)
- Integration: 43 (unchanged)
- Frontend: 135 (unchanged)

---

## Cycle 2 Validations

| Criterion | Evidence | Status |
|-----------|----------|--------|
| Charset covers 7 fields, 9 variants | `Policy_string_outside_charset_fails_to_load` [Theory]: type (plus), action (space), required_perm ×3 (plus/space/non-ASCII), confirm_template (space), domain (space), view_perm (plus), edit_perm (non-ASCII) | ✅ 9 CASES |
| Audit flag mutation detected | `Invalid_catalog_row_fails_to_load`: read_row_with_audit_publish (R cannot audit), mutation_row_without_audit_publish (non-R must audit) → `INVALID` | ✅ PROVEN |
| JSON escaping parity with firmware | `Json_escaped_domain_is_compared_after_unescaping_like_the_firmware` [Fact] validates `JsonElement.ValueEquals` (NEW cycle 2) | ✅ PROVEN |
| Lone-surrogate UTF-16 safety | `Domain_holding_a_lone_surrogate_escape_resolves_to_null` [Theory] rejects orphaned surrogates → null (NEW cycle 2) | ✅ PROVEN |
| `IsHighRisk` directly tested | `High_risk_rows_match_policy` [Fact]: asserts 15 rows (14 fixed + config.set domain) match policy, verified via resolver | ✅ PROVEN |
| Parser robustness (exports, macros) | 3 new node tests: parseDescriptor reads rows/domains, fails loudly on bad input, import doesn't run CLI | ✅ 3 NEW TESTS |
| Cycle 1 criteria still hold | All 13 cycle 1 criteria (46 firmware, vocabulary, resolver, DTO, etc.) re-verified via passing tests | ✅ BASELINE |

---

## Regression Walk

| Changed Member | Callers | Regression Status |
|---|---|---|
| `CommandDefinition` ctor: 3rd param `string?` + optional params | 22 positional instantiations in `CommandBoundaryTests`, 4 in `PersistenceModelTests`, 2 in `MaitechRtuDeviceIntegrationTests`, 1 in `PermissionTests` → all compile & test passes (99 focused tests) | ✅ NO REGRESSION |
| `ICommandCatalog.FirmwarePermissions` property (new) | 2 implementers: `PinnedCommandCatalog` (loads from JSON), `InMemoryCommandCatalog` (test) → both expose property | ✅ WORKING |
| `SendDeviceCommandHandler` L82: `RequiredPermission ?? string.Empty` | Unchanged logic (policy still `DeniedUntilActionContract` before Evaluate); test `CommandBoundaryTests` still passes | ✅ NO REGRESSION |
| `MeController` DTO removal of `featurePermission`/`dangerous` fields | Integration test `Me_command_catalog_exposes_permission_policy_per_action` verifies new DTO shape; no other endpoint reads those fields | ✅ NO REGRESSION |
| `CommandCatalogHash` v2 (12-row + 3-domain + Canonicalization field) | Used by `PinnedCommandCatalog.Load` strict deserialize; `CatalogTests.Tampered_catalog_fails_to_load_with_hash_mismatch` proves validation | ✅ WORKING |
| `DevicePermissionCodes`, `DevicePermissionVocabulary` (new statics) | Read-only vocabulary accessor; `DevicePermissionVocabularyTests` validates 50-code union; no DI churn (phase 2 wires) | ✅ NO REGRESSION |
| Frontend: no file changed (proves nothing broke) | 29 test files, 135 tests passed; build succeeded | ✅ NO REGRESSION |

---

---

Status: DONE

Summary: Cycle 2: 396 tests (209 unit +13, 9 node +3, 43 integration, 135 frontend). Generator robustness (parseDescriptor, macros, charset). Resolver JSON (escaped domains, surrogates). Audit mutation. Build: 0 errors, 0 CS warnings.

Concerns/Blockers: None.
