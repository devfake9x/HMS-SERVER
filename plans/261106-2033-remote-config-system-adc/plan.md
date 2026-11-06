---
title: "Cấu hình thiết bị từ xa: nhóm Hệ thống & ADC"
status: completed
priority: P1
effort: medium
mode: tdd
created: 2026-11-06
branch: claude/mqtt-device-connection-36f705
brainstorm: plans/reports/261106-2029-brainstorm-remote-config-system-adc.md
contract: HMS-IDF/engineering-docs/api/mqtt-api.md, bản đang ghim 83e93d77e (§0 R3/R4, §2, §3, §5, §6.1, §6.2)
builds_on: [261106-0100-mqtt-device-link, 261106-0735-do-relay-control, 261106-1040-monitoring-read-actions]
supersedes: [261001-0030-full-hardware-mqtt-apis-c2 phase-01 catalog activation (system/ADC writes), phase-03 adc.toggle_channel]
blockedBy: []
blocks: []
tags: [mqtt, config, frontend]
---

# Cấu hình từ xa: Hệ thống & ADC

Mở 5 lệnh ghi cấu hình trong catalog đang ghim. Người dùng ghi qua form trên hai thẻ có sẵn của trang thiết bị.

Plan dựng một khung an toàn chung cho lệnh ghi, dùng lại được cho các đợt sau:
- **Payload:** kiểm theo từng action và từ chối mặc định.
- **Lane ghi theo thiết bị:**
  - mỗi thiết bị có tối đa 1 lệnh ghi đang chờ, 10 lệnh ghi mỗi phút;
  - khi thiết bị báo job còn chạy, lane giữ thêm 60 s.
- **Giờ ký:** lệnh ghi luôn dùng giờ server.
- **Không tự gửi lại:** thiếu chứng cứ "chưa gửi" thì đọc lại trạng thái, không gửi lại.
- **Job còn chạy:** poll `system.get_operations` tới khi xong.
- **Audit:** lưu payload của lệnh ghi đã mở.

## Quyết định (user, 2026-11-06)

- **Phạm vi:** nhóm "Hệ thống & ADC". Các lệnh mạng, Wi-Fi, MQTT, reboot và `set_time` để đóng.
- **Sau red-team, `system.set_ntp_servers` cũng đóng.** Lý do: ai có `sys.time_edit` có thể trỏ thiết bị tới một NTP server sai giờ, khi đó:
  - mọi lệnh sau bị `expired` hoặc `future_timestamp`, kể cả lệnh trả lại, nên phải ra hiện trường;
  - nếu đồng hồ lùi về trước 2020, thiết bị bỏ kiểm tuổi lệnh và lệnh cũ có thể bị phát lại (spec §2, §3).
  - `sync_ntp` vẫn mở, vì nó chỉ đồng bộ với server đã cấu hình. Danh sách NTP chỉ hiển thị.
- **Kiểm live:** ghi trên `hms_toan_lab/gateway_870c`, đọc lại, rồi đặt về giá trị cũ. User báo team firmware trước khi chạy.
- **Áp dụng mọi phát hiện red-team còn lại** (xem "Red Team Review").
- **Claude tự quyết theo uỷ quyền:**
  - form đặt trên thẻ đọc có sẵn;
  - quyền dùng mã firmware có sẵn, không đưa vào `HighRisk`. Lý do: tách quyền xem và quyền sửa như AWS IAM (Get/Put) và K8s RBAC (get vs update); 5 lệnh này đều tác động thấp sau khi đã bỏ NTP server;
  - phase 3 liệt kê role thật đang giữ các mã này.

## 5 lệnh mở

| Thẻ | Lệnh | Class | Input server nhận | Đọc lại | Quyền firmware |
|---|---|---|---|---|---|
| Thiết bị & thời gian | `system.set_hostname` | W | `{hostname}`: `[A-Za-z0-9-]`, 1..63 byte, không bắt đầu hay kết thúc bằng `-` | `system.get_hostname` | `sys.hostname_edit` |
| | `system.set_timezone` | W | `{timezone}` nằm trong danh sách TZ POSIX của server | `system.get_time` | `sys.time_edit` |
| | `system.sync_ntp` | C | không tham số | `system.get_time` | `sys.time_edit` |
| Nguồn & ADC | `adc.set_channel` | W | `{ai: 1..16, name}`, `type` và `enabled` tuỳ chọn. UI luôn gửi đủ 4 trường, trong đó `type`/`enabled` lấy từ lần đọc | `adc.get_channels` | `adc.config_edit` |
| | `adc.toggle_channel` | C | `{ai: 1..16, enabled: bool}` | `adc.get_channels` | `adc.enable` |

- **Catalog:** cả 5 lệnh đã có trong catalog đang ghim. Catalog JSON, hash và từ vựng quyền giữ nguyên.
- **Poll job:** control chỉ bật khi người dùng được gửi lệnh ghi, các lệnh đọc lại tương ứng và `system.get_operations` (`sys.info_view`).

## Phases

| # | Phase | Priority | Status | Depends on |
|---|---|---|---|---|
| 1 | [Server: mở 5 lệnh ghi và khung an toàn](phase-01-server-config-writes.md) | P1 | completed | — |
| 2 | [UI: form sửa trên hai thẻ](phase-02-config-edit-ui.md) | P1 | completed | 1 |
| 3 | [Simulator, kiểm live và tài liệu](phase-03-live-verification-docs.md) | P1 | completed | 1, 2 |

Mỗi phase theo TDD: viết test thất bại trước, rồi code tới khi xanh. Test cũ chỉ được đổi ở đúng các assertion mà
phase liệt kê là có chủ đích.

## Acceptance (toàn plan)

- **Catalog:**
  - đúng 5 lệnh trên được mở thêm, tổng 33 lệnh mở (26 đọc + 2 DO + 5 cấu hình);
  - mọi lệnh class W/C/D/F khác vẫn `DeniedUntilActionContract`. Có test cho `system.set_ntp_servers`, `system.set_time`, `system.reboot`, `system.factory_reset`, `config.set`, `network.*`, `mqtt.*`, `broker.*`, `modbus.*`, `ota.*`. Gửi một lệnh đóng bằng role có quyền tương ứng phải trả `DENIED_UNTIL_ACTION_CONTRACT`.
- **Kiểm payload:**
  - payload sai bị từ chối trước khi ký (`COMMAND_PAYLOAD_INVALID`), nên không có gì được publish;
  - lệnh W/C/D/F không có luật kiểm riêng bị `CONFIG_ACTION_UNSUPPORTED`.
- **Lane ghi** (mọi class trừ R và B):
  - mỗi thiết bị có tối đa 1 lệnh đang chờ; lệnh thứ hai bị `DEVICE_WRITE_BUSY`;
  - tối đa 10 lệnh ghi được gửi mỗi phút cho mỗi thiết bị; lệnh thừa bị `DEVICE_WRITE_RATE_LIMITED`;
  - slot giữ tới khi có reply, hoặc tới max(`Deadline`, `ExpiresAt` + 2 s);
  - reply job còn chạy giữ lane thêm 60 s;
  - lane đọc và lệnh relay không bị ảnh hưởng.
- **Giờ ký:** lệnh W/C luôn dùng giờ server làm `RequestedAt`, bỏ qua giá trị client gửi.
- **Audit:** lưu payload của 5 lệnh mở (không có secret). Các lệnh W/C khác giữ `null` cho tới khi có che theo từng action.
- **Quyền:**
  - từ chối quyền trả HTTP 200 với `Failed`/`DEVICE_PERMISSION_REQUIRED`, không phải 403;
  - `allowed-commands` chỉ liệt kê lệnh mà người gọi được gửi;
  - reply bị ẩn nếu quyền đã mất.
- **UI:**
  - chỉ cho sửa khi lần đọc tương ứng thành công trong vòng 60 s;
  - hộp xác nhận hiện giá trị cũ → mới;
  - khoá cả thao tác đọc lẫn ghi của thẻ trong lúc ghi;
  - chỉ `certainlyNotSent` hoặc HTTP 4xx mới tính là "chưa gửi"; mọi trường hợp khác đều đọc lại (cùng quy tắc với DO panel);
  - đọc lại so cả bản ghi chứ không chỉ trường vừa sửa; giờ thiết bị lệch hơn 30 s thì cảnh báo;
  - `command_result` của lệnh cấu hình do người khác gửi đánh dấu dữ liệu thẻ là cũ;
  - modal C2 ẩn 5 lệnh này.
- **Gateway thật:**
  - 5 lệnh `Executed`, hoặc `Acknowledged` mà job kết thúc thành công;
  - đọc lại thấy giá trị mới; đặt lại thì thấy giá trị gốc;
  - trạng thái cấu hình cuối (hostname, timezone, kênh ADC) trùng giá trị gốc;
  - sniffer chỉ thấy các lệnh trong kịch bản.
- **Test:** toàn bộ .NET và frontend xanh; `vue-tsc` sạch.

## Kết quả (2026-11-06)

- **Phase:** 3/3 xong.
- **Test:** 571 unit + 117 integration .NET, 325 frontend; `vue-tsc` sạch; EF không có thay đổi model.
- **Review:**
  - tester: lane ghi giữ được với 20 luồng; tìm ra lỗi surrogate lẻ;
  - code-reviewer: 0 Critical/High; 2 Medium + các Low đã sửa. Xem [reports/code-review-report.md](reports/code-review-report.md).
- **Live:** [reports/live-verification.md](reports/live-verification.md).
  - 5/5 lệnh `Executed` trên gateway (firmware 1.0.108); đọc lại khớp; đặt lại khớp giá trị gốc;
  - sniffer: 48 lệnh, 11 lệnh ghi đúng kịch bản, `ttl_ms` 20000.
- **Role bị ảnh hưởng:** [reports/affected-roles.md](reports/affected-roles.md) (dev: không có role nào).
- **Chỗ code khác plan:** mục "As built" của từng phase.

## Ngoài phạm vi

- **Lệnh để đóng:**
  - `system.set_ntp_servers` và `system.set_time` (đồng hồ thiết bị);
  - reboot, factory reset, OTA;
  - `config.set`;
  - mọi lệnh `network.*`, Wi-Fi, `mqtt.*`, `broker.*`, Modbus.
- **ADC:** UI không cho đổi `type` của kênh. Server chỉ nhận `type` để gửi lại đúng giá trị vừa đọc.
- **Phía server:**
  - theo dõi job thay UI, và đóng audit `Acknowledged` khi job xong;
  - lưu lịch sử cấu hình;
  - cấu hình hàng loạt nhiều thiết bị.

## Spec gap (đưa user chuyển cho team firmware, không tự gửi)

- **TZ:** định dạng chuỗi TZ mà `system.set_timezone` chấp nhận.
- **ADC `set_channel`:**
  - các giá trị `type` hợp lệ;
  - giới hạn độ dài `name`;
  - khi không gửi `type`/`enabled` thì thiết bị giữ hay xoá giá trị cũ.
- **`system.get_time`:** trả `ntp_servers`/`ntp_mode` nhưng §6.1 không liệt kê.
- **Hostname:** đổi hostname có ảnh hưởng MQTT `client_id` hay session không; `applied_immediately:false` nghĩa là gì.
- **NTP:** chưa có cách giới hạn NTP server hợp lệ. Đây là điều kiện để mở `set_ntp_servers`.
- **Rollback:** chưa có commit-confirm hay rollback cho lệnh đổi mạng và MQTT.

## Rủi ro còn lại

- **Lane chỉ trong bộ nhớ:** restart server làm mất lane, chỉ còn audit `FailUnfinished` đánh dấu outcome không rõ. Giả định server chạy một instance.
- **Reply job trễ:** reply "còn chạy" đến sau khi sweep đã đóng lệnh thì bị bỏ và mất `operation_id`. Audit ghi `DEVICE_RESPONSE_TIMEOUT`; UI vẫn đọc lại.
- **Audit `Acknowledged`:** giữ trạng thái đó mãi, vì server không theo dõi job.
- **`ResponseTimeoutSeconds` tuỳ chỉnh được** (1..600, mặc định 30): giữ slot lâu hơn nếu tăng, trong khi UI chỉ chờ kết quả lệnh cố định 40 s (job chờ thêm tối đa 60 s).
- **Role cũ được ghi ngay:** role đang giữ `sys.hostname_edit`, `sys.time_edit`, `adc.config_edit`, `adc.enable` ghi được ngay sau phase 1; phase 3 liệt kê các role này. Admin không phải master-admin gán được các mã này.
- **`sys.time_edit` dùng chung:** mã này cũng là quyền của `set_time` và `set_ntp_servers`. Hai lệnh đó đóng nhờ catalog chứ không nhờ quyền.
- **`command_result` gửi tới mọi người xem thiết bị:** event có `detail` (`operation_id`, mã lỗi thiết bị), không có payload.

## Red Team Review

### Session — 2026-11-06

**Reviewers:** Security Adversary, Assumption Destroyer, Failure Mode Analyst (Standard tier: Fact Checker + Contract Verifier). Reports: `reports/from-code-reviewer-to-planner-red-team-*-plan-review-report.md`.

**Findings:** 28 raw → 15 after merging; all carried file:line evidence. 15 accepted (3 sub-points rejected, listed below).
**Severity:** 2 Critical, 5 High, 8 Medium.

| # | Finding | Severity | Disposition | Applied to |
|---|---|---|---|---|
| F1 | `set_ntp_servers` under non-HighRisk `sys.time_edit` hands over the device clock: lockout via `expired`/`future_timestamp`, replay once the clock is pre-2020 | Critical | Accept → user decision: keep `set_ntp_servers` closed, keep `sync_ntp`, add epoch check | plan.md, all phases |
| F2 | Live tool stops without read-back or revert on `unknown`/mismatch; no job path; "6/6 Executed" and final full compare cannot pass | Critical | Accept | Phase 3 |
| F3 | "POST returns 403" is false (HTTP 200 + `DEVICE_PERMISSION_REQUIRED`); UI outcome table incomplete | High | Accept: reuse the DO panel rule, shared helper | Phase 1, Phase 2 |
| F4 | Audit stores no payload for W/C (`SummarizePayload`) | High | Accept | Phase 1 |
| F5 | Opening silently activates codes already held by roles; plan listed codes, not roles | High | Accept | Phase 3 |
| F6 | `adc.set_channel {ai,name}` may reset `type`/`enabled`, and the server could not send them back | High | Accept: echo read values, compare whole channel record, spec gap | Phase 1, 2, 3 |
| F7 | Write lane holes: pending reply frees the lane mid-job; client `RequestedAt` up to +60 s; D/F would bypass the lane | High | Accept: server time for W/C, hold to max(`Deadline`, `ExpiresAt`+2 s), 60 s job hold, lane for every class except R/B | Phase 1 |
| F8 | `refresh` is a no-op while the card is reading; read buttons stay live during a write | Medium | Accept | Phase 2 |
| F9 | Job polling needs `sys.info_view` and a read slot; "realtime carries outcome only" is false | Medium | Accept (realtime detail kept, statement corrected) | Phase 2, plan.md |
| F10 | No write frequency limit | Medium | Accept: 10/min/device | Phase 1, Phase 3 pacing |
| F11 | Edits based on stale reads; concurrent editors overwrite | Medium | Accept: 60 s freshness + stale on `command_result` | Phase 2 |
| F12 | ADC name lets bidi/zero-width characters through; `client_id` link not pre-checked | Medium | Accept (hostname uniqueness rejected: spec does not require it) | Phase 1, Phase 3 |
| F13 | TDD assertion list wrong ("28→34", `set_time` test, "no publish", test adapter skips policy) | Medium | Accept | Phase 1 |
| F14 | Simulator lacks the reads the flow needs, and has no job delay | Medium | Accept | Phase 3 |
| F15 | Small false facts (`HMS_API_BASE`, "max 30 s", `applied_immediately:false` ignored) | Medium | Accept | All phases |

Rejected sub-points: hide `detail` from realtime events (low impact, statement corrected instead); hostname
uniqueness across devices (not a spec requirement); validating `operation_id` on the read (the device rejects bad
input, and it is a read).

### Whole-Plan Consistency Sweep

- **Files reread:** `plan.md`, `phase-01-server-config-writes.md`, `phase-02-config-edit-ui.md`, `phase-03-live-verification-docs.md`; all three phase files were rewritten after the review.
- **Decision deltas:**
  - 6 → 5 actions (`set_ntp_servers` closed);
  - 34 → 33 opened;
  - 403 → HTTP 200 + `DEVICE_PERMISSION_REQUIRED`;
  - write lane now covers every class except R/B, holds to max(`Deadline`, `ExpiresAt`+2 s), adds a 60 s job hold and 10 writes/min;
  - W/C use server time;
  - audit payload for the opened config actions;
  - `adc.set_channel` sends all four fields;
  - UI uses the DO panel's read-back rule, 60 s freshness, epoch check;
  - live tool auto-restores and has `--restore`; the live busy test was dropped (server-only, unit-tested).
- **Stale terms searched:** `set_ntp_servers`, `6 lệnh`, `34`, `403`, `28`, `tối đa 30 s`, `chỉ mang outcome`, `tool-env.js` + `HMS_API_BASE`. Remaining hits are intentional: the closed-list entries, the decision record, the spec gap and this table.
- **Unresolved contradictions:** 0.
