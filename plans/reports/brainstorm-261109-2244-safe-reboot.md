# Brainstorm: reboot an toàn qua MQTT (`system.reboot`)

- Ngày: 2026-11-09. User duyệt thiết kế, chọn `/ck:plan --tdd`.
- Nhánh: `claude/mqtt-device-connection-36f705` @ `45f5c14`.
- Spec: `HMS-IDF/engineering-docs/api/mqtt-api.md`. Server ghim `83e93d7`; bản hiện tại `9bfc168`. Row `system.reboot` giống nhau ở hai bản.
- Chỉ đọc spec, không đọc source firmware.

## Vấn đề

- Người vận hành không khởi động lại được gateway từ xa khi thiết bị treo hoặc sau khi đổi cấu hình cần reboot (`set_hostname` trả `reboot_recommended`, `config.set` trả `needs_reboot`).
- OTA sau này cũng cần phần theo dõi thiết bị khởi động lại.

## Vì sao chọn reboot

| Hướng | Kết luận |
|---|---|
| Reboot an toàn | **Chọn.** Spec đủ (§6.1, §7, §11); server đã có confirm và dry-run |
| Đồng bộ catalog | Bị chặn: §11 ghi "98 cặp" nhưng bảng có 108 row, nên generator từ chối. Spec còn đổi hằng ngày |
| Gia cố broker ACL | Chờ đặc tả ACL (mục 13 trong `brainstorm-261108-1700-mqtt-spec-coverage-and-requests.md`) |

## Hợp đồng spec

- §6.1 `system.reboot`:
  - input `{delay_ms?: 0..60000}`; mặc định 2000 ms;
  - "lập lịch reboot", nên reply đến trước khi thiết bị khởi động lại.
- §11: class F, quyền `sys.reboot`, TTL 60000, confirm bắt buộc, template `REBOOT-<DEVID>`.
- §7: trường gốc `confirm` = `REBOOT-<device_id>`, với `<device_id>` là identity MQTT. Thiết bị chưa có identity thì fail closed. Có dry-run.
- §3: OTA, reboot và factory_reset dùng chung throttle 60 s. Queue 16 slot, có thể trả `LANE_FULL`.
- §2, §10:
  - heartbeat `B/status` mỗi 3000 ms, có `uptime_seconds`;
  - `B/status/connectivity` retained/LWT `{state: online|offline}`; retained có thể cũ;
  - LWT/heartbeat không thay read-back.
- §6.4 (2026-11-09, D-665): sau khi khởi động, kênh DO không force về **OFF**; kênh đang force giữ mức force.
- R4: không có reply không có nghĩa là lệnh chưa chạy. Không tự gửi lại.
- R10: `clean_session=true` mặc định, nên lệnh publish lúc thiết bị offline có thể mất.

## Server đã có (kiểm trên nhánh)

- `HmsIdfCommandProtocolAdapter.cs:47-52`: dựng `confirm` từ template với `binding.MqttDeviceId`; khác request thì `CONFIRM_MISMATCH`.
- Có `dry_run`; row không hỗ trợ thì `DRY_RUN_UNSUPPORTED`.
- `HmsIdfCommandCodec.cs`: ghi `confirm`/`dry_run` ở root.
- `DeviceLinkHandlers.cs:247`: uptime giảm thì coi là thiết bị vừa reboot (DO có thể đã reset).
- `DeviceLinkContracts.cs`: `LastHeartbeatAt`, `Connectivity`, `ConnectivityAt` (store in-memory).
- `PinnedCommandCatalog.cs`: class F không mở qua danh sách nào (`_ => false`).
- Quyền high-risk chỉ master-admin (`DeviceAccessHandlers.cs`, `HIGH_RISK_REQUIRES_MASTER_ADMIN`).
- `DeviceCommandModal.vue:139-143`: modal C2 gửi `confirm_token` trong params, dùng id server, nên **sai §7**. Server chặn được (fail closed) nhưng UI vẫn sai.

## Thiết kế đã duyệt

### Server

1. **Mở đúng một row class F:** `system.reboot`, qua danh sách riêng. `factory_reset` và `ota.*` vẫn đóng, có test khẳng định.
2. **Policy payload trước khi ký:** chỉ `delay_ms` tuỳ chọn, số nguyên 0..60000. Trường lạ hoặc giá trị sai thì 400; không gửi gì.
3. **Đi qua lane ghi hiện có** (1 lệnh ghi đang chờ mỗi thiết bị):
   - Đang theo dõi một lần reboot thì lệnh reboot thứ hai bị từ chối, không gửi.
   - Không bao giờ tự gửi lại.
4. **Theo dõi phía server** để audit có kết quả cuối:
   - Lần gửi được ghi sau khi reply `success` (đã lập lịch).
   - **Đã reboot:** heartbeat có lại với uptime nhỏ hơn uptime trước khi gửi, hoặc có connectivity `offline` rồi `online` sau mốc gửi.
   - **Không thấy:** quá 120 s chưa thấy thì ghi kết quả này, không gửi lại.
   - Dry-run: reply `success` và uptime **không** được reset.
5. **Audit:** người gửi, dry-run hay thật, `delay_ms`, kết quả theo dõi.
6. **Realtime:** đẩy trạng thái theo dõi qua SignalR hiện có cho trang thiết bị.

### UI

1. **Nút "Khởi động lại"** trên trang thiết bị. Chỉ hiện khi `allowed-commands` có `system.reboot`.
2. **Hộp xác nhận:**
   - Liệt kê relay đang BẬT sẽ TẮT; kênh force giữ nguyên. Dữ liệu lấy từ trạng thái DO đã xác minh; chưa có thì đọc `control.get_status` trước.
   - Người dùng phải gõ đúng `REBOOT-<mqttDeviceId>`.
3. **Luồng:** dry-run → lệnh thật → "Đang khởi động lại…" → "Đã khởi động lại" hoặc "Chưa thấy thiết bị lên lại".
   - Sau khi đã reboot: đọc lại `system.get_info` và `control.get_status`.
4. **Khoá nút** trong lúc theo dõi. Không tự gửi lại.
5. **Modal C2 ẩn mọi lệnh class F**, và bỏ đoạn gán `confirm_token` sai.

### Simulator (`tools/mqtt-broker/spec-device-simulator.js`)

`system.reboot` theo spec:
- kiểm `confirm` và hỗ trợ dry-run;
- trả reply rồi offline trong lúc boot;
- uptime về 0, DO không force về OFF;
- throttle 60 s.

## Ngoài phạm vi

- `factory_reset`, `ota.*`, reboot nhiều thiết bị.
- Tự reboot sau khi đổi hostname.
- Giữ trạng thái theo dõi qua lần server khởi động lại: mất thì ghi "không rõ", như lane hiện tại.
- Cho người dùng chỉnh `delay_ms` trên UI: UI bỏ trống, dùng mặc định 2000 ms.

## Tiêu chí chấp nhận

1. Thiếu hoặc sai `confirm` thì bị từ chối trước khi ký; sniffer không thấy lệnh.
2. Người không phải master-admin, hoặc thiếu `sys.reboot` trên thiết bị, thì bị từ chối.
3. Dry-run: reply `success`, uptime không reset, không mất kết nối.
4. Reboot thật trên simulator và trên `gateway_870c`: UI báo "Đã khởi động lại"; đọc lại `system.get_info` và `control.get_status` thấy relay không force đã OFF.
5. Reboot thứ hai lúc đang theo dõi bị server từ chối.
6. `factory_reset`, `ota.remote_start`, `ota.apply` vẫn `DENIED_UNTIL_ACTION_CONTRACT`.
7. Modal C2 không còn hiện lệnh class F.
8. Unit, integration, vitest và `vue-tsc` đều xanh; số test không giảm.

## Rủi ro

| Rủi ro | Cách xử lý |
|---|---|
| Thiết bị không lên lại | Cùng firmware nên rủi ro thấp. Live test cần người ở lab |
| Relay đang BẬT bị TẮT | Hộp xác nhận liệt kê rõ. Live test chỉ khi user báo trước (DO1 nối vòng DI1 ở lab) |
| Connectivity retained cũ | Không dựa riêng vào connectivity: lấy uptime giảm làm bằng chứng chính, connectivity chỉ bổ trợ |
| Spec không định nghĩa shape reply dry-run | Coi `success:true` là qua; lưu reply. Đã nằm trong danh sách đặc tả cần xin (mục 3) |
| Tracking in-memory | Restart server mất trạng thái theo dõi, ghi "không rõ"; ổn với giả định một instance hiện tại |

## Bước tiếp

- `/ck:plan --tdd` với báo cáo này.
- Live test trên `gateway_870c` chỉ chạy sau khi user báo trước: gateway mất kết nối, DO1 về OFF.
