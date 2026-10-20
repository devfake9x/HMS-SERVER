# Phase 2 review: device roles, memberships, persistence

Reviewer: code-reviewer. Date: 2026-10-05. Phase file: `phase-02-device-roles-memberships-persistence.md`. Code paths are relative to `D:/WORKSPACE/PROJECT/HMS-SERVER`.

## 1. Decision

**PASS_WITH_RISK, 8/10.**

Every acceptance criterion in `context-snippets.json` is met, the build is clean and both suites are green. The CAS design is sound:
- `Version` and `RoleId` are concurrency tokens in the model only, with no DDL.
- The `IsModified` fix is correct.
- InMemory and EF behave the same.

**One medium defect.** After a PostgreSQL constraint error on the permission rows, `EfDeviceRoleStore.Update` throws a raw `DbUpdateException` instead of `DEVICE_ROLE_VERSION_STALE` or `DEVICE_ROLE_NOT_FOUND` (M-1). That contradicts the plan's invariant table and the store's own class comment. The defect went unnoticed because no reload-and-map branch is tested (M-2).

**Fix size and effect.** The fix is about 10 lines. In a scratch copy it turns the new tests from red to green and leaves all existing tests green. Fix it, and add the interceptor tests, before phase 3 starts mapping these codes.

**What I verified (the repo was not modified or built):**
- **Tests:**
  - `dotnet test tests/Hms.Server.UnitTests/Hms.Server.UnitTests.csproj --no-build`: 248/248.
  - `dotnet test tests/Hms.Server.IntegrationTests/Hms.Server.IntegrationTests.csproj --no-build`: 64/64.
  - The binaries (21:44) are newer than every phase-2 source; the newest source is from 21:43:52.
- **Build:** I copied `src/`, `tests/` and `Hms.Server.slnx` (without bin/obj) into the scratchpad and built the copy: 0 warnings, 0 errors.
- **Pending model changes:** `Model_snapshot_has_no_pending_changes` passes. It calls `HasPendingModelChanges()` on the Npgsql model, the same check the CLI makes. I did not run `dotnet ef`, because it would build the repo.
- **Frontend gate:** not re-run, because `npm run build` writes to `frontend/dist`. No file under `frontend/` has changed since 2026-10-01, so phase 2 cannot have affected it.
- **Scope:** the files changed on 2026-10-05 from 21:31 onward are exactly the phase-2 file list. Phase-1 files stop at 20:56.
- **Probes:** all ran in scratch projects that use copies of the built DLLs:
  - `phase2-probe`:
    - injects races into EF;
    - captures the SQL Npgsql would send, without opening a connection;
    - checks when EF opens a transaction;
    - shows the JSON shape of the grant records.
  - `phase2-build`: source copy used for the fix and the mutation runs.

  Paths are listed under Metrics.

## 2. Acceptance coverage

| # | Criterion | Code | Tests | Status |
|---|---|---|---|---|
| 1 | Build has 0 errors; tests green | n/a | 248 unit + 64 integration green (`--no-build`); scratch build 0 warnings / 0 errors | Met |
| 2 | No pending model changes; migration creates 4 tables with §8 indexes/FKs, then UPDATE before DELETE; grantor migration adds 2 columns | `20261005143949_AddDeviceRolesAndMemberships.cs:14-135` (4 CreateTable, 6 CreateIndex), `:140` UPDATE with GREATEST, `:143` DELETE; `20261005144047_AddPermissionGrantGrantor.cs:14-25` | `PersistenceModelTests.cs:74-86`, `:827-846`, `:848-861` | Met (see note 1) |
| 3 | Grantor fields saved and read back; legacy rows read null/false; positional call sites compile | `PermissionContracts.cs:12-25`; `EfPermissionStore.cs:66-67,82-83,103-118`; positional constructions `PermissionContracts.cs:64,75` | `PersistenceModelTests.cs:863-893` | Met |
| 4 | Unique (UserId, DeviceId); unique (OrganizationId, NormalizedName); FKs per §8; model-only concurrency tokens | `ApplicationDbContext.cs:156-212`; snapshot `:300-302`, `:403-405` | `PersistenceModelTests.cs:346-433` | Met |
| 5 | `Operator` then `operator` gives NAME_TAKEN and writes nothing; another org is fine | `DeviceAccessContracts.cs:105-114,218-227`; `EfDeviceAccessStores.cs:31-35,74-75,144-145` | `InMemoryDeviceAccessStoreTests.cs:203-227`; `PersistenceModelTests.cs:529-560` | Met |
| 6 | Membership: null to A to B, mismatch, absent remove, remove writes audit, regrant restores (EF and InMemory) | `DeviceAccessContracts.cs:175-207`; `EfDeviceAccessStores.cs:187-235` | `InMemoryDeviceAccessStoreTests.cs:15-105`; `PersistenceModelTests.cs:609-681,759-779` | Met |
| 7 | 200 parallel upserts give exactly one winner | `DeviceAccessContracts.cs:175-189` (check and write under one lock) | `InMemoryDeviceAccessStoreTests.cs:107-160` | Met |
| 8 | Stale update; remove order; remove deletes role, permissions and writes 1 audit in one save | `EfDeviceAccessStores.cs:66-142`; `DeviceAccessContracts.cs:116-149` | `InMemoryDeviceAccessStoreTests.cs:229-250,267-293`; `PersistenceModelTests.cs:472-527,562-607` | Met |
| 9 | EF lost race maps to the codes; audit in the same SaveChanges | `EfDeviceAccessStores.cs:95-100,129-133,243-249,213-215` | `PersistenceModelTests.cs:683-757` | Met, with the accepted deviation (§4). M-1 covers a race that is not a token race. |
| 10 | DI in both branches | `DependencyInjection.cs:78-89` | `PersistenceModelTests.cs:895-927` | Met |
| 11 | No existing test changed except `Model_has_management_tables_only` | n/a | See note 2 | Met (no git) |

Note 1: I reviewed the migration file.
- The token columns are a plain `integer` and a plain `uuid`, with no DDL.
- The audit values are `text`.
- `Down` drops the tables in FK order.

Note 2: the scout map (`scout-persistence-surface-map.md:46-58`) lists the line spans of the 13 tests that existed before this phase. Each still has the same length, except `Model_has_management_tables_only`, which gained 4 lines (`:68-71`).

## 3. Findings

### Critical
None.

### High
None.

### Medium

**M-1. After a PostgreSQL constraint error on the permission rows, `EfDeviceRoleStore.Update` throws a raw `DbUpdateException` instead of a store code.**

Location: `src/Hms.Server.Infrastructure/Persistence/EfDeviceAccessStores.cs:101-107`. The class comment at `:9-12` claims the store "reports the code its pre-checks would report now", which is not true for this branch.

**Why it happens:**
1. Probe P8 captured the Npgsql batch for an update that adds a permission:
   ```
   INSERT INTO device_access_audits ...;
   INSERT INTO device_role_permissions ...;
   UPDATE device_roles SET ... WHERE "Id" = @p14 AND "Version" = @p15;
   ```
   No statement has `RETURNING`.
2. EF throws `DbUpdateConcurrencyException` only from its rows-affected check. That check runs only after every statement in the batch has succeeded.
3. If the permission INSERT fails first, PostgreSQL aborts the batch before the version predicate counts. The error is 23505 for a duplicate key, or 23503 for a missing role.
4. EF wraps that error as a plain `DbUpdateException`.
5. The catch at `:101` re-checks only `NameTaken` and otherwise runs `throw;`.

**Failure scenario:**
1. Admins A and B save role R (version 1, `{device.view}`) at the same time, and both add `device.edit_basics`.
2. A loads R tracked (`:69`). B commits before A's `SaveChanges` (`:93`). If B's transaction is still open, A's INSERT waits on B's row lock and fails once B commits.
3. A's INSERT of `(R, device.edit_basics)` fails with 23505, and A gets a raw `DbUpdateException` instead of `DEVICE_ROLE_VERSION_STALE`.
4. If B had deleted R instead, the INSERT fails with 23503. A should have received `DEVICE_ROLE_NOT_FOUND`.

**Impact:**
- Phase 3 maps only `InvalidOperationException.Message`, so this surfaces as an unhandled exception (HTTP 500) instead of 409 or 404.
- Data stays consistent: the batch runs inside a transaction (probe P12), and the catch clears the change tracker.

**Evidence:**
- Two scratch tests fail on the current code with `Actual: DbUpdateException`:
  - `Role_update_lost_to_concurrent_permission_add_reports_stale`;
  - `Role_update_lost_to_concurrent_delete_reports_not_found`.
- On EF InMemory, the same race without injection throws a raw `ArgumentException` "An item with the same key has already been added" (probe P1).

**Fix.** Replace both catches at `:95-107` with a single reload that repeats the pre-checks in their original order. Because `DbUpdateConcurrencyException` derives from `DbUpdateException`, the existing token test (`PersistenceModelTests.cs:732-741`) still gets `DEVICE_ROLE_VERSION_STALE`. In the scratch copy, with the fix:
- probes: 8/8;
- integration: 72/72;
- unit: 248/248;
- 0 warnings.

```csharp
catch (DbUpdateException error)
{
    // PostgreSQL can report a lost race through the permission rows
    // (unique or foreign key) before the Version predicate runs, so
    // every failure re-runs the pre-checks in their order.
    db.ChangeTracker.Clear();
    var storedVersion = db.DeviceRoles.AsNoTracking()
        .Where(x => x.Id == role.Id.Value)
        .Select(x => (int?)x.Version)
        .SingleOrDefault();
    if (storedVersion is null)
        throw new InvalidOperationException("DEVICE_ROLE_NOT_FOUND", error);
    if (storedVersion != role.Version - 1)
        throw new InvalidOperationException("DEVICE_ROLE_VERSION_STALE", error);
    if (NameTaken(organizationId, role.NormalizedName, role.Id.Value))
        throw new InvalidOperationException("DEVICE_ROLE_NAME_TAKEN", error);
    throw;
}
```

**M-2. No shipped test runs any of the reload-and-map branches, which is how M-1 went unnoticed (gap 1).**

The branches are in `EfDeviceAccessStores.cs`:

| Lines | Store method | Mutation in the scratch copy | Shipped tests (64 integration + 248 unit) | Interceptor probes |
|---|---|---|---|---|
| `:57-63` | `Add` | M5: mapping removed | survives | killed |
| `:101-107` | `Update` | (the M-1 defect) | not detected | fail on current code |
| `:134-141` | `Remove` | M6: mapping removed | survives | killed |
| `:250-260` | membership `Save` | M2: mapping removed | survives | killed |

These branches are also the "PostgreSQL backstop" that the plan's risk table relies on (`phase-02-device-roles-memberships-persistence.md:279`).

**Fix:** adopt the `SaveChangesInterceptor` tests described in §4 (gap 1). A ready-to-copy file with 8 tests is listed under Metrics. The core of the interceptor:

```csharp
private sealed class Race(Action competingWrite, bool raise = true) : SaveChangesInterceptor
{
    private bool _fired;
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (_fired) return result;
        _fired = true;
        competingWrite();   // commits through a second context on the same InMemoryDatabaseRoot
        if (raise) throw new DbUpdateException("constraint violation reported by the provider");
        return result;
    }
}
```

### Low

**L-1. No test checks that InMemory `Update` ignores the organization on the caller's copy (gap 2).**
- Location: `src/Hms.Server.Application/Authorization/DeviceAccessContracts.cs:125,128-129`.
- Evidence: mutation M1 switches both uses to `role.OrganizationId`, and every test still passes.
- If this regresses, a copy that claims another organization moves the role in dev and API tests. The name check would also run in the wrong organization, so InMemory and EF would silently disagree.
- On the EF side the write is covered (`PersistenceModelTests.cs:514-526`), but the organization used by the name check (`EfDeviceAccessStores.cs:73-74`) is not.
- **Fix:** add an InMemory test.
  1. Call `Update` with `DeviceRole.Rehydrate(role.Id.Value, otherOrg, ..., version)`.
  2. Assert that `Find` still returns the stored organization.
  3. Assert that a role with the same name in `otherOrg` does not block the rename.
  4. Assert that a role with the same name in the stored organization does block it.

  Add the same name-check assertion to the EF test.

**L-2. EF `Update` of a missing role is untested on both paths (gap 3).**
- Pre-check path: `EfDeviceAccessStores.cs:69-70`. Mutation M3 returns the wrong code there, and every shipped test still passes.
- Token path (the role is deleted after the load): `:98-99`. No shipped test runs it.
- **Fix:** add the two scratch tests:
  - `Role_update_of_missing_role_reports_not_found`;
  - `Role_rename_lost_to_concurrent_delete_reports_not_found_through_the_token`.

**L-3. The grant endpoints' JSON gained two fields, and `publicContracts` does not list them.**

`PermissionGrantsController.cs:45,54,59,72-75,121-126` returns the records from `PermissionContracts.cs:12-25` unchanged. Probe P15 shows what that means on the wire.

Today the change is additive and harmless:
- `"grantedBy":null,"grantedByMasterAdmin":false` now appears in:
  - `POST /api/v1/permissions/features`;
  - `POST /api/v1/permissions/resource-scopes`;
  - `GET /api/v1/permissions/{userId}`.
- The frontend DTOs (`frontend/src/core/api/adminApi.ts:30-41`) ignore the extra fields.
- No test pins the response shape.

Once phase 3 fills in the grantor, the field serializes as `"grantedBy":{"value":"<guid>"}`. That contradicts design-contract §6, which requires plain Guid ids in DTOs (`design-contract.md:169,185`).

**Fix:**
- Record the additive wire change in `context-snippets.json` under `publicContracts`.
- In the HTTP phase, map `grantedBy` to `Guid?` in an explicit snapshot DTO instead of serializing `UserId?`.

## 4. Verdicts on the deviation, the 3 test gaps and the self-review note

### Deviation: "a lost race on EF writes no audit" is not provable on EF InMemory

**Acceptable.** The no-audit guarantee rests on three links. The two that belong to this code now have evidence:

1. **Each store write sends the guarded row and its audit row in one `SaveChanges`.**
   - The tests assert it (`PersistenceModelTests.cs:594-602,946-958`).
   - The captured SQL (P8 to P10) shows the audit INSERT in the same command as the guarded UPDATE or DELETE.
2. **EF opens a transaction before it sends such a batch.**
   - P12, a 3-statement batch: `transaction requested: 1, commands sent without transaction: 0`.
   - P13, a 2-statement batch: same result.
   - P14, a 1-statement control: no transaction.
   - Nothing in `src/` changes `AutoTransactionBehavior`. The only explicit transaction is the unrelated one at `EfProvisioningBundleStore.cs:62`.
3. **PostgreSQL rolls back an uncommitted transaction.** This is the only link left unproven, and it is not worth faking.

The rewording of criterion 9 and the comment at `PersistenceModelTests.cs:942-945` are accurate.

Two caveats:
- EF InMemory is genuinely non-transactional. No future test may assert "no audit row" for a lost race on that provider.
- Optional: a one-line test can keep link 2 from regressing:
  ```csharp
  Assert.Equal(AutoTransactionBehavior.WhenNeeded, new ApplicationDbContext(npgsqlOptions).Database.AutoTransactionBehavior);
  ```

**The `IsModified` fix also matters for atomicity.**
- Without it, a re-grant that changes nothing sends only the audit INSERT (P11). That is a 1-statement batch with no compare-and-set and no transaction.
- With it, EF sends `UPDATE device_memberships SET "RoleId" = @p10 WHERE "Id" = @p11 AND "RoleId" = @p12` (P10).
- Removing the line makes `Ef_lost_race_maps_concurrency_conflicts_to_store_codes` fail (mutation M4 is killed), so the fix is covered.
- In production, `GrantedAt` normally differs between calls, so the fix only closes the identical-timestamp corner case. It is still correct.

### Gap 1: the reload branches are untested

**Confirmed (M-2). Adopt the `SaveChangesInterceptor` approach.**

How it works:
1. The interceptor runs the competing write through a second context inside `SavingChanges`. At that point the store has done its tracked load and pre-checks, which is the real PostgreSQL timing.
2. It then throws `DbUpdateException`.
3. The store's real reload-and-map code runs against state written by a real competing store call.

Why this is not faking PostgreSQL:
- The only thing substituted is the exception type, which is the one thing EF InMemory cannot produce.
- There is no emulation of unique or FK semantics.
- No pure function has to be extracted, so plan step D2 (`phase-02...md:237`) is satisfied without faking the database.

What it catches: mutations M2, M3, M5 and M6, and it exposed M-1 immediately.

Why not let EF InMemory collide natively: a native collision throws `ArgumentException` (P1), which never reaches the `DbUpdateException` branches.

Practical rules:
- fire the interceptor once;
- throw a plain `DbUpdateException`, not the concurrency subtype;
- assert that the change tracker is empty afterwards.

### Gap 2: InMemory `Update` keeping the stored organization

**Confirmed by M1. Low.** See L-1.

### Gap 3: EF `Update` of a missing role

**Confirmed by M3. Low.** The NOT_FOUND result on the token path is also untested. See L-2.

### Self-review note

**Confirmed for `Update`, refuted for `Remove`.**

`Update`: confirmed. See M-1 (SQL from P8, probes P2 and P3, red-to-green with the fix).

`Remove`: refuted.
- Its batch (P9) is: `INSERT device_access_audits; DELETE FROM device_role_permissions WHERE ... (once per permission); DELETE FROM device_roles WHERE "Id" = ... AND "Version" = ...`.
- A DELETE that matches 0 rows raises no PostgreSQL error.
- The membership FK (Restrict) can fire only when the role row actually matches, which means the version is current. In that case `DEVICE_ROLE_IN_USE` is the right code. Probe P7 and the scratch test `Role_remove_lost_to_concurrent_assignment_reports_in_use` pass.
- So a stale or deleted role always surfaces as `DbUpdateConcurrencyException`, which maps to STALE or NOT_FOUND (`:129-133`).

Optional: give `Remove` the same unified reload for symmetry. It is not needed.

I checked `Add` and membership `Save` the same way. Both map correctly (P4 to P6).

## 5. Regression and contract status

### Blast radius

**`FeaturePermissionGrant` and `ResourceScopeGrant` callers.** Each reads only the fields it needs:
- `ScopeAuthorization.cs:28-30,51-53`
- `FeaturePermissionAuthorizationHandler.cs:46-48`
- `MeController.cs:25-33`
- `DeviceCommandsController.cs:58-66`

None of them relies on record equality, `Deconstruct` or the number of fields. The build passes, and the positional constructions are unchanged (`PermissionContracts.cs:64,75`).

**`EfPermissionStore`.** Its paths are unchanged apart from the two mapped fields.
- `GrantDeviceAction` and `DeviceActionGrants` are untouched (`EfPermissionStore.cs:88-101,120-124`).
- `GrantDeviceActionHandler` is still registered (`DependencyInjection.cs:92`).

**DI** (`DependencyInjection.cs:78-89`):
- The new registrations sit after the `IPermissionStore` block.
- Development forwards both interfaces to one singleton.
- Database mode registers scoped EF stores.
- No existing registration was touched.

No regression found.

### Contracts

- Public types and the database schema are additive, as `publicContracts` states.
- The DELETE of `device-action` rows was accepted by the owner (V3) and only reaches a database when the whole plan is released.
- The UPDATE that aligns the policy counter would do nothing if the counter row were missing. That cannot happen: row 1 is always inserted by `20260929032719_AddPolicyVersionCounter.cs:27-30`.
- `Down` documents that the deleted rows are not restored.
- One additive wire change is not disclosed: L-3.

### Patterns (repo conventions)

- **InMemory store:** `_gate` plus `lock` on every member, with each check and its write under the same lock (`DeviceAccessContracts.cs:84-231`). This matches `LicenseContracts.cs:180-222`. The store keeps and returns copies of roles.
- **EF reads** use `AsNoTracking` (`EfDeviceAccessStores.cs:17,23,164,169,175,181,268`).
- **Errors** are reported as `InvalidOperationException(code)`.
- **Failed saves:** after a failed `SaveChanges` the stores call `ChangeTracker.Clear()` and reload, as `EfCommandAuditStore.cs:79-92` does. M-1 is the only exception.

### Plan follow-ups (for the lead; I did not edit the plan)

- The phase-02 frontmatter still says `status: pending` (`:4`), and the Todo and Success Criteria checkboxes are unchecked. Update them after M-1 and M-2 are fixed.
- Step D2 was skipped. The interceptor tests now cover it.

## Metrics

- **Tests:**
  - Repo binaries (`--no-build`): 248 unit and 64 integration, all green.
  - Scratch copy with the fix and the 8 probes: 248 unit and 72 integration, all green.
- **Build:** scratch copy, 0 warnings and 0 errors (nullable enabled in every project).
- **Mutation runs** (scratch copy, original store code):

  | Mutation | Shipped tests | Probe file |
  |---|---|---|
  | M1 | survives | not targeted (InMemory) |
  | M2 | survives | killed |
  | M3 | survives | killed |
  | M4 | killed | not targeted |
  | M5 | survives | killed |
  | M6 | survives | killed |

- **Scratch artifacts** (outside the repo; the scratchpad may be cleaned, and the snippets above are the essential parts):
  - Interceptor test file (8 tests): `C:/Users/toann/AppData/Local/Temp/claude/D--WORKSPACE-PROJECT-HMS-SERVER/daebfbef-46ca-4e9f-b506-cf6e26aef926/scratchpad/phase2-build/tests/Hms.Server.IntegrationTests/ReviewRaceProbeTests.cs`
  - Probes P1 to P15: `C:/Users/toann/AppData/Local/Temp/claude/D--WORKSPACE-PROJECT-HMS-SERVER/daebfbef-46ca-4e9f-b506-cf6e26aef926/scratchpad/phase2-probe/Program.cs`

Status: DONE_WITH_CONCERNS
Summary: Phase 2 meets all 11 acceptance criteria; build and tests are green. One medium defect: `EfDeviceRoleStore.Update` throws a raw `DbUpdateException` instead of STALE or NOT_FOUND when PostgreSQL rejects the permission rows of a concurrently updated or deleted role. It went unnoticed because no reload branch is tested. A verified 10-line fix and 8 interceptor tests are provided.
Concerns/Blockers: Fix M-1 and add the interceptor tests (M-2) before phase 3 maps these store codes. L-3 (undisclosed additive JSON fields on the grant endpoints, and the `{value}` shape once the grantor is filled in) needs a decision in the HTTP phase.
