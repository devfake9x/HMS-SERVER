# Độ hoàn thiện API MQTT thiết bị và đồng bộ catalog (2026-11-10)

Trạng thái: **đã duyệt** (user, 2026-11-10): đồng bộ catalog lên spec HEAD. Bước tiếp: `/ck:plan --tdd`.

Nguồn:
- Spec `HMS-IDF/engineering-docs/api/mqtt-api.md`: ghim `83e93d77e`, HEAD `c80797d` (2026-11-10). Chỉ đọc qua `git show`.
- Server: nhánh `claude/mqtt-device-connection-36f705` @ `ed01757`. `PinnedCommandCatalog.cs` (`EnabledActions`, `PhysicalOutputActions`, `ConfigWriteActions`, `FlashActions`), `command-catalog.json`, `scripts/generate-command-catalog.mjs`, `tests/Hms.Server.UnitTests/CatalogTests.cs`.
- Thay cho số liệu cũ ở `brainstorm-261108-1700-mqtt-spec-coverage-and-requests.md` §1 (khi đó mở 33; nay 34).

## 1. Trả lời: đã hoàn thành chưa?

**Chưa.** Server mở 34/108 lệnh §11 (31%). Khung giao thức xong.

| Phần spec | Server |
|---|---|
| §1-§5 envelope, HMAC, reply, timeout, operation | Xong |
| §7 `confirm`, `dry_run` | Xong |
| §10 heartbeat, connectivity, capabilities, events | Xong |
| §8 cấu hình RTU lớn theo khung | Chưa |
| §9 telemetry SIGN/ENCRYPT | Chưa (thiết bị chưa có key `tlm_*`) |
| R13-R15 chỉ nhận từ primary | Chưa cần (chỉ khi mở SMS/force) |

Lệnh mở (34): R 26, W 3, C 2, B 2, F 1. Đã kiểm live trên `gateway_870c` trừ `system.reboot` (simulator).

Firmware tự ghi `release-qualified = 0/14` (spec L53-89): phía thiết bị cũng chưa chốt release lệnh nào.

## 2. 74 lệnh chưa mở, theo lý do

| Nhóm | Số | Lệnh | Chặn bởi |
|---|---|---|---|
| A. Chưa có trong catalog ghim | 14 | `tags.list/get`; `modbus.get_rtu_devices/tags/live/diagnostics`; `sms.get/send_status/send/delete`; `control.force_set/force_clear/get_config/set_config` | §11 ghi "98/100 cặp", bảng 108 row → `generate-command-catalog.mjs` (`assertStatedCounts`) từ chối |
| B1. Đọc rủi ro thấp, có trong catalog | ~14 | `broker.*` đọc (9), `modbus.get_rtu_status/get_rtu_config/check_rtu_config`, `network.wifi_known_list/wifi_ap_clients`, `sms.get_status/storage_stats` | Không chặn; chưa làm. Modbus/broker list ở firmware `SLICE_BLOCKED_REVIEW` |
| B2. Đọc có thể lộ secret/riêng tư | ~6 | `config.list_domains/get`, `ota.remote_config_get`, `wifi_sta_config_get`, `wifi_ap_config_get`, `sms.list`, `wifi_scan_results` (cần `scan_start` C) | Câu hỏi spec 16; quyền riêng tư |
| C. Ghi/điều khiển | 34 | `network.*` ghi + Wi-Fi, `set_time/set_ntp_servers`, `mqtt.set_config/reconnect`, `broker.*` ghi, `config.set`, Modbus ghi, `battery.clear_alert/quick_start`, `control.set_all` | Câu 4, 5, 11; §8; `set_all` cố ý đóng |
| D. Class F | 3 | `system.factory_reset`, `ota.remote_start`, `ota.apply` | Câu 1-2; ACL broker |

Lệch quyền đang có thật: `status.tags` mở với `sys.info_view` (spec cũ); spec mới đòi `tag.view`. Người chỉ có `sys.info_view` vẫn đọc được tag.

## 3. Khác biệt §11 giữa `83e93d77e` và `c80797d` (đã đối chiếu từng row)

- Header bảng giống nhau. Không row nào bị xoá.
- **Đổi 1:** `status.tags` R, `sys.info_view` → `tag.view`.
- **Thêm 14:**

| Row | Class | Quyền | TTL | Confirm |
|---|---|---|---|---|
| `tags.list`, `tags.get` | R | `tag.view` | 120000 | — |
| `modbus.get_rtu_devices` | R | `mb_rtu.slave_view` | 120000 | — |
| `modbus.get_rtu_tags`, `get_rtu_live` | R | `mb_rtu.tag_view` | 120000 | — |
| `modbus.get_rtu_diagnostics` | R | `mb_rtu.config_view` | 120000 | — |
| `sms.get` | R | `sms.view` | 120000 | — |
| `sms.send_status` | R | `sms.send` | 120000 | — |
| `sms.send` | B | `sms.send` | 60000 | `SMS-SEND-<DEVID>` |
| `sms.delete` | D | `sms.delete` | 60000 | `SMS-DELETE-<DEVID>` |
| `control.force_set`, `force_clear` | B | `gpio.do_force` | 60000 | — |
| `control.set_config` | B | `gpio.do_config` | 60000 | — |
| `control.get_config` | R | `gpio.do_view` | 120000 | — |

- Quyền firmware mới (chưa có ở server): `tag.view`, `mb_rtu.slave_view`, `mb_rtu.tag_view`, `sms.send`, `sms.delete`, `gpio.do_force`, `gpio.do_config` (7). `CatalogTests` hiện ghim 46 quyền → 53.
- Câu đếm: bản ghim nói 94 (khớp 94 row); HEAD nói 98 rồi 100, bảng 108.

## 4. Hướng đã cân nhắc

| Hướng | Ưu | Nhược | Kết luận |
|---|---|---|---|
| **Đồng bộ catalog** (chọn) | Sửa lỗ quyền `status.tags`; nền cho force DO, `get_config`, tags | Phải vượt câu đếm sai của spec | Chọn |
| Mở ~14 lệnh đọc rủi ro thấp | Không chờ firmware | Kiểm live cần bàn thử; simulator không có các lệnh này | Sau |
| Chỉ gửi câu hỏi spec | Không tốn công code | Không tiến được gì tới khi có trả lời | Nên làm song song (việc của user) |

## 5. Thiết kế đã duyệt

### Quyết định
1. **Câu đếm lệch:** thêm cờ `--table-count <N>` cho `scripts/generate-command-catalog.mjs`. Khi câu đếm trong prose khác số row bảng, bộ sinh vẫn lỗi, trừ khi `--table-count` bằng **đúng** số row bảng; khi đó in cảnh báo nêu số trong prose và số row. Không thêm field vào `command-catalog.json` (loader .NET chặt schema; hash chỉ phủ cặp). Ghi lệch vào docs/journal. Không sửa repo HMS-IDF.
2. **`status.tags`:** theo spec, `tag.view`; không migration tự cấp. Plan có báo cáo role bị ảnh hưởng (dev: 1 master-admin; chưa có production).
3. **High-risk grant** (`DevicePermissionCodes.HighRisk`, chỉ master-admin cấp): thêm `gpio.do_force`, `gpio.do_config`, `sms.send`, dù lệnh tương ứng còn đóng.

### Phạm vi
- `command-catalog.json` ghim `c80797d`: 108 row, 13 namespace, hash mới, `catalog_version` mới, `source_commit/blob/hash` mới.
- `EnabledActions` (34), `PhysicalOutputActions`, `ConfigWriteActions`, `FlashActions` không đổi. 14 row mới `DENIED_UNTIL_ACTION_CONTRACT` kể cả master-admin.
- `CatalogTests`: commit, hash, 108/13, số quyền, class counts, confirm templates.
- UI: hash lấy từ server (`/me`, `catalogVerification.ts`); phiên đang mở với hash cũ sẽ thấy lệch và tải lại (hành vi Zero-Drift có sẵn).
- Docs: `architecture-device-mqtt-api-catalog.md` (94 → 108, lệch câu đếm), `architecture-mqtt-device-link.md` (quyền `status.tags`), roadmap.

### Ngoài phạm vi
- Mở bất kỳ row mới nào (force DO, `get_config`, tags, Modbus theo trang, SMS): mỗi nhóm một plan.
- Mở nhóm B1/B2, §8, §9.

### Nghiệm thu
- Bộ sinh: chạy với `--idf` + `--table-count 108` ra catalog 108 row; không cờ hoặc cờ sai số → lỗi rõ; test của bộ sinh phủ 3 ca (khớp/không cờ/cờ sai). Đọc spec từ commit, không từ working tree đang sửa của HMS-IDF (kiểm cách bộ sinh đọc; `source_blob` phải khớp `git ls-tree c80797d`).
- `GET /me` trả catalog 108 row, hash mới; `allowed-commands` vẫn đúng 34 cho master-admin.
- Gửi `status.tags` với người có `sys.info_view` nhưng không `tag.view` → `DEVICE_PERMISSION_REQUIRED`; có `tag.view` → qua.
- 14 row mới: gửi bởi master-admin → `DENIED_UNTIL_ACTION_CONTRACT`, không ký, không publish.
- Admin không phải master-admin gán `gpio.do_force`/`gpio.do_config`/`sms.send` → `HIGH_RISK_REQUIRES_MASTER_ADMIN`.
- Test: unit/integration/vitest xanh, số không giảm (mốc 658/127/537); `vue-tsc` sạch.

## 6. Rủi ro

| Rủi ro | Giảm thiểu |
|---|---|
| Cờ `--table-count` thành thói quen, che trôi spec | Cờ phải bằng đúng số row; cảnh báo in ra; mỗi lần re-pin được review |
| Role mất quyền đọc tag | Báo cáo role bị ảnh hưởng; admin cấp `tag.view` |
| Phiên UI đang mở thấy hash lệch | Hành vi có sẵn: tải lại catalog |
| Bộ sinh đọc working tree HMS-IDF (đang có thay đổi chưa commit của session HIL) | Plan kiểm và ép đọc blob của commit `c80797d` |

## 7. Việc song song của user

Gửi danh sách câu hỏi spec (mục 1-19 trong `brainstorm-261108-1700-mqtt-spec-coverage-and-requests.md` §3) cho team firmware. Mục 8 (câu đếm §11) sẽ bỏ được cờ `--table-count`.

## 8. Bước tiếp

`/ck:plan --tdd` với báo cáo này.
