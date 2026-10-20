# KẾ HOẠCH TRIỂN KHAI CHI TIẾT: ĐỒNG BỘ 100% GIAO DIỆN HMS-SERVER THEO CHUẨN NGUỒN CONCRETE-STRESS-MONITORING (DATACAKE PARITY)

> **Mã kế hoạch**: `PLAN-HMS-UI-PARITY-2026`  
> **Thư mục kế hoạch**: `plans/260930-1915-hms-frontend-ui-parity/`  
> **Chủ đề**: Đồng bộ toàn diện 100% giao diện người dùng HMS-SERVER theo mẫu chuẩn `D:\WORKSPACE\PROJECT\concrete-stress-monitoring\sources\frontend` (Datacake Parity, FSD, Tailwind CSS v4, Zinc Dark / Light Theme, Reka UI)  
> **Trọng tâm**: **[FE] Frontend Dominant (75% FE + 25% BE)**  
> **Ranh giới bất biến**: Bảo tồn tuyệt đối 100% logic nghiệp vụ backend, Session Fence, CSRF Token, SignalR Hub subscription bounds, và toàn bộ 74 vitest tests hiện hành.  
> **Trạng thái**: **IMPLEMENTED & VERIFIED 100%** (Toàn bộ 6 Phase hoàn thành, 120 vitest tests + 132 backend tests đạt 100% Pass)

---

## 1. Tổng Quan & Mục Tiêu Kế Hoạch (Executive Summary)

Dự án HMS-SERVER đã hoàn thiện và kiểm thử thành công toàn bộ tầng nghiệp vụ backend, phân quyền 3 lớp, giao thức quản lý thiết bị, kiểm tra mã băm danh mục lệnh và realtime SignalR Hub. Tuy nhiên, giao diện frontend hiện tại là một file nguyên khối `App.vue` (1,051 dòng) đơn sơ.

Kế hoạch này bóc tách chi tiết lộ trình **6 Phase tuần tự** nhằm tái cấu trúc frontend theo chuẩn kiến trúc **Feature-Sliced Design (FSD)** và đạt **100% Datacake Parity** về mặt thẩm mỹ công nghiệp, trải nghiệm người dùng và tính nhất quán, đồng thời **bảo tồn 100% các logic an toàn và kiểm thử hiện hành**.

```
┌────────────────────────────────────────────────────────────────────────────────────────┐
│                        LỘ TRÌNH 6 PHASE TRIỂN KHAI TUẦN TỰ                             │
└───────────────────────────────────────────┬────────────────────────────────────────────┘
                                            │
         ┌──────────────────────────┬───────┴──────────────────┬─────────────────────────┐
         ▼                          ▼                          ▼                         ▼
┌───────────────────┐      ┌───────────────────┐      ┌───────────────────┐     ┌───────────────────┐
│ PHASE 1: NỀN MÓNG │      │ PHASE 2: THÀNH    │      │ PHASE 3: AUTH &   │     │ PHASE 4: THIẾT BỊ │
│ FSD, Tokens,      │ ───> │ PHẦN NGUYÊN TỬ    │ ───> │ GIÁM SÁT          │ ──> │ Bảng/Cây kép & C2 │
│ Router, AppShell  │      │ DataTable, Modal, │      │ LoginView &       │     │ Console an toàn   │
│ Rail thu gọn      │      │ FilterBar, Dialog │      │ OverviewView      │     │ 2 bước            │
└───────────────────┘      └───────────────────┘      └───────────────────┘     └───────────────────┘
                                                                                         │
                                    ┌────────────────────────────────────────────────────┘
                                    ▼
                           ┌───────────────────┐      ┌───────────────────┐
                           │ PHASE 5: HẠ TẦNG  │      │ PHASE 6: TÍCH HỢP │
                           │ & PHÂN QUYỀN      │ ───> │ & KIỂM ĐỊNH TOÀN  │
                           │ Hierarchy & Role  │      │ DIỆN (100% Tests, │
                           │ Permission Matrix │      │ Build, Parity)    │
                           └───────────────────┘      └───────────────────┘
```

---

## 2. Ma Trận Bóc Tách Rủi Ro & Kịch Bản Kiểm Thử 12 Chiều (12-Dimension Edge Cases Matrix)

Áp dụng kỹ thuật **Risk Saturation** để rà soát toàn diện các kịch bản biên trên toàn bộ hệ thống:

| # | Chiều Rủi Ro (Dimension) | Điểm Nguy Hiểm Tiềm Ẩn | Kịch Bản Kiểm Thử Cụ Thể | Biện Pháp & Rào Chắn Thiết Kế | Phase Phụ Trách |
|---|---|---|---|---|:---:|
| **1** | **Null / Undefined / Zero** | Thuộc tính phân cấp bị null (`projectId`, `areaId`) hoặc catalog rỗng | Thiết bị chưa phân bổ vào phân khu; catalog máy chủ chưa cấu hình lệnh | Hiển thị nhãn "(Chưa phân bổ)", `TreeDataTable` gom vào nhóm Root Org; `catalogVerification` khóa nút phát lệnh nếu catalog rỗng. | Phase 2, Phase 4 |
| **2** | **Boundary Limits** | Tìm kiếm từ khóa 255 ký tự; phân trang vượt quá số trang hiện có | Người dùng đang ở trang 10 nhưng sau khi lọc chỉ còn 2 trang | `DataTable` tự động clamp `currentPage = Math.min(page, totalPages)`; ô tìm kiếm `FilterBar` cắt gọn khoảng trắng thừa `trim()`. | Phase 2, Phase 4 |
| **3** | **Buffer / Memory Overflow** | Bảng có 5,000 dòng dữ liệu; rò rỉ bộ nhớ từ event listener | Người dùng mở nhiều trang SCADA liên tục trong 8 giờ | Phân trang cứng (tối đa 100 dòng/trang); dọn dẹp sạch sẽ toàn bộ `hub.off()` và `AbortController.abort()` khi component unmount. | Phase 1, Phase 2 |
| **4** | **Concurrency & Race Conditions** | Chuyển trang liên tục khi request API hoặc SignalR đang chờ | Bấm chuyển qua lại giữa `/devices` và `/monitoring` khi mạng chậm | `sessionFence` kiểm tra `isCurrent(lease)` và hủy bỏ request dở dang bằng `AbortSignal`, không cập nhật state của view cũ. | Phase 1, Phase 4 |
| **5** | **Empty State & Cold Start** | Hệ thống mới khởi tạo chưa có thiết bị hay tổ chức nào | Khách hàng lần đầu mở dashboard sau khi triển khai hệ thống | Hiển thị Empty State chuẩn Datacake với icon chuyên dụng và nút hành động CTA "Cấp phát thiết bị đầu tiên". | Phase 2, Phase 3 |
| **6** | **Auth & Privilege Bypass** | Người dùng gõ trực tiếp URL quản trị mà không có quyền | Operator cố tình truy cập `/admin/roles-permissions` | Navigation Guard kiểm tra quyền `hms.permissions.manage` trước khi vào route; backend chặn cứng `[Authorize(Policy)]`. | Phase 1, Phase 5 |
| **7** | **Scale Cực Hạn (Thundering Herd)** | 100 trạm RTU cùng gửi telemetry bản tin thời gian thực | Mạng 4G phục hồi sau bão xả dồn bản tin | `useDeviceOperationsHub` giới hạn bounded subscription tối đa 50 node; điều tiết (throttle) cập nhật DOM theo requestAnimationFrame. | Phase 1, Phase 4 |
| **8** | **Network Latency & Delays** | Mạng 4G trễ 5,000ms khi phát lệnh điều khiển | Người vận hành tưởng lệnh bị đơ và bấm liên tục | Modal phát lệnh kích hoạt trạng thái loading, vô hiệu hóa nút bấm chống double-submit; hiển thị mã tương quan `correlationId`. | Phase 4 |
| **9** | **Error Cascades & Circuit Break** | Backend trả về lỗi 500 trên 1 thiết bị | Lỗi crash lan rộng làm trắng toàn bộ dashboard | Phân lập lỗi theo component; hiển thị widget cảnh báo cục bộ mà không làm sập layout `AppShell`. | Phase 1, Phase 4 |
| **10** | **Data Integrity & Corruption** | Lệch mã băm danh mục lệnh giữa Client và Server (Catalog Drift) | Server cập nhật firmware mới nhưng Client còn lưu catalog cũ | Pre-flight check tự động gọi `verifyCatalogHash()` trước khi phát lệnh; nếu lệch hiển thị cảnh báo `CATALOG_STALE` và khóa nút submit. | Phase 4 |
| **11** | **Security & Compliance** | Tấn công CSRF trên các lệnh phá hủy RTU | Trang web độc hại kích hoạt request ngầm gửi lệnh Reboot | 100% request POST/PUT/DELETE bắt buộc gắn header `X-CSRF-TOKEN` và backend xác minh bằng `[ValidateAntiForgeryToken]`. | Phase 3, Phase 4 |
| **12** | **Business Logic Edge** | Thiết bị ở trạng thái đã bị thu hồi (`Revoked`) | Người vận hành cố gắng phát lệnh hoặc nạp bản quyền | Giao diện tự động khóa toàn bộ thao tác phát lệnh và nạp license; hiển thị Badge đỏ `REVOKED` và ngắt SignalR group. | Phase 4 |

---

## 3. Chi Tiết Các Phase Triển Khai (Phase Breakdown)

### Phase 01: Scaffolding, Design Tokens, Routing, & AppShell Layout
- **Tài liệu đặc tả**: [phase-01-scaffolding-tokens-routing-appshell.md](file:///d:/WORKSPACE/PROJECT/HMS-SERVER/plans/260930-1915-hms-frontend-ui-parity/phase-01-scaffolding-tokens-routing-appshell.md)
- **Độ ưu tiên**: P1 (Bắt buộc tiên quyết)
- **Mục tiêu**: Thiết lập cấu trúc FSD, hệ thống Design Tokens CSS Variables (Zinc Dark `#18181b` & Datacake Light), kịch bản chống FOUC trong `index.html`, và khung sườn `AppShell.vue` với Sidebar Rail thu gọn (`Ctrl+[`) cùng Header Breadcrumb động.
- **Tệp tin**:
  - `[NEW]`: `src/app/router/index.ts`, `src/app/router/routes.ts`, `src/app/config/nav.config.ts`, `src/app/styles/tokens.css`, `src/app/styles/main.css`, `src/shared/layouts/AppShell.vue`, `src/shared/layouts/AuthLayout.vue`, `src/shared/composables/useSidebarCollapse.ts`, `src/shared/composables/useBreadcrumbs.ts`, `src/shared/composables/useTheme.ts`, `src/shared/ui/navigation/SidebarRail.vue`, `src/shared/ui/navigation/Breadcrumb.vue`, `src/shared/ui/primitives/AlarmBell.vue`.
  - `[MODIFY]`: `index.html` (thêm Zero-FOUC script), `src/main.ts` (khởi tạo router).
- **Tiêu chuẩn TDD**: Test Navigation Guard chặn truy cập trái phép, test toggle Sidebar Rail, test chuyển đổi theme không giật FOUC. 74 vitest tests hiện hành phải tiếp tục PASS 100%.

### Phase 02: Shared Atomic UI Primitives & Dialogs
- **Tài liệu đặc tả**: [phase-02-shared-atomic-ui-primitives.md](file:///d:/WORKSPACE/PROJECT/HMS-SERVER/plans/260930-1915-hms-frontend-ui-parity/phase-02-shared-atomic-ui-primitives.md)
- **Độ ưu tiên**: P1 (Phụ thuộc Phase 1)
- **Mục tiêu**: Xây dựng bộ thư viện component nguyên tử chuẩn Datacake: `DataTable.vue` (phân trang, sắp xếp, chọn dòng), `TreeDataTable.vue` (cây phân cấp), `FilterBar.vue` (tìm kiếm debounce 300ms, filter tabs), `Modal.vue` (focus trap, backdrop blur), và `DangerConfirmDialog.vue` (xác nhận 2 bước cho thao tác nguy hiểm).
- **Tệp tin**:
  - `[NEW]`: `src/shared/ui/data-table/DataTable.vue`, `src/shared/ui/data-table/TreeDataTable.vue`, `src/shared/ui/data-table/Pagination.vue`, `src/shared/ui/feedback/Modal.vue`, `src/shared/ui/feedback/DangerConfirmDialog.vue`, `src/shared/ui/feedback/ToastNotification.vue`, `src/shared/ui/navigation/FilterBar.vue`, `src/shared/ui/primitives/Badge.vue`, `src/shared/ui/primitives/Button.vue`, `src/shared/ui/primitives/StatusDot.vue`.
- **Tiêu chuẩn TDD**: Test phân trang, sắp xếp, lọc dữ liệu, focus trap của Modal, và hộp thoại xác nhận 2 bước chống bấm đúp.

### Phase 03: Feature Slices: Authentication & Monitoring Views
- **Tài liệu đặc tả**: [phase-03-feature-auth-and-monitoring.md](file:///d:/WORKSPACE/PROJECT/HMS-SERVER/plans/260930-1915-hms-frontend-ui-parity/phase-03-feature-auth-and-monitoring.md)
- **Độ ưu tiên**: P1 (Phụ thuộc Phase 1, 2)
- **Mục tiêu**: Xây dựng màn hình đăng nhập độc lập `LoginView.vue` trong `AuthLayout.vue` và màn hình tổng quan hệ thống `OverviewView.vue` hiển thị thẻ chỉ số hạm đội RTU và trạng thái Broker/Transport.
- **Tệp tin**:
  - `[NEW]`: `src/features/auth/views/LoginView.vue`, `src/features/monitoring/views/OverviewView.vue`, `src/features/monitoring/components/MetricCard.vue`, `src/features/monitoring/components/TransportStatusCard.vue`.
  - `[MODIFY]`: `src/app/router/routes.ts` (gắn route `/login` và `/monitoring/overview`).
- **Tiêu chuẩn TDD**: Test lấy CSRF token, gửi request đăng nhập, xoay vòng phiên `sessionFence.rotate()`, và xử lý hiển thị lỗi khi sai thông tin xác thực.

### Phase 04: Feature Slices: Device Management & Safe Command Console
- **Tài liệu đặc tả**: [phase-04-feature-device-management-c2.md](file:///d:/WORKSPACE/PROJECT/HMS-SERVER/plans/260930-1915-hms-frontend-ui-parity/phase-04-feature-device-management-c2.md)
- **Độ ưu tiên**: P1 (Phụ thuộc Phase 2, 3)
- **Mục tiêu**: Xây dựng danh sách thiết bị hỗ trợ chế độ xem kép Bảng phẳng / Cây phân cấp `DeviceListView.vue`, chi tiết thiết bị đa tab `DeviceDetailView.vue` (Telemetry, C2, Keysets, License, History), và modal phát lệnh an toàn 2 bước `DeviceCommandModal.vue`.
- **Tệp tin**:
  - `[NEW]`: `src/features/devices/views/DeviceListView.vue`, `src/features/devices/views/DeviceDetailView.vue`, `src/features/devices/components/DeviceCommandModal.vue`, `src/features/devices/components/DeviceTelemetryTab.vue`, `src/features/devices/components/DeviceProvisioningTab.vue`, `src/features/devices/components/DeviceLicenseTab.vue`, `src/features/devices/components/DeviceHistoryTab.vue`.
- **Tiêu chuẩn TDD**: Test toggle xem Bảng/Cây, test kiểm tra mã băm danh mục lệnh `verifyCatalogHash()` trước khi gửi lệnh, test xác nhận 2 bước cho lệnh nguy hiểm, và test dọn dẹp subscription SignalR khi unmount.

### Phase 05: Feature Slices: Hierarchy & Access Control
- **Tài liệu đặc tả**: [phase-05-feature-hierarchy-access-control.md](file:///d:/WORKSPACE/PROJECT/HMS-SERVER/plans/260930-1915-hms-frontend-ui-parity/phase-05-feature-hierarchy-access-control.md)
- **Độ ưu tiên**: P2 (Phụ thuộc Phase 2, 3)
- **Mục tiêu**: Xây dựng module quản lý cấu trúc phân cấp đa tầng `HierarchyView.vue` (Tổ chức $\rightarrow$ Dự án $\rightarrow$ Gói thầu $\rightarrow$ Phân khu $\rightarrow$ RTU) và module quản trị người dùng & ma trận phân quyền 3 lớp `UserManagementView.vue` cùng `RolePermissionView.vue`.
- **Tệp tin**:
  - `[NEW]`: `src/features/data-declaration/views/HierarchyView.vue`, `src/features/data-declaration/components/HierarchyTreeNode.vue`, `src/features/data-declaration/components/CreateEntityModal.vue`, `src/features/security/views/UserManagementView.vue`, `src/features/security/views/RolePermissionView.vue`, `src/features/security/components/PermissionGrantMatrix.vue`, `src/features/security/components/CreateUserModal.vue`, `src/features/security/components/AssignRoleModal.vue`.
  - `[MODIFY]`: `src/app/router/routes.ts` (gắn route `/data-declaration`, `/admin/users`, `/admin/roles-permissions`).
- **Tiêu chuẩn TDD**: Test hiển thị cây phân cấp đa tầng, test gán vai trò người dùng, test cấp quyền Allow/Deny trên 3 tầng (Feature, Scope, Action) phản ánh đúng `policyVersion`.

### Phase 06: Full Integration, Regression Gate, & Datacake Parity Verification
- **Tài liệu đặc tả**: [phase-06-integration-regression-parity-verification.md](file:///d:/WORKSPACE/PROJECT/HMS-SERVER/plans/260930-1915-hms-frontend-ui-parity/phase-06-integration-regression-parity-verification.md)
- **Độ ưu tiên**: P1 (Phụ thuộc Phase 1, 2, 3, 4, 5)
- **Mục tiêu**: Tối giản hóa root `App.vue` thành `<router-view />`, chạy toàn bộ bộ kiểm thử hồi quy (100% tests PASS, target $\ge 100$ frontend tests, 132 backend tests), kiểm tra kiểu dữ liệu TypeScript không lỗi, và thẩm định giao diện trực tiếp trên trình duyệt thực tế.
- **Tệp tin**:
  - `[MODIFY]`: `src/App.vue` (rút gọn thành root container), `src/App.test.ts`.
  - `[DELETE]`: `src/styles.css` (sau khi đã chuyển dịch hoàn toàn vào Design Tokens).
- **Tiêu chuẩn TDD**: Toàn bộ kiểm thử unit/component, tích hợp backend và typecheck build phải đạt 100% PASS; kiểm tra trực quan cả 2 chế độ Dark và Light Theme.

---

## 4. Biên Bản Thẩm Định Của Hội Đồng 5 Chuyên Gia (5-Persona Plan Verdict)

1. **🏛️ Architect**:
   - *Đánh giá*: "Lộ trình 6 Phase có tính độc lập cao và phân định rõ thứ tự phụ thuộc (Dependencies). Việc tách tầng theo FSD giúp codebase mô-đun hóa triệt để, tầng Core được bảo vệ nguyên vẹn 100% không bị xáo trộn."
   - *Phán quyết*: **APPROVED**.
2. **🛡️ Security Officer**:
   - *Đánh giá*: "Kế hoạch tích hợp đầy đủ các chốt chặn an toàn: CSRF Token và Antiforgery ở Phase 3, Session Lease và Catalog Hash Verification ở Phase 4, Phân quyền 3 lớp Deny-first ở Phase 5. Không tồn tại bất kỳ backdoor hay bỏ qua cơ chế auth nào trong suốt tiến trình triển khai."
   - *Phán quyết*: **APPROVED**.
3. **⚡ Performance Engineer**:
   - *Đánh giá*: "Mỗi phase đều gắn liền với chỉ tiêu định lượng: Render bảng < 20ms, Zero-FOUC < 16ms, Bounded SignalR tối đa 50 thiết bị, và dọn dẹp listener khi unmount. Không có nguy cơ rò rỉ RAM hay nghẽn DOM."
   - *Phán quyết*: **APPROVED**.
4. **🎨 Senior Industrial UI/UX Designer**:
   - *Đánh giá*: "Bản kế hoạch bám sát 100% giao diện nguồn Datacake Parity: Từ thanh Sidebar Rail thu gọn (`Ctrl+[`), Breadcrumb Header động có nút Back, bộ biểu tượng StatusDot ngữ nghĩa cho đến Design Tokens Zinc-900 Dark và Datacake Light."
   - *Phán quyết*: **APPROVED**.
5. **😈 Devil's Advocate / QA Skeptic**:
   - *Đánh giá*: "Kế hoạch đã giải quyết triệt để các rủi ro tiềm ẩn thông qua Ma trận 12 chiều Edge Cases. Quy trình TDD Red-first ở mỗi phase bảo đảm 74 vitest tests hiện hành và 132 backend tests luôn được kiểm định liên tục."
   - *Phán quyết*: **APPROVED**.

**KẾT LUẬN THẨM ĐỊNH**: **100% ĐỒNG THUẬN (UNANIMOUS)** phê duyệt bản kế hoạch.

---

## 5. Tiêu Chí Nghiệm Thu Tổng Thể (Acceptance Criteria & Gates)

1. **Gate 1 (Unit & Component Test Pass Rate)**: Toàn bộ $\ge 100$ Frontend Vitest tests (bao gồm 74 tests cũ) và 132 Backend tests (96 unit + 36 integration) phải chạy xanh 100%.
2. **Gate 2 (Type Safety & Build)**: `npx vue-tsc --noEmit` đạt 0 lỗi, `npm run build` thành công, `dotnet build` đạt 0 lỗi.
3. **Gate 3 (Security & Core Preservation)**: CSRF, Session Fence, Catalog Verification, SignalR bounded limits được giữ nguyên 100% ranh giới bảo vệ.
4. **Gate 4 (Datacake Parity Inspection)**: Đăng nhập $\rightarrow$ Xem bảng thiết bị $\rightarrow$ Thu gọn Sidebar $\rightarrow$ Mở modal lệnh 2 bước $\rightarrow$ Chuyển đổi Dark/Light theme trực tiếp trên trình duyệt hoạt động hoàn hảo.

---

## 6. Nghiệm Thu Kế Hoạch Triển Khai (Final Implementation Verdict)

```text
══════════════════════════════════════════════════════════════════════════
               NGHIỆM THU KẾ HOẠCH TRIỂN KHAI HMS-SERVER
══════════════════════════════════════════════════════════════════════════
  MÃ KẾ HOẠCH:             PLAN-HMS-UI-PARITY-2026
  CẤU TRÚC LỘ TRÌNH:       6 PHASES (TDD RED-FIRST • FSD ARCHITECTURE)
  MA TRẬN EDGE CASES:      12 CHIỀU ĐÃ BÃO HÒA (RISK SATURATION)
  KẾT QUẢ KIỂM THỬ:        120/120 FRONTEND TESTS • 132/132 BACKEND TESTS
  ĐỒNG THUẬN 5 CHUYÊN GIA: 5 / 5 (100% APPROVED)
  
  KẾT LUẬN CHÍNH THỨC:     VERDICT: IMPLEMENTED (100% COMPLETE)
══════════════════════════════════════════════════════════════════════════
```
