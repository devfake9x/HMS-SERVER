---
title: "Đồng bộ catalog lệnh lên spec c80797d (108 row, status.tags cần tag.view)"
description: "Bộ sinh nhận --accept-stated-count khi câu đếm §11 lệch bảng; kiểm §11 độc lập; catalog ghim c80797d; status.tags theo tag.view; 4 quyền high-risk mới; 14 row mới vẫn đóng."
status: completed
priority: P2
effort: 8.5h
mode: tdd
created: 2026-11-10
branch: claude/mqtt-device-connection-36f705
brainstorm: plans/reports/brainstorm-261110-1745-mqtt-api-completeness-and-catalog-sync.md
contract: HMS-IDF/engineering-docs/api/mqtt-api.md @ c80797d9d5b817174cd2cfbcb6ee62effe503ff2 (blob 84ff812f68567e07cfd3a2dfeeb862ceb48630f6; chỉ đọc qua git --no-replace-objects). §0 R11 L415, R13 L417, R15 L419, R16 L420. §11 L1347-1484 (câu đếm L1351-1352, bảng action L1365-1474, bảng domain L1476-1483). Ghim cũ 83e93d77e (blob 6485e00f…). Cả hai commit chỉ nằm trên nhánh feature của HMS-IDF.
builds_on: [261005-0556-device-permissions-what-where, 261110-1335-do-channel-ownership]
supersedes: []
blockedBy: []
blocks: []
tags: [mqtt, catalog, permissions, generator]
---

# Đồng bộ catalog lệnh lên spec `c80797d`

Catalog ghim `83e93d77e` (94 row). Spec HEAD `c80797d` có 108 row: thêm 14, đổi quyền `status.tags`
(`sys.info_view` → `tag.view`). Bộ sinh từ chối HEAD vì câu đếm đầu tiên của §11 ("98 cặp, 12 namespace", L1351,
câu lịch sử) lệch bảng (108 row, 13 namespace). Plan này: cờ `--accept-stated-count 98/12` cho bộ sinh, script kiểm
§11 độc lập, ghim lại catalog, đóng lỗ quyền `status.tags`, thêm 4 mã high-risk. Không mở row mới nào
(`EnabledActions` vẫn 34).

## Luồng dữ liệu

1. **Ghim lại (máy dev, một lần):** object DB của HMS-IDF `c80797d:engineering-docs/api/mqtt-api.md` →
   `run` → `readSpecAtCommit` qua `git --no-replace-objects -c core.fsmonitor=false -C <repo> …`, env không có biến
   `GIT_*` (`rev-parse --verify <sha>^{commit}`, `rev-parse <sha>:<path>` → blob, `show <sha>:<path>` → bytes,
   sha1 bytes = blob; hiện ở `generate-command-catalog.mjs:395-413`, phase 1 tách ra) →
   `parseSpecManifest(md, { acceptStatedCount: { pairs: 98, namespaces: 12 } })` → `catalogHash` → `renderCatalog`
   → `src/Hms.Server.Infrastructure/Commands/command-catalog.json` (embedded resource).
   Song song, không dùng code bộ sinh: `scripts/verify-command-catalog-repin.mjs` đọc cùng commit, tách row §11 bằng
   regex dòng, tính pair-set SHA, diff với `83e93d77e` và so từng field với JSON vừa sinh → review → ghim trong `CatalogTests`.
   Không bước nào đọc working tree của HMS-IDF.
2. **Runtime:** JSON nhúng → `PinnedCommandCatalog` ctor (`PinnedCommandCatalog.cs:127-163`: parse chặt, tính lại hash,
   kiểm từng row, mở theo allowlist từng class :249-257, `FirmwarePermissions` :157-161) →
   - `DevicePermissionVocabulary.All` → `RoleRules.CheckVocabulary` rồi `CheckHighRisk` (`DeviceAccessHandlers.cs:54-57`, :359-370; `DevicePermissionCodes.HighRisk` `DevicePermissionCodes.cs:21-28`). High-risk chỉ kiểm lúc ghi grant: tạo role :57, sửa :106, xoá :166, gán/gỡ membership `CheckRoleChange` :225, :289, :315-327;
   - `GET /api/v1/me/command-catalog` (`MeController.cs:48-71`, cờ `highRisk` theo `CommandPermissionResolver.IsHighRisk` :36-38);
   - `GET /api/v1/devices/{id}/allowed-commands` (`DeviceCommandsController.cs:52-77`);
   - `POST /commands` → `SendDeviceCommandHandler`: `CATALOG_STALE` (:86-93) → `COMMAND_NOT_IN_CATALOG` (:95-99) → `DENIED_UNTIL_ACTION_CONTRACT` (:101-104) → quyền (:108-118, chỉ quyền của role membership, `DeviceAccessResolver.cs:23-35`) → class F chỉ master-admin (:123-127) → confirm (:129-132) → giữ chỗ audit (:171) → adapter → transport. Lúc gửi không kiểm high-risk.
3. **Deploy:** catalog là embedded resource → build mới + khởi động lại API. Khởi động lại xoá store reply, store DO, lane, theo dõi reboot (in-memory) và đánh `OUTCOME_UNKNOWN_AFTER_RESTART` cho lệnh chưa xong (`Program.cs:157-164`). Tab UI cũ giữ `mqtt-api-83e93d77e` + hash cũ → lệnh kế tiếp nhận `CATALOG_STALE` trước khi ký → `App.vue:778-783`, `DeviceOutputsPanel.vue:162-165`, `rebootPlan.ts:168` báo tải lại; `verifyCatalog` khoá `reloadRequired` (`frontend/src/catalogVerification.ts`). Xem mục Cut-over.

## Quyết định

Nguồn: brainstorm đã duyệt 2026-11-10; D-A, D-B là quyết định user sau red-team (2026-11-10), thay quyết định 1 và mở rộng quyết định 3 của brainstorm. "Lấp chỗ trống" là chi tiết không ai quyết, có lý do.

1. **D-A — `--accept-stated-count <pairs>/<namespaces>`** (CLI, không lưu ở đâu; không thêm field vào `command-catalog.json`). Cờ ghim đúng các số **sai** mà nó bỏ qua: câu đếm đầu tiên của §11 (L1351 "98 cặp, 12 namespace"), nên hôm nay là `98/12`. Bộ sinh vẫn đọc câu đầu tiên ("N cặp", "N namespace"; `generate-command-catalog.mjs:170-171`, :374-386).

   | Câu đếm đầu tiên so với bảng | Cờ | Kết quả |
   |---|---|---|
   | khớp | không | OK, không cảnh báo |
   | lệch | không | lỗi; gợi ý `--accept-stated-count <số prose>` |
   | bất kỳ | số khác số prose | lỗi `… does not match the count §11 states (p/n); update or remove the flag` |
   | khớp | đúng số prose | lỗi `… agrees with its action table; remove --accept-stated-count` |
   | lệch | đúng số prose | OK; một cảnh báo `… accepted by --accept-stated-count p/n` |
   | không còn câu đếm | bất kỳ | lỗi như hiện nay |

   Không thông báo lỗi nào in số row/namespace của bảng để chép (thông báo cũ `… its action table has 108 rows` cũng bỏ). Bảng được kiểm riêng: script độc lập (quyết định 4) và hash ghim. Cờ hết dùng được ngay khi câu đầu tiên đổi (sửa hoặc xoá): bộ sinh đòi sửa/bỏ cờ.
2. **Nguồn spec:** commit đầy đủ 40 hex; mọi lệnh git chạy `git --no-replace-objects -c core.fsmonitor=false -C <repo>` với env đã bỏ mọi biến tên `GIT_*` (chặn `refs/replace`, `GIT_DIR`, `GIT_OBJECT_DIRECTORY`, `GIT_ALTERNATE_OBJECT_DIRECTORIES`, `GIT_REPLACE_REF_BASE`, `GIT_CONFIG_*`…); kiểm blob. Phase 1 tách `readSpecAtCommit`, `gitCommand`, `run(argv, deps)` (export; `main` chỉ nối) và test đường CLI bằng git giả. HMS-IDF hôm nay không có `refs/replace`.
3. **Ghim `c80797d9d5b817174cd2cfbcb6ee62effe503ff2`:** `catalog_version` `mqtt-api-c80797d9d`; `source_blob` `84ff812f68567e07cfd3a2dfeeb862ceb48630f6` (= `ls-tree`); `source_hash` `8cde6ab454b7148bd29ab5a49c5187ec98b1143b4f11f84de76c95d9451c6406` (= sha256 của `git show`; cách này cho đúng `ef41c113…` của ghim cũ). **Giá trị kỳ vọng**, chỉ ghim khi bộ sinh tạo lại đúng nó và script độc lập đồng ý: `catalog_hash` `658e191a9e66f14a6885183b6ae9d9ec72a9e25a396c3cd30c80bdcd4df3413b`, pair-set `4286c13cd55ef43ca344ba98d07f7e99b98f111872d408d2afb97ec6017c2b07` (pair-set do extract regex dòng, không dùng code bộ sinh; cùng cách cho lại `dbda8317…` của ghim cũ).
4. **Kiểm §11 độc lập, có commit:** `scripts/verify-command-catalog-repin.mjs` (không import bộ sinh) + test. In số row, namespace, pair-set SHA; diff row giữa hai commit (thêm/đổi/xoá theo field); so từng field với `command-catalog.json`; `--expect-pair-set` để thoát 1 khi lệch. Thay script diff nháp ở scratchpad.
5. **Chặn đổi quyền row mở:** test có commit liệt kê 34 action mở với (class, quyền, TTL, confirm), sửa tay. Lần ghim này đổi đúng một dòng (`status.tags`). Người chỉ có `sys.info_view` mở đúng 8 action (`Assert.Equal`).
6. **`status.tags`:** quyền đổi chỉ qua catalog; row vẫn mở. Không migration tự cấp. Phase 3 viết báo cáo role. Spec `c80797d` đổi reply sang dạng trang (§6 L612; firmware 1.0.108 trên gateway vẫn trả mảng): follow-up, không thuộc plan. Comment `PinnedCommandCatalog.cs:44-49` sửa cho đúng bằng chứng hiện tại (bảng "API đã đóng…" L47: dạng trang `PASS_HOST`/`HIL_PASS`; receipt `SLICE_PASS` dạng mảng giữ làm lịch sử, L2129).
7. **D-B — high-risk:** thêm đúng `gpio.do_force`, `gpio.do_config`, `sms.send`, `sms.delete` vào `DevicePermissionCodes.HighRisk` (9 → 13 mã). `sms.view`, `tag.view`, `mb_rtu.*` giữ thường. Hệ quả (theo luật có sẵn `IsHighRisk` theo quyền của row): `/me/command-catalog` báo `highRisk:true` thêm 6 action `control.force_set`, `control.force_clear`, `control.set_config`, `sms.delete`, `sms.send`, `sms.send_status` (18 → 24, tính lại từ spec). High-risk **chỉ kiểm lúc ghi grant** (tạo/sửa/xoá role, gán/gỡ membership), không kiểm lúc gửi, không xét lại grant đã có: đổi phân loại về sau hoặc rollback phải chạy SQL tìm người đang giữ mã trước (Phase 3, `reports/affected-roles.md`).
8. **14 row mới đóng** (`DENIED_UNTIL_ACTION_CONTRACT`, kể cả master-admin). 4 allowlist của `PinnedCommandCatalog.cs` giữ nguyên (file chỉ sửa comment, quyết định 6); `OutputCommandPolicy`/`ConfigCommandPolicy` vẫn fail-closed cho B/W/C/D khác.
9. **Frontend không đổi code** (đã kiểm): không có danh sách quyền firmware hard-code (`DevicePermissionsTab.vue:33-40` chỉ 4 mã server); không đọc `status.tags`; `highRisk` không xuất hiện trong `frontend/src` ngoài test; version/hash lấy từ server. Phase 3 chỉ chạy hồi quy.
10. **Sửa một kỳ vọng test, không sửa code** (lấp chỗ trống): `Catalog_ttl_60000_rows_are_capped_to_the_command_ttl_limit` theo luật handler (Physical hoặc Flash → 10 s, `SendDeviceCommandHandler.cs:143-145`), vì 4 row B mới có TTL 60000.
11. **Kênh grant:** role/membership chưa có HTTP endpoint → test high-risk ở mức handler (tạo, sửa, xoá role; gán, gỡ membership). Kênh thứ hai có HTTP: device-action grant (`POST /api/v1/permissions/device-actions`, `PUT /api/v1/devices/{id}/permissions`) nhận chuỗi bất kỳ nhưng không mở lệnh nào (`DeviceAccessResolver.cs:23-35` chỉ đọc role membership) → integration test khoá điều này.

## Phases

| # | Phase | Priority | Status | Depends on | Effort |
|---|---|---|---|---|---|
| 1 | [Bộ sinh: `--accept-stated-count`, git an toàn, `run`, script kiểm độc lập](phase-01-generator-stated-count.md) | P2 | completed | — | 3h |
| 2 | [Ghim catalog `c80797d`, server và test](phase-02-catalog-repin-server.md) | P2 | completed | 1 | 3.5h |
| 3 | [Tài liệu, báo cáo role, diff §6, hồi quy frontend, E2E tab cũ](phase-03-docs-roles-report.md) | P2 | completed | 2 | 2h |

- **TDD:** mỗi phase viết test đỏ trước (lỗi biên dịch/import cũng tính là đỏ), rồi code tới khi xanh. Test cũ chỉ đổi ở đúng các assertion phase liệt kê.
- **File ownership** (chạy tuần tự, không song song):
  - Phase 1: `scripts/generate-command-catalog.mjs` (trừ hằng `SOURCE_COMMIT`), `scripts/generate-command-catalog.test.mjs`, `scripts/verify-command-catalog-repin.mjs` (mới), `scripts/verify-command-catalog-repin.test.mjs` (mới).
  - Phase 2: `scripts/generate-command-catalog.mjs` **chỉ hằng `SOURCE_COMMIT`** (L21 lúc lập plan), `src/Hms.Server.Infrastructure/Commands/command-catalog.json` (sinh ra, không sửa tay), `src/Hms.Server.Infrastructure/Commands/PinnedCommandCatalog.cs` (**chỉ comment** :44-49), `src/Hms.Server.Domain/Authorization/DevicePermissionCodes.cs`, test: `tests/Hms.Server.UnitTests/{CatalogTests,DevicePermissionCodesTests,DevicePermissionVocabularyTests,CommandPermissionResolverTests,DeviceCommandAuthorizationTests,DeviceAccessHandlerTests}.cs`, `tests/Hms.Server.IntegrationTests/ManagementApiTests.cs`. Hằng và JSON cùng một commit.
  - Phase 3: `docs/architecture-device-mqtt-api-catalog.md`, `docs/architecture-mqtt-device-link.md`, `docs/project-roadmap.md`, `docs/journals/261110-mqtt-catalog-sync.md` (mới), file trong plan này (gồm `reports/affected-roles.md`, `reports/open-actions-contract-diff.md`), `plans/reports/brainstorm-261108-1700-mqtt-spec-coverage-and-requests.md` (§3.B mục 8 + câu hỏi 20), `plans/261001-0030-full-hardware-mqtt-apis-c2/plan.md` (ghi chú frontmatter: `pending` → `completed`).
- Không đưa mã plan, số phase hay nhãn review vào comment code, tên test, commit message.
- Không mở, liệt kê, grep file nào trong `D:/WORKSPACE/PROJECT/HMS-IDF`; chỉ lệnh git (`rev-parse`, `ls-tree`, `branch --contains`, `for-each-ref refs/replace`, `show <rev>:engineering-docs/api/mqtt-api.md`), luôn kèm `--no-replace-objects`, và hai script (vốn chỉ đọc object git).

## Acceptance (toàn plan)

- **Bộ sinh:** `node scripts/generate-command-catalog.mjs --idf D:/WORKSPACE/PROJECT/HMS-IDF --accept-stated-count 98/12` ra catalog 108 row, in đúng 1 dòng `warning:`. Không cờ → exit 1 (`spec §11 states 98 pairs, which disagrees with its action table; …`), không in `108`. `--accept-stated-count 100/13` hoặc `108/13` → exit 1 `… does not match the count §11 states (98/12); update or remove the flag`. `--check --accept-stated-count 98/12` → up to date.
- **Nguồn spec:** `source_commit` = `c80797d9d5b817174cd2cfbcb6ee62effe503ff2`; `source_blob` = `git --no-replace-objects ls-tree c80797d …` = `84ff812f…`; `source_hash` = `8cde6ab4…`; mọi lệnh git có `--no-replace-objects` và env không `GIT_*` (test); `run` từ chối commit không đủ 40 hex trước khi gọi git (test).
- **Kiểm độc lập:** `verify-command-catalog-repin.mjs --rev c80797d… --base 83e93d77e… --expect-pair-set 4286c13c…` → 108 row, 13 namespace, thêm đúng 14, đổi đúng 1 (`status.tags` `required_perm`), xoá 0, 0 lệch với JSON; chạy với `--rev 83e93d77e…` trên JSON cũ → 94/12, `dbda8317…`, 0 lệch.
- **Catalog:** `catalog_hash` = `658e191a…` từ cả bộ sinh, JSON và loader .NET; metadata đổi đúng 5 field.
- **Row mở:** test 34 action mở xanh; so với trước chỉ đổi dòng `status.tags`.
- **API:** `GET /api/v1/me/command-catalog` 108 action, hash mới, `highRisk` true trên 24 action; master-admin `allowed-commands` đúng 34, không có row nào trong 14 row mới.
- **`status.tags`:** người chỉ có `sys.info_view` → `allowed-commands` đúng 8 action, gửi `status.tags` → `DEVICE_PERMISSION_REQUIRED`, không giữ chỗ audit; người có `tag.view` → `allowed-commands` = `["status.tags"]`, gửi → `COMMAND_KEY_NOT_PROVISIONED` (bằng code master-admin nhận), có giữ chỗ audit.
- **14 row mới:** master-admin gửi → `DENIED_UNTIL_ACTION_CONTRACT`, không giữ chỗ audit, không ký, không publish.
- **High-risk:** admin không phải master-admin tạo, sửa, xoá role, gán hoặc gỡ membership liên quan `gpio.do_force`/`gpio.do_config`/`sms.send`/`sms.delete` → `HIGH_RISK_REQUIRES_MASTER_ADMIN`; master-admin tạo được.
- **Device-action grant:** grant `gpio.do_write`, `gpio.do_force`, `sys.reboot` qua HTTP không thêm gì vào `allowed-commands`; gửi `control.set_output` vẫn `DEVICE_PERMISSION_REQUIRED`.
- **Test** (con số dự kiến, ghi số thật): .NET unit 658 → ~686; integration 127 → ~143 (chạy với `Mqtt__Enabled=false`); node 11 → ~31 (2 file); vitest 537 (không đổi); `vue-tsc --noEmit` sạch. Không số nào giảm so với mốc bước 0 mỗi phase.
- **E2E tab cũ (local, bắt buộc):** tab mở trước khi đổi server bấm một lệnh đọc → `CATALOG_STALE`, không ký, simulator không nhận lệnh; tải lại → lệnh đọc `Executed`. Bằng chứng trong `reports/live-verification.md`.
- **Docs:** ghim `c80797d` (chỉ trên nhánh feature), lệnh ghim lại có `--accept-stated-count 98/12` và lý do, khi nào bỏ cờ, R13/R15/R16, việc server phải làm trước khi mở row đóng, high-risk chỉ kiểm lúc grant + SQL, cut-over; báo cáo `reports/affected-roles.md`, `reports/open-actions-contract-diff.md`.

## Test matrix

| Lớp | Phủ |
|---|---|
| Node `generate-command-catalog.test.mjs` | câu đếm khớp; lệch + cờ đúng → 1 cảnh báo (kể cả layout thật: chỉ câu thứ hai đã sửa); lệch không cờ → lỗi không lộ số bảng; cờ sai số → lỗi; prose đã khớp mà còn cờ → lỗi "remove"; `parseArgs`; `readSpecAtCommit`; `gitCommand` (`--no-replace-objects`, env sạch); `run` (đúng 3 lệnh git, cảnh báo ra stderr trước stdout, `--check` + gợi ý cờ, chặn commit không đủ 40 hex trước git) |
| Node `verify-command-catalog-repin.test.mjs` | chỉ đọc bảng §11; map "Theo domain"/"—" → null; pair-set như helper .NET; diff thêm/đổi/xoá; so với catalog; git an toàn và không import bộ sinh |
| Unit .NET | `CatalogTests` (metadata, 108/13, class, TTL, confirm, 53 quyền, 15 row theo spec, 14 row đóng, `status.tags` mở + `tag.view`, **34 action mở**); từ vựng 57/53; `HighRisk` 13 mã; 24 row high-risk; 5 row confirm; cap TTL B/F; role high-risk 4 mã × (tạo/sửa/xoá role, gán/thay/gỡ membership) |
| Integration .NET (`ManagementApiTests`) | `/me/command-catalog` 108 + 24 high-risk; `allowed-commands` master-admin 34; `status.tags` theo `tag.view` (8 action của `sys.info_view`, `FindPlan`, code chính xác); 14 row đóng; device-action grant không mở lệnh |
| Vitest + `vue-tsc` | hồi quy, không test mới; `CATALOG_STALE` đã có test (`catalogVerification.test.ts:20`, `DeviceOutputsPanel.test.ts:218-230`, `useDeviceWrite.test.ts:46`, `rebootPlan.test.ts:254`) |
| CLI thật (HMS-IDF, chỉ object git) | các lần chạy ở Acceptance; script độc lập trên cả hai commit |
| E2E | **Bắt buộc** (phase 3 bước 6, local: Mosquitto 8088 + API + simulator + Vite): tab mở trước khi đổi server, bấm một lệnh đọc → `CATALOG_STALE`, simulator không nhận lệnh; tải lại → đọc chạy. Gateway thật ngoài phạm vi |

## Không hồi quy (giữ nguyên, có test)

- 34 action mở, class, quyền, TTL, confirm (trừ `status.tags`: quyền theo spec): test mới `Open_actions_keep_their_reviewed_class_permission_ttl_and_confirm`; `Pinned_catalog_opens_only_the_listed_reads_relay_outputs_config_writes_and_reboot` (`CatalogTests.cs:139-163`, 34 :157), `Monitoring_reads_are_open_…` (:135, 26 đọc), `Only_reboot_opens_among_flash_rows`, `Only_the_system_and_adc_config_writes_open_…`.
- `allowed-commands`: `Allowed_commands_lists_what_the_caller_may_send_on_this_device` (+ khoá 34), `Allowed_commands_follows_the_device_role_…` (`ManagementApiTests.cs:340-358`), `Config_writes_open_to_their_permission_holders_…` (:361-406), `Reboot_is_for_the_master_admin_only_…` (:409-458).
- Xác minh hash catalog: `Tampered_catalog_fails_to_load_with_hash_mismatch`, `Catalog_hash_must_be_lowercase_hex`, golden vector C#/JS, `Stale_client_catalog_is_rejected_before_transport` (`CommandBoundaryTests.cs:257`), `Device_command_requires_catalog_metadata_at_the_public_boundary` (`ManagementApiTests.cs:923`). Tab UI cũ: tải lại, không vỡ (vitest có sẵn).
- `HIGH_RISK_REQUIRES_MASTER_ADMIN` cho mã cũ: `DeviceAccessHandlerTests` :173-181, :184-189, :191-204, :566-581; `PhysicalOutputSafetyTests.cs:294`; `ManagementApiTests.cs:452`.
- Bộ sinh với spec ghim cũ: `--check` (không cờ) trên `83e93d77e` vẫn up to date ở cuối phase 1.

## Tương thích ngược

- **Client cũ của `/me/command-catalog`:** shape không đổi; thêm 14 action (`available:false`), `status.tags.requiredPermission` đổi, 6 action đổi `highRisk`.
- **Tab UI đang mở:** `CATALOG_STALE` trước khi ký → tải lại (hành vi có sẵn).
- **Role đang có:** role chỉ có `sys.info_view` mất `status.tags` (gửi, `allowed-commands`). Không migration (quyết định 6). 4 mã high-risk mới chỉ master-admin cấp/sửa/gỡ từ nay; grant đã có trước đó không bị xét lại (mã chưa có trong từ vựng trước lần ghim này nên hiện không role nào chứa chúng).
- **Dữ liệu:** không migration DB; catalog là embedded resource. Reply và trạng thái DO đã lưu mất khi khởi động lại (in-memory, `DependencyInjection.cs:42`), với mọi người và mọi lệnh; không phải hiệu ứng của quyền.
- **`tools/`:** đọc catalog qua API (`device-*-check.js`), `CLOSED_ROWS` của `device-reboot-check.js:35` vẫn đúng.

## Cut-over (deploy phase 2)

1. Catalog nhúng → build mới + khởi động lại API. Khởi động lại xoá store reply (`DependencyInjection.cs:42`, `DeviceLinkContracts.cs:435`), store DO, lane, theo dõi reboot (`docs/architecture-mqtt-device-link.md:546`, :675); lệnh chưa xong thành `OUTCOME_UNKNOWN_AFTER_RESTART` (`Program.cs:157-164`).
2. Chỉ deploy khi không có lệnh ghi DO, xung hay reboot đang chạy (không lệnh nào chờ reply; không reboot đang theo dõi).
3. Sau khi khởi động: tab cũ nhận `CATALOG_STALE` ở lệnh kế tiếp và phải tải lại. Lệnh ghi nào thành `OUTCOME_UNKNOWN_AFTER_RESTART`: đọc trạng thái thiết bị (`control.get_status`) trước khi thử lại, không gửi lại mù.
4. Reply đọc trước đó không còn (kết quả `Executed` không kèm dữ liệu): đọc lại.

## Rollback

- **Phase 1:** revert commit; chỉ script + test, catalog không đổi.
- **Phase 2:** revert code thôi là chưa đủ. Sau revert, 4 mã high-risk mới mất luật chỉ-master-admin, mà gán role không kiểm từ vựng (`DeviceAccessHandlers.cs:182-254`), `own` lấy từ quyền thô của role (`DeviceAccessResolver.cs:35`): admin thường đang giữ role chứa `gpio.do_force` gán được cho người khác, và membership đó còn nguyên khi ghim lại. Thứ tự:
  1. Trước revert: chạy SQL chỉ đọc (Phase 3, `affected-roles.md`) tìm role chứa một trong 7 mã mới và membership của chúng. Có kết quả → master-admin gỡ mã khỏi role (hoặc xoá role) trước.
  2. Revert một commit (hằng + JSON + comment + `HighRisk` + test) → về `83e93d77e`. Tab mang hash mới gặp `CATALOG_STALE` → tải lại. Cần commit `83e93d77e` còn truy cập được (cũng chỉ ở nhánh feature).
  3. Khi ghim lại: chạy lại SQL cho membership của role chứa mã high-risk, xem `GrantedBy`; master-admin rà các grant tạo trong cửa sổ revert.
  4. Mã lạ còn trong role sau revert không khớp quyền nào; lần sửa role sau bị `DEVICE_PERMISSION_UNKNOWN` tới khi bỏ mã.
  Hôm nay chưa có HTTP endpoint cho role/membership, nên cửa sổ này chỉ mở qua ghi thẳng vào DB/store.
- **Phase 3:** revert commit docs.

## Ngoài phạm vi

- Mở bất kỳ row mới nào (force DO, `get_config`/`set_config`, tags, Modbus theo trang, SMS): mỗi nhóm một plan. Catalog chỉ mang 9 field của bảng; nó không mang TTL trần, chỉ-primary hay posture. Trước khi mở, server phải tự làm:
  - **R13** (SMS, spec L417, L719): mọi `sms.*` chỉ qua broker primary; `sms.get`/`send`/`delete` cần posture signed và đồng hồ đã đồng bộ, TTL bảng là trần; `sms.list` cần posture signed.
  - **R15** (force DO, L419): chỉ primary, signed, đồng hồ đã đồng bộ, `ttl_ms` ≤ 60000; force giữ qua reboot.
  - **R16** (`control.set_config`, L420): như R15; quyền `gpio.do_config`.
  - **Server coi mọi row class B là relay:** audit giữ payload (`EfCommandAuditStore.cs:276-283`: số điện thoại, nội dung SMS; `sms.delete` class D cũng giữ), đánh stale trạng thái DO (`DeviceOutputState.cs:128-137`, `DeviceLinkHandlers.cs:139`), bỏ qua lane ghi (`CommandLanes.cs:10-11`), cap TTL 10 s (`SendDeviceCommandHandler.cs:143-145`), allowlist B duy nhất là `PhysicalOutputActions` (`PinnedCommandCatalog.cs:252`), `OutputCommandPolicy` viết cho payload relay. Phải tách trước khi mở `sms.send`, `control.force_*`, `control.set_config`.
- Reply dạng trang của `status.tags` (follow-up c).
- Nhóm B1/B2, §8, §9 (brainstorm §2). Sửa repo HMS-IDF hoặc câu đếm §11.

## Rủi ro

| Rủi ro | Khả năng × Ảnh hưởng | Giảm thiểu |
|---|---|---|
| Cờ thành thói quen, che trôi spec | Trung bình × Trung bình | Cờ ghim số prose sai: đổi câu đếm → bộ sinh đòi sửa/bỏ cờ; lỗi không in số bảng; bảng kiểm bằng script độc lập + hash ghim; cảnh báo in mỗi lần |
| Row mới lọt vào bảng mà câu đếm không đổi (cờ vẫn khớp) | Trung bình × Trung bình | Chủ đích D-A: cờ không kiểm bảng. Script độc lập in thêm/đổi/xoá; test 34 action mở; hash ghim; review |
| Parser bỏ sót/đọc sai row mà hash vẫn khớp chính nó | Thấp × Cao | Script độc lập regex dòng, không import bộ sinh; `--expect-pair-set`; so từng field với JSON |
| Đổi quyền một row mở lọt qua lần ghim sau | Thấp × Cao | Test 34 action mở sửa tay; `sys.info_view` đúng 8 action |
| `refs/replace` hoặc biến `GIT_*` làm bộ sinh đọc spec giả | Thấp × Cao | `--no-replace-objects`, env sạch, test; bước 0 phase 2 kiểm `for-each-ref refs/replace` rỗng |
| Commit ghim (`c80797d`, `83e93d77e`) chỉ ở nhánh feature; rebase/squash làm mất → không sinh lại, không `--check`, không rollback được bằng bộ sinh | Trung bình × Trung bình | Bước 0 ghi `branch -r --contains`, dừng nếu không nhánh remote nào chứa; docs ghi blob; follow-up (a): user xin team firmware tag commit ghim. `CatalogTests` vẫn ghim blob + sha256 |
| HMS-IDF HEAD đổi hoặc working tree đang sửa (session HIL) | Trung bình × Cao | Chỉ đọc object của sha 40 hex; kiểm blob |
| Cửa sổ rollback cho admin thường gán mã high-risk mới | Thấp × Cao | Mục Rollback: SQL trước và sau; hôm nay không có HTTP endpoint role |
| Đổi phân loại high-risk về sau không xét lại grant cũ | Trung bình × Trung bình | Docs + SQL tìm người giữ mã trước khi đổi; D-B thêm `sms.delete` ngay bây giờ |
| Device-action grant về sau bị nối vào phân quyền lệnh | Thấp × Cao | Integration test khoá "device-action grant không mở lệnh" |
| Deploy giữa lúc ghi DO → outcome không rõ, thử lại mù | Thấp × Trung bình | Mục Cut-over |
| Role mất quyền đọc tag | Thấp × Thấp | Báo cáo role; master-admin (hoặc admin giữ `tag.view`) cấp `tag.view` |
| Firmware 1.0.108 không có các row mới / `status.tags` vẫn dạng mảng | Cao × Thấp | Row mới đóng; reply không bị server/UI đọc theo field; follow-up (c) |

## Follow-up

| # | Việc | Chủ | Điều kiện xong |
|---|---|---|---|
| (a) | Xin team firmware tag (bảo vệ) các commit spec đã ghim (`c80797d`, `83e93d77e`) hoặc cam kết không rewrite nhánh. Việc liên team: plan không tự làm | User | Tag tồn tại; docs ghi tên tag |
| (b) | Gửi câu hỏi spec 8 (một câu đếm duy nhất) và 20 (§11 L1359 ghi "(R14)" cho luật SMS, đúng là R13) | User chuyển | Khi câu 8 có trả lời và L1351 đổi: bỏ `--accept-stated-count` |
| (c) | `status.tags` dạng trang (spec §6 L612, L927-929): server/UI chưa phân trang; gateway 1.0.108 còn mảng | Dev, plan riêng | Quyết định giữ mở theo dạng trang hoặc đóng |
| (d) | Trước khi mở row đóng: R13/R15/R16 + tách hành vi class B (mục Ngoài phạm vi) | Dev, mỗi nhóm một plan | Plan riêng |
| (e) | Tuỳ chọn: `CheckRoleChange`/gán role fail-closed với mã ngoài từ vựng hiện hành (thu hẹp cửa sổ rollback) | Dev | Cần plan riêng |

## Open questions / khác brainstorm

Không có câu hỏi mở chặn việc. Chỗ code/spec khác brainstorm (plan viết theo code và theo quyết định user mới):

1. **Câu đếm:** câu đầu §11 (L1351) là câu lịch sử "98 cặp, 12 namespace"; câu sau (L1352) "100 cặp, 13 namespace"; bảng 108/13. D-A (user, 2026-11-10) thay quyết định 1: cờ ghim cả hai số prose `98/12`. (Câu cũ trong plan "đúng chữ 'pair/namespace' của quyết định 1" sai: brainstorm không có chữ đó; đã bỏ.)
2. **Bộ sinh vốn không đọc working tree** (`generate-command-catalog.mjs:402-413`), nhưng git mặc định theo `refs/replace` và env kế thừa: thêm `--no-replace-objects` + env sạch.
3. **"`GET /me`" của brainstorm** là `GET /api/v1/me/command-catalog` (`MeController.cs:13`, :48).
4. **High-risk khi gán role** kiểm ở mức handler; kênh device-action grant có HTTP nhưng không mở lệnh (test khoá).
5. **Hệ quả của D-B:** `highRisk:true` trong catalog API trên 24 action (thêm `sms.send_status` là lệnh đọc, quyền `sms.send`; `sms.delete`).
6. **Kỳ vọng TTL của một test phải sửa** (quyết định 10); luật handler không đổi.
7. **Reply đã lưu:** mất khi khởi động lại (in-memory), với mọi người; bỏ câu cũ "role mất reply `status.tags` đã lưu".
8. **Báo cáo role:** seed dev không tạo device role (`Program.cs:58-87`), master-admin bỏ qua kiểm quyền thiết bị (`DeviceAccessPolicy.cs:37-38`). Báo cáo kèm SQL chỉ đọc; agent không đọc connection string.
9. **Frontend:** không có fixture hay code cần đổi.
10. **`sms.delete`** nay là high-risk (D-B mở rộng quyết định 3); `sms.view` giữ thường dù R13 gom `sms.get` cùng nhóm.
11. **§6 của row mở đã đổi:** 7 action mở có dòng §6 khác giữa hai commit; `status.tags` đổi sang dạng trang và receipt `SLICE_PASS` dạng mảng thành lịch sử. Phase 3 liệt kê; dạng trang là follow-up (c).
12. **Commit ghim chỉ trên nhánh feature** (`feat/next-work-20261019`, `claude/status-tags-paging`); không tag, không nằm trong `main`/`develop`.

## Red Team Review

Session 2026-11-10: 3 reviewer (security adversary, assumption destroyer, failure-mode analyst), báo cáo ở `reports/from-code-reviewer-to-planner-red-team-*-plan-review-report.md`. 22 mục thô (7 + 7 + 8) → 12 mục gộp: 0 Critical, 3 High, 9 Medium. User chấp nhận cả 12 và ra 2 quyết định (D-A, D-B).

| # | Finding | Severity | Disposition | Applied to |
|---|---|---|---|---|
| 1 | High-risk chỉ kiểm lúc grant; cửa sổ rollback; sai tên mã `DEVICE_PERMISSION_UNKNOWN`; thiếu test xoá role/gỡ membership; `plain` chưa seed | High | Accept (+ D-B) | Quyết định 7; Rollback; Phase 2 bước 1 (Theory role); Phase 3 báo cáo role |
| 2 | Commit ghim chỉ ở nhánh feature; mất thì không sinh lại/`--check`/rollback | High | Accept | Rủi ro; Follow-up (a); Phase 2 bước 0; Phase 3 docs |
| 3 | Không gì chặn đổi quyền row mở ở lần ghim sau | High | Accept | Quyết định 5; Phase 2 bước 1 (test 34 action, 8 action `sys.info_view`) |
| 4 | `--table-count` chỉ chép số tool vừa in; đọc câu lịch sử nên không bao giờ hết dùng | Medium | Accept → D-A | Quyết định 1; Phase 1 |
| 5 | "Đường thứ hai" cho pair-set chạy cùng parser; script diff bỏ ở scratchpad | Medium | Accept | Quyết định 3-4; Phase 1 (script độc lập); Phase 2 bước 4-6 |
| 6 | Git theo `refs/replace` và env kế thừa | Medium | Accept | Quyết định 2; Phase 1 (`gitCommand` + test); Phase 2 bước 0 |
| 7 | Test `readSpecAtCommit` không phủ đường CLI | Medium | Accept | Quyết định 2; Phase 1 (`run` + test) |
| 8 | Test `status.tags` phía dương chỉ là tập loại trừ; test danh tính cũ hard-code hash | Medium | Accept | Phase 2 bước 2 (`FindPlan`, code chính xác; bỏ test danh tính cũ) |
| 9 | Không so §6 của 34 action mở; `status.tags` đổi dạng; comment `SLICE_PASS` sai | Medium | Accept | Quyết định 6; Phase 2 (comment); Phase 3 (`open-actions-contract-diff.md`); Follow-up (c) |
| 10 | Không có cut-over; câu "mất reply `status.tags` đã lưu" sai | Medium | Accept | Mục Cut-over; Tương thích ngược; Phase 3 docs, báo cáo role |
| 11 | Trích sai R14-R16; catalog không mang TTL trần/primary/posture; class B bị coi là relay | Medium | Accept | Ngoài phạm vi; Follow-up (b), (d); Phase 3 docs, câu hỏi 20 |
| 12 | Kênh device-action grant có HTTP, không kiểm | Medium | Accept | Quyết định 11; Phase 2 bước 2 (integration test); Phase 3 báo cáo role |

### Whole-Plan Consistency Sweep

Đã rà plan.md + 3 phase sau khi sửa (2026-11-10):
- Tên cờ: chỉ còn `--accept-stated-count 98/12`; `--table-count` không còn trong yêu cầu, test hay docs của plan.
- High-risk: 13 mã, 4 mã mới, 24 row high-risk, danh sách ordinal giống nhau ở plan.md, Phase 2 (`DevicePermissionCodesTests`, `CommandPermissionResolverTests`, `ManagementApiTests`) và Phase 3.
- Giá trị ghim: commit/blob/sha256/catalog_hash/pair-set giống nhau ở frontmatter, quyết định 3, Acceptance, Phase 2.
- Số test: node 11 → 31 (25 + 6), unit 658 → ~686, integration 127 → ~143; Phase 1 bước 3, Phase 2 bước 8, Phase 3 bước 4, Acceptance khớp nhau.
- Mã lỗi `DEVICE_PERMISSION_UNKNOWN` ở Rollback, Phase 2, Phase 3.
- R13/R15/R16 (không còn "R14-R16"); câu hỏi 20 ở Follow-up và Phase 3.
- Câu "mất reply `status.tags` đã lưu" đã bỏ ở Luồng dữ liệu, Tương thích ngược, Open questions, Phase 3.
- File ownership: `PinnedCommandCatalog.cs` (chỉ comment) thuộc Phase 2; 2 script mới thuộc Phase 1; không file nào thuộc hai phase trừ `generate-command-catalog.mjs` (Phase 2 chỉ hằng `SOURCE_COMMIT`, chạy sau Phase 1).
- Ghi chú frontmatter plan C2 đã đổi theo cờ mới và 4 mã.
- Kết quả: nhất quán. Không còn mâu thuẫn trong plan. Số mục thô đã sửa thành 22 (7 + 7 + 8; coordinator ghi nhầm 21 lúc trình user).
- Hai gợi ý reviewer không lấy: (1) chặn gán role chứa mã ngoài từ vựng hiện hành: là thay đổi code, để Follow-up (e); (2) lỗi khi §11 có nhiều câu đếm: mâu thuẫn D-A (cờ buộc vào câu đầu). Hệ quả chấp nhận: cờ không bắt được row bảng mới; script kiểm độc lập, test 34 action và hash ghim bắt việc đó.

## Validation Log

### Session 1 — 2026-11-10
**Trigger:** user chọn validate sau red-team.
**Questions asked:** 3. Bước kiểm code bỏ qua: Red Team Review đã có bằng chứng file:line; không còn tag `[UNVERIFIED]`.

#### Questions & Answers

1. **[Scope]** Spec c80797d đổi reply status.tags sang dạng trang (firmware 1.0.108 còn trả mảng). Plan đồng bộ catalog xử lý status.tags thế nào?
   - Options: Giữ mở, phân trang là follow-up | Đóng status.tags tới khi có phân trang | Thêm phân trang vào plan này
   - **Answer:** Giữ mở, phân trang là follow-up
   - **Rationale:** `EnabledActions` giữ 34; dạng trang là follow-up (c). Khi gateway nâng firmware, thẻ đọc hiện trang đầu dạng thô.
2. **[Risks]** Commit spec ghim (c80797d) chỉ nằm trên nhánh feature của HMS-IDF. Có chờ team firmware tag commit trước khi ghim không?
   - Options: Làm ngay, tag sau | Chờ có tag mới ghim
   - **Answer:** Làm ngay, tag sau
   - **Rationale:** Phase 2 bước 0 vẫn dừng nếu không còn nhánh remote nào chứa commit; blob + sha256 ghim trong test/docs; follow-up (a) chạy song song.
3. **[Assumptions]** Sau khi ghim catalog, có bắt buộc kiểm tay trên máy local: tab UI mở trước khi đổi server → bấm một lệnh đọc → thấy yêu cầu tải lại?
   - Options: Bắt buộc, chạy local | Giữ tuỳ chọn
   - **Answer:** Bắt buộc, chạy local
   - **Rationale:** Đường `CATALOG_STALE` qua UI thật chỉ có test đơn vị; ghim hash là lần đầu đổi catalog từ khi có UI mới.

#### Confirmed Decisions
- `status.tags`: giữ mở với `tag.view`; phân trang là follow-up (c).
- Ghim `c80797d` ngay; xin tag song song (follow-up a).
- E2E tab cũ trên local: bắt buộc (phase 3 bước 6).

#### Action Items
- [x] Phase 3 bước 6: "Tuỳ chọn" → "Bắt buộc", môi trường local, bằng chứng trong `reports/live-verification.md`.
- [x] Phase 3 Success Criteria: thêm mục E2E.
- [x] plan.md: Acceptance thêm mục E2E; Test matrix dòng E2E; effort phase 3 1.5h → 2h, tổng 8h → 8.5h.

#### Impact on Phases
- Phase 3: thêm E2E bắt buộc (bước 6, Success Criteria, effort). Phase 1, 2: không đổi.

### Whole-Plan Consistency Sweep
- "Tuỳ chọn" cho E2E không còn ở plan.md (Test matrix, Acceptance) hay phase 3 (bước 6, Success Criteria). Chữ "Tuỳ chọn" còn lại chỉ ở Follow-up (e), không liên quan.
- Effort khớp: frontmatter plan 8.5h = 3 + 3.5 + 2; frontmatter phase 3 = 2h; bảng Phases = 2h.
- `status.tags` giữ mở ở Quyết định 6, Acceptance, Follow-up (c), Rủi ro: khớp câu trả lời 1. Ghim ngay: Rủi ro + Follow-up (a) + Phase 2 bước 0: khớp câu trả lời 2.
- Không còn mâu thuẫn.

## Kết quả

Xong 2026-11-10. Chưa commit.

- **Catalog:** ghim `c80797d9d5b817174cd2cfbcb6ee62effe503ff2`, version `mqtt-api-c80797d9d`, 108 row, 13 namespace, hash `658e191a…3413b`. 14 row mới đóng; số action mở vẫn 34. `status.tags` đòi `tag.view`. HighRisk 13 mã.
- **Test:** build 0 lỗi, 3 warning (như mốc). Unit 686, integration 143, node 33, vitest 537, `vue-tsc` sạch. Chi tiết: `reports/tester-regression-report.md` (ghi node 32, trước khi thêm test cho L1).
- **Kiểm độc lập:** `scripts/verify-command-catalog-repin.mjs --rev c80797d… --base 83e93d77e…` → 108 row, 0 lệch so với `command-catalog.json`.
- **E2E tab cũ (local):** PASS. Tab cũ nhận `CATALOG_STALE`, không ký, simulator không nhận lệnh; tải lại → 108/13, `system.ping` `Executed`. Sai lệch: `status.services` không có trong simulator nên `Executed` chứng minh bằng lệnh khác. Phiên dev phải đăng nhập lại sau khi restart API (khoá ephemeral). Xem `reports/live-verification.md`.
- **Code review:** không có blocker, đủ tiêu chí (a)–(f), 5/5 mutant bị bắt. Xem `reports/code-review-report.md`.
  - L1 (checker bỏ qua dòng §11 lệch khuôn): **đã sửa**, có test.
  - M1 (class B/D không còn chỉ là relay; audit giữ payload SMS): điều kiện trước khi mở `sms.*`, `control.force_*`, `control.set_config`. Đã ghi ở `docs/architecture-mqtt-device-link.md` và Follow-up (d).
  - Chưa làm: L2 (checker không so `source_commit`/blob/hash/`config_domains`), L3 (`parseArgs`/`main` chưa có test), L4 (`DeviceCommandAuthorizationTests.cs:215-219` tính expected bằng chính predicate đang test).
- **Ghi nhận ngoài phạm vi:** dialog C2 hiện `CATALOG_STALE` dạng mã thô, không gợi ý tải lại; comment cũ ở `CommandPermissionResolver.cs:42`.
- **Việc của user:** Follow-up (a) tag commit spec bên HMS-IDF; (b) chuyển câu hỏi spec 8 và 20.
