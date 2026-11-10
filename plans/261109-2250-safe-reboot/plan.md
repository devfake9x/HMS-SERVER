---
title: "Khởi động lại thiết bị an toàn qua MQTT (system.reboot)"
status: completed
priority: P1
effort: medium
mode: tdd
created: 2026-11-09
branch: claude/mqtt-device-connection-36f705
brainstorm: plans/reports/brainstorm-261109-2244-safe-reboot.md
contract: HMS-IDF/engineering-docs/api/mqtt-api.md, catalog ghim 83e93d77e, đối chiếu HEAD 9bfc168 (2026-11-09). Row system.reboot giống nhau ở hai bản (§6.1 L604, §7 L1087, §11 L1363 ở 9bfc168). Dùng thêm §0 R3/R4/R10, §2, §3, §4.2 L551 (dry-run), §5, §6.4 "DO khi khởi động" (chỉ có ở 9bfc168, L868-882) và L866, §10
builds_on: [261106-0100-mqtt-device-link, 261106-0735-do-relay-control, 261106-1040-monitoring-read-actions, 261106-2033-remote-config-system-adc]
supersedes: [261001-0030-full-hardware-mqtt-apis-c2 phase-03 system.reboot (confirm_token trong params), phase-05 tự điền token REBOOT/OTA-APPLY trong modal C2 và hộp xác nhận class F, phase-06 kiểm reboot HIL]
blockedBy: []
blocks: []
tags: [mqtt, reboot, frontend]
---

# Khởi động lại an toàn: `system.reboot`

Mở đúng một lệnh class F, **chỉ cho master-admin**. Server kiểm chặt trước khi ký, giữ cổng từ lúc publish, theo dõi
thiết bị lên lại để audit có kết quả cuối, và không bao giờ tự gửi lại. UI có nút riêng: hộp xác nhận liệt kê relay
sẽ đổi, chạy thử (dry-run) trước lệnh thật, và chỉ báo "Đã khởi động lại" khi uptime đã ký xác nhận.

## Luồng dữ liệu

1. **UI:** đọc `system.get_info` (uptime đã ký) và `control.get_status` → hộp xác nhận (gõ `REBOOT-<mqttDeviceId>`) → `POST /commands` `{dryRun:true, confirm, payload:{}}` → `POST` `{dryRun:false, confirm, payload:{}}`.
2. **Handler:** quyền `sys.reboot` hoặc master-admin (`SendDeviceCommandHandler.cs:107-117`) → **class F cần master-admin** (mới, red-team #2) → thiếu confirm → `CONFIRM_REQUIRED` (:119-122) → trần TTL (:129-135, sửa) → giờ server (:144-145).
3. **Adapter:** `CONFIRM_MISMATCH` (`HmsIdfCommandProtocolAdapter.cs:46-53`), `DRY_RUN_UNSUPPORTED` (:55-56), luật payload `{}` (:62-64, thêm luật), đọc khoá rồi ký (:66-84). Codec ghi `confirm`/`dry_run` ở root (`HmsIdfCommandCodec.cs:58-61`).
4. **Transport + registry:** `TryRegisterWrite` (`MqttDeviceCommandTransport.cs:78-86`; `DeviceLinkContracts.cs:324-348`) chạy cổng reboot **trong cùng bước atomic**: đang giữ → `DEVICE_REBOOT_IN_PROGRESS`; chưa đủ 60 s → `DEVICE_REBOOT_TOO_SOON`; nhận → **giữ cổng từ lúc publish** (red-team #1, #12) → publish.
5. **Reply** (`DeviceLinkHandlers.cs:44-116`), **sau khi `Remove` trả true** (red-team #10): lệnh thật thành công → đổi "giữ" thành "theo dõi", audit `DEVICE_REBOOT_SCHEDULED`, DO đánh dấu cũ, `command_result`; thiết bị từ chối → nhả cổng; `Acknowledged` → vẫn giữ.
6. **Theo dõi:** heartbeat (`DeviceLinkHandlers.cs:239-255`, uptime đọc từ chính message) → `DEVICE_REBOOT_OBSERVED`; sweep 2 s (`MqttDeviceLinkService.cs:26`, :121, :290-295) → `DEVICE_REBOOT_NOT_OBSERVED` (không kết luận khi link broker của server đang mất); 10 phút sau đó vẫn nâng lên `OBSERVED` nếu thấy uptime giảm; đổi binding/revoke → `DEVICE_REBOOT_OUTCOME_UNKNOWN`; server khởi động lại → `FailUnfinished` (`Program.cs:157-165`) → `OUTCOME_UNKNOWN` (chỉ khi audit ở Postgres).
7. **UI:** nghe `command_result`, poll `GET /commands/{id}` (`DeviceCommandsController.cs:79-116`) → đọc lại `system.get_info`, `control.get_status`, `system.get_time` → "Đã khởi động lại" chỉ khi uptime đã ký nhỏ hơn uptime đã ký trước đó.

## Quyết định (brainstorm đã duyệt 2026-11-09; red-team và quyết định user 2026-11-09; phần Claude tự quyết theo uỷ quyền ghi lý do)

1. **Mở đúng một row:** thêm `FlashActions = {("system","reboot")}` trong `PinnedCommandCatalog`; nhánh `CommandClass.Flash` của switch (`PinnedCommandCatalog.cs:236-242`, hiện rơi vào `_ => false` ở :241) dùng danh sách này. Ba row F còn lại vẫn `DeniedUntilActionContract`, có test: `ota.apply` (`command-catalog.json:814-822`), `ota.remote_start` (:847-855), `system.factory_reset` (:924-932) (red-team, fact check). Row reboot ở :1011-1021. Catalog JSON, hash, từ vựng quyền không đổi.
2. **Chỉ master-admin, kiểm lúc gửi** (red-team #2, user quyết 2026-11-09, khôi phục tiêu chí 2 của brainstorm):
   - Class F từ người không phải master-admin → `Failed`/`HIGH_RISK_REQUIRES_MASTER_ADMIN` (mã có sẵn, `DeviceAccessHandlers.cs:26`), trước khi giữ chỗ audit và trước khi ký. Thiếu `sys.reboot` vẫn `DEVICE_PERMISSION_REQUIRED` như cũ (`DeviceAccessPolicy.cs:31-44`). `allowed-commands` chỉ liệt kê `system.reboot` cho master-admin (`DeviceCommandsController.cs:67-72`).
   - Lý do: hiện chưa có endpoint HTTP tạo role hay membership (handler chỉ đăng ký ở `DependencyInjection.cs:121-125`), nên quy tắc này không tốn gì lúc này. Muốn nới phải có quyết định riêng.
   - So với chuẩn: K8s RBAC, AWS IAM, GCP IAM, Azure RBAC giới hạn ai được *cấp* quyền, rồi thêm điều kiện lúc chạy (AWS condition MFA, Azure PIM kích hoạt có thời hạn), chứ không bắt admin lúc chạy. Đây là quy tắc sản phẩm chặt hơn chuẩn, user đã duyệt.
   - Hệ quả: chỉ master-admin (người giữ allowlist relay, `DeviceLinkController.cs:86-87`) reboot được, nên reboot không còn là đường vòng qua allowlist (red-team #3).
3. **Payload chỉ `{}`** (red-team #1): luật trong `ConfigCommandPolicy` (cổng cho W/C/D/F, `ConfigCommandPolicy.cs:8-16`, :31-78): `payload.Count == 0`. Thiết bị dùng mặc định 2000 ms (§6.1 L604), nên server biết trước hạn và không có `delay_ms:0` (khi đó reply có thể không kịp đi). UI vốn gửi `{}`.
4. **Confirm và dry-run:** dùng cơ chế có sẵn, không sửa. Token dựng từ `binding.MqttDeviceId` (§7 L1093). **Dry-run** chạy trước confirm/dedup/safe-mode/throttle và không gọi handler; `would_apply:true` chỉ là mô tả xem trước (§4.2 L551, red-team #5). Dry-run thành công chỉ chứng minh chữ ký, khoá, đồng hồ và row được thiết bị nhận; lệnh thật vẫn có thể bị `confirm_required` hoặc `UNAUTHORIZED`.
5. **TTL:** class F dùng trần 10 s như class B (`MaxPhysicalCommandTtlMs`, `SendDeviceCommandHandler.cs:24`, :133); hiện row 60000 bị cắt còn 20 s (:20, :133-135). **Lý do duy nhất:** một lệnh reboot đến trễ tệ hơn một lệnh bị từ chối. Trần này **không** giới hạn phát lại khi đồng hồ thiết bị chậm (thiết bị nhận `ts` trước giờ của nó tới 60 s) hay chưa hợp lệ (bỏ kiểm tuổi lệnh, §3 L481), cũng không chặn broker giao lại lệnh khi `clean_session` tắt (R10 L414) (red-team #4). Các rủi ro đó do điều kiện live (Quyết định 13) và ACL broker xử lý. `/me/command-catalog` vẫn báo `ttlMs` 60000 (`MeController.cs:70`) vì đó là row của spec; server ký 10000.
6. **Lane ghi:** class F đã đi lane ghi (`CommandLanes.cs:10-11`; transport :78-86): 1 lệnh chờ mỗi thiết bị (`DEVICE_WRITE_BUSY`), 10 lệnh/phút. Không có reply thì chính lệnh đang chờ giữ lane tới hạn chờ reply 30 s (`DeviceLinkContracts.cs:332-333`; `MqttLinkOptions.cs:27`); mốc `ExpiresAt` + 2 s ở :372-375 đã qua khi TTL là 10 s (red-team, fact check).
7. **Cổng reboot, giữ từ lúc publish** (red-team #1, #12):
   - Kiểm **trong bước atomic của registry**: `TryRegisterWrite` nhận thêm hàm `admit`, gọi sau các kiểm "bận" và sau khi đếm tần suất (chưa ghi slot), trước khi đăng ký. Lệnh bị từ chối không tốn slot 10 lệnh/phút. Thứ tự khoá: lane → tracker; tracker không gọi ngược registry.
   - **Mọi `system.reboot`** (thật hay dry-run) khi thiết bị đang bị giữ hoặc đang theo dõi → `DEVICE_REBOOT_IN_PROGRESS`.
   - **Reboot thật** trong 60 s kể từ reboot thật trước được nhận → `DEVICE_REBOOT_TOO_SOON`, bất kể kết quả lần trước (khớp throttle 60 s của thiết bị, §3 L483). Dry-run không tính, vì preview chạy trước throttle (L551).
   - **Giữ:** reboot thật được nhận → giữ tới lúc nhận + 2 s + 120 s. Reply thành công → chuyển sang theo dõi. Chỉ hai trường hợp nhả sớm: thiết bị từ chối rõ (`DEVICE_REJECTED`), hoặc chắc chắn chưa gửi (mã NeverSent, `MqttDeviceCommandTransport.cs:105-109`). Reply mất, reply trễ (bị bỏ vì quá 30 s, `DeviceLinkHandlers.cs:53-55`), `Acknowledged`, publish không xác nhận, reply rơi ở hàng inbound đầy (`MqttDeviceLinkService.cs:303-304`): vẫn giữ tới hết hạn.
   - **Lệnh khác** (ghi cấu hình, relay, đọc) chạy như hiện nay. Lý do: chúng có lane và quy tắc đọc lại riêng (R4); chặn thêm sẽ khoá các thẻ tới 2 phút mà không làm reboot an toàn hơn.
8. **Theo dõi phía server** (`IDeviceRebootTracker`, singleton trong bộ nhớ, file mới):
   - **Bằng chứng chính:** heartbeat MQTT có `uptime_seconds` nhỏ hơn heartbeat trước. Uptime lấy **từ chính message** heartbeat, không từ `IDeviceTelemetryStore`, vì `POST /devices/{id}/telemetry` và `tools/mqtt-broker/hms-telemetry-bridge.js` cũng ghi store đó (red-team #9). Detail ghi `evidence=heartbeat`: heartbeat không ký (§10 L1333), nên audit chỉ là bằng chứng mức heartbeat; UI xác minh bằng uptime đã ký.
   - **Bằng chứng phụ:** connectivity `offline` rồi `online` sau mốc; chỉ ghi vào detail.
   - **Hạn:** reply + 2 s + 120 s → `NOT_OBSERVED`. Khi link broker của chính server đang mất, sweep không kết luận; có lại link thì hạn lùi tới ít nhất lúc đó + 30 s (red-team #11).
   - **Đuôi 10 phút:** sau `NOT_OBSERVED`, heartbeat có uptime giảm nâng row lên `OBSERVED` (chỉ từ `NOT_OBSERVED`, detail `late_s`) (red-team #11).
   - **Khi đang giữ mà chưa có reply thành công:** nếu thấy uptime giảm, hết hạn giữ thì row có mã "chưa rõ" (`MQTT_PUBLISHED`, `DEVICE_RESPONSE_TIMEOUT`, `MQTT_PUBLISH_UNCONFIRMED`, `DEVICE_ACCEPTED`) được nâng lên `Executed`/`OBSERVED` (detail `reply=none`); không thấy thì để nguyên row. Ghi lúc hết hạn giữ (sau sweep timeout 30 s) để không bị sweep ghi đè.
   - **Đổi binding sang topic khác và revoke:** xoá mốc uptime; bản ghi đang theo dõi kết thúc `OUTCOME_UNKNOWN` (detail `abandoned=rebind|revoked`) (red-team #9, #10).
   - **Điểm gắn:** heartbeat và connectivity trong `DeviceInboundMessageHandler` (`DeviceLinkHandlers.cs:239-269`); hạn trong `SweepExpiredAsync` (:118-146). Sweep chạy trên vòng housekeeping, song song với inbound (`MqttDeviceLinkService.cs:121` và :325): mỗi bản ghi chỉ kết thúc một lần dưới khoá của tracker.
   - **Tracker là tham số bắt buộc** của transport, correlator, inbound, handler binding và handler revoke (red-team #6), để không có đường nào âm thầm tắt cổng.
9. **Audit:** không thêm status, không migration EF. `Code` mang kết quả theo dõi.
   - Mã: `DEVICE_REBOOT_SCHEDULED` (detail `uptime_before`), `DEVICE_REBOOT_OBSERVED` (`evidence=heartbeat`, uptime trước → sau, số giây, connectivity, `late_s` hoặc `reply=none` nếu có), `DEVICE_REBOOT_NOT_OBSERVED`, `DEVICE_REBOOT_OUTCOME_UNKNOWN` (`abandoned=…`, `tracking=unavailable`, hoặc sau khởi động lại server với detail riêng).
   - **Ghi có điều kiện** (red-team #10): `ICommandAuditStore.TryCompleteAsync(plan, result, fromCodes)` chỉ ghi khi `Code` hiện tại nằm trong `fromCodes`. Hiện `RecordAsync` ghi đè mọi row chưa revoke (`EfCommandAuditStore.cs:149-160`).
   - **Dry-run hay thật** (red-team #13): với class F, `PayloadSummary` = `{"dry_run":<bool>,"payload":{…}}`, ghi lúc giữ chỗ (`EfCommandAuditStore.cs:85-100`, :221-237), nên mọi kết quả (kể cả timeout) đều phân biệt được. Bảng audit không có cột dry-run (`PersistenceEntities.cs:80-106`).
   - Người gửi đã có (`IssuedBy`, `EfCommandAuditStore.cs:98`).
10. **Realtime:** dùng lại `command_result` (`DeviceLinkHandlers.cs:38`, :174-208), cùng `correlationId`, phát mỗi lần code đổi. Lệnh được lên lịch thì đánh dấu DO cũ với lý do mới `device_reboot_scheduled` (event `outputs`) (red-team #14), vì projection bỏ qua mọi class khác B (`DeviceOutputState.cs:101-102`). UI vẫn poll vì event có thể rơi (`REALTIME_QUEUE_FULL`, :204-207).
11. **UI:** nút riêng trên trang thiết bị. Hộp xác nhận nói đúng §6.4 (red-team #3): kênh không force sẽ TẮT; kênh force TẮT giữ TẮT; kênh force BẬT có thể TẮT trong lúc khởi động và BẬT lại ở lần ghi line đầu tiên (L866, L876); luật DO `SET_HIGH`/`SET_LOW` chạy ở lần đánh giá đầu sau khởi động nên một relay có thể BẬT (L880). Modal C2 ẩn mọi lệnh class F (`class` từ `MeController.cs:57-71`) và bỏ đoạn gán `confirm_token` sai (`DeviceCommandModal.vue:138-143`). Chi tiết ở phase 2.
12. **Live:** chạy trên simulator `hms_server_selftest/sim_001`. Gateway `hms_toan_lab/gateway_870c` **hoãn** (Validation Session 1, 2026-11-10): không có người ở lab, và một session khác đang dùng bàn thử để chạy HIL. Bước gateway thành việc tiếp theo; chỉ chạy khi có người ở lab, bàn thử rảnh và user nói "go" (gateway mất kết nối, DO1 về OFF).
13. **Phát lại trên broker không ACL** (OQ3, user chấp nhận cho lab 2026-11-09, có điều kiện; red-team #4, #7): chỉ chạy gateway khi `mqtt.get_config` cho `clean_session: true` và `system.get_time` lệch giờ server không quá ±5 s. Sniffer chỉ lưu `id`, `type.action`, `ttl_ms`, `dry_run`, có `confirm` hay không; không lưu `sig` hay byte gốc; xoá log sau khi đếm. ACL broker là **điều kiện bắt buộc trước production**.

## Phases

| # | Phase | Priority | Status | Depends on |
|---|---|---|---|---|
| 1 | [Server: mở reboot, cổng và theo dõi](phase-01-server-reboot.md) | P1 | completed (1 tiêu chí Postgres mở, xem follow-up b) | — |
| 2 | [UI: nút Khởi động lại, hộp xác nhận, modal C2](phase-02-reboot-ui.md) | P1 | completed | 1 |
| 3 | [Simulator, kiểm live và tài liệu](phase-03-simulator-live-docs.md) | P1 | completed (gateway hoãn, không tính) | 1, 2 |

- **TDD:** mỗi phase viết test đỏ trước, rồi code tới khi xanh. Test cũ chỉ đổi ở đúng các assertion phase liệt kê là có chủ đích.
- **File ownership:** phase 1 chỉ `src/`, `tests/`; phase 2 chỉ `frontend/`; phase 3 chỉ `tools/`, `docs/`, file plan. Không phase nào chạy song song.

## Acceptance (toàn plan)

- **Catalog:** đúng `system.reboot` được mở thêm, tổng 34 lệnh mở. `system.factory_reset`, `ota.remote_start`, `ota.apply` vẫn `DENIED_UNTIL_ACTION_CONTRACT`, kể cả khi master-admin gửi.
- **Quyền** (red-team #2): chỉ master-admin reboot được. Người khác có `sys.reboot` → HTTP 200 + `Failed`/`HIGH_RISK_REQUIRES_MASTER_ADMIN`, không giữ chỗ audit, không ký; thiếu `sys.reboot` → `DEVICE_PERMISSION_REQUIRED`; `allowed-commands` chỉ có `system.reboot` cho master-admin; admin không phải master-admin không gán được `sys.reboot` (`HIGH_RISK_REQUIRES_MASTER_ADMIN`, có sẵn).
- **Trước khi ký:** thiếu confirm → `CONFIRM_REQUIRED`; confirm dựng từ id server hay sai → `CONFIRM_MISMATCH`; payload khác `{}` → `COMMAND_PAYLOAD_INVALID` (red-team #1). Không lệnh nào rời server trong các trường hợp này.
- **Cổng** (red-team #1, #12): từ lúc reboot thật được nhận vào lane tới khi nhả, mọi `system.reboot` → `DEVICE_REBOOT_IN_PROGRESS`; reboot thật trong 60 s → `DEVICE_REBOOT_TOO_SOON`; reply mất, trễ, `Acknowledged`, publish không xác nhận đều giữ cổng; lệnh bị từ chối không tốn slot (11 reboot bị từ chối, rồi `set_hostname` vẫn publish). Lệnh ghi cấu hình và relay vẫn qua.
- **Dry-run** (red-team #5, #13): reply `success`, không giữ cổng, không theo dõi; `PayloadSummary` có `"dry_run":true` với mọi kết quả. Trên simulator (gateway khi bước hoãn được chạy): uptime đã ký (`system.get_info`) không giảm, connectivity không `offline` trong 20 s sau đó.
- **Reboot thật:** audit `SCHEDULED` rồi `OBSERVED` (hoặc `NOT_OBSERVED`, có thể nâng lên `OBSERVED` trong 10 phút; hoặc `OUTCOME_UNKNOWN`). Không ghi đè row đã sang mã khác. Mỗi lần đổi code có `command_result`. DO bị đánh dấu cũ ngay khi lên lịch. Không lệnh nào được gửi lại.
- **TTL:** lệnh reboot ký `ttl_ms` 10000.
- **UI:**
  - nút chỉ hiện khi `allowed-commands` có `system.reboot`; khoá khi catalog chưa xác minh, link offline, thiếu `mqttDeviceId`, hoặc đang chạy;
  - hộp xác nhận nói đúng §6.4 (kênh không force TẮT; force TẮT giữ; force BẬT có thể TẮT trong lúc boot rồi BẬT lại; luật DO có thể BẬT relay) (red-team #3); đóng khi trạng thái relay đổi; lúc bấm xác nhận kiểm lại cổng và tuổi lần đọc (red-team #15); nút xác nhận chỉ bật khi gõ đúng `REBOOT-<mqttDeviceId>`;
  - luồng dry-run → lệnh thật → "Đang khởi động lại…" → kết quả cuối, xử lý được mọi mã ở lần poll đầu, `Acknowledged` và lỗi poll khi server khởi động lại (red-team #15);
  - "Đã khởi động lại" chỉ khi uptime `system.get_info` đã ký sau < trước; nếu không: "Thiết bị báo đã lên lại (chưa xác minh)" (red-team #9);
  - sau khi reboot đọc được bảng relay (có thử lại khi `busy`/`LANE_FULL`), nếu không thì kết quả riêng "Chưa kiểm được trạng thái relay" và acceptance không đạt (red-team #15);
  - modal C2 không còn lệnh class F; không còn chuỗi `confirm_token`.
- **Simulator:** UI báo "Đã khởi động lại" (đã xác minh); đọc lại thấy kênh không force OFF, kênh force đúng mức force; relay BẬT do luật được báo riêng, không tính là lỗi (red-team #3); các kịch bản lỗi (không lên lại, không reply, `UNAUTHORIZED`, server khởi động lại) cho đúng kết quả.
- **`gateway_870c` (hoãn, không tính vào việc hoàn tất plan; Validation Session 1):** khi chạy thì như simulator, cộng thêm: sniffer (đã lược chữ ký) chỉ thấy các lệnh trong kịch bản; DO1 trả về trạng thái trước phiên; thời gian khởi động được đo và ghi (red-team #7, #11).
- **Test:** toàn bộ .NET unit + integration và frontend vitest xanh, số test không giảm so với mốc đo ở bước 0 phase 1; `vue-tsc` sạch; `has-pending-model-changes` không có thay đổi.

## Kết quả (2026-11-10)

- **Phase:** 3/3 xong. Phase 3 hoàn tất không kèm bước gateway (hoãn, Validation Session 1). Tick chưa làm: 3 mục gateway trong phase 3.
- **Mở:** 1 tiêu chí phase 1 (audit, nhánh Postgres) chưa có bằng chứng chạy, xem follow-up (b).
- **Test:**
  - .NET unit 640/640 (3 lần); integration 126/126 (`Mqtt__Enabled=false`); `dotnet ef migrations has-pending-model-changes`: không thay đổi.
  - Vitest 475/475 (2 lần); `vue-tsc` sạch; `vite build` OK.
  - Mốc trước plan: unit 571, integration 117, vitest 325.
- **Review:**
  - Server và tool: 0 Critical/High. M1-M3, L1-L4, L10, L11 đã sửa test-first; L5, L6 chấp nhận. [reports/code-review-server-tools-report.md](reports/code-review-server-tools-report.md)
  - Frontend: 0 Critical/High. M1-M3, L2, L3, L9, L10 đã sửa; L1, L4-L8, L11 chấp nhận. [reports/code-review-frontend-report.md](reports/code-review-frontend-report.md)
  - Tester: [reports/tester-full-regression-report.md](reports/tester-full-regression-report.md). Lỗ hổng "ota.* gửi bởi master-admin" đã đóng sau báo cáo (3 ca trong `ManagementApiTests`).
- **Live (simulator `hms_server_selftest/sim_001`):**
  - Kịch bản 1-7 và 9 PASS; kịch bản 8 (UI) PASS sau 2 lần sửa UI. [reports/live-verification.md](reports/live-verification.md)
  - Gateway `hms_toan_lab/gateway_870c`: **hoãn**. Lý do: không có người ở lab, và một session khác đang dùng bàn thử cho HIL.
- **Role bị ảnh hưởng:** [reports/affected-roles.md](reports/affected-roles.md). Dev: 1 master-admin, 0 role giữ `sys.reboot`.
- **Chỗ code khác plan:** mục "As built" của từng phase.

### Điều kiện chạy gateway (sau này)

Chạy khi **cả ba** đúng: có người ở lab; bàn thử rảnh; user nói "go". Trước đó:
- `--preflight` PASS: `clean_session` true, đồng hồ ±5 s, không có MQTT trial, không có image chờ. Shape `ota.get_status` chưa biết nên preflight có thể FAIL (spec gap).
- Chỉ một HMS-SERVER nói chuyện với gateway; tắt `hms-telemetry-bridge.js`.
- User luôn xác nhận tay gateway không có luật DO (`SET_HIGH`/`SET_LOW`, theo mức hay sự kiện) hay trigger, dù thiết bị có gửi `owner` hay không (spec L883: luật chưa bắn vẫn bắn ở lần đánh giá đầu sau boot); preflight FAIL nếu kênh do luật/trigger giữ và `--reboot` dừng khi có dòng `ASK` tới khi có `--rules-confirmed` (plan `261110-1335-do-channel-ownership`); team firmware đã được báo.
- Kiểm firmware có thay đổi force-OFF ngày 2026-11-08 (spec L850), theo cảnh báo của tool.
- Sniffer đã lược trên `hms_toan_lab/gateway_870c/#`; xoá log sau khi đếm.
- Kịch bản gateway 1-7 trong phase 3; T+180 s; không gửi lại.

### Follow-up (phải làm để hoàn tất plan)

| # | Việc | Chủ | Điều kiện xong |
|---|---|---|---|
| (a) | Chạy gateway live (điều kiện trên) | User, người ở lab | Kịch bản gateway 1-7 PASS hoặc ghi rõ bước lệch và vì sao; DO1 về trạng thái trước phiên; mục gateway trong `reports/live-verification.md` đầy đủ; log sniffer đã xoá; 3 mục hoãn của phase 3 tick |
| (b) | Chạy nhánh Postgres của audit: `TryCompleteAsync`, `FailUnfinished` relational, kịch bản 9 với `OUTCOME_UNKNOWN` | Dev có Postgres | Test chạy trên Postgres xanh; tick tiêu chí phase 1 còn mở |
| (c) | Môi trường dev: (1) user-secrets Development trỏ CA path đã mất → integration cần `Mqtt__Enabled=false` (làm mới secret hoặc ép tắt trong `ManagementApiFactory`); (2) Aedes (`tools/mqtt-broker/broker.js`) chỉ nói MQTT 3.1.1, MQTTnet 5 dùng 5.0 → quyết định: đặt protocol version ở server, đổi broker dev, hoặc ghi Mosquitto là broker dev | User (quyết định), dev | Integration chạy không cần biến môi trường; quyết định (2) được ghi và thực hiện |
| (d) | Gửi câu hỏi spec cho team firmware: [brainstorm-261108-1700-mqtt-spec-coverage-and-requests.md](../reports/brainstorm-261108-1700-mqtt-spec-coverage-and-requests.md) (mục "Spec gap" của plan này) | User chuyển | Team firmware đã nhận; trả lời được ghi vào plan hoặc brainstorm |
| (e) | ACL broker trước production (điều kiện bắt buộc của Quyết định 13) | Team hạ tầng | ACL có hiệu lực trên broker production trước khi bật reboot cho production |

## Test matrix

| Lớp | Phủ |
|---|---|
| Unit .NET | catalog mở/đóng; master-admin lúc gửi; luật `{}`; adapter (confirm, dry-run, chữ ký); TTL; tracker (giữ, theo dõi, đuôi, link mất, bỏ, kết thúc một lần); registry (cổng trong bước atomic, không tốn slot, xen kẽ tất định); correlator + inbound (SCHEDULED → OBSERVED/NOT_OBSERVED, uptime từ message, ghi có điều kiện, rebind/revoke); `FailUnfinished` và `TryCompleteAsync` (in-memory) |
| Integration .NET | `/me` catalog; `allowed-commands` theo master-admin; HTTP quyền/confirm/payload; `PayloadSummary` dry-run (EF in-memory provider); DI với `enableLiveMqtt: true` (transport thật có tracker). Nhánh relational (Postgres) của `EfCommandAuditStore`: chỉ review, dev không có Postgres (red-team #6) |
| Vitest | luật hộp xác nhận (force BẬT, luật DO); luồng reboot (mọi mã, `Acknowledged`, lỗi poll, không gửi lại); kiểm uptime đã ký; thử lại khi `busy`/`LANE_FULL`; panel (đóng khi relay đổi, kiểm lại lúc xác nhận); `DangerConfirmDialog` slot; modal C2 ẩn class F; trang thiết bị chuyển event, làm mới link |
| E2E | simulator: thường, không lên lại, reboot không reply, không reply không reboot, kênh force BẬT/TẮT, throttle `UNAUTHORIZED`, server khởi động lại. Gateway (hoãn, Validation Session 1): một lần reboot qua UI + kiểm âm tính (không ký) + sniffer đã lược |

## Rollback

- **Phase 1:** bỏ `("system","reboot")` khỏi `FlashActions` → row về `DeniedUntilActionContract`; UI tự ẩn nút vì `allowed-commands` không còn lệnh. Không có migration; tracker chỉ trong bộ nhớ. Revert commit nếu cần bỏ hẳn.
- **Phase 2:** revert commit frontend; server vẫn an toàn khi không có UI.
- **Phase 3:** simulator và tool không ảnh hưởng production. Live: quá T+180 s sau reply mà chưa thấy thiết bị lên lại thì người ở lab xử lý tại chỗ (phase 3); không gửi thêm lệnh reboot. Cuối phiên trả DO1 về trạng thái trước phiên (red-team #7).

## Ngoài phạm vi

- `factory_reset`, `ota.*`, reboot nhiều thiết bị.
- Tự reboot sau khi đổi hostname.
- Giữ trạng thái cổng và theo dõi qua lần server khởi động lại (rủi ro còn lại; throttle 60 s của thiết bị là lớp thứ hai).
- `delay_ms` qua API hay UI: server chỉ nhận `{}` (red-team #1).
- Endpoint cho biết "đang theo dõi reboot" khi mở lại trang: server từ chối bằng `DEVICE_REBOOT_IN_PROGRESS`, UI hiện mã đó.
- Server tự gửi lệnh đọc đã ký để xác minh (server không tự phát lệnh); UI xác minh bằng uptime đã ký (red-team #9).
- Chế độ simulator phát lại lệnh hoặc `clean_session=0` (điều kiện live thay thế) (red-team #4).

## Spec gap (đưa user chuyển cho team firmware, không tự gửi; trùng mục 3 của `brainstorm-261108-1700-mqtt-spec-coverage-and-requests.md`)

- **Reply dry-run:** L551 chỉ nói có `would_apply:true`; các field khác chưa mô tả. (Thứ tự dry-run so với confirm/throttle đã có ở L551, red-team #5.)
- **Reply trước reboot:** §6.1 nói "lập lịch reboot" với mặc định 2000 ms, nhưng không nói rõ reply luôn đi trước khi khởi động lại.
- **Throttle sau reboot:** throttle 60 s có giữ qua reboot không (dedup thì mất, §2 L455).
- **Force BẬT qua `esp_restart`:** L866 nói khi dừng driver mọi line về OFF; một kênh force BẬT có tắt trong lúc khởi động lại không, và bao lâu (red-team #3).
- **Thứ tự khi lên lại:** heartbeat và connectivity (graceful `offline` hay chỉ LWT), thời gian tối đa để kết nối lại (đo trên gateway ở bước đang hoãn, Validation Session 1).
- **Tín hiệu xác nhận reboot:** chỉ có `uptime_seconds` (heartbeat `status.dump`, §6.1 L610, §10 L1318); không có boot count hay reset reason trong `system.get_info`.
- **`ota.get_status`:** field nào cho biết có image đang chờ (staged) hay chờ xác minh; cần cho bước kiểm trước live (red-team #7).
- **`<device_id>` của confirm:** có luôn trùng segment topic `B` (server dùng `binding.MqttDeviceId`) không, hay là `system.get_device_id`.
- **`UNAUTHORIZED`** dùng chung cho dedup, safe mode và throttle (§5 L571): server không phân biệt được.

## Rủi ro còn lại

- **Phát lại trên broker không ACL** (chấp nhận cho lab, Quyết định 13): sau reboot thiết bị quên dedup; nếu đồng hồ chưa hợp lệ, thiết bị bỏ kiểm tuổi lệnh (§3 L481); nếu `clean_session` tắt, broker có thể giao lại lệnh (R10). Ai bắt được một lệnh reboot đã ký có thể phát lại. Lab giảm bằng điều kiện `clean_session`/đồng hồ và sniffer không lưu chữ ký. Production cần ACL broker trước.
- **Trạng thái trong bộ nhớ:** server khởi động lại thì mất cổng và theo dõi; với Postgres row còn `SCHEDULED` thành `OUTCOME_UNKNOWN`, với store in-memory row mất hẳn (404). Throttle 60 s của thiết bị là lớp chặn thứ hai, nếu còn sau reboot (spec gap). Giả định một instance.
- **Heartbeat không ký:** client lạ trên broker có thể gửi uptime thấp, làm audit ghi `OBSERVED` (`evidence=heartbeat`) và nhả cổng sớm. Cổng vẫn còn mốc 60 s; UI chỉ báo "Đã khởi động lại" khi uptime đã ký xác nhận.
- **Uptime không giảm:** thiết bị vừa khởi động vài giây trước khi reboot có thể có uptime sau reboot lớn hơn lần trước → `NOT_OBSERVED` dù đã reboot; UI báo "chưa xác minh".
- **Lệnh ghi khác trong lúc reboot:** bị mất (R10 `clean_session`) → `DEVICE_RESPONSE_TIMEOUT`; UI đọc lại theo quy tắc chung.
- **Relay:** kênh không force TẮT, kênh force BẬT có thể chập TẮT, luật DO có thể BẬT relay; hộp xác nhận nói rõ. Thời gian chập của kênh force BẬT chưa biết (spec gap).
- **Nhánh Postgres của audit chưa có test chạy:** `TryCompleteAsync` relational và `FailUnfinished` relational chỉ được review (red-team #6).
- **Server, accepted sau review (2026-11-10):**
  - L5: broker link mất ngắn hơn một vòng sweep không tới `TakeDue(brokerConnected:false)`; heartbeat trong lúc mất bị mất. Heartbeat sau đó vẫn cho thấy uptime giảm.
  - L6: reboot thật mới trong đuôi 10 phút thay bản ghi đuôi của reboot trước; `NOT_OBSERVED` lần đầu không còn ghi được.
- **Frontend, accepted sau review (2026-11-10):**
  - L1: phrase rỗng (định danh MQTT bị xoá khi hộp đang mở) làm ô gõ biến mất; gate kiểm lại và server `CONFIRM_REQUIRED` chặn.
  - L4: mở lại hộp sau đọc cũ hơn 30 s không giải thích lý do.
  - L5: không kiểm lại danh sách relay giữa dry-run và lệnh thật (dry-run tối đa 40 s).
  - L6: `readWithRetry` lặp logic phân loại đọc của `useDeviceReads`; hai nơi có thể lệch nhau.
  - L7: `nudge()` cũng rút ngắn pause retry đọc lại (trong cửa sổ 30 s).
  - L8: DTO relay không có `forced`; đổi force cùng mức không đổi chữ trong hộp.
  - L11: cảnh báo "force BẬT nhưng sau khởi động đang TẮT" có thể báo nhầm vì line chỉ BẬT lại ở lần ghi đầu (spec gap L876).
- **Tool và simulator, ghi nhận:**
  - `tools/device-reboot-check.js` chế độ live không có test tự động; chỉ `--self-check`.
  - Simulator không đại diện thời gian boot và LWT của gateway; kênh force refusal theo spec L828; reply dry-run chỉ `would_apply:true`.
  - Preflight của tool FAIL nếu shape của `ota.get_status` chưa rõ (spec gap); gateway có thể FAIL preflight vì lý do này.
  - `App.vue` (fallback shell) vẫn liệt kê action class F nhưng không gửi `confirm`/payload nên server từ chối trước khi ký; không phải đường gửi thứ hai.

## Open questions

Đã đóng hết ngày 2026-11-09:
1. **Master-admin lúc gửi:** khôi phục tiêu chí 2 của brainstorm (Quyết định 2).
2. **Người thiếu `gpio.do_view`:** không còn ý nghĩa, vì chỉ master-admin reboot được và master-admin có mọi quyền.
3. **Phát lại trên broker không ACL:** chấp nhận cho lab, có điều kiện (Quyết định 13).
4. **Vị trí cổng:** cổng chạy trong bước atomic của registry, trước khi đếm slot và trước khi publish (Quyết định 7).
5. **Dry-run và throttle:** spec L551 đã trả lời; bỏ bước chờ 65 s.

## Red Team Review

### Session — 2026-11-09

**Reviewers:** Security Adversary (SA, 7 findings), Assumption Destroyer (AD, 9), Failure Mode Analyst (FM, 8); Standard tier with Fact Checker and Contract Verifier. Reports: `reports/from-code-reviewer-to-planner-red-team-*-plan-review-report.md`.

**Findings:** 24 raw → 15 after merging; all carried file:line or spec-line evidence. 15 accepted (user: apply all), 4 sub-points rejected (below).
**Severity:** 8 High, 7 Medium.

| # | Finding | Severity | Disposition | Applied to |
|---|---|---|---|---|
| 1 | Gate armed only on a success reply; lost/late/`Acknowledged`/unconfirmed/queue-dropped replies leave reboots ungated; `delay_ms:0` allowed (SA F3, FM F1, part of AD F4) | High | Accept: hold at publish (publish + 2 s + 120 s), success upgrades to tracking, only `DEVICE_REJECTED`/NeverSent clear it; 60 s min gap between real class F publishes; payload `{}` only. FM F8 (restart) → residual risk | plan.md, phase 1, 2, 3 |
| 2 | Send-time rule weaker than approved criterion 2 (SA F1) | High | Accept (user decision): master-admin at send for class F, `HIGH_RISK_REQUIRES_MASTER_ADMIN` before signing; `allowed-commands` master-admin only | plan.md, phase 1, 2, 3 |
| 3 | Dialog misstates §6.4: force-ON may drop during boot, DO rules may turn a relay ON; allowlist bypass (SA F2, AD F2) | High | Accept: wording, force-ON fixture, `checkAfterReboot` checks forced level and reports rule-driven ON as info; allowlist bypass resolved by #2 | plan.md, phase 2, 3 |
| 4 | 10 s TTL does not bound replay (clock lag/invalid, redelivery); live run creates the replay artifact (SA F4) | High | Accept: Decision 5 restated; live preconditions `clean_session`, ±5 s; sniffer redaction and deletion; read `system.get_time` after `OBSERVED` | plan.md, phase 2, 3 |
| 5 | Spec L551 already answers three "spec gaps"; simulator contradicts it (AD F1, FM F5) | High | Accept: gaps removed, OQ5 closed, 65 s wait dropped; simulator evaluates `dry_run` first, replies `{would_apply:true}`; dry-run wording; simulator throttle switch for the `UNAUTHORIZED` UI branch | plan.md, phase 2, 3 |
| 6 | Gate tests cannot fail: phantom race test, DI never builds the live transport, relational branch untested, missing affected tests (AD F3, FM F7) | High | Accept: tracker required everywhere; DI test with `enableLiveMqtt: true`; deterministic interleaving test; Postgres branch review-only; `PhysicalOutputSafetyTests.cs:253`, `DeviceOutputStateTests` listed | plan.md, phase 1 |
| 7 | Live run ignores staged firmware, MQTT trial, `clean_session`, rules, shared bench; no time limit or DO1 restore (FM F3) | High | Accept: preflight before "go", T+180 s limit, DO1 restore | plan.md, phase 3 |
| 8 | Live negative probes are real, non-dry-run commands (FM F4) | High | Accept: every probe refused before signing sends no `confirm`, `dryRun:true`, `{}`; tool checks catalog availability first | plan.md (test matrix), phase 3 |
| 9 | `OBSERVED` rests on unsigned heartbeats; tracker reads the shared store; rebind keeps baseline; bridge writes uptime (AD F5, SA F5) | Medium | Accept: uptime from the message, `evidence=heartbeat`, UI verifies with signed uptime, reset on rebind/revoke, bridge stopped, `--dry-run` uses signed `get_info` | plan.md, phase 1, 2, 3 |
| 10 | Completion writes are unconditional; start before `Remove`; contradictory revoke test; restart expectation wrong on in-memory store (AD F4, FM F1 part) | Medium | Accept: upgrade after `Remove` true, `TryCompleteAsync` compare-and-set, revoke/rebind → `OUTCOME_UNKNOWN`, restart scenario (phase 3 simulator step 9) per store | plan.md, phase 1, 3 |
| 11 | `NOT_OBSERVED` final; written while the server's own link is down; 120 s unmeasured (AD F9, FM F2) | Medium | Accept: 10 min tail upgrade from `NOT_OBSERVED` only; no conclusion while link down (deadline extended); boot time measured | plan.md, phase 1, 2, 3 |
| 12 | Refused probes burn write-rate slots (SA F7) | Medium | Accept: `admit` inside `TryRegisterWrite`, before the slot is counted; 11-refusals test | plan.md, phase 1 |
| 13 | Audit cannot tell a failed/unknown dry-run from a real reboot (SA F6) | Medium | Accept: class F `PayloadSummary` `{"dry_run":…,"payload":…}` at reservation | plan.md, phase 1 |
| 14 | DO state stays "verified" during the boot; simulator LWT timing does not transfer (AD F6) | Medium | Accept: `device_reboot_scheduled` stale reason; simulator drops like a chip reset, no early auto-reconnect (mqtt.js default UNVERIFIED) | plan.md, phase 1, 2, 3 |
| 15 | UI misses first-poll codes, `Acknowledged`, poll errors, stale online flag, device `busy`/`LANE_FULL`, stale dialog (FM F6, AD F7, AD F8) | Medium | Accept: branch by code, `followJobs:false`, swallow poll errors, 404 after restart, link refresh, bounded read retries + "relay chưa kiểm", dialog closes on `outputs`, re-check on confirm | plan.md, phase 2 |

Fact-check fixes also applied: order of the class F rows in `command-catalog.json`; lane hold is the 30 s pending entry, not `ExpiresAt` + 2 s; 33 → 34 in `docs/architecture-mqtt-device-link.md:61` and `docs/architecture-device-mqtt-api-catalog.md:137` (phase 3); `/me` still advertises TTL 60000 (Decision 5); extend `Every_opened_config_write_has_a_payload_rule` to `FlashActions` (phase 1).

**Rejected sub-points:**
- Simulator replay / `clean_session=0` mode (SA F4): out of scope; the live preconditions cover it.
- Server-initiated signed read to confirm a reboot (SA F5, AD F5): the server does not originate commands; the UI verifies with its own signed `system.get_info`.
- Postgres test for the relational audit branch (AD F3c): no Postgres in dev; marked review-only.
- Keeping gate and tracking across a server restart (FM F8): residual risk; the device's 60 s throttle is the second layer.

**User decisions (2026-11-09, in chat):** apply all 15; OQ1 → master-admin at send (Decision 2); OQ2 closed (moot); OQ3 accepted for the lab with conditions (Decision 13); OQ4 closed by #12; OQ5 closed by #5.

### Whole-Plan Consistency Sweep

- **Files reread:** `plan.md`, `phase-01-server-reboot.md`, `phase-02-reboot-ui.md`, `phase-03-simulator-live-docs.md` (all four rewritten after the review). Also edited: `plans/reports/brainstorm-261108-1700-mqtt-spec-coverage-and-requests.md` item 3/A (one bullet: dry-run order answered by L551; `delay_ms` not asked).
- **Decision deltas:**
  - payload `{delay_ms?: 0..60000}` → `{}` only;
  - any `sys.reboot` holder may send → master-admin only at send time; `allowed-commands` master-admin only;
  - three dry-run spec gaps → removed (L551); OQ5 and the 65 s wait → removed; simulator checks `dry_run` first and replies `{would_apply:true}`;
  - "kênh force giữ mức" → force-OFF stays OFF, force-ON may drop OFF during boot then returns, DO rules may turn a relay ON (reported as info);
  - tracker optional (`= null`) → required in transport, correlator, inbound, binding and revoke handlers;
  - uptime from `IDeviceTelemetryStore` → from the heartbeat message; `evidence=heartbeat`; UI verifies with signed `system.get_info`;
  - gate armed at the success reply and checked after lane registration → hold armed at admission inside `TryRegisterWrite` (before the slot is counted), plus a 60 s gap between real reboots (`DEVICE_REBOOT_TOO_SOON`);
  - `Start` before `Remove` → `Upgrade` after `Remove` returns true; unconditional `RecordAsync` → `TryCompleteAsync` compare-and-set;
  - `NOT_OBSERVED` final → 10 min tail upgrade; no conclusion while the server's broker link is down;
  - dry-run marker in success detail → class F `PayloadSummary` `{"dry_run":…,"payload":…}` for every outcome;
  - DO state left "verified" during boot → `device_reboot_scheduled` stale reason;
  - TTL 10 s "shortens replay" → "a late reboot is worse than a refused one" only; `/me` still shows 60000;
  - live: preflight (`clean_session`, ±5 s, no staged image, no MQTT trial, one server, no DO rules), redacted sniffer deleted after counting, T+180 s limit, DO1 restore, boot time measured; unsigned negative probes only;
  - restart scenario: `OUTCOME_UNKNOWN` (Postgres) vs 404 `COMMAND_NOT_FOUND` (in-memory dev store).
- **Stale terms searched** (hits left are intentional):
  - `delay_ms` (9: the `{}` rule and its rejection tests, the simulator's spec-side handling, "Ngoài phạm vi", the red-team table);
  - `0..60000` (1: simulator accepts the spec range);
  - `giữ mức` / `force giữ` (0); `IsTracking` (0); `Start(` (0); `GetLatest` (0); `dry_run=true` detail (0); `Role cũ` (0);
  - `65 s` (3: all say it was dropped);
  - `telemetry` (3: why the store is not used, the test that injects a higher store uptime, stopping the bridge);
  - `ExpiresAt` (3: the lane fact fix and the TTL test);
  - `= null` (1: the registry's optional `admit`, intentional);
  - `CONFIRM_MISMATCH` in live gateway steps (0; simulator, unit and integration only);
  - `gpio.do_view` (1: closed OQ2).
- **Contradictions found and fixed during the sweep:** 3. Red-team row 10 named "scenario 7" (now simulator step 9); rows 2 and 8 listed incomplete "applied to"; the inbound-drop citation pointed at `MqttDeviceLinkService.cs:44-45` instead of :303-304.
- **Unresolved contradictions:** 0. **Open questions:** 0.

## Validation Log

### Session 1 — 2026-11-10
**Trigger:** user chọn "Validate plan trước" sau red-team.
**Questions asked:** 4

#### Verification Results
- Tier: Standard. Mục Red Team Review đã có bằng chứng kiểm chứng (khoảng 140 claim, 3 reviewer), nên theo `validate-workflow` bước này chỉ giải các nhãn `[UNVERIFIED]` còn lại.
- Claims checked: 2 | Verified: 2 | Failed: 0 | Unverified: 0
  - mqtt.js 5.16.0 mặc định `reconnectPeriod: 1000` (`tools/mqtt-broker/node_modules/mqtt/build/lib/client.js:67`, checkout chính) → phase 3 giữ yêu cầu `reconnectPeriod: 0` lúc "boot".
  - Ctor `MqttDeviceLinkService` chỉ cần `MqttLinkOptions` hợp lệ; `Validate()` thoát khi `Enabled=false` (`MqttLinkOptions.cs:44-45`) → test DI ở phase 1 dùng option mặc định.

#### Questions & Answers

1. **[Scope]** Plan reboot (phase 1): tracker phía server có 2 phần thêm sau red-team — (a) "đuôi 10 phút": nếu đã ghi NOT_OBSERVED mà sau đó thấy uptime giảm thì nâng lên OBSERVED; (b) khi mất reply nhưng thấy thiết bị đã reboot thì nâng row timeout lên OBSERVED (reply=none). Giữ hay cắt cho gọn?
   - Options: Giữ cả hai | Cắt cả hai | Chỉ giữ đuôi 10 phút
   - **Answer:** Giữ cả hai.
   - **Rationale:** audit của lệnh nguy hiểm phải phản ánh đúng thiết bị có reboot hay không, kể cả khi mất reply hoặc gateway lên chậm qua 4G.
2. **[Risk]** Phase 3 yêu cầu: khi reboot thật gateway_870c, nếu quá 180 giây thiết bị chưa lên lại thì phải có người tại lab xử lý. Điều kiện này có đáp ứng được không?
   - Options: Có người ở lab lúc chạy | Nhờ team firmware trực | Không có, chỉ chạy simulator
   - **Answer:** Không có, chỉ chạy simulator.
   - **Rationale:** không có người ở lab thì một gateway không lên lại sẽ nằm đó tới khi có người ra; plan không chấp nhận rủi ro đó.
3. **[Assumption]** Trước lần reboot thật trên gateway_870c, có chờ team firmware trả lời 2 điểm spec chưa rõ không (reply có luôn đi trước khi reboot không; relay force BẬT có tắt trong lúc khởi động lại không)?
   - Options: Không chờ, live theo plan | Chờ firmware trả lời
   - **Answer:** không chọn option.
   - **Custom input:** "sesion khác đang hill.     bạn có thể làm gì khác?"
   - **Rationale:** một session khác đang chạy HIL trên bàn thử, nên gateway đằng nào cũng không dùng được lúc này. Kết hợp với câu 2: bước gateway hoãn hẳn; hai câu hỏi spec vẫn nằm trong danh sách gửi firmware (user chuyển).
4. **[Tradeoff]** Spec (§4.2 L551) nói dry-run chỉ chứng minh khoá, chữ ký, đồng hồ và row được nhận. Luồng UI có giữ bước dry-run trước lệnh thật không?
   - Options: Giữ dry-run | Bỏ dry-run
   - **Answer:** Giữ dry-run.
   - **Rationale:** bắt sớm lỗi khoá/đồng hồ/row trước lệnh thật, không tốn throttle của thiết bị.

#### Confirmed Decisions
- Tracker: giữ đuôi 10 phút và nâng `reply=none`, đúng như Quyết định 8.
- Dry-run: giữ trong luồng UI, đúng như phase 2.
- Gateway live: hoãn. Plan hoàn tất với bằng chứng simulator; reboot gateway thành việc tiếp theo, chỉ chạy khi có người ở lab và bàn thử rảnh.

#### Action Items
- [x] Phase 3: đánh dấu hoãn ở Overview bước 4, Implement 4–6, Tests After, Success Criteria (tách mục "Hoãn").
- [x] plan.md: Quyết định 12, Acceptance (tách simulator / gateway), Test matrix E2E.
- [x] Giải 2 nhãn `[UNVERIFIED]` (phase 1 test DI, phase 3 `reconnectPeriod`).

#### Impact on Phases
- Phase 1: không đổi phạm vi; chỉ thay nhãn `[UNVERIFIED]` của test DI bằng kết quả đã kiểm.
- Phase 2: không đổi.
- Phase 3: phần gateway (preflight, kịch bản 1–7, sniffer, đo thời gian boot, khôi phục DO1) hoãn; phase hoàn tất khi xong simulator, tool, "ai reboot được", tài liệu.

### Whole-Plan Consistency Sweep
- Files reread: `plan.md`, `phase-01-server-reboot.md` (đoạn test DI), `phase-03-simulator-live-docs.md`, `phase-02-reboot-ui.md` (tìm "gateway").
- Decision deltas checked: 2 (gateway live → hoãn; `[UNVERIFIED]` → đã kiểm).
- Stale terms searched: `gateway_870c`, `"go"`, `sniffer`, `thời gian boot`, `UNVERIFIED`. Các chỗ còn nhắc gateway đều đã mang nhãn "hoãn", hoặc nằm trong mô tả bước sẽ chạy sau này (điều kiện trước "go", Rollback, Risk).
- Grep `gateway`: plan.md 18, phase-01 1, phase-02 3, phase-03 20 chỗ.
- Reconciled stale references: 4.
  - plan.md: acceptance dry-run "simulator và gateway"; spec gap "phase 3 đo trên gateway".
  - phase-03: tiêu đề "Điều kiện trước khi xin go"; tiêu đề kịch bản "Gateway".
- Còn lại là chủ đích:
  - thủ tục cho lần chạy sau: điều kiện, kịch bản 1–7, Quyết định 13, Rollback, bảng rủi ro;
  - `gateway_870c` làm chuỗi ví dụ trong test phase 2;
  - log của red-team và validation.
- Unresolved contradictions: 0.
