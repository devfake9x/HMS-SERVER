# Phase 3 review, cycle 2: verification of the fix round

Reviewer: code-reviewer. Date: 2026-10-06. Code paths are relative to `D:/WORKSPACE/PROJECT/HMS-SERVER`.

- `S/` = `C:/Users/toann/AppData/Local/Temp/claude/D--WORKSPACE-PROJECT-HMS-SERVER/daebfbef-46ca-4e9f-b506-cf6e26aef926/scratchpad/phase03-review-c2/`, a pristine copy of `Hms.Server.slnx`, `src` and `tests`, used for builds and mutations.
- `P/` = `.../scratchpad/phase03-review-c2-probe/`, a second copy that adds my probes.

The repo was not modified except for this report and my agent-memory notes. Before I wrote this, the repo `src` and `tests` were still byte-identical to `S/`.

## 1. Verdict

**PASS WITH CONCERNS, 7/10.**

**Cycle-1 findings.** Every cycle-1 finding is fixed as decided and pinned by a test, with one exception: L-4 is partial, because 2 test comments still say "phase-2".

**Mutation.**
- All 12 cycle-1 survivors are now killed.
- The cycle-1 hang mutant now fails fast, and cycle-1 m22 is killed.

**New-code correctness.** The new code mostly holds:
- The PostgreSQL lock path is sound by reading.
- The subtree rule, the wildcard lift, scope-first in the command handler and fail-closed are all correct.
- Rule A on role updates is sound.
- 21 of 30 mutants on the new logic are killed.

**The new delegation-chain logic has three verified gaps, and Q16 opens one RT4 bypass:**

| Finding | What it is |
|---|---|
| **M-1** | A demoted middle appointer ends the chain. This deviates from the Q18 text and was found here; it is confirmed over HTTP. |
| **M-2** | The 32-link cap truncates the walk instead of failing closed. |
| **M-3** | The "latest grantor" rule can be captured by a redundant re-grant. This is a design gap in Q18 itself and needs an owner decision. |
| **M-4** | Q16's lift revives allows over scope that RT4(b) never checked, because it read the masked feature set. This is a regression enabled by this round. |

M-2, M-3 and M-4 were first noted by the parallel cycle-2 adversarial review (shared reviewer notes). I verified each independently with probes through the real handlers.

**Gate claim.** The controller's "0 warnings" is inaccurate. A clean build of the exact repo tree reports 2 `xUnit1031` warnings.

**Recommendation.** Keep phase 3 open until:
- M-1, M-2 and M-4 are fixed with tests;
- the owner decides M-3, together with the chain definition behind M-1;
- the 5 pinning tests of L-1 are added.

| Severity | Count | Items |
|---|---|---|
| Critical | 0 | |
| High | 0 | |
| Medium | 4 | M-1 demoted link; M-2 depth cap fails open; M-3 capture by redundant re-grant (owner decision); M-4 mask, widen, lift (Q16 regression) |
| Low | 3 | L-1: 5 unpinned rules (probe tests ready); L-2: build warnings; L-3: L-4 remnant |
| Info | 4 | I-1 to I-4 |

## 2. Cycle-1 findings: verification

| Cycle-1 item | Decision | Status | Code | Pinned by (evidence) |
|---|---|---|---|---|
| C-1 / D1: lookalike `maſter-admin` minted a master-admin | no-decision fix | **FIXED** | `RoleAuthorizationPolicy.cs:11-17` (NFC, then upper-invariant, ordinal); `IdentityUserManagement.cs:60-62` also compares through `roles.NormalizeKey`; used at `:78` (create) and `:92` (assign) | `PermissionTests.Reserved_role_check_catches_names_identity_folds_onto_master_admin`; HTTP `User_admin_cannot_assign_master_admin_through_a_lookalike_role_name` (403, not in role) and `User_admin_cannot_create_a_lookalike_master_admin_role`; `Identity_role_management_rejects_a_lookalike_master_admin_name_for_non_master`. Mutants R1 and R3 killed; R2 equivalent (I-2) |
| H-1: resource allow pushed the target into a carved-out sub-org | Q15, grant **and** deny | **FIXED** | `PermissionContracts.cs:192-195`; the check runs before the deny early return, against the chain-cut scope | `PermissionTests.Non_master_cannot_grant_or_deny_an_organization_whose_subtree_leaves_their_scope` and `..._is_in_their_scope`; HTTP `Resource_grant_rejected_when_its_subtree_leaves_the_grantor_scope_over_http` (all persisted grants). Q1 and Q2 killed |
| M-1: a deny `*` could never be lifted | Q16 | **FIXED** (but see the new M-4) | `PermissionContracts.cs:133-134`: only a master writes the `*` key. The resolver already strips an allow `*` and takes the latest row per key, so a master allow `*` cancels the deny `*` and grants nothing of its own | `PermissionTests.Master_admin_lifts_a_wildcard_deny_with_a_wildcard_allow`, `Wildcard_allow_grant_is_rejected_for_non_master`; HTTP `Wildcard_allow_lifts_a_block_only_for_master_admin_over_http`. W1 and W2 killed |
| D2 / D3 / L-1: check-then-write races | Q17 | **FIXED** (in-memory proven; PostgreSQL by reading, §3.2) | `PermissionContracts.cs:95-99`, `:116-127`, `:165-176`; `EfPermissionStore.cs:123-138`, `:173` | `GrantSerializationTests` (2 tests). L1 killed. L2 survived and L3 survived (test gaps: L-1, I-1) |
| D4: `permissions.manage` × membership laundering | Q18 | **FIXED** | `DelegationChain.cs`; `DeviceAccessHandlers.cs:205-208`, `:226` | T, appointed by A, can neither assign A (`DelegationChainTests.Appointed_admin_cannot_assign_or_remove_the_membership_of_its_appointer`) nor hand out more than A holds on D (`Appointed_admin_hands_out_no_device_permission_its_appointer_lacks_there`). A3 and B5 killed |
| D5: command endpoint told a foreign device from a missing one | no-decision fix | **FIXED** | `SendDeviceCommandHandler.cs:46-53` | `DeviceCommandAuthorizationTests.Out_of_scope_device_reads_like_a_missing_device_before_catalog_checks`; HTTP `Out_of_scope_device_command_reads_like_a_missing_device`. S1 killed |
| Fail-open on an unreadable current role | no-decision fix | **FIXED** | `DeviceAccessHandlers.cs:223-228`, `:287-292`, `:330-333` | `DeviceAccessHandlerTests.Unreadable_current_role_counts_as_stronger_than_a_non_master` (replace and remove, no write). F1 and F2 killed |
| U1: `AdvanceManyPostgres` lock order | no-decision fix | **FIXED (by reading)** | `EfAuthorizationEpochStore.cs:106` `... ORDER BY device_id ON CONFLICT ...`: valid PostgreSQL. Rows are inserted and conflict-locked in the sorted order of the subplan | PostgreSQL only; not runnable here (accepted) |
| L-4: plan and decision IDs in comments | no-decision fix | **PARTIAL** | `src` is clean (the remaining `§` hits cite the external MQTT spec, which is fine). `tests/Hms.Server.UnitTests/DeviceAccessTestFixture.cs:16` and `:133` still say "phase-2" | L-3 below |
| L-5 / F1: hang instead of failure | no-decision fix | **FIXED** | `CommandBoundaryTests.cs:438-439` `WaitAsync(10 s)` | X35 (cycle-1 M35 retargeted to `request.Action`) is now killed in 18.2 s, with `Concurrent_same_correlation_claims_transport_once` failing instead of hanging |
| L-6 / M1: regression test that proved nothing | no-decision fix | **FIXED** | Renamed `In_scope_device_command_reaches_the_catalog_lookup`, with its own user. `Device_command_requires_catalog_metadata_at_the_public_boundary` also gets its own user | With scope-first, the renamed test now pins that an in-scope caller passes the scope gate. The shared `ResourceScopedUserId`, which carries a persisted Child deny, would have made both tests order-dependent |
| I-1 / m22: epoch signal when `Advance()` throws | no-decision fix | **FIXED** | `AdvanceFailingPermissionStore` (DispatchProxy) | `DeviceAccessHandlerTests.Epoch_still_advances_when_the_policy_version_signal_fails`. X22 (m22 retargeted) killed |
| I-3 / M4: EF DI branch | no-decision fix | **FIXED** | — | `PersistenceModelTests.Database_registration_resolves_device_access_resolver_and_handlers_with_scope_validation` (resolver, 5 handlers, 2 grant handlers, `ValidateScopes = true`) |
| Mutation survivors M7, M21, M26, M27, M28, M30, M31, M32, M33, M34, M36, M47 | no-decision fix | **FIXED (12/12 killed)** | — | §5.2. My independent rerun with the tester's harness agrees with the tester's cycle-2 rerun |

Two cycle-1 items were outside this round:
- **L-2 (N+1):** accepted, Risk row phase-03:388.
- **L-3 (padded `" * "`):** unchanged, Low and inert.

## 3. New-code correctness

### 3.1 DelegationChain (`DelegationChain.cs`)

**Correct stop conditions:**
- master-issued row: `GrantedByMasterAdmin`;
- current master-admin role: `users.IsMasterAdmin`, which compares `NormalizedName`;
- a row without a grantor;
- a repeat, with the actor itself pre-seeded in `seen`.

**Three ways the walk itself is weaker than intended:**
- (a) It takes the latest row, allow **or deny**, and stops on a deny. That is a fourth stop condition, not in Q18 (M-1).
- (b) `MaxDepth` 32 *truncates* the walk, so the links above the cap are dropped rather than refused (M-2).
- (c) Under the Q18 definition, "the latest allow" can be written by anyone with the target in scope, including a redundant re-grant (M-3).

**Cycle guard.** Still needed:
- Rule A blocks cycles at write time, but two concurrent mutual appointments (A to B under B's lock, B to A under A's lock) both pass and form one.
- As a mutant N3 is equivalent: the cap terminates the loop, and so does `seen`, since every step adds a new user.

**Rule B bounds:**
- Each appointer's bounds come from persisted latest-wins grants only, with no claims: `PermissionGrantResolver.Features/Organizations` plus `OrganizationScopeResolver.Effective`.
- A deny `*` on an appointer bounds the appointee to nothing, which fails closed.

**Device bound.**
- Computed with a membership-only actor through the resolver, so a foreign-org role gives nothing.
- `SeesAllInScope` never counts, which keeps V6 strict for appointers as well.

**Rule A on role update** (the controller's extension). It is sound:
- Editing a role R that appointer B holds on D changes B's access exactly as replacing B's membership would, and Rule A forbids that replacement.
- Growth was already impossible: Rule B cuts own(D) to R.old wherever B holds R, so the extension adds shrinks and renames only.
- There is no side door. Deleting R is blocked while it is in use (V5), and assigning B is blocked by Rule A.
- It blocks even a description-only edit. That is consistent with the contract text ("may not update a role held by anyone in their chain"), and the order (after version and high-risk, before the per-device loop) matches contract §4.

### 3.2 WithUserLock

**In-memory** (`PermissionContracts.cs:95-99`):
- One global `_gate`, reentrant (Monitor). Every store call inside re-enters it.
- Lock order is always `_gate`, then the organization store or EF InMemory. Nothing takes them in reverse, so there is no deadlock.

**EF, non-PostgreSQL** (`EfPermissionStore.cs:125-129`): a static process lock. Correct, but nothing tests it (I-1).

**EF on PostgreSQL** (`EfPermissionStore.cs:131-137`), checked by reading:
- **Transaction.** `UseNpgsql(conn)` has no retrying execution strategy (`DependencyInjection.cs:156`), so `BeginTransaction()` is legal.
- **Lock call.** `pg_advisory_xact_lock(@p0, @p1)` binds the `(int4, int4)` overload. The lock is released at commit, or at rollback on dispose if `decide` throws.
- **Shared context.** `EfPermissionStore`, `EfOrganizationStore` and `IdentityUserManagement` (registered for both `IUserManagement` and `IUserScopeStore`, `DependencyInjection.cs:209-210`) share the scoped `ApplicationDbContext`. Every read and write inside the decision therefore runs on the transaction's connection.
- **Visibility.** Under READ COMMITTED, each statement after the lock statement takes a new snapshot, so it sees a competitor's commit.
- **Advance enlistment.** `AdvancePostgres` finds the connection already open (`shouldClose` is false) and enlists the command (`:173`). `SaveChanges` inside an explicit transaction uses savepoints.
- **Deadlock.** None. Each transaction takes exactly one advisory lock, always first; the `policy_versions` row lock comes second and is held until commit. Every other `policy_versions` writer is a single autocommit statement that takes no advisory lock.
- **Side effect.** Grant decisions now receive versions in commit order, which also closes cycle-1 L-1's "insert at v after v+1" interleaving.
- **Reentrancy.** The PostgreSQL path is not reentrant (I-3).

### 3.3 The other new rules

- **Subtree rule** (`PermissionContracts.cs:192-195`): runs for grant and deny, against the chain-cut scope. Correct.
- **Wildcard lift:** non-masters get `WILDCARD_GRANT_FORBIDDEN` (400) for `*` whether allow or deny. A master's allow `*` writes a row that only lifts. That is correct on its own. But a lift now *revives* allows that RT4(b) could not see while they were masked (M-4).
- **Command handler, scope first** (`SendDeviceCommandHandler.cs:46-53`):
  - It resolves access once, right after the device lookup, and reuses it for the policy.
  - A master-admin passes regardless of scope.
  - An out-of-scope revoked device gives `DEVICE_NOT_FOUND`, so no state leaks. An in-scope revoked device still gives `DEVICE_REVOKED` after the catalog stage, unchanged.
  - The idempotency precheck still runs first (accepted, transport plan).
- **Fail-closed:** applies to both replace and remove. A master is exempt, and a missing membership still returns early. Correct.
- **Reserved name:**
  - A comparison in Identity's normalisation space catches U+017F, and U+0131 still does not fold.
  - Under OrdinalIgnoreCase the controller falls through to Identity's check and returns 400 instead of 403, so the HTTP tests pin the controller guard too (R1 killed by both).

## 4. Regression walk

### 4.1 Changed members and their callers

| Changed member | Callers / implementers | Result |
|---|---|---|
| `IPermissionStore.WithUserLock` (new member) | Implementers: `InMemoryPermissionStore`, `EfPermissionStore`, and the `AdvanceFailingPermissionStore` DispatchProxy, which dispatches generically. Callers: the two grant handlers and one EF test | No other implementer exists |
| `GrantResourceScopeHandler` (+`IOrganizationStore`), `UpdateDeviceRoleHandler` (+`IUserScopeStore`) | DI (scoped, both branches resolved by tests) and the fixture | No other constructor call sites |
| `RoleAuthorizationPolicy.IsReserved` (wider) | `UsersController.cs:63`, `:83`; `IdentityUserManagement.cs:61` | `Program.cs` bootstrap uses the constant directly, so it is unaffected. `IsReserved(null)` is still false |
| `SendDeviceCommandHandler` | Only `DeviceCommandsController` | Intended change of order |
| `EfPermissionStore.AdvancePostgres` (enlists in the current transaction) | Also called outside a transaction (device handlers, `IdentityUserManagement` create/assign) | There `CurrentTransaction` is null, so behaviour is unchanged |
| Grant rule `*` (Q16) | `GrantResourceScopeHandler` RT4(b) reads the masked `Features(...).Allowed` | **Regression path: M-4** |
| New code `GRANT_TO_DELEGATOR_FORBIDDEN` | `PermissionGrantsController.Rejected` default branch gives 403 | Phase 4's membership and role routes must map it to 403 too (contract §4) |

### 4.2 Pre-existing tests whose assertions or setup changed

| Test | Change | Judgement |
|---|---|---|
| `PermissionTests.Wildcard_allow_grant_is_rejected_for_every_actor`, now `..._for_non_master` | Master assertion removed | Intended (Q16). The master behaviour moved to a new test |
| `PermissionTests.Persisted_feature_grant_overrides_claim_baseline` | + `DoesNotContain(..., Allowed)` | Strengthened (M27) |
| `PermissionTests.Claim_deny_wins_over_claim_allow_on_the_same_key` | + `DoesNotContain(..., Allowed)` | Strengthened (M26) |
| `DeviceAccessHandlerTests.Role_create_accepts_server_and_config_domain_codes` | + PolicyVersion +1 | Strengthened (M33) |
| `DeviceAccessHandlerTests.Role_delete_reports_stale_version_before_in_use` | + PolicyVersion unchanged on reject, +1 on delete | Strengthened (M34) |
| `CommandBoundaryTests.Concurrent_same_correlation_claims_transport_once` | `WaitAsync(10 s)` | Intended (L-5); assertions unchanged |
| `DeviceAccessHandlerTests` (section comment), `CommandPermissionResolverTests` (comment) | IDs removed from comments | Comment only |
| `DeviceAccessTestFixture` | Constructor arguments, `Unreadable`, `OnIsMasterAdmin`, the proxy | Additive setup. Existing behaviour unchanged, because the hooks default to off |
| `ManagementApiTests.Device_command_uses_persisted_user_organization_when_org_claim_is_absent`, now `In_scope_device_command_reaches_the_catalog_lookup` | Renamed, own user, message | Intended (L-6/M1) |
| `ManagementApiTests.Device_command_requires_catalog_metadata_at_the_public_boundary` | Own user | Intended. Under scope-first, the shared user's persisted Child deny would have made it order-dependent |
| `ManagementApiTests.Feature_grant_rejects_wildcard_allow_even_for_master_admin`, now `Wildcard_allow_lifts_a_block_only_for_master_admin_over_http` | The master allow `*` assertion inverted from 400 to 200 | Intended (Q16) |

No assertion was weakened beyond what an owner decision requires.

## 5. Mutation results

**Harness.** The tester's `phase03-mut/mutate.js` runner, unchanged (raw-byte restore, strict baseline, watchdog, integrity check), with the mutants spliced in from `S/harness/mutants-c2.js` and `S/harness/mutants-extra.js`. Every target matched exactly once (`--check`).

**Baselines.** All green at 402/103. Integrity was restored byte for byte, and the copies stayed identical to the repo.

### 5.1 New logic: 30 mutants, 21 killed, 9 survived

| ID | Mutant | Result | Killed by / classification |
|---|---|---|---|
| N1 | Chain ignores `GrantedByMasterAdmin` | KILLED | 12 `DelegationChainTests` + 2 HTTP |
| N2 | Chain ignores the appointer's current master-admin role | **SURVIVED** | Test gap. It fails closed: a promoted appointer's sparse grants would bound the appointee. L-1, probe P2 |
| N3 | No cycle guard | **SURVIVED** | Equivalent as a mutant: `MaxDepth` terminates. The cap itself fails open (M-2) |
| N4 | A denied latest appointment does not end the chain (follows the deny's grantor) | **SURVIVED** | No test pins deny-link behaviour in either direction. M-1 |
| N5 | `MaxDepth = 1` | KILLED | `Admin_two_appointments_down_cannot_act_on_either_appointer` |
| A1 | Rule A dropped, feature grant/deny | KILLED | `DelegationChainTests` ×3, HTTP `Appointed_admin_cannot_act_on_its_appointer_over_http` |
| A2 | Rule A dropped, resource grant/deny | KILLED | `Appointed_admin_cannot_grant_or_deny_anything_to_its_appointer` |
| A3 | Rule A dropped, assign/replace | KILLED | `Appointed_admin_cannot_assign_or_remove_the_membership_of_its_appointer` |
| A4 | Rule A dropped, remove | KILLED | same |
| A5 | Rule A dropped, role update | KILLED | `Appointed_admin_cannot_change_a_role_its_appointer_holds` |
| B1 | Rule B dropped, features in a feature grant | KILLED | `Appointed_admin_grants_no_feature_its_appointer_lacks` |
| B2 | Rule B dropped, target features in a resource grant | **SURVIVED** | Test gap (L-1, probe P3) |
| B3 | Rule B dropped, orgs in a resource grant/deny | KILLED | `Appointed_admin_grants_no_organization_its_appointer_lacks` |
| B4 | Rule B dropped, target scope in a feature grant | **SURVIVED** | Test gap (L-1, probe P4) |
| B5 | Rule B dropped, device permissions on assign/replace | KILLED | 2 `DelegationChainTests` |
| B6 | Rule B dropped, device permissions on remove | **SURVIVED** | Test gap (L-1, probe P5) |
| B7 | Rule B dropped, device permissions on role update | KILLED | `Appointed_admin_cannot_widen_a_role_beyond_its_appointer_on_devices_using_it` |
| Q1 | Subtree rule checks only the org itself | KILLED | unit + HTTP subtree tests |
| Q2 | Subtree rule skipped for deny | KILLED | `Non_master_cannot_grant_or_deny_an_organization_whose_subtree_leaves_their_scope` (+1) |
| W1 | Non-master allow `*` passes the wildcard check | KILLED | unit + HTTP |
| W2 | Master allow `*` rejected (no lift) | KILLED | `Master_admin_lifts_a_wildcard_deny_with_a_wildcard_allow` + HTTP |
| L1 | In-memory `WithUserLock` without the lock | KILLED | both `GrantSerializationTests` |
| L2 | Resource handler bypasses `WithUserLock` | **SURVIVED** | Test gap (L-1, probe P6) |
| L3 | EF non-PostgreSQL `WithUserLock` without the process lock | **SURVIVED** | Unpinned, but the branch only serves direct-construction tests (I-1) |
| S1 | Command handler checks scope only after the catalog stage | KILLED | unit + HTTP out-of-scope tests |
| F1 | Fail-closed removed | KILLED | `Unreadable_current_role_counts_as_stronger_than_a_non_master` |
| F2 | Fail-closed removed from remove only | KILLED | same |
| R1 | `IsReserved` back to OrdinalIgnoreCase | KILLED | unit + 2 HTTP |
| R2 | Identity `NormalizeKey` check dropped | **SURVIVED** | Equivalent under the default normalizer (I-2) |
| R3 | Identity reserved check dropped from `AssignRoleAsync` | KILLED | `Identity_role_management_rejects_a_lookalike_master_admin_name_for_non_master` |

### 5.2 Cycle-1 survivors (tester's definitions, unchanged targets) and two retargeted cycle-1 mutants

All 14 were killed.

| Mutant | Killed by |
|---|---|
| M7 | `DeviceAccessPolicyTests.In_scope_actor_without_a_management_feature_does_not_see_every_device` |
| M21 | `DeviceCommandAuthorizationTests.Replay_of_a_confirmed_command_without_confirm_still_requires_confirm` |
| M26 | `PermissionTests.Claim_deny_wins_over_claim_allow_on_the_same_key` |
| M27 | `PermissionTests.Persisted_feature_grant_overrides_claim_baseline` + `AccessActorFactoryTests.Persisted_deny_removes_a_claim_allow_from_the_actor_features` |
| M28 | `PermissionTests.Non_master_may_allow_after_master_reversed_own_deny` |
| M30 | `DeviceAccessHandlerTests.Assigning_the_current_role_again_is_a_no_op` |
| M31 | `Role_create_accepts_name_and_description_at_their_maximum_length` |
| M32 | `Unexpected_store_failure_propagates_from_role_create` |
| M33 | `Role_create_accepts_server_and_config_domain_codes` |
| M34 | `Role_delete_reports_stale_version_before_in_use` |
| M36 | HTTP `Device_command_passes_confirm_through_to_the_handler` |
| M47 | `RealtimeTests.Epoch_store_advance_many_moves_each_distinct_device_once` |
| X35 (cycle-1 M35) | Fails fast, no hang |
| X22 (cycle-1 m22) | `Epoch_still_advances_when_the_policy_version_signal_fails` |

The SignalR backlog flake also appeared next to the real killer in M28 and M31.

### 5.3 Probes in `P/`

All 10 probes pass on the current code (`P/` suite: 411/411 unit, 104/104 integration).

- **P1:** `ReviewC2Probes.P1_...` and HTTP `ManagementApiTests.ReviewC2_P1_...`. These are M-1's evidence.
- **P2–P6:** `ReviewC2Probes`. Rerunning N2, B2, B4, B6 and L2 with them added: **5/5 killed**, each by its probe.
- **P7–P9:** `ReviewC2MoreProbes`. These are the evidence for M-2, M-3 and M-4.

## 6. New findings

### Medium

**M-1. A demoted middle appointer ends the delegation chain, so Rule A stops protecting, and Rule B stops bounding by, every appointer above it.** CONFIRMED over HTTP; found in this review.

- **Where:** `src/Hms.Server.Application/Authorization/DelegationChain.cs:28-34`. Each link takes the latest `permissions.manage` row, allow or deny, and `appointment is not { Deny: false, ... }` breaks the walk when that row is a deny.
- **Contract:** Q18 (`design-contract.md:39`) defines the appointer as "the grantor of their latest `permissions.manage` **allow**". It lists three stop conditions; a denied link is not one of them.
- **Risk table:**
  - Row phase-03:385 paraphrases the chain as built on the "latest row", but only discusses legacy rows.
  - Row :386 claims that after an appointer loses `permissions.manage`, "Rule B still cuts by the appointer's current permissions". That holds for the direct appointer only; everyone above is silently dropped.
- **Scenario** (persisted grants only, `P/tests/Hms.Server.IntegrationTests/ManagementApiTests.cs` `ReviewC2_P1_demoted_middle_appointer_lets_bottom_admin_strip_the_top_over_http`):
  1. The master seeds C with `permissions.manage` and scope Child; B and A get scope Child.
  2. C grants B `permissions.manage` (200). B grants A `permissions.manage` (200).
  3. A denies C's `permissions.manage`: **403 `GRANT_TO_DELEGATOR_FORBIDDEN`**.
  4. C demotes B (deny `permissions.manage`, 200). A keeps it under the accepted no-cascade backlog.
  5. A denies C's `permissions.manage`: **200**. C's next grant attempt gets 403.
- **Rule B lapses too** (unit P1): before the demotion A cannot hand out `devices.manage` (C lacks it); after the demotion A can.
- **Who can trigger the demotion:** the master, C, or any non-master outside B's chain who has B in scope.
- **Minimal fix** (validated in `P/`): walk to the grantor of the latest **allow**.
  ```csharp
  .LatestFeatureGrants(permissions.FeatureGrants(current).Where(x => x.Permission == PermissionsManage && !x.Deny))
  ...
  if (appointment is not { GrantedByMasterAdmin: false, GrantedBy: { } appointer } ||
  ```
  With it, all 402 unit and 103 integration repo tests pass, as do P2–P6; only the two P1 probes flip. Add a test in the P1 shape.
- **Interaction with M-3:** "latest allow" is exactly what M-3 captures, so decide M-1 and M-3 together (see M-3's option (a), which fixes both).

**M-2. The chain cap truncates the walk instead of failing closed: 33 links down, the appointers above the cap no longer protect or bound.** CONFIRMED by a unit probe through the real handlers (P7).

- **Where:** `DelegationChain.cs:16` and `:26`. `while (chain.Count < MaxDepth)` returns the first 32 links as if the chain ended there.
- **Scenario:**
  - B (master-appointed) appoints A, and A chains 32 own appointees S1 to S32, each appointed by the previous one.
  - A, and S31, get `GRANT_TO_DELEGATOR_FORBIDDEN` when denying B's `permissions.manage`. S32 gets **OK**, because its chain `[S31..S1, A]` has 32 entries and B is cut off.
  - S32's Rule B bound no longer includes B either.
- **Reach.** An admin holding `users.manage` and `permissions.manage` can build the chain alone over HTTP, with about 100 requests. Every link passes Rule A, Rule B, RT4 and Q15, because all links share A's org and scope. The controller adds only `CanManageTarget`, which every link passes.
- **Impact:** a self-made path around Rule A and Rule B toward one's own appointer.
- **Fix:** fail closed when the walk reaches the cap with a further appointer still pending. For example, return a "truncated" marker that makes every non-master decision `GRANT_EXCEEDS_OWN_PERMISSIONS`. Alternatively, drop the cap: the `seen` guard already bounds the loop by the number of distinct users. Add a cap/cap+1 test.

**M-3. "Appointer = grantor of the latest `permissions.manage` allow" can be captured by a redundant re-grant (design gap in Q18; owner decision).** CONFIRMED by a unit probe (P8).

- **Where:** the Q18 definition itself, implemented at `DelegationChain.cs:28-31`; `GrantFeaturePermissionHandler` writes an allow even when the target already holds the key.
- **Scenario:**
  - A peer admin X (non-master, with `permissions.manage` and the victim V in scope) re-grants V `permissions.manage`, which V already holds from the master. The request returns OK.
  - V's chain is now `[X]`. V gets `GRANT_TO_DELEGATOR_FORBIDDEN` against X, and is bound by X's holdings: V can no longer hand out `devices.manage`, which X lacks.
  - X can still act on V.
  - V cannot undo it (`SELF_GRANT_FORBIDDEN`). After a master re-grant, X can recapture with one more request.
- **Impact:** one request turns the symmetric peer relation, which the owner accepted (Risk row :387), into a one-sided one. That defeats Rule A's purpose.
- **Options (owner):**
  - (a) Define the appointer as the grantor of the latest `permissions.manage` allow that was either master-issued or switched the key from not-allowed to allowed. Redundant re-grants and later denies then do not move it, which also fixes M-1, and master re-grants still end the chain.
  - (b) Make a non-master allow on a key the target already holds a no-op: OK, no row written.
  - (c) Accept, and record it in the Risk table.

**M-4. Q16's lift revives allows over scope that RT4(b) never checked: mask, widen, lift.** CONFIRMED by a unit probe (P9). This is a regression made reachable by this round, because before Q16 a deny `*` was permanent.

- **Where:** `PermissionContracts.cs:199-201`. RT4(b) compares `PermissionGrantResolver.Features([], [], targetGrants).Allowed`, and `Features` clears `Allowed` when `Denied` contains `*` (`PermissionGrantResolver.cs:45-47`).
- **Scenario:**
  1. Target T holds `devices.manage` (master).
  2. An admin with scope Parent but no `devices.manage` tries to widen T to Parent: `GRANT_EXCEEDS_OWN_PERMISSIONS`. Correct.
  3. The master suspends T with a deny `*`. The same widening now returns **OK**, because T's masked feature set is empty.
  4. The master lifts the suspension (allow `*`, Q16). T now has `devices.manage` over Parent, a combination no single non-master could grant.
- **Fix:** compare against the target's latent allows, ignoring the mask: `PermissionGrantResolver.LatestFeatureGrants(grants).Where(x => !x.Deny && x.Permission != PermissionGrantResolver.Wildcard)`. Add a mask, widen, lift test. Rule B's own use of masked `Allowed` (appointers, the actor) is fail-closed and needs no change.

### Low

**L-1. Five new rules are implemented correctly but no test pins them** (survivors N2, B2, B4, B6, L2). Each probe below passes on current code and kills its mutant (§5.3). They can be moved into `DelegationChainTests` and `GrantSerializationTests` almost as written (`P/tests/Hms.Server.UnitTests/ReviewC2Probes.cs`).

| Probe | Rule (code) | What it pins |
|---|---|---|
| P2 | Chain stop on a current master-admin role, `DelegationChain.cs:33` | An appointer later promoted to master-admin ends the chain |
| P3 | Rule B, target features in a resource grant, `PermissionContracts.cs:200` | |
| P4 | Rule B, target scope in a feature grant, `PermissionContracts.cs:152` | |
| P5 | Rule B, device permissions on remove, `DeviceAccessHandlers.cs:290` | |
| P6 | The resource handler's `WithUserLock`, `PermissionContracts.cs:166` | Pauses the **resource** handler |

Why P6 is needed: the existing serialization tests only pause the feature handler. Every in-memory store read takes the same `_gate`, so an unlocked resource handler still blocks behind the paused one, and the missing lock goes unnoticed.

**L-2. The build is not warning-free.**
- A clean `--no-incremental` build of the exact repo tree reports 2 `xUnit1031` warnings, at `tests/Hms.Server.UnitTests/GrantSerializationTests.cs:33` (`scopeGrant.Wait(Settle)`) and `:68` (`adminAllow.Wait(Settle)`).
- Nothing in the repo suppresses them: there is no `Directory.Build.*`, no `.editorconfig` and no `NoWarn`. The controller's gate claim says 0.
- Fix: `Assert.NotSame(scopeGrant, await Task.WhenAny(scopeGrant, Task.Delay(Settle)));`, the pattern probe P6 uses.

**L-3. Remnant of L-4.** `tests/Hms.Server.UnitTests/DeviceAccessTestFixture.cs:16` ("the phase-2 role/membership store") and `:133` ("Forwards to the phase-2 store") still carry a phase number. Fix: "the role/membership store".

### Info

- **I-1.** L3 survives. Nothing proves that the EF non-PostgreSQL `ProcessGate` (`EfPermissionStore.cs:125-129`) serialises. DI wires `EfPermissionStore` only when a connection string is configured (Npgsql), so this branch serves only direct-construction tests. Risk row :384 ("tests run the EF non-PostgreSQL branch") is literally true (`Ef_permission_store_runs_a_user_locked_decision_and_keeps_its_write`).
- **I-2. Equivalent mutants:**
  - R2: the `NormalizeKey` check (`IdentityUserManagement.cs:61-62`) is redundant under the default normalizer and keeps a custom `ILookupNormalizer` honest.
  - N3: see §3.1.
- **I-3.** `WithUserLock` reentrancy depends on the provider. In-memory and EF non-PostgreSQL re-enter (Monitor). On PostgreSQL, `BeginTransaction` throws when a transaction is already open. No caller nests today, but a future nested call would pass every test and fail only on PostgreSQL. Either state "not reentrant" on `IPermissionStore.WithUserLock`, or run `decide()` directly when `CurrentTransaction` is not null.
- **I-4.** `MeController.cs:28-35` still unions grant history (cycle-1 I-4, phase 4). After a Q16 lift, a non-master shows `*` in `allowedPermissions` (and the old `*` in `deniedPermissions`) until phase 4 switches `/me/permissions` to the resolver. This is UI-only, and the release is already gated on phase 4.

## 7. Commands run

```bash
# repo (read-only)
cd /d/WORKSPACE/PROJECT/HMS-SERVER && find src tests -type f -newer <cycle-1 review report> -not -path '*/bin/*' -not -path '*/obj/*'   # 24 files, = brief
diff -u --strip-trailing-cr scratchpad/phase03-mut/<file> <file>      # exact fix-round diff (tester copy = pre-fix repo, INTEGRITY-checked)
ls --time-style=full-iso tests/*/bin/Debug/net10.0/*.dll              # binaries 07:55:53-55 newer than last source edit 07:55:51
cd /d/WORKSPACE/PROJECT/HMS-SERVER && dotnet test Hms.Server.slnx --no-build --nologo   # 402/402, 103/103
grep -rnE "Q[0-9]+|RT[0-9]+|design contract|phase-?[0-9]|§" src tests --include=*.cs     # L-4 check
diff -rq -x bin -x obj -x .tmp-unit-out src S/src; ... tests S/tests # freshness before writing: identical

# S/ (pristine copy)
tar --exclude='*/bin' --exclude='*/obj' -cf - Hms.Server.slnx src tests | (cd S/ && tar -xf -)
dotnet build Hms.Server.slnx --nologo [-v q | --no-incremental]     # 0 errors, 2 warnings (xUnit1031)
dotnet test Hms.Server.slnx --no-build --nologo                      # 402/103
node harness/mutate.js . --source=<repo> [--check]                   # 30 new mutants: 21 killed, 9 survived
node tester-harness/mutate.js . --source=<repo> --only=M7,...,M47    # 12/12 killed
node harness/mutate-extra.js . --source=<repo> --only=X35,X22        # 2/2 killed, no hang

# P/ (copy + probes)
dotnet build; dotnet test --filter "ReviewC2Probes|ReviewC2MoreProbes|ReviewC2_P1"   # 10/10 pass on current code
dotnet test Hms.Server.slnx --no-build                               # 411/104 green
node harness/mutate.js . --source=<repo> --only=N2,B2,B4,B6,L2       # 5/5 killed by P2-P6
(apply M-1 fix) dotnet build; dotnet test Hms.Server.slnx --no-build # originals 402+103 green; only the 2 P1 probes flip; restored
```

## 8. Plan follow-ups (no plan file edited)

The "Bổ sung sau review vòng 1" addendum at phase-03:357-363:

| Line | Item | Status |
|---|---|---|
| :357 | Lookalike role name | Met |
| :358 | Subtree rule | Met |
| :359 | Wildcard key | Met, but the lift interacts with RT4(b) (M-4) |
| :360 | Serialisation | Met: in-memory proven, PostgreSQL by reading |
| :361 | Chain | Met for intact chains up to 32 links, master re-appointment and cycles. Not met for a demoted middle link (M-1), deep chains (M-2) or redundant re-grants (M-3) |
| :362 | Command scope-first | Met |
| :363 | Fail-closed | Met |

Original criterion 12, partial in cycle 1 because of H-1, is now met for H-1 but reopened by M-4.

Before marking phase 3 complete:
1. M-1, M-2 and M-4: fixes plus tests.
2. M-3: an owner decision on the appointer definition, taken together with M-1. Then update contract Q18 and the Risk rows :385 and :386.
3. Add the P2–P6 tests.
4. Clear the 2 `xUnit1031` warnings.
5. Fix the 2 fixture comments.

For phase 4:
- Map `GRANT_TO_DELEGATOR_FORBIDDEN` to 403 on the membership and role routes.
- Switch `MeController` (I-4).

## 9. Unresolved questions

1. **M-3 / M-1 (owner):** which appointer definition?
   - (a) The latest master-issued allow, or the latest allow that switched `permissions.manage` on. This fixes both.
   - (b) Make non-master redundant allows no-ops, and fix M-1 separately with the latest-allow walk.
   - (c) Accept capture, and record it in the Risk table.
2. **M-2:** fail closed at the cap, or drop the cap and rely on the `seen` guard? Both are safe; failing closed keeps the query cost bounded.

Status: DONE_WITH_CONCERNS
Summary: Every cycle-1 finding is fixed as decided and pinned (L-4 has a 2-comment remnant), all 12 cycle-1 survivors are killed, and repo tests pass 402/103. The new delegation-chain code has three verified gaps: M-1, a demoted middle link drops the appointers above it, confirmed over HTTP; M-2, the 32-link cap fails open; M-3, a redundant re-grant captures the appointer relation. Q16's lift also opens an RT4(b) bypass (M-4: mask, widen, lift).
Concerns/Blockers: M-1, M-2 and M-4 need small fixes plus tests; the M-1 fix is validated in scratch. M-3 needs an owner decision on the appointer definition, best taken together with M-1. L-1's 5 pinning tests are ready in P/. The build has 2 xUnit1031 warnings, contrary to the gate claim.
