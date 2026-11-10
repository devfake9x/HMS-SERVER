---
phase: 1
title: "Bộ sinh: --accept-stated-count, git an toàn, run(), script kiểm §11 độc lập"
status: completed
priority: P2
effort: 3h
dependencies: []
---

# Phase 1: Bộ sinh và script kiểm độc lập

## Overview

`scripts/generate-command-catalog.mjs` từ chối spec `c80797d` vì câu đếm đầu tiên của §11 lệch bảng. Phase này:
1. cờ `--accept-stated-count <pairs>/<namespaces>` (D-A, quyết định 1 của `plan.md`);
2. git an toàn: `--no-replace-objects`, env bỏ `GIT_*` (quyết định 2);
3. tách `run(argv, deps)` để test được đường CLI bằng git/fs giả; `main` chỉ nối;
4. script mới `scripts/verify-command-catalog-repin.mjs` tách row §11 không dùng code bộ sinh (quyết định 4).

Không đổi hằng `SOURCE_COMMIT` (phase 2 đổi cùng JSON), không chạm `src/`, `tests/`.

## Hiện trạng (đã đọc khi lập plan)

- `main` (L392-444) chỉ đọc object git: L395-398 `execFileSync('git', ['-C', idfRepo, ...args])` với env kế thừa; L402 `rev-parse --verify <commit>^{commit}`, L407 so bằng, L409 `rev-parse <commit>:<path>`, L410 `git show`, L411-413 sha1 bytes = blob. Git mặc định theo `refs/replace`: một replace ref làm cả blob id lẫn bytes cùng đổi nên kiểm blob vẫn qua (red-team đã thử trên repo nháp). HMS-IDF hôm nay không có `refs/replace`.
- `assertStatedCounts` (L374-386) đọc kết quả **đầu tiên** của `PAIR_COUNT` (L170) và `NAMESPACE_COUNT` (L171). Thông báo L378 in số row của bảng. Spec `c80797d` L1351 (câu lịch sử): "98 cặp, 12 namespace"; L1352: "100 cặp, 13 namespace"; bảng 108 row, 13 type. Ghim cũ `83e93d77e`: "94 cặp, 12 namespace", khớp bảng.
- `parseArgs` (L446-463, chưa export): `--check`, `--idf <p>`, `--idf=<p>`.
- `parseSpecManifest` (L177-204) trả `{ commands, config_domains }`; caller: `main` L415 và test.
- `main` không export, chỉ chạy khi `isEntryPoint()` (L488-506); test duy nhất chạm CLI kiểm import không chạy gì (test.mjs:344-351).
- Test: `node --test scripts/generate-command-catalog.test.mjs` → **11 pass** (chạy lúc lập plan). Không CI nào chạy bộ sinh; không có `package.json` ở root (`CatalogTests.cs:26-29`).

## Requirements

### `parseArgs` (export)
- Nhận `--accept-stated-count <p>/<n>` và `--accept-stated-count=<p>/<n>`; giá trị khớp `/^([1-9]\d*)\/([1-9]\d*)$/` → `options.acceptStatedCount = { pairs, namespaces }` (số); mặc định `undefined`. Sai hoặc thiếu → lỗi
  `--accept-stated-count needs <pairs>/<namespaces>, the counts the first §11 count sentence states`. Không kiểm cờ lặp (giống `--idf`).
- Comment đầu file (L7-13): thêm `[--accept-stated-count <pairs>/<namespaces>]` và hai câu: cờ nhận đúng số mà câu đếm đầu tiên của §11 ghi khi câu đó lệch bảng; đổi câu đó (sửa hay xoá) thì bộ sinh đòi sửa hoặc bỏ cờ.

### `parseSpecManifest(markdown, { acceptStatedCount } = {})`
- Trả `{ commands, config_domains, warnings }` (`warnings: string[]`). `catalogHash`/`canonicalize`/`renderCatalog` không đổi (chỉ đọc 2 bảng).
- `assertStatedCounts(prose, commands, accept)` trả mảng cảnh báo. `p`, `n` = câu đếm đầu tiên; `rows`, `types` = của bảng. Thứ tự:
  1. Thiếu "N cặp" hoặc "N namespace" → lỗi như hiện nay (L376, L381), kể cả khi có cờ.
  2. Có cờ và (`accept.pairs !== p` hoặc `accept.namespaces !== n`) → lỗi
     `--accept-stated-count ${ap}/${an} does not match the count §11 states (${p}/${n}); update or remove the flag`.
  3. Không cờ, `p !== rows` → lỗi `spec §11 states ${p} pairs, which disagrees with its action table; review the table, then rerun with --accept-stated-count ${p}/${n}`;
     không cờ, `n !== types` → lỗi `spec §11 states ${n} namespaces, which disagrees with its action table; review the table, then rerun with --accept-stated-count ${p}/${n}`.
  4. Có cờ (đã khớp prose) và prose khớp bảng → lỗi
     `spec §11 states ${p} pairs and ${n} namespaces, which agrees with its action table; remove --accept-stated-count`.
  5. Có cờ, prose lệch bảng → một cảnh báo
     `spec §11 states ${p} pairs and ${n} namespaces, which disagrees with its action table; accepted by --accept-stated-count ${p}/${n}`.
- Không thông báo nào in `rows` hay `types`. Regex test cũ (`/states 7 pairs/`, `/states 5 namespaces/`, `/pair count/`, `/namespace count/`) vẫn khớp.

### Git an toàn
- `gitCommand(repo, args, env = process.env)` (export, thuần): trả
  `{ file: 'git', args: ['--no-replace-objects', '-c', 'core.fsmonitor=false', '-C', repo, ...args], env }` với `env` là bản sao bỏ mọi khoá khớp `/^GIT_/i`; không sửa object đầu vào.
- `runGit(repo, args)`: `execFileSync(file, args, { env, maxBuffer: 16 MiB, stdio: ['ignore','pipe','pipe'] })` theo `gitCommand`.

### `readSpecAtCommit(git, commit = SOURCE_COMMIT, specPath = SOURCE_PATH)` (export)
- `git` là `(...args) => Buffer`. `commit` không khớp `/^[0-9a-f]{40}$/` → lỗi `pinned spec commit must be a full 40-hex commit id, found ${commit}` trước mọi lời gọi git.
- Gọi đúng 3 lệnh theo thứ tự: `['rev-parse','--verify',`${commit}^{commit}`]`, `['rev-parse',`${commit}:${specPath}`]`, `['show',`${commit}:${specPath}`]`. Resolve khác → lỗi như L407; bytes lệch blob → lỗi như L412. Trả `{ bytes, sourceBlob }`.

### `run(argv, deps = {})` (export); `main` chỉ nối
- `deps`: `git = runGit` (dạng `(repo, args) => Buffer`), `fs` (`existsSync`, `readFileSync`, `writeFileSync`; mặc định `node:fs`), `stdout = console.log`, `stderr = console.error`, `env = process.env`, `commit = SOURCE_COMMIT`.
- Làm đúng việc `main` hiện làm (L393-443): `parseArgs` → repo (`--idf`, rồi `env.HMS_IDF_REPO`, rồi mặc định) → `readSpecAtCommit((...a) => git(repo, a), commit)` (bọc lỗi lệnh git đầu tiên với gợi ý `--idf`/`HMS_IDF_REPO` như L404-405) → `parseSpecManifest(text, { acceptStatedCount })` → mỗi cảnh báo `stderr('generate-command-catalog: warning: ' + w)` **trước** mọi output stdout → ghi file hoặc `--check`. `--check` lệch → stderr gợi ý lệnh chạy lại kèm `--accept-stated-count p/n` nếu đã truyền, trả 1; khớp → stdout "up to date", trả 0. Lỗi → ném; `main`: `process.exitCode = run(argv)` trong try/catch hiện có (L499-506).
- Export thêm `SOURCE_COMMIT`, `SOURCE_PATH`, `OUTPUT_PATH`. Giá trị `SOURCE_COMMIT` không đổi ở phase này.

### Script kiểm độc lập `scripts/verify-command-catalog-repin.mjs` (mới)
- Header: lý do tồn tại (kiểm parser của bộ sinh bằng một cách đọc khác); **không import** `generate-command-catalog.mjs` (chép riêng phần git an toàn, chủ ý không DRY).
- Export thuần:
  - `extractActionRows(markdown)`: chuẩn hoá NFC, CRLF → LF; chỉ các dòng từ dòng bắt đầu `## 11. ` tới dòng `## ` kế tiếp; mỗi dòng khớp
    ``^\| `([A-Za-z0-9_]+)\.([A-Za-z0-9_]+)` \| ([RWCDBF]) \| `([^`]+)` \| (\d+) \| (Có|Không) \| `([^`]+)` \|$`` → `{ type, action, class, required_perm, ttl_ms, dry_run, confirm_template }`; `Theo domain (bảng sau)` → `required_perm: null`; `—` → `confirm_template: null`; trùng khoá → lỗi.
  - `pairSetSha256(rows)`: sắp `(type, action)` ordinal, `JSON.stringify([{type, action}, …])`, sha256 hex (cùng thuật toán `CatalogTests.PairSetSha256`, `CatalogTests.cs:561-570`).
  - `diffRows(base, head)` → `{ added, removed, changed: [{ key, fields: { tên: [cũ, mới] } }] }`, sắp ordinal.
  - `compareWithCatalog(rows, catalog)` → danh sách chuỗi lệch: row thiếu trong catalog, row thừa, khác `class`, `required_perm`, `domain_policy` (= `required_perm === null`), `ttl_ms`, `dry_run`, `confirm_template`, `confirm_required` (= template khác null).
  - `gitCommand` (bản riêng, cùng luật), `readSpecAt(git, rev)` (chặn rev không đủ 40 hex trước git; `rev-parse --verify`, `show <rev>:engineering-docs/api/mqtt-api.md`).
- CLI: `node scripts/verify-command-catalog-repin.mjs --rev <sha40> [--base <sha40>] [--idf <repo>] [--catalog <path>] [--expect-pair-set <hex>]`. In: rev, số row, số namespace, pair-set SHA; nếu có `--base`: danh sách thêm/xoá/đổi (kèm field cũ → mới); số lệch với catalog (mặc định file `command-catalog.json` đã commit) và từng dòng lệch. Thoát 1 khi có lệch catalog, pair-set khác `--expect-pair-set`, hoặc tham số sai.

## Related Code Files

- **Modify:** `scripts/generate-command-catalog.mjs` (L7-13, L177-204, L374-386, L392-506; không đụng hằng `SOURCE_COMMIT`), `scripts/generate-command-catalog.test.mjs`.
- **Create:** `scripts/verify-command-catalog-repin.mjs`, `scripts/verify-command-catalog-repin.test.mjs`.
- **Không sửa:** `src/`, `tests/`, `command-catalog.json`.

## Implementation Steps

0. **Mốc** (ghi vào `reports/phase-01-implementation-report.md`):
   - `node --test scripts/generate-command-catalog.test.mjs` → mong 11 pass.
   - `git status --short src/Hms.Server.Infrastructure/Commands/` rỗng.
   - `node scripts/generate-command-catalog.mjs --idf D:/WORKSPACE/PROJECT/HMS-IDF --check` → "up to date (hash b1d47a78…)" (ghim cũ; chỉ đọc object git).
1. **Test đỏ, bộ sinh** (`scripts/generate-command-catalog.test.mjs`). Fixture mới:
   - `STALE_COUNTS = mutateSpec('6 cặp, 4 namespace trong snapshot.', 'Snapshot cũ có 5 cặp, 3 namespace. Kiểm lại: 7 cặp, 4 namespace trong snapshot.')` (câu đầu cũ, như spec L1351-1352).
   - `SECOND_FIXED = mutateSpec('6 cặp, 4 namespace trong snapshot.', 'Snapshot cũ có 5 cặp, 3 namespace. Hiện tại: 6 cặp, 4 namespace trong snapshot.')` (chỉ câu thứ hai đã sửa cho khớp bảng).
   - Bảng fixture: 6 row, 4 namespace. Git giả: hàm ghi lại `[repo, args]`, trả bytes fixture và blob tính bằng `sha1('blob <len>\0' + bytes)` trong test.

   Test mới (14):
   - `parseSpecManifest returns no warnings when the stated counts match the table`: `parseSpecManifest(SPEC_FIXTURE).warnings` deepEqual `[]`.
   - `parseSpecManifest accepts the stated counts named by --accept-stated-count, and warns`: `STALE_COUNTS` và `SECOND_FIXED` với `{ acceptStatedCount: { pairs: 5, namespaces: 3 } }` → `commands`/`config_domains` deepEqual kết quả của `SPEC_FIXTURE`; `warnings` deepEqual đúng
     `['spec §11 states 5 pairs and 3 namespaces, which disagrees with its action table; accepted by --accept-stated-count 5/3']`; cảnh báo không khớp `/\b6\b|\b4\b/`.
   - `parseSpecManifest rejects stated counts that disagree with the table when no flag names them`: `STALE_COUNTS` → message bằng đúng
     `spec §11 states 5 pairs, which disagrees with its action table; review the table, then rerun with --accept-stated-count 5/3` và không khớp `/\b6\b|\b4\b/`;
     chỉ lệch namespace (`'6 cặp, 5 namespace'`) → throws `/states 5 namespaces, which disagrees with its action table; review the table, then rerun with --accept-stated-count 6\/5/`.
   - `parseSpecManifest rejects an --accept-stated-count that is not what §11 states`: `STALE_COUNTS` với `7/4` (câu thứ hai), `6/4` (số bảng), `5/4`, `98/12` → mỗi ca throws `/does not match the count §11 states \(5\/3\); update or remove the flag/`; `SPEC_FIXTURE` với `5/3` → throws `/does not match the count §11 states \(6\/4\)/` (prose đã đổi mà cờ còn).
   - `parseSpecManifest asks to remove --accept-stated-count once §11 agrees with its table`: `SPEC_FIXTURE` với `6/4` → throws `/which agrees with its action table; remove --accept-stated-count/`; bỏ câu đếm (`'6 cặp, 4 namespace trong snapshot.'` → `'Snapshot.'`) với `5/3` → throws `/pair count/`.
   - `parseArgs reads --accept-stated-count as <pairs>/<namespaces>`: `['--accept-stated-count','98/12']`, `['--accept-stated-count=98/12']` → `{ pairs: 98, namespaces: 12 }`; `[]` → `undefined`; thiếu giá trị, `'98'`, `'98/'`, `'/12'`, `'0/12'`, `'98/0'`, `'098/12'`, `'98:12'`, `'98/12/1'`, `'-1/2'`, `'1e2/3'`, `'a/b'` → throws `/--accept-stated-count needs <pairs>\/<namespaces>/`; `['--check','--idf','x','--accept-stated-count','9/2']` → `{ check: true, idf: 'x', acceptStatedCount: { pairs: 9, namespaces: 2 } }`.
   - `readSpecAtCommit reads the spec only from git objects of the pinned commit`: lời gọi deepEqual 3 lệnh ở Requirements với `SOURCE_COMMIT`/`SOURCE_PATH`; `bytes` bằng fixture; `SOURCE_COMMIT` khớp `/^[0-9a-f]{40}$/`; `SOURCE_PATH === 'engineering-docs/api/mqtt-api.md'`.
   - `readSpecAtCommit refuses bytes that do not hash to the commit blob`: → throws `/textconv or filter/`.
   - `readSpecAtCommit refuses a commit id that is abbreviated, symbolic or resolves elsewhere`: `'HEAD'`, `'c80797d'`, sha 40 hex viết hoa → throws `/full 40-hex commit id/`, git giả không được gọi; sha 40 hex mà `rev-parse --verify` trả sha khác → throws `/resolved to/`.
   - `gitCommand reads without replace objects and without inherited GIT_ variables`: `gitCommand('R', ['show','a:b'], { PATH:'p', HOME:'h', GIT_DIR:'x', git_object_directory:'y', GIT_ALTERNATE_OBJECT_DIRECTORIES:'z', GIT_REPLACE_REF_BASE:'r', GIT_CONFIG_COUNT:'1' })` → `file 'git'`, `args` deepEqual `['--no-replace-objects','-c','core.fsmonitor=false','-C','R','show','a:b']`, `env` deepEqual `{ PATH:'p', HOME:'h' }`; object đầu vào không đổi.
   - `run writes the catalog from the pinned commit through three git object reads`: `run(['--idf','R'], deps)` → 0; lời gọi git deepEqual `[['R',[…3 lệnh…]]]`; đúng một lần ghi vào `OUTPUT_PATH`, JSON có `source_commit === SOURCE_COMMIT`, `source_blob` = blob giả, `catalog_version === 'mqtt-api-' + SOURCE_COMMIT.slice(0, 9)`; stdout dòng đầu bắt đầu `wrote`. Không `--idf`, `env.HMS_IDF_REPO = 'E'` → repo `'E'`.
   - `run prints the accepted count warning on stderr before anything on stdout`: git giả trả `STALE_COUNTS`, argv `['--accept-stated-count','5/3']`; một mảng log chung cho stdout/stderr → dòng `stderr` `generate-command-catalog: warning: spec §11 states 5 pairs and 3 namespaces, …` đứng trước mọi dòng `stdout`; trả 0.
   - `run --check reports a stale catalog with the rerun hint and writes nothing`: lấy text từ một lần chạy ghi; `--check` với fs trả text khác → 1, stderr chứa `--accept-stated-count 5/3`, không ghi; fs trả đúng text (và bản CRLF) → 0, stdout chứa `is up to date`, không ghi.
   - `run refuses a commit that is not a full id before calling git`: `deps.commit = 'HEAD'` rồi `'c80797d'` → throws `/full 40-hex commit id/`, git giả không được gọi.
   - Đỏ vì: import `parseArgs`, `readSpecAtCommit`, `gitCommand`, `run`, `SOURCE_COMMIT`, `SOURCE_PATH`, `OUTPUT_PATH` chưa export (lỗi link module, cả file đỏ); sau khi export: `warnings` `undefined`, option bị bỏ qua.
2. **Test đỏ, script kiểm** (`scripts/verify-command-catalog-repin.test.mjs`, fixture riêng có bảng mồi ở §10/§12 và bảng domain trong §11):
   - `extractActionRows reads only the §11 action table`: đúng các row §11, đếm row và namespace; bảng mồi và bảng domain không lọt vào.
   - `extractActionRows maps the by-domain permission and the empty confirm to null`: `config.get` → `required_perm: null`; `—` → `null`; `Có` → `dry_run: true`; bản NFD và CRLF cho cùng kết quả.
   - `pairSetSha256 hashes the sorted pairs like the server test helper`: row lộn thứ tự (có `Zeta.b`, `a.x`, `a.Y`) → bằng sha256 của chuỗi viết tay `[{"type":"Zeta","action":"b"},{"type":"a","action":"Y"},{"type":"a","action":"x"}]`.
   - `diffRows lists added, removed and changed rows by field`.
   - `compareWithCatalog reports missing, extra and differing rows`: catalog dựng tại chỗ; giống hệt → `[]`; khác `required_perm`, thiếu row, thừa row, `confirm_required` lệch template → mỗi cái một dòng.
   - `the checker reads git without replace objects or inherited GIT_ variables and does not import the generator`: `gitCommand` như test bộ sinh; `readSpecAt(git, 'HEAD')` throws trước khi gọi git; nguồn của script (đọc file) không khớp `/from\s+['"][^'"]*generate-command-catalog/`.
   - Đỏ vì: file script chưa có.
3. **Code** theo Requirements.
4. **Xanh:**
   - `node --test scripts/generate-command-catalog.test.mjs scripts/verify-command-catalog-repin.test.mjs` → 11 cũ + 14 + 6 = **31 pass**; không sửa assertion cũ nào.
   - `node scripts/generate-command-catalog.mjs --accept-stated-count 0/1` → exit 1 `--accept-stated-count needs …` (lỗi ở `parseArgs`, chưa gọi git).
   - Ghim cũ không hồi quy: `node scripts/generate-command-catalog.mjs --idf D:/WORKSPACE/PROJECT/HMS-IDF --check` → up to date, không cảnh báo; thêm `--accept-stated-count 94/12` → exit 1 `… which agrees with its action table; remove --accept-stated-count`.
   - Script kiểm trên ghim cũ: `node scripts/verify-command-catalog-repin.mjs --rev 83e93d77e4a433efce665d669b567b3acc51b735 --expect-pair-set dbda8317758e5f25c5a4dee8456841ef3689fea4707cf5a27c30ffad656f78d9` → 94 row, 12 namespace, 0 lệch với `command-catalog.json` hiện tại, exit 0. Ghi output vào report.
   - `git status --short src/` rỗng.

## Success Criteria

- [ ] D-A đủ 5 ca (khớp / lệch không cờ / cờ sai số / prose đã khớp mà còn cờ / lệch + cờ đúng), có test; lỗi không in số của bảng.
- [ ] Spec có câu đếm khớp: hành vi và output như cũ.
- [ ] Mọi lệnh git của hai script có `--no-replace-objects`, `-c core.fsmonitor=false`, env không `GIT_*` (test).
- [ ] `run` là đường CLI duy nhất; test bằng git/fs giả: đúng 3 lệnh git, cảnh báo ra stderr trước stdout, `--check` + gợi ý, chặn commit không đủ 40 hex trước git.
- [ ] Script kiểm không import bộ sinh; trên ghim cũ cho lại `dbda8317…`, 94/12, 0 lệch.
- [ ] `src/` không đổi; `SOURCE_COMMIT` vẫn `83e93d77e…`.

## Risk Assessment

| Rủi ro | Khả năng × Ảnh hưởng | Giảm thiểu |
|---|---|---|
| Đổi return shape làm hỏng test cũ hoặc hash | Thấp × Trung bình | `orderedTables` chỉ đọc 2 field; test CRLF/NFD so cả object nên `warnings: []` hai bên; golden vector không đổi |
| Cờ nuốt cả lỗi khác (thiếu câu đếm, bảng hỏng) | Thấp × Cao | Cờ chỉ thay bước 3-5; bước 1 và mọi kiểm bảng chạy như cũ; có test |
| Bỏ biến `GIT_*` làm git trên Windows không chạy | Thấp × Thấp | Git for Windows không cần biến `GIT_*` để đọc repo cục bộ; bước 4 chạy thật trên ghim cũ |
| Script kiểm lệch cách đọc với bộ sinh trên spec tương lai | Trung bình × Thấp | Đó là mục đích: lệch thì dừng, xem bằng mắt; cả hai cho cùng kết quả trên hai commit hiện có |
| Hai bản `gitCommand` trôi khác nhau | Thấp × Thấp | Cùng một test hình thức ở cả hai file |

Rollback: revert commit; catalog không đổi.

## As built

Khớp plan, trừ:
- Test script kiểm: 7 test (thêm `extractActionRows refuses a duplicate row and a document without section 11`), tổng node 11 → 32 (11 + 14 + 7), không phải 31.
- `diffRows`: `added`/`removed` là mảng row đầy đủ (không chỉ khoá); `changed` đúng `[{ key, fields }]`.
- `readSpecAt` (script kiểm) không kiểm blob (đúng plan), chỉ kiểm commit resolve và 40 hex.
- Kiểm c80797d trên `command-catalog.json` cũ (chưa ghim lại) cho 15 lệch (14 thiếu + `status.tags`), exit 1: đúng kỳ vọng trước phase 2.
- Bản red của bộ sinh được ghi lại trên bản HEAD copy ở scratchpad (sau khi code đã viết): lỗi import `OUTPUT_PATH`.
