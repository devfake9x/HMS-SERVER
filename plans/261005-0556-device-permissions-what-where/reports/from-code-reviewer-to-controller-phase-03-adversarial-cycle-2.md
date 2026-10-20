---
title: "Phase 3 adversarial review, cycle 2: the cycle-1 fixes and the new rules Q15-Q18"
date: 2026-10-06
reviewer: code-reviewer (adversarial)
scope: files changed after the cycle-1 adversarial report (11 src, 13 tests); contract §0 Q15-Q18, §4 grant and delegation rules, §5 command handler order; phase-03 Success Criteria lines 355-363, Risk rows 384-388, Security Considerations line 396
decision: BLOCKED
---

# Phase 3 adversarial review, cycle 2

Paths:
- `S2/` = `C:/Users/toann/AppData/Local/Temp/claude/D--WORKSPACE-PROJECT-HMS-SERVER/daebfbef-46ca-4e9f-b506-cf6e26aef926/scratchpad/phase03-adversarial-c2/`. This is a tar copy of `Hms.Server.slnx`, `src` and `tests`, without bin/obj. Its `src` was byte-identical to the repo when I finished (`diff -rq`).
- `M2/` = `.../scratchpad/phase03-adversarial-c2-mut/`, a fresh copy without probes, used for mutants.

All builds, probes, mutants and fix trials ran in scratch. In the repo I only ran `dotnet test Hms.Server.slnx --no-build --nologo` and read files. Besides this report, I appended three lines to my agent-memory note (`.claude/agent-memory/code-reviewer/reference_authz_probe_heuristics.md` and its `MEMORY.md` index line), as in cycle 1. Revert them if unwanted.

## Summary

The cycle-1 fixes hold:
- **Reserved names:** an exhaustive Unicode sweep finds no lookalike left.
- **Subtree rule (Q15):** enforced for both grants and denies.
- **Wildcard writes (Q16):** only a master-admin can write the `*` key.
- **Feature-grant serialization (Q17):** the feature handler is serialized.
- **Command handler:** it checks scope first.
- **Unreadable current role:** fails closed.

The new delegation chain (Q18, Rule A) breaks in two ways that need nobody else's help. Both are proven over HTTP with persisted grants only.

1. **P1 (medium, owner decision): a peer captures an admin with one redundant grant.** Any admin whose scope covers a peer re-grants the peer the `permissions.manage` it already holds, and so becomes the peer's appointer. The peer can no longer act on the capturer. Everything the peer hands out is now limited to what the capturer holds. The peer cannot undo it, and a master re-grant is undone by one more call.
2. **P2 (medium): the chain is truncated after 32 links.** An admin who holds `users.manage` and `permissions.manage` chains 32 accounts of their own. The 32nd account demotes the admin's appointer. The contract has no such stop.

Two low items:
- **P3:** a deny on a middle admin's `permissions.manage` ends the chain there. This is against the contract wording, and a legitimate demotion therefore loosens the limits on every admin below it.
- **P4:** lifting a deny `*` (Q16) revives the target's earlier feature grants, including over scope that a non-master widened during the suspension.

Missing proof:
- The resource grant handler's lock is unproven: mutant C14 survives.
- The two Rule B cuts inside RT4 (C4, C5) are untested.
- The chain stop at an appointer who is now a master-admin (C1) is untested.
- No test pins whether a deny row ends the chain (C11).
- The build has two new warnings.

Nothing reaches master-admin, and no path grants authority beyond what a master granted somewhere in the chain.

## Verdict (JSON-ready)

```json
{
  "decision": "BLOCKED",
  "blockingReasons": [
    "P2 (medium): DelegationChain.Of stops after 32 links. Contract §0 Q18 has no such stop. An admin with users.manage + permissions.manage chains 32 own accounts, and the 32nd demotes the appointer over HTTP: Rule A is bypassed without anyone's cooperation. A fail-closed fix is validated in scratch.",
    "P1 (medium, owner decision): Q18 defines the appointer as the grantor of the latest permissions.manage allow. Under that rule, any admin whose scope covers a peer becomes the peer's appointer with one redundant grant (proven over HTTP). The peer can no longer act on the capturer, the peer's delegation is cut to the capturer's holdings, the peer cannot undo it, and a master re-grant is undone by recapture."
  ],
  "disprovenClaims": [
    {"claim": "Q18 / SC line 361 / SecCons line 396: a non-master never acts on anyone in their appointment chain. Fails at depth 32 (P2).", "severity": "medium"},
    {"claim": "Q18 purpose: 'lower admins do not manage the admins above them'. A peer makes itself 'above' with one redundant grant (P1).", "severity": "medium (owner decision)"},
    {"claim": "Q18: the appointer is the grantor of the latest permissions.manage allow, and the chain stops only at a master-admin, a row without grantor or a repeat. The code also stops at a deny row (P3).", "severity": "low"},
    {"claim": "Q16: the wildcard allow 'grants nothing' and RT4(b) holds. Lifting a deny * revives features over scope widened during the suspension (P4).", "severity": "low"}
  ],
  "reachableRegressions": [],
  "missingProof": [
    "C14: resource grant handler without WithUserLock survives (feature twin C15 is killed)",
    "C4: Rule B organization cut in the feature-allow RT4(a) check is untested",
    "C5: Rule B feature cut in the resource-allow RT4(b) check is untested",
    "C1: chain stop at an appointer who is now a master-admin is untested",
    "C11: no test pins whether a deny row ends the chain",
    "C2/C13: cap value and the EF process lock are untested (info)",
    "2 new build warnings xUnit1031 (GrantSerializationTests.cs:33, :68)"
  ],
  "unverifiedClaims": [
    "EfPermissionStore.WithUserLock PostgreSQL branch (transaction + pg_advisory_xact_lock + AdvancePostgres enlisted): correct by inspection, not run",
    "AdvanceManyPostgres ORDER BY: correct by inspection, not run"
  ]
}
```

## 1. DISPROVEN CLAIMS

### P1 (medium; owner decision). A peer captures an admin with one redundant grant

**Claim.**
- Contract §0 Q18 (design-contract.md:39) gives Rule A's purpose: "lower admins do not manage the admins above them". The chain is meant to model who appointed whom.
- phase-03 line 396: "non-master không tác động người trong chuỗi bổ nhiệm mình" (a non-master never acts on anyone in their appointment chain).

**Path.**
- `DelegationChain.cs:28-36`: the appointer is the `GrantedBy` of the user's latest `permissions.manage` row.
- `PermissionContracts.cs:129-157` accepts a non-master allow of `permissions.manage` for a target who already holds it. The only conditions are:
  - the target is not in the grantor's own chain (:142-145);
  - the grantor's features, cut to their chain, contain the permission (:151);
  - the target's persisted scope is a subset of the grantor's scope, cut to their chain (:149-152);
  - no master deny stands on that key (:154-156).
- The controller adds `CanManageTarget`: the target's home org must be in the grantor's scope.
- Two peers with the same scope meet all of these conditions. The redundant row then makes the grantor the appointer.

**Evidence** (all pass on current code):
- **H1 over HTTP.** Probe: `S2/tests/Hms.Server.IntegrationTests/AdversarialCycle2HttpProbes.cs` `H1_peer_regrant_captures_a_master_appointed_admin_over_http`.
  - Setup: victim and peer are both appointed by the master, both with scope Child. Grants are persisted only.
  - Before: the victim's deny on the peer returns 200, and the victim granting `devices.manage` to X returns 200.
  - The peer sends `POST /permissions/features {victim, permissions.manage}` and gets 200.
  - After: the victim's deny on the peer gets 403 `GRANT_TO_DELEGATOR_FORBIDDEN`. The victim granting `devices.manage` to Y gets 403 `GRANT_EXCEEDS_OWN_PERMISSIONS`.
- **P1 (unit).** Probe: `S2/tests/Hms.Server.UnitTests/AdversarialCycle2Probes.cs` `P1_peer_regrant_captures_a_master_appointed_admin`.
  - Same steps as H1.
  - In addition, the victim's device delegation is now cut to the peer's role on D. The peer has none, so assigning even a `device.view` role gets `GRANT_EXCEEDS_OWN_PERMISSIONS`.
  - The victim's attempt to undo it with a self-grant gets `SELF_GRANT_FORBIDDEN`.
  - The peer can still act on the victim.
- **P1b (unit).** Probe: `P1b_peer_recaptures_after_the_master_reappoints`.
  - The documented recovery is a master re-grant, which ends the chain (`Reappointment_by_master_admin_ends_the_chain`). It works only until the peer re-grants again: one call.

**Impact.**
- The owner kept peer management (Risk row line 387): peers can deny each other. P1 makes that one-sided. The first admin to re-grant becomes immune to the other, silently: the victim keeps `permissions.manage` and sees nothing until a request fails.
- The victim's delegation is limited to the capturer's holdings: a denial of service on the victim's admin role.
- The capturer gains no new authority.

**For the owner.** The code follows the contract text, so the defect is in the Q18 definition itself.
- **Original decision (Q18):** "An admin's appointer is the grantor of their latest permissions.manage allow".
- **Concern:** under this definition, the appointer relation can be assigned by any admin whose scope covers the target.
- **Options:**
  - (a) Reject a non-master allow of `permissions.manage` for a target who already holds it.
    - Validated in S2: H1 and P1 flip; the 402 unit and 103 integration repo tests stay green.
    - Residual: a peer can deny and then re-allow. That takes two calls and visibly demotes the victim, which amounts to a real re-appointment.
  - (b) Define the appointer as the grantor of the allow that started the current holding, that is, the earliest allow after the latest deny. A master-issued row would still end the chain.
  - (c) Only a master-admin grants `permissions.manage`. This was cycle-1 D4 option (c). It removes P1 and P2 at once.

### P2 (medium). The 32-link cap truncates the chain: 32 accounts of one's own act on the appointer

**Claim.**
- Contract §0 Q18: the chain "stops at a master-admin (row flag or current role), a row without grantor, or a repeat".
- SC line 361: Rule A holds "kể cả hai tầng" (across two levels too), and in general for anyone in the chain.

**Path.**
- `DelegationChain.cs:16` sets `MaxDepth = 32`. Line 26, `while (chain.Count < MaxDepth)`, ends the walk silently.
- `CheckTarget` (:42-43) and the Rule B cuts (:46-75) then ignore every appointer beyond link 32.
- The contract has no depth stop.

**Attack.**
1. Admin A holds `users.manage` and `permissions.manage` and was appointed by B.
2. A creates S1 to S32 in scope with `POST /users`. A chooses the password and `EmailConfirmed`.
3. A grants S1, and each S_i grants S_{i+1}, the resource Child plus `permissions.manage`. Each grant passes Q15, RT4 and Rule B.
4. The chain of S31 is [S30..S1, A, B], 32 entries. The chain of S32 is [S31..S1, A], also 32 entries, so B is cut off.

**Evidence** (pass on current code):
- **H2 over HTTP.** Probe: `H2_32_deep_sockpuppet_chain_demotes_the_appointer_over_http`.
  - S31's deny of B's `permissions.manage` gets 403 `GRANT_TO_DELEGATOR_FORBIDDEN`.
  - S32's identical request gets 200, and B no longer holds `permissions.manage`.
  - About 100 API calls; no other person is involved.
- **P2 (unit).** Probe `P2_chain_depth_cap_lets_a_32_deep_sockpuppet_chain_act_on_the_appointer` shows the same result.

**Fix (validated).**
- When the walk reaches the cap with appointers left, fail closed: Rule A forbids every target, and Rule B cuts to nothing.
- The S2 trial: `chain.Count >= MaxDepth || chain.Contains(target)` in `CheckTarget`. P2 and H2 flip; the repo tests stay at 402 and 103, all green.
- A precise fix needs a "truncated" flag, so that a chain of exactly 32 complete links is not over-blocked.
- Mutant C2 (cap 2) survives, so no test pins the cap value either.

### P3 (low). A deny on a middle admin's permissions.manage ends the chain there

**Claim.** Contract §0 Q18 says the appointer is the grantor of the user's "latest `permissions.manage` allow", and its list of stops has no deny. The controller's brief lists the same stops.

**Path.** `DelegationChain.cs:28-34` takes the latest row of the key and stops when that row is a deny (the `Deny: false` pattern).

**Evidence** (unit, pass):
- **P3.** Setup: top appoints middle, and middle appoints bottom. Top lacks `devices.manage`; middle and bottom hold it.
  - Before: bottom's deny on top gets `GRANT_TO_DELEGATOR_FORBIDDEN`, and bottom granting `devices.manage` gets `GRANT_EXCEEDS_OWN_PERMISSIONS`.
  - Then top demotes middle with a deny on `permissions.manage`.
  - After: both of bottom's requests get OK.
  - Cascading revocation is backlog, so bottom keeps `permissions.manage`. Demoting a link therefore loosens Rules A and B for every admin below it.
- **P3b.** A request built just before the actor's own appointer demotes them is no longer bound by Rule A. The actor snapshot is stale, and the deny row ends the chain.

**Fix (validated).** Filter to allow rows only: add `&& !x.Deny` to the filter at :29. P3 and P3b flip; the repo tests stay green.

Mutant C11, which is this fix, survives: no test pins either reading. Risk row 386 says Rule B still cuts by the appointer's current permissions. With the deny stop, Rule B cuts only by the demoted admin, and no longer by anyone above them.

### P4 (low). Lifting a deny * revives features over scope widened during the suspension (Q16 × RT4(b))

**Claim.**
- Q16: the revoking row is the `*` allow, "which grants nothing".
- RT4(b): a non-master resource allow requires the target's allowed features to be a subset of the actor's.

**Path.**
- `PermissionGrantResolver.cs:45-47` clears `Allowed` while a deny `*` stands.
- `PermissionContracts.cs:199-201` compares that `Allowed` set, which is empty, with the actor's features.

**Evidence (unit P4, pass):**
1. The master gives T `devices.manage` and the scope Child.
2. A non-master over Parent, without `devices.manage`, grants T Parent. This gets `GRANT_EXCEEDS_OWN_PERMISSIONS`.
3. The master denies `*`. The same grant now gets OK.
4. The master allows `*`.
5. T now has `devices.manage` over Parent and Child. Neither the non-master nor the master granted that combination.

Before Q16, a deny `*` was permanent, so the earlier grants could never come back.

**Fix (validated).** RT4(b) should compare the target's latest allow rows, not the masked `Allowed` set: `LatestFeatureGrants(...).Where(x => !x.Deny && x.Permission != "*")`. P4 flips; the repo tests stay green. The same revival happens when the master re-allows a single key, but that is an explicit master decision.

## 2. REACHABLE REGRESSIONS

None. Every behaviour change since cycle 1 is intended:
- Rule B can block delegation where an appointer holds less.
- Q15 now also limits denies.
- Lookalike names are reserved; the sweep found no false positive.
- Commands check scope first. Master-admin, revoked devices and cached results behave as before (§6).
- In-memory grant decisions now share one lock. This costs only performance, and only in development.

## 3. MISSING PROOF

| # | Sev | Gap | Evidence |
|---|---|---|---|
| M1 | low-medium | The resource grant handler's lock is unproven. Losing it reopens the cycle-1 D2/D3 races for resource grants. | C14 (`GrantResourceScopeHandler.Handle` without `WithUserLock`) **survives**. C15 (the same for the feature handler) is killed by both `GrantSerializationTests`. Those tests pause only the feature handler. Each read of the resource handler takes the global in-memory gate, so they pass either way. A test that pauses the resource handler after its RT4(b) read would kill C14, for example a `DispatchProxy` over `IPermissionStore` that pauses in `ResourceGrants(target)` and forwards `WithUserLock` to the real store. |
| M2 | low | Rule B's organization cut inside a feature allow (RT4(a), `PermissionContracts.cs:149-152`) is untested. | C4 survives. Probe `M_feature_allow_checks_target_scope_against_chain_cut_scope` passes on current code and kills C4. |
| M3 | low | Rule B's feature cut inside a resource allow (RT4(b), :199-200) is untested. | C5 survives. Probe `M_resource_allow_checks_target_features_against_chain_cut_features` kills it. |
| M4 | low | The chain stop at an appointer who is now a master-admin (`DelegationChain.cs:33`) is untested. Without the stop, an admin whose appointer was later promoted is cut to the master's few persisted grants. | C1 survives. Probe `M_appointer_promoted_to_master_admin_ends_the_chain` kills it. |
| M5 | low | Nothing pins whether a deny row ends the chain. | C11 survives (see P3). |
| M6 | info | The cap value and the non-PostgreSQL EF process lock are untested. The EF branch is used only in development and tests. | C2 and C13 survive. |
| M7 | low | Two new build warnings: `xUnit1031` at `GrantSerializationTests.cs:33` and `:68`, from a blocking `Task.Wait(Settle)` in an async test. Cycle 1 built with 0 warnings. | Full `--no-incremental` build in M2. |

## 4. UNVERIFIED (PostgreSQL only)

**U1. `EfPermissionStore.WithUserLock` PostgreSQL branch** (:123-138), with `AdvancePostgres` running in its transaction (:173). By inspection:
- The advisory lock is transaction-scoped. It is released at commit or rollback, so it is safe with connection pooling.
- An exception disposes the transaction and rolls it back.
- No `EnableRetryOnFailure` is configured (`DependencyInjection.cs:156`), so EF allows user-started transactions.
- Nothing nests `WithUserLock`.
- Lock order is always the advisory lock first, then the `policy_versions` row. The `Advance()` calls outside a grant run in autocommit and take only the row lock. I found no inversion.
- One side effect: the row lock is held from `Advance` until COMMIT. Grant decisions for different users therefore serialize their final steps, and post-commit `Advance()` calls from the membership and role handlers wait for them.

This is already in the Risk table (two-replica soak).

**U2. `AdvanceManyPostgres ORDER BY device_id`.** Correct by inspection.

## 5. Other (low / info)

- **The chain cost is under the attacker's control.**
  - Each grant decision reads up to 32 links: one feature-grant query and one `IsMasterAdmin` query per link. Rule B then reads once per appointer.
  - A role update multiplies this by the number of devices × 2 × the chain length (`DeviceAccessHandlers.cs:123-131` → `DelegationChain.cs:68-75`).
  - On PostgreSQL, these reads run inside the grant transaction.
  - With P2's attacker-built 32-link chain, this is far more than the "vài truy vấn" (a few queries) of Risk row 388. Fixing P2 with a small cap bounds it.
- **Visual lookalike role names** are separate roles that `IsInRole("master-admin")` never matches, so the risk is UI spoofing only. Examples: Cyrillic а (U+0430), a zero-width space, a fullwidth m, U+2011. Probe: `Visual_lookalikes_are_distinct_roles`. Cycle 1's suggestion of a name allowlist is still open.

## 6. Attacks that held

**Reserved role names:**
- Sweep: every single substitution or insertion of each Unicode scalar value at each position of "master-admin" (1,112,064 values × 25 slots). Only U+017F, at the "s", folds onto `MASTER-ADMIN`, and `IsReserved` catches it (probe `Reserved_name_sweep_single_edits`).
- By construction, `IsReserved` applies Identity's own normalizer after `Trim`, so it covers every name Identity would resolve. `IdentityUserManagement` repeats the check with `roles.NormalizeKey`.
- Kelvin- and Angstrom-style singletons do not apply (the name has no K or Å). Fullwidth and zero-width characters do not fold under NFC. A lone surrogate cannot arrive, through either the UTF-8 route path or System.Text.Json.
- The reverse holds too: no legitimate name is wrongly reserved. Only names that normalize to `MASTER-ADMIN` are.
- The seeded role's name `master-admin` (`Program.cs`) matches both the principal's role claim and the normalized name.

**Q15 (subtree rule):**
- It applies to grants and denies (C6 and C7 killed).
- Re-parenting is impossible: `Organization.ParentId` is get-only, and `UpdateOrganizationHandler` only renames (`ManagementContracts.cs:246-253`).
- Organizations created later under X stay inside the grantor's scope, because scope is closed under descendants.
- A nested carve-out blocks both a grant and a deny on the parent.

**Q16:** a non-master's `*` allow or deny gets `WILDCARD_GRANT_FORBIDDEN`. A master's allow `*` adds nothing to `Allowed`, except for the revival shown in P4.

**Q17 (in memory):**
- The feature handler is serialized (C12 and C15 killed).
- An exception releases the Monitor.
- Reentrancy is harmless: nothing nests `WithUserLock` and nothing changes thread inside `decide`.
- The only grant writers outside the lock are the inert device-action handler and test seeds.

**Rule A coverage:**
- Covered: feature and resource grants and denies, membership assign, replace and remove, and role update (C8, C9, C10 killed).
- Role delete cannot touch a role in use (`DEVICE_ROLE_IN_USE`).
- Creating a role with the appointer's name only takes the name.
- Assigning an Identity role to someone in the chain carries no authority: only `master-admin` is checked by name, and it is reserved.
- Removing an orphaned membership of a deleted appointer is blocked (`DeviceAccessHandlers.cs:277-279`). This is stricter than Q13, so it is safe.

**Rule A evasion without help from anyone:**
- Own accounts up to depth 31 → `GRANT_TO_DELEGATOR_FORBIDDEN`.
- A re-grant from one's own appointees is blocked, because the actor is in their chain.
- Self-grant → `SELF_GRANT_FORBIDDEN`.
- Cycles end at the repeat check.
- Legacy rows without a grantor are an accepted Risk row (385).
- Escaping through a third admin's re-grant needs that admin, which was accepted.

**Rule B:**
- Features, organizations and device permissions are cut per appointer (C8 killed). The C4 and C5 paths behave correctly (M_ probes).
- Device permissions held on another device are cut per device.
- A role from another org gets `DEVICE_ROLE_ORGANIZATION_MISMATCH`.
- An appointer later promoted to master-admin ends the chain correctly (M_ probe).

**Command handler:**
- An out-of-scope device and a missing one both return `DEVICE_NOT_FOUND`, before every catalog code (repo test `Out_of_scope_device_command_reads_like_a_missing_device`). The controller has no pre-check of its own.
- A master-admin passes.
- An in-scope revoked device still reaches `DEVICE_REVOKED`.
- The cached result is returned only after every check.
- The idempotency precheck before scope was accepted (no plans exist).

**Unreadable current role:** fails closed for a non-master (`Unreadable_current_role_counts_as_stronger_than_a_non_master`).

**Repo regression run:** 402/402 unit and 103/103 integration with `--no-build`. The binaries (07:55:55) are newer than the last source edit (07:55:51).

## 7. Fix trials (scratch only, each reverted; src re-checked identical)

| Candidate | Change | Probes that flipped | Repo tests |
|---|---|---|---|
| P2 | `CheckTarget`: `chain.Count >= MaxDepth \|\| chain.Contains(target)` (fail closed at the cap) | P2, H2 | 402 / 103 green |
| P3 | `DelegationChain.cs:29` filter `&& !x.Deny` (latest allow) | P3, P3b | 402 / 103 green (tried together with P2) |
| P4 | RT4(b) target features = latest allow rows without `*` | P4 | 402 / 103 green |
| P1 option (a) | Reject a non-master re-grant of `permissions.manage` to a current holder | P1, H1 | 402 / 103 green |

## 8. Commands run

**Repo, read-only:**
- `cd /d/WORKSPACE/PROJECT/HMS-SERVER && dotnet test Hms.Server.slnx --no-build --nologo` → 402/402 and 103/103.
- `find src tests -type f -newer <cycle-1 adversarial report>`: the change set. Also checked the test DLL mtimes against the sources.
- greps:
  - the contract's chain stops and depth;
  - who issues `perm` claims (no code in `src`);
  - direct `GrantFeature`/`GrantResource` writers (only the handlers);
  - `EnableRetryOnFailure` (none);
  - `IsReserved` callers;
  - existing tests for lookalike names, Q15, Q16 and Q18.
- `diff -rq --exclude=bin --exclude=obj src S2/src` → identical, apart from the build-output directories.

**Scratch:**
- `tar --exclude='*/bin' --exclude='*/obj' ... | tar -x` into S2 and into M2.
- `dotnet build Hms.Server.slnx` → 0 errors, 2 warnings (`xUnit1031`). Also with `--no-incremental` in M2.
- `dotnet test ... --filter AdversarialCycle2Probes` → 11 passed. `--filter AdversarialCycle2HttpProbes` → 2 passed. Full S2 run → 413 unit (402 + 11) and 105 integration (103 + 2), all green.
- `python3 M2/mutate.py` → 14 mutants on the new code.
  - Killed: C6, C7, C8, C9, C10, C12, C15.
  - Survived: C1, C2, C4, C5, C11, C13, C14.
  - Re-running C1, C4 and C5 with the M_ probes added: each is killed by its probe.
- Fix trials in S2 (§7). Each was reverted by copying the repo file back.

**Probe files** (scratch only, nothing in the repo):
- `S2/tests/Hms.Server.UnitTests/AdversarialCycle2Probes.cs`: P1, P1b, P2, P3, P3b, P4, three M_ probes, the reserved-name sweep, and the lookalike check.
- `S2/tests/Hms.Server.IntegrationTests/AdversarialCycle2HttpProbes.cs`: H1, H2.
- `M2/mutate.py`.

Status: DONE_WITH_CONCERNS
Summary: The cycle-1 fixes hold, including an exhaustive reserved-name sweep, the Q15 subtree rule, Q16 wildcard writes, the scope-first command handler and the feature-handler lock. But Rule A of the new delegation chain can be broken without cooperation, both proven over HTTP: one redundant grant lets a peer capture an admin (P1), and the 32-link cap lets 32 own accounts act on the appointer (P2). There are also two low contract deviations (P3, P4) and five untested rules, the resource-handler lock among them.
Concerns/Blockers: P2 needs the fail-closed cap fix, validated in scratch. P1 needs an owner decision on the Q18 appointer definition, with options (a)-(c). P3 and P4 are one-line fixes, validated in scratch. Add tests for C14, C4, C5, C1 and C11 and remove the 2 new warnings before closing phase 3.
