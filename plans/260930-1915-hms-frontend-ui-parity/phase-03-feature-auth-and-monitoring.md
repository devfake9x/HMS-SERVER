---
phase: 3
title: "Feature Slices: Authentication & Monitoring Views"
status: complete
priority: P1
dependencies: [1, 2]
---

# Phase 03: Feature Slices: Authentication & Monitoring Views

## Overview
Hiện thực hóa lát cắt tính năng Xác thực đăng nhập `features/auth/views/LoginView.vue` độc lập chuẩn Datacake Parity và màn hình Tổng quan hệ thống `features/monitoring/views/OverviewView.vue`, tích hợp trực tiếp với tầng bảo mật `core/api/adminApi.ts` và `core/auth/sessionFence.ts`.

## Requirements
- **Functional**:
  - `LoginView.vue` + `AuthLayout.vue`:
    - Giao diện thẻ đăng nhập tập trung, nền tối Zinc-950 kết hợp gradient Emerald, hiển thị logo và tiêu đề hệ thống.
    - Trường nhập liệu Email và Password kèm validation định dạng phía client.
    - Hộp chọn "Ghi nhớ phiên đăng nhập" (`rememberMe`).
    - Quy trình xác thực an toàn:
      1. Tự động lấy CSRF Token từ `getCsrf()` trước khi submit.
      2. Gọi `login(email, password, { csrfToken })` gửi cookie xác thực an toàn.
      3. Xoay vòng phiên (`sessionFence.rotate()`) để triệt tiêu toàn bộ rò rỉ dữ liệu phiên cũ.
      4. Nạp thông tin người dùng từ `adminApi.permissions()` và chuyển hướng về URL trước đó hoặc `/monitoring/overview`.
    - Hiển thị thông báo lỗi rõ ràng nếu sai mật khẩu hoặc tài khoản bị khóa (`INVALID_CREDENTIALS`).
  - `OverviewView.vue`:
    - Thẻ chỉ số tổng quan hạm đội RTU (Fleet KPI Cards):
      - Tổng số thiết bị RTU.
      - Thiết bị trực tuyến (Online Dot).
      - Thiết bị mất kết nối (Offline).
      - Thiết bị có cảnh báo / lỗi (Alert / Danger).
    - Thẻ trạng thái kết nối máy chủ & Broker (Broker / Transport Status): Hiển thị trạng thái Deferred/Active của MQTT Broker và kết nối SignalR Hub.
    - Phím tắt hành động nhanh (Quick Actions): Nút dẫn đến Cấp phát thiết bị mới, Quản lý phân quyền, và Xem nhật ký kiểm toán.
- **Non-Functional**:
  - Thời gian phản hồi đăng nhập hiển thị loading spinner ngay lập tức (< 50ms).
  - Khóa toàn bộ form khi đang trong quá trình xác thực.

## 12-Dimension Edge Cases & Scenarios
- **D1 (Null/Undefined)**: Người dùng bấm Đăng nhập khi để trống trường email hoặc password $\rightarrow$ Báo lỗi validation đỏ ngay tại input mà không gửi network request.
- **D6 (Auth Bypass)**: Kẻ xấu cố tình đổi URL thành `/monitoring/overview` khi cookie đã hết hạn $\rightarrow$ API trả về 401, session fence kích hoạt invalidate, chuyển hướng người dùng về `/login`.
- **D8 (Latency)**: Kết nối mạng chậm trong khi đăng nhập $\rightarrow$ Nút bấm chuyển sang spinner "Đang xác thực...", vô hiệu hóa click lặp lại, cho phép hủy bỏ nếu quá timeout 10 giây.
- **D10 (Data Integrity)**: Đổi phiên đăng nhập từ tài khoản `operator` sang `master-admin` $\rightarrow$ `sessionFence` xóa sạch toàn bộ reactive cache, đảm bảo `master-admin` nạp đúng quyền wildcard `*`.

## Related Code Files
- **[NEW]**:
  - `frontend/src/features/auth/views/LoginView.vue`
  - `frontend/src/features/monitoring/views/OverviewView.vue`
  - `frontend/src/features/monitoring/components/MetricCard.vue`
  - `frontend/src/features/monitoring/components/TransportStatusCard.vue`
- **[MODIFY]**:
  - `frontend/src/app/router/routes.ts` (Gắn component view thực tế cho route `/login` và `/monitoring/overview`)

## Test Plan (TDD Red-First Checklist)
- [ ] `tests/LoginView.test.ts`: Kiểm tra lấy CSRF token, gửi request đăng nhập và kích hoạt xoay vòng phiên `sessionFence.rotate()`.
- [ ] `tests/LoginView.test.ts`: Kiểm tra hiển thị thông báo lỗi khi API trả về mã lỗi 401 `INVALID_CREDENTIALS`.
- [ ] `tests/OverviewView.test.ts`: Kiểm tra tính toán chính xác số lượng thiết bị Online, Offline, và trạng thái Broker từ danh sách thiết bị.
