# Phase 3 scripted mutation run

Saved by the controller from the tester's hand-back: the environment did not let the subagent write report files. Every result below comes from the script output (`scratchpad/phase03-mut/results.txt`, embedded at the end); the controller checked that file's BASELINE, SURVIVED, INTEGRITY and SUMMARY lines.

## Summary

- 49 mutants: **37 KILLED, 12 SURVIVED**, 0 compile errors, 0 targets not found or ambiguous.
- Requested list (M1-M25, M11 split in two): 24 of 26 killed; M7 and M21 survived. Added mutants (M26-M48): 13 killed, 10 survived.
- All 12 survivors are test gaps; none is an equivalent mutant. The most important is **M7**: `SeesAllInScope` no longer requiring devices.manage or permissions.manage goes unnoticed, and phases 4-5 gate device visibility on that flag.
- Under M35 (authorizing by `request.Action`, i.e. bug 1 returning) one test waits forever, so `dotnet test` hangs instead of failing: the wait at `CommandBoundaryTests.cs:438` has no timeout.

## Setup

- Fresh copy of `Hms.Server.slnx`, `src`, `tests` (bin/obj excluded) in `scratchpad/phase03-mut/`; 254 files sha256-identical to the repo at the start and end of every run. Nothing in the repo was built, cleaned or tested.
- Harness `mutate.js` (shape of the phase-2 cycle-3 harness): restores original bytes raw (all 13 target files are CRLF without BOM), reports AMBIGUOUS / TARGET NOT FOUND, runs a strict baseline first, writes stdout and `results.txt`, checks each target file is pristine before every mutant, checks parsed failures against the runner's counts, and requires the mutated assembly to differ from the baseline build.
- Watchdog (added after run 1 hung on M35): kills the whole process tree after 180 s, then reruns the mutant once with `--blame-hang-timeout 30s` to name the hanging test. Flaky rule: if only `SignalR*` tests fail, the mutant is rerun once.
- Run 3 is authoritative (baseline green on the first attempt); runs 2 and 3 agree on all 49 mutants, run 1 agrees on the 35 it reached.

## Baseline

Green in 9.2 s: unit 372/372, integration 94/94.

## Results (run 3)

| ID | File | Change | Result | Killing tests |
|---|---|---|---|---|
| M1 | PermissionGrantResolver.cs | `Has` checks denies before the master-admin bypass | KILLED | PermissionTests.Master_admin_ignores_feature_and_wildcard_denies, PermissionTests.Feature_policy_handler_master_admin_passes_despite_persisted_deny, ManagementApiTests.Master_admin_with_persisted_users_deny_still_creates_roles |
| M2 | PermissionGrantResolver.cs | Same-version tie picks allow | KILLED | PermissionTests.Latest_grant_with_equal_policy_version_prefers_deny |
| M3 | PermissionGrantResolver.cs | Wildcard kept in Allowed | KILLED | AccessActorFactoryTests.Wildcard_feature_allow_is_ignored, PermissionTests.Feature_wildcard_allow_grants_nothing |
| M4 | PermissionGrantResolver.cs | Wildcard deny no longer clears Allowed | KILLED | AccessActorFactoryTests.Wildcard_feature_allow_is_ignored |
| M5 | AccessActorFactory.cs | Master-admin gets all orgs minus expanded denies | KILLED | AccessActorFactoryTests.Master_admin_keeps_every_organization_despite_resource_denies, ManagementApiTests.Master_admin_device_list_ignores_persisted_resource_deny |
| M6 | DeviceAccessResolver.cs | Role from another org accepted | KILLED | DeviceAccessPolicyTests.Resolver_ignores_role_from_another_organization |
| M7 | DeviceAccessResolver.cs | `SeesAllInScope = master or orgInScope` | **SURVIVED** | none |
| M8 | DeviceAccessPolicy.cs | Revoked checked before scope | KILLED | DeviceAccessPolicyTests.Out_of_scope_device_is_not_found_even_when_revoked |
| M9 | DeviceAccessHandlers.cs | Assign: master-admin skips the role-version check | KILLED | DeviceAccessHandlerTests.Assign_with_stale_role_version_is_rejected |
| M10 | DeviceAccessHandlers.cs | Assign: epoch advanced on a fresh assign | KILLED | DeviceAccessHandlerTests.Fresh_assign_does_not_advance_device_epoch |
| M11a | DeviceAccessHandlers.cs | Assign: PolicyVersion advanced before the store write | KILLED | DeviceAccessHandlerTests.Membership_write_precedes_policy_version_and_epoch_advance, DeviceAccessHandlerTests.Store_level_membership_conflict_surfaces_membership_changed |
| M11b | DeviceAccessHandlers.cs | Remove: epoch advanced before the store write | KILLED | DeviceAccessHandlerTests.Membership_write_precedes_policy_version_and_epoch_advance |
| M12 | DeviceAccessHandlers.cs | UpdateRole: subset check on the new permissions only | KILLED | DeviceAccessHandlerTests.Non_master_cannot_shrink_role_exceeding_own_permissions_on_assigned_devices |
| M13 | DeviceAccessHandlers.cs | UpdateRole: `AdvanceMany` skipped | KILLED | DeviceAccessHandlerTests.Role_update_advances_all_affected_devices_in_one_call |
| M14 | DeviceAccessHandlers.cs | Remove: current-role check skipped | KILLED | DeviceAccessHandlerTests (3 current-role tests) |
| M15 | DeviceAccessHandlers.cs | CreateRole: high-risk check removed | KILLED | DeviceAccessHandlerTests.Non_master_admin_cannot_add_or_remove_high_risk_permission |
| M16 | PermissionContracts.cs | SELF_GRANT_FORBIDDEN removed | KILLED | PermissionTests.Non_master_cannot_self_grant_feature_or_scope, ManagementApiTests.Feature_and_resource_grants_reject_self_grant_for_non_master |
| M17 | PermissionContracts.cs | GRANT_OVERRIDES_MASTER_DECISION removed | KILLED | PermissionTests.Non_master_cannot_reverse_master_admin_feature_deny, ManagementApiTests.Non_master_cannot_reverse_master_admin_deny_over_http |
| M18 | PermissionContracts.cs | Wildcard deny accepted from a non-master | KILLED | PermissionTests.Wildcard_deny_grant_requires_master_admin, ManagementApiTests.Feature_grant_rejects_wildcard_deny_from_non_master |
| M19 | PermissionContracts.cs | GrantResource: target-features subset check dropped | KILLED | PermissionTests.Resource_grant_rejected_when_target_holds_features_grantor_lacks |
| M20 | PermissionContracts.cs | GrantResource: org-in-actor-scope check dropped | KILLED | PermissionTests.Non_master_cannot_grant_scope_outside_own_scope |
| M21 | SendDeviceCommandHandler.cs | Cached result returned before CONFIRM_REQUIRED | **SURVIVED** | none |
| M22 | SendDeviceCommandHandler.cs | TTL always the dispatcher default | KILLED | DeviceCommandAuthorizationTests.Catalog_ttl_60000_rows_plan_sixty_second_expiry |
| M23 | DeviceAccessHandlers.cs | `PostCommitSignals.Run` rethrows | KILLED | DeviceAccessHandlerTests.Post_commit_signal_failure_returns_ok_and_keeps_the_write |
| M24 | EfAuthorizationEpochStore.cs | `AdvanceMany` without Distinct | KILLED | PersistenceModelTests.Authorization_epoch_advance_many_increments_each_device_once |
| M25 | IdentityUserManagement.cs | `IsMasterAdmin` compares the raw role name | KILLED | ManagementApiTests.Deny_on_master_admin_target_is_rejected_over_http |
| M26 | PermissionGrantResolver.cs | Claim allow kept despite a claim deny on the same key | **SURVIVED** | none |
| M27 | PermissionGrantResolver.cs | Claim allow kept despite a persisted row on the key | **SURVIVED** | none |
| M28 | PermissionContracts.cs | Any past master deny blocks, not just the latest master row | **SURVIVED** | none |
| M29 | PermissionContracts.cs | Latest row from any grantor decides | KILLED | PermissionTests.Non_master_can_reverse_non_master_deny |
| M30 | DeviceAccessHandlers.cs | Re-assigning the current role is no longer a no-op | **SURVIVED** | none |
| M31 | DeviceAccessHandlers.cs | Role name length `>` becomes `>=` | **SURVIVED** | none |
| M32 | DeviceAccessHandlers.cs | `StoreWrites.Try` maps every InvalidOperationException | **SURVIVED** | none |
| M33 | DeviceAccessHandlers.cs | CreateRole doesn't advance PolicyVersion | **SURVIVED** | none |
| M34 | DeviceAccessHandlers.cs | DeleteRole doesn't advance PolicyVersion | **SURVIVED** | none |
| M35 | SendDeviceCommandHandler.cs | Access checked against `request.Action` (bug 1) | KILLED (hang + 12 failures) | CommandBoundaryTests x7, DeviceCommandAuthorizationTests x5; hang in CommandBoundaryTests.Concurrent_same_correlation_claims_transport_once |
| M36 | DeviceCommandsController.cs | `Confirm` not passed to the handler | **SURVIVED** | none |
| M37 | DeviceAccessResolver.cs | `CanManageAccess` ignores org scope | KILLED | DeviceAccessPolicyTests.Resolver_sets_flags_only_for_in_scope_features |
| M38 | DeviceAccessPolicy.cs | Master-admin allowed before the revocation check | KILLED | DeviceAccessPolicyTests.Revoked_device_is_denied_before_master_admin_bypass, CommandBoundaryTests.Master_admin_bypasses_membership_but_not_revocation |
| M39 | DeviceAccessPolicy.cs | `SeesAllInScope` grants every permission | KILLED | DeviceAccessPolicyTests.Scope_admin_without_membership_sees_device_view_only |
| M40 | PermissionContracts.cs | Target-scope-within-actor-scope check dropped | KILLED | PermissionTests.Feature_grant_rejected_when_target_scope_exceeds_grantor_scope (+ HTTP twin) |
| M41 | PermissionGrantsController.cs | GRANT_TARGET_IS_MASTER_ADMIN mapped to 403 | KILLED | ManagementApiTests.Deny_on_master_admin_target_is_rejected_over_http |
| M42 | DeviceAccessHandlers.cs | Remove checks the target user even with a membership | KILLED | 4 DeviceAccessHandlerTests (foreign, orgless and orphaned member removal) |
| M43 | OrganizationScopeResolver.cs | Denies subtracted without expanding to child orgs | KILLED | AccessActorFactoryTests.Denied_organization_is_subtracted_after_expansion |
| M44 | PermissionContracts.cs | GrantResource: GRANT_TARGET_IS_MASTER_ADMIN removed | KILLED | PermissionTests.Deny_targeting_master_admin_is_rejected |
| M45 | DeviceAccessResolver.cs | `SeesAllInScope` without the org-in-scope condition | KILLED | DeviceAccessPolicyTests.Resolver_sets_flags_only_for_in_scope_features |
| M46 | PermissionContracts.cs | Feature rows always `GrantedByMasterAdmin = false` | KILLED | PermissionTests.Grants_record_grantor_and_master_flag (+2) |
| M47 | RealtimeContracts.cs | In-memory `AdvanceMany` without Distinct | **SURVIVED** | none |
| M48 | DeviceAccessHandlers.cs | UpdateRole: affected devices not de-duplicated | KILLED | DeviceAccessHandlerTests.Role_update_advances_all_affected_devices_in_one_call |

Killing-test lists above omit the unrelated SignalR flakes; the full lists are in the embedded results.

## Survivor analysis (all test gaps)

- **M7 (high):** an in-scope non-master without devices.manage or permissions.manage gets `SeesAllInScope = true`, so `device.view` is allowed without a membership. Every in-scope actor in the tests holds one of the two features. Add a negative resolver/policy assertion (`DeviceAccessPolicyTests`).
- **M21 (medium, latent until catalog rows are enabled):** replaying a confirm row's correlation id without `Confirm` would return the cached result; every confirm test uses a new correlation id. Add a replay test (`DeviceCommandAuthorizationTests`).
- **M30 (medium):** re-assigning the held role must stay a no-op (no write, no PolicyVersion or epoch advance). Add a test (`DeviceAccessHandlerTests`).
- **M28 (medium-low, fails closed only):** master deny, master re-allow, then a non-master allow must be OK; the resource-side twin has the same gap. Add tests (`PermissionTests`).
- **M32 (medium-low):** an unexpected store exception must propagate, not become an outcome code. Add a test using the `BeforeWrite` hook.
- **M26 / M27 (low):** `Allowed` must not keep a claim allow that a claim deny or a persisted row overrides; `Has` hides it, but `AccessActor.FeaturePermissions` is built from `Allowed`. Add `DoesNotContain` assertions (`PermissionTests`, `AccessActorFactoryTests`).
- **M31 (low):** a 128-character name and a 512-character description must be accepted. Add a boundary test.
- **M33 / M34 (low):** role create and a successful delete advance PolicyVersion; rejected deletes do not. Add assertions.
- **M36 (low now, rises with plan 261001):** the controller must pass `Confirm` through; needs an integration factory with an enabled catalog row.
- **M47 (low):** the in-memory `AdvanceMany` must count a duplicate id once. Add a test (`RealtimeTests`).

## Other findings

- **F1:** `CommandBoundaryTests.cs:438` awaits `transport.Entered.Task` without a timeout; under M35 run 1 sat for 13+ minutes. Fix: `WaitAsync(TimeSpan.FromSeconds(10))`.
- **F2:** two SignalR tests are flaky: `SignalRConnectionBacklogTests.Emits_runtime_metrics_for_drop_retry_and_resynchronization` (6 failures) and `SignalRDeviceEventDeliveryServiceTests.Failed_connection_is_retried_without_replaying_to_a_successful_connection` (3 failures), 9 of 104 runs. Neither uses a mutated type; no classification changed.
- **F3:** the earlier tester report said no mutants survive; M7 and M21 do.

## Full results.txt (run 3)

```text
# phase 3 mutation run (full)
date: 2026-10-06T00:07:29.128Z
copy: C:\Users\toann\AppData\Local\Temp\claude\D--WORKSPACE-PROJECT-HMS-SERVER\daebfbef-46ca-4e9f-b506-cf6e26aef926\scratchpad\phase03-mut
command: dotnet test C:\Users\toann\AppData\Local\Temp\claude\D--WORKSPACE-PROJECT-HMS-SERVER\daebfbef-46ca-4e9f-b506-cf6e26aef926\scratchpad\phase03-mut\Hms.Server.slnx --nologo -v q
watchdog: 180 s per run (process tree killed); a hung mutant is rerun once with --blame-hang-timeout 30s --blame-hang-dump-type none to name the hanging test
flaky rule: when only SignalR* tests fail for a mutant, that mutant is rerun once and both results are reported; the baseline gets no rerun (any failure stops)
mutants selected: 49 of 49
target files (sha256 at start):
  6f439f1f04ec62e51c3311cd3656b069788575cf674c980d4a593294e37a9ee2  src/Hms.Server.Application/Authorization/PermissionGrantResolver.cs
  6a5b0b89fec5750c24f80f370f7be03f2e10cb873a5789d57b120f5e1e932bc8  src/Hms.Server.Api/Authorization/AccessActorFactory.cs
  8164920fcc68bdc051828190f04779ac2898a59c3fbe876112e0c1535661cbe9  src/Hms.Server.Application/Authorization/DeviceAccessResolver.cs
  45b81fd8ae78f4d82cadff3131fa007a3215286c70b61c17545a96c0516a1355  src/Hms.Server.Domain/Authorization/DeviceAccessPolicy.cs
  a9a4402d71f9bd7d8d1bd84eac6642ce59771fe234fd254caffc704a9b0e1294  src/Hms.Server.Application/Authorization/DeviceAccessHandlers.cs
  8b8045ebc23c0d8c61ab4e1b3c7bdeedc3cefad89e8057c2c3c2262d20f25ed7  src/Hms.Server.Application/Authorization/PermissionContracts.cs
  f29d08bce3c5d36cf0fe6a505a63ae6a6e41b1137da3c4071e77bcdc677733fa  src/Hms.Server.Application/Commands/SendDeviceCommandHandler.cs
  5cc980c445cd77838fa8982d025f09110e7a16ec16693616a1686e8d656832dc  src/Hms.Server.Infrastructure/Persistence/EfAuthorizationEpochStore.cs
  fbe16c58f338379f9482cfb7e3edb612796df8aa362f9e34a28bd411f051ec18  src/Hms.Server.Infrastructure/Identity/IdentityUserManagement.cs
  58fa62f8e00ad07cd95c70f3383546790efb7991e75c292c2c8bba00253b653a  src/Hms.Server.Api/Controllers/DeviceCommandsController.cs
  9bebd38b793b2009fef8caf2a8886ca6bb0bc8236347223fc95dbeac413b494b  src/Hms.Server.Api/Controllers/PermissionGrantsController.cs
  625171f6879bce3c6d30b7304f0ef512b08e487c14d6ecda218ff96fd38fe6b8  src/Hms.Server.Application/Authorization/OrganizationScopeResolver.cs
  0a0224aaf211ab3b5b25962ad830dcd78b94bf8a2aa2f15358843e2091206c2f  src/Hms.Server.Application/Realtime/RealtimeContracts.cs
all target files byte-identical to D:\WORKSPACE\PROJECT\HMS-SERVER

BASELINE: GREEN (9.2 s) -- Hms.Server.UnitTests.dll: Passed failed=0 passed=372 skipped=0 total=372; Hms.Server.IntegrationTests.dll: Passed failed=0 passed=94 skipped=0 total=94

M1 Has: denies evaluated before the master-admin check [src/Hms.Server.Application/Authorization/PermissionGrantResolver.cs]: KILLED by PermissionTests.Master_admin_ignores_feature_and_wildcard_denies, PermissionTests.Feature_policy_handler_master_admin_passes_despite_persisted_deny, ManagementApiTests.Master_admin_with_persisted_users_deny_still_creates_roles (9.2 s)
M2 Latest: same-version tie prefers allow [src/Hms.Server.Application/Authorization/PermissionGrantResolver.cs]: KILLED by PermissionTests.Latest_grant_with_equal_policy_version_prefers_deny (9.0 s)
M3 Features: wildcard kept in Allowed [src/Hms.Server.Application/Authorization/PermissionGrantResolver.cs]: KILLED by AccessActorFactoryTests.Wildcard_feature_allow_is_ignored, PermissionTests.Feature_wildcard_allow_grants_nothing (8.5 s)
M4 Features: wildcard deny no longer clears Allowed [src/Hms.Server.Application/Authorization/PermissionGrantResolver.cs]: KILLED by AccessActorFactoryTests.Wildcard_feature_allow_is_ignored (8.5 s)
M5 EffectiveOrganizations: master-admin through the deny-subtracting path [src/Hms.Server.Api/Authorization/AccessActorFactory.cs]: KILLED by AccessActorFactoryTests.Master_admin_keeps_every_organization_despite_resource_denies, ManagementApiTests.Master_admin_device_list_ignores_persisted_resource_deny (8.1 s)
M6 Resolver: role of another organization accepted [src/Hms.Server.Application/Authorization/DeviceAccessResolver.cs]: KILLED by DeviceAccessPolicyTests.Resolver_ignores_role_from_another_organization (8.8 s)
M7 Resolver: SeesAllInScope without devices.manage/permissions.manage [src/Hms.Server.Application/Authorization/DeviceAccessResolver.cs]: SURVIVED (8.4 s)
M8 Policy: revoked checked before scope [src/Hms.Server.Domain/Authorization/DeviceAccessPolicy.cs]: KILLED by DeviceAccessPolicyTests.Out_of_scope_device_is_not_found_even_when_revoked (8.6 s)
M9 Assign: expectedRoleVersion check skipped for master-admin [src/Hms.Server.Application/Authorization/DeviceAccessHandlers.cs]: KILLED by DeviceAccessHandlerTests.Assign_with_stale_role_version_is_rejected, SignalRConnectionBacklogTests.Emits_runtime_metrics_for_drop_retry_and_resynchronization (9.2 s)
M10 Assign: device epoch advanced on a fresh assign too [src/Hms.Server.Application/Authorization/DeviceAccessHandlers.cs]: KILLED by DeviceAccessHandlerTests.Fresh_assign_does_not_advance_device_epoch (9.7 s)
M11a Assign: PolicyVersion advanced before the store write [src/Hms.Server.Application/Authorization/DeviceAccessHandlers.cs]: KILLED by DeviceAccessHandlerTests.Membership_write_precedes_policy_version_and_epoch_advance, SignalRConnectionBacklogTests.Emits_runtime_metrics_for_drop_retry_and_resynchronization, DeviceAccessHandlerTests.Store_level_membership_conflict_surfaces_membership_changed (12.3 s)
M11b Remove: device epoch advanced before the store write [src/Hms.Server.Application/Authorization/DeviceAccessHandlers.cs]: KILLED by DeviceAccessHandlerTests.Membership_write_precedes_policy_version_and_epoch_advance (12.3 s)
M12 UpdateRole: subset check uses only the new permissions [src/Hms.Server.Application/Authorization/DeviceAccessHandlers.cs]: KILLED by SignalRConnectionBacklogTests.Emits_runtime_metrics_for_drop_retry_and_resynchronization, DeviceAccessHandlerTests.Non_master_cannot_shrink_role_exceeding_own_permissions_on_assigned_devices (10.2 s)
M13 UpdateRole: AdvanceMany skipped [src/Hms.Server.Application/Authorization/DeviceAccessHandlers.cs]: KILLED by DeviceAccessHandlerTests.Role_update_advances_all_affected_devices_in_one_call (10.0 s)
M14 Remove: current-role high-risk/subset check skipped for non-masters [src/Hms.Server.Application/Authorization/DeviceAccessHandlers.cs]: KILLED by DeviceAccessHandlerTests.Non_master_admin_cannot_replace_or_remove_high_risk_current_role, DeviceAccessHandlerTests.Non_master_admin_cannot_replace_or_remove_current_role_exceeding_own_permissions, DeviceAccessHandlerTests.Removing_foreign_member_still_obeys_high_risk_and_own_permission_rules (10.1 s)
M15 CreateRole: high-risk allowed for non-masters [src/Hms.Server.Application/Authorization/DeviceAccessHandlers.cs]: KILLED by DeviceAccessHandlerTests.Non_master_admin_cannot_add_or_remove_high_risk_permission (9.9 s)
M16 GrantFeature: SELF_GRANT_FORBIDDEN removed [src/Hms.Server.Application/Authorization/PermissionContracts.cs]: KILLED by PermissionTests.Rejected_grant_does_not_advance_policy_version, PermissionTests.Non_master_cannot_self_grant_feature_or_scope, ManagementApiTests.Feature_and_resource_grants_reject_self_grant_for_non_master (8.8 s)
M17 GrantFeature: GRANT_OVERRIDES_MASTER_DECISION removed [src/Hms.Server.Application/Authorization/PermissionContracts.cs]: KILLED by PermissionTests.Non_master_cannot_reverse_master_admin_feature_deny, SignalRDeviceEventDeliveryServiceTests.Failed_connection_is_retried_without_replaying_to_a_successful_connection, ManagementApiTests.Non_master_cannot_reverse_master_admin_deny_over_http (11.9 s)
M18 GrantFeature: wildcard deny accepted from a non-master [src/Hms.Server.Application/Authorization/PermissionContracts.cs]: KILLED by PermissionTests.Wildcard_deny_grant_requires_master_admin, ManagementApiTests.Feature_grant_rejects_wildcard_deny_from_non_master (12.7 s)
M19 GrantResource: target-features subset check dropped [src/Hms.Server.Application/Authorization/PermissionContracts.cs]: KILLED by PermissionTests.Resource_grant_rejected_when_target_holds_features_grantor_lacks (10.7 s)
M20 GrantResource: organization-in-actor-scope check dropped [src/Hms.Server.Application/Authorization/PermissionContracts.cs]: KILLED by PermissionTests.Rejected_grant_does_not_advance_policy_version, PermissionTests.Non_master_cannot_grant_scope_outside_own_scope (11.3 s)
M21 SendCommand: CONFIRM_REQUIRED checked after the cached-result return [src/Hms.Server.Application/Commands/SendDeviceCommandHandler.cs]: SURVIVED (9.3 s)
M22 SendCommand: catalog DefaultTtlMs ignored (dispatcher default always) [src/Hms.Server.Application/Commands/SendDeviceCommandHandler.cs]: KILLED by DeviceCommandAuthorizationTests.Catalog_ttl_60000_rows_plan_sixty_second_expiry (8.0 s)
M23 PostCommitSignals.Run: rethrow instead of logging [src/Hms.Server.Application/Authorization/DeviceAccessHandlers.cs]: KILLED by DeviceAccessHandlerTests.Post_commit_signal_failure_returns_ok_and_keeps_the_write (14.3 s)
M24 EfAuthorizationEpochStore.AdvanceMany: Distinct dropped [src/Hms.Server.Infrastructure/Persistence/EfAuthorizationEpochStore.cs]: KILLED by PersistenceModelTests.Authorization_epoch_advance_many_increments_each_device_once (12.6 s)
M25 IsMasterAdmin: raw role name compared to NormalizedName [src/Hms.Server.Infrastructure/Identity/IdentityUserManagement.cs]: KILLED by ManagementApiTests.Deny_on_master_admin_target_is_rejected_over_http (12.2 s)
M26 Resolve: claim allow kept when a claim deny names the same key [src/Hms.Server.Application/Authorization/PermissionGrantResolver.cs]: SURVIVED (11.5 s)
M27 Resolve: claim allow kept when a persisted row decides the key [src/Hms.Server.Application/Authorization/PermissionGrantResolver.cs]: SURVIVED (10.2 s)
M28 DenyStands(feature): any historical master deny blocks, not the latest master row [src/Hms.Server.Application/Authorization/PermissionContracts.cs]: SURVIVED (8.9 s)
M29 DenyStands(feature): latest row of any grantor instead of latest master row [src/Hms.Server.Application/Authorization/PermissionContracts.cs]: KILLED by PermissionTests.Non_master_can_reverse_non_master_deny (11.7 s)
M30 Assign: re-assigning the current role is no longer a no-op [src/Hms.Server.Application/Authorization/DeviceAccessHandlers.cs]: SURVIVED (9.9 s)
M31 CheckText: name of exactly NameMaxLength rejected (> becomes >=) [src/Hms.Server.Application/Authorization/DeviceAccessHandlers.cs]: SURVIVED (8.0 s)
M32 StoreWrites.Try: every InvalidOperationException mapped to a code [src/Hms.Server.Application/Authorization/DeviceAccessHandlers.cs]: SURVIVED (8.6 s)
M33 CreateRole: PolicyVersion not advanced [src/Hms.Server.Application/Authorization/DeviceAccessHandlers.cs]: SURVIVED (9.5 s)
M34 DeleteRole: PolicyVersion not advanced [src/Hms.Server.Application/Authorization/DeviceAccessHandlers.cs]: SURVIVED (10.6 s)
M35 SendCommand: access evaluated on request.Action, not the resolved permission [src/Hms.Server.Application/Commands/SendDeviceCommandHandler.cs]: KILLED (hang) by CommandBoundaryTests.Concurrent_same_correlation_claims_transport_once; also failed: CommandBoundaryTests.Authorized_command_is_recorded_without_claiming_delivery, DeviceCommandAuthorizationTests.Adc_view_role_runs_adc_get_status, CommandBoundaryTests.Stale_in_flight_reservation_fails_closed_without_retrying_transport, CommandBoundaryTests.Same_correlation_with_different_command_is_rejected, DeviceCommandAuthorizationTests.Rows_without_ttl_use_dispatcher_default, DeviceCommandAuthorizationTests.Config_set_requires_edit_permission_of_domain, DeviceCommandAuthorizationTests.Config_get_resolves_view_permission_from_json_string_domain, CommandBoundaryTests.Same_correlation_reuses_the_recorded_result, DeviceCommandAuthorizationTests.Removed_then_reassigned_membership_runs_command_again, CommandBoundaryTests.Retrying_after_key_rotation_is_fenced, CommandBoundaryTests.Deferred_transport_never_claims_publish_or_execution, CommandBoundaryTests.Future_command_timestamp_is_rejected, SignalRDeviceEventDeliveryServiceTests.Failed_connection_is_retried_without_replaying_to_a_successful_connection (180.3 s + 37.2 s) [run 1, specified command: HANG: no completion within 180 s, watchdog killed the process tree; failed before the kill: CommandBoundaryTests.Authorized_command_is_recorded_without_claiming_delivery, DeviceCommandAuthorizationTests.Adc_view_role_runs_adc_get_status, CommandBoundaryTests.Stale_in_flight_reservation_fails_closed_without_retrying_transport, CommandBoundaryTests.Same_correlation_with_different_command_is_rejected, DeviceCommandAuthorizationTests.Rows_without_ttl_use_dispatcher_default, DeviceCommandAuthorizationTests.Config_set_requires_edit_permission_of_domain, DeviceCommandAuthorizationTests.Config_get_resolves_view_permission_from_json_string_domain, CommandBoundaryTests.Same_correlation_reuses_the_recorded_result, DeviceCommandAuthorizationTests.Removed_then_reassigned_membership_runs_command_again, CommandBoundaryTests.Retrying_after_key_rotation_is_fenced, CommandBoundaryTests.Deferred_transport_never_claims_publish_or_execution, CommandBoundaryTests.Future_command_timestamp_is_rejected; run 2, diagnostic with --blame-hang-timeout 30s --blame-hang-dump-type none: KILLED by DeviceCommandAuthorizationTests.Adc_view_role_runs_adc_get_status, DeviceCommandAuthorizationTests.Rows_without_ttl_use_dispatcher_default, CommandBoundaryTests.Authorized_command_is_recorded_without_claiming_delivery, DeviceCommandAuthorizationTests.Config_set_requires_edit_permission_of_domain, DeviceCommandAuthorizationTests.Config_get_resolves_view_permission_from_json_string_domain, DeviceCommandAuthorizationTests.Removed_then_reassigned_membership_runs_command_again, CommandBoundaryTests.Stale_in_flight_reservation_fails_closed_without_retrying_transport, CommandBoundaryTests.Same_correlation_with_different_command_is_rejected, CommandBoundaryTests.Same_correlation_reuses_the_recorded_result, CommandBoundaryTests.Retrying_after_key_rotation_is_fenced, CommandBoundaryTests.Deferred_transport_never_claims_publish_or_execution, CommandBoundaryTests.Future_command_timestamp_is_rejected, SignalRDeviceEventDeliveryServiceTests.Failed_connection_is_retried_without_replaying_to_a_successful_connection]
M36 DeviceCommandsController: request.Confirm not passed to the handler [src/Hms.Server.Api/Controllers/DeviceCommandsController.cs]: SURVIVED (8.5 s)
M37 CanManageAccess: organization scope not required [src/Hms.Server.Application/Authorization/DeviceAccessResolver.cs]: KILLED by DeviceAccessPolicyTests.Resolver_sets_flags_only_for_in_scope_features (8.1 s)
M38 Policy: master-admin allowed before the revocation check [src/Hms.Server.Domain/Authorization/DeviceAccessPolicy.cs]: KILLED by DeviceAccessPolicyTests.Revoked_device_is_denied_before_master_admin_bypass, CommandBoundaryTests.Master_admin_bypasses_membership_but_not_revocation (7.8 s)
M39 Policy: SeesAllInScope grants every permission, not only device.view [src/Hms.Server.Domain/Authorization/DeviceAccessPolicy.cs]: KILLED by DeviceAccessPolicyTests.Scope_admin_without_membership_sees_device_view_only (7.3 s)
M40 GrantFeature: target-scope subset of actor scope not required [src/Hms.Server.Application/Authorization/PermissionContracts.cs]: KILLED by PermissionTests.Feature_grant_rejected_when_target_scope_exceeds_grantor_scope, ManagementApiTests.Feature_grant_rejected_when_target_scope_exceeds_grantor_scope_over_http (7.9 s)
M41 PermissionGrantsController: GRANT_TARGET_IS_MASTER_ADMIN mapped to 403 [src/Hms.Server.Api/Controllers/PermissionGrantsController.cs]: KILLED by ManagementApiTests.Deny_on_master_admin_target_is_rejected_over_http (9.3 s)
M42 Remove: target user checked even when a membership exists (Q13 dropped) [src/Hms.Server.Application/Authorization/DeviceAccessHandlers.cs]: KILLED by DeviceAccessHandlerTests.Org_admin_can_remove_orgless_member, DeviceAccessHandlerTests.Org_admin_can_remove_orphaned_membership_of_deleted_user, DeviceAccessHandlerTests.Removing_foreign_member_still_obeys_high_risk_and_own_permission_rules, DeviceAccessHandlerTests.Org_admin_can_remove_foreign_member_from_own_device (8.8 s)
M43 OrganizationScopeResolver.Effective: denied seeds subtracted without expansion [src/Hms.Server.Application/Authorization/OrganizationScopeResolver.cs]: KILLED by AccessActorFactoryTests.Denied_organization_is_subtracted_after_expansion (8.2 s)
M44 GrantResource: GRANT_TARGET_IS_MASTER_ADMIN removed [src/Hms.Server.Application/Authorization/PermissionContracts.cs]: KILLED by PermissionTests.Deny_targeting_master_admin_is_rejected, SignalRConnectionBacklogTests.Emits_runtime_metrics_for_drop_retry_and_resynchronization (9.7 s)
M45 Resolver: SeesAllInScope without the organization-in-scope condition [src/Hms.Server.Application/Authorization/DeviceAccessResolver.cs]: KILLED by DeviceAccessPolicyTests.Resolver_sets_flags_only_for_in_scope_features (9.7 s)
M46 GrantFeature: rows always written with GrantedByMasterAdmin = false [src/Hms.Server.Application/Authorization/PermissionContracts.cs]: KILLED by PermissionTests.Grants_record_grantor_and_master_flag, PermissionTests.Non_master_cannot_reverse_master_admin_feature_deny, ManagementApiTests.Non_master_cannot_reverse_master_admin_deny_over_http (8.3 s)
M47 InMemoryAuthorizationEpochStore.AdvanceMany: Distinct dropped [src/Hms.Server.Application/Realtime/RealtimeContracts.cs]: SURVIVED (7.7 s)
M48 UpdateRole: affected devices not de-duplicated [src/Hms.Server.Application/Authorization/DeviceAccessHandlers.cs]: KILLED by DeviceAccessHandlerTests.Role_update_advances_all_affected_devices_in_one_call (8.2 s)

INTEGRITY: all 13 target files restored byte-for-byte
INTEGRITY: target files byte-identical to D:\WORKSPACE\PROJECT\HMS-SERVER
SUMMARY: KILLED=37, SURVIVED=12
```

## Cycle-2 re-run of the 12 survivors (2026-10-06)

After the cycle-2 fixes added tests for every survivor, the same harness re-ran M7, M21, M26, M27, M28, M30, M31, M32, M33, M34, M36 and M47 on a fresh copy (`scratchpad/phase03-mut-c2/`, 257 files byte-identical to the repo). No mutant needed re-targeting. Baseline green (unit 402/402, integration 103/103). **All 12 KILLED**, each by the test written for it; in the M47 run the known flaky SignalR backlog test also failed next to the real killing test `RealtimeTests.Epoch_store_advance_many_moves_each_distinct_device_once`. Verified by the controller from `phase03-mut-c2/results.txt`:

```text
BASELINE: GREEN (14.2 s) -- Hms.Server.UnitTests.dll: Passed failed=0 passed=402 skipped=0 total=402; Hms.Server.IntegrationTests.dll: Passed failed=0 passed=103 skipped=0 total=103
M7 Resolver: SeesAllInScope without devices.manage/permissions.manage [src/Hms.Server.Application/Authorization/DeviceAccessResolver.cs]: KILLED by DeviceAccessPolicyTests.In_scope_actor_without_a_management_feature_does_not_see_every_device (8.3 s)
M21 SendCommand: CONFIRM_REQUIRED checked after the cached-result return [src/Hms.Server.Application/Commands/SendDeviceCommandHandler.cs]: KILLED by DeviceCommandAuthorizationTests.Replay_of_a_confirmed_command_without_confirm_still_requires_confirm (8.2 s)
M26 Resolve: claim allow kept when a claim deny names the same key [src/Hms.Server.Application/Authorization/PermissionGrantResolver.cs]: KILLED by PermissionTests.Claim_deny_wins_over_claim_allow_on_the_same_key (9.7 s)
M27 Resolve: claim allow kept when a persisted row decides the key [src/Hms.Server.Application/Authorization/PermissionGrantResolver.cs]: KILLED by PermissionTests.Persisted_feature_grant_overrides_claim_baseline, AccessActorFactoryTests.Persisted_deny_removes_a_claim_allow_from_the_actor_features (9.7 s)
M28 DenyStands(feature): any historical master deny blocks, not the latest master row [src/Hms.Server.Application/Authorization/PermissionContracts.cs]: KILLED by PermissionTests.Non_master_may_allow_after_master_reversed_own_deny (8.2 s)
M30 Assign: re-assigning the current role is no longer a no-op [src/Hms.Server.Application/Authorization/DeviceAccessHandlers.cs]: KILLED by DeviceAccessHandlerTests.Assigning_the_current_role_again_is_a_no_op (9.6 s)
M31 CheckText: name of exactly NameMaxLength rejected (> becomes >=) [src/Hms.Server.Application/Authorization/DeviceAccessHandlers.cs]: KILLED by DeviceAccessHandlerTests.Role_create_accepts_name_and_description_at_their_maximum_length (7.9 s)
M32 StoreWrites.Try: every InvalidOperationException mapped to a code [src/Hms.Server.Application/Authorization/DeviceAccessHandlers.cs]: KILLED by DeviceAccessHandlerTests.Unexpected_store_failure_propagates_from_role_create (8.1 s)
M33 CreateRole: PolicyVersion not advanced [src/Hms.Server.Application/Authorization/DeviceAccessHandlers.cs]: KILLED by DeviceAccessHandlerTests.Role_create_accepts_server_and_config_domain_codes (9.5 s)
M34 DeleteRole: PolicyVersion not advanced [src/Hms.Server.Application/Authorization/DeviceAccessHandlers.cs]: KILLED by DeviceAccessHandlerTests.Role_delete_reports_stale_version_before_in_use (10.0 s)
M36 DeviceCommandsController: request.Confirm not passed to the handler [src/Hms.Server.Api/Controllers/DeviceCommandsController.cs]: KILLED by ManagementApiTests.Device_command_passes_confirm_through_to_the_handler (10.6 s)
M47 InMemoryAuthorizationEpochStore.AdvanceMany: Distinct dropped [src/Hms.Server.Application/Realtime/RealtimeContracts.cs]: KILLED by SignalRConnectionBacklogTests.Emits_runtime_metrics_for_drop_retry_and_resynchronization, RealtimeTests.Epoch_store_advance_many_moves_each_distinct_device_once (8.7 s)
INTEGRITY: all 13 target files restored byte-for-byte
INTEGRITY: target files byte-identical to D:\WORKSPACE\PROJECT\HMS-SERVER
SUMMARY: KILLED=12
```
