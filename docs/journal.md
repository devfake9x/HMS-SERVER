# NHẬT KÝ KỸ THUẬT (ENGINEERING JOURNAL)
## Dự án: HMS-SERVER — Hệ Thống Máy Chủ SCADA Quản Lý RTU ESP32

> **TÀI LIỆU LỊCH SỬ — KHÔNG PHẢI NGUỒN NORMATIVE HIỆN HÀNH**  
> Nội dung dưới đây ghi lại kiến trúc telemetry/SCADA cũ và các giả định đã bị
> superseded, gồm HKDF/master-secret, TimescaleDB và Redis Streams. Không dùng
> làm contract cho implementation hiện tại. Xem
> `plans/260924-2044-hms-server-management/plan.md`,
> `docs/journals/260924-hms-server-management-design.md` và
> `docs/journals/260930-frontend-ui-datacake-parity-design.md`,
> `docs/journals/260930-security-audit-and-code-review.md`,
> `docs/journals/260930-deep-implementation-planning-datacake-parity.md`,
> `docs/journals/260930-milestone-3-datacake-parity-implementation-summary.md`,
> `docs/journals/260930-ui-components-parity-design.md`,
> `docs/journals/260930-ui-components-parity-verification.md`,
> `docs/journals/260930-device-permissions-parity-design.md` và
> `docs/journals/260930-device-permissions-parity-implementation.md` (kèm
> `docs/architecture-device-permissions-parity.md`,
> `docs/architecture-ui-components-parity.md`, `docs/architecture-frontend-ui-sync.md`,
> `docs/security-audit-report.md`, `docs/project-roadmap.md`,
> `docs/research-report.md`, `docs/brainstorm-report.html` và
> `plans/260930-2200-device-permissions-datacake-parity/plan.md`)
> để biết quyết định hiện hành.

> **Mã phiên**: `JRN-HMS-SERVER-2026-FINAL`  
> **Ngày thực hiện**: 2026-09-21  
> **Nhiệm vụ**: Khảo sát sâu hệ thống lớn tại `D:\WORKSPACE\PROJECT\HMS-IDF`, phản biện kiến trúc và chốt đặc tả thiết kế hệ sinh thái máy chủ điều khiển.

---

## 1. Bối Cảnh & Phân Tích Hệ Thống Lớn (`HMS-IDF`)
Khảo sát toàn bộ kiến trúc data-plane và firmware tại `D:\WORKSPACE\PROJECT\HMS-IDF` cho thấy:
- Thiết bị RTU là dòng vi điều khiển công nghiệp **ESP32-S3 (8MB PSRAM)** với trục dữ liệu **Tag-DB** quản lý tối đa 512 tags, chuỗi Write-Hook (Scale, Formula $ax+b$, Filter), bộ đệm PSW ring buffer (1-1024 mẫu) tính toán AVG/MIN/MAX.
- RTU có cơ chế lưu trữ ngoại tuyến **3-Tier Offline Store (Serialize-then-Spill)** ghi file `.hst` vào thẻ nhớ MicroSD khi mất sóng 4G và tự động **Replay** khi có mạng lại.
- RTU có 9 namespace lệnh (`system`, `config`, `adc`, `network`, `mqtt`, `broker`, `status`, `battery`, `modbus`), có hàng đợi nhận lệnh giới hạn cứng **1024 bytes** (`CMD_SIGNABLE_MAX`), yêu cầu chữ ký số **HMAC-SHA256** phái sinh từ MAC phần cứng qua thuật toán **HKDF-SHA256**.

---

## 2. Các Quyết Định Kiến Trúc Cốt Lõi Được Chọn

1. **Broker & Phân Quyền Per-Device (EMQX v5.8+ Preset ACL)**:
   - Sử dụng EMQX với tính năng Dynamic HTTP Authentication Webhook. Khi RTU kết nối, Server kiểm tra mật khẩu Bcrypt và trả về **Preset ACL** ngay trong gói tin bắt tay (Handshake).
   - RTU bị khóa cứng trong topic `hms/<device_id>/#`, loại bỏ 100% chi phí network call cho mỗi tin nhắn cảm biến tiếp theo.
2. **Tầng Đệm Chống Sốc (Redis Streams Ingestion)**:
   - Khi 1,000 RTU xả hàng triệu bản tin từ thẻ nhớ SD sau giông bão, EMQX chuyển tiếp trực tiếp vào Redis Streams (`stream:telemetry`).
   - Ingest Worker đọc từng lô 500 bản ghi và thực hiện multi-row insert vào TimescaleDB, triệt tiêu nguy cơ cạn kiệt Connection Pool và khóa I/O đĩa.
3. **Cơ Sở Dữ Liệu Kết Hợp (PostgreSQL 16 + TimescaleDB)**:
   - Bảng quan hệ ACID cho Users, Sessions, Devices, Credentials, Secrets.
   - Bảng chuỗi thời gian `telemetry_records` dùng Hypertables phân vùng 7 ngày, nén Columnar ZSTD/Gorilla sau 2 ngày (giảm 92% đĩa), và xóa tự động sau 90 ngày bằng `drop_chunks()` tức thì không lock bảng.
4. **Kênh Điều Khiển C2 Ký Số HMAC-SHA256**:
   - Khóa ký số phái sinh từ `master_secret` và `mac_address` qua HKDF-SHA256 khớp 100% với firmware `HMS-IDF`.
   - Gói tin điều khiển bọc đúng canonical format `root.data.action` và gắn `,"sig":"<hex>"}` ở trường cuối cùng, kích thước luôn dưới 1024 bytes.
5. **Giao Diện SCADA Web Realtime (React + TanStack + Better-Auth)**:
   - Web Client kết nối qua WebSocket Gateway nội bộ của Server.
   - Phân quyền chặt chẽ: SuperAdmin, Operator, Viewer. Lệnh nguy hiểm (Class F - Reboot, Reset) bắt buộc xác nhận 2 bước `confirm: "REBOOT-<device_id>"`.

---

## 3. Các Phương Án Đã Xem Xét & Lý Do Loại Bỏ

| Phương án | Đánh giá & Lý do LOẠI BỎ |
|---|---|
| **Mosquitto với cấu hình file tĩnh** | Loại bỏ vì không hỗ trợ cấp phát động tài khoản MQTT per-device. Mỗi khi thêm RTU mà phải reload broker là điều bất khả thi trong vận hành. |
| **Microservices phân tán (Go + Kafka + ClickHouse)** | Loại bỏ vì **Overengineering trầm trọng**, tiêu tốn quá nhiều RAM (>12GB) và làm gãy tính Type-Safe đồng bộ giữa Backend và Frontend. |
| **Trình duyệt kết nối trực tiếp vào Broker qua WSS** | Loại bỏ vì **Rủi ro bảo mật nghiêm trọng**: Lộ cấu trúc topic nội bộ, mất kiểm soát RBAC người dùng web và không thể bảo vệ khóa ký số HMAC ở client side. |

---

## 4. Rủi Ro Chấp Nhận & Phương Án Giảm Thiểu

1. **Rủi ro Lệch Đồng Hồ (Clock Skew)**:
   - *Rủi ro*: Nếu đồng hồ RTU lệch quá 60 giây so với Server, lệnh có `ttl_ms` sẽ bị firmware reject (`future_timestamp` hoặc `expired`).
   - *Biện pháp*: Server tự động gửi lệnh đồng bộ thời gian `system.sync_ntp` định kỳ khi phát hiện RTU khởi động.
2. **Giới Hạn Kích Thước Lệnh 1024 Bytes**:
   - *Rủi ro*: Cấu hình Modbus nhiều thanh ghi có thể vượt quá 1024 bytes.
   - *Biện pháp*: Server chia nhỏ cấu hình thành nhiều lệnh hoặc nén tên trường ngắn gọn, tuyệt đối không truyền file nhị phân qua MQTT.
3. **Duy Trì Ngữ Cảnh Khi Bước Vào Lập Trình**:
   - *Biện pháp*: Đã thiết lập bộ 16 file neo ngữ cảnh (Anchor Context Files) định nghĩa các hợp đồng Zod Type-Safe trong `packages/shared`.
