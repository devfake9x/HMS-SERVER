# Red-team review: Assumption Destroyer, plan 261110-1747-mqtt-catalog-sync

Date: 2026-11-10. Reviewer: code-reviewer (hostile, plan-only). Worktree `ck-project-management-review-dd19d6` @ `ed01757`.
No lint, build or test was run (instruction). Spec read only through `git -C HMS-IDF show <rev>:engineering-docs/api/mqtt-api.md`
for `83e93d77e` and `c80797d`. Counts were recomputed in scratch by importing `scripts/generate-command-catalog.mjs` (no CLI, no repo
write) and, independently, by raw line counting of the `git show` text.

Most of the plan's numbers check out (see Fact Checker). The findings below are about mechanisms that do not do what the plan says they do.

## Finding 1: Grant-time-only high-risk makes "revisit `sms.delete` when SMS opens" impossible to apply to roles granted before then
- **Severity:** High
- **Location:** plan.md "Quyết định" 5 and "Open questions" 10; Phase 3 "Báo cáo role" item 5.
- **Flaw:** The plan defers `sms.delete` (class D, confirm `SMS-DELETE-<DEVID>`), `sms.view` (gates `sms.get`, which returns message content) and `mb_rtu.*`/`tag.view` to a later decision ("xem lại khi mở SMS"). That assumes a later `HighRisk` change still has an effect. It does not. `HighRisk` is checked only when a role is created, edited, assigned or removed. Sending checks only plain set membership, plus the master-admin rule for class F. The vocabulary comes straight from the catalog. So the moment this plan lands, any admin with `permissions.manage` can put the 7 new codes into roles. Moving one of them into `HighRisk` later will not remove it from those roles.
- **Failure scenario:** After deploy, a non-master org admin creates a role with `sms.delete` and `sms.view`. A later plan opens `sms.get`/`sms.delete` and adds `sms.delete` to `HighRisk`. Existing holders keep deleting SMS and reading message content (PII) with no master-admin sign-off. The plan's claim that old roles cannot contain the high-risk codes (Phase 3 item 5) holds only for the 3 codes made high-risk now, not for the next ones.
- **Evidence:** `src/Hms.Server.Application/Authorization/DeviceAccessHandlers.cs:57`, `:106`, `:166`, `:324-325`, `:367-369` (every high-risk check is grant-time); `src/Hms.Server.Application/Commands/SendDeviceCommandHandler.cs:114-127` (send time: `DeviceAccessPolicy.Evaluate`, then master-admin only for Flash); `src/Hms.Server.Domain/Authorization/DeviceAccessPolicy.cs:41` (plain `Contains`); `src/Hms.Server.Application/Authorization/DeviceAccessHandlers.cs:359-364` and `src/Hms.Server.Infrastructure/Commands/PinnedCommandCatalog.cs:157-161` (vocabulary = catalog permissions, so new codes are grantable on deploy). Spec `c80797d` §0 R13 (L417) puts `sms.get`, `sms.send` and `sms.delete` under the same primary-only, signed and clock-synced constraints. The spec treats them alike, but the plan makes only `sms.send` high-risk.
- **Suggested fix:** This is a user decision (brainstorm decision 3 named exactly 3 codes), so present the options instead of choosing: (a) add `sms.delete` (and decide on `sms.view`) to `HighRisk` now; (b) keep the decision, and write into plan.md and `affected-roles.md` that high-risk is grant-time only and not retroactive, with the read-only SQL (`device_role_permissions."Permission" IN (...)`) that must be run before any later reclassification; (c) keep codes of rows that are not open out of the grantable vocabulary until their row opens. Option (c) changes `CheckVocabulary` and needs its own plan. Whatever the choice, correct open question 10, which currently implies the decision can be revisited at no cost.

## Finding 2: `--table-count` checks nothing the tool has not just told the operator, and its "drop the flag" exit is unlikely to trigger
- **Severity:** Medium
- **Location:** plan.md "Quyết định" 1, Risk row 1; Phase 1 Requirements `assertStatedCounts` steps 2-5; Phase 3 Docs L290 ("bỏ cờ khi spec sửa câu đếm (bộ sinh sẽ báo 'is not needed')").
- **Flaw:** (1) The new errors print the very number the flag requires. Step 2 ends `(${commands.length} rows)`, the no-flag error says "rerun with --table-count <row count>", and the write summary prints `108 commands`. So "the flag must equal the exact row count" (Risk row 1 mitigation) is copy-paste, not a check. (2) The generator compares against the **first** "N cặp"/"N namespace" match in §11, and at `c80797d` the spec itself labels that sentence as history: L1351 "Snapshot nền lịch sử trước G01 có 98 cặp, 12 namespace". L1352 is a dated re-check ("Kiểm lại ngày 2026-10-22 … HEAD `69cce605` có 100 cặp"). Neither sentence claims to describe the current table. The natural firmware fix is to update the dated sentence and keep the history. That leaves the first match at 98, so the `is not needed` branch never fires and the flag stays forever. The flag also silently accepts the namespace mismatch (12 vs 13), which no number the operator types is ever compared against.
- **Failure scenario:** At the next re-pin the table has 109 or 107 rows because of a spec editing slip. The operator reads `does not match the §11 action table (107 rows)` and reruns with `--table-count 107`; the generator accepts it. The only real gate left is the catalog hash pin plus human diff review, which existed before this plan. The Risk row 1 rating (Trung bình × Trung bình) understates likelihood: given the historical first sentence, the flag is close to certain to become permanent.
- **Evidence:** `scripts/generate-command-catalog.mjs:170-171` (first-match regexes), `:374-386` (`assertStatedCounts`), `:470-486` (summary prints the row count); `git show c80797d:engineering-docs/api/mqtt-api.md` L1351-1352; `plans/reports/brainstorm-261108-1700-mqtt-spec-coverage-and-requests.md:78-81` (spec question 8 asks for "một câu đếm duy nhất"; nothing in the plan handles a reply that keeps the history sentence).
- **Suggested fix:** Decision 1 is user-approved, so give the user these options: (a) compare against the **last** count sentence, or accept when **any** stated count matches; the "not needed" warning then works with the likely spec fix; (b) do not echo the row count in the mismatch errors, so `--table-count` has to come from an independent count (see Finding 3); (c) say plainly in docs and the journal that the flag is an acknowledgement, not a verification, and that the hash pin plus diff review is the gate. Either way, add a Phase 1 test with the real two-sentence layout where only the second sentence is updated to the table count.

## Finding 3: The "second path" pair-set cross-check and the loader re-hash both rerun the same parser
- **Severity:** Medium
- **Location:** Phase 2 Implementation step 5; Success Criteria "hash và pair-set có bằng chứng từ hai đường"; plan.md Risk row 2.
- **Flaw:** Step 5 computes the pair-set SHA from `parseSpecManifest` output. `Pinned_catalog_pairs_match_spec_manifest` computes it from a catalog that was written by the same `parseSpecManifest` call. The loader re-hash (step 6) only proves the JSON matches itself. A parser defect (a row dropped, a misread cell, a mis-split `type.action`) produces equal values on both "paths" and is then pinned. The permission catalog is the authorization boundary (spec R11: the device checks no permission), so the mitigation for "pin copied without review" (Thấp × Cao) is weaker than stated.
- **Failure scenario:** A future spec row uses a layout the parser misreads but does not reject. Step 5 values agree, the pin goes in, and only the step 4 diff (against a table also produced with the parser's help, see Phase 2 "Số kỳ vọng … đưa qua `parseSpecManifest`") stands in the way.
- **Evidence:** `tests/Hms.Server.UnitTests/CatalogTests.cs:102-111`, `:561-570` (pair-set computed from `new PinnedCommandCatalog().List()`); `scripts/generate-command-catalog.mjs:177-204`, `:415`; `src/Hms.Server.Infrastructure/Commands/PinnedCommandCatalog.cs:138-140` (re-hash of the document's own rows).
- **Suggested fix:** Make step 5 genuinely independent: extract `type.action`, class and permission from the raw `git show` text with a line filter (for example `sed -n '1365,1474p' | grep '^| \`'`, then split the cells) and compare with the JSON rows field by field. In this review a raw `grep -c '^| \`[A-Za-z_]*\.[A-Za-z_]*\` |'` over §11 gives 108 rows and 13 namespaces, which agrees with the parser. Reference values from this review (parser-derived, so a second run rather than a second method): `catalog_hash` `658e191a9e66f14a6885183b6ae9d9ec72a9e25a396c3cd30c80bdcd4df3413b`, pair-set `4286c13cd55ef43ca344ba98d07f7e99b98f111872d408d2afb97ec6017c2b07`. The same method reproduces the old pins `b1d47a78…` and `dbda8317…`.

## Finding 4: The `readSpecAtCommit` tests do not cover the CLI that actually reads the spec
- **Severity:** Medium
- **Location:** Phase 1 Requirements `readSpecAtCommit` / `main`, Implementation step 1 (3 `readSpecAtCommit` tests), Success Criteria 3.
- **Flaw:** The tests exercise only the exported function. `main` is not exported, and it runs only when `isEntryPoint()` is true. The CLI acceptance runs (step 3: `--check`, `--table-count 94/95`, `0`) pass whether `main` calls `readSpecAtCommit` or keeps the current inline L400-413 code. So the 40-hex guard, the "only 3 git commands" claim, the order of stderr warnings and the `--check` hint that repeats `--table-count` are all unproven on the path that matters. This is a lock on code that nothing guarantees is used.
- **Failure scenario:** An implementer exports `readSpecAtCommit` for the tests but leaves `main` on the old inline block (or adds a path that ignores `tableCount`). All 20 node tests and every manual CLI check stay green.
- **Evidence:** `scripts/generate-command-catalog.mjs:392-444` (`main`, not exported), `:400-413` (inline git reads), `:488-497` (`isEntryPoint`), `:499-506` (CLI gate); `scripts/generate-command-catalog.test.mjs:344-351` (the only test that touches the CLI checks that importing does nothing).
- **Suggested fix:** Export a `run(argv, { git, readFile, writeFile, stdout, stderr })` that `main` only wires to `execFileSync`/`fs`. Test it with a fake `git`: the 40-hex refusal happens before any git call; the call log is exactly the 3 commands; warnings reach stderr before stdout in both write and `--check` modes; exit codes are 0 or 1 as specified; the stale hint includes `--table-count N`.

## Finding 5: New integration assertions that pass for the wrong reasons
- **Severity:** Medium
- **Location:** Phase 2 Implementation step 2: `Status_tags_needs_tag_view_and_sys_info_view_alone_is_not_enough` (tagViewer POST) and `A_command_with_the_previous_catalog_identity_is_refused_as_stale`.
- **Flaw:** (1) The tagViewer POST checks only that the code is **not** one of `{DEVICE_PERMISSION_REQUIRED, DENIED_UNTIL_ACTION_CONTRACT, CATALOG_STALE}`. That passes on `DEVICE_NOT_FOUND`, `PERMISSION_UNMAPPED`, `COMMAND_NOT_IN_CATALOG`, `HIGH_RISK_REQUIRES_MASTER_ADMIN` or any adapter failure. Once the permission passes, the request takes a deterministic path: audit reservation, then `HmsIdfCommandProtocolAdapter` with a binding but no keyset. It also leaves an audit reservation in the shared `IClassFixture` host, which the plan does not mention. (2) The stale-identity test hard-codes `b1d47a78…`. It is red only because that is the current identity, and after any later re-pin it stays green without testing a "previous" catalog. Handler-level stale rejection is already covered.
- **Failure scenario:** A wrong scope seed (for example the role created in the wrong organization) gives `DEVICE_NOT_FOUND` for tagViewer and the test still passes. The `allowed-commands` assertion does give positive evidence, but the POST half adds none.
- **Evidence:** `src/Hms.Server.Application/Commands/SendDeviceCommandHandler.cs:67-118`, `:170-196`; `src/Hms.Server.Infrastructure/Commands/HmsIdfCommandProtocolAdapter.cs:30-70` (binding, capabilities, confirm, policies, then `COMMAND_KEY_NOT_PROVISIONED` when no keyset is found); `tests/Hms.Server.IntegrationTests/ManagementApiTests.cs:30` (shared `IClassFixture`); `tests/Hms.Server.UnitTests/CommandBoundaryTests.cs:257-275` (existing stale test).
- **Suggested fix:** Assert the exact downstream code for tagViewer (run once and pin it; from the adapter flow it should be `COMMAND_KEY_NOT_PROVISIONED`) and assert `FindPlan` is **not** null, which proves the request got past every authorization gate. Drop the literal-hash stale test, or build the "previous identity" from `git show HEAD~:src/.../command-catalog.json` metadata in a scratch check instead of a permanent test.

## Finding 6: Wrong rule numbers for opening the closed rows, and spec constraints the catalog cannot carry are not recorded
- **Severity:** Medium
- **Location:** plan.md "Ngoài phạm vi" ("R14-R16 … spec L1355-1363"); Phase 3 Docs, the new `## Catalog ghim` section ("điều kiện R14-R16 khi mở các row sau này").
- **Flaw:** In spec `c80797d` §0, SMS is **R13** (L417), R14 is the `mqtt.set_config` trial, R15 is force DO and R16 is DO `set_config` (L418-420). §11 L1359 mislabels the SMS/`mqtt.set_config` rule as "(R14)", and the plan copies that into docs which future SMS plans will read. Separately, §11 L1353-1363 states per-row constraints: TTL is a ceiling (not a default) for `sms.get`/`send`/`delete`, `force_*` and `set_config`; `sms.*` is accepted only from the primary broker; signed posture and a synced clock are required. The catalog keeps only the 9 table fields, so these constraints disappear when the catalog is generated. The plan does not say that opening any of the 14 rows needs server code outside the catalog.
- **Failure scenario:** A later SMS plan follows the docs ("R14-R16"), misses R13 (including the `sms.list` posture rule and the TTL ceiling), opens `sms.get` with a client-overridable TTL, and the device refuses `schema:ttl_ms_invalid`, or worse, the plan assumes the catalog TTL is a ceiling.
- **Evidence:** spec `c80797d` L417-420, L719, L1353-1363 (via `git show`); `scripts/generate-command-catalog.mjs:36-38` (`ROW_FIELDS`: no ceiling, broker or posture field); `src/Hms.Server.Infrastructure/Commands/PinnedCommandCatalog.cs:285-295` (strict document schema).
- **Suggested fix:** Cite "R13 (SMS), R15 (force DO), R16 (DO set_config)" with §0 line numbers. Add one doc sentence: "the catalog does not encode TTL ceilings, primary-only or posture requirements; opening any of the 14 rows needs server enforcement outside the catalog". Add "§11 L1359 '(R14)' should read R13" to the spec question list.

## Finding 7: The pinned commit exists only on two feature branches; regenerating and `--check` depend on it staying reachable
- **Severity:** Medium
- **Location:** plan.md Risk row 3 ("HMS-IDF HEAD đổi hoặc working tree đang sửa"); Phase 3 step 4 (`--check --table-count 108`) and Docs L290 (re-pin command).
- **Flaw:** The risk analysis covers a moving HEAD and a dirty working tree, but not a commit that disappears. `c80797d` is contained only in `feat/next-work-20261019` and `claude/status-tags-paging` (local and origin). There is no mainline branch or tag, and an active HIL session works in that repo. A rebase or squash merge of those branches makes the commit unreachable. A fresh clone then fails `rev-parse --verify` and the documented regenerate/`--check` command stops working. The old pin `83e93d77e` has the same property, so this is an unstated dependency the plan carries forward, not one it introduces.
- **Failure scenario:** Firmware squash-merges `feat/next-work-20261019`. A month later someone runs the documented `--check --table-count 108` from a new HMS-IDF clone and gets `cannot resolve HMS-IDF commit …`. CatalogTests still pass because they pin blob and sha256, but the generator can no longer reproduce or verify the catalog.
- **Evidence:** `git -C HMS-IDF branch -a --contains c80797d9d5b8…` lists only `claude/status-tags-paging`, `feat/next-work-20261019` and their `remotes/origin/*` copies; `scripts/generate-command-catalog.mjs:400-406` (hard failure when the commit is missing).
- **Suggested fix:** Add the dependency to the risk table. Ask the user (it is a cross-repo action) whether HMS-IDF can tag the pinned spec commits (for example `spec-pin/c80797d`), or record in docs that a fresh clone must fetch those branches.

---

## Fact Checker

### Phase 1
| Claim | Result |
|---|---|
| `main` reads only git objects: rev-parse --verify / compare / rev-parse blob / show / sha1 = blob | VERIFIED `scripts/generate-command-catalog.mjs:392`, `:402`, `:407`, `:409`, `:410`, `:411-413` |
| `assertStatedCounts` reads the first `PAIR_COUNT` (L170) and `NAMESPACE_COUNT` (L171) | VERIFIED `:170-171`, `:374-386` (function starts at L374, not L373) |
| Spec `c80797d` L1351 "98 cặp, 12 namespace", L1352 "100 cặp, 13 namespace"; table 108 rows / 13 types | VERIFIED (git show; parser and raw grep both give 108/13) |
| `83e93d77e` parses OK, 94 rows, hash `b1d47a78…` | VERIFIED (scratch import of the module) |
| `c80797d` without a flag fails with `spec §11 states 98 pairs but its action table has 108 rows` | VERIFIED (scratch) |
| `parseArgs` L446-463 not exported; accepts `--check`, `--idf`, `--idf=` | VERIFIED `:446-463` |
| `parseSpecManifest` L177-204 returns `{commands, config_domains}`; callers are `main` and the tests | VERIFIED `:177-204`, `:415`; test.mjs `:227`, `:253`, `:258-261`, `:340` |
| 11 node tests today; 9 new give 20 | VERIFIED by static count (11 `test(` blocks; 9 listed in step 1); not run |
| No CI and no root `package.json` | VERIFIED (no `package.json`, no CI config references the generator); `CatalogTests.cs:26-29` |
| Old regexes (`/states 7 pairs/`, `/states 5 namespaces/`, `/pair count/`, `/namespace count/`) still match after the change | VERIFIED test.mjs `:282-286` against the new message formats |

### Phase 2
| Claim | Result |
|---|---|
| Catalog today `mqtt-api-83e93d77e`, `b1d47a78…`, 94 rows, 6 domains | VERIFIED `src/Hms.Server.Infrastructure/Commands/command-catalog.json:2-9` |
| Test lines to change (CatalogTests :15, :18-19, :22-24, :30, :107-109, :217-222, :228-233, :258-262, :271-287, :372) | VERIFIED `tests/Hms.Server.UnitTests/CatalogTests.cs` at those lines |
| 14 added rows, 1 changed (`status.tags` → `tag.view`), 0 removed, domains unchanged | VERIFIED (scratch diff of parser output for both commits) |
| R58 W18 C17 D4 B7 F4; 53 firmware / 57 vocabulary; TTL 60000 = 29 (5 confirm; 21 Wi-Fi), 120000 = 79; 5 confirm rows; 3 dry run | VERIFIED (scratch) |
| 12 high-risk codes give 23 high-risk rows, listed ordinally as in the plan | VERIFIED (scratch, applying `CommandPermissionResolver.IsHighRisk` rules `:36-38`) |
| `source_blob` `84ff812f…`, `source_hash` `8cde6ab4…`; the same method reproduces `ef41c113…` for the old pin | VERIFIED (`git ls-tree`, sha256 of `git show` bytes) |
| TTL test fix: the handler caps Physical or Flash at 10 s; the test adapter applies no output/config policy, so 4 B rows reach `COMMAND_RECORDED` | VERIFIED `SendDeviceCommandHandler.cs:143-145`; `tests/Hms.Server.UnitTests/PublishableTestProtocolAdapter.cs:7-17`; `DeviceCommandAuthorizationTests.cs:24-27`, `:198-221` |
| Role create check order is org → text → vocabulary → high-risk; update checks high-risk after vocabulary | VERIFIED `DeviceAccessHandlers.cs:54-57`, `:94-106` |
| Baselines 658 unit / 127 integration / 537 vitest | VERIFIED from `plans/261110-1335-do-channel-ownership/reports/tester-full-regression-report.md` (not rerun) |
| New rows stay closed through the allowlists; second barrier fails closed for other B/W/C/D | VERIFIED `PinnedCommandCatalog.cs:249-257`; `OutputCommandPolicy` default → `PhysicalActionUnsupported`; `ConfigCommandPolicy.cs:33`, `:77-79` (D goes to `CONFIG_ACTION_UNSUPPORTED`) |

Notes: the new `DeviceAccessHandlerTests` theory uses `plain` without seeding it (`_world.SeedRole(_world.Child.Id, DevicePermissionCodes.DeviceView)` is needed, as in `:570`). `infoViewer`'s allowed set is `ota.get_status, status.dump, status.services, system.get_device_id, system.get_heap, system.get_info, system.get_operations, system.ping` (scratch), which matches the "contains" assertions.

### Phase 3
| Claim | Result |
|---|---|
| No hard-coded firmware permission list in the frontend; `DevicePermissionsTab.vue:33-40` holds 4 server codes | VERIFIED `frontend/src/features/devices/components/DevicePermissionsTab.vue:33-40`; grep for `gpio.do_|sms.send|mb_rtu.|tag.view` outside tests: none |
| Frontend never reads `status.tags`; no fixture holds 94, the old hash or the version | VERIFIED (grep of `frontend/src`: none) |
| `highRisk` unused outside tests | VERIFIED, and stronger: it does not appear anywhere in `frontend/src` |
| Two identical `catalogVerification.ts` copies; `CATALOG_STALE` handled at App.vue:778, DeviceOutputsPanel.vue:163, rebootPlan.ts:168 | VERIFIED (`diff -q` identical; grep) |
| No HTTP endpoint creates or assigns device roles | VERIFIED: role/membership handlers are referenced only in `DeviceAccessHandlers.cs` and `DependencyInjection.cs:124-128`. `POST /api/v1/permissions/device-actions` and `PUT /devices/{id}/permissions` store free-form action strings (`PermissionContracts.cs:220-229`), but `DeviceAccessResolver.cs:35` and `DeviceAccessPolicy.cs:5-7` take permissions only from the membership role, so they grant nothing |
| `MayReadReply` re-checks against the current catalog | VERIFIED `DeviceCommandsController.cs:105` (call; the plan says :104), `:121-128` |
| Dev seed creates only the master-admin user | VERIFIED `src/Hms.Server.Api/Program.cs:58-87` |
| In-memory role store without Postgres | VERIFIED `src/Hms.Server.Infrastructure/DependencyInjection.cs:106-117` |
| SQL table and column names | VERIFIED `20261005143949_AddDeviceRolesAndMemberships.cs` (device_roles Id/OrganizationId/Name; device_memberships RoleId; device_role_permissions RoleId/Permission) |
| "dev = 0 device roles; production: none yet" | UNVERIFIED: depends on DB state; the plan supplies the SQL instead |

## Contract Verifier: every consumer of a changed interface

- **`parseSpecManifest(markdown, { tableCount })` return value gains `warnings`:** `scripts/generate-command-catalog.mjs:415` (main), and test.mjs `:227`, `:253`, `:258`, `:260-261`, `:340`. No other importer in the repo (`git grep`). `catalogHash`/`renderCatalog` read only `commands` and `config_domains` (`:75-92`), so the extra field cannot reach the hash.
- **New exports `parseArgs`, `readSpecAtCommit`, `SOURCE_COMMIT`, `SOURCE_PATH`:** only test.mjs `:8-15`.
- **`command-catalog.json` content:** `PinnedCommandCatalog.cs:15`, `:181-186` (embedded resource read); `CatalogTests.cs:572-576`; generator `--check` `:431-433`. The plans/260924 fixtures read their own copy (`contract-artifacts-fixture.mjs:16`, `:65`), not this file.
- **Catalog rows and permissions (`ICommandCatalog`):** `SendDeviceCommandHandler.cs:95-118`; `DeviceCommandsController.cs:67-74` (allowed-commands), `:127-128` (MayReadReply); `MeController.cs:61-71`; `DeviceAccessHandlers.cs:359-364` via `DevicePermissionVocabulary.cs:11`; `HmsIdfCommandProtocolAdapter.cs:43-63`.
- **`DevicePermissionCodes.HighRisk` / `IsHighRisk`:** `CommandPermissionResolver.cs:36-38` → `MeController.cs:71`; `DeviceAccessHandlers.cs:57`, `:106`, `:166`, `:324`, `:367-369`. Tests: `DevicePermissionCodesTests.cs:21-44`, `PhysicalOutputSafetyTests.cs:292-295`, `CommandPermissionResolverTests.cs:134-151`, and the `DeviceAccessHandlerTests` high-risk cases.
- **HTTP `/api/v1/me/command-catalog` (108 actions, 5 `highRisk` flips, `status.tags.requiredPermission`):** `frontend/src/core/api/adminApi.ts:231`, `:233`; `frontend/src/adminApi.ts:167`, `:169`; consumers `App.vue:958-959`, `DeviceOutputsPanel.vue:62`, `commandAvailability.ts:17` (both copies); `tools/device-read-check.js:79`, `tools/device-config-check.js:60`, `tools/device-reboot-check.js:164-169` (`CLOSED_ROWS` still valid). None reads counts, `highRisk` or `status.tags`.
- **HTTP `/api/v1/devices/{id}/allowed-commands`:** `frontend/src/core/api/adminApi.ts:270`; integration tests at `ManagementApiTests.cs:302-458`.

```
Status: DONE_WITH_CONCERNS
Summary: 7 findings - Critical 0, High 1, Medium 6. Plan counts, hashes and line references verified; mechanisms (flag gate, second-path pin, readSpecAtCommit lock, integration negatives) weaker than claimed; high-risk deferral is not retroactive.
```
