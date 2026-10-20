---
phase: 6
title: "Full Integration, Regression Gate, & Datacake Parity Verification"
status: complete
priority: P1
dependencies: [1, 2, 3, 4, 5]
---

# Phase 06: Full Integration, Regression Gate, & Datacake Parity Verification

## Overview
Giai đoạn tích hợp toàn diện và kiểm định chất lượng: Thay thế file nguyên khối `App.vue` cũ bằng root component `<router-view />` tinh gọn, chạy toàn bộ bộ kiểm thử hồi quy (74 vitest tests cũ + bộ test mới cho các component FSD, 132 backend tests), kiểm tra kiểu dữ liệu TypeScript, và xác nhận độ đồng bộ giao diện 100% trên trình duyệt thực tế.

## Requirements
- **Functional**:
  - Tối giản hóa `frontend/src/App.vue` từ 1,051 dòng xuống dưới 30 dòng (chỉ đóng vai trò root router container `<router-view />` và các modal thông báo toàn cục).
  - Kết nối toàn bộ các route trong `frontend/src/app/router/routes.ts` với các views và layouts đã xây dựng ở các Phase 1–5.
  - Đồng bộ trạng thái toàn cục (User info, CSRF token, Session Fence, SignalR Operations Hub) thông qua AppShell và các Stores.
  - Kiểm tra thẩm mỹ Datacake Parity trên trình duyệt:
    - Sidebar Rail thu gọn mượt mà bằng phím tắt `Ctrl+[` và nút bấm.
    - Breadcrumb động phản ánh chính xác ngữ cảnh trang con.
    - Chuyển đổi Dark/Light theme không có hiện tượng chớp trắng FOUC.
    - Bảng dữ liệu hiển thị đúng chuẩn với phân trang, lọc và cây phân cấp.
- **Non-Functional**:
  - 100% tests PASS:
    - Frontend Vitest Tests $\ge$ 100 tests PASS (bao gồm 74 tests cũ + các bài test mới).
    - Backend Unit Tests: 96 / 96 tests PASS.
    - Backend Integration Tests: 36 / 36 tests PASS.
  - Build & Typecheck: `npx vue-tsc --noEmit` đạt 0 lỗi, `npm run build` thành công.
  - Tốc độ tải trang ban đầu (First Contentful Paint) < 800ms trên mạng nội bộ.

## 12-Dimension Edge Cases & Scenarios
- **D4 (Concurrency)**: Kiểm thử tải chuyển trang liên tục 20 lần giữa các route $\rightarrow$ Không xuất hiện lỗi `Unhandled Promise Rejection`, SignalR ngắt kết nối và đăng ký lại đúng cách.
- **D6 (Auth Expiry)**: Phiên làm việc hết hạn giữa chừng khi đang ở trang `/devices/:id` $\rightarrow$ Tự động chuyển hướng về `/login` với tham số `redirect` được mã hóa URL an toàn, sau khi đăng nhập lại được đưa về đúng trang chi tiết thiết bị đó.
- **D8 (Latency)**: API backend phản hồi chậm 2000ms $\rightarrow$ Giao diện hiển thị loading skeleton đồng bộ, không bị vỡ bố cục hay lệch cột bảng.
- **D10 (Data Integrity)**: Toàn bộ các kiểm thử về CSRF, Session Fence, Catalog Verification và Bounded Realtime Hub phải chứng minh các ranh giới bảo mật không bị suy giảm sau khi di chuyển sang FSD.

## Related Code Files
- **[MODIFY]**:
  - `frontend/src/App.vue` (Thay thế nội dung bằng root router view)
  - `frontend/src/App.test.ts` (Cập nhật mount test cho root component mới)
- **[DELETE]**:
  - `frontend/src/styles.css` (Sau khi đã chuyển toàn bộ CSS vào `src/app/styles/tokens.css` và `main.css`)

## Test Plan (TDD Red-First Checklist)
- [ ] Chạy `npm run test`: Đảm bảo 100% tests của toàn bộ 15 tệp tin test cũ và các tệp tin test mới chạy xanh.
- [ ] Chạy `npm run build`: Đảm bảo `vue-tsc --noEmit` đạt 0 lỗi và bundle Vite được tạo thành công.
- [ ] Chạy `dotnet test Hms.Server.slnx -c Release --no-build`: Đảm bảo 132 bài kiểm thử Backend đạt 100% PASS.
- [ ] Kiểm tra kiểm thử trực quan trên cổng `http://127.0.0.1:5173` và `http://127.0.0.1:5099`: Đăng nhập $\rightarrow$ Xem bảng thiết bị $\rightarrow$ Thu gọn Sidebar $\rightarrow$ Mở modal lệnh $\rightarrow$ Đổi Dark/Light theme $\rightarrow$ Đăng xuất.
