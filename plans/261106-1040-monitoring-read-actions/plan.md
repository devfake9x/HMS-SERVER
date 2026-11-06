---
title: "Mở lệnh đọc giám sát qua MQTT và thẻ đọc trên trang thiết bị"
status: completed
priority: P1
effort: medium
mode: tdd
created: 2026-11-06
branch: claude/mqtt-device-connection-36f705
brainstorm: plans/reports/261106-1029-brainstorm-monitoring-read-actions.md
contract: HMS-IDF/engineering-docs/api/mqtt-api.md, bản đang ghim 83e93d77e (§2, §3, §5, §6, §11, R3/R4)
builds_on: [261106-0100-mqtt-device-link, 261106-0735-do-relay-control]
supersedes: [261001-0030-full-hardware-mqtt-apis-c2 phase-01 catalog activation (read part)]
blockedBy: []
blocks: []
tags: [mqtt, monitoring, frontend]
---

# Monitoring read actions

Mở 15 lệnh đọc đã có trong catalog đang ghim, hiện trên 3 thẻ của trang thiết bị: thiết bị và thời gian, mạng,
nguồn và ADC. Không còn phải đọc JSON trong modal C2. Plan cũng thêm các chốt an toàn cho việc nhiều lệnh đọc
dùng chung hàng đợi lệnh của thiết bị với lệnh relay.

## Quyết định (user, 2026-11-06)

- **Đường dữ liệu:** đọc theo yêu cầu qua luồng gửi lệnh rồi chờ reply đã xác minh (`runDeviceCommand`). Server không lưu bản đọc riêng, không poll.
- **Lệnh nhạy cảm để đóng:** `sms.*`, `config.get`, `ota.remote_config_get`.
- **Sau red-team:**
  - Giữ catalog đang ghim (`83e93d77e`, 94 dòng), không ghim lại spec.
  - Bỏ hẳn phần Modbus và tags khỏi plan này: `modbus.*`, `tags.*`, thẻ Modbus.
  - Lý do: generator từ chối spec `18a63e8ed` (§11 ghi 98 cặp, bảng có 104), và các dòng mới là firmware chưa phát hành.
- **Áp dụng mọi phát hiện red-team còn lại** (xem "Red Team Review").

## 15 lệnh mở

| Thẻ | Lệnh | Quyền firmware |
|---|---|---|
| Thiết bị & thời gian | `system.get_time` | `sys.time_view` |
| | `system.get_hostname` | `sys.hostname_view` |
| | `system.get_device_id`, `ota.get_status` | `sys.info_view` |
| Mạng | `network.get_priority` | `net.priority_view` |
| | `network.get_ethernet` | `eth.view` |
| | `network.get_modem` | `modem.view` |
| | `network.wifi_sta_status` | `wifi.sta_view` |
| | `network.wifi_ap_status` | `wifi.ap_view` |
| Nguồn & ADC | `battery.get_status`, `battery.get_alerts` | `sys.battery_view` |
| | `adc.get_config`, `adc.get_channels`, `adc.get_values` (`adc.get_status` mở cho modal) | `adc.view` |

Tất cả là class R, đã có trong catalog đang ghim. Không có quyền mới nào.

## Phases

| # | Phase | Priority | Status | Depends on |
|---|---|---|---|---|
| 1 | [Server: mở lệnh đọc và chốt an toàn](phase-01-server-reads-and-safety.md) | P1 | completed | — |
| 2 | [UI: ba thẻ đọc](phase-02-read-cards-ui.md) | P1 | completed | 1 |
| 3 | [Kiểm chứng live và tài liệu](phase-03-live-verification-docs.md) | P1 | completed | 1, 2 |

Mỗi phase theo TDD: test thất bại viết trước, rồi code tới khi xanh. Test cũ chỉ đổi ở đúng các assertion mà
phase liệt kê là có chủ đích.

## Acceptance (toàn plan)

- **Catalog:** đúng 15 lệnh trên được mở thêm (tổng 28 lệnh mở = 11 đọc cũ + 15 + 2 DO). Catalog JSON, hash và từ vựng quyền không đổi.
- **An toàn hàng đợi thiết bị:**
  - mỗi thiết bị có tối đa N lệnh đọc đang chờ (mặc định 4); lệnh thừa bị từ chối trước khi gửi;
  - lệnh vật lý không bị giới hạn này;
  - mọi lệnh có `ttl_ms ≤ 20000`, lệnh relay `≤ 10000` (as built; plan ban đầu là 10 s cho mọi lệnh).
- **Realtime:** lệnh đọc không phát event `command_result`. "Last Heartbeat" chỉ đổi theo heartbeat (event `status`); event connectivity chỉ làm mới cờ online/broker của link.
- **`GET /api/v1/devices/{id}/allowed-commands`:**
  - trả các lệnh đang mở mà người gọi được gửi trên thiết bị đó;
  - dựng quyền một lần cho mỗi request;
  - dùng đúng quy tắc scope của `POST .../commands`.
- **`GET .../commands/{correlationId}`** kiểm lại quyền của action khi đọc.
- **Ba thẻ:**
  - đọc khi bấm, huỷ được, không chạy chồng;
  - dữ liệu đúng thiết bị đang xem;
  - giờ đọc theo từng lệnh; đọc lỗi thì không giữ giá trị cũ;
  - báo rõ `CATALOG_STALE`, thiếu quyền, thiết bị bận hay offline.
- **Gateway thật:** 15 lệnh đều `Executed`, đối chiếu đúng spec. Chỗ lệch ghi thành spec gap. Fixture đã redact.
- **Test:** toàn bộ .NET và frontend xanh; `vue-tsc` sạch.

## Kết quả (2026-11-06)

- **Phase:** 3/3 xong.
- **Test:** 494 unit + 114 integration .NET, 203 frontend; `vue-tsc` sạch; EF không còn thay đổi model chưa migrate.
- **Review:** tester (burst 10 lệnh đọc song song vẫn giữ giới hạn) và code-reviewer (0 Critical; 1 High + 6 Medium đã sửa). Xem [code-review-report.md](reports/code-review-report.md).
- **Live:** [reports/live-verification.md](reports/live-verification.md).
  - 15/15 lệnh `Executed` trên gateway; 4 spec gap;
  - giới hạn lệnh đọc giữ được trên thiết bị thật;
  - sniffer xác nhận không có lệnh ghi.
- **Chỗ code khác plan:** mục "As built" ở cuối mỗi phase. Đáng chú ý: TTL 20 s cho lệnh đọc (lệnh relay giữ 10 s); mỗi người dùng chỉ được nửa số slot đọc; lệnh đọc lại relay có slot dành riêng.

## Ngoài phạm vi

- **Ghim lại spec và mọi lệnh Modbus/tags.** Làm lại khi HMS-IDF commit firmware và sửa số đếm §11; báo team firmware về lỗi đếm.
- **Lệnh ghi và lệnh nhạy cảm:** mọi lệnh ghi; `sms.*`; `config.get`; `ota.remote_config_get`; `broker.*`; `network.wifi_ap_clients`; `network.wifi_scan_results`.
- **Phía server:** lưu bản đọc, poll định kỳ, lịch sử, biểu đồ.
- **Audit:** chính sách retention (để plan sau; plan này chỉ thêm index theo status).
- **Modal C2:** vẫn gate theo feature permission như hiện tại; chưa dùng `allowed-commands`.

## Rủi ro còn lại

- **Broker lab anonymous:** reply đọc là plaintext đã ký, ai trên broker cũng đọc được; lệnh đọc bị replay thì vô hại. Cần ACL và xác thực broker (ngoài plan).
- **Role cũ được thêm quyền đọc:** role đang giữ các mã quyền ở bảng trên sẽ đọc được ngay sau phase 1. Phase 3 ghi danh sách này vào tài liệu.
- **Reply lệch spec:** thẻ hiện trường lạ dạng JSON thô; phase 3 đối chiếu từng lệnh.

## Red Team Review

### Session — 2026-11-06

**Reviewers:** Security Adversary, Assumption Destroyer, Failure Mode Analyst (Standard tier: Fact Checker + Contract Verifier). Reports: `reports/from-code-reviewer-to-planner-red-team-*-plan-review-report.md`.

**Findings:** 28 raw → 15 after merging; all carried file:line or spec-line evidence. 13 accepted and applied, 2 not applicable after the scope decision.
**Severity:** 1 Critical, 8 High, 6 Medium.

| # | Finding | Severity | Disposition | Applied to |
|---|---|---|---|---|
| F1 | Generator rejects spec `18a63e8ed` (§11 says 98 pairs, table has 104); new rows are unreleased firmware overlay | Critical | Accept → user decision: keep pin `83e93d77e`, drop Modbus/tags | plan.md, all phases rewritten |
| F2 | Reads share the device's 16-slot queue and rate bucket with relay commands; server has no per-device cap; `LANE_FULL` without backoff | High | Accept | Phase 1 (read cap), Phase 2 (retry once), Phase 3 (live check) |
| F3 | Modbus card contradicts the spec's paged-read semantics | Critical/High | N/A (Modbus dropped) | — |
| F4 | ADC card joins on `ai`, absent from `adc.get_status` | High | Accept | Phase 2 (use `get_values` + `get_channels`) |
| F5 | Read sequences not cancellable, can overlap, leak across device switch | High | Accept | Phase 2 |
| F6 | Empty/partial/old data shown as current | High | Accept | Phase 2 |
| F7 | Existing roles silently gain reads through reused permission codes | High | Accept (document) | Phase 3 |
| F8 | Undefined reply shapes and real fixtures can leak modem identity, SSIDs, URLs | High | Accept | Phase 3 (redaction rule) |
| F9 | Wrong counts for the re-pin (high-risk 21, ~10 assertions) | High | N/A (no re-pin) | — |
| F10 | Reads live 120 s on the device vs 30 s server timeout | Medium | Accept | Phase 1 (TTL ≤ 20 s for every command, relay 10 s; see As built) |
| F11 | `command_result` flood to every viewer; any event counted as heartbeat | Medium | Accept | Phase 1 (no realtime for reads), Phase 2 (heartbeat) |
| F12 | `allowed-commands` would run 100+ DB queries; scope rule differs from `POST /commands` | Medium | Accept | Phase 1 |
| F13 | Reply readable after permission loss; shared reply store eviction; audit has no status index | Medium | Accept (retention out of scope) | Phase 1, Phase 2 (`no_data`) |
| F14 | `CATALOG_STALE` unhandled; HTTP error body dropped; permission refusal is HTTP 200 | Medium | Accept | Phase 2 |
| F15 | Phase 4 tools live only in a session scratchpad and truncate replies; broker plaintext | Medium | Accept | Phase 3 (repo tool), plan.md residual risk |

Also fixed: the wrong "spec closes only 9 `SLICE_PASS`" comment guidance, and the unneeded route-collision reasoning for `allowed-commands`.

### Whole-Plan Consistency Sweep

- Files reread: `plan.md`, `phase-01-server-reads-and-safety.md`, `phase-02-read-cards-ui.md`, `phase-03-live-verification-docs.md`. The old four phase files were deleted (rewritten from scratch).
- Decision deltas: 4 phases → 3; 21 actions → 15; no catalog re-pin (pin stays `83e93d77e`, catalog/hash/permissions unchanged); Modbus and tags removed; 4 cards → 3; added read cap, TTL for every command, no `command_result` for reads, permission re-check on reply read, audit status index.
- Stale terms searched: `18a63e8ed`, `modbus`, `get_rtu`, `tags.`, `104`, `21`, `re-pin`, `4 thẻ`, `20 lệnh`. Remaining hits are intentional: the decision record, the out-of-scope list, Phase 1 test 1 (asserts `modbus.*` stays closed) and the Phase 3 note for the firmware team.
- Unresolved contradictions: 0.
