---
phase: 5
title: "Feature Slices: Hierarchy & Access Control"
status: complete
priority: P2
dependencies: [2, 3]
---

# Phase 05: Feature Slices: Hierarchy & Access Control

## Overview
Hiện thực hóa module Quản trị cấu trúc hạ tầng phân cấp `features/data-declaration/views/HierarchyView.vue` và module Quản trị người dùng & Phân quyền bảo mật 3 lớp `features/security/views/UserManagementView.vue` cùng `RolePermissionView.vue` (tích hợp ma trận phân quyền `PermissionGrantMatrix.vue`).

## Requirements
- **Functional**:
  - `HierarchyView.vue`:
    - Hiển thị cây phân cấp đa tầng trực quan: `Tổ chức (Organization) -> Dự án (Project) -> Gói thầu (Package) -> Phân khu (Area) -> Trạm RTU (Device)`.
    - Thao tác quản trị trên từng nút cây: Thêm mới con (Add child), Đổi tên (Rename), Chỉnh sửa thuộc tính, Xóa (Delete kèm kiểm tra ràng buộc không có con).
    - Modal biểu mẫu thêm/sửa tổ chức, dự án, gói thầu, phân khu tích hợp xác thực CSRF Token.
    - Bộ đếm chỉ số thiết bị trực tiếp trên từng nhánh cây (ví dụ: `Station Alpha (12 RTU)`).
  - `UserManagementView.vue`:
    - Bảng danh sách người dùng với các thông tin: Email, Trạng thái xác thực email (`Confirmed`), Các vai trò được gán (`Roles`), Tổ chức trực thuộc.
    - Modal tạo tài khoản người dùng mới: Nhập email, mật khẩu ban đầu, chọn tổ chức.
    - Modal gán vai trò: Hỗ trợ gán vai trò `master-admin`, `operator`, `viewer`.
  - `RolePermissionView.vue` & `PermissionGrantMatrix.vue`:
    - Kế thừa và nâng cấp component `PermissionGrantEditor.vue` thành `PermissionGrantMatrix.vue` đạt chuẩn Datacake Parity.
    - Ma trận 3 tầng phân quyền:
      1. **Tầng 1: Quyền Tính Năng (Feature Permissions)**: Cấp phép hoặc Từ chối (Allow / Deny) cho từng quyền (`devices.manage`, `commands.execute`, `licenses.manage`, `users.manage`).
      2. **Tầng 2: Phạm Vi Tài Nguyên (Resource Scopes)**: Cấp quyền hoặc Chặn quyền trên từng Tổ chức cụ thể. Tự động áp dụng mở rộng cây tổ chức (`ScopeResolver`).
      3. **Tầng 3: Hành Động Thiết Bị (Device Actions)**: Cấp phép hành động chi tiết trên từng thiết bị cụ thể.
    - Thẻ màu sắc phân biệt trực quan: Xanh lá cho Allow, Đỏ cảnh báo cho Deny, Viền vàng cho quyền thừa kế từ vai trò.
- **Non-Functional**:
  - Thao tác gán quyền lập tức cập nhật `policyVersion` phía backend.
  - Phản hồi lưu phân quyền < 100ms.

## 12-Dimension Edge Cases & Scenarios
- **D1 (Null/ParentId)**: Tạo tổ chức cấp cao nhất (Root Organization) có `parentId == null` $\rightarrow$ Xử lý chính xác không ném lỗi null reference.
- **D4 (Concurrency)**: Hai quản trị viên cùng sửa quyền của một người dùng $\rightarrow$ Backend kiểm soát phiên bản `policyVersion`, frontend hiển thị thông báo làm mới nếu có xung đột phiên bản.
- **D6 (Authorization Bypass)**: Người dùng thuộc vai trò `operator` cố tình mở màn hình phân quyền $\rightarrow$ Bị chặn bởi chính sách `hms.permissions.manage` và `[Authorize(Policy = "hms.permissions.manage")]`.
- **D10 (Data Integrity)**: Xóa một tổ chức đang chứa 3 thiết bị RTU $\rightarrow$ Hệ thống hiển thị cảnh báo chặn và không cho phép xóa cho đến khi các thiết bị được di chuyển sang tổ chức khác.

## Related Code Files
- **[NEW]**:
  - `frontend/src/features/data-declaration/views/HierarchyView.vue`
  - `frontend/src/features/data-declaration/components/HierarchyTreeNode.vue`
  - `frontend/src/features/data-declaration/components/CreateEntityModal.vue`
  - `frontend/src/features/security/views/UserManagementView.vue`
  - `frontend/src/features/security/views/RolePermissionView.vue`
  - `frontend/src/features/security/components/PermissionGrantMatrix.vue`
  - `frontend/src/features/security/components/CreateUserModal.vue`
  - `frontend/src/features/security/components/AssignRoleModal.vue`
- **[MODIFY]**:
  - `frontend/src/app/router/routes.ts` (Gắn route `/data-declaration`, `/admin/users`, `/admin/roles-permissions`)

## Test Plan (TDD Red-First Checklist)
- [ ] `tests/HierarchyView.test.ts`: Kiểm tra hiển thị cấu trúc cây lồng nhau và kích hoạt modal tạo entity mới.
- [ ] `tests/UserManagementView.test.ts`: Kiểm tra nạp danh sách tài khoản, mở modal tạo user và gán vai trò.
- [ ] `tests/PermissionGrantMatrix.test.ts`: Kiểm tra thao tác cấp quyền Allow/Deny trên 3 tầng (Feature, Scope, Action) và gửi request an toàn.
