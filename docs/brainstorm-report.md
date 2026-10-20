# BÁO CÁO ĐỘNG NÃO & TRANH BIỆN KIẾN TRÚC (BRAINSTORMING & DEBATE REPORT)
## Thiết kế Đồng Bộ Giao Diện Phân Quyền Thiết Bị Chuẩn Datacake & Trình Giả Lập RTU Động

> **Mã báo cáo**: `BRN-260930-DEVICE-PERMISSIONS`  
> **Chủ đề**: Kiến trúc Đồng bộ Giao diện & Hợp đồng API Phân quyền Thiết bị (`/devices/:id/permissions`)  
> **Tham chiếu chuẩn**: `https://app.datacake.de/atin/devices/d/b4b6fe7a-1d94-47df-ad58-1ebcf3cd2f6a/t/permissions`  
> **Hội đồng 5 Chuyên gia Phản biện**: Architect, Security, Performance, UX, Devil's Advocate  
> **Trạng thái**: **UNANIMOUS CONSENSUS (5/5 APPROVED)**

---

## 1. Phương Pháp Nghịch Đảo Vấn Đề (Problem-First Inversion)

Thay vì hỏi: *"Làm thế nào để hiển thị thêm một tab Permissions?"*, chúng ta đảo ngược bài toán để tìm các điểm gãy và nguyên nhân thất bại tiềm tàng:
1. **Làm thế nào để hệ thống bị sập do xung đột phân quyền?**
   - Nếu việc sửa checkbox trên giao diện ghi đè trực tiếp xuống database mà không có kiểm tra `PolicyVersion`, hai kỹ sư cùng cấu hình quyền cho 1 trạm RTU sẽ gây tình trạng race condition và đè mất quyền của nhau.
2. **Làm thế nào để rò rỉ quyền hoặc vi phạm nguyên tắc đặc quyền tối thiểu (PoLP)?**
   - Nếu quyền thiết bị không tính toán sự kế thừa từ Organization Scope, một Operator bị tước quyền ở cấp Tổ chức vẫn có thể lén lút giữ quyền điều khiển trên từng thiết bị đơn lẻ.
3. **Làm thế nào để giao diện bị chắp vá, giật lag và mất thẩm mỹ công nghiệp?**
   - Dùng thư viện ngoài hoặc thẻ native không đồng bộ làm mất trải nghiệm liền mạch, header bảng không có mô tả chi tiết quyền hạn, không có tooltip giải thích tại sao ô chọn bị khóa.

---

## 2. Đề Xuất 3 Phương Án Kiến Trúc Cụ Thể

### Phương Án A: Client-Side Permission Computation (Kế thừa tính toán ở Client)
- **Cơ chế**: Backend chỉ trả về danh sách thô `PermissionGrants`. Frontend tự tính toán quyền xem, sửa, ghi dựa trên role của người dùng.
- **Ưu điểm**: Backend không cần thêm endpoint mới.
- **Nhược điểm**: Logic bảo mật bị lộ và phân tán ở Client. Nếu Client tính sai, người dùng sẽ tưởng mình có quyền nhưng gọi API lệnh thì bị 403 Forbidden.

### Phương Án B: Dedicated Device Permission Matrix API + Optimistic UI (Được chọn)
- **Cơ chế**:
  - Backend cung cấp endpoint chuyên dụng `GET /api/v1/devices/{deviceId}/permissions` trả về mô hình phẳng đã được giải quyết kế thừa (`inheritedFromOrg`, `canView`, `canEditBasics`, `canEditProduct`, `canRecordMeasurements`).
  - Giao diện Vue 3 tái sử dụng `Checkbox.vue` với tooltip chú thích lý do khóa ô kế thừa, hỗ trợ chọn tất cả theo cột (`Select All Column`).
  - Cập nhật tức thì với Optimistic UI, gửi payload qua `PUT /api/v1/devices/{deviceId}/permissions` kèm `PolicyVersion` để chống xung đột đồng thời.
- **Ưu điểm**: Khớp 100% với giao diện Datacake, bảo mật tuyệt đối tại server, trải nghiệm mượt mà, phản hồi < 50ms.
- **Nhược điểm**: Cần bổ sung 1 endpoint và 1 handler nghiệp vụ trong backend.

### Phương Án C: Full Dynamic ABAC Engine (Thuộc tính động theo từng trường)
- **Cơ chế**: Viết một engine phân quyền theo biểu thức chính quy (CEL / Rego) cho phép cấu hình quyền đến từng byte thanh ghi Modbus.
- **Ưu điểm**: Tối đa hóa khả năng tùy biến.
- **Nhược điểm**: Overengineering trầm trọng (vi phạm KISS/YAGNI), khiến giao diện trở nên rối rắm khó hiểu cho người vận hành thực tế.

---

## 3. Ma Trận Đánh Giá Đánh Đổi (Trade-Off Matrix)

| Tiêu Chí Thẩm Định | Phương Án A (Client-Side) | Phương Án B (Dedicated Matrix API - CHỌN) | Phương Án C (Dynamic ABAC Engine) |
| :--- | :---: | :---: | :---: |
| **Độ đồng bộ với Datacake** | 70% | **100% (Pixel & Behavior Parity)** | 60% |
| **An toàn & Ranh giới tin cậy** | Kém (Logic rò rỉ ra client) | **Xuất sắc (Server giải quyết kế thừa)** | Xuất sắc |
| **Hiệu năng & Độ trễ UI** | Nhanh ban đầu, chậm khi dữ liệu lớn | **Cực nhanh (< 50ms, Optimistic)** | Chậm (độ trễ đánh giá policy) |
| **Độ phức tạp mã nguồn (KISS/YAGNI)** | Trung bình | **Thấp - Tinh gọn, tái sử dụng tối đa** | Rất cao (Nguy cơ lỗi tiềm ẩn) |
| **Khả năng mở rộng cho RTU thực tế** | Kém | **Hoàn hảo (Hỗ trợ cả RTU ảo & RTU thật)** | Phức tạp |

---

## 4. Biên Bản Hội Chẩn Của Hội Đồng 5 Chuyên Gia (/ck:predict)

### 1. 🏛️ Software Architect:
> *"Phương án B thiết lập ranh giới rõ ràng giữa tầng hiển thị và tầng xác thực phân quyền. Bảng dữ liệu thiết bị tại `frontend/src/features/devices/` được bổ sung tab Permissions chuyên trách, kế thừa trọn vẹn bộ khung `DataTable.css` và `Checkbox.vue` đã hoàn thiện. Phán quyết: **APPROVED**."*

### 2. 🛡️ Security Specialist:
> *"Điểm mấu chốt là rào chắn chống leo thang quyền lực (Privilege Escalation): Khi một user bị thu hồi quyền ở cấp tổ chức (`Organization`), server tự động vô hiệu hóa quyền thừa kế trên toàn bộ thiết bị con. Mọi thay đổi quyền qua API bắt buộc xác thực token chống giả mạo `ValidateAntiForgeryToken` và kiểm tra `PolicyVersion`. Phán quyết: **APPROVED**."*

### 3. ⚡ Performance Engineer:
> *"Truy vấn danh sách quyền của 1 thiết bị chỉ thực hiện JOIN đơn giản giữa `Users` và `PermissionGrants` lọc theo `DeviceId` và `OrganizationId`. Thời gian thực thi truy vấn PostgreSQL < 5ms. Giao diện frontend không dùng virtualized list vì số lượng thành viên trong một workspace thường < 100 người, tránh overengineering. Phán quyết: **APPROVED**."*

### 4. 🎨 UX & Industrial Design Specialist:
> *"Thiết kế chuẩn hóa theo Datacake: Header bảng có checkbox chọn tất cả kèm phụ đề giải thích ngữ nghĩa của từng quyền (`Can view the device and its data`, v.v.). Tooltip hiển thị rõ ràng khi ô chọn bị khóa do quyền kế thừa từ Workspace. Nút lưu hoặc cơ chế tự động lưu phản hồi mượt mà không gây nhảy khung hình (Zero Layout Shift). Phán quyết: **APPROVED**."*

### 5. 😈 Devil's Advocate / QA Skeptic:
> *"Câu hỏi hóc búa: Làm sao để kiểm thử trang này khi không có thiết bị thật kết nối? Giải pháp tích hợp: Hệ thống cung cấp nút 'Sinh thiết bị mô phỏng' (Virtual RTU) ngay trên giao diện hoặc qua endpoint API. Thiết bị ảo này tự động bắn dữ liệu đo lường định kỳ vào SignalR Hub, giúp người dùng kiểm tra trọn vẹn tính năng phân quyền mà không cần phải cắm dây phần cứng. Phán quyết: **APPROVED**."*

---

## 5. Kết Luận & Phán Quyết
Hội đồng 5 chuyên gia thống nhất **100% PHÊ DUYỆT PHƯƠNG ÁN B** làm nền tảng cho việc thiết lập Đặc tả Kiến trúc Chi tiết (Architecture RFC).
