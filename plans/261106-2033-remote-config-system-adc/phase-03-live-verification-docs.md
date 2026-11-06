---
phase: 3
title: "Simulator, kiểm live và tài liệu"
status: completed
priority: P1
dependencies: [1, 2]
---

# Phase 3: Simulator, kiểm live và tài liệu

## Overview

Theo thứ tự:
1. Cho simulator trả lời đủ các lệnh mà luồng sửa cần, để kiểm UI và tool, gồm cả đường job, mà không đụng thiết bị thật.
2. Viết tool trong repo chạy kịch bản: lưu giá trị gốc → ghi → đọc lại → đặt lại → đọc lại. Gặp bất thường, tool đọc lại trước: trường đang ở giá trị thử thì tự đặt về giá trị gốc; trường lệch cả hai thì dừng và in lệnh `--restore`.
3. Chạy trên `hms_toan_lab/gateway_870c` sau khi user báo team firmware.
4. Liệt kê các role bị ảnh hưởng và viết tài liệu.

## Requirements

### Simulator (`tools/mqtt-broker/spec-device-simulator.js`)
- **Hiện chỉ trả `control.*`, `ping`, `get_info`** (`spec-device-simulator.js:67-99`). Thêm handler theo shape §6.1/§6.2, state trong bộ nhớ:
  - **Thẻ "Thiết bị & thời gian":**
    - `system.get_hostname`, `system.set_hostname` (trả `{hostname, applied_immediately: true, reboot_recommended: false}`);
    - `system.get_time` (`epoch`, `datetime`, `timezone`, `ntp_synced`, `ntp_servers`, `ntp_mode`), `system.set_timezone`, `system.sync_ntp`;
    - `system.get_device_id`, `ota.get_status`.
  - **Thẻ "Nguồn & ADC":**
    - `battery.get_status`, `battery.get_alerts`;
    - `adc.get_config`, `adc.get_channels`, `adc.get_values`, `adc.set_channel`, `adc.toggle_channel`.
  - **Khác:** `system.get_operations`, `mqtt.get_config` (có `client_id`).
- **Biến env để mô phỏng tình huống lỗi:**
  - `SIM_ADC_JOB=1`: `adc.set_channel` trả reply job còn chạy; job kết thúc sau 10 s, khi đó `get_operations` trả `terminal`.
  - `SIM_HOSTNAME_DEFERRED=1`: `set_hostname` trả `applied_immediately:false`; `get_hostname` vẫn trả giá trị cũ.
  - `SIM_ADC_DROP_FIELDS=1`: `set_channel` mà thiếu `type` thì xoá `type` (để kiểm cảnh báo lệch).
  - `SIM_CLOCK_SKEW_S=<n>`: `epoch` lệch n giây (để kiểm cảnh báo đồng hồ).

### Tool kiểm live (`tools/device-config-check.js`)
- **Credential:** dùng `tools/tool-env.js` (`HMS_ADMIN_EMAIL`/`HMS_ADMIN_PASSWORD`); `HMS_API_BASE` đọc trong tool như `device-read-check.js:22`. Tool không in và không ghi credential. Server ký lệnh, tool không cần khoá lệnh.
- **Chặn chạy nhầm:**
  - bắt buộc `--device <guid>` và `--confirm-live`;
  - `--plan` chỉ in kịch bản, không gửi gì.
- **`--restore <original.json>`:** đọc trạng thái hiện tại, so với file gốc, ghi lại từng trường khác giá trị gốc (mỗi lần ghi đều đọc lại).
- **Phân loại kết quả:** dùng cùng quy tắc với UI.
  - "Chưa gửi" chỉ khi `ContractUnavailable`, `TransportNotConfigured`, mã NeverSent hoặc HTTP 4xx. Không chép `device-read-check.js:55`, vì file đó coi mọi dispatch không phải `Published` là chưa gửi.
  - `Acknowledged` + `operation_id`: poll `system.get_operations` tới `terminal` (tối đa 60 s).
  - Thành công khi `Executed`, hoặc job `terminal` + `resultSuccess:true`.
- **Nhịp gửi:** cách nhau ≥ 7 s giữa hai lệnh ghi, để không chạm giới hạn 10 lệnh ghi/phút.
- **Kịch bản, mỗi bước chờ kết quả rồi mới sang bước sau:**
  1. **Đọc gốc:** `get_hostname`, `get_time`, `get_channels`, `mqtt.get_config`. Lưu vào `tools/device-reads/<ts>-config-original.json` trước mọi lệnh ghi.
  2. **Kiểm trước bước hostname:** nếu `mqtt.get_config.client_id` rỗng (thiết bị có thể lấy client id từ hostname), bỏ bước 3 và ghi lý do.
  3. **Hostname:**
     - `<gốc>` → `<gốc>-t` (cắt để ≤ 63 byte) → đọc lại → về `<gốc>` → đọc lại;
     - sau mỗi lần đổi, theo dõi heartbeat/connectivity 15 s;
     - `applied_immediately:false`: ghi nhận và vẫn đặt lại.
  4. **Múi giờ:** `<gốc>` → `UTC0` → đọc lại → về `<gốc>` → đọc lại. Bỏ bước này nếu giá trị gốc không nằm trong `SupportedTimezones`.
  5. **`sync_ntp`:** đọc lại; kỳ vọng `ntp_synced:true` và `epoch` lệch giờ máy chạy tool ≤ 30 s.
  6. **ADC** (chỉ khi `available` và có kênh `ai` hợp lệ đầu tiên):
     - `set_channel {ai, name: <gốc>-t, type: <gốc>, enabled: <gốc>}` → đọc lại, so cả bản ghi kênh → về tên gốc → đọc lại;
     - `toggle_channel` đảo trạng thái → đọc lại → về trạng thái gốc → đọc lại.
- **Khi một bước `unknown` hoặc đọc lại lệch:**
  - không dừng ngay: đọc lại trạng thái trước (spec R4);
  - trường đó đang ở giá trị gốc → không làm gì;
  - đang ở giá trị thử → ghi lại giá trị gốc (lệnh mới, `id` mới), rồi đọc lại;
  - không phải cả hai → dừng, in lệnh `--restore` kèm đường dẫn file gốc.
  - Sau đó dừng kịch bản và chạy kiểm trạng thái cuối.
- **Trạng thái cuối:** chỉ so trường cấu hình: `hostname`, `timezone`, `ntp_mode`/`ntp_servers`, và `name`/`type`/`enabled` của kênh ADC. Không so `epoch`, `datetime`, `last_sync` và các trường đổi theo thời gian.
- **Output:** report JSON đầy đủ vào `tools/device-reads/` (đã gitignore).

### Tiền điều kiện live
- **Báo team firmware:** user báo rằng sẽ ghi hostname, múi giờ, `sync_ntp`, tên và trạng thái kênh ADC, rồi đặt lại. Kèm hai câu hỏi:
  - `set_channel` khi không gửi `type`/`enabled` thì giữ hay xoá giá trị cũ;
  - đổi hostname có ảnh hưởng MQTT session hay không.
- **Khoá lệnh:** `system.get_info` trả `Executed`. Nếu hết giờ thì nghi khoá trước tiên.
- **Sniffer:** bật trên `hms_toan_lab/gateway_870c/cmd/#` suốt phiên.

### Role bị ảnh hưởng [RT F5]
- **Truy vấn DB dev** (qua API roles hoặc SQL chỉ đọc): liệt kê role và tổ chức đang giữ `sys.hostname_edit`, `sys.time_edit`, `adc.config_edit`, `adc.enable`, cùng số membership của mỗi role.
- **Ghi vào `reports/affected-roles.md`:** tên role và số lượng, không ghi email người dùng.

### Kiểm live
- **Simulator trước:** chạy tool `--plan` với simulator; chạy thật với simulator ở các chế độ env trên; kiểm UI ở cả 4 chế độ.
- **Gateway:**
  - chạy tool; ghi bảng: lệnh, status, giá trị trước/sau (thay bằng chuỗi giả trong tài liệu), đọc lại khớp hay không, thời gian;
  - qua UI, đổi tên kênh ADC rồi đổi lại; chụp màn hình (chỉ giữ ảnh thẻ ADC).
- **Sniffer:** đếm lệnh ghi khớp số bước kịch bản; không có lệnh ghi nào ngoài 5 lệnh.
- **Trạng thái cuối:** trùng giá trị gốc ở mọi trường cấu hình.
- **Không kiểm `DEVICE_WRITE_BUSY` trên gateway:** giới hạn này từ chối ngay ở server, test đơn vị đã phủ.

### Tài liệu
- **`docs/architecture-mqtt-device-link.md`**, mục mới "Cấu hình từ xa — Hệ thống & ADC":
  - 5 lệnh, quyền, luật payload;
  - lane ghi: `DEVICE_WRITE_BUSY`, `DEVICE_WRITE_RATE_LIMITED`, mốc giữ slot, giữ lane khi job còn chạy;
  - giờ server cho lệnh ghi; audit payload;
  - quy tắc đọc lại dùng chung; poll job; cảnh báo đồng hồ;
  - các lệnh vẫn đóng và lý do NTP;
  - spec gap; rủi ro còn lại.
- **Role:** dẫn tới `reports/affected-roles.md`. Ghi rõ:
  - admin không phải master-admin gán được bốn mã này;
  - `sys.time_edit` cũng là quyền của `set_time` và `set_ntp_servers` (hai lệnh này đóng nhờ catalog).
- **`docs/architecture-device-mqtt-api-catalog.md`:** trạng thái mở của 5 lệnh.
- **Sửa dòng sai** trong `docs/architecture-mqtt-device-link.md` nói `PayloadSummary` "lưu nguyên payload": thực tế chỉ lưu cho B/D/F và 5 lệnh cấu hình.
- **Ghi chú cho team firmware** (đưa user chuyển, không tự gửi): danh sách spec gap trong `plan.md`.
- **Plan:** cập nhật `plan.md`, các phase (status, "As built"); viết journal `docs/journals/261106-remote-config-system-adc.md`.

## Tests first

Đây là phase kiểm chứng. Nếu repo có test cho simulator thì thêm test cho handler mới; còn lại là kiểm tay theo
từng chế độ env. Test của phase 1 và 2 phải vẫn xanh.

## Implementation Steps

1. Sửa simulator; kiểm UI với simulator ở 4 chế độ.
2. Viết `tools/device-config-check.js`; chạy `--plan`, chạy thật và `--restore` với simulator, gồm trường hợp ép lệch (`SIM_ADC_DROP_FIELDS=1`).
3. Liệt kê role bị ảnh hưởng.
4. Chờ user xác nhận đã báo team firmware.
5. Kiểm khoá (`system.get_info`), bật sniffer, chạy tool trên gateway.
6. Kiểm qua UI, chụp màn hình.
7. So trạng thái cuối với giá trị gốc.
8. Viết tài liệu, plan, journal. Commit cục bộ khi user đồng ý; không push.

## Related Code Files

- **Create:**
  - `tools/device-config-check.js`
  - `plans/261106-2033-remote-config-system-adc/reports/live-verification.md`
  - `reports/affected-roles.md`, `reports/screens/*`
  - `docs/journals/261106-remote-config-system-adc.md`
- **Modify:**
  - `tools/mqtt-broker/spec-device-simulator.js`
  - `docs/architecture-mqtt-device-link.md`, `docs/architecture-device-mqtt-api-catalog.md`
  - `plan.md`, các phase

## Success Criteria

- [ ] UI và tool chạy đúng với simulator ở cả 4 chế độ; `--restore` đưa simulator về giá trị gốc. **Một phần:** đã chạy thật chế độ thường và `SIM_ADC_JOB=1`; ba chế độ còn lại (hostname chưa áp dụng, mất trường ADC, lệch đồng hồ) chỉ được phủ bằng test frontend; `--restore` mới chạy trên gateway (không có trường lệch).
- [x] Trên gateway: 5/5 lệnh thành công (`Executed`, hoặc job kết thúc thành công); đọc lại khớp; đặt lại khớp giá trị gốc.
- [x] Sniffer khớp kịch bản, không có lệnh ghi lạ.
- [x] Trạng thái cấu hình cuối của gateway trùng giá trị gốc.
- [x] Tài liệu ghi đủ role bị ảnh hưởng, các lệnh vẫn đóng, spec gap và rủi ro.

## Risk Assessment

- **Đổi hostname làm rớt MQTT session:**
  - bước 2 bỏ qua hostname khi `client_id` rỗng; bước 3 theo dõi heartbeat;
  - rớt link thì chờ thiết bị kết nối lại rồi chạy `--restore`;
  - không kết nối lại được thì báo user (thiết bị lab).
- **`set_channel` xoá `type`:** UI và tool luôn gửi `type` lấy từ lần đọc, nên kể cả khi thiết bị xoá trường bỏ trống thì giá trị vẫn được ghi lại.
- **Khoá lệnh đổi giữa chừng** (đã xảy ra hai lần ngày 2026-11-06): mọi lệnh hết giờ.
  - Tool đọc lại; lệnh đọc cũng hết giờ thì dừng và in hướng dẫn `--restore`.
  - Chạy `--restore` sau khi khoá đúng lại.
- **Tắt kênh ADC làm dừng telemetry** của kênh trong vài giây; tool bật lại ngay.
- **Reply chứa thông tin định danh** (hostname): chỉ lưu trong `tools/device-reads/`; tài liệu dùng chuỗi giả.

## As built (2026-11-06)

Kết quả: [reports/live-verification.md](reports/live-verification.md).
- **Simulator:** đủ handler + 4 chế độ `SIM_*`. Tool qua chế độ thường và chế độ `SIM_ADC_JOB=1`. UI qua cả đường job.
- **Tool, sửa sau review:**
  - M-2: lệnh trả giá trị gốc bị `DEVICE_WRITE_BUSY`/`DEVICE_WRITE_RATE_LIMITED` (chắc chắn chưa gửi) thì chờ rồi gửi lại, tối đa 90 s;
  - L-3: bỏ qua trường mà giá trị gốc không qua được luật của server;
  - L-4: bước hostname bị bỏ qua trừ khi đọc chắc chắn được `client_id`;
  - L-7: đọc được reply `get_operations` lồng một cấp;
  - report lưu reply data của lệnh ghi.
- **Gateway:**
  - 5/5 `Executed`; đọc lại và đặt lại khớp; `--restore` kiểm lại không thấy trường nào lệch;
  - sniffer: 48 lệnh, 48 reply, 11 lệnh ghi, `ttl_ms` 20000;
  - UI đổi tên AI-1 rồi đổi lại; ảnh `screens/adc-rename-lab-gateway.jpg`.
- **Không kiểm `DEVICE_WRITE_BUSY` trên gateway:** giới hạn này chặn ngay ở server, test đơn vị đã phủ.
- **Role:** [reports/affected-roles.md](reports/affected-roles.md). Dev dùng DB in-memory và chưa có API tạo device role, nên không có role nào. Có sẵn câu SQL cho Postgres.
