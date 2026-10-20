---
name: auth-migration-runner-and-enum-conventions
description: auth service drizzle migrate runner wraps ALL pending migrations in ONE txn; statement-breakpoint is NOT a commit; ALTER TYPE ADD VALUE + use will fail
metadata:
  type: project
---

auth service (`be-mono/services/auth`) drizzle migration execution facts — verified 2026-06-22 while red-teaming plan `260622-multi-tenant-auth-tenant-seam`:

- **Migrate runner wraps ALL pending migrations in a SINGLE transaction.** `drizzle-orm/pg-core/dialect.js` `migrate()` does `session.transaction(async tx => { for migration { for stmt { tx.execute(stmt) } } })`. So every pending migration file AND every `--> statement-breakpoint` statement runs in ONE txn.
- **`--> statement-breakpoint` is only a string `split()`** (base `migrator.js`), NOT a commit. It does not separate transactions.
- **Consequence:** `ALTER TYPE ... ADD VALUE` followed by any use of the new value in the same migrate run → Postgres `unsafe use of new value`. A standalone migration file does NOT fix this on a fresh DB (all pending files apply together in one txn).
- Test runner: `src/tests/helpers/migrate-test-db.ts` uses `drizzle-orm/postgres-js/migrator` (same dialect.migrate), runs via bunfig `[test].preload`, and `process.exit(1)` on failure → migration failure = 0 tests run, red CI with minimal diagnostic.
- Prod runner: `make db-migrate` → `bun run db:migrate` → `drizzle-kit migrate` (also txn-wrapped).

**Why:** these facts falsify the common plan assumption "statement-breakpoint makes ADD VALUE safe". **How to apply:** any plan adding an enum value (e.g. account_type 'admin') OR doing data backfill that USES a new enum value must commit the ADD VALUE in a strictly-earlier release, or use text+CHECK instead of pgEnum. Backfill writing the new value must be a standalone script (not a migration file) run after the enum is committed.

Related auth invariants found same review:
- Seed (`src/db/seed.ts`) uses `onConflictDoNothing` everywhere → changing a role's `accountTypeScope` value in ROLE_DEFS is a silent no-op on existing rows. `roles.accountTypeScope` is plain `text` (`schema/rbac.ts`), so no migration catches the drift.
- `resolveUserRoleIds` (`services/admin-service.ts`) enforces role scope ∈ {null,'user'} at ASSIGNMENT time only. `buildAccessClaims`/`effectivePermissions` (`services/rbac-service.ts`) do NOT filter by scope at runtime → flipping a role's scope does not revoke existing grants, but blocks new grants via createUser.
- FE parses API responses with hard `schema.parse()` (`fe-mono/packages/services/src/services/service-utils.ts`); account/me zod enums are `z.enum(["customer","user"])` → a new BE enum value throws for the whole payload (breaks entire list page), not per-row.
