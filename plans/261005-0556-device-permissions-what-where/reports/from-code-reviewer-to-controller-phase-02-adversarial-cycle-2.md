---
title: "Phase 2 adversarial validation, cycle 2: cycle-1 fix claims A-F"
date: 2026-10-05
reviewer: code-reviewer (adversarial)
scope: EfDeviceAccessStores.cs, DeviceAccessContracts.cs, DeviceRole.cs, phase-02/03/04 notes, harness/context-snippets.json
decision: PASS_WITH_RISK
---

# Phase 2 adversarial validation, cycle 2

## Verdict (JSON-ready)

```json
{
  "decision": "PASS_WITH_RISK",
  "disprovenClaims": [
    {
      "claim": "E",
      "severity": "low",
      "evidence": "Mutants B1, B2 and B3 pass all 47 PersistenceModelTests, the only tests that reference the EF device stores. B1 removes the name check from the Update recheck (EfDeviceAccessStores.cs:76-83). B2 makes the Remove recheck treat a missing role as present (:101-108). B3 removes `error is DbUpdateConcurrencyException ||` (:244). Scratch probes P2, P3 and P1/P1b each kill one of them and pass on the current code.",
      "breakingScenario": "Three database-rejection branches have no test, and each one handles a race PostgreSQL can produce. (1) Role rename vs a concurrent rename or create of the same name: the unique index rejects the save. (2) A double-submitted role delete: the loser's DELETE matches 0 rows. (3) A membership replace or remove (expected A) after another admin removed the member and re-added it with A: the token rejects the old row Id although the current role equals the expected role again. Today the store maps these to DEVICE_ROLE_NAME_TAKEN, DEVICE_ROLE_NOT_FOUND and DEVICE_MEMBERSHIP_CHANGED. If a refactor drops any of these branches, CI stays green and the raw DbUpdateException becomes an HTTP 500 in phase 3."
    },
    {
      "claim": "F",
      "severity": "low",
      "evidence": "context-snippets.json:38 says phase 3 must return grantedBy as a plain Guid for POST /permissions/features, POST /permissions/resource-scopes and GET /permissions/{userId}. The phase-03 note (:213) covers only the POST 200 body, and phase 3 keeps the snapshot as it is until phase 4 (:59). Phase 4 keeps PermissionGrantSnapshot with the record lists (phase-04:125 'DTO cũ, giữ kiểu'; :63 'không cần code riêng'). JSON probe with web defaults on a stamped grant: \"grantedBy\":{\"value\":\"22222222-...\"}. The phase-04 test (:186) asserts only that grantedBy is non-null, so this shape passes it.",
      "breakingScenario": "After phase 3 stamps the grantor, POST returns grantedBy as a plain Guid, but GET /permissions/{userId} returns {\"value\": ...} for the same field, and that continues through phase 4. This contradicts the publicContracts line and the contract §6 rule for ids. No tested path catches it. The frontend does not read grantedBy today, so nothing breaks yet."
    },
    {
      "claim": "B",
      "severity": "info",
      "evidence": "The claim says 'a concurrency exception on a membership always maps to CHANGED'. The repository's own test Ef_membership_change_lost_to_concurrent_change_and_role_delete_reports_role_not_found (PersistenceModelTests.cs:1025-1046) maps a token conflict to DEVICE_ROLE_NOT_FOUND, because the Upsert recheck runs ThrowIfRoleMissing first (EfDeviceAccessStores.cs:210-214).",
      "breakingScenario": "Only the wording is wrong; the code matches the intended order. Accurate statement: on Upsert, a token conflict maps to DEVICE_ROLE_NOT_FOUND when the target role is gone and to CHANGED otherwise; on Remove it always maps to CHANGED. The general sentence 'reports the same code the pre-checks would report now' needs the same exception. In the remove-and-re-add case (probe P1) the store reports CHANGED where fresh pre-checks would accept the write. The code comment at :240-241 documents this, and it is the safer choice."
    },
    {
      "claim": "F",
      "severity": "very-low",
      "evidence": "phase-02 risk row 288 says ChangeTracker.Clear() is called 'only after a failed SaveChanges'. Save now also clears after a failure while staging or adding the audit row (EfDeviceAccessStores.cs:288-292). phase-02:114 still describes catching DbUpdateConcurrencyException before DbUpdateException. The code has one DbUpdateException catch (:282-287) and checks the exception type only in ThrowIfChanged.",
      "breakingScenario": "The documentation no longer matches the code. Behaviour is unaffected."
    }
  ],
  "unverifiedClaims": [
    "PostgreSQL behaviour was not run (no database, by constraint). The mapping relies on these PostgreSQL rules: a unique check waits for the conflicting transaction to finish, an FK check takes a KEY SHARE lock, and a token-guarded UPDATE or DELETE re-evaluates its row after waiting for the lock. So the loser's error arrives only after the winner commits, and the recheck reads committed state. This is reasoning from PostgreSQL semantics, not a measurement. The same holds for the rollback of the loser's audit row.",
    "A deadlock (40P01) between two store writes was not exercised. EF orders modification commands by key, so lock-order deadlocks look unlikely. If one did occur, the recheck could run before the surviving transaction commits, and Save would rethrow the raw exception, which matches the claimed 'otherwise rethrow' behaviour."
  ],
  "missingProof": [
    "B1, B2 and B3 above: no test for the Update NAME_TAKEN recheck, the Remove NOT_FOUND recheck, or the membership token conflict when the current role equals the expected role. Test shapes: scratch Cycle2ProbeTests P2, P3, P1 and P1b, which reuse the RaceInterceptor pattern.",
    "Role Update order STALE before NAME_TAKEN, claimed in B for both stores. The code is right (probe P7: stale version plus taken name reports DEVICE_ROLE_VERSION_STALE in EF and InMemory). Mutants B8 (EF, EfDeviceAccessStores.cs:117-119) and B9 (InMemory, DeviceAccessContracts.cs:123-125) swap the two checks and pass every test.",
    "The Update recheck checks names in the stored organization: mutant B10 uses the organization from the caller's copy and passes. Very low risk, because phase-3 handlers build the copy from Find, so it always carries the stored organization.",
    "Staging inside the try: mutant A3 moves stage() before the try and passes. Very low risk; no reachable trigger was found, because stage() throws only on caller bugs such as duplicate tracked keys.",
    "Unchanged and accepted in cycle 1: rollback of the audit row on PostgreSQL (risk row 291), and the 200-thread InMemory test, which detects a split lock only probabilistically (row 293)."
  ],
  "reachableRegressions": []
}
```

## Claims that held (with evidence)

All mutants and probes ran on an isolated copy (`scratchpad/cycle2-adversarial/sln`). The repository was used only for `dotnet test --no-build`.

**Baseline.**
- Repository: 251 unit and 77 integration tests pass with `--no-build`. The binaries are dated 22:31:44-48, after the last source edit at 22:30:31; no `.cs` file is newer.
- Scratch copy: 47 PersistenceModelTests and 32 DeviceRoleTests + InMemoryDeviceAccessStoreTests pass.
- Mutants: 44 valid, 37 killed, 7 survived (A3, B1, B2, B3, B8, B9, B10). One mutant, E2l, was written wrongly (it kept the original EnsureValid); its corrected version E2l2 is killed.

**A (Save discipline): holds for failures inside `Save`.**
- These mutants are killed:
  - A1: no Clear on database rejection (10 tests fail);
  - A2: no Clear on other failures (`Ef_failed_staging_leaves_nothing_for_a_later_save`);
  - A4: recheck runs before Clear (9 tests);
  - A5: no recheck (9 tests);
  - A6: an unexplained rejection is swallowed (`Ef_unexplained_database_rejection_is_rethrown_and_nothing_stays_tracked`);
  - A7: only concurrency exceptions are caught (6 tests).
- Precision notes:
  - A rejected pre-check stages nothing, but the rows it read stay tracked. Probe P4 shows `Unchanged DeviceRoleRecord, Unchanged DeviceRolePermissionRecord`, with `HasChanges()` false. So nothing is left to save.
  - The stale read accepted in risk row 292 still reproduces (probe P5). A second Update in the same scope, after an outside commit, is rejected with DEVICE_ROLE_VERSION_STALE although it is based on fresh data.
  - If the recheck query itself throws, that exception replaces the original one.

**B (recheck order and content): holds in code.**
- Each recheck calls the same `ThrowIf*` function as its pre-check (EfDeviceAccessStores.cs:82, :107, :212-213), so the order matches by construction.
- These mutants are killed: B4 (Upsert recheck without the role check), B5 (Upsert recheck order swapped), B6 (Add recheck does nothing), B7 (membership Remove recheck does nothing), B11 (Remove recheck without IN_USE), B12 (Remove recheck without the version check), B13 (Update recheck without the version check) and B14 (EF Remove checks IN_USE before STALE).
- The cases the claim lists are each covered by a test: double submit, update vs delete (through the FK and through the token), add vs same name, remove vs assignment, and membership insert vs insert or vs role delete.
- The uncovered paths are listed above (B1, B2, B3, B8, B9).

**C (no regression): holds.**
- C1 (forced `IsModified` removed, :208) is killed by `Ef_lost_race_maps_concurrency_conflicts_to_store_codes`.
- C2 (audit row in a second SaveChanges) is killed by 5 tests, including `AssertLostRace` (PersistenceModelTests.cs:1193-1205).
- Pre-checks run before `Save` in all five writes (inspection), and the audit-count assertions show that rejected pre-checks write nothing.
- Diff against the pre-phase-2 copy (`review-build-c3`): 0 lines removed from PersistenceModelTests.cs.

**D (overflow): holds.** DeviceRole.cs:79 computes `checked(Version + 1)` before any assignment. D1 (assign first) and D2 (`unchecked`) are both killed by `Device_role_update_at_max_version_leaves_state_unchanged` (DeviceRoleTests.cs:115-126).

**E, other parts: hold.**
- E1 (compare-and-set before the role check in EF Upsert) is killed by `Ef_membership_upsert_rejects_unknown_role` (:826-834).
- Removing EnsureValid from any of the 10 writes is killed (E2a-E2e for EF, E2g-E2k for InMemory). Moving it after the checks is killed too (E2f for EF, E2l2 for InMemory).
- E3 and E4 (InMemory Update writes, or checks names in, the organization of the caller's copy) are killed by `Role_update_keeps_the_stored_organization`.
- E5 and E6 (the EF equivalents) are killed.

**F, other parts: hold.**
- `publicContracts` (context-snippets.json:38) lists the two grant fields. Grep confirms that PermissionGrantsController is the only code that serializes grant records; MeController, ScopeAuthorization and DeviceCommandsController only read fields from them.
- Risk row 291: probe P6 on EF InMemory shows the loser's audit row persisted. Audits went from 4 to 6 (membership) and from 6 to 8 (role update), with one competing write each.
- Risk row 292: reproduced by probe P5. No false accept was found: the token's original value always guards the write.
- Risk row 293: accurate; the test is still probabilistic.
- D2 note (phase-02:238): mutant R1 recreates the cycle-1 Update mapping and fails `Ef_role_update_lost_to_identical_concurrent_update_reports_stale` and `Ef_role_update_lost_to_concurrent_delete_reports_not_found`. Mutant R2 recreates the cycle-1 membership mapping and fails the change-plus-role-delete test.

## Recommended actions

1. Low, before phase 3 relies on these codes: add three RaceInterceptor tests to PersistenceModelTests. Template: `scratchpad/cycle2-adversarial/sln/tests/Hms.Server.IntegrationTests/Cycle2ProbeTests.cs`.
   - Rename lost to a concurrent rename of another role to the same name, which must report DEVICE_ROLE_NAME_TAKEN (`rejectSave: true`).
   - Role Remove lost to a concurrent Remove, which must report DEVICE_ROLE_NOT_FOUND (`rejectSave: false`).
   - Membership Upsert and Remove with expected A, lost to a remove and re-add with A, which must report DEVICE_MEMBERSHIP_CHANGED (`rejectSave: false`).
2. Low: pin the Update order with one case in each store: a stale version plus a taken name must report DEVICE_ROLE_VERSION_STALE (probe P7).
3. Low, a decision for the controller. Either:
   - map the GET `/permissions/{userId}` rows to a DTO with `Guid? grantedBy` in phase 4 (and assert the shape in the phase-04 test at :186); or
   - narrow the publicContracts line to the POST bodies and record that GET keeps the `{value}` shape of the existing `userId`/`organizationId` fields.
4. Very low: update phase-02 risk row 288 and line 114 to describe the single-catch `Save`. Reword claim B's sentence about membership concurrency so it matches the order NOT_FOUND then CHANGED.

## Scratch artefacts (outside the repository)

All are under `C:/Users/toann/AppData/Local/Temp/claude/D--WORKSPACE-PROJECT-HMS-SERVER/daebfbef-46ca-4e9f-b506-cf6e26aef926/scratchpad/cycle2-adversarial/`:
- `run-mutants.js`, `mutants.js` and `mutants2.js` define and run the 45 mutant definitions.
- `results-*.txt` and `mutant-run.log` hold the per-mutant outcomes.
- `sln/tests/Hms.Server.IntegrationTests/Cycle2ProbeTests.cs` holds probes P1-P5; `Cycle2ProbeAuditTests.cs` holds P6 and P7.
- `json/` is the JSON shape probe.

Status: DONE_WITH_CONCERNS
Summary: The code behind claims A-D holds, and every enumerated race maps to the right code on the current build (37 of 44 mutants killed; all probes pass). Claim E is partly disproven, because three reachable database-rejection branches and the Update STALE-before-NAME_TAKEN order have no test. Claim F is partly disproven, because the plain-Guid grantedBy plan does not cover the GET snapshot.
Concerns/Blockers: Nothing blocks. PostgreSQL behaviour is inferred, not executed.
