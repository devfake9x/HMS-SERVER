---
phase: 2
title: "Vai trò, membership và persistence"
status: completed
priority: P1
dependencies: []
---

# Phase 2: Vai trò, membership và persistence

## Context Links

- Hợp đồng (BINDING): [design-contract.md](./reports/design-contract.md) §3 (chỉ `DeviceRoleId`, `DeviceRole` kể cả `NormalizedName`, `DeviceMembership`), §4 (store contracts kèm CAS, `DeviceAccessAuditEntry`, InMemory; KHÔNG handler/resolver), §8 (persistence, gồm cột người cấp trên `permission_grants` và câu căn bộ đếm `policy_versions`), §0 Q12, §11 V3, V5.
- Scout: [scout-persistence-surface-map.md](./reports/scout-persistence-surface-map.md), [scout-authz-core-surface-map.md](./reports/scout-authz-core-surface-map.md).
- Brainstorm: `plans/reports/261004-2234-brainstorm-device-permissions-what-where.md` §5.3 (vai trò theo org), §5.4 (membership upsert + audit).

## Overview

Thêm mô hình domain vai trò/membership, hợp đồng store tầng Application (InMemory thread-safe), bản ghi EF + cấu hình + store EF, migration `AddDeviceRolesAndMemberships` và DI. Store là nguồn duy nhất quyết định tên role trùng trong org, không phân biệt hoa/thường (so `NormalizedName`; Postgres thêm unique index chặn đua) — handler không tự kiểm trùng, chỉ map `DEVICE_ROLE_NAME_TAKEN` store báo; store cũng là chốt cuối cho CAS `Version` khi sửa/xoá role và CAS role hiện tại khi ghi membership. Thêm hai field người cấp (`GrantedBy`, `GrantedByMasterAdmin`) vào grant feature/resource cùng migration `AddPermissionGrantGrantor`, để phase 3 chặn non-master đảo deny của master-admin (Q12) [RT13]. Migration `AddDeviceRolesAndMemberships` căn bộ đếm `policy_versions` trước khi xoá dòng `device-action` [RT9]. Phase chỉ thêm mới: chưa xoá `DeviceActionGrant`, chưa đụng controller/handler; solution compile và test xanh suốt phase.

## Key Insights

- Bảng snake_case qua `ToTable`, cột PascalCase mặc định, không value converter; typed id map tay trong store: `src/Hms.Server.Infrastructure/Persistence/ApplicationDbContext.cs:49,61`, `src/Hms.Server.Infrastructure/Persistence/EfManagementStores.cs:12-13,56,89`.
- FK chỉ có ở cây org, đều `Restrict`: `ApplicationDbContext.cs:53-56,66-69,99-102,123-126`. Mẫu unique `(OrganizationId, Name)` + FK org: projects/areas `ApplicationDbContext.cs:93-103,117-127` (ở đây khoá theo `NormalizedName`). PK ghép: `device_keysets` `ApplicationDbContext.cs:82`.
- Precedent tên chuẩn hoá + concurrency token đến từ Identity (`ApplicationDbContext.cs:18` kế thừa `IdentityDbContext`): `AspNetRoles.NormalizedName` + unique `RoleNameIndex`, `ConcurrencyStamp` `.IsConcurrencyToken()` kiểu `text` không max length (`Migrations/ApplicationDbContextModelSnapshot.cs:31-47`). Token chỉ là metadata model: migration tạo `ConcurrencyStamp` như cột `text` thường, không DDL riêng (`Migrations/20260928040747_InitialManagement.cs:22,43`), chỉ snapshot/Designer ghi `.IsConcurrencyToken()` — đúng kiểu §8 ghi cho `device_memberships.RoleId` (token chỉ ở model, không DDL). Code dự án chưa tự cấu hình concurrency token nào.
- Mẫu audit: `LicenseTrustAuditRecord` `PersistenceEntities.cs:104-114`, cấu hình `ApplicationDbContext.cs:163-174` (AuditId PK, Actor 256, Change 32, index entity+OccurredAt). Upsert find-or-create + audit cùng `SaveChanges`: `EfLicenseStores.cs:28-61`. Bắt `DbUpdateException` rồi reload bên thắng: `EfCommandAuditStore.cs:79-92`, `EfLicenseStores.cs:110-122`.
- InMemory thread-safe mẫu `_gate` + `lock`: `src/Hms.Server.Application/Licensing/LicenseContracts.cs:180-222`. Ngược lại `InMemoryOrganizationStore` không lock và trả chính instance đang lưu (`src/Hms.Server.Application/Management/ManagementContracts.cs:78-86`) — không dùng được cho `DeviceRole` (mutable, có `Version`).
- DI rẽ nhánh theo connection string: scoped `Ef*` / singleton `InMemory*` (`src/Hms.Server.Infrastructure/DependencyInjection.cs:46-61,74-77`). Dev + mọi API test chạy InMemory singleton.
- EF InMemory không ép unique/FK nhưng có kiểm concurrency token khi UPDATE/DELETE (`IsConcurrencyConflict` trong `Microsoft.EntityFrameworkCore.InMemory.dll` 10.0.10); không có test Postgres; app không gọi `Migrate()`; dev dùng `EnsureCreatedAsync` (`src/Hms.Server.Api/Program.cs:59`) → unique/FK chỉ chứng minh qua model-metadata test + kiểm tra tường minh trong store; nhánh thua đua của concurrency token test được trên EF InMemory.
- Design-time factory cứng Npgsql (`ApplicationDbContextFactory.cs:8-14`); snapshot `ProductVersion 10.0.10` (`Migrations/ApplicationDbContextModelSnapshot.cs:20`) đang khớp Designer mới nhất (`20260929090323_AddLicenseTrustAudits`); `dotnet ef --version` = 10.0.10 (global tool). Precedent thao tác dữ liệu viết tay trong migration sinh tự động: `20260929032719_AddPolicyVersionCounter.cs:27-30`.
- Dòng legacy `permission_grants.Kind = "device-action"`: ghi `EfPermissionStore.cs:90`, đọc `EfPermissionStore.cs:112`.
- Bộ đếm `policy_versions` được seed `(1, 0)` viết tay (`20260929032719_AddPolicyVersionCounter.cs:27-30`); `AdvancePostgres` chỉ cộng bộ đếm, không so `MAX(permission_grants.PolicyVersion)` (`EfPermissionStore.cs:117-139`), chỉ getter (`:13-24`) và nhánh provider khác (`:36-53`) gộp MAX. DB có grant trước migration đó sẽ cấp version trùng hoặc thấp hơn dòng cũ → câu `UPDATE … GREATEST(…)` trong migration mới (idempotent, vô hại với DB tạo đủ chuỗi migration) [RT9].
- `FeaturePermissionGrant(UserId, Permission, Deny, PolicyVersion)` và `ResourceScopeGrant(UserId, OrganizationId, Deny, PolicyVersion)` là record positional (`PermissionContracts.cs:10-11`); `PermissionGrantRecord` chưa có cột người cấp (`PersistenceEntities.cs:81-92`); `EfPermissionStore.GrantFeature/GrantResource` (`:56-82`) và `FeatureGrants/ResourceGrants` (`:99-109`) map tay → thêm field cuối, có mặc định, mọi call site positional vẫn compile [RT13].
- `UserId` nằm ở `src/Hms.Server.Domain/Authorization/AuthorizationPolicy.cs:6` (namespace `Hms.Server.Domain.Authorization`); phase 3 xoá `AuthorizationContext`/`DeviceAuthorizationPolicy` trong file này nên phải giữ `UserId` cùng namespace.
- Grep `DeviceRole|DeviceMembership|DeviceAccess` trong `src/` + `tests/`: 0 kết quả → không va tên, phase thuần additive.
- Mẫu test: `Model_has_management_tables_only` `tests/Hms.Server.IntegrationTests/PersistenceModelTests.cs:41-60`; index metadata `:84-99`; DI `:162-183`; unit test tạo InMemory store trực tiếp (`tests/Hms.Server.UnitTests/ManagementTests.cs`).

## Requirements

**Functional**
- `DeviceRoleId(Guid)`; `DeviceRole` (Id, OrganizationId, Name ≤128 trim non-blank, `NormalizedName = Name.Trim().ToUpperInvariant()`, Description ≤512 trim, blank→null, Permissions `IReadOnlySet<string>` bất biến, Version bắt đầu 1); `Create`, `Rehydrate`, `Update(name, description, permissions)` tăng Version 1.
- `DeviceMembership(UserId, DeviceId, DeviceRoleId RoleId, UserId GrantedBy, DateTimeOffset GrantedAt)`.
- `IDeviceRoleStore`, `IDeviceMembershipStore` đúng chữ ký CAS §4 (`Remove(id, expectedVersion, audit)`; `Upsert/Remove(..., expectedRoleId, audit)`), `DeviceAccessAuditEntry` (`PreviousValue/NewValue` = snapshot JSON gọn, không giới hạn độ dài), hằng `Change` (6 giá trị §4) đúng tên contract.
- InMemory: một lớp `InMemoryDeviceAccessStore` implement cả hai interface, một `_gate`, audit trong bộ nhớ.
- EF: 4 record, 4 entity config theo §8 (concurrency token `device_roles.Version`, `device_memberships.RoleId` chỉ ở model, không DDL), `EfDeviceRoleStore`, `EfDeviceMembershipStore`; mỗi ghi + audit = một `SaveChanges`.
- Migration `AddDeviceRolesAndMemberships` + hai câu viết tay theo thứ tự: `UPDATE policy_versions … GREATEST(…)` rồi `DELETE` dòng `device-action` [RT9]; DI cả hai nhánh.
- Người cấp grant [RT13]: `FeaturePermissionGrant`/`ResourceScopeGrant` thêm field cuối `UserId? GrantedBy = null, bool GrantedByMasterAdmin = false`; `PermissionGrantRecord` thêm `Guid? GrantedBy`, `bool GrantedByMasterAdmin`; `EfPermissionStore` ghi và đọc hai field (dòng cũ → null/false); `InMemoryPermissionStore` lưu nguyên record nên không đổi; migration `AddPermissionGrantGrantor` (sinh tự động, cột bool mặc định false).

**Non-functional**
- Additive; không đổi public contract hiện có; không value converter; đọc `AsNoTracking`; store không tự kiểm quyền (handler phase 3 lo).
- InMemory ≡ EF về bất biến: tên duy nhất/org theo `NormalizedName`, một membership/(user, device), CAS `Version` (Update/Remove role), thứ tự lỗi Remove role, CAS role hiện tại (Upsert/Remove membership); nhánh lỗi không ghi gì, kể cả audit.
- Tên role trùng: store là nguồn duy nhất (kiểm `NormalizedName` + unique index Postgres → `DEVICE_ROLE_NAME_TAKEN`); domain không mở helper chuẩn hoá public, nên tầng trên không có đường kiểm trùng thứ hai.
- `ListByRole` một query (index RoleId) để phase 3 fan-out epoch.

## Architecture

**Domain** (`src/Hms.Server.Domain/Authorization/`)
```csharp
public readonly record struct DeviceRoleId(Guid Value) { public static DeviceRoleId New() => new(Guid.NewGuid()); }
public sealed class DeviceRole {
  public const int NameMaxLength = 128, DescriptionMaxLength = 512, PermissionMaxLength = 160;
  public DeviceRoleId Id { get; } public OrganizationId OrganizationId { get; }
  public string Name { get; private set; } public string? Description { get; private set; }
  public string NormalizedName => Name.Trim().ToUpperInvariant(); // khoá unique theo org; chỉ store dùng để kiểm trùng
  public IReadOnlySet<string> Permissions { get; private set; } // FrozenSet<string>, StringComparer.Ordinal
  public int Version { get; private set; }
  public static DeviceRole Create(OrganizationId organizationId, string name, string? description, IEnumerable<string> permissions);
  public static DeviceRole Rehydrate(Guid id, Guid organizationId, string name, string? description, IEnumerable<string> permissions, int version);
  public void Update(string name, string? description, IEnumerable<string> permissions); // validate trước, rồi mới gán + Version++
}
public sealed record DeviceMembership(UserId UserId, DeviceId DeviceId, DeviceRoleId RoleId, UserId GrantedBy, DateTimeOffset GrantedAt);
```
Domain chỉ kiểm hình thức (permission non-blank, ≤160, không trim); kiểm từ vựng (`DEVICE_PERMISSION_UNKNOWN`) và high-risk là việc handler phase 3. Lỗi hình thức → `ArgumentException` như `Organization.Create`; handler phase 3 đổi tên/mô tả sai thành `DEVICE_ROLE_INVALID` (400), không để exception lọt ra (§3). `ToUpperInvariant` của .NET đổi hoa từng ký tự, không đổi độ dài → `NormalizedName` ≤128 như `Name`. Không có helper chuẩn hoá public: chỉ store so `NormalizedName` để kiểm trùng (pre-check + unique index), handler không tự kiểm — một nguồn quyết định, không có quy tắc thứ hai để lệch.

**Application** (`src/Hms.Server.Application/Authorization/DeviceAccessContracts.cs`)
```csharp
public interface IDeviceRoleStore {
  DeviceRole? Find(DeviceRoleId id);
  IReadOnlyList<DeviceRole> ListByOrganization(OrganizationId organizationId); // order by Name
  void Add(DeviceRole role, DeviceAccessAuditEntry audit); // NormalizedName trùng trong org → DEVICE_ROLE_NAME_TAKEN
  void Update(DeviceRole role, DeviceAccessAuditEntry audit); // CAS: Version lưu == role.Version - 1; tên trùng như Add
  void Remove(DeviceRoleId id, int expectedVersion, DeviceAccessAuditEntry audit); // NOT_FOUND → VERSION_STALE → IN_USE
}
public interface IDeviceMembershipStore {
  DeviceMembership? Find(UserId userId, DeviceId deviceId);
  IReadOnlyList<DeviceMembership> ListByDevice(DeviceId deviceId);
  IReadOnlyList<DeviceMembership> ListByRole(DeviceRoleId roleId);
  IReadOnlyList<DeviceMembership> ListByUser(UserId userId);
  // CAS trên role hiện tại: expectedRoleId null = chưa có membership; lệch → DEVICE_MEMBERSHIP_CHANGED
  void Upsert(DeviceMembership membership, DeviceRoleId? expectedRoleId, DeviceAccessAuditEntry audit);
  bool Remove(UserId userId, DeviceId deviceId, DeviceRoleId? expectedRoleId, DeviceAccessAuditEntry audit); // vắng + null → false, không audit
}
public sealed record DeviceAccessAuditEntry(Guid AuditId, UserId Actor, string Change, OrganizationId? OrganizationId,
  DeviceId? DeviceId, UserId? TargetUserId, DeviceRoleId? RoleId, string? PreviousValue, string? NewValue, DateTimeOffset OccurredAt)
{ public void EnsureValid(); } // chỉ kiểm Change ∈ DeviceAccessChanges; Previous/NewValue = snapshot JSON gọn, không giới hạn độ dài
public static class DeviceAccessChanges { RoleCreated="ROLE_CREATED", RoleUpdated="ROLE_UPDATED", RoleDeleted="ROLE_DELETED",
  MembershipAssigned="MEMBERSHIP_ASSIGNED", MembershipChanged="MEMBERSHIP_CHANGED", MembershipRemoved="MEMBERSHIP_REMOVED"; IsKnown(string) }
public sealed class InMemoryDeviceAccessStore : IDeviceRoleStore, IDeviceMembershipStore { /* _gate, roles, (UserId, DeviceId)→membership, audit list; Audit() snapshot */ }
```
InMemory lưu/trả **bản sao** `DeviceRole` (Rehydrate) để handler sửa object tìm được không rò vào store trước `Update`. `Remove` role báo lỗi giống `Update` (ném code, trả `void`); chỉ `Remove` membership trả `bool` cho nhánh no-op.

**Bất biến store (InMemory và EF giống nhau; lỗi = `InvalidOperationException(code)` theo mẫu `EfManagementStores.cs:69`; nhánh lỗi không ghi gì, kể cả audit)**

| Tình huống | EF (dev InMemory provider + Postgres) | InMemory | Code |
|---|---|---|---|
| Add/Update: `NormalizedName` trùng role khác cùng org (`Operator` vs `operator`) — store là nguồn duy nhất, handler không pre-check | pre-check `Any` theo (OrganizationId, NormalizedName), trừ chính nó; Postgres: unique index → `DbUpdateException` → reload, còn trùng thì map | check dưới lock | `DEVICE_ROLE_NAME_TAKEN` |
| Update/Remove role không tồn tại | find tracked trả null | check dưới lock | `DEVICE_ROLE_NOT_FOUND` |
| Version lưu ≠ `role.Version - 1` (Update) hoặc ≠ `expectedVersion` (Remove, kiểm trước in-use) | pre-check + `Version.IsConcurrencyToken()` → `DbUpdateConcurrencyException` → reload: mất dòng thì `DEVICE_ROLE_NOT_FOUND` | check dưới lock | `DEVICE_ROLE_VERSION_STALE` |
| Remove role còn membership | pre-check `Any(RoleId)`; Postgres FK Restrict → `DbUpdateException` → reload, còn membership thì map | check dưới lock | `DEVICE_ROLE_IN_USE` |
| Upsert với role không tồn tại (kiểm trước CAS) | pre-check `Any(Id)`; FK backstop | check dưới lock | `DEVICE_ROLE_NOT_FOUND` |
| Upsert/Remove membership: role hiện tại ≠ `expectedRoleId` (`null` = chưa có membership) | pre-check trên dòng tracked; `RoleId` là concurrency token chỉ ở model (không DDL, §8) → EF đưa `RoleId` gốc vào `WHERE` của UPDATE/DELETE, 0 dòng → `DbUpdateConcurrencyException`; insert đua → unique (UserId, DeviceId) → `DbUpdateException`; cả hai → reload rồi map | check + ghi trong cùng một lock | `DEVICE_MEMBERSHIP_CHANGED` |
| Remove membership vắng, `expectedRoleId` null | trả `false`, không ghi, không audit | như EF | — |

Membership ghi theo find-then-write trên dòng tracked theo (UserId, DeviceId): có dòng → sửa `RoleId/GrantedBy/GrantedAt`, giữ `Id`; không có → insert `Id = Guid.NewGuid()`; Remove xoá dòng khớp, trả `true`. `RoleId` là concurrency token chỉ ở model, không DDL (đúng §8): migration chỉ tạo cột `RoleId` thường (FK + index), snapshot ghi `.IsConcurrencyToken()`, EF tự thêm giá trị gốc vào `WHERE` → CAS nguyên tử trên Postgres thay vì last-writer-wins; `device_roles.Version` cũng là token không sinh DDL. Upsert ép `IsModified` trên `RoleId` để UPDATE (kèm điều kiện token) vẫn được gửi khi giá trị không đổi. Mọi lần ghi đi qua `DeviceAccessWrites.Save` (thay đổi + dòng audit + `SaveChanges` trong một khối `try`; precedent `EfCommandAuditStore.cs:83`):
- lỗi bất kỳ → `db.ChangeTracker.Clear()`;
- `DbUpdateException` (gồm cả lỗi concurrency) → chạy lại đúng các check của lệnh đó theo cùng thứ tự trên dữ liệu đọc mới (`AsNoTracking`) rồi báo mã đầu tiên khớp. Membership gặp lỗi token → `DEVICE_MEMBERSHIP_CHANGED` sau check role;
- không check nào giải thích được → ném lại lỗi gốc (lỗi FK khác không bị che).

`DeviceId` FK chỉ Postgres ép; handler phase 3 kiểm thiết bị tồn tại.

**EF mapping** (`PersistenceEntities.cs`, `ApplicationDbContext.cs`, theo §8 nguyên văn)
- `DeviceRoleRecord` {Id, OrganizationId, Name, NormalizedName, Description?, Version, CreatedBy (Guid), CreatedAt, UpdatedAt, `List<DeviceRolePermissionRecord> Permissions`}; `device_roles`: Name 128 required, NormalizedName 128 required, Description 512, unique (OrganizationId, NormalizedName) (không index unique trên `Name`), `Version` `IsConcurrencyToken()` (chỉ ở model), `HasOne<OrganizationRecord>().WithMany()` Restrict, `HasMany(Permissions).WithOne().HasForeignKey(RoleId)` Cascade.
- `DeviceRolePermissionRecord` {RoleId, Permission}; `device_role_permissions`: PK (RoleId, Permission), Permission 160.
- `DeviceMembershipRecord` {Id, UserId, DeviceId, RoleId, GrantedBy, GrantedAt}; `device_memberships`: PK Id, unique (UserId, DeviceId), idx DeviceId, idx RoleId, `RoleId` `IsConcurrencyToken()` — chỉ ở model, không DDL (§8; không `IsRowVersion()`, không trigger/`xmin`), FK DeviceId→`DeviceRecord` Restrict, FK RoleId→`DeviceRoleRecord` Restrict; không FK UserId (§8).
- `DeviceAccessAuditRecord` {AuditId, Actor (string = `Actor.Value.ToString("D")`), Change, OrganizationId?, DeviceId?, TargetUserId?, RoleId?, PreviousValue?, NewValue?, OccurredAt}; `device_access_audits`: Actor 256 required, Change 32 required, PreviousValue/NewValue không `HasMaxLength` (Npgsql → `text`), idx (DeviceId, OccurredAt), idx (RoleId, OccurredAt); không FK (audit phải sống sau khi xoá role).
- `CreatedBy = audit.Actor`, `CreatedAt = UpdatedAt = audit.OccurredAt` khi Add; Update ghi `Name` + `NormalizedName` cùng lúc, chỉ đặt `UpdatedAt` (giữ `CreatedBy/CreatedAt`); **không bao giờ ghi `OrganizationId` khi Update** (org bất biến, như `EfManagementStores.cs:27-33,66-78`).
- Update permission bằng diff (xoá thiếu, thêm mới) trên record tracked `Include(Permissions)` — tránh xoá+thêm cùng key trong một `SaveChanges`. Remove role xoá tường minh dòng permission (EF InMemory không cascade dòng chưa tracked) + role + audit trong một `SaveChanges`.
- `PermissionGrantRecord` [RT13]: `GrantedBy` (`uuid` null), `GrantedByMasterAdmin` (`boolean` not null, mặc định false); không index mới (phase 3 lọc trong bộ nhớ trên grant của một user).

## Related Code Files

**Create**

| Path | Thay đổi |
|---|---|
| `src/Hms.Server.Domain/Authorization/DeviceRole.cs` | `DeviceRoleId`, `DeviceRole` |
| `src/Hms.Server.Domain/Authorization/DeviceMembership.cs` | record `DeviceMembership` |
| `src/Hms.Server.Application/Authorization/DeviceAccessContracts.cs` | 2 interface, audit entry, `DeviceAccessChanges`, `InMemoryDeviceAccessStore` |
| `src/Hms.Server.Infrastructure/Persistence/EfDeviceAccessStores.cs` | `EfDeviceRoleStore`, `EfDeviceMembershipStore` |
| `src/Hms.Server.Infrastructure/Persistence/Migrations/<ts>_AddDeviceRolesAndMemberships.cs` + `.Designer.cs` | sinh bằng `dotnet ef` + `UPDATE policy_versions` và `DELETE` viết tay [RT9] |
| `src/Hms.Server.Infrastructure/Persistence/Migrations/<ts>_AddPermissionGrantGrantor.cs` + `.Designer.cs` | sinh bằng `dotnet ef`, chỉ 2 `AddColumn` trên `permission_grants` [RT13] |
| `tests/Hms.Server.UnitTests/DeviceRoleTests.cs` | test domain |
| `tests/Hms.Server.UnitTests/InMemoryDeviceAccessStoreTests.cs` | test InMemory store |

**Modify**

| Path | Thay đổi |
|---|---|
| `src/Hms.Server.Infrastructure/Persistence/PersistenceEntities.cs` | +4 record; `PermissionGrantRecord` + `GrantedBy`, `GrantedByMasterAdmin` [RT13] |
| `src/Hms.Server.Application/Authorization/PermissionContracts.cs` | `FeaturePermissionGrant`/`ResourceScopeGrant` thêm field cuối `UserId? GrantedBy = null, bool GrantedByMasterAdmin = false` (chưa đổi handler — phase 3) [RT13] |
| `src/Hms.Server.Infrastructure/Persistence/EfPermissionStore.cs` | `GrantFeature/GrantResource` ghi, `FeatureGrants/ResourceGrants` đọc hai field mới [RT13] |
| `src/Hms.Server.Infrastructure/Persistence/ApplicationDbContext.cs` | +4 `DbSet` (`DeviceRoles`, `DeviceRolePermissions`, `DeviceMemberships`, `DeviceAccessAudits`) + 4 entity block |
| `src/Hms.Server.Infrastructure/Persistence/Migrations/ApplicationDbContextModelSnapshot.cs` | tool tái sinh, không sửa tay |
| `src/Hms.Server.Infrastructure/DependencyInjection.cs` | nhánh Ef scoped / InMemory singleton ngay sau khối `IPermissionStore` (:74-77) |
| `tests/Hms.Server.IntegrationTests/PersistenceModelTests.cs` | **test hiện có đổi**: `Model_has_management_tables_only` thêm 4 bảng; + test mới |

**Delete**: không có (xoá `DeviceActionGrant`, `GrantDeviceActionHandler` thuộc phase sau).

## Implementation Steps

### A. Tests Before (red)

Viết trước; đỏ vì type chưa có (compile fail ở project test) — chấp nhận trong nhánh làm việc, xanh lại ở bước C.
1. Khoá hành vi hiện tại (phải xanh trước và sau phase): toàn bộ `PersistenceModelTests` hiện có, `ManagementTests`, `PermissionTests` (kể cả `Three_permission_grants_advance_one_shared_policy_version` còn dùng `GrantDeviceActionHandler`), `ManagementApiTests` (ma trận `/permissions` vẫn chạy — phase này không đụng controller).
2. `PersistenceModelTests.Model_snapshot_has_no_pending_changes` (mới, xanh ngay trên code hiện tại = khoá): context `UseNpgsql("Host=localhost;Database=hms_server_design;Username=hms")`, `Assert.False(db.Database.HasPendingModelChanges())` (không mở kết nối). Đỏ sau bước C3, xanh lại sau C4.
3. `tests/Hms.Server.UnitTests/DeviceRoleTests.cs`:
   - `Device_role_create_trims_name_and_starts_at_version_one`
   - `Device_role_normalized_name_is_trimmed_upper_invariant` (`" Operator "` → `NormalizedName == "OPERATOR"`; `Update` đổi tên → `NormalizedName` đổi theo)
   - `Device_role_rejects_blank_or_overlong_name` (Theory: `""`, `"  "`, 129 ký tự) → `ArgumentException`
   - `Device_role_normalizes_blank_description_and_rejects_overlong` (513 ký tự)
   - `Device_role_rejects_blank_or_overlong_permission_code` (`" "`, 161 ký tự)
   - `Device_role_update_increments_version_and_replaces_permissions`
   - `Device_role_invalid_update_leaves_state_and_version_unchanged`
   - `Device_role_permissions_are_an_immutable_snapshot` (sửa set nguồn sau `Create` không ảnh hưởng; `((ICollection<string>)role.Permissions).Add` ném `NotSupportedException`)
   - `Device_role_rehydrate_preserves_version`
4. `tests/Hms.Server.UnitTests/InMemoryDeviceAccessStoreTests.cs`:
   - `Membership_upsert_with_matching_expected_role_replaces_state` (null→A rồi A→B, `expectedRoleId` đúng → `Find` = B, `ListByDevice` 1 dòng, 2 audit ASSIGNED/CHANGED)
   - `Membership_upsert_rejects_expected_role_mismatch` (expected null khi đang A; expected B khi đang A; expected A khi chưa có → `DEVICE_MEMBERSHIP_CHANGED`, state + audit không đổi)
   - `Membership_remove_rejects_expected_role_mismatch` (expected B hoặc null khi đang A; expected A khi chưa có → `DEVICE_MEMBERSHIP_CHANGED`, không audit)
   - `Membership_remove_absent_with_null_expected_role_returns_false_without_audit`
   - `Membership_remove_then_regrant_restores_access` (remove expected A → `true` + audit REMOVED; upsert expected null → có lại; khoá lỗi 2 ở tầng store)
   - `Concurrent_upserts_with_same_expected_role_have_exactly_one_winner` (Theory: cặp đang A + expected A, hoặc cặp trống + expected null; `Parallel.For` 200 upsert sang B → đúng 1 thành công, 199 `DEVICE_MEMBERSHIP_CHANGED`, 1 dòng, đúng 1 audit mới cho cặp đó; 50 cặp khác chạy xen với expected null đều thành công)
   - `Membership_upsert_rejects_unknown_role` (kiểm trước CAS)
   - `List_by_role_returns_only_memberships_of_that_role`
   - `Role_store_rejects_case_insensitive_duplicate_name_in_same_organization` (`Operator` rồi `operator` cùng org → `DEVICE_ROLE_NAME_TAKEN`, qua Add lẫn Update đổi tên, state + audit không đổi; cùng tên ở org khác OK; đổi hoa/thường tên của chính nó OK)
   - `Role_update_requires_next_version` (stale → `DEVICE_ROLE_VERSION_STALE`, không audit)
   - `Role_store_returns_copies` (sửa object `Find` trả về không đổi state lưu)
   - `Role_remove_checks_missing_then_version_then_in_use` (id lạ → `DEVICE_ROLE_NOT_FOUND`; role đang dùng + version sai → `DEVICE_ROLE_VERSION_STALE`; role đang dùng + version đúng → `DEVICE_ROLE_IN_USE`; không audit, role còn nguyên)
   - `Role_remove_with_current_version_deletes_role_and_writes_audit` (`Find` null, 1 audit `ROLE_DELETED`)
   - `Audit_entry_validates_change_only` (Change lạ → `ArgumentException`; `PreviousValue/NewValue` 4096 ký tự vẫn hợp lệ)
5. `PersistenceModelTests` (mới):
   - mở rộng `Model_has_management_tables_only`: `device_roles`, `device_role_permissions`, `device_memberships`, `device_access_audits`
   - `Device_role_name_is_unique_per_organization` (index (OrganizationId, NormalizedName) `IsUnique`; không index unique nào trên `Name`)
   - `Device_membership_is_unique_per_user_and_device` (unique (UserId, DeviceId) + index DeviceId + index RoleId không unique)
   - `Device_access_foreign_keys_restrict_except_role_permissions_cascade` (role→org Restrict, membership→device Restrict, membership→role Restrict, permission→role Cascade, không FK trên `UserId` và trên audit)
   - `Device_access_columns_match_contract_lengths_and_keys` (Name/NormalizedName 128 required, Description 512, Permission 160, Actor 256, Change 32, PreviousValue/NewValue không max length; PK (RoleId, Permission); idx audit (DeviceId, OccurredAt), (RoleId, OccurredAt); `device_roles.Version` và `device_memberships.RoleId` là concurrency token chỉ ở model: `IsConcurrencyToken` true, `ValueGenerated.Never` — không phải rowversion do DB sinh, không DDL)
   - `Ef_device_role_store_round_trips_permissions_and_writes_audit` (đọc bằng context thứ hai cùng DB name → chứng minh persisted + `ChangeTracker` rỗng; `NormalizedName` lưu đúng; `CreatedBy/CreatedAt` lấy từ audit; 1 audit `ROLE_CREATED`)
   - `Ef_device_role_update_diffs_permissions_and_bumps_version` (+ stale → không thêm audit)
   - `Ef_device_role_store_rejects_case_insensitive_duplicate_name_in_same_organization` (`Operator` rồi `operator` → `DEVICE_ROLE_NAME_TAKEN`, không thêm role/audit; org khác OK)
   - `Ef_device_role_remove_checks_missing_then_version_then_in_use` (như InMemory; không audit)
   - `Ef_device_role_remove_deletes_permissions_and_audit_in_one_save` (context thứ hai: không còn role và dòng `device_role_permissions`, 1 audit `ROLE_DELETED`)
   - `Ef_membership_upsert_replaces_role_and_writes_audit` (null→A→B, `expectedRoleId` đúng: 1 dòng, `Id` giữ nguyên, 2 audit)
   - `Ef_membership_upsert_and_remove_reject_expected_role_mismatch` (`DEVICE_MEMBERSHIP_CHANGED`, dòng + audit không đổi; remove vắng + expected null → `false`, không audit)
   - `Ef_lost_race_maps_concurrency_conflicts_to_store_codes` (context 1 đã track bản cũ, context 2 cùng DB ghi trước; qua context 1: membership `Upsert`/`Remove` với `expectedRoleId` cũ → `DEVICE_MEMBERSHIP_CHANGED`, role `Update`/`Remove` với version cũ → `DEVICE_ROLE_VERSION_STALE`; chứng minh hai concurrency token, và dòng audit nằm cùng `SaveChanges` với lần ghi bị chặn rồi tracker rỗng — EF InMemory không có transaction nên không thể hiện được việc rollback audit, xem Risk Assessment)
   - `Ef_membership_remove_writes_audit_and_regrant_restores`
   - `Ef_membership_lists_by_role_device_and_user`
   - `Ef_membership_upsert_rejects_unknown_role`
   - `Migration_purges_legacy_device_action_grants` (`new AddDeviceRolesAndMemberships().UpOperations` có `CreateTableOperation` 4 bảng, `SqlOperation` chứa `UPDATE policy_versions` với `GREATEST` và `SqlOperation` chứa `DELETE FROM permission_grants WHERE "Kind" = 'device-action'`, câu UPDATE đứng trước câu DELETE) [RT9]
   - `Migration_adds_permission_grant_grantor_columns` (`new AddPermissionGrantGrantor().UpOperations` = đúng 2 `AddColumnOperation` trên `permission_grants`: `GrantedBy` nullable, `GrantedByMasterAdmin` not null mặc định false) [RT13]
   - `Ef_permission_store_round_trips_grantor` (grant feature/resource có `GrantedBy` + `GrantedByMasterAdmin = true` → đọc lại đủ; dòng ghi không có hai field → null/false) [RT13]
   - `Development_registration_resolves_one_in_memory_device_access_store` (2 interface → cùng instance `InMemoryDeviceAccessStore`)
   - `Database_registration_uses_scoped_ef_device_access_stores` (`databaseConnectionString` giả + `allowInMemoryDevelopment: true`; kiểm `ServiceDescriptor` ImplementationType + `Scoped`, không mở DB)

### B. Seams / infrastructure

1. Không cần seam mới: clock/actor đi qua `DeviceAccessAuditEntry` (store không đọc đồng hồ). `FrozenSet` có sẵn trong BCL (.NET 10).
2. Xác nhận tool: `dotnet ef --version` → 10.0.10 (khớp `Microsoft.EntityFrameworkCore.Design` 10.0.10 trong `Hms.Server.Infrastructure.csproj`).

### C. Refactor / implement

1. Domain: `DeviceRole.cs` (kể cả `NormalizedName`; không helper chuẩn hoá public), `DeviceMembership.cs` → test domain xanh.
2. Application: `DeviceAccessContracts.cs` (interface, audit entry + `EnsureValid`, `DeviceAccessChanges`, `InMemoryDeviceAccessStore` với `_gate`, sao chép role, check bất biến bảng trên — so `NormalizedName`, CAS `Version`, CAS role hiện tại — check và ghi trong cùng một lock, gọi `EnsureValid` đầu mỗi ghi) → test InMemory xanh.
3. Infrastructure: 4 record + 4 entity block + 4 `DbSet` (`NormalizedName`, 2 concurrency token chỉ ở model — không DDL, audit values `text`); `EfDeviceAccessStores.cs` (đọc `AsNoTracking().Include(Permissions)` rồi `Rehydrate`; ghi = find tracked → pre-check theo bảng → record + audit record → một `SaveChanges`; dịch exception như bảng). Lúc này `Model_snapshot_has_no_pending_changes` đỏ (đúng kỳ vọng).
4. Migration (từ thư mục repo):
   ```
   dotnet ef migrations add AddDeviceRolesAndMemberships --project src/Hms.Server.Infrastructure/Hms.Server.Infrastructure.csproj --startup-project src/Hms.Server.Infrastructure/Hms.Server.Infrastructure.csproj --context ApplicationDbContext --output-dir Persistence/Migrations
   ```
   Không kết nối DB (factory chỉ dựng options). Kiểm file sinh: chỉ 4 `CreateTable` + index + FK (`device_roles` có `NormalizedName` + unique (OrganizationId, NormalizedName); `PreviousValue/NewValue` kiểu `text`; `device_memberships.RoleId` và `device_roles.Version` là cột thường — không DDL nào cho concurrency token, chỉ snapshot ghi `.IsConcurrencyToken()`), không `AlterColumn`/`DropTable` trên bảng cũ, namespace `Hms.Server.Infrastructure.Persistence.Migrations`, snapshot giữ `ProductVersion 10.0.10`. Thêm cuối `Up`, đúng thứ tự [RT9]:
   ```csharp
   migrationBuilder.Sql("UPDATE policy_versions SET \"Version\" = GREATEST(\"Version\", COALESCE((SELECT MAX(\"PolicyVersion\") FROM permission_grants), 0)) WHERE \"Id\" = 1;");
   migrationBuilder.Sql("DELETE FROM permission_grants WHERE \"Kind\" = 'device-action';");
   ```
   Câu UPDATE căn bộ đếm theo mọi dòng grant đang có (gồm cả dòng `device-action` sắp xoá) nên `Advance()` sau đó luôn lớn hơn mọi dòng và getter không tụt sau DELETE. `Down` giữ `DropTable` sinh sẵn + comment: dòng grant legacy đã xoá không khôi phục, bộ đếm không hạ.
   Review SQL: `dotnet ef migrations script 20260929090323_AddLicenseTrustAudits AddDeviceRolesAndMemberships --project <như trên> --startup-project <như trên>`.
4b. Người cấp grant [RT13]: thêm field cuối vào `FeaturePermissionGrant`/`ResourceScopeGrant`, hai property vào `PermissionGrantRecord`, map trong `EfPermissionStore`; rồi `dotnet ef migrations add AddPermissionGrantGrantor` (cùng tham số). Kiểm file sinh: chỉ 2 `AddColumn` trên `permission_grants` (`GrantedByMasterAdmin` `defaultValue: false`), không đụng bảng khác.
5. DI (`DependencyInjection.cs` sau :77): có connection string → `AddScoped<IDeviceRoleStore, EfDeviceRoleStore>()`, `AddScoped<IDeviceMembershipStore, EfDeviceMembershipStore>()`; không → `AddSingleton<InMemoryDeviceAccessStore>()` + 2 forwarding `AddSingleton<I…>(sp => sp.GetRequiredService<InMemoryDeviceAccessStore>())`. Không đăng ký handler (phase 3), không gỡ `GrantDeviceActionHandler` (:80).
6. Compile: phase chỉ thêm type/file, không rename/xoá → không có điểm gãy cần hấp thụ; mỗi bước C1-C5 tự build được.

### D. Tests After

1. Chạy lại toàn bộ test bước A → xanh (gồm `Model_snapshot_has_no_pending_changes` sau C4b, `Migration_purges_legacy_device_action_grants`, `Migration_adds_permission_grant_grantor_columns`, `Ef_permission_store_round_trips_grantor`, `Ef_lost_race_maps_concurrency_conflicts_to_store_codes`, 2 test DI).
2. Bổ sung nếu lộ ra khi implement: `Ef_device_access_stores_map_unique_violation_after_reload` (tên role trùng → `DEVICE_ROLE_NAME_TAKEN`, insert membership đua → `DEVICE_MEMBERSHIP_CHANGED`) chỉ khi logic reload sau `DbUpdateException` tách được thành hàm thuần test được; không giả lập Postgres (EF InMemory không ép unique).
   **Đã làm khác (owner duyệt ở review vòng 1, 2026-10-05):** các nhánh map lỗi DB được test bằng `RaceInterceptor` trong `PersistenceModelTests`. Interceptor commit lần ghi cạnh tranh thật trong `SavingChanges` (sau load + pre-check của store), rồi ném `DbUpdateException` như PostgreSQL ném khi vướng unique/FK. DB không bị giả, code map là code thật. Test này đã bắt được lỗi `Update` ném `DbUpdateException` thô.

### Regression Gate

```
dotnet build Hms.Server.slnx
dotnet test Hms.Server.slnx
dotnet ef migrations has-pending-model-changes --project src/Hms.Server.Infrastructure/Hms.Server.Infrastructure.csproj --startup-project src/Hms.Server.Infrastructure/Hms.Server.Infrastructure.csproj
cd frontend && npm run typecheck && npm test && npm run build
```
Phải xanh: `tests/Hms.Server.UnitTests/DeviceRoleTests.cs`, `tests/Hms.Server.UnitTests/InMemoryDeviceAccessStoreTests.cs`, `tests/Hms.Server.IntegrationTests/PersistenceModelTests.cs`, `tests/Hms.Server.UnitTests/PermissionTests.cs`, `tests/Hms.Server.UnitTests/ManagementTests.cs`, `tests/Hms.Server.IntegrationTests/ManagementApiTests.cs`. Frontend không đổi; chạy để xác nhận nhánh vẫn xanh.

## Todo List

- [x] Viết test domain + InMemory + PersistenceModel + DI (đỏ); thêm test khoá snapshot (xanh)
- [x] `DeviceRole.cs` (kể cả `NormalizedName`), `DeviceMembership.cs`
- [x] `DeviceAccessContracts.cs` + `InMemoryDeviceAccessStore` (CAS role/membership dưới lock)
- [x] 4 record + entity config + `DbSet` (`NormalizedName`, 2 concurrency token chỉ ở model, audit `text`)
- [x] `EfDeviceAccessStores.cs` (pre-check + map `DbUpdateConcurrencyException`/`DbUpdateException`)
- [x] Sinh migration, thêm `UPDATE policy_versions` rồi `DELETE`, review SQL script
- [x] Field người cấp trên grant record + `EfPermissionStore` + migration `AddPermissionGrantGrantor`
- [x] DI hai nhánh
- [x] Regression Gate xanh

## Success Criteria

- [x] `dotnet build Hms.Server.slnx` 0 lỗi; `dotnet test Hms.Server.slnx` xanh toàn bộ.
- [x] `has-pending-model-changes` báo không thay đổi; `AddDeviceRolesAndMemberships` chỉ tạo 4 bảng + index/FK theo §8 + `UPDATE policy_versions` (trước) + `DELETE` (sau); `AddPermissionGrantGrantor` chỉ thêm 2 cột [RT9, RT13].
- [x] Grant feature/resource lưu và đọc lại `GrantedBy`/`GrantedByMasterAdmin`; dòng cũ đọc ra null/false; mọi call site positional cũ vẫn compile [RT13].
- [x] Unique (UserId, DeviceId), unique (OrganizationId, NormalizedName), FK Restrict/Cascade đúng §8, `device_roles.Version` + `device_memberships.RoleId` là concurrency token chỉ ở model (không DDL; `RoleId` đúng §8) — chứng minh bằng metadata test + review file migration.
- [x] `Operator` rồi `operator` cùng org → `DEVICE_ROLE_NAME_TAKEN` do store báo (nguồn duy nhất), không ghi role/audit; cùng tên ở org khác OK (EF và InMemory).
- [x] Upsert null→A→B với `expectedRoleId` đúng → một dòng, role mới, 2 audit; `expectedRoleId` lệch (upsert/remove) → `DEVICE_MEMBERSHIP_CHANGED`, không audit; remove vắng + expected null → `false`, không audit; remove → audit; regrant → có lại (EF và InMemory).
- [x] 200 upsert song song cùng `expectedRoleId` trên InMemory → đúng một bên thắng, còn lại `DEVICE_MEMBERSHIP_CHANGED`, một dòng/(user, device).
- [x] Role Update stale → `DEVICE_ROLE_VERSION_STALE`; Remove theo thứ tự `DEVICE_ROLE_NOT_FOUND` → `DEVICE_ROLE_VERSION_STALE` → `DEVICE_ROLE_IN_USE`; nhánh lỗi không ghi audit; Remove thành công xoá role + permission + 1 audit `ROLE_DELETED` trong một `SaveChanges`.
- [x] Thua đua trên EF (concurrency token, hoặc PostgreSQL chặn bằng unique/FK) → đúng mã store (`DEVICE_MEMBERSHIP_CHANGED` / `DEVICE_ROLE_VERSION_STALE` / `DEVICE_ROLE_NOT_FOUND` / `DEVICE_ROLE_NAME_TAKEN` / `DEVICE_ROLE_IN_USE`), dòng audit trong cùng `SaveChanges` (PostgreSQL rollback cùng; EF InMemory không chứng minh được — chỗ lệch đã chấp nhận).
- [x] Dev (không connection string) resolve một `InMemoryDeviceAccessStore` cho cả hai interface; có connection string → `Ef*` scoped.
- [x] Không có test cũ nào bị sửa ngoài `Model_has_management_tables_only`.

## Risk Assessment

| Risk | Mitigation |
|---|---|
| EF InMemory không ép unique/FK → test xanh dù logic sai | Pre-check tường minh trong cả hai store + metadata test + backstop Postgres dịch sang cùng code |
| Tầng trên tự kiểm tên trùng → hai nơi quyết định, quy tắc chuẩn hoá có thể lệch | Store là nguồn duy nhất (`NormalizedName` + unique index Postgres); domain không mở helper chuẩn hoá public; Next Steps ghi rõ handler chỉ map `DEVICE_ROLE_NAME_TAKEN` |
| CAS chỉ nằm ở pre-check → hai request cùng đọc bản cũ rồi cùng ghi (TOCTOU) trên Postgres | `Version`/`RoleId` là concurrency token chỉ ở model → EF sinh UPDATE/DELETE có điều kiện theo giá trị gốc (không cần DDL); insert đua chặn bởi unique (UserId, DeviceId); test thua đua chạy trên EF InMemory (có kiểm token) |
| Migration/SQL Postgres không chạy trong CI | Test `HasPendingModelChanges` + `UpOperations`; review `migrations script`; tuỳ chọn apply thử vào Postgres local trước merge |
| `DELETE` dòng `device-action` không đảo ngược được; code legacy còn đọc chúng tới phase 3-4 | Toàn plan release một lần; backup trước khi apply prod; V3 đã chốt: xoá (Validation Session 1). Dev/test dùng `EnsureCreated` nên không ảnh hưởng |
| DB Postgres có grant trước bộ đếm → latest-wins của phase 3 chọn sai dòng [RT9] | Câu UPDATE `GREATEST` căn bộ đếm trước DELETE; phase 3 thêm tie-break deny thắng. Repo không có dấu hiệu DB như vậy; chi phí một câu idempotent |
| Grant cũ không có người cấp [RT13] | Đọc ra null/false; phase 3 chỉ bảo vệ deny master-issued ghi sau migration (đã ghi ở risk phase 3) |
| Handler sửa object role tìm được làm hỏng state InMemory | Store lưu/trả bản sao; test `Role_store_returns_copies` |
| `ChangeTracker.Clear()` trên DbContext scoped dùng chung | Chỉ gọi khi một lần ghi thất bại (lúc stage hoặc lúc `SaveChanges`; precedent `EfCommandAuditStore.cs:83`), để không còn gì staged cho lần save sau. Các store khác gọi `SaveChanges` ngay trong cùng phương thức ghi, nên không có thay đổi chờ nào bị xoá oan |
| Phase 1 chạy song song va file | Phase 1 sở hữu catalog/`CommandDefinition`/`DevicePermissionCodes`; phase 2 không chạm các file đó; `DependencyInjection.cs` và `ApplicationDbContext.cs` chỉ phase 2 sửa trong cặp 1-2 |
| Phase 3 xoá `AuthorizationPolicy.cs` làm mất `UserId` | Ghi rõ ở Next Steps: giữ `UserId` trong namespace `Hms.Server.Domain.Authorization` |
| EF InMemory không có transaction: khi thua đua nó vẫn ghi dòng audit | Chấp nhận (review vòng 1). Test chứng minh dòng audit và lần ghi bị chặn nằm trong một `SaveChanges`, và sau lỗi tracker rỗng. Rollback dựa vào transaction của PostgreSQL, môi trường hiện chưa có để test |
| Một context đã track dòng cũ (nhiều lần ghi trong một request) | EF có thể báo nhầm `DEVICE_ROLE_VERSION_STALE` hoặc `DEVICE_MEMBERSHIP_CHANGED`, nhưng không bao giờ chấp nhận sai. Handler phase 3 đọc không track và ghi một lần mỗi request nên không gặp. Chấp nhận |
| Membership bị gỡ rồi gán lại đúng role cũ, giữa lúc store đọc và lúc ghi | Lần ghi thua nhận `DEVICE_MEMBERSHIP_CHANGED` vì dòng đã đổi (`Id` mới), dù role hiện tại khớp lại. Thử lại là thành công. Handler phase 3 có thể gặp: map 409 như các lần thua đua khác |
| Test 200 luồng của InMemory phát hiện lỗi tách lock theo xác suất | Chấp nhận: cấu trúc lock đơn giản (kiểm + ghi trong một `lock`) và đã qua review |

## Security Considerations

- Store không kiểm quyền; mọi ghi phải đi qua handler phase 3 (delegation, high-risk, org match). Controller không gọi `Upsert/Remove/Add/Update` trực tiếp.
- CAS ở store: hai admin sửa cùng membership/role đồng thời → đúng một bên ghi, bên kia nhận code 409, không ghi đè âm thầm (đóng TOCTOU giữa `Find` của handler và lúc ghi).
- Tên role so theo `NormalizedName` → không tạo được hai role chỉ khác hoa/thường (`Operator` vs `operator`) dễ gây gán nhầm; chỉ store quyết định (pre-check + unique index Postgres), handler không có đường kiểm thứ hai.
- Audit append-only: không có API sửa/xoá audit; không FK để audit sống sau khi xoá role. `PreviousValue/NewValue` là snapshot JSON gọn (Guid, mã quyền, tên role) — không email/display name (handler không được đưa PII vào).
- Fail closed: membership trỏ role không tồn tại bị từ chối ở store; Postgres FK Restrict chặn dangling khi race.
- High-risk không lưu DB (suy ra từ `DevicePermissionCodes.HighRisk` lúc kiểm) → không có cờ lệch thời.
- `DELETE` dòng `device-action` loại bỏ grant `*` legacy (lỗi 3) khỏi DB.
- Grant feature/resource ghi người cấp và cờ master-admin → truy được ai cấp/đảo, và phase 3 dựa vào cờ này để giữ deny của master-admin (Q12) [RT13].
- Bất biến role-org ở tầng handler (§8); store Update không bao giờ ghi `OrganizationId`; owner org thiết bị bất biến (`EfManagementStores.cs:66-78`).

## Open Questions

Không còn câu hỏi mở. Đã chốt ở Validation Session 1 (2026-10-05): <!-- Updated: Validation Session 1 - V3, V5 confirmed -->
1. V3: migration **xoá** dòng `device-action` (giữ `DELETE` theo §8, backup trước khi apply lên DB có dữ liệu).
2. V5: store **từ chối** xoá role đang dùng (`DEVICE_ROLE_IN_USE`, FK membership→role Restrict) theo §4/§8.

## Next Steps

- Phase 3 dùng: `IDeviceRoleStore.Find` + `IDeviceMembershipStore.Find` cho `DeviceAccessResolver` (1 + 1 read); `ListByRole` để lấy danh sách thiết bị cho một lần `AdvanceMany` khi sửa role [RT7]; field `GrantedBy`/`GrantedByMasterAdmin` cho luật Q12 của grant handler [RT13]; `DeviceAccessChanges` + `DeviceAccessAuditEntry` (snapshot JSON gọn) trong handler; truyền `expectedVersion` vào `IDeviceRoleStore.Remove` và `expectedRoleId` vào `Upsert`/`Remove` membership (CAS ở store là chốt cuối sau check sớm của handler); map `InvalidOperationException.Message` (`DEVICE_ROLE_NAME_TAKEN`, `DEVICE_ROLE_VERSION_STALE`, `DEVICE_ROLE_NOT_FOUND`, `DEVICE_ROLE_IN_USE`, `DEVICE_MEMBERSHIP_CHANGED`) sang `DeviceAccessOutcome.Code`; tên/mô tả sai hình thức → `DEVICE_ROLE_INVALID` (kiểm trước hoặc bắt `ArgumentException` của `DeviceRole.Create/Update`); tên trùng: handler **không** pre-check — kiểm `NormalizedName` của store (cùng unique index Postgres) là nguồn duy nhất, handler chỉ map `DEVICE_ROLE_NAME_TAKEN` store báo, nên mã này luôn đến sau mọi kiểm tra của handler; `DeviceRole.Version` cho `expectedVersion`; phải giữ `UserId` trong namespace domain authorization khi dọn `AuthorizationPolicy.cs`; đăng ký handler trong DI.
- Phase 4 dùng `ListByDevice`/`ListByOrganization` (qua handler/resolver) cho GET memberships và device-roles.
