# PM report: rà lại session MQTT cũ (2026-11-08)

Nguồn: transcript session `15092bb7` (worktree `mqtt-device-connection-36f705`, 05/11 17:28 → 06/11 17:10 UTC), git log nhánh `claude/mqtt-device-connection-36f705`, các `plan.md`, `docs/project-roadmap.md` §5, `docs/architecture-mqtt-device-link.md`.

## 1. Đã làm

11 commit hơn `develop`, tip `45f5c14`. Chưa push (không có trên `origin`).

| Commit | Nội dung |
|---|---|
| `b84a65c` chore | Gỡ build artifact lỡ commit; credential của tool chuyển sang biến môi trường |
| `422b689` feat | Kênh MQTT live: lệnh ký HMAC, đối chiếu reply, timeout, audit; nhận heartbeat/connectivity/capabilities |
| `b0babe5`, `87ca1aa` docs | Kiến trúc, plan, scout; round trip có ký với gateway thật sau khi xoay key |
| `6b0aef5` feat | Relay DO: `control.set_output`, `control.pulse`; allowlist kênh theo thiết bị; đọc lại để xác nhận |
| `45ba9df` feat | 15 lệnh đọc giám sát, 3 thẻ đọc trên trang thiết bị |
| `171b9da` feat | Ghi cấu hình từ xa: hostname, múi giờ, `sync_ntp`, tên kênh ADC, bật/tắt kênh ADC |
| `d78cab1`, `c58c62e`, `2f087d3` docs | Plan, kết quả live, tài liệu kiến trúc, journal |
| `45f5c14` chore | Ghi chú của agent code-reviewer |

| Plan | Status |
|---|---|
| `261106-0100-mqtt-device-link` | done |
| `261106-0735-do-relay-control` | completed |
| `261106-1040-monitoring-read-actions` | completed |
| `261106-2033-remote-config-system-adc` | completed |

- Catalog ghim: 33/94 action đang mở (26 đọc, 2 relay, 5 ghi cấu hình).
- Live trên `gateway_870c` (firmware 1.0.108): mọi lệnh đã mở đều `Executed`. Sau lần chạy thử, gateway đã về giá trị gốc.
- Test theo báo cáo cuối của session cũ: 571 unit, 117 integration, 325 vitest, `vue-tsc` sạch. Report này chưa chạy lại.

## 2. Đang làm

- Không có việc nào dở. Lệnh cuối là "commit", đã xong; cây làm việc sạch.
- API (:5099) và FE (:5173) đã tắt.
- Worktree `mqtt-device-connection-36f705` đã bị xoá khỏi đĩa sau lần rà đầu ngày 08/11. Nhánh vẫn nguyên ở `45f5c14`, và cây làm việc khi đó sạch nên không mất gì. Muốn làm tiếp thì checkout nhánh vào một worktree.
- Worktree của session này nằm trên `develop` (`05a33ea`), nên không có code MQTT.
- Đang chờ team firmware trả lời các spec gap: định dạng TZ, `type`/độ dài `name` của ADC, hostname với `client_id`, allowlist NTP, rollback cho lệnh mạng/MQTT, reply job "accepted, still running".

## 3. Còn lại

| Nhóm | Chặn bởi |
|---|---|
| `system.reboot` (class F) | Không chặn. Spec §7 có mẫu confirm; cần theo dõi heartbeat sau reboot |
| Đọc Modbus RTU (`get_rtu_status/config`) | Spec ghi "pending review"; đã có evidence HIL |
| `tags.list/get`, đọc Modbus theo trang | Firmware chưa release; phải sinh lại catalog từ spec mới |
| `set_ntp_servers`, `set_time` | Thiếu allowlist NTP; rủi ro lệch đồng hồ |
| Ghi mạng/Wi-Fi/MQTT | Firmware chưa có rollback, có thể mất đường điều khiển |
| OTA, `factory_reset` | Rủi ro cao; cần redaction audit và confirm |
| `broker.*` pipeline | Chưa phân tích |
| Gia cố | Broker ACL/auth (anonymous), Postgres cho dev, retention audit, lane khi chạy nhiều instance |
| Sót lại từ review | Dev seed mở lại allowlist DO mỗi lần khởi động; `RequestedAt` lệch cho lệnh đọc; chưa backoff khi `LANE_FULL` |

Plan cũ vẫn ghi `in-progress`, cần đồng bộ lại: `261001-0030` (phần DO đã bị thay thế), `261005-0556` (phase 3–6 có thể đã cũ), `260924-2044`.

## 4. Khuyến nghị

1. Quyết định push nhánh và mở PR vào `develop`: 11 commit hiện chỉ nằm ở máy local.
2. Tính năng tiếp theo: reboot an toàn. Đây là lệnh vận hành còn thiếu không bị firmware chặn.
3. Một đợt nhỏ: sửa dev seed allowlist (dev đang điều khiển relay thật) và đồng bộ 3 plan cũ.

## Câu hỏi mở

- Team firmware đã trả lời spec gap chưa?
- Khoá lệnh gateway từng bị dán vào chat ngày 06/11, nên đang nằm trong transcript local. Có xoay khoá khi làm phần gia cố không?
