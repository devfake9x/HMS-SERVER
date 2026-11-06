---
phase: 2
title: "UI: ba thẻ đọc"
status: completed
priority: P1
dependencies: [1]
---

# Phase 2: Read cards on the device page

## Overview

Ba thẻ trên tab Telemetry: **Thiết bị & thời gian**, **Mạng**, **Nguồn & ADC**. Mỗi thẻ có nút "Đọc", gửi
lần lượt các lệnh của mình qua `runDeviceCommand`, và hiện dữ liệu từ reply đã xác minh. Thẻ không bao giờ để dữ
liệu cũ trông như mới, và huỷ được giữa chừng.

## Requirements

### Gửi lệnh
- **Không tự đọc khi mở trang.** Chỉ đọc khi bấm.
- **Lần lượt, không song song;** lệnh sau vẫn chạy khi lệnh trước lỗi.
- **`DEVICE_READ_BUSY`** (server từ chối) hoặc **`LANE_FULL`** (thiết bị từ chối, spec §5): thử lại một lần sau 2 s với correlation id mới, rồi báo lỗi [RT F2].
- **Huỷ** [RT F5]:
  - mỗi lần đọc có một `AbortController` truyền vào `runDeviceCommand`;
  - huỷ khi thẻ unmount, khi `deviceId` đổi, hoặc khi người dùng bấm "Dừng";
  - nút "Đọc" bị khoá khi đang đọc, nên không có chuỗi chạy chồng;
  - kết quả gắn với `deviceId` lúc gửi; kết quả về muộn của thiết bị khác bị bỏ.
- **`DeviceDetailView`:** theo dõi tham số `id` của route (trang được dùng lại khi đổi thiết bị). Đổi `id` thì tải lại dữ liệu thiết bị, `deviceLink`, `allowedCommands`, và reset các thẻ.

### Hiện kết quả [RT F6]
- **Mỗi lệnh có trạng thái riêng:** chưa đọc, đang đọc, đọc được lúc HH:MM:SS (tuổi), hoặc lỗi. Thẻ không dùng chung một "Đọc lúc" cho cả nhóm lệnh.
- **Đọc lại thất bại** thì bỏ giá trị cũ của lệnh đó và hiện "Không đọc được lúc HH:MM:SS: <mã>", như panel DO xoá trạng thái khi tải lỗi.
- **`Executed` mà không có data** (spec R3: `success:true` không kèm `data`, hoặc server không còn giữ reply) → "Thiết bị không trả dữ liệu, hãy đọc lại".
- **Lỗi lệnh:**
  - với `Failed`/`ContractUnavailable`, hiện `code` và `detail` từ body. Thiếu quyền là `Failed/DEVICE_PERMISSION_REQUIRED` qua HTTP 200, không phải 403 [RT F14];
  - HTTP lỗi khác hiện mã HTTP.
- **`CATALOG_STALE`** [RT F14]: hiện "Catalog trên server đã đổi, hãy tải lại trang" ở thẻ. Panel DO cũng nhận cùng xử lý (hiện đang hiện mã trần).
- **Offline:**
  - thiết bị offline (`link.online === false`) hoặc server mất broker (`brokerConnected === false`) thì hiện cảnh báo, vẫn cho đọc;
  - dữ liệu đã đọc trước đó gắn nhãn "có thể đã cũ".
- **Trường lạ hoặc sai kiểu** đưa vào mục "Khác" dạng JSON thô, không làm vỡ thẻ.
- **`available:false`** (pin hay ADC không có phần cứng) → "Không có phần cứng".

### Gating
- Chỉ gửi lệnh có trong `allowedCommands` (phase 1); lệnh không được phép hiện "Thiếu quyền" và không được gửi.
- Thẻ không còn lệnh nào được phép, hoặc catalog chưa qua kiểm tin cậy, thì ẩn nút.

### Từng thẻ (dạng data theo spec §6)

| Thẻ | Lệnh | Hiện |
|---|---|---|
| Thiết bị & thời gian | `system.get_time` | `datetime`, `epoch`, `ntp_synced`, `last_sync`, `rtc_available`, `rtc_model`, `timezone` |
| | `system.get_hostname` | `hostname` |
| | `system.get_device_id` | `device_id`, `mac` |
| | `ota.get_status` | spec không nêu trường: danh sách khoá–giá trị |
| Mạng | `network.get_priority` | `internet_uplink`, `active_uplink`, `has_connectivity` |
| | `network.get_ethernet` | `type`, `connected`, `active`, IP, `gateway`, `netmask` |
| | `network.get_modem` | projection §6.5: danh sách khoá–giá trị |
| | `network.wifi_sta_status` | `enabled`, `connected`, `ssid`, IP, `rssi`, `channel`, `auth` |
| | `network.wifi_ap_status` | `enabled`, `pending_restart`, `ssid`, `channel`, `client_count` |
| Nguồn & ADC | `battery.get_status`, `get_alerts` | `voltage`, `soc`, `charge_rate`, `is_charging`, `is_hibernating`, cờ cảnh báo |
| | `adc.get_config`, `get_channels`, `get_values` | bảng kênh ghép theo `ai` [RT F4]: `ai`, `name`, `type`, `enabled` (từ `get_channels`) với `value`, `unit`, `quality` (từ `get_values`) |

- `adc.get_status` không có `ai` (spec §6), nên thẻ không dùng; lệnh vẫn mở cho modal.

### Heartbeat [RT F11]
- `DeviceDetailView.onEvent` chỉ cập nhật "Last Heartbeat" và áp telemetry cho event heartbeat/status và connectivity.
- Các event khác (`command_result` của lệnh không phải đọc, `outputs`, `events`) không còn làm thiết bị trông như còn sống, và không bị áp vào telemetry.

## Architecture

```
DeviceDetailView (tab Telemetry, khi có binding MQTT; watch route id)
  ├─ tải deviceLink + allowedCommands
  └─ 3 thẻ: deviceId, csrfToken, link, catalog, catalogVerified, allowedCommands
       └─ DeviceReadCard (khung: tiêu đề, Đọc/Dừng, trạng thái theo lệnh, cảnh báo)
            └─ useDeviceReads(deviceId): readSequence(steps, signal) → map action → ReadState
                 └─ runDeviceCommand(..., { signal })  (đã có)
```

`ReadState = { kind: 'idle' | 'reading' | 'ok' | 'error' | 'no_data', data?, readAt?, code?, detail? }`

## Related Code Files

- **Create:**
  - `frontend/src/core/commands/useDeviceReads.ts`, `useDeviceReads.test.ts`
  - `frontend/src/features/devices/components/reads/DeviceReadCard.vue`, `ReadFieldList.vue`
  - `frontend/src/features/devices/components/reads/DeviceInfoTimeCard.vue`, `DeviceNetworkCard.vue`, `DevicePowerAdcCard.vue`
  - test `.test.ts` cho các thẻ; fixture theo dạng spec (phase 3 bổ sung reply thật đã redact)
- **Modify:**
  - `frontend/src/features/devices/views/DeviceDetailView.vue` (tải `allowedCommands`, đặt 3 thẻ, watch route `id`, heartbeat chỉ từ status/connectivity)
  - `DeviceDetailView.test.ts`
  - `frontend/src/features/devices/components/DeviceOutputsPanel.vue` (thông báo `CATALOG_STALE`)

## Tests first (viết trước, phải đỏ)

1. **`useDeviceReads`:**
   - gửi lần lượt; lệnh lỗi không chặn lệnh sau;
   - `DEVICE_READ_BUSY`/`LANE_FULL` thử lại đúng một lần sau 2 s, với id mới;
   - abort dừng ngay, không gửi lệnh tiếp;
   - kết quả của `deviceId` cũ bị bỏ.
2. **`DeviceReadCard`:**
   - không gửi khi mở;
   - nút khoá khi đang đọc, có "Dừng";
   - ẩn khi catalog chưa tin cậy hoặc không lệnh nào được phép;
   - lệnh không được phép hiện "Thiếu quyền" và không được gửi;
   - `CATALOG_STALE` hiện hướng dẫn tải lại;
   - `DEVICE_PERMISSION_REQUIRED` hiện mã;
   - offline thì gắn "có thể đã cũ";
   - đọc lại thất bại thì giá trị cũ biến mất;
   - `Executed` không data → "Thiết bị không trả dữ liệu".
3. **`ReadFieldList`:** trường biết trước có nhãn; trường lạ vào "Khác"; sai kiểu không vỡ.
4. **Từng thẻ:** render từ fixture; `available:false`; ADC ghép `get_channels` và `get_values` theo `ai`, kể cả khi một kênh chỉ có ở một bên.
5. **`DeviceDetailView`:**
   - 3 thẻ hiện khi có binding;
   - đổi route `id` thì tải lại và reset thẻ;
   - event `command_result` không đổi "Last Heartbeat";
   - test cũ vẫn xanh.
6. **`DeviceOutputsPanel`:** `CATALOG_STALE` hiện hướng dẫn tải lại; các test panel cũ vẫn xanh.

## Implementation Steps

1. Viết test 1–6, chạy để xác nhận đỏ.
2. `useDeviceReads`, rồi `ReadFieldList` và `DeviceReadCard`.
3. Ba thẻ.
4. `DeviceDetailView`: `allowedCommands`, thẻ, watch `id`, heartbeat; panel DO: `CATALOG_STALE`.
5. `npx vitest run` và `npx vue-tsc --noEmit`.

## Success Criteria

- [x] Ba thẻ hiện đúng từ fixture; không thẻ nào tự gửi khi mở trang.
- [x] Không chuỗi đọc nào chạy chồng hoặc tiếp tục sau khi rời trang hay đổi thiết bị.
- [x] Không giá trị nào hiện như mới sau khi đọc lại thất bại.
- [x] Lệnh không được phép không bao giờ được gửi từ thẻ.
- [x] Test cũ xanh; `vue-tsc` sạch.

## Risk Assessment

- **Spec để mở `ota.get_status` và `network.get_modem`:** hiện khoá–giá trị; phase 3 tinh nhãn theo reply thật.
- **Thiết bị offline:** mỗi lệnh chờ tới 40 s; thẻ thiết bị & thời gian có thể mất khoảng 2–3 phút. Nút "Dừng" và cảnh báo offline giảm tác hại; không tự rút ngắn thời gian chờ.
- **Rollback:** bỏ 3 thẻ khỏi view; composable và component đứng riêng; phần heartbeat và `CATALOG_STALE` là sửa lỗi độc lập.

## As built (2026-11-06)

- **Relay panel:**
  - a write and its read-back stay on the device they were sent to;
  - on a device switch, the panel clears its locks, errors and state;
  - the second read-back waits 1.5 s (`DEVICE_READ_BUSY` / busy lane);
  - a failed manual "Đọc trạng thái" now says why (`do-read-error`, including `CATALOG_STALE`).
- **Device page:**
  - on a route change it also resets `telemetryData` and `commandHistory`;
  - the invented "2 moments ago" heartbeat is gone ("Never" until a real heartbeat or telemetry);
  - `connectivity` events refresh the link flags.
- **Cards, after the live replies:**
  - `ota.get_status` is unwrapped (the device nests `{success, data}`) and its fields labelled;
  - `ntp_mode` added; `last_sync` shown as a date; `available` labelled;
  - long fractions rounded to 4 significant digits;
  - ADC rows need a real `ai` (null no longer becomes channel 0), and an ADC reply of unknown shape is shown raw.
- **Accessibility:** card buttons have `aria-label`s naming the card; card content is `aria-live="polite"`.
