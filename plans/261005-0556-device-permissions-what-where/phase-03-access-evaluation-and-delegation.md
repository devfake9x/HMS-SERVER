---
phase: 3
title: "Kiểm quyền thiết bị và uỷ quyền"
status: pending
priority: P1
dependencies: [1, 2]
---

# Phase 3: Kiểm quyền thiết bị và uỷ quyền

## Context Links

- Hợp đồng (BINDING): [design-contract.md](./reports/design-contract.md) §0 (Q4, Q6–Q9, Q11–Q13), §1 (`DevicePermissionVocabulary.All`, high-risk), §3 (AccessActor, DeviceAccess, `InScope`/`SeesAllInScope`/`CanManageAccess`, DeviceAccessPolicy, feature `*`, master-admin vô điều kiện), §4 (resolver, `AccessActorFactory`, store CAS, handlers, delegation rules, write ordering, `PermissionGrantResolver` + `LatestFeatureGrants`/`LatestResourceGrants`, luật grant và ranh giới sở hữu với phase HTTP), §5 (handler wiring, hàng rào `PolicyVersion`), §6 (`AccessActorFactory` dùng lại ở HTTP; `POST /devices` ngoài phạm vi → 404), §9 (frontend soi gương thứ tự `isMasterAdmin` → deny → allow), §11 (V1–V6), §12 (thứ tự chạy)
- Scout: [scout-authz-core-surface-map.md](./reports/scout-authz-core-surface-map.md), [scout-device-surfaces-surface-map.md](./reports/scout-device-surfaces-surface-map.md), [scout-persistence-surface-map.md](./reports/scout-persistence-surface-map.md)
- Brainstorm: `plans/reports/261004-2234-brainstorm-device-permissions-what-where.md` §5.1, §5.5
- Phụ thuộc: phase 1 (`CommandDefinition` v2, `DevicePermissionCodes`, `CommandPermissionResolver`, `DevicePermissionVocabulary.All(ICommandCatalog)`), phase 2 (`DeviceRole` + `NormalizedName`, `DeviceMembership`, `IDeviceRoleStore` (CAS `Version`, `Remove(id, expectedVersion, audit)`), `IDeviceMembershipStore` (CAS `expectedRoleId`), `DeviceAccessAuditEntry`, InMemory/EF stores + DI, field `GrantedBy`/`GrantedByMasterAdmin` trên `FeaturePermissionGrant`/`ResourceScopeGrant` + migration `AddPermissionGrantGrantor` [RT13])

## Overview

Thay `AuthorizationContext`/`DeviceAuthorizationPolicy` (khoá action trần, `*`, union allow/deny) bằng `AccessActor` + `DeviceAccessResolver` + `DeviceAccessPolicy` đánh giá theo mã quyền resolve từ `(type, action[, domain])`; membership là WHERE grant cho đúng một thiết bị. Tạo `AccessActorFactory` (điểm dựng actor duy nhất, phase HTTP và realtime dùng lại) trên `PermissionGrantResolver` latest-wins, lõi là hai helper dòng `LatestFeatureGrants`/`LatestResourceGrants`; allow `*` hết hiệu lực ở mọi lớp. master-admin vô điều kiện (Q4, contract §3): xét trước mọi deny, feature deny lẫn resource deny không hạn chế master-admin, master-admin luôn có mọi org. Thêm 5 handler uỷ quyền (role CRUD, membership assign/remove) theo luật tập con/high-risk và luật cấp feature/resource grant. Controller chỉ chỉnh tối thiểu để compile; mapping HTTP 404/403 của route thiết bị và endpoint mới thuộc phase 4.

**Ranh giới với phase 4 (HTTP):** phase này sở hữu trọn luật của `GrantFeaturePermissionHandler`/`GrantResourceScopeHandler` (`WILDCARD_GRANT_FORBIDDEN` cho allow `*` bởi mọi actor và deny `*` bởi non-master, `GRANT_TARGET_IS_MASTER_ADMIN`, `SELF_GRANT_FORBIDDEN`, `GRANT_EXCEEDS_OWN_PERMISSIONS` gồm cả luật phạm vi của người nhận, `GRANT_OVERRIDES_MASTER_DECISION`) [RT1, RT4, RT13], mapping outcome → HTTP của `PermissionGrantsController` cho các mã đó (400/403 `{code}`) và test unit + integration của chúng. Phase 4 không viết lại luật, mapping hay test này; ở `PermissionGrantsController`, phase 4 chỉ xoá endpoint `device-actions` và đổi snapshot `GET /permissions/{userId}` sang `LatestFeatureGrants`/`LatestResourceGrants`.

## Key Insights

- Lỗi 1 nằm đúng một chỗ: `src/Hms.Server.Application/Commands/SendDeviceCommandHandler.cs:79-83` truyền `request.Action` vào `Evaluate`, key `(DeviceId, action)` ở `src/Hms.Server.Domain/Authorization/AuthorizationPolicy.cs:46-47,62-63`. Hiện latent vì mọi row pinned `DeniedUntilActionContract` (`SendDeviceCommandHandler.cs:74-77`); sống trong test dùng `InMemoryCommandCatalog`.
- `*` được honour ở `AuthorizationPolicy.cs:34,47,52,63`, `ScopeAuthorization.cs:57,61`, `FeaturePermissionAuthorizationHandler.cs:27,34`; sinh ra ở `DeviceCommandsController.cs:66,82-84`.
- Hiện deny thắng master-admin ở cả hai lớp: feature deny được xét trước `IsInRole("master-admin")` (`ScopeAuthorization.cs:56-62`, `FeaturePermissionAuthorizationHandler.cs:25-36`); resource deny bị trừ cả khỏi "mọi org" của master-admin (`ScopeAuthorization.cs:33-38`, `ExceptWith`). Contract §3 đảo lại: master-admin vô điều kiện — xét trước mọi deny, không trừ org. Muốn hạn chế thì master-admin khác gỡ role qua endpoint `DELETE /api/v1/users/{userId}/roles/master-admin` do phase 4 thêm (Q11). Code hiện chưa có đường gỡ role, lockout hay đổi security stamp (`UserManagementContracts.cs:15-30`, `UsersController.cs:74-93`); `RoleAuthorizationPolicy` chỉ canh tạo/gán. [RT1]
- Không test hiện có nào khẳng định deny chặn master-admin: trong `tests/` chỉ `ManagementApiTests.Identity_role_mutation_advances_the_shared_policy_version` dùng role `master-admin`, không có deny (đã grep `master-admin`/`ClaimTypes.Role`). `CommandBoundaryTests.Wildcard_master_grants_still_respect_explicit_denies` ("master" là org tên `Master` + grant `*`; khẳng định device-action deny thắng `*`) bị thay ở A.5. Hai regression lock có deny (`Role_mutation_fails_closed_when_persisted_users_permission_is_denied`, `Persisted_resource_grants_are_used_for_scope_filtering_and_denies_win`) dùng actor không có role nên vẫn đúng, không sửa.
- Union-of-history (lỗi 2 bản feature/resource): `ScopeAuthorization.cs:26-31,49-54`, `FeaturePermissionAuthorizationHandler.cs:25-36`; store append-only `PermissionContracts.cs:35-40` (InMemory `List<>` singleton không lock, `DependencyInjection.cs:77`).
- Leo thang feature grant: `PermissionGrantsController.cs:43-55` chỉ kiểm non-blank (nhận cả `*`); `CanManageTarget` (`:103-114`) cho self. Grant handler gọi `Advance()` rồi append (`PermissionContracts.cs:45-65`) — version nằm trong row nên giữ thứ tự này.
- Snapshot `PermissionGrantsController.Get` (`:35-40`) trả nguyên lịch sử `FeatureGrants`/`ResourceGrants` (cùng key có cả allow lẫn deny) → phase 4 đổi sang helper dòng của phase này. Mỗi grant lấy version mới từ `Advance()` (InMemory `Interlocked.Increment`; Postgres upsert tăng nguyên tử `policy_versions` trong `EfPermissionStore.AdvancePostgres`). Version chỉ duy nhất và tăng dần khi bộ đếm đã ≥ mọi dòng grant: `AdvancePostgres` chỉ cộng bộ đếm (seed 0 ở migration `AddPolicyVersionCounter`), không so `MAX(grants)` (`EfPermissionStore.cs:117-139`). Migration phase 2 căn lại bộ đếm; helper vẫn có tie-break "cùng version → deny thắng" để kết quả xác định với dòng trùng version còn sót từ cơ chế cũ. [RT9]
- `DeviceCommandsController.cs:36-56` dựng org từ claim `org`/home org, master-admin fallback org đầu tiên (`:48-51`); parse claim `device-action`/`deny-device-action` (`:77-80,95-98`) — không code nào trong src phát hành các claim này (scout authz-core finding 1).
- `DevicesController.Create` (`:153-159`) kiểm `HasFeaturePermission("devices.manage")` rồi `CanAccessOrganization(request.OwnerOrganizationId)`; body `ownerOrganizationId` là strong id `{ "value": "<guid>" }` (`CreateDeviceRequest`, `ManagementContracts.cs:10-12`; frontend `strongId`). Phase 4 đổi owner org ngoài phạm vi thành 404 → test wildcard phải dùng owner org trong phạm vi để 403 chỉ đến từ thiếu feature (cũng để test đỏ thật trước phase: hiện `*` cho 201).
- `Evaluate` chỉ có 1 caller production (`SendDeviceCommandHandler.cs:79`) + 4 assertion test (`CommandBoundaryTests.cs:487,493`, `PermissionTests.cs:117,134`). `AuthorizationDecision` chỉ dùng trong file đó.
- Thứ tự handler hiện tại: actor → idempotency precheck → device → catalog trust/stale → `Find` → availability → authz → cached result → TTL (`SendDeviceCommandHandler.cs:18-111`). Payload HTTP là `JsonElement` (scout device-surfaces risk).
- TTL hiện `DefaultTtlMs is > 0 ? … : 120s` (`:94-96`); contract §5 đổi thành `DefaultTtlMs ?? 120000`. Test có 1 site TTL `null`, còn lại `120_000`.
- `CommandRequestFingerprint.Compute` (`src/Hms.Server.Application/Commands/CommandContracts.cs:136-149`) liệt kê field tường minh → thêm `Confirm` vào `DeviceCommandRequest` (`:11-21`, trailing optional) không đổi fingerprint, không vỡ 7 call site positional.
- `CommandDefinition` (contract §3) không có cờ `ConfirmRequired`; confirm-required ⇔ `ConfirmTemplate != null` (3 row: `system.reboot`, `system.factory_reset`, `ota.apply`).
- `IAuthorizationEpochStore.Advance(deviceId)` chỉ được gọi ở `RevokeDeviceHandler` (`PermissionContracts.cs:89`); EF store không có transaction chung (`EfPermissionStore.cs:117-146` raw SQL) → contract chọn thứ tự fail-closed, không thêm transaction. Mỗi `Advance` là một round trip riêng (`EfAuthorizationEpochStore.cs:42-85`) → sửa role dùng `AdvanceMany` một câu lệnh, và tín hiệu sau commit là best-effort [RT7].
- `EffectiveOrganizations` không lấy home org (`ScopeAuthorization.cs:12-40`) và contract §3 giữ nguyên; member không có resource grant vẫn in-scope trên đúng thiết bị có membership (membership là WHERE grant của thiết bị đó).
- `ScopeAuthorization` là `internal static`, Api không có `InternalsVisibleTo`; `Hms.Server.UnitTests` tham chiếu Api → `AccessActorFactory` để `public static` mới unit test trực tiếp. Mọi call site `ScopeAuthorization` đều truyền `IPermissionStore` → giữ chữ ký public, chỉ đổi thân hàm. Hằng role: `RoleAuthorizationPolicy.MasterAdminRole`.
- `IUserScopeStore` (`Exists`, `Organization`; `UserManagementContracts.cs:32-36`) chỉ có cài đặt Identity scoped (`DependencyInjection.cs:191`) → unit test cần double in-memory. Phase này thêm `IsMasterAdmin(UserId)` cho luật deny nhắm master-admin; `IdentityUserManagement` cài bằng truy vấn đồng bộ `UserRoles` join `Roles` (`ApplicationDbContext` là `IdentityDbContext<ApplicationUser, ApplicationRole, Guid>`) [RT1].
- `IPermissionStore` còn `GrantDeviceAction`/`DeviceActionGrants` (`PermissionContracts.cs:20,23`) tới khi phase HTTP xoá → class test implement interface này sẽ vỡ compile ở phase đó. Thứ tự ghi chứng minh bằng wrapper store role/membership + `IAuthorizationEpochStore` ghi lại, dùng `InMemoryPermissionStore` thật.
- Hàng rào realtime hiện so cả `PolicyVersion` toàn cục (`RealtimeContracts.cs:42-50`) → mỗi `Advance()` của handler phase này làm rớt event mọi thiết bị cho tới khi phase realtime bỏ hàng rào (contract §5).
- Seed grant trực tiếp qua handler trong integration test: `ManagementApiTests.cs:159-160,240-241,265-266,328-329` → vỡ compile khi handler nhận actor; seed `:234-241` cấp grant cho user chưa có trong Identity → grant handler không được đòi target tồn tại.
- Số dòng trích dẫn là trước phase 1–2; khi làm định vị theo symbol (contract §12).

## Requirements

**Functional**
- F1 `DeviceAccessPolicy.Evaluate` theo đúng thứ tự §3: scope → revoked → master-admin → `device.view`+`SeesAllInScope` → membership permission → `DEVICE_PERMISSION_REQUIRED`. Không còn `*`.
- F2 `DeviceAccessResolver.Resolve(actor, device)` (1 lần đọc membership + 1 lần đọc role): `InScope` = org thiết bị ∈ effective orgs **hoặc** có membership hợp lệ trên thiết bị; `SeesAllInScope` = master-admin, hoặc `devices.manage`/`permissions.manage` với org thiết bị ∈ effective orgs (membership không bao giờ cấp); `CanManageAccess` = master-admin, hoặc `permissions.manage` với org thiết bị ∈ effective orgs. Role khác org thiết bị → không role, không scope (fail-closed).
- F3 `SendDeviceCommandHandler.HandleAsync(DeviceCommandRequest, AccessActor, CancellationToken)`: `PERMISSION_UNMAPPED` khi resolver trả null; `CONFIRM_REQUIRED` khi row cần confirm và `Confirm` blank; TTL `DefaultTtlMs ?? 120000`; cached result chỉ trả sau mọi check.
- F4 5 handler uỷ quyền đúng luật §4 (codes nguyên văn): target user qua `IUserScopeStore`; tên/mô tả sai → `DEVICE_ROLE_INVALID`; quyền ∉ `DevicePermissionVocabulary.All(catalog)` → `DEVICE_PERMISSION_UNKNOWN`; tên trùng (không phân biệt hoa/thường) do store báo `DEVICE_ROLE_NAME_TAKEN` (handler không pre-check); CAS `expectedRoleId` (pre-check + store) và `expectedVersion` (update; delete theo thứ tự store `NOT_FOUND → VERSION_STALE → IN_USE`); assign kiểm `expectedRoleVersion` == phiên bản role (`DEVICE_ROLE_VERSION_STALE`, mọi actor) [RT3]; gỡ membership đang có không kiểm home org của người giữ (Q13) [RT14]; sửa role non-master: `(old ∪ new) ⊆ perms(actor, D)` trên mọi thiết bị dùng role [RT2]; write ordering: store + audit → tín hiệu sau commit best-effort: `IPermissionStore.Advance()`, rồi epoch (thay/gỡ membership: epoch D; gán mới: không advance epoch; sửa role: một lần `AdvanceMany`), lỗi tín hiệu → log và vẫn `OK` [RT7].
- F5 `PermissionGrantResolver`: helper dòng `LatestFeatureGrants(grants)`/`LatestResourceGrants(grants)` giữ mỗi key một dòng có `PolicyVersion` lớn nhất, cùng version thì deny thắng [RT9] — cài đặt latest-wins duy nhất; tập allowed/denied dựng trên chúng; claim là baseline chỉ test cấp (deny claim thắng allow claim cùng key; mọi persisted row cùng key ghi đè); allow `*` không bao giờ cấp; deny `*` chặn hết với non-master; `Has` xét master-admin trước mọi deny.
- F6 `GrantFeaturePermissionHandler`/`GrantResourceScopeHandler` nhận actor: allow `*` → `WILDCARD_GRANT_FORBIDDEN` (400) với mọi actor; deny `*` chỉ master-admin (non-master → `WILDCARD_GRANT_FORBIDDEN`); non-master: không self-grant (`SELF_GRANT_FORBIDDEN` 403), feature allow mà grantor không đang giữ → `GRANT_EXCEEDS_OWN_PERMISSIONS` (403), resource có org ∉ effective orgs → `GRANT_EXCEEDS_OWN_PERMISSIONS`. Thêm [RT1, RT4, RT13]: deny (feature hoặc resource) nhắm user giữ role master-admin → `GRANT_TARGET_IS_MASTER_ADMIN` (400, mọi actor); non-master feature allow: org hiệu lực hiện tại của target ⊆ `actor.EffectiveOrganizations`, non-master resource allow: feature allowed hiện tại của target ⊆ `actor.FeaturePermissions` (cả hai `GRANT_EXCEEDS_OWN_PERMISSIONS`); non-master allow trên key mà dòng master-issued mới nhất là deny → `GRANT_OVERRIDES_MASTER_DECISION` (403, Q12); mọi dòng grant ghi `GrantedBy` + `GrantedByMasterAdmin` từ actor. Phase này sở hữu luật, mapping HTTP ở `PermissionGrantsController` và test unit + integration của chúng; phase 4 không làm lại.
- F7 `AccessActorFactory.FromPrincipal(ClaimsPrincipal, IOrganizationStore, IPermissionStore)` (không `HttpContext`): NameIdentifier thiếu/sai → `null`; effective orgs: master-admin = mọi org (xét trước, không đọc resource grant, không trừ deny), non-master = resource grant latest-wins (+ claim `org-scope` chỉ test) mở rộng xuống org con, trừ deny (đã mở rộng); feature = `Allowed` latest-wins (không chứa `*`). `ScopeAuthorization` ủy quyền vào cùng code, không có helper dựng actor thứ hai.
- F8 `DeviceCommandsController` dựng actor qua `AccessActorFactory`; bỏ claim `org`/`device-action`/`deny-device-action` và fallback org đầu tiên; truyền `Confirm`. `PermissionGrantsController` dùng `AccessActorFactory`, map outcome → 200 / 400 / 403 `{code}` (mapping cuối; snapshot giữ nguyên tới phase 4).
- F9 master-admin vô điều kiện (contract §3): `ScopeAuthorization.HasFeaturePermission`, `ScopeAuthorization.EffectiveOrganizations` (qua `AccessActorFactory`) và `FeaturePermissionAuthorizationHandler` xét master-admin trước deny; feature deny (kể cả deny `*`, claim hay persisted) và resource deny không hạn chế master-admin ở route feature, effective orgs hay device access.

**Non-functional**
- Solution compile + toàn bộ test xanh ở cuối phase; không đổi HTTP status hiện có của commands (vẫn 200 + code — phase 4 đổi).
- Không transaction mới; thao tác ghi fail-closed trong ≤1 tick realtime nếu crash giữa chừng. Write đã commit thì handler trả `OK` kể cả khi tín hiệu sau commit lỗi [RT7].
- InMemory stores thread-safe (lock) vì singleton trong dev/test.
- `IDeviceAccessResolver` + 5 handler đăng ký scoped (chạy được với store InMemory singleton lẫn EF scoped).
- Không phát hành riêng phase này: `Advance()` còn kích hoạt hàng rào `PolicyVersion` cũ cho tới phase realtime (plan.md, contract §5/§12).

## Architecture

```csharp
// Domain/Authorization/AccessActor.cs
public sealed record AccessActor(UserId UserId, bool IsMasterAdmin,
    IReadOnlySet<OrganizationId> EffectiveOrganizations, IReadOnlySet<string> FeaturePermissions);
// Domain/Authorization/DeviceAccessPolicy.cs — DeviceAccess, DeviceAccessDecision, DeviceAccessPolicy
public static class DeviceAccessCodes { Allowed = "ALLOWED", DeviceNotFound = "DEVICE_NOT_FOUND",
    DeviceRevoked = "DEVICE_REVOKED", PermissionRequired = "DEVICE_PERMISSION_REQUIRED" } // const string
```
`DeviceAccess`, `DeviceAccessDecision`, `Evaluate` đúng nguyên văn contract §3.

`PermissionGrantResolver` (Application/Authorization, pure):

```csharp
public static class PermissionGrantResolver
{
    // latest row per key by PolicyVersion, ascending output; the only latest-wins implementation
    public static IReadOnlyList<FeaturePermissionGrant> LatestFeatureGrants(IEnumerable<FeaturePermissionGrant> grants); // key (UserId, Permission ordinal)
    public static IReadOnlyList<ResourceScopeGrant> LatestResourceGrants(IEnumerable<ResourceScopeGrant> grants);       // key (UserId, OrganizationId)
    // sets built on the latest rows; claims only fill keys without a persisted row
    public static ResolvedFeaturePermissions Features(IEnumerable<string> claimAllow, IEnumerable<string> claimDeny, IEnumerable<FeaturePermissionGrant> grants);
    public static ResolvedOrganizationSeeds Organizations(IEnumerable<OrganizationId> claimAllow, IEnumerable<OrganizationId> claimDeny, IEnumerable<ResourceScopeGrant> grants);
    // reads perm/deny-perm, org-scope/deny-org-scope claims plus persisted grants of the NameIdentifier user
    public static ResolvedFeaturePermissions Features(ClaimsPrincipal principal, IPermissionStore? permissions);
    public static ResolvedOrganizationSeeds Organizations(ClaimsPrincipal principal, IPermissionStore? permissions);
}
public sealed record ResolvedFeaturePermissions(IReadOnlySet<string> Allowed, IReadOnlySet<string> Denied)
{ public bool Has(string permission, bool isMasterAdmin); } // isMasterAdmin → true before any deny; Denied ∋ "*"|p → false; else Allowed ∋ p
public sealed record ResolvedOrganizationSeeds(IReadOnlySet<OrganizationId> Allowed, IReadOnlySet<OrganizationId> Denied);
```

Theo key (`(UserId, Permission)` ordinal / `(UserId, OrganizationId)`): `LatestFeatureGrants`/`LatestResourceGrants` giữ dòng có `PolicyVersion` lớn nhất, trả theo `PolicyVersion` tăng dần; cùng `PolicyVersion` trên một key → deny thắng (xác định, fail-closed; dòng trùng chỉ có thể còn từ cơ chế version trước bộ đếm, xem Key Insights) [RT9]. `Features`/`Organizations` lấy dòng từ hai helper này rồi mới dùng claim cho key không có dòng (deny claim thắng allow claim) — không có cài đặt latest-wins thứ hai (inline `GroupBy`/`MaxBy`) ở đâu khác; snapshot `PermissionGrantsController` (phase 4) dùng lại đúng hai helper. `Allowed` không bao giờ chứa `*`, rỗng khi trạng thái cuối của `*` là deny; `Denied` giữ `*` (để `/me/permissions` trả deny `*`, contract §4). Overload `ClaimsPrincipal` là nơi duy nhất biết tên claim. `Has(p, isMasterAdmin)`: `isMasterAdmin` → true trước khi xét `Denied`; non-master: `Denied ∋ "*"|p` → false; còn lại `Allowed ∋ p`.

`AccessActorFactory` (`src/Hms.Server.Api/Authorization/AccessActorFactory.cs`):

```csharp
public static class AccessActorFactory
{
    // no HttpContext; missing or non-Guid NameIdentifier → null (fail closed)
    public static AccessActor? FromPrincipal(ClaimsPrincipal principal, IOrganizationStore organizations, IPermissionStore permissions);
    // single implementation of organization scope; ScopeAuthorization delegates here;
    // master-admin → every organization, decided before any grant or deny is read
    public static IReadOnlySet<OrganizationId> EffectiveOrganizations(ClaimsPrincipal principal, IOrganizationStore organizations, IPermissionStore? permissions);
}
```

- `IsMasterAdmin = IsInRole(RoleAuthorizationPolicy.MasterAdminRole)`; `FeaturePermissions = PermissionGrantResolver.Features(principal, permissions).Allowed`. Mọi consumer của `AccessActor` (`DeviceAccessResolver`, role/membership/grant handler) xét `IsMasterAdmin` trước `FeaturePermissions`, nên deny lưu trên tài khoản master-admin không có tác dụng.
- `EffectiveOrganizations`: master-admin → mọi org (`organizations.List()`), trả ngay — không đọc resource grant, không trừ deny; non-master: seeds = `PermissionGrantResolver.Organizations(principal, permissions)` → `OrganizationScopeResolver.Expand(seeds.Allowed)` trừ `Expand(seeds.Denied)` (giữ hành vi `ScopeAuthorization.cs:33-38` cho non-master, bỏ `ExceptWith` trên master-admin).
- `ScopeAuthorization` giữ chữ ký public: `EffectiveOrganizations` → `AccessActorFactory.EffectiveOrganizations`; `HasFeaturePermission` → `PermissionGrantResolver.Features(user, permissions).Has(p, user.IsInRole(RoleAuthorizationPolicy.MasterAdminRole))`. `FeaturePermissionAuthorizationHandler` (Infrastructure, không thấy Api) gọi cùng `Has` → master-admin `Succeed` trước khi xét deny. Luật "master-admin trước deny" chỉ nằm ở `ResolvedFeaturePermissions.Has` (feature) và `AccessActorFactory.EffectiveOrganizations` (scope); không còn chỗ nào tự gộp claim/grant, tự đọc deny trước master-admin hay honour allow `*`.

`DeviceAccessResolver` (Application/Authorization; ctor `IDeviceMembershipStore`, `IDeviceRoleStore`):
- `orgInScope = actor.EffectiveOrganizations.Contains(device.OwnerOrganizationId)`.
- Membership `Find(actor.UserId, device.Id)` → `roles.Find(roleId)`; hợp lệ khi role tồn tại và `role.OrganizationId == device.OwnerOrganizationId`; không hợp lệ → `RoleId = null`, `Permissions = ∅`, không tính scope.
- `InScope = orgInScope || membership hợp lệ` (home org không cấp scope).
- `SeesAllInScope = IsMasterAdmin || (orgInScope && features ∋ "devices.manage" | "permissions.manage")`.
- `CanManageAccess = CanManageAccess(actor, device.OwnerOrganizationId)` — static, dùng chung role handlers: `IsMasterAdmin || (org ∈ effective && features ∋ "permissions.manage")`.

`SendDeviceCommandHandler` (ctor thêm `IDeviceAccessResolver accessResolver`):

```
actor mismatch → idempotency precheck → device → catalog metadata/trust/stale → Find → availability
→ permission = CommandPermissionResolver.Resolve(definition, request.Payload)   null → PERMISSION_UNMAPPED (ContractUnavailable)
→ DeviceAccessPolicy.Evaluate(accessResolver.Resolve(actor, device), device, permission)   !Allowed → Failed(code)
→ definition.ConfirmTemplate != null && IsNullOrWhiteSpace(request.Confirm) → CONFIRM_REQUIRED (Failed)
→ cachedResult → ttl = DefaultTtlMs ?? 120_000 → timestamp/expired → reserve → adapter → transport
```

Delegation (`Application/Authorization/DeviceAccessHandlers.cs`):

```csharp
public sealed record DeviceAccessOutcome(string Code, DeviceRole? Role = null, DeviceMembership? Membership = null);
public static class DeviceAccessOutcomeCodes { /* OK, PERMISSION_MANAGEMENT_REQUIRED, DEVICE_NOT_FOUND, USER_NOT_FOUND,
  SELF_GRANT_FORBIDDEN, DEVICE_ROLE_NOT_FOUND, DEVICE_ROLE_ORGANIZATION_MISMATCH, DEVICE_MEMBERSHIP_CHANGED,
  HIGH_RISK_REQUIRES_MASTER_ADMIN, GRANT_EXCEEDS_OWN_PERMISSIONS, ORGANIZATION_NOT_FOUND, DEVICE_ROLE_INVALID,
  DEVICE_ROLE_NAME_TAKEN, DEVICE_PERMISSION_UNKNOWN, DEVICE_ROLE_VERSION_STALE, DEVICE_ROLE_IN_USE, WILDCARD_GRANT_FORBIDDEN,
  GRANT_TARGET_IS_MASTER_ADMIN, GRANT_OVERRIDES_MASTER_DECISION */ }
CreateDeviceRoleHandler.Handle(AccessActor actor, OrganizationId org, string name, string? description, IReadOnlyCollection<string> permissions)
UpdateDeviceRoleHandler.Handle(AccessActor actor, OrganizationId org, DeviceRoleId roleId, string name, string? description, IReadOnlyCollection<string> permissions, int expectedVersion)
DeleteDeviceRoleHandler.Handle(AccessActor actor, OrganizationId org, DeviceRoleId roleId, int expectedVersion)
AssignDeviceMembershipHandler.Handle(AccessActor actor, DeviceId deviceId, UserId target, DeviceRoleId roleId, DeviceRoleId? expectedRoleId, int expectedRoleVersion) // [RT3]
RemoveDeviceMembershipHandler.Handle(AccessActor actor, DeviceId deviceId, UserId target, DeviceRoleId? expectedRoleId)
```

- Ctor: store role/membership/device/org, `IDeviceAccessResolver`, `IPermissionStore`, `IAuthorizationEpochStore`; membership handler thêm `IUserScopeStore`; create/update role thêm `ICommandCatalog` (vocabulary = `DevicePermissionVocabulary.All(catalog)`, không derive lại).
- Membership, theo thứ tự: device null hoặc `!IsMasterAdmin && !access.InScope` → `DEVICE_NOT_FOUND`; `!access.CanManageAccess` → `PERMISSION_MANAGEMENT_REQUIRED`; `current = memberships.Find(target, D)`; target user — assign, và remove khi `current == null`: `!users.Exists(target)`, hoặc (non-master) `users.Organization(target)` null/∉ effective → `USER_NOT_FOUND`; remove khi `current != null` (Q13): không kiểm `IUserScopeStore`, dựa vào membership đang lưu, nên gỡ được member org khác, member không org và membership mồ côi của user đã xoá [RT14]; target == actor (non-master) → `SELF_GRANT_FORBIDDEN`; role (assign) → `DEVICE_ROLE_NOT_FOUND` → `DEVICE_ROLE_ORGANIZATION_MISMATCH` → `expectedRoleVersion != role.Version` → `DEVICE_ROLE_VERSION_STALE` (cả ba áp cho cả master, trước mọi ghi) [RT3]; pre-check `expectedRoleId` ≠ role hiện tại → `DEVICE_MEMBERSHIP_CHANGED`; non-master: `(R ∪ current) ∩ H ≠ ∅` → `HIGH_RISK_REQUIRES_MASTER_ADMIN`; `(R ∪ current) ⊄ perms(actor, D)` → `GRANT_EXCEEDS_OWN_PERMISSIONS`; rồi `memberships.Upsert/Remove(..., expectedRoleId, audit)` — store CAS là nguồn thật, lệch do race sau pre-check → `DEVICE_MEMBERSHIP_CHANGED`, không advance. `perms(actor, D) = access.Permissions` (chỉ role membership của chính actor).
- Helper nội bộ `DelegationRules.CheckRoleChange(actor, ownPerms, newPerms?, currentPerms?) → string?` dùng chung assign/remove/role-update (role update truyền quyền cũ của role làm `currentPerms`, nên kiểm cả `old ⊆ own`) [RT2]; `H` = `DevicePermissionCodes.IsHighRisk` (phase 1).
- No-op (sau mọi check): assign đúng role hiện tại, hoặc remove khi không có membership và `expectedRoleId == null` → `OK`, không ghi, không advance.
- Role, theo thứ tự: org không tồn tại hoặc (non-master) ∉ effective → `ORGANIZATION_NOT_FOUND`; `!CanManageAccess(actor, org)` → `PERMISSION_MANAGEMENT_REQUIRED`; tên trim rỗng/>128 hoặc mô tả >512 (hằng `DeviceRole`) → `DEVICE_ROLE_INVALID` (kiểm trước `DeviceRole.Create/Update`, không để `ArgumentException` thoát); quyền ∉ vocabulary (kể cả `*`) → `DEVICE_PERMISSION_UNKNOWN`; update/delete: role không có hoặc khác `org` → `DEVICE_ROLE_NOT_FOUND`, pre-check `expectedVersion` → `DEVICE_ROLE_VERSION_STALE`; non-master: H ∩ (old ∪ new) ≠ ∅ → `HIGH_RISK_REQUIRES_MASTER_ADMIN` (kiểm ngoài vòng lặp thiết bị, nên role chưa gán ở đâu vẫn bị kiểm); update non-master: `(old ∪ new) ⊆ perms(actor, D)` ∀ D ∈ distinct `ListByRole(roleId)` — gọi `DelegationRules.CheckRoleChange(actor, perms(actor, D), new, old)` cho từng D — else `GRANT_EXCEEDS_OWN_PERMISSIONS`; admin không thu nhỏ hay làm rỗng được role vượt quyền mình (luật V4 áp cho mọi holder) [RT2]; rồi ghi store và trả nguyên mã store báo (`InvalidOperationException(code)`, phase 2): `DEVICE_ROLE_NAME_TAKEN` (unique `NormalizedName`; handler không pre-check tên trùng và không tự chuẩn hoá tên — store là nguồn thật), `DEVICE_ROLE_VERSION_STALE` (CAS update), delete `Remove(roleId, expectedVersion, audit)` → `DEVICE_ROLE_NOT_FOUND` → `DEVICE_ROLE_VERSION_STALE` → `DEVICE_ROLE_IN_USE`. Chỉ map đúng các mã contract; lỗi khác ném tiếp.
- Ghi [RT7]: store write (+ audit, một `SaveChanges`) → tín hiệu sau commit, mỗi bước bọc `try/catch` riêng, lỗi thì log (audit id, device ids) và vẫn trả `OK`; bước epoch vẫn chạy khi bước `PolicyVersion` lỗi:
  - `permissions.Advance()`;
  - epoch: thay hoặc gỡ membership → `epochs.Advance(D)`; gán mới (`current == null`) → không advance epoch, vì không ai mất quyền (reconcile và hub đọc quyền live); sửa role → `epochs.AdvanceMany(distinct devices của ListByRole(roleId))` (đọc trước khi ghi); xoá role thành công → không có device.
  
  Audit `PreviousValue/NewValue` = snapshot JSON compact (`System.Text.Json` mặc định; cột `text`, không giới hạn độ dài): membership `{"roleId":"<guid>"}`; role `{name, description, permissions (sort ordinal), version}`; không email/tên hiển thị.

Grant handlers (`PermissionContracts.cs`) — luật, mapping HTTP và test thuộc trọn phase này:

```csharp
public sealed record PermissionGrantOutcome(string Code, FeaturePermissionGrant? Feature = null, ResourceScopeGrant? Resource = null);
GrantFeaturePermissionHandler.Handle(AccessActor actor, UserId target, string permission, bool deny = false)
GrantResourceScopeHandler.Handle(AccessActor actor, UserId target, OrganizationId organization, bool deny = false)
```

- Ctor: `IPermissionStore`, `IUserScopeStore` (chỉ `IsMasterAdmin`), `IOrganizationStore` (mở rộng org của target) [RT1, RT4].
- Feature, theo thứ tự: `permission == "*"` và allow → `WILDCARD_GRANT_FORBIDDEN` (mọi actor, kể cả master-admin); deny `*` bởi non-master → `WILDCARD_GRANT_FORBIDDEN` (contract không đặt mã riêng); deny mà `users.IsMasterAdmin(target)` → `GRANT_TARGET_IS_MASTER_ADMIN` (mọi actor; master-admin không bị grant hạn chế, Q4/Q11) [RT1]; non-master: target == actor → `SELF_GRANT_FORBIDDEN`; non-master allow mà `actor.FeaturePermissions ∌ permission` → `GRANT_EXCEEDS_OWN_PERMISSIONS` (deny một feature mình không giữ vẫn được); non-master allow mà org hiệu lực hiện tại của target ⊄ `actor.EffectiveOrganizations` → `GRANT_EXCEEDS_OWN_PERMISSIONS`; org của target = `PermissionGrantResolver.Organizations(∅, ∅, permissions.ResourceGrants(target))` → `OrganizationScopeResolver.Expand(Allowed)` trừ `Expand(Denied)`; home org của target đã do `CanManageTarget` kiểm [RT4]; non-master allow mà dòng mới nhất có `GrantedByMasterAdmin = true` của key (target, permission) là deny → `GRANT_OVERRIDES_MASTER_DECISION`. So với dòng master-issued mới nhất, không phải dòng mới nhất nói chung, để "tự deny trước rồi allow" không lách được [RT13].
- Resource, theo thứ tự: deny mà `users.IsMasterAdmin(target)` → `GRANT_TARGET_IS_MASTER_ADMIN` [RT1]; non-master: self → `SELF_GRANT_FORBIDDEN`; org ∉ `actor.EffectiveOrganizations` → `GRANT_EXCEEDS_OWN_PERMISSIONS`; non-master allow mà feature allowed hiện tại của target (`PermissionGrantResolver.Features(∅, ∅, permissions.FeatureGrants(target)).Allowed`) ⊄ `actor.FeaturePermissions` → `GRANT_EXCEEDS_OWN_PERMISSIONS` — không ai mở rộng phạm vi dưới feature người khác cấp mà mình không có [RT4]; non-master allow mà dòng master-issued mới nhất của key (target, org) là deny → `GRANT_OVERRIDES_MASTER_DECISION` [RT13].
- OK: `Advance()` → append dòng có `GrantedBy = actor.UserId`, `GrantedByMasterAdmin = actor.IsMasterAdmin` (crash giữa hai bước = không có grant) [RT13]. Bị từ chối: không advance, không ghi. Mã lấy từ `DeviceAccessOutcomeCodes` (một bảng mã chung với 5 handler uỷ quyền).
- Target-user rule của grant giữ ở `PermissionGrantsController.CanManageTarget` (403, regression lock). Grant handler không đòi target tồn tại: user chưa có trong Identity → `IsMasterAdmin` false, không có resource/feature grant → mọi luật mới đều qua (seed integration `:234-241` vẫn chạy).
- `PermissionGrantsController`: actor qua `AccessActorFactory.FromPrincipal` (null → `Forbid()`); `OK` → 200 grant (shape giữ); `WILDCARD_GRANT_FORBIDDEN`/`GRANT_TARGET_IS_MASTER_ADMIN` → 400 `{code}`; `SELF_GRANT_FORBIDDEN`/`GRANT_EXCEEDS_OWN_PERMISSIONS`/`GRANT_OVERRIDES_MASTER_DECISION` → 403 `{code}`; check sẵn có (`CanManagePermissions`, `PERMISSION_REQUEST_INVALID`, `CanManageTarget`, org tồn tại/trong scope) giữ nguyên. Đây là mapping cuối cho các mã grant — phase 4 không map lại; snapshot `Get` giữ nguyên ở phase này (phase 4 đổi sang helper dòng).
- `DeviceCommandsController`: actor qua `AccessActorFactory.FromPrincipal` (null → `Forbid()`); bỏ `IUserScopeStore`, claim `org`/`device-action`/`deny-device-action`, fallback org đầu tiên; `SendCommandRequest.Confirm` → `DeviceCommandRequest.Confirm`; vẫn `Ok(result)`.

## Related Code Files

**Create**

| Path | Mô tả |
|---|---|
| `src/Hms.Server.Domain/Authorization/AccessActor.cs` | record §3 |
| `src/Hms.Server.Domain/Authorization/DeviceAccessPolicy.cs` | `DeviceAccess`, `DeviceAccessDecision`, `DeviceAccessCodes`, `DeviceAccessPolicy.Evaluate` |
| `src/Hms.Server.Application/Authorization/DeviceAccessResolver.cs` | `IDeviceAccessResolver`, `DeviceAccessResolver` (+ static `CanManageAccess`) |
| `src/Hms.Server.Application/Authorization/DeviceAccessHandlers.cs` | 5 handler, `DeviceAccessOutcome`, `DeviceAccessOutcomeCodes`, `DelegationRules` (internal) |
| `src/Hms.Server.Application/Authorization/PermissionGrantResolver.cs` | helper dòng `LatestFeatureGrants`/`LatestResourceGrants`, tập latest-wins dựng trên chúng, 2 record kết quả (`Has` xét master-admin trước deny) |
| `src/Hms.Server.Api/Authorization/AccessActorFactory.cs` | `public static`: `FromPrincipal`, `EffectiveOrganizations` (master-admin mọi org, không trừ deny) |
| `tests/Hms.Server.UnitTests/DeviceAccessTestFixture.cs` | store phase 2 + `RecordingDeviceAccessStore`/`RecordingEpochStore`, `TestUserScopes : IUserScopeStore`, `PinnedCommandCatalog`, `Member/Admin/MasterAdmin` → `AccessActor`, `Handlers()` |
| `tests/Hms.Server.UnitTests/AccessActorFactoryTests.cs` | principal → actor |
| `tests/Hms.Server.UnitTests/DeviceAccessPolicyTests.cs` | policy + resolver |
| `tests/Hms.Server.UnitTests/DeviceAccessHandlerTests.cs` | ma trận uỷ quyền, CAS, thứ tự ghi, audit |
| `tests/Hms.Server.UnitTests/DeviceCommandAuthorizationTests.cs` | test theo catalog pinned (bare-action, domain, unmapped, confirm, TTL) |

**Modify**

| Path | Thay đổi |
|---|---|
| `src/Hms.Server.Domain/Authorization/AuthorizationPolicy.cs` | xoá `AuthorizationContext`, `AuthorizationDecision`, `DeviceAuthorizationPolicy`; giữ `UserId` |
| `src/Hms.Server.Application/Authorization/PermissionContracts.cs` | `PermissionGrantOutcome`; 2 grant handler nhận actor + luật F6 (gồm phạm vi người nhận, deny của master-admin, deny nhắm master-admin) và ghi `GrantedBy`/`GrantedByMasterAdmin` (field do phase 2 thêm) [RT1, RT4, RT13]; `InMemoryPermissionStore` thêm lock; giữ `DeviceActionGrant`/`GrantDeviceActionHandler` (phase 4 xoá) |
| `src/Hms.Server.Application/Authorization/UserManagementContracts.cs` | `IUserScopeStore.IsMasterAdmin(UserId)` [RT1] |
| `src/Hms.Server.Infrastructure/Identity/IdentityUserManagement.cs` | cài `IsMasterAdmin`: inject `ApplicationDbContext`, truy vấn đồng bộ `UserRoles` join `Roles` theo `NormalizedName` của `RoleAuthorizationPolicy.MasterAdminRole` [RT1] |
| `src/Hms.Server.Application/Realtime/RealtimeContracts.cs` | `IAuthorizationEpochStore.AdvanceMany(IReadOnlyCollection<DeviceId>)`; InMemory lặp `Advance` [RT7] |
| `src/Hms.Server.Infrastructure/Persistence/EfAuthorizationEpochStore.cs` | `AdvanceMany`: Postgres một câu `INSERT … SELECT unnest(@device_ids), 1 ON CONFLICT ("DeviceId") DO UPDATE SET "Epoch" = "device_authorization_epochs"."Epoch" + 1` (tham số `Guid[]`); provider khác: Find/cập nhật từng record rồi một `SaveChanges` [RT7] |
| `src/Hms.Server.Application/Commands/SendDeviceCommandHandler.cs` | chữ ký `AccessActor`, ctor + `IDeviceAccessResolver`, luồng §Architecture |
| `src/Hms.Server.Application/Commands/CommandContracts.cs` | `DeviceCommandRequest` thêm `string? Confirm = null` (cuối) |
| `src/Hms.Server.Infrastructure/Authorization/FeaturePermissionAuthorizationHandler.cs` | `PermissionGrantResolver.Features(context.User, permissions).Has(p, isMasterAdmin)` — master-admin `Succeed` trước deny; bỏ allow `*` |
| `src/Hms.Server.Api/Authorization/ScopeAuthorization.cs` | `EffectiveOrganizations` → `AccessActorFactory` (master-admin mọi org, không trừ deny); `HasFeaturePermission` → `PermissionGrantResolver` (master-admin trước deny); chữ ký public giữ |
| `src/Hms.Server.Api/Controllers/DeviceCommandsController.cs` | actor qua `AccessActorFactory`; bỏ `IUserScopeStore`, claim parsing, fallback; `SendCommandRequest.Confirm` |
| `src/Hms.Server.Api/Controllers/PermissionGrantsController.cs` | actor qua `AccessActorFactory`; outcome → 200; 400 `{code}` cho `WILDCARD_GRANT_FORBIDDEN`/`GRANT_TARGET_IS_MASTER_ADMIN`; 403 `{code}` cho `SELF_GRANT_FORBIDDEN`/`GRANT_EXCEEDS_OWN_PERMISSIONS`/`GRANT_OVERRIDES_MASTER_DECISION` (mapping cuối) [RT1, RT13]; snapshot giữ nguyên. Body 200 vẫn là record grant (giữ shape, owner chốt 2026-10-05). Vì vậy mọi id ra dạng `{"value": ...}`, kể cả `grantedBy` có từ phase 2. §6 chỉ áp Guid phẳng cho DTO mới, và frontend đọc được cả hai dạng |
| `src/Hms.Server.Infrastructure/DependencyInjection.cs` | `AddScoped<IDeviceAccessResolver, DeviceAccessResolver>` + 5 handler |
| `tests/Hms.Server.UnitTests/CommandBoundaryTests.cs` | **test đổi**: helper `Authorization(...)` → fixture; 5 site `new SendDeviceCommandHandler` thêm resolver (`:206`, `:216`, `:240`, `:336` và `new(` trong helper `CreateHandler` `:516-525`); đổi tên/code 4 test, viết lại 2 test wildcard/deny (kể cả `Wildcard_master_grants_still_respect_explicit_denies`) |
| `tests/Hms.Server.UnitTests/PermissionTests.cs` | **test đổi**: `Feature_wildcard_deny_overrides_exact_allow` viết lại cho non-master, xoá `Device_wildcard_deny_overrides_wildcard_allow`, chữ ký grant handler; thêm test resolver + helper dòng, policy handler (master-admin trước deny), luật grant |
| `tests/Hms.Server.IntegrationTests/ManagementApiTests.cs` | **test đổi**: 4 site seed dùng actor master-admin; thêm 11 test (grant/wildcard, master-admin bỏ qua deny, 3 test luật grant mới) |
| `tests/Hms.Server.IntegrationTests/PersistenceModelTests.cs` | thêm test DI resolve resolver + handlers; `AdvanceMany` tăng đơn điệu mọi device trong một lần gọi [RT7] |

**Delete**: không xoá file (chỉ xoá type trong `AuthorizationPolicy.cs`).

## Implementation Steps

### A. Tests Before (red)

1. `DeviceAccessTestFixture`: `InMemoryDeviceAccessStore` (phase 2) bọc trong `RecordingDeviceAccessStore : IDeviceRoleStore, IDeviceMembershipStore` (chuyển tiếp mọi call; mỗi lần ghi lưu audit, `InMemoryPermissionStore.PolicyVersion` và epoch mọi device của fixture tại thời điểm ghi; hook `BeforeWrite` để chèn ghi đồng thời); `RecordingEpochStore : IAuthorizationEpochStore` bọc `InMemoryAuthorizationEpochStore`, lưu `(deviceId, PolicyVersion)` mỗi lần `Advance`, lưu mỗi lần `AdvanceMany` thành một lời gọi, và có cờ `ThrowOnAdvance` để giả lỗi tín hiệu sau commit [RT7]; `InMemoryPermissionStore` thật (không class test nào implement `IPermissionStore`); `TestUserScopes : IUserScopeStore` (user → org?, tập user master-admin cho `IsMasterAdmin`) [RT1]; `Catalog = new PinnedCommandCatalog()` (vocabulary 50 thật); `InMemoryOrganizationStore` (cha/con/org khác), `InMemoryDeviceStore`, `Resolver`. Factory: `Member(UserId, Device, params string[] perms)` tạo role trong org của device + membership, trả actor `EffectiveOrganizations = ∅`, feature rỗng (scope chỉ từ membership); `Admin(UserId, OrganizationId scope, params string[] features)`; `MasterAdmin(UserId)` (`EffectiveOrganizations` = mọi org của fixture, như factory); `Handlers()` dựng 5 handler + 2 grant handler trên các double này.
2. `AccessActorFactoryTests` (red: type chưa có; principal + `InMemoryPermissionStore` + cây org cha/con/cháu + org khác):
   - `Missing_or_invalid_name_identifier_yields_no_actor` (Theory: thiếu, `"not-a-guid"`) → `null`.
   - `Master_admin_actor_covers_every_organization`.
   - `Master_admin_keeps_every_organization_despite_resource_denies` (role `master-admin` + persisted resource deny cha + claim `deny-org-scope` con → `EffectiveOrganizations(...)` lẫn `FromPrincipal(...)!.EffectiveOrganizations` = mọi org; `IsMasterAdmin` true).
   - `Resource_grant_expands_to_descendant_organizations`.
   - `Denied_organization_is_subtracted_after_expansion` (non-master: allow cha + deny con → con, cháu ngoài scope).
   - `Latest_grant_wins_after_grant_deny_grant` (resource và feature `users.manage`: grant→deny→grant → có; grant→deny → không).
   - `Wildcard_feature_allow_is_ignored` (claim/persisted allow `*` → `FeaturePermissions` không chứa `*` lẫn mã khác; deny `*` → `FeaturePermissions` rỗng).
3. `DeviceAccessPolicyTests` (red: type chưa có):
   - `Out_of_scope_device_is_not_found_even_when_revoked` → `DEVICE_NOT_FOUND`.
   - `Revoked_device_is_denied_before_master_admin_bypass` → `DEVICE_REVOKED`.
   - `Master_admin_is_allowed_without_membership_or_scope`.
   - `Member_with_only_a_membership_is_in_scope_and_allowed_per_role` (không resource grant: `InScope`, `adc.view` ALLOWED, `sys.reboot` → `DEVICE_PERMISSION_REQUIRED`, `SeesAllInScope`/`CanManageAccess` false).
   - `Membership_does_not_extend_scope_to_other_devices_of_the_organization` (cùng member, thiết bị khác cùng org, không membership → `DEVICE_NOT_FOUND`).
   - `Scope_admin_without_membership_sees_device_view_only` (`device.view` ALLOWED, `adc.view` → `DEVICE_PERMISSION_REQUIRED`).
   - `Membership_role_allows_exact_code_only`; `Wildcard_in_role_permissions_grants_nothing`.
   - `Resolver_ignores_role_from_another_organization` (không role, không scope → `DEVICE_NOT_FOUND`).
   - `Resolver_sets_flags_only_for_in_scope_features` (`devices.manage` → SeesAll, không CanManage; `permissions.manage` → cả hai; org ngoài scope → false kể cả khi có membership).
4. `DeviceCommandAuthorizationTests` — catalog = `new PinnedCommandCatalog().List()` map `with { Availability = Enabled }` vào `InMemoryCommandCatalog`; quyền đọc từ catalog, chỉ literal `adc.view`/`adc.config_edit` (contract); actor `Member(...)` chỉ có membership:
   - `Adc_view_role_cannot_run_get_status_of_other_namespaces` (Theory mqtt/sms/battery/ota/control) → `DEVICE_PERMISSION_REQUIRED`, transport rỗng; guard: `RequiredPermission != "adc.view"`.
   - `Adc_view_role_cannot_run_mqtt_get_config`; `Adc_view_role_runs_adc_get_status` → `COMMAND_RECORDED`.
   - `Config_get_resolves_view_permission_from_json_string_domain` (payload `JsonElement` "adc").
   - `Config_set_requires_edit_permission_of_domain`.
   - `Config_command_with_unmapped_domain_is_permission_unmapped` (Theory: thiếu, `"ADC"`, số `42`, `"nope"`) → `PERMISSION_UNMAPPED`, `ContractUnavailable`.
   - `Confirm_required_rows_reject_blank_confirm` (Theory reboot/factory_reset/ota.apply; null, `"  "`) → `CONFIRM_REQUIRED`; `Confirm_required_rows_accept_non_blank_confirm`; `Only_three_rows_require_confirmation`.
   - `Catalog_ttl_60000_rows_plan_sixty_second_expiry` (đếm 24 row; `Plan.ExpiresAt - RequestedAt == 60s`, actor master-admin, domain/confirm điền khi cần); `Rows_without_ttl_use_dispatcher_default` (120s).
   - `Confirm_is_not_part_of_request_fingerprint`.
   - `Removed_then_reassigned_membership_runs_command_again` (qua Assign/Remove handler).
5. `CommandBoundaryTests` rewrite: `Missing_feature_permission_is_denied_before_transport` → `Missing_device_permission_is_denied_before_transport` (`DEVICE_PERMISSION_REQUIRED`); `Cached_result_is_not_returned_after_feature_permission_is_revoked` → `Cached_result_is_not_returned_after_membership_is_removed`; `Same_correlation_with_different_command_is_rejected`: role chứa cả 2 mã thay `*`; `Explicit_device_deny_wins_over_allow` → `Actor_without_scope_or_membership_gets_device_not_found`; `Wildcard_master_grants_still_respect_explicit_denies` (khẳng định device-action deny thắng `*`; device deny không còn, master-admin vô điều kiện) → `Master_admin_bypasses_membership_but_not_revocation` (master-admin không membership/scope → chạy được; thiết bị Revoked → `DEVICE_REVOKED`: revoke là trạng thái thiết bị, không phải deny). Giữ nguyên assertion các test catalog/idempotency/fence (khoá hành vi).
6. `DeviceAccessHandlerTests`:
   - Membership: `Devices_manage_without_permissions_manage_cannot_assign_membership` (lỗi 3) → `PERMISSION_MANAGEMENT_REQUIRED`; `Out_of_scope_device_membership_change_is_not_found`; `Target_user_missing_outside_scope_or_without_organization_is_not_found` (Theory, tách hai nhóm: assign, và remove khi không có membership; master-admin + target org null → `OK`) [RT14]; `Non_master_admin_cannot_assign_self`; `Master_admin_can_assign_self`; `Role_must_belong_to_device_owner_organization` (cả master); `Missing_role_is_not_found`; `Stale_expected_role_is_rejected_on_assign_and_remove` (pre-check) → `DEVICE_MEMBERSHIP_CHANGED`; `Store_level_membership_conflict_surfaces_membership_changed` (hook `BeforeWrite` ghi role khác thẳng vào store sau pre-check → `DEVICE_MEMBERSHIP_CHANGED`, `PolicyVersion`/epoch không đổi).
   - Membership (luật): `Non_master_admin_cannot_assign_role_exceeding_own_device_permissions` (+ subset OK); `Non_master_admin_cannot_assign_high_risk_role_even_when_holding_it`; `Master_admin_can_assign_high_risk_role`; `Non_master_admin_cannot_replace_or_remove_high_risk_current_role`; `Non_master_admin_cannot_replace_or_remove_current_role_exceeding_own_permissions` (luật V4); `Scope_admin_without_own_membership_cannot_delegate_on_device` (luật V6: `permissions.manage` trong scope, không membership, gán `{device.view}` → `GRANT_EXCEEDS_OWN_PERMISSIONS`).
   - Membership (phiên bản role) [RT3]: `Assign_with_stale_role_version_is_rejected` (role bị sửa giữa lúc tải và lúc gán; actor master-admin và non-master → `DEVICE_ROLE_VERSION_STALE`, không ghi, không advance).
   - Membership (gỡ member ngoài phạm vi, Q13) [RT14]: `Org_admin_can_remove_foreign_member_from_own_device` (holder home org khác → `OK`, audit `MEMBERSHIP_REMOVED`); `Org_admin_can_remove_orgless_member`; `Org_admin_can_remove_orphaned_membership_of_deleted_user` (`TestUserScopes` không còn user); `Org_admin_cannot_replace_role_of_foreign_member` (→ `USER_NOT_FOUND`); `Removing_foreign_member_still_obeys_high_risk_and_own_permission_rules` (role hiện tại có H → `HIGH_RISK_REQUIRES_MASTER_ADMIN`; vượt quyền actor → `GRANT_EXCEEDS_OWN_PERMISSIONS`).
   - Membership (ghi): `Membership_write_precedes_policy_version_and_epoch_advance` (dựng trên thay role và gỡ: lúc store ghi `PolicyVersion` = v0, epoch D = e0; lúc `epochs.Advance(D)` `PolicyVersion` = v0+1; sau cùng chỉ epoch D tăng 1); `Fresh_assign_does_not_advance_device_epoch` (`PolicyVersion` tăng, epoch D giữ nguyên) [RT7]; `Post_commit_signal_failure_returns_ok_and_keeps_the_write` (`ThrowOnAdvance`: outcome `OK`, membership và audit đã lưu, không exception thoát handler) [RT7]; `Removing_absent_membership_without_expected_role_is_a_no_op` (không ghi, không advance); `Membership_writes_emit_assigned_changed_removed_audit` (wrapper giữ audit; `PreviousValue/NewValue` JSON `{"roleId":…}`).
   - Role: `Role_create_requires_permissions_manage_in_scope` (`ORGANIZATION_NOT_FOUND`/`PERMISSION_MANAGEMENT_REQUIRED`); `Role_create_rejects_invalid_name_or_description` (Theory `""`, `"  "`, 129 ký tự, mô tả 513 → `DEVICE_ROLE_INVALID`, không exception, không ghi); `Role_create_rejects_unknown_or_wildcard_permission` → `DEVICE_PERMISSION_UNKNOWN`; `Role_create_accepts_server_and_config_domain_codes` (`device.view`, `sys.config_import` → `OK`); `Role_name_is_unique_per_organization_ignoring_case` (`"Operator"` rồi `" operator "` cùng org → `DEVICE_ROLE_NAME_TAKEN`; wrapper ghi nhận lần `Add` thứ hai đã tới store — mã đến từ store, handler không pre-check; org khác OK); `Non_master_admin_cannot_add_or_remove_high_risk_permission`; `Role_update_with_stale_version_is_rejected`; `Role_update_must_stay_within_admin_permissions_on_every_assigned_device`; `Non_master_cannot_shrink_role_exceeding_own_permissions_on_assigned_devices` (R = {device.view, adc.config_edit} trên D1 — actor chỉ có {device.view} — và D2 — actor không membership; PUT `[]` và `[device.view]` đều `GRANT_EXCEEDS_OWN_PERMISSIONS`, không ghi store, không advance) [RT2]; `Role_update_advances_all_affected_devices_in_one_call` (đúng một lời gọi `AdvanceMany` chứa đúng các device dùng role; D3 role khác giữ nguyên; lời gọi xảy ra sau `PolicyVersion`) [RT7]; `Role_of_another_organization_is_not_found` (update/delete với `org` khác → `DEVICE_ROLE_NOT_FOUND`); `Role_delete_reports_stale_version_before_in_use` (role đang dùng + version cũ → `DEVICE_ROLE_VERSION_STALE`; đúng version → `DEVICE_ROLE_IN_USE` (luật V5); gỡ membership → `OK`).
7. `PermissionTests`:
   - Test hiện có: `Feature_wildcard_deny_overrides_exact_allow` viết lại trên resolver (giữ tên; actor non-master: deny `*` + allow `device.read` → `Has("device.read", isMasterAdmin: false)` false); xoá `Device_wildcard_deny_overrides_wildcard_allow` (thay bằng `Wildcard_in_role_permissions_grants_nothing`).
   - Resolver: `Feature_regrant_after_deny_is_granted`, `Persisted_feature_grant_overrides_claim_baseline`, `Feature_wildcard_allow_grants_nothing`, `Master_admin_ignores_feature_and_wildcard_denies` (persisted deny `users.manage` + deny `*` → `Has("users.manage", isMasterAdmin: true)` true, `Has(..., false)` false; `Denied` vẫn chứa `*` và `users.manage`), `Resource_regrant_after_deny_restores_scope`.
   - Helper dòng: `Latest_feature_grants_keep_the_last_row_per_key` (qua grant handler + `InMemoryPermissionStore` thật: `users.manage` grant→deny→grant và `devices.manage` grant→deny → đúng 2 dòng: `users.manage` allow mang `PolicyVersion` lớn nhất của key, `devices.manage` deny; `PolicyVersion` các dòng gốc tăng ngặt → không có tie); `Latest_resource_grants_keep_the_last_row_per_key` (cùng kịch bản cho org); `Latest_grant_is_chosen_by_policy_version_not_input_order` (đảo thứ tự input → cùng kết quả, sắp theo `PolicyVersion` tăng dần); `Latest_grant_with_equal_policy_version_prefers_deny` (feature và resource: hai dòng cùng key, cùng version, allow + deny, cả hai thứ tự input → deny) [RT9].
   - Policy handler: `Feature_policy_handler_ignores_wildcard_allow`, `Feature_policy_handler_uses_latest_persisted_grant`, `Feature_policy_handler_master_admin_passes_despite_persisted_deny` (role `master-admin`, không claim `perm`, persisted deny `users.manage`, cả biến thể thêm claim `deny-perm=*` → requirement `users.manage` `Succeed`; cùng deny trên non-master có claim `perm users.manage` → không `Succeed`).
   - Grant handler: `Wildcard_allow_grant_is_rejected_for_every_actor` (`WILDCARD_GRANT_FORBIDDEN`, cả master-admin); `Wildcard_deny_grant_requires_master_admin`; `Non_master_cannot_self_grant_feature_or_scope` (`SELF_GRANT_FORBIDDEN`); `Grantor_must_hold_feature_permission` (`GRANT_EXCEEDS_OWN_PERMISSIONS`; deny feature không giữ → `OK`); `Master_admin_can_grant_any_feature_to_self`; `Non_master_cannot_grant_scope_outside_own_scope` (`GRANT_EXCEEDS_OWN_PERMISSIONS`); `Rejected_grant_does_not_advance_policy_version`.
   - Grant handler, luật mới:
     - [RT4] `Feature_grant_rejected_when_target_scope_exceeds_grantor_scope` (target có resource grant persisted trên org ngoài phạm vi actor → `GRANT_EXCEEDS_OWN_PERMISSIONS`; target không có resource grant → `OK`); `Resource_grant_rejected_when_target_holds_features_grantor_lacks`.
     - [RT13] `Non_master_cannot_reverse_master_admin_feature_deny` và `Non_master_cannot_reverse_master_admin_resource_deny`, mỗi test có cả nhánh non-master tự ghi deny trước rồi allow → vẫn `GRANT_OVERRIDES_MASTER_DECISION`; `Master_admin_can_reverse_own_deny`; `Non_master_can_reverse_non_master_deny`; `Grants_record_grantor_and_master_flag`.
     - [RT1] `Deny_targeting_master_admin_is_rejected` (feature và resource, actor master-admin lẫn non-master → `GRANT_TARGET_IS_MASTER_ADMIN`, không advance; allow nhắm master-admin → `OK`).
   - `Three_permission_grants_advance_one_shared_policy_version` giữ, chỉ đổi chữ ký (actor master-admin, `outcome.Feature!.PolicyVersion`). Nó khoá tính đơn điệu trên `InMemoryPermissionStore`; với Postgres, migration căn bộ đếm của phase 2 và tie-break deny thắng giữ helper đúng [RT9].
8. Integration (`ManagementApiTests` trừ khi ghi khác; actor có persisted grant dùng `Guid.NewGuid()` riêng để không lẫn với test khác của fixture dùng chung):
   - `Persisted_feature_regrant_after_deny_restores_management_access` (allow→deny→allow `users.manage` → POST `/users/roles` 204).
   - Hai test dưới seed deny thẳng vào `IPermissionStore` (`Advance()` rồi `GrantFeature`/`GrantResource`, `Deny = true`), không qua grant handler: handler nay từ chối deny nhắm master-admin (`GRANT_TARGET_IS_MASTER_ADMIN`), nên dòng này mô phỏng dữ liệu cũ [RT1].
   - `Master_admin_with_persisted_users_deny_still_creates_roles` (seed deny `users.manage`; client chỉ có role `master-admin`, không claim `perm`; CSRF; POST `/api/v1/users/roles` → 204 — qua cả policy `hms.user.manage` lẫn `ScopeAuthorization.HasFeaturePermission` trong `UsersController.CreateRole`; hiện 403).
   - `Master_admin_device_list_ignores_persisted_resource_deny` (seed resource deny Child; client role `master-admin`; `GET /api/v1/devices` chứa `ChildDeviceId` và `UnrelatedDeviceId`; hiện thiếu `ChildDeviceId`).
   - `Feature_grant_rejects_wildcard_allow_even_for_master_admin` (client master-admin, target đã seed Identity, CSRF, POST `/permissions/features` `permission:"*"` → 400 `WILDCARD_GRANT_FORBIDDEN`).
   - `Feature_grant_rejects_wildcard_deny_from_non_master` (client `org-scope` Parent + `perm permissions.manage`, target seed ở Child, `permission:"*", deny:true` → 400 `WILDCARD_GRANT_FORBIDDEN`; cùng request bởi master-admin → 200).
   - `Feature_and_resource_grants_reject_self_grant_for_non_master` (actor seed Identity ở Child, claim `org-scope` Parent + `perm permissions.manage` + `perm devices.manage`; POST `/permissions/features` `devices.manage` và `/permissions/resource-scopes` org Child cho chính mình → cả hai 403 `SELF_GRANT_FORBIDDEN`).
   - `Feature_grant_rejects_permission_the_grantor_does_not_hold` (client `org-scope` Parent + `perm permissions.manage`, target seed ở Child: allow `users.manage` → 403 `GRANT_EXCEEDS_OWN_PERMISSIONS`; deny `users.manage` → 200).
   - `Wildcard_feature_claim_grants_no_management_access` (claim `perm=*` + `org-scope` Parent: `GET /api/v1/users` → 403 qua policy handler; `POST /api/v1/devices` có CSRF, body `{ name: "wildcard-<guid>", ownerOrganizationId: { value: ParentOrganizationId } }` — owner org **trong phạm vi** nên 403 chỉ đến từ thiếu `devices.manage` (`ScopeAuthorization.HasFeaturePermission`), luật 404-org-ngoài-phạm-vi của phase 4 không đổi được kỳ vọng; hiện `*` cho 201 nên test đỏ thật).
   - Luật grant mới qua HTTP:
     - `Feature_grant_rejected_when_target_scope_exceeds_grantor_scope_over_http` [RT4]: client `org-scope` Child + `perm permissions.manage` + `perm devices.manage`; target = user home Child có resource grant persisted trên Parent (như `ResourceScopedUserId` của fixture); allow `devices.manage` → 403 `GRANT_EXCEEDS_OWN_PERMISSIONS`.
     - `Non_master_cannot_reverse_master_admin_deny_over_http` [RT13]: master-admin seed deny `devices.manage` cho target; client non-master giữ `devices.manage` + `permissions.manage` trong phạm vi cấp lại allow → 403 `GRANT_OVERRIDES_MASTER_DECISION`.
     - `Deny_on_master_admin_target_is_rejected_over_http` [RT1]: target seed trong Identity có role `master-admin`; client master-admin POST deny → 400 `GRANT_TARGET_IS_MASTER_ADMIN`.
   - `PersistenceModelTests.Development_registration_resolves_device_access_resolver_and_handlers` (`IDeviceAccessResolver` scoped = `DeviceAccessResolver`; 5 handler resolve trong scope); `Authorization_epoch_advance_many_increments_each_device_once` (EF InMemory: mỗi device tăng đúng 1, device mới bắt đầu từ 1) [RT7].
9. Regression lock (phải xanh không sửa assertion): `Identity_role_mutation_advances_the_shared_policy_version` (master-admin), `Role_mutation_fails_closed_when_persisted_users_permission_is_denied` và `Persisted_resource_grants_are_used_for_scope_filtering_and_denies_win` (actor không role → deny vẫn có hiệu lực với non-master), `Persisted_users_permission_can_reach_management_endpoint_without_bootstrap_claim`, `Permission_grants_require_target_user_and_resource_scope`, `Permission_snapshot_returns_persisted_grants_for_in_scope_target` (chỉ đổi chữ ký seed), `Device_command_uses_persisted_user_organization_when_org_claim_is_absent`, `Device_command_requires_catalog_metadata_at_the_public_boundary`, `Revocation_*`, toàn bộ hub/SignalR tests.

### B. Seams / infrastructure

1. Thêm `AccessActor`, `DeviceAccessPolicy` (Domain) — additive, compile xanh.
2. Thêm `PermissionGrantResolver` (helper `LatestFeatureGrants`/`LatestResourceGrants` trước; `Features`/`Organizations` dựng trên chúng; `Has` xét master-admin trước deny); `AccessActorFactory` (Api, `public static`) trên resolver + `OrganizationScopeResolver` (master-admin → mọi org trước khi đọc grant); `IDeviceAccessResolver`/`DeviceAccessResolver` (store phase 2).
3. Thêm `DeviceAccessHandlers.cs`: vocabulary `DevicePermissionVocabulary.All(catalog)` (phase 1); giới hạn tên/mô tả lấy hằng `DeviceRole` (phase 2).
4. DI: `AddScoped<IDeviceAccessResolver, DeviceAccessResolver>()` + 5 `AddScoped<…Handler>()` cạnh `RevokeDeviceHandler` (`DependencyInjection.cs:81`).

### C. Refactor / implement (thứ tự hấp thụ compile break)

1. Bước 1 (một commit): `CommandContracts.cs` `Confirm`; `SendDeviceCommandHandler` sang `AccessActor` + resolver; `DeviceCommandsController` dựng actor qua `AccessActorFactory.FromPrincipal`, bỏ `organizations.List()` fallback, claim `org`/`device-action`/`deny-*`, `IUserScopeStore`; `SendCommandRequest.Confirm`; `CommandBoundaryTests` + `DeviceCommandAuthorizationTests` cùng commit → không còn reference `AuthorizationContext` ngoài `PermissionTests`.
2. Bước 2: `PermissionTests` — viết lại `Feature_wildcard_deny_overrides_exact_allow` trên resolver (giữ tên, không còn `AuthorizationContext`) và xoá `Device_wildcard_deny_overrides_wildcard_allow` (đúng như A.7); xoá `AuthorizationContext`, `AuthorizationDecision`, `DeviceAuthorizationPolicy` khỏi `AuthorizationPolicy.cs`; `grep -rn --include=*.cs "AuthorizationContext\|DeviceAuthorizationPolicy" src tests` = 0.
3. Bước 3: `ScopeAuthorization` ủy quyền `AccessActorFactory.EffectiveOrganizations` / `PermissionGrantResolver`; `FeaturePermissionAuthorizationHandler` dùng `PermissionGrantResolver` (chữ ký public giữ → 0 call site đổi; allow `*` hết hiệu lực; master-admin xét trước deny, không bị trừ org) → `Feature_policy_handler_master_admin_passes_despite_persisted_deny`, `Master_admin_with_persisted_users_deny_still_creates_roles`, `Master_admin_device_list_ignores_persisted_resource_deny`, `Wildcard_feature_claim_grants_no_management_access` xanh.
4. Bước 4 (một commit): `IUserScopeStore.IsMasterAdmin` + cài đặt Identity; grant handler nhận actor + `PermissionGrantOutcome`, ctor thêm `IUserScopeStore`/`IOrganizationStore`, đủ luật F6 (gồm phạm vi người nhận, deny của master-admin, deny nhắm master-admin) và ghi người cấp [RT1, RT4, RT13]; `PermissionGrantsController` dùng `AccessActorFactory.FromPrincipal` + map outcome (mapping cuối, phase 4 không sửa lại); seed `ManagementApiTests.cs:159-160,240-241,265-266,328-329` dùng `AccessActor` master-admin tĩnh; `PermissionTests` chữ ký mới; `InMemoryPermissionStore` thêm lock → test grant handler và 7 test integration grant xanh.
5. Bước 5: `IAuthorizationEpochStore.AdvanceMany` (InMemory + EF) [RT7]; hoàn thiện 5 handler uỷ quyền (thứ tự check, `expectedRoleVersion`, gỡ member theo Q13, luật sửa role `(old ∪ new)`, map mã store, write ordering với tín hiệu best-effort, audit JSON) → `DeviceAccessHandlerTests` xanh.
6. Commit gợi ý: `feat(authz): evaluate device access by membership role permission`, `fix(authz): resolve grants latest-wins and make master-admin unconditional`, `fix(authz): reject wildcard, self and over-scope permission grants`, `feat(authz): enforce delegation rules for device roles and memberships`.

### D. Tests After

- Chạy lại toàn bộ test mục A → xanh. Bổ sung nếu thiếu: `Command_on_revoked_device_returns_device_revoked_for_member` (member chỉ có membership; giữ `Cached_result_is_not_returned_after_device_is_revoked`), `Non_string_payload_domain_never_falls_back_to_view_permission`.

### Regression Gate

```bash
dotnet build Hms.Server.slnx
dotnet test Hms.Server.slnx
cd frontend && npm run typecheck && npm test && npm run build   # phase không sửa frontend; xác nhận không drift
```

Phải xanh: `AccessActorFactoryTests.cs`, `CommandBoundaryTests.cs`, `DeviceCommandAuthorizationTests.cs`, `DeviceAccessPolicyTests.cs`, `DeviceAccessHandlerTests.cs`, `PermissionTests.cs`, `CatalogTests.cs`, `DeviceEventsHubTests.cs`, `SignalRDeviceEventDeliveryServiceTests.cs`, `ManagementApiTests.cs`, `PersistenceModelTests.cs`, `MaitechRtuDeviceIntegrationTests.cs`.

## Todo List

- [ ] Fixture (wrapper ghi lại, `IUserScopeStore` double, catalog pinned) + test red (factory, policy, command authz, handlers, grants, helper dòng, master-admin bỏ qua deny, integration)
- [ ] `AccessActor`, `DeviceAccessPolicy`, `DeviceAccessCodes`
- [ ] `PermissionGrantResolver` (`LatestFeatureGrants`/`LatestResourceGrants` + tập dựng trên chúng; `Has` xét master-admin trước deny) + `AccessActorFactory` (master-admin mọi org, không trừ deny)
- [ ] `DeviceAccessResolver` (`InScope` gồm membership) + DI scoped
- [ ] `SendDeviceCommandHandler` mới (`PERMISSION_UNMAPPED`, `CONFIRM_REQUIRED`, TTL) + `Confirm`
- [ ] `DeviceCommandsController` dựng actor qua `AccessActorFactory`, bỏ claim/fallback
- [ ] Xoá `AuthorizationContext`/`AuthorizationDecision`/`DeviceAuthorizationPolicy`
- [ ] `ScopeAuthorization` + `FeaturePermissionAuthorizationHandler` ủy quyền resolver (allow `*` hết hiệu lực; master-admin xét trước deny)
- [ ] Grant handlers nhận actor (`WILDCARD_GRANT_FORBIDDEN`, `GRANT_TARGET_IS_MASTER_ADMIN`, `SELF_GRANT_FORBIDDEN`, `GRANT_EXCEEDS_OWN_PERMISSIONS` gồm phạm vi người nhận, `GRANT_OVERRIDES_MASTER_DECISION`; ghi người cấp) + `IUserScopeStore.IsMasterAdmin` + `PermissionGrantsController` map outcome 400/403 (mapping cuối, phase 4 không làm lại) + test unit/integration + seed integration
- [ ] `IAuthorizationEpochStore.AdvanceMany` (InMemory + EF)
- [ ] 5 delegation handlers (`DEVICE_ROLE_INVALID`, vocabulary, store CAS, `expectedRoleVersion`, gỡ member theo Q13, sửa role `(old ∪ new) ⊆ own`, write ordering với tín hiệu best-effort, audit JSON)
- [ ] Review vòng 1: tên role chuẩn hoá theo Identity; luật cây con (Q15); gỡ chặn `*` bởi master (Q16); `IPermissionStore.WithUserLock` (Q17); `DelegationChain` luật A + B (Q18); handler lệnh kiểm phạm vi trước catalog; fail closed khi role hiện tại không đọc được; `AdvanceManyPostgres` theo thứ tự id; bỏ mã plan khỏi comment; test lấp 12 mutant sống sót
- [ ] Regression gate xanh

## Success Criteria

- [ ] `grep -rnE --include=*.cs "AuthorizationContext|DeviceAuthorizationPolicy|FEATURE_PERMISSION_(REQUIRED|DENIED)|RESOURCE_SCOPE_(REQUIRED|DENIED)|DEVICE_ACTION_PERMISSION|ScopeAuthorization\.Actor" src tests` = 0 kết quả.
- [ ] Allow `*` không cấp gì: `ScopeAuthorization.cs`, `FeaturePermissionAuthorizationHandler.cs`, Domain authorization, `DeviceCommandsController.cs` không còn nhắc `*`; trong Application authorization chỉ còn check deny `*` (`PermissionGrantResolver`) và từ chối `*` (grant handler).
- [ ] master-admin vô điều kiện: persisted deny `users.manage` (và deny `*`) không chặn policy `users.manage` lẫn `ScopeAuthorization.HasFeaturePermission` của master-admin (`POST /users/roles` 204); resource deny không bớt org nào của master-admin (`GET /devices` thấy thiết bị của org bị deny); luật "master-admin trước deny" chỉ nằm ở `ResolvedFeaturePermissions.Has` và `AccessActorFactory.EffectiveOrganizations`.
- [ ] `AccessActorFactory`: id thiếu/sai → `null`; master-admin mọi org kể cả khi có resource deny (claim lẫn persisted); non-master: mở rộng org con rồi trừ deny; latest-wins grant→deny→grant; bỏ allow `*`. `ScopeAuthorization` chỉ ủy quyền, không tự gộp claim/grant.
- [ ] `LatestFeatureGrants`/`LatestResourceGrants`: grant→deny→grant giữ dòng cuối; chọn theo `PolicyVersion`, không theo thứ tự input; `Features`/`Organizations` dựng trên chúng, không có chỗ thứ hai chọn dòng grant mới nhất.
- [ ] Member chỉ có membership: in scope và đúng quyền role trên thiết bị đó, `DEVICE_NOT_FOUND` trên thiết bị khác cùng org; scope admin không membership chỉ có `device.view`.
- [ ] Role `adc.view`: 5 namespace `get_status` + `mqtt.get_config` → `DEVICE_PERMISSION_REQUIRED`; `adc.get_status` → `COMMAND_RECORDED`.
- [ ] 3 row confirm → `CONFIRM_REQUIRED` khi blank; 24 row TTL 60000 plan 60s; row null TTL → 120s.
- [ ] Ma trận uỷ quyền đủ code §4 (kể cả `DEVICE_ROLE_INVALID`, `DEVICE_ROLE_NAME_TAKEN` không phân biệt hoa/thường do store báo — handler không pre-check, delete stale trước in-use, CAS store → `DEVICE_MEMBERSHIP_CHANGED`); role nhận `device.view` và `sys.config_import`.
- [ ] Thứ tự ghi: lúc store ghi, `PolicyVersion`/epoch chưa tăng; lúc advance epoch, `PolicyVersion` đã tăng; epoch chỉ tăng ở device mà có holder có thể mất quyền (thay/gỡ membership: đúng device đó; gán mới: không device nào; sửa role: một lời gọi `AdvanceMany`); tín hiệu lỗi sau commit vẫn trả `OK` (wrapper ghi lại; không class test nào implement `IPermissionStore`) [RT7].
- [ ] Assign với `expectedRoleVersion` cũ → `DEVICE_ROLE_VERSION_STALE` cho cả master-admin [RT3]; non-master không thu nhỏ được role vượt quyền mình [RT2]; admin có `CanManageAccess` gỡ được member org khác/không org/mồ côi, không thay role của họ được [RT14].
- [ ] Feature grant allow→deny→allow = granted; cùng version → deny thắng [RT9]; allow `*` → `WILDCARD_GRANT_FORBIDDEN` với mọi actor; deny `*` chỉ master-admin; deny nhắm master-admin → `GRANT_TARGET_IS_MASTER_ADMIN` [RT1]; non-master không self (feature lẫn resource), không cấp feature mình không giữ, không cấp vượt phạm vi org hay feature của mình qua người nhận [RT4], không đảo deny do master-admin ghi kể cả khi tự deny trước [RT13]; mọi dòng grant mới có `GrantedBy`/`GrantedByMasterAdmin`; `PermissionGrantsController` trả 400/403 `{code}` đúng mã — luật, mapping và test unit + integration nằm trọn ở phase này; claim/grant allow `*` không qua `ScopeAuthorization` lẫn policy handler.
- [ ] `Wildcard_feature_claim_grants_no_management_access`: `POST /devices` với owner org trong phạm vi (Parent) → 403 do thiếu `devices.manage`, không phụ thuộc luật 404-ngoài-phạm-vi của phase 4.
- [ ] `IDeviceAccessResolver` + 5 handler resolve được từ DI (scoped).
- [ ] `dotnet test Hms.Server.slnx` xanh; frontend gate xanh.

Bổ sung sau review vòng 1 (quyết định owner 2026-10-06, contract §0 Q15–Q18):

- [ ] Tên role giả dạng `master-admin` mà Identity chuẩn hoá về role thật (U+017F) bị coi là role dành riêng: `PUT /users/{id}/roles/{tên}` và `POST /users/roles` của non-master → 403, `IdentityUserManagement` trả `RESERVED_ROLE_FORBIDDEN`.
- [ ] Non-master cấp hoặc deny org X chỉ khi cả cây con của X nằm trong phạm vi mình (Q15) → `GRANT_EXCEEDS_OWN_PERMISSIONS`, kể cả qua HTTP.
- [ ] Chỉ master-admin ghi được khoá `*`; dòng allow `*` của master không cấp gì, chỉ gỡ deny `*` (Q16); non-master nhận `WILDCARD_GRANT_FORBIDDEN`.
- [ ] Hai quyết định grant cho cùng một user chạy tuần tự (Q17): grant feature và grant org đồng thời của hai admin không cộng dồn vượt quyền ai; deny của master không bị allow non-master chen ngang đảo ngược.
- [ ] Chuỗi bổ nhiệm (Q18): non-master không cấp/deny/gán/thay/gỡ cho người trong chuỗi bổ nhiệm mình, và không sửa role mà người trong chuỗi đang giữ (`GRANT_TO_DELEGATOR_FORBIDDEN`), kể cả hai tầng; quyền thiết bị, feature, org uỷ được bị cắt theo người bổ nhiệm; master cấp lại `permissions.manage` thì hết chuỗi; dòng tạo vòng không lặp vô hạn.
- [ ] Handler lệnh: thiết bị ngoài phạm vi trả `DEVICE_NOT_FOUND` như thiết bị không tồn tại, trước mọi mã catalog.
- [ ] Role hiện tại của membership không đọc được → non-master bị từ chối khi thay/gỡ (fail closed).

## Risk Assessment

| Risk | Mitigation |
|---|---|
| Phase 1/2 lệch tên API (`CommandPermissionResolver`, `DevicePermissionVocabulary.All`, chữ ký store CAS) | Bám tên contract; mọi chỗ gọi gói trong 3 file Application + wrapper test; sửa 1 chỗ nếu lệch |
| Race giữa pre-check membership và ghi | Store CAS `expectedRoleId` (phase 2) trả `DEVICE_MEMBERSHIP_CHANGED`; handler không advance; có test chèn ghi đồng thời |
| Race role update vs assign role đó lên device mới → subset check bỏ sót device | Chấp nhận (quy mô nhỏ; master-admin là người duy nhất chạm H nên rủi ro leo thang thấp) |
| Role bị sửa trong khe vài ms giữa kiểm `expectedRoleVersion` và ghi membership [RT3] | Chấp nhận: store không khoá role; khe chỉ còn vài ms thay vì cả thời gian admin xem màn hình |
| Crash hoặc lỗi giữa store write và tín hiệu (`Advance`/epoch) [RT7] | Handler vẫn trả `OK` và log audit id + device ids; holder còn `device.view` lỡ `AUTHORIZATION_CHANGED` tới lần bump kế tiếp hoặc khi tải lại (chỉ UX, server kiểm lệnh live); holder mất `device.view` vẫn bị reconcile evict trong một tick |
| Gán mới không advance epoch [RT7] | Admin `SeesAllInScope` đang xem D được gán role mới không nhận `AUTHORIZATION_CHANGED`; nút lệnh mới hiện sau khi tải lại (chỉ UX) |
| Dòng grant cũ không có `GrantedByMasterAdmin` [RT13] | Deny master-admin ghi trước release không được bảo vệ khỏi non-master; chấp nhận (chưa có dữ liệu production) |
| Admin org con gỡ được membership của user org cha nếu role đó nằm trong quyền admin con trên D (Q13) [RT14] | Owner chấp nhận; luật high-risk và V4 vẫn chặn gỡ membership mạnh hơn mình |
| `Advance()` global làm rớt event realtime mọi device cho tới khi phase realtime bỏ hàng rào `PolicyVersion` | Không phát hành trước phase realtime (plan.md, contract §5/§12); chạy liền 3 → 4 → 5 trong một working tree |
| Bỏ claim `org`/home-org khỏi commands đổi hành vi user không org (trước 403, nay body code) | Không có issuer production; test hiện có giữ 200; phase 4 thêm 404 trước handler |
| Grant feature `*` đã persist trong DB mất hiệu lực | Chủ đích (lỗi 3), fail-closed; master-admin vẫn bypass theo role; cấp lại bằng mã tường minh |
| Deny đã lưu trên tài khoản master-admin (feature hoặc resource) mất hiệu lực | Chủ đích (Q4, contract §3). Hạn chế master-admin = endpoint gỡ role của phase 4 (Q11); deny mới nhắm master-admin bị từ chối `GRANT_TARGET_IS_MASTER_ADMIN`; trước mọi lần deploy lên DB có dữ liệu chạy truy vấn read-only liệt kê dòng deny feature/resource thuộc tài khoản master-admin (plan.md, mục phát hành). Không test hiện có nào khẳng định hành vi cũ (đã grep); test mới khoá hành vi mới [RT1] |
| Persisted allow ghi đè claim deny (baseline) | Đúng contract §4; claim chỉ test cấp, production không phát hành |
| Phase 4 viết lại luật/mapping/test grant → trùng hoặc lệch | Ranh giới ghi ở Overview, F6, Architecture và Next Steps; phase 4 chỉ xoá `device-actions` và đổi snapshot sang helper dòng |
| Test catalog phụ thuộc mã quyền firmware | Đọc từ `PinnedCommandCatalog`, chỉ literal `adc.view`/`adc.config_edit` theo contract |
| Nhánh PostgreSQL của `WithUserLock` (transaction + `pg_advisory_xact_lock`) và `AdvanceManyPostgres` (`ORDER BY`) chưa chạy được ở môi trường này (không có PostgreSQL) | Đã review; test chạy nhánh in-memory và nhánh EF khác PostgreSQL; chạy lại trên PostgreSQL thật khi có hạ tầng (two-replica soak) |
| Chuỗi bổ nhiệm dựa trên dòng `permissions.manage` mới nhất; dòng cũ không có `GrantedBy` (trước phase 2) kết thúc chuỗi, nên admin bổ nhiệm từ trước không bị giới hạn theo người bổ nhiệm (Q18) | Chấp nhận như dòng cũ ở RT13 (chưa có dữ liệu production) |
| Chưa thu hồi dây chuyền (Q18): người bổ nhiệm mất `permissions.manage` thì admin do họ bổ nhiệm vẫn giữ `permissions.manage` | Backlog. Luật B vẫn cắt theo quyền hiện tại của người bổ nhiệm, nên admin đó không cấp tiếp được `permissions.manage` hay quyền người bổ nhiệm đã mất |
| Quyền admin thường không quản lý admin ngang hàng (mức Entra/Workspace) chưa áp dụng; chỉ chặn chuỗi cha (Q18 luật A) | Owner chọn luật A + B (2026-10-06) |
| Lookup chuỗi bổ nhiệm và giới hạn của từng người bổ nhiệm tốn thêm vài truy vấn mỗi quyết định grant/uỷ quyền; sửa role non-master vẫn 3 truy vấn mỗi thiết bị dùng role | Chấp nhận ở quy mô hiện tại; tối ưu khi đo được |

## Security Considerations

- Fail-closed: permission null → `PERMISSION_UNMAPPED`; domain chỉ nhận JSON string khớp chính xác; role khác org → không quyền, không scope; NameIdentifier sai → không có actor; `*` không cấp gì ở mọi lớp — bypass duy nhất là role `master-admin`, vô điều kiện và xét trước mọi deny; deny (kể cả deny `*`) chỉ hạn chế non-master.
- master-admin vô điều kiện là quyết định Q4. Cách hạn chế là endpoint gỡ role của phase 4 (Q11): chỉ master-admin, đổi security stamp, không gỡ master-admin cuối cùng. Gán role `master-admin` vẫn chỉ master-admin làm được (`RoleAuthorizationPolicy`, so tên theo cách Identity chuẩn hoá nên tên giả dạng như `maſter-admin` cũng là role dành riêng; review vòng 1 tìm ra đường leo thang này qua U+017F và đã chặn) → non-master không có đường tự nâng lên bypass. Deny nhắm master-admin bị từ chối thay vì trả 200 vô tác dụng [RT1].
- Ngoài scope và không tồn tại cùng `DEVICE_NOT_FOUND` trong policy/handler; handler lệnh kiểm phạm vi ngay sau khi tìm thiết bị, trước mọi mã catalog (scope trước revoked → không lộ trạng thái device org khác).
- Membership chỉ mở scope cho đúng một thiết bị, không cấp `SeesAllInScope`/`CanManageAccess`, không mở thiết bị khác cùng org; resource deny không gỡ scope đến từ membership — thu hồi bằng gỡ membership; admin có `CanManageAccess` trên thiết bị gỡ được mọi membership trên đó, kể cả của user org khác (Q13) [RT14].
- Chặn leo thang: không self-grant (feature/resource/membership); khoá `*` chỉ master-admin ghi (deny `*` chặn mọi feature của non-master, allow `*` của master không cấp gì mà chỉ gỡ deny `*`, Q16); grantor phải đang giữ feature; feature allow không đẩy quyền ra ngoài phạm vi org của grantor, resource allow không mở phạm vi dưới feature grantor không có [RT4]; cấp hoặc deny một org phải phủ cả cây con của org đó (Q15); hai quyết định grant cho cùng user chạy tuần tự (Q17); non-master không tác động người trong chuỗi bổ nhiệm mình và chỉ uỷ phần mà cả chuỗi bổ nhiệm cùng giữ (Q18); non-master không đảo deny do master-admin ghi [RT13]; tập con theo membership của actor, cả khi sửa role: không thu nhỏ role vượt quyền mình [RT2]; gán role phải khớp phiên bản role admin đã xem [RT3]; high-risk chỉ master-admin (cả role hiện tại khi replace/remove); `devices.manage` không còn quyền uỷ quyền.
- `Confirm` không vào fingerprint, không persist trong audit, không log; so khớp token thật thuộc transport plan.
- Audit mọi thay đổi role/membership (actor, snapshot JSON cũ/mới, không PII); hết union-of-history nên revoke có hiệu lực ngay ở HTTP request kế tiếp.

## Open Questions

Không còn câu hỏi mở. Đã chốt ở Validation Session 1 (2026-10-05): <!-- Updated: Validation Session 1 - V1, V4, V5, V6 confirmed -->
1. V6 — v1 strict: admin con có `permissions.manage` nhưng không có membership trên D có `perms(actor, D) = ∅` → không uỷ quyền được gì trên D (kể cả `device.view`, dù `SeesAllInScope`); master-admin gán role cho admin trước. `Scope_admin_without_own_membership_cannot_delegate_on_device` khoá hành vi này; role cấp tổ chức để v2.
2. V4 — đối xứng: luật current-role áp cho replace/remove và cho sửa role `(old ∪ new) ⊆ perms(actor, D)` [RT2]. V5 — chặn xoá role đang dùng (`DEVICE_ROLE_IN_USE`).
3. V1 — `sys.config_import` không vào high-risk; test high-risk của handler giữ nguyên, `sys.config_import` vẫn được nhận vào vocabulary.

## Next Steps

- Phase 4 (HTTP): dùng `AccessActorFactory.FromPrincipal` (đã có, không chuyển), `IDeviceAccessResolver`, `DeviceAccessPolicy`, `DeviceAccessCodes`, 5 handler + `DeviceAccessOutcomeCodes` (map status cho route memberships/device-roles theo §4/§6, `DEVICE_ROLE_INVALID` → 400); PUT memberships truyền `expectedRoleVersion`, GET trả `canRemove` và `version` của role [RT3, RT14]; thêm `IUserManagement.RemoveRoleAsync` + `DELETE /api/v1/users/{userId}/roles/{role}` (Q11) và `ListDirectoryAsync` [RT1, RT8]; `PermissionGrantResolver.Features` cho `MeController` (`isMasterAdmin`, deny `*` giữ trong `deniedPermissions`) và `LatestFeatureGrants`/`LatestResourceGrants` cho snapshot `PermissionGrantsController` (không `GroupBy`/`MaxBy` inline); thêm 404 trước handler commands và 403 cho `DEVICE_PERMISSION_REQUIRED`/`DEVICE_REVOKED`; xoá `DeviceActionGrant`, `GrantDeviceActionHandler`, endpoint `device-actions`, `DevicePermissionsController` (kèm dòng device-action trong `Three_permission_grants_advance_one_shared_policy_version`). Không làm lại phần phase này sở hữu: luật grant handler (`WILDCARD_GRANT_FORBIDDEN`, `GRANT_TARGET_IS_MASTER_ADMIN`, `SELF_GRANT_FORBIDDEN`, `GRANT_EXCEEDS_OWN_PERMISSIONS`, `GRANT_OVERRIDES_MASTER_DECISION`), mapping outcome → HTTP của `PermissionGrantsController` và test unit + integration của chúng (kể cả test grant wildcard 400) — chỉ giữ chúng xanh khi gỡ device-action. `Wildcard_feature_claim_grants_no_management_access` dùng owner org trong phạm vi nên luật 404 mới của `POST /devices` không đổi kỳ vọng 403.
- Phase 5 (realtime): `AccessActorFactory.FromPrincipal` + `IDeviceAccessResolver` + `DeviceAccessPolicy` với `device.view` trong hub/delivery (master-admin qua nhờ `AccessActor.IsMasterAdmin`, không deny nào áp lên); dựa vào epoch được advance bởi handler phase này (gán mới không advance epoch; sửa role advance qua `AdvanceMany`); bỏ hàng rào `PolicyVersion` — từ đó mới phát hành được.
- Plan transport 261001 (bật row catalog): đưa idempotency precheck (`FindPlan`, `CORRELATION_ID_REUSED`, `KEY_GENERATION_FENCE`) ra sau `CONFIRM_REQUIRED` và cân nhắc chia idempotency theo user. Plan này giữ nguyên thứ tự vì mọi row còn `DeniedUntilActionContract`, nên precheck không bao giờ thấy plan nào (red team, finding bị bác cho plan này).
