# Phase 3 implementation report

Status: DONE_WITH_CONCERNS (docs and reports only; the stale-tab E2E is the controller's step and is not run here)

## Baseline and regression (steps 0, 1, 5)
- Frontend: `npx vitest run` → 537 passed (41 files). `npx vue-tsc --noEmit` → exit 0, no output. No `frontend/` file changed against HEAD (`git diff --name-only HEAD -- frontend` empty).
- Node: `node --test scripts/generate-command-catalog.test.mjs scripts/verify-command-catalog-repin.test.mjs` → 32 pass, 0 fail (phase 1 total; the phase file said 31).
- Generator: `node scripts/generate-command-catalog.mjs --idf D:/WORKSPACE/PROJECT/HMS-IDF --check --accept-stated-count 98/12` → one `warning:` line, `command-catalog.json is up to date (hash 658e191a9e66f14a6885183b6ae9d9ec72a9e25a396c3cd30c80bdcd4df3413b)`, exit 0.
- .NET: not run in this phase (controller owns the running API). Figures taken from the phase 2 report: unit 686, integration 143.

## Frontend hard-code grep (step 1)
`grep -rnE "gpio\.do_|sms\.|mb_rtu\.|tag\.view|status\.tags|highRisk" frontend/src` (not counting tests) gives one hit:
- `frontend/src/features/devices/components/DeviceOutputsPanel.vue:315`: display text "Không có quyền xem DO (cần gpio.do_view trên thiết bị này)". `gpio.do_view` is unchanged by this plan (not in the new set), the text is shown only in the forbidden state, and no logic reads it. Not a permission list; no stop. Noted for the next firmware-permission change.
- No `status.tags` reader in the frontend. `highRisk` does not appear in `frontend/src`.

## Role reports (step 2)
- `reports/affected-roles.md` written. Code references checked again: `CreateDeviceRoleHandler` / `AssignDeviceMembershipHandler` are registered only in `DependencyInjection.cs:124-128` and have no HTTP caller. `grep` for them in `src` (excluding `bin`/`obj`) returns those lines plus the handler definitions in `DeviceAccessHandlers.cs:40, 182`.
- SQL A and SQL B are copied verbatim from the phase file. Column and table names checked against migration `20261005143949_AddDeviceRolesAndMemberships.cs` and the mapping in `ApplicationDbContext.cs:178, 199, 206`; no later migration renames them. Not run.

## Contract diff (step 3)
- `reports/open-actions-contract-diff.md` written. Seven first-row changes, matching the red-team list. Only `status.tags` breaks the reply contract (array → paged object); no server or UI reader of it exists. Follow-up (c) stays open.

## Docs, journal and notes (step 4)
- `docs/architecture-device-mqtt-api-catalog.md`: header count (L6, L26), §3 notice, status box (108 / 13 / 53 / 14 closed / 34 open), line about `c80797d` replacing the stale pin, §4.1 item 2 (generator at `c80797d`, 108 actions, re-pin commands with `--accept-stated-count 98/12`, why the flag exists), item 2 vocabulary (57 = 53 + 4; 7 new codes; `HighRisk` 13; 24 high-risk actions; write-time only).
- `docs/architecture-mqtt-device-link.md`: new `## Catalog ghim` section before `## API`; 14 closed rows added to the closed list; `status.tags` live-row note; DO-section line "catalog vẫn ghim" replaced; monitoring paragraph updated.
- `docs/project-roadmap.md`: Milestone 4 line no longer fixes "94"; §5 has a row for this plan; "Còn đóng" lists SMS, force DO, `control.get_config`/`set_config`; "34 mở" is tied to `c80797d` (108 row).
- `docs/journals/261110-mqtt-catalog-sync.md` created (English).
- `plans/reports/brainstorm-261108-1700-mqtt-spec-coverage-and-requests.md`: §3.B item 8 note; item 20 added (unsent).
- `plans/261001-0030-full-hardware-mqtt-apis-c2/plan.md`: frontmatter note for this plan `(pending)` → `(completed)`. See concerns.

## Numbers checked against code (step 4)
| Claim | Value | Check |
|---|---|---|
| rows | 108 | `command-catalog.json` |
| namespaces | 13 | distinct `type` values |
| firmware permissions | 53 (57 with server codes) | union of row `required_perm` and `config_domains` view/edit |
| open actions | 34 | `PinnedCommandCatalog` allowlists (26 + 2 + 5 + 1) |
| closed rows added | 14 | JSON diff against `HEAD` |
| `HighRisk` codes | 13 | `DevicePermissionCodes.cs:23-35` |
| high-risk rows | 24 | rows with a high-risk permission or `config.set` (domain edit permissions) |
| `sys.info_view` open actions | 8 | 9 rows with `sys.info_view`, `config.list_domains` closed |
| new vocabulary codes | 7 | `tag.view`, `mb_rtu.slave_view`, `mb_rtu.tag_view`, `sms.send`, `sms.delete`, `gpio.do_force`, `gpio.do_config` |
| dates | 2026-11-10 | session date |
| spec lines | L415, L417, L419, L420, L612, L927-929, L1351-1352, L1359 | read from `git show` of `c80797d` |

## Files
- Created: `plans/261110-1747-mqtt-catalog-sync/reports/affected-roles.md`, `plans/261110-1747-mqtt-catalog-sync/reports/open-actions-contract-diff.md`, `plans/261110-1747-mqtt-catalog-sync/reports/phase-03-implementation-report.md`, `docs/journals/261110-mqtt-catalog-sync.md`.
- Modified: `docs/architecture-device-mqtt-api-catalog.md`, `docs/architecture-mqtt-device-link.md`, `docs/project-roadmap.md`, `plans/reports/brainstorm-261108-1700-mqtt-spec-coverage-and-requests.md`, `plans/261001-0030-full-hardware-mqtt-apis-c2/plan.md`, `plans/261110-1747-mqtt-catalog-sync/phase-03-docs-roles-report.md` (As built).
- Not touched: `src/`, `tests/`, `frontend/`, `scripts/`, `tools/`, `.claude/`. HMS-IDF: read only through `git --no-replace-objects show`.

## Concerns
1. **E2E (step 6) not run here.** The controller runs it and writes `reports/live-verification.md`; the plan's "Kết quả" and phase status are the controller's.
2. **Plan C2 note flipped to `(completed)` before the E2E.** The spec asked for it in this phase. If the E2E fails, revert that one word.
3. **Docs not changed on purpose (historical sections, spec says leave them):** `architecture-device-mqtt-api-catalog.md` L43 (firmware manifest), L82 (capabilities snapshot), L149 (the 94-row table), L305 (§4 blueprint status), L330 (conclusion); `project-roadmap.md` L69 (Milestone 4 phase 1 scope, dated 2026-10).
4. **`validate-docs.cjs`:** the project path `.claude/scripts/validate-docs.cjs` does not exist in this worktree. The global copy ran over all of `docs/` and did not finish in about 20 minutes, so it was stopped. A narrowed run on scratch copies of the four changed docs (sources limited to `src/Hms.Server.Infrastructure/Commands`, `src/Hms.Server.Domain/Authorization`, `src/Hms.Server.Application`, `scripts`) exited 0. Its warnings are heuristic: error codes (`DENIED_UNTIL_ACTION_CONTRACT`, `CATALOG_STALE`) and enum-like names are not in `.env.example`, and links to `plans/` fail only because the scratch copy has no `plans/`. Links, anchors and backticked repo paths were checked directly against the worktree instead: all new ones resolve. Pre-existing `file:///d:/…` links in the roadmap and C2 plan were not changed.
5. Spec question 8 and 20 are updated but unsent. Tagging of `c80797d` and `83e93d77e` is follow-up (a).
