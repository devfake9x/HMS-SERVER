---
phase: 4
title: "Kiểm chứng live và cập nhật tài liệu"
status: completed
priority: P1
dependencies: [1, 2, 3]
---

# Phase 4: Live verification + docs

## Overview

Chứng minh lát DO chạy thật trên hai môi trường: simulator theo spec cho cả 6 kênh, và gateway thật với **chỉ DO1**. Kiểm cả các lớp an toàn: danh sách kênh, lệnh vật lý không có reply, restart giữa chừng. Ghi kết quả vào tài liệu.

## Requirements

### Simulator (`hms_server_selftest/sim_001`, broker lab)
- Copy `sim-device.js` từ scratchpad vào `tools/mqtt-broker/spec-device-simulator.js` [RT#15]. Tham số đọc từ env; không có key hay credential trong file.
- Simulator xử lý `control.set_output`, `control.pulse`, `control.get_status` với 6 kênh; DO6 ở mode `pulse`.
- `pulse` vào kênh mode `toggle` → `success:false,error:"ESP_ERR_INVALID_STATE",data:{error:"..."}`. Thông điệp lỗi là do simulator chọn; không bịa thêm mã lỗi.
- Có chế độ "im lặng" để thử timeout.

### Gateway thật (`gateway_870c`, `AllowedOutputChannels=[1]`)
1. Panel → **Đọc trạng thái**.
2. Panel: DO1 ON → `Executed` → đọc lại ON.
3. Panel: DO1 OFF → đọc lại OFF.
4. **Script API:** `set_output` DO2 → `DO_CHANNEL_NOT_ALLOWED`. Sniffer trên `hms_toan_lab/gateway_870c/cmd/#` không thấy lệnh nào.
5. **Script API:** `pulse` DO1 khi kênh đang `toggle` → `DEVICE_REJECTED` có lý do. Sau đó panel đọc lại.
6. **Script API:** gỡ danh sách kênh (đặt `[]` bằng master-admin) → `set_output` DO1 → `DO_CHANNEL_NOT_ALLOWED`. Sau đó đặt lại `[1]`.

### Simulator (an toàn vòng đời)
7. Simulator ở chế độ im lặng → `set_output` → sau 30 s ra `DEVICE_RESPONSE_TIMEOUT`. Panel vẫn khoá và tự đọc lại. Trạng thái DO chuyển stale.
8. Gửi lệnh rồi restart API trong lúc lệnh đang `Published` → sau khi khởi động, audit ghi `OUTCOME_UNKNOWN_AFTER_RESTART`.

### UI
Có screenshot cho các bước 1–3 và 7.

### An toàn
- **Trước khi đụng relay thật:** báo team firmware rằng server sẽ điều khiển DO1.
- **Sau khi thử:** trả DO1 về OFF và đọc lại để xác nhận.

## Related Code Files

- **Create:** `tools/mqtt-broker/spec-device-simulator.js`
- **Modify:**
  - `docs/architecture-mqtt-device-link.md`: mục DO control (action, danh sách kênh mặc định đóng, TTL vật lý, store trạng thái và quyền, audit, rủi ro còn lại về replay/broker anonymous, các spec gap)
  - `plans/261106-0735-do-relay-control/plan.md` (status)

## Tests first

Phase này là kiểm chứng live, không có test tự động mới. Test của phase 1–3 phải xanh trước khi bắt đầu.

## Implementation Steps

1. Chạy `hms-api` và `hms-frontend`. Gateway bind với `[1]` qua seed.
2. Đưa simulator vào repo. Chạy simulator, bind một thiết bị seed, chạy các bước 7–8 cùng kịch bản 6 kênh.
3. Chạy các bước 1–6 trên gateway thật, có sniffer đi kèm bước 4.
4. Chụp screenshot.
5. Trả DO1 về OFF, đọc lại.
6. Cập nhật tài liệu và status của plan. Commit cục bộ (không push nếu người dùng chưa yêu cầu).

## Success Criteria

- [x] Bảng kết quả các bước 1–8 được ghi trong tài liệu.
- [x] DO1 kết thúc ở OFF, đã đọc lại.
- [x] Không có lệnh nào tới DO2–DO6 của gateway thật (sniffer xác nhận).

## Risk Assessment

- **Tool HIL của firmware điều khiển DO1 cùng lúc:** báo trước. Kết quả đọc lại quyết định trạng thái thật.
- **Mất kết nối giữa chừng:** lệnh hết giờ ra `DEVICE_RESPONSE_TIMEOUT`, panel tự đọc lại (spec R4).

## As built (2026-11-06)

Results: [reports/live-verification.md](reports/live-verification.md); screenshots in `reports/screens/`.

- Steps 1–7 done live (gateway: DO1 only; simulator: 6 channels). DO1 ended OFF, read back; the gateway allowlist was closed again (`[]`).
- Step 8 (restart mid-command) is covered by unit and EF tests only: the dev server runs on the in-memory DB, which a restart wipes.
- The gateway answers a pulse on a toggle channel with `ESP_ERR_INVALID_ARG` (spec names no code); the simulator follows the device.
- The simulator publishes a `<base>/status` heartbeat every 5 s; without it the server rightly shows the device offline and its relay state as unknown.
