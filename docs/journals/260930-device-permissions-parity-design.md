# NHẬT KÝ KỸ THUẬT: THIẾT KẾ ĐỒNG BỘ MA TRẬN PHÂN QUYỀN THIẾT BỊ THEO CHUẨN DATACAKE
**Mã phiên**: `JRN-260930-DEVICE-PERMISSIONS-DESIGN`  
**Ngày**: 30/09/2026  
**Tác giả**: Hội đồng 5 Chuyên gia Kiến trúc (ClaudeKit & Antigravity)  
**Trạng thái**: **ARCHITECTURE READY**

---

## 1. Bối Cảnh & Dữ Liệu Khảo Sát Thực Nghiệm
Người dùng yêu cầu đồng bộ chính xác 100% giao diện trang quản lý quyền của thiết bị theo mẫu:
`https://app.datacake.de/atin/devices/d/b4b6fe7a-1d94-47df-ad58-1ebcf3cd2f6a/t/permissions`
Đồng thời đưa ra lựa chọn kết nối:
- Thiết bị thật tại mạng nội bộ: `http://maitech-rtu.local/login` (RTU ESP32-S3 `HMS-IDF`).
- Hoặc thiết bị ảo giả lập qua MQTT được tạo trực tiếp từ Web, không hardcode.

Thông qua kết nối Chrome CDP trên port 9222, Hội đồng đã trích xuất trực tiếp ảnh chụp màn hình `datacake_permissions_page.png` và cấu trúc DOM HTML thật của Datacake để làm cơ sở thiết kế chuẩn mực.

---

## 2. Các Quyết Định Kiến Trúc Được Chọn
1. **Thiết kế Bảng Ma Trận Phân Quyền (`Permissions Card & Table`)**:
   - Header bảng có checkbox chọn tất cả theo cột và mô tả chi tiết:
     - `View`: *Can view the device and its data*
     - `Metadata`: *Can edit device metadata*
     - `Definition`: *Can edit the device definition*
     - `Measurements`: *Can record measurements*
   - Xử lý quyền kế thừa từ Tổ chức/Workspace: Hiển thị checkbox khóa xám (`disabled="" checked=""`) kèm tooltip giải thích lý do.
2. **Hợp Đồng API Chuyên Biệt**:
   - `GET /api/v1/devices/{deviceId}/permissions`: Trả về danh sách thành viên kèm trạng thái quyền đã tính toán sự kế thừa từ Organization.
   - `PUT /api/v1/devices/{deviceId}/permissions`: Cập nhật quyền hạn vi mô, kiểm tra `PolicyVersion` chống ghi đè đồng thời.
3. **Mô Đun Giả Lập RTU MQTT Động (Virtual RTU Simulator)**:
   - Cho phép tạo thiết bị bất kỳ lúc nào trên Web ("Enroll Device").
   - Service nền tự động mô phỏng bản tin đo lường định kỳ 5 giây, bắn vào SignalR Hub để kiểm tra giao diện thời gian thực mà không cần nạp cứng dữ liệu trong mã nguồn.

---

## 3. Các Phương Án Đã Bị Loại Bỏ
- **Tính toán quyền hoàn toàn ở Client**: Bị loại bỏ vì làm rò rỉ logic phân quyền ra trình duyệt và gây lỗi lệch pha quyền hạn giữa Client và Server.
- **Dynamic ABAC Rule Engine**: Bị loại bỏ vì overengineering không cần thiết, làm phức tạp hóa trải nghiệm của người vận hành công nghiệp.
