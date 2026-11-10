# PM report: safe reboot completion (2026-11-10)

Plan: `plans/261109-2250-safe-reboot/plan.md` | Branch: `claude/mqtt-device-connection-36f705` (uncommitted, no commits made) | Sync-back: all 3 phase files swept.

## Status

| Phase | Status | Tiêu chí tick | Ghi chú |
|---|---|---|---|
| 1 Server | completed | 6/7 | Audit (Postgres) mở, follow-up (b) |
| 2 UI | completed | 6/6 | Residual L1, L4-L8, L11 chấp nhận |
| 3 Simulator, live, docs | completed | 3/3 hoàn tất; 0/3 hoãn | Gateway hoãn, không tính |

Plan: `status: completed`. Hoàn tất theo phạm vi đã duyệt (simulator). Gateway vẫn mở (follow-up a).

## Bằng chứng chính

| Hạng mục | Kết quả | Nguồn |
|---|---|---|
| .NET unit | 640/640 (3 lần) | tester, controller |
| .NET integration | 126/126 (`Mqtt__Enabled=false`) | controller (tester chạy 123, trước 3 ca bổ sung) |
| EF model | không thay đổi | phase 1, tester |
| Vitest | 475/475 (2 lần) | phase 2, tester |
| vue-tsc, vite build | sạch, OK | phase 2, tester |
| Simulator kịch bản 1-7, 9 | PASS | `reports/live-verification.md` |
| UI kịch bản 8 | PASS (sau 2 lần sửa) | controller, `reports/screens/` |
| Gateway `gateway_870c` | hoãn | Validation Session 1 |

## Review

| Báo cáo | Critical/High | Medium | Xử lý |
|---|---|---|---|
| Server và tool | 0 | M1, M2, M3 | M1-M3 sửa test-first; L1-L4, L10, L11 sửa; L5, L6 chấp nhận |
| Frontend | 0 | M1, M2, M3 | M1-M3 sửa; L2, L3, L9, L10 sửa; L1, L4-L8, L11 chấp nhận |
| Tester | 0 | gap: ota.* master-admin | Đã đóng sau báo cáo (3 ca, `ManagementApiTests`) |

## Thay đổi phạm vi và lệch plan

| # | Lệch | Lý do | Tác động |
|---|---|---|---|
| 1 | Aedes thay bằng Mosquitto 2.1.2 (loopback 8088) | Aedes chỉ MQTT 3.1.1; MQTTnet 5 dùng 5.0 | Dev broker chưa dùng được với server; follow-up (c) |
| 2 | Hold = max(accept + 122 s, reply deadline + 2 s); `Admit` nhận deadline | Review M1: timeout reply > 120 s làm hold kết thúc sớm | Đúng hơn plan; test 180 s |
| 3 | `TimeProvider` tuỳ chọn trong transport | Test mốc 60 s / 120 s | Không đổi hành vi |
| 4 | Tracker không có trạng thái `Abandoned` | Abandon đưa completion `OUTCOME_UNKNOWN` vào hàng đợi | Tương đương hành vi plan |
| 5 | `RebootKind`/`rebootPlan` chuyển sang `core/` | Review L9: `core` không import `features` | Không đổi hành vi |
| 6 | Thêm `RebootConfirmBody.vue`, `RebootResult.vue`, prop `promptSuffix`, `inputPlaceholder` | Giữ panel gọn; chữ tiếng Việt đúng | Mặc định cũ giữ nguyên |
| 7 | Thêm `SIM_SILENT_REAL` | Kiểm "không reply, không reboot" mà không chặn dry-run | Chỉ simulator |
| 8 | Thứ tự kiểm simulator: dry-run, throttle, confirm, payload | Admission trước handler (§3 L483) | Chỉ simulator |
| 9 | Gateway hoãn | Không có người ở lab; bàn thử đang dùng cho HIL | Plan hoàn tất không gồm gateway |

## Rủi ro: cập nhật

| Rủi ro | Trạng thái | Hành động |
|---|---|---|
| Hold ngắn hơn reply timeout (M1 server) | Đóng | Sửa test-first |
| Tool coi publish chưa xác nhận là "chưa gửi" (M2) | Đóng | Sửa theo precedent |
| Simulator busy không đúng dạng spec (M3) | Đóng | Sửa dạng spec; regex anchored |
| Tab switch huỷ lần chạy (frontend M1) | Đóng | State lên page scope |
| `relay_unchecked` không nói verified (M2 FE) | Đóng | Text kết hợp |
| Preflight đọc sau unmount (M3 FE) | Đóng | Abort khi dispose |
| Server L5, L6 | Mới, chấp nhận | Trong `plan.md` |
| Frontend L1, L4-L8, L11 | Mới, chấp nhận | Trong `plan.md` |
| Tool live không test tự động; simulator không đại diện thời gian gateway | Mới, ghi nhận | Trong `plan.md` |
| Nhánh Postgres audit chưa chạy | Mở | Follow-up (b) |
| Gateway chưa có số đo boot/LWT | Mở | Follow-up (a) |
| Mất cổng/theo dõi khi server restart (in-memory) | Mở, đã có | Rủi ro còn lại cũ |
| Phát lại trên broker không ACL | Mở, điều kiện production | Follow-up (e) |

## Blocker

| Việc | Stall | Owner | Đường mở |
|---|---|---|---|
| Gateway live (phase 3, bước 4) | Hoãn từ 2026-11-10; vượt một phiên | User, người ở lab | Có người ở lab, bàn thử rảnh, user nói "go"; preflight PASS |
| Nhánh Postgres | Chưa có Postgres dev | Dev | Chạy test trên Postgres |
| Integration cần `Mqtt__Enabled=false` | Môi trường, từ trước plan | User | Làm mới user-secrets hoặc ép tắt trong factory |

## Hành động tiếp theo (hoàn tất plan)

| # | Việc | Owner | Done khi |
|---|---|---|---|
| a | Chạy gateway live | User, người ở lab | Kịch bản gateway 1-7 PASS hoặc lệch có lý do; DO1 restore; sniffer xoá; 3 mục hoãn tick |
| b | Chạy Postgres: `TryCompleteAsync`, `FailUnfinished`, kịch bản 9 `OUTCOME_UNKNOWN` | Dev | Test xanh trên Postgres; tick tiêu chí phase 1 |
| c | Quyết định broker dev; sửa môi trường integration | User (quyết), dev | Integration không cần biến môi trường; broker dev chạy được với MQTTnet 5 |
| d | Gửi câu hỏi spec cho firmware | User chuyển | Firmware đã nhận; trả lời ghi lại |
| e | ACL broker production | Hạ tầng | ACL có hiệu lực trước khi bật reboot production |

## Lưu ý kiểm soát

- `plans/261001-0030-full-hardware-mqtt-apis-c2/plan.md` đã có supersede ở frontmatter (dòng 10-13, `supersededParts` đủ 8 phần). Không sửa thêm. Dòng ghi chú còn ghi `(pending)`; nên đổi thành `completed` khi user đồng ý.
- Không có TaskUpdate nào trong phiên này, nên không có mapping task→phase cần đối chiếu.

## Câu hỏi chưa giải quyết

1. Phase 1 đóng với tiêu chí audit Postgres còn mở. Chấp nhận như vậy, hay giữ phase 1 ở trạng thái khác đến khi có Postgres?
2. Có planned `ResponseTimeoutSeconds` trên 120 s cho gateway 4G không? Nếu có, M1 thành High; cần validate ở `MqttLinkOptions` hoặc giữ cách hiện tại (đã dùng deadline).
3. `/me/command-catalog` báo `system.reboot` `available:true` cho mọi người gọi (theo plan). UI không đọc nhầm là quyền? Cần xác nhận.
4. L8: có thêm `forced` vào `OutputChannelDto` không? Hiện hộp xác nhận không thấy đổi force cùng mức.
5. Dev broker: đặt protocol version ở server, đổi broker, hay ghi Mosquitto là broker dev? (Quyết định của user.)
6. Có chạy thêm `SIM_SILENT=system.reboot` (dry-run cũng im) qua UI không? Tuỳ chọn, chưa làm.
7. `ota.get_status`: shape nào cho biết image chờ? Cần cho preflight gateway.
