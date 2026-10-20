# Phase 2 review, cycle 2: device roles, memberships, persistence

Reviewer: code-reviewer. Date: 2026-10-05. Code paths are relative to `D:/WORKSPACE/PROJECT/HMS-SERVER`. `S/` means `C:/Users/toann/AppData/Local/Temp/claude/D--WORKSPACE-PROJECT-HMS-SERVER/daebfbef-46ca-4e9f-b506-cf6e26aef926/scratchpad/`.

## 1. Decision

**PASS, 9/10 (advisory).**

- All five cycle-1 findings are closed: M-1, M-2, L-1 and L-2 are fixed, and L-3 is accepted as documentation.
- The adversarial items are closed the same way.
- The restructure introduced no regression.

I ran 30 mutants against a scratch copy, and the shipped suite killed 29. The survivor is a test gap, not a defect (N-1, Low). The only other new item is plan text that no longer matches the code (N-2, Low, docs only). Neither blocks phase 3.

**What I verified.** The repo was not modified. All builds and mutations ran in `S/cycle2-review/`.

- **Scope.** Exactly 5 files changed since cycle 1:
  - `EfDeviceAccessStores.cs`
  - `DeviceRole.cs`
  - `DeviceRoleTests.cs`
  - `InMemoryDeviceAccessStoreTests.cs`
  - `PersistenceModelTests.cs`

  I compared against the adversarial reviewer's cycle-1 copy (`S/p2adv-cr/sln`, all mutants restored), and its line numbers match the cycle-1 report. Every other file under `src/` and `tests/` is identical, ignoring CR at line ends. That includes the migrations, the snapshot, `ApplicationDbContext`, the entities, DI and the InMemory store.
- **Build.** I copied `src/`, `tests/` and `Hms.Server.slnx` without bin/obj and built the copy: 0 warnings, 0 errors.
- **Tests in the repo** (`--no-build`): 251/251 unit and 77/77 integration. The binaries (22:31:44–48) are newer than the last source edit (22:30:31).
- **Tests in the scratch copy:** the same counts. Over 14 unit runs, one unrelated pre-existing test failed twice (§6).
- **Pending model changes:** `Model_snapshot_has_no_pending_changes` passes, and no model or migration file changed in cycle 2.
- **Mutation testing:** 30 mutants, 29 killed (§4).
- **Probes:** 4 scratch-only tests (§4, §5). All pass on the current code.

## 2. Cycle-1 findings: status

| Finding | Status | Evidence |
|---|---|---|
| **M-1**: `Update` threw a raw `DbUpdateException` after a unique or FK rejection | **Fixed** | On any `DbUpdateException`, the store re-reads the role (`EfDeviceAccessStores.cs:76-83`) and re-runs the same `ThrowIfUpdateRejected` as the pre-check (`:113-120`): NOT_FOUND, then STALE, then NAME_TAKEN. Two tests cover the bug: `Ef_role_update_lost_to_identical_concurrent_update_reports_stale` (`PersistenceModelTests.cs:929`) and `Ef_role_update_lost_to_concurrent_delete_reports_not_found` (`:946`). Mutant m01 restores the exact cycle-1 catch logic, and both tests kill it. |
| **M-2**: no test ran any reload-and-map branch | **Fixed**, with one gap left (N-1) | `RaceInterceptor` (`:1300-1317`) and 9 race tests (`:923-1061`). Every rejection branch of all 5 writes now has a killing test: m01–m05, m07–m09 and m23–m28. Only the token-conflict operand of `ThrowIfChanged` (`EfDeviceAccessStores.cs:244`) is still untested (m06, see N-1). |
| **L-1**: InMemory `Update` ignoring the copy's organization was untested | **Fixed** | `Role_update_keeps_the_stored_organization` (`InMemoryDeviceAccessStoreTests.cs:230`) kills m20, m20b and m20c: both uses switched, only the name check, or only the write. On the EF side, `Ef_device_role_update_checks_names_in_the_stored_organization` (`PersistenceModelTests.cs:851`) kills m11. |
| **L-2**: EF `Update` of a missing role was untested on both paths | **Fixed** | Pre-check path: `Ef_device_role_update_of_missing_role_reports_not_found` (`:838`). Token path: `Ef_role_rename_lost_to_concurrent_delete_reports_not_found_through_the_token` (`:961`). m28 (missing role reported as STALE) is killed by 3 tests, and m23 by 2. |
| **L-3**: two JSON fields were not listed in `publicContracts` | **Accepted** (documentation) | `reports/harness/context-snippets.json` `publicContracts[2]` lists `grantedBy` and `grantedByMasterAdmin` on the 3 endpoints. `phase-03-access-evaluation-and-delegation.md:213` requires phase 3 to return `grantedBy` as a plain `Guid?` DTO. No code change is needed now: phase 2 never stamps a grantor, so the field is always `null`. |

**Adversarial cycle-1 items**

| Item | Status | Evidence |
|---|---|---|
| Claim 2, medium (same as M-1) | Fixed | See M-1. |
| Claim 2, very low: after a token conflict, membership reported CHANGED before checking the role | Fixed | The recheck now checks the role first, then CHANGED (`:210-214`). Test at `:1026`. m04 (order swapped) and m05 (role check dropped) are killed. |
| Claim 2, low: role left staged when audit staging failed | Fixed | Staging, audit and save all run in one `try` (`:276-281`). The general catch clears the tracker (`:288-292`). Test at `:902`. m09 is killed. |
| Claim 7, very low: overflow left the role half-updated | Fixed | `DeviceRole.cs:79` computes `checked(Version + 1)` before any assignment (`:81-84`). Test at `DeviceRoleTests.cs:116`. m21 is killed. |
| Missing proof: EF checks the role before the compare-and-set | Fixed | `PersistenceModelTests.cs:826-834`. m10 is killed. |
| Missing proof: `EnsureValid` on every write | Fixed | EF: `:876`, which kills m12–m15. InMemory: `InMemoryDeviceAccessStoreTests.cs:348`, which kills m16–m19. Both tests also assert that nothing was written. |
| Claim 1: stale tracked rows give a false STALE | Accepted | Phase-02 risk row `:292`. See §6.2; I confirmed the membership equivalent and that neither ever accepts a stale write. |
| Claim 8: the 200-thread test is probabilistic | Accepted | Risk row `:293`. See §6.3. |
| PostgreSQL rollback of the audit row is not executed | Accepted | Risk row `:291`. See §6.1. |

## 3. Mandatory checks

**(a) Is each cycle-1 finding resolved?** Yes; see §2.

**(b) Did the restructure introduce a regression?** No. Evidence per item:

- **Every pre-check still runs before staging:**
  - `Add`: `:29-30` before `:32`.
  - `Update`: `:53-56` before `:58`.
  - `Remove`: `:88-91` before `:93`.
  - `Upsert`: `:180-184` before `:186`.
  - membership `Remove`: `:219-224` before `:226`.
- **Nothing is written on a rejected pre-check.** Probe `Probe_every_pre_check_rejection_leaves_no_pending_change` runs 10 pre-check rejections on one context, covering every code from all 5 writes. After each one, `ChangeTracker.HasChanges()` is false. A final `SaveChanges()` returns 0, and a second context sees an unchanged audit count, role version and membership.
- **The `IsModified` token fix is in place** at `:208`. m22 (line removed) is killed by `Ef_lost_race_maps_concurrency_conflicts_to_store_codes`.
- **Lost-race token behaviour holds.** That test is green. It also kills m24 (Remove recheck reading the tracked row), m25 (membership Remove recheck removed) and m27 (Update recheck removed).
- **No exception is swallowed.** Both catch blocks end in `throw;`, or they throw a store code with the `DbUpdateException` as `InnerException` (`:282-292`). m08 replaces the rethrow with `return` and is killed by `:1049`.
- **`ChangeTracker.Clear()` runs only on failure**, at `:284` and `:290`, both inside catch blocks. m07 and m09 (each Clear removed) are killed.
- **Behaviour changes against cycle 1.** All are intended, plus one that cannot be reached:
  1. `Update`: a unique or FK rejection now maps to NOT_FOUND or STALE before NAME_TAKEN (the M-1 fix).
  2. `Upsert`: a token conflict whose target role was deleted now reports `DEVICE_ROLE_NOT_FOUND`, the same order as the pre-check.
  3. A failure while staging now clears the tracker.
  4. Role `Update` and `Remove`: a token conflict that the recheck cannot explain is now rethrown; before, it was always mapped to STALE or NOT_FOUND. **This cannot happen through the store API:**
     - A token conflict needs a changed role row.
     - Every role write bumps `Version`.
     - Permission rows only disappear through `Update` (which bumps `Version`) or `Remove` (which deletes the role).
     - So the recheck always finds the role missing or at a different version.
     - If it ever did happen, rethrowing an unexplained rejection is the rule the code documents (`:265-267`).

**(c) No public contract change beyond `context-snippets.json`.**
- No public signature changed.
- The only renamed symbol is internal: `DeviceAccessAuditRecords` became `DeviceAccessWrites`. The repo has no `InternalsVisibleTo`.
- No controller, DTO, migration or model file changed.
- The L-3 wire fields are now listed (`publicContracts[2]`).

**(d) Repo patterns and comment accuracy.**
- `DeviceAccessWrites.Save` replaces five catch blocks that differed from each other. That difference is exactly how M-1 happened, so the helper removes real complexity.
- It is internal and used only in this file.
- It follows the "clear the tracker, reload, map" precedent of `EfCommandAuditStore.cs:79-92`.
- The new comments at `:111-112`, `:206-207`, `:240-241` and `:262-267`, and those in the tests at `:923-927` and `:1300-1302`, match the code and the probes.
- Plan text drifted (N-2).

**(e) Build and type errors.** None: 0 warnings and 0 errors on a clean scratch build with nullable enabled.

## 4. Would the race tests catch their targets?

**Yes, for every branch except one operand (N-1).** Each mutant below was applied alone to the scratch copy, which was then rebuilt and run against the full affected suite.

| Mutant | Result: tests that kill it |
|---|---|
| m01: Update recheck restored to the cycle-1 logic (concurrency → STALE or NOT_FOUND, otherwise only NAME_TAKEN) | `..._identical_concurrent_update_reports_stale`, `Ef_role_update_lost_to_concurrent_delete_reports_not_found` |
| m02: Add recheck removed | `Ef_role_add_lost_to_concurrent_same_name_reports_name_taken` |
| m03: Remove recheck without IN_USE | `Ef_role_remove_lost_to_concurrent_assignment_reports_in_use` |
| m04: Upsert recheck order swapped | `Ef_membership_change_lost_to_concurrent_change_and_role_delete_reports_role_not_found` |
| m05: Upsert recheck without the role check | the test above, and `Ef_membership_insert_lost_to_role_delete_reports_role_not_found` |
| **m06: `ThrowIfChanged` without `error is DbUpdateConcurrencyException \|\|`** | **Survives (77/77).** Killed by 2 scratch probes (N-1). |
| m07: no `Clear()` in the `DbUpdateException` catch | 10 tests, including every caller of `AssertRejected` and `AssertLostRace` |
| m08: unexplained rejection swallowed | `Ef_unexplained_database_rejection_is_rethrown_and_nothing_stays_tracked` |
| m09: no `Clear()` in the general catch | `Ef_failed_staging_leaves_nothing_for_a_later_save` |
| m10: compare-and-set before the role check (EF) | `Ef_membership_upsert_rejects_unknown_role` |
| m11: EF Update name check in the copy's organization | `Ef_device_role_update_checks_names_in_the_stored_organization` |
| m12–m15: EF `EnsureValid` removed from Update, Remove, Upsert or membership Remove | `Ef_device_access_writes_reject_unknown_audit_change_without_writing` (each mutant) |
| m16–m19: the same for InMemory | `Every_write_rejects_an_unknown_audit_change_without_writing` (each mutant) |
| m20, m20b, m20c: InMemory uses the copy's organization (both uses, name check only, write only) | `Role_update_keeps_the_stored_organization` (each mutant) |
| m21: assign before the overflow check | `Device_role_update_at_max_version_leaves_state_unchanged` |
| m22: `IsModified` removed | `Ef_lost_race_maps_concurrency_conflicts_to_store_codes` |
| m23: Update recheck reads the staged record | `..._through_the_token`, `Ef_role_update_lost_to_concurrent_delete_reports_not_found` |
| m24: Remove recheck reads the tracked record | `Ef_lost_race_maps_concurrency_conflicts_to_store_codes` |
| m25: membership Remove recheck removed | `Ef_lost_race_maps_concurrency_conflicts_to_store_codes` |
| m26: `ThrowIfChanged` without `current != expectedRoleId` | `Ef_membership_insert_lost_to_concurrent_insert_reports_changed` |
| m27: Update recheck removed | 4 tests |
| m28: missing role reported as STALE | 3 tests |

**How faithfully they model PostgreSQL.** The interceptor commits a real competing write through a second context after the store's tracked load and pre-checks. The only thing it substitutes is the class of the provider error.

- In each `rejectSave` test, the state the recheck reads equals what PostgreSQL would show: the competitor has committed, and the victim's batch was rolled back.
  - The SQLSTATE each test stands for: 23505 on the permission PK, 23503 on the role FK, 23505 on unique `(OrganizationId, NormalizedName)`, 23503 Restrict from memberships, 23505 on unique `(UserId, DeviceId)`, and 23503 on the device FK.
  - Each matches the batch order captured in cycle 1 (P8–P10).
- The two `rejectSave: false` tests use EF InMemory's own token check, which behaves like a 0-row UPDATE on PostgreSQL.

A precision note on the controller summary, which says "each race test throws `DbUpdateException`":
- 6 tests throw after a competing write;
- 2 let the token check reject the save;
- 1 (`:1049`) throws with no competing write.

The coverage is unaffected.

## 5. New findings

### Critical / High / Medium

None.

### Low

**N-1. The token-conflict operand of `ThrowIfChanged` is untested.**

Location: `src/Hms.Server.Infrastructure/Persistence/EfDeviceAccessStores.cs:240-245`, the operand `error is DbUpdateConcurrencyException ||` at `:244`.

- **Failure scenario:**
  1. Admin A replaces or removes U's membership on device D (expected role R). The store has read row X.
  2. Before A saves, admin B removes the membership and grants R again, which creates a new row Y.
  3. On PostgreSQL, A's `UPDATE` or `DELETE ... WHERE "Id" = X AND "RoleId" = R` matches 0 rows, so EF throws `DbUpdateConcurrencyException`.
  4. The recheck now finds R, which equals the expected role.
  5. Today the operand maps this to `DEVICE_MEMBERSHIP_CHANGED`, which is correct.
  6. If a later edit drops the operand (for example "only report CHANGED when the role differs"), all 77 tests stay green. The store then rethrows the raw `DbUpdateConcurrencyException`. Phase 3 maps only contract codes (`phase-03...md:157`), so this would surface as HTTP 500 instead of 409.
- **Evidence:**
  - m06 survives the shipped suite.
  - Scratch probes `Probe_upsert_lost_to_remove_and_regrant_of_same_role_reports_changed` and `Probe_remove_lost_to_remove_and_regrant_of_same_role_reports_changed` pass on the current code. Under m06 they fail with `Actual: typeof(Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)`.
- **Fix.** Add this test with the existing helpers, plus the same test for `Remove` (`memberships.Remove(user, device, viewer.Id, ...)`):

```csharp
[Fact]
public void Ef_membership_change_lost_to_remove_and_regrant_of_the_same_role_reports_changed()
{
    // The row the check read is gone; a new row holds the expected role again.
    var contexts = InMemoryContexts(nameof(Ef_membership_change_lost_to_remove_and_regrant_of_the_same_role_reports_changed));
    var viewer = SeedRole(contexts, OrganizationId.New(), "Viewer", "device.view");
    var editor = SeedRole(contexts, OrganizationId.New(), "Editor", "device.edit_basics");
    var user = NewUser();
    var device = DeviceId.New();
    Assign(contexts, user, device, viewer.Id);
    var race = new RaceInterceptor(() =>
    {
        using (var other = contexts())
            new EfDeviceMembershipStore(other).Remove(user, device, viewer.Id, AccessAudit(DeviceAccessChanges.MembershipRemoved));
        Assign(contexts, user, device, viewer.Id);
    }, rejectSave: false);
    using var db = contexts(race);

    AssertRejected(db, "DEVICE_MEMBERSHIP_CHANGED", () => new EfDeviceMembershipStore(db).Upsert(
        Member(user, device, editor), viewer.Id, AccessAudit(DeviceAccessChanges.MembershipChanged)));
}
```

**N-2. Phase-02 plan text no longer matches the restructured code (docs only).**

Location: `plans/261005-0556-device-permissions-what-where/phase-02-device-roles-memberships-persistence.md`. It is read by later phases and by whoever ticks the boxes.

- **`:114`** says the store catches `DbUpdateConcurrencyException` before `DbUpdateException`.
  - The code now has one `DbUpdateException` catch that re-runs the checks in order (`EfDeviceAccessStores.cs:282-287`).
  - It also has a general catch for staging failures (`:288-292`).
- **`:288`** (risk row) says `Clear()` is called only after a failed `SaveChanges`.
  - It is now also called when staging fails, before `SaveChanges` (`:288-292`).
  - That also discards any unrelated pending change in the shared scoped context. No caller has one: phase 3 does one store write per handler call and reads through `AsNoTracking`.
  - Unlike `EfLicenseStores.cs:114-122`, which detaches only its own candidate, this clears everything. That is acceptable, but the row should say so.
- **`:272`** (success criterion) and **`:199`** (test description) still promise "no audit" on an EF lost race.
  - The accepted deviation (risk row `:291`, harness criterion 9) is that EF InMemory cannot show this; rollback relies on PostgreSQL.
- **`:292`** mentions only a false STALE. The membership store has the same false `DEVICE_MEMBERSHIP_CHANGED` (§6.2).

**Fix:** reword these four places. No code change is needed.

### Informational

- The store still lets EF's own `InvalidOperationException`s through, carrying EF's message. An example is an identity conflict when an audit id is reused in one scope; cycle-1 code did the same. That is safe only because phase 3 maps the five contract codes and rethrows anything else (`phase-03...md:157`). Keep that rule when the handlers are written.

## 6. Accepted limitations: are they reasonable?

### 6.1 EF InMemory has no transaction, so audit rollback relies on PostgreSQL

**Reasonable.**
- The EF stores are registered only when there is a connection string, which means Npgsql: `DependencyInjection.cs:78-81` with `:145-150`.
- The development fallback uses EF InMemory for the `DbContext` (`:154`), but registers `InMemoryDeviceAccessStore` for these interfaces (`:83-88`). So the EF device-access stores never run on EF InMemory outside tests.
- Two links of the chain are proven:
  - Cycle 1 (P12–P14) showed that EF opens a transaction for every multi-statement save.
  - `AssertLostRace` (`PersistenceModelTests.cs:1193-1205`) proves that the rejected write and its audit row are in the same save.
- The only unproven link is PostgreSQL's own rollback.

Condition: reword the success criterion `:272` (N-2) before ticking it.

### 6.2 Stale tracked rows can give a false STALE, never a false accept

**Reasonable, and I confirmed both halves.**
- **Roles:**
  - The `Version` token uses the tracked original value, and versions only increase.
  - So a save is accepted only if the database still equals the tracked value, which means the tracked value was current.
  - A stale-but-passing pre-check is caught by the token (`Ef_lost_race...`, cases 3 and 4).
- **Memberships:** probe `Probe_stale_tracked_membership_gives_false_changed_but_never_a_false_accept`:
  - With a fresh expectation, the store reports a false `DEVICE_MEMBERSHIP_CHANGED`.
  - With the stale expectation, the pre-check passes, the `RoleId` token rejects the write, and the database is unchanged.
- **Reach:**
  - Phase-3 handlers read through `Find` (`AsNoTracking`) and make one store write per call (`phase-03...md:154-157`).
  - So the first tracking query in a scope is the store's own, and the limitation cannot be reached in the planned flows.

### 6.3 The 200-thread InMemory test is probabilistic

**Reasonable.**
- Each check and its write sit in one `lock` block (`DeviceAccessContracts.cs:178-188`, `:194-206`), and the code is unchanged since cycle 1.
- Cycle 1 measured that a split-lock mutant is detected in about 2 of 10 runs.
- A deterministic test would need a hook in production code, which is not worth it.
- Optional: starting the workers on a `Barrier` would raise the detection rate cheaply.

### Environment note (not phase 2)

`SignalRConnectionBacklogTests.Emits_runtime_metrics_for_drop_retry_and_resynchronization` failed in 2 of 14 unit runs on the scratch copy: `Expected: 1, Actual: 2` at `:124`.
- **Cause:** a process-wide `MeterListener` also counts `MarkRetry` measurements from tests running in parallel.
- **Not phase 2:** the file dates from 2026-09-30 and is unchanged.
- A red unit run on that test alone should not be blamed on this phase.

## 7. Regression and contract status

- **Regressions:** none. All 251 unit and 77 integration tests are green. The behaviour changes are the intended fixes listed in §3(b).
- **Public contracts:** unchanged in cycle 2. The L-3 additive wire fields are now declared.
- **Database schema:** unchanged in cycle 2, with no pending model changes.
- **Security posture:** unchanged.
  - The stores still do no authorization; the handlers in phase 3 own it.
  - Failures leave nothing staged, so an un-audited write can no longer ride a later save.
  - PostgreSQL rejections map to the codes in design-contract §4.

## 8. Plan follow-ups (for the lead; I did not edit the plan)

- Phase-02 frontmatter still says `status: pending` (`:4`), and the Todo and Success Criteria boxes are unchecked. Update them after N-2 is reworded.
- Optional before phase 3: add the N-1 tests (two small tests, the snippet above).

## Metrics

- **Tests:** repo `--no-build`: 251/251 unit and 77/77 integration. Scratch copy: the same, plus 4 probes, all green.
- **Build:** clean scratch build, 0 warnings and 0 errors.
- **Mutation testing:** 30 mutants, 29 killed by the shipped suite. The survivor is m06 (N-1).
- **Scratch artifacts** (outside the repo):
  - `S/cycle2-review/`: source copy; `tests/Hms.Server.IntegrationTests/ReviewCycle2Probes.cs` holds the 4 probes.
  - `S/cycle2-mut/`: `mutations.js` (the catalogue), `apply.js`, `run.sh`.

Status: DONE
Summary: Phase 2 cycle 2 passes (9/10). M-1, M-2, L-1, L-2 and the adversarial items are fixed with tests that kill 29 of 30 mutants, L-3 is documented, and the restructure introduced no regression; repo tests are 251/251 unit and 77/77 integration, and the clean scratch build has 0 warnings.
Concerns/Blockers: Two Low, non-blocking items. N-1: the token-conflict operand of `ThrowIfChanged` (`EfDeviceAccessStores.cs:244`) has no test; a two-test fix is provided. N-2: four places in the phase-02 plan text no longer match the restructured code.
