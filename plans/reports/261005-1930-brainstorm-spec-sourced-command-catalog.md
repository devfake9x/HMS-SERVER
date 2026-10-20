---
title: "Brainstorm — catalog quyền lấy từ đặc tả MQTT, không đọc code firmware"
date: 2026-10-05
status: agreed
plan: plans/261005-0556-device-permissions-what-where
modes: []
---

# Catalog quyền lấy từ đặc tả MQTT

## Vấn đề

Server và thiết bị trao đổi qua MQTT, nên hợp đồng giữa hai bên là **đặc tả**, không phải code firmware. Phase 1 của plan `261005-0556` vi phạm điều này: generator đọc `components/command-dispatcher/cmd_descriptor.c` (code C) để sinh `command-catalog.json`, và 13 chỗ trong plan/contract/phase dẫn chứng bằng file `.c`. Chủ dự án gọi đây là sai lầm (2026-10-05). Hệ quả phụ: hai vòng review cuối sa vào làm cứng parser C trước các mẹo tiền xử lý (`#if`, digraph, `#define` đổi tên), là thứ chỉ tồn tại vì đọc code.

## Phát hiện (chỉ đọc đặc tả, không đọc code)

- Đặc tả: `HMS-IDF/engineering-docs/api/mqtt-api.md`, phát hành ở chính commit đang ghim `83e93d77e` (plan firmware `261004-2103-tai-lieu-server-mqtt`, "tài liệu MQTT cho người làm server"). Working tree trùng commit.
- **§11 "Manifest permission để server thực thi"**: bảng 94 cặp (class, permission, TTL default, dry run, confirm) + bảng 6 domain cho `config.get/set`. So bằng script với catalog server đang nhúng: **0 khác biệt** ở class, permission, TTL, dry_run, confirm, domain; 46 mã quyền.
- Ba trường `audit_publish`, `accepts_body`, `outcome_deferred` chỉ có trong code; server không dùng; §11 không có.
- Đặc tả đã phủ: R11 (thiết bị không kiểm quyền), §3 (tham số ở `root.data.data`, cấm key trùng kể cả khác hoa/thường), §6.1 (`config.set system` chỉ nhận hostname → cơ sở V1), §7 (token confirm theo device_id), §5 (mã lỗi), §2 (`B/capabilities`).

## Quyết định (chủ dự án, 2026-10-05)

| # | Quyết định |
|---|---|
| D1 | Nguồn catalog = §11 của `mqtt-api.md` ở commit ghim; generator đọc tài liệu qua `git show`, không đọc code |
| D2 | Chỉ làm phần đặc tả đã có: bỏ 3 trường chỉ có trong code và mọi luật suy từ code; đặc tả chưa đủ vì thiết bị còn đang code |
| D3 | Tạm dừng mảng MQTT; khoảng trống đặc tả ghi thành danh sách chờ, **chưa gửi** bên firmware |
| D4 | Hoàn thiện các phần còn lại (phase 2–6) theo design contract (thiết kế chuẩn) |

## Phương án đã cân nhắc

| Phương án | Ưu | Nhược | Kết luận |
|---|---|---|---|
| A. Parse §11 (Markdown) ở commit ghim | Đúng hợp đồng; xoá parser C; nội dung không đổi | Bảng Markdown đổi dạng là vỡ → kiểm header chặt, báo lỗi | **Chọn** |
| A'. Chờ firmware phát hành manifest JSON | Ổn định nhất | Chặn tiến độ | Để sau (G1) |
| B. Server gõ tay JSON theo §11 | Đơn giản | Lặp dữ liệu, dễ chép sai | Loại |
| C. Lấy từ `B/capabilities` lúc chạy | Luôn khớp thiết bị | Cần transport MQTT (đang dừng), thiếu mẫu confirm, vocabulary vai trò phải tĩnh | Chỉ làm phép kiểm lệch trong plan transport |

## Thiết kế chốt (sửa phase 1)

- Generator đọc `git show 83e93d77e:engineering-docs/api/mqtt-api.md`, cắt §11, parse 2 bảng. Báo lỗi khi: tiêu đề cột khác, class ngoài R/W/C/D/B/F, permission ngoài charset (hoặc khác "Theo domain (bảng sau)"), TTL không phải số nguyên dương, Dry run khác Có/Không, confirm khác `—`/mẫu hợp lệ, cặp trùng, số dòng khác câu "94 cặp" của chính §11, bảng domain thiếu/thừa. Xoá toàn bộ parser C.
- Catalog: 9 trường/dòng `type, action, class, required_perm, domain_policy, ttl_ms, confirm_required, confirm_template, dry_run`; `ttl_ms` là số của §11 (không null); bỏ `dispatcher_default_ttl_ms`; `source_path = engineering-docs/api/mqtt-api.md`, `source_blob/source_hash` của tài liệu; `catalog_version = mqtt-api-83e93d77e`. Charset guard + golden vector JS/C# giữ nguyên ý nghĩa.
- C#: `CommandCatalogRow` 9 trường; loader bỏ luật của 3 trường đã xoá (gồm luật audit thêm ở review vòng 2); giữ JSON chặt (key trùng), hash, class, một nguồn quyền, confirm ⇔ mẫu, TTL > 0, charset, cặp/domain duy nhất. Resolver giữ so khớp chặt (chính sách server; G3).
- Test: khẳng định firmware → khẳng định theo §11; bỏ test 3 trường; test node cho parser Markdown.
- Tài liệu: contract §0/§1/§2/§5/§10 + §13 mới (khoảng trống); phase 1 viết lại theo đặc tả; gỡ trích dẫn `.c` ở plan.md, phase 4; báo cáo cũ giữ làm lịch sử, ghi chú đã thay thế.

## Khoảng trống đặc tả (danh sách chờ, chưa gửi)

| # | Thiếu | Vì sao server cần |
|---|---|---|
| G1 | §11 dạng máy đọc được (JSON) hoặc cam kết giữ cột bảng | Parse Markdown dễ vỡ khi tài liệu đổi dạng |
| G2 | Phiên bản/hash manifest trong §11 và `B/capabilities`/`system.get_info` | Phát hiện thiết bị chạy manifest khác bản server ghim |
| G3 | Luật so khớp `domain` (hoa/thường, chỉ chuỗi, vị trí đọc) | Server đang tự chọn khớp chặt |
| G4 | Mô tả một dòng cho mỗi mã quyền (tuỳ chọn) | Hiển thị ở màn sửa vai trò |

Thêm: khi bên firmware thêm lệnh SMS (đang làm), §11 phải cập nhật và server ghim lại đặc tả.

## Rủi ro

- Bảng Markdown đổi dạng → generator báo lỗi to, không sinh catalog sai; khắc phục bằng G1.
- Thiết bị lệch đặc tả (firmware chưa xong) → không phát hiện được cho tới G2/plan transport; hiện mọi dòng vẫn `DeniedUntilActionContract` nên không gửi lệnh thật.

## Tiêu chí thành công

- Generator chỉ đọc `mqtt-api.md`; không còn tham chiếu file `.c` trong generator, contract, plan, phase.
- Catalog sinh lại có cùng 94 dòng, 46 mã, 6 domain như §11; `--check` exit 0.
- `dotnet test`, node test, frontend gate xanh.

## Bước tiếp theo

1. Cập nhật plan/contract/phase 1 theo thiết kế.
2. `/ck:cook` lại phase 1 (TDD), review, finalize.
3. Cook phase 2 → 6 theo design contract.
