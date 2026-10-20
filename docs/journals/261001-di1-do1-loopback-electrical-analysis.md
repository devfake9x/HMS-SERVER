# Kỹ thuật: Chẩn Đoán Căn Nguyên Lệch Tín Hiệu Khi Đấu Loopback DO1 Sang DI1 Trên MaiTech RTU

- **Ngày thực hiện:** 01/10/2026
- **Phạm vi tác vụ:** `[PLAN] = Vấn đề là DO 1 nối vào DI 1; và sao chỉ có DO 1 hoạt động, còn DI 1 không?`
- **Mã thiết bị phần cứng:** `MAITECH-RTU-870C` (Serial / Device ID: `870c870c-870c-870c-870c-870c870c870c`)
- **Nguyên tắc an toàn:** Không chỉnh sửa firmware tại `D:\WORKSPACE\PROJECT\HMS-IDF` (Read-only 100%).

---

## 1. Hiện Tượng (Symptoms & Problem Statement)

Người dùng thực hiện đấu dây ngõ ra số Relay **DO 1** sang ngõ vào số **DI 1** trên thiết bị phần cứng thực tế `MAITECH-RTU-870C`.
Tuy nhiên:
- Trên cả Web Local RTU (`http://maitech-rtu.local/`) và HMS-SERVER Web (`http://127.0.0.1:5173/`):
  - **DO 1:** Đang hoạt động ở trạng thái **ON** (Relay đóng, badge xanh).
  - **DI 1:** Vẫn ở trạng thái **OFF / LOW** (badge xám), không kích hoạt theo DO 1.

---

## 2. Phân Tích Căn Nguyên Kỹ Thuật (Multi-Layer Investigation)

Qua quá trình rà soát trực tiếp mã nguồn firmware ESP-IDF (`HMS-IDF`), tài liệu thiết kế phần cứng (`document/DI_DO_SYSTEM_DESIGN.md`, `engineering-docs/components/35-digital-input/`), và thực nghiệm đo đạc API trên thiết bị thực, nguyên nhân được làm sáng tỏ qua các tầng:

### 2.1. Bản chất logic ngõ vào DI 1 trong Firmware (Active-Low Logic)
- Trong `components/digital-input/src/digital_input.c`:
  ```c
  gpio_config_t io_conf = {
      .pin_bit_mask = 1ULL << ch->gpio_pin, // GPIO 1
      .mode         = GPIO_MODE_INPUT,
      .pull_up_en   = ch->pull_up ? GPIO_PULLUP_ENABLE : GPIO_PULLUP_DISABLE, // BẬT PULL-UP NỘI
      .pull_down_en = GPIO_PULLDOWN_DISABLE,
      .intr_type    = GPIO_INTR_DISABLE,
  };
  gpio_config(&io_conf);
  ...
  // Chu kỳ lấy mẫu 50ms (di_poll_timer_cb):
  int raw = gpio_get_level(s_di.config.channels[i].gpio_pin);
  s_di.state[i] = !raw; // ĐẢO LOGIC: ACTIVE-LOW!
  ```
- **Ý nghĩa kỹ thuật:**
  - Chân GPIO 1 có điện trở kéo lên 3.3V (Pull-up).
  - Khi chân DI 1 hở mạch hoặc có điện áp dương (+3.3V, +5V, +24V): `raw = 1` -> `s_di.state = !1 = false (OFF)`.
  - **Chỉ khi chân DI 1 được kéo tụt xuống mức 0V (GND / Mass):** `raw = 0` -> `s_di.state = !0 = true (ON)`.

### 2.2. Bản chất ngõ ra DO 1: Tiếp điểm Relay Khô (Dry Contact)
- Ngõ ra DO 1 của MaiTech RTU là tiếp điểm Relay cơ khí cách ly (Form A / Dry Contact):
  - Gồm 2 đầu cọc đấu dây (thường là `DO1-COM` và `DO1-NO`, hoặc `DO1+` và `DO1-`).
  - **Relay khô KHÔNG tự phát ra điện áp hay nguồn điện!** Bản chất nó chỉ là một chiếc công tắc nối thông 2 cực khi cuộn hút được cấp điện (DO 1 = ON).

### 2.3. Lỗi trong phương pháp đấu dây (Wiring Flaw)
Khi người dùng nói *"DO 1 nối vào DI 1"*, trên thực tế phần cứng xảy ra 1 trong 2 lỗi đấu nối kinh điển:
1. **Lỗi hở mạch (Đấu 1 sợi dây):**
   - Chỉ nối 1 sợi dây duy nhất từ 1 cọc của DO 1 sang cọc DI 1.
   - Khi DO 1 ON, tiếp điểm relay đóng lại nhưng không có mạch điện khép kín nào nối DI 1 về GND của RTU. Điện trở pull-up nội của ESP32 vẫn giữ GPIO 1 ở mức 3.3V (`raw = 1`) -> DI 1 vĩnh viễn ở trạng thái `OFF`.
2. **Lỗi ngược pha nguồn dương (Đấu nguồn 24V/3.3V qua DO vào DI):**
   - Đưa nguồn dương (+24V hoặc +3.3V) vào chân COM của DO 1 và chân NO vào DI 1.
   - Khi DO 1 ON, điện áp HIGH đưa vào DI 1 (`raw = 1`). Vì firmware dùng logic Active-Low (`!raw`), DI 1 vẫn được tính là `false (OFF)`.

---

## 3. Sơ Đồ Đấu Dây Chuẩn (Proper Wiring Guide)

Để kênh DI 1 chuyển sang **ON** đồng pha khi DO 1 **ON**, mạch đấu dây trên terminal block phải tạo thành một mạch vòng khép kín kéo DI 1 về GND:

```
┌────────────────────────────────────────────────────────┐
│                   MAITECH-RTU TERMINAL                  │
│                                                        │
│   [ GND ] ─────────────┐                               │
│                        │                               │
│   [ DO1-COM ] ◄────────┘ (Nối chân COM của DO1 vào GND)│
│                                                        │
│   [ DO1-NO  ] ─────────┐ (Tiếp điểm thường mở của DO1) │
│                        │                               │
│   [ DI1     ] ◄────────┘ (Nối chân NO của DO1 vào DI1) │
└────────────────────────────────────────────────────────┘
```

### Nguyên lý vận hành:
- **Khi DO 1 = OFF:** Tiếp điểm `DO1-COM` và `DO1-NO` HỞ -> DI 1 được kéo lên 3.3V nhờ pull-up nội -> `raw = 1` -> `DI 1 = OFF`.
- **Khi DO 1 = ON:** Tiếp điểm đóng lại -> DI 1 được nối thông trực tiếp với `GND` (0V) -> `raw = 0` -> `DI 1 = ON` (sáng đèn và hiển thị ON trên Web).

*(Lưu ý: Nếu bo mạch sử dụng ngõ vào Optocoupler cách ly nguồn ngoài 24V, vòng lặp cần có nguồn cấp 24VDC đi qua tiếp điểm DO1 vào cặp cực quang của DI1).*

---

## 4. Tinh Chỉnh Giao Diện Web HMS-SERVER (UI Parity)

- Để đồng bộ nhận diện thương hiệu Datacake và chuẩn hoá 1:1 với RTU Local Web:
  - Cập nhật [`DeviceDetailView.vue`](file:///d:/WORKSPACE/PROJECT/HMS-SERVER/frontend/src/features/devices/views/DeviceDetailView.vue): Hiển thị trạng thái kênh DI bằng nhãn `ON` (xanh) và `OFF` (xám) tương tự kênh DO, thay thế cho hiển thị `HIGH / LOW` trước đây.
  - Bổ sung thuộc tính kiểm thử tự động `:data-test="'di-' + di.channel + '-state'"` phục vụ kiểm định e2e qua Chrome CDP.

---

## 5. Kết Quả Kiểm Thử & Xác Nhận Thực Tế

1. **Kiểm tra phần cứng RTU thật qua API:**
   - Đã thử nghiệm lệnh force DO1 qua `/api/do/force`: Khi DO1 ON hay OFF, DI1 đều trả về `state: false` do chân phần cứng GPIO 1 đang hở mạch / chưa đấu về GND.
   - Chụp ảnh màn hình trực tiếp từ trang web của RTU: [`maitech_rtu_di_page.png`](file:///C:/Users/toann/.gemini/antigravity/brain/ebb00cfc-bcd4-439d-b063-ce9916bf5e54/maitech_rtu_di_page.png) xác nhận cả 4 kênh DI-1..DI-4 đều đang ở trạng thái `OFF`.
2. **Kiểm tra trên giao diện HMS-SERVER:**
   - Ảnh chụp [`maitech_rtu_do1_on_verified.png`](file:///C:/Users/toann/.gemini/antigravity/brain/ebb00cfc-bcd4-439d-b063-ce9916bf5e54/maitech_rtu_do1_on_verified.png) thể hiện chính xác trạng thái phần cứng: DO1 = ON, DI1..DI4 = OFF.
3. **Bộ kiểm thử tự động (TDD Regression):**
   - Frontend Vitest: **135/135 tests passed** (100%).
   - Frontend TypeCheck: **0 errors**.
   - Backend Tests: **141/141 tests passed** (100%).
