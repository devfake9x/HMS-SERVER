---
title: "Điều khiển DO relay qua MQTT (set_output, pulse) có đọc lại"
status: completed
created: 2026-11-06
mode: tdd
brainstorm: plans/reports/261106-0730-brainstorm-do-relay-control.md
contract: HMS-IDF/engineering-docs/api/mqtt-api.md §3, §6.4, §11, R4 (snapshot 2026-10-28)
blockedBy: []
blocks: []
supersedes: [261001-0030-full-hardware-mqtt-apis-c2 phase-03 (control.* part; it assumed 4 channels, the gateway has 6)]
builds_on: [261106-0100-mqtt-device-link]
---

# DO relay control over MQTT

Lát điều khiển đầu tiên trên link MQTT đã chạy: bật/tắt DO, kích xung, đọc lại trạng thái thật.

## Quyết định

- **Làm DO trước.** Trên gateway thật **chỉ DO1** (vòng nối về DI1); DO2–DO6 chỉ thử trên simulator. (brainstorm, 2026-11-06)
- **Mở `control.set_output` đi trước campaign firmware.** Spec §6.4 đã có contract; chỉ `pulse` có `SLICE_PASS`. Kết quả luôn được xác nhận bằng đọc lại. (user, red-team 2026-11-06)
- **`set_all` vẫn tắt** vì không atomic.
- **Danh sách kênh cho phép mặc định đóng.** Chỉ master-admin đổi được.
- **Trạng thái DO chỉ lấy từ reply `get_status` đã xác minh.** Xem nó cần quyền `gpio.do_view`.

## Phases

| # | Phase | Priority | Status | Depends on |
|---|---|---|---|---|
| 1 | [Server: lõi an toàn cho lệnh vật lý](phase-01-server-safety-core.md) | P1 | completed | — |
| 2 | [Server: trạng thái DO đã xác minh, quyền xem, audit](phase-02-output-state-and-audit.md) | P1 | completed | 1 |
| 3 | [UI: điều khiển trên panel DO, đọc lại có khoá kênh](phase-03-do-panel-ui.md) | P1 | completed | 1, 2 |
| 4 | [Kiểm chứng live và cập nhật tài liệu](phase-04-live-verification.md) | P1 | completed | 1, 2, 3 |

Mỗi phase theo TDD: viết test thất bại trước ("Tests first"), sau đó code cho tới khi xanh. Test cũ chỉ được đổi ở đúng những assertion mà phase liệt kê là thay đổi có chủ đích.

## Acceptance (toàn plan)

### Gateway thật
- Panel: DO1 ON → `Executed` → đọc lại DO1 `state:true` → ON. OFF tương tự.
- DO2 bị server từ chối với `DO_CHANNEL_NOT_ALLOWED`; sniffer xác nhận không có gì được publish.
- Pulse DO1 khi kênh đang `toggle` → `DEVICE_REJECTED` có lý do, sau đó đọc lại.
- Danh sách kênh rỗng thì không kênh nào điều khiển được.

### Simulator
- `set_output` và `pulse` trên DO1–DO6 chạy trọn vòng, có đọc lại.
- Lệnh không có reply → `DEVICE_RESPONSE_TIMEOUT`: kênh vẫn khoá, tự đọc lại, trạng thái chuyển stale.
- Restart API khi đang có lệnh → audit ghi `OUTCOME_UNKNOWN_AFTER_RESTART`.

### An toàn
- Lệnh vật lý mang `ttl_ms ≤ 10000`.
- Không đường UI nào gửi lệnh DO ghi mà không khoá kênh và không đọc lại.
- Trạng thái DO không ghi được từ heartbeat hay `POST /telemetry`, và không lộ cho người thiếu `gpio.do_view`.
- `gpio.do_write` là quyền rủi ro cao.

### Audit
- Mọi lệnh đã qua bước reservation có audit row ghi người gửi, detail và tóm tắt payload (với lệnh vật lý).
- Lệnh bị từ chối sớm được ghi log có cấu trúc.

### Test
Toàn bộ test .NET và frontend xanh; `vue-tsc` sạch.

## Kết quả (2026-11-06)

- 4/4 phase xong. Test: 483 unit + 109 integration .NET, 168 frontend, `vue-tsc` sạch.
- Review: [tester](reports/tester-report.md), [code-review](reports/code-review-report.md) (0 Critical; High + 7 Medium đã sửa; Low còn lại ghi ở tài liệu).
- Live: [reports/live-verification.md](reports/live-verification.md). Gateway: DO1 ON/OFF đọc lại đúng, DO2–DO6 không nhận lệnh nào (sniffer + bộ đếm thiết bị). Bước restart (8) chỉ kiểm bằng test vì dev dùng DB in-memory.
- Chỗ code khác plan: mục "As built" cuối mỗi phase.

## Ngoài phạm vi

- `set_all`; reboot/factory_reset/OTA; ghi network/mqtt/modbus/broker/adc/battery/config.
- Khung ghi chung: dry_run, poll job, confirm §7.
- Phase 3–6 của plan quyền `261005`.
- DI qua MQTT (spec C12 không có action).
- Đổi mode kênh (không có action MQTT).
- ACL/xác thực trên broker.

## Rủi ro còn lại (ghi vào tài liệu)

- **Broker lab cho anonymous đọc/ghi:** lệnh đã ký có thể bị bắt và replay sau khi thiết bị reboot (dedup mất), hoặc khi đồng hồ thiết bị chưa sync (bỏ qua kiểm tuổi). Cách khắc phục là ACL và xác thực trên broker (spec gap G-c).
- **Store trạng thái DO là in-memory:** sau restart phải đọc lại.

## Spec gaps (ghi lại, không tự suy)

- Giới hạn `duration_ms` của pulse: spec nói lấy qua HTTP. Server tự đặt trần 60 s như một chính sách an toàn.
- Số kênh DO tối đa: chỉ biết qua `control.get_status`.
- `set_output` chưa có trạng thái đóng trong campaign.

## Red Team Review

### Session — 2026-11-06

**Reviewers:** Security Adversary, Assumption Destroyer, Failure Mode Analyst (tier Standard: Fact Checker + Contract Verifier). Báo cáo đầy đủ nằm trong `reports/from-code-reviewer-to-planner-red-team-*-plan-review-report.md`.

**Phát hiện:** 30 thô, sau khi gộp còn 15 + 2 bị bác. Áp dụng 14, 1 điểm do user quyết (mở `set_output`).

**Mức độ:** 2 Critical, 7 High, 6 Medium.

| # | Finding | Severity | Disposition | Applied To |
|---|---|---|---|---|
| 1 | Danh sách kênh mặc định mở, `PUT /link` xoá được, chỉ cần `devices.manage`, không audit | Critical | Accept | Phase 1 |
| 2 | Chỉ đọc lại sau `Executed`; kết quả không chắc chắn mở khoá kênh, dẫn tới điều khiển 2 lần | Critical | Accept | Phase 1, 3 |
| 3 | TTL 120 s lớn hơn timeout 30 s, nên lệnh chạy trễ sau khi đã báo timeout | High | Accept | Phase 1 |
| 4 | Ghi DO qua `Record` xoá telemetry; event `status` bị coalescing ghi đè; trạng thái làm giả được | High | Accept | Phase 2, 3 |
| 5 | Data `get_status` (cần `gpio.do_view`) lộ cho mọi người xem | High | Accept | Phase 2, 3 |
| 6 | Trạng thái cũ hiển thị như mới; pulse đọc lại lúc relay vẫn ON; mất `pulseActive` | High | Accept | Phase 2, 3 |
| 7 | Restart làm lệnh kẹt ở `Published`; sweep không chạy khi broker mất kết nối | High | Accept | Phase 1 |
| 8 | Audit thiếu người gửi, payload, detail; lệnh bị từ chối sớm không được ghi | High | Accept (partial) | Phase 2 |
| 9 | UI không thấy quyền theo device role | High | Accept | Phase 2, 3 |
| 10 | `set_output` chưa đóng trong spec | Medium | Accept (user decision: mở) | plan.md, Phase 1 |
| 11 | `duration_ms` không có trần | Medium | Accept | Phase 1 |
| 12 | Modal C2 là đường điều khiển DO thứ hai, `value:1` bị chặn | Medium | Accept | Phase 3, 4 |
| 13 | `gpio.do_write` không thuộc nhóm rủi ro cao | Medium | Accept | Phase 1 |
| 14 | `POST /telemetry` không có CSRF, không kiểm quyền thiết bị | Medium | Accept | Phase 1 |
| 15 | Projection không có try/catch; câu "không sửa test cũ" không giữ được; thiếu chỗ gọi và file; DI giả | Medium | Accept | Phase 1–4 |
| R1 | Replay lệnh đã ký trên broker anonymous | High | Reject (hạ tầng, ngoài phạm vi; ghi là rủi ro còn lại) | plan.md |
| R2 | "Không có file ADR 260927" | Medium | Reject (sai: file có ở `docs/decisions/260927-*.md`) | — |

### Whole-Plan Consistency Sweep

- **Files reread:** `plan.md`, `phase-01-server-safety-core.md`, `phase-02-output-state-and-audit.md`, `phase-03-do-panel-ui.md`, `phase-04-live-verification.md`.
- **Decision deltas đã kiểm (9):**
  1. 3 phase thành 4 phase.
  2. Null/rỗng đổi nghĩa từ "mở" sang "đóng".
  3. Danh sách kênh chuyển sang endpoint riêng cho master-admin.
  4. Trạng thái DO chuyển từ telemetry `digital_outputs` sang store riêng.
  5. Event `status` thay bằng event `outputs`.
  6. Đọc lại sau mọi kết quả không chắc chắn, không chỉ sau `Executed`.
  7. TTL vật lý 10 s.
  8. Modal không còn gửi được lệnh DO.
  9. `set_output` được mở đi trước campaign.
- **Stale references đã sửa:** 6.
  - `digital_outputs` (từ cách chiếu cũ);
  - "null = không giới hạn";
  - "`PUT /link` nhận `allowedOutputChannels`";
  - "UI không tự parse reply / event status";
  - "test cũ không được sửa";
  - file `phase-01/02/03` cũ đã xoá.
- **Unresolved contradictions:** 0.
