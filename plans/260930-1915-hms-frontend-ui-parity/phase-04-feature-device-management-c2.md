---
phase: 4
title: "Feature Slices: Device Management & Safe Command Console"
status: complete
priority: P1
dependencies: [2, 3]
---

# Phase 04: Feature Slices: Device Management & Safe Command Console

## Overview
Hiện thực hóa module Quản lý thiết bị RTU và Bàn điều khiển lệnh an toàn (C2 Console) chuẩn Datacake Parity, bao gồm danh sách thiết bị hỗ trợ chế độ kép Bảng/Cây `DeviceListView.vue`, chi tiết thiết bị đa tab `DeviceDetailView.vue`, và modal phát lệnh an toàn 2 bước `DeviceCommandModal.vue`.

## Requirements
- **Functional**:
  - `DeviceListView.vue`:
    - Tích hợp nút chuyển đổi chế độ xem kép: Bảng phẳng (`DataTable.vue`) hoặc Cây phân cấp tổ chức (`TreeDataTable.vue`).
    - Tích hợp `FilterBar.vue`: Tìm kiếm từ khóa theo MAC, Tên thiết bị; các tab lọc trạng thái (`All`, `Active`, `Provisioning`, `Revoked`).
    - Cột hiển thị: Tên thiết bị, Mã định danh/MAC, Thuộc tổ chức, Dự án/Phân khu, Trạng thái hoạt động (Status Dot), Thế hệ khóa (`Key Generation`), Hành động nhanh (Xem chi tiết, Phát lệnh, Thu hồi).
    - Nút bấm hành động: Cấp phát thiết bị mới (`Provision RTU`), Làm mới danh sách, Xuất dữ liệu.
  - `DeviceDetailView.vue`:
    - Header thiết bị: Tên RTU, Device ID, MAC, Trạng thái kết nối thời gian thực (SignalR Status Dot), Thế hệ khóa hiện tại, Nút quay lại (`canGoBack`).
    - Hệ thống 5 Tab chức năng chuẩn Datacake:
      1. **Tab 1: Tổng quan (Telemetry & Health)**: Trạng thái pin, cường độ sóng 4G/LTE, nhiệt độ, dung lượng thẻ nhớ SD.
      2. **Tab 2: Điều khiển C2 (Command Console)**: Danh mục các lệnh có sẵn từ `adminApi.commandCatalog()`, nút mở `DeviceCommandModal.vue`.
      3. **Tab 3: Bộ Khóa & Cấp Phát (Keyset & Provisioning)**: Nút tạo gói cấu hình (`Create Bundle`), nút tải gói tệp an toàn trong RAM (`secureDownload.ts`).
      4. **Tab 4: Bản Quyền Thiết Bị (Device License)**: Kiểm tra chữ ký KMS, tính năng cấp phép và ngày hết hạn. Hỗ trợ áp dụng license mới (`applyLicense`).
      5. **Tab 5: Lịch Sử Lệnh (Command History)**: Bảng ghi nhận các lệnh đã phát, mã tương quan `correlationId`, trạng thái phản hồi từ thiết bị.
  - `DeviceCommandModal.vue`:
    - Tự động gọi `verifyCatalog()` kiểm tra mã băm SHA-256 (`catalogHash`) trước khi mở form nhập liệu lệnh.
    - Cảnh báo và khóa nút nếu danh mục lệnh bị lệch (Catalog Drift $\rightarrow$ Yêu cầu reload).
    - Đối với lệnh nguy hiểm (Reboot, Reset, Format SD): Tích hợp `DangerConfirmDialog.vue` yêu cầu xác nhận 2 bước rõ ràng.
    - Trạng thái gửi lệnh tích hợp AbortController với session lease: Hủy bỏ ngay lập tức nếu phiên đổi hoặc người dùng đóng modal.
  - Tích hợp SignalR Hub:
    - Khi mở `DeviceDetailView.vue`, tự động gọi `operationsHub.joinDevice(deviceId)`.
    - Khi rời khỏi component (hook `onUnmounted`), bắt buộc gọi `operationsHub.leaveDevice(deviceId)` để giải phóng subscription.
- **Non-Functional**:
  - Tuân thủ giới hạn tối đa 50 thiết bị được theo dõi đồng thời.
  - Phản hồi trạng thái lệnh điều khiển trong vòng < 200ms.

## 12-Dimension Edge Cases & Scenarios
- **D1 (Null/Zero)**: Thiết bị chưa được gán vào dự án hoặc phân khu (`projectId == null`) $\rightarrow$ Hiển thị nhãn "(Chưa phân bổ)" với màu xám mờ thay vì chuỗi trống.
- **D4 (Concurrency)**: Lập trình viên mở 2 tab trình duyệt cùng phát lệnh trên 1 thiết bị $\rightarrow$ Mã tương quan `correlationId` duy nhất và SignalR cập nhật trạng thái đồng thời trên cả 2 tab.
- **D5 (State Transitions)**: Thiết bị chuyển sang trạng thái `Revoked` trong khi người vận hành đang xem màn hình chi tiết $\rightarrow$ SignalR bắn sự kiện `DeviceRevoked`, giao diện lập tức khóa toàn bộ nút phát lệnh và hiển thị thông báo đỏ.
- **D10 (Data Integrity)**: Mã băm catalog trên máy chủ bị thay đổi $\rightarrow$ `DeviceCommandModal` phát hiện `CATALOG_STALE`, tự động vô hiệu hóa lệnh và yêu cầu người dùng đồng bộ lại catalog.

## Related Code Files
- **[NEW]**:
  - `frontend/src/features/devices/views/DeviceListView.vue`
  - `frontend/src/features/devices/views/DeviceDetailView.vue`
  - `frontend/src/features/devices/components/DeviceCommandModal.vue`
  - `frontend/src/features/devices/components/DeviceTelemetryTab.vue`
  - `frontend/src/features/devices/components/DeviceProvisioningTab.vue`
  - `frontend/src/features/devices/components/DeviceLicenseTab.vue`
  - `frontend/src/features/devices/components/DeviceHistoryTab.vue`

## Test Plan (TDD Red-First Checklist)
- [ ] `tests/DeviceListView.test.ts`: Kiểm tra chuyển đổi chế độ xem Bảng và Cây, lọc theo trạng thái và tìm kiếm từ khóa.
- [ ] `tests/DeviceDetailView.test.ts`: Kiểm tra chuyển đổi 5 tab chức năng và gọi `joinDevice` / `leaveDevice` trên SignalR Hub khi mount/unmount.
- [ ] `tests/DeviceCommandModal.test.ts`: Kiểm tra cơ chế chặn gửi lệnh nếu mã băm catalog không khớp (`catalogHash verification`).
- [ ] `tests/DeviceCommandModal.test.ts`: Kiểm tra hiển thị hộp thoại xác nhận 2 bước cho các lệnh nguy hiểm (`dangerous === true`).
