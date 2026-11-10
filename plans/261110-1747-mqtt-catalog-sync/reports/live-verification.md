# E2E tab cũ sau re-pin catalog (local, 2026-11-10)

Kết quả: **PASS**, có một sai lệch so với kịch bản (mục 4) và một hạn chế của môi trường dev (mục 3).

## 1. Môi trường

- Mosquitto local `127.0.0.1:8088`; API `:5099`; Vite `:5173`; simulator `tools/mqtt-broker/spec-device-simulator.js` trên `hms_server_selftest/sim_001`; thiết bị `870c870c-870c-870c-870c-870c870c870c`.
- Không chạm broker lab `192.168.1.101:443`, không chạm `hms_toan_lab/#`.
- API "cũ": worktree tách ở `ed01757` (catalog `mqtt-api-83e93d77e`). API "mới": cây làm việc hiện tại (catalog `mqtt-api-c80797d9d`).
- Trình duyệt: pane tích hợp, đăng nhập bằng tài khoản seed dev.

## 2. Các bước và bằng chứng

| # | Bước | Quan sát |
|---|---|---|
| 1 | Mở trang thiết bị trên API cũ | Catalog 94 action, hash `b1d47a78…`, dropdown 12 subsystem (không có `tags`) |
| 2 | Dừng API cũ, chạy API mới, **không** tải lại tab | Simulator log vẫn 1 dòng (`sim connected…`) |
| 3 | Tab cũ gửi `status.services` | Request body mang `catalogVersion: mqtt-api-83e93d77e`, `catalogHash: b1d47a78…`. Response HTTP 200: `status: ContractUnavailable`, `code: CATALOG_STALE`, `correlationId: c2-mwaoqvxi-3awn6`, `keyGeneration: null` |
| 4 | Kiểm không ký, không publish | Dialog: "Command Not Sent to Device / Status: ContractUnavailable / Code: CATALOG_STALE". Log API: `Command status.services … refused before audit: CATALOG_STALE`. Simulator log vẫn 1 dòng. Log Mosquitto không có publish |
| 5 | Tải lại tab | Catalog `mqtt-api-c80797d9d`, hash `658e191a…`, 108 action, 13 subsystem (thêm `tags`), allowed-commands 34, `status.tags` đòi `tag.view` |
| 6 | Gửi lại `status.services` | `Published / MQTT_PUBLISHED`, correlation `c2-mwaotbik-hxav1`; simulator: `[12:49:45] CMD status.services id=c2-mwaotbik-hxav1` → `RES … success=false error=unknown_action`. UI: `Device outcome: Failed / DEVICE_REJECTED / unknown_action` |
| 7 | Gửi `system.ping` | `Published / MQTT_PUBLISHED`, correlation `c2-mwaou4a6-qfnzn`; simulator: `[12:50:22] CMD system.ping` → `RES system.ping success=true`. UI: `Device outcome: Executed / DEVICE_EXECUTED`, `{"status":"alive"}` |

## 3. Hạn chế môi trường dev (đã phải đi vòng)

Development dùng `UseEphemeralDataProtectionProvider` (`Program.cs:15`). Mỗi lần khởi động lại API, cookie phiên `hms.server.session` và token antiforgery mất hiệu lực. Vì thế ở bước 3, lần bấm đầu tiên trên tab cũ trả **401**, chưa tới cổng catalog.

Cách đi vòng: chạy JS trong trang để đăng nhập lại qua `fetch` (204) và bọc `fetch` chỉ để thay header `X-CSRF-TOKEN` cho `POST …/commands`. State của SPA (catalog cũ trong bộ nhớ) không bị đụng; request ở bước 3 do chính SPA tạo. Sau khi tải lại ở bước 5, lớp bọc không còn (`patched: false`).

Hệ quả: bước 3 chứng minh server từ chối catalog cũ trước khi ký, và UI hiển thị đúng mã. Nó **không** tái hiện nguyên vẹn trải nghiệm production (provider bền, phiên sống qua restart) vì không có môi trường đó ở local.

## 4. Sai lệch so với kịch bản

- Kịch bản đòi "cùng lệnh đọc chạy (`Executed`)". `status.services` không có trong simulator nên sau khi tải lại nó được ký, publish, tới thiết bị và bị thiết bị trả `unknown_action` (bước 6). Điều đó đủ chứng minh cổng catalog đã mở. `Executed` được chứng minh bằng `system.ping` (bước 7), không phải cùng lệnh.

## 5. Ghi nhận ngoài phạm vi

- Dialog C2 console hiện `CATALOG_STALE` dạng mã thô, không có câu "hãy tải lại trang" như thẻ đọc và panel DO. Chưa sửa.
- Trên API cũ, `GET /devices/{id}/outputs` trả 404. Chưa điều tra.

## 6. Dọn dẹp

Dừng: Mosquitto local, API, simulator, Vite do phiên này mở. Gỡ worktree tạm `ed01757`. Broker lab không bị chạm.
