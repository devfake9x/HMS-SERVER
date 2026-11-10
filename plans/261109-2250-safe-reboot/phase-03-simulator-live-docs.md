---
phase: 3
title: "Simulator, kiểm live và tài liệu"
status: completed
priority: P1
dependencies: [1, 2]
---

# Phase 3: Simulator, kiểm live và tài liệu

## Overview

Theo thứ tự:
1. Simulator trả lời `system.reboot` theo spec: dry-run trước mọi kiểm khác (L551); kiểm `confirm`; reply rồi rớt kết nối như reset chip; uptime về 0; DO theo §6.4; throttle 60 s.
2. Tool trong repo (`tools/device-reboot-check.js`) kiểm trước live, chụp trạng thái trước/sau, chạy kiểm âm tính không ký gì, dry-run và đối chiếu.
3. Chạy hết trên simulator (`hms_server_selftest/sim_001`), cả UI.
4. **Hoãn** (Validation Session 1, 2026-11-10): reboot `hms_toan_lab/gateway_870c` chưa chạy trong plan này. Lý do: không có người ở lab, và một session khác đang dùng bàn thử để chạy HIL. Kể cả `--preflight` chỉ đọc cũng không chạy trên gateway lúc này. Bước này thành việc tiếp theo, chỉ chạy khi có người ở lab và bàn thử rảnh; lúc đó vẫn theo đủ điều kiện dưới đây và chờ user nói "go". Phase 3 hoàn tất khi xong bước 1–3 và 5.
5. Liệt kê ai reboot được; viết tài liệu, journal.

## Requirements

### Simulator (`tools/mqtt-broker/spec-device-simulator.js`)
- **Hiện trạng:**
  - handler chỉ nhận `data.data` (:247-248), nên không thấy root `confirm`/`dry_run`;
  - `system.get_info` không có `uptime_seconds` (:115);
  - `control.get_status` không có `forced`/`enabled`/`gpioOk` (:71-73);
  - kết nối không có LWT (:207-211);
  - uptime tính từ `startedAt` là `const` (:213);
  - `SIM_SILENT` bỏ qua cả handler (:245), nên không mô phỏng được "đã reboot mà không reply".
- **Sửa:**
  - truyền envelope cho handler (`handler(data, envelope)`); handler cũ bỏ qua tham số thứ hai;
  - `system.get_info` thêm `uptime_seconds`, `hostname`; `startedAt` đổi thành biến;
  - mỗi kênh DO có `forced`, `enabled: true`, `gpioOk: true`; env `SIM_FORCED=<ch>:on|off,...` (ví dụ `2:on,4:off`);
  - LWT `{"state":"offline"}` retained trên `<base>/status/connectivity`, keepalive 10 s; khi kết nối publish `{"state":"online"}` retained;
  - `control.get_status` ngay sau boot trả `busy` + `retry_after_ms:1000` trong `SIM_DO_WARMUP_S` giây (mặc định 2), để kiểm đường thử lại của UI (red-team #15).
- **`system.reboot`** (§6.1 L604, §7 L1087, §3 L483, §4.2 L551) (red-team #5):
  1. `dry_run:true` → **trước mọi kiểm khác** (không kiểm confirm, throttle, dedup): reply `success:true`, `data:{would_apply:true}`. Các field khác của reply không có trong spec, nên simulator không thêm.
  2. root `confirm` khác `REBOOT-<device_id>` (device_id = segment cuối của `SIM_TOPIC_BASE`, như :115) → envelope error `confirm_required` (R3a).
  3. Payload khác `{}`: simulator nhận `delay_ms` 0..60000 theo spec (server không gửi).
  4. **Throttle:** reboot thật trong `SIM_THROTTLE_S` (mặc định 60) giây kể từ lần trước → envelope error `UNAUTHORIZED`. Throttle nằm trong RAM, mất khi boot (giả định, spec gap). `SIM_THROTTLE_SURVIVES=1` giữ nó qua boot, để một reboot thật thứ hai sau 60 s của server nhưng trong `SIM_THROTTLE_S` của simulator đi vào nhánh `UNAUTHORIZED` của UI (red-team #5).
  5. Lệnh hợp lệ: reply `success:true`, `data:{delay_ms}`. Sau `delay_ms`, **rớt như reset chip** (red-team #14): huỷ socket, không gửi DISCONNECT, không tự kết nối lại. Broker phát LWT sau khoảng 1,5 × keepalive. Chờ `SIM_BOOT_SECONDS` (mặc định 8), đặt lại `startedAt`, DO theo §6.4 L870-876 (kênh không force và pulse → OFF; kênh force → mức force; kênh force BẬT tắt trong lúc "boot"), rồi kết nối lại.
  - Đã kiểm (Validation Session 1): mqtt.js 5.16.0 mặc định `reconnectPeriod: 1000` (`tools/mqtt-broker/node_modules/mqtt/build/lib/client.js:67`, đọc ở checkout chính; worktree này chưa có `node_modules`). Vì vậy trong lúc "boot" phải đặt `reconnectPeriod: 0`, sau đó tự kết nối lại; kiểm bằng log.
  - Thời điểm `offline` ở simulator không chuyển được sang gateway; report ghi rõ.
- **Env thử lỗi:**
  - `SIM_REBOOT_NO_RETURN=1`: reply rồi không bao giờ lên lại → `NOT_OBSERVED`.
  - `SIM_REBOOT_NO_REPLY=1` (red-team #1): reboot mà không reply → cổng phía server vẫn giữ, reboot thứ hai bị từ chối trong lúc boot; hết hạn giữ thì row timeout được nâng lên `OBSERVED` (`reply=none`).
  - `SIM_SILENT=system.reboot` có sẵn (:14-15): không reply, không reboot.
- Header comment (:1-26) cập nhật danh sách env.

### Tool kiểm live (`tools/device-reboot-check.js`, mới)
- **Mẫu:** `tools/device-config-check.js`: login qua `tool-env.js` (:19-24), `HMS_API_BASE`, cookie + CSRF (:35-59), quy tắc "chưa gửi" (:62-67). Không in, không ghi credential. Server ký; tool không cần khoá lệnh. Tài khoản phải là master-admin (red-team #2).
- **Chặn chạy nhầm:**
  - mọi chế độ có gửi lệnh cần `--device <guid>` và `--confirm-live`; `--plan` chỉ in kịch bản;
  - trước mọi chế độ live, đọc `/api/v1/me/command-catalog`: `system.factory_reset`, `ota.apply`, `ota.remote_start` phải `available:false`, nếu không thì dừng (red-team #8).
- **Hình dạng probe** (red-team #8): mọi probe mong bị từ chối **trước khi ký** gửi **không có `confirm`, `dryRun:true`, payload `{}`**. Nếu cổng server hỏng, thiết bị chỉ nhận một dry-run, vô hại (L551).
- **Chế độ:**
  - `--preflight` (red-team #4, #7): đọc `/link` (online, `mqttDeviceId`), `system.get_info`, `control.get_status`, `system.get_time`, `mqtt.get_config`, `mqtt.get_status`, `ota.get_status`. Lưu `tools/device-reads/<ts>-reboot-before.json`. Trả PASS chỉ khi:
    - `clean_session === true` (thiếu field → FAIL);
    - |`epoch` − giờ server UTC| ≤ 5 s;
    - `config_trial.state === "none"`;
    - `ota.get_status` không cho thấy image đang chờ hay chờ xác minh. Shape chưa có trong spec: không đọc chắc được → FAIL (spec gap).
  - `--checks`: các probe không ký:
    - `system.reboot` không confirm → `CONFIRM_REQUIRED`;
    - `system.factory_reset` → `DENIED_UNTIL_ACTION_CONTRACT`.
    Không lệnh nào rời server. `CONFIRM_MISMATCH` và `COMMAND_PAYLOAD_INVALID` cần confirm hoặc payload, nên chỉ kiểm trên simulator, unit và integration.
  - `--dry-run`: một dry-run có confirm đúng; kỳ vọng `Executed`; theo dõi 20 s bằng `system.get_info` đã ký (red-team #9): uptime tăng đều; `/link` không `offline`.
  - `--reboot`: `--preflight` + `--checks` + dry-run + lệnh thật; poll `GET /commands/{id}` mỗi 2 s tới mã cuối (tối đa 180 s). Ghi mốc reply, `offline`, heartbeat đầu sau boot; **thời gian từ reply tới heartbeat đầu** (red-team #11); uptime trước/sau. Dùng trên simulator; trên gateway chỉ khi đường UI không dùng được.
  - `--probe-in-progress --watch <correlationId>` (red-team #8): đăng nhập trước khi bấm UI; ngay trước khi gửi, đọc `GET /commands/{correlationId}`; chỉ gửi khi mã là `DEVICE_REBOOT_SCHEDULED` hoặc còn trong cửa sổ giữ. Probe là dry-run có confirm đúng và payload `{}`, vì phải tới được cổng trong registry; lọt ra thì vẫn vô hại. Kỳ vọng `DEVICE_REBOOT_IN_PROGRESS`. Gửi muộn (mã đã là kết quả cuối) → ghi "skipped", không phải FAIL. Ghi cả hai mốc thời gian.
  - `--after <before.json>`: đọc `system.get_info`, `control.get_status` (thử lại `busy`/`LANE_FULL` tới 30 s), `system.get_time`. So với file trước:
    - uptime nhỏ hơn; firmware như cũ;
    - kênh không force OFF, hoặc BẬT có ghi chú "có thể do luật DO";
    - kênh force đúng mức force;
    - `ntp_synced` và độ lệch đồng hồ.
  - `--restore-do <before.json>` (red-team #7): nếu DO1 khác trạng thái trong file trước → một `control.set_output` DO1 về trạng thái đó, rồi đọc lại. Cần DO1 trong allowlist (đã có từ plan 261106-0735).
- **Không bao giờ gửi lại** `system.reboot`. Kết quả `unknown` → chỉ đọc `system.get_info`, in hướng dẫn kiểm tra tay.
- **Output:** report JSON vào `tools/device-reads/` (đã gitignore, `.gitignore:18`).

### Điều kiện trước khi xin "go" (gateway; bước đang hoãn, Validation Session 1) (red-team #4, #7)
- `--preflight` PASS (`clean_session`, ±5 s, không có phiên thử MQTT, không có image chờ).
- Chỉ một HMS-SERVER nói chuyện với gateway: tắt các server ở worktree khác; tắt `tools/mqtt-broker/hms-telemetry-bridge.js` (nó POST uptime cho thiết bị `870c…`) (red-team #9). Quan sát sniffer 60 s: không có `cmd/#` nào không khớp `id` trong audit của server này.
- User xác nhận: gateway không có luật DO `SET_HIGH`/`SET_LOW`; team firmware đã được báo, không nạp firmware lại trong phiên (bench dùng chung); có người ở lab.
- Khoá lệnh còn đúng: `system.get_info` trả `Executed`.
- **Sniffer** trên `hms_toan_lab/gateway_870c/#` suốt phiên, broker TLS lab như phiên 2026-11-06. Chỉ lưu `id`, `type.action`, `ttl_ms`, `dry_run`, có `confirm` hay không (user quyết 2026-11-09). Không lưu `sig` hay byte gốc. **Xoá log sau khi đếm.**
- **Giới hạn thời gian:** quá T+180 s sau reply mà UI chưa "Đã khởi động lại" → người ở lab kiểm tra tại chỗ (nguồn, đèn). Nếu cấp nguồn lại không đủ: web cục bộ `http://maitech-rtu.local/` hoặc gọi team firmware. Không gửi thêm lệnh reboot.

### Kịch bản
- **Simulator** (không đụng gateway):
  1. Chạy tool trên simulator **chưa sửa** → phải FAIL ở bước dry-run (`unknown_action`): bước "đỏ trước".
  2. Sửa simulator. Chạy `--preflight`, `--checks`, thêm các probe chỉ chạy trên simulator (`CONFIRM_MISMATCH`, `{delay_ms:2000}` → `COMMAND_PAYLOAD_INVALID`; đều `dryRun:true`), rồi `--dry-run`, `--reboot`, `--probe-in-progress` (trong lúc `--reboot` đang theo dõi), `--after`: tất cả PASS.
  3. `SIM_REBOOT_NO_RETURN=1` → `NOT_OBSERVED` sau khoảng 122 s; không lệnh nào được gửi lại.
  4. `SIM_REBOOT_NO_REPLY=1` → `DEVICE_RESPONSE_TIMEOUT`; reboot thứ hai lúc đang boot → `IN_PROGRESS`; hết hạn giữ → `OBSERVED` (`reply=none`).
  5. `SIM_SILENT=system.reboot` → `DEVICE_RESPONSE_TIMEOUT`; không thấy uptime giảm → row giữ nguyên; UI "chưa rõ", đọc `system.get_info` một lần.
  6. `SIM_FORCED=2:on,4:off` + bật DO1 trước → hộp xác nhận có "DO1 sẽ TẮT", "DO4 force TẮT", "DO2 force BẬT có thể TẮT trong lúc khởi động"; sau reboot DO1 OFF, DO2 ON, DO4 OFF.
  7. `SIM_THROTTLE_S=90 SIM_THROTTLE_SURVIVES=1`: reboot qua UI, chờ quá 60 s, reboot lần hai → `UNAUTHORIZED`; UI hiện đúng câu, đọc `system.get_info` một lần.
  8. UI: luồng đầy đủ, chụp màn hình hộp xác nhận và kết quả "Đã khởi động lại" (đã xác minh). Trong lúc theo dõi, gửi reboot từ tab khác → "Thiết bị đang khởi động lại". Đổi tên ADC trong lúc theo dõi vẫn được. Bật một relay khi hộp đang mở → hộp đóng.
  9. **Dừng server trong lúc theo dõi, khởi động lại** (red-team #10): với audit in-memory (dev hiện tại, `DependencyInjection.cs:65-68`) → `GET /commands/{id}` trả 404 `COMMAND_NOT_FOUND` và UI báo "Server đã khởi động lại…". Với Postgres → `DEVICE_REBOOT_OUTCOME_UNKNOWN`. Dev không có Postgres nên chỉ chạy nhánh in-memory.
- **Gateway** (hoãn, Validation Session 1; khi chạy thì sau "go"):
  1. `--preflight` PASS; `--checks` → 0 lệnh `system.reboot` hay `system.factory_reset` trên sniffer.
  2. `--dry-run` → `Executed`, uptime đã ký tăng, không `offline`.
  3. Mở sẵn `--probe-in-progress --watch` ở terminal khác. Reboot qua UI (gồm dry-run của UI). Không cần chờ 65 s: dry-run không tính vào throttle (L551) (red-team #5).
  4. Trong lúc UI "Đang khởi động lại…": chạy probe → `DEVICE_REBOOT_IN_PROGRESS`, hoặc "skipped" nếu gateway đã lên.
  5. UI "Đã khởi động lại" (đã xác minh); `--after before.json` PASS; ghi thời gian từ reply tới heartbeat đầu.
  6. `--restore-do before.json` → DO1 về trạng thái trước phiên.
  7. Chụp màn hình panel (che hostname và topic nếu có).
- **Sniffer:**
  - `system.reboot` = 2 dry-run + 1 thật (tool `--dry-run` và dry-run của UI; một reboot thật);
  - 0 hoặc 1 `control.set_output` (khôi phục DO1);
  - không lệnh ghi nào ngoài kịch bản; `ttl_ms` reboot = 10000.

### Ai reboot được (red-team #2)
- Truy vấn DB dev (API hoặc SQL chỉ đọc): danh sách tài khoản master-admin (người reboot được), và role cùng tổ chức đang giữ `sys.reboot`, kèm số membership. Các role này **không** reboot được sau plan này; chỉ có ý nghĩa nếu sau này nới quy tắc.
- Ghi `reports/affected-roles.md`: số tài khoản, tên role và số lượng; không email.

### Tài liệu
- **`docs/architecture-mqtt-device-link.md`:**
  - :61 "33 action" → 34, thêm `FlashActions` (red-team, fact check);
  - mục mới "Khởi động lại an toàn": lệnh, master-admin lúc gửi, luật `{}`, trần TTL và giới hạn của nó, cổng (giữ từ lúc publish, 60 s, mã từ chối), tracker (bằng chứng heartbeat, hạn, đuôi 10 phút, link mất, bỏ khi rebind/revoke), mã audit, `PayloadSummary` dry-run, realtime, UI (xác minh bằng uptime đã ký), kết quả live, thời gian boot đo được, spec gap, rủi ro còn lại; **ACL broker là điều kiện bắt buộc trước production**;
  - danh sách đóng (:66) và bảng "Lệnh vẫn đóng" (:461): bỏ `system.reboot`, giữ `factory_reset`, `ota.*` kèm lý do;
  - bảng lý do DO cũ (:166): thêm `device_reboot_scheduled`;
  - rủi ro phát lại (:241): thêm hệ quả reboot lặp và điều kiện lab.
- **`docs/architecture-device-mqtt-api-catalog.md`:**
  - :137 "33 action mở" → 34;
  - row reboot (:162) ghi trạng thái mở, chỉ master-admin, payload `{}`; `/me` báo TTL 60000 còn server ký 10000;
  - mục hộp xác nhận class F trong modal (:307-309): modal không còn class F.
- **`docs/project-roadmap.md` §5** (:102-115): thêm dòng reboot; :114 tổng 34 action mở; dòng "Còn đóng" bỏ reboot.
- **Journal:** `docs/journals/261109-safe-reboot.md`.
- **Plan:** cập nhật `plan.md`, các phase (status, "As built").

### Ghi chú plan cũ
- `plans/261001-0030-full-hardware-mqtt-apis-c2/plan.md`: đã thêm ở bước lập plan (comment + `supersededParts`). Không sửa gì khác ở plan cũ.

## Architecture

```
tool (HMS API, cookie + CSRF, master-admin) ─► HMS-SERVER ─► broker ─► simulator | gateway_870c
sniffer (B/#, redacted: id, type.action, ttl_ms, dry_run, confirm?) ─► broker; log deleted after counting
UI (browser, master-admin) ─► HMS-SERVER (same device)
evidence: tools/device-reads/*.json, reports/live-verification.md, reports/screens/*
```

## Related Code Files

- **Create:**
  - `tools/device-reboot-check.js`
  - `plans/261109-2250-safe-reboot/reports/live-verification.md`, `reports/affected-roles.md`, `reports/screens/*`
  - `docs/journals/261109-safe-reboot.md`
- **Modify:**
  - `tools/mqtt-broker/spec-device-simulator.js`
  - `docs/architecture-mqtt-device-link.md`, `docs/architecture-device-mqtt-api-catalog.md`, `docs/project-roadmap.md`
  - `plan.md`, các phase file
- **Delete:** không. Log sniffer xoá sau phiên; không nằm trong repo.

## Tests Before

- Repo không có test cho simulator. "Đỏ trước" ở phase này: chạy `node tools/device-reboot-check.js --device <sim guid> --confirm-live --reboot` với simulator chưa sửa; phải FAIL ở dry-run (`DEVICE_REJECTED`/`unknown_action`).
- `--plan` in đủ các bước và không gửi gì (log simulator: 0 lệnh).
- Bảng probe của tool có một kiểm tự động trong tool (`--self-check`): mọi probe "trước khi ký" đều không `confirm`, `dryRun:true`, payload `{}` (red-team #8).

## Implement

1. Viết `tools/device-reboot-check.js`; chạy "đỏ trước" trên simulator cũ.
2. Sửa simulator; chạy kịch bản simulator 2–9.
3. Liệt kê ai reboot được.
4. **Hoãn** (Validation Session 1): `--preflight` trên gateway và kịch bản gateway 1–7 chỉ chạy khi có người ở lab và bàn thử rảnh. Khi đó: preflight (chỉ đọc), xin user "go" kèm kết quả, sniffer đã lược, kịch bản 1–7.
5. Viết `reports/live-verification.md` cho phần simulator (bước, mã, thời gian, uptime trước/sau, DO trước/sau, thời gian "boot" của simulator). Ghi rõ phần gateway đang hoãn và vì sao. Đo thời gian boot của gateway thuộc bước đã hoãn.
6. Nếu bước gateway chạy sau này: bổ sung `reports/live-verification.md` (thời gian boot, số lệnh sniffer), rồi xoá log sniffer.
7. Tài liệu, journal, plan. Commit cục bộ khi user đồng ý; không push.

## Tests After

- Kịch bản simulator 2–9 PASS; tool report lưu trong `tools/device-reads/`.
- Gateway: **hoãn** (Validation Session 1). Khi chạy: kịch bản 1–7 PASS, hoặc ghi rõ bước nào khác kỳ vọng và vì sao (spec gap).
- Test phase 1 và 2 vẫn xanh. Phase này không sửa code `src/` hay `frontend/`; nếu live lộ lỗi thì quay lại phase tương ứng, viết test đỏ trước.

## Regression Gate

- `dotnet test tests/Hms.Server.UnitTests` và `dotnet test tests/Hms.Server.IntegrationTests` xanh.
- `cd frontend && npx vitest run` xanh; `npx vue-tsc --noEmit` sạch.
- `node tools/device-reboot-check.js --plan` và `--self-check` chạy không lỗi, không gửi gì.

## Success Criteria

- [x] Simulator: thường, không lên lại, reboot không reply, không reply không reboot, kênh force BẬT/TẮT, `UNAUTHORIZED`, server khởi động lại (in-memory): đúng kết quả; không lệnh nào bị gửi lại. (Kịch bản 2-7, 9 PASS; `reports/live-verification.md`.)
- [x] Probe không ký không publish gì (simulator); `--probe-in-progress` bị từ chối hoặc ghi "skipped". (Scenario 2b, 2d: 0 probe tới simulator.)
- [x] Tài liệu ghi đủ: lệnh mở, master-admin, mã audit, ai reboot được, spec gap, rủi ro còn lại, ACL trước production. Kết quả live ghi là "simulator; gateway hoãn". (`docs/architecture-mqtt-device-link.md`, `docs/architecture-device-mqtt-api-catalog.md`, `docs/project-roadmap.md`, `reports/affected-roles.md`; fact-check trong code review server.)

**Hoãn, không tính vào việc hoàn tất phase** (Validation Session 1, 2026-11-10): chỉ chạy khi có người ở lab và bàn thử không bị session khác dùng cho HIL. Các mục dưới đây **chưa làm**, không tick.
- [ ] Preflight gateway PASS trước khi xin "go". (hoãn)
- [ ] Gateway: UI "Đã khởi động lại" (đã xác minh); `--after` PASS; DO1 về trạng thái trước phiên; thời gian boot được ghi. (hoãn)
- [ ] Sniffer (đã lược) khớp kịch bản; `ttl_ms` reboot = 10000; log đã xoá. (hoãn)

## Risk Assessment

| Rủi ro | Khả năng × Ảnh hưởng | Giảm thiểu |
|---|---|---|
| Gateway không lên lại | Thấp × Cao | Preflight (không image chờ, không phiên thử); có người ở lab; giới hạn T+180 s; đường phục hồi ngoài cấp nguồn |
| Image hoặc cấu hình đang chờ được áp lúc boot | Thấp × Cao | `--preflight` FAIL thì không xin "go" |
| Phát lại hoặc giao lại lệnh reboot | Thấp × Cao | `clean_session: true`, đồng hồ ±5 s, sniffer không lưu chữ ký, xoá log |
| Probe "âm tính" lọt ra thiết bị | Thấp × Cao | Không confirm, `dryRun:true`, `{}`; kiểm catalog trước khi chạy |
| DO1 tắt làm hỏng thí nghiệm khác ở lab | Trung bình × Trung bình | User báo trước; hộp xác nhận nói rõ; `--restore-do` cuối phiên |
| Luật DO bật relay sau boot | Thấp × Thấp | User xác nhận không có luật; nếu có, báo như thông tin |
| Server khác hoặc bridge cùng nói chuyện với gateway | Trung bình × Trung bình | Tắt chúng; quan sát sniffer 60 s trước khi bắt đầu |
| Khoá lệnh đổi giữa chừng (đã xảy ra 2026-11-06) | Thấp × Trung bình | Kiểm `system.get_info` trước; hết giờ thì dừng, không gửi reboot |
| LWT không đến hoặc đến muộn | Trung bình × Thấp | Connectivity chỉ là bằng chứng phụ; uptime quyết định |
| Report chứa hostname/topic thật | Trung bình × Thấp | Chỉ lưu trong `tools/device-reads/`; tài liệu dùng chuỗi giả |

## As built

Nguồn: `reports/phase-03-implementation-report.md` (kèm "Fixes after code review"), `reports/live-verification.md`.

**Chỗ code khác plan**
- **Broker:** `tools/mqtt-broker/broker.js` (Aedes) không dùng được với server: Aedes chỉ nói MQTT 3.1.1, MQTTnet 5 kết nối bằng 5.0 (`unacceptable protocol version`). Thay bằng Mosquitto 2.1.2 cục bộ, chỉ loopback 127.0.0.1:8088, không persistence. Mosquitto lab (192.168.1.101, port 443) không bị chạm. Chưa sửa `src/` (open issue, xem follow-up (c) của `plan.md`).
- **`SIM_SILENT_REAL=system.reboot`** (không có trong kế hoạch): chỉ im lặng lệnh thật. `SIM_SILENT=system.reboot` cũng làm im dry-run nên UI dừng ở bước chạy thử (`dry_run_failed`), không tới lệnh thật.
- **Thứ tự kiểm ở simulator:** dry-run → throttle → confirm → payload (kiểm admission trước handler, §3 L483). Kế hoạch ghi confirm trước.
- **Kênh force:** simulator từ chối ghi vào kênh force bằng `ESP_ERR_INVALID_STATE` với `data.error:"channel_forced"` (spec L828) (review L7).
- **Busy:** simulator trả `{error:'ESP_ERR_INVALID_STATE', data:{error:'busy', retry_after_ms:1000}}` (review M3). Server ghép detail `ESP_ERR_INVALID_STATE: busy`.
- **Tool `tools/device-reboot-check.js`:** đọc giờ thiết bị từ `reply.receivedAt` của server (không tính độ trễ poll); kiểm "uptime giảm" có hiệu chỉnh đồng hồ và in cả quy tắc nghiêm (quy tắc UI); đăng nhập lại khi 401 (dev session mất sau restart API); `--expect rejected|server_restarted`.
- `refused()` của tool theo precedent `certainlyNotSent` (`device-config-check.js`): HTTP 4xx, `ContractUnavailable`, `TransportNotConfigured`, mã never-sent, và `Failed` với mã pre-sign đã biết. `Failed/MQTT_PUBLISH_UNCONFIRMED` và mọi kết quả khác được coi là đã gửi (follow row, không gửi lại) (review M2). `--self-check` có 9 ca.
- `--preflight` in firmware và cảnh báo về thay đổi force-OFF ngày 2026-11-08 (spec L850) (review L9).
- Tool retry `busy|LANE_FULL|UNAUTHORIZED` theo regex anchored `/(^|:\s*)(LANE_FULL|busy|UNAUTHORIZED)\b/i`.
- Ghi chú đường UI cho gateway: caveat firmware (2026-11-08) đã có trong `docs/architecture-mqtt-device-link.md`.
- `DEVICE_REBOOT_TOO_SOON` bị driver của tool gặp hai lần (49 s và 56 s sau reboot thật trước); server từ chối trước khi gửi (`ContractUnavailable`). Đây là bằng chứng thêm cho cổng 60 s.

**Đỏ trước:** simulator chưa sửa, `--dry-run`: `FAIL dry run ... DEVICE_REJECTED`, simulator log `unknown_action`. `--reboot` trên simulator cũ dừng ở `--preflight` (chưa gửi gì), không tới dry-run.

**Kết quả kịch bản simulator (`sim_001`)**

| # | Kịch bản | Kết quả | Bằng chứng chính |
|---|---|---|---|
| 1 | Đỏ trước | PASS (đỏ đúng như kỳ vọng) | `unknown_action` ở dry-run |
| 2 | Bình thường + probe | PASS | preflight 7/7; 6 probe bị từ chối trước khi ký, simulator thấy 0; dry-run `would_apply:true`; `SCHEDULED` 0 s → `OBSERVED` 10 s; probe → `DEVICE_REBOOT_IN_PROGRESS`; `--after` uptime 53 → 0 |
| 3 | Không lên lại | PASS | `NOT_OBSERVED` ở 124 s; 2 lệnh reboot (1 dry, 1 thật); khi simulator lên lại: `OBSERVED` `late_s=48` |
| 4 | Không reply | PASS (2 lần) | `DEVICE_RESPONSE_TIMEOUT` 31 s; probe ở 35 s → `IN_PROGRESS`; `OBSERVED` (`reply=none`) ở 124 s |
| 5 | Im lặng, không reboot | PASS | `DEVICE_RESPONSE_TIMEOUT` 33 s; row giữ qua 122 s; uptime 151 (không reset) |
| 6 | Kênh force | PASS | DO1 ON → OFF; DO2 force ON giữ ON; DO4 force OFF giữ OFF |
| 7 | Throttle | PASS | reboot thứ hai ở +81 s: `DEVICE_REJECTED`/`UNAUTHORIZED`; dry-run ngay sau đó `Executed` (cổng đã nhả) |
| 8 | UI | PASS sau 2 lần sửa UI (controller, trình duyệt) | xem `reports/live-verification.md`, mục "Scenario 8"; ảnh trong `reports/screens/` |
| 9 | Server dừng và khởi động lại (in-memory) | PASS | `GET /commands/{id}` → 404 `COMMAND_NOT_FOUND`; tool kết thúc `server_restarted`. Nhánh Postgres (`OUTCOME_UNKNOWN`) chưa chạy |

Sau sửa review: chạy lại kịch bản 2 và 6 đều PASS (`OBSERVED` ở 10 s; uptime 163 → 0 và 56 → 0; `busy` đúng dạng spec được tool retry).

**Số đo thời gian (chỉ simulator, không dùng cho gateway)**
- Simulator: reply tới `offline` khoảng 2 s (socket đóng ngay, Mosquitto phát will ngay). Gateway thật có thể giữ phiên nửa mở, will đến sau 1,5 × keepalive: chưa đo được.
- Thời gian tới heartbeat đầu sau boot: 10 s (simulator). Thời gian boot gateway: chưa đo.

**Residual và giới hạn (ghi trong `docs/architecture-mqtt-device-link.md`)**
- Review L5, L6 của server (xem phase 1).
- Tool: chế độ live không có test tự động; chỉ `--self-check` (9 ca `refused()`, probe không ký có `dryRun:true`, payload `{}`).
- Simulator: thời gian boot và LWT không đại diện gateway; kênh force refusal theo spec L828; reply dry-run chỉ `would_apply:true`.
- `ota.get_status`: shape chưa biết (spec gap). Tool preflight FAIL nếu không đọc được trạng thái OTA; simulator trả `ota.state:"idle"`. Gateway có thể cần chỉnh khi biết shape.

**Số đo test**
- Không sửa `src/`, `tests/`, `frontend/` trong phase này. Số test giữ nguyên từ phase 1 và 2 (unit 640, integration 126, vitest 475).
