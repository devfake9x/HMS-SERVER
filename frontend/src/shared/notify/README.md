# Module Thông Báo: `shared/notify` (react-toastify v11 × Datacake)

Module quản lý và hiển thị thông báo trạng thái cấp ứng dụng (Toast Notification), tuân thủ tiêu chuẩn kiến trúc FSD Base Architecture tầng `shared/`.

**[ĐO 2026-09-05] Look hiện tại là bản CLONE Datacake**: Datacake chạy react-toastify v11 và ghi đè nền tinted theo mức — error `red-100` + chữ `red-700`, success `green-100`/`green-700`, warning `yellow-50`/`yellow-700`, info nền trắng; radius 6px, không viền, không kính mờ, progress bar đặc 5px, bounce gốc `Toastify__bounceInRight/OutRight`, Roboto 12.8px/400, rộng 312px. Bản "Sonner glass" cũ (backdrop blur, viền trái 3px, progress gradient) đã bị loại toàn bộ vì không tồn tại trên Datacake.

---

## 1. Cấu Trúc Thành Phần

```
src/shared/notify/
├── notify.ts          # Hàng đợi module-level, điều phối timer, gộp trùng, timeout 5s, pause/resume
├── notify.test.ts     # Kiểm thử đơn vị cho hàng đợi và timer
├── ToastHost.vue      # Component hiển thị (Teleport body), markup Sonner × Datacake, micro-drag guard
├── ToastHost.css      # Số đo + animation (KHÔNG scoped — xem chú thích cuối file), token màu ở app/styles/main.css
├── ToastHost.test.ts  # Kiểm thử component (render thẻ, progress bar, click/hover/active)
└── README.md          # Tài liệu kiến trúc này
```

---

## 2. Các Đặc Tính Kỹ Thuật Trọng Tâm

### 2.1. Chuỗi Xử lý Lỗi Tự Động
```
fetch
 ──► core/http/errors.ts               (Phân loại Network / Abort / Api / Schema)
 ──► core/http/response.ts             (Zod schema validation)
 ──► shared/errors/to-user-message.ts  (Dịch lỗi sang câu tiếng Việt chuẩn)
 ──► shared/notify/notify.ts           (Gộp trùng, gán timeout 5s, kích hoạt toast)
 ──► shared/notify/ToastHost.vue       (Render UI Datacake + progress bar đặc 5px)
```

### 2.2. Đặc tả Giao diện (clone Datacake, đo 2026-09-05)
- **Nền tinted theo mức** qua token `--toast-*-bg/fg/close/bar` khai hex đặc trong `app/styles/main.css` cho cả hai theme — gate 9 (`check-contrast.mjs`) nghiệm từng cặp. Progress bar là đồng hồ thuần thị giác nên KHÔNG khai cặp (lý do ghi ở chính CSS).
- **Thẻ phẳng**: radius 6px, padding 8px, min-height 64px, rộng 312px, không viền, shadow 2 lớp `0 1px 10px rgba(0,0,0,.1), 0 2px 15px rgba(0,0,0,.05)`.
- **Icon 4 mức**: path FILL gốc react-toastify v11 (trích từ `react-toastify@11.0.5/dist/index.mjs`, đúng bản Datacake bundle), màu = màu chữ mức.
- **Nút đóng X**: mark-up v11 `viewBox="0 0 14 16"` fill currentColor, hitbox 24×24, opacity 0.3 → 1 hover/focus, màu đậm hơn chữ một bậc (×800).
- **Thanh tiến trình**: đặc 5px, màu solid theo mức (×500), `scaleX(1) → scaleX(0)` trên GPU, dừng khi hover/focus.
- **Vào/ra**: keyframes bounce gốc `Toastify__bounceInRight` / `Toastify__bounceOutRight` (0.7s); icon zoomIn 0.3s; `prefers-reduced-motion` tắt hết.
- **Tương tác giữ nguyên**: micro-drag guard (< 6px là click, ≥ 6px + bôi đen là copy), close-on-click, Escape xoá sạch, pause/resume theo hover/focus.

### 2.3. Khả năng Tiếp cận (Accessibility)
- `role="alert"` cho thông báo lỗi; `role="status"` cho các thông báo thông thường.
- `role="progressbar" aria-label="notification timer"` trên thanh tiến trình.
- Hỗ trợ phím tắt `Escape` xóa sạch thông báo đang hiển thị.
- Tôn trọng thiết lập `prefers-reduced-motion: reduce`.
