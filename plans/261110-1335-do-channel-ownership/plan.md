---
title: "Panel DO nói đúng kênh nào ghi được (enabled, gpioOk, forced, owner)"
description: "Server giữ 5 field kênh của control.get_status; panel khoá nút kèm lý do; hộp reboot và preflight nêu kênh do luật giữ."
status: completed
priority: P2
effort: 9h
mode: tdd
created: 2026-11-10
branch: claude/mqtt-device-connection-36f705
brainstorm: plans/reports/brainstorm-261110-1329-do-channel-ownership.md
contract: HMS-IDF/engineering-docs/api/mqtt-api.md @ c80797d (đọc qua git show). L797 (field get_status, giá trị owner), L818 (pulse không đổi trong đợt owned_by_rule), L822 (shape từ chối), L827-831 (4 lý do), L839 (enabled/gpioOk/forced), L853 (force_clear + rule_level), L870-885 (DO khi khởi động; L883 luật SET_HIGH/SET_LOW), L1054-1057 (HIL). Catalog vẫn ghim 83e93d77e.
builds_on: [261106-0735-do-relay-control, 261109-2250-safe-reboot]
supersedes: []
blockedBy: []
blocks: []
tags: [mqtt, do-relay, reboot, frontend, simulator]
---

# Panel DO nói đúng kênh nào ghi được

Thiết bị đã báo mỗi kênh có `enabled`, `gpioOk`, `forced`, `owner`, `owner_id` (spec L797), nhưng server bỏ chúng
(`OutputChannelState`, `DeviceOutputState.cs:13`), nên panel cho bấm trên kênh chắc chắn bị từ chối. Plan này giữ 5
field (chỉ thêm, không suy diễn), khoá nút kèm lý do, dịch 4 lỗi trạng thái, và cho hộp reboot, preflight biết kênh
nào do luật giữ.

## Tên field (dùng đúng các tên này ở mọi phase)

| Thiết bị (reply `control.get_status`) | Server C# (`OutputChannelState`, `OutputChannelDto`) | JSON `GET /outputs` (camelCase mặc định, đã kiểm `ManagementApiTests.cs:654-657`) | TS `OutputChannelDto` |
|---|---|---|---|
| `enabled` | `Enabled` (`bool?`) | `enabled` | `enabled?: boolean \| null` |
| `gpioOk` | `GpioOk` (`bool?`) | `gpioOk` | `gpioOk?: boolean \| null` |
| `forced` | `Forced` (`bool?`) | `forced` | `forced?: boolean \| null` |
| `owner` | `Owner` (`string?`) | `owner` | `owner?: OutputOwner \| null` |
| `owner_id` | `OwnerId` (`string?`) | `ownerId` | `ownerId?: string \| null` |

Reboot panel và tool đọc **reply thô** của thiết bị (`reply.data`), nên đọc `owner_id`; chỉ `GET /outputs` dùng `ownerId`.

## Luồng dữ liệu

1. **Đọc:** reply `control.get_status` đã ký → `DeviceCommandResponseCorrelator.HandleAsync` (`DeviceLinkHandlers.cs:45-127`) → `DeviceReplyProjection.ApplyAsync` (`DeviceOutputState.cs:86-100`) → `ParseChannels` (:151-172, thêm 5 field) → `IDeviceOutputStateStore.Set` → `GET /api/v1/devices/{id}/outputs` (`DeviceLinkController.cs:38-55`, map sang `OutputChannelDto` :214) → `adminApi.deviceOutputs` (`adminApi.ts:258-260`) → `DeviceOutputsPanel.load` (:96-111) → `outputWriteLock(ch)` → nút khoá kèm lý do.
2. **Ghi bị từ chối:** thiết bị trả `success:false`, `error:"ESP_ERR_INVALID_STATE"`, `data:{error:"owned_by_rule",channel,rule_id}` (L822, L831) → `ToResult` (`DeviceLinkHandlers.cs:245-268`, **không đổi**) → `Failed`/`DEVICE_REJECTED`, Detail `ESP_ERR_INVALID_STATE: owned_by_rule`; reply lưu (`DeviceLinkHandlers.cs:115-122`) → `GET /commands/{id}` trả `reply.data` cho người gửi (`DeviceCommandsController.cs:99-118`) → panel `stateRefusal(detail, reply.data)` → câu tiếng Việt + mã gốc → đường đọc lại có sẵn (`readBackTwice`, `DeviceOutputsPanel.vue:150-156`, :214-221) → `load` → khoá hiện ra (nếu thiết bị gửi `owner`).
3. **Reboot:** `DeviceRebootPanel.prepare` đọc reply thô `control.get_status` (`DeviceRebootPanel.vue:73-75`) → `channelsOf` (`rebootPlan.ts:48-65`, thêm `owner`/`ownerId` từ `owner`/`owner_id`) → `relaysBeforeReboot` (:80-89, thêm `ruleHeld`) → `RebootConfirmBody.vue`; sau boot `checkAfterReboot` (:96-109) → ghi chú → `RebootResult.vue`.
4. **Preflight:** `tools/device-reboot-check.js` `snapshot` (:222-243) → `relaysOf` (:204-208, thêm owner) → `ruleHolders` (mới) → `preflightVerdict` (:246-257): FAIL kèm danh sách, hoặc `ASK` khi thiết bị không gửi `owner`.

## Quyết định (brainstorm đã duyệt 2026-11-10; mục đánh dấu "lấp chỗ trống" là chi tiết brainstorm không nói, có lý do)

1. **Server:** `OutputChannelState` thêm `Enabled`, `GpioOk`, `Forced`, `Owner`, `OwnerId`, tham số tuỳ chọn mặc định `null` (3 chỗ dựng cũ không đổi: `DeviceRebootFlowTests.cs:230`, `PhysicalOutputSafetyTests.cs:168`, `ManagementApiTests.cs:649`). Thiếu field hoặc sai kiểu → `null`, không suy diễn (`forced:true` không sinh `owner:"force"` và ngược lại). `owner` là chuỗi ngoài `none|force|level_rule|event_rule|trigger|unknown` (so khớp ordinal), hoặc kiểu khác chuỗi và khác `null` → `"unknown"`. `owner_id`: chuỗi giữ nguyên, số → `GetRawText()`, kiểu khác → `null`.
2. **API chỉ thêm:** `OutputChannelDto` thêm 5 property, luôn có mặt (`null` khi không biết). 4 field cũ và shape ngoài (`channels`, `readAt`, `stale`, `staleReason`) không đổi. Catalog JSON, hash, từ vựng quyền không đổi: owner là shape reply, không phải row §11.
3. **Thứ tự khoá** (Bật, Tắt, Xung và ô ms): (1) `gpioOk === false` → "GPIO không dùng được"; (2) `enabled === false` → "Kênh tắt trong cấu hình"; (3) `forced === true` hoặc `owner === "force"` → "Đang force"; (4) `owner === "level_rule"` → "Luật #`ownerId` giữ kênh" (thiếu `ownerId` → "Luật giữ kênh").
4. **Xung khoá trên kênh `level_rule`:** có chủ đích. Spec không nói pulse bị `owned_by_rule` (L818 còn ghi "`control.pulse` không đổi trong đợt này"), và luật đặt lại mức ≤ 1 s (L853). Câu hỏi spec 17; khi có trả lời chỉ đổi một điều kiện trong `outputWriteLock`.
5. **Không khoá theo owner** khi `owner` thiếu (firmware cũ) hoặc `unknown`: thiết bị quyết. `event_rule`, `trigger`: không khoá, ghi chú "Luật/trigger có thể đổi kênh sau lệnh tay".
6. **Khoá theo lần đọc cuối, kể cả khi trạng thái cũ (stale/offline)** (lấp chỗ trống). Lý do: khoá sai chỉ chặn một lệnh mà thiết bị có thể nhận, và nút "Đọc trạng thái" luôn có (`canRead`) để làm mới; mở sai thì thiết bị từ chối và UI dịch lỗi. Hai chiều đều vô hại, chọn cách đơn giản.
7. **Dịch 4 lỗi** `gpio_unavailable`, `channel_disabled`, `channel_forced`, `owned_by_rule`: lý do lấy từ đuôi `result.detail` (định dạng `error: data.error` của `ToResult`); id luật lấy từ `reply.data.rule_id` khi có. Mã gốc (`DEVICE_REJECTED: ESP_ERR_INVALID_STATE: owned_by_rule`) vẫn hiện, font mono. Không thử lại lệnh ghi.
8. **Đọc lại một lần:** đường đọc lại sau mọi kết quả thiết bị đã có (`operate` → `readBackTwice`); khi lần đọc đầu thành công thì chỉ có một `get_status`. Thêm duy nhất: Xung bị từ chối bởi 4 lỗi trên thì bỏ lần đọc sau khi hết xung (`DeviceOutputsPanel.vue:236-242`), vì relay không đổi (L827-831).
9. **Hộp reboot:** kênh `level_rule` có dòng riêng "DO6 do luật #7 giữ: sau khởi động về TẮT, rồi luật đặt lại mức (có thể BẬT)" và không nằm trong dòng "sẽ TẮT" (lấp chỗ trống: tránh hai dòng mâu thuẫn). Câu chung "Luật DO SET_HIGH/SET_LOW (nếu có)…" **vẫn hiện cả khi có `owner`** (lấp chỗ trống): `owner` chỉ nói ai giữ kênh lúc đọc; luật chưa giữ kênh nào vẫn có thể bắn ở lần đánh giá đầu sau boot (L883). "Không có relay nào đang BẬT" chỉ hiện khi cả kênh luật giữ cũng đang TẮT.
10. **Kết quả reboot:** relay BẬT sau boot trên kênh `level_rule` (theo lần đọc sau, hoặc theo danh sách trước) → ghi chú "DOn BẬT sau khởi động do luật #id", chip "DOn BẬT (luật #id)"; không tính là lỗi.
11. **Simulator** (cấu hình chỉ qua env, `spec-device-simulator.js:8`): `SIM_DO_LAYOUT=gateway` (DO1 ghi được; DO2/DO3 `gpioOk:false`; DO4/DO5 force TẮT, `owner:"force"`; DO6 toggle, `owner:"level_rule"`, `owner_id:1`, luật giữ mức BẬT), `SIM_DO_NO_OWNER=1` (không gửi `owner`/`owner_id`), `SIM_DO_DISABLED=<list>`. Từ chối mang `channel` (+`rule_id`). `control.pulse` không kiểm owner (L818; câu hỏi 17).
12. **Preflight:** thêm kiểm "no DO channel held by a rule or trigger" (FAIL kèm danh sách kênh `level_rule`/`event_rule`/`trigger`). Thiết bị không gửi `owner`, hoặc có kênh `unknown` → dòng `ASK` (bước xác nhận tay giữ nguyên, không FAIL) (`unknown` là lấp chỗ trống: không chứng minh được là không có luật). Luôn in một dòng `WARN` về luật chưa giữ kênh (L883). 2026-11-10, user chose: --reboot blocks on ASK unless --rules-confirmed; manual no-DO-rule confirmation stays unconditional; owner check is an extra guard.
13. **Live gateway ngoài phạm vi** (bàn thử đang chạy HIL). Follow-up: một lần đọc `control.get_status` (read-only) trên `gateway_870c` để biết 1.0.108 có gửi `owner` không.

## Phases

| # | Phase | Priority | Status | Depends on | Effort |
|---|---|---|---|---|---|
| 1 | [Server: 5 field kênh](phase-01-server-channel-fields.md) | P2 | completed | — | 2h |
| 2 | [Panel DO, hộp reboot, kết quả reboot](phase-02-panel-and-reboot-ui.md) | P2 | completed | 1 | 4h |
| 3 | [Simulator, preflight, tài liệu](phase-03-simulator-tool-docs.md) | P2 | completed | 1, 2 | 3h |

- **TDD:** mỗi phase viết test đỏ trước (lỗi biên dịch cũng tính là đỏ), rồi code tới khi xanh. Test cũ chỉ đổi ở đúng các assertion phase liệt kê.
- **File ownership:** phase 1 chỉ `src/`, `tests/`; phase 2 chỉ `frontend/`; phase 3 chỉ `tools/`, `docs/`, file plan (gồm `plans/261109-2250-safe-reboot/plan.md` và `plans/reports/brainstorm-261108-1700-mqtt-spec-coverage-and-requests.md`). Không phase nào chạy song song.
- Không đưa mã plan, số phase hay nhãn review vào comment code, tên test, commit message.

## Acceptance (toàn plan)

- **Server:** reply đủ field → projection đủ; thiếu field → `null`; `owner` lạ → `"unknown"`; `owner_id` số → chuỗi; 4 field cũ (`Channel`, `State`, `Mode`, `PulseActive`) không đổi; field sai kiểu không làm hỏng cả reply.
- **API:** `GET /outputs` có `enabled`, `gpioOk`, `forced`, `owner`, `ownerId` trên mỗi kênh (giá trị `null` khi không biết), không thiếu, không thừa property nào khác; shape ngoài không đổi.
- **Catalog:** `git diff src/Hms.Server.Infrastructure/Commands/command-catalog.json` rỗng; `CatalogTests` (spec commit `83e93d77e…`, `CatalogTests.cs:15`; hash `b1d47a78…`, :30) xanh, không sửa; docs ghi spec HEAD `c80797d`, catalog vẫn ghim `83e93d77e`.
- **Panel (simulator `SIM_DO_LAYOUT=gateway`):** chỉ DO1 bấm được; DO2/DO3 "GPIO không dùng được", DO4/DO5 "Đang force", DO6 "Luật #1 giữ kênh" (Xung cũng khoá nếu kênh ở pulse mode, có test vitest).
- **Không có owner (`SIM_DO_NO_OWNER=1`):** không kênh nào khoá theo owner; khoá theo `gpioOk`/`enabled`/`forced` vẫn áp khi thiết bị gửi các field đó.
- **`owned_by_rule`:** hiện "Luật #id giữ kênh: thiết bị không đổi relay" và mã gốc mono; đúng một `set_output` được gửi; một `get_status` đọc lại (khi lần đọc thành công); sau đó khoá hiện ra nếu thiết bị gửi `owner`.
- **Reboot:** hộp xác nhận nêu DO6 kèm id luật, câu chung về luật vẫn còn; kết quả ghi "do luật #id" và không tính lỗi. Preflight FAIL khi có kênh do luật/trigger giữ, in danh sách; `ASK` khi thiết bị không gửi `owner`.
- **Không hồi quy** (giữ nguyên, có test):
  - R4: lệnh có thể đã tới thiết bị giữ kênh khoá tới khi đọc lại (`DeviceOutputsPanel.vue:172-222`; test `locks the channel, reads back after the device acts, and only then shows ON`, `reads back even when the device outcome is unknown`, `keeps the channel locked and reads back after %s`).
  - Không tự gửi lại lệnh ghi.
  - Regex retry đọc `/(^|:\s*)(LANE_FULL|busy|UNAUTHORIZED)\b/i` giữ nguyên ở `useDeviceReboot.ts:75` và `tools/device-reboot-check.js:156`; 4 lý do mới không khớp regex (thêm ca vào `--self-check`).
  - Hộp reboot chỉ đóng khi relay thật sự khác (`relaysMatch`, `rebootPlan.ts:72-78`, không đổi): owner đổi một mình không đóng hộp.
  - Projection vẫn đánh stale sau `DEVICE_REJECTED` (`DeviceOutputState.cs:110-111`); `ToResult` không đổi.
- **Test:** .NET unit ≥ 640 + test mới, integration ≥ 126 + test mới (chạy với `Mqtt__Enabled=false`), vitest ≥ 475 + test mới; `vue-tsc --noEmit` sạch; `node tools/device-reboot-check.js --self-check` PASS. Số không giảm so với mốc đo ở bước 0 mỗi phase.

## Test matrix

| Lớp | Phủ |
|---|---|
| Unit .NET (`DeviceOutputStateTests`) | đủ field; thiếu field; owner lạ/kiểu khác; `owner_id` số/chuỗi/null; field sai kiểu không hỏng reply |
| Integration .NET (`ManagementApiTests`) | tên property JSON, `null` khi không biết, tập property đúng 9, shape ngoài không đổi; catalog không đổi (`CatalogTests` có sẵn) |
| Vitest | `outputLocks` (thứ tự khoá, ghi chú, dịch lỗi); panel (khoá + lý do, stale vẫn khoá, không owner, đọc lại một lần, không gửi lại, xung bị từ chối); `rebootPlan` (`ruleHeld`, ghi chú sau boot, `owner:"force"`); reboot panel (dòng luật, câu chung còn, kết quả, owner đổi không đóng hộp) |
| Tool | `--self-check`: `ruleHolders` (không owner → null; none/force → rỗng; level_rule/event_rule/trigger → có; unknown → ASK), regex busy với 4 lý do |
| E2E (simulator `hms_server_selftest/sim_001`) | bố cục gateway trên panel; không owner; `owned_by_rule` qua panel (một ghi, một đọc); hộp reboot và một reboot thật trên simulator; preflight FAIL/ASK/PASS. Từ chối `gpio_unavailable`/`channel_disabled` của simulator không bấm được qua panel (đã khoá): chỉ review code + vitest dịch lỗi |
| Gateway thật | Ngoài phạm vi. Follow-up: một `control.get_status` read-only |

## Tương thích ngược

- **Client cũ của `GET /outputs`:** chỉ thấy thêm property; không property nào đổi tên, đổi kiểu hay biến mất.
- **Firmware cũ (không `owner`, hoặc không cả `enabled`/`gpioOk`/`forced`):** field `null` → không khoá theo field đó; thiết bị vẫn từ chối, UI dịch lỗi; preflight in `ASK`.
- **FE mới với server chưa có phase 1:** field TS là tuỳ chọn → thiếu = không khoá, panel chạy như trước.
- **Dữ liệu:** store DO in-memory, không migration; sau khi deploy, lần `get_status` đầu điền field mới.
- **Catalog và quyền:** không đổi (ghim `83e93d77e`, hash `b1d47a78…`).

## Rollback

- **Phase 1:** revert commit. Field chỉ thêm; client cũ bỏ qua; FE phase 2 coi thiếu field là không khoá.
- **Phase 2:** revert commit frontend; server vẫn trả field thừa, vô hại.
- **Phase 3:** revert commit tool/docs. Simulator không chạy production. Bỏ kiểm owner của preflight thì quay về bước xác nhận tay (precondition cũ trong plan reboot).
- Không migration, không đổi catalog, không state mới ngoài các field trong store in-memory có sẵn.

## Ngoài phạm vi

- `control.force_set`/`force_clear`, `set_all`, `get_config`/`set_config` (chưa có trong catalog ghim; đồng bộ catalog còn chặn bởi §11 ghi số cặp lệch bảng).
- Sửa/xoá luật (HTTP `/api/do-rule/remove` của thiết bị, không qua MQTT).
- Server coi 4 lỗi trạng thái là "relay không đổi" để không đánh stale: không làm; giữ R4 thận trọng, lần đọc lại xoá stale.
- Chạy trên gateway thật.

## Spec gap (đưa user chuyển cho team firmware, không tự gửi; phase 3 nối vào `brainstorm-261108-1700-mqtt-spec-coverage-and-requests.md` §3)

17. `control.pulse` trên kênh có luật theo mức: bị từ chối `owned_by_rule` không? (L831 chỉ nêu `set_output`, `set_all`, `set_config`; L818 ghi pulse không đổi trong đợt này.)
18. `owner`/`owner_id` có từ firmware nào; 1.0.108 có gửi không?
19. Kiểu của `owner_id` (số hay chuỗi)?

## Rủi ro còn lại

| Rủi ro | Khả năng × Ảnh hưởng | Giảm thiểu |
|---|---|---|
| Firmware 1.0.108 không gửi `owner` | Cao × Thấp | Field optional; không khoá theo owner; thiết bị vẫn từ chối, UI dịch lỗi; preflight hỏi tay |
| Lần đọc cũ → khoá sai/mở sai | Trung bình × Thấp | Quyết định 6; "Đọc trạng thái" luôn có; đọc lại sau mỗi lệnh ghi (R4) |
| Khoá Xung khi thiết bị thật cho phép | Trung bình × Thấp | Câu hỏi 17; một điều kiện |
| Preflight PASS dù có luật chưa giữ kênh nào (luật bắn ở boot, L883) | Thấp × Cao | Dòng `WARN` luôn in; câu chung trong hộp reboot giữ nguyên |
| Gateway `gateway_870c` có DO6 `level_rule` (HIL L1054) → preflight FAIL ở đó | Cao × Thấp | Chủ đích: không reboot thử khi luật có thể bật relay. Muốn chạy thì xoá luật qua HTTP thiết bị trước (việc của người ở lab) |
| Hộp reboot mở đúng lúc luật đổi owner: danh sách luật cũ | Thấp × Thấp | Hộp chỉ đóng khi relay đổi (giữ nguyên); lúc xác nhận kiểm tuổi lần đọc ≤ 30 s (`DeviceRebootPanel.vue:25`, :93-97) |
| `ownerId` dài hoặc lạ | Thấp × Thấp | Đến từ reply đã ký; chỉ hiển thị dạng text (Vue escape) |

## Open questions / khác brainstorm

Không có câu hỏi mở cho user. Chỗ code thật khác brainstorm (plan viết theo code):

1. **API outputs có DTO riêng:** endpoint map `OutputChannelState` sang `OutputChannelDto` (`DeviceLinkController.cs:53`, :214), không serialize record. Phase 1 sửa cả hai.
2. **Đọc lại sau lỗi đã có:** `operate` luôn đọc lại sau mọi kết quả thiết bị (`DeviceOutputsPanel.vue:214-221`), tối đa 2 lần nếu lần đầu hỏng. Không thêm lần đọc; chỉ bỏ lần đọc sau xung khi xung bị từ chối bởi 4 lỗi (Quyết định 8).
3. **Preflight không có bước hỏi user:** `device-reboot-check.js` không hỏi gì; "user xác nhận không có luật DO" là điều kiện tay ở `plans/261109-2250-safe-reboot/plan.md:125` và `docs/architecture-mqtt-device-link.md:623`. Phase 3 thêm kiểm owner vào preflight, in `ASK` khi thiếu owner, và sửa hai dòng điều kiện đó.
4. **Simulator chỉ nhận env:** tên cờ là `SIM_DO_LAYOUT=gateway` (không phải `--do-layout`), cộng `SIM_DO_NO_OWNER=1`, `SIM_DO_DISABLED=<list>`.
5. **Câu chung về luật trong hộp reboot giữ cả khi có owner** và kênh `level_rule` tách khỏi dòng "sẽ TẮT" (Quyết định 9). Brainstorm chỉ nói "thiếu owner → giữ câu chung"; không mâu thuẫn, chỉ làm rõ.
6. **`unknown` ở preflight → `ASK`** (Quyết định 12). Brainstorm chỉ nói về panel cho `unknown`.
7. **Id luật trong thông báo lỗi** lấy từ `reply.data.rule_id` (người gửi đọc được reply, `DeviceCommandsController.cs:102-118`); `ToResult` không đổi.
8. **Simulator không từ chối pulse theo owner** (L818); panel vẫn khoá Xung theo quyết định.
9. **Dòng spec:** khối "DO khi khởi động" ở HEAD là L870-885 (brainstorm ghi L860/866-885); luật SET_HIGH/SET_LOW là L883.

## Kết quả (2026-11-10)

- **Phase:** 3/3 xong (phase 1 và 2 chạy song song: file tách biệt, hợp đồng JSON chốt trong plan). Báo cáo từng phase trong `reports/`.
- **Test** (mốc trước plan: unit 640, integration 126, vitest 475):
  - .NET build 0 lỗi, 0 cảnh báo; unit 658/658; integration 127/127 (`Mqtt__Enabled=false`); `command-catalog.json` không diff. [reports/tester-full-regression-report.md](reports/tester-full-regression-report.md)
  - Vitest 537/537 (2 lần); `vue-tsc` sạch; `vite build` OK.
  - `node tools/device-reboot-check.js --self-check` PASS (exit 0).
- **Review:**
  - Server + frontend: 0 Critical/High. M1 (mã lỗi cũ dính vào lỗi thời gian xung) và L1 (ghi chú reboot gán sai luật khi lần đọc sau nêu owner khác) sửa test-first; L4 thêm test khoá `enabled:false` và khoá luật khi stale. L2 (surrogate lẻ trong `owner` làm mất cả lần đọc: reply đã ký, giống `mode`) và L3 (`owner_id` số hiển thị hai kiểu: chờ câu hỏi 19) chấp nhận. [reports/code-review-server-frontend-report.md](reports/code-review-server-frontend-report.md)
  - Tools + docs: 0 Critical/High. M1 (owner lạ phải `ASK`), M3 (Xung của simulator từ chối GPIO/tắt/force), L1-L6 đã sửa. M2: user chọn (b)+(c) ngày 2026-11-10, xem Quyết định 12. [reports/code-review-tools-docs-report.md](reports/code-review-tools-docs-report.md)
  - Tester G3: thêm 4 ca vitest "lỗi trạng thái không phải busy, không thử lại đọc sau boot".
- **Live (simulator `hms_server_selftest/sim_001`):** S1-S4 trên UI PASS (controller, trình duyệt trong app); S5 preflight FAIL/ASK/PASS và một reboot thật PASS (agent phase 3). [reports/live-verification.md](reports/live-verification.md)
  - Chưa chạy lại live sau các sửa review của tools (Xung simulator, cổng `--rules-confirmed`): chỉ có `--self-check` và chạy handler simulator với MQTT giả.
- **Docs:** `architecture-mqtt-device-link.md`, `architecture-device-mqtt-api-catalog.md`; điều kiện gateway của plan reboot (xác nhận tay luôn bắt buộc); câu hỏi 17-19 ở brainstorm §3.D (chưa gửi).
- **Gateway thật:** không chạy (ngoài phạm vi).
- **Ngoài phạm vi, có sẵn trên `develop`:** đăng nhập chuyển tới `/overview` → "Không tìm thấy trang" (`LoginView.vue:49`).

## Follow-up

| # | Việc | Chủ | Điều kiện xong |
|---|---|---|---|
| (a) | Một `control.get_status` read-only trên `gateway_870c` (khi bàn thử rảnh): ghi có/không `owner`, `owner_id`, kiểu `owner_id` | User hoặc dev khi bàn rảnh | Kết quả ghi vào `reports/` của plan này; trả lời được câu 18, 19 |
| (b) | Gửi câu hỏi 17-19 cho team firmware | User chuyển | Trả lời ghi vào plan hoặc brainstorm spec |
| (c) | Nếu câu 17 trả lời "pulse không bị từ chối": bỏ điều kiện Xung trên `level_rule` | Dev | Một test vitest đổi; ghi lý do |
