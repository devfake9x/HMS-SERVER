---
phase: 1
title: "Catalog v2 và từ vựng quyền (nguồn: đặc tả MQTT §11)"
status: completed
priority: P1
dependencies: []
revised: 2026-10-05
completed: 2026-10-05
---

# Phase 1: Catalog v2 và từ vựng quyền

> **Sửa 2026-10-05 (design contract Q14):** bản đầu của phase này sinh catalog bằng cách đọc code firmware. Đó là sai: server và thiết bị giao tiếp qua MQTT nên hợp đồng là **đặc tả**. Phase này nay lấy catalog từ §11 của `mqtt-api.md`, chỉ giữ những gì đặc tả nói, và không mở file code firmware nào. Brainstorm: `plans/reports/261005-1930-brainstorm-spec-sourced-command-catalog.md`.

## Context Links

- Design contract (BINDING): [design-contract.md](./reports/design-contract.md) §0 (Q1, Q14), §1 vocabulary, §2 catalog v2 (nguồn §11), §3 (`CommandClass`, `DomainPermission`, `CommandDefinition`), §5 (`CommandPermissionResolver`), §6 (DTO `/me/command-catalog`), §11 V1, §12, §13 (khoảng trống đặc tả).
- Đặc tả (read-only): HMS-IDF `83e93d77e4a433efce665d669b567b3acc51b735`:`engineering-docs/api/mqtt-api.md` — §0 (R11), §3 (envelope), §5 (lỗi), §6 (catalog, ý nghĩa class), §7 (confirm), §11 (manifest permission cho server).

## Overview

Generator đọc **tài liệu đặc tả** ở commit ghim, cắt §11, parse bảng action và bảng domain, rồi sinh `command-catalog.json`. Loader C# parse chặt, kiểm hash, validate từng dòng và project vào `CommandDefinition`. Từ vựng quyền `DevicePermissionVocabulary.All` = 46 mã firmware (từ catalog) + 4 mã server = 50. Hành vi lệnh không đổi: mọi dòng vẫn `DeniedUntilActionContract` (transport MQTT tạm dừng).

Hiện trạng: bản đầu đã cài xong và xanh (catalog, loader, resolver, vocabulary, DTO, test). Nội dung catalog bản đầu trùng §11 (so bằng script: 0 khác biệt), nên phase này chỉ đổi **nguồn và hình dạng**, không đổi chính sách.

## Key Insights (từ đặc tả)

- §11: "94 cặp, 12 namespace". Bảng action có đúng 6 cột `Action | Class | Permission server | TTL default ms | Dry run | Confirm`. Ô `Action` là `` `type.action` ``; `Class` là một chữ không backtick; `Permission server` là `` `code` `` hoặc `` `Theo domain (bảng sau)` `` (chỉ `config.get`, `config.set`); `TTL default ms` là số (60000 hoặc 120000); `Dry run` là `Có`/`Không`; `Confirm` là `` `—` `` hoặc mẫu `` `REBOOT-<DEVID>` ``.
- §11 bảng domain có 3 cột `Domain | config.get | config.set`, 6 dòng: system, network, wifi_sta, wifi_ap, mqtt, adc. §6.1 liệt kê đúng 6 domain này; `system` chỉ nhận hostname (cơ sở V1: `sys.config_import` không high-risk).
- §6 đầu mục: class R/W/C/D/F/B = read / write config / control / destructive / flash hoặc reboot / physical hoặc cost.
- §0 R11: thiết bị **không kiểm permission**; server phân quyền theo cột permission ở §11 trước khi ký. Catalog là ranh giới bảo mật.
- §3: tham số ở `root.data.data`; JSON cấm key trùng kể cả khác hoa/thường. §7: token confirm là mẫu với `<device_id>` thay bằng identity MQTT; dry run chỉ 3 dòng.
- §11 không có: audit publish, nhận body, outcome trễ, TTL mặc định riêng của dispatcher → không đưa vào catalog (Q14). Luật so khớp `domain` không được nêu (G3) → server giữ khớp chặt.
- Byte tài liệu `git show` (LF): blob `6485e00f4c609a1ba57063cfd7e7aef75955c58b`, SHA-256 `ef41c113b001459b2cc0d0e6bb8c3fd00f8008c22bd583af8a2e72d6f33e5274`.
- Gotcha hash chéo ngôn ngữ (giữ từ bản đầu): System.Text.Json mặc định escape `<` và `>` thành backslash + `u003C` / `u003E` (hex hoa). Mọi chuỗi chính sách trong §11 nằm trong charset `^[A-Za-z0-9_.<>-]+$`, nên `JSON.stringify` + thay `<`/`>` cho đúng byte STJ; golden vector chung JS/C# khoá điều đó.
- Code server đã có (bản đầu): `CommandDefinition` (Class, PermissionByDomain, ConfirmTemplate, DryRunSupported, `DispatcherDefaultTtlMs`), `DevicePermissionCodes`, `DevicePermissionVocabulary`, `CommandPermissionResolver`, DTO `/me/command-catalog`, `PinnedCommandCatalog.Load(Stream)`, `CommandCatalogHash` + golden vector. Chúng giữ nguyên, trừ phần nêu ở Requirements.

## Requirements

**Functional**
- F1 Generator `scripts/generate-command-catalog.mjs` (Node ≥ 20, không dependency): `--idf <path>` > env `HMS_IDF_REPO` > mặc định `D:/WORKSPACE/PROJECT/HMS-IDF`; commit ghim là hằng; chỉ đọc `git show <commit>:engineering-docs/api/mqtt-api.md` (kiểm byte khớp blob id); `--check` không ghi, exit 1 khi JSON commit khác output. Export `parseSpecManifest(markdown)` (thay `parseDescriptor`) cùng 5 hàm canonicalizer/serializer cho node:test (không cần HMS-IDF).
- F2 `parseSpecManifest` cắt từ heading `## 11. Manifest permission để server thực thi` tới heading cấp 2 kế tiếp; parse 2 bảng; báo lỗi to theo contract §2 (header khác, số ô sai, class lạ, permission ngoài charset và khác `Theo domain (bảng sau)`, TTL không phải số nguyên dương ≤ 86400000, Dry run khác `Có`/`Không`, Confirm khác `—`/chuỗi chính sách có `<DEVID>`, action hoặc domain trùng, quyền theo domain dùng ngoài `config.get` (R) / `config.set` (không R), số cặp hoặc số namespace khác câu "N cặp, M namespace" của §11, bảng domain rỗng). Chỉ nhận đúng bố cục §11: heading §11 duy nhất ở cột 0 và heading ATX kế tiếp (mọi cấp) kết thúc mục, dòng bảng bắt đầu ở cột 0, dòng phân cách đủ số ô, sau bảng là dòng trống hoặc hết mục, bảng lệnh có ít nhất 1 dòng, không có code fence, khối HTML (`<`), blockquote (`>`) hay gạch setext; định danh kiểm nguyên văn, chỉ heading/prose/chữ tiếng Việt cố định so ở NFC. Không mô hình Markdown/HTML ngoài §11 (fence hay comment chưa đóng ở mục trước) — hash ghim và review khi ghim lại đặc tả lo phần đó. `config.get` và `config.set` cùng lấy quyền theo domain hoặc cùng không.
- F3 JSON v2 đúng thứ tự top-level contract §2 (không còn `dispatcher_default_ttl_ms`), 9 trường/dòng `type, action, class, required_perm, domain_policy, ttl_ms, confirm_required, confirm_template, dry_run`; `catalog_version = mqtt-api-83e93d77e`; `source_path = engineering-docs/api/mqtt-api.md`; `source_blob/source_hash` của tài liệu.
- F4 `CommandCatalogRow` 9 trường (`TtlMs` kiểu `int`, không null); hash v2 = generator byte-for-byte; loader parse chặt (thiếu/thừa/trùng key → lỗi), validate (class, đúng 1 nguồn quyền, confirm ⇔ mẫu, TTL > 0, cặp và domain duy nhất, charset), project 6 trường; bỏ mọi luật của 3 trường đã xoá (gồm luật "mutation phải audit" thêm ở review vòng 2) và kiểm `dispatcher_default_ttl_ms`.
- F5 Giữ nguyên: `DevicePermissionCodes`, `DevicePermissionVocabulary.All` (50), `CommandPermissionResolver` (khớp chặt, `ValueEquals`, surrogate lẻ → null), DTO `/me/command-catalog` (10 field; `ttlMs = DefaultTtlMs ?? DispatcherDefaultTtlMs`).

**Non-functional**
- Không mở file code firmware; mọi fact về thiết bị dẫn mục đặc tả.
- Output deterministic (sort ordinal, LF, 1 newline cuối); không chạm frontend; không chạm `DependencyInjection.cs`.

## Architecture

```
mqtt-api.md @ commit (git show) ─► parseSpecManifest (§11) ─► renderCatalog ─► command-catalog.json (embedded)
        export assertPolicyCharset / canonicalJson / canonicalize / catalogHash / renderCatalog / parseSpecManifest
                 ◄── generate-command-catalog.test.mjs (node:test, fixture §11 tổng hợp, không cần HMS-IDF)
PinnedCommandCatalog ─ strict parse ─► CommandCatalogHash.Compute (C#) ─► validate ─► CommandDefinition[94]
        └─ FirmwarePermissions (46) ∪ DevicePermissionCodes.ServerCodes (4) ─► DevicePermissionVocabulary.All (50)
```

Mapping một dòng §11 → row: `type.action` tách ở dấu `.` đầu tiên; `class` = ô Class; `Theo domain (bảng sau)` → `required_perm = null`, `domain_policy = true`, ngược lại `required_perm` = code, `domain_policy = false`; `ttl_ms` = số; `confirm_required = (Confirm ≠ —)`; `confirm_template` = mẫu hoặc `null`; `dry_run = (Dry run = Có)`. Ô có backtick thì bỏ backtick ngoài cùng; ô không backtick lấy nguyên (đã trim).

## Related Code Files

**Modify**

| Path | Mô tả |
|---|---|
| `scripts/generate-command-catalog.mjs` | Thay parser C bằng `parseSpecManifest` (Markdown §11); đọc `engineering-docs/api/mqtt-api.md`; bỏ toàn bộ code parse C |
| `scripts/generate-command-catalog.test.mjs` | Fixture §11 tổng hợp: đường chạy đúng + bảng báo lỗi; golden vector 9 trường; field order 9 trường; top-level không còn TTL dispatcher |
| `src/Hms.Server.Infrastructure/Commands/command-catalog.json` | Sinh lại bằng generator (không sửa tay) |
| `src/Hms.Server.Infrastructure/Commands/CommandCatalogHash.cs` | `CommandCatalogRow` 9 trường, `TtlMs` là `int` |
| `src/Hms.Server.Infrastructure/Commands/PinnedCommandCatalog.cs` | Bỏ `dispatcher_default_ttl_ms`, bỏ luật audit/outcome; comment dẫn đặc tả |
| `tests/Hms.Server.UnitTests/CatalogTests.cs` | Khẳng định theo §11; bỏ test cờ chỉ có trong code; golden vector 9 trường |
| `docs/architecture-device-mqtt-api-catalog.md` | Dòng ~275: catalog sinh từ §11 đặc tả |
| `plans/260924-2044-hms-server-management/plan.md` | Blockquote superseded: nguồn là §11 đặc tả |

**Không đổi** (đã đúng): `CommandDefinition.cs`, `DevicePermissionCodes.cs`, `DevicePermissionVocabulary.cs`, `CommandPermissionResolver.cs`, `CommandContracts.cs`, `SendDeviceCommandHandler.cs`, `MeController.cs`, `CommandPermissionResolverTests.cs`, `DevicePermissionCodesTests.cs`, `DevicePermissionVocabularyTests.cs`, `ManagementApiTests.cs`.

## Implementation Steps

### A. Tests Before (red)

1. `scripts/generate-command-catalog.test.mjs`:
   - `parseSpecManifest reads the action and domain tables`: fixture §11 nhỏ (heading §11 thật, câu "N cặp", vài dòng fixed + 2 dòng `Theo domain` + bảng domain, một heading `## 12.` sau đó) → đúng rows/domains.
   - `parseSpecManifest fails loudly on a table it cannot vouch for`: header đổi tên/thiếu cột, ô thừa/thiếu, class `X`, permission có khoảng trắng, TTL `0`/`6e4`/`-1`/rỗng, Dry run `Yes`, Confirm có khoảng trắng, action trùng, domain trùng, câu "N cặp" lệch số dòng, thiếu bảng domain khi có dòng `Theo domain`, thiếu heading §11.
   - `canonicalize writes the fixed field order` (9 trường; field lạ như `audit_publish` → lỗi), golden vector mới, `renderCatalog` (top-level 10 khoá).
2. `CatalogTests.cs`:
   - Metadata: top-level 10 khoá đúng thứ tự §2; `source_path = engineering-docs/api/mqtt-api.md`; `source_blob = 6485e00f…`, `source_hash = ef41c113…`; `catalog_version = mqtt-api-` + 9 ký tự commit.
   - Giữ: 94 cặp + fingerprint tập cặp; mọi dòng `DeniedUntilActionContract`; class R49 W18 C17 D3 B3 F4 và `Dangerous ⇔ Flash`; 46 quyền, 6 domain; một nguồn quyền; 3 mẫu confirm + dry run.
   - TTL: 24 dòng 60000 (3 confirm + 21 `network.wifi_*`), 70 dòng 120000, không dòng nào null.
   - Theory dòng: cột TTL nay là 120000 cho dòng mặc định.
   - Bỏ `Committed_catalog_flags_match_firmware` và các case `read_row_with_audit_publish`, `read_row_with_outcome_deferred`, `mutation_row_without_audit_publish`; thêm `null_ttl` (→ `ROW_INVALID`).
   - Hash theo từng trường: tự sinh từ khoá serialize (9 + 3).
   - Golden vector 9 trường (literal JS và C# giống hệt; điền escape bằng script, không gõ tay).

### B. Implement

3. `CommandCatalogHash.cs`: row 9 trường, `TtlMs` `int`.
4. Generator: `parseSpecManifest`, đọc tài liệu, mapping trên; xoá parser C; giữ `--check`, kiểm blob id, entry-point guard.
5. `PinnedCommandCatalog.cs`: bỏ `dispatcher_default_ttl_ms` khỏi document và kiểm metadata; bỏ luật audit/outcome; `DefaultTtlMs = row.TtlMs`.
6. Chạy generator → JSON mới; chạy lại 2 lần + `--check`.
7. Docs: `docs/architecture-device-mqtt-api-catalog.md`, blockquote plan cũ.

### C. Tests After

8. Toàn bộ test A xanh (C# + node:test); `--check` exit 0; sửa tay 1 byte JSON → exit 1, khôi phục bằng generator.

### Regression Gate

```bash
node --test scripts/generate-command-catalog.test.mjs
node scripts/generate-command-catalog.mjs --check
dotnet build Hms.Server.slnx
dotnet test Hms.Server.slnx
cd frontend && npm run typecheck && npm test && npm run build
```

## Todo List

- [x] Test A đỏ trước (node:test parser §11 + C# CatalogTests theo đặc tả)
- [x] `CommandCatalogRow` 9 trường
- [x] Generator đọc §11 (xoá parser C)
- [x] Loader bỏ luật/trường không có trong đặc tả
- [x] Sinh lại JSON; `--check`
- [x] Docs
- [x] Regression Gate xanh

## Success Criteria

- [x] Generator chỉ đọc `engineering-docs/api/mqtt-api.md`; không còn đường dẫn file code firmware trong generator, test, contract, plan, phase.
- [x] Catalog: 94 dòng, 46 mã, 6 domain, R49 W18 C17 D3 B3 F4, 24 dòng TTL 60000 + 70 dòng 120000, 3 mẫu confirm + dry run — đều đúng như §11.
- [x] Mỗi dòng đúng 9 trường; không còn `audit_publish`, `accepts_body`, `outcome_deferred`, `dispatcher_default_ttl_ms`.
- [x] Hash JS ≡ C# (golden vector chung); bảng §11 sai dạng → generator báo lỗi, không sinh catalog.
- [x] Nội dung catalog bị khoá trong CI: `CatalogTests` ghim `catalog_hash`, nên sửa bất kỳ dòng nào (kể cả đã tính lại hash) làm test đỏ; loader áp cùng luật với generator (TTL ≤ 86400000, mẫu confirm có `<DEVID>`, binding `config.get`/`config.set`, catalog không rỗng, hash chữ thường).
- [x] `/me/command-catalog` không đổi hình dạng (10 field).
- [x] `dotnet test`, node test, frontend gate xanh.

## Risk Assessment

| Risk | Mitigation |
|---|---|
| Tài liệu đổi dạng bảng §11 | Kiểm header và từng ô chặt → báo lỗi to; G1 (manifest JSON) để sau |
| Thiết bị lệch đặc tả (firmware chưa xong) | Mọi dòng vẫn `DeniedUntilActionContract`; G2 khi bật MQTT |
| Đặc tả thêm lệnh (SMS) | Ghim lại commit, sinh lại; hash đổi → `CATALOG_STALE` có chủ đích |
| Golden literal bị hỏng do công cụ giải mã backslash-u | Điền escape bằng script; so literal JS/C# sau mỗi lần sửa |

## Security Considerations

- R11: catalog là ranh giới bảo mật → parse chặt, validate, fail-fast ở lần resolve đầu; hash phủ mọi trường của §11 nên đổi chính sách luôn ra `CATALOG_STALE`.
- Resolver fail-closed (khớp chặt, chỉ chuỗi); §3 cấm key trùng kể cả khác hoa/thường.
- Generator dùng `execFileSync` mảng tham số, chỉ đọc qua `git show`, không ghi gì vào HMS-IDF; import từ test không chạy git.

## Open Questions

Khoảng trống đặc tả G1–G4: design contract §13 (chưa gửi bên firmware).

## Next Steps

- Phase 3 dùng `CommandPermissionResolver.Resolve` (null → `PERMISSION_UNMAPPED`), `DevicePermissionVocabulary.All` (validate role), `DevicePermissionCodes.HighRisk`, `ConfirmTemplate` (→ `CONFIRM_REQUIRED`), `DefaultTtlMs`.
- Phase 4 dùng `DevicePermissionVocabulary.All` cho `GET /api/v1/device-permission-codes` (nhãn firmware = chính code, G4).
- Phase 5/6 không đổi so với bản đầu.
