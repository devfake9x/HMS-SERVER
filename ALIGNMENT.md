# BẢN ĐỒ ĐỒNG BỘ NGỮ CẢNH & HIỆP ƯỚC HỆ THỐNG (ALIGNMENT.md)
## Hệ Thống Quản Lý & Điều Khiển Hạm Đội RTU Công Nghiệp (HMS-SERVER)

> **TÀI LIỆU ĐÃ SUPERSEDED — KHÔNG PHẢI NGUỒN NORMATIVE HIỆN HÀNH**  
> Bản này là alignment lịch sử của kiến trúc telemetry/SCADA cũ. Không dùng
> nó để chọn stack, lưu telemetry, derive command key, hoặc xác nhận firmware
> compatibility. Nguồn hiện hành là
> `plans/260924-2044-hms-server-management/plan.md` và các phase/decision
> record đã reconcile; behavior firmware phải được kiểm chứng từ `HMS-IDF`.

> **Dự án**: HMS-SERVER (Enterprise Industrial IoT & SCADA Web Server)  
> **Firmware nguồn tương thích**: `D:\WORKSPACE\PROJECT\HMS-IDF` (ESP32-S3 RTU 8MB PSRAM)  
> **Trạng thái**: TÀI LIỆU LỊCH SỬ / SUPERSEDED — KHÔNG KHÓA ĐẶC TẢ HIỆN HÀNH  
> **Nguyên tắc lịch sử**: Đây là quan điểm cũ, không phải bằng chứng tương thích hay nguồn chọn kiến trúc hiện hành.

---

## 1. Định Nghĩa Bài Toán (Problem Statement)
Xây dựng một hệ thống máy chủ công nghiệp hoàn chỉnh (Fullstack Web Server + SCADA Platform) để:
1. **Quản lý hạm đội thiết bị phân tán (Fleet Management)**: Quản trị hàng nghìn trạm RTU ESP32-S3 hiện trường kết nối qua 4G LTE Modem, WiFi hoặc Ethernet.
2. **Cấp phát định danh & Phân quyền động (Dynamic Per-Device MQTT Provisioning)**: Mỗi RTU được cấp 1 cặp tài khoản MQTT riêng biệt (`username`, `password_hash`) và 1 khóa bí mật (`master_secret`). Broker EMQX xác thực qua Webhook và cấp **Preset ACL** khóa cứng RTU chỉ được giao tiếp trong namespace `hms/<device_id>/#`.
3. **Xử lý đa luồng dữ liệu (Dual-Stream Ingestion)**: Phân tách minh bạch giữa luồng **Realtime Telemetry** (đẩy tức thời lên Web SCADA) và luồng **Offline Replay** (dữ liệu tích lũy từ thẻ nhớ SD khi mất sóng 4G xả lên, ghi đúng mốc thời gian lịch sử).
4. **Kênh điều khiển hai chiều tin cậy tuyệt đối (C2 Command Plane)**: Phát lệnh cấu hình và điều khiển Modbus, ADC, DI/DO, Network, Reboot có ký số **HMAC-SHA256** phái sinh từ MAC phần cứng qua **HKDF-SHA256**, tuân thủ giới hạn **1024 bytes** của task queue ESP32.
5. **Nền tảng SCADA Web hiện đại**: Giao diện Digital Twin, giám sát lưới Tag thời gian thực, bảng điều khiển hiện trường có khóa an toàn hai bước, phân quyền vận hành nghiêm ngặt qua **Better-Auth RBAC**.

---

## 2. Ranh Giới Kỹ Thuật & Anti-Goals (Những Điều Cấm Tuyệt Đối)

| # | Anti-Goal (Điều cấm kỵ) | Phân tích rủi ro & Lý do cấm | Cơ chế thực thi chuẩn |
|---|---|---|---|
| **AG-1** | **CẤM dùng chung tài khoản MQTT** | Lộ một thiết bị sẽ làm sập toàn bộ mạng lưới; không thể truy vết thiết bị nào gửi dữ liệu giả mạo. | Server tự sinh credentials độc lập cho từng RTU, ủy quyền qua EMQX Dynamic Auth Webhook với Preset ACL. |
| **AG-2** | **CẤM gửi gói tin điều khiển > 1024 bytes** | Firmware ESP32 có queue nhận lệnh giới hạn cứng `CMD_SIGNABLE_MAX = 1024 bytes`. Gói tin lớn hơn sẽ bị drop ngay tại tầng network. | Lệnh MQTT chỉ chứa JSON cấu hình tinh gọn. Mọi file nhị phân lớn (Firmware OTA, chứng chỉ, backup) chuyển sang dùng **Presigned S3/MinIO HTTP URLs**. |
| **AG-3** | **CẤM nối trực tiếp Web Client vào Broker MQTT** | Trình duyệt kết nối WSS trực tiếp vào broker làm lộ cấu trúc topic, mất kiểm soát RBAC người dùng web và không thể bảo vệ khóa ký số HMAC ở client side. | Web Client kết nối qua **WebSocket Gateway nội bộ** của Web Server, được bảo vệ bằng session của Better-Auth. |
| **AG-4** | **CẤM ghi trực tiếp Telemetry vào Database quan hệ** | Khi 1,000 RTU cùng xả dữ liệu từ thẻ nhớ sau sự cố mất điện/mất mạng (Reconnect Storm), database sẽ nghẽn khóa và sập ngay lập tức. | Sử dụng **Redis Streams** làm bộ đệm chống sốc (Shock Absorber), Ingest Worker xả batch vào **TimescaleDB Hypertables**. |
| **AG-5** | **CẤM ghi đè thời gian hiện tại lên dữ liệu Replay** | RTU lưu dữ liệu ngoại tuyến trên thẻ nhớ khi mất sóng 4G. Nếu Server lấy thời gian hiện tại khi nhận gói tin, toàn bộ biểu đồ lịch sử quan trắc sẽ bị sai lệch hoàn toàn. | Server bắt buộc trích xuất trường thời gian gốc (`ts`) từ gói tin cảm biến để ghi vào cơ sở dữ liệu lịch sử. |
| **AG-6** | **CẤM gửi lệnh thiếu chữ ký số HMAC-SHA256** | Kẻ xấu trên đường truyền có thể inject gói tin giả mạo để đóng ngắt van, tắt máy bơm hoặc factory reset thiết bị. | Mọi lệnh điều khiển bắt buộc có chữ ký HMAC-SHA256 phái sinh từ MAC phần cứng, gắn ở trường cuối cùng của envelope: `,"sig":"<hex>"}`. |
