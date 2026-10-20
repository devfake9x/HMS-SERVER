---
phase: 4
title: "Chặn HTTP và endpoint mới"
status: pending
priority: P1
dependencies: [3]
---

# Phase 4: Chặn HTTP và endpoint mới

## Context Links

- Hợp đồng (binding): [design-contract.md](./reports/design-contract.md) §0 Q4/Q6/Q7/Q9/Q11–Q13; §4 mục User management (gỡ role, `ListDirectoryAsync`); §3 (`InScope` gồm membership, master-admin vô điều kiện — feature/resource deny không áp, route feature cần org ∈ effective orgs, `DEVICE_ROLE_INVALID`); §4 (`AccessActorFactory` do phase 3 tạo, `PermissionGrantResolver.LatestFeatureGrants/LatestResourceGrants` cho snapshot, phần Deleted, delegation rules; luật grant handler, mapping outcome `PermissionGrantsController` và test của chúng thuộc phase 3); §5 (`DeviceCommandsController`); §6 (toàn bộ: `DeviceGate`, luật DTO, bảng endpoint — hàng `GET /api/v1/devices` gồm thiết bị member thấy ngoài effective orgs); §9 (frontend bypass master-admin trước deny; mục UI của docs thuộc phase frontend); §11 (V1–V6); §12 (thứ tự chạy).
- Scout: [scout-device-surfaces-surface-map.md](./reports/scout-device-surfaces-surface-map.md), [scout-authz-core-surface-map.md](./reports/scout-authz-core-surface-map.md).
- Brainstorm: `plans/reports/261004-2234-brainstorm-device-permissions-what-where.md` §5.5, §5.6, §7.
- Phase trước: [phase-01](./phase-01-catalog-v2-permission-vocabulary.md) (`DevicePermissionVocabulary`, `DevicePermissionCodes`, `CommandPermissionResolver`), [phase-03](./phase-03-access-evaluation-and-delegation.md) (`AccessActor`, `AccessActorFactory`, `IDeviceAccessResolver`, `DeviceAccessPolicy`, 5 handler, `PermissionGrantResolver` gồm `LatestFeatureGrants/LatestResourceGrants`, luật grant handler + mapping outcome `PermissionGrantsController`). Sau (tuần tự, §12): [phase-05](./phase-05-realtime-access-enforcement.md), [phase-06](./phase-06-frontend-roles-members-gating.md).
- Số dòng trích dẫn trong file này đo trên code **trước** phase 1–3 (§12): khi thực thi, định vị theo symbol (type, method, tên test); số dòng chỉ để tham khảo.

## Overview

Đưa mô hình what + where ra biên HTTP: `DeviceGate` (dùng `AccessActorFactory` của phase 3) quyết định phạm vi/quyền ở mọi route theo thiết bị, tách 404 (ngoài phạm vi/không tồn tại) và 403 (thiếu quyền); thêm endpoint memberships, device-roles, device-permission-codes, my-access; xoá hẳn đường `DeviceActionGrant` (controller ma trận Datacake, endpoint `device-actions`, store, DI). Ở `PermissionGrantsController` phase này chỉ xoá endpoint `device-actions` và đổi snapshot sang helper latest-wins của phase 3; luật grant và mapping outcome giữ nguyên như phase 3. master-admin vô điều kiện ở mọi route (§3). Thêm `DELETE /api/v1/users/{userId}/roles/{role}` để master-admin gỡ được role của nhau, kể cả `master-admin` (Q11) [RT1]. Ingest telemetry chỉ master-admin ở v1 [RT5].

## Key Insights

- Mọi route thiết bị hiện trả 404 chỉ khi không tồn tại rồi `Forbid()` khi ngoài phạm vi → lộ id tồn tại ở org khác: `DevicesController` (`Update`, `RealtimeAuthorization`, `GetTelemetry`, `IngestTelemetry`, `Revoke`), `ProvisioningController.Create/Download`, `LicensesController.Apply`, `PermissionGrantsController.GrantDeviceAction` (device null cũng 403).
- `DeviceCommandsController` sau phase 3 đã dựng actor qua `AccessActorFactory`, bỏ fallback org đầu tiên và claim `device-action`, nhưng vẫn `Ok(handler result)` mọi mã; thiết bị không tồn tại ra 200 `DEVICE_NOT_FOUND` (oracle) → phase này thêm 404 trước handler và map 403/404 (§5).
- Bốn mã server chưa được chặn ở đâu: `Update` dùng `devices.manage`, ingest telemetry không kiểm quyền (không CSRF), list/GET chỉ kiểm phạm vi. `UpdateDeviceRequest(Name, ProjectId, PackageId, AreaId)` gộp rename + classification (`ManagementContracts.cs`, `UpdateDeviceHandler`).
- Ingest telemetry là đường của máy: activate thiết bị Provisioning, ghi đè telemetry mới nhất (kể cả `digital_outputs`) và phát event `status` tới mọi viewer (`DevicesController.cs:96-129`, `DeviceTelemetryContracts.cs:116-122`). Brainstorm §5.6 chốt ingest = định danh dịch vụ, không phải user; bridge (`tools/mqtt-broker/hms-telemetry-bridge.js`) và hai test RTU đăng nhập master-admin → v1 chỉ master-admin; `device.record_measurements` chưa có điểm chặn [RT5].
- Chưa có đường gỡ role: `IUserManagement` chỉ có List/Create/CreateRole/AssignRole (`UserManagementContracts.cs:15-30`); `UsersController` chỉ `PUT {userId}/roles/{role}` (`:74-93`, kiểm `users.manage` → `CanAccessTarget` → reserved role); `AssignRoleAsync` không đổi security stamp (`IdentityUserManagement.cs:71-87`); `AddIdentityCookies()` không cấu hình `SecurityStampValidatorOptions` → mặc định 30 phút (`DependencyInjection.cs:169-170`) [RT1].
- `IdentityUserManagement.ListAsync` stream `AspNetUsers` rồi `await GetRolesAsync` trên cùng DbContext trong vòng lặp (`:15-25`): Npgsql không có MARS và không có retrying strategy → lỗi "command already in progress" ngay dòng đầu; `ManagedUserDto` không có org nên mỗi user thêm một query `Organization` (`:50-55`). Test chạy EF InMemory nên không thấy [RT8].
- Tiêu đề/mô tả 4 quyền thiết bị hiện nằm ở `DevicePermissionsController.cs:24-30` ("View", "Metadata", "Definition", "Measurements") → chuyển sang map nhãn của `device-permission-codes` trước khi xoá controller [RT12].
- Lỗi 3 ở `DevicePermissionsController.Update` (`devices.manage` được cấp quyền, ghi không atomic, `Forbid` giữa vòng lặp, tự cấp cho chính mình); endpoint thứ hai `PermissionGrantsController.GrantDeviceAction` nhận cả `*`. `ResolveDisplayName` hard-code email cá nhân; docs cũng có ở mẫu JSON §4 (`docs/architecture-device-permissions-parity.md:171-172`, phase này viết lại) và §5.2 (`:246-247`, mục UI — phase frontend, §9).
- Consumer của API device-action gỡ cùng lúc: `PermissionContracts.cs` (`DeviceActionGrant`, 2 member `IPermissionStore` + `InMemoryPermissionStore`, `GrantDeviceActionHandler`), `EfPermissionStore` (`GrantDeviceAction`, `DeviceActionGrants`), `DependencyInjection.cs` (`AddScoped<GrantDeviceActionHandler>`), `PermissionGrantsController`, `MeController.Permissions`, `PermissionTests.Three_permission_grants_advance_one_shared_policy_version`. Hiện chỉ `InMemoryPermissionStore`, `EfPermissionStore` implement `IPermissionStore` (đã grep); phase 3 được dặn không tạo implementer trong tests — vẫn grep lại khi xoá.
- `/me/permissions` gộp lịch sử allow/deny, cùng key nằm ở cả hai danh sách, master-admin được append `*`. Snapshot `PermissionGrantsController.Get` trả nguyên lịch sử append-only (`FeatureGrants`, `ResourceGrants`, `DeviceActionGrants`), không lọc dòng mới nhất.
- Claim `perm`/`org-scope` chỉ do test cấp (`TestAuthHandler`), contract §4 giữ claim làm baseline → chỉ port test cần quyền **cấp thiết bị** sang role + membership; test dùng claim cho feature/scope giữ nguyên.
- Fixture `ManagementApiFactory` dùng chung cả class (store InMemory singleton): `Device_revoke_endpoint_changes_lifecycle_state` revoke `ChildDeviceId`; `Persisted_resource_grants_are_used_for_scope_filtering_and_denies_win` ghi deny Child cho `ResourceScopedUserId`, latest-wins đè claim `org-scope` Child → hai test lệnh đang dùng user này sẽ 404 ở gate tuỳ thứ tự chạy. Test mới/sửa tự seed user/device/role riêng.
- Catalog pinned để mọi dòng `DeniedUntilActionContract` → nhánh 403 lệnh chỉ chạm được với catalog test có dòng `Enabled`.
- `RequestLimitTests` khoá `DeviceCommandsController.Send` và giới hạn 64 KiB — giữ tên method/attribute.
- `ScopeAuthorization` là `internal static`, không `InternalsVisibleTo` → `DeviceGate` để `public` để unit test được.
- `InScope` (§3) = org thiết bị ∈ effective orgs **hoặc** actor có membership trên thiết bị → member chỉ có membership vẫn tới được thiết bị đó (200/403 theo role) và thấy nó trong list nếu role có `device.view`. Hàng `GET /api/v1/devices` ở §6 ghi đúng luật này: master-admin thấy tất cả; người khác thấy thiết bị thoả (`SeesAllInScope` AND org ∈ effective orgs) OR (membership của actor trên thiết bị đó có role chứa `device.view`, kể cả khi org ngoài effective orgs). Membership chỉ mở phạm vi thiết bị, không mở feature: `SeesAllInScope` cần org ∈ effective orgs (§3), nên route feature (`devices.manage`, `provisioning.manage`, `licenses.manage`) giữ điều kiện org ∈ effective orgs cho phần 403 ("feature perms unchanged", §6).
- Đầu vào từ phase 3 (đọc code thật trước khi gọi): `AccessActorFactory.FromPrincipal(ClaimsPrincipal, IOrganizationStore, IPermissionStore) → AccessActor?` (null → `Forbid()`); `IDeviceAccessResolver` (nguồn duy nhất của `InScope`/`SeesAllInScope`/`CanManageAccess`/`Permissions`); `DeviceAccessCodes`, `DeviceAccessOutcomeCodes`; static `DeviceAccessResolver.CanManageAccess(actor, orgId)`; `DelegationRules.CheckRoleChange(actor, ownPerms, newPerms?, currentPerms?)` (`internal`); `Create/Update/DeleteDeviceRoleHandler.Handle(actor, org, …)`, `AssignDeviceMembershipHandler.Handle(actor, deviceId, target, roleId, expectedRoleId, expectedRoleVersion)`, `RemoveDeviceMembershipHandler.Handle(actor, deviceId, target, expectedRoleId)` [RT3]; `PermissionGrantResolver.Features/Organizations` và helper row-level `LatestFeatureGrants(grants)`/`LatestResourceGrants(grants)` (dòng mới nhất theo key, §4 — set resolution và snapshot dùng chung, không có bản latest-wins thứ hai). Phase 3 cố ý giữ `DeviceActionGrant`, `GrantDeviceActionHandler` và dòng device-action trong `PermissionTests` để phase này xoá.
- Luật grant feature/resource (`WILDCARD_GRANT_FORBIDDEN`/`GRANT_TARGET_IS_MASTER_ADMIN` 400, `SELF_GRANT_FORBIDDEN`/`GRANT_EXCEEDS_OWN_PERMISSIONS`/`GRANT_OVERRIDES_MASTER_DECISION`/`GRANT_TO_DELEGATOR_FORBIDDEN` 403), luật cây con (Q15), gỡ chặn `*` bởi master (Q16), tuần tự hoá `IPermissionStore.WithUserLock` (Q17), chuỗi bổ nhiệm `DelegationChain` (Q18), mapping outcome trong `PermissionGrantsController` và test của chúng (kể cả test integration wildcard) thuộc phase 3 (§4). Phase này không viết lại, không thêm test trùng; chỉ xoá endpoint `device-actions` và đổi snapshot, nhóm test grant của phase 3 là khoá hồi quy. Khi gỡ 2 member device-action của `IPermissionStore`, giữ `WithUserLock`.
- master-admin vô điều kiện (§3, Q4): feature deny, deny `*` và resource deny không chặn master-admin ở đâu cả (route feature, effective orgs = mọi org, truy cập thiết bị); muốn hạn chế thì master-admin khác gỡ role qua endpoint phase này thêm (Q11) [RT1]. Thiết bị Revoked vẫn 403 `DEVICE_REVOKED` với master-admin (thứ tự policy §3). `AccessActor.FeaturePermissions` của master-admin chỉ chứa allow tường minh, nên route feature phải xét `actor.IsMasterAdmin` trước, không đọc thẳng `FeaturePermissions`. Đã grep: không test hiện có nào kỳ vọng master-admin bị feature deny chặn. `Role_mutation_fails_closed_when_persisted_users_permission_is_denied` dùng actor non-master; master-admin chỉ có ở `Identity_role_mutation_advances_the_shared_policy_version` (claim role + allow) và hai test RTU đăng nhập cookie admin bootstrap, không test nào ghi deny cho master-admin. Test mới của phase này cũng không kỳ vọng như vậy. Vì vậy không phải đổi kỳ vọng test nào; phase này thêm test khoá bypass ở các route nó sửa.
- `perms(actor, D)` chỉ là role membership của chính actor (§4, V6 đã chốt v1 strict) → admin non-master trong test memberships phải có membership riêng chứa đủ quyền của role định cấp.
- Sau phase này `ScopeAuthorization.CanAccessDevice` chỉ còn caller trong `Hubs/` (phase 5 thay); phase 5 chạy ngay sau (§12) và không sửa `DevicesController.cs`.

## Requirements

Functional
- Actor dựng bằng `AccessActorFactory.FromPrincipal` (phase 3, chỉ dùng). `DeviceGate` (§6) là đường duy nhất route thiết bị quyết định phạm vi/quyền; không route thiết bị nào gọi trực tiếp `ScopeAuthorization.CanAccessDevice`.
- `GET /api/v1/devices` (đúng hàng §6): master-admin thấy tất cả; người khác thấy thiết bị thoả (a) `SeesAllInScope` (`devices.manage`/`permissions.manage`) AND org ∈ effective orgs, OR (b) membership của actor trên thiết bị đó có role (cùng org thiết bị) chứa `device.view`, kể cả khi org ngoài effective orgs (§3 `InScope`, Q9).
- `GET /devices/{id}/my-access` (mới): 404 ngoài phạm vi; 200 `{deviceId, isMasterAdmin, seesAllInScope, canManageAccess, roleId, roleName, permissions[]}`, `Cache-Control: no-store`; thiết bị Revoked trong phạm vi → 200 với `permissions: []` (cờ và role giữ nguyên).
- `PUT /devices/{id}`: master-admin, hoặc `devices.manage` + org ∈ effective orgs, hoặc membership: đổi tên cần `device.edit_basics`, đổi classification cần `device.edit_product`.
- `POST /devices`, `POST /devices/{id}/revoke`: master-admin, hoặc `devices.manage` + org ∈ effective orgs; ngoài phạm vi → 404 (`ORGANIZATION_NOT_FOUND` / `DEVICE_NOT_FOUND`).
- `GET realtime-authorization`, `GET telemetry`: `device.view`. `POST telemetry`: chỉ master-admin ở v1 — ngoài phạm vi → 404, trong phạm vi không phải master-admin → 403 (`Forbid()`), Revoked → 403 `DEVICE_REVOKED`; activate + publish giữ nguyên (CSRF giữ nguyên — §10) [RT5].
- `POST /commands`: gate 404 trước handler; handler `DEVICE_PERMISSION_REQUIRED`/`DEVICE_REVOKED` → 403 kèm body, `DEVICE_NOT_FOUND` → 404; mã khác 200 + body (§5).
- Provisioning, Licenses: `FindInScope` null → 404, rồi feature perm (giữ nguyên; master-admin qua thẳng) → 403.
- master-admin vô điều kiện (§3): không route nào của phase này để feature deny, deny `*` hay resource deny chặn master-admin (list, gate, route feature, my-access); chỉ Revoked vẫn 403 `DEVICE_REVOKED` theo thứ tự policy.
- Memberships, device-roles, device-permission-codes theo §6; mọi id trong DTO mới là `Guid` trần. Memberships: PUT body có `expectedRoleVersion` [RT3]; GET trả `canRemove` cho từng dòng và `version` cho từng role [RT3, RT14]; liệt kê cả member org khác đang có membership trên D (chỉ gỡ được, không thay role — Q13). Codes: `{code, group, highRisk, label, commands[]}`, không còn `source` [RT12].
- Gỡ role (Q11) [RT1]: `DELETE /api/v1/users/{userId}/roles/{role}` chỉ master-admin, với mọi role — non-master → 403 kể cả khi có `users.manage` (khác assign, nơi `users.manage` gán được role không dành riêng); CSRF; target không tồn tại hoặc id sai → 403 như assign → `IUserManagement.RemoveRoleAsync` (thứ tự lỗi ở contract §4 User management: `RESERVED_ROLE_FORBIDDEN` → `USER_ID_INVALID`/`USER_NOT_FOUND`/`ROLE_NOT_FOUND` → `LAST_MASTER_ADMIN` → lỗi Identity như `UserNotInRole`); thành công 204, lỗi 400 `{errors}` như assign. `RemoveRoleAsync` gọi `RemoveFromRoleAsync` → `UpdateSecurityStampAsync` → `permissions.Advance()`; `AssignRoleAsync` cũng đổi stamp sau khi gán; `SecurityStampValidatorOptions.ValidationInterval` = 1 phút.
- `IUserManagement.ListDirectoryAsync` → `DirectoryUser(UserId, Email, OrganizationId?)` một projection `ToListAsync`, dùng cho memberships GET [RT8].
- `/me/permissions`: thêm `userId` (Guid), `isMasterAdmin` (`true` nghĩa là toàn quyền, deny không áp — §3; client không suy quyền master-admin từ hai danh sách); latest-wins qua `PermissionGrantResolver`; `permissions` không có `*` (không append cho master-admin); không entry `device:`.
- `PermissionGrantsController`: chỉ (a) xoá endpoint `POST device-actions` cùng ctor deps, (b) snapshot `GET {userId}` lấy mỗi key một dòng mới nhất qua `PermissionGrantResolver.LatestFeatureGrants/LatestResourceGrants` (§4; không latest-wins inline); mỗi dòng mang `grantedBy`/`grantedByMasterAdmin` (field record do phase 2 thêm, không cần code riêng) [RT13]. Luật grant handler, mapping outcome và test của chúng (kể cả `WILDCARD_GRANT_FORBIDDEN` 400) thuộc phase 3: giữ nguyên, không viết test trùng.
- Xoá `DevicePermissionsController`, `POST /api/v1/permissions/device-actions`, `DeviceActionGrant`, `GrantDeviceActionHandler`, `IPermissionStore.GrantDeviceAction/DeviceActionGrants` (ở mọi implementer), code device-action trong `EfPermissionStore`, dòng DI.

Non-functional
- 404 cho thiết bị không tồn tại và ngoài phạm vi giống hệt nhau (status + body `{code:"DEVICE_NOT_FOUND"}`).
- List không N+1: một `ListByUser` + đọc role theo roleId distinct.
- Memberships GET số query cố định, không phụ thuộc số user: `ListDirectoryAsync` + `ListByDevice` + `ListByOrganization` + resolver; không `GetRolesAsync` hay `Organization` theo từng user [RT8].
- Mutation mới: `[ValidateAntiForgeryToken]` + `[RequestSizeLimit(64 * 1024)]`.
- Không thêm hạ tầng transaction; quy tắc ghi do handler phase 3 giữ.

## Architecture

```csharp
// src/Hms.Server.Api/Authorization/DeviceGate.cs  (public, scoped, đăng ký ở Program.cs)
public sealed class DeviceGate(IDeviceStore devices, IDeviceAccessResolver resolver)
{
    // null nếu không tồn tại hoặc (!IsMasterAdmin && !InScope); InScope lấy từ resolver; không xét Revoked
    public Device? FindInScope(AccessActor actor, DeviceId id);
    // DeviceAccessPolicy.Evaluate(resolver.Resolve(actor, device), device, requiredPermission); Device != null chỉ khi Allowed
    public (Device? Device, DeviceAccessDecision Decision) Check(AccessActor actor, DeviceId id, string requiredPermission);
    public static ActionResult ToResult(DeviceAccessDecision decision, string? requiredPermission = null);
    public static int StatusFor(string code); // mã DeviceAccessOutcome → HTTP status
}

// DTO mới: mọi id là Guid trần (strong id serialize thành {value})
public sealed record DeviceAccessDto(Guid DeviceId, bool IsMasterAdmin, bool SeesAllInScope, bool CanManageAccess,
    Guid? RoleId, string? RoleName, IReadOnlyList<string> Permissions);
public sealed record DeviceMembershipsDto(Guid DeviceId, IReadOnlyList<DeviceMemberDto> Members, IReadOnlyList<DeviceRoleOptionDto> Roles);
public sealed record DeviceMemberDto(Guid UserId, string DisplayName, string Email, Guid? RoleId, string? RoleName, bool CanEdit, bool CanRemove); // CanRemove [RT14]
public sealed record DeviceRoleOptionDto(Guid RoleId, string Name, IReadOnlyList<string> Permissions, int Version, bool Assignable); // Version [RT3]
public sealed record AssignDeviceMembershipRequest(Guid RoleId, Guid? ExpectedRoleId, int ExpectedRoleVersion); // [RT3]
public sealed record DeviceMembershipDto(Guid UserId, Guid RoleId, string RoleName, Guid GrantedBy, DateTimeOffset GrantedAt);
public sealed record DeviceRoleDto(Guid RoleId, Guid OrganizationId, string Name, string? Description, IReadOnlyList<string> Permissions, int Version);
public sealed record CreateDeviceRoleRequest(string? Name, string? Description, IReadOnlyList<string>? Permissions);
public sealed record UpdateDeviceRoleRequest(string? Name, string? Description, IReadOnlyList<string>? Permissions, int ExpectedVersion);
public sealed record DevicePermissionCodeDto(string Code, string Group, bool HighRisk, string Label, IReadOnlyList<string> Commands); // [RT12]
public sealed record PermissionSnapshotDto(Guid UserId, bool IsMasterAdmin, long PolicyVersion, DateTimeOffset ExpiresAt,
    IReadOnlyList<string> Permissions, IReadOnlyList<string> DeniedPermissions);
```

- `Check`, `FindInScope`, `ToResult` đúng §6; `StatusFor` là bổ sung (không đổi API §6) dùng chung cho memberships/roles controller.
- `ToResult`: `DEVICE_NOT_FOUND`→404 `{code}`; `DEVICE_REVOKED`→403 `{code}`; `DEVICE_PERMISSION_REQUIRED`→403 `{code, permission}` (`ObjectResult` 403, không `Forbid()` để giữ body).
- `StatusFor` (hằng `DeviceAccessCodes`/`DeviceAccessOutcomeCodes`, không literal): 404 = `DEVICE_NOT_FOUND, USER_NOT_FOUND, DEVICE_ROLE_NOT_FOUND, ORGANIZATION_NOT_FOUND`; 403 = `DEVICE_REVOKED, DEVICE_PERMISSION_REQUIRED, PERMISSION_MANAGEMENT_REQUIRED, SELF_GRANT_FORBIDDEN, HIGH_RISK_REQUIRES_MASTER_ADMIN, GRANT_EXCEEDS_OWN_PERMISSIONS, GRANT_OVERRIDES_MASTER_DECISION`; 409 = `DEVICE_MEMBERSHIP_CHANGED, DEVICE_ROLE_VERSION_STALE, DEVICE_ROLE_NAME_TAKEN, DEVICE_ROLE_IN_USE`; 400 = `DEVICE_ROLE_INVALID, DEVICE_ROLE_ORGANIZATION_MISMATCH, DEVICE_PERMISSION_UNKNOWN, WILDCARD_GRANT_FORBIDDEN, GRANT_TARGET_IS_MASTER_ADMIN` và mọi mã lạ (không bao giờ 200). Ba mã grant có mặt vì `DeviceAccessOutcomeCodes` là bảng mã chung; status khớp mapping grant của phase 3, để `StatusFor_maps_every_device_access_code` không khoá status khác [RT1, RT13].
- Route feature (`devices.manage`, `provisioning.manage`, `licenses.manage`): `FindInScope` (404) trước; `actor.IsMasterAdmin` → qua thẳng (§3, deny không áp; không đọc `actor.FeaturePermissions` vì nó chỉ chứa allow tường minh); ngược lại `!HasFeaturePermission(...) || !actor.EffectiveOrganizations.Contains(device.OwnerOrganizationId)` → `Forbid()` như hiện tại.

Luồng theo endpoint
- `List` (hàng `GET /api/v1/devices` §6): master-admin → bỏ lọc org; ngược lại `seesAll = HasFeaturePermission("devices.manage") || HasFeaturePermission("permissions.manage")` (cùng điều kiện feature với `SeesAllInScope`, tính một lần cho cả list); helper private `MemberViewableDevices(actor)` → `IReadOnlyDictionary<DeviceId, OrganizationId>` (một `IDeviceMembershipStore.ListByUser` + `IDeviceRoleStore.Find` theo roleId distinct, chỉ role chứa `device.view`); giữ device khi `(seesAll && actor.EffectiveOrganizations ∋ device org) || viewable[device.Id] == device org` (role khác org → bỏ, cùng luật fail-closed của resolver); giữ nguyên các filter query.
- `Update`: `FindInScope` null → 404; master-admin, hoặc `devices.manage` + org ∈ effective → handler; ngược lại `RequiredEditPermissions(device, request)` = {`device.edit_basics` nếu `request.Name?.Trim()` ≠ `device.Name` (Ordinal)} ∪ {`device.edit_product` nếu Project/Package/Area khác}; rỗng → {`device.edit_basics`} (fail-closed); `access = resolver.Resolve(actor, device)`, mỗi mã `DeviceAccessPolicy.Evaluate(access, device, code)` → lỗi đầu tiên `DeviceGate.ToResult(decision, code)`.
- `RealtimeAuthorization`, `GetTelemetry`: `gate.Check(actor, id, DevicePermissionCodes.DeviceView)`; `RealtimeAuthorizationDto` và `TELEMETRY_NOT_FOUND` giữ nguyên.
- `IngestTelemetry` [RT5]: `FindInScope` null → 404; `!actor.IsMasterAdmin` → `Forbid()`; master-admin: `gate.Check(actor, id, DevicePermissionCodes.DeviceView)` → Revoked thì 403 `DEVICE_REVOKED`; khối activate + publish giữ nguyên. Không dùng `device.record_measurements` ở v1.
- `Revoke`: `FindInScope` null → 404, rồi luật route feature. `Create`: org không tồn tại, hoặc (non-master) ∉ effective → 404 `ORGANIZATION_NOT_FOUND`; non-master thiếu `devices.manage` → `Forbid()`; master-admin không bị deny chặn (effective orgs = mọi org, §3).
- `MyAccess` (mới): `FindInScope` null → 404; `access = resolver.Resolve(actor, device)`; `roleName` qua `IDeviceRoleStore.Find`; `permissions` = Revoked ? `[]` : sort Ordinal; no-store; `DeviceAccessDto` đặt trong `DevicesController.cs` (mẫu `RealtimeAuthorizationDto`).
- `DeviceCommandsController.Send`: actor (phase 3; null → `Forbid()`) → `gate.FindInScope` null → 404 `{code:"DEVICE_NOT_FOUND"}` → handler → `DEVICE_PERMISSION_REQUIRED|DEVICE_REVOKED` → `StatusCode(403, result)`; `DEVICE_NOT_FOUND` (thiết bị bị xoá sau gate) → `NotFound(result)`; còn lại `Ok(result)`. Ctor thêm `DeviceGate`. Nếu phase 3 chưa thêm `SendCommandRequest.Confirm` (`string? Confirm = null`, cuối) thì thêm ở đây.
- `DeviceMembershipsController` route `api/v1/devices/{deviceId:guid}/memberships`:
  - `GET`: `FindInScope` null → 404; `access = resolver.Resolve(...)`; không `CanManageAccess` → 403 `{code:"PERMISSION_MANAGEMENT_REQUIRED"}`; trả `DeviceMembershipsDto`. User lấy từ một lần `IUserManagement.ListDirectoryAsync` (projection id, email, org), lọc trong bộ nhớ [RT8]: members = có membership trên D (`ListByDevice`) ∪ org ∈ effective orgs của actor (master-admin: mọi user). Membership mà user không còn trong directory (user đã bị xoá; hiện chưa có đường xoá user nên chưa xảy ra) vẫn được liệt kê với `email = ""`, `displayName` = chuỗi userId, `canEdit = false`, `canRemove` theo luật Q13 — UI gỡ được qua dòng chỉ-gỡ [RT14]. Roles = `ListByOrganization(D.OwnerOrganizationId)`, kèm `version` [RT3]. `assignable = CheckRoleChange(actor, access.Permissions, role.Permissions, null) == null`; `canEdit = user có trong directory && (target ≠ actor || master) && (master || org target ∈ effective) && CheckRoleChange(actor, access.Permissions, null, currentRole?.Permissions) == null` (vế đầu khớp kiểm `users.Exists` của handler assign, nên dòng mồ côi luôn `canEdit = false`); `canRemove = membership tồn tại && (target ≠ actor || master) && CheckRoleChange(actor, access.Permissions, null, currentRole.Permissions) == null` — không xét home org của người giữ (Q13) [RT14]. Cùng predicate với handler (B.4), không viết lại luật.
  - `PUT /{userId:guid}` body `AssignDeviceMembershipRequest` → `FindInScope` (404) → `AssignDeviceMembershipHandler(…, ExpectedRoleId, ExpectedRoleVersion)` → 200 `DeviceMembershipDto` | `StatusFor(code)` `{code}` (`DEVICE_ROLE_VERSION_STALE` → 409) [RT3].
  - `DELETE /{userId:guid}?expectedRoleId=` → `FindInScope` (404) → `RemoveDeviceMembershipHandler` → 204 | `StatusFor(code)`. Thiếu `expectedRoleId` = kỳ vọng "không có membership" → 409 nếu đang có (CAS bắt buộc, §4). Membership đang có của user org khác/không org gỡ được (Q13) [RT14].
  - `DisplayNameFrom(email)`: chỉ suy từ prefix email (bỏ nhánh hard-code).
- `DeviceRolesController` route `api/v1/organizations/{organizationId:guid}/device-roles`: `GET` (org không tồn tại/ngoài phạm vi → 404 `ORGANIZATION_NOT_FOUND`; không `DeviceAccessResolver.CanManageAccess(actor, org)` → 403 `PERMISSION_MANAGEMENT_REQUIRED`) → `DeviceRoleDto[]`; `POST` `CreateDeviceRoleRequest` → 201; `PUT /{roleId:guid}` `UpdateDeviceRoleRequest` → 200; `DELETE /{roleId:guid}?expectedVersion=` (`[BindRequired]`, thiếu → 400) → 204. Handler nhận org của route; role thuộc org khác → 404 `DEVICE_ROLE_NOT_FOUND` (test khoá; handler chưa kiểm thì controller `Find` trước). Field request nullable, không DataAnnotations, để model binding không chặn trước: controller truyền `Name ?? ""`, `Permissions ?? []`; tên/mô tả sai → handler trả `DEVICE_ROLE_INVALID` → 400 `{code}` (§3).
- `DevicePermissionCodesController` `GET api/v1/device-permission-codes` (`[Authorize]`): một mục cho mỗi mã của `DevicePermissionVocabulary.All(catalog)` (50); `group` = đoạn trước dấu `.` đầu tiên (4 mã server nằm trọn trong group `device`, nên không cần `source`); `highRisk = DevicePermissionCodes.IsHighRisk(code)`; `label` từ map tĩnh riêng trong file controller [RT12]: `device.view` → "View: can view the device and its data", `device.edit_basics` → "Metadata: can edit device metadata", `device.edit_product` → "Definition: can edit the device definition" (chuyển từ `DevicePermissionsController.cs:24-30`), `device.record_measurements` → "Measurements: reserved, no effect in v1" [RT5]; mã firmware dùng chính code (đặc tả §11 không có nhãn; mô tả từng mã là khoảng trống G4, contract §13); `commands`: `"<type>.<action>"` cho dòng quyền cố định, `"<type>.<action>:<domain>"` cho dòng `domain_policy`, quyền domain lấy qua `CommandPermissionResolver` (phase 1, không tự map class → view/edit); mã server `commands: []`; sort Ordinal theo `code`.
- `UsersController.RemoveRole` [RT1]: `[HttpDelete("{userId}/roles/{role}")]`, `[ValidateAntiForgeryToken]`; `!User.IsInRole(RoleAuthorizationPolicy.MasterAdminRole)` → `Forbid()` cho mọi role (Q11); id sai hoặc `!CanAccessTarget(target)` (user không tồn tại) → `Forbid()`; rồi `users.RemoveRoleAsync(userId, role, actor)` → 204 hoặc 400 `{errors}`. Policy `hms.user.manage` của class vẫn áp, master-admin qua vô điều kiện.
- `MeController.Permissions`: `PermissionSnapshotDto`; feature + resource (`organization:{N}`) qua `PermissionGrantResolver.Features/Organizations` latest-wins (claim baseline, persisted mới nhất đè; controller không tự gộp); một key chỉ ở một danh sách; `permissions` không bao giờ có `*`, kể cả với master-admin; `isMasterAdmin=true` nghĩa là toàn quyền (§3), frontend bypass master-admin trước khi xét deny (§9), nên deny trong `deniedPermissions` (kể cả deny `*`) chỉ chặn non-master; không `device:`. `command-catalog` thuộc phase 1, không đụng.
- `PermissionGrantsController`: actor đã qua `AccessActorFactory` (phase 3); bỏ `GrantDeviceActionHandler`, `IDeviceStore` khỏi ctor; xoá action `GrantDeviceAction` + `GrantDeviceActionRequest`; `PermissionGrantSnapshot(UserId, FeatureGrants, ResourceGrants, PolicyVersion)` (DTO cũ, giữ kiểu) = `PermissionGrantResolver.LatestFeatureGrants(permissions.FeatureGrants(target))`, `PermissionGrantResolver.LatestResourceGrants(permissions.ResourceGrants(target))` (row persisted của target, mỗi key một dòng mới nhất; không `GroupBy`/`MaxBy` inline trong controller). Action `features`/`resource-scopes`, `CanManagePermissions`, `CanManageTarget` và mapping outcome của phase 3 giữ nguyên, không sửa.

## Related Code Files

Create

| Path | Thay đổi |
|---|---|
| `src/Hms.Server.Api/Authorization/DeviceGate.cs` | `FindInScope`, `Check`, `ToResult` (§6) + `StatusFor` |
| `src/Hms.Server.Api/Controllers/DeviceMembershipsController.cs` | GET/PUT/DELETE memberships + DTO + `DisplayNameFrom` |
| `src/Hms.Server.Api/Controllers/DeviceRolesController.cs` | CRUD vai trò theo org + DTO |
| `src/Hms.Server.Api/Controllers/DevicePermissionCodesController.cs` | GET vocabulary + DTO |
| `tests/Hms.Server.IntegrationTests/DeviceAccessSeed.cs` | extension seed device/role/membership qua DI store |
| `tests/Hms.Server.IntegrationTests/DeviceAccessApiTests.cs` | list, update split, feature scope, master-admin không bị deny chặn, telemetry, my-access, memberships, roles, codes, 404 parity |
| `tests/Hms.Server.IntegrationTests/DeviceCommandAuthorizationApiTests.cs` | factory thay `ICommandCatalog` bằng `InMemoryCommandCatalog` có dòng `Enabled`; 404/403/200 lệnh |
| `tests/Hms.Server.UnitTests/DeviceGateTests.cs` | `ToResult`/`StatusFor`, `FindInScope`/`Check` |

Modify

| Path | Thay đổi |
|---|---|
| `src/Hms.Server.Api/Controllers/DevicesController.cs` | list (membership + `SeesAllInScope`, đúng hàng §6), update split, create/revoke 404, master-admin không bị deny chặn, `device.view`, ingest chỉ master-admin [RT5], `GET my-access`; ctor + `DeviceGate`, `IDeviceAccessResolver`, `IDeviceMembershipStore`, `IDeviceRoleStore` |
| `src/Hms.Server.Api/Controllers/UsersController.cs` | + `RemoveRole` (`DELETE {userId}/roles/{role}`) [RT1] |
| `src/Hms.Server.Application/Authorization/UserManagementContracts.cs` | + `RemoveRoleAsync`, `ListDirectoryAsync`, record `DirectoryUser` [RT1, RT8] |
| `src/Hms.Server.Infrastructure/Identity/IdentityUserManagement.cs` | cài `RemoveRoleAsync` (đếm holder `master-admin` qua `GetUsersInRoleAsync` trước khi gỡ) và `ListDirectoryAsync`; `AssignRoleAsync` đổi stamp sau khi gán; `ListAsync` (GET /users) giữ nguyên — lỗi query lồng của nó báo riêng [RT1, RT8] |
| `src/Hms.Server.Api/Controllers/DeviceCommandsController.cs` | ctor + `DeviceGate`; gate 404 trước handler; map 403/404 thay `return Ok(...)` cuối `Send` |
| `src/Hms.Server.Application/Authorization/DeviceAccessHandlers.cs` | chỉ đổi `DelegationRules` `internal` → `public` (không đổi hành vi) |
| `src/Hms.Server.Api/Controllers/ProvisioningController.cs` | `Create`, `Download`: `FindInScope` → 404, rồi luật route feature (master-admin qua thẳng; non-master feature + org scope) → `Forbid()` |
| `src/Hms.Server.Api/Controllers/LicensesController.cs` | `Apply`: tương tự |
| `src/Hms.Server.Api/Controllers/PermissionGrantsController.cs` | xoá `GrantDeviceAction`, `GrantDeviceActionRequest`, ctor `GrantDeviceActionHandler`/`IDeviceStore`, field `DeviceActionGrants`; snapshot qua `PermissionGrantResolver.LatestFeatureGrants/LatestResourceGrants`. Dòng snapshot vẫn là record grant (giữ kiểu DTO cũ, owner chốt 2026-10-05), nên `grantedBy` ra `{"value": ...}` như `userId`. Action grant và mapping outcome (phase 3) không đổi |
| `src/Hms.Server.Api/Controllers/MeController.cs` | chỉ action `Permissions` + `PermissionSnapshotDto` |
| `src/Hms.Server.Api/Program.cs` | `AddScoped<DeviceGate>()` cạnh các đăng ký service Api (sau `AddSignalR()`) |
| `src/Hms.Server.Application/Authorization/PermissionContracts.cs` | xoá `DeviceActionGrant`, `IPermissionStore.GrantDeviceAction/DeviceActionGrants` + impl InMemory, `GrantDeviceActionHandler` |
| `src/Hms.Server.Infrastructure/Persistence/EfPermissionStore.cs` | xoá `GrantDeviceAction`, `DeviceActionGrants` |
| `src/Hms.Server.Infrastructure/DependencyInjection.cs` | xoá `AddScoped<GrantDeviceActionHandler>()`; `services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(1))` cạnh `AddIdentityCookies()` [RT1] |
| `tests/**` class implement `IPermissionStore` (nếu grep C.6 thấy) | xoá 2 member device-action cùng bước |
| `tests/Hms.Server.IntegrationTests/ManagementApiTests.cs` | **test đổi**: xem A.1; xoá 2 test Datacake; factory `SeedIdentityUserAsync` thêm `email` tuỳ chọn; thêm test gỡ role (A.1) |
| `tests/Hms.Server.IntegrationTests/UserRoleRemovalCookieTests.cs` (mới) | cookie thật như `MaitechRtuDeviceIntegrationTests`, factory đặt `ValidationInterval = TimeSpan.Zero`: master-admin bị gỡ role thì cookie cũ bị từ chối [RT1] |
| `tests/Hms.Server.IntegrationTests/IdentityUserManagementTests.cs` (mới) | `RemoveRoleAsync`, `LAST_MASTER_ADMIN`, đổi stamp khi gán/gỡ, `ListDirectoryAsync` [RT1, RT8] |
| `tests/Hms.Server.UnitTests/PermissionTests.cs` | **test đổi** `Three_permission_grants_advance_one_shared_policy_version` → bỏ dòng `GrantDeviceActionHandler`, đổi tên `Feature_and_resource_grants_advance_one_shared_policy_version` |
| `tests/Hms.Server.UnitTests/RequestLimitTests.cs` | **test thêm** 64 KiB cho memberships PUT, roles POST/PUT |
| `docs/architecture-device-permissions-parity.md` | sang mô hình mới, trừ §5 UI: ở §5 chỉ thêm một dòng trỏ ở đầu mục (xem C.9) |
| `docs/architecture-hms-server.md` | ghi chú ngắn ở `:30`, `:436` |

Delete

| Path | Lý do |
|---|---|
| `src/Hms.Server.Api/Controllers/DevicePermissionsController.cs` | thay bằng memberships (§6) |

Không đổi nhưng là khoá hồi quy: `tests/Hms.Server.IntegrationTests/MaitechRtuDeviceIntegrationTests.cs` (hai test master-admin cookie: list, POST/GET telemetry).

## Implementation Steps

### A. Tests Before (red)

1. `ManagementApiTests.cs` — sửa test hiện có (compile được, đỏ do status/route):
   - `Realtime_authorization_snapshot_is_scoped_nonrevoked_and_no_store`: caller = user mới + claim org-scope Parent + membership role `{device.view}` trên thiết bị seed riêng → 200 no-store; unrelated → **404** `DEVICE_NOT_FOUND` (từ 403); revoked trong phạm vi → 403 + body `DEVICE_REVOKED`; anonymous 401.
   - `Device_update_rejects_a_device_outside_the_callers_scope`, `Device_revoke_rejects_a_device_outside_the_callers_scope` → 404 + code.
   - Phase 3 (review vòng 1) đã đổi tên `Device_command_uses_persisted_user_organization_when_org_claim_is_absent` thành `In_scope_device_command_reaches_the_catalog_lookup`, và cho nó cùng `Device_command_requires_catalog_metadata_at_the_public_boundary` user mới thay `ResourceScopedUserId`: handler lệnh nay kiểm phạm vi trước catalog nên deny Child của test khác làm chúng phụ thuộc thứ tự. Phase này chỉ còn bỏ claim `devices.command` (caller vẫn org-scope Child); không đụng literal catalog → vẫn 200 `COMMAND_NOT_IN_CATALOG` / `CATALOG_STALE`.
   - `Me_permissions_and_catalog_are_no_store_and_include_trust_metadata`: thêm `userId` (`GetGuid()`) = caller, `isMasterAdmin=false`.
   - `Permission_snapshot_returns_persisted_grants_for_in_scope_target`: thêm allow→deny trên một key feature khác `device.read` (vd `users.manage`) và allow→deny trên một org → mỗi key đúng một dòng, dòng đó `Deny=true` (snapshot qua `LatestFeatureGrants/LatestResourceGrants`); dòng seed bằng actor master-admin có `grantedByMasterAdmin=true` và `grantedBy` khác null, cùng dạng với `userId` [RT13]; assertion `device.read` hiện có giữ nguyên.
   - `Persisted_resource_grants_are_used_for_scope_filtering_and_denies_win`: thêm persisted `devices.manage` + thiết bị org Parent → `Contains` (list cần `SeesAllInScope`).
   - Xoá `Device_permissions_matrix_returns_definitions_and_user_grants_with_datacake_parity`, `Device_permissions_mutation_requires_csrf_and_supports_optimistic_concurrency` (thay bởi A.2).
   - Mới, gỡ role qua HTTP (Q11) [RT1]: `Master_admin_can_remove_reserved_role_over_http` (seed trong Identity hai user có `master-admin` — target và một người khác — để không chạm `LAST_MASTER_ADMIN` → 204, target mất role); `Non_master_cannot_remove_any_role` (non-master có `users.manage` + target trong phạm vi: gỡ `master-admin` → 403, gỡ một role tự tạo không dành riêng → 403; khoá luật "chỉ master-admin, mọi role" của Q11); `Remove_role_requires_csrf_and_existing_target` (thiếu CSRF → 400; user không tồn tại → 403).
   - `IdentityUserManagementTests` (mới, project integration; mỗi test dựng service provider riêng với EF InMemory DB tên riêng để đếm holder chính xác) [RT1, RT8]: `Remove_role_rotates_security_stamp_and_advances_policy_version`; `Removing_last_master_admin_is_rejected` (`LAST_MASTER_ADMIN`, role còn nguyên, stamp không đổi); `Remove_role_not_assigned_returns_identity_error`; `Assign_role_rotates_security_stamp`; `List_directory_projects_id_email_and_organization` (user không org → `OrganizationId` null).
   - Mới: `Device_permissions_matrix_endpoint_is_removed` (caller master-admin, thiết bị tồn tại: GET và PUT có CSRF `/devices/{id}/permissions` → 404 hoặc 405; master-admin luôn qua gate nên đây là route đã mất, không phải 404 của gate); `Device_action_grant_endpoint_is_removed` (master-admin, CSRF, body cũ hợp lệ, POST `/api/v1/permissions/device-actions` → 404 hoặc 405). Hai test này là bằng chứng phía tests rằng endpoint đã gỡ (C.7 không grep `tests`). `Me_permissions_are_latest_wins_without_wildcard_or_device_entries` (allow→deny→allow chỉ ở `permissions`; master-admin có deny persisted `users.manage` (seed thẳng vào `IPermissionStore`, mô phỏng dữ liệu cũ [RT1]) vẫn `isMasterAdmin=true`, `permissions` không `*`, không entry `device:`). Không thêm test grant wildcard: phase 3 đã có test integration `WILDCARD_GRANT_FORBIDDEN` 400 (khoá ở A.4).
2. `DeviceAccessApiTests.cs` (`IClassFixture<ManagementApiFactory>`, mỗi test seed device/user/role riêng; mọi id trong response assert bằng `GetGuid()`):
   - `Missing_and_out_of_scope_devices_return_identical_not_found` (Theory: GET telemetry, realtime-authorization, my-access, memberships; PUT device, memberships/{userId}; POST telemetry, revoke, commands, provisioning-bundles, licenses/apply; body hợp lệ model, có CSRF; caller chỉ org-scope Parent, không feature → chứng minh 404 đi trước 403 feature) → Guid ngẫu nhiên và `UnrelatedDeviceId` cùng 404 `{code:"DEVICE_NOT_FOUND"}`.
   - List: `Device_list_includes_member_only_device_outside_effective_organizations` (không org-scope; role `{device.view}` trên A thuộc org Unrelated → list chỉ có A; B role `{adc.view}` và `UnrelatedDeviceId` ẩn; realtime-authorization A → 200, B → 403 `DEVICE_PERMISSION_REQUIRED`); `Device_list_hides_in_scope_devices_without_device_view_from_members` (org-scope Parent, không feature: `ChildDeviceId` ẩn, thiết bị có membership `{device.view}` hiện); `Device_list_shows_all_in_scope_devices_to_scope_admin` (`permissions.manage` + org-scope Parent, không membership: có `ChildDeviceId`, không `UnrelatedDeviceId`); `Device_list_shows_every_device_to_master_admin`.
   - Update: `Member_rename_requires_edit_basics` (rename 200; đổi classification → 403 `{code, permission:"device.edit_product"}`); `Member_classification_change_requires_edit_product` (ngược lại); `Device_update_without_manage_or_membership_is_forbidden` (403 `DEVICE_PERMISSION_REQUIRED`).
   - Create/revoke/feature: `Device_create_for_out_of_scope_organization_returns_not_found` (404 `ORGANIZATION_NOT_FOUND`); `Device_revoke_requires_devices_manage_in_scope` (member → 403); `Membership_scope_does_not_grant_feature_routes` (member-only thiết bị ngoài effective orgs + claim `devices.manage`, `provisioning.manage`, `licenses.manage` → revoke/provisioning/licenses 403); `Master_admin_is_not_blocked_by_feature_or_scope_denies` (§3: user claim role `master-admin` + deny persisted `*`, `devices.manage`, `provisioning.manage`, `licenses.manage` và resource deny org của thiết bị seed riêng, ghi thẳng vào `IPermissionStore` (`Advance()` rồi `GrantFeature`/`GrantResource`, `Deny = true`) vì grant handler nay từ chối deny nhắm master-admin, nên dòng này mô phỏng dữ liệu cũ [RT1] → list có thiết bị; my-access `isMasterAdmin=true`; `PUT /devices/{id}` 200; `POST /devices` vào org đó, provisioning, licenses/apply không 403/404 (tới handler); revoke chạy cuối, không 403/404).
   - Telemetry: `Telemetry_read_requires_device_view`; `Telemetry_ingest_requires_master_admin` (member có role chứa `device.record_measurements` → 403; master-admin → 200; revoked → 403 `DEVICE_REVOKED`; ngoài phạm vi → 404) [RT5].
   - my-access: `My_access_returns_member_role_and_sorted_permissions`; `My_access_for_scope_admin_has_no_role`; `My_access_flags_master_admin` (no-store); `My_access_for_revoked_device_returns_empty_permissions` (200, `permissions: []`).
   - Memberships (actor non-master = org-scope Parent + `permissions.manage` + membership riêng trên D đủ quyền role định cấp — §4/V6): `Device_membership_mutations_require_csrf` (PUT/DELETE 400); `Device_membership_with_stale_expected_role_returns_conflict` (409 `DEVICE_MEMBERSHIP_CHANGED`, cả DELETE); `Device_membership_self_grant_is_forbidden` (403 `SELF_GRANT_FORBIDDEN`); `Device_membership_exceeding_own_permissions_is_forbidden` (403 `GRANT_EXCEEDS_OWN_PERMISSIONS`); `Device_membership_with_high_risk_role_requires_master_admin` (403 `HIGH_RISK_REQUIRES_MASTER_ADMIN`); `Master_admin_can_assign_high_risk_role` (200, my-access của target có `sys.reboot`); `Devices_manage_alone_cannot_manage_memberships` (403 `PERMISSION_MANAGEMENT_REQUIRED`, lỗi 3); `Device_membership_role_from_another_organization_is_rejected` (400 `DEVICE_ROLE_ORGANIZATION_MISMATCH`); `Device_membership_list_requires_permission_management` (chỉ org-scope → 403; đủ quyền → `{deviceId, members, roles}`, self row `canEdit=false`, role rủi ro cao `assignable=false`, `displayName` "First Last" cho email prefix `first.last`); `Removing_and_reassigning_membership_restores_device_access` (lỗi 2, member có org-scope Parent: PUT → realtime-authorization 200; DELETE → 403 `DEVICE_PERMISSION_REQUIRED`; PUT lại `expectedRoleId=null` → 200); `Device_membership_assign_with_stale_role_version_returns_conflict` (role sửa sau GET, PUT với version cũ → 409 `DEVICE_ROLE_VERSION_STALE`, membership không đổi; GET lại có `version` mới) [RT3]; `Org_admin_can_remove_foreign_member_and_list_shows_remove_only_row` (master-admin gán user org Unrelated lên D của Parent; GET bởi admin Parent: dòng đó `canEdit=false`, `canRemove=true`; PUT đổi role → 404 `USER_NOT_FOUND`; DELETE → 204) [RT8, RT14].
   - Roles: `Device_role_create_returns_version_one` (201); `Device_role_name_taken_is_case_insensitive` (POST "Operators" rồi "operators", PUT đổi tên trùng khác hoa/thường → 409 `DEVICE_ROLE_NAME_TAKEN`); `Device_role_unknown_permission_returns_bad_request` (kể cả `*` → 400 `DEVICE_PERMISSION_UNKNOWN`); `Device_role_invalid_name_or_description_returns_bad_request` (blank, 129 ký tự, mô tả 513 → 400 `DEVICE_ROLE_INVALID`, không 500); `Device_role_of_another_organization_in_route_returns_not_found` (404 `DEVICE_ROLE_NOT_FOUND`); `Device_role_update_with_stale_version_returns_conflict` (409 `DEVICE_ROLE_VERSION_STALE`, đúng version → 200 version 2); `Device_role_delete_requires_current_version` (thiếu `expectedVersion` → 400; stale → 409 `DEVICE_ROLE_VERSION_STALE`, kể cả khi role đang dùng); `Device_role_delete_in_use_returns_conflict` (đúng version → 409 `DEVICE_ROLE_IN_USE`, gỡ membership → 204); `Device_roles_for_out_of_scope_organization_return_not_found` (GET/POST → 404); `Device_role_with_high_risk_permission_requires_master_admin`; `Device_roles_require_permission_management`; `Device_role_mutations_require_csrf`.
   - Codes: `Device_permission_codes_expose_vocabulary_labels_commands_and_high_risk` (50 mục = `DevicePermissionVocabulary.All` của catalog trong DI; không có field `source`; 4 mã group `device` có `label` khác code, `device.record_measurements` ghi rõ chưa có hiệu lực; mã firmware có `label` = code; `sys.reboot` highRisk, `commands` chứa `system.reboot`; `adc.view` chứa `adc.get_status` và `config.get:adc`; anonymous 401) [RT12].
   - Cookie (`UserRoleRemovalCookieTests`) [RT1]: `Demoted_master_admin_cookie_is_rejected_after_stamp_validation` (đăng nhập cookie thật bằng master-admin B; master-admin A gỡ role của B; request kế tiếp của B tới route chỉ master-admin → 401, vì factory đặt `ValidationInterval = TimeSpan.Zero` và stamp đã đổi).
3. `DeviceCommandAuthorizationApiTests.cs` (factory riêng, `TestAuthHandler` dùng lại, catalog `Enabled`: `adc/get_status`→`adc.view`, `sms/get_status`→`sms.view`, `system/reboot`→`sys.reboot` có `ConfirmTemplate`; version/hash đọc `ICommandCatalogMetadata` trong DI):
   - `Command_permission_is_resolved_per_type_and_action` (member chỉ có membership role `{adc.view}`: adc → 200 code ≠ `DEVICE_PERMISSION_REQUIRED`; sms → 403 body `DEVICE_PERMISSION_REQUIRED` — lỗi 1 ở biên HTTP).
   - `Command_on_revoked_device_returns_forbidden` (403 `DEVICE_REVOKED`); `Command_for_out_of_scope_device_returns_not_found_before_catalog_checks` (lệnh lạ → 404, không 200 `COMMAND_NOT_IN_CATALOG`).
   - `Confirm_required_command_without_confirm_returns_ok_with_confirm_required` (master-admin, không `confirm` → 200 `CONFIRM_REQUIRED`; có `confirm` → code khác `CONFIRM_REQUIRED`).
4. Khoá hồi quy phải xanh không sửa: `Organization_scope_filters_device_list_to_descendants`, `Device_list_supports_server_side_text_filter_with_scope_enforcement`, `Device_update_renames_and_clears_nullable_classification_without_changing_owner`, `Device_revoke_endpoint_changes_lifecycle_state`, `Device_command_mutation_requires_csrf_token`, `Provisioning_bundle_download_is_post_only_and_csrf_protected`, `Provisioning_a_revoked_device_is_rejected_at_the_http_boundary` (`FindInScope` không xét Revoked → vẫn 400 `DEVICE_REVOKED` từ handler), `Permission_grants_require_target_user_and_resource_scope`, `Role_mutation_fails_closed_when_persisted_users_permission_is_denied` (feature deny vẫn chặn non-master), nhóm test grant của phase 3 (`Feature_grant_rejects_*` trong `ManagementApiTests`, test grant handler trong `PermissionTests`: luật grant + mapping outcome `PermissionGrantsController`, kể cả `WILDCARD_GRANT_FORBIDDEN` 400), hai test `MaitechRtuDeviceIntegrationTests`, `RequestLimitTests`.

### B. Seams / infrastructure

1. `DeviceAccessSeed.cs`: `SeedDevice(this IServiceProvider, Guid orgId, string name)`, `SeedDeviceRole(..., Guid orgId, params string[] permissions)` (tên `role-{Guid:N}`), `SeedMembership(..., Guid userId, Guid deviceId, Guid roleId)` — gọi thẳng `IDeviceRoleStore.Add` / `IDeviceMembershipStore.Upsert(membership, expectedRoleId: null, audit)` (phase 2) với audit actor cố định. `ManagementApiFactory.SeedIdentityUserAsync(userId, orgId, email = null)`.
2. `DeviceGate.cs` (public) + `Program.cs` `AddScoped<DeviceGate>()`.
3. `DeviceGateTests.cs`: `ToResult_maps_not_found_revoked_and_permission_required` (404/403/403 + body); `StatusFor_maps_every_device_access_code` (bảng Architecture; mã lạ → 400); `Find_in_scope_follows_resolver_scope` (store InMemory + `DeviceAccessResolver` thật: không tồn tại/ngoài phạm vi → null; member-only ngoài effective orgs → device; master-admin với `EffectiveOrganizations` rỗng → device, `Check` ALLOWED; revoked trong phạm vi → `FindInScope` trả device, `Check` → `DEVICE_REVOKED` với Device null, kể cả master-admin).
4. Predicate `assignable`/`canEdit`/`canRemove`: đổi `DelegationRules` và `DelegationChain` (phase 3) sang `public`, không đổi logic. Preview phải khớp handler: quyền uỷ được trên D là `DelegationChain.DevicePermissions(...)` (cắt theo chuỗi bổ nhiệm, Q18 luật B); dòng của người trong chuỗi bổ nhiệm của actor có `canEdit=false`, `canRemove=false` (luật A); role hiện tại không đọc được → không sửa/gỡ được với non-master. `DeviceAccessHandlerTests` và `DelegationChainTests` (phase 3) bảo vệ hành vi.

### C. Refactor / implement

1. `DevicesController`: List, Update, RealtimeAuthorization, GetTelemetry, IngestTelemetry, Revoke, Create, `MyAccess` theo Architecture (master-admin xét `IsMasterAdmin` trước mọi check feature). Đỏ → xanh: A.1 (realtime/update/revoke), A.2 list/update/feature/master-admin/telemetry/my-access.
2. `DeviceCommandsController`: ctor + `DeviceGate`; `FindInScope` 404 trước handler; map 403/404. Giữ tên `Send`, `[RequestSizeLimit(64 * 1024)]`, `[ValidateAntiForgeryToken]`; không đổi chỗ dựng actor (phase 3).
3. `ProvisioningController`, `LicensesController`: actor null → `Forbid()`; `gate.FindInScope(...)` null → `NotFound(new { code = "DEVICE_NOT_FOUND" })`; rồi luật route feature (master-admin qua thẳng; non-master thiếu feature hoặc org ∉ effective → `Forbid()`); bỏ `ScopeAuthorization.CanAccessDevice`.
4. `IUserManagement.ListDirectoryAsync` + `DirectoryUser` [RT8]; `DeviceRolesController`, `DevicePermissionCodesController` (map nhãn, bỏ `source`) [RT12], `DeviceMembershipsController` (`canRemove`, role `version`, `expectedRoleVersion`) [RT3, RT14].
5. `MeController.Permissions` sang DTO mới.
5b. Gỡ role [RT1]: `IUserManagement.RemoveRoleAsync` + cài đặt Identity (đổi stamp, chặn master-admin cuối cùng); `AssignRoleAsync` đổi stamp; `UsersController.RemoveRole`; `SecurityStampValidatorOptions.ValidationInterval` 1 phút → `IdentityUserManagementTests`, test gỡ role HTTP và `UserRoleRemovalCookieTests` xanh.
6. Gỡ đường device-action, build đỏ chỉ nằm trong bước này: (a) `PermissionGrantsController` bỏ endpoint + ctor deps; snapshot gọi `PermissionGrantResolver.LatestFeatureGrants/LatestResourceGrants` (không latest-wins inline); không sửa action grant, mapping outcome và test grant của phase 3; (b) `MeController` đã xong ở C.5; (c) ghi lại chuỗi email trong `ResolveDisplayName` (cho C.7) rồi xoá `DevicePermissionsController.cs`; (d) xoá `DeviceActionGrant`, `GrantDeviceActionHandler`, 2 member interface + impl `InMemoryPermissionStore`; cùng lúc `rg -n "IPermissionStore" tests` → mọi class implement (kể cả probe/wrapper nếu phase 3 lỡ tạo) bỏ 2 member; (e) xoá code device-action `EfPermissionStore.cs`; (f) xoá dòng DI; (g) `PermissionTests` → `Feature_and_resource_grants_advance_one_shared_policy_version` (kỳ vọng 1, 2; giữ chữ ký actor của phase 3). `dotnet build Hms.Server.slnx` xanh sau (g); không commit giữa (a)–(g).
7. Kiểm tra sạch, chỉ trên `src`: `rg -n "DeviceActionGrant|GrantDeviceAction|device-action|DevicePermissionsController" src --glob '!**/Migrations/**' --type cs` = 0 (migration xoá dòng `device-action` của phase 2 được loại bởi glob); `rg -n "CanAccessDevice" src/Hms.Server.Api/Controllers --type cs` = 0 (còn lại chỉ `Hubs/`, phase 5 gỡ); `rg -n "GroupBy|MaxBy" src/Hms.Server.Api/Controllers/PermissionGrantsController.cs` = 0; chuỗi email ghi ở C.6(c): `src` = 0, docs parity chỉ còn trong §5 (phase frontend). Không grep `tests`: test hợp lệ vẫn chứa các chuỗi này (URL `/permissions/device-actions` trong test endpoint đã xoá, `Migration_purges_legacy_device_action_grants` của phase 2). Phía tests, việc gỡ endpoint được chứng minh bằng `Device_permissions_matrix_endpoint_is_removed` và `Device_action_grant_endpoint_is_removed` (404/405).
8. `RequestLimitTests`: thêm assert 64 KiB cho `DeviceMembershipsController.Assign`, `DeviceRolesController.Create/Update`.
9. Docs `docs/architecture-device-permissions-parity.md`, **không sửa nội dung §5 (UI)** — phase frontend sở hữu (§9), kể cả tên/email thật ở §5.2. Ở §5 chỉ thêm đúng một dòng ngay dưới tiêu đề mục: `> Mục UI này do phase frontend của plan plans/261005-0556-device-permissions-what-where cập nhật theo mô hình vai trò + membership; nội dung bên dưới còn mô tả ma trận cũ.` (phase frontend thay dòng này khi viết lại §5). Phần còn lại: header đánh dấu thay ma trận Datacake bằng role + membership (trỏ `plans/261005-0556-device-permissions-what-where`); §1.1 bỏ "4 quyền hạt nhân", nêu 4 mã server + catalog v2; §1.2 thay `PolicyVersion` bằng `expectedRoleId`/`expectedVersion`; §2 sơ đồ `permission_grants(kind='device-action')` → `device_roles`/`device_memberships`; §3 schema theo contract §8; **§3.1 xoá** hai index không tồn tại (thực tế chỉ `(UserId, Kind, PolicyVersion)` trong `ApplicationDbContext`); §4 thay bằng memberships, device-roles, my-access, device-permission-codes, quy tắc 404/403, mẫu JSON dùng placeholder (bỏ tên/email thật); thêm một câu: "feature deny và resource deny không còn chặn master-admin; muốn hạn chế master-admin thì master-admin khác gỡ role qua `DELETE /api/v1/users/{userId}/roles/master-admin`" [RT1]; §7 trỏ plan mới. `docs/architecture-hms-server.md:30, :436`: thêm một dòng "phân quyền thiết bị = vai trò theo org + membership theo thiết bị, quyền rủi ro cao chỉ master-admin, xem `docs/architecture-device-permissions-parity.md`".

### D. Tests After

1. Toàn bộ A.1–A.3 xanh; A.4 xanh không sửa.
2. `DeviceGateTests`, `RequestLimitTests` mở rộng xanh.
3. Sau C.6 chạy lại `Device_permissions_matrix_endpoint_is_removed`, `Device_action_grant_endpoint_is_removed` (caller master-admin luôn qua gate → 404/405 là do route đã mất, không phải do gate) và Theory 404 parity (route còn lại vẫn 404 `DEVICE_NOT_FOUND` qua gate).
4. Nhóm test grant của phase 3 (A.4) xanh không sửa sau khi gỡ endpoint `device-actions` và đổi snapshot.

### Regression Gate

```bash
dotnet build Hms.Server.slnx
dotnet test Hms.Server.slnx
cd frontend && npm run typecheck && npm test && npm run build
```

Phải xanh: `ManagementApiTests.cs`, `DeviceAccessApiTests.cs`, `DeviceCommandAuthorizationApiTests.cs`, `IdentityUserManagementTests.cs`, `UserRoleRemovalCookieTests.cs`, `MaitechRtuDeviceIntegrationTests.cs`, `PersistenceModelTests.cs`, `PermissionTests.cs`, `CommandBoundaryTests.cs`, `RequestLimitTests.cs`, `DeviceGateTests.cs`, `DeviceAccessPolicyTests.cs`, `DeviceAccessHandlerTests.cs`, `DeviceEventsHubTests.cs`, `SignalRDeviceEventDeliveryServiceTests.cs`. Frontend không đổi ở phase này; chạy để xác nhận không vỡ.

## Todo List

- [ ] A.1 sửa/xoá/thêm test `ManagementApiTests.cs`
- [ ] A.2 `DeviceAccessApiTests.cs`
- [ ] A.3 `DeviceCommandAuthorizationApiTests.cs`
- [ ] B.1 `DeviceAccessSeed.cs` + `SeedIdentityUserAsync(email)`
- [ ] B.2 `DeviceGate` + đăng ký `Program.cs`
- [ ] B.3 `DeviceGateTests`
- [ ] B.4 `DelegationRules` public, dùng chung cho preview
- [ ] C.1 `DevicesController` (master-admin vô điều kiện)
- [ ] C.2 `DeviceCommandsController` gate 404 + map 403/404
- [ ] C.3 Provisioning/Licenses qua `FindInScope`
- [ ] C.4 3 controller mới (+ `ListDirectoryAsync`, `canRemove`, role `version`, `expectedRoleVersion`, nhãn mã quyền)
- [ ] C.5 `/me/permissions`
- [ ] C.5b gỡ role: `RemoveRoleAsync`, `UsersController.RemoveRole`, đổi stamp khi gán/gỡ, `ValidationInterval` 1 phút
- [ ] C.6 gỡ đường device-action (kể cả implementer `IPermissionStore` trong tests), snapshot qua `LatestFeatureGrants/LatestResourceGrants`, `PermissionTests`
- [ ] C.7 grep sạch chỉ trên `src` (không grep tests); endpoint đã gỡ chứng minh bằng test 404/405
- [ ] C.8 `RequestLimitTests`
- [ ] C.9 docs (trừ §5 UI; một dòng trỏ ở đầu §5)
- [ ] Regression Gate

## Success Criteria

- [ ] Mọi route thiết bị trong Theory 404 parity trả status + body giống hệt cho id không tồn tại và id ngoài phạm vi; không controller nào gọi `ScopeAuthorization.CanAccessDevice`.
- [ ] Role `{adc.view}` gọi `sms/get_status` qua HTTP → 403 `DEVICE_PERMISSION_REQUIRED`; `adc/get_status` → 200.
- [ ] Gỡ rồi gán lại membership → realtime-authorization lại 200.
- [ ] `devices.manage` một mình → 403 trên memberships; non-master không tự cấp, không cấp vượt quyền, không cấp role rủi ro cao; master-admin cấp được.
- [ ] List đúng hàng §6: member thấy đúng thiết bị có membership với `device.view` (kể cả ngoài effective orgs), không thấy thiết bị khác; admin `devices.manage`/`permissions.manage` thấy mọi thiết bị trong effective orgs; master-admin thấy tất cả; membership không mở route feature.
- [ ] master-admin không bị feature deny, deny `*` hay resource deny chặn ở list, gate, route feature, my-access (`Master_admin_is_not_blocked_by_feature_or_scope_denies`); thiết bị Revoked vẫn 403 `DEVICE_REVOKED`.
- [ ] my-access thiết bị Revoked trong phạm vi → 200, `permissions: []`.
- [ ] Vai trò: tên trùng khác hoa/thường → 409 `DEVICE_ROLE_NAME_TAKEN`; DELETE thiếu/sai `expectedVersion` → 400/409 `DEVICE_ROLE_VERSION_STALE`; tên/mô tả sai → 400 `DEVICE_ROLE_INVALID`.
- [ ] Mọi id trong DTO mới là Guid trần (test `GetGuid()` trên `userId`, `roleId`, `organizationId`); device-permission-codes có 50 mục, mỗi mục có `label` và `commands`, không có `source` [RT12].
- [ ] Gỡ role: master-admin gỡ được `master-admin` của người khác (204, stamp đổi), không gỡ được master-admin cuối cùng, non-master không gỡ được role nào (kể cả role không dành riêng); cookie của người bị gỡ bị từ chối ở lần xét stamp kế tiếp (≤ 1 phút; test đặt 0) [RT1].
- [ ] Ingest telemetry: chỉ master-admin; member có `device.record_measurements` → 403 [RT5].
- [ ] Memberships: PUT với `expectedRoleVersion` cũ → 409 `DEVICE_ROLE_VERSION_STALE` [RT3]; member org khác hiện ở GET với `canEdit=false`, `canRemove=true` và DELETE được [RT14]; GET dùng số query cố định (`ListDirectoryAsync`) [RT8].
- [ ] `/me/permissions` có `userId`, `isMasterAdmin` (master-admin có deny persisted vẫn `true`); `permissions` không `*`, không entry `device:`; không key nằm ở cả hai danh sách.
- [ ] Snapshot `PermissionGrantsController` mỗi key một dòng mới nhất qua `PermissionGrantResolver.LatestFeatureGrants/LatestResourceGrants`, không còn latest-wins inline; luật grant và mapping outcome của phase 3 không đổi, nhóm test grant phase 3 (kể cả `WILDCARD_GRANT_FORBIDDEN` 400) xanh không sửa.
- [ ] Không còn `DevicePermissionsController`, `DeviceActionGrant`, `GrantDeviceActionHandler`, endpoint `device-actions`: grep C.7 trên `src` = 0, hai test endpoint đã gỡ trả 404/405; không còn email cá nhân trong `src` và các mục docs phase này viết lại.
- [ ] Hai test RTU master-admin xanh không sửa; Regression Gate xanh.
- [ ] Docs parity không còn claim index không tồn tại; §5 (UI) giữ nguyên nội dung, chỉ có thêm một dòng trỏ phase frontend ở đầu mục.

## Risk Assessment

- Frontend hiện tại gọi endpoint đã xoá và dựa vào `*` của master-admin → UI hỏng runtime giữa phase 4 và 6 (unit test frontend mock nên vẫn xanh) → phát hành 4–6 cùng một release; ghi rõ trong PR.
- Fixture dùng chung (revoke `ChildDeviceId`, deny Child cho `ResourceScopedUserId`) → test mới/sửa tự seed user/device riêng, không dựa thứ tự chạy.
- Membership mở phạm vi thiết bị → route feature bị mở rộng nếu chỉ dựa `FindInScope` → phần 403 giữ org ∈ effective orgs; `Membership_scope_does_not_grant_feature_routes` khoá.
- Preview `canEdit`/`assignable` lệch handler → một predicate dùng chung (B.4); test memberships kiểm cả preview lẫn PUT thật.
- Code phase 3 lệch chữ ký ghi trong phase-03 → đọc code thật trước, chỉnh call site; không đổi file phase 3 ngoài B.4. Nếu `PermissionGrantResolver` chưa có `LatestFeatureGrants/LatestResourceGrants` thì bổ sung đúng ở đó theo §4 (set resolution dùng lại, kèm unit test), không viết latest-wins inline trong controller.
- master-admin bị chặn nhầm nếu controller đọc `actor.FeaturePermissions` (chỉ chứa allow tường minh) hoặc tự trừ deny → luật route feature xét `actor.IsMasterAdmin` trước; `Master_admin_is_not_blocked_by_feature_or_scope_denies` và `DeviceGateTests` khoá.
- Lặp test/luật grant của phase 3 → hai nguồn kỳ vọng lệch nhau → phase này không thêm test grant, chỉ giữ chúng làm khoá hồi quy (A.4).
- Grep cleanup trên `tests` trúng chuỗi hợp lệ (URL trong test endpoint đã gỡ, test migration) → C.7 chỉ grep `src` (`--type cs`, bỏ `Migrations`); phía tests chứng minh bằng assert 404/405.
- Route một thiết bị cần `DeviceAccess` (my-access, update, memberships GET) gọi resolver hai lần (gate + controller) → thêm ≤ 2 lần đọc, chấp nhận; list dùng `ListByUser` một lần + role distinct.
- DTO lệch phase 6 → shape đã chốt ở §6; test integration khoá field name và kiểu Guid.
- EF InMemory không bắt được lỗi query lồng (MARS) của Npgsql [RT8] → memberships GET chỉ dùng một projection `ToListAsync`; trước phát hành smoke tay `GET /api/v1/devices/{id}/memberships` trên Development + `ConnectionStrings:Postgres` (bước tuỳ chọn, như bước Postgres local của phase 2; workspace chưa có Postgres nên không làm cổng bắt buộc).
- Gỡ role trong sản phẩm cũng là công cụ của một master-admin xấu (gỡ các master-admin khác) [RT1] → owner chấp nhận (Q11); chặn gỡ master-admin cuối cùng; hai lệnh gỡ đồng thời có thể cùng qua kiểm "không phải người cuối" → chấp nhận (hiếm), khôi phục bằng seed/DB.
- `ValidationInterval` 1 phút thêm một lần đọc user + stamp mỗi phút mỗi phiên → không đáng kể ở quy mô hiện tại [RT1].

## Security Considerations

- Đóng oracle tồn tại thiết bị: gate 404 chạy trước handler lệnh, trước kiểm feature ở provisioning/licenses/revoke/create.
- Danh sách thành viên (email) chỉ trả cho `CanManageAccess`; gồm cả user org khác đang có membership trên D (minh bạch ai đang truy cập thiết bị) — các dòng đó chỉ gỡ được, không đổi role được (Q13) [RT8, RT14].
- Ingest telemetry chỉ master-admin: người không giả được trạng thái thiết bị (ngõ ra, sức khoẻ) qua quyền giao được [RT5].
- Xoá hai writer cho phép tự cấp `*` (`DevicePermissionsController.Update`, `POST device-actions`); `devices.manage` không còn cấp quyền; luật grant feature/resource (từ chối `*`, self-grant, vượt quyền) cùng mapping outcome do phase 3 giữ; luật uỷ quyền membership/role do handler phase 3 giữ, controller chỉ map mã.
- master-admin vô điều kiện (§3): deny không hạn chế master-admin; muốn hạn chế thì master-admin khác gỡ role qua `DELETE /api/v1/users/{userId}/roles/master-admin` (Q11): đổi security stamp, cookie bị từ chối trong ≤ 1 phút, realtime rớt trong một tick [RT1]. Revoked vẫn chặn master-admin (403 `DEVICE_REVOKED`). Non-master vẫn bị feature deny và deny `*` chặn.
- Membership chỉ mở phạm vi một thiết bị, không mở feature (`devices.manage`/`provisioning.manage`/`licenses.manage` vẫn cần org scope).
- Mutation mới có CSRF + giới hạn 64 KiB; CAS bắt buộc (`expectedRoleId`, `expectedVersion`).
- Body 403 chỉ lộ mã quyền thiếu cho người đã trong phạm vi.
- Còn tồn tại: ingest telemetry không CSRF (§10; nay chỉ master-admin gọi được). Vai trò trên cookie HTTP được xét lại mỗi phút (`ValidationInterval`), và gán/gỡ role đổi stamp nên cookie cũ bị từ chối ở lần xét đó [RT1].
- Xoá email/tên cá nhân hard-code khỏi source và các mục docs phase này viết lại (§5.2 do phase frontend).

## Open Questions

Không còn câu hỏi mở. Đã chốt ở Validation Session 1 (2026-10-05), đúng như phase đang viết: <!-- Updated: Validation Session 1 - V1-V6 and Q11 scope confirmed -->
1. V2 — lệnh thiếu quyền hoặc thiết bị thu hồi trả **403** kèm `{code}` (map trong `DeviceCommandsController`, test A.3 giữ).
2. V4 đối xứng (`canEdit`, `canRemove`, remove/replace, luật sửa role của phase 3); V5 chặn xoá role đang dùng (`Device_role_delete_in_use_returns_conflict`); V6 v1 strict (admin non-master trong test memberships có membership riêng). V1 không đổi cờ `highRisk`; V3 không ảnh hưởng phase này.
3. Q11 — endpoint gỡ role giữ **chỉ master-admin, với mọi role** (`Non_master_cannot_remove_any_role`).

## Next Steps

- Phase 5 (chạy ngay sau, §12): hub/delivery dùng `IDeviceAccessResolver` + `DeviceAccessPolicy` với `device.view`, thay các caller `CanAccessDevice` còn lại trong `Hubs/`; không sửa `DevicesController.cs`.
- Phase 5: master-admin bị gỡ role có stamp mới → refresher realtime trả null ở tick kế tiếp (đã có test refresher cho stamp đổi).
- Phase 6 tiêu thụ: `GET my-access` (Revoked trong phạm vi → 200, `permissions: []`); memberships GET `{deviceId, members[{…, canEdit, canRemove}], roles[{…, version}]}`, PUT `{roleId, expectedRoleId, expectedRoleVersion}`, DELETE `?expectedRoleId=`; device-roles `{roleId, organizationId, name, description, permissions, version}`, PUT body có `expectedVersion`, DELETE `?expectedVersion=` bắt buộc; `device-permission-codes` `{code, group, highRisk, label, commands: string[]}` (`type.action`, `type.action:domain`); `DELETE /api/v1/users/{userId}/roles/{role}` (204 / 400 `{errors}`) [RT1, RT3, RT12, RT14]; `/me/permissions` `userId`/`isMasterAdmin` (`true` = toàn quyền, frontend bypass trước deny — §3/§9), `permissions` không `*`; lệnh 404/403 body có `code`; endpoint `/permissions` và `device-actions` đã xoá; bảng mã → HTTP ở `DeviceGate.StatusFor`; mọi id là chuỗi Guid; cập nhật §5 (UI) của docs parity (thay dòng trỏ phase này để lại ở đầu §5, thay tên/email thật ở §5.2).
- Plan `261001-0030-full-hardware-mqtt-apis-c2` dựa vào gate lệnh này khi bật transport.
