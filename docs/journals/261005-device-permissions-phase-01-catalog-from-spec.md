# Catalog v2 & Từ Vựng Quyền từ Đặc Tả MQTT — Phase 1 Hoàn Tất

**Mã nhật ký**: `JRN-DEVICE-PERMISSIONS-PHASE-01-CATALOG-FROM-SPEC-2026`  
**Ngày thực hiện**: 05/10/2026  
**Chủ đề**: Phase 1 "Catalog v2 + từ vựng quyền" của plan `261005-0556-device-permissions-what-where` (`/ck:cook --tdd`): làm lại catalog lệnh từ đặc tả MQTT sau khi lần đầu đọc nhầm code firmware.  
**Trạng thái**: **Hoàn tất; owner duyệt kèm rủi ro thấp đã ghi nhận**

---

## 1. Sai lầm

Lần đầu, catalog lệnh được dựng bằng cách đọc code C của firmware HMS-IDF. Owner bác cách làm này: server và thiết bị trao đổi qua MQTT, nên server chỉ được làm việc qua đặc tả; thiếu thông tin thì liệt kê và yêu cầu bổ sung đặc tả, không xem code.

Phase được làm lại từ §11 ("Manifest permission để server thực thi") của `HMS-IDF/engineering-docs/api/mqtt-api.md` @ `83e93d77e`. Chú thích trong code và docs nhắc tới firmware đã được gỡ; quy tắc được lưu thành memory để các phiên sau không lặp lại.

## 2. Quyết định Q14 (design contract)

- Tích hợp chỉ qua đặc tả.
- Phần cần MQTT tạm dừng: đặc tả chưa đầy đủ, firmware chưa xong.
- Chỗ đặc tả còn thiếu (G1–G5) ghi ở design contract §13, chưa gửi đội firmware.
- Phần không cần MQTT (phase 2–6: vai trò, thành viên, kiểm quyền, HTTP, realtime, frontend) làm tiếp theo design contract.

## 3. Kết quả

- `scripts/generate-command-catalog.mjs` đọc §11 qua `git show` tại commit đã ghim, parse chặt (gặp layout lạ là dừng), sinh `src/Hms.Server.Infrastructure/Commands/command-catalog.json` v2:
  - 94 lệnh thuộc 12 namespace (`adc`, `battery`, `broker`, `config`, `control`, `modbus`, `mqtt`, `network`, `ota`, `sms`, `status`, `system`); 3 lệnh cần xác nhận.
  - 6 config domain: `adc`, `mqtt`, `network`, `system`, `wifi_ap`, `wifi_sta`.
  - 9 trường mỗi dòng: `type`, `action`, `class`, `required_perm`, `domain_policy`, `ttl_ms`, `confirm_required`, `confirm_template`, `dry_run`.
  - Hash SHA-256 trên JSON chuẩn hoá. `--check` thoát mã 1 khi file lệch với §11.
- `PinnedCommandCatalog` (C#) kiểm tra metadata, từng dòng và hash khi khởi tạo; lỗi trả `COMMAND_CATALOG_METADATA_INVALID`, `COMMAND_CATALOG_ROW_INVALID`, `COMMAND_CATALOG_HASH_MISMATCH`.
- Hash bên C# và bên JS khoá với nhau bằng một golden vector chung trong cả hai bộ test.
- Từ vựng quyền: 46 mã từ §11 + 4 mã server (`device.view`, `device.edit_basics`, `device.edit_product`, `device.record_measurements`) = 50 mã.
- `GET /api/v1/me/command-catalog` trả chính sách quyền theo từng action. Mọi action vẫn `DeniedUntilActionContract` vì MQTT đang dừng.

## 4. Kiểm chứng

| Hạng mục | Kết quả |
|---|---|
| Unit + integration (.NET) | 219 + 43 pass |
| Test generator (Node) | 11/11 |
| `--check` | exit 0 |
| Frontend | 135/135 |
| Catalog so với §11 | 0 khác biệt |

## 5. Review

- 3 vòng review. Vòng cuối: review chính PASS 9/10, review đối kháng PASS_WITH_RISK (thấp).
- Owner duyệt kèm rủi ro. Giới hạn còn lại ghi ở design contract §2 "Known limitations": số đếm giấu trong inline comment, list item dính sát bảng, bảng GFM không có dấu `|` và ô quyền `-` (hai trường hợp này dừng an toàn), vài check chưa có test riêng. Quyết định ghi ở `reports/harness/risk-gate.json`.

## 6. Bài học

1. Hợp đồng tích hợp là đặc tả, không phải code của bên kia. Thiếu thông tin thì yêu cầu bổ sung đặc tả.
2. Các vòng review trôi dần vào edge case hiếm, trước là của C, sau là của Markdown; giới hạn vòng cuối giúp dừng vòng lặp.
3. Báo cáo test của subagent có số liệu và khẳng định sai; phải kiểm lại trước khi báo owner.
4. Tool Edit/Write tự giải mã chuỗi escape backslash-u; literal escape trong source cần placeholder + script nhỏ.
5. Bản cập nhật docs lúc finalize tự gán nhãn phase sai; phải đối chiếu với plan trước khi chấp nhận.

## 7. Tiếp theo

- Phase 2: vai trò, thành viên, lưu trữ (theo design contract).
- Plan MQTT `261001-0030-full-hardware-mqtt-apis-c2` vẫn bị chặn; G1–G5 chờ owner cho phép mới gửi.
- Chưa commit: project không có git repo.
