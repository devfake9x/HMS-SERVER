---
phase: 2
title: "Shared Atomic UI Primitives & Dialogs"
status: complete
priority: P1
dependencies: [1]
---

# Phase 02: Shared Atomic UI Primitives & Dialogs

## Overview
Xây dựng thư viện component nguyên tử chuẩn Datacake Parity phục vụ hiển thị dữ liệu SCADA công nghiệp, bao gồm bảng dữ liệu phẳng `DataTable.vue`, bảng dữ liệu phân cấp `TreeDataTable.vue`, thanh công cụ lọc `FilterBar.vue`, và hệ thống hộp thoại an toàn `Modal.vue` kết hợp `DangerConfirmDialog.vue` xác nhận 2 bước.

## Requirements
- **Functional**:
  - `DataTable.vue`:
    - Hiển thị danh sách có phân trang (Pagination), lựa chọn số dòng trên trang (`PageSizeSelect`: 10, 25, 50, 100).
    - Hỗ trợ sắp xếp cột (Sorting: asc, desc, none) với icon mũi tên rõ ràng.
    - Hỗ trợ chọn dòng (Checkbox selection) và chọn tất cả (Select all).
    - Có trạng thái tải dữ liệu (Loading Skeleton) và trạng thái rỗng (Empty State) với icon và thông điệp tùy biến.
  - `TreeDataTable.vue`:
    - Kế thừa các tính năng của DataTable và bổ sung hiển thị dạng cây phân cấp (Hierarchical Tree) cho cấu trúc: `Tổ chức -> Dự án -> Gói thầu -> Phân khu -> RTU`.
    - Hỗ trợ mở rộng / thu gọn từng nút (Expand/Collapse Node), nhớ trạng thái mở rộng của cây.
    - Nút thụt lề trực quan với đường kẻ dẫn hướng (Guide Lines) chuẩn công nghiệp.
  - `FilterBar.vue`:
    - Ô tìm kiếm từ khóa tích hợp cơ chế Debounce 300ms, tự động xóa khoảng trắng thừa (`trim()`).
    - Các Tab lọc trạng thái nhanh (Tabs: `All`, `Active`, `Provisioning`, `Revoked`, `Alert`).
    - Vùng chứa nút bấm thao tác (Action Slots: Nút thêm mới, nút xuất báo cáo, nút làm mới).
  - `Modal.vue`:
    - Teleport ra ngoài `<body>`, lớp nền mờ (Backdrop Blur `bg-zinc-950/70`).
    - Focus Trap (giữ phím Tab bên trong modal), đóng khi bấm phím `Escape` hoặc bấm ra ngoài (nếu không ở chế độ persistent).
    - Khóa cuộn trang nền (`document.body.style.overflow = 'hidden'`).
  - `DangerConfirmDialog.vue`:
    - Hộp thoại cảnh báo rủi ro chuyên dụng cho các hành động phá hủy (Thu hồi thiết bị, xóa vai trò, lệnh reboot RTU).
    - Xác nhận 2 bước: Yêu cầu người dùng bấm nút xác nhận nguy hiểm hoặc nhập đúng từ khóa định danh (`CONFIRM-<DEVICE-NAME>`).
    - Trạng thái nút bấm chuyển sang Loading khi đang gửi lệnh, chống bấm đúp (Debounce / Double-submit lock).
  - `Badge.vue`, `StatusDot.vue`, `ToastNotification.vue`:
    - Hiển thị nhãn trạng thái ngữ nghĩa: `Online` (Xanh ngọc `#10b981`), `Offline` (Xám `#71717a`), `Danger` (Đỏ `#ef4444`), `Warning` (Vàng `#f59e0b`).
- **Non-Functional**:
  - Tốc độ render bảng 100 dòng < 20ms.
  - Khả năng tiếp cận WCAG AA: Đầy đủ các thuộc tính `aria-label`, `role="dialog"`, `aria-modal="true"`.

## 12-Dimension Edge Cases & Scenarios
- **D1 (Null/Undefined)**: Dữ liệu truyền vào bảng là mảng rỗng `[]` hoặc `null` $\rightarrow$ Hiển thị EmptyState thân thiện kèm nút CTA, không ném lỗi runtime.
- **D2 (Boundary Limits)**: Người dùng chuyển sang trang 10 nhưng sau khi lọc danh sách chỉ còn 2 trang $\rightarrow$ `DataTable` tự động điều chỉnh trang hiện tại về trang 1.
- **D3 (Buffer/Memory Overflow)**: Danh sách có 1,000 dòng dữ liệu $\rightarrow$ Phân trang chặt chẽ (tối đa 100 dòng/trang) tránh làm nghẽn DOM.
- **D4 (Concurrency)**: Bấm nhanh nút "Xác nhận phát lệnh" 3 lần liên tiếp $\rightarrow$ `DangerConfirmDialog` khóa nút bấm ngay lần click đầu tiên, ngăn chặn gửi trùng request.
- **D8 (Timing/Latency)**: Đóng Modal trong khi API bên trong đang đợi phản hồi $\rightarrow$ Hủy bỏ AbortSignal gắn kèm, không cập nhật state của component đã đóng.

## Related Code Files
- **[NEW]**:
  - `frontend/src/shared/ui/data-table/DataTable.vue`
  - `frontend/src/shared/ui/data-table/TreeDataTable.vue`
  - `frontend/src/shared/ui/data-table/Pagination.vue`
  - `frontend/src/shared/ui/feedback/Modal.vue`
  - `frontend/src/shared/ui/feedback/DangerConfirmDialog.vue`
  - `frontend/src/shared/ui/feedback/ToastNotification.vue`
  - `frontend/src/shared/ui/navigation/FilterBar.vue`
  - `frontend/src/shared/ui/primitives/Badge.vue`
  - `frontend/src/shared/ui/primitives/Button.vue`
  - `frontend/src/shared/ui/primitives/StatusDot.vue`

## Test Plan (TDD Red-First Checklist)
- [ ] `tests/DataTable.test.ts`: Kiểm tra phân trang, sắp xếp cột và chọn dòng.
- [ ] `tests/TreeDataTable.test.ts`: Kiểm tra mở rộng/thu gọn nút cây phân cấp và thụt lề dữ liệu.
- [ ] `tests/FilterBar.test.ts`: Kiểm tra phát sự kiện search sau 300ms debounce và chuyển đổi filter tab.
- [ ] `tests/DangerConfirmDialog.test.ts`: Kiểm tra yêu cầu xác nhận 2 bước và trạng thái khóa nút bấm.
- [ ] `tests/Modal.test.ts`: Kiểm tra đóng bằng phím Escape, backdrop blur và focus trap.
