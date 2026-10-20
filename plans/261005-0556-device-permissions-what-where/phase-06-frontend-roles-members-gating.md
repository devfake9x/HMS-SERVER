---
phase: 6
title: "Frontend: vai trò, thành viên, gate theo thiết bị"
status: pending
priority: P1
dependencies: [4, 5]
---
# Phase 6: Frontend: vai trò, thành viên, gate theo thiết bị

## Context Links
- Hợp đồng: [design-contract.md](./reports/design-contract.md) §0 Q11–Q13, §9 (toàn bộ), DTO §6 (HTTP), §7 (reason realtime), §3 (thứ tự `DeviceAccessPolicy`; master-admin vô điều kiện, deny không áp lên master-admin; feature allow `*` không cấp gì, deny `*` chặn mọi non-master), §4 (mã lỗi, luật uỷ quyền, `canEdit`), §5 (`CommandPermissionResolver`, `CONFIRM_REQUIRED`, `PERMISSION_UNMAPPED`), §10 (ngoài phạm vi), §11 (validation V1–V6), §12 (thứ tự chạy, phase 4–6 cùng một release).
- Scout: [scout-frontend-surface-map.md](./reports/scout-frontend-surface-map.md), [scout-realtime-surface-map.md](./reports/scout-realtime-surface-map.md).
- Brainstorm: `plans/reports/261004-2234-brainstorm-device-permissions-what-where.md` §5.7.
- Docs: `docs/architecture-device-permissions-parity.md` §5 (UI). Phase 4 (HTTP) viết lại các mục ngoài §5 và chỉ để lại một dòng trỏ ở đầu §5; phase này viết lại toàn bộ §5 và thay dòng trỏ đó (C7).

## Overview
Đưa frontend lên mô hình what+where: gom module trùng về `frontend/src/core/**` (kể cả bộ ba realtime), client API có lỗi kiểu (status + `code`) cùng map mã lỗi phủ mọi mã UI có thể nhận, helper quyền fail-closed (master-admin vô điều kiện như server → deny → allow exact; allow `*` không cấp gì), trang Vai trò thiết bị, tab Thành viên thay ma trận 4 quyền, gate tab/nút/lệnh theo `my-access`, hub phân biệt reason, và viết lại phần UI của doc kiến trúc. Server vẫn là thẩm quyền cuối; UI chỉ ẩn/khoá.

## Key Insights
- 6 module trùng còn sống: `frontend/src/adminApi.ts` (App.vue:4, PermissionGrantEditor.vue:3), `permissions.ts` (App.vue:7), `commandAvailability.ts` (App.vue:6) và bộ ba realtime `useDeviceOperationsHub.ts` (App.vue:12), `signalRConnection.ts` (App.vue:13), `realtimeAuthorization.ts`. Bộ ba import theo chuỗi `signalRConnection` → `useDeviceOperationsHub` → `realtimeAuthorization`, nên chỉ xoá được cùng nhau. Bản core giống hệt hoặc là superset: core adminApi thêm telemetry (core/api/adminApi.ts:102-116, 213-214), core permissions thêm hằng `PERMISSION`/`ROLE`, commandAvailability chỉ khác dòng import (dòng 1). Test chỉ phủ bản top-level (adminApi.test.ts:2, permissions.test.ts:2, commandAvailability.test.ts:2, useDeviceOperationsHub.test.ts:8, signalRConnection.test.ts:2, realtimeAuthorization.test.ts:2) và đều import tương đối `./X`. App.test.ts chỉ mock `./adminApi` (dòng 21) và `./signalRConnection` (dòng 28), không import trực tiếp. PermissionGrantEditor.test.ts vừa import `{ adminApi } from './adminApi'` (dòng 5) vừa mock cùng đường (dòng 7); factory mock còn `grantDeviceAction: vi.fn()`, fixture `snapshot` (dòng 8) còn `deviceActionGrants`. Xoá `adminApi.ts` top-level mà sót dòng 5 thì vitest không resolve được module và vue-tsc đỏ. Thư mục `src/core` chưa có test nào. `catalogVerification.ts` top-level không import module nào sắp bị xoá.
- `tsconfig.json` include `src/**/*.ts`, nên `vue-tsc --noEmit` (`npm run typecheck`, cũng là bước đầu của `npm run build`) kiểm cả `*.test.ts`. Test đỏ import symbol hoặc file chưa có sẽ giữ typecheck đỏ cho tới khi code xong.
- `request()` ném `Error('Request failed (N)')`, bỏ mất body (core/api/adminApi.ts:156). Đã có `ApiError`/`ForbiddenError`/`toApiError`/`parseErrorBody` (core/http/errors.ts:34-63, 96-147) nhưng chưa ai dùng. Code nằm ở `body.raw.code`.
- `can()` coi allow `'*'` là master-admin (core/auth/permissions.ts:69), chưa biết cờ `isMasterAdmin` và xét deny trước mọi thứ. `canDeviceAction` có đường wildcard `device:{id}:action:*` (72-81), chỉ test dùng. Theo contract §3/§6/§9: server bỏ allow `*` cho feature; deny `*` vẫn chặn mọi non-master-admin; master-admin vô điều kiện (deny không áp lên master-admin; muốn hạn chế thì master-admin khác gỡ role qua `DELETE /api/v1/users/{userId}/roles/master-admin` của phase 4, Q11 [RT1]); `/me/permissions` thôi append `*` và trả thêm `userId`, `isMasterAdmin`. Frontend đổi theo: bypass `isMasterAdmin` đứng trước deny. Trong `src`, chỉ `permissions.test.ts:18` còn dựa vào allow `*`.
- Catalog DTO hiện có `featurePermission`, và `dangerous` được dùng lại làm `requiresConfirmation` (MeController.cs:63-66). Enum serialize ra tên (`JsonStringEnumConverter`, Program.cs:28-29), nên `class` sẽ là `"Flash"`, `"Read"`... Theo contract §6, `permissionByDomain` là `{ [domain]: permission }` và server đã resolve sẵn theo class của action.
- Modal lệnh gate theo snapshot global (DeviceCommandModal.vue:73-76). Nó đoán nguy hiểm theo tên (66-71, bỏ sót `ota`). Nó ép `!bg-orange-500` cả khi danger (335-341). Có bug `canSubmit` so `!== undefined` trong khi `findCatalogAction` trả `null` (125-127). Kết quả 200 luôn hiện `Command Dispatched Successfully` kèm `Code` (gán ở dòng 166, khối `command-success-result` dòng 306-320), kể cả mã từ chối mà contract §5/§6 trả 200 + body (`CONFIRM_REQUIRED`, `PERMISSION_UNMAPPED`).
- DeviceDetailView lấy device bằng `listDevices('').find` và chỉ log lỗi (282-317). View không watch `route.params.id` (63-67), và mọi tab đều không gate (493-582, 476-486, 903, 964-974, 1050-1056). `onRevoked` gán `Revoked` với mọi reason (268-272). Trạng thái thiết bị chỉ có ở `DeviceDto.state` của list (badge 430-433); `my-access` không trả state.
- Hub chuyển `reason` qua `onRevoked` nhưng không rẽ nhánh theo nó. Sau khi revoke, hub không tự join lại (useDeviceOperationsHub.ts:110-121, 228-240). `joinDevice` chỉ gỡ `requiresReauthorization` khi lần join bắt đầu trong trạng thái reauth (231, 235-239), nên có thể tái dùng cho auto re-join. Fence client bỏ event cùng epoch có `policyVersion` thấp hơn (realtimeAuthorization.ts:22-25). Server vẫn gắn `policyVersion` vào event (phase 5 giữ `AuthorizedDeviceEvent.PolicyVersion`), nên phép so này chỉ xếp thứ tự event trong một epoch của từng thiết bị. Test `dispatches DeviceEvent and tears down pending work on DeviceRevoked` dùng fixture `reason: 'AUTHORIZATION_REVOKED'` (useDeviceOperationsHub.test.ts:100). Đây là giá trị wire cũ (mặc định ở SignalRDeviceEventDeliveryService.cs:214) mà phase 5 thay bằng `DEVICE_REVOKED`/`ACCESS_REMOVED`/`AUTHORIZATION_CHANGED`. Hub chưa rẽ nhánh theo reason, nên đổi fixture sang reason mới vẫn xanh trên code hiện tại. Join lại đúng một lần với `.catch(noop)` sẽ kẹt: `STALE_AUTHORIZATION_EPOCH` khi join lại để device nằm trong `requiresReauthorization` (useDeviceOperationsHub.ts:119, 235), App.vue bỏ qua join cho device đang bị đánh dấu (App.vue:257), view vẫn báo "connected" → cần thử lại có giới hạn và báo consumer khi hết lượt [RT7].
- App.vue: `commandCanSubmit` dùng `featurePermission` (App.vue:125). `grant('device')` gọi `grantDeviceAction` (680-691, nhánh 687), refs ở 100-101 và 453-454. `onRevoked` xoá device khỏi list với mọi reason (148-152).
- PermissionGrantEditor có form device-action dạng free-text, gõ được `*` (PermissionGrantEditor.vue:102-118, 137-139, 151-156). Lỗi grant chỉ hiện câu chung (80, 98), nên mã của grant handler (`WILDCARD_GRANT_FORBIDDEN`/`GRANT_TARGET_IS_MASTER_ADMIN` 400, `SELF_GRANT_FORBIDDEN`/`GRANT_EXCEEDS_OWN_PERMISSIONS`/`GRANT_OVERRIDES_MASTER_DECISION` 403, contract §4) không tới người dùng [RT1, RT13]. RolePermissionView không bao giờ gán CSRF (RolePermissionView.vue:23, 131) và hardcode `:allowed="true"` (130).
- Server tính `canEdit` của memberships GET bằng cùng predicate với handler. Dòng của chính actor chỉ có `canEdit: true` khi actor là master-admin (contract §4: target ≠ actor trừ master-admin; §9). UI khớp theo: dòng của mình chỉ đọc, trừ master-admin. Server thêm `canRemove`: member org khác/không org trên thiết bị của org mình có `canEdit=false`, `canRemove=true` (Q13) [RT14]; role trả kèm `version` để gửi `expectedRoleVersion` [RT3].
- `DevicePermissionsTab` hiện có tiêu đề + mô tả cho 4 quyền; `device-permission-codes` mới trả `label` và `commands` (lệnh mà mỗi mã chặn) — editor hiển thị cả hai [RT12].
- `UserManagementView` (router mode) có cột Roles và cột Actions nhưng không có thao tác role nào; gán role chỉ có ở template legacy của App.vue (không render ở router mode). Cột Roles đang đọc `item.role || 'Operator'` trong khi DTO có `roles: string[]` (UserManagementView.vue:167-170) → nút gỡ role (Q11) đặt ở cột Actions, sửa luôn cột Roles [RT1].
- Route/nav: `/admin/roles-permissions` nằm ở routes.ts:72-77, nav group admin ở app/nav.config.ts:68-92. `NavLink.permission` đã có (shared/navigation/nav.ts:40), `RouteMeta.access` đã khai (shared/navigation/route-meta.d.ts:20). AppShell mặc định subject SUPER_ADMIN (AppShell.vue:39), nên link vẫn hiện với mọi người; wiring subject nằm ngoài phạm vi (§10).
- `Select` có chế độ `native` và option `disabled` (shared/ui/Select.vue:24-40, 253-262). `Button` primary là orange (shared/ui/primitives/Button.vue:29).

## Requirements
**Functional**
1. Chỉ dùng `core/**`. Xoá cùng lúc 6 module top-level: `adminApi`, `permissions`, `commandAvailability` và bộ ba realtime `useDeviceOperationsHub`, `signalRConnection`, `realtimeAuthorization`. Repoint App.vue (kèm mock trong App.test.ts) và PermissionGrantEditor.vue (kèm import dòng 5 và mock dòng 7 trong PermissionGrantEditor.test.ts; factory mock bỏ `grantDeviceAction`). Test của 6 module chuyển sang cạnh bản core. Riêng test hub đổi fixture reason cũ `AUTHORIZATION_REVOKED` sang 3 reason mới, mỗi reason có kỳ vọng riêng.
2. `request()` ném `ApiError` (status, body, `code`). Thêm 10 method (§9, gồm `removeRole` [RT1]), DTO dùng đúng tên §6: memberships GET `{deviceId, members, roles}` (member có `canEdit`, `canRemove`; role có `version`) [RT3, RT14], vai trò `{roleId, organizationId, name, description, permissions, version}`, mã quyền `{code, group, highRisk, label, commands}` [RT12], mọi id là string. Bỏ `devicePermissions`, `updateDevicePermissions`, `grantDeviceAction` cùng type ma trận và `DeviceActionGrantDto`.
3. `can()` theo contract §9: snapshot phải hợp lệ và còn hạn (điều kiện tiên quyết, fail-closed), rồi bypass khi `isMasterAdmin === true` (vô điều kiện, giống server: deny không áp lên master-admin) → deny (exact hoặc `*`) → allow exact. Allow `*` không cấp gì; deny `*` chặn mọi non-master. Thêm `canOnDevice(access, code)` fail-closed, không nhận `*`. Bỏ `canDeviceAction`.
4. Trang `/admin/device-roles` (name `admin-device-roles`) gồm: chọn org, danh sách vai trò, tạo/sửa với picker gom theo `group` (mỗi mã hiện `label` và các lệnh nó chặn [RT12]), quyền high-risk khoá nếu không phải master-admin, xử lý 409 `expectedVersion`, thông báo `DEVICE_ROLE_IN_USE`. Nav link có `permission: 'permissions.manage'`.
5. `DeviceMembersTab` thay `DevicePermissionsTab`: mỗi user một select vai trò; dòng của chính mình chỉ đọc, trừ khi người xem là master-admin (khớp `canEdit` của server, contract §4/§9); vai trò không gán được thì disabled kèm lý do; trước khi lưu hiện danh sách quyền của vai trò đang chọn (gom nhóm, badge high-risk) và gửi `expectedRoleVersion`, 409 `DEVICE_ROLE_VERSION_STALE` → banner "vai trò đã đổi" + reload [RT3]; dòng `canRemove && !canEdit` (member org khác) chỉ có nút Gỡ (Q13) [RT14]; banner riêng cho 404/403/409; CSRF + `expectedRoleId`.
6. DeviceDetailView: fetch `myDeviceAccess` (watch id), gate tab/nút, có trạng thái riêng cho not-found, forbidden, revoked và access-removed. Thiết bị Revoked trong phạm vi trả my-access 200 với `permissions` rỗng, view hiển thị trạng thái revoked. Truyền `access` vào modal; `onRevoked` rẽ nhánh theo reason; hub join lại xong thì tải lại telemetry, hết lượt thử thì `realtimeStatus='disconnected'` [RT7].
7. DeviceCommandModal: gate theo `requiredPermission`. Dòng domain-policy lấy `payload.domain` (chỉ nhận string) trong JSON params rồi tra `permissionByDomain[domain]`: so khớp chính xác, phân biệt hoa thường, không rẽ nhánh theo `class`. Danger lấy từ `class`/`highRisk`. Có ô confirm khi `requiresConfirmation` và gửi kèm `confirm`. Kết quả 200 mang mã có trong map lỗi (`CONFIRM_REQUIRED`, `PERMISSION_UNMAPPED`) hiện là lỗi kèm câu của map, không hiện "Dispatched".
8. Hub: `AUTHORIZATION_CHANGED` → tự lấy epoch mới và join lại, tối đa 3 lần thử (tính cả lần đầu) với backoff có jitter (250 ms–2 s) giữa các lần, thử lại khi `STALE_AUTHORIZATION_EPOCH` hoặc lấy epoch lỗi tạm; dừng khi `DEVICE_NOT_FOUND`/`DEVICE_PERMISSION_REQUIRED`/`DEVICE_REVOKED`, đổi hub/phiên, hoặc `leaveDevice`; thành công → `onRejoined(deviceId)`; hết lượt → giữ `requiresReauthorization` và gọi `onRejoinFailed(deviceId, code)` [RT7]. `ACCESS_REMOVED` (server chỉ gửi khi đánh giá truy cập từ chối, không gửi khi refresh phiên lỗi) và `DEVICE_REVOKED` → bỏ device, consumer hiển thị đúng trạng thái. `REALTIME_RESYNC_REQUIRED` giữ hành vi cũ: bỏ device, chờ reauth thủ công; server nay cũng gửi reason này khi refresh principal lỗi, nên trang vẫn dùng được [RT6]. `realtimeAuthorization.ts` giữ nguyên phép so `policyVersion`.
9. `core/auth/deviceAccessErrors.ts` map đủ 22 mã UI có thể nhận (contract §4/§5/§6, gồm `GRANT_TARGET_IS_MASTER_ADMIN`, `GRANT_OVERRIDES_MASTER_DECISION` [RT1, RT13]) sang câu tiếng Việt, có fallback, kèm test liệt kê từng mã. Dùng chung ở DeviceRolesView, DeviceMembersTab, DeviceCommandModal và lỗi grant của PermissionGrantEditor.
10. Viết lại toàn bộ phần UI (§5) của `docs/architecture-device-permissions-parity.md` theo mô hình mới, thay dòng trỏ mà phase 4 để lại ở đầu §5.
11. `UserManagementView`: master-admin (cờ `isMasterAdmin` của `/me/permissions`) thấy nút gỡ cho từng role đã gán, có `DangerConfirmDialog`; gọi `removeRole` kèm CSRF rồi reload; lỗi `LAST_MASTER_ADMIN` (body `{errors}`) hiện câu riêng, lỗi khác hiện câu chung. Cột Roles hiện `item.roles` [RT1].

**Non-functional**
- Fail-closed ở mọi chỗ: không có access, payload lỗi hoặc snapshot hết hạn đều trả false. Không thêm store hay framework guard (YAGNI). Nút primary mới dùng `Button variant="primary"`. Khi xong phase, `npm run typecheck`, `npm test` và `npm run build` phải xanh. Typecheck đỏ có chủ đích từ A1 tới C6 (xem B).

## Architecture
```
DeviceDetailView ──myDeviceAccess(id)──▶ DeviceAccessDto ──┬─▶ canOnDevice / canIssueAnyCommand (tab, nút)
   │  permissions() (feature, userId, isMasterAdmin)       ├─▶ DeviceCommandModal(:access)
   │                                                        └─▶ DeviceMembersTab(:access, :current-user-id)
   └─ useDeviceOperationsHub(onRevoked: reason, onRejoined, onRejoinFailed) ── AUTHORIZATION_CHANGED → joinDevice (epoch mới, tối đa 3 lần thử) + refetch access; onRejoined → refetch telemetry; onRejoinFailed → realtime disconnected [RT7]
UserManagementView ─▶ listUsers / permissions (isMasterAdmin) ─▶ removeRole(userId, role) [RT1]
DeviceRolesView ─▶ listOrganizations / listDeviceRoles(org) / devicePermissionCodes / permissions ─▶ DeviceRoleEditor
```
```ts
// core/http/errors.ts — ApiError
get code(): string | undefined // body.kind==='json' && typeof raw.code==='string'
// core/api/adminApi.ts — request(): if (!response.ok) throw toApiError(response.status, await parseErrorBody(response), endpoint)
// DTO theo contract §6; mọi id là string (server gửi Guid trần, không bọc {value})
export interface DeviceAccessDto { deviceId: string; isMasterAdmin: boolean; seesAllInScope: boolean; canManageAccess: boolean; roleId: string | null; roleName: string | null; permissions: string[] } // thiết bị Revoked trong phạm vi: 200, permissions []
export interface DevicePermissionCodeDto { code: string; group: string; highRisk: boolean; label: string; commands: string[] } // [RT12]
export interface DeviceRoleDto { roleId: string; organizationId: string; name: string; description: string | null; permissions: string[]; version: number }
export interface DeviceMembershipsDto { deviceId: string; members: Array<{ userId: string; displayName: string; email: string; roleId: string | null; roleName: string | null; canEdit: boolean; canRemove: boolean }>; roles: Array<{ roleId: string; name: string; permissions: string[]; version: number; assignable: boolean }> } // canRemove [RT14], version [RT3]
export type CommandClass = 'Read' | 'Write' | 'Control' | 'Destructive' | 'Physical' | 'Flash'
export interface CommandCatalogAction { type: string; action: string; available: boolean; class: CommandClass; requiredPermission: string | null; permissionByDomain: Record<string, string> | null; requiresConfirmation: boolean; dryRunSupported: boolean; ttlMs: number; highRisk: boolean }
// permissionByDomain = { [domain]: permission }, server đã resolve theo class của action (cột config-domain của §11 mà action đó dùng)
// PermissionSnapshot += userId: string; isMasterAdmin: boolean. PermissionGrantSnapshot -= deviceActionGrants. sendCommand body += confirm?: string
listDeviceRoles(orgId) GET /api/v1/organizations/{orgId}/device-roles (no-store)
createDeviceRole(orgId, {name, description, permissions}) POST · updateDeviceRole(orgId, roleId, {...,expectedVersion}) PUT
deleteDeviceRole(orgId, roleId, expectedVersion) DELETE ?expectedVersion= · devicePermissionCodes() GET /api/v1/device-permission-codes
deviceMemberships(deviceId) GET /api/v1/devices/{id}/memberships (no-store) · myDeviceAccess(deviceId) GET .../my-access (no-store)
assignDeviceMembership(deviceId, userId, {roleId, expectedRoleId, expectedRoleVersion}) PUT .../memberships/{userId}   // [RT3]
removeDeviceMembership(deviceId, userId, expectedRoleId) DELETE .../memberships/{userId}?expectedRoleId=
removeRole(userId, role) DELETE /api/v1/users/{userId}/roles/{role}   // 204; lỗi 400 {errors} [RT1]
// core/auth/permissions.ts — mirror contract §3/§9
export function can(snapshot: PermissionSnapshot | null | undefined, permission: string, now?: Date): boolean
//   snapshot thiếu/hết hạn/sai hình → false (điều kiện tiên quyết, không phải luật quyền);
//   isMasterAdmin === true → true (vô điều kiện, deny không áp, giống server);
//   deniedPermissions chứa permission hoặc '*' → false; còn lại permissions.includes(permission) (allow '*' không cấp gì)
export const DEVICE_VIEW = 'device.view'
export function canOnDevice(access: DeviceAccessDto | null | undefined, code: string): boolean
//   !access||!code → false; isMasterAdmin===true → true; code===DEVICE_VIEW && seesAllInScope===true → true;
//   Array.isArray(permissions) && permissions.includes(code)   (không có nhánh '*')
export function canManageDeviceAccess(access: DeviceAccessDto | null | undefined): boolean // isMasterAdmin===true || canManageAccess===true
// core/commands/commandAvailability.ts — mirror CommandPermissionResolver §5
export function resolveCommandPermission(a: CatalogAction, payload: Record<string, unknown>): string | null
//   a.requiredPermission ?? (typeof payload.domain === 'string' ? a.permissionByDomain?.[payload.domain] ?? null : null)
//   không rẽ nhánh theo class; chỉ đọc own key (Object.hasOwn), nên domain 'constructor'/'__proto__' → null
export function isDangerousCommand(a): boolean   // class Flash|Destructive || highRisk
export function requiresCommandConfirmation(a): boolean // a.requiresConfirmation
export function canIssueAnyCommand(access: DeviceAccessDto | null | undefined, actions: readonly CommandCatalogAction[]): boolean // quyền ứng viên = requiredPermission hoặc mọi giá trị permissionByDomain; true khi canOnDevice với ít nhất một
// core/realtime/useDeviceOperationsHub.ts
export const DEVICE_REVOKED_REASON = { DEVICE_REVOKED, ACCESS_REMOVED, AUTHORIZATION_CHANGED, REALTIME_RESYNC_REQUIRED } as const
// options += onRejoined?(deviceId), onRejoinFailed?(deviceId, code)   [RT7]
// onDeviceRevoked: giữ teardown + onRevoked; nếu reason===AUTHORIZATION_CHANGED && wasSubscribed → rejoinWithRetry(id):
//   tối đa 3 lần thử (tính cả lần đầu), chờ jitter 250 ms–2 s giữa các lần; thử lại khi STALE_AUTHORIZATION_EPOCH hoặc lấy epoch lỗi tạm;
//   dừng khi DEVICE_NOT_FOUND | DEVICE_PERMISSION_REQUIRED | DEVICE_REVOKED, hub/phiên đổi, hoặc leaveDevice(id);
//   thành công → onRejoined(id); hết lượt → giữ requiresReauthorization + onRejoinFailed(id, code); không unhandled rejection
// REALTIME_RESYNC_REQUIRED và reason lạ: giữ hành vi cũ (bỏ device, requiresReauthorization, chờ reauth thủ công)
// core/realtime/realtimeAuthorization.ts: không sửa — so policyVersion chỉ để xếp thứ tự event trong cùng epoch của một thiết bị
```
- `core/auth/deviceAccessErrors.ts` map mã sang câu tiếng Việt cho mọi mã UI có thể nhận (contract §4/§5/§6), lấy từ `ApiError.code` hoặc từ `code` của kết quả lệnh 200:
  ```ts
  export const DEVICE_ACCESS_ERROR_CODES: readonly string[]          // đúng 22 mã dưới đây
  export function deviceAccessMessage(code: unknown): string | null  // tra bằng Map nên chỉ own key: mã lạ, 'constructor', '__proto__' → null
  export function deviceAccessErrorMessage(error: unknown, fallback?: string): string
  //   ApiError có code trong map → câu của map; còn lại → fallback, không có thì HttpError.userMessage, rỗng thì 'Không thực hiện được yêu cầu.'
  ```
  - Thiết bị (§3, §6): `DEVICE_NOT_FOUND`, `DEVICE_REVOKED`, `DEVICE_PERMISSION_REQUIRED`.
  - Membership và uỷ quyền (§4): `PERMISSION_MANAGEMENT_REQUIRED`, `SELF_GRANT_FORBIDDEN`, `HIGH_RISK_REQUIRES_MASTER_ADMIN`, `GRANT_EXCEEDS_OWN_PERMISSIONS`, `GRANT_TO_DELEGATOR_FORBIDDEN` (không thao tác lên người đã bổ nhiệm mình, contract Q18), `USER_NOT_FOUND`, `DEVICE_MEMBERSHIP_CHANGED`.
  - Grant feature (contract Q16): master-admin gỡ một deny `*` bằng `{permission: "*", deny: false}`; nếu UI có thao tác này thì gọi là "Gỡ chặn toàn bộ", không phải "cấp tất cả" (dòng đó không cấp quyền nào).
  - Vai trò (§3, §4): `DEVICE_ROLE_NOT_FOUND`, `DEVICE_ROLE_ORGANIZATION_MISMATCH`, `DEVICE_ROLE_INVALID`, `DEVICE_PERMISSION_UNKNOWN`, `DEVICE_ROLE_NAME_TAKEN`, `DEVICE_ROLE_VERSION_STALE`, `DEVICE_ROLE_IN_USE`, `ORGANIZATION_NOT_FOUND`.
  - Grant feature/resource (§4): `WILDCARD_GRANT_FORBIDDEN`, `GRANT_TARGET_IS_MASTER_ADMIN`, `GRANT_OVERRIDES_MASTER_DECISION` [RT1, RT13].
  - Kết quả lệnh 200 + body (§5): `CONFIRM_REQUIRED`, `PERMISSION_UNMAPPED`.
  - Dùng chung ở DeviceRolesView, DeviceMembersTab, DeviceCommandModal (lỗi HTTP và kết quả 200) và PermissionGrantEditor (lỗi grant). Không import chéo feature (DRY).
- DeviceDetailView: `accessState: 'loading'|'ready'|'revoked'|'not-found'|'forbidden'|'access-removed'|'error'`. Bước 1 chạy song song `getCsrf`, `myDeviceAccess` và `listDevices('')`; list đã được server lọc theo phạm vi nên không lộ thêm gì. Kết quả:
  - 404 → not-found. 403 → forbidden. Lỗi khác, kể cả list lỗi → error.
  - 200 và device trong list có `state === 'Revoked'` → revoked. Đây là trường hợp contract §6 trả 200 với `permissions` rỗng; server từ chối mọi route theo thiết bị bằng `DEVICE_REVOKED`, kể cả với master-admin.
  - 200 nhưng `!canOnDevice(access, DEVICE_VIEW)` → forbidden.
  - Revoked và forbidden đều dừng ở đây: không gọi telemetry/catalog, không join hub.
  - Ready → `Promise.all` (permissions, catalog, telemetry) rồi `joinRealtime()`.
  - Khi `watch(deviceId)` đổi: `leaveDevice(cũ)`, reset state, `sessionFence.rotate()`, chạy lại. Tab đang mở mà bị ẩn thì về `telemetry`.
- Gate tab: commands và nút C2/Dispatch theo `canIssueAnyCommand`; provisioning theo `can(snapshot,'provisioning.manage')`; licenses theo `can(snapshot,'licenses.manage')`; Thành viên (giữ key `permissions` để deep link `/devices/:id/permissions` còn chạy) theo `canManageDeviceAccess`; telemetry và audit theo `device.view`.
- `onRevoked`:
  - `DEVICE_REVOKED` → `device.state='Revoked'`, `accessState='revoked'`, đóng modal.
  - `ACCESS_REMOVED` → `access-removed`, xoá access, đóng modal (server chỉ gửi khi đánh giá truy cập từ chối, không gửi khi refresh phiên lỗi) [RT6].
  - `AUTHORIZATION_CHANGED` → `refreshAccess()`; hub tự join lại. `onRejoined` → tải lại telemetry (bù event bị bỏ giữa lúc evict và join lại); `onRejoinFailed` → `realtimeStatus='disconnected'` [RT7].
  - Reason khác, gồm `REALTIME_RESYNC_REQUIRED` → `realtimeStatus='disconnected'`, giữ hành vi thủ công như cũ; trang và tab vẫn dùng được (server gửi reason này cả khi refresh principal lỗi) [RT6].
- DeviceMembersTab: đọc `members`/`roles` từ `{deviceId, members, roles}`. Một dòng sửa được khi `row.canEdit && (access?.isMasterAdmin === true || (currentUserId !== null && row.userId !== currentUserId))`, khớp `canEdit` của server (contract §4/§9): master-admin sửa được cả dòng của mình; non-master không bao giờ sửa dòng của mình, kể cả khi `canEdit` lỡ là true; non-master chưa biết `currentUserId` thì mọi dòng khoá. Option vai trò là `{value: roleId, label, disabled: !assignable}`. Lý do disabled tính ở client: `permissions ⊄ access.permissions` → "Vượt quyền của bạn trên thiết bị"; ngược lại → "Chứa quyền rủi ro cao — chỉ master-admin". Khi chọn một vai trò, dòng đó hiện danh sách quyền của vai trò (gom theo nhóm, badge high-risk) để người gán thấy đúng thứ mình cấp [RT3]. Lưu từng dòng: chọn rỗng → `removeDeviceMembership(expectedRoleId=row.roleId)`, còn lại → `assignDeviceMembership({roleId, expectedRoleId: row.roleId, expectedRoleVersion: role.version})` [RT3]. Dòng `canRemove && !canEdit` (member org khác/không org, Q13) không có select, chỉ có nút Gỡ gọi `removeDeviceMembership(expectedRoleId=row.roleId)` [RT14]. Xong thì reload. 409 → banner xung đột + reload (`DEVICE_ROLE_VERSION_STALE` → "Vai trò đã đổi kể từ lúc tải, xem lại quyền trước khi gán") [RT3]; 403/404/400 → banner qua `deviceAccessErrorMessage`. CSRF dùng prop, thiếu thì gọi `getCsrf()` (giống DevicePermissionsTab.vue:58-66).
- DeviceRolesView/DeviceRoleEditor (DTO `DeviceRoleDto`): editor nhận `role|null, codes, isMasterAdmin, saving, error` và emit `save({name, description, permissions})`/`cancel`. Checkbox gom theo `group` (sort ordinal); mỗi mã hiện `label` và, dạng chữ phụ, danh sách `commands` nó chặn (ví dụ `sys.config_export` → `config.get:system`) [RT12]; code high-risk bị `disabled` kèm icon khoá nếu không phải master-admin. Với non-master, vai trò đã chứa high-risk thì editor chỉ đọc và nút Xoá bị khoá. Xử lý lỗi:
  - 409 `DEVICE_ROLE_VERSION_STALE` → banner, reload list, đóng editor.
  - `DEVICE_ROLE_NAME_TAKEN`, `DEVICE_ROLE_INVALID`, `DEVICE_PERMISSION_UNKNOWN` → lỗi inline qua map, giữ editor.
  - `DEVICE_ROLE_IN_USE` → banner "đang được gán, gỡ thành viên trước".
  - Mã khác → banner qua `deviceAccessErrorMessage`.
- UserManagementView [RT1]: tải thêm `permissions()` cùng lúc `listUsers`; cột Roles render từng role trong `item.roles`; với master-admin, mỗi role có nút gỡ (icon, `aria-label` "Gỡ role {role}") mở `DangerConfirmDialog`; xác nhận → `getCsrf` → `removeRole(user.id, role, {csrfToken})` → reload. Lỗi: `ApiError` có body `{errors}` chứa `LAST_MASTER_ADMIN` → "Không thể gỡ master-admin cuối cùng"; còn lại → câu chung. Non-master không thấy nút (server vẫn chặn).

## Related Code Files
**Create**
| Path | Thay đổi |
|---|---|
| frontend/src/core/api/adminApi.test.ts | Chuyển từ `src/adminApi.test.ts` (A0) + contract test mới |
| frontend/src/core/auth/permissions.test.ts | Chuyển (A0) + test thứ tự `isMasterAdmin` → deny → allow, `canOnDevice` |
| frontend/src/core/commands/commandAvailability.test.ts | Chuyển (A0) + test field catalog mới |
| frontend/src/core/realtime/useDeviceOperationsHub.test.ts | Chuyển (A0), fixture `AUTHORIZATION_REVOKED` đổi sang 3 reason mới với kỳ vọng riêng từng reason; + test reason (A4) |
| frontend/src/core/realtime/realtimeAuthorization.test.ts | Chuyển nguyên trạng (A0); khoá phép so `policyVersion` trong một epoch |
| frontend/src/core/realtime/signalRConnection.test.ts | Chuyển nguyên trạng (A0) |
| frontend/src/core/auth/deviceAccessErrors.ts (+ `.test.ts`) | Map 22 mã (contract §4/§5/§6) → thông báo, có fallback; test mỗi mã có câu (A12) |
| frontend/src/features/security/views/DeviceRolesView.vue (+ `.test.ts`) | Trang vai trò thiết bị |
| frontend/src/features/security/components/DeviceRoleEditor.vue (+ `.test.ts`) | Form tạo/sửa + picker quyền |
| frontend/src/features/devices/components/DeviceMembersTab.vue (+ `.test.ts`) | Tab thành viên (dòng của mình chỉ master-admin sửa được) |
| frontend/src/features/security/views/RolePermissionView.test.ts | Khoá việc lấy CSRF |
| frontend/src/app/router/routes.test.ts | Khoá route + nav link mới |

**Modify**
| Path | Thay đổi |
|---|---|
| frontend/src/core/http/errors.ts | Getter `ApiError.code` |
| frontend/src/core/api/adminApi.ts | `ApiError`, DTO theo §6, 10 method mới (gồm `removeRole`), bỏ ma trận/device-action, `confirm` |
| frontend/src/core/auth/permissions.ts | `can()` theo thứ tự `isMasterAdmin` → deny → allow exact (bỏ allow `*`); thêm `canOnDevice`, `canManageDeviceAccess`; bỏ `canDeviceAction` |
| frontend/src/core/commands/commandAvailability.ts | `CatalogAction` mới (`permissionByDomain` kiểu `Record<string, string>` hoặc null) + 4 helper |
| frontend/src/core/realtime/useDeviceOperationsHub.ts | Hằng reason, auto re-join khi `AUTHORIZATION_CHANGED` có thử lại giới hạn + jitter, option `onRejoined`/`onRejoinFailed` [RT7] |
| frontend/src/features/security/views/UserManagementView.vue (+ `.test.ts`) | Nút gỡ role cho master-admin (confirm, CSRF, `LAST_MASTER_ADMIN`), cột Roles đọc `roles` [RT1] |
| frontend/src/App.vue | Repoint import 4/6/7/12/13 sang `@/core/...` (B1); dòng 125 → `requiredPermission ?? ''`; bỏ nhánh 687 + refs 100-101, 453-454; `onRevoked` 148-152 chỉ xoá khi `DEVICE_REVOKED`/`ACCESS_REMOVED` |
| frontend/src/App.test.ts | Đổi mock `./adminApi` → `'@/core/api/adminApi'` và `./signalRConnection` → `'@/core/realtime/signalRConnection'` (B1) |
| frontend/src/PermissionGrantEditor.vue | Import core (B1); bỏ form/handler/list device-action; lỗi grant qua `deviceAccessErrorMessage`, câu cũ làm fallback (C2) |
| frontend/src/PermissionGrantEditor.test.ts | A11: viết lại test dòng 58, bỏ `grantDeviceAction` khỏi factory `vi.mock` (dòng 7) và `deviceActionGrants` khỏi fixture (dòng 8), thêm test mã lỗi grant. B1: đổi import dòng 5 và đường mock dòng 7 sang `'@/core/api/adminApi'` |
| frontend/src/features/security/views/RolePermissionView.vue | `getCsrf` trong `loadData`; `:allowed="can(permissions,'permissions.manage')"` |
| frontend/src/features/devices/views/DeviceDetailView.vue | Access, trạng thái (gồm revoked), gate, watch id, reason |
| frontend/src/features/devices/views/DeviceDetailView.test.ts | Mock `myDeviceAccess`/`deviceMemberships` thay `devicePermissions`, fixture catalog mới, route reactive, fake connection bắt `DeviceRevoked` (test sửa) |
| frontend/src/features/devices/components/DeviceCommandModal.vue | Prop `access`, resolver, confirm, danger; kết quả 200 mang mã trong map hiện là lỗi |
| frontend/src/features/devices/components/DeviceCommandModal.test.ts | Fixture catalog mới (`class`, `requiredPermission`, `permissionByDomain: Record<string, string>`, `highRisk`) + prop `access` (test sửa); test kết quả 200 mang `CONFIRM_REQUIRED` |
| frontend/src/app/router/routes.ts | Route `admin-device-roles`, `meta.access.permissions: ['permissions.manage']` |
| frontend/src/app/nav.config.ts | Link "Vai trò thiết bị", icon `shield-check` (shared/ui/icons.ts:115), `permission: 'permissions.manage'` |
| docs/architecture-device-permissions-parity.md | Viết lại toàn bộ §5 (UI) theo mô hình mới (contract §9), thay dòng trỏ phase 4 để lại ở đầu §5; các mục khác do phase 4 |

**Delete**
- B1: `frontend/src/{adminApi,permissions,commandAvailability,useDeviceOperationsHub,signalRConnection,realtimeAuthorization}.ts`. 6 file `.test.ts` đi kèm đã chuyển cạnh bản core ở A0.
- C6: `frontend/src/features/devices/components/DevicePermissionsTab.vue` + `.test.ts`.

**Không sửa**
- `frontend/src/core/realtime/realtimeAuthorization.ts`: giữ phép so `policyVersion` trong một epoch.
- `frontend/src/catalogVerification.ts`: App.vue:9 vẫn dùng.

## Implementation Steps
### A. Tests Before (red)
0. **Khoá hành vi (phải xanh trước khi sửa code):**
   - Chuyển 6 file test sang cạnh bản core: `adminApi`, `permissions`, `commandAvailability`, `useDeviceOperationsHub`, `signalRConnection`, `realtimeAuthorization`. Import tương đối `./X` tự trỏ sang bản core nên không phải sửa import.
   - Chỉ một chỗ đổi nội dung: test hub `dispatches DeviceEvent and tears down pending work on DeviceRevoked` bỏ fixture `reason: 'AUTHORIZATION_REVOKED'` (dòng 100 cũ), viết lại thành `it.each` theo 3 reason wire mới, mỗi reason có kỳ vọng riêng:
     - Cả 3 (`DEVICE_REVOKED`, `ACCESS_REMOVED`, `AUTHORIZATION_CHANGED`): ngay sau emit, `state.devices` không còn d1, controller pending đã abort, `onRevoked` nhận đúng reason.
     - `DEVICE_REVOKED`, `ACCESS_REMOVED`: `requiresReauthorization` có d1; sau `flushPromises` không có `JoinDevice` nào ngoài lần join đầu.
     - `AUTHORIZATION_CHANGED`: ở đây chỉ khoá phần teardown đồng bộ. Kỳ vọng tự join lại nằm ở A4, vì nó đỏ tới B4.
     - Hub hiện chưa rẽ nhánh theo reason, nên mọi kỳ vọng trên xanh ngay và vẫn xanh sau B4.
   - Chưa đổi App.test.ts và PermissionGrantEditor.test.ts; import/mock của hai file này đổi ở B1, cùng lúc repoint.
   - Chạy `npm run typecheck && npm test`: phải xanh, chứng minh bản core tương đương. Đây là mốc typecheck xanh cuối cùng trước cửa sổ đỏ (xem B).
   - Các test được khoá:
     - adminApi: `does not treat a failed authorization response as an empty resource list` (phải giữ `toThrow('403')` sau khi đổi sang `ApiError`), `sends same-origin credentials and CSRF on mutations...`, `serializes strong identifier request fields as { value }`, `submits commands through the deferred transport contract...`.
     - Hub: 14 test (test reason ở trên chạy 3 case), đặc biệt `does not let a delayed join undo a newer revoke for the same device` và `keeps stale events from restoring revoked state`.
     - realtimeAuthorization: 7 test, gồm `rejects a snapshot with the same epoch but a lower policy version` và `tracks authorization epochs and policy versions per device`.
     - signalRConnection: `builds a browser connection without opening it during construction`.
     - permissions: `fails closed for absent or expired snapshots`, `fails closed when a persisted deny overrides an allow`.
     - commandAvailability: `only resolves actions that the server returned in its catalog`.
     - App.test.ts: 2 test, chạy lại sau B1.
1. `core/api/adminApi.test.ts`:
   - `throws ApiError carrying status and body code`: 409 → `instanceof ApiError`, `status` 409, `code` `DEVICE_ROLE_VERSION_STALE`; 403 → `ForbiddenError`.
   - Vai trò: `lists device roles for an organization without caching`; `creates a device role with CSRF`; `updates a device role with expectedVersion and CSRF`; `deletes a device role with expectedVersion query and CSRF`; `loads device permission codes`.
   - Membership: `loads device memberships without caching` (trả nguyên `{deviceId, members, roles}`, có `canRemove` và role `version`); `assigns a membership with expectedRoleId, expectedRoleVersion and CSRF` (body `{roleId, expectedRoleId, expectedRoleVersion}`, id là string trần, không bọc `{value}`) [RT3]; `removes a membership with expectedRoleId query and CSRF`; `loads my device access without caching`.
   - User: `removes a user role with CSRF` (DELETE `/api/v1/users/{id}/roles/{role}`, role được encode) [RT1].
   - Mã quyền: `loads device permission codes` khẳng định shape `{code, group, highRisk, label, commands}` [RT12].
   - Lệnh: `sends the confirmation token with a command`.
   - `no longer exposes device-action or permission-matrix methods` (`'grantDeviceAction'`, `'devicePermissions'`, `'updateDevicePermissions'` `in adminApi` → false). Test âm này phải nêu tên đã xoá, nên grep tên đã xoá ở Regression Gate loại `*.test.ts`.
   - Xoá 2 test ma trận (dòng 145-186 cũ). Test snapshot grant bỏ `deviceActionGrants`.
2. `core/auth/permissions.test.ts`:
   - `isMasterAdmin passes without exact allow`: `isMasterAdmin: true` với `permissions: []` → true; snapshot hết hạn hoặc sai hình (`permissions: null`) → false; `'true'` kiểu string → false.
   - `master-admin with a deny is still allowed`: deny exact và deny `'*'` đều không chặn `isMasterAdmin: true` (giống server, contract §3/§9).
   - `allow * grants nothing`: `permissions: ['*']` → false với mọi mã.
   - `deny * blocks non-master`: `isMasterAdmin` false hoặc thiếu, `permissions: ['devices.read']`, `deniedPermissions: ['*']` → false với mọi mã, kể cả mã đang allow.
   - `canOnDevice fails closed for missing access, empty code or malformed permissions`.
   - `canOnDevice ignores a wildcard entry`: `['*']` gọi `sys.reboot` → false.
   - `canOnDevice lets see-all admins view but not command`: với `seesAllInScope`, `device.view` true, `sys.reboot` false.
   - `canDeviceAction is no longer exported` (import namespace, `'canDeviceAction' in module` → false).
   - Test cũ `supports explicit permissions and master wildcard` đổi tên thành `supports explicit permissions` và bỏ assertion `['*']` → true. Bỏ test `keeps device action authority separate...`.
3. `core/commands/commandAvailability.test.ts`. Fixture đầu file đổi sang shape mới; assertion của 3 test cũ giữ nguyên.
   - `requires confirmation only when the catalog says so`, thay cho `requires explicit confirmation for dangerous actions`.
   - `derives danger from class and high-risk flag`.
   - `resolves fixed and domain-policy permissions`:
     - `requiredPermission` cố định luôn thắng.
     - `permissionByDomain: { adc: 'adc.config_edit' }` + `{domain:'adc'}` → `adc.config_edit`; giá trị của map được dùng nguyên, kể cả khi `class: 'Read'`.
     - `'ADC'`, `1`, `{}`, thiếu domain, domain lạ, `'constructor'` → null; `permissionByDomain: null` → null.
   - `detects whether the device role can issue any catalog command`.
4. `core/realtime/useDeviceOperationsHub.test.ts`:
   - `re-joins with a fresh epoch after AUTHORIZATION_CHANGED`: epoch 3→4, lần invoke cuối là `JoinDevice('d1', 4)`, `subscribedDevices` có d1, `requiresReauthorization` không còn d1, `onRevoked` nhận reason.
   - `does not re-join after REALTIME_RESYNC_REQUIRED`: device nằm trong `requiresReauthorization`, chờ reauth thủ công như cũ.
   - Thử lại có giới hạn (fake timers) [RT7]: `retries after STALE_AUTHORIZATION_EPOCH and joins with the newer epoch` (lần join đầu reject `STALE_AUTHORIZATION_EPOCH`, lần sau thành công → `subscribedDevices` có d1, `onRejoined('d1')` một lần); `flags the device and notifies the consumer after the final failed attempt` (3 lần đều lỗi → `requiresReauthorization` có d1, `onRejoinFailed('d1', code)` đúng một lần, không unhandled rejection); `does not retry after DEVICE_PERMISSION_REQUIRED` (một lần join, rồi `onRejoinFailed`); `stops retrying after leaveDevice`.
   - `does not re-join after the hub was disconnected`.
   - `ACCESS_REMOVED`/`DEVICE_REVOKED` không cần test riêng ở đây: test reason ở A0 đã khoá "bỏ device, không join lại".
5. `DeviceMembersTab.test.ts`:
   - `renders one role selector per member`.
   - `keeps the current user row read-only for a non-master`, kể cả khi `canEdit` true.
   - `lets a master-admin edit their own row`: `access.isMasterAdmin: true`, dòng của mình `canEdit: true` → select bật, lưu gọi `assignDeviceMembership` kèm `expectedRoleId`; server trả `canEdit: false` thì vẫn khoá.
   - `disables every row for a non-master when the current user is unknown`.
   - `disables roles the caller cannot assign and shows the reason`.
   - `assigns a role with CSRF and expectedRoleId`.
   - `shows the selected role permissions before saving` (nhóm + badge high-risk) [RT3].
   - `sends expectedRoleVersion and shows a role-changed banner on DEVICE_ROLE_VERSION_STALE` (rồi reload) [RT3].
   - `offers only removal for a foreign member row` (`canRemove: true`, `canEdit: false` → không có select, nút Gỡ gọi `removeDeviceMembership` với `expectedRoleId`) [RT14].
   - `removes a membership with the current role as expectedRoleId`.
   - `reloads and shows a conflict banner on 409`.
   - `shows a forbidden banner on 403 with the server code message` (câu lấy từ `deviceAccessErrorMessage`).
   - `shows a not-found state when memberships return 404`.
   - `fetches CSRF when none is provided`.
6. `DeviceRolesView.test.ts`: `lists roles of the selected organization`; `locks high-risk permissions for a non-master admin`; `unlocks high-risk permissions for a master-admin`; `reloads roles and shows a banner on a stale version (409)`; `explains that a role in use cannot be deleted`; `keeps the editor open with an inline message on DEVICE_ROLE_NAME_TAKEN or DEVICE_ROLE_INVALID`; `creates a role with CSRF and the picked permissions`.
7. `DeviceRoleEditor.test.ts`: `groups permission codes by group`; `shows the label and the commands each permission gates` [RT12]; `keeps a high-risk role read-only for a non-master admin`; `emits trimmed name and selected permissions on save`; `disables save while the name is blank`.
8. `routes.test.ts`: `registers the device roles route with permissions.manage access`; `adds a device roles nav link guarded by permissions.manage`.
9. `DeviceDetailView.test.ts`:
   - `hides commands, provisioning, licenses and members tabs without permission`.
   - `shows a not-found state when my-access returns 404`: `realtimeAuthorization` không được gọi.
   - `shows a forbidden state when device.view is missing`: `deviceTelemetry` không được gọi.
   - `shows the revoked state for an in-scope revoked device`: my-access 200 với `permissions: []`, list trả `state: 'Revoked'` → `device-revoked`; không gọi `deviceTelemetry`/`realtimeAuthorization`.
   - `refetches access when the route id changes`: route mock dạng `reactive`.
   - `passes device access to the command modal`.
   - Reason, dùng fake connection để bắt handler `DeviceRevoked`: `marks the device revoked only for DEVICE_REVOKED`; `shows the access-removed state on ACCESS_REMOVED`; `refetches access on AUTHORIZATION_CHANGED without marking revoked`; `keeps the page usable on REALTIME_RESYNC_REQUIRED` (tab còn, `realtimeStatus='disconnected'`, không hiện access-removed) [RT6]; `reloads telemetry after an automatic re-join` và `marks realtime disconnected when the automatic re-join fails` [RT7].
   - Sửa test cũ `switches between tabs...`: chờ `device-members-tab` và dùng fixture có đủ quyền.
10. `DeviceCommandModal.test.ts`:
    - `enables submit only when the device role holds the required permission`.
    - `resolves the config permission from the params domain`: fixture `permissionByDomain: { adc: 'adc.config_edit' }`, params `{"domain":"adc"}`.
    - `requires a confirmation token when the catalog demands it`; `sends the confirmation token`.
    - `uses the danger variant for flash or high-risk commands`.
    - `shows the mapped message instead of success when the result carries CONFIRM_REQUIRED`: `sendCommand` trả 200 `{code: 'CONFIRM_REQUIRED'}` → `command-error-result` có câu của map, không có `command-success-result`.
    - Hai test cũ giữ assertion: `catalog-unverified-warning`, và submit disabled + `!bg-orange-500`.
11. `PermissionGrantEditor.test.ts`:
    - Test dòng 58 đổi thành `submits resource denies and offers no device-action form` (`[data-test=device]` không tồn tại).
    - Cùng lúc bỏ `grantDeviceAction: vi.fn()` khỏi factory `vi.mock` (dòng 7) và `deviceActionGrants: []` khỏi fixture `snapshot` (dòng 8). Import dòng 5 và đường mock dòng 7 đổi ở B1.
    - `shows the server code message when a grant is rejected`: `grantFeature` reject `ApiError` 400 `WILDCARD_GRANT_FORBIDDEN`, 400 `GRANT_TARGET_IS_MASTER_ADMIN`, 403 `GRANT_OVERRIDES_MASTER_DECISION` → message là câu của map [RT1, RT13]; mã lạ → giữ câu cũ `Permission grant was rejected by the backend.`.
    - `RolePermissionView.test.ts`: `fetches CSRF and passes it to the grant editor`.
12. `core/auth/deviceAccessErrors.test.ts`:
    - `has a message for every device access code the UI can receive`: test liệt kê nguyên văn 22 mã ở Architecture. Mỗi mã: `deviceAccessMessage(code)` là chuỗi không rỗng, và `deviceAccessErrorMessage(new ApiError(400, { kind: 'json', raw: { code } }, url))` trả đúng câu đó. `DEVICE_ACCESS_ERROR_CODES` khớp đúng tập 22 mã.
    - `falls back for unknown codes, inherited keys and non-API errors`: `'SOMETHING_NEW'`, `'constructor'`, `'__proto__'` → `deviceAccessMessage` null; `ApiError` mã lạ → `fallback` nếu có truyền, không thì `userMessage`; `new Error('x')` và `null` → câu chung.
13. `UserManagementView.test.ts` [RT1] (test hiện có giữ nguyên assertion): `renders assigned roles from the roles array`; `shows a remove-role action only to a master-admin`; `removes a role with CSRF after confirmation and reloads`; `explains LAST_MASTER_ADMIN`.

### B. Seams / infrastructure
1. Gom module:
   - Repoint App.vue:4,6,7,12,13 và PermissionGrantEditor.vue:3 sang `@/core/...`.
   - Đổi mock trong App.test.ts: `./adminApi` → `@/core/api/adminApi`, `./signalRConnection` → `@/core/realtime/signalRConnection`.
   - PermissionGrantEditor.test.ts: đổi cả `import { adminApi } from './adminApi'` (dòng 5) lẫn `vi.mock('./adminApi', …)` (dòng 7) sang `'@/core/api/adminApi'`. Sót dòng 5 thì sau khi xoá file, vitest không resolve được module và vue-tsc đỏ. Factory mock đã bỏ `grantDeviceAction` từ A11.
   - Xoá cùng lúc 6 module top-level: `adminApi.ts`, `permissions.ts`, `commandAvailability.ts`, `useDeviceOperationsHub.ts`, `signalRConnection.ts`, `realtimeAuthorization.ts`. Test của chúng đã nằm cạnh bản core từ A0.
   - Kiểm bằng vitest theo file: các case khoá ở A0, 2 test App.test.ts và 3 case không đổi của PermissionGrantEditor.test.ts vẫn pass; case đỏ của A vẫn đỏ đúng lý do. Không ghi nhận typecheck ở bước này, vì test đỏ của A đã làm nó đỏ.
2. `errors.ts`: thêm getter `code`. `request()` ném `toApiError(...)`. Message giữ dạng `HTTP <status> <url>`, nên regex `/403|401/` ở PermissionGrantEditor.vue:61,79,97,115 vẫn khớp.
3. `adminApi.ts`: DTO và method như Architecture; xoá type 96-100, 118-147, `DeviceActionGrantDto` và method 208-212.
4. `permissions.ts` (`can()` theo thứ tự `isMasterAdmin` → deny → allow exact, sau bước kiểm snapshot; `canOnDevice`, `canManageDeviceAccess`), `commandAvailability.ts`, `deviceAccessErrors.ts` (22 mã, fallback), hub (reason + auto re-join có thử lại giới hạn, `onRejoined`/`onRejoinFailed` [RT7]; không sửa `realtimeAuthorization.ts`).
- **Cửa sổ typecheck đỏ có chủ đích:** từ A1 (test đỏ import symbol/file chưa có), qua B3–B4 (vỡ App.vue:125, DeviceCommandModal.vue:75/250, PermissionGrantEditor.vue:108/137, DevicePermissionsTab.vue và fixture test), tới hết C6. Không bước nào trong cửa sổ này được ghi là typecheck xanh; theo dõi tiến độ bằng vitest theo file. Checkpoint typecheck đầu tiên nằm ngay sau C6, là bước xoá DevicePermissionsTab.vue. Bước này được xếp làm bước code cuối để mọi file mà test A import đều đã tồn tại.

### C. Refactor / implement
1. App.vue: các điểm ở bảng Modify (legacy template giữ nguyên, §10).
2. PermissionGrantEditor.vue: bỏ refs 15-16/19, `canDeviceEdit` 29, `submitDevice` 102-118, list 137-139, form 151-156. Lỗi của `submit`/`submitResource` (dòng 80, 98) hiển thị qua `deviceAccessErrorMessage(error, <câu cũ>)`; giữ nguyên emit `authorization-failed`. RolePermissionView: CSRF + `:allowed`.
3. DeviceCommandModal:
   - Prop `access: DeviceAccessDto | null` thay `permissions`.
   - `requiredPermission = resolveCommandPermission(action, parsedParams)`: tra `permissionByDomain[domain]`, không rẽ nhánh theo class. `hasPermission = !!p && canOnDevice(access, p)`. Badge hiện `p ?? 'Chọn domain hợp lệ'`.
   - `isDangerous = isDangerousCommand`. Class orange chỉ ép khi không danger. DangerConfirmDialog vẫn dùng cho lệnh danger.
   - Ô `confirm` (data-test `command-confirm-input`) hiện khi `requiresCommandConfirmation`. `canSubmit` cần ô này không rỗng. Reset khi đổi action hoặc đóng modal. Body gửi `confirm`.
   - Sửa `currentAction.value !== null`. Lỗi HTTP hiển thị qua `deviceAccessErrorMessage`. Kết quả 200 có `deviceAccessMessage(res.code) !== null` (`CONFIRM_REQUIRED`, `PERMISSION_UNMAPPED`) hiện ở `command-error-result` với câu của map, không vào `command-success-result`.
4. `DeviceRoleEditor.vue` (label + commands mỗi mã [RT12]), `DeviceRolesView.vue` (nút Tạo/Lưu dùng `Button variant="primary"`), route + nav link.
4b. `UserManagementView.vue`: cột Roles đọc `roles`, nút gỡ role cho master-admin với `DangerConfirmDialog`, CSRF, xử lý `LAST_MASTER_ADMIN` [RT1].
5. DeviceDetailView: access/state/gate/watch/reason như Architecture. Bốn khối trạng thái thay header + tab, đều có nút về Fleet: `device-not-found`, `device-forbidden`, `device-revoked` (giữ tên thiết bị + badge Revoked), `device-access-removed`. Truyền `:access="deviceAccess"` vào modal.
6. Tạo `DeviceMembersTab.vue` (root data-test `device-members-tab`; predicate sửa dòng như Architecture, dòng của mình chỉ master-admin sửa được), xoá DevicePermissionsTab.vue + test, đổi import và khối 1050-1056 của DeviceDetailView. **Checkpoint:** chạy `npm run typecheck && npm test`. Đây là lần đầu typecheck phải xanh kể từ A0: mọi file mà test A import đã có, và không còn DevicePermissionsTab gọi method đã xoá.
7. Docs `docs/architecture-device-permissions-parity.md` §5 (UI, contract §9). Phase 4 viết lại các mục ngoài §5 và chỉ để lại một dòng trỏ ở đầu §5. Phase này viết lại toàn bộ §5 và xoá dòng trỏ đó; nội dung mới thay chỗ, không để hai bản song song. Đọc lại toàn doc trước khi sửa để thuật ngữ khớp các mục phase 4 đã viết:
   - Tiêu đề §5 bỏ "Datacake Parity", đặt theo mô hình vai trò + thành viên.
   - §5.1: tab `Permissions` → `Thành viên`. Tab hiện theo my-access/feature (commands, provisioning, licenses, Thành viên). Thêm 4 trạng thái `device-not-found`, `device-forbidden`, `device-revoked`, `device-access-removed`.
   - §5.2: thay thẻ ma trận 4 quyền bằng tab Thành viên (mỗi người một select vai trò, dòng của mình chỉ đọc trừ master-admin, vai trò không gán được thì disabled kèm lý do, xem quyền của vai trò trước khi lưu, dòng member org khác chỉ gỡ được, banner 404/403/409, CSRF + `expectedRoleId` + `expectedRoleVersion`) [RT3, RT14].
   - Thêm §5.3, trang Vai trò thiết bị: `/admin/device-roles`, picker theo `group` có nhãn và danh sách lệnh mỗi quyền chặn, high-risk khoá với non-master, 409 `expectedVersion`, `DEVICE_ROLE_IN_USE` [RT12].
   - Thêm §5.4, modal lệnh và reason hub: gate `requiredPermission`/`permissionByDomain`, ô confirm, danger theo `class`/`highRisk`; `AUTHORIZATION_CHANGED` tự join lại có thử lại giới hạn rồi tải lại telemetry, `REALTIME_RESYNC_REQUIRED` thủ công (cả khi refresh phiên lỗi); thông báo lỗi qua map mã dùng chung, mã lạ rơi về câu chung [RT6, RT7].
   - Thêm §5.5, gỡ role ở Quản lý người dùng: chỉ master-admin thấy nút, có xác nhận, không gỡ được master-admin cuối cùng [RT1].
   - Chỉ dùng placeholder; xoá tên/email thật đang có ở §5.2 cũ (phase 4 để lại mục này cho phase này).

### D. Tests After
- A1–A12 xanh mà không nới assertion; các case khoá ở A0 vẫn xanh.
- Đối chiếu doc §5 (C7) với data-test, route và trạng thái đã implement. §5 không còn dòng trỏ của phase 4, không còn tên/email thật.

### Regression Gate
```bash
(cd frontend && npm run typecheck && npm test && npm run build)
dotnet build Hms.Server.slnx
dotnet test Hms.Server.slnx   # phase này không đổi backend; chạy để xác nhận toàn plan xanh
# (1) Tên đã xoá còn trong code, không tính file test → phải rỗng
grep -rnE "featurePermission|canDeviceAction|grantDeviceAction|DeviceActionGrant|deviceActionGrants|devicePermissions|updateDevicePermissions|DevicePermissionMatrix|DevicePermissionsTab|device-actions" frontend/src --include='*.ts' --include='*.vue' --exclude='*.test.ts'
# (2) Key mock/fixture của API cũ và reason wire cũ trong file test → phải rỗng
grep -rnE "\b(featurePermission|grantDeviceAction|devicePermissions|updateDevicePermissions|deviceActionGrants)\s*:|AUTHORIZATION_REVOKED" frontend/src --include='*.test.ts'
# (3) File đã xoá hoặc đã chuyển → không in gì
ls frontend/src/{adminApi,permissions,commandAvailability,useDeviceOperationsHub,signalRConnection,realtimeAuthorization}{,.test}.ts frontend/src/features/devices/components/DevicePermissionsTab{.vue,.test.ts} 2>/dev/null
```
- Phải xanh:
  - Test đã chuyển sang core: `core/api/adminApi.test.ts`, `core/auth/permissions.test.ts`, `core/commands/commandAvailability.test.ts`, `core/realtime/useDeviceOperationsHub.test.ts`, `core/realtime/realtimeAuthorization.test.ts`, `core/realtime/signalRConnection.test.ts`.
  - Test sửa hoặc mới: `App.test.ts`, `PermissionGrantEditor.test.ts`, `core/auth/deviceAccessErrors.test.ts`, `DeviceMembersTab.test.ts`, `DeviceRolesView.test.ts`, `DeviceRoleEditor.test.ts`, `RolePermissionView.test.ts`, `routes.test.ts`, `DeviceDetailView.test.ts`, `DeviceCommandModal.test.ts`, `UserManagementView.test.ts` (thêm test, test cũ giữ assertion) [RT1].
  - Test không đổi: `DeviceListView`, `OverviewView`, `HierarchyView`, `LoginView`, `DangerConfirmDialog`, `primitives` (`bg-orange-600`), `catalogVerification`.
- Grep (1) loại `*.test.ts`, vì test âm (`no longer exposes device-action or permission-matrix methods`, `canDeviceAction is no longer exported`) phải nêu tên đã xoá. Không dựng tên động trong test để né grep. Grep (2) bù phần test: chỉ bắt dạng key `name:` (factory `vi.mock`, fixture) và reason `AUTHORIZATION_REVOKED`, không bắt chuỗi nằm trong test âm.

## Todo List
- [ ] A0 chuyển 6 file test sang cạnh bản core (test hub đổi fixture `AUTHORIZATION_REVOKED` sang 3 reason mới, kỳ vọng riêng từng reason); typecheck + test xanh
- [ ] A1–A12 viết test red (A11 bỏ `grantDeviceAction` khỏi factory mock; A12 map mã lỗi)
- [ ] B1 repoint App.vue (+ mock App.test.ts) và PermissionGrantEditor (+ import dòng 5 và mock dòng 7 của test), xoá 6 module top-level
- [ ] B2 `ApiError` + `code`
- [ ] B3–B4 DTO, method (gồm `removeRole`), `can()` (master-admin → deny → allow), helper catalog, map 22 mã lỗi, hub reason + join lại có thử lại giới hạn
- [ ] C1–C3 App.vue, PermissionGrantEditor/RolePermissionView, DeviceCommandModal
- [ ] C4 DeviceRoleEditor (label + commands), DeviceRolesView, route, nav
- [ ] C4b UserManagementView: gỡ role cho master-admin
- [ ] C5 DeviceDetailView
- [ ] C6 DeviceMembersTab (dòng của mình chỉ master-admin sửa được), xoá DevicePermissionsTab, checkpoint typecheck + test đầu tiên
- [ ] C7 viết lại toàn bộ docs §5, thay dòng trỏ của phase 4
- [ ] D test sau, Regression Gate, 3 lệnh kiểm sạch

## Success Criteria
- [ ] Không còn 6 module top-level (`adminApi`, `permissions`, `commandAvailability`, `useDeviceOperationsHub`, `signalRConnection`, `realtimeAuthorization`). App.vue (+ mock App.test.ts) và PermissionGrantEditor.vue (+ import và mock trong PermissionGrantEditor.test.ts, factory không còn `grantDeviceAction`) import `core/**`; test nằm cạnh bản core.
- [ ] Mọi lỗi HTTP của adminApi là `ApiError` có `status` và `code`. Contract test phủ cả 10 endpoint mới (gồm `removeRole`) (shape theo §6, id là string trần) và CSRF trên mọi mutation.
- [ ] `can()`: snapshot hợp lệ và còn hạn với `isMasterAdmin === true` thì qua vô điều kiện (deny exact hoặc `*` không áp lên master-admin, giống server); với non-master, deny (exact hoặc `*`) thắng allow; allow `*` không cấp gì. `canOnDevice` không nhận `*`.
- [ ] Map lỗi có câu cho đủ 22 mã (contract §4/§5/§6), test liệt kê từng mã; mã lạ và khoá kế thừa rơi về fallback. Kết quả lệnh 200 mang `CONFIRM_REQUIRED`/`PERMISSION_UNMAPPED` hiện là lỗi, không phải Dispatched; lỗi grant `WILDCARD_GRANT_FORBIDDEN` hiện đúng câu.
- [ ] Member không có quyền không thấy tab commands/provisioning/licenses/members và nút C2. Out-of-scope → `device-not-found`; thiếu `device.view` → `device-forbidden`; thiết bị Revoked trong phạm vi (my-access 200, `permissions` rỗng) → `device-revoked`. Ba trạng thái này không gọi hub hay telemetry.
- [ ] Modal: `config.set` với `{"domain":"adc"}` đòi đúng quyền ở `permissionByDomain.adc` (fixture `adc.config_edit`), không rẽ nhánh `class`. `requiresConfirmation` bắt buộc nhập confirm và body có `confirm`.
- [ ] Non-master không sửa được dòng của chính mình, kể cả khi `canEdit` true; master-admin sửa được dòng của mình khi server trả `canEdit`. Vai trò không gán được hiện disabled kèm lý do; banner 409/403/404 tách biệt.
- [ ] Trước khi gán, tab Thành viên hiện quyền của vai trò đang chọn và gửi `expectedRoleVersion`; vai trò đổi giữa chừng → banner + reload [RT3]. Dòng member org khác chỉ có nút Gỡ [RT14].
- [ ] Non-master không tick được quyền high-risk; 409 version → reload. Mỗi mã quyền trong editor có nhãn và danh sách lệnh nó chặn [RT12].
- [ ] `AUTHORIZATION_CHANGED` → tự join lại (tối đa 3 lần thử, tính cả lần đầu, khi gặp `STALE_AUTHORIZATION_EPOCH`), không hiện Revoked; join lại xong tải lại telemetry; hết lượt → realtime disconnected, không kẹt im lặng [RT7]. `ACCESS_REMOVED` → trạng thái access-removed. `REALTIME_RESYNC_REQUIRED` giữ thủ công như cũ, trang vẫn dùng được [RT6]. Test hub không còn reason `AUTHORIZATION_REVOKED`; mỗi reason mới có kỳ vọng riêng.
- [ ] Master-admin gỡ được role của người khác từ Quản lý người dùng (có xác nhận, CSRF); non-master không thấy nút; `LAST_MASTER_ADMIN` có câu riêng [RT1].
- [ ] Doc §5 được viết lại toàn bộ và dòng trỏ của phase 4 đã được thay. §5 mô tả UI vai trò, thành viên (dòng của mình chỉ master-admin sửa được) và gate; không còn ma trận 4 quyền, không còn tên/email thật.
- [ ] Regression Gate xanh; grep (1), grep (2) rỗng; `ls` (3) không in gì.

## Risk Assessment
| Risk | Mitigation |
|---|---|
| Phase 4 phát DTO lệch tên field §6 | Shape khoá ở hai đầu theo §6: integration test phase 4 và contract test `core/api/adminApi.test.ts` |
| `class` serialize khác tên enum | Fixture dùng tên enum (§6, Program.cs:28-29); `isDangerousCommand` có test |
| Message lỗi đổi từ `Request failed (N)` sang `HTTP N url`, hiển thị qua `err.message` (LoginView.vue:53, DeviceListView.vue:360...) | Chỉ khác chữ; giữ status trong message; test `toThrow('403')` khoá |
| Snapshot từ server cũ còn allow `*` mà thiếu `isMasterAdmin` | Fail-closed: UI ẩn bớt, server vẫn quyết. Phase 4 lên trước trong cùng release |
| UI cho master-admin qua dù có deny | Chủ đích, giống server (contract §3/§9); muốn hạn chế thì master-admin khác gỡ role ở Quản lý người dùng (Q11) [RT1]. Test `master-admin with a deny is still allowed` khoá để UI không ẩn chức năng server vẫn cho |
| Server trả mã mà map chưa có | Fallback theo thứ tự: `fallback` truyền vào → `HttpError.userMessage` → câu chung. Test A12 khoá danh sách 22 mã; thêm mã thì sửa map và test cùng lúc |
| Grep tên đã xoá khớp nhầm test âm, hoặc bỏ sót key mock cũ trong test | Grep (1) loại `*.test.ts`; grep (2) quét test chỉ theo dạng key `name:` và `AUTHORIZATION_REVOKED` |
| Typecheck đỏ kéo dài từ A1 tới C6 | Mốc xanh ở A0. Trong cửa sổ đỏ chỉ theo dõi bằng vitest theo file, không ghi nhận xanh. Checkpoint đầu tiên sau C6 |
| Re-join dồn dập khi nhiều `AUTHORIZATION_CHANGED` liên tiếp (admin lưu nhiều dòng) [RT7] | `STALE_AUTHORIZATION_EPOCH` không phải hàng rào nếu chỉ join một lần — nó làm viewer kẹt. Giảm thiểu: tối đa 3 lần thử (tính cả lần đầu) với jitter 250 ms–2 s, dừng ở mã quyền/phạm vi, báo consumer khi hết lượt; gán mới không advance epoch nên thêm thành viên không đá ai; join idempotent; fence per-device ở core/realtime/realtimeAuthorization.ts:22-25 |
| Master-admin lỡ tay gỡ role của chính mình [RT1] | `DangerConfirmDialog` bắt xác nhận; server chặn gỡ master-admin cuối cùng |
| Hai kết nối hub (App.vue + DeviceDetailView) | Cả hai dùng hub core có reason (App.vue chuyển sang core ở B1); App.vue không xoá device khi gặp `AUTHORIZATION_CHANGED` |
| Nav link hiện với mọi người vì subject mặc định SUPER_ADMIN (AppShell.vue:39) | Server trả 403/404; view tự hiện thông báo khi `!can('permissions.manage')`; wiring subject/guard ngoài phạm vi (§10) |
| Mọi dòng catalog `available=false` (§2) nên modal không gửi được lệnh thật | Đúng thiết kế; test dùng fixture `available: true` |
| Deep link tới tab bị ẩn | Watch `visibleTabs` → về `telemetry` |

## Security Considerations
- UI gate chỉ là UX; server (§3–§7) quyết định. Helper fail-closed: allow `*` không cấp gì (cả feature lẫn thiết bị); deny (exact hoặc `*`) chặn mọi non-master; master-admin vô điều kiện như server và chỉ lấy từ cờ `isMasterAdmin === true` của snapshot hợp lệ, còn hạn; `permissionByDomain` và map lỗi chỉ đọc own key.
- CSRF `X-CSRF-TOKEN` trên POST/PUT/DELETE vai trò, membership và gỡ role; sửa luôn chỗ RolePermissionView thiếu token.
- Nút gỡ role chỉ hiện với master-admin; server vẫn kiểm (reserved role, master-admin cuối cùng) [RT1]. Tab Thành viên hiện quyền thật của vai trò trước khi gán, nên tên vai trò gây hiểu nhầm không đánh lừa được người gán [RT3].
- 404 hiển thị chung một thông điệp "Không tìm thấy thiết bị", không phân biệt nonexistent với ngoài phạm vi (không lộ sự tồn tại).
- Query string chỉ chứa id/version (`expectedVersion`, `expectedRoleId`), không có dữ liệu nhạy cảm. Confirm token không log, không lưu, reset khi đóng modal.
- Lỗi hiển thị qua map code, không render body thô; mã lạ rơi về fallback. Chặn tự sửa (trừ master-admin, khớp `canEdit` server) và high-risk ở UI; server vẫn kiểm (`SELF_GRANT_FORBIDDEN`, `HIGH_RISK_REQUIRES_MASTER_ADMIN`).
- Bỏ form device-action free-text (gõ được `*`) ở PermissionGrantEditor và App.vue.

## Open Questions
Không còn câu hỏi mở. Đã chốt ở Validation Session 1 (2026-10-05): <!-- Updated: Validation Session 1 - V1-V6 and Q11 scope confirmed -->
1. V2 — lệnh thiếu quyền trả 403 → `ApiError` → `deviceAccessErrorMessage`; nhánh kết quả 200 ở C3 vẫn hiện `CONFIRM_REQUIRED`/`PERMISSION_UNMAPPED` là lỗi.
2. V1, V4, V5 không đổi code phase này (UI đọc `highRisk`, `canEdit`, `canRemove`, `assignable` từ server; banner `DEVICE_ROLE_IN_USE` khi server trả). V6 — v1 strict: admin chưa có membership trên thiết bị thấy mọi vai trò disabled với lý do "Vượt quyền của bạn trên thiết bị"; UI vai trò cấp tổ chức để v2. V3 thuần backend.
3. Q11 — nút gỡ role ở UserManagementView chỉ hiện với master-admin, cho mọi role.

## Next Steps
- Phase cuối của plan. Plan `261001-0030-full-hardware-mqtt-apis-c2` dùng lại `canOnDevice`, `resolveCommandPermission`, ô confirm, map mã lỗi (mã mới của transport thêm vào map và test A12) và hub có reason khi bật transport. Khớp confirm token chính xác vẫn ngoài phạm vi (§10).
- Follow-up ngoài phạm vi: UI gán role ở router mode (UserManagementView của phase này chỉ thêm gỡ role) [RT1]; UI sửa thiết bị cho member theo `device.edit_basics`/`device.edit_product`; wiring subject/guard cho AppShell và router; gate DeviceListView; bỏ legacy template App.vue; gom nốt các module trùng còn lại (`catalogVerification.ts`, `sessionFence.ts`, `secureDownload.ts`, `requestRevision.ts`...); audit hex brand.
