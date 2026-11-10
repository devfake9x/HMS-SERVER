# Panel DO nói đúng kênh nào ghi được (2026-11-10)

Trạng thái: **đã duyệt** (user, 2026-11-10). Bước tiếp: `/ck:plan --tdd`.

Nguồn:
- Spec `HMS-IDF/engineering-docs/api/mqtt-api.md` @ `c80797d` (2026-11-10, "document level rules, output ownership and the rule permissions"). Chỉ đọc qua `git show`, không đọc source firmware.
- Server: nhánh `claude/mqtt-device-connection-36f705` @ `e8f8d20` (đã push lên `origin/feat/mqtt-device-link`).

## 1. Vấn đề

Panel DO cho bấm Bật/Tắt/Xung trên mọi kênh. Trên gateway thật, theo evidence HIL trong spec, **chỉ DO1 ghi được**:

| Kênh | Trạng thái (spec L1054, HIL 2026-11-06 + LM0 2026-11-10) | Thiết bị trả |
|---|---|---|
| DO1 | `owner:none` | ghi được |
| DO2, DO3 | `gpioOk:false` | `gpio_unavailable` |
| DO4, DO5 | `forced:true`, `owner:force` | `channel_forced` |
| DO6 | `owner:level_rule`, `owner_id` = luật | `owned_by_rule` (+`rule_id`) |

Gốc rễ trong code:
- `OutputChannelState(int Channel, bool State, string? Mode, bool? PulseActive)` (`DeviceOutputState.cs:13`) bỏ `enabled`, `gpioOk`, `forced`, `owner`, `owner_id`.
- Nút trong `DeviceOutputsPanel.vue:343-367` chỉ khoá theo `channelBusy`.
- Lỗi hiện thô: `DEVICE_REJECTED: ESP_ERR_INVALID_STATE: owned_by_rule` (`DeviceLinkHandlers.ToResult` ghép `error` + `data.error`).
- Hộp reboot và preflight chỉ biết `forced`; luật DO được nói chung chung ("luật DO có thể BẬT relay"), và gateway run bắt user tự xác nhận không có luật.

## 2. Spec liên quan (HEAD `c80797d`)

- `control.get_status` (L797): channel `{channel,state,mode,enabled,gpioOk,forced,owner,owner_id,…}`. `owner` ∈ `none|force|level_rule|event_rule|trigger|unknown`; `owner_id` = id luật khi `level_rule`, không thì `null`.
- Lý do từ chối DO (L820-834): `channel_forced`, `channel_disabled`, `gpio_unavailable`, `owned_by_rule` (+`channel`, `rule_id`) đều "relay không đổi", không thử lại. `owned_by_rule` áp cho `set_output`, `set_all`, `set_config`; **không nói** về `pulse`.
- DO khi khởi động (L866-885): kênh không force về OFF; kênh force lấy mức force; luật SET_HIGH/SET_LOW bắn ở lần đánh giá đầu; luật theo mức đặt lại mức trong ≤1 s sau `force_clear` (L853).

## 3. Các hướng đã cân nhắc

| Hướng | Ưu | Nhược | Kết luận |
|---|---|---|---|
| **A. Khoá nút + lý do** (chọn) | Nút phản ánh đúng thiết bị; không gửi lệnh chắc chắn bị từ chối | Khoá sai nếu lần đọc cũ; cần xử lý field thiếu | Chọn, với: chỉ khoá theo field thiết bị thật gửi, đọc lại sau lỗi trạng thái |
| B. Không khoá, chỉ cảnh báo | Không bao giờ khoá sai | Vẫn cho bấm nút chắc chắn bị từ chối | Loại |
| C. Chỉ panel, bỏ reboot/tool | Nhỏ hơn | Preflight vẫn bắt user xác nhận luật DO bằng tay | Loại |

## 4. Thiết kế đã duyệt

### Server
- `OutputChannelState` thêm `Enabled bool?`, `GpioOk bool?`, `Forced bool?`, `Owner string?`, `OwnerId string?`.
- Thiếu field → `null`; không suy diễn. `owner` ngoài 6 giá trị đã biết → `unknown`.
- API outputs chỉ thêm field (client cũ không vỡ). Hash catalog không đổi: đây là shape reply, không phải row §11.

### Panel DO (`DeviceOutputsPanel.vue`)
- Khoá Bật/Tắt/Xung kèm một lý do (thứ tự ưu tiên):
  1. `gpioOk:false` → "GPIO không dùng được"
  2. `enabled:false` → "Kênh tắt trong cấu hình"
  3. `forced:true` hoặc `owner:force` → "Đang force"
  4. `owner:level_rule` → "Luật #`owner_id` giữ kênh"
- `event_rule`, `trigger`: không khoá; ghi chú "luật/trigger có thể đổi kênh sau lệnh tay".
- `owner` thiếu (firmware cũ) hoặc `unknown`: không khoá theo owner; thiết bị quyết.
- **Xung cũng khoá trên kênh `level_rule`** (quyết định có chủ đích; spec không nói về `pulse`, và luật đặt lại mức ≤1 s). Ghi thành câu hỏi spec.
- Dịch lỗi `owned_by_rule`, `channel_forced`, `channel_disabled`, `gpio_unavailable`; mã gốc vẫn hiện (mono). Sau 4 lỗi này: đọc lại `control.get_status` một lần để khoá hiện ra. Không thử lại lệnh ghi (giữ regex retry hiện có: chỉ `LANE_FULL|busy|UNAUTHORIZED`).

### Reboot
- `rebootPlan.ts` / `RebootConfirmBody.vue`: liệt kê kênh `level_rule` theo tên + id luật: "sau boot về OFF, rồi luật đặt lại mức (có thể BẬT)". Thiếu `owner` → giữ câu cảnh báo chung hiện tại.
- `RebootResult.vue`: relay BẬT sau boot trên kênh `level_rule` ghi "do luật #id", không tính lỗi.

### Tool
- `tools/mqtt-broker/spec-device-simulator.js`: `owner`/`owner_id` trong `get_status`; lỗi `owned_by_rule`, `channel_disabled`, `gpio_unavailable`; cờ dựng bố cục giống gateway (DO2/3 không GPIO, DO4/5 force, DO6 luật theo mức).
- `tools/device-reboot-check.js --preflight`: đọc `owner`; kênh `level_rule`/`event_rule`/`trigger` → FAIL kèm danh sách, thay bước user xác nhận "không có luật DO". Firmware không gửi `owner` → giữ bước hỏi user.

### Docs
- `docs/architecture-mqtt-device-link.md` mục DO: field owner, thứ tự khoá, mã lỗi, spec ref `c80797d`.
- `docs/architecture-device-mqtt-api-catalog.md`: ghi spec HEAD mới, catalog vẫn ghim `83e93d7`.

## 5. Ngoài phạm vi

- `control.force_set`/`force_clear`, `set_all`, `get_config`/`set_config` (chưa có trong catalog ghim; đồng bộ catalog còn chặn bởi mục 8 spec: §11 vẫn ghi "98 cặp… 100 cặp" trong khi bảng 108 row).
- Sửa/xoá luật (HTTP `/api/do-rule/remove` của thiết bị, không qua MQTT).
- Chạy trên gateway thật (bàn thử đang chạy HIL). Khi rảnh: chỉ một lần đọc `control.get_status` để biết 1.0.108 có gửi `owner` không.

## 6. Nghiệm thu

- Server: reply có đủ field → projection có đủ; thiếu field → `null`; `owner` lạ → `unknown`; field cũ không đổi.
- Simulator bố cục gateway: chỉ DO1 bấm được; DO2/3 "GPIO không dùng được", DO4/5 "Đang force", DO6 "Luật #id giữ kênh".
- Simulator không gửi `owner`: không kênh nào khoá theo owner.
- `owned_by_rule` hiện tiếng Việt + mã gốc; không thử lại; panel tự đọc lại một lần.
- Hộp reboot nêu DO6 kèm id luật; preflight FAIL khi có kênh do luật giữ.
- .NET unit + integration, vitest xanh, số test không giảm (mốc: unit 640, integration 126, vitest 475); `vue-tsc` sạch.

## 7. Rủi ro

| Rủi ro | Giảm thiểu |
|---|---|
| Firmware 1.0.108 không gửi `owner` | Field optional; không khoá theo owner khi thiếu; thiết bị vẫn từ chối và UI dịch lỗi |
| Lần đọc cũ → khoá sai/mở sai | Đọc lại sau lỗi trạng thái; giữ logic stale hiện có (R4) |
| `owner_id` kiểu số hay chuỗi | Server parse về chuỗi (`ToString`) |
| Khoá Xung khi thiết bị thật cho phép | Câu hỏi spec 17; đổi một điều kiện khi có trả lời |

## 8. Câu hỏi spec thêm (chưa gửi; nối vào danh sách `brainstorm-261108-1700-mqtt-spec-coverage-and-requests.md`)

17. `control.pulse` trên kênh có luật theo mức: bị từ chối `owned_by_rule` không?
18. `owner`/`owner_id` có từ firmware nào; 1.0.108 có gửi không?
19. Kiểu của `owner_id` (số hay chuỗi)?

## 9. Bước tiếp

1. `/ck:plan --tdd` với báo cáo này: 3 phase (server → UI + reboot → simulator, tool, docs).
2. PR `feat/mqtt-device-link` → `develop`: chờ user đăng nhập GitHub để mở (đã push).
