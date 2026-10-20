---
title: "Phase 2 adversarial validation: device roles, memberships, persistence"
date: 2026-10-05
reviewer: code-reviewer (adversarial)
scope: phase-02-device-roles-memberships-persistence.md, design-contract.md §3 §4 §8
decision: PASS_WITH_RISK
---

# Phase 2 adversarial validation

## Verdict (JSON-ready)

```json
{
  "decision": "PASS_WITH_RISK",
  "disprovenClaims": [
    {
      "claim": 2,
      "severity": "medium",
      "evidence": "EfDeviceAccessStores.cs:101-107: the non-concurrency DbUpdateException branch of EfDeviceRoleStore.Update re-checks only NameTaken, then rethrows. Captured Npgsql batch for the staged update (probe P6): INSERT device_access_audits; INSERT device_role_permissions; UPDATE device_roles ... WHERE \"Id\" = @p AND \"Version\" = @p. A failing permission INSERT aborts the batch before the guarded UPDATE runs, so EF raises DbUpdateException, not DbUpdateConcurrencyException.",
      "breakingScenario": "(a) Two concurrent identical updates of role R at Version 1 that both add permission P (double-submit or client retry) both pass the pre-checks. The winner commits. The loser's INSERT (R,P) hits 23505 on PK_device_role_permissions, and the store rethrows a raw DbUpdateException. Contract §4 expects DEVICE_ROLE_VERSION_STALE. (b) An update of R adding P races Remove(R) and the delete commits first: 23503 FK, raw DbUpdateException; expected DEVICE_ROLE_NOT_FOUND. Probes P2a and P2b (pg=true) reproduce the store side. The explicit transaction keeps the data consistent, but once phase 3 maps only InvalidOperationException codes, the loser gets HTTP 500 instead of 409/404. The same race on EF InMemory escapes as a raw ArgumentException (duplicate key; P2a pg=false)."
    },
    {
      "claim": 2,
      "severity": "very-low",
      "evidence": "EfDeviceAccessStores.cs:243-249: the membership DbUpdateConcurrencyException branch always reports DEVICE_MEMBERSHIP_CHANGED without reloading, while the pre-check order is DEVICE_ROLE_NOT_FOUND first (:190-194).",
      "breakingScenario": "Probe P3: Upsert A->B (expected A) while another scope commits A->C and deletes role B. The store reports DEVICE_MEMBERSHIP_CHANGED; the pre-checks now report DEVICE_ROLE_NOT_FOUND. InMemory checks and writes under one lock, so it reports NOT_FOUND."
    },
    {
      "claim": 1,
      "severity": "low",
      "evidence": "EfDeviceAccessStores.cs:69 and :265-266 read the row with a tracking query. If the scoped DbContext already tracks that row, EF returns the tracked instance and does not refresh its values, so the Version/RoleId pre-check runs on stale data.",
      "breakingScenario": "Probe P4: one context adds a role (row stays tracked). Another scope commits v2. The first context reads fresh (AsNoTracking) v2, updates the domain role to v3 and calls Update: EF reports DEVICE_ROLE_VERSION_STALE; InMemory accepts the same sequence. This fails safe: the tokens prevent a false accept. It is unreachable while every request does one write per scope."
    },
    {
      "claim": 5,
      "severity": "low",
      "evidence": "PermissionGrantsController.cs:35-40, :54, :72, :121-126 serialize FeaturePermissionGrant/ResourceScopeGrant directly. JSON probe with web defaults: {..., \"policyVersion\":7, \"grantedBy\":null, \"grantedByMasterAdmin\":false}.",
      "breakingScenario": "GET /api/v1/permissions/{userId}, POST /api/v1/permissions/features and POST /api/v1/permissions/resource-scopes now emit two extra fields. This is additive (the TS client ignores them) but is not declared in publicContracts. Once phase 3 stamps the grantor, these endpoints expose grantor user ids."
    },
    {
      "claim": 7,
      "severity": "very-low",
      "evidence": "DeviceRole.cs:80-82 assign Name/Description/Permissions before checked(Version + 1) at :83.",
      "breakingScenario": "Probe P1: DeviceRole.Rehydrate(..., \"Before\", \"desc\", [device.view], int.MaxValue).Update(\"After\", null, [sys.reboot]) throws OverflowException and leaves Name=After, Description=null, Permissions=sys.reboot, Version=int.MaxValue. Reaching it needs 2^31-1 updates or a corrupt row."
    },
    {
      "claim": 8,
      "severity": "low",
      "evidence": "InMemoryDeviceAccessStoreTests.cs:107-160 is probabilistic. Mutant M1b splits the check and the write of InMemory Upsert into two critical sections (Monitor.Exit/Enter between them); the test caught it in 2 of 10 runs (4 of 5 with a Thread.Yield in the gap). Mutants M5, M6, M6e, M7, M7b, M8 survive (see missingProof).",
      "breakingScenario": "A future refactor that moves the write out of the checking lock (a TOCTOU regression) passes CI about 80% of the time."
    },
    {
      "claim": 2,
      "severity": "low",
      "note": "Outside the literal 'rejected by the database' scope. Requires a caller bug (an audit id reused within one scope).",
      "evidence": "EfDeviceAccessStores.cs:37-52 (and the Update/Upsert staging): when db.DeviceAccessAudits.Add throws after the role is staged, the store does not call ChangeTracker.Clear().",
      "breakingScenario": "Probe P5: Add(roleOne, audit X), then Add(roleTwo, audit X with the same AuditId) throws an identity-conflict InvalidOperationException, leaving 'Added DeviceRoleRecord, Added DeviceRolePermissionRecord' staged. The next SaveChanges on the same scope (EfPermissionStore.GrantFeature) persisted role Two with no audit row (roles=[One,Two], audits=1)."
    }
  ],
  "unverifiedClaims": [
    "PostgreSQL runtime behaviour was not executed (no DB, per constraints). This covers migration apply, unique/FK enforcement, and which SQLSTATE the losing batch gets. The medium finding is inferred from the captured Npgsql batch order plus PostgreSQL's abort-on-first-error semantics; the store's mapping of that exception is reproduced on EF InMemory with an interceptor.",
    "Claim 3 is verified at SQL level (captured WHERE clauses, one explicit transaction per guarded batch) but not executed against PostgreSQL.",
    "Full-suite counts 248/64 were not re-run. The phase filters were run with --no-build on current binaries (21:44, after the last source edit at 21:43): 29 unit (DeviceRoleTests + InMemoryDeviceAccessStoreTests) and 34 PersistenceModelTests, all green."
  ],
  "missingProof": [
    "EF 'unknown role checked before the CAS': mutant M5 (CAS before RoleExists, EfDeviceAccessStores.cs:190-194) survives all 34 PersistenceModelTests. Ef_membership_upsert_rejects_unknown_role (PersistenceModelTests.cs:812-825) uses expected null with no row, so the order cannot be observed. Only InMemory proves it (InMemoryDeviceAccessStoreTests.cs:173-174). The code itself is correct by inspection.",
    "DbUpdateException reload-and-map branches have no tests: M7 (role Update branch reduced to a bare rethrow) and M7b (membership Save branch reduced to a bare rethrow) both survive. These branches can be tested today without PostgreSQL: a SaveChangesInterceptor commits a competing write, then throws DbUpdateException (probe P2 pattern). This contradicts the plan's note that they are testable only after extracting a pure function.",
    "EnsureValid on write methods other than Add: removing it from InMemory Upsert (M6) or EF Upsert (M6e) survives. No EF test feeds an unknown Change at all.",
    "InMemory Update keeping the stored organization: M8 (writes role.OrganizationId) survives. Only the EF path is tested.",
    "Atomicity of the InMemory check+write is shown only probabilistically (claim 8 above). The single lock block at DeviceAccessContracts.cs:178-188 is correct by inspection.",
    "That the audit row rolls back with a lost race on PostgreSQL rests on EF opening an explicit transaction. The SQL probe confirms one TransactionStarting per guarded write + audit SaveChanges; not executed against a DB."
  ],
  "reachableRegressions": [
    "No runtime path in this phase calls the new stores (grep: only DI, the stores and tests reference them; no controller or handler was modified after phase 1, per timestamps).",
    "Wire: three permission endpoints gain two additive JSON fields (see claim 5).",
    "Deployment (acknowledged by the plan): the EF model now selects permission_grants.GrantedBy/GrantedByMasterAdmin, so this build against a DB without AddPermissionGrantGrantor fails every grant read. Migrations apply in order, and AddDeviceRolesAndMemberships (14:39:49) precedes AddPermissionGrantGrantor (14:40:47), so getting the columns also runs the DELETE of device-action grants that the current code enforces until phases 3-4. Phase 2 cannot be deployed alone to a DB with data. This is consistent with 'release the whole plan at once'.",
    "Latent for phase 3: the medium claim-2 finding becomes an HTTP 500 on concurrent or double-submitted role edits as soon as handlers and endpoints exist."
  ]
}
```

## Claims that held (with evidence)

**Claim 3 (concurrency tokens make an atomic CAS on PostgreSQL): holds.** No DB was opened. The probe uses interceptors that suppress connection open and transaction start, and capture the SQL text. Captured Npgsql SQL:
- Membership upsert with identical values: `UPDATE device_memberships SET "RoleId" = @p10 WHERE "Id" = @p11 AND "RoleId" = @p12`. Without the forced `IsModified` (EfDeviceAccessStores.cs:215), EF sends no UPDATE at all, so the flag is necessary.
- Membership remove: `DELETE FROM device_memberships WHERE "Id" = @p10 AND "RoleId" = @p11`.
- Role update: `UPDATE device_roles SET ... WHERE "Id" = @p14 AND "Version" = @p15`. Role remove: `DELETE FROM device_roles WHERE "Id" = @p12 AND "Version" = @p13`.
- EF begins one explicit transaction per guarded write + audit SaveChanges (1, 0 for the single-statement case, 1, 1), so the audit row is rolled back when EF sees 0 rows affected.
- Mutants M2 (drop the `Version` token), M3 (drop the forced `IsModified`) and M4 (drop the `RoleId` token) each fail `Ef_lost_race_maps_concurrency_conflicts_to_store_codes`. `Model_snapshot_has_no_pending_changes` stays green without the tokens, which confirms the tokens produce no DDL.

**Claim 4 (migrations and snapshot): holds.**
- `20261005143949_AddDeviceRolesAndMemberships.cs` contains exactly the §8 objects:
  - 4 CreateTable with PKs `Id`, `Id`, `(RoleId, Permission)` and `AuditId`;
  - FKs: roles→organizations Restrict, memberships→device_roles Restrict, memberships→devices Restrict, permissions→roles Cascade;
  - 6 indexes, of which (UserId, DeviceId) and (OrganizationId, NormalizedName) are unique;
  - after all indexes, the UPDATE GREATEST statement at :140, then the DELETE at :143.
- Designer 1 and Designer 2 differ only in the two grantor properties. Designer 2's body is identical to `ApplicationDbContextModelSnapshot.cs`.
- `AddPermissionGrantGrantor` contains only the 2 `AddColumn` operations (nullable uuid, and boolean NOT NULL with default false).

**Claim 6 (DI): holds.** DependencyInjection.cs:78-89 registers exactly what the claim states. Grep finds no other registration of the new interfaces. The DI test resolves the same singleton across two scopes.

**Claim 5 (additive), apart from the wire note above.**
- Diff against the pre-phase-2 copy (`scratchpad/review-build-c3`, files dated 2026-09-29/30) shows purely additive edits in PermissionContracts.cs, EfPermissionStore.cs, PersistenceEntities.cs, ApplicationDbContext.cs (+62/-0) and DependencyInjection.cs.
- The PersistenceModelTests.cs diff is additions only, so no existing test body changed.
- No consumer uses record equality or deconstruction on the grants; ScopeAuthorization, FeaturePermissionAuthorizationHandler and MeController only project fields.
- Legacy rows read back as null/false: the column is nullable, the migration default is false, and `Grantor(x)` maps null to null.

**Claim 7 (immutability), apart from the overflow.**
- `Permissions` is a `FrozenSet` (Ordinal), and `Rehydrate` copies via `ToFrozenSet`.
- InMemory stores and returns role copies (DeviceAccessContracts.cs:92, 101, 111, 128).
- Precision note: memberships and audit entries are returned by reference (DeviceAccessContracts.cs:151-173, 209-213). They are immutable positional records, so no mutable state leaks.

**Claim 1, other listed invariants: hold by inspection in both stores.**
- Names are compared on `NormalizedName`, and Update uses the stored org.
- Remove order is NOT_FOUND → VERSION_STALE → IN_USE.
- The membership CAS treats null as "no membership".
- Parity note outside the listed invariants: `ListByOrganization` orders with `StringComparer.Ordinal` in memory, but with the DB collation in EF.

## Recommended actions

1. **Medium, fix before phase 3 consumes the store.** In `EfDeviceRoleStore.Update`, merge the two catch blocks into one full re-check after `Clear()`:

   ```csharp
   catch (DbUpdateException error) // includes DbUpdateConcurrencyException
   {
       db.ChangeTracker.Clear();
       var stored = db.DeviceRoles.AsNoTracking().Where(x => x.Id == role.Id.Value)
           .Select(x => (int?)x.Version).SingleOrDefault();
       if (stored is null) throw new InvalidOperationException("DEVICE_ROLE_NOT_FOUND", error);
       if (stored != role.Version - 1) throw new InvalidOperationException("DEVICE_ROLE_VERSION_STALE", error);
       if (NameTaken(organizationId, role.NormalizedName, role.Id.Value))
           throw new InvalidOperationException("DEVICE_ROLE_NAME_TAKEN", error);
       throw;
   }
   ```

   Then add tests that use an interceptor (commit a competing write in `SavingChanges`, then throw `DbUpdateException`) for the duplicate-permission and deleted-role races. Use the same pattern to cover the role `Add`/`Remove` branches and the membership `Save` branch (M7, M7b).
2. Add the missing proofs: an EF version of "unknown role + mismatched expected → DEVICE_ROLE_NOT_FOUND"; unknown-`Change` tests for Update/Remove/Upsert on both stores; InMemory org immutability. Either strengthen the concurrency test (start all workers on a `Barrier`, run more iterations) or label it a smoke test.
3. Low:
   - Compute `checked(Version + 1)` before the assignments in `DeviceRole.Update`.
   - In the EF writes, stage the audit record first, or wrap staging plus `SaveChanges` in a handler that clears the tracker on any exception.
   - In the membership concurrency branch, check that the assigned role exists before reporting CHANGED.
   - Declare the additive JSON fields in publicContracts, or project the grant DTOs when phase 4 reshapes the snapshot.

## Probe artefacts (scratch, outside the repo; repo bin/obj untouched)

All under `C:/Users/toann/AppData/Local/Temp/claude/D--WORKSPACE-PROJECT-HMS-SERVER/daebfbef-46ca-4e9f-b506-cf6e26aef926/scratchpad/p2adv-cr/`:
- `probe/Program.cs` contains P1 to P6. It references the built `src/Hms.Server.Infrastructure/bin/Debug/net10.0` DLLs.
- `sql/` captures the Npgsql SQL and counts explicit transactions.
- `json/` shows the wire shape of the grant records.
- `run-mutant.sh` with `mut/*.from|*.to` runs the mutants on an isolated copy in `sln/`.

Status: DONE_WITH_CONCERNS
Summary: Claims 3, 4 and 6 hold; 1, 2, 5, 7 and 8 are partly disproven. The main defect: on PostgreSQL, a concurrent or double-submitted role update that adds a permission, or races a delete, escapes as a raw DbUpdateException instead of DEVICE_ROLE_VERSION_STALE or DEVICE_ROLE_NOT_FOUND.
Concerns/Blockers: Fix the medium mapping in EfDeviceRoleStore.Update and test the DbUpdateException branches before phase 3 maps store codes. PostgreSQL behaviour is inferred from captured SQL, not executed.
