# BÁO CÁO NGHIÊN CỨU & KHẢO SÁT TIỀN LỆ KIẾN TRÚC UI/UX (RESEARCH REPORT)
## ĐỒNG BỘ 100% GIAO DIỆN HMS-SERVER THEO CHUẨN NGUỒN CONCRETE-STRESS-MONITORING (DATACAKE PARITY)

> **Dự án**: HMS-SERVER (Enterprise Industrial IoT & SCADA Management Platform)  
> **Chủ đề khảo sát**: `D:\WORKSPACE\PROJECT\concrete-stress-monitoring\sources\frontend`  
> **Mục tiêu**: Thiết kế kiến trúc đồng bộ 100% giao diện người dùng theo chuẩn thiết kế nguồn (Datacake Parity, FSD, Tailwind CSS v4, Zinc Dark / Light Theme, Reka UI), bảo tồn toàn vẹn 100% các logic nghiệp vụ và ranh giới an toàn hiện hành.  
> **Trạng thái**: KHẢO SÁT CHUYÊN SÂU & ĐẶC TẢ KIẾN TRÚC

---

## 1. Phân Tích Hiện Trạng & Ranh Giới Kỹ Thuật

### 1.1. Hiện Trạng Giao Diện HMS-SERVER Hiện Hành
Codebase frontend hiện hành tại `HMS-SERVER/frontend`:
- **Kiến trúc**: Cấu trúc phẳng tập trung trong một file nguyên khối `src/App.vue` (1,051 dòng), đi kèm file phong cách tối giản `src/styles.css` (265 dòng).
- **Trạng thái & Logic**: Logic nghiệp vụ cực kỳ vững chắc, đã được kiểm thử 74/74 unit/component vitest tests bao gồm:
  - Xác thực CSRF Token & Session Authentication Cookie.
  - Phân quyền 3 lớp (Feature, Resource Scope, Device Action) với cơ chế Master-Admin wildcard `*`.
  - Session Fence (`sessionFence.ts`) bảo vệ dữ liệu khi đổi phiên đăng nhập.
  - Command Catalog Verification (`catalogVerification.ts`) với hash verification chống catalog drift.
  - Bounded Realtime Hub (`useDeviceOperationsHub.ts` + `signalRConnection.ts`) qua WebSocket SignalR.
  - Tải bundle an toàn không lưu cache trình duyệt (`secureDownload.ts`).
- **Điểm nghẽn UI/UX**:
  - Giao diện dạng trang kiểm thử kỹ thuật (Dev Console), chưa đạt chuẩn Dashboard SCADA / IoT công nghiệp hiện đại.
  - Chưa có hệ thống Layout hoàn chỉnh (thiếu Sidebar thu gọn, Breadcrumb header, Alarm Bell, User Dropdown, Theme Switcher, Multi-locale).
  - Chưa có Router đa trang (`vue-router`), toàn bộ hiển thị bằng biến cờ `v-if="activeSection === '...'"` trong một file.
  - Bảng dữ liệu chưa có phân trang, sắp xếp cột, tìm kiếm nâng cao, hoặc hiển thị cây phân cấp quan hệ (Hierarchy Tree).

### 1.2. Kiến Trúc Chuẩn Nguồn: `concrete-stress-monitoring/sources/frontend`
Giao diện nguồn được xây dựng theo chuẩn **Datacake Industrial IoT Platform Parity**:
- **Cấu trúc FSD (Feature-Sliced Design)**:
  - `app/`: Entry point, Router, Navigation Config, Global Styles, Token Themes.
  - `core/`: Auth interfaces, Permissions contracts, Network baseline.
  - `shared/`: Thư viện Component cơ sở hoàn chỉnh (`ui/`, `layouts/`, `notify/`, `theme/`, `i18n/`, `navigation/`).
  - `entities/`: Mô hình miền dữ liệu dùng chung (`hierarchy`, `rtu`, `modbus-slave`, `signal`).
  - `features/`: Các lát cắt tính năng độc lập (`auth`, `devices`, `company`, `project`, `package`, `area`, `roles`, `users`, `audit-log`).
- **Hệ Thống Design Tokens (CSS Variables)**:
  - Không biên dịch cứng class màu sắc vào bundle (tránh dùng `dark:` inline của Tailwind để đổi theme mà không cần rebuild).
  - Khai báo token qua CSS variables tại `:root` (Light Theme) và `html[data-theme='dark']` (Zinc Dark Theme).
  - **Bảng màu Zinc Dark**: Nền Zinc-900 (`#18181b`), Card/Viền Zinc-800 (`#27272a`), Màu nhấn thao tác Emerald (`#10b981`), Màu trạng thái ngữ nghĩa: Success (`#34d399`), Danger (`#f87171`), Warning (`#fbbf24`), Info (`#38bdf8`).
- **Thư Viện Component Chuẩn Mực**:
  - `AppShell.vue`: Layout 3 phần với Sidebar thu gọn (Collapsed Rail Mode, phím tắt `Ctrl+[` hoặc `Alt+B`), Header tích hợp Breadcrumb động + nút quay lại (`canGoBack`) + AlarmBell + Theme/Locale Switcher + UserDropdown.
  - `DataTable` & `TreeDataTable`: Bảng phân cấp cây cho hạ tầng Thiết bị / Đơn vị / Dự án, hỗ trợ Selection, Sorting, Pagination, PageSizeSelect.
  - `FilterBar`: Toolbar tìm kiếm với debounce, tab phân loại nhanh, URL query synchronization.
  - `Modal` & `DangerConfirmDialog`: Dialog an toàn hỗ trợ Focus Trap, Scroll Lock, phím Escape, xác nhận 2 bước cho các hành vi phá hủy (Revoke, Delete).

---

## 2. Nghiên Cứu Tiền Lệ Kiến Trúc Sản Phẩm Công Nghiệp Lớn

Để đảm bảo thiết kế đạt đẳng cấp quốc tế, chúng tôi khảo sát 3 nền tảng IoT & SCADA mã nguồn mở và thương mại hàng đầu:

### 2.1. Datacake IoT Platform
- **Mô hình**: Thiết kế chuyên biệt cho thiết bị LoRaWAN, Cellular 4G RTU và Modbus Gateway.
- **Điểm mạnh áp dụng**:
  - Cấu trúc thanh bên trái tối giản (Sidebar Rail) tiết kiệm không gian quan trắc biểu đồ.
  - Breadcrumb nằm trực tiếp trên Header chính, tích hợp nút điều hướng lùi (Back Chevron) giữ mạch ngữ cảnh khi duyệt sâu vào cấu hình RTU.
  - Phân tách rõ ràng giữa trạng thái thiết bị phẳng (Flat Device Inventory) và quan hệ cấu trúc vật lý (Topology Hierarchy).

### 2.2. ThingsBoard Enterprise IoT Platform
- **Mô hình**: Quản lý hàng triệu thiết bị đa tổ chức (Multi-tenant Hierarchy: Tenant -> Customer -> Asset -> Device).
- **Điểm tương đồng với HMS-SERVER**:
  - Mô hình Master Organization -> Sub-organizations -> Projects -> Packages -> Areas -> Devices.
  - Quản trị bảo mật cấp phát khóa (Provisioning Credentials & Keysets) phân lập cho từng thiết bị.
  - Nhật ký kiểm toán thao tác (Audit Trail) cho toàn bộ các lệnh phát xuống thiết bị.

### 2.3. Grafana Enterprise UI System
- **Mô hình**: Dashboard quan trắc thời gian thực, bảng Tag Modbus và Alert Management.
- **Điểm mạnh áp dụng**:
  - Hệ thống trạng thái màu sắc chuẩn WCAG AA: Đảm bảo độ tương phản của Status Dot và Status Badge trên cả nền tối và sáng.
  - Drawer Panel / Sliding Sheet: Mở rộng chi tiết thiết bị mà không làm mất vị trí cuộn của bảng danh sách chính.

---

## 3. Bản Đồ Ánh Xạ Tính Năng (Mapping Matrix: HMS $\rightarrow$ Concrete Frontend)

| Tính Năng Nghiệp Vụ HMS-SERVER | Component & View Hiện Tại | Component & View Tương Ứng Trong Chuẩn Nguồn | Ghi Chú Kiến Trúc |
|---|---|---|---|
| **Đăng nhập & Phiên làm việc** | Modal login inline trong `App.vue` | `features/auth/views/LoginView.vue` + `shared/layouts/AuthLayout.vue` | Chuyển thành trang Login chuyên biệt, bảo toàn CSRF & Antiforgery, chuyển hướng sau đăng nhập |
| **Tổng quan Hệ thống (Overview)** | Tab `activeSection === 'Overview'` | `app/views/HomeView.vue` + `features/monitoring/OverviewTab.vue` | Hiển thị thẻ chỉ số hạm đội, trạng thái Broker/Transport, phím tắt nhanh |
| **Quản lý Thiết bị RTU** | Tab `activeSection === 'Devices'` (Bảng phẳng đơn sơ) | `features/devices/views/DeviceListView.vue` (Table + Tree toggle) | Tích hợp `TreeDataTable` và `DataTable`, Filter tabs (Active/Provisioning/Revoked), Search debounce |
| **Chi Tiết & Bảng Điều Khiển RTU** | Khối gửi lệnh và danh sách thiết bị | `features/devices/views/DeviceDetailView.vue` + `DeviceCommandModal.vue` | Trang chi tiết thiết bị với các tab: Thông tin chung, Lệnh điều khiển (2-step confirm), Cấp phát Bundle, License, Lịch sử lệnh |
| **Cấu Trúc Phân Cấp (Hierarchy)** | Quản lý Organizations/Projects/Packages/Areas rời rạc | `features/data-declaration/views/DataDeclarationView.vue` + `features/company/`, `project/`, `area/` | Cây thư mục phân cấp trực quan, Modal thêm/sửa từng cấp với kiểm tra ràng buộc sở hữu |
| **Phân Quyền & Người Dùng** | `PermissionGrantEditor.vue` inline | `features/user/`, `features/role/` + `PermissionGrantMatrix.vue` | Giao diện quản lý người dùng, gán vai trò, ma trận Allow/Deny 3 lớp trực quan với thẻ màu trạng thái |
| **Giao Cảm Thời Gian Thực (SignalR)** | `useDeviceOperationsHub.ts` | `shared/realtime/useDeviceOperationsHub.ts` + `AlarmBell.vue` | Giữ 100% logic SignalR Hub, hiển thị chuông báo động trên thanh header và cập nhật Status Dot tức thì |

---

## 4. Kết Luận Nghiên Cứu & Khuyến Nghị Kiến Trúc
1. **Kiến trúc đồng bộ hoàn toàn khả thi**: Cả 2 dự án đều sử dụng Vue 3 + TypeScript + Vite. Toàn bộ hệ thống UI components của `concrete-stress-monitoring` có thể được kế thừa và đóng gói thành hệ thống UI chuẩn mực cho `HMS-SERVER`.
2. **Quy tắc bảo tồn logic 100%**: Tuyệt đối không thay đổi contract API backend (`adminApi.ts`), giữ nguyên các cơ chế an toàn (`sessionFence`, `catalogVerification`, `requestRevision`, `secureDownload`). Các views mới chỉ đơn thuần đóng vai trò lớp trình diễn (Presentation Layer) gắn vào các client/stores đã được kiểm chứng.
3. **Tiến trình triển khai khuyến nghị**: Triển khai theo mô hình chuyển đổi không rủi ro: Lập đặc tả RFC chi tiết $\rightarrow$ Bổ sung router và design system $\rightarrow$ Di chuyển từng view từ `App.vue` thành các feature views độc lập $\rightarrow$ Chạy kiểm thử vitest và typecheck bảo đảm 100% pass.
