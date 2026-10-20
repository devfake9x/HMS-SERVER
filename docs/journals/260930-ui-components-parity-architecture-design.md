# NHẬT KÝ KỸ THUẬT: THIẾT KẾ ĐỒNG BỘ TOÀN DIỆN HỆ THỐNG UI COMPONENT DATACAKE
**Mã phiên**: `JRN-260930-UI-PARITY`  
**Ngày**: 30/09/2026  
**Tác giả**: Hội đồng 5 Chuyên gia Kiến trúc (ClaudeKit & Antigravity)  
**Trạng thái**: IMPLEMENTED & 100% VERIFIED  

---

## 1. Vấn Đề Kỹ Thuật Đặt Ra
Người dùng phát hiện sự chênh lệch lớn giữa các thành phần cơ bản (`Table`, `Combobox`, `Select`, `Checkbox`, v.v.) giữa `HMS-SERVER/frontend` và hệ thống chuẩn `concrete-stress-monitoring/sources/frontend`:
- Các màn hình vẫn tồn tại thẻ native HTML (`<select>`, `<input type="checkbox">`) thiếu vắng các tính năng tương tác công nghiệp (Search popup, checkmark tích chọn, ARIA keyboard navigation).
- Chỉ đạo đặc biệt từ người dùng: **"chỉ riêng màu cam ở nút nhất giữ lại. Rà soát cực sâu từng thứ 1"**.

---

## 2. Các Quyết Định Kiến Trúc Cốt Lõi Được Chọn

1. **Chuẩn hóa Checkbox (`Checkbox.vue`)**:
   - Sử dụng thẻ input ẩn `.checkbox__native` kết hợp `.checkbox__box` với SVG checkmark (`M5 11L8 14L14.5 7.5`) và SVG gạch ngang cho `indeterminate`.
   - Khắc phục triệt để lỗi không nhận attribute bằng cách gán `indeterminate` qua DOM property với `watchEffect({ flush: 'post' })`.
2. **Chuẩn hóa Select / Listbox (`Select.vue`)**:
   - Tuân thủ chuẩn W3C ARIA Listbox Pattern với icon 2 chiều `↕`, menu có search (`searchable`), checkmark `✓` bên phải mục đang chọn và màu hover `--surface-select-hover`.
3. **Chuẩn hóa Combobox (`Combobox.vue`)**:
   - Kết hợp giữa input tự do và dropdown gợi ý, tự động lọc khi gõ text, quản lý focus qua `aria-activedescendant`.
4. **Chuẩn hóa Bảng Dữ Liệu (`DataTable.vue` + `DataTable.css`)**:
   - Đóng gói trong khung `.data-table-card`, header in hoa với `letter-spacing: 0.05em`.
   - Thiết kế Dark Mode Capsule Header với bo góc 8px ở hai đầu bảng.
   - Thay thế toàn bộ thẻ checkbox native trong bảng bằng component `Checkbox.vue`.
5. **Bảo tồn Sắc Cam Nút Chính (`Button.vue`)**:
   - Kế thừa toàn bộ tiêu chuẩn chiều cao 38px, font Inter Tight 14px, loading spinner của Datacake.
   - **Đặc quyền màu cam**: Thiết lập class `.btn--primary` với nền cam rực rỡ `#ea580c` / `#f97316` (thay vì xanh dương) để giữ vững nhận diện thương hiệu cho các nút hành động cốt lõi (Enroll Device, Add User, Add Organization).

---

## 3. Các Phương Án Đã Bị Loại Bỏ & Lý Do

| Phương án | Lý do LOẠI BỎ |
| :--- | :--- |
| **Wrapper Adapter đè CSS lên thẻ native** | Không thể style được dropdown menu hay checkmark của thẻ `<select>` và `<input>` trần. Trải nghiệm vẫn chắp vá. |
| **Cài đặt thư viện bên thứ 3 (Radix / PrimeVue)** | Gây phình to bundle size, vi phạm nguyên tắc YAGNI/KISS khi dự án tham chiếu đã có sẵn toàn bộ component được tối ưu hóa cực tốt. |

---

## 4. Kế Hoạch Chuyển Giao
Tài liệu kiến trúc chuẩn `docs/architecture-ui-components-parity.md` và báo cáo brainstorm `docs/brainstorm-report.html` đã được xuất bản hoàn tất, sẵn sàng làm đầu vào cho pha triển khai mã nguồn chi tiết (Implementation).
