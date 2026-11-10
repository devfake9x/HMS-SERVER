# Brainstorm: tách hành vi class B khỏi giả định "mọi class B là relay"

- Ngày: 2026-11-10
- Nguồn: review finding M1 của plan `261110-1747-mqtt-catalog-sync`, Follow-up (d); `docs/architecture-mqtt-device-link.md` dòng 83
- Quyết định: hướng B (thuộc tính hành vi trên `CommandDefinition`), tự chốt theo quyền MQTT/device-link user đã giao
- Bước tiếp: `/ck:plan --tdd`

## 1. Vấn đề

Catalog ghim `mqtt-api-c80797d9d` có 15 row class B/D/F. Server quyết định hành vi theo chữ cái class, viết từ lúc class B chỉ gồm relay.

Row B/D/F hiện có:

| Class | Action | Mở? |
|---|---|---|
| B | `control.set_output`, `control.pulse` | Mở |
| B | `control.set_all`, `control.force_set`, `control.force_clear`, `control.set_config`, `sms.send` | Đóng |
| D | `sms.delete`, `broker.delete_pipeline`, `broker.delete_signal`, `network.wifi_known_remove` | Đóng |
| F | `system.reboot` | Mở |
| F | `system.factory_reset`, `ota.apply`, `ota.remote_start` | Đóng |

Chỗ đọc class:

| # | Vị trí | Hành vi hiện tại | Sai với action nào |
|---|---|---|---|
| 1 | `EfCommandAuditStore.cs:280` | Lưu nguyên payload vào `PayloadSummary` cho mọi B/D/F | `sms.send` (số điện thoại, nội dung), `sms.delete`, các row D khác chưa xét |
| 2 | `DeviceOutputState.cs:128` | Đánh stale DO sau mọi lệnh B | `sms.send` |
| 3 | `DeviceLinkHandlers.cs:139` | Đánh stale DO khi lệnh B hết hạn | `sms.send` |
| 4 | `CommandLanes.cs:11` (5 nơi gọi) | Mọi B bỏ qua lane ghi | Mọi B không do panel DO tuần tự hoá |
| 5 | `SendDeviceCommandHandler.cs:143` | TTL kẹp 10 s cho B và F | Không sai; trần riêng cho SMS là việc của plan mở SMS (R15) |
| 6 | `ConfigCommandPolicy.cs:33`, `OutputCommandPolicy.cs:27` | Config policy bỏ qua B; output policy từ chối B lạ | Fail closed, đúng; nhưng B không phải relay không có chỗ đặt luật payload |

Hiện trạng thật: **chưa có rò rỉ**. Row đóng bị từ chối ở `SendDeviceCommandHandler.cs:101` trước khi có plan, nên không có `PayloadSummary`. Rủi ro tiềm ẩn: thêm một row vào allowlist là đủ kích hoạt cả 4 hành vi sai, không test nào đỏ.

## 2. Yêu cầu

- Output: mã server đọc hành vi theo action, không theo class; test khoá.
- Acceptance:
  - 34 action đang mở giữ nguyên hành vi: nội dung `PayloadSummary`, stale DO, lane, TTL.
  - Catalog hash `658e191a…`, pair-set, `command-catalog.json` không đổi. Không migration.
  - Test với catalog giả lập mở `sms.send`: payload không vào audit; DO không stale; lệnh vào lane ghi.
  - Test khoá: action B/D/F mở mà thiếu khai báo hành vi thì đỏ.
- Ngoài phạm vi: mở action; R13/R15/R16; redaction theo trường; đổi TTL; UI; `/me/command-catalog` shape.
- Ràng buộc: .NET 10; không đọc source HMS-IDF; không chạm broker lab.
- Touchpoints: `CommandDefinition.cs`, `PinnedCommandCatalog.cs`, `CommandLanes.cs`, `EfCommandAuditStore.cs`, `DeviceOutputState.cs`, `DeviceLinkHandlers.cs`, `DeviceLinkContracts.cs`, `MqttDeviceCommandTransport.cs`, `SendDeviceCommandHandler.cs`, test unit + integration.

## 3. Các hướng

### A. Chỉ sửa audit
Giữ payload theo danh sách action. Nhỏ nhất, chặn rủi ro dữ liệu. Stale DO và lane vẫn sai; lần mở sau phải dò lại 6 chỗ.

### B. Thuộc tính hành vi trên `CommandDefinition` (chọn)
Catalog là nơi duy nhất suy ra hành vi; nơi dùng đọc thuộc tính.

- `AuditPayload` (bool): true chỉ khi action nằm trong `PhysicalOutputActions`, `ConfigWriteActions`, `FlashActions`. Mặc định false.
- `AffectsOutputs` (bool): class B và `type == "control"`.
- Lane output (bỏ qua lane ghi): chỉ `PhysicalOutputActions`. Class Read giữ lane đọc. Còn lại lane ghi.
- TTL: giữ nguyên luật theo class.

Được: một nguồn; mặc định an toàn; test khoá chặn việc quên. Mất: đổi record domain, ~8 file, phải chứng minh không đổi hành vi cho 34 action.

### C. Strategy theo họ action
Mở rộng tốt nhất. Thừa với 2 action B đang mở.

## 4. Lý do chọn B

- A để lại 3/4 hành vi sai; chi phí dò lại lặp ở mỗi plan mở action.
- C vi phạm YAGNI.
- B đúng mẫu đang dùng: catalog đã suy `Availability`, `Dangerous` từ row + allowlist.

## 5. Rủi ro và lưu ý triển khai

- **Đổi hành vi ngầm của 34 action mở.** Giảm: viết test đặc tả hành vi hiện tại trước (TDD), chạy xanh trước và sau.
- **`CommandLanes.IsWrite(CommandClass)` đổi chữ ký** sang nhận definition: 5 nơi gọi, có cả registry pending. Kiểm từng nơi.
- **Definition dựng tay trong test** (không qua catalog): mặc định thuộc tính phải cho ra hành vi an toàn, và test hiện có dựng relay bằng tay phải khai báo lại. Đếm số test bị ảnh hưởng ở bước plan.
- **`system.reboot` (F)**: audit hiện bọc `{dry_run, payload}`; giữ nguyên nhánh này.
- **Row D**: sau thay đổi mặc định không lưu payload. Hiện không row D nào mở nên không đổi hành vi quan sát được; ghi vào docs để plan mở D phải quyết định.
- Docs phải sửa: `architecture-mqtt-device-link.md` dòng 83, 201, 303; `CommandLanes` summary.

## 6. Chỉ số thành công

- Toàn bộ test hiện có xanh, không sửa kỳ vọng nào của 34 action mở.
- Test mới: giả lập mở `sms.send`; test khoá khai báo hành vi.
- `node scripts/generate-command-catalog.mjs --check` và checker độc lập: 108 row, 0 lệch.
- Không còn `CommandClass.Physical` ở 4 chỗ #1-#4 (grep).

## 7. Câu hỏi còn mở

- TTL trần cho `sms.send` và việc "chỉ qua primary broker" (R13/R15): để plan mở SMS.
- `control.set_all`, `control.force_*`, `control.set_config` khi mở đi lane output hay lane ghi: để plan mở DO nâng cao; mặc định sau thay đổi này là lane ghi.
