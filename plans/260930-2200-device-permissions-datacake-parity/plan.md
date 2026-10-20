# KẾ HOẠCH TRIỂN KHAI: ĐỒNG BỘ PHÂN QUYỀN THIẾT BỊ THEO CHUẨN DATACAKE PARITY (100% PARITY)
**Mã kế hoạch**: `PLAN-DEVICE-PERMISSIONS-PARITY-2026`  
**Thư mục**: `plans/260930-2200-device-permissions-datacake-parity/`  
**Chủ đề**: Triển khai giao diện và API ma trận phân quyền thiết bị chuẩn Datacake (`/devices/:id/permissions`), tích hợp RTU thực tế và giả lập động  
**Trọng tâm**: Fullstack Rigor (100% FE + 100% BE)  
**Trạng thái**: **COMPLETED (HOÀN THÀNH 100% TOÀN BỘ CÁC PHASE)**
**Bị thay thế bởi**: `plans/261005-0556-device-permissions-what-where/` — ma trận 4 quyền và `DevicePermissionsController` được thay bằng vai trò theo tổ chức + membership theo thiết bị.

---

## 1. Kết Quả Triển Khai Các Phase (Phase Completion Matrix)

| Phase | Trọng Tâm | Tệp Tin Tác Động | Kết Quả Nghiệm Thu | Trạng Thái |
| :---: | :--- | :--- | :--- | :---: |
| **Phase 1** | **Backend API & Handlers** | `DevicePermissionsController.cs`, `ManagementApiTests.cs` | Endpoints `GET` & `PUT /api/v1/devices/{id}/permissions` trả về ma trận 4 quyền (`view`, `edit_basics`, `edit_product`, `record_measurements`), tính toán kế thừa từ Org/Role. 134/134 backend tests PASS. | **DONE** |
| **Phase 2** | **Frontend API & Contracts** | `frontend/src/adminApi.ts`, `frontend/src/core/api/adminApi.ts`, `adminApi.test.ts` | Khai báo types và client methods `devicePermissions()`, `updateDevicePermissions()`. Test mock API PASS. | **DONE** |
| **Phase 3** | **Component `DevicePermissionsTab.vue`** | `DevicePermissionsTab.vue`, `DevicePermissionsTab.test.ts` | Bảng ma trận chuẩn Datacake với `Checkbox.vue`, select-all cột, tooltip quyền thừa kế từ Workspace, optimistic toggle. Test TDD 6/6 PASS. | **DONE** |
| **Phase 4** | **Tích hợp `DeviceDetailView.vue`** | `DeviceDetailView.vue`, `DeviceDetailView.test.ts`, `routes.ts` | Bổ sung tab `Permissions`, metadata strip (`Serial Number`, `Last update`, `Tags`), breadcrumb Datacake `Fleet > <DeviceName>`. Test PASS. | **DONE** |
| **Phase 5** | **Trình Giả Lập RTU Động (Virtual Simulator)** | `DeviceDetailView.vue`, `DeviceListView.vue` | Khả năng tạo và kích hoạt thiết bị ảo giả lập telemetry qua SignalR/MQTT không hardcode. Thiết bị thật tại `http://maitech-rtu.local/login` phản hồi HTTP 200/405 sẵn sàng. | **DONE** |
| **Phase 6** | **Hồi Quy & Xác Thực Trực Quan (Chrome CDP)** | Toàn bộ test suites, Chrome port 9222 | 128/128 frontend vitest tests PASS, 134/134 dotnet tests PASS, 0 lỗi build (`vue-tsc --noEmit && vite build`), chụp ảnh màn hình kiểm chứng trực tiếp trên live app. | **DONE** |

---

## 2. Minh Chứng Kiểm Chứng Trực Quan (Visual Verification Evidence)
1. **Light Mode Permissions Tab**: `device_permissions_tab_live.png`
2. **Unsaved State Tracking (1 unsaved, Orange Save Button Active)**: `device_permissions_unsaved_live.png`
3. **Saved State with Concurrency & CSRF Protected (Policy v1 Banner)**: `device_permissions_saved_live.png`
4. **Dark Mode WCAG Contrast Compliant**: `device_permissions_dark_live.png`
