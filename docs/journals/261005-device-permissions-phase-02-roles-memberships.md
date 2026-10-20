# Vai Trò, Thành Viên và Lưu Trữ — Phase 2 Hoàn Tất

**Mã nhật ký**: `JRN-DEVICE-PERMISSIONS-PHASE-02-ROLES-MEMBERSHIPS-PERSISTENCE-2026`  
**Ngày thực hiện**: 05/10/2026  
**Chủ đề**: Phase 2 "Vai trò, membership và persistence" của plan `261005-0556-device-permissions-what-where` (`/ck:cook --tdd`): xây dựng domain model, store contract và EF persistence.  
**Trạng thái**: **Hoàn tất; owner duyệt kèm rủi ro đã ghi nhận sau 3 vòng review**

---

## 1. Kết Cấu Xây Dựng

**Domain:**
- `DeviceRole`: vai trò thuộc một tổ chức (mỗi tổ chức có thư viện vai trò riêng); tên ≤128 ký tự (đã trim), không trùng trong tổ chức, không phân biệt hoa/thường (so qua `NormalizedName`); mô tả ≤512; quyền là tập bất biến; `Version` bắt đầu từ 1 và tăng mỗi lần cập nhật.
- `DeviceMembership`: một vai trò trên mỗi cặp (người dùng, thiết bị).

**Store Contract** (`IDeviceRoleStore`, `IDeviceMembershipStore`):
- So sánh rồi mới ghi (compare-and-set):
  - cập nhật/xoá vai trò kiểm tra `Version`;
  - upsert/xoá thành viên kiểm tra vai trò hiện tại (`expectedRoleId`, null = không có).
- Mã lỗi: `DEVICE_ROLE_NAME_TAKEN`, `DEVICE_ROLE_NOT_FOUND`, `DEVICE_ROLE_VERSION_STALE`, `DEVICE_ROLE_IN_USE`, `DEVICE_MEMBERSHIP_CHANGED`.
- Mỗi ghi cũng ghi hàng audit (`device_access_audits`).

**Entity Framework:**
- Bảng: `device_roles`, `device_role_permissions`, `device_memberships`, `device_access_audits`.
- Token đồng thời (model-only): `device_roles.Version` và `device_memberships.RoleId`.
- `InMemoryDeviceAccessStore`: kiểm tra và ghi dưới một khóa.

**Migration:**
- `AddDeviceRolesAndMemberships`: tạo 4 bảng, căn chỉnh bộ đếm `policy_versions`, xoá dòng grant `device-action` cũ.
- `AddPermissionGrantGrantor`: thêm 2 cột ghi người cấp vào `permission_grants`.
- Chưa áp dụng vào cơ sở dữ liệu; chỉ áp khi toàn bộ plan xong, sau khi sao lưu.

## 2. Vấn Đề Tìm Thấy và Sửa Chữa

### Lỗi EF Membership Upsert (Phát Hiện Qua Test)

Một test ghi nhận lỗi thật: EF membership upsert khi viết giá trị giống như trước không gửi UPDATE, nên token `RoleId` không bao giờ được kiểm tra. Store báo cáo thành công, nhưng admin khác đã thay đổi membership rồi. **Sửa:** cưỡng bách UPDATE bằng cách set `IsModified` trên `RoleId`.

### Xử Lý Ngoại Lệ Database (Vòng Review 1)

`EfDeviceRoleStore.Update` ném `DbUpdateException` thô, điều mà phase 3 sẽ biến thành HTTP 500 thay vì 409/404. Xảy ra khi PostgreSQL bác dòng quyền của một vai trò vừa bị người khác sửa hoặc xoá (ví dụ bấm gửi 2 lần, hoặc sửa trong lúc người khác xoá). Chưa có test nào chạy phần code map lỗi DB sang mã store.

**Sửa:** một đường ghi `DeviceAccessWrites.Save`:
- thay đổi, hàng audit và lưu trong một khối;
- bất kỳ lỗi nào cũng xóa change tracker;
- từ chối database sẽ chạy lại cùng các kiểm tra trên dữ liệu mới, không thì ném lỗi gốc.

### Test Race Condition

Test đua mới dùng `SaveChangesInterceptor` (`RaceInterceptor`): commit thật một lần ghi cạnh tranh, rồi làm `SaveChanges` thất bại đúng như PostgreSQL sẽ làm. Vòng review 2–3 lấp các gap test còn lại; chạy mutation cho thấy mỗi test mới bắt đúng lỗi nó nhắm tới.

**Lưu ý:** EF InMemory không có transaction. Test chỉ chứng minh được dòng audit và lần ghi bị từ chối nằm trong cùng một `SaveChanges`; việc rollback dựa vào transaction của PostgreSQL (môi trường này không có PostgreSQL).

## 3. Kiểm Chứng

| Hạng mục | Kết quả |
|---|---|
| Unit tests | 219 → 252 |
| Integration tests | 43 → 81 |
| Tất cả đều pass | ✓ |
| `has-pending-model-changes` | none |
| Frontend | không đổi |

## 4. Quyết Định Owner

- Duyệt phase 2 kèm rủi ro đã ghi nhận sau 3 vòng review (vòng cuối có giới hạn).
- Endpoint cấp grant hiện tại (`/api/v1/permissions/*`) giữ nguyên JSON shape; `grantedBy` tuần tự hóa thành `{value}` như các id khác.
- Design contract §6: quy tắc plain-Guid áp dụng vào DTO mới; frontend chấp nhận cả hai hình dạng.

## 5. Bài Học

1. EF không gửi UPDATE khi không có gì thay đổi, nên token đồng thời chỉ được kiểm tra nếu UPDATE bị cưỡng bách.
2. Một lần ghi dựa trên dữ liệu cũ có thể bị PostgreSQL chặn bằng constraint khác (khoá trùng hoặc FK trên dòng con) trước khi điều kiện phiên bản kịp chạy. Map lỗi phải chạy lại mọi check, không đoán theo kiểu exception.
3. Interceptor commit thật một lần ghi cạnh tranh rồi làm `SaveChanges` thất bại, nhờ vậy test được cách xử lý đua với DB mà không cần PostgreSQL thật.
4. Báo cáo reviewer và tester chứa số lượng và khẳng định sai; phải xác minh trước khi báo owner.

## 6. Tiếp Theo

- Phase 3: đánh giá quyền truy cập (access evaluation) và ủy quyền; sử dụng store này và map mã store.
- Tác vụ riêng: test flaky `SignalRConnectionBacklogTests.Emits_runtime_metrics_for_drop_retry_and_resynchronization` được đề nghị.
- Chưa commit: project không có git repo.
