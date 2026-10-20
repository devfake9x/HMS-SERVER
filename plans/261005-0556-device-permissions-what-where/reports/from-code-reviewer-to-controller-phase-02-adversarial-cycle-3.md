---
title: "Phase 2 adversarial validation, cycle 3: claims G-I (cycle-3 delta only)"
date: 2026-10-05
reviewer: code-reviewer (adversarial)
scope: 5 new tests (PersistenceModelTests.cs, InMemoryDeviceAccessStoreTests.cs); doc edits in phase-02/03/04 and harness/context-snippets.json publicContracts[2]
decision: PASS_WITH_RISK
---

# Phase 2 adversarial validation, cycle 3

`S/` = `C:/Users/toann/AppData/Local/Temp/claude/D--WORKSPACE-PROJECT-HMS-SERVER/daebfbef-46ca-4e9f-b506-cf6e26aef926/scratchpad/`. All builds, mutants and probes ran in `S/cycle3-adversarial/sln`. In the repository I only ran `dotnet test <project> --no-build`.

## Verdict (JSON-ready)

```json
{
  "decision": "PASS_WITH_RISK",
  "disprovenClaims": [
    {
      "claim": "H",
      "severity": "low",
      "evidence": "Phase 4 now contradicts itself on the snapshot shape. The edited lines phase-04:154 ('Dòng snapshot là DTO với mọi id là Guid phẳng, kể cả grantedBy') and :186 ('grantedBy là chuỗi Guid') ask for DTO rows. The unedited lines :63 ('field record do phase 2 thêm, không cần code riêng') and :125 ('PermissionGrantSnapshot(UserId, FeatureGrants, ResourceGrants, PolicyVersion) (DTO cũ, giữ kiểu) = PermissionGrantResolver.LatestFeatureGrants(permissions.FeatureGrants(target))') still return the record rows, which serialize ids as {value}. The cycle-2 report cited exactly these two lines as the cause of F. Scope also differs: publicContracts says the GET snapshot returns DTOs with every id as a plain Guid, but :154 covers only the rows, and :125 keeps the top-level `UserId UserId` (a strong id, so {value}).",
      "breakingScenario": "A phase-4 implementer who follows :125 together with :154 turns the existing regression test red. Permission_snapshot_returns_persisted_grants_for_in_scope_target reads the body with ReadFromJsonAsync<PermissionGrantSnapshot>() (ManagementApiTests.cs:392). In a scratch probe, deserializing a plain Guid into FeaturePermissionGrant throws JsonException. The plan does not say whether to change the snapshot's row types or the type the test deserializes into. Also, :186 asserts only grantedBy, so rows that flatten grantedBy alone still pass, and so does a top-level userId left as {value}. Phase-03:213 warns against exactly that shape ('Đừng chỉ làm phẳng riêng grantedBy')."
    },
    {
      "claim": "H",
      "severity": "low",
      "evidence": "context-snippets.json:38 (edited) says 'phase 3 (POST bodies) ... return DTOs with every id, grantedBy included, as a plain Guid'. Phase-03:213 (edited) now makes this conditional: 'Khi đổi body này, trả DTO ...' (when this body is changed). Phase-03:177 still says 'OK → 200 grant (shape giữ)' (shape kept). Phase 3 stamps GrantedBy on every row (phase-03:175). No phase-3 test pins the POST body shape: the planned grant tests check status and code only, and the existing POST tests (ManagementApiTests.cs:258, :346-371) check status only.",
      "breakingScenario": "Phase 3 follows its own file and keeps the record body. After phase 3, POST /permissions/features and /resource-scopes return \"grantedBy\":{\"value\":...}. After phase 4, GET rows return grantedBy as a plain Guid. The same field then has two shapes across endpoints, which is the cycle-2 F scenario reversed, and it contradicts publicContracts. Nothing breaks at runtime today: the frontend sends both POSTs as request<unknown> (core/api/adminApi.ts:206-207) and never reads the body."
    },
    {
      "claim": "H",
      "severity": "very-low",
      "evidence": "The stale-tracked-row risk row (phase-02:297) says the false DEVICE_MEMBERSHIP_CHANGED happens 'kể cả khi membership bị gỡ rồi gán lại đúng role cũ' (also after a remove and re-grant of the same role). It also says phase-3 handlers, which read untracked and write once per request, 'không gặp' (do not meet it). Scratch probes contradict both parts. First case: a stale tracked row, plus a remove and re-grant of the same role committed before the write. Upsert and Remove both succeed, because the tracking query returns the new row Y; the stale row X plays no part. Second case: one fresh context and one write, which is how a phase-3 handler works. A remove and re-grant committed between the pre-check and SaveChanges gives DEVICE_MEMBERSHIP_CHANGED with an inner DbUpdateConcurrencyException, and an immediate retry succeeds. This is the scenario of the new test Ef_membership_writes_lost_to_remove_and_regrant_of_the_same_role_report_changed.",
      "breakingScenario": "Only the documentation is wrong. The row puts a race outcome under the stale-tracking risk, so phase 3 is told that its handlers never see this CHANGED. Under concurrency they do, as a 409 that a retry clears. The behaviour is intended and safe (EfDeviceAccessStores.cs:240-241)."
    }
  ],
  "unverifiedClaims": [
    "PostgreSQL behaviour of the three new race paths is inferred, not run, because there is no database (by constraint). The paths: the rename gets 23505 after the competitor commits; the double delete and the remove and re-grant each get a 0-row DELETE or UPDATE after waiting for the lock. The tests stand in for these with the RaceInterceptor DbUpdateException or with EF InMemory's missing-row concurrency exception, which is the same exception class Npgsql raises.",
    "The controller's own cycle3-mut run left no log. Its six claims were re-established by my independent run (below), not by reading its output."
  ],
  "missingProof": [
    "No GET test enforces the 'every id' rule: phase-04:186 asserts only grantedBy's shape, and nothing planned checks a row's userId/organizationId or the snapshot's userId.",
    "No phase-3 test asserts the POST body shape (publicContracts[2]).",
    "Out of the delta, unchanged from cycle 2: staging inside the try (A3) and the PostgreSQL rollback of the loser's audit row."
  ],
  "reachableRegressions": []
}
```

## Claims that held (with evidence)

### G: each new test fails for its target mutant, passes today, and is not vacuous. Holds.

**Baseline.**
- Repository (`--no-build`): 252/252 unit and 81/81 integration tests pass.
  - I ran the suites 4 times; every run was green.
  - The binaries (23:04:46-48) are newer than the last source edit (23:01:45).
  - The 5 new tests are discovered and pass when run alone.
- Scratch copy: 0 warnings and 0 errors, the same test counts, and test DLL sizes identical to the repository.

**Mutation testing** (`S/cycle3-adversarial/run.js`, `mutants.js`).
- I ran 14 mutants, each applied alone, followed by a rebuild and both full suites.
- Six are exact replicas of the controller's mutants; eight are my own variants.
- Every mutant is killed, each by exactly one test: its targeted new test.
- Nothing else fails under any mutant, which also confirms that each gap was real before cycle 3.

| Mutant | Killed by |
|---|---|
| B1a (controller): name check gated on `cause is null` | `Ef_role_rename_lost_to_concurrent_same_name_rename_reports_name_taken` |
| B1b: Update recheck inlines only NOT_FOUND and STALE | same |
| B1c: Update recheck maps NAME_TAKEN to STALE | same |
| B10a (controller): recheck passes the caller's organization | same |
| B10b: recheck projection replaces the organization with the caller's | same |
| B2a (controller): Remove recheck `current ?? expectedVersion` | `Ef_role_remove_lost_to_concurrent_remove_reports_not_found` |
| B2b: Remove recheck skips a missing role | same |
| B2c: Remove recheck reports a missing role as STALE | same |
| B3a (controller): `ThrowIfChanged` without the token operand | `Ef_membership_writes_lost_to_remove_and_regrant_of_the_same_role_report_changed` |
| B3u: Upsert recheck alone ignores the token | same |
| B3r: membership Remove recheck alone ignores the token | same |
| B9 (controller): EF name check before version check | `Ef_role_update_reports_stale_before_name_taken` |
| B9b: EF pre-check alone checks the name first | same |
| B8 (controller): InMemory name check before version check | `Role_update_reports_stale_before_name_taken` |

**Non-vacuity.**
- B1b/B1c, B2b/B2c and B3u/B3r each change only one write's recheck lambda. So each kill proves that the test reaches that recheck after the competing commit, not a pre-check.
- B3u and B3r prove both halves of the two-part membership test independently. The Remove half runs only after the Upsert half passes, and B3r is killed there.
- B9b changes only the pre-check, so the EF order test pins the pre-check order. The recheck shares `ThrowIfUpdateRejected`, so it has the same order.
- Probes (`S/cycle3-adversarial/sln/tests/Hms.Server.IntegrationTests/Cycle3AdversarialProbes.cs`, 5/5 green):
  - The interceptor fires exactly once in each test.
  - Each code carries the provider error as `InnerException`: `DbUpdateException` for the rename, `DbUpdateConcurrencyException` for the double delete and for the remove and re-grant.
  - In the rename race, the competitor's rename is committed, and the loser's row stays `Operator`, at version 1, in the stored organization.
- The tests are deterministic:
  - no threads;
  - a new `InMemoryDatabaseRoot` per test;
  - a new InMemory store per test-class instance (`InMemoryDeviceAccessStoreTests.cs:12`).

### H: parts that hold

- **Phase-02 Save paragraph (:114-117)** matches `EfDeviceAccessStores.cs:268-293`:
  - one `try` wraps stage, the audit add and `SaveChanges`;
  - both catch blocks call `Clear()`;
  - on `DbUpdateException` (concurrency included), the store re-runs the same `ThrowIf*` checks;
  - a rejection no check explains is rethrown with `throw;`.
- **The membership sentence** ("token conflict → CHANGED after the role check") matches the code: `:210-214` and `:244`. The membership Remove recheck has no role check, so the sentence holds there trivially.
- **The cited precedent** `EfCommandAuditStore.cs:83` is the `Clear()` call.
- **Phase-02:204 (the `Ef_lost_race...` description)** matches `PersistenceModelTests.cs:683-757`. There are four stale-context cases, two per token. `AssertLostRace` proves one save, the audit row in the same batch, and an empty tracker.
- **Phase-02:277 (success criterion)** holds: each of the five store codes now has a lost-race test, and the audit rollback is left to PostgreSQL, which is the accepted deviation.
- **Phase-02:293 (`Clear()` risk row)** holds: `Clear()` runs in both catch blocks and nowhere else.
  - Info: the premise "Store khác đọc AsNoTracking" (other stores read with AsNoTracking) is loose. Several write paths read tracked rows: `EfManagementStores.cs:29,:68`, `EfClassificationStores.cs:18`, `EfLicenseStores.cs:99`, `EfAuthorizationEpochStore.cs:27` and `EfPermissionStore.cs:36`.
  - Each of these calls `SaveChanges` in the same method, so the conclusion still holds: no pending change is discarded.
- **Phase-03:213 facts** hold:
  - The 200 body is the grant record as-is (`PermissionGrantsController.cs:54`, `:72`).
  - Every id serializes as `{value}`. JSON probe with web defaults: `"userId":{"value":"1111..."}` and, once set, `"grantedBy":{"value":"2222..."}`.
  - `grantedBy` is `null` today: the handlers never set it (`PermissionContracts.cs:64`, `:75`).
  - The members that phase-04:154 says to delete exist (controller `:19`, `:22`, `:78-98`, `:119`, `:125`).
- **Frontend:**
  - Both copies of `adminApi.ts` type the grant ids as `string | { value: string }` (`core/api/adminApi.ts:31-50`).
  - The only consumer of the snapshot is `PermissionGrantEditor.vue:30`. It reads ids through `id()`, which accepts both shapes.
  - POST bodies are ignored, and `grantedBy` is neither typed nor read.
  - So "the frontend accepts both shapes" holds.
- **Contract §6** (`design-contract.md:169`, `:185`):
  - The DTO rule says every id in a new DTO is a plain Guid.
  - The GET row says "each row with grantedBy and grantedByMasterAdmin".
  - The edits agree with §6. The three conflicts above are between plan lines, not with §6.
- **context-snippets.json** parses, with 4 `publicContracts` entries.

### I: no regression; the test files only gained tests. Holds.

- **Only the two test files changed.** I compared all 119 `.cs`/`.csproj`/`.json` files under src/ and tests/ against both cycle-2 copies (`S/cycle2-review`, `S/cycle2-adversarial/sln`), ignoring CR at line ends; nothing else differs.
- **Both diffs are pure insertions:**
  - `PersistenceModelTests.cs`: 1335 → 1423 lines (4 tests);
  - `InMemoryDeviceAccessStoreTests.cs`: 390 → 406 lines (1 test).
  - Both files keep CRLF line endings throughout.
- **Production files predate cycle 3:** `EfDeviceAccessStores.cs` was last edited at 22:30:31 and `DeviceAccessContracts.cs` at 21:37:42. So the controller's mutation script never touched the repository.
- **No plan IDs or finding codes** appear in the new test names or comments.

## Recommended actions (documentation only, non-blocking)

1. **Low.** In phase-04, rewrite `:63` and `:125` to match `:154`.
   - Map the rows to DTOs with plain-Guid ids.
   - Decide whether the snapshot's top-level `userId` is flattened too (publicContracts says every id).
   - Say how `ManagementApiTests.cs:392` deserializes the new shape.
   - Extend `:186` to assert one row's `userId`/`organizationId` shape as well, so that flattening `grantedBy` alone fails.
2. **Low.** Make phase 3 and publicContracts agree. Either:
   - make phase-03:213 unconditional, drop "(shape giữ)" at `:177`, and add one integration assertion on a POST body's `grantedBy` shape; or
   - narrow publicContracts[2] to say that the POST bodies keep the record shape.
3. **Very low.** In phase-02:297, move the remove-and-re-grant case out of the stale-tracking row. State it separately: a lost race against a remove and re-grant of the same role reports `DEVICE_MEMBERSHIP_CHANGED`, the client can retry, phase-3 handlers can hit it, and it is intended (`EfDeviceAccessStores.cs:240-241`).

## Scratch artefacts (outside the repository)

All are under `S/cycle3-adversarial/`:
- `sln/`: the source copy.
- `mutants.js` and `run.js`: the mutant catalogue and runner.
- `mutant-run-1.log` and `results-*.txt`: the per-mutant outcomes.
- `sln/tests/Hms.Server.IntegrationTests/Cycle3AdversarialProbes.cs`: 5 probes.
- `jsonprobe/`: the JSON shape probe, which serializes records and deserializes a plain Guid into a record.

Status: DONE_WITH_CONCERNS
Summary: G and I hold. All 14 mutants, including replicas of the controller's six, are killed, each only by its targeted new test; the tests reach the intended recheck and pre-check paths; and cycle 3 only added tests, with 252/81 green. H is partly disproven by three documentation inconsistencies (two low, one very low); no code is affected.
Concerns/Blockers: Nothing blocks phase 2. Phases 3 and 4 inherit contradictory grant-shape instructions (phase-04:63/:125 vs :154/:186; publicContracts vs phase-03:177/:213), and no planned test enforces the "every id" rule.
