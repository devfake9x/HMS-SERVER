# Phase 3 review: access evaluation and delegation

Reviewer: code-reviewer. Date: 2026-10-06. Code paths are relative to `D:/WORKSPACE/PROJECT/HMS-SERVER`. `S/` means `C:/Users/toann/AppData/Local/Temp/claude/D--WORKSPACE-PROJECT-HMS-SERVER/daebfbef-46ca-4e9f-b506-cf6e26aef926/scratchpad/phase03-review/`.

## 1. Summary

**PASS WITH CONCERNS, 7/10.** The phase-3 code scores 8/10 on its own. I took off one point for C-1, a pre-existing privilege escalation in the blast radius. C-1 breaks the security premise this phase relies on and must be fixed before any release. Fix H-1 before closing phase 3. M-1 needs an owner decision. The Low items can follow.

The implementation follows the design contract closely:

- The evaluation order is correct.
- Latest-wins with "deny wins on a tie" is implemented exactly once.
- The wildcard is neutralised at every layer.
- master-admin bypasses every deny, and only `ResolvedFeaturePermissions.Has` and `AccessActorFactory.EffectiveOrganizations` implement that rule.
- All 19 delegation/grant codes, the compare-and-set paths, the Q13 removal rule and the best-effort post-commit write ordering are in place and tested.
- The tests are strong: 21 of 22 targeted mutants are killed (§2).

**Findings**

| Severity | Count | Items |
|---|---|---|
| Critical | 1 | **C-1** (pre-existing, outside the changed files, release blocker): a non-master with `users.manage` can grant the real `master-admin` role, including to themselves, through the lookalike name `"maſter-admin"` (U+017F). Confirmed over HTTP. Since phase 3 makes master-admin unconditional, this is now a path to unrestricted power. |
| High | 1 | **H-1**: a resource allow lets a non-master push a target into a sub-org carved out of the grantor's own scope. This is a residual hole in RT4, confirmed over HTTP. The fix is about 3 lines, and I validated it against the full suite in `S/`. |
| Medium | 1 | **M-1** (contract gap): a deny `*` can never be lifted. |
| Low | 6 | L-1 to L-6 |
| Info | 7 | I-1 to I-7 |

## 2. What I verified

The repo was not modified except for this report and my agent-memory note. All builds, probes and mutations ran in `S/`.

- **Scope.** There is no git repository, so I listed `.cs` files newer than `reports/pm-261005-2336-phase-02-sync.md`. The result is exactly the 29 files named in the brief. The only non-`.cs` change is `Hms.Server.Application.csproj`, which adds `Microsoft.Extensions.Logging.Abstractions` 10.0.10, aligned with the other 10.0.10 packages. No `frontend/src` file changed. `frontend/dist` was rebuilt at 06:03:55, which suggests the controller ran the frontend gate.
- **Build.** The scratch copy (`Hms.Server.slnx`, `src`, `tests`, without bin/obj) built with **0 warnings, 0 errors**, including a full `--no-incremental` rebuild.
- **Tests.**
  - In the repo with `--no-build`: unit **372/372**, integration **94/94**.
  - The scratch copy gives the same counts.
- **Probes.** 9 scratch-only tests, all passing on the current code, each asserting the current behaviour:
  - `S/tests/Hms.Server.UnitTests/ReviewProbes.cs`: P1 to P6.
  - `S/tests/Hms.Server.IntegrationTests/ReviewIntegrationProbes.cs`: P1 over HTTP.
  - `S/tests/Hms.Server.IntegrationTests/ReviewDiProbes.cs`: the EF DI branch, with scope validation on.
  - `S/tests/Hms.Server.IntegrationTests/ReviewRoleProbes.cs`: C-1.
- **Freshness.** Before writing this report I compared the repo `src`/`tests` (`.cs`, `.csproj`, `.json`) with the scratch copy: 0 differences.
- **Mutation.** 22 mutants on the phase-3 rules (`S/mutate.py`, results in `S/mutation-results.json`): 21 killed, 1 survived (m22, see I-1).

| # | Mutant | Killed by (first tests) |
|---|---|---|
| m01 | tie prefers allow | `Latest_grant_with_equal_policy_version_prefers_deny` |
| m02 | `Has` ignores master-admin | `Master_admin_ignores_feature_and_wildcard_denies`, `Master_admin_with_persisted_users_deny_still_creates_roles` |
| m03 | allow `*` kept | `Wildcard_feature_allow_is_ignored`, `Feature_wildcard_allow_grants_nothing` |
| m04 | master-admin checked before revoked | `Revoked_device_is_denied_before_master_admin_bypass` |
| m05 | membership opens no scope | 4 member tests; also **hangs** the suite (L-5) |
| m06 | foreign-org role kept | `Resolver_ignores_role_from_another_organization` |
| m07 | V4 current role ignored | 4 V4/RT2 tests |
| m08 | role-update device loop skipped | `Role_update_must_stay_within_admin_permissions_on_every_assigned_device` |
| m09 | Q12 compared with the latest row overall | `Non_master_cannot_reverse_master_admin_feature_deny` (laundering branch) |
| m10 | fresh assign advances the epoch | `Fresh_assign_does_not_advance_device_epoch` |
| m11 | remove checks the target org (breaks Q13) | `Org_admin_can_remove_orgless_member`, `..._orphaned_...` |
| m12 | non-master may deny `*` | unit + integration |
| m13 | master-target check after the master early return | unit + integration |
| m14 | RT4(b) dropped | `Resource_grant_rejected_when_target_holds_features_grantor_lacks` |
| m15 | RT4(a) dropped | unit + integration |
| m16 | factory subtracts denies for a master-admin | 2 factory tests + 2 RTU integration tests |
| m17 | per-device `Advance` instead of `AdvanceMany` | `Role_update_advances_all_affected_devices_in_one_call` |
| m18 | no `expectedRoleVersion` check | `Assign_with_stale_role_version_is_rejected` |
| m19 | confirm check removed | `Confirm_required_rows_reject_blank_confirm` |
| m20 | EF `AdvanceMany` +2 | `Authorization_epoch_advance_many_increments_each_device_once` |
| m21 | `IsMasterAdmin` always false | `Deny_on_master_admin_target_is_rejected_over_http` |
| m22 | epoch skipped when `Advance()` throws | **survived** (I-1, accepted test constraint) |

## 3. (a) Success criteria, phase-03 lines 336–352

| # | Criterion | Status | Code | Tests |
|---|---|---|---|---|
| 1 | Removed symbols/codes grep = 0 | **Met** | grep over `src tests`: 0 hits | — |
| 2 | Allow `*` grants nothing; listed files never mention `*` | **Met** | `PermissionGrantResolver.cs:45-47`, `PermissionContracts.cs:117`. `"*"` remains only in `MeController`/`DevicePermissionsController` (phase 4). | `Feature_wildcard_allow_grants_nothing` (PermissionTests:169), `Feature_policy_handler_ignores_wildcard_allow` (:293), `Wildcard_feature_allow_is_ignored` (AccessActorFactoryTests:114), `Wildcard_in_role_permissions_grants_nothing` (DeviceAccessPolicyTests:92), `Wildcard_feature_claim_grants_no_management_access` (ManagementApiTests:775) |
| 3 | master-admin is unconditional, and the rule lives only in `Has` + `EffectiveOrganizations` | **Met** | `PermissionGrantResolver.cs:11-15`, `AccessActorFactory.cs:37-38`. No other claim/grant merging exists. | ManagementApiTests:677, :690; PermissionTests:181, :317; AccessActorFactoryTests:55. Mutants m02 and m16 killed. |
| 4 | Factory: invalid id → null; master-admin gets all orgs despite denies; descendants minus denies; latest-wins; no `*`; `ScopeAuthorization` only delegates | **Met** | `AccessActorFactory.cs:16-42`, `ScopeAuthorization.cs:14-25` | 7 tests in AccessActorFactoryTests |
| 5 | `LatestFeatureGrants`/`LatestResourceGrants` are the only latest-wins code; selection by version | **Met** | `PermissionGrantResolver.cs:29-33, 92-100`; it is the only `GroupBy` in `src` | PermissionTests:215, :237, :258, :277 |
| 6 | Membership-only member works on that device only; scope admin gets `device.view` only | **Met** | `DeviceAccessResolver.cs:20-36` | DeviceAccessPolicyTests:46, :62, :72 |
| 7 | `adc.view` role: 5× `get_status` and `mqtt.get_config` → `DEVICE_PERMISSION_REQUIRED`; `adc.get_status` recorded | **Met** | `SendDeviceCommandHandler.cs:84-94` | DeviceCommandAuthorizationTests:40, :53, :64 |
| 8 | 3 confirm rows; 24 rows with TTL 60000; null TTL → 120 s | **Met** | `SendDeviceCommandHandler.cs:96-106` | DeviceCommandAuthorizationTests:133, :155, :164, :184 |
| 9 | Delegation matrix covers every code, plus NAME_TAKEN from the store, stale before in-use, store compare-and-set → CHANGED, `device.view`/`sys.config_import` accepted | **Met** | `DeviceAccessHandlers.cs:39-285` | DeviceAccessHandlerTests (all 19 codes). Probe P4 confirms that a store-level update conflict maps to `DEVICE_ROLE_VERSION_STALE` with no signal. |
| 10 | Write ordering; epoch moves only where access can shrink; a signal failure still returns OK | **Met** | `DeviceAccessHandlers.cs:63-67, 128-135, 228-236, 276-282, 396-411` | DeviceAccessHandlerTests:323, :356, :369, :578 |
| 11 | RT3 for master-admin too; RT2 no shrink; RT14 remove foreign/orgless/orphaned but not replace | **Met** | `:206-207`, `:110-123`, `:257-263` | DeviceAccessHandlerTests:231, :560, :253-318 |
| 12 | Grant rules (wildcard, master target, self, own feature, own scope through the recipient [RT4], Q12 incl. laundering, grantor stamped, 400/403 `{code}`) | **Partial** | `PermissionContracts.cs:97-187`, `PermissionGrantsController.cs:107-112` | PermissionTests:333-514, ManagementApiTests:707-840. **H-1:** the resource-allow path can still push a recipient beyond the grantor's org scope. |
| 13 | `Wildcard_feature_claim_grants_no_management_access` uses an in-scope owner org | **Met** | — | ManagementApiTests:775-791 |
| 14 | Resolver + 5 handlers resolve from DI (scoped) | **Met** | `DependencyInjection.cs:94-100` | PersistenceModelTests:1254. The EF branch is also verified by my probe (I-3). |
| 15 | `dotnet test` green; frontend gate green | **Met / not run by me** | 372/94. The frontend gate would write `frontend/dist` in the repo, so I did not run it. No frontend source changed. | — |

## 4. (b) Regression walk: every caller of changed members

| Changed member | Callers / implementers | Result |
|---|---|---|
| `ScopeAuthorization` (4 methods) | Areas, Devices, DevicePermissions, Licenses, Organizations, Packages, PermissionGrants, Projects, Provisioning and Users controllers; `DeviceEventsHub:84`; `SignalRDeviceEventDeliveryService:80, 131, 177` | Signatures unchanged, and every caller passes `IPermissionStore`. Semantics changed only as intended: latest-wins, no allow `*`, master-admin unconditional. The regression locks (`Role_mutation_fails_closed_...`, `Persisted_resource_grants_..._denies_win`, `Persisted_users_permission_...`, `Permission_grants_require_target_user_and_resource_scope`, hub/SignalR tests) stay green. |
| `FeaturePermissionAuthorizationHandler` | Policies `hms.user.manage` (UsersController) and `hms.permissions.manage` (PermissionGrantsController); registered scoped in `Program.cs:33` | Same resolution as `ScopeAuthorization`. A master-admin now passes despite a persisted deny or a claim `deny-perm=*` (intended). |
| Grant handlers | Only `PermissionGrantsController` and the integration seeds (`ManagementApiTests:849-863`, master-admin seeder) | 200 body shape kept. `GrantDeviceActionHandler` untouched. |
| `SendDeviceCommandHandler` | Only `DeviceCommandsController` | Step order matches §Architecture. The idempotency precheck still runs before authorization (accepted, Next Steps). Status stays 200. |
| `IUserScopeStore` | Implementations: `IdentityUserManagement` (now also takes the scoped `ApplicationDbContext`, the same instance its `UserManager` uses) and `TestUserScopes`. Callers: Users, PermissionGrants, DevicePermissions controllers, the membership handlers, the grant handlers. | Additive member. `IsMasterAdmin` is translatable SQL (`EXISTS` over `AspNetUserRoles`/`AspNetRoles` by `NormalizedName`) and is exercised over HTTP; mutant m21 is killed. |
| `IAuthorizationEpochStore` | Implementations: InMemory, EF, `RecordingEpochStore`. Callers: `DevicesController` (`Current`), hub/delivery (`Current`), `RevokeDeviceHandler` (`Advance`), `UpdateDeviceRoleHandler` (`AdvanceMany`) | Existing callers are untouched. `AdvanceMany` is called only for a non-empty `affected` list. |
| `DeviceCommandsController` | Route `api/v1/devices/{id}/commands` | Constructor drops `IUserScopeStore`; CSRF, the 64 KB limit and `Ok(result)` are unchanged. The actor is null → `Forbid()`. |
| `PermissionGrantsController` | `features`, `resource-scopes` | New 400/403 `{code}`. The existing guard checks still run first (`Forbid()` without a body). The snapshot and `device-actions` are unchanged (phase 4). |
| Realtime | Hub + delivery, through `ScopeAuthorization`; the global `PolicyVersion` fence | They inherit the new scope semantics. Every delegation write calls `Advance()` and trips the old fence until phase 5 (accepted). |

## 5. (c) Contract status

All the intentional changes in the brief are present and behave as specified:

- `POST /api/v1/permissions/features` and `/resource-scopes` return:
  - 400 `{code}` for `WILDCARD_GRANT_FORBIDDEN` and `GRANT_TARGET_IS_MASTER_ADMIN`;
  - 403 `{code}` for `SELF_GRANT_FORBIDDEN`, `GRANT_EXCEEDS_OWN_PERMISSIONS` and `GRANT_OVERRIDES_MASTER_DECISION`.

  The 200 body is still the grant record, with ids as `{value}`. `grantedBy` and `grantedByMasterAdmin` are now populated.
- `confirm` is optional and trailing on both requests, and the fingerprint is unchanged (`CommandContracts.cs:136-149`; `Confirm_is_not_part_of_request_fingerprint`).
- New command body codes: `DEVICE_NOT_FOUND`, `DEVICE_REVOKED`, `DEVICE_PERMISSION_REQUIRED`, `PERMISSION_UNMAPPED` (`ContractUnavailable`) and `CONFIRM_REQUIRED`. The old feature/resource/device-action codes are gone.
- The intended semantics are in place: latest-wins, and master-admin is unconditional.
- `AuthorizationContext`, `AuthorizationDecision` and `DeviceAuthorizationPolicy` are removed.
- `IUserScopeStore.IsMasterAdmin` and `IAuthorizationEpochStore.AdvanceMany` are added.

Other signature changes required by the phase file:

- `SendDeviceCommandHandler`: constructor, and `HandleAsync(…, AccessActor, …)`.
- Grant handlers: they now take the actor and return `PermissionGrantOutcome`.
- DI: 6 new scoped registrations. No registration was removed and no lifetime changed.

`Confirm` is never persisted (`EfCommandAuditStore` maps explicit fields) and never logged. The audit JSON holds ids, codes, the role name/description and the version, with no email or display name.

**No unintended break found.** One benign deviation: the `GrantResourceScopeHandler` constructor lacks the `IOrganizationStore` that the phase file lists. The H-1 fix needs it.

## 6. (d) Pattern notes

**Consistent with the codebase:**

- The `InMemoryPermissionStore` now locks `_gate` like the other InMemory stores.
- `StoreWrites.Try` maps only the listed `InvalidOperationException(code)` values (`when` filter); anything else propagates.
- `PostCommitSignals` is the only catch-all, as the contract requires; it logs the audit id and the device ids.
- DI lifetimes are right: scoped handlers over singleton or scoped stores, and no captive dependency. My EF-branch probe with `ValidateScopes = true` resolves all phase-3 services.
- The helper classes (`DelegationRules`, `RoleRules`, `MembershipRules`) are internal and domain-anchored.
- There are no suppressions and no widening casts.
- `JsonSerializerDefaults.Web` in the audit produces the same bytes as the default options for these lowercase anonymous objects.

**Deviations:** L-4 (plan IDs in comments) and L-5 (test without a timeout).

## 7. (e) Build

0 warnings, 0 errors on a full scratch rebuild. Repo `--no-build` tests: 372/372 and 94/94. No CS warnings were introduced.

## 8. Findings

### Critical (blast radius, pre-existing)

**C-1. A lookalike role name lets any `users.manage` holder mint a master-admin.** CONFIRMED over HTTP. A parallel reviewer's memory note pointed me to it; I verified it independently.

- **Where:** none of these files changed in phase 3.
  - `src/Hms.Server.Application/Authorization/RoleAuthorizationPolicy.cs:8-9`: `IsReserved` uses `string.Equals(…, OrdinalIgnoreCase)`, which does not fold U+017F `ſ`.
  - `UsersController.cs:63-65` (create) and `:83-85` (assign), and `IdentityUserManagement.cs:72` (create) and `:86` (assign), rely on `IsReserved`.
  - Identity's `UpperInvariantLookupNormalizer` maps `ſ` to `S`. `RoleExistsAsync` and `AddToRoleAsync` therefore resolve `"maſter-admin"` to the real `MASTER-ADMIN` role.

- **Scenario:**
  1. Admin A has `users.manage` and scope over target T. `CanAccessTarget` (`UsersController.cs:95-105`) has no self check, so T can be A.
  2. A sends `PUT /api/v1/users/{T}/roles/ma%C5%BFter-admin` with CSRF. The response is 204.
  3. T now holds `master-admin`. After the next sign-in, T's cookie carries the role.

- **Why phase 3 makes it critical:** phase 3 makes that role unconditional:
  - every org;
  - every feature;
  - every deny ignored;
  - deny grants against a master-admin are rejected (`GRANT_TARGET_IS_MASTER_ADMIN`).

  The phase's Security Considerations (phase-03:377) assume "Gán role `master-admin` vẫn chỉ master-admin làm được (`RoleAuthorizationPolicy`) → non-master không có đường tự nâng lên bypass". C-1 falsifies that premise.

- **Evidence:** probe `S/tests/Hms.Server.IntegrationTests/ReviewRoleProbes.cs`. It asserts:
  - `IsReserved("maſter-admin") == false`;
  - the normalised name is `"MASTER-ADMIN"`;
  - the PUT returns 204;
  - `IUserScopeStore.IsMasterAdmin(T) == true`.

  The probe passes on the current code.

- **Fix:** decide on the role the Identity store will actually resolve, not on the raw string.
  1. In `AssignRoleAsync`/`CreateRoleAsync`, compare the Identity-normalised name with `roles.NormalizeKey(MasterAdminRole)`. Better still, look up the role entity and check its `NormalizedName`.
  2. Make `RoleAuthorizationPolicy.IsReserved` normalise the same way, for example `role?.Trim().ToUpperInvariant() == "MASTER-ADMIN"`, or route it through `ILookupNormalizer`.
  3. Add a test with `"maſter-admin"` and other compatibility forms. Phase 4 adds `RemoveRoleAsync`, so it also needs the reserved-role check on the resolved role.

- **Ownership:** this belongs to the user-management surface, phase 4 or an immediate hotfix, not to phase-3 code. It must be closed before release.

### High

**H-1. A resource allow pushes the target into a sub-org that is carved out of the grantor's scope (residual RT4 hole).** CONFIRMED.

- **Where:**
  - `src/Hms.Server.Application/Authorization/PermissionContracts.cs:155-174`. Line 163 checks only the seed org.
  - `src/Hms.Server.Api/Controllers/PermissionGrantsController.cs:71`. The same seed-org check.

- **Scenario:**
  1. A master gives admin A scope on Parent and a deny on Child, so A's effective orgs are {Parent}. A holds `permissions.manage` and `devices.manage`. Target T has Parent as home org.
  2. A grants T the feature `devices.manage`. T has no resource grant yet, so RT4(a) passes: T's scope is a subset of A's.
  3. A grants T the resource scope Parent. Parent is in A's scope, and T's features are a subset of A's.
  4. T's effective orgs become `Expand(Parent)` = {Parent, Child}. T lists Child devices and can edit or revoke them; A cannot.

  With `users.manage`, A can create T as a sock puppet, which defeats the master's carve-out. The rule is also order-dependent: granting the resource first and the feature second is rejected by RT4(a).

- **Why it matters:** the contract text (§4, "org ∉ effective orgs") is followed literally. The result still breaks:
  - plan.md acceptance: "không cấp vượt quyền mình (kể cả qua phạm vi org … của người nhận [RT4])";
  - the phase's Security Considerations ("chặn leo thang");
  - the spirit of Q12, because a master-issued restriction is bypassed by proxy.

- **Evidence:**
  - Unit probe P1 asserts that T's scope contains Child and A's does not, and that the reverse order gives `GRANT_EXCEEDS_OWN_PERMISSIONS`.
  - The HTTP probe (`ReviewIntegrationProbes.P1_…`) uses claims `org-scope` Parent and `deny-org-scope` Child. Both POSTs return 200. The target's `GET /devices` contains `ChildDeviceId`. The target's `PUT /devices/{child}` returns 200, while the admin gets 403.

- **Fix:** for a non-master resource allow, require the whole subtree to be in the grantor's scope, and inject `IOrganizationStore`:
  ```csharp
  if (!OrganizationScopeResolver.Expand([organizationId], organizations.List()).IsSubsetOf(actor.EffectiveOrganizations))
      return DeviceAccessOutcomeCodes.GrantExceedsOwnPermissions;
  ```
  I applied this in `S/`: all 372 unit and 94 integration tests still pass, and only my two P1 probes flip. Add a unit test and an integration test mirroring the probes. The fix needs a one-line amendment of contract §4, with owner sign-off, because it tightens a stated rule.

  Related: a deny has the mirror effect. A deny by A on Parent also removes T's master-granted Child scope, and the contract allows non-master denies. The owner may want the subtree rule for denies too.

### Medium

**M-1. A deny `*` can never be lifted (contract gap).** CONFIRMED by probe P2.

- **Where:** `PermissionContracts.cs:117-118` rejects allow `*` for every actor. `PermissionGrantResolver.cs:45-47`: a deny `*` clears `Allowed`. Grants are append-only and no delete endpoint exists.
- **Scenario:** a master suspends U with a deny `*`. Later:
  - allow `*` → 400 `WILDCARD_GRANT_FORBIDDEN`;
  - allow `users.manage` → 200, but `Has` stays false.

  Nothing can supersede the `*` key short of DB surgery or promoting U to master-admin.
- **Context:** this is not a regression, because union-of-history already made every deny permanent. But latest-wins exists to make revocation reversible. An allow `*` row grants nothing anyway (the resolver strips it), so a master-issued allow `*` would only lift the deny.
- **Fix (owner decision):** either reject allow `*` only for non-masters (a master allow `*` then acts purely as a lift), or add an explicit lift operation. Then update F6/contract §4 and the two tests `Wildcard_allow_grant_is_rejected_for_every_actor` and `Feature_grant_rejects_wildcard_allow_even_for_master_admin`.

### Low

**L-1. Q12 race: an interleaved non-master allow can outrank a master deny.** CONFIRMED by deterministic interleaving (probe P5, wrapper store).

- **Where:** `PermissionContracts.cs:102-111` and `:143-153` (check, then `Advance()`, then insert, not atomic). The rule itself is at `:180-187`.
- **Scenario:**
  1. Master M takes version v but has not inserted its deny yet.
  2. A's allow request passes `DenyStands`, takes v+1 and inserts.
  3. M inserts its deny at v.

  The latest row is A's allow. M's deny got 200 and committed last, but it has no effect.
- **Window:** one DB round trip, the same class as the accepted RT3 window.
- **Fix:** take the version and insert in one statement, for example `WITH v AS (UPDATE policy_versions … RETURNING "Version") INSERT … SELECT … FROM v`. Otherwise add the race to the Risk Assessment.

**L-2. Per-device reads in a non-master role update (N+1).** CONFIRMED by probe P6: 25 devices give 25 `IDeviceStore.Find` calls.

- **Where:** `DeviceAccessHandlers.cs:110-123`.
- **Cost:** for each device using the role, one device read plus a resolver call (membership read plus role read with `Include`), so about 3 queries per device. The cost is bounded by the role's device count, and the master path skips the loop.
- **Fix:**
  1. Call `memberships.ListByUser(actor.UserId)` once.
  2. Load the distinct roles once.
  3. For each device, own(D) is that role's permissions when its org equals the edited role's org. The owner-org invariant makes the device read unnecessary.

**L-3. Permission strings are not normalised; a padded wildcard is accepted.** CONFIRMED by probe P3.

- **Where:** `PermissionGrantsController.cs:49` checks only `IsNullOrWhiteSpace`. `PermissionContracts.cs:108-117` compares exactly against `"*"` and stores the raw string.
- **Scenario:** a master posts `" * "` and gets 200 with a junk row, which silently dodges `WILDCARD_GRANT_FORBIDDEN`. Junk allows also count in the RT4(b) subset check: a master's `" * "` row blocks every non-master scope grant to that target.
- **Reach:** only API clients, because the frontend trims (`App.vue:685`). Probably pre-existing; I could not verify without git.
- **Fix:** trim before validating, and compare the wildcard after trimming.

**L-4. Plan and decision IDs in code comments.** This conflicts with the user's rule "Stable Code Artifacts" (`~/.claude/rules/review-audit-self-decision.md`).

- **Where:**
  - `DeviceAccessHandlers.cs:259` ("Q13") and `:393` ("design contract §4")
  - `PermissionContracts.cs:119` ("Q4", "Q11") and `:177` ("Q12")
  - `PermissionGrantResolver.cs:10` and `AccessActorFactory.cs:12` ("design contract §3")
  - tests: `DeviceAccessTestFixture.cs:14, 131` ("phase-2") and `DeviceAccessHandlerTests.cs:250` ("Q13")
- **Fix:** state the invariant itself, for example "A deny never restricts a master-admin; removing the role is the way."

**L-5. A regression makes the suite hang instead of fail.** CONFIRMED.

- **Where:** `CommandBoundaryTests.cs:437-438` awaits `transport.Entered.Task` with no timeout.
- **Evidence:** with mutant m05 (a scope regression) the unit run hung for more than 10 minutes. `--blame-hang-timeout 20s` then aborted it with a hang dump. The repo has no CI hang timeout.
- **Fix:** `await transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));` or `await Task.WhenAny(firstTask, transport.Entered.Task)` followed by an assert.

**L-6. A regression-locked test now proves nothing.** `ManagementApiTests.cs:435-465` (`Device_command_uses_persisted_user_organization_when_org_claim_is_absent`).

- The home org no longer grants scope (contract §3).
- The handler returns `COMMAND_NOT_IN_CATALOG` before access evaluation.
- The client also carries `org-scope`.

So neither the name nor the assertion message ("The persisted organization should resolve the command scope") is true any more. Keep it green for now, as the phase requires, and rename or rewrite it in phase 4 with the 404 gate.

### Informational

- **I-1.** Mutant m22 survived: no test proves that the epoch signal still runs when `IPermissionStore.Advance()` throws. The code is correct, because the two signals are separate `PostCommitSignals.Run` calls (`DeviceAccessHandlers.cs:133-135, 233-236, 281-282`). The plan accepts this gap, since no test class implements `IPermissionStore`.
- **I-2.** `EfAuthorizationEpochStore.cs:100-113` was reviewed but not executed, because there is no PostgreSQL here. As read, the SQL is valid:
  - `unnest(@device_ids) AS device_id` yields a uuid column `device_id`;
  - Npgsql infers `uuid[]` from `Guid[]`;
  - the ids are de-duplicated, so `ON CONFLICT DO UPDATE` never touches a row twice.

  The non-Postgres path is covered (mutant m20 is killed). Run it once against PostgreSQL before release.
- **I-3.** `Development_registration_resolves_device_access_resolver_and_handlers` covers only the in-memory branch. My EF-branch probe (`S/tests/Hms.Server.IntegrationTests/ReviewDiProbes.cs`) passes. Consider adding it.
- **I-4.** `MeController.cs:24-37` still unions grant history and appends `*` for master-admins, so `/me/permissions` disagrees with enforcement until phase 4. This belongs to phase 4.
- **I-5.** Pre-existing: several list endpoints call `ScopeAuthorization` once per item, and each call loads every org plus the grants:
  - `UsersController.cs:30-37`
  - `OrganizationsController.cs:22`
  - `AreasController.cs:22`
  - `ProjectsController.cs:23`
  - `PackagesController.cs:24`

  Phase 3 did not change the cost (the master-admin path is now cheaper).
- **I-6.** The pre-existing flaky test `SignalRConnectionBacklogTests.Emits_runtime_metrics_for_drop_retry_and_resynchronization` failed in 1 of 6 runs on unmodified scratch. It is already tracked in the phase-02 sync.
- **I-7.** `DevicePermissionsController` still writes device-action grants (including `*`, by `devices.manage` holders). No code evaluates them for commands any more, so they are inert until phase 4 deletes the controller.

## 9. Plan follow-ups (no plan file edited)

- Every Todo item is implemented. Success criterion 12 is partial because of H-1. The frontend gate was not run by me.
- Recommendation: keep phase 3 open until H-1 is fixed with tests and M-1 is decided. Then the phase can be marked complete. The frontmatter still says `status: pending`; changing it is left to the controller or PM.
- For phase 4:
  - rename or rewrite L-6;
  - switch `MeController` (I-4).

## 10. Commands run

```bash
cd /d/WORKSPACE/PROJECT/HMS-SERVER && find src tests -type f -name "*.cs" -newer plans/.../reports/pm-261005-2336-phase-02-sync.md -not -path "*/bin/*" -not -path "*/obj/*"
grep -rnE --include=*.cs "AuthorizationContext|DeviceAuthorizationPolicy|FEATURE_PERMISSION_(REQUIRED|DENIED)|RESOURCE_SCOPE_(REQUIRED|DENIED)|DEVICE_ACTION_PERMISSION|ScopeAuthorization\.Actor" src tests   # 0
grep -n '"\*"' <ScopeAuthorization, FeaturePermissionAuthorizationHandler, Domain/Authorization, DeviceCommandsController>    # 0
cd /d/WORKSPACE/PROJECT/HMS-SERVER && dotnet test Hms.Server.slnx --no-build --nologo                     # 372/372, 94/94
tar --exclude='*/bin' --exclude='*/obj' -cf - Hms.Server.slnx src tests | (cd S/ && tar -xf -)
cd S/ && dotnet build Hms.Server.slnx --nologo [--no-incremental]                                      # 0 warnings, 0 errors
cd S/ && dotnet test Hms.Server.slnx --no-build --nologo                                               # 372/94
cd S/ && dotnet test ... --filter "FullyQualifiedName~ReviewProbes|ReviewIntegrationProbes|ReviewDiProbes|ReviewRoleProbes"
cd S/ && (scratch-only) apply the H-1 fix + run the full suite                                         # 372+94 originals green, only the P1 probes flip
cd S/ && python3 mutate.py                                                                             # 22 mutants, 21 killed
cd S/ && dotnet test tests/Hms.Server.UnitTests/... --filter Concurrent_same_correlation_claims_transport_once --blame-hang-timeout 20s   # hang with m05
cd S/ && (6x) dotnet test tests/Hms.Server.UnitTests/... --no-build                                    # flake check
```

## 11. Unresolved questions

0. **C-1:** who owns the reserved-role normalisation fix, an immediate hotfix or phase 4 (which touches `IUserManagement` for `RemoveRoleAsync`)? Either way, it blocks the release.
1. **H-1:** may contract §4 be tightened so that a non-master resource allow requires the whole subtree to be in the grantor's scope? Should the same rule apply to denies?
2. **M-1:** how should a deny `*` be lifted: a master-only allow `*` acting as a lift, or a dedicated operation?
3. **L-1:** fix the Q12 race now with a single-statement insert, or record it in the Risk Assessment?

Status: DONE_WITH_CONCERNS
Summary: Phase 3 meets 14 of 15 success criteria; build 0/0, tests 372/94, 21 of 22 mutants killed. One pre-existing Critical in the blast radius (C-1: a `users.manage` holder can mint a master-admin with the lookalike role name "maſter-admin", confirmed over HTTP), one High residual RT4 hole (H-1, confirmed over HTTP) and one Medium contract gap (M-1: deny `*` can never be lifted).
Concerns/Blockers: C-1 blocks release (normalise reserved-role checks the way Identity does). H-1 needs a small code fix (validated in scratch) plus a one-line contract §4 amendment with owner sign-off. M-1 needs an owner decision. L-1 to L-6 are optional before phase 4.
