# Diff §6 của 34 action mở: `83e93d77e` → `c80797d`

Ngày: 2026-11-10. Plan: `plans/261110-1747-mqtt-catalog-sync/`. Phase 3, báo cáo chỉ đọc.

## 1. Cách làm và lệnh đã dùng

Chỉ đọc object git của HMS-IDF, không đọc working tree, không ghi:

```
git --no-replace-objects -C D:/WORKSPACE/PROJECT/HMS-IDF show 83e93d77e4a433efce665d669b567b3acc51b735:engineering-docs/api/mqtt-api.md
git --no-replace-objects -C D:/WORKSPACE/PROJECT/HMS-IDF show c80797d9d5b817174cd2cfbcb6ee62effe503ff2:engineering-docs/api/mqtt-api.md
```

- Kiểm nguồn: sha256 của bản `c80797d` là `8cde6ab4…` và của `83e93d77e` là `ef41c113…`, khớp `source_hash` của hai lần ghim.
- §6 là từ `## 6. Catalog action và tham số` đến `## 7. Thao tác nhạy cảm`: `83e93d77e` dòng 184-316 (132 dòng), `c80797d` dòng 585-1087 (502 dòng).
- Với mỗi action: (a) dòng bảng đầu tiên có dạng `` | `type.action` | `` (dòng hàng động của action, không tính bảng biên nhận/bằng chứng sau đó); (b) các đoạn con §6 nhắc `` `type.action` ``. Hai bản được so sánh theo từng mục, khoảng trắng đã chuẩn hoá.
- Script so sánh nằm trong scratchpad của phiên, không nằm trong repo.
- Bảng biên nhận sau §6 (ví dụ `control.get_status`, `control.set_output`, `control.pulse` với cột `SLICE_PASS`/`HIL_PASS`) là lịch sử, không phải hợp đồng reply. Không tính vào "đổi".

## 2. Kết quả tổng

- **Dòng hàng động đổi: 7 action** (đúng 7 action red-team đã nêu): `mqtt.get_status`, `mqtt.get_config`, `system.get_info`, `status.tags`, `control.get_status`, `control.set_output`, `network.get_modem`.
- **Đoạn con nhắc action đổi (không đổi dòng hàng động): thêm 4**: `system.get_operations`, `status.services`, `control.pulse`, `system.reboot`. Các đoạn này đổi do nội dung chung (đoạn mô tả `mqtt.set_config`, `status.tags` theo trang), không đổi tham số hay reply của action.
- Phân loại 7 dòng đổi:

| Loại | Action |
|---|---|
| **Phá hợp đồng** (1) | `status.tags` |
| Chỉ thêm field (1) | `control.get_status` (thêm 5 field kênh và refusal `busy`) |
| Chỉ thêm field + ghi chú (1) | `mqtt.get_status` (thêm `config_trial`) |
| Nới tham số, chữ (1) | `control.set_output` (`value` nhận 0/1 phía thiết bị; reply không đổi field) |
| Chỉ chữ (3) | `mqtt.get_config`, `system.get_info`, `network.get_modem` |

## 3. Bảng 34 action mở

Cột "đoạn" = đoạn con §6 nhắc action có đổi. Cột "đọc reply" = dấu hiệu code/UI/tool đọc theo tên; số trong ngoặc là số file (không tính test, `bin`, `obj`, `node_modules`).

| # | Action | Dòng đổi? | Đoạn đổi? | Loại | Đọc reply (code, UI, tool) |
|---|---|---|---|---|---|
| 1 | `mqtt.get_status` | có | có | Chỉ thêm field `config_trial` và ghi chú lỗi cJSON | Không có trong `src`/`frontend/src`. `tools/device-reboot-check.js:315` (đọc field); `tools/mqtt-broker/spec-device-simulator.js:266` đã trả `config_trial` |
| 2 | `mqtt.get_config` | có | có | Chỉ chữ: `mode` là mode **đã lưu** | `tools/device-config-check.js:159`, `tools/device-reboot-check.js:314`. Không đổi field |
| 3 | `system.get_info` | có | có | Chỉ chữ: tham số `data` lồng bị bỏ qua. Field reply không đổi | `rebootPlan.ts:132`, `useDeviceReboot.ts:23,35,146` (uptime, firmware), `DeviceRebootPanel.vue`, `tools/device-reboot-check.js`. Không phá |
| 4 | `system.get_heap` | không | không | — | 0 |
| 5 | `system.ping` | không | không | — | 2 (tools) |
| 6 | `system.get_operations` | không | có | — | `useDeviceWrite.ts:89,138`, `configGate.ts:8`, `tools/device-config-check.js:105` |
| 7 | `status.services` | không | có | — | 0 |
| 8 | `status.dump` | không | không | — | 0 (`DeviceDetailView.vue:199` đọc `tag_count` từ reply `status.dump`, không đổi) |
| 9 | `status.tags` | **có** | có | **Phá hợp đồng**: reply mảng → trang `{schema:"status.tags.v1",generation,snapshotUptimeMs,items[],offset,limit,returned,total,max_limit,truncated}`; tham số `{offset?,limit?}`; mặc định `limit` 10 (§6 L927-929) | **Không có reader** (chỉ comment `PinnedCommandCatalog.cs:46`). Gateway 1.0.108 vẫn trả mảng. **Follow-up (c)** |
| 10 | `network.get_all_status` | không | không | — | 0 |
| 11 | `control.get_status` | **có** | có | Thêm field kênh `enabled`, `gpioOk`, `forced`, `owner`, `owner_id` (`owner` có sáu giá trị); thêm refusal `busy`. Field cũ giữ nguyên | `DeviceOutputState.cs:120` và các `OutputOwners` (`:43-54`), `DeviceLinkController.cs:36`, `MqttDeviceCommandTransport.cs:52-73`; UI `adminApi.ts:121-135`, `outputLocks.ts`, `rebootPlan.ts:16`. Đã xử lý field mới (plan `261110-1335`); không phá |
| 12 | `system.get_time` | không | không | — | 9 |
| 13 | `system.get_hostname` | không | không | — | 6 |
| 14 | `system.get_device_id` | không | không | — | 3 |
| 15 | `ota.get_status` | không | không | — | 4 |
| 16 | `network.get_priority` | không | không | — | 2 |
| 17 | `network.get_ethernet` | không | không | — | 2 |
| 18 | `network.get_modem` | **có** | có | Chỉ chữ: "Router modem projection", không có danh sách field (§6.5) | `DeviceNetworkCard.vue:13,18,55` (đã coi là projection không có field cố định), `tools/device-read-check.js:17`. Không phá |
| 19 | `network.wifi_sta_status` | không | không | — | 2 |
| 20 | `network.wifi_ap_status` | không | không | — | 2 |
| 21 | `battery.get_status` | không | không | — | 3 |
| 22 | `battery.get_alerts` | không | không | — | 3 |
| 23 | `adc.get_config` | không | không | — | 5 (`DevicePowerAdcCard.vue`, `adcData.ts`, `tools/device-config-check.js:156`) |
| 24 | `adc.get_channels` | không | không | — | 7 |
| 25 | `adc.get_status` | không | không | — | 2 |
| 26 | `adc.get_values` | không | không | — | 5 |
| 27 | `control.set_output` | **có** | có | Tham số: `value` nhận bool hoặc 0/1 phía thiết bị; `channel,state` (đọc lại sau ghi); từ chối có `data.error`. Field không đổi | `DeviceOutputsPanel.vue:242` (gửi bool), `OutputCommandPolicy.cs:34-38` (server vẫn chỉ nhận bool, không đổi), `tools/device-reboot-check.js:604`. Không phá |
| 28 | `control.pulse` | không | có | — | 3 |
| 29 | `system.set_hostname` | không | không | — | 5 |
| 30 | `system.set_timezone` | không | không | — | 6 |
| 31 | `system.sync_ntp` | không | không | — | 5 |
| 32 | `adc.set_channel` | không | không | — | 5 |
| 33 | `adc.toggle_channel` | không | không | — | 5 |
| 34 | `system.reboot` | không | có | — | 6 (`rebootPlan.ts`, `useDeviceReboot.ts`, `DeviceRebootPanel.vue`, `tools/device-reboot-check.js`). Không đổi field |

## 4. Hành động theo kết quả

- **`status.tags`: follow-up (c), không sửa code.** Server, UI và tool không đọc reply, nên không có gì vỡ ngay. Gateway 1.0.108 vẫn trả mảng, nên hợp đồng `c80797d` chưa khớp firmware hiện có. Khi có firmware hỗ trợ trang, phải quyết định giữ mở theo trang hay đóng lại.
- **`control.get_status`:** field mới đã được server và UI xử lý từ plan `261110-1335-do-channel-ownership`. Không cần thay đổi.
- **Không có thay đổi phá field nào mà server hoặc UI đang đọc** ngoài `status.tags` (không có reader). Điều kiện dừng của phase ("dừng, báo controller") không được kích hoạt.
- Ghi chú spec: `control.set_output` và `system.get_info` đổi chữ ở đoạn tham số; đây không phải đổi field.

## 5. Điều kiện kiểm

- Hành vi chỉ kiểm bằng so sánh văn bản §6 và grep. Không gọi thiết bị, không đọc broker.
- Tên và số file tham chiếu lấy từ lệnh grep ngày 2026-11-10 trên cây làm việc của worktree; các dòng có thể lệch khi code đổi.
