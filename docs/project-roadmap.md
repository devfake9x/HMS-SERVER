# LỘ TRÌNH DỰ ÁN HMS-SERVER (PROJECT ROADMAP)

> **Dự án**: HMS-SERVER (Enterprise SCADA & Industrial IoT Management Platform)  
> **Cập nhật gần nhất**: 2026-09-30  
> **Trạng thái tổng thể**: **MILESTONE 3 & 3.1 COMPLETED — PRODUCTION READY**  

---

## 1. Bản Đồ Các Cột Mốc Chiến Lược (Milestone Overview)

| Cột Mốc (Milestone) | Trọng Tâm Nghiệp Vụ | Trạng Thái | Tài Liệu Quy Chiếu |
|---|---|:---:|---|
| **Milestone 1** | **Core Control-Plane & Management APIs**: Quản lý Thiết bị, Tổ chức, Phân quyền 3 lớp, License, C2 Command catalog. | **HOÀN THÀNH** | `plans/260924-2044-hms-server-management/` |
| **Milestone 2** | **Security Hardening & Zero-Drift**: Bảo vệ CSRF/Antiforgery, Session Fence, Catalog Hash verification, SignalR Epochs. | **HOÀN THÀNH** | `docs/security-audit-report.md` |
| **Milestone 3** | **Frontend Datacake Parity & FSD Synchronization**: Đồng bộ 100% giao diện theo chuẩn `concrete-stress-monitoring` (FSD, Zinc Dark, Rail Sidebar). | **HOÀN THÀNH** | `plans/260930-1915-hms-frontend-ui-parity/` |
| **Milestone 4** | **Full Hardware MQTT APIs Integration & Dynamic C2 Console**: Tích hợp toàn diện 94 MQTT Actions của ESP32 RTU (HMS-IDF) lên Web qua Dynamic Schema Form. | **THIẾT KẾ HOÀN TẤT** | `docs/architecture-device-mqtt-api-catalog.md` |

---

## 2. Chi Tiết Lộ Trình Milestone 3: Frontend Datacake Parity UI Sync

Kế hoạch chi tiết được quản lý tại: [plans/260930-1915-hms-frontend-ui-parity/plan.md](file:///d:/WORKSPACE/PROJECT/HMS-SERVER/plans/260930-1915-hms-frontend-ui-parity/plan.md) (Báo cáo tương tác: [plan.html](file:///d:/WORKSPACE/PROJECT/HMS-SERVER/plans/260930-1915-hms-frontend-ui-parity/plan.html)).

```
Phase 1 (Nền móng FSD & AppShell) ──> Phase 2 (UI Nguyên tử) ──> Phase 4 (Thiết bị & C2) ──┐
  │                                           │                                              │
  └───> Phase 3 (Auth & Giám sát) ────────────┴──> Phase 5 (Hạ tầng & Quyền) ───────────────┴──> Phase 6 (Tích hợp & Chốt)
```

### Các Phase Triển Khai
1. **Phase 01: Scaffolding, Design Tokens, Routing, & AppShell Layout**  
   - *Đặc tả*: [phase-01-scaffolding-tokens-routing-appshell.md](file:///d:/WORKSPACE/PROJECT/HMS-SERVER/plans/260930-1915-hms-frontend-ui-parity/phase-01-scaffolding-tokens-routing-appshell.md)  
   - *Trọng tâm*: Tách FSD, thiết lập `tokens.css` (Zinc Dark / Datacake Light), router với guards, `AppShell.vue` với Sidebar Rail thu gọn (`Ctrl+[`).  
   - *Trạng thái*: **HOÀN THÀNH**.

2. **Phase 02: Shared Atomic UI Primitives & Dialogs**  
   - *Đặc tả*: [phase-02-shared-atomic-ui-primitives.md](file:///d:/WORKSPACE/PROJECT/HMS-SERVER/plans/260930-1915-hms-frontend-ui-parity/phase-02-shared-atomic-ui-primitives.md)  
   - *Trọng tâm*: `DataTable.vue`, `TreeDataTable.vue`, `FilterBar.vue`, `Modal.vue`, `DangerConfirmDialog.vue` xác nhận 2 bước.  
   - *Trạng thái*: **HOÀN THÀNH**.

3. **Phase 03: Feature Slices: Authentication & Monitoring Views**  
   - *Đặc tả*: [phase-03-feature-auth-and-monitoring.md](file:///d:/WORKSPACE/PROJECT/HMS-SERVER/plans/260930-1915-hms-frontend-ui-parity/phase-03-feature-auth-and-monitoring.md)  
   - *Trọng tâm*: `LoginView.vue` với CSRF và session fence; `OverviewView.vue` với thẻ chỉ số hạm đội RTU.  
   - *Trạng thái*: **HOÀN THÀNH**.

4. **Phase 04: Feature Slices: Device Management & Safe Command Console**  
   - *Đặc tả*: [phase-04-feature-device-management-c2.md](file:///d:/WORKSPACE/PROJECT/HMS-SERVER/plans/260930-1915-hms-frontend-ui-parity/phase-04-feature-device-management-c2.md)  
   - *Trọng tâm*: `DeviceListView.vue` (Table/Tree), `DeviceDetailView.vue` (5 tabs), `DeviceCommandModal.vue` (catalogHash check & 2-step confirm).  
   - *Trạng thái*: **HOÀN THÀNH**.

5. **Phase 05: Feature Slices: Hierarchy & Access Control**  
   - *Đặc tả*: [phase-05-feature-hierarchy-access-control.md](file:///d:/WORKSPACE/PROJECT/HMS-SERVER/plans/260930-1915-hms-frontend-ui-parity/phase-05-feature-hierarchy-access-control.md)  
   - *Trọng tâm*: `HierarchyView.vue` (cây tổ chức đa tầng), `UserManagementView.vue`, `RolePermissionView.vue` với ma trận phân quyền 3 lớp Allow/Deny.  
   - *Trạng thái*: **HOÀN THÀNH**.

6. **Phase 06: Full Integration, Regression Gate, & Datacake Parity Verification**  
   - *Đặc tả*: [phase-06-integration-regression-parity-verification.md](file:///d:/WORKSPACE/PROJECT/HMS-SERVER/plans/260930-1915-hms-frontend-ui-parity/phase-06-integration-regression-parity-verification.md)  
   - *Trọng tâm*: Rút gọn `App.vue` thành root container, kiểm thử hồi quy 100% tests PASS (Frontend & Backend), kiểm tra trực quan trên trình duyệt.  
   - *Trạng thái*: **HOÀN THÀNH**.

7. **Milestone 3.1: Micro-Component Parity & Design System Refinement**  
   - *Đặc tả*: [architecture-ui-components-parity.md](file:///d:/WORKSPACE/PROJECT/HMS-SERVER/docs/architecture-ui-components-parity.md)  
   - *Trọng tâm*: Chuẩn hóa 100% `Table`, `Combobox`, `Select`, `Checkbox`, `Input`, `Button` theo chuẩn BEM Datacake, bảo tồn màu cam `#ea580c` / `#f97316` ở nút chính.  
   - *Trạng thái*: **HOÀN THÀNH (100% VERIFIED & PRODUCTION READY)**.

8. **Milestone 4: Full Hardware MQTT APIs Integration & Dynamic C2 Console**  
   - *Đặc tả Kiến trúc*: [architecture-device-mqtt-api-catalog.md](file:///d:/WORKSPACE/PROJECT/HMS-SERVER/docs/architecture-device-mqtt-api-catalog.md)  
   - *Kế hoạch Triển khai (6 Phases)*: [plans/261001-0030-full-hardware-mqtt-apis-c2/plan.md](file:///d:/WORKSPACE/PROJECT/HMS-SERVER/plans/261001-0030-full-hardware-mqtt-apis-c2/plan.md) ([Báo cáo tương tác: plan.html](file:///d:/WORKSPACE/PROJECT/HMS-SERVER/plans/261001-0030-full-hardware-mqtt-apis-c2/plan.html))  
   - *Trọng tâm*: Tích hợp toàn diện 94 MQTT Actions của ESP32 RTU (HMS-IDF) qua Dynamic Schema Form, chuẩn hóa timestamp seconds, hỗ trợ token xác nhận Class F/D, và kiểm thử HIL trên bo mạch thực tế.  
   - *Trạng thái*: 🟢 **KẾ HOẠCH TDD ĐÃ PHÊ DUYỆT (ARENA DEBATE 35/35 CONFIRMED - READY FOR COOK)**.

---

## 3. Bản Đồ Triển Khai Milestone 4 (6-Phase TDD Breakdown)

```
Phase 01 (Catalog Activation & RBAC) ──> Phase 02 (Live MQTT Transport)
                                                │
       ┌────────────────────────────────────────┴────────────────────────────────────────┐
       ▼                                                                                 ▼
Phase 03 (Core: DO Relay, ADC, System)                            Phase 04 (Advanced: Network, Modbus, SMS, OTA)
       │                                                                                 │
       └────────────────────────────────────────┬────────────────────────────────────────┘
                                                ▼
                                 Phase 05 (Frontend C2 Console & Specialized Tabs)
                                                │
                                                ▼
                                 Phase 06 (E2E Hardware-in-the-Loop HIL Testing)
```

---

## 4. Tiêu Chí Nghiệm Thu Chiến Lược (Strategic Quality Gates)
- **100% Test Pass Rate**: Duy trì 137 Backend Tests PASS (99 unit + 38 integration) và 128 Frontend Tests PASS (28 files).
- **Hardware Boundary Protection**: Giữ nguyên tắc **READ-ONLY 100%** trên `D:\WORKSPACE\PROJECT\HMS-IDF` (0 file thay đổi).
- **Orange Button Consistency**: Giữ vững mã màu cam thương hiệu (`#f97316` / `#ea580c`) trên các nút nhấn thao tác.
- **Zero Type Errors**: `vue-tsc --noEmit` đạt 0 lỗi, `dotnet build` đạt 0 lỗi.
- **Zero Regression**: Toàn bộ ranh giới CSRF, session fence, mã băm catalog và SignalR bounds được bảo tồn 100%.
