# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

When asked about the codebase, project structure, or to find code, ALWAYS use the `codebase-retrieval` MCP tool (server `codebase-retrieval`) in the root workspace FIRST, before reading individual files. Use `codebase-retrieval` instead of Grep/Glob or the Explore subagent for codebase exploration and search tasks. The tool has a deferred schema — call `ToolSearch` (`select:mcp__codebase-retrieval__codebase-retrieval`) to load it before the first call.

When you need to read a specific file but don't know the exact line range, use the file-retrieval MCP tool instead of reading the entire file. Describe what information you need and it returns only the relevant snippets with line numbers. Use the Read tool with the returned line ranges (expanded as needed) to get current content before making edits.

# Admin Monorepo — Agent Routing & Conventions

> File này là **dispatcher** cho mọi AI agent làm việc với repo `admin/`. Khi nhận task: scan bảng routing → đọc đúng section trong README sub-project. **KHÔNG đoán + KHÔNG sửa khi chưa hiểu pattern.**

## Layout

```
admin/
├── AGENTS.md               ← bạn đang đọc — routing + meta rules (Code Search)
├── CLAUDE.md               ← symlink → AGENTS.md (Claude Code & generic AI agent đọc cùng)
├── Makefile                ← dev orchestration (infra + services); path service = be-mono/services/*
├── infra/                  ← docker-compose (Postgres, Redis, Redpanda/Kafka, Sequin CDC, OTel/SigNoz)
├── be-mono/                ← Bun workspace — BE monorepo (đối xứng fe-mono); 1 bun.lock, catalog, scope @reecotech/*
│   ├── package.json        ← workspaces: services/* + packages/* · catalog (hono/drizzle/zod/...)
│   ├── tsconfig.base.json  ← config gốc; mỗi service/package `extends`
│   ├── services/
│   │   ├── auth/           ← @reecotech/auth — Hono auth service (:3001)
│   │   ├── background-service/ ← @reecotech/background-service — BullMQ/Kafka consumer (:3002)
│   │   └── admin-operator/ ← @reecotech/admin-operator — Hono backend (Tier 3 + register pattern)
│   │       └── README.md   ← BE architecture, conventions, RPC, take notes
│   └── packages/
│       └── shared/         ← @reecotech/shared — authz (Casbin), events (CDC/Kafka), telemetry (OTel)
└── fe-mono/                ← pnpm workspace — FE monorepo (React 19 + antd 6 + Vite+)
    ├── README.md           ← stack, lệnh, checklist tạo app FE mới
    ├── apps/
    │   └── business-management/ ← Console quản trị nội bộ doanh nghiệp (business self-admin)
    │       └── README.md        ← FE setup, registry pattern, checklist thêm feature, lệnh vp
    └── packages/
        ├── ui-core/        ← antd 6 theme tokens (design tokens, KHÔNG override CSS)
        ├── services/       ← axios DI factory (`configureClient`/`getClient`), generic middlewares
        ├── schemas/        ← Zod schemas dùng chung (FE validation)
        ├── types/          ← TypeScript types/interfaces dùng chung
        ├── constants/      ← Hằng số dùng chung
        ├── socket/         ← Socket.IO client wrapper (`autoConnect: false`)
        └── telemetry/      ← OTel tracing/metrics khởi tạo
```

> **BE monorepo:** auth/background-service/admin-operator + shared đã gom vào `be-mono/` (Bun workspace, scope `@reecotech/*`, 1 `bun.lock`, catalog pin version chung). `shared` là dependency thật `@reecotech/shared` (`workspace:*`), import qua tên — KHÔNG còn alias `../shared/*`. be-mono (Bun) tách biệt fe-mono (pnpm).

## Quick Routing — Task nào đọc đâu

### Backend (`be-mono/services/`)

| Task | File | Section |
|---|---|---|
| **Catalog domain** (taxonomy industries/product-categories/languages + problems/solutions/systems/products + public API) — Ở **admin-operator**, KHÔNG phải auth | `be-mono/services/admin-operator/README.md` | **Catalog Domain — plan 260707-1443** (D1 schema ownership, D2 2-journal, D5 actor model, D7 publish-state ptr, D13 public) |
| Catalog engines generic (publish/revert/translate/attach/slug/bulk/duplicate) | `be-mono/services/admin-operator/README.md` | **Catalog-core engines** + `src/lib/catalog/` |
| **Product config templates** (thay `product_type`) — template đa-mẫu/đa-ngôn-ngữ theo category; product đa-category; attributes namespaced theo `templateId` PER-LOCALE | `be-mono/services/admin-operator/README.md` | **Product Config Templates — plan 260708-1243** (REFERENCE/DIST, GM CRUD `:categoryId/config-templates`, reference `?categoryIds=`, `product-types` XOÁ→404) |
| **Product variants** (SKU cho tổ hợp option; template item + inventory ref variant thay `option_ids`/`variant_code`; giá đa-level auto/override; single-primary made real) — ride `bm:product:*` | `be-mono/services/admin-operator/README.md` | **Product Variants — plan product-variants** (4 bảng DIST colocate products, immutability `VARIANT_IN_USE`, resolve order + `MISSING_BASE_PRICE`, `lib/catalog/option-pricing.ts`, migrations 0031-0033) |
| Permission `bm:*`/`gm:*` cho catalog route mới | `be-mono/services/admin-operator/README.md` | **D5 — requireCatalogPermission**; seed migration ở AUTH (authz = auth domain) |
| Citus gotcha khi viết query/migration catalog | `be-mono/services/admin-operator/README.md` | **Take Notes — Catalog/Citus gotchas** (co-located join, unique thiếu dist-key) |
| Indexing worker (queue `catalog-index`) | `be-mono/services/background-service` — driver `CatalogIndexer` noop | README admin-operator **D9** |
| Setup, install, run dev/typecheck/test | `be-mono/services/admin-operator/README.md` | **Quick Start** |
| Hiểu kiến trúc Tier 3 + register pattern | `be-mono/services/admin-operator/README.md` | **Kiến trúc — Tier 3 + Register Pattern** |
| Thêm route/endpoint mới (vd `books`) | `be-mono/services/admin-operator/README.md` | **Template — Thêm feature `books`** (Tier 3, createHandlers) |
| Thêm middleware mới | `be-mono/services/admin-operator/README.md` | **Decision Tree → Middleware**, file `src/middleware/index.middleware.ts` |
| Đổi error handling / shape | `be-mono/services/admin-operator/README.md` | **Take Notes #8 (ApplyGlobalResponse)** + file `src/errors/index.error.ts` |
| RPC client setup, `AppType`, `ApplyGlobalResponse` | `be-mono/services/admin-operator/README.md` | **RPC — Type-safe client cho FE** |
| Validation với Zod (`zValidator`) | `be-mono/services/admin-operator/README.md` | Template `books` — `validators.ts` |
| Tách handler ra file riêng | `be-mono/services/admin-operator/README.md` | Template `books` — `handlers.ts` + **Take Notes #9 (createHandlers)** |
| Env var mới | `be-mono/services/admin-operator/README.md` | **Take Notes #6** + file `src/config/env.ts` |
| tsconfig flags gì? | `be-mono/services/admin-operator/README.md` | **tsconfig — Lý do từng flag** |
| Trước khi PR | `be-mono/services/admin-operator/README.md` | **Review Checklist** |
| Đụng vào `src/index.ts`? | ❌ **FROZEN** — đọc `be-mono/services/admin-operator/README.md` § *Composition root* trước khi quyết định |
| `c.notFound()` hoặc `(c: Context) =>` plain? | ❌ **anti-pattern** — đọc `be-mono/services/admin-operator/README.md` § *❌ KHÔNG làm* + *Take Notes #9* |

### Frontend (`fe-mono/`)

> `business-management` (trước đây là `auth-ui`) đã move vào `fe-mono/apps/business-management` (phase 03). KHÔNG còn `auth-ui/` ở root.

#### App: `fe-mono/apps/business-management/`

| Task | File | Section |
|---|---|---|
| Vite+ CLI commands (`vp dev/build/check/test`) | `fe-mono/apps/business-management/README.md` | **Vite+ — toolchain duy nhất** |
| Hiểu registry pattern (routes/services/schemas/stores) | `fe-mono/apps/business-management/README.md` | **Kiến trúc — Registry Pattern** |
| Thêm màn hình/feature mới | `fe-mono/apps/business-management/README.md` | **Checklist thêm feature mới (5 bước)** |
| Sidebar/menu mới | `fe-mono/apps/business-management/README.md` § Registry — sidebar derive từ `src/routes/index.ts`, KHÔNG hardcode menu |
| Code-split / lazy route | `fe-mono/apps/business-management/README.md` § Registry — route dùng `lazy`, `handle`+`path` giữ static; feature `index.ts` CHỈ export `*Routes` (đừng re-export page) |
| Ẩn/hiện UI theo quyền | `fe-mono/apps/business-management/src/hooks/use-can-hint.ts` — `useCanHint`/`canHint` đọc `/me`; default chưa-có-data = hiện tất cả (server enforce thật) |
| Thêm axios interceptor app-specific | `fe-mono/apps/business-management/src/axios/middlewares/index.ts` (mảng đăng ký) |
| Theme/màu sắc | `fe-mono/packages/ui-core/src/theme/` — CHỈ antd 6 design tokens, KHÔNG override CSS |
| Gọi BE API | `fe-mono/apps/business-management/src/services/` (axios qua `@/axios`) — endpoint bám backend plan `260518-1629` |

#### Packages: `fe-mono/packages/`

| Task | Package | Nguyên tắc |
|---|---|---|
| Thêm/sửa antd 6 design tokens, theme màu sắc | `ui-core/src/theme/` | CHỈ design tokens, KHÔNG override CSS; app import `@reecotech/ui-core/styles.css` |
| Thêm generic axios middleware (dùng nhiều app) | `packages/services/` — `configureClient`/`getClient` | Middleware generic (không biết `VITE_*`); env inject qua config object, KHÔNG `import.meta.env` trong package |
| Axios DI trong app mới | `fe-mono/apps/<app>/src/axios/` | Gọi `configureClient(api)` một lần khi boot; dùng `getClient()` call-time ở service — KHÔNG giữ ref import-time (C2) |
| Zod schemas dùng chung | `packages/schemas/` | Import vào cả FE app lẫn package khác; KHÔNG hardcode domain-specific shape ở đây |
| TypeScript types/interfaces chung | `packages/types/` | Shared types; app-specific types để trong app |
| Hằng số chung | `packages/constants/` | Enum, const string, v.v.; KHÔNG import env |
| Socket.IO client | `packages/socket/` | `autoConnect: false`; app tự `.connect()` sau auth |
| OTel tracing/metrics | `packages/telemetry/` | Gọi `initTelemetry(cfg)` ở entrypoint app; cfg truyền từ `import.meta.env` trong app |
| Tạo FE app mới trong workspace | `fe-mono/README.md` | Checklist 7 dep `@reecotech/* workspace:*` + compose axios + `.env*` gitignore (C3) |

### ⚠️ Mandatory FE Page Rules (tuân theo 100%, KHÔNG ngoại lệ)

> Các quy tắc dưới đây là **BẮT BUỘC** cho mọi FE app trong `fe-mono/`. Vi phạm = reject PR.

1. **List page → PHẢI dùng `ListPageLayout`** (from `@reecotech/ui-core`).
   - Mọi trang danh sách (list/table/grid) PHẢI wrap trong `<ListPageLayout>`.
   - KHÔNG tự build header/title/action bar riêng — dùng props `title`, `description`, `actions`, `search`, `filters` của `ListPageLayout`.
   - Import: `import { ListPageLayout } from "@reecotech/ui-core";`

2. **Detail/Form page → PHẢI dùng `DetailPageLayout`** (from `@reecotech/ui-core`).
   - Mọi trang chi tiết (detail view, tabbed detail, form/create/edit) PHẢI wrap trong `<DetailPageLayout>`.
   - KHÔNG tự build page header + breadcrumb + tabs riêng — dùng props `title`, `description`, `actions`, `breadcrumbLeaf` (`ReactNode | BreadcrumbTrailItem[]`), `tabs` của `DetailPageLayout`.
   - Import: `import { DetailPageLayout } from "@reecotech/ui-core";`

3. **Buttons action → PHẢI dùng antd v6 `variant="solid"`; `color` theo chức năng**.
   - Action chính không nguy hiểm (Tạo, Lưu, Cập nhật, Xuất bản, Liên kết, Nhân bản, v.v.) → `color="primary" variant="solid"`.
   - Action nguy hiểm (Xoá, Gỡ, v.v.) → `color="danger" variant="solid"`.
   - Cảnh báo / chú ý (khi cần) → `color="orange" variant="solid"` (hoặc semantic color phù hợp) — vẫn `variant="solid"`.
   - KHÔNG dùng `variant="filled"` (tonal) hay `type="primary"` cho action chính. Action phụ (Huỷ, Bỏ chọn, filter) → default / outlined / text / link.
   - Ví dụ: `<Button color="primary" variant="solid" icon={<SaveOutlined />}>Lưu</Button>`

4. **Action của tab → PHẢI nằm trên TAB BAR, KHÔNG nhồi toolbar bên trong nội dung tab**.
   - TUYỆT ĐỐI KHÔNG design nội dung tab kiểu "label (đếm) bên trái + nút action bên phải" nằm đối xứng nhau BÊN TRONG tab content (vd `Hạng mục (3)` ... `[+ Thêm hạng mục]`).
   - KHÔNG lặp lại tiêu đề/đếm của tab bên trong tab content — tab label đã nói rồi.
   - State của drawer/modal "tạo mới" do PAGE giữ và truyền xuống component con dạng controlled prop (`addOpen`/`onAddClose`); state "sửa" giữ trong component con.
   - Vi phạm = reject PR.

```tsx
// ✅ ĐÚNG — List page
import { ListPageLayout } from "@reecotech/ui-core";

export const BooksPage = () => (
  <ListPageLayout
    title="Sách"
    actions={
      <Button color="primary" variant="solid" icon={<PlusOutlined />}>
        Tạo sách
      </Button>
    }
  >
    <BooksTable />
  </ListPageLayout>
);

// ✅ ĐÚNG — Detail page: solid + color theo chức năng
import { DetailPageLayout } from "@reecotech/ui-core";

export const BookDetailPage = () => (
  <DetailPageLayout
    title={book.name}
    actions={
      <>
        <Button color="primary" variant="solid" icon={<EditOutlined />}>Sửa</Button>
        <Button color="danger" variant="solid" icon={<DeleteOutlined />}>Xoá</Button>
      </>
    }
    tabs={tabs}
  />
);

// ❌ SAI — KHÔNG tự build layout
export const BooksPage = () => (
  <Flex vertical>
    <PageHeader title="Sách" />  {/* ← KHÔNG ĐƯỢC */}
    <BooksTable />
  </Flex>
);

// ✅ ĐÚNG — action của tab nằm trên TAB BAR (DetailTab.actions)
const tabs: DetailTab[] = [
  {
    key: "chapters",
    label: "Chương",
    actions: canWrite ? (
      <Button color="primary" variant="solid" icon={<PlusOutlined />} onClick={() => setAddOpen(true)}>
        Thêm chương
      </Button>
    ) : undefined,
    children: <ChaptersTable addOpen={addOpen} onAddClose={() => setAddOpen(false)} />, // ← nội dung tab TRẦN
  },
];

// ❌ SAI — toolbar "label (đếm) ↔ nút action" nhồi BÊN TRONG nội dung tab
export const ChaptersTab = () => (
  <Flex vertical gap={12}>
    <Flex justify="space-between" align="center">      {/* ← KHÔNG ĐƯỢC */}
      <Typography.Text strong>Chương ({chapters.length})</Typography.Text>
      <Button icon={<PlusOutlined />}>Thêm chương</Button>
    </Flex>
    <ChaptersTable />
  </Flex>
);
```

---

### Cross-cutting

| Task | Reference |
|---|---|
| Code search / khám phá codebase | § **Code Search Strategy** dưới (`codebase-retrieval` MCP) |
| Git workflow (branch/commit) | § **Git Conventions** dưới |
| Secret/token mới | Đặt trong `.env` (gitignored). KHÔNG commit. KHÔNG để trong code. |
| Thêm dependency BE | `cd be-mono && bun add <pkg> --filter @reecotech/<svc>` (Bun workspace; KHÔNG chạy ở root admin) |
| Thêm dependency FE (app) | `cd fe-mono/apps/business-management && vp install <pkg>` (Vite+ — KHÔNG bun/npm/pnpm trực tiếp trong app) |
| Thêm dependency FE (package) | `cd fe-mono && pnpm add <pkg> --filter @reecotech/<package-name>` |
| Version dùng chung BE (hono/drizzle/zod...) | `be-mono/package.json` § `catalog` — sửa 1 chỗ, service ref bằng `"catalog:"` |
| Install toàn bộ be-mono | `cd be-mono && bun install` (1 `bun.lock`) |
| Install toàn bộ fe-mono | `cd fe-mono && pnpm i` |

## Git Conventions

### Branch model
```
main      ← production, PROTECTED (không force-push)
develop   ← integration, feature branch off từ đây
<type>/<short-description>  ← feature branches off từ develop
```

### Branch name prefix
| Prefix | Dùng cho |
|---|---|
| `feat/` | Feature mới (vd `feat/auth-login`) |
| `fix/` | Bug fix (vd `fix/cors-credentials`) |
| `docs/` | Chỉ docs (vd `docs/consolidate-agents-to-root`) |
| `chore/` | Maintenance, deps, config (vd `chore/bump-hono-v5`) |
| `refactor/` | Refactor không đổi behavior (vd `refactor/extract-auth-middleware`) |

### Commit message — Conventional Commits
```
<type>(<scope>): <description>

[body]
```
- **type**: `feat`, `fix`, `docs`, `chore`, `refactor`, `test`, `style`, `perf`
- **scope**: `admin-operator`, `fe`, `monorepo` (omit nếu cross-cutting)
- Ví dụ:
  - `feat(admin-operator): add users CRUD endpoint`
  - `fix(fe): correct CORS origin in api client`
  - `docs: consolidate agent docs at monorepo root`
  - `chore(admin-operator): bump @hono/zod-validator to 0.9`

### Workflow
1. `git checkout develop && git pull` — từ develop mới nhất.
2. `git checkout -b <type>/<name>` — tạo feature branch.
3. Code + commit (Conventional Commits).
4. `git push -u origin <type>/<name>`.
5. Mở MR trên GitLab → review → merge vào `develop`.
6. Định kỳ release: merge `develop` → `main`.

## Code Search Strategy — `codebase-retrieval` MCP Tool

> Tool này dùng **embedding-based semantic search** (vector search) — KHÔNG phải keyword match.
> Nó match theo ý nghĩa ngữ cảnh, nên cách viết query quyết định chất lượng kết quả.

### Nguyên tắc vàng

1. **Ngắn gọn, cụ thể, 1 ý duy nhất per query** — KHÔNG nhồi nhiều câu hỏi vào 1 query.
2. **Chứa tên symbol/pattern thật** — tên function, class, schema, file path nếu biết.
3. **Chỉ rõ scope (service/layer)** — repo này có BE + FE cùng dùng terminology giống nhau (`accounts`, `departments`, `roles`...). KHÔNG scope → kết quả có thể trả FE component thay vì BE route.
4. **KHÔNG viết dạng câu lệnh dài** kiểu "Find the function that validates X and also handles Y in the context of Z" — semantic search bị phân tán.

### ✅ Query tốt (DO)

```
# Tìm schema validator cụ thể
"ListAccountsQuerySchema departmentId zod validator"

# Tìm route handler với middleware keyword
"Hono route /admin/departments requireAuth requireScopedPermission"

# Tìm service function cụ thể
"adminService.listAccounts filter departmentId"

# Tìm middleware/guard logic
"requireStepUp verifyStepUpToken middleware auth service"

# Tìm DB schema/table
"departments table drizzle schema businessId distributed"

# Tìm test pattern
"admin departments routes test 401 403 permission"

# Tìm trong scope rõ ràng
"token refresh logic axios interceptor business-management"
```

### ❌ Query tệ (DON'T)

```
# Quá dài, nhiều ý — semantic search bị confuse
"Find the admin-operator backend endpoint for listing accounts (GET /api/v1/admin/accounts) with departmentId query parameter validation, and the departments endpoint. I need to see the Zod validator schema and how empty string is handled."

# Quá chung chung — match nhiều thứ không liên quan
"accounts list endpoint"

# Mô tả hành vi thay vì tên code thật
"the function that checks if user has permission to access department"

# Không có scope — FE và BE đều có "accounts", "departments"
"GET /admin/accounts route handler"
```

### Workflow khuyến nghị

1. **Lần đầu tiên cần tìm code**: dùng `codebase-retrieval` với query ngắn + scope.
2. **Nếu kết quả không đúng**: thử lại với tên symbol cụ thể hơn hoặc thêm service scope.
3. **Sau khi biết file path**: dùng `file-retrieval` hoặc Read tool trực tiếp.
4. **Tìm ALL occurrences** (exhaustive): dùng Grep — `codebase-retrieval` trả top-K, KHÔNG exhaustive.
5. **Trước khi edit**: LUÔN Read file đầy đủ (hoặc đoạn liên quan) để xác nhận state hiện tại.

### Khi nào KHÔNG dùng `codebase-retrieval`

| Tình huống | Dùng tool gì |
|---|---|
| Tìm TẤT CẢ references của 1 symbol | **Grep** (`pattern: "symbolName"`) |
| Đã biết file path, cần đọc nội dung | **Read** hoặc **file-retrieval** |
| Tìm file theo tên/pattern | **Glob** (`pattern: "**/accounts/**"`) |
| Cần xem directory structure | **Read** mode Directory |

### Đặc thù repo này (cùng terminology FE/BE)

Repo `admin/` có FE và BE dùng CÙNG tên domain: `accounts`, `departments`, `roles`, `permissions`.
Nếu query không scope → vector search hay trả **FE component** (vì FE file thường dài hơn, mention
keyword nhiều lần → embedding score cao hơn BE route file ngắn).

**Luôn thêm scope** khi tìm BE code:
- `"Hono route ..."` (framework keyword giúp disambiguate)
- Tên schema/function thật: `"ListAccountsQuerySchema"`, `"requireScopedPermission"`

## Take Notes — Drizzle Migration

- **Custom migration = PHẢI thêm `_journal.json`**: Drizzle chỉ apply migration có entry trong `meta/_journal.json`. Tạo file `.sql` thủ công mà quên thêm journal entry → `db:migrate` bỏ qua → schema drift → runtime crash. Luôn verify: số entry journal = số file `.sql` (không tính `.reverse.sql`).
- **Snapshot không bắt buộc cho `migrate`**: `drizzle-kit migrate` chỉ cần journal + SQL. Snapshot (`meta/XXXX_snapshot.json`) chỉ cần cho `drizzle-kit generate` (tính diff).

## Take Notes — Drizzle + Citus Migration

- **Dùng `migrate.ts` thay `drizzle-kit migrate`**: drizzle-kit CLI có bug (#5601) nuốt SQL error, chỉ hiện NOTICE → exit(1) không diagnostic. Script `be-mono/services/auth/migrate.ts` dùng drizzle-orm programmatic migrate + postgres.js `onnotice: () => {}` → hiện error thật, chạy ổn trên Citus.
- **Breakpoint bắt buộc**: Mỗi statement trong `.sql` PHẢI ngăn cách bằng `--> statement-breakpoint`. Không có → drizzle-orm gom toàn bộ file thành 1 prepared statement → Citus worker reject "cannot insert multiple commands into a prepared statement".
- **KHÔNG inline REFERENCES trong CREATE TABLE**: Citus emit NOTICE khi FK tới REFERENCE table → drizzle-kit crash. Tách FK ra `ALTER TABLE ADD CONSTRAINT` riêng.
- **1 ADD CONSTRAINT per ALTER TABLE**: Citus reject multi-subcommand ALTER TABLE. Mỗi FK = 1 ALTER TABLE riêng.
- **Distribute TRƯỚC FK**: Gọi `create_distributed_table()` TRƯỚC `ALTER TABLE ADD FK` tới distributed table. Nếu không → Citus reject "FK from local to distributed not supported".
- **Co-locate khi có FK composite tới distributed table**: `create_distributed_table('new_table', 'business_id', colocate_with => 'target_table')`. Không co-locate → Citus reject "not colocated". Trỏ tới bất kỳ table nào cùng colocation group đều OK.
- **ON DELETE SET NULL/SET DEFAULT bị cấm khi distribution key trong FK**: Dùng `NO ACTION` hoặc `CASCADE` thay thế.
- **Identity column + Citus = DDL hạn chế**: Table có `GENERATED AS IDENTITY` trên Citus không thể `undistribute_table()` hay DROP FK trong một số trường hợp. Dùng `DO $$ BEGIN ... EXCEPTION WHEN duplicate_object THEN NULL; END $$;` để FK migration idempotent.
- **`permissions` table schema**: columns = `code, resource, action, description`. KHÔNG có `name`, `app_scope`.
- **Test-harness Citus worker-registration PHẢI best-effort (single-node degrade)**: `migrate-test-db.ts` gọi `citus_add_node('citus_worker_1'/'citus_worker_2', 5432)`. Trên CI (ephemeral multi-node) hostname resolve; trên dev cluster đơn-node hostname KHÔNG resolve → `could not translate host name` làm ABORT toàn bộ bootstrap → mọi test của service đó fail. PHẢI bọc `citus_set_coordinator_host` + `pg_dist_authinfo` + `citus_add_node` trong `try/catch` nuốt lỗi `/could not translate host name|failed with the following error|shards are still/` rồi fallback `citus_set_coordinator_host('localhost')` (mẫu chuẩn: `admin-operator/src/tests/helpers/migrate-test-db.ts`; auth harness đã đồng bộ theo). Lưu ý: các test topology multi-worker (`auth .../citus-topology.test.ts`: `citus_get_active_worker_nodes ≥ 2`, shard placement 2 worker) VẪN fail single-node — đó là hạn chế môi trường, không phải regression code.

### Template migration Citus chuẩn — thứ tự statement

```sql
-- 1. CREATE TYPE
CREATE TYPE "my_enum" AS ENUM ('a', 'b');
--> statement-breakpoint

-- 2. CREATE TABLE — KHÔNG inline REFERENCES
CREATE TABLE "my_table" (
  "id" uuid DEFAULT gen_random_uuid() NOT NULL,
  "business_id" uuid NOT NULL,
  CONSTRAINT "my_table_pkey" PRIMARY KEY ("business_id", "id")
);
--> statement-breakpoint

-- 3. CREATE INDEX
CREATE INDEX "my_table_biz_idx" ON "my_table" ("business_id");
--> statement-breakpoint

-- 4. Distribute — TRƯỚC FK, co-locate nếu cần FK composite tới distributed table
SELECT create_distributed_table('my_table', 'business_id', colocate_with => 'departments');
--> statement-breakpoint

-- 5. FK — mỗi cái 1 ALTER TABLE riêng, NO ACTION nếu dist key trong FK
ALTER TABLE "my_table"
  ADD CONSTRAINT "my_table_business_id_fk"
  FOREIGN KEY ("business_id") REFERENCES "businesses"("id") ON DELETE CASCADE;
--> statement-breakpoint

-- 6. INSERT seed (đúng column names: code, resource, action, description)
INSERT INTO "permissions" ("code", "resource", "action", "description")
VALUES ('bm:thing:read', 'thing', 'read', 'Read things')
ON CONFLICT ("code") DO NOTHING;
```

---

## Take Notes — Multi-language UI (Translation Tabs)

Khi entity hỗ trợ đa ngôn ngữ (translations), FE **PHẢI call API lấy danh sách languages** rồi render tabs động — KHÔNG hardcode tab `vi`/`en`.

**Pattern:**
1. Gọi `useLanguagesListQuery()` → lấy danh sách languages từ BE (**chỉ `active`** — xem lưu ý dưới)
2. Render `<Tabs>` với `items` map từ languages array
3. Mỗi tab = 1 locale, chứa các field dịch (name, description, ...)
4. **Trim `lang.code`** trước khi dùng làm key (DB `char(5)` pad trailing spaces)

```tsx
import { Tabs } from "antd";
import { useLanguagesListQuery } from "@/hooks";

const { data: languages } = useLanguagesListQuery();

{languages && languages.length > 0 && (
  <Tabs
    items={languages.map((lang) => {
      const code = lang.code.trim(); // ← BẮT BUỘC trim
      return {
        key: code,
        label: lang.nativeName,
        children: (
          <>
            {/* Fields đa ngôn ngữ cho locale này */}
            <Input name={`translations.${code}.name`} />
            <Input.TextArea name={`translations.${code}.description`} />
          </>
        ),
      };
    })}
  />
)}
```

**Lưu ý:**
- **TUYỆT ĐỐI KHÔNG render tab cho language `inactive`.** BE `GET /admin/languages` mặc định trả **TẤT CẢ** language (kể cả inactive) — đây là chủ ý, vì màn quản lý Languages cần thấy inactive. Phòng tuyến nằm ở FE hook: `useLanguagesListQuery()` đã mặc định `{ status: "active" }` (`business-management/src/hooks/use-languages-queries.ts`, `global-management/src/hooks/use-languages.ts`) → **cứ dùng hook, KHÔNG tự gọi service**. Chỉ màn quản lý Languages dùng `useAllLanguagesListQuery()` (GM). Render tab cho locale đã tắt = user nhập nội dung không bao giờ publish được.
- Số tab = số languages **active** API trả về (dynamic, không hardcode)
- Key dùng `lang.code.trim()` (tránh `"vi   "` padding)
- Fields không cần dịch (categoryCode, slug, status) đặt **bên ngoài** tabs
- Fields cần dịch (name, description) đặt **bên trong** mỗi tab
- **Surface do CALLER cung cấp, KHÔNG lồng card**: `TranslationTabsField` (ui-core) render tab + field trần (không tự bọc `<Card>`). Caller đặt nó trong đúng 1 `<Card>` để có nền trắng + padding — create page dùng Card "Nội dung đa ngôn ngữ", detail tab dùng `<Card>` riêng. Tránh nested card (card lồng card = anti-pattern).

> UI/UX work must read `openspec/ui-dna.md` (or the existing DNA doc) before any visual change.
