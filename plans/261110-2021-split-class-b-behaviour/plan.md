---
title: "Tách hành vi lệnh khỏi chữ cái class (class B không còn mặc định là relay)"
status: completed
mode: hard
tdd: true
created: 2026-11-10
branch: claude/mqtt-device-connection-36f705
brainstorm: plans/reports/brainstorm-261110-2019-split-class-b-behaviour.md
blockedBy: []
blocks: []
---

# Tách hành vi lệnh khỏi chữ cái class

## Vì sao

Catalog `mqtt-api-c80797d9d` có class B gồm cả relay (`control.*`) lẫn `sms.send`. Bốn chỗ trong server suy hành vi từ chữ cái class: lưu payload vào audit, đánh stale DO (2 chỗ), chọn lane. Hôm nay không rò rỉ vì row đóng bị từ chối trước khi có plan. Nhưng thêm một dòng vào allowlist là đủ để payload SMS vào `PayloadSummary`, DO bị đánh stale sai và lệnh bỏ qua lane ghi, không test nào đỏ.

## Thiết kế

`CommandDefinition` thêm ba thuộc tính bool, mặc định `false` (an toàn):

| Thuộc tính | Ý nghĩa | Catalog suy ra từ |
|---|---|---|
| `KeepsAuditPayload` | Payload được lưu vào `PayloadSummary` | `isRelay` hoặc action thuộc `ConfigWriteActions` ∪ `FlashActions` |
| `AffectsOutputs` | Lệnh có thể đổi trạng thái DO, phải đánh stale | class B và `type == "control"` |
| `UsesOutputLane` | Bỏ qua lane ghi (panel DO tự tuần tự hoá) | `isRelay` |

`isRelay` = class B **và** `type == "control"` **và** action thuộc `PhysicalOutputActions`. Điều kiện `type` là bắt buộc: `PhysicalOutputActions` cũng là danh sách mở row class B, nên nếu chỉ dựa vào nó thì thêm `sms.send` vào danh sách vẫn lưu payload và bỏ lane ghi (payload được ghi lúc đặt chỗ, trước khi `OutputCommandPolicy` từ chối).

`PinnedCommandCatalog.ToDefinition` là nơi duy nhất gán. Nơi dùng đọc thuộc tính:

| Nơi dùng | Trước | Sau |
|---|---|---|
| `EfCommandAuditStore.SummarizePayload` | class B/D/F hoặc `ConfigWriteActions` | `Definition.KeepsAuditPayload`; nhánh bọc `{dry_run, payload}` cho class F giữ nguyên |
| `DeviceOutputState` (sau reply) | class B | `Definition.AffectsOutputs` |
| `DeviceLinkHandlers` (sweep hết hạn) | class B | `Definition.AffectsOutputs` |
| `CommandLanes.IsWrite` (5 nơi gọi) | không phải R, không phải B | `IsWrite(CommandDefinition)`: không phải R và không `UsesOutputLane` |

Không đổi: TTL kẹp 10 s theo class (`SendDeviceCommandHandler`), `OutputCommandPolicy`, `ConfigCommandPolicy`, `command-catalog.json`, catalog hash, DB schema, shape của `/me/command-catalog`.

## Phases

| # | Phase | Status | File |
|---|---|---|---|
| 1 | Thuộc tính hành vi trên definition + catalog suy ra + test khoá | completed | [phase-01](phase-01-behaviour-properties-on-catalog.md) |
| 2 | Chuyển nơi dùng sang thuộc tính + docs | completed | [phase-02](phase-02-consumers-read-properties.md) |

Phase 2 phụ thuộc phase 1. Phase 1 tự nó không đổi hành vi nào.

## Acceptance

- [x] 34 action đang mở: `PayloadSummary`, stale DO, lane, TTL giống trước (test hiện có xanh, không sửa kỳ vọng; chỉ sửa cách dựng definition).
- [x] `sms.send` từ catalog thật: `KeepsAuditPayload`, `AffectsOutputs`, `UsesOutputLane` đều `false`, và vẫn `false` kể cả khi nó nằm trong `PhysicalOutputActions` (suy ra theo `type`, không theo danh sách mở); test ở từng nơi dùng chứng minh payload không vào audit, DO không stale, lệnh vào lane ghi.
- [x] Mọi phần tử của `PhysicalOutputActions` có `type == "control"` (test).
- [x] Test khoá: mọi row B/D/F có kỳ vọng hành vi tường minh; row B/D/F mới hoặc mới mở mà chưa khai báo thì đỏ.
- [x] `command-catalog.json` không đổi byte nào; hash `658e191a…` giữ nguyên; không migration.
- [x] Không còn đọc `CommandClass.Physical` ở audit store, `DeviceOutputState`, `DeviceLinkHandlers`, `CommandLanes`.
- [x] `dotnet build` 0 warning mới; toàn bộ unit + integration test xanh.

## Ngoài phạm vi

Mở action mới; R13/R15/R16; redaction theo trường; trần TTL cho SMS; UI; lane của `control.set_all`/`force_*`/`set_config` khi mở (mặc định sau plan này: lane ghi, plan mở DO nâng cao quyết định).

## Rủi ro chính

| Rủi ro | Giảm |
|---|---|
| Test dựng definition bằng tay mất hành vi relay vì mặc định `false`, hoặc vẫn xanh nhưng chứng minh ít hơn | Phase 2 bắt buộc lấy definition từ catalog thật cho relay, reboot và test audit; chỉ definition tổng hợp mới dựng tay |
| Sót nơi gọi `IsWrite` | Xoá overload nhận `CommandClass` để compiler bắt |

Đã kiểm: `/me/command-catalog` map tay sang DTO (`MeController.cs:60-71`), không nơi nào serialize `CommandDefinition`, nên thuộc tính mới không lọt ra API.

## Red-team

Báo cáo: [reports/red-team-plan-review.md](reports/red-team-plan-review.md). Đã áp dụng cả 6 phát hiện: `isRelay` có điều kiện `type` (F1), test audit lấy definition từ catalog (F2), relay dựng tay phải đủ ba cờ hoặc lấy từ catalog (F3), sửa danh sách test (F4), thêm dòng docs 337/453/476 (F5), bỏ bước kiểm DTO và test giá trị mặc định (F6).

## Kết quả (2026-11-10)

- Build: 0 lỗi; 3 warning có sẵn, không nằm trong file đã sửa (`ManagementApiTests.cs:366`, `GrantSerializationTests.cs:33,68`).
- Unit 710/710. Integration 147/147 với `Mqtt__Enabled=false` (cách chạy ghi ở `docs/architecture-mqtt-device-link.md`; thiếu biến này thì 76 test trượt vì CA path cũ trong user-secrets, không liên quan thay đổi). Script 33/33; `generate-command-catalog.mjs --check --accept-stated-count 98/12` khớp hash `658e191a…`.
- Review: [reports/code-review.md](reports/code-review.md), không có phát hiện critical/high. Đã sửa: M1 (thêm test ghi tên 8 action giữ payload audit, để mở thêm action ghi cấu hình như Wi-Fi/broker không tự lưu mật khẩu), L1 (doc `AffectsOutputs`: `false` không phải phía thận trọng; `system.factory_reset` chưa có đường đánh stale, plan mở nó phải quyết định), L2 (docs dòng 83), L3 (`Pulse` lấy từ catalog). Test: [reports/test-report.md](reports/test-report.md).
- Còn lại cho plan mở row B/D: redaction, lane, trần TTL (vẫn theo class), `OutputCommandPolicy`, và stale sau `factory_reset`.

## Plan liên quan

- `261110-1747-mqtt-catalog-sync` (completed): nguồn của việc này (Follow-up d). Không phụ thuộc ngược.
- Plan mở SMS / DO nâng cao (chưa có): sẽ dựa trên thuộc tính ở đây.
