# Độ phủ spec MQTT và đặc tả cần xin thêm (2026-11-08)

Nguồn:
- Spec `HMS-IDF/engineering-docs/api/mqtt-api.md`: bản server đang ghim `83e93d7` và bản hiện tại `9bfc168` (2026-11-09).
- Server: nhánh `claude/mqtt-device-connection-36f705` @ `45f5c14`, gồm `PinnedCommandCatalog.cs`, `command-catalog.json`, `docs/architecture-mqtt-device-link.md`.
- Chỉ đọc spec, không đọc source firmware.

## 1. Lệnh (§11)

| | Số row |
|---|---|
| Spec hiện tại §11 | 108 (13 namespace) |
| Catalog server (ghim `83e93d7`) | 94 |
| Server đã mở | 33: 26 R, 2 B, 5 W/C |
| Chưa mở | 75: 32 R, 15 W, 15 C, 4 D, 5 B, 4 F |
| Firmware đã đóng `SLICE_PASS` | 9 lệnh đọc trong snapshot, cộng `control.set_output`, C18 và `system.get_logs_metadata` theo addendum; release-qualified 0/14 |

Có trong spec mới nhưng catalog server chưa có (14):
- `tags.list`, `tags.get`;
- `sms.get`, `sms.send`, `sms.send_status`, `sms.delete`;
- `modbus.get_rtu_devices`, `get_rtu_diagnostics`, `get_rtu_live`, `get_rtu_tags`;
- `control.force_set`, `control.force_clear`;
- `control.get_config`, `control.set_config` (thêm ngày 2026-11-09).

Row đổi giữa hai bản: `status.tags` đổi quyền `sys.info_view` → `tag.view`. Server đang mở `status.tags` với **quyền cũ**.

Chưa mở theo namespace:

| Namespace | Chưa mở |
|---|---|
| system | `reboot`, `factory_reset` (F); `set_time`, `set_ntp_servers` (C) |
| ota | `remote_start`, `apply` (F); `remote_config_get` (R) |
| network | 21 lệnh: Wi-Fi STA/AP, known networks, scan, `configure_ethernet`, `set_priority` |
| mqtt | `set_config` (W), `reconnect` (C) |
| modbus | 7 lệnh đọc, `set_rtu_config` / `replace_rtu_config` (W), `restart_rtu` (C) |
| broker | 16 lệnh (pipeline, signal, chain…) |
| sms | 7 lệnh |
| config | `get`, `list_domains`, `set` |
| control | `set_all` (cố ý đóng: không atomic), `force_set`, `force_clear`, `get_config`, `set_config` |
| tags | `list`, `get` |
| battery | `clear_alert`, `quick_start` (C) |

## 2. Phần không phải lệnh

| Spec | Server |
|---|---|
| §1–§5: envelope, HMAC, reply, timeout, operation | Xong |
| §7: confirm, dry_run | Server xong: tự dựng `confirm` theo MQTT device id, sai thì `CONFIRM_MISMATCH`. Modal C2 gửi sai (`confirm_token` trong params), nhưng bị chặn (fail closed). Chưa mở lệnh class F nào |
| §8: RTU framed ≤ 131072 B (R5–R10, R12) | Chưa làm |
| §9: telemetry SIGN/ENCRYPT | Chưa làm; thiết bị chưa có key `tlm_*` |
| §10: heartbeat, connectivity, capabilities, events | Xong |
| R13–R15: chỉ nhận từ primary; phiên thử `mqtt.set_config` 120 s | Chưa làm |

## 3. Đặc tả cần xin thêm (gửi team firmware)

### A. Chặn tính năng kế tiếp

1. **`ota.get_status`** (§6.2): tên field, các trạng thái (idle/downloading/staged/failed…), ý nghĩa của `rollback`. Reply thật lồng `{firmware, partition, ota, rollback}` nhưng spec không mô tả.
2. **OTA tải và apply** (§6.2, §7):
   - Thiết bị tin CA nào cho `url`? Có cho HTTP không? URL có được chứa token không?
   - Giới hạn size; hành vi khi tải hỏng giữa chừng.
   - Image boot được nhưng không vào được MQTT thì có rollback không? Ghi chú C14 nói firmware xác nhận image trước khi khởi tạo MQTT.
3. **`system.reboot`** (§7):
   - Reply gửi trước khi reboot hay không?
   - Thứ tự heartbeat/connectivity khi thiết bị lên lại; thời gian tối đa để có lại kết nối.
   - Server lấy tín hiệu nào để xác nhận đã reboot (uptime, boot count trong `system.get_info`)?
   - Các field của reply dry-run ngoài `would_apply`. Thứ tự đã có ở spec L551 (preview chạy trước confirm/dedup/safe-mode/throttle, không gọi handler), nên không hỏi lại; `delay_ms` không cần hỏi vì server chỉ gửi `{}` (plan 261109-2250).
4. **Lệnh mạng/Wi-Fi** (§6.3): có cơ chế phiên thử và tự hoàn tác giống `mqtt.set_config` (R14) cho `configure_ethernet`, `set_priority`, `wifi_sta_*` không? Nếu không có, server tiếp tục để đóng.
5. **NTP và giờ**: ràng buộc server NTP hợp lệ cho `set_ntp_servers`; khi nào `set_time` an toàn để dùng.
6. **ADC và hệ thống** (§6.1–6.2):
   - Danh sách chuỗi TZ hợp lệ.
   - ADC: `type` hợp lệ, `name` tối đa bao nhiêu byte; khi không gửi `type`/`enabled` thì giữ hay xoá giá trị cũ.
   - Hostname: đổi hostname có ảnh hưởng `client_id`/session không; `applied_immediately:false` nghĩa là gì.
7. **Job** (§5): lệnh nào có thể trả "accepted, still running" trên firmware 1.0.108.

### B. Spec tự mâu thuẫn

8. Số đếm trong §11 không khớp bảng:
   - Câu đầu ghi "98 cặp, 12 namespace", và generator đọc câu này. Câu sau ghi "100 cặp, 13 namespace" (kiểm lại 22/10).
   - Bảng thực tế có 108 row, nên generator từ chối spec.
   - Cần một câu đếm duy nhất, đúng với bảng.
9. §11 thiếu row `system.get_logs_metadata`, dù addendum 2026-11-03 nói đã admit và `SLICE_PASS`. Cần thêm row (class, quyền, TTL).
10. Bảng snapshot "verified 2026-10-28" đã cũ so với các addendum (`set_output`, `get_logs_metadata`, quyền `status.tags`). Cần một bảng trạng thái hiện hành.
11. Map firmware version ↔ manifest:
    - Các row Modbus theo trang và phiên thử `set_config` ghi "working tree, chưa vào HEAD". Firmware 1.0.108 có những row nào?
    - `B/capabilities` có liệt kê đúng các row mà image đang chạy chấp nhận không?
12. Field ngoài spec: `system.get_time` trả thêm `ntp_servers`, `ntp_mode`; `ota.get_status` lồng thêm một cấp.

### C. Bảo mật và hạ tầng

13. Danh tính và ACL broker cho server và thiết bị. Quick path bảo lấy "ACL từ kênh provisioning" nhưng không định nghĩa ACL đó. Broker lab đang anonymous.
14. Lấy `key_version` qua MQTT; thuật toán `fingerprint` của `GET /api/security/keys`.
15. Telemetry §9: khi nào thiết bị có key `tlm_*`, và luồng cấp key.
16. `config.get` có cam kết redact secret không; field nhạy cảm của `ota.remote_config_get`.

### D. DO ownership (spec `c80797d`, 2026-11-10)

Chưa gửi. Nguồn: plan `261110-1335-do-channel-ownership`.

17. `control.pulse` trên kênh có luật theo mức: bị từ chối `owned_by_rule` không? (L831 chỉ nêu `set_output`, `set_all`, `set_config`; L818 ghi pulse không đổi trong đợt này.)
18. `owner`/`owner_id` có từ firmware nào; 1.0.108 có gửi không?
19. Kiểu của `owner_id` (số hay chuỗi)?

## 4. Thứ tự đề xuất

1. User gửi mục 3 cho firmware.
2. **Reboot an toàn**, không chờ firmware: §7 đã đủ, server đã có `confirm`/dry-run. Cần thêm: luồng UI đúng, theo dõi heartbeat sau reboot, đóng job trong audit.
3. **Đồng bộ catalog** với spec mới: thêm 14 row, sửa quyền `status.tags`. Làm khi firmware sửa mục 8–9; trước đó generator có thể từ chối.
4. Sau khi firmware trả lời mục 1–2: OTA. Sau mục 4: lệnh mạng.
