# Kỹ thuật: Khắc phục Hiện Tượng Lệch Trạng Thái Relay DO1 Giữa Local Web và HMS-SERVER qua MQTT

- **Ngày thực hiện:** 01/10/2026
- **Phạm vi tác vụ:** `[PLAN] = Xử lý vấn đề tại sao ở web local DO1 on còn web qua mqtt thì không?`
- **Mã thiết bị phần cứng:** `MAITECH-RTU-870C` (Serial / Device ID: `870c870c-870c-870c-870c-870c870c870c`)
- **Nguyên tắc bất di bất dịch:** Tuyệt đối không can thiệp, không sửa source firmware thiết bị tại `D:\WORKSPACE\PROJECT\HMS-IDF` (Read-only 100%).

---

## 1. Hiện Tượng (Symptoms & Divergence)

1. **Trên Local Web RTU (`http://maitech-rtu.local/ioControl/digitalOutput`):**
   - Kênh **DO-1 (GPIO Pin 21)** đang ở trạng thái **ON** (Forced ON, badge xanh sáng).
2. **Trên HMS-SERVER Web (`http://127.0.0.1:5173/devices/870c870c-870c-870c-870c-870c870c870c`):**
   - Kênh **DO1 P21** hiển thị **OFF** (badge xám).

---

## 2. Phân Tích Căn Nguyên (Root Cause Analysis - RCA)

Quá trình điều tra đa tầng giữa firmware C/ESP-IDF, MQTT Broker, Telemetry Bridge, Backend .NET API, và Vue Frontend đã phát hiện 3 nguyên nhân cốt lõi:

### 2.1. Căn nguyên tại luồng truyền thông Firmware định kỳ (`HMS-IDF`)
- Trong `HMS-IDF/main/ops_status.c` (`ops_status_dump()`), bản tin telemetry định kỳ được publish tới MQTT topic `hms_toan_lab/gateway_870c/status` chỉ serialize các chỉ số hệ thống:
  - `uptime_seconds`, `free_heap`, `psram`, `network`, `services`, `tag_count`.
- **Thiết kế gốc của firmware không đóng gói mảng `digital_outputs` và `digital_inputs` vào MQTT status topic này.** Trạng thái DO chỉ được trả về qua Web API nội bộ của RTU (`http://maitech-rtu.local/api/do/status`) hoặc khi nhận lệnh C2 riêng biệt.

### 2.2. Căn nguyên tại Telemetry Store của Backend (`Hms.Server.Application`)
- Khi telemetry bridge chuyển tiếp gói tin MQTT `status` thô (chu kỳ 3s) đến API endpoint `POST /api/v1/devices/{deviceId}/telemetry`, lớp `InMemoryDeviceTelemetryStore.cs` thực hiện gán đè:
  ```csharp
  // Cũ: Thay thế toàn bộ Data bằng payload mới
  _store[deviceId] = new DeviceTelemetryRecord(deviceId, DateTimeOffset.UtcNow, telemetry);
  ```
- Vì payload MQTT định kỳ không có key `digital_outputs`, việc gán đè toàn bộ Dictionary khiến key `digital_outputs` trước đó (nếu đã từng được đồng bộ từ Web API) bị xóa sạch hoàn toàn!

### 2.3. Căn nguyên tại Frontend Normalization (`DeviceDetailView.vue`)
- Khi backend trả về telemetry thiếu mảng `digital_outputs` (hoặc rỗng), frontend rơi vào fallback mặc định:
  ```typescript
  // Cũ: Fallback tất cả DO1 - DO6 đều có state = false
  { channel: 1, gpioPin: 21, state: false, mode: 'toggle' }, ...
  ```
  dẫn tới DO1 luôn hiển thị `OFF`.

---

## 3. Giải Pháp Triển Khai (3-Layer Defense-in-Depth)

Áp dụng phương pháp phòng vệ đa tầng mà không cần chạm vào firmware thiết bị (`HMS-IDF`):

### 3.1. Tầng 1: Telemetry Bridge Enrichment (`tools/mqtt-broker/hms-telemetry-bridge.js`)
- Bổ sung bộ nhớ đệm phần cứng `cachedHardwareState`:
  ```javascript
  const cachedHardwareState = {
    digital_outputs: [],
    digital_inputs: [],
    mqtt: null,
    hostname: 'maitech-rtu',
    lastSync: null
  };
  ```
- Giảm chu kỳ polling RTU Web API từ 4s xuống 2s để phản ánh tức thì các thao tác bật/tắt relay trên Web Local.
- Khi nhận được bản tin định kỳ từ MQTT `hms_toan_lab/gateway_870c/status`, bridge chủ động **bổ sung (enrich)** `cachedHardwareState.digital_outputs` và `digital_inputs` vào payload trước khi gửi tới Backend HMS-SERVER.
- Khởi động pre-fetch ngay khi bridge chạy để dữ liệu relay luôn sẵn sàng.

### 3.2. Tầng 2: Backend Telemetry Merging (`src/Hms.Server.Application/Realtime/DeviceTelemetryContracts.cs`)
- Cải tiến `InMemoryDeviceTelemetryStore.Record()`:
  ```csharp
  public ValueTask RecordAsync(string deviceId, IReadOnlyDictionary<string, object?> telemetry, CancellationToken cancellationToken = default)
  {
      _store.AddOrUpdate(
          deviceId,
          _ => new DeviceTelemetryRecord(deviceId, DateTimeOffset.UtcNow, telemetry),
          (_, existing) =>
          {
              var merged = new Dictionary<string, object?>(existing.Data, StringComparer.OrdinalIgnoreCase);
              foreach (var kvp in telemetry)
              {
                  merged[kvp.Key] = kvp.Value;
              }
              // Bảo vệ các trường phần cứng quan trọng không bị xoá mất
              return new DeviceTelemetryRecord(deviceId, DateTimeOffset.UtcNow, merged);
          });
      return ValueTask.CompletedTask;
  }
  ```
- Nhờ cơ chế merge này, các bản tin telemetry định kỳ không chứa `digital_outputs` sẽ không bao giờ làm mất trạng thái relay hiện hành.

### 3.3. Tầng 3: Frontend Data Normalization (`DeviceDetailView.vue`)
- Chuẩn hóa kiểu dữ liệu boolean cho relay DO (`ch.state === true || ch.state === 1 || String(ch.state).toLowerCase() === 'true'`).
- Không ghi đè mảng nếu payload trả về mảng rỗng.
- Gắn thẻ data-test `:data-test="'do-' + ch.channel + '-state'"` phục vụ kiểm thử tự động e2e / CDP.

---

## 4. Kết Quả Kiểm Thử & Đo Lường

### 4.1. Unit & Integration Tests (Backend & Frontend)
- **Backend Tests:** Đã bổ sung integration test `TelemetryIngestion_PreservesDigitalOutputs_WhenPartialMqttStatusIngested` trong `tests/Hms.Server.IntegrationTests/MaitechRtuDeviceIntegrationTests.cs`.
  - Kết quả: **141/141 tests PASSED** (42 Integration Tests + 99 Unit Tests).
- **Frontend Tests:**
  - `npm test`: **135/135 tests PASSED**.
  - `vue-tsc --noEmit`: **0 lỗi** typecheck.

### 4.2. Kiểm chứng Trực quan trên Trình Duyệt Thực tế (Chrome Remote Debugging)
- Kết nối CDP trực tiếp vào Chrome tab `http://127.0.0.1:5173/devices/870c870c-870c-870c-870c-870c870c870c`:
  - **Kênh DO1 (Pin 21):** Hiển thị nhãn **ON**, màu xanh ngọc (`bg-emerald-500/10 text-emerald-600 dark:text-emerald-400`).
  - **Kênh DO2 - DO6:** Hiển thị nhãn **OFF** (chuẩn xác với cấu hình trên phần cứng).
  - **Ảnh chụp bằng chứng:** `maitech_rtu_do1_on_verified.png` đã được lưu và kiểm tra thành công.
  - Đồng bộ hoàn hảo 1:1 với Local Web RTU (`http://maitech-rtu.local/ioControl/digitalOutput`).

---

## 5. Danh Sách Tệp Tác Động
1. `tools/mqtt-broker/hms-telemetry-bridge.js` (Hardware state cache & enrichment).
2. `src/Hms.Server.Application/Realtime/DeviceTelemetryContracts.cs` (Dictionary merging in `InMemoryDeviceTelemetryStore`).
3. `frontend/src/features/devices/views/DeviceDetailView.vue` (Robust boolean normalization & test attributes).
4. `tests/Hms.Server.IntegrationTests/MaitechRtuDeviceIntegrationTests.cs` (Integration test for telemetry merging).
5. `tools/login-and-verify-do1.js` (Automated CDP verification & snapshot script).
