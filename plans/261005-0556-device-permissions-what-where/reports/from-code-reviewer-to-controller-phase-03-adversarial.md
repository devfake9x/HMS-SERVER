---
title: "Phase 3 adversarial review: access evaluation and delegation"
date: 2026-10-06
reviewer: code-reviewer (adversarial)
scope: phase-03 Success Criteria (lines 336-352) and Security Considerations (lines 374-382); the changed files listed by the controller, plus their callers (UsersController, IdentityUserManagement, RoleAuthorizationPolicy, DevicesController, hub)
decision: BLOCKED
---

# Phase 3 adversarial review

`S/` = `C:/Users/toann/AppData/Local/Temp/claude/D--WORKSPACE-PROJECT-HMS-SERVER/daebfbef-46ca-4e9f-b506-cf6e26aef926/scratchpad/phase03-adversarial/`. This is a tar copy of `Hms.Server.slnx`, `src` and `tests` without `bin` and `obj`. All builds, probes and the one fix check ran there. In the repository I only ran `dotnet test Hms.Server.slnx --no-build --nologo`.

## Summary

Most of the phase-3 logic holds under attack: the policy order, the resolver, the delegation subset and high-risk rules, the membership compare-and-set, latest-wins resolution, the wildcard handling, write ordering and best-effort signals. One attack gives full privilege escalation, and it disproves a security claim this phase relies on.

1. A non-master who holds only `users.manage` can give the reserved `master-admin` role to themselves. The trick is to spell the role name with U+017F LATIN SMALL LETTER LONG S: `PUT /api/v1/users/{self}/roles/ma%C5%BFter-admin`.
   - Proven over HTTP with persisted grants only (no test claims): 204, and the user's next principal has `IsInRole("master-admin") == true`.
   - The root cause is older than phase 3. But phase 3 makes master-admin unconditional, so a deny can no longer contain the attacker. Phase 3 also has no in-product way to remove the role.
   - A fix of one line closes it and keeps every repo test green (checked in `S/`).
2. The grant handlers check and then write without making the two steps atomic. Two grantors with complementary powers, sending concurrent requests, can combine a scope and a feature that neither of them may grant (RT4). A master deny that lands inside the window is reversed (Q12). Neither race is in the accepted Risk table.
3. Smaller items:
   - a design gap: `permissions.manage` × membership;
   - the command endpoint tells an existing foreign device apart from a missing one (phase 4's gate closes this);
   - a fail-open default for a current role that cannot be read (not reachable today);
   - N+1 queries in role update;
   - Postgres `AdvanceMany` lock ordering (not verified);
   - one regression-lock test whose meaning changed.

## Verdict (JSON-ready)

```json
{
  "decision": "BLOCKED",
  "blockingReason": "Reachable escalation of a non-master holding users.manage to master-admin over HTTP. It disproves Security Considerations line 377, and phase 3 makes the result unconditional and, in phase 3, irremovable in-product.",
  "disprovenClaims": [
    {"claim": "SecCons L377: 'Gán role master-admin vẫn chỉ master-admin làm được (RoleAuthorizationPolicy) → non-master không có đường tự nâng lên bypass'", "severity": "critical"},
    {"claim": "SecCons L380 / SC L349: grants cannot exceed the grantor's org scope or features through the target [RT4]; a non-master cannot reverse a master deny [RT13/Q12]. Both fail under concurrency.", "severity": "medium (RT4), low (Q12)"},
    {"claim": "SecCons L380: delegation is limited to a subset of the actor's own membership. Bypassed through permissions.manage × a stronger member (design gap).", "severity": "low"},
    {"claim": "SecCons L378: out of scope and missing both give DEVICE_NOT_FOUND 'trong policy/handler'. Holds for DeviceAccessPolicy and the delegation handlers, not for SendDeviceCommandHandler.", "severity": "low (phase 4 gate closes it)"}
  ],
  "reachableRegressions": [],
  "missingProof": 6,
  "unverified": 1
}
```

## 1. DISPROVEN CLAIMS

### D1 (critical). A non-master with `users.manage` becomes master-admin by spelling the role with U+017F

**Claim.** Phase-03 line 377: only a master-admin can assign `master-admin`, so a non-master has no path to the bypass.

**Path.**
- `RoleAuthorizationPolicy.IsReserved` (`src/Hms.Server.Application/Authorization/RoleAuthorizationPolicy.cs:8-9`) compares with `StringComparison.OrdinalIgnoreCase`. .NET ordinal casing deliberately does not fold U+017F, so `"maſter-admin"` is not reserved.
- `UsersController.AssignRole` (`src/Hms.Server.Api/Controllers/UsersController.cs:76-93`) needs only `users.manage` and a target in scope. There is no self check, so the caller may target themselves. It then calls `IdentityUserManagement.AssignRoleAsync` (`src/Hms.Server.Infrastructure/Identity/IdentityUserManagement.cs:80-96`).
- That method repeats the same `IsReserved` check (line 86), then calls `roles.RoleExistsAsync(role)` (line 91) and `users.AddToRoleAsync(user, role)` (line 92).
- Both Identity calls look the role up through `UpperInvariantLookupNormalizer` (`Normalize().ToUpperInvariant()`), which maps `ſ` to `S`. They therefore find the real `MASTER-ADMIN` row and insert the user into it.
- The next principal carries the role claim `master-admin` (the role's `Name`). `AccessActorFactory` (`AccessActorFactory.cs:44-45`), `ScopeAuthorization` and the policy handler then treat the caller as an unconditional master-admin, and so does `IUserScopeStore.IsMasterAdmin` (lines 58-64).

**Evidence** (probes pass on current code):
- `S/tests/Hms.Server.IntegrationTests/AdversarialPersistedProbeTests.cs` `Org_user_admin_with_persisted_grants_self_promotes_to_master_admin`.
  - Setup: the attacker's only authority is a persisted `users.manage` and a resource scope Child, both issued by a master.
  - Result: `PUT .../roles/maſter-admin` returns 204, and `principalFactory.CreateAsync(attacker).IsInRole("master-admin") == true`.
- `S/tests/Hms.Server.IntegrationTests/AdversarialProbeTests.cs` `Non_master_with_users_manage_self_assigns_master_admin_via_long_s`. Output: `PUT long-s role -> 204; IsInRoleAsync(master-admin)=True; next principal IsInRole(master-admin)=True; IUserScopeStore.IsMasterAdmin=True`.
- `Runtime_casing_facts`: `"maſter-admin".Normalize().ToUpperInvariant() == "MASTER-ADMIN"`, while `OrdinalIgnoreCase` gives `False`.
- `Dotless_i_variant_is_not_resolved_to_master_admin`: U+0131 is not a bypass, because `ToUpperInvariant` special-cases it. The request gets 400 `ROLE_NOT_FOUND`.

**Impact.**
- Any org-level user admin becomes a global, deny-immune master-admin. As master-admin they can grant anything, create and assign high-risk roles, and stamp their own denies as master-issued (Q12).
- In phase 3 there is no endpoint to remove the role. Q11's `DELETE /users/{id}/roles/{role}` comes in phase 4. The escalation is therefore permanent short of a database edit.
- Before phase 3, a deny still restricted a master-admin. Phase 3 removes that containment (Q4, accepted).
- The same root cause hijacks bootstrap. If the real role does not exist yet, `POST /users/roles {"name":"maſter-admin"}` creates a role whose normalized name is `MASTER-ADMIN`. A later `RoleExistsAsync("master-admin")` then returns true, and `AddToRoleAsync(admin, "master-admin")` puts the bootstrap admin into the look-alike role, whose claim is not `master-admin`.

**Fix (checked).**
- Compare in the normalizer's own space, in `S/` only: `string.Equals(role?.Trim().Normalize().ToUpperInvariant(), MasterAdminRole.ToUpperInvariant(), StringComparison.Ordinal)`.
- With that change both escalation probes get 403. All 372 unit and 94 integration repo tests stay green.
- Better still:
  - resolve the role entity first and refuse a non-master when `role.NormalizedName == NormalizeKey(MasterAdminRole)`;
  - make `CreateRoleAsync` accept only `^[a-z0-9][a-z0-9._-]*$`;
  - add the U+017F case to the tests for assign, create and phase 4's remove.
- `RemoveRoleAsync` in phase 4 reuses `IsReserved` as defense in depth, so it needs the same fix.

### D2 (medium). The RT4 rule is defeated when two grants run concurrently

**Claim.** Phase-03 lines 349 and 380: no grant can push a feature beyond the grantor's org scope, or widen scope under features the grantor lacks.

**Path.**
- `GrantFeaturePermissionHandler.Handle` (`PermissionContracts.cs:102-112`) runs `Check` (lines 114-138, which reads the target's scope at line 130), then `Advance()` and append (lines 108-110).
- `GrantResourceScopeHandler` does the same (lines 143-174).
- No lock or transaction spans the check and the append. `InMemoryPermissionStore` locks each call separately, and the EF store autocommits.

**Evidence.**
- `S/tests/Hms.Server.UnitTests/AdversarialProbeTests.cs` `Concurrent_feature_and_scope_grants_combine_beyond_both_grantors`.
- A wrapper `IPermissionStore` runs the competing grant inside `Advance()`, that is, after the checks and before the append.
- The race:
  - a Child admin holding `devices.manage` grants T `devices.manage`;
  - at the same time, a Parent admin without `devices.manage` grants T the Parent scope;
  - both return OK, and T ends up with `devices.manage` over Parent.
- In either sequential order, `Feature_grant_rejected_when_target_scope_exceeds_grantor_scope` and `Resource_grant_rejected_when_target_holds_features_grantor_lacks` reject the second grant.

**Reachability.** It needs two admin accounts with complementary powers, either colluding or held by one person, and concurrent requests. Retries make it reliable, because each check costs several database round trips. It is not in the phase-03 Risk table, which accepts only the role-update-versus-assign race and the `expectedRoleVersion` window.

**Fix.** Make the check and the append atomic per target:
- Postgres: one transaction with `pg_advisory_xact_lock(<target uuid hash>)`, then re-read and check before inserting;
- InMemory: a store method that runs the check under `_gate`.

### D3 (low). A master deny that lands between a non-master's check and write is reversed (Q12)

**Same path as D2.** `MasterDecisions.DenyStands` is read at line 135, before `Advance()` and append.

**Evidence.**
- `AdversarialProbeTests.Master_deny_landing_between_check_and_write_is_reversed_by_non_master_allow`.
- The master-issued deny row exists, yet the non-master allow (written later, with a higher version) is the latest row, and `devices.manage` is allowed.

**Reachability.** The window is a few milliseconds. A non-master who repeats the allow request can catch it. The same fix as D2 closes it.

### D4 (low; design gap, owner decision). A non-master can turn a stronger member into a delegator

**Claim.** Line 380 (and Q4): a non-master delegates only a subset of their own permissions on device D.

**Evidence.**
- `AdversarialProbeTests.Permissions_manage_grant_lets_grantor_obtain_device_permissions_through_target`.
- Setup:
  - admin A holds `permissions.manage` over Child and the role `{device.view}` on D;
  - member T holds `{device.view, adc.config_edit}` on D and the scope Child, both from the master.
- Steps:
  1. A grants T `permissions.manage`. RT4 passes, because T's scope is within A's.
  2. T then assigns A the strong role. Every rule passes.
  3. A now has `adc.config_edit` on D.
- The step needs T's cooperation. But T can now spread those permissions to anyone in scope, which the master who gave T the role may not have intended.

**Options.**
- (a) Accept, and record it in the Risk table.
- (b) RT4 analogue: a non-master may grant `permissions.manage`, or widen the scope of a holder of `permissions.manage`, only if, on every device in the target's resulting scope, the target's membership role is within the actor's own role on that device.
- (c) Only a master-admin may grant `permissions.manage`.

### D5 (low; phase 4 closes it). The command handler tells a foreign existing device from a missing one

**Claim.** Line 378: out of scope and missing both give `DEVICE_NOT_FOUND` in the policy and handler.

**Path.** In `SendDeviceCommandHandler.cs`, a missing device returns at lines 46-50. Every catalog-stage code comes before the scope decision at lines 90-94: `CATALOG_STALE`, `COMMAND_NOT_IN_CATALOG`, `DENIED_UNTIL_ACTION_CONTRACT` and `PERMISSION_UNMAPPED` (lines 52-88).

**Evidence.**
- `S/tests/Hms.Server.IntegrationTests/AdversarialProbeTests.cs` `Command_endpoint_distinguishes_foreign_device_from_missing_device`.
- A caller with no scope gets `200 {"code":"CATALOG_STALE"}` for the other org's device, and `200 {"code":"DEVICE_NOT_FOUND"}` for a random id.

**Status.** Contract §5/§6 has phase 4 resolve the device and its scope before the handler, which closes this. That is acceptable only because phase 3 is not released on its own. The claim does hold for:
- `DeviceAccessPolicy`;
- the two membership handlers, where `MembershipRules.CheckDevice` runs first;
- the three role handlers, where `RoleRules.CheckOrganization` runs first and both a missing and an out-of-scope org give `ORGANIZATION_NOT_FOUND`.

## 2. REACHABLE REGRESSIONS

None found. Every behaviour change I traced is intended or listed as accepted:
- latest-wins;
- allow `*` is gone;
- master-admin is unconditional;
- the home org no longer gives command scope;
- device-action grants are inert;
- the global `PolicyVersion` advances on membership and role writes.

## 3. MISSING PROOF

Each behaviour below is correct but no repo test pins it, except M1, which is a test whose meaning changed.

- **M1 (low). A regression-lock test now proves nothing.**
  - `Device_command_uses_persisted_user_organization_when_org_claim_is_absent` (`ManagementApiTests.cs:436-465`) is named after home-org command scope, which phase 3 removed on purpose.
  - It passes only because `COMMAND_NOT_IN_CATALOG` is returned before any scope check. An out-of-scope caller gets a stage code too (D5 probe).
  - Its `ResourceScopedUserId` also shares state with `Persisted_resource_grants_are_used_for_scope_filtering_and_denies_win`, which writes a persisted deny on Child. That deny overrides the test's `org-scope` claim, so the result depends on test order, and the stage-code ordering hides this.
  - Fix: rename the test and assert that the home org grants no scope. Phase 4 can then assert the 404.
- **M2. `CONFIRM_REQUIRED` before a cached result.** A retry that drops `Confirm` must not get the confirmed first result. My probe `S/tests/Hms.Server.UnitTests/AdversarialCommandProbeTests.cs` `Retry_without_confirm_does_not_return_the_cached_confirmed_result` passes; no repo test covers this.
- **M3. Signal failures.**
  - A `PolicyVersion` signal failure still advances the epoch (contract §4 "each is wrapped separately"); probe `Policy_version_signal_failure_still_advances_the_device_epoch` passes.
  - The role-update signal-failure path returns OK; probe `Role_update_signal_failure_returns_ok` passes.
  - The repo test covers only `ThrowOnAdvance` on a membership replace.
- **M4. DI on the EF path.** The Success Criteria say the resolver and the 5 handlers resolve from DI. `PersistenceModelTests` proves this only for the in-memory development registration. My probe `AdversarialRegistrationProbeTests` resolves them, plus the grant handlers and `SendDeviceCommandHandler`, with a Postgres connection string and `ValidateScopes = true`, without opening a connection. It passes.
- **M5. Store-level conflicts on update.**
  - The `DEVICE_ROLE_VERSION_STALE` and `DEVICE_ROLE_NAME_TAKEN` mapping on role *update* when the store, not the pre-check, reports the conflict.
  - Only the update pre-check and the create path are tested.
- **M6. Unicode reserved-role name.** No test covers it; needed after the D1 fix.

## 4. UNVERIFIED (PostgreSQL only)

**U1. `EfAuthorizationEpochStore.AdvanceManyPostgres`** (`EfAuthorizationEpochStore.cs:100-113`).
- By inspection the SQL is right:
  - `unnest(@device_ids)` with a `Guid[]` value infers `uuid[]`;
  - `ON CONFLICT` cannot hit the same row twice, because ids are de-duplicated (line 43).
- `ids` comes unsorted from `ListByRole`. Two concurrent role updates that share devices can therefore deadlock (40P01). `PostCommitSignals` swallows the error, so one update's epochs never move, and phase 5 would miss `AUTHORIZATION_CHANGED` for those subscribers.
- Sort `ids` before the statement.
- No test runs this path; `PersistenceModelTests` uses EF InMemory.

## 5. Other findings (non-blocking)

- **Low hardening (fail-open default).** `DeviceAccessHandlers.cs:211` and `:269` pass `roles.Find(current.RoleId)?.Permissions`. When the current role cannot be read, `DelegationRules.CheckRoleChange` treats it as "no current role", which skips the high-risk and subset rules.
  - Probe `Unreadable_current_role_skips_the_current_role_rule`: a non-master replaces a `sys.reboot` membership and gets OK.
  - It is not reachable today: FK Restrict and the in-use check under the store lock prevent it.
  - For a non-master, a current role that cannot be read should fail closed, with `DEVICE_ROLE_NOT_FOUND` or treated as maximal.
- **Low/medium performance (N+1).** The non-master loop in `UpdateDeviceRoleHandler` (`DeviceAccessHandlers.cs:114-122`) makes 3 database round trips per device that uses the role: `devices.Find`, membership `Find` and role `Find`.
  - Load `ListByUser(actor.UserId)` and its roles once instead. The device's org equals the role's org by construction.
- **Low.** Feature codes are neither trimmed nor validated.
  - `PermissionGrantsController.cs:49` checks only `IsNullOrWhiteSpace`, and `PermissionContracts.cs:117` matches `"*"` exactly.
  - So a non-master's deny `" *"` or `"*\t"` returns 200 and writes an inert row (probe `Non_master_whitespace_wildcard_deny_is_accepted_as_an_inert_key`). This is not an escalation, because resolution is also exact.
- **Informational.** `InMemoryCommandAuditStore` (`CommandContracts.cs:211-212`, `Records` at line 232) keeps the whole `Plan.Request` in memory, `Confirm` included (asserted in M2's probe). The EF store does not persist it, and nothing logs it, so line 381 holds for the production store.
- **Informational (contract-mandated codes).**
  - `DEVICE_ROLE_ORGANIZATION_MISMATCH` versus `DEVICE_ROLE_NOT_FOUND` reveals whether a foreign role id exists.
  - `GRANT_TARGET_IS_MASTER_ADMIN` lets a holder of `permissions.manage` find which in-scope users are master-admins, by sending a deny on a junk key.
  - Both are of low value: ids are random GUIDs, and `/users` already shows roles to holders of `users.manage`.
- **Note for the controller.** The repo's test binaries (05:54:13) are older than the edits to `ScopeAuthorization.cs`, `AccessActor.cs` and `FeaturePermissionAuthorizationHandler.cs` (05:55-05:56). The `--no-build` run therefore tested slightly stale binaries. A fresh build of the current sources in `S/` is green.

## 6. Attacks that held

- Self-grant of a feature, a resource or a membership by a non-master → `SELF_GRANT_FORBIDDEN`. A master may self-grant (Q4).
- Laundering through a second account (A grants an alt account, which grants back) stays within A's own features, scope and device role.
- Sequential RT4: a feature allow to a target with wider scope, or a resource allow to a target with stronger features → `GRANT_EXCEEDS_OWN_PERMISSIONS` in either order.
- Deny trick to slip past RT4 (deny the target's feature, widen its scope, re-allow) → the re-allow is still blocked by RT4.
- Master deny laundering:
  - "own deny first, then allow" → `GRANT_OVERRIDES_MASTER_DECISION`;
  - an interleaved master allow and deny uses the latest master-issued row;
  - equal versions → deny wins.
- Master deny `*`:
  - a non-master allow on a single key has no effect, because `Denied ∋ "*"` clears `Allowed`;
  - a non-master cannot write the key `*` at all.
- Wildcard variants (`" *"`, `"*\t"`, `USERS.MANAGE`, `*` inside a longer string) are inert both when granted and when resolved (exact, ordinal).
- Role create or update with a high-risk code, `*`, a case variant or an unknown code → `HIGH_RISK_REQUIRES_MASTER_ADMIN` / `DEVICE_PERMISSION_UNKNOWN`. Non-master checks cover the old and the new set even when the role is assigned nowhere.
- Growing or shrinking a role used where the actor is weaker → `GRANT_EXCEEDS_OWN_PERMISSIONS`. An actor cannot grow their own role, because the role on that device is both the old set and their own.
- Assign a role from another org → `DEVICE_ROLE_ORGANIZATION_MISMATCH`.
- Device out of scope or missing → `DEVICE_NOT_FOUND`.
- Stale role version → `DEVICE_ROLE_VERSION_STALE`, master included.
- Stale `expectedRoleId` or a store compare-and-set conflict → `DEVICE_MEMBERSHIP_CHANGED`, with no signal sent.
- Replace or remove a stronger or high-risk current role, foreign members included (Q13) → blocked. Replacing a foreign member's role → `USER_NOT_FOUND`.
- `devices.manage` alone → `PERMISSION_MANAGEMENT_REQUIRED`.
- A scope admin without a membership cannot delegate (V6).
- A membership opens exactly one device.
- The resolver ignores a role from another org, and a missing role gives no scope.
- Policy order:
  - out of scope (revoked included) → `DEVICE_NOT_FOUND` before `DEVICE_REVOKED`;
  - revocation stops a master-admin too.
- A missing or non-Guid NameIdentifier gives a null actor → `Forbid()`.
- Principal `IsInRole` and the database `NormalizedName` agree for every spelling except U+017F (D1).
  - U+0131 is not a bypass.
  - A stale cookie only matters once phase 4's removal endpoint exists.
- Commands:
  - `PERMISSION_UNMAPPED` for a domain that is missing, mis-cased, numeric or unknown;
  - `CONFIRM_REQUIRED` comes before the cached result;
  - `Confirm` is not in the fingerprint, the EF audit or any log;
  - TTL defaults to 120000.
- Write ordering:
  - store, then `PolicyVersion`, then epoch;
  - a fresh assign moves no epoch;
  - a role update makes one `AdvanceMany` call;
  - when a signal fails, the handler still returns OK and the other signal still runs.
- `AdvanceMany` with duplicate ids advances each device once (InMemory and EF InMemory).
- Widening scope by re-parenting an org or changing a device's owner is not possible: the update handlers only rename.
- Device-action grants written through the legacy `DevicePermissionsController` and `POST /permissions/device-actions` authorize nothing in phase 3. They are read only by `MeController`, the snapshot and the legacy matrix (phase 4 deletes these).
- Success-criterion greps pass:
  - SC1 grep finds 0 results;
  - SC2 finds `"*"` only in `PermissionGrantResolver` and the grant handler;
  - latest-wins for feature and resource grants is implemented only at `PermissionGrantResolver.cs:92-100`. The legacy device-action matrix has its own copy, which phase 4 deletes.
- Accepted and not counted: the role-update-versus-assign race (Risk table). Note that one admin can trigger it on purpose by sending the two requests concurrently, whereas the table's reason ("quy mô nhỏ", small scale) assumes an accident. High-risk codes stay out of reach.

## 7. Commands run

Repo, read-only:
- `cd /d/WORKSPACE/PROJECT/HMS-SERVER && dotnet test Hms.Server.slnx --no-build --nologo` → 372/372 unit, 94/94 integration.
- The success-criterion greps: SC1, the `"*"` grep and the latest-wins grep.
- An mtime check of the test DLLs against the sources.

Scratch `S/`:
- `tar --exclude='*/bin' --exclude='*/obj' --exclude='*/.tmp-unit-out' ... | tar -x` into `S/`.
- `dotnet build Hms.Server.slnx --nologo -v q` → 0 warnings, 0 errors.
- `dotnet test tests/Hms.Server.IntegrationTests/... --filter AdversarialProbeTests|AdversarialPersistedProbeTests|AdversarialRegistrationProbeTests` → 6 passed, each confirming its finding.
- `dotnet test tests/Hms.Server.UnitTests/... --filter AdversarialProbeTests|AdversarialCommandProbeTests` → 8 passed.
- Full `dotnet test Hms.Server.slnx --no-build` → 380/380 unit (372 + 8 probes), 100/100 integration (94 + 6 probes).
- D1 fix check: I changed `IsReserved` to compare `Trim().Normalize().ToUpperInvariant()` ordinally.
  - Result: the two escalation probes get 403; `Runtime_casing_facts` flips as expected; all repo tests stay green.
  - Then I reverted it.

Probe files (scratch only, nothing in the repo):
- `S/tests/Hms.Server.IntegrationTests/AdversarialProbeTests.cs`
- `S/tests/Hms.Server.IntegrationTests/AdversarialPersistedProbeTests.cs`
- `S/tests/Hms.Server.UnitTests/AdversarialProbeTests.cs`
- `S/tests/Hms.Server.UnitTests/AdversarialCommandProbeTests.cs`

Status: DONE_WITH_CONCERNS
Summary: Phase 3's access evaluation and delegation hold against direct attack. But a non-master holding users.manage can give themselves the master-admin role through the U+017F spelling "maſter-admin", which disproves Security Considerations line 377, and the grant handlers' check-then-write can be raced past the RT4 and Q12 rules.
Concerns/Blockers: D1 must be fixed before release; the fix is one line plus tests, and it was checked in scratch. D2 and D3 need the check and the append to be atomic per target. D4 needs an owner decision.
