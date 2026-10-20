# Ma Trận Kịch Bản Kiểm Thử Phần Cứng RTU Qua Giao Diện Web (RTU Web HIL Scenario Matrix)

> **Mã ma trận:** `SCN-RTU-WEB-HIL-261001`  
> **Phạm vi kiểm thử:** Toàn diện các API và giao diện Web của ESP32-S3 RTU tại `http://maitech-rtu.local/` thông qua Chrome Remote Debugging (CDP / WebSocket) kết hợp với HMS-SERVER C2 Platform.  
> **Quy chuẩn:** Pipeline Kiểm Thử Chuyên Sâu TDD v3.0.0 (`goal-test`).

---

## 1. Bảng Ma Trận Kịch Bản Kiểm Thử Đa Chiều (12 Dimensions)

| Mã Case | Phân nhóm kiểm thử | Kịch bản thử nghiệm | Dữ liệu đầu vào (Input) | Kết quả kỳ vọng (Expected Output) |
|---|---|---|---|---|
| **TC-HIL-01** | Happy Path: DO Toggle ON | Bật rơ-le kênh 1 qua Web API | `POST /api/do/set` `{"channel": 1, "value": true}` | `status: 200`, `success: true`, `data.state: true` |
| **TC-HIL-02** | Happy Path: DO Toggle OFF | Tắt rơ-le kênh 1 qua Web API | `POST /api/do/set` `{"channel": 1, "value": false}` | `status: 200`, `success: true`, `data.state: false` |
| **TC-HIL-03** | Happy Path: Multi-Channel | Điều khiển liên tiếp DO4, DO5, DO6 | `POST /api/do/set` với channel 4, 5, 6 | `status: 200`, các kênh phản hồi độc lập chính xác |
| **TC-HIL-04** | Happy Path: Pulse DO | Phát xung rơ-le kênh 1 (200ms) | `POST /api/do/pulse` `{"channel": 1, "durationMs": 200}` | `status: 200`, rơ-le kích hoạt rồi tự ngắt sau 200ms |
| **TC-BND-01** | Boundary: Channel 0 | Gọi điều khiển kênh dưới ngưỡng | `POST /api/do/set` `{"channel": 0, "value": true}` | `status: 400`, lỗi `"Invalid channel"` |
| **TC-BND-02** | Boundary: Channel 7 | Gọi điều khiển kênh vượt ngưỡng (6 DOs) | `POST /api/do/set` `{"channel": 7, "value": true}` | `status: 400`, lỗi `"Invalid channel"` |
| **TC-BND-03** | Boundary: Channel Âm | Gọi điều khiển kênh với số âm | `POST /api/do/set` `{"channel": -1, "value": true}` | `status: 400`, lỗi `"Invalid channel"` |
| **TC-MAL-01** | Malformed: Empty Body | Gửi request body rỗng | `POST /api/do/set` `{}` | `status: 400`, lỗi `"Missing channel or value"` |
| **TC-MAL-02** | Malformed: Missing Value | Gửi request thiếu giá trị `value` | `POST /api/do/set` `{"channel": 1}` | `status: 400`, lỗi `"Missing channel or value"` |
| **TC-MAL-03** | Malformed: Invalid JSON | Gửi chuỗi JSON không hợp lệ | `POST /api/do/set` `"not-json"` | `status: 400`, lỗi `"Invalid JSON"` |
| **TC-SEC-01** | Auth: Missing Token | Gửi request không kèm auth token | `POST /api/do/set` (No Authorization header) | `status: 401`, từ chối truy cập |
| **TC-SEC-02** | Auth: Invalid Token | Gửi token giả mạo | `POST /api/do/set` `Authorization: Bearer fake123` | `status: 401`, từ chối truy cập |
| **TC-CON-01** | Concurrency: Mutex Lock | Gửi 10 lệnh toggle dồn dập | 10 async fetch requests đồng thời | Không panic, mutex serialize chuẩn, 100% trả về 200 |
| **TC-RES-01** | Memory: Heap Leak Check | Đo bộ nhớ DRAM trước & sau 50 calls | Gọi liên tục 50 lần `/api/do/status` và `/api/system/info` | `freeInternalHeap` ổn định (dao động < 2KB, không rò rỉ) |
| **TC-TEL-01** | Telemetry: System Info | Đọc thông tin phần cứng RTU | `GET /api/system/info` | `status: 200`, `projectName: "HMS"`, `ntpSynced: true` |
| **TC-TEL-02** | Telemetry: System Health | Đọc tình trạng sức khỏe phân hệ | `GET /api/system/health` | `status: 200`, `conditions` chứa thông tin network, mqtt, heap |
| **TC-TEL-03** | Telemetry: Digital Inputs | Đọc trạng thái 4 kênh DI | `GET /api/di/status` | `status: 200`, trả về 4 channels GPIO 1, 2, 3, 4 |
| **TC-TEL-04** | Telemetry: MQTT Config | Đọc cấu hình MQTT Broker | `GET /api/mqtt/config` | `broker: "broker.emqx.io"`, `clientId: "hms_gateway_870c"` |
| **TC-E2E-01** | E2E: Telemetry Bridge | RTU Web thay đổi DO phản ánh qua MQTT | Đổi DO -> Telemetry bridge đọc MQTT | Telemetry bridge ghi nhận cập nhật |
| **TC-UI-01** | UI Parity & Visual State | Chụp ảnh kiểm chứng giao diện Web | Chrome CDP chụp màn hình `maitech-rtu.local` | UI trực quan, hiển thị đúng trạng thái rơ-le |

---

## 2. Tiêu Chí Nghiệm Thu (Acceptance Criteria)
1. **100% Test Cases Đạt Chuẩn (Green)**: Toàn bộ 20 kịch bản kiểm thử trên phải vượt qua.
2. **Bảo toàn phần cứng vi điều khiển**: Bộ nhớ DRAM không bị cạn kiệt, RTU không bị crash hoặc reset trong suốt quá trình test.
3. **Mã nguồn firmware `HMS-IDF` không bị can thiệp (0 bytes modified)**.
