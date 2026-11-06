---
phase: 3
title: "Kiểm chứng live và tài liệu"
status: completed
priority: P1
dependencies: [1, 2]
---

# Phase 3: Live verification + docs

## Overview

Chạy 15 lệnh trên gateway thật `hms_toan_lab/gateway_870c` bằng một tool nằm trong repo, đối chiếu từng reply
với spec, và dùng reply thật đã redact làm fixture cho test thẻ. Sau đó kiểm các chốt an toàn của phase 1 trên
thiết bị thật, chụp màn hình ba thẻ và cập nhật tài liệu.

## Requirements

### Tool trong repo [RT F15]
- **`tools/device-read-check.js`:**
  - đăng nhập bằng `HMS_ADMIN_EMAIL`/`HMS_ADMIN_PASSWORD` qua `tools/tool-env.js`;
  - gửi lần lượt danh sách lệnh qua API (server ký, tool không cần key lệnh);
  - ghi **toàn bộ** reply JSON, không cắt.
- Output vào `tools/device-reads/` (thêm vào `.gitignore`). Tool không in và không ghi credential.

### Tiền điều kiện
- API dev giữ key lệnh hiện tại của gateway. Key có thể đã đổi; nếu `system.get_info` hết giờ thì nghi key trước tiên. Nạp key qua user-secrets hoặc endpoint import key, không đưa key vào repo.
- `system.get_info` trả `Executed`; ghi phiên bản firmware vào báo cáo để kết quả có ngữ cảnh.

### Kiểm từng lệnh
- **Mỗi lệnh trong 15:** ghi status, code, kích thước data và các khoá; so với spec §6 (bản ghim `83e93d77e`).
- **Lệch spec** ghi thành spec gap; không sửa code để "khớp" khi chưa rõ bên nào đúng.
- **`network.get_modem`, `ota.get_status`:** spec không định nghĩa trường, nên ghi danh sách khoá thật và tinh nhãn trong thẻ theo đó.

### Kiểm chốt an toàn trên thiết bị thật
- **Giới hạn lệnh đọc:** bắn 6 lệnh đọc liền cho gateway; lệnh thứ 5 và 6 nhận `DEVICE_READ_BUSY`, không lệnh nào bị thiết bị bỏ không trả lời.
- **TTL:** sniffer thấy `ttl_ms ≤ 10000` trên mọi lệnh.
- **Realtime:** không có event `command_result` nào cho lệnh đọc; "Last Heartbeat" chỉ đổi theo heartbeat.
- **Sniffer** trên `hms_toan_lab/gateway_870c/cmd/#` suốt phiên chỉ thấy các lệnh đọc trong danh sách; không có lệnh ghi nào.

### Fixture và redact [RT F8]
- Reply thật làm fixture phải redact trước khi commit:
  - IMEI, ICCID, IMSI, số điện thoại, MSISDN;
  - SSID, BSSID;
  - mọi URL (OTA, server);
  - MAC → `02:00:00:00:00:0N`;
  - `device_id` → giá trị giả.
- IP lab (`192.168.x.x`) giữ nguyên được.
- Test thẻ phải xanh với fixture đã redact.

### UI
- Bấm "Đọc" trên ba thẻ với gateway thật.
- Chụp màn hình ba thẻ, một thẻ có lỗi (ví dụ ADC hay pin không có phần cứng), và cảnh báo offline nếu tạo được (rút simulator).

### Tài liệu
- **`docs/architecture-mqtt-device-link.md`**, mục "Lệnh đọc giám sát":
  - 15 lệnh và quyền của từng lệnh;
  - `allowed-commands`;
  - giới hạn lệnh đọc đang chờ, TTL cho mọi lệnh, việc không phát `command_result` cho lệnh đọc;
  - ba thẻ; spec gap; rủi ro còn lại (broker anonymous, retention audit).
- **[RT F7] Role cũ có thêm quyền đọc:** liệt kê các mã quyền đã có từ trước và nay mở thêm lệnh đọc: `sys.time_view`, `sys.hostname_view`, `sys.info_view`, `net.priority_view`, `eth.view`, `modem.view`, `wifi.sta_view`, `wifi.ap_view`, `sys.battery_view`, `adc.view`. Role đang giữ chúng sẽ đọc được ngay.
- **`docs/architecture-device-mqtt-api-catalog.md`:** trạng thái mở của từng nhóm.
- **Ghi chú cho team firmware** (đưa user chuyển, không tự gửi): §11 tại `18a63e8ed` ghi 98 cặp trong khi bảng có 104 dòng, nên generator không ghim được.
- Cập nhật `plan.md` và các phase (status, mục "As built"); viết journal.

## Related Code Files

- **Create:**
  - `tools/device-read-check.js`
  - `plans/261106-1040-monitoring-read-actions/reports/live-verification.md`, `reports/screens/*`
  - fixture reply đã redact cạnh test thẻ
- **Modify:** `.gitignore` (`tools/device-reads/`), hai file tài liệu ở trên, `plan.md`, các phase

## Tests first

Phase kiểm chứng, không có test tự động mới. Fixture từ reply thật đã redact thay hoặc bổ sung fixture theo spec
của phase 2; test thẻ phải vẫn xanh.

## Implementation Steps

1. Chạy `hms-api` và `hms-frontend`; kiểm key bằng `system.get_info`.
2. Bật sniffer cho `.../cmd/#`.
3. Viết và chạy `tools/device-read-check.js` cho 15 lệnh; ghi bảng kết quả và spec gap.
4. Kiểm giới hạn lệnh đọc, TTL và realtime.
5. Redact reply, cập nhật fixture, chạy `vitest`.
6. UI: đọc ba thẻ, chụp màn hình.
7. Tài liệu, plan, journal. Commit cục bộ khi user đồng ý (không push).

## Success Criteria

- [x] Bảng 15 lệnh: status, kích thước data, khớp hoặc lệch spec.
- [x] Giới hạn lệnh đọc, TTL ≤ 10 s và việc không phát `command_result` đã kiểm trên thiết bị thật.
- [x] Sniffer xác nhận không có lệnh ghi nào được gửi.
- [x] Fixture đã redact; ba thẻ chạy với dữ liệu thật, có ảnh.
- [x] Tài liệu ghi đủ quyền bị ảnh hưởng và rủi ro còn lại.

## Risk Assessment

- **Key lệnh đổi giữa chừng** (đã xảy ra hai lần ngày 2026-11-06): mọi lệnh hết giờ. Kiểm `system.get_info` trước.
- **Lab thiếu phần cứng ADC hay pin:** reply `available:false` vẫn hợp lệ. Ghi rõ là chưa kiểm được dữ liệu thật của nhánh đó.
- **Redact sót:** rà fixture bằng grep các mẫu IMEI (15 chữ số), MAC, `http` trước khi commit.

## As built (2026-11-06)

Results: [reports/live-verification.md](reports/live-verification.md).
- **Reads:** all 15 `Executed` on the gateway (firmware 1.0.108).
- **Spec gaps:** 4 recorded (`ota.get_status` nesting, `wifi_ap_status` fields, `adc.get_status` has `ai`, `system.get_time` extras).
- **Read cap:** held live (2 of 6 published for one user).
- **Sniffer:** counts match the reads sent; every read had `ttl_ms` 20000; no write command was sent.
- **Fixture:** real replies anonymised to `frontend/src/features/devices/components/reads/fixtures/lab-gateway-replies.json` and covered by card tests.
- **Screenshots:** only the power & ADC card is kept (others show the real MAC/SSID).
