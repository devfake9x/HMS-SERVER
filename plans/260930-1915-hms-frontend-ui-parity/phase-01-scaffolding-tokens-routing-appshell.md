---
phase: 1
title: "Scaffolding, Design Tokens, Routing, & AppShell Layout"
status: complete
priority: P1
dependencies: []
---

# Phase 01: Scaffolding, Design Tokens, Routing, & AppShell Layout

## Overview
Thiết lập nền móng kiến trúc Feature-Sliced Design (FSD), hệ thống Design Tokens CSS Variables (Zinc Dark & Datacake Light), cấu hình Vue Router 4 với Navigation Guards bảo vệ phiên, và khung sườn `AppShell.vue` với Sidebar Rail thu gọn (`Ctrl+[`) và Header Breadcrumbs động.

## Requirements
- **Functional**:
  - Tách thư mục `src/` thành chuẩn FSD: `app/`, `core/`, `shared/`, `entities/`, `features/`.
  - Di chuyển toàn bộ các tệp tin logic bảo mật cốt lõi (`adminApi.ts`, `sessionFence.ts`, `catalogVerification.ts`, `secureDownload.ts`, `useDeviceOperationsHub.ts`, `signalRConnection.ts`, `realtimeAuthorization.ts`, `permissions.ts`, `deviceFilters.ts`, `commandAvailability.ts`, `deviceInventoryState.ts`, `redact.ts`, `requestRevision.ts`) vào `src/core/` mà không làm thay đổi bất kỳ logic nội bộ nào.
  - Cài đặt `vue-router` và thiết lập hệ thống định tuyến phân cấp:
    - `/login`: Giao diện xác thực độc lập (`AuthLayout.vue`).
    - `/`: Chuyển hướng tự động đến `/monitoring/overview`.
    - `/monitoring/overview`, `/monitoring/dashboard`, `/monitoring/live-map`.
    - `/devices`, `/devices/:id`.
    - `/data-declaration`.
    - `/alarms`, `/audit-logs`.
    - `/admin/users`, `/admin/roles-permissions`, `/admin/licenses`.
  - Xây dựng `src/app/config/nav.config.ts` chứa định nghĩa 4 nhóm menu công nghiệp.
  - Triển khai `src/shared/layouts/AppShell.vue`:
    - Thanh bên thu gọn (Sidebar Rail): 64px khi thu gọn, 260px khi mở rộng, phím tắt `Ctrl+[` hoặc `Alt+B`, tooltip khi hover, nhớ trạng thái trong `localStorage`.
    - Header Bar: Breadcrumb động theo route hierarchy, nút quay lại (`canGoBack`), AlarmBell với popover số đếm sự cố, Theme Switcher (Dark/Light), Locale Switcher (vi/en), User Dropdown (thông tin tài khoản, vai trò `master-admin`, nút Đăng xuất an toàn).
  - Triển khai Zero-FOUC injection script trong `index.html` và file `src/app/styles/tokens.css`.
- **Non-Functional**:
  - Thời gian chuyển đổi theme < 16ms (không giật khung hình).
  - Bundle size gia tăng < 30 KB.
  - Zero hydration flicker hoặc Flash of Unstyled Content (FOUC).

## 12-Dimension Edge Cases & Scenarios
- **D1 (User Types)**: Người dùng chưa đăng nhập truy cập trực tiếp route `/devices` $\rightarrow$ Navigation Guard chặn và chuyển hướng về `/login?redirect=%2Fdevices`.
- **D4 (Scale)**: Sidebar mở rộng/thu gọn liên tục 50 lần $\rightarrow$ Không rò rỉ event listener, transition CSS mượt mà không vỡ layout bảng bên phải.
- **D5 (State Transitions)**: F5 làm mới trang ở bất kỳ route nào $\rightarrow$ Giữ nguyên trạng thái theme đã chọn từ `localStorage` và nạp lại đúng route hiện hành.
- **D6 (Environment)**: Trình duyệt tắt JavaScript hoặc chặn localStorage $\rightarrow$ Theme mặc định an toàn sang Zinc Dark, không làm crash ứng dụng.
- **D8 (Authorization)**: Người dùng thường cố tình gõ URL `/admin/roles-permissions` $\rightarrow$ Route guard kiểm tra permission claim và hiển thị 403 Forbidden.

## Related Code Files
- **[NEW]**:
  - `frontend/src/app/router/index.ts`
  - `frontend/src/app/router/routes.ts`
  - `frontend/src/app/config/nav.config.ts`
  - `frontend/src/app/styles/tokens.css`
  - `frontend/src/app/styles/main.css`
  - `frontend/src/shared/layouts/AppShell.vue`
  - `frontend/src/shared/layouts/AuthLayout.vue`
  - `frontend/src/shared/composables/useSidebarCollapse.ts`
  - `frontend/src/shared/composables/useBreadcrumbs.ts`
  - `frontend/src/shared/composables/useTheme.ts`
  - `frontend/src/shared/ui/navigation/SidebarRail.vue`
  - `frontend/src/shared/ui/navigation/Breadcrumb.vue`
  - `frontend/src/shared/ui/primitives/AlarmBell.vue`
- **[MODIFY]**:
  - `frontend/index.html` (Thêm inline Zero-FOUC script)
  - `frontend/src/main.ts` (Khởi tạo router và import tokens)
  - `frontend/package.json` (Thêm dependency `vue-router`)
  - Đường dẫn import trong các tệp tin tests để trỏ đúng vào `src/core/`.

## Test Plan (TDD Red-First Checklist)
- [ ] `tests/routerGuards.test.ts`: Kiểm tra Navigation Guard chuyển hướng về `/login` nếu chưa có phiên đăng nhập hợp lệ.
- [ ] `tests/useSidebarCollapse.test.ts`: Kiểm tra toggle collapse, phím tắt `Ctrl+[` và ghi nhận `localStorage`.
- [ ] `tests/useBreadcrumbs.test.ts`: Kiểm tra tạo breadcrumb chính xác từ route path lồng nhau (`/devices/:id/commands`).
- [ ] `tests/useTheme.test.ts`: Kiểm tra chuyển đổi `data-theme` giữa `dark` và `light` đồng thời cập nhật class `dark` trên `document.documentElement`.
- [ ] Tất cả 74 vitest tests hiện hành phải tiếp tục **PASS 100%**.
