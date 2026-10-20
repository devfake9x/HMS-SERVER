---
phase: 5
title: "Trung Tâm Điều Khiển C2 Web UI & Bảng Điều Khiển Chuyên Biệt (Frontend C2 Console & Specialized Tabs)"
status: pending
priority: P1
dependencies: [3, 4]
---

# Phase 05: Trung Tâm Điều Khiển C2 Web UI & Bảng Điều Khiển Chuyên Biệt

## Overview
Xây dựng giao diện điều khiển C2 toàn diện trên Vue 3 / TypeScript, kế thừa phong cách Datacake UI: bao gồm modal điều phối lệnh tổng quát `DeviceCommandModal.vue` với dynamic parameter schema, tab điều khiển ngõ ra rơ-le tức thời (Digital Outputs Panel), tab giám sát tín hiệu cảm biến ADC (ADC Gauge Table), và hộp thoại xác nhận lệnh phá hủy 2 bước. Đảm bảo giữ vững màu cam thương hiệu (`#f97316` / `#ea580c`) trên toàn bộ các nút thao tác.

## Requirements
- **Functional**:
  - `DeviceCommandModal.vue`:
    - Dropdown chọn Subsystem (12 subsystems) và Action (94 actions).
    - Vùng nhập tham số thông minh: Render schema form theo action (hoặc JSON editor linh hoạt cho chuyên gia).
    - Tự động điền tham số mặc định và token bảo vệ (`REBOOT-<DEVID>`, `OTA-APPLY-<DEVID>`).
    - Nút Dispatch Command giữ màu cam chuẩn (`!bg-orange-500 hover:!bg-orange-600 !border-orange-500 !text-white`).
  - `DeviceDetailView.vue`:
    - Tab `Outputs (Relay)`: 4 công tắc chuyển mạch cho DO1 - DO4, nút bấm kích xung Pulse (nhập mili-giây).
    - Tab `Sensors (ADC)`: Hiển thị bảng 8 kênh analog kèm thanh đo phần trăm (gauge bar) và giá trị mA/V.
    - Tab `Commands (Audit)`: Nhật ký lịch sử lệnh đã phát, trạng thái ACK và thời gian thực thi.
  - Hộp thoại an toàn `DangerConfirmDialog.vue`: Yêu cầu gõ lại đúng Device ID trước khi thực thi lệnh Destructive.
- **Non-functional**:
  - Hỗ trợ đầy đủ Dark Mode & Light Mode (đồng bộ bảng màu CSS Variables Datacake).
  - Khả năng tiếp cận WCAG AA (độ tương phản màu cam trên nền tối và sáng).
  - Tối ưu hiệu năng: Không gây re-render toàn trang khi nhận telemetry cập nhật trạng thái relay.

## Architecture
- Vue 3 `<script setup lang="ts">`, Pinia store cho telemetry cache.
- SignalR `useDeviceOperationsHub` phản hồi sự kiện real-time.
- Shared UI primitives: `Button.vue`, `Badge.vue`, `Select.vue`, `Checkbox.vue`, `Modal.vue`.

## Related Code Files
- Modify: `frontend/src/features/devices/components/DeviceCommandModal.vue`
- Create: `frontend/src/features/devices/components/DeviceOutputsTab.vue`
- Create: `frontend/src/features/devices/components/DeviceAdcTab.vue`
- Modify: `frontend/src/features/devices/views/DeviceDetailView.vue`
- Create: `frontend/src/features/devices/components/DeviceOutputsTab.test.ts`
- Create: `frontend/src/features/devices/components/DeviceAdcTab.test.ts`

## Implementation Steps (TDD First)
1. **Viết Test Đỏ (Red Tests)**:
   - `DeviceOutputsTab.test.ts`: Test render 4 switch DO, click switch kích hoạt gọi API `adminApi.sendCommand` với payload tương ứng.
   - `DeviceAdcTab.test.ts`: Test hiển thị 8 kênh ADC và cập nhật khi có props telemetry mới.
   - `DeviceCommandModal.test.ts`: Test kiểm thực JSON lỗi làm vô hiệu hoá nút submit; test nút có class màu cam.
2. **Hiện Thực Mã Nguồn (Green)**:
   - Hiện thực `DeviceOutputsTab.vue` và `DeviceAdcTab.vue`.
   - Tích hợp vào `DeviceDetailView.vue`.
3. **Refactor & Verify**:
   - Chạy `npm test` và `npm run build` bảo đảm 100% test pass và build không lỗi.

## Success Criteria
- [ ] Giao diện Web hiển thị trực quan các công tắc relay và giá trị ADC.
- [ ] Nút bấm điều khiển giữ vững màu cam thương hiệu theo quy tắc bất biến.
- [ ] Hộp thoại xác nhận 2 bước ngăn chặn hoàn toàn thao tác nhầm lẫn đối với lệnh nguy hiểm.
