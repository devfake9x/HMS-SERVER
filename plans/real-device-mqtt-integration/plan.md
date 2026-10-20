# Kế Hoạch Triển Khai: Tích Hợp Thiết Bị Thật ESP32-S3 (maitech-rtu.local) & MQTT Telemetry Bridge

## 1. Mục tiêu & Phạm vi
* Đưa thiết bị thật `MAITECH-RTU-870C` (ID: `a74041f1-04cd-4270-a8a7-d6985e306f17`) vào hệ thống quản lý HMS-SERVER.
* Cầu nối Telemetry Ingestion Bridge: Thu nhận luồng dữ liệu thời gian thực từ `hms_toan_lab/gateway_870c/#` trên MQTT Broker.
* Cung cấp API backend để nhận và cache telemetry phần cứng (Uptime, Heap, Ethernet, Viettel 4G, Services).
* Hiển thị Card Telemetry Sống trên Frontend (`DeviceDetailView.vue`) chuẩn phong cách Datacake.
* Tuân thủ triệt để: Chỉ thao tác với ESP32 qua Web API, không nạp lại firmware.

---

## 2. Ma trận 12 Chiều Edge Cases

1. **Null / Undefined Payload**: Xử lý gói tin MQTT payload rỗng hoặc JSON hỏng mà không gây sập bridge.
2. **Broker Disconnect / Reconnect**: Tự động kết nối lại khi mạng gián đoạn, không rò rỉ socket hay bộ nhớ.
3. **Device Offline Transition**: Nhận biết `state: "offline"` từ LWT hoặc timeout và cập nhật giao diện ngay lập tức.
4. **Viettel 4G / Eth Dual Uplink**: Xử lý tình huống thiết bị chuyển đổi linh hoạt giữa Ethernet và 4G Modem.
5. **High Telemetry Rate**: Bầu đệm (debounce/throttle) trước khi broadcast SignalR để tránh nghẽn giao diện.
6. **Concurrent Web Updates**: Bảo toàn tính nhất quán khi người dùng xem chi tiết thiết bị lúc telemetry ập tới.
7. **Cross-Site Request Forgery (CSRF)**: Mọi endpoint backend tiếp nhận telemetry nội bộ đều có bảo vệ hoặc xác thực token.
8. **Stale Data Cleanup**: Tự động đánh dấu dữ liệu cũ nếu quá 30 giây không có nhịp tim mới từ thiết bị.
9. **Zero Values**: Hiển thị chính xác khi RSSI = 0 hoặc tag count = 0, không nhầm lẫn với missing data.
10. **Device ID Mapping**: Ánh xạ chuẩn xác giữa `gateway_870c` (MQTT hardware identifier) và UUID trong CSDL (`a74041f1-04cd-4270-a8a7-d6985e306f17`).
11. **Dark / Light Theme Parity**: Giao diện thẻ telemetry hiển thị đồng bộ tuyệt đối trên cả nền sáng và nền tối.
12. **Orange Action Accent**: Duy trì màu cam chuẩn `#ea580c` / `#f97316` cho nút hành động chính.

---

## 3. Các Giai Đoạn Triển Khai (Phases)

* **Phase 1: Backend Telemetry Storage & API**:
  * Bổ sung endpoint `GET /api/v1/devices/{id}/telemetry` và `POST /api/v1/devices/{id}/telemetry` vào [`DevicesController.cs`](file:///d:/WORKSPACE/PROJECT/HMS-SERVER/src/Hms.Server.Api/Controllers/DevicesController.cs) (hoặc Controller tương ứng).
  * Bộ nhớ đệm telemetry thiết bị `IDeviceTelemetryCache` cho phép lưu snapshot mới nhất và đẩy qua SignalR.
* **Phase 2: Ingestion & Bridge Daemon**:
  * Tạo service `tools/mqtt-broker/hms-telemetry-bridge.js`: Subscribe `hms_toan_lab/gateway_870c/#`, chuẩn hóa dữ liệu, post về API máy chủ.
  * Tự động khởi chạy và duy trì hoạt động ngầm.
* **Phase 3: Frontend Live Hardware Telemetry Component**:
  * Cập nhật [`adminApi.ts`](file:///d:/WORKSPACE/PROJECT/HMS-SERVER/frontend/src/core/api/adminApi.ts) để hỗ trợ fetch telemetry.
  * Thêm Card `Live Hardware Status (ESP32-S3 RTU)` vào [`DeviceDetailView.vue`](file:///d:/WORKSPACE/PROJECT/HMS-SERVER/frontend/src/features/devices/views/DeviceDetailView.vue).
* **Phase 4: Kiểm thử TDD & Xác minh trực quan qua Chrome CDP**:
  * Chạy `dotnet test` và `npm run test:run` để đảm bảo 100% tests vượt qua.
  * Chụp ảnh màn hình giao diện web hiển thị thông số thật của ESP32 (Uptime 5+ ngày, Viettel 4G, Eth IP).
