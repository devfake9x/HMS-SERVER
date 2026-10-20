---
phase: 5
title: "Chặn realtime và thu hồi tức thì"
status: pending
priority: P1
dependencies: [3]
---

# Phase 5: Chặn realtime và thu hồi tức thì

## Context Links

- [design-contract.md](./reports/design-contract.md): §7 Realtime (chính); §3 thứ tự `DeviceAccessPolicy.Evaluate`, `InScope` (org ∈ effective orgs **hoặc** có membership trên thiết bị), `SeesAllInScope`, master-admin vô điều kiện (deny feature/resource không áp lên master-admin); §4 `AccessActorFactory.FromPrincipal` (phase 3 tạo; master-admin = mọi org, không trừ deny), `IDeviceAccessResolver` (scoped), thứ tự ghi (store + audit → tín hiệu sau commit best-effort: `PolicyVersion` → epoch; thay/gỡ membership advance epoch thiết bị đó, gán mới không advance, sửa role qua một lần `AdvanceMany`) [RT7], §0 Q11 (gỡ role master-admin), handler cần `ICommandCatalog` (vocabulary) và `IUserScopeStore` (luật target-user), test dùng một double `IUserScopeStore`; §1 `DevicePermissionCodes.DeviceView`; §5/§12 chỉ phát hành sau phase 6 (phase 4–6 cùng một release); §10 out of scope (backplane); §11 V1–V6; §12 thứ tự chạy.
- [scout-realtime-surface-map.md](./reports/scout-realtime-surface-map.md): bản đồ hub, delivery, fence, test.
- Brainstorm: `plans/reports/261004-2234-brainstorm-device-permissions-what-where.md` §5.6 (SignalR subscribe = `device.view`; thu hồi → epoch + ngắt).
- [plan.md](./plan.md) acceptance: "ngắt trong một tick, đúng reason", "thiết bị A không làm rớt sự kiện thiết bị B", "member chỉ có membership vẫn thấy đúng thiết bị đó".
- Phụ thuộc: [phase-03](./phase-03-access-evaluation-and-delegation.md) (`AccessActorFactory`, resolver, policy, handler; fixture test `DeviceAccessTestFixture` với `TestUserScopes`, `PinnedCommandCatalog`, `Handlers()`). Liên quan, không phụ thuộc: [phase-04](./phase-04-http-enforcement-and-endpoints.md) (`realtime-authorization`; `DeviceGate` chỉ cho HTTP), [phase-06](./phase-06-frontend-roles-members-gating.md) (client xử lý reason).

## Overview

Hub `EnsureAuthorized` và 3 điểm kiểm trong delivery chuyển từ org-scope (`CanAccessDevice`) sang gọi thẳng `IDeviceAccessResolver` + `DeviceAccessPolicy` với `device.view` (không qua `DeviceGate`, vốn là helper HTTP). Membership đưa đúng thiết bị đó vào scope (§3), nên member chỉ có membership (không resource grant) vẫn join và nhận event của thiết bị đó. Master-admin vô điều kiện (§3): deny feature/resource không chặn join và không đuổi subscription của master-admin. Tách reason thu hồi thành hằng (`DEVICE_REVOKED`, `ACCESS_REMOVED`, `AUTHORIZATION_CHANGED`, `REALTIME_RESYNC_REQUIRED`) và bỏ hàng rào `PolicyVersion` toàn cục để đổi quyền ở thiết bị A không làm rớt sự kiện của thiết bị B.

## Key Insights

- Số dòng trích dưới đây đo trước khi các phase trước sửa code; định vị theo symbol (§12).
- Hub chỉ kiểm org-scope: `src/Hms.Server.Api/Hubs/DeviceEventsHub.cs:76-92`. Device thiếu → `DEVICE_NOT_FOUND` (:82-83), ngoài scope → `DEVICE_SCOPE_DENIED` (:84-85, lộ việc thiết bị tồn tại, sai rule 404). Revoked kiểm sau scope (:86-87), epoch kiểm cuối (:89-91).
- `RevalidateDevice` gói mọi lỗi thành `DEVICE_REAUTHORIZATION_REQUIRED` và gỡ cả registry lẫn group (`DeviceEventsHub.cs:41-46`). Giữ nguyên hành vi này.
- Delivery tạo một DI scope mỗi tick 100ms (`SignalRDeviceEventDeliveryService.cs:21,32-37`). Cùng một predicate lặp ở 3 chỗ: enqueue :77-80, send :127-132, reconcile :174-177. Reconcile chạy đầu tick trên mọi subscription (:39,159-181).
- Fence toàn cục: `RealtimeEventFence.IsCurrent` đòi epoch bằng **và** `PolicyVersion` bằng (`src/Hms.Server.Application/Realtime/RealtimeContracts.cs:42-50`). Hậu quả: event bị bỏ im lặng lúc enqueue (`SignalRDeviceEventDeliveryService.cs:68-69`) và kết nối bị đuổi lúc send (:132-134) mỗi khi `PolicyVersion` tăng, kể cả do tạo user (`src/Hms.Server.Infrastructure/Identity/IdentityUserManagement.cs:43`). `DrainForDelivery` vốn chỉ lọc theo epoch (`RealtimeContracts.cs:140-152`). Từ phase 3, mọi ghi membership/role cũng advance `PolicyVersion`, nên không được phát hành trước phase này; vẫn chỉ phát hành sau phase 6 (phase 4–6 cùng một release, §5/§12), vì trước đó frontend cũ còn gọi endpoint mà phase 4 đã xoá.
- Reason hiện tại: mặc định `AUTHORIZATION_REVOKED` (`SignalRDeviceEventDeliveryService.cs:214`), resync `REALTIME_RESYNC_REQUIRED` (:88,:123,:147). Client không tách được "bị gỡ quyền" với "quyền đổi, join lại được".
- Refresh principal lỗi thì gỡ **im lặng** (`SignalRDeviceEventDeliveryService.cs:198-203`, bỏ cả backlog connection ở :200). Refresher mặc định nuốt exception thành `null` (`src/Hms.Server.Infrastructure/Identity/IdentityRealtimePrincipalRefresher.cs:52-56`), cùng `null` dùng cho user mất, bị khoá hay đổi stamp (`:29-43`) → server không tách được lỗi DB tạm thời với mất hiệu lực thật, nên phase này gửi reason không kết thúc `REALTIME_RESYNC_REQUIRED` [RT6].
- Epoch đọc từ DB mỗi lần (`src/Hms.Server.Infrastructure/Persistence/EfAuthorizationEpochStore.cs:11-15`), nên epoch là primitive đuổi khách xuyên replica. Hiện chỉ `RevokeDeviceHandler` advance epoch (`src/Hms.Server.Application/Authorization/PermissionContracts.cs:89`). Phase 3 thêm advance khi thay/gỡ membership và sửa role (`AdvanceMany`); gán mới không advance (§4) [RT7].
- Epoch theo thiết bị, không theo user: mỗi lần thay/gỡ membership hay sửa role đuổi **mọi** subscriber của thiết bị đó — `ACCESS_REMOVED` cho người mất quyền, `AUTHORIZATION_CHANGED` cho mọi người còn lại (kể cả người không đổi quyền) — và event mang epoch cũ bị bỏ im lặng ở `DrainForDelivery` (`RealtimeContracts.cs:140-152`), ở fence lúc enqueue (`SignalRDeviceEventDeliveryService.cs:68-69`) và khi gỡ backlog (`:236`). Đây là thiết kế thô có chủ đích (chạy đúng xuyên replica); phase 6 làm cho việc join lại bền hơn [RT7].
- `CanAccessDevice` (`src/Hms.Server.Api/Authorization/ScopeAuthorization.cs:72-77`, class `internal`) là org-scope thuần, không biết membership: member chỉ có membership hiện bị `DEVICE_SCOPE_DENIED` ở hub và bị reconcile đuổi ngay. Hàm còn caller HTTP mà §6 chuyển sang `DeviceGate`; phase này chỉ bỏ lời gọi trong `Api/Hubs`.
- §3: `InScope` = org thiết bị ∈ effective orgs **hoặc** actor có membership trên thiết bị; `SeesAllInScope` không bao giờ đến từ membership. Phần org cố định trong tick (`Device.OwnerOrganizationId` get-only, `src/Hms.Server.Domain/Devices/Device.cs:29`); phần membership và quyền role có thể đổi giữa tick, nên mỗi lần kiểm đều resolve live (không cache theo tick) [RT11]. Revoked phải đọc live.
- §3 (cập nhật): master-admin vô điều kiện. `AccessActorFactory` cho master-admin mọi org, không trừ deny (§4); policy cho master-admin qua ngay sau kiểm revoked; feature deny (kể cả `*`) không đụng tới quyết định thiết bị của master-admin. Code hiện tại trừ deny cả với master-admin (`ScopeAuthorization.cs:33-38`), nên resource deny trên org thiết bị làm hub trả `DEVICE_SCOPE_DENIED` và reconcile đuổi master-admin. Phase 3 chuyển `ScopeAuthorization` sang ủy quyền `AccessActorFactory`; phase này khoá hành vi mới trên đường realtime. Grant handler feature/resource chỉ advance `PolicyVersion`, không đổi epoch (`PermissionContracts.cs:45-65`), nên deny chỉ tác động realtime qua reconcile đọc live: scope admin mất quyền thì bị đuổi, master-admin thì không. Muốn chặn master-admin thì master-admin khác gỡ role qua endpoint của phase 4 (Q11): stamp đổi → refresher trả `null` ở tick kế tiếp → `REALTIME_RESYNC_REQUIRED`; join lại thì hub trả `AUTHENTICATION_REQUIRED` [RT1, RT6].
- Phase 3 tạo `AccessActorFactory.FromPrincipal(ClaimsPrincipal, IOrganizationStore, IPermissionStore)` (§4: không phụ thuộc `HttpContext`, `NameIdentifier` thiếu/sai → `null`; effective orgs từ resource grant latest-wins, claim `org-scope` chỉ dành cho test) và đăng ký `IDeviceAccessResolver` scoped: hub (mỗi invocation) và delivery (scope mỗi tick) đều resolve từ scope hiện tại. `DeviceGate` (§6) trả status/body HTTP; Hubs không dùng.
- Test khoá giả định cũ: `tests/Hms.Server.UnitTests/RealtimeTests.cs:63-74` (drift `PolicyVersion` ⇒ không current), :171-190 (`"AUTHORIZATION_REVOKED"`); `SignalRDeviceEventDeliveryServiceTests.cs:103` (`Assert.Empty(Sent)` khi refresh lỗi); principal test chỉ có claim `org-scope` (`DeviceEventsHubTests.cs:28-32`, `SignalRDeviceEventDeliveryServiceTests.cs:30-32,81-83,124-126,183-185`).
- Frontend bỏ qua `reason` (`frontend/src/features/devices/views/DeviceDetailView.vue:268-272`). Fixture `'AUTHORIZATION_REVOKED'` ở `frontend/src/useDeviceOperationsHub.test.ts:100` chỉ là mock phía client, không vỡ. Phase 6 xử lý reason và chuyển test này sang `core/realtime/` (§9).
- Phase 2 dùng một `InMemoryDeviceAccessStore` cho cả `IDeviceRoleStore` và `IDeviceMembershipStore` (phase-02, DI sau `DependencyInjection.cs:77`); store EF đọc `AsNoTracking`, nên đọc lại trong cùng scope tick thấy dữ liệu mới.
- Handler thật của phase 3 cần phụ thuộc ngoài store: `UpdateDeviceRoleHandler` cần `ICommandCatalog` (quyền phải ∈ `DevicePermissionVocabulary.All(catalog)`, nên `device.view`/`adc.view` chỉ hợp lệ trên catalog v2 thật); `AssignDeviceMembershipHandler` cần `IUserScopeStore` (target phải `Exists`, kể cả khi actor là master-admin); `RemoveDeviceMembershipHandler` chỉ cần nó khi không có membership để gỡ — gỡ membership đang có dựa vào chính membership đó (Q13) [RT14]. Phase 3 đã tạo `DeviceAccessTestFixture` (`tests/Hms.Server.UnitTests/DeviceAccessTestFixture.cs`, cùng assembly) với `TestUserScopes : IUserScopeStore`, `Catalog = new PinnedCommandCatalog()` và `Handlers()` dựng handler thật trên store của chính fixture đó. Thêm một double `IUserScopeStore` hay catalog test thứ hai vào `Hms.Server.UnitTests` sẽ trùng tên type (CS0101) hoặc lặp code, nên fixture realtime dùng lại fixture phase 3 (§4).

## Requirements

**Functional**
- F1 `JoinDevice`/`RevalidateDevice` dùng `device.view` qua resolver + policy. Code: `DEVICE_NOT_FOUND` (thiếu thiết bị, hoặc ngoài scope: org ∉ effective orgs và không có membership; kể cả revoked ngoài scope), `DEVICE_REVOKED`, `DEVICE_PERMISSION_REQUIRED`, `STALE_AUTHORIZATION_EPOCH`. Giữ `AUTHENTICATION_REQUIRED`; Revalidate vẫn gói lỗi thành `DEVICE_REAUTHORIZATION_REQUIRED`. Bỏ `DEVICE_SCOPE_DENIED`.
- F2 Master-admin vào mọi thiết bị tồn tại, chưa revoked, không cần membership, kể cả khi bị deny feature (kể cả `*`) hoặc deny resource trên org thiết bị (§3: vô điều kiện; chỉ endpoint gỡ role `master-admin` của phase 4 mới chặn được, Q11). Scope admin (`devices.manage` hoặc `permissions.manage`, org thiết bị ∈ effective orgs) thấy mọi thiết bị trong phạm vi. Member cần role có `device.view`; membership tự đưa thiết bị đó vào scope, nên member không có resource grant vẫn join được đúng thiết bị đó, còn thiết bị khác vẫn `DEVICE_NOT_FOUND`.
- F3 Ba điểm kiểm trong delivery dùng cùng một evaluator. Chọn reason theo thứ tự cố định (bảng ở Architecture). Deny feature/resource không đổi epoch, chỉ có hiệu lực qua reconcile đọc live: scope admin mất quyền → `ACCESS_REMOVED`; subscription master-admin không bao giờ bị đuổi vì deny.
- F4 Mỗi lần kiểm resolve `DeviceAccess` live (không cache theo tick); epoch và trạng thái thiết bị luôn đọc live [RT11].
- F5 `RealtimeEventFence` chỉ so epoch thiết bị. Bỏ việc đuổi khách vì `PolicyVersion` lúc gửi. `DeviceRevokedNotification` vẫn mang `PolicyVersion` để giữ hình payload.
- F6 Refresh principal lỗi (`null` hoặc exception) thì **gửi `DeviceRevoked` reason `REALTIME_RESYNC_REQUIRED` rồi mới gỡ**, vẫn fail-closed; `ACCESS_REMOVED` chỉ dành cho kết quả đánh giá truy cập bị từ chối sau khi refresh thành công (dòng 3; xem Architecture §Quyết định) [RT6].

**Non-functional**
- Gỡ membership hoặc sửa role thì bị đuổi trong vòng reconcile kế tiếp (≤ 1 tick sau khi epoch tăng hoặc quyền đổi). Không phụ thuộc registry cục bộ, nên đúng cả khi nhiều replica.
- Mỗi lần kiểm đọc membership + role một lần (membership theo unique index, role theo PK) [RT11]. Chi phí dựng actor xấp xỉ `CanAccessDevice` hiện tại: thêm một query `FeatureGrants`, bớt một–hai lần đọc bảng org.
- Không đổi DI, `Program.cs`, frontend (resolver đã được phase 3 đăng ký). Không thêm reason ngoài 4 reason ở §7.
- Assembly `Hms.Server.UnitTests` chỉ có một double `IUserScopeStore` (`TestUserScopes` của `DeviceAccessTestFixture`); fixture realtime không khai báo lại type nào của fixture phase 3.

## Architecture

**Thành phần mới** `src/Hms.Server.Api/Hubs/RealtimeDeviceAuthorizer.cs` (public, giống các class Hubs khác, để test trực tiếp): lớp mỏng, không trạng thái, gọi thẳng `AccessActorFactory` + `IDeviceAccessResolver` + `DeviceAccessPolicy`, không qua `DeviceGate`. Mỗi lần gọi hub tạo một instance; delivery tạo một instance mỗi tick (gom store của scope tick). Không đăng ký DI. Không cache `DeviceAccess`: mỗi lần kiểm đọc live, đúng như đường hub [RT11].

```csharp
public sealed class RealtimeDeviceAuthorizer(IDeviceStore devices, IOrganizationStore organizations,
    IPermissionStore permissions, IDeviceAccessResolver resolver, IAuthorizationEpochStore epochs)
{
    public IAuthorizationEpochStore Epochs => epochs;
    public long PolicyVersion => permissions.PolicyVersion;

    public DeviceAccessDecision Evaluate(ClaimsPrincipal principal, DeviceId deviceId)
    {
        var actor = AccessActorFactory.FromPrincipal(principal, organizations, permissions);
        if (actor is null) return new(false, "AUTHENTICATION_REQUIRED");          // fail closed
        var device = devices.Find(deviceId);                                      // live: revoke visible immediately
        if (device is null) return new(false, DeviceAccessCodes.DeviceNotFound);   // same code as out of scope
        var access = resolver.Resolve(actor, device);                             // membership + role, read live
        return DeviceAccessPolicy.Evaluate(access, device, DevicePermissionCodes.DeviceView);
    }

    public string? EvictionReason(DeviceSubscription subscription)
    {
        var epochMoved = epochs.Current(subscription.DeviceId) != subscription.AuthorizationEpoch;
        var decision = Evaluate(subscription.Principal, subscription.DeviceId);
        if (decision.Code == DeviceAccessCodes.DeviceRevoked) return DeviceRevokedReasons.DeviceRevoked;
        if (!decision.Allowed) return DeviceRevokedReasons.AccessRemoved;
        return epochMoved ? DeviceRevokedReasons.AuthorizationChanged : null;
    }
}
```

Access luôn đọc live và được đánh giá trước epoch, nên membership/role đổi giữa tick vẫn ra `ACCESS_REMOVED` chứ không phải `AUTHORIZATION_CHANGED` [RT11].

**Hub**: ctor thêm `IDeviceAccessResolver`. `EnsureAuthorized` theo thứ tự: authenticated → `Evaluate` (actor trước device, nên không dò được sự tồn tại thiết bị khi chưa có danh tính) → `!Allowed` thì `throw new HubException(decision.Code)` → epoch khác thì `STALE_AUTHORIZATION_EPOCH`. Thứ tự scope → revoked của policy khiến revoked ngoài scope vẫn là `DEVICE_NOT_FOUND`.

**Delivery**: đầu mỗi tick dựng `tick = new RealtimeDeviceAuthorizer(...)` từ scope của tick (gồm `IDeviceAccessResolver` scoped). Các hàm `Reconcile/Queue/Drain/RefreshSubscription/RevokeAndRemove` nhận `tick` thay cho 4 store, rút gọn danh sách tham số đang lặp 6 lần. `RevokeAndRemoveAsync(subscription, tick, reason, ct)` bỏ giá trị mặc định của `reason`, nên compiler buộc mọi chỗ gọi truyền reason tường minh.

**Chọn reason** (theo đúng thứ tự):

| # | Điều kiện | Reason |
|---|---|---|
| 1 | refresh principal trả `null` hoặc ném lỗi [RT6] | `REALTIME_RESYNC_REQUIRED` |
| 2 | `decision.Code == DEVICE_REVOKED` | `DEVICE_REVOKED` |
| 3 | không `Allowed` (thiếu thiết bị, ngoài scope, thiếu `device.view`, không dựng được actor) | `ACCESS_REMOVED` |
| 4 | `Allowed` nhưng epoch hiện tại ≠ epoch của subscription; lúc gửi: event mang epoch cũ | `AUTHORIZATION_CHANGED` |
| 5 | backlog đầy, cần resync, hết lượt retry | `REALTIME_RESYNC_REQUIRED` |

Tại drain, kiểm quyền (2-4) trước resync (5), để reason bảo mật thắng (hiện resync được kiểm trước, :121-125). Gỡ membership kéo theo epoch tăng nhưng vẫn ra `ACCESS_REMOVED`, vì quyền được đánh giá (đọc live) trước epoch; member chỉ có membership bị gỡ thì policy trả `DEVICE_NOT_FOUND` (mất scope), vẫn là `ACCESS_REMOVED`. Sửa role mà vẫn giữ `device.view`, hoặc thay/gỡ membership của **người khác** trên cùng thiết bị, thì ra `AUTHORIZATION_CHANGED`: client lấy epoch mới và join lại (phase 6 thử lại có giới hạn). Gán mới không tăng epoch nên không ai bị đuổi [RT7].

Deny feature/resource không bao giờ đưa master-admin vào dòng 3: actor master-admin có mọi org, không trừ deny (§4), và policy cho qua ngay sau kiểm revoked (§3). Deny cũng không đổi epoch, nên không sinh dòng 4. Gỡ role `master-admin` qua endpoint phase 4 đổi security stamp, nên ở tick kế tiếp refresher trả `null` → dòng 1 (`REALTIME_RESYNC_REQUIRED`); join lại thì hub trả `AUTHENTICATION_REQUIRED` [RT1, RT6].

**RealtimeContracts.cs**
```csharp
public static class DeviceRevokedReasons
{
    public const string DeviceRevoked = "DEVICE_REVOKED";
    public const string AccessRemoved = "ACCESS_REMOVED";
    public const string AuthorizationChanged = "AUTHORIZATION_CHANGED";
    public const string RealtimeResyncRequired = "REALTIME_RESYNC_REQUIRED";
}
public static class RealtimeEventFence   // per-device epoch only; global PolicyVersion is not a realtime fence
{
    public static bool IsCurrent(AuthorizedDeviceEvent deviceEvent, IAuthorizationEpochStore epochs) =>
        epochs.Current(deviceEvent.DeviceId) == deviceEvent.AuthorizationEpoch;
}
```
`AuthorizedDeviceEvent.PolicyVersion` giữ nguyên: `DevicesController.cs:118-126` vẫn gắn, frontend fence vẫn đọc.

**Quyết định: refresh principal lỗi thì gửi `REALTIME_RESYNC_REQUIRED`** [RT6]. Hiện client vẫn tưởng mình đang subscribe sau khi bị khoá tài khoản hoặc đổi security stamp, nên phải gửi thông báo. Refresher trả `null` cho user mất/khoá/đổi stamp và nuốt mọi exception thành `null`, nên server không phân biệt được lỗi DB tạm thời với phiên mất hiệu lực thật. Reason không kết thúc hợp với trường hợp mơ hồ: client hiện "realtime disconnected", trang vẫn dùng được; join lại thủ công đi qua `RefreshPrincipalAsync` của hub, phiên khoá/xoá/đổi stamp nhận `AUTHENTICATION_REQUIRED`. `ACCESS_REMOVED` chỉ dành cho kết quả đánh giá truy cập bị từ chối sau khi refresh thành công (dòng 3: ngoài phạm vi, thiếu thiết bị, thiếu `device.view`, không dựng được actor), nên client có thể coi nó là trạng thái mất quyền. Không đổi refresher (`IdentityRealtimePrincipalRefresherTests` giữ nguyên). Payload chỉ chứa deviceId client đã biết, cộng epoch/`PolicyVersion` vốn đã lộ qua `realtime-authorization`. Việc gỡ vẫn nằm trong `finally` của `RevokeAndRemoveAsync`. Khi DB sập hẳn, chính việc dựng payload (đọc epoch/`PolicyVersion`) cũng lỗi nên việc gỡ vẫn im lặng như hiện nay. `_backlogs.Remove(connectionId)` (cả connection) được giữ, vì mọi subscription của connection dùng chung principal.

## Related Code Files

**Create**

| Path | Thay đổi |
|---|---|
| `src/Hms.Server.Api/Hubs/RealtimeDeviceAuthorizer.cs` | evaluator dùng chung cho hub và delivery; resolve live, không cache; chọn reason [RT11] |
| `tests/Hms.Server.UnitTests/RealtimeAccessFixture.cs` | lớp mỏng bọc `DeviceAccessTestFixture` của phase 3 (composition): dùng lại store, `TestUserScopes`, `Catalog` (`PinnedCommandCatalog`), `Resolver`, actor `MasterAdmin` và `Handlers()`; chỉ thêm seed device/role/membership, principal builder (member chỉ membership hoặc có `org-scope`, scope admin, master-admin) và `Services(refresher)`; không định nghĩa double `IUserScopeStore` hay catalog thứ hai; dùng chung cho 3 file test (Hub, Authorizer, Delivery) |
| `tests/Hms.Server.UnitTests/RealtimeDeviceAuthorizerTests.cs` | test chọn reason và đọc live [RT11] |
| `tests/Hms.Server.IntegrationTests/RealtimeWiringTests.cs` | kích hoạt `DeviceEventsHub` và resolve `IDeviceAccessResolver` (scoped) từ container thật |

**Modify**

| Path | Thay đổi |
|---|---|
| `src/Hms.Server.Api/Hubs/DeviceEventsHub.cs` | ctor + `IDeviceAccessResolver`; `EnsureAuthorized` qua authorizer; bỏ `DEVICE_SCOPE_DENIED` |
| `src/Hms.Server.Api/Hubs/SignalRDeviceEventDeliveryService.cs` | authorizer theo tick; 3 điểm kiểm; bảng reason; bỏ drift `PolicyVersion`; refresh lỗi gửi `REALTIME_RESYNC_REQUIRED` [RT6] |
| `src/Hms.Server.Application/Realtime/RealtimeContracts.cs` | `DeviceRevokedReasons`; fence chỉ epoch (bỏ tham số `currentPolicyVersion`) |
| `tests/Hms.Server.UnitTests/DeviceEventsHubTests.cs` | **sửa test cũ** `Revalidate_adds_group_before_registering_subscription` (ctor + membership); thêm test |
| `tests/Hms.Server.UnitTests/SignalRDeviceEventDeliveryServiceTests.cs` | **sửa 4 test cũ** (DI thêm resolver/store, principal có membership `device.view`; :103 đổi assertion; thêm assertion reason); thêm test |
| `tests/Hms.Server.UnitTests/RealtimeTests.cs` | **sửa** :63-74 (fence) và :171-190 (reason); thêm test hằng |
| `tests/Hms.Server.UnitTests/DeviceAccessTestFixture.cs` | chỉ khi thiếu: expose public các thành phần fixture realtime dùng lại (store, `TestUserScopes`, `Catalog`, `Resolver`, `Handlers()`); additive, test phase 3 không đổi |

**Delete**: không có file. Bỏ literal `DEVICE_SCOPE_DENIED`, `AUTHORIZATION_REVOKED` và các lời gọi `ScopeAuthorization.CanAccessDevice` trong `Api/Hubs`; xoá hàm `CanAccessDevice` nếu `src` không còn caller.

**Không chạm** (phase khác sở hữu): `AccessActorFactory.cs`, `DeviceAccessResolver.cs`, `DependencyInjection.cs` (phase 2/3, chỉ dùng); `ScopeAuthorization.cs` (trừ xoá `CanAccessDevice` khi hết caller), `DeviceGate.cs`, `DevicesController.cs` (phạm vi HTTP; Hubs không dùng `DeviceGate`); `SignalRSubscriptionRegistry.cs`, `EfAuthorizationEpochStore.cs` (phase 3 thêm `AdvanceMany`), `IdentityRealtimePrincipalRefresher.cs` (giữ nguyên, reason do delivery chọn) [RT6, RT7], `frontend/**` (phase 6).

## Implementation Steps

### A. Tests Before (red)

1. `RealtimeAccessFixture` (helper test) bọc một `DeviceAccessTestFixture` của phase 3 (property `Access`, composition), không dựng store riêng:
   - Dùng lại từ `Access`: store role/membership (wrapper ghi lại bọc `InMemoryDeviceAccessStore`), `InMemoryDeviceStore`, `InMemoryOrganizationStore`, `InMemoryPermissionStore`, epoch store, `Resolver`, `TestUserScopes` (double `IUserScopeStore` duy nhất của assembly), `Catalog = new PinnedCommandCatalog()` (vocabulary 50 thật), actor `MasterAdmin(UserId)` và `Handlers()` (`UpdateDeviceRoleHandler`, `RemoveDeviceMembershipHandler` thật trên đúng các instance đó).
   - Không khai báo lại double `IUserScopeStore`, catalog test hay type nào của fixture phase 3 (trùng tên type trong cùng assembly → CS0101). Thiếu accessor thì thêm accessor public vào `DeviceAccessTestFixture` (additive, không đổi hành vi test phase 3).
   - Chỉ thêm phần realtime: `AddDevice` (vào một org cố định của `Access`); `CreateRole(params string[])` (cùng org đó, vì role phải thuộc org thiết bị — Q8; trả role kèm `Version` cho `UpdateDeviceRoleHandler`; mã phải ∈ vocabulary, vd. `DevicePermissionCodes.DeviceView`, `adc.view`); `Assign(user, device, role)` (ghi thẳng store, không advance epoch; đăng ký user vào `TestUserScopes` với org thiết bị để handler thật qua luật target-user); `MemberPrincipal(userId, orgScoped = false)` (mặc định chỉ có `NameIdentifier`: membership là nguồn scope duy nhất, không resource grant; `orgScoped` thêm claim `org-scope`) / `ScopeAdminPrincipal(feature)` / `MasterAdminPrincipal()` (claim role `RoleAuthorizationPolicy.MasterAdminRole`) dựng `ClaimsPrincipal`, đặt tên khác các factory `AccessActor` của phase 3; `Services(refresher)` dựng `ServiceProvider` đăng ký đúng các instance mà `Handlers()` dùng (singleton) + `IDeviceAccessResolver` scoped như phase 3, để thay đổi qua handler thấy ngay ở hub/delivery.
2. `RealtimeTests.cs`:
   - `Event_fence_ignores_policy_version_drift_and_tracks_epoch_only`: thay test :63-74. Event (epoch 0, PolicyVersion 4), store đã `Advance()` tới 5 vẫn current; `epochs.Advance` thì không current.
   - `Device_revoked_reasons_are_stable_wire_values`: 4 hằng bằng đúng chuỗi §7.
   - `Device_revoked_notification_carries_current_authorization_context`: đổi `"AUTHORIZATION_REVOKED"` thành `DeviceRevokedReasons.AccessRemoved`.
3. `DeviceEventsHubTests.cs`:
   - `Join_rejects_missing_device_as_not_found` (khoá hành vi cũ).
   - `Join_rejects_out_of_scope_device_as_not_found` (đỏ: hiện `DEVICE_SCOPE_DENIED`).
   - `Join_rejects_revoked_out_of_scope_device_as_not_found` (đỏ).
   - `Join_rejects_in_scope_revoked_device_as_revoked` (member có `device.view`).
   - `Join_rejects_in_scope_member_without_device_view` (Theory: `org-scope` không membership; chỉ membership với role thiếu `device.view`) → `DEVICE_PERMISSION_REQUIRED` (đỏ: hiện cho vào hoặc `DEVICE_SCOPE_DENIED`).
   - `Join_allows_membership_only_member_on_that_device_only` (đỏ: hiện `DEVICE_SCOPE_DENIED`): không `org-scope`, không resource grant, role `device.view` trên D1 → join D1, registry có entry, group được add; D2 cùng org, không membership → `DEVICE_NOT_FOUND`.
   - `Join_allows_master_admin_without_membership_or_scope`.
   - `Join_allows_master_admin_despite_feature_and_resource_denies` (khoá §3; code trước plan trả `DEVICE_SCOPE_DENIED` vì trừ deny khỏi org của master-admin): ghi deny feature `*` và deny resource org thiết bị cho user master-admin thẳng vào `InMemoryPermissionStore` như grant handler (`Advance()` rồi `GrantFeature`/`GrantResource`, `Deny = true`) → join thành công, registry có entry, group được add.
   - `Join_allows_scope_admin_without_membership` (Theory `devices.manage`, `permissions.manage`).
   - `Join_rejects_stale_epoch_after_access_passes` → `STALE_AUTHORIZATION_EPOCH`.
   - `Revalidate_failure_removes_group_and_subscription` (khoá hành vi :41-46): thiếu `device.view` → `DEVICE_REAUTHORIZATION_REQUIRED`, có lệnh remove group, registry trống.
   - Sửa `Revalidate_adds_group_before_registering_subscription`: principal `orgScoped` có membership `device.view`, assertion giữ nguyên.
4. `RealtimeDeviceAuthorizerTests.cs` [RT11]:
   - `Membership_change_is_seen_by_the_next_evaluation`: cùng một instance, `Evaluate` cho phép; gỡ membership (ghi thẳng store) → lần `Evaluate` kế tiếp trả `DEVICE_PERMISSION_REQUIRED` (member `orgScoped`) hoặc `DEVICE_NOT_FOUND` (member chỉ membership).
   - `Eviction_reason_follows_access_before_epoch` (Theory): gỡ + epoch tăng → `ACCESS_REMOVED`; revoked + epoch → `DEVICE_REVOKED`; vẫn được phép + epoch → `AUTHORIZATION_CHANGED`; không đổi gì → `null`; master-admin bị deny feature `*` + deny resource org thiết bị → `null`, cùng deny + epoch tăng → `AUTHORIZATION_CHANGED` (không bao giờ `ACCESS_REMOVED`).
   - `Missing_device_or_identity_maps_to_access_removed`.
5. `SignalRDeviceEventDeliveryServiceTests.cs` (thay đổi membership/role đi qua handler thật lấy từ `Access.Handlers()`: `AssignDeviceMembershipHandler` qua luật target-user nhờ `TestUserScopes`, `RemoveDeviceMembershipHandler` gỡ membership đang có (không cần `TestUserScopes`, Q13), `UpdateDeviceRoleHandler` kiểm vocabulary trên `PinnedCommandCatalog`; actor `Access.MasterAdmin(...)` là một user admin riêng, không phải user đang subscribe; `RevokeDeviceHandler` dựng trên cùng device/epoch store của fixture; deadline 3-4s như test cũ):
   - `Membership_only_member_receives_events_until_membership_removed` (đỏ: hiện hub trả `DEVICE_SCOPE_DENIED` và reconcile đuổi ngay vì không có org scope): member không `org-scope`, không resource grant, `DeviceEventsHub.JoinDevice` thành công, event D1 tới; `RemoveDeviceMembershipHandler` → đúng 1 `DeviceRevoked` reason `ACCESS_REMOVED`, registry trống, event publish sau đó không tới; join lại → `DEVICE_NOT_FOUND`.
   - `Membership_removal_evicts_with_access_removed_within_one_tick` (member `orgScoped`, `RemoveDeviceMembershipHandler`): `DeviceRevoked` reason `ACCESS_REMOVED`, registry trống, không có `DeviceEvent` publish sau đó.
   - `Role_edit_keeping_device_view_evicts_all_holders_with_authorization_changed` (`UpdateDeviceRoleHandler`, R `{device.view}` → `{device.view, adc.view}` với `expectedVersion` của R; R gán cho U1@D1, U2@D1, U3@D2, D1 và D2 cùng org của R): cả 3 nhận `AUTHORIZATION_CHANGED`; sau đó `DeviceEventsHub.JoinDevice` với epoch mới thành công cho cả 3, event kế tiếp tới đủ.
   - `Role_edit_removing_device_view_evicts_with_access_removed` (R → `{adc.view}`).
   - `Other_viewers_rejoin_after_membership_change_of_another_user` [RT7]: Y (membership riêng `{device.view}`) và một scope admin cùng xem D; thay role của X trên D rồi gỡ X qua handler thật → Y và scope admin nhận `AUTHORIZATION_CHANGED`, join lại với epoch mới thành công và nhận event kế tiếp.
   - `Fresh_assign_leaves_existing_viewers_subscribed` [RT7]: gán mới cho Z trên D qua `AssignDeviceMembershipHandler` → Y không nhận `DeviceRevoked`, registry còn subscription, event kế tiếp tới.
   - `Unrelated_device_keeps_streaming_after_policy_version_advance`: user subscribe A và B; gỡ membership trên A; event B gắn `PolicyVersion` trước khi advance → vẫn giao, không có `DeviceRevoked` cho B, registry còn B (đỏ: hiện bị bỏ ở :68).
   - `Device_revoke_evicts_with_device_revoked_reason` (`RevokeDeviceHandler`).
   - `Membership_removed_without_epoch_change_is_evicted_on_next_tick` (gỡ thẳng trong store): chứng minh access đọc live và fail-closed khi tín hiệu epoch sau commit bị lỡ hoặc crash giữa các bước của §4 [RT7, RT11].
   - `Scope_loss_evicts_scope_admin_with_access_removed` (Theory; deny ghi thẳng `InMemoryPermissionStore`, không bump epoch; khoá hành vi reconcile đọc live): deny resource org thiết bị (policy `DEVICE_NOT_FOUND`); deny feature `*` (mất `SeesAllInScope`, `DEVICE_PERMISSION_REQUIRED`) → đúng 1 `DeviceRevoked` reason `ACCESS_REMOVED`, registry trống.
   - `Feature_and_resource_denies_never_evict_master_admin` (khoá §3): master-admin (`MasterAdminPrincipal`, không membership, không resource grant) subscribe D1; ghi cùng hai deny như test trên (deny feature `*` + deny resource org D1, không bump epoch); publish event D1 sau khi ghi deny → event tới (chứng minh reconcile và kiểm lúc gửi đã chạy sau deny), không có `DeviceRevoked`, registry còn subscription.
   - `Principal_refresh_returning_null_notifies_resync_required` [RT6].
   - Sửa `Principal_refresh_exception_removes_membership_fail_closed` → đổi tên `Principal_refresh_failure_notifies_resync_required_and_removes_membership`: đúng 1 `DeviceRevoked` reason `REALTIME_RESYNC_REQUIRED`, không bao giờ `ACCESS_REMOVED`, registry trống (thay `Assert.Empty` :103) [RT6].
   - Sửa `Revoking_one_subscription_preserves_sibling_device_backlog`: thêm assertion reason = `REALTIME_RESYNC_REQUIRED`. Hai test cũ còn lại chỉ đổi phần seed và DI.
6. `RealtimeWiringTests.Device_events_hub_dependencies_resolve_from_application_container`: dùng `ManagementApiFactory`, trong một scope gọi `ActivatorUtilities.CreateInstance<DeviceEventsHub>` và `GetRequiredService<IDeviceAccessResolver>()` (scoped, phase 3 đăng ký). Thiếu đăng ký DI thì tick sẽ ném lỗi mỗi 100ms mà không ai thấy; test này bắt lỗi đó.
7. Chạy `dotnet test` và xác nhận đỏ (compile hoặc assertion) đúng các test mới hay sửa ở trên. Test ghi “khoá” có thể đã xanh (hành vi đã đúng từ trước, hoặc từ khi phase 3 đổi `ScopeAuthorization`); không ép chúng đỏ.

### B. Seams / infrastructure

1. Dùng `AccessActorFactory.FromPrincipal(ClaimsPrincipal, IOrganizationStore, IPermissionStore)` do phase 3 tạo (`src/Hms.Server.Api/Authorization/AccessActorFactory.cs`, §4). Chỉ một chỗ gọi, trong `RealtimeDeviceAuthorizer`; không tạo bản thứ hai trong Hubs. Chữ ký lệch §4 thì chỉ chỉnh chỗ gọi đó.
2. Xác nhận phase 3 đã `AddScoped<IDeviceAccessResolver, DeviceAccessResolver>()` (§4) và phase 1 có hằng `DevicePermissionCodes.DeviceView` (§1). Không hard-code chuỗi trong Hubs, không dùng `DeviceGate`.
3. Thêm `DeviceRevokedReasons` và đổi chữ ký `RealtimeEventFence.IsCurrent` trong `RealtimeContracts.cs`.

### C. Refactor / implement

1. Tạo `RealtimeDeviceAuthorizer` như Architecture.
2. Hub: thêm `IDeviceAccessResolver` vào ctor, viết lại `EnsureAuthorized`, xoá `using Hms.Server.Api.Authorization` nếu không còn dùng. Hub activation do DI lo, không sửa `Program.cs`.
3. Delivery: dựng authorizer theo tick; viết lại 3 điểm kiểm bằng `EvictionReason`. Enqueue: fence epoch-only, event cũ bỏ im lặng như cũ. Send: `EvictionReason ?? (fence ? null : AuthorizationChanged)`, rồi resync. `RefreshSubscriptionAsync` nhận `tick`, lỗi thì gọi `RevokeAndRemoveAsync(..., RealtimeResyncRequired)` [RT6]. `RevokeAndRemoveAsync` lấy epoch/`PolicyVersion` qua `tick`.
4. Absorb compile break trong phase: đổi chữ ký fence làm vỡ đúng 2 chỗ gọi trong delivery và 1 test (`RealtimeTests.cs:72-73`); ctor hub mới làm vỡ `DeviceEventsHubTests.cs:31-32`; bỏ default reason chỉ ảnh hưởng hàm private. Cả 3 file test được sửa ở bước A. Không có caller nào ngoài các file này (grep `RealtimeEventFence|AUTHORIZATION_REVOKED|DEVICE_SCOPE_DENIED` trong `src`/`tests` hiện chỉ trả về các file trên).
5. Dọn: grep `CanAccessDevice` trong `src`/`tests`; hết caller thì xoá hàm (thay đổi duy nhất trong `ScopeAuthorization.cs`), còn caller thì giữ.

### D. Tests After

1. Chạy toàn bộ test bước A, phải xanh.
2. Grep `src/Hms.Server.Api/Hubs` không còn `CanAccessDevice`, `DeviceGate`, `DEVICE_SCOPE_DENIED`, `AUTHORIZATION_REVOKED`. `RealtimeEventFence` không còn tham chiếu `PolicyVersion`.
3. Đọc lại 3 điểm kiểm: mọi nhánh đuổi khách đều truyền một hằng `DeviceRevokedReasons`.
4. Fixture: trong `tests/Hms.Server.UnitTests` chỉ một type implement `IUserScopeStore` (`TestUserScopes` trong `DeviceAccessTestFixture.cs`); `RealtimeAccessFixture.cs` không khai báo catalog hay double nào trùng fixture phase 3.

### Regression Gate

```bash
dotnet build Hms.Server.slnx
dotnet test Hms.Server.slnx --filter "FullyQualifiedName~DeviceEventsHubTests|FullyQualifiedName~SignalRDeviceEventDeliveryServiceTests|FullyQualifiedName~RealtimeDeviceAuthorizerTests|FullyQualifiedName~RealtimeTests|FullyQualifiedName~RealtimeWiringTests"
dotnet test Hms.Server.slnx
cd frontend && npm run typecheck && npm test && npm run build
```
Phải xanh, không đổi: `SignalRSubscriptionRegistryTests.cs`, `SignalRConnectionBacklogTests.cs`, `IdentityRealtimePrincipalRefresherTests.cs`, các test còn lại của `RealtimeTests.cs`, cộng toàn bộ test của các phase đã chạy trước (1-3; thêm 4 khi chạy cùng working tree), kể cả các test phase 3 dùng `DeviceAccessTestFixture` (phase này chỉ được thêm accessor vào fixture đó). Frontend không bị sửa; chạy để xác nhận đổi chuỗi reason trên dây không làm vỡ gì.

## Todo List

- [ ] Viết `RealtimeAccessFixture` trên `DeviceAccessTestFixture` (dùng lại `TestUserScopes`, `PinnedCommandCatalog`, `Handlers()`; không double `IUserScopeStore` thứ hai) và các test đỏ (Realtime, Hub, Authorizer, Delivery, Wiring), gồm member chỉ có membership và master-admin bị deny
- [ ] Seam: dùng `AccessActorFactory` của phase 3; xác nhận `IDeviceAccessResolver` scoped và hằng `DevicePermissionCodes.DeviceView`
- [ ] `DeviceRevokedReasons` + fence epoch-only trong `RealtimeContracts.cs`
- [ ] Tạo `RealtimeDeviceAuthorizer` (resolve live, chọn reason)
- [ ] Hub dùng authorizer, bỏ `DEVICE_SCOPE_DENIED`
- [ ] Delivery: authorizer theo tick, bảng reason, bỏ đuổi khách vì `PolicyVersion`, refresh lỗi gửi `REALTIME_RESYNC_REQUIRED`
- [ ] Xoá `CanAccessDevice` nếu `src` hết caller
- [ ] Sửa test cũ (hub 1, delivery 4, realtime 2)
- [ ] Regression gate xanh

## Success Criteria

- [ ] Ngoài scope và thiếu thiết bị cùng trả `DEVICE_NOT_FOUND` ở hub; trong scope thiếu `device.view` trả `DEVICE_PERMISSION_REQUIRED`; master-admin và scope admin vào được không cần membership; master-admin vào được cả khi bị deny feature `*` và deny resource org thiết bị.
- [ ] Member chỉ có membership (không resource grant, không `org-scope`) join và nhận event của đúng thiết bị đó; thiết bị khác cùng org trả `DEVICE_NOT_FOUND`; gỡ membership đó thì `DeviceRevoked` `ACCESS_REMOVED`.
- [ ] Gỡ membership thì `DeviceRevoked` `ACCESS_REMOVED` trong vòng reconcile kế tiếp; sửa role (giữ `device.view`) thì mọi holder trên mọi thiết bị nhận `AUTHORIZATION_CHANGED` và join lại được với epoch mới.
- [ ] Thay/gỡ membership của X làm viewer khác trên cùng thiết bị nhận `AUTHORIZATION_CHANGED`, join lại và nhận event kế tiếp; gán mới không đuổi ai [RT7].
- [ ] Revoke thiết bị thì `DEVICE_REVOKED`; backlog đầy hoặc hết retry thì `REALTIME_RESYNC_REQUIRED`; refresh principal lỗi thì `REALTIME_RESYNC_REQUIRED` rồi registry trống, không bao giờ `ACCESS_REMOVED` [RT6].
- [ ] Deny feature/resource không đuổi subscription master-admin (event sau deny vẫn tới, không có `DeviceRevoked`); cùng deny đó đuổi scope admin với `ACCESS_REMOVED`.
- [ ] Event thiết bị B vẫn được giao sau khi `PolicyVersion` tăng do thay đổi ở thiết bị A.
- [ ] Access đọc live ở mọi lần kiểm; không còn cache theo tick hay tham số `reread` [RT11].
- [ ] Hubs gọi thẳng `IDeviceAccessResolver` + `DeviceAccessPolicy` (không `DeviceGate`, không `CanAccessDevice`); `RealtimeWiringTests` xanh.
- [ ] Test realtime lấy handler thật từ `DeviceAccessTestFixture`; assembly `Hms.Server.UnitTests` chỉ có một double `IUserScopeStore` (`TestUserScopes`).
- [ ] `dotnet test Hms.Server.slnx` và frontend gate xanh.

## Risk Assessment

| Rủi ro | Giảm thiểu |
|---|---|
| Chữ ký `AccessActorFactory.FromPrincipal` (phase 3) lệch §4 | Chỉ một chỗ gọi trong `RealtimeDeviceAuthorizer`; sửa tại đó, không tạo bản thứ hai trong Hubs |
| Giữa phase 5 và 6, frontend coi mọi revoke là Revoked (`DeviceDetailView.vue:268-272`), nên `AUTHORIZATION_CHANGED` hiện nhầm | Không phát hành giữa phase 5 và 6 (phase 4–6 cùng một release, §5/§12); trong working tree chấp nhận tạm, vì trước phase này mọi đổi quyền cũng đuổi khách; phase 6 thêm join lại |
| Test delivery phụ thuộc thời gian, dễ flaky | Dùng `EventuallyAsync` + deadline 3-4s như test cũ, không đo ms; "một tick" được đảm bảo về cấu trúc (reconcile đầu tick, access đọc live) |
| Resolver ném lỗi thì cả tick dừng, không giao event cho ai | Fail-closed, không rò sự kiện; giữ hành vi hiện tại (lỗi `CanAccessDevice` cũng dừng tick); log error |
| Tải DB: build actor + refresher mỗi subscription mỗi tick | Mỗi lần kiểm chỉ thêm 2 lần đọc có index (membership theo unique, role theo PK); build actor xấp xỉ chi phí cũ; tick rảnh chỉ reconcile một lần mỗi subscription; cache actor/principal theo connection chỉ thêm khi đo thấy cần (YAGNI) [RT11] |
| Lỗi DB tạm thời khi refresh principal [RT6] | Gửi `REALTIME_RESYNC_REQUIRED`, không `ACCESS_REMOVED`: client hiện realtime disconnected, trang vẫn dùng được, không hiện màn mất quyền; join lại thủ công qua hub (phiên thật sự mất hiệu lực nhận `AUTHENTICATION_REQUIRED`); vẫn fail-closed. DB sập hẳn thì dựng payload cũng lỗi, việc gỡ im lặng như hiện nay |
| Mọi thay/gỡ membership hay sửa role đuổi mọi subscriber của thiết bị liên quan và bỏ event đang chờ của thiết bị đó [RT7] | Chấp nhận ở v1: chỉ có event `status` gộp được (bản mới nhất); epoch thô chạy đúng xuyên replica; gán mới không advance epoch; phase 6 join lại có retry giới hạn và tải lại telemetry; test khoá viewer khác join lại được |
| Event stale epoch còn kẹt sau Leave + Join nhanh làm đuổi khách một subscription hợp lệ | Hiếm; reason `AUTHORIZATION_CHANGED` cho phép join lại ngay |
| `DeviceAccessTestFixture` (phase 3) thiếu accessor hoặc đổi API mà fixture realtime cần | Thêm accessor public additive vào fixture đó (test phase 3 không đổi); không copy double `IUserScopeStore`/catalog sang fixture realtime, vì trùng tên type làm vỡ compile cả assembly |
| Người vận hành tưởng deny chặn được master-admin (hành vi cũ: deny resource đuổi master-admin khỏi stream) | Có chủ đích theo Q4/§3; deny nhắm master-admin bị grant handler từ chối (phase 3); chặn master-admin bằng endpoint gỡ role (Q11), có hiệu lực trong một tick nhờ principal refresh (stamp đổi → `REALTIME_RESYNC_REQUIRED`); test khoá cả hai phía (master-admin giữ stream, scope admin bị đuổi) [RT1] |

## Security Considerations

- Không lộ sự tồn tại thiết bị: thiếu và ngoài scope cùng một code; actor dựng trước khi tra thiết bị; revoked ngoài scope vẫn là `DEVICE_NOT_FOUND` (thứ tự policy §3). Membership là WHERE grant cho đúng một thiết bị: member chỉ có membership chỉ biết thiết bị của mình (role thiếu `device.view` → `DEVICE_PERMISSION_REQUIRED`), thiết bị khác cùng org vẫn `DEVICE_NOT_FOUND`, gỡ membership thì thiết bị trở lại `DEVICE_NOT_FOUND`.
- Fail-closed ở mọi nhánh: không dựng được actor, không thấy thiết bị, refresh lỗi, resolver lỗi đều dẫn tới không giao event hoặc đuổi khách.
- `DeviceAccess` (membership/role/scope), trạng thái revoked và epoch đều đọc live ở mọi lần kiểm, nên gỡ quyền, revoke hoặc advance epoch giữa tick vẫn chặn được lúc gửi [RT11].
- Master-admin lấy từ principal được refresh mỗi tick (role và stamp đọc lại từ Identity). Endpoint gỡ role của phase 4 (Q11) đổi stamp, nên ở tick kế tiếp refresher trả `null` → `REALTIME_RESYNC_REQUIRED` và join lại bị từ chối. Deny feature/resource không chặn master-admin (§3, Q4), nên endpoint gỡ role là cách chặn một master-admin; revoke thiết bị vẫn chặn mọi người [RT1, RT6].
- Payload `DeviceRevoked` không chứa role/permission. Mọi event vẫn chỉ cần `device.view` (v1); map quyền theo loại event là việc của plan transport `261001-0030`.
- Xuyên replica: đuổi khách dựa vào epoch DB + reconcile, không dựa vào registry cục bộ; không có backplane (§10).

## Open Questions

1. §7 không nói gì về `RevalidateDevice`; phase này giữ `DEVICE_REAUTHORIZATION_REQUIRED` (che code cụ thể). Phase 6 cần code chi tiết thì dùng `JoinDevice`. Xác nhận chấp nhận.
2. ~~V5~~ Đã chốt (Validation Session 1, 2026-10-05): chặn xoá vai trò khi còn membership, nên không có đường đuổi khách từ xoá vai trò và không cần test delivery thêm. V1–V4, V6 (đã chốt) không đổi hành vi realtime. <!-- Updated: Validation Session 1 - V5 confirmed -->

## Next Steps

- Phase 6 dùng: reason trên dây `DEVICE_REVOKED` (đánh dấu Revoked), `ACCESS_REMOVED` (đánh giá truy cập bị từ chối → trạng thái mất quyền), `AUTHORIZATION_CHANGED` (refetch epoch + join lại, thử lại có giới hạn khi `STALE_AUTHORIZATION_EPOCH`, rồi tải lại telemetry) [RT7], `REALTIME_RESYNC_REQUIRED` (giữ như cũ: realtime disconnected, join lại thủ công; nay gồm cả refresh principal lỗi, kể cả phiên mất hiệu lực) [RT6]. Hub error `DEVICE_NOT_FOUND` / `DEVICE_REVOKED` / `DEVICE_PERMISSION_REQUIRED` / `STALE_AUTHORIZATION_EPOCH` / `AUTHENTICATION_REQUIRED`; Revalidate vẫn `DEVICE_REAUTHORIZATION_REQUIRED`. `DEVICE_SCOPE_DENIED` và `AUTHORIZATION_REVOKED` không còn. Payload `DeviceRevoked` giữ nguyên hình `{deviceId, authorizationEpoch, policyVersion, reason}`. Fixture `'AUTHORIZATION_REVOKED'` (`useDeviceOperationsHub.test.ts:100`, chuyển sang `core/realtime/` theo §9) nên đổi sang reason mới.
- Phase 6 lưu ý: server bỏ đuổi khách vì `PolicyVersion` nhưng vẫn gắn `PolicyVersion` (đơn điệu) vào event và `DeviceRevoked`. So sánh `policyVersion` phía client ở `frontend/src/core/realtime/realtimeAuthorization.ts:22-25` (bỏ event cùng epoch có `policyVersion` thấp hơn) có thể giữ: nó chỉ sắp thứ tự trong một epoch.
- Phase 6 lưu ý: server không đuổi master-admin vì deny feature/resource (§3), khớp bypass `isMasterAdmin` vô điều kiện phía client (§9).
- Plan `261001-0030` (transport): thêm loại event mới thì gắn quyền theo loại event tại `QueueAuthorizedEventAsync` qua `RealtimeDeviceAuthorizer`. Event kết quả lệnh không gộp được như `status`, nên sau `AUTHORIZATION_CHANGED` client cần resync/snapshot (ví dụ đọc lại trạng thái lệnh qua HTTP), không chỉ dựa vào giao event có fence epoch — event trong khoảng evict → join lại bị bỏ [RT7].
