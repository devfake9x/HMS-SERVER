---
title: "Design contract — device permissions what + where"
date: 2026-10-05
status: binding
source: plans/reports/261004-2234-brainstorm-device-permissions-what-where.md
scouts: reports/scout-*-surface-map.md
---

# Design contract — device permissions what + where

Binding for every phase file. Names, routes, codes, tables below are canonical. A phase that needs to deviate must say so explicitly and update this file.

## 0. Owner decisions (2026-10-04/05)

| # | Decision |
|---|---|
| Q1 | Device contract = HMS-IDF `refactor-all-upate-v6` @ `83e93d77e4a433efce665d669b567b3acc51b735` (read-only) |
| Q2 | No fixed/seeded roles. Admins create roles per need |
| Q3 | Role library per Organization |
| Q4 | master-admin full power; sub-org admin grants only a subset of own permissions on that device |
| Q5 | WHERE = device only (v1); schema leaves room for Area/Project |
| Q6 | Out-of-org-scope (and nonexistent) device → 404; in scope but missing permission → 403 |
| Q7 | High-risk permissions: only master-admin may put them into a role or assign/modify a membership whose role contains them |
| Q8 | Role used on a device must belong to the device's owning org (no parent-org roles) |
| Q9 | Members see only devices where their role has `device.view`; scope admins see all in scope |
| Q10 | Plan mode `--tdd`, scope HOLD |
| Q11 | Containing a master-admin = in-product role removal: master-admin-only `DELETE /api/v1/users/{userId}/roles/{role}`, rotates the security stamp, refuses to remove the last master-admin (red team V7, 2026-10-05) |
| Q12 | A deny written by a master-admin cannot be reversed by a non-master; grant rows record the grantor (red team V8, 2026-10-05) |
| Q13 | An admin with `CanManageAccess` on device D may remove any existing membership on D whatever the holder's home org; self, high-risk and own-permission rules still apply; assign/replace keep the target-org rule (red team V9, 2026-10-05) |
| Q14 | Server and device talk over MQTT, so the server integrates **only through the MQTT spec** `HMS-IDF/engineering-docs/api/mqtt-api.md` at the pinned commit, never by reading firmware source. The command catalog comes from spec §11. The server implements only what the spec defines (the device is still being coded); MQTT-dependent work is paused; spec gaps are listed in §13 and not sent to the firmware team yet (owner, 2026-10-05; `plans/reports/261005-1930-brainstorm-spec-sourced-command-catalog.md`) |

Owner decisions from the phase-3 review (2026-10-06), each modelled on how large systems handle the same case:

| # | Decision |
|---|---|
| Q15 | A non-master grant **or deny** on organization X must be covered by the actor's authority over X's whole subtree: `Expand([X]) ⊆` actor scope (cut to their appointers', Q18). A grant at a node reaches everything under it, so authority must cover all of it (Kubernetes RBAC "same scope", GCP/Azure IAM per-node `setIamPolicy` / role-assignment rights). |
| Q16 | A deny `*` is lifted by revoking it, not by a competing allow (AWS IAM, GCP deny policies, Azure deny assignments). In the latest-wins ledger the revoking row is the `*` allow, which grants nothing: only a master-admin may write it; non-masters get `WILDCARD_GRANT_FORBIDDEN` for `*` either way. |
| Q17 | One grant decision (its reads and its write) runs as one step per target user, across replicas: in-memory store lock; PostgreSQL transaction holding `pg_advisory_xact_lock`; other EF providers a process lock. Closes the check-then-write races between two admins and the master-deny race. |
| Q18 | Delegation chain. An admin's appointer is the grantor of their latest `permissions.manage` allow; the chain follows appointers up and stops at a master-admin (row flag or current role), a row without grantor, or a repeat. Rule A: a non-master never grants, denies, assigns, replaces or removes anything for anyone in their chain (`GRANT_TO_DELEGATOR_FORBIDDEN` 403; "lower admins do not manage the admins above them", as in ARBAC97 administrative ranges, Microsoft Entra, Google Workspace). Rule B: what a non-master hands out (features, organizations, device permissions on D) is cut to what every appointer in the chain holds (delegation boundary: delegated ⊆ delegator, as in RBAC delegation models and AWS permissions boundaries). A master-admin re-grant of `permissions.manage` ends the chain. Cascading revocation is backlog. |

`[RTn]` marks text changed by red-team finding RTn (`from-code-reviewer-to-planner-red-team-adjudicated-findings-report.md`).

Correction vs brainstorm: spec §11 lists **46** firmware permission codes (44 on action rows + `sys.config_export`, `sys.config_import` via config domain `system`), not 45. Vocabulary = 46 firmware + 4 server = **50**.

## 1. Permission vocabulary (WHAT)

- Firmware codes: derived at runtime from the pinned catalog, which comes from spec §11 (rows `required_perm` ∪ `config_domains[*].view_perm/edit_perm`), via `ICommandCatalog.FirmwarePermissions`. Never hard-code the 46 list in C#; a test asserts count 46.
- Server codes (server-owned, C# constants): `device.view`, `device.edit_basics`, `device.edit_product`, `device.record_measurements` (`DevicePermissionCodes.DeviceView`, `.DeviceEditBasics`, `.DeviceEditProduct`, `.DeviceRecordMeasurements`, `.ServerCodes`). `device.record_measurements` is reserved in v1: no route requires it, because telemetry ingest is master-admin-only until service-identity ingest lands (brainstorm §5.6). Its code label says so. [RT5]
- The one 50-code vocabulary: `DevicePermissionVocabulary.All(ICommandCatalog)` in `src/Hms.Server.Application/Commands/DevicePermissionVocabulary.cs` = `FirmwarePermissions ∪ ServerCodes` (FrozenSet, test asserts 50). Role validation and `GET /device-permission-codes` use only this accessor.
- High-risk set (server policy, C# constant, single source): `sys.reboot`, `sys.factory_reset`, `sys.ota`, `mqtt.config_edit`, `eth.edit`, `net.priority_edit`, `wifi.sta_edit`, `wifi.sta_connect`. `sys.config_import` is not high-risk (V1 confirmed 2026-10-05): per spec §6.1, `config.set` for domain `system` accepts only the hostname, the same effect as `system.set_hostname` / `sys.hostname_edit`. <!-- Updated: Validation Session 1 - V1 confirmed; Session 2 - cited from the spec (Q14) -->
- Consequences accepted: `sys.ota` also gates read `ota.remote_config_get`; edit keys include their class D deletes (`broker.*_edit`, `wifi.known_edit`).
- Home: `src/Hms.Server.Domain/Authorization/DevicePermissionCodes.cs` (`ServerCodes`, `HighRisk`, `IsHighRisk(code)`).

## 2. Catalog v2 (spec → server)

Source (Q14): spec `engineering-docs/api/mqtt-api.md` of HMS-IDF at the pinned commit, §11 "Manifest permission để server thực thi": the action table (columns `Action | Class | Permission server | TTL default ms | Dry run | Confirm`) and the config-domain table (`Domain | config.get | config.set`). Firmware source code is never read; anything §11 does not state is not in the catalog.

Generator: `scripts/generate-command-catalog.mjs` (Node, no deps). Reads the document bytes via `git -C <hmsIdfRepo> show <commit>:engineering-docs/api/mqtt-api.md` (never a working-tree file), takes §11 up to the next ATX heading of any level and parses the two tables. It fails loudly when a table header differs, a row has the wrong number of cells, a class is not one of `R W C D B F`, a permission is neither a policy string nor `Theo domain (bảng sau)`, a TTL is not a positive decimal integer up to 86400000 (spec §3), Dry run is not `Có`/`Không`, Confirm is neither `—` nor a policy string containing `<DEVID>`, an action or domain repeats, a domain-table permission is used by anything but `config.get` (class R) and `config.set` (class not R), or the pair or namespace count differs from what §11 itself states ("94 cặp, 12 namespace"). Inside §11 it accepts only the layout §11 uses: the §11 heading appears once at column 0 and any later ATX heading ends the section; table lines start at column 0; a delimiter row has as many cells as its header; a table is followed by a blank line or the section end; the action table has at least one row; no code fence, HTML block (a line starting with `<`), blockquote (a line starting with `>`) or setext underline appears. Identifiers are checked as written; only headings, prose and the fixed Vietnamese words are compared in NFC. Not modelled: Markdown or HTML outside §11 that changes how §11 renders (an unclosed code fence or HTML comment in an earlier section). The pinned catalog hash (see the DRY exception below) and the review of a spec re-pin cover that case.

Known limitations of the parser, accepted by the owner on 2026-10-05 after the third review cycle (all low; they matter only when the spec is re-pinned, which the pinned catalog hash forces through review):
- The stated counts are matched in the §11 prose as written, so a count inside an inline HTML comment in that sentence would be read. Policy data cannot be hidden this way: inline HTML in a table cell fails the charset.
- A list item directly above a table, without a blank line, is accepted although a renderer would show the table as list text; the data read is the same as the visible text.
- A GFM table written without leading pipes inside §11 is read as prose; its rows are then absent from the catalog, which fails closed (`COMMAND_NOT_IN_CATALOG`).
- A `-` in the Permission column passes the charset and is read as a permission code; it fails closed (no role can meaningfully grant it).
- Untested checks (behaviour correct, no dedicated test): the parser's config.set charset check (the canonicalizer charset check still catches it), the section end on an empty or indented `##`, and the loader's per-row `config` type check in isolation. Writes `src/Hms.Server.Infrastructure/Commands/command-catalog.json`.

JSON v2 (top-level, in this order):
```json
{
  "catalog_version": "mqtt-api-83e93d77e",
  "catalog_hash": "<sha256 v2>",
  "canonicalization": "v2: commands sorted ordinal (type,action) with fixed field order; config_domains sorted ordinal by domain; {commands,config_domains} compact System.Text.Json; UTF-8; SHA-256 lowercase hex",
  "source_repo": "HMS-IDF",
  "source_commit": "83e93d77e4a433efce665d669b567b3acc51b735",
  "source_path": "engineering-docs/api/mqtt-api.md",
  "source_blob": "6485e00f4c609a1ba57063cfd7e7aef75955c58b",
  "source_hash": "ef41c113b001459b2cc0d0e6bb8c3fd00f8008c22bd583af8a2e72d6f33e5274",
  "config_domains": [{"domain": "adc", "view_perm": "adc.view", "edit_perm": "adc.config_edit"}],
  "commands": [{"type": "system", "action": "reboot", "class": "F", "required_perm": "sys.reboot",
                "domain_policy": false, "ttl_ms": 60000, "confirm_required": true,
                "confirm_template": "REBOOT-<DEVID>", "dry_run": true}]
}
```
- Row field order (hash input): `type, action, class, required_perm, domain_policy, ttl_ms, confirm_required, confirm_template, dry_run`. From §11: `type.action` split at the first `.`; `required_perm` = the permission, or `null` with `domain_policy = true` for `Theo domain (bảng sau)`; `ttl_ms` = the TTL default column (always a positive integer, 60000 or 120000 in this spec); `confirm_required` = Confirm ≠ `—`; `confirm_template` = the template or `null`; `dry_run` = Dry run is `Có`.
- Not in the catalog because §11 does not state them: audit publishing, body acceptance, deferred outcome, a separate dispatcher default TTL (Q14). A later plan that needs one asks for it as a §11 column (§13).
- Hash: `CommandCatalogHash.Compute` (C#, default `System.Text.Json` options) is the reference; the generator must emit identical bytes; ordinal sort (not localeCompare). The generator does not emulate the full STJ escape table [RT10]. It first enforces the policy-string charset `^[A-Za-z0-9_.<>-]+$` on every string in the hash input: row `type, action, class, required_perm, confirm_template`, and config-domain `domain, view_perm, edit_perm`. A violation is a generator error naming the row and field. It then canonicalizes with `JSON.stringify` and replaces `<` with a backslash followed by `u003C` and `>` with a backslash followed by `u003E` (six ASCII characters each, uppercase hex). Under the guard these are the only characters default STJ escapes. A `node --test` suite (charset guard, `<DEVID>` escaping, ordinal sort, LF output) runs without HMS-IDF; one JS/C# golden vector covers the real alphabet.
- `PinnedCommandCatalog` verifies the hash when the singleton is first resolved, not at process start; `CatalogTests` and every integration test that calls a catalog-dependent endpoint (`/me/command-catalog`, commands) force that load [RT10]. It fails with `COMMAND_CATALOG_HASH_MISMATCH`, parses strictly (unknown, missing or duplicate properties fail), validates the catalog as the generator does (at least one row; class letter; exactly one permission source, a domain-table permission only for `config.get` (R) and `config.set` (not R), both or neither, with a non-empty domain table; `confirm_required` ⇔ `confirm_template != null` and the template contains `<DEVID>`; `ttl_ms` in 1..86400000; unique pairs and domains; policy charset; the stored hash equal to the lowercase computed hash), projects every field it reads (`RequiredPermission`, `Class`, `PermissionByDomain`, `ConfirmTemplate`, `DryRunSupported`, `DefaultTtlMs`), and keeps `Availability = DeniedUntilActionContract` for all rows (MQTT transport paused, Q14).
- Tests read version/hash from the pinned catalog instance instead of literals wherever possible (DRY), with one deliberate exception: `CatalogTests` pins the catalog hash as a literal, so any content change, even with a recomputed hash, fails CI (CI cannot run `--check` without HMS-IDF). Re-pinning the spec updates that literal in the same change, after the regenerated catalog is checked against §11.

## 3. Domain model

`src/Hms.Server.Domain/Commands/CommandDefinition.cs`:
```csharp
public enum CommandClass { Read, Write, Control, Destructive, Physical, Flash } // R W C D B F
public sealed record DomainPermission(string ViewPermission, string EditPermission);
public sealed record CommandDefinition(
    string Type, string Action, string? RequiredPermission, CommandAvailability Availability,
    bool Dangerous, bool Idempotent, int? DefaultTtlMs,
    CommandClass Class = CommandClass.Read,
    IReadOnlyDictionary<string, DomainPermission>? PermissionByDomain = null,
    string? ConfirmTemplate = null, bool DryRunSupported = false);
```
(`FeaturePermission` renamed `RequiredPermission`; `Dangerous` = `Class == Flash`.)

`src/Hms.Server.Domain/Authorization/`:
- `DeviceRole.cs`: `readonly record struct DeviceRoleId(Guid Value)`; `DeviceRole` (Id, OrganizationId, Name ≤128 trimmed non-blank, Description ≤512?, Permissions `IReadOnlySet<string>`, Version int starts 1); `Create`, `Rehydrate`, `Update(name, description, permissions)` increments Version. Name uniqueness per org is case-insensitive via `NormalizedName = Name.Trim().ToUpperInvariant()` (persisted column + unique index). Invalid name/description → handler code `DEVICE_ROLE_INVALID` (400), never an unhandled `ArgumentException`.
- `DeviceMembership.cs`: `sealed record DeviceMembership(UserId UserId, DeviceId DeviceId, DeviceRoleId RoleId, UserId GrantedBy, DateTimeOffset GrantedAt)`.
- `AccessActor.cs`: `sealed record AccessActor(UserId UserId, bool IsMasterAdmin, IReadOnlySet<OrganizationId> EffectiveOrganizations, IReadOnlySet<string> FeaturePermissions)` — replaces `AuthorizationContext` (deleted together with `DeviceAuthorizationPolicy`, device-action sets, `*` device wildcard).
- `DeviceAccessPolicy.cs`:
```csharp
public sealed record DeviceAccess(bool IsMasterAdmin, bool InScope, bool SeesAllInScope, bool CanManageAccess,
    DeviceRoleId? RoleId, IReadOnlySet<string> Permissions);
public sealed record DeviceAccessDecision(bool Allowed, string Code);
public static class DeviceAccessPolicy {
  // order: scope → revoked → master-admin → permission
  public static DeviceAccessDecision Evaluate(DeviceAccess access, Device device, string requiredPermission);
}
```
  Order: `!IsMasterAdmin && !InScope` → `DEVICE_NOT_FOUND`; `device.State == Revoked` → `DEVICE_REVOKED`; `IsMasterAdmin` → `ALLOWED`; `requiredPermission == device.view && SeesAllInScope` → `ALLOWED`; `Permissions.Contains(requiredPermission)` → `ALLOWED`; else `DEVICE_PERMISSION_REQUIRED`. Codes live in `DeviceAccessCodes` (Domain).
- `InScope` = device org ∈ `actor.EffectiveOrganizations` **or** the actor has a membership on that device. The user's home org (`ApplicationUser.OrganizationId`) does not grant scope (unchanged semantics: admin scope comes from resource grants). A membership is itself a WHERE grant for that one device.
- `SeesAllInScope` = master-admin, or feature `devices.manage`/`permissions.manage` with device org ∈ effective orgs (a membership alone never grants it). `CanManageAccess` = master-admin, or feature `permissions.manage` with device org ∈ effective orgs.
- Feature permissions: an allow `*` no longer grants anything (removed from `ScopeAuthorization` and `FeaturePermissionAuthorizationHandler`); a deny `*` still denies everything for non-master-admins.
- master-admin is unconditional ("toàn quyền", Q4): feature denies and resource-scope denies do not restrict a master-admin anywhere (feature routes, effective orgs = all orgs, device access). To restrict a master-admin, another master-admin removes the role with `DELETE /api/v1/users/{userId}/roles/master-admin` (Q11). That rotates the security stamp, so the HTTP cookie is rejected at the next security-stamp validation (interval 1 minute) and the realtime principal refresh drops the session within one tick. This replaces the old deny-beats-master-admin behaviour. Grant handlers reject a deny whose target holds the master-admin role (`GRANT_TARGET_IS_MASTER_ADMIN` 400), so an operator never sees a no-op deny succeed. [RT1]
- Feature routes (e.g. `devices.manage`, `provisioning.manage`, `licenses.manage`) require the device org ∈ effective orgs for their feature check; a device membership never widens feature-permission access.

## 4. Application layer

`src/Hms.Server.Application/Authorization/DeviceAccessContracts.cs`:
- `IDeviceRoleStore`: `Find(DeviceRoleId)`, `ListByOrganization(OrganizationId)`, `Add(DeviceRole, DeviceAccessAuditEntry)`, `Update(DeviceRole, DeviceAccessAuditEntry)` (compare-and-set on Version; stale → `DEVICE_ROLE_VERSION_STALE`), `Remove(DeviceRoleId, int expectedVersion, DeviceAccessAuditEntry)` (order: missing → `DEVICE_ROLE_NOT_FOUND`, stale version → `DEVICE_ROLE_VERSION_STALE`, in use → `DEVICE_ROLE_IN_USE`).
- `IDeviceMembershipStore`: `Find(UserId, DeviceId)`, `ListByDevice(DeviceId)`, `ListByRole(DeviceRoleId)`, `ListByUser(UserId)`, `Upsert(DeviceMembership, DeviceRoleId? expectedRoleId, DeviceAccessAuditEntry)`, `Remove(UserId, DeviceId, DeviceRoleId? expectedRoleId, DeviceAccessAuditEntry)`. Store-level compare-and-set on the current role (`null` = no membership expected); mismatch → `DEVICE_MEMBERSHIP_CHANGED`.
- `DeviceAccessAuditEntry(Guid AuditId, UserId Actor, string Change, OrganizationId? OrganizationId, DeviceId? DeviceId, UserId? TargetUserId, DeviceRoleId? RoleId, string? PreviousValue, string? NewValue, DateTimeOffset OccurredAt)`; `Change` ∈ `ROLE_CREATED, ROLE_UPDATED, ROLE_DELETED, MEMBERSHIP_ASSIGNED, MEMBERSHIP_CHANGED, MEMBERSHIP_REMOVED` (`DeviceAccessChanges`). Previous/new values are compact JSON snapshots (unbounded `text` column).
- InMemory implementations (thread-safe with a lock; singleton in dev like other InMemory stores; one `InMemoryDeviceAccessStore` may implement both interfaces so in-use/exists checks mirror FK Restrict).
- `IDeviceAccessResolver` / `DeviceAccessResolver.Resolve(AccessActor, Device) → DeviceAccess` in `src/Hms.Server.Application/Authorization/DeviceAccessResolver.cs` (one membership read + one role read). Registered scoped (works with InMemory-singleton and EF-scoped stores).
- `src/Hms.Server.Api/Authorization/AccessActorFactory.cs` (created in the access-evaluation phase, consumed by HTTP and realtime phases): `static AccessActor? FromPrincipal(ClaimsPrincipal principal, IOrganizationStore organizations, IPermissionStore permissions)` — no `HttpContext`; missing/invalid `NameIdentifier` → `null` (fail closed). Effective orgs = latest-wins resource grants (+ test-only `org-scope` claims) expanded to descendants, minus denies; master-admin = all orgs (no deny subtraction).
- `src/Hms.Server.Application/Authorization/DeviceAccessHandlers.cs`: `CreateDeviceRoleHandler`, `UpdateDeviceRoleHandler`, `DeleteDeviceRoleHandler`, `AssignDeviceMembershipHandler`, `RemoveDeviceMembershipHandler`. Each returns `DeviceAccessOutcome(string Code, DeviceRole? Role = null, DeviceMembership? Membership = null)`; `Code == "OK"` on success.
- Write ordering (no new transaction infra): validate → store write + audit row in one `SaveChanges` → post-commit signals: `IPermissionStore.Advance()`, then the epoch advance. A membership replace or remove advances that device's epoch. A fresh assign (no current membership) advances no epoch, because nobody's access shrinks. A role update advances every distinct device of `ListByRole(roleId)` in one `IAuthorizationEpochStore.AdvanceMany(devices)` call; role delete has no memberships (V5). The signals are best-effort: each is wrapped separately, a failure is logged with the audit id and device ids, and the handler still returns `OK`, because the write is committed and HTTP and realtime re-check access live. A crash between steps fails closed within one tick. [RT7]
- Feature/resource grants: `PermissionGrantResolver` (latest by `PolicyVersion` per key wins; equal `PolicyVersion` on the same key → deny wins, deterministic and fail-closed [RT9]; claims are a test-only baseline that any persisted row for the same key overrides) used by `AccessActorFactory`, `ScopeAuthorization`, `FeaturePermissionAuthorizationHandler`, `MeController`, `PermissionGrantsController` snapshot. It exposes row-level helpers `LatestFeatureGrants(grants)` / `LatestResourceGrants(grants)` (latest row per key) that both the set resolution and the snapshot use — no second inline latest-wins implementation. `GrantFeaturePermissionHandler` / `GrantResourceScopeHandler` take the actor and enforce: the `*` key is written only by a master-admin, so allow `*` and deny `*` from a non-master are rejected (`WILDCARD_GRANT_FORBIDDEN` 400); a master-admin allow `*` grants nothing and only lifts an earlier deny `*` (Q16); no self-grant unless master-admin (`SELF_GRANT_FORBIDDEN` 403); a non-master never acts on anyone in their delegation chain (`GRANT_TO_DELEGATOR_FORBIDDEN` 403, Q18); grantor must currently hold the feature permission (feature allow grants) unless master-admin (`GRANT_EXCEEDS_OWN_PERMISSIONS` 403). Further rules [RT4, RT13, RT1]:
  - Non-master feature allow: the target's current effective orgs ⊆ actor effective orgs. The target's orgs are its persisted latest-wins resource grants, expanded to descendants, minus denies. Non-master resource allow: the target's current allowed features ⊆ actor features. Both return `GRANT_EXCEEDS_OWN_PERMISSIONS` 403.
  - Non-master resource grant or deny on org X: `Expand([X])` ⊆ actor effective orgs, else `GRANT_EXCEEDS_OWN_PERMISSIONS` 403 (Q15).
  - For a non-master, "actor features" and "actor effective orgs" in these rules are cut to what every appointer in their delegation chain holds (persisted latest-wins grants of each appointer) (Q18, Rule B).
  - Check and write run inside `IPermissionStore.WithUserLock(target, …)` (Q17): the in-memory store takes its lock; `EfPermissionStore` opens a transaction and takes `pg_advisory_xact_lock(namespace, key(target))` on PostgreSQL (the policy counter and the row are written in that transaction), and a process lock on other providers.
  - Grant rows record `GrantedBy` and `GrantedByMasterAdmin` (§8), stamped from the actor by both handlers. A non-master allow on a key whose latest master-issued row (`GrantedByMasterAdmin = true`) is a deny → `GRANT_OVERRIDES_MASTER_DECISION` 403 (Q12). The handler compares against the latest master-issued row, not the latest row overall, so writing an own deny first cannot launder the reversal. Non-master denies stay allowed. Legacy rows (`GrantedByMasterAdmin = false`) are not protected.
  - A deny whose target holds the master-admin role → `GRANT_TARGET_IS_MASTER_ADMIN` 400 (Q4, Q11).
  
  The access-evaluation phase owns these handler rules, the `PermissionGrantsController` outcome mapping, and their tests; the HTTP phase only deletes the device-actions endpoint and changes the snapshot. `/me/permissions` keeps a deny `*` in `deniedPermissions` (the frontend honours it).
- Deleted: `DeviceActionGrant`, `GrantDeviceActionHandler`, `IPermissionStore.GrantDeviceAction/DeviceActionGrants`, `POST /api/v1/permissions/device-actions`, claim parsing of `device-action`/`deny-device-action`, master-admin first-org fallback in `DeviceCommandsController`.

### Delegation rules (handlers)

Let `perms(actor, D)` = permissions of actor's own membership role on device D (empty if none), cut for a non-master to the role each appointer in their delegation chain holds on D (an appointer without a role on D leaves nothing) (Q18, Rule B). Let `H` = high-risk set.
- A non-master never assigns, replaces or removes the membership of anyone in their delegation chain (`GRANT_TO_DELEGATOR_FORBIDDEN` 403, Q18 Rule A); checked after the target and self rules. Editing a role acts on everyone holding it, so a non-master may not update a role held by anyone in their chain (same code, checked after the version and high-risk checks, before the per-device subset check).
- A current membership whose role cannot be read counts as stronger than any non-master (`GRANT_EXCEEDS_OWN_PERMISSIONS` 403) on replace and remove: fail closed.
- Manage memberships on D: master-admin, or `CanManageAccess` for D. Else `PERMISSION_MANAGEMENT_REQUIRED` (403); D out of scope → `DEVICE_NOT_FOUND` (404).
- Target user (assign/replace, and remove when no membership exists): must exist and its org ∈ actor effective orgs (else `USER_NOT_FOUND` 404). Target ≠ actor unless master-admin (`SELF_GRANT_FORBIDDEN` 403) for every membership write.
- Remove of an existing membership (Q13) [RT14]: checks the stored membership instead of `IUserScopeStore`; the holder's home org is not checked, so a member from another org, an org-less member and an orphaned membership of a deleted user are all removable by an admin with `CanManageAccess` on D. Self, high-risk and own-permission rules below still apply.
- Assign/replace with role R: R exists (`DEVICE_ROLE_NOT_FOUND` 404); R.OrganizationId == D.OwnerOrganizationId (`DEVICE_ROLE_ORGANIZATION_MISMATCH` 400); `expectedRoleVersion` == R.Version, else `DEVICE_ROLE_VERSION_STALE` 409, checked after the two checks above and before any write, for every actor [RT3]; compare-and-set `expectedRoleId` vs current (`DEVICE_MEMBERSHIP_CHANGED` 409).
- Non-master-admin: R.Permissions ∩ H = ∅ and current role (if any) ∩ H = ∅ (`HIGH_RISK_REQUIRES_MASTER_ADMIN` 403); R.Permissions ⊆ perms(actor, D) and current role ⊆ perms(actor, D) (`GRANT_EXCEEDS_OWN_PERMISSIONS` 403). Remove uses the same current-role rules.
- Role create (org O): master-admin or `permissions.manage` with O ∈ effective orgs (else 404 `ORGANIZATION_NOT_FOUND` if out of scope, 403 `PERMISSION_MANAGEMENT_REQUIRED`); valid name/description (`DEVICE_ROLE_INVALID` 400); name unique per org, case-insensitive (`DEVICE_ROLE_NAME_TAKEN` 409); permissions ⊆ `DevicePermissionVocabulary.All` (`DEVICE_PERMISSION_UNKNOWN` 400); non-master: no H.
- Role update: as create + `expectedVersion` (`DEVICE_ROLE_VERSION_STALE` 409). Non-master: old and new permissions free of H, checked even when R is assigned nowhere; and (old ∪ new) ⊆ perms(actor, D) for every device D where R is assigned (`GRANT_EXCEEDS_OWN_PERMISSIONS`). This is the V4 current-role rule applied to every holder: an admin cannot shrink or empty a role that exceeds their own permissions. [RT2]
- Role delete: `expectedVersion` required (`DEVICE_ROLE_VERSION_STALE` 409, checked before in-use); rejected while any membership uses it (`DEVICE_ROLE_IN_USE` 409); non-master: role free of H.
- A sub-org admin without a membership on device D has perms(actor, D) = ∅ and therefore cannot delegate anything on D (master-admin assigns the admin a role first). Org-level role assignment is v2 (Area/Project/Org inheritance). V6 confirmed 2026-10-05: v1 strict. <!-- Updated: Validation Session 1 - V6 confirmed -->
- Handlers depend on `IUserScopeStore` (`Exists`, `Organization`, new `IsMasterAdmin(UserId)` for the grant-target rule [RT1]) for the target-user rule; target with no org is out of scope for non-master actors. Test fixtures provide a small in-memory `IUserScopeStore` double.

### User management [RT1, RT8]

- `IUserManagement.RemoveRoleAsync(string userId, string role, UserManagementActor actor, CancellationToken)`; the endpoint admits only master-admins, and the method keeps its own reserved-role check as defense in depth. Order: a reserved role needs a master-admin actor (`RESERVED_ROLE_FORBIDDEN`) → `USER_ID_INVALID` / `USER_NOT_FOUND` / `ROLE_NOT_FOUND` → removing the last holder of `master-admin` → `LAST_MASTER_ADMIN` → `RemoveFromRoleAsync` (Identity error codes, e.g. `UserNotInRole`, pass through) → `UpdateSecurityStampAsync` → `IPermissionStore.Advance()`. `AssignRoleAsync` also rotates the stamp after a successful add.
- `SecurityStampValidatorOptions.ValidationInterval` = 1 minute, so a demoted session loses HTTP access within a minute.
- `RoleAuthorizationPolicy.IsReserved` compares role names the way Identity's default normalizer does (NFC, then upper-invariant, ordinal), and `IdentityUserManagement` also compares through the role manager's own `NormalizeKey`: a lookalike name that Identity folds onto `master-admin` (U+017F long s) is the reserved role for create, assign and remove.
- `IUserManagement.ListDirectoryAsync(CancellationToken) → IReadOnlyList<DirectoryUser>`; `DirectoryUser(UserId UserId, string Email, OrganizationId? OrganizationId)`. One materialized projection over `AspNetUsers` (`ToListAsync`), no per-user role or org query. Memberships GET uses it instead of `ListAsync` + `IUserScopeStore.Organization`.

## 5. Commands

- `CommandPermissionResolver.Resolve(CommandDefinition, IReadOnlyDictionary<string, object?> payload) → string?` (Application/Commands): fixed `RequiredPermission`, or for `domain_policy` rows read `payload["domain"]` — only a JSON string (`string` or `JsonElement.ValueKind == String`), exact case-sensitive match; Read class → view perm, else edit perm; anything else → null. Exact, string-only matching is server policy: spec §6.1 lists the six lowercase domains and §5 answers `permission_unmapped` for an unmapped one, but the spec states no matching rule (gap G3, §13). Choosing view or edit by class equals §11's per-column binding (`config.get` → view column, `config.set` → edit column) because the generator and the loader only accept a catalog where `config.get` is R and `config.set` is not.
- `SendDeviceCommandHandler.HandleAsync(DeviceCommandRequest, AccessActor, CancellationToken)`; right after the device lookup, a device out of the actor's scope answers `DEVICE_NOT_FOUND` exactly like a missing one, before any catalog-stage code (`CATALOG_STALE`, `COMMAND_NOT_IN_CATALOG`, …) could reveal it exists; after catalog lookup + availability: permission null → `PERMISSION_UNMAPPED`; `DeviceAccessPolicy.Evaluate(resolver.Resolve(actor, device), device, permission)`; confirm-required row and blank `request.Confirm` → `CONFIRM_REQUIRED` (exact token vs firmware MQTT device_id deferred to transport plan). TTL: `DefaultTtlMs ?? 120000`.
- `DeviceCommandRequest` and `SendCommandRequest` gain `string? Confirm`.
- Codes removed: `FEATURE_PERMISSION_REQUIRED/DENIED`, `RESOURCE_SCOPE_REQUIRED/DENIED`, `DEVICE_ACTION_PERMISSION_REQUIRED/DENIED`. Replaced by `DEVICE_NOT_FOUND`, `DEVICE_REVOKED`, `DEVICE_PERMISSION_REQUIRED`.
- `DeviceCommandsController`: builds `AccessActor` (via `AccessActorFactory`); resolves device + scope first (out-of-scope/nonexistent → 404 `DEVICE_NOT_FOUND`, before the handler); maps handler `DEVICE_PERMISSION_REQUIRED` and `DEVICE_REVOKED` → 403 with body, handler `DEVICE_NOT_FOUND` (device deleted after the gate) → 404; all other codes stay 200 with body (catalog/idempotency contract unchanged).
- Between the access-evaluation phase and the realtime phase, membership/role writes advance the global `PolicyVersion`, which the old realtime fence still checks; and the old frontend calls endpoints removed by the HTTP phase until the frontend phase lands. Release only after the frontend phase (phases 4–6 ship in one release).

## 6. HTTP surface

Shared Api helpers (`src/Hms.Server.Api/Authorization/`): `AccessActorFactory.FromPrincipal(...)` (from the access-evaluation phase), `DeviceGate` (scoped, registered in `Program.cs`): `Check(actor, deviceId, requiredPermission) → (Device?, DeviceAccessDecision)`, `FindInScope(actor, deviceId) → Device?` (for routes that keep feature permissions, e.g. provisioning/licenses), `ToResult(decision, string? requiredPermission = null)`: `DEVICE_NOT_FOUND`→404 `{code}`, `DEVICE_REVOKED`→403 `{code}`, `DEVICE_PERMISSION_REQUIRED`→403 `{code, permission}`. Every device route decides scope through `DeviceGate` (no direct `ScopeAuthorization.CanAccessDevice`).

DTO rules: every id in new request/response DTOs is a plain `Guid` (never a strong-id type, which serializes as `{value}`); the existing grant endpoints (`/api/v1/permissions/*`) keep the grant-record shape, so their ids, `grantedBy` included, stay `{value}` (owner, 2026-10-05; the frontend reads both shapes); `class` serializes as enum name; `permissionByDomain` = `{ [domain]: permission }` already resolved for that action's class (the §11 config-domain column for that action); memberships GET = `{deviceId, members:[{userId, displayName, email, roleId?, roleName?, canEdit, canRemove}], roles:[{roleId, name, permissions, version, assignable}]}` (`canEdit` = may replace; `canRemove` = may remove under Q13 [RT14]; a membership whose user is missing from the user directory is still listed with `email = ""`, `displayName` = the user id, `canEdit = false`; role `version` feeds `expectedRoleVersion` [RT3]); device role DTO = `{roleId, organizationId, name, description, permissions, version}`; permission-code DTO = `{code, group, highRisk, label, commands[]}`, where `commands` = `string[]` of `type.action` or `type.action:domain` and `label` comes from a static server map (the 4 `device.*` codes carry a title and description; firmware codes use the code itself in v1, because spec §11 gives no labels, gap G4) [RT12]; `my-access` for an in-scope revoked device = 200 with empty `permissions`.

| Endpoint | Rule |
|---|---|
| `GET /api/v1/devices` | master-admin all; else (`SeesAllInScope` AND org ∈ effective orgs) OR (the actor's membership on that device has a role with `device.view` — even when the org is outside effective orgs) |
| `GET /devices/{id}/my-access` (new) | 404 out of scope; 200 `{deviceId, isMasterAdmin, seesAllInScope, canManageAccess, roleId, roleName, permissions[]}` |
| `PUT /devices/{id}` | `devices.manage` in scope, or membership: rename needs `device.edit_basics`, classification change needs `device.edit_product` |
| `POST /devices`, `POST /devices/{id}/revoke` | `devices.manage` + scope (out of scope → 404) |
| `GET /devices/{id}/realtime-authorization`, `GET /devices/{id}/telemetry` | `device.view` |
| `POST /devices/{id}/telemetry` | master-admin only in v1: out of scope → 404; Revoked → 403 `DEVICE_REVOKED`; non-master in scope → 403 (`Forbid()`, like other master-admin-only routes). CSRF unchanged — out of scope [RT5] |
| `POST /devices/{id}/commands` | §5 |
| Provisioning, Licenses (device-scoped) | feature perms unchanged; out of scope → 404 |
| `GET/PUT/DELETE /api/v1/devices/{deviceId}/memberships[/{userId}]` (new, replaces `/permissions`) | §4 rules; PUT body `{roleId, expectedRoleId, expectedRoleVersion}` [RT3]; DELETE `?expectedRoleId=`; GET rows `{userId, displayName, email, roleId?, roleName?, canEdit, canRemove}` + `roles[{roleId,name,permissions,version,assignable}]`; GET runs a fixed number of queries (`ListDirectoryAsync` + `ListByDevice` + `ListByOrganization` + resolver) [RT8] |
| `GET/POST /api/v1/organizations/{organizationId}/device-roles`, `PUT/DELETE .../{roleId}` (new) | §4 rules; PUT body `{name, description, permissions[], expectedVersion}`; DELETE `?expectedVersion=` |
| `GET /api/v1/device-permission-codes` (new) | authenticated; `[{code, group, highRisk, label, commands[]}]` [RT12] |
| `DELETE /api/v1/users/{userId}/roles/{role}` (new) [RT1] | master-admin only, for every role (Q11): non-master → 403 (`Forbid()`), even with `users.manage`; CSRF; unknown target → 403 like assign; `IUserManagement.RemoveRoleAsync` (§4 User management); 204, errors → 400 `{errors}` like assign |
| `GET /api/v1/permissions/{userId}` | latest row per key, each row with `grantedBy` and `grantedByMasterAdmin` [RT13] |
| `GET /api/v1/me/permissions` | + `userId`, `isMasterAdmin`; latest-wins; no device-action entries; no `*` append |
| `GET /api/v1/me/command-catalog` | action DTO: `type, action, available, class, requiredPermission, permissionByDomain, requiresConfirmation (catalog), dryRunSupported, ttlMs, highRisk` (replaces `featurePermission`, `dangerous`-derived confirm) |
| `POST /api/v1/permissions/device-actions` | **deleted** |
| `DevicePermissionsController` | **deleted** (replaced by memberships) |

Optimistic concurrency uses per-membership `expectedRoleId` and per-role `expectedVersion`, never the global `PolicyVersion`.

## 7. Realtime

- Hub `EnsureAuthorized` and the 3 delivery checks use `DeviceAccessResolver` + `DeviceAccessPolicy` with `device.view`. Hub errors: `DEVICE_NOT_FOUND` (missing or out of scope), `DEVICE_REVOKED`, `DEVICE_PERMISSION_REQUIRED`, `STALE_AUTHORIZATION_EPOCH`.
- Reason constants (`RealtimeContracts.cs` `DeviceRevokedReasons`): `DEVICE_REVOKED`; `ACCESS_REMOVED` (only when the access evaluation of a successfully refreshed principal denies the subscription: out of scope, device missing, no `device.view`, or no actor can be built; never for a principal-refresh failure); `AUTHORIZATION_CHANGED` (epoch moved, access still allowed → client re-joins); `REALTIME_RESYNC_REQUIRED` (existing; also every principal-refresh failure — user missing, locked out, stamp changed, or lookup error — because the server cannot tell transient from permanent, so the client state stays non-terminal) [RT6].
- `RealtimeEventFence` = per-device epoch only (global `PolicyVersion` clause removed; send-time PolicyVersion eviction removed).
- Each delivery check resolves access live; there is no per-tick cache. Cache the actor or principal per connection only if measurement shows the need [RT11].
- Every membership replace/remove and every role update evicts all subscribers of the affected devices: `ACCESS_REMOVED` for those whose access evaluation now fails, `AUTHORIZATION_CHANGED` for everyone else; their in-flight events for those devices are dropped. This is accepted in v1 because the only event type is the coalescible `status`; the client re-joins with bounded retry and refetches telemetry (§9) [RT7].

## 8. Persistence

| Table | Columns (PascalCase) | Keys / indexes |
|---|---|---|
| `device_roles` | Id, OrganizationId, Name(128), NormalizedName(128), Description(512 null), Version (EF concurrency token), CreatedBy, CreatedAt, UpdatedAt | PK Id; unique (OrganizationId, NormalizedName); FK OrganizationId→organizations Restrict |
| `device_role_permissions` | RoleId, Permission(160) | PK (RoleId, Permission); FK RoleId→device_roles Cascade |
| `device_memberships` | Id, UserId, DeviceId, RoleId (EF concurrency token, model only, no DDL), GrantedBy, GrantedAt | PK Id; unique (UserId, DeviceId); idx DeviceId; idx RoleId; FK DeviceId→devices Restrict; FK RoleId→device_roles Restrict |
| `device_access_audits` | AuditId, Actor(256), Change(32), OrganizationId?, DeviceId?, TargetUserId?, RoleId?, PreviousValue (text)?, NewValue (text)?, OccurredAt | PK AuditId; idx (DeviceId, OccurredAt); idx (RoleId, OccurredAt) |
| `permission_grants` (existing) [RT13] | + GrantedBy (uuid, null for legacy rows), GrantedByMasterAdmin (bool, default false) | unchanged |

- Migration generated with `dotnet ef migrations add AddDeviceRolesAndMemberships` (design-time factory = Npgsql) + two hand-added statements, in this order [RT9]:
  1. `UPDATE policy_versions SET "Version" = GREATEST("Version", COALESCE((SELECT MAX("PolicyVersion") FROM permission_grants), 0)) WHERE "Id" = 1;` — aligns the counter with existing grant rows, so later `Advance()` values sort above them.
  2. `DELETE FROM permission_grants WHERE "Kind" = 'device-action';`
- Second migration `AddPermissionGrantGrantor` (generated) adds the two `permission_grants` columns [RT13].
- No FK to AspNetUsers (no precedent). Role-org invariant enforced in handlers (owner org and parent are immutable).
- EF InMemory does not enforce unique/FK → stores find-then-update explicitly; constraints proven with model-metadata tests.

## 9. Frontend

- Consolidate onto `frontend/src/core/**` for modules this change edits: delete top-level `adminApi.ts`, `permissions.ts`, `commandAvailability.ts`, and the realtime trio `useDeviceOperationsHub.ts`, `signalRConnection.ts`, `realtimeAuthorization.ts` together (they import each other); repoint `App.vue`, `PermissionGrantEditor.vue`; move their tests next to core copies.
- `core/api/adminApi.ts`: `request()` throws `ApiError` (`core/http/errors.ts`) carrying status + body `code`; new methods `listDeviceRoles, createDeviceRole, updateDeviceRole, deleteDeviceRole, devicePermissionCodes, deviceMemberships, assignDeviceMembership, removeDeviceMembership, myDeviceAccess, removeRole` (`removeRole` [RT1]; `assignDeviceMembership` sends `expectedRoleVersion` [RT3]); remove `devicePermissions, updateDevicePermissions, grantDeviceAction`. All ids are strings (server sends plain Guids).
- `core/auth/permissions.ts`: order = `isMasterAdmin` bypass (unconditional, mirrors the server) → deny (exact or `*`) → exact allow; allow `*` grants nothing; remove `canDeviceAction` and `*` device paths; add `canOnDevice(access, code)` fail-closed.
- `DeviceCommandModal` resolves config domain permissions with `action.permissionByDomain?.[domain] ?? null` (no class branch).
- Docs: the UI section of `docs/architecture-device-permissions-parity.md` is updated by the frontend phase (the HTTP phase rewrites the rest).
- New `features/security/views/DeviceRolesView.vue` (+ `components/DeviceRoleEditor.vue`), route `/admin/device-roles` name `admin-device-roles`, nav link with `permission: 'permissions.manage'`.
- `features/devices/components/DeviceMembersTab.vue` replaces `DevicePermissionsTab.vue` (own row read-only except for master-admin, matching the server's `canEdit`; non-assignable roles disabled with reason; 404/403/409 states). Before saving it shows the selected role's permissions (grouped, high-risk badge) and sends `expectedRoleVersion`; a row with `canRemove && !canEdit` offers only a remove action [RT3, RT14].
- `DeviceRoleEditor` shows each code with its `label` and, as secondary text, the commands it gates [RT12].
- `features/security/views/UserManagementView.vue`: a master-admin sees a remove action (with confirm) for each assigned role, calling `removeRole` [RT1].
- `DeviceDetailView.vue`: fetch `myDeviceAccess`, gate tabs/buttons, distinct not-found/forbidden states, pass access to `DeviceCommandModal`.
- `DeviceCommandModal.vue`: gate on `requiredPermission` (config.*: resolve from params JSON `domain`), confirm input when `requiresConfirmation`, danger from `class`/`highRisk`.
- Hub: `AUTHORIZATION_CHANGED` → refetch epoch + re-join, at most 3 attempts in total with jittered backoff (250 ms–2 s) between them, retrying on `STALE_AUTHORIZATION_EPOCH` or a transient epoch-fetch failure; stop on `DEVICE_NOT_FOUND`, `DEVICE_PERMISSION_REQUIRED`, `DEVICE_REVOKED`, a hub/session change or `leaveDevice`; after the last failure keep `requiresReauthorization` and call the consumer's `onRejoinFailed(deviceId, code)`; after a successful re-join the view refetches telemetry [RT7]. `ACCESS_REMOVED` → access-removed state; `DEVICE_REVOKED` → device Revoked; `REALTIME_RESYNC_REQUIRED` → realtime disconnected, manual re-join (also used for principal-refresh failures) [RT6].
- New primary buttons use `shared/ui/primitives/Button.vue` `variant="primary"` (brand orange).

## 10. Out of scope (this plan)

Everything MQTT beyond what spec §11 defines (paused, Q14); MQTT transport / envelope / HMAC; enabling catalog availability; exact confirm token vs firmware device_id; Area/Project inheritance; runtime capabilities drift check; role cloning across orgs; route-guard framework and nav subject wiring beyond the new link; CSRF on telemetry ingest; telemetry ingest by a service identity and the manual-measurements endpoint that `device.record_measurements` will gate [RT5]; per-user idempotency scoping and moving the idempotency precheck after authorization (input for the transport plan, which enables catalog rows); user lockout/disable; `CommandTransportController` fake status; SignalR backplane; exact-hex brand audit; legacy App.vue template removal.

Permission labels: spec §11 gives none and firmware source is not read (Q14), so firmware codes are labelled with the code itself and `commands[]` (derived from §11) explains what each one gates [RT12]; a one-line meaning per code is spec gap G4 (§13).

## 11. Validation questions (answered 2026-10-05, Validation Session 1)

<!-- Updated: Validation Session 1 - V1-V6 answered -->
| # | Question | Answer |
|---|---|---|
| V1 | Add `sys.config_import` to high-risk? | No (same effect as `sys.hostname_edit` on `83e93d77e`) |
| V2 | Command permission denial as HTTP 403 (vs 200 + code)? | 403 with body `{code}` |
| V3 | Delete legacy `device-action` grant rows in the migration? | Yes (back up first) |
| V4 | Symmetric current-role rule for remove/replace and role update [RT2]? | Yes |
| V5 | Reject role delete while in use? | Yes, 409 `DEVICE_ROLE_IN_USE` |
| V6 | Sub-org admin without a membership on a device cannot delegate on it (v1 strict) vs an org-level admin role now? | v1 strict; org-level roles are v2 |

Also re-confirmed: Q11 remove-role endpoint stays master-admin-only for every role.

Red-team decisions V7–V9 are already answered by the owner: see §0 Q11–Q13.

## 12. Execution order

True dependencies: 1, 2 → 3 → 4, 5 → 6. Phases share the two .NET test projects and the frontend test suite, so in a single working tree run them strictly in order 1 → 2 → 3 → 4 → 5 → 6; parallel only in separate worktrees. Line numbers cited in later phases predate earlier phases' edits — locate edits by symbol. Release only after phase 6 (phases 4–6 in one release).

## 13. Spec gaps (waiting list, Q14)

Recorded 2026-10-05; **not sent** to the firmware team yet. Nothing here is implemented until the spec covers it.

| # | Missing from `mqtt-api.md` | Why the server needs it |
|---|---|---|
| G1 | §11 in a machine-readable form (JSON), or a commitment to keep the table columns stable | The generator parses a Markdown table and fails as soon as its layout changes |
| G2 | A manifest version or hash in §11 and in `B/capabilities` or `system.get_info` | Lets the server detect a device whose manifest differs from the pinned spec |
| G3 | The matching rule for `config.get/set` `domain` (case, string only, where it is read) | The server currently chooses exact, string-only matching on its own |
| G4 | A one-line meaning per permission code (optional) | Shown next to each code in the role editor |
| G5 | One name for the confirm placeholder: §11 writes `<DEVID>`, §7 writes `<device_id>` | The server checks templates for `<DEVID>`; the token itself is built by the transport plan |

Also expected: when the firmware adds SMS write actions over MQTT (work in progress on the firmware side), §11 changes and the server re-pins the spec.
