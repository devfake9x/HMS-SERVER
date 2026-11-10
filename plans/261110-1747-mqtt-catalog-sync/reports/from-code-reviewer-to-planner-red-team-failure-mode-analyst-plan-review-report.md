# Red team: failure-mode analysis of plan 261110-1747-mqtt-catalog-sync

Reviewer: code-reviewer, acting as failure-mode analyst, with the Fact Checker and Contract Verifier roles. Date: 2026-11-10.

## Scope

- Plan files: `plan.md` and phases 01-03. Approved design: `plans/reports/brainstorm-261110-1745-mqtt-api-completeness-and-catalog-sync.md`.
- Code: worktree `ck-project-management-review-dd19d6`, branch `claude/mqtt-device-connection-36f705`, HEAD `ed01757`.
- Spec: I read it only through `git -C HMS-IDF show <rev>:engineering-docs/api/mqtt-api.md`, at both pins. In this report, `spec@c80797d Lnnn` and `spec@83e93d77e Lnnn` mean line nnn of the export at that commit.
- HMS-IDF metadata: I used only `rev-parse`, `ls-tree`, `branch --contains`, `for-each-ref` and `log -- <spec path>`. I opened no other HMS-IDF file.
- I ran no lint, build or test. The only code I executed was a scratch import of `parseSpecManifest` and `catalogHash` on the two spec exports. Importing the module never calls git. I edited no file other than this report.

## Results

Eight findings: 0 Critical, 1 High, 7 Medium. The fact-check tables and the consumer inventory come after the findings.

---

## Finding 1: The new pin is today's tip of an unmerged, active feature branch; if that history is rewritten, the catalog can no longer be regenerated, and nothing would notice

- **Severity:** High
- **Location:**
  - plan.md: frontmatter `contract`, Decision 3, and the risk-table row "HMS-IDF HEAD đổi hoặc working tree đang sửa".
  - Phase 2, step 0 and step 3.
  - Phase 3, docs bullet for L290 (the re-pin command that docs will publish).
- **Flaw:** The plan defends against a dirty working tree and a moving HEAD by reading objects of a 40-hex commit only. It does not check that the commit stays reachable. `c80797d` is not on `main` or `develop`.
- **Failure scenario:**
  1. The firmware team squash-merges `feat/next-work-20261019` into `develop` and deletes the branch. Alternatively, the HMS-IDF HIL session amends or force-pushes `claude/status-tags-paging`.
  2. A fresh clone no longer contains `c80797d`. On the dev machine, the object is pruned at the next gc after the reflog expires.
  3. Anyone who follows the Phase 3 docs (`--check --table-count 108`) gets `cannot resolve HMS-IDF commit c80797d…`. The `source_commit` provenance of the shipped catalog can no longer be checked.
  4. Nothing fails meanwhile, because no CI runs the generator.
  5. Rollback has the same exposure: `83e93d77e` is also only on feature branches.
- **Evidence:**
  - `git -C HMS-IDF branch -a --contains c80797d9d5b8…`: only `feat/next-work-20261019` and `claude/status-tags-paging`, plus their `origin/` copies.
  - `origin/main` and `origin/develop` are both at `dc5e4c10b` (2025-12-09).
  - `git log -1 c80797d`: committed 2026-11-10 10:54 +0700. It is the current HMS-IDF `HEAD`.
  - `git log --all -- engineering-docs/api/mqtt-api.md`: five spec commits between 2026-11-08 and 2026-11-10.
  - `scripts/generate-command-catalog.mjs:400-407`: an unresolvable commit is a hard error, and there is no fallback.
  - `tests/Hms.Server.UnitTests/CatalogTests.cs:26-29`: "CI cannot run the generator's --check".
  - The repo has no CI config (`git ls-files` shows no workflow or pipeline file).
- **Suggested fix:**
  - Add to Phase 2 step 0: record `git -C HMS-IDF branch -r --contains <sha>` in the report, and stop the re-pin if no remote branch contains the commit.
  - Make it a user action, not an HMS-IDF edit by the plan: ask the HMS-IDF owner to push a tag for the pin (for example `hms-server/catalog-c80797d`), or to confirm that the branch will not be rewritten.
  - Record the blob id in the journal and docs (`git cat-file blob 84ff812f…` still works while any ref keeps the blob).
  - Add the reachability caveat to the risk table.

## Finding 2: The re-pin changes the contract version of the 34 open actions, but the plan diffed only §11; seven open rows changed in §6, and `status.tags` lost the SLICE_PASS status that justifies keeping it open

- **Severity:** Medium
- **Location:**
  - plan.md: Decision 4, "Không hồi quy" (the 34 open actions), and the risk-table row "Firmware 1.0.108 …".
  - Brainstorm §3: "đã đối chiếu từng row" covered §11 only.
  - Phase 3, docs bullet for device-link L123.
- **Flaw:** After the re-pin, `catalog_version` `mqtt-api-c80797d9d` says the server speaks spec c80797d. The plan never compares the §6 contracts of the open actions between the two pins. I compared the first `| \`type.action\` |` row of every open action in both exports. Seven differ: `mqtt.get_status`, `mqtt.get_config`, `system.get_info`, `status.tags`, `control.get_status`, `network.get_modem`, `control.set_output`. Most changes add fields. `status.tags` breaks its contract.
- **Failure scenario:**
  1. A `tag.view` holder sends `status.tags` with `{}`.
  2. Firmware at c80797d returns one page: 10 of up to 512 tags, with `truncated:true`. Neither the server nor the UI can page.
  3. The live gateway (fw 1.0.108) still returns the old array.
  4. Phase 3 docs will say "`status.tags` cần `tag.view`, vẫn mở" with no note about the reply shape.
  5. The code comment that keeps `status.tags` open ("what the pinned spec snapshot closed as SLICE_PASS") becomes false at the new pin.
  6. Read payloads are not validated, so `{offset, limit}` reaches the device as sent.
  7. The plan's integration test posts `{}` and checks only the permission code.
- **Evidence:**
  - `spec@83e93d77e L211`: `status.tags` returns an array snapshot.
  - `spec@c80797d L612`: `status.tags` takes `{offset?:0..65535,limit?:1..35}` and returns `{schema:"status.tags.v1",…,truncated}`.
  - `spec@c80797d L927-929`: default `limit=10`.
  - `spec@c80797d L2129`: "C07 status.tags | `PASS_HOST` và `HIL_PASS` dạng trang …; receipt `SLICE_PASS` cũ là dạng mảng | NO".
  - `src/Hms.Server.Infrastructure/Commands/PinnedCommandCatalog.cs:44-49` and `:61`.
  - `docs/architecture-mqtt-device-link.md:118-126`: the live check on fw 1.0.108 recorded `status.tags` at 6.7 KB, the array form.
  - `src/Hms.Server.Application/DeviceLink/ConfigCommandPolicy.cs:33`: returns null (allow) for every Read.
- **Suggested fix:**
  - Add a Phase 2 step: diff §6 between the pins for every row in the four allowlists, and record for each of the seven changed rows whether the change only adds fields or breaks the contract, and which server or UI code reads it.
  - Ask the user to decide `status.tags` explicitly:
    - keep it open under the paged contract (update the `PinnedCommandCatalog.cs:44-49` comment, which means adding that file to Phase 2 ownership, and update docs L123);
    - or close it until paging exists.

## Finding 3: Rollback silently removes `gpio.do_force`, `gpio.do_config` and `sms.send` from the high-risk set, and memberships a non-master delegates during the rollback survive the roll-forward

- **Severity:** Medium
- **Location:**
  - plan.md, "Rollback", Phase 2.
  - Phase 2 risk-table row "Revert sau khi role đã có mã mới".
  - Phase 3, `affected-roles.md` item 6.
- **Flaw:** The plan says a rollback only leaves unknown codes that match nothing, and that the next role edit returns `PERMISSION_UNKNOWN`. It misses three things:
  - The rollback must also remove the 3 codes from `HighRisk`, because the subset test fails otherwise.
  - While rolled back, non-master admins can delegate roles that hold those codes.
  - Nothing re-checks memberships when the server rolls forward.
- **Failure scenario:**
  1. After the forward deploy, the master-admin gives admin A role R `{device.view, gpio.do_force}` on device D.
  2. The change is reverted.
  3. A assigns R to users B and C. This is allowed: no code in R is high-risk any more, and every code is in A's own set.
  4. The change is redeployed. B and C now hold `gpio.do_force`, granted by a non-master. No startup check runs and no epoch moves.
  5. When a later plan opens `control.force_*`, B and C can force relays. This breaks Decision 3.
  6. Also, the error code the plan cites does not exist: the code is `DEVICE_PERMISSION_UNKNOWN`.
- **Evidence:**
  - `src/Hms.Server.Domain/Authorization/DevicePermissionCodes.cs:21-28`.
  - `tests/Hms.Server.UnitTests/DevicePermissionCodesTests.cs:20-33` (`High_risk_set_is_a_subset_of_firmware_vocabulary` ties `HighRisk` to the catalog).
  - `src/Hms.Server.Application/Authorization/DeviceAccessHandlers.cs`:
    - `:224-228` and `:323-326`: assignment rules.
    - `:106`: update.
    - `:166`: delete.
    - `:359-365`: `CheckVocabulary` checks only the submitted list.
    - `:31`: `PermissionUnknown = "DEVICE_PERMISSION_UNKNOWN"`.
  - `device_memberships.GrantedBy` exists (migration `20261005143949_AddDeviceRolesAndMemberships.cs:66`).
- **Suggested fix:**
  - In the rollback steps, require a read-only SQL check before reverting: `device_role_permissions."Permission"` IN (the 7 new codes). Require a master-admin to remove those codes first.
  - Add a roll-forward SQL check to `affected-roles.md`: memberships whose role holds a `HighRisk` code and whose `GrantedBy` is not a master-admin.
  - Correct the code name everywhere (plan.md "Rollback", Phase 2 step 1 "Đỏ vì", Phase 3 item 6).

## Finding 4: The plan has no cut-over procedure; the re-pin restart wipes every stored reply and turns in-flight writes into outcome-unknown, and the plan documents a reply effect that cannot happen

- **Severity:** Medium
- **Location:**
  - plan.md: "Luồng dữ liệu" step 2 (the stored-reply read bullet), "Tương thích ngược" (existing roles), Open question 7.
  - Phase 3: `affected-roles.md` item 1, the device-link doc bullet ("kể cả đọc reply cũ"), and step 5 (E2E).
- **Flaw:** The catalog is an embedded resource, so the re-pin needs a new build and a restart. That restart has side effects the plan does not cover:
  - Every in-flight command is marked outcome-unknown.
  - The in-memory lanes and the reboot watch are lost.
  - Every stored reply is dropped, for every user and every action, because the reply store is in-memory only.

  The "role loses its stored `status.tags` replies" effect the plan writes into the role report and docs cannot be observed: after the restart there are no stored replies left.
- **Failure scenario:**
  1. In an old tab, an operator sends `set_output` on DO1.
  2. The deploy restarts the API before the device replies.
  3. At startup, the audit row becomes `OUTCOME_UNKNOWN_AFTER_RESTART`.
  4. The panel's read-back (`control.get_status`) still carries the old catalog version, so the server answers `CATALOG_STALE`. The panel shows "tải lại trang" instead of the relay state.
  5. The operator reloads and sends `set_output` again with a new correlation id. A pulse runs twice, or a toggle is undone.
  6. Separately: reads that completed just before the restart now show `no_data`, because the result says Executed but the reply is gone.
- **Evidence:**
  - `src/Hms.Server.Infrastructure/DependencyInjection.cs:42`: `ICommandResponseStore` is an in-memory singleton.
  - `src/Hms.Server.Application/DeviceLink/DeviceLinkContracts.cs:435`: capacity 2048, no persistence.
  - `src/Hms.Server.Infrastructure/Commands/PinnedCommandCatalog.cs:181-186`: the catalog is an embedded resource.
  - `src/Hms.Server.Api/Program.cs:162`: `FailUnfinished("OUTCOME_UNKNOWN_AFTER_RESTART")`.
  - `docs/architecture-mqtt-device-link.md:546` and `:675`: lane and reboot watch are in memory.
  - `src/Hms.Server.Api/Controllers/DeviceCommandsController.cs:99-118`.
  - `frontend/src/core/commands/useDeviceReads.ts:64-71`.
  - `frontend/src/features/devices/components/DeviceOutputsPanel.vue:162-165`.
- **Suggested fix:**
  - Add a "Cut-over" section to plan.md and the Phase 3 docs:
    - deploy only when `command_audits` has no unfinished row and no reboot is being watched;
    - otherwise, record each such row as outcome-unknown;
    - tell operators to read device state before retrying anything.
  - Replace the stored-reply wording (Open question 7, "Tương thích ngược", `affected-roles` item 1, the device-link doc bullet) with "every stored reply is lost at the restart; the store is in-memory".
  - Extend Phase 3 step 5 to cover a relay write that is in flight across the restart (simulator), not only a read.

## Finding 5: The `--table-count` exit condition cannot trigger as designed, because the generator reads a sentence the spec marks as history; and the namespace extension cites wording that is not in the approved decision

- **Severity:** Medium
- **Location:**
  - plan.md: Decision 1 (the table, and "Cờ phủ cả câu namespace … 'pair/namespace count'"), Open question 1.
  - Phase 1, Requirements `assertStatedCounts`.
  - Phase 3, docs bullet "bỏ cờ khi spec sửa câu đếm (bộ sinh sẽ báo 'is not needed')".
- **Flaw:**
  - The first count sentence is a historical snapshot by its own wording. The second is a dated re-check. Neither claims to be the current size.
  - The generator keeps reading the first match.
  - A spec fix that keeps the history (which is true) and adds or updates a current count would still read "98 cặp, 12 namespace". So the flag stays mandatory forever, and the "is not needed" warning never fires.
  - The flag also accepts a namespace mismatch with a value that says nothing about namespaces.
  - The plan says the approved decision used the words "pair/namespace count", but the brainstorm does not contain them.
- **Failure scenario:**
  1. The firmware team answers spec question 8 by appending "Hiện tại 108 cặp, 13 namespace".
  2. The generator still fails without the flag. Docs keep calling the flag temporary.
  3. Later, a re-pin with a renamed namespace (same row count, 14 namespaces instead of 13) passes with one warning on stderr.
- **Evidence:**
  - `spec@c80797d L1351`: "Snapshot nền lịch sử trước G01 có 98 cặp, 12 namespace."
  - `spec@c80797d L1352`: "Kiểm lại ngày 2026-10-22: … 100 cặp, 13 namespace".
  - `scripts/generate-command-catalog.mjs:170-171` and `:375`, `:380`: `exec` takes the first match.
  - Brainstorm §5, decision 1: only "câu đếm trong prose khác số row bảng".
  - `grep "pair/namespace"` matches only `plan.md:58` and `plan.md:152`.
- **Suggested fix:**
  - Mark the namespace extension as a planner gap-fill that needs the user's confirmation, and remove the "đúng chữ 'pair/namespace' của quyết định 1" claim.
  - Either add a separate `--namespace-count <N>`, or make spec question 8 exact (one count sentence, replacing the historical one), and say in the docs that the flag stays while L1351 exists.
  - Optionally, fail when §11 has more than one "N cặp" sentence, so the ambiguity is explicit rather than silently resolved to the first sentence.

## Finding 6: The re-pin's checks are a throwaway scratch script and a "second path" that runs the same parser, and the flag's own error message prints the value to pass

- **Severity:** Medium
- **Location:**
  - plan.md, risk-table row 2 ("pair-set đối chiếu từ spec qua đường thứ hai").
  - Phase 2, steps 4-6.
  - Phase 1, the new error hint.
- **Flaw:**
  - Step 5 recomputes the pair set with `parseSpecManifest` on the same bytes the generator parsed. The .NET loader recomputes the hash from the generator's output.
  - Neither check is independent of the parser.
  - The step 4 diff script stays in the scratchpad, so the next re-pin has no diff tool.
  - The error message gives the row count. The proposed hint tells the user to rerun with it.
- **Failure scenario:**
  1. A future §11 row that the table splitter mis-reads is dropped. The catalog has 107 rows.
  2. The error says "has 107 rows". The developer passes `--table-count 107`.
  3. Step 5 reproduces the same pair set, and the loader reproduces the same hash.
  4. `CatalogTests` is re-pinned to the defective output.
- **Evidence:**
  - Phase 2 step 4: "script node chỉ đọc, trong scratchpad, không commit".
  - `scripts/generate-command-catalog.mjs:415` (the same parser), and `:378` (the error prints the row count).
  - `tests/Hms.Server.UnitTests/CatalogTests.cs:101-111` and `:561-570`.
- **Independent cross-check (done for this review):**
  - An awk/grep extraction of `| \`type.action\` |` rows from §11 of `spec@c80797d` gave 108 pairs, 13 namespaces, and pair-set SHA-256 `4286c13cd55ef43ca344ba98d07f7e99b98f111872d408d2afb97ec6017c2b07`. It uses no generator code, and it equals the parser's value.
  - The parser path gives `catalog_hash` `658e191a9e66f14a6885183b6ae9d9ec72a9e25a396c3cd30c80bdcd4df3413b`.
  - On `83e93d77e`, the same path reproduces the current `b1d47a78…` and `dbda8317…`.
- **Suggested fix:**
  - Replace step 5 with a parser-independent extraction (grep/awk), and write the exact command into the report and the docs.
  - Have the generator print the `type.action` keys it adds, removes and changes against the committed JSON whenever it writes. Every re-pin then shows the delta without a scratch script.
  - Use the values above as expected results.

## Finding 7: The `status.tags` integration test proves the refusal but not the grant; its positive half passes on `DEVICE_NOT_FOUND` or `COMMAND_NOT_IN_CATALOG`

- **Severity:** Medium
- **Location:** Phase 2 step 2, `Status_tags_needs_tag_view_and_sys_info_view_alone_is_not_enough`.
- **Flaw:** The `tagViewer` POST asserts only that the code is not one of `{DEVICE_PERMISSION_REQUIRED, DENIED_UNTIL_ACTION_CONTRACT, CATALOG_STALE}`. Every other refusal also satisfies that.
- **Failure scenario:** The test stays green in any of these cases:
  - A setup mistake (wrong organization or no binding).
  - A later catalog without `status.tags`.
  - An unmapped permission.

  In each case the POST is refused for another reason (`DEVICE_NOT_FOUND`, `COMMAND_NOT_IN_CATALOG` or `PERMISSION_UNMAPPED`). The `allowed-commands` half covers only the listing, not the send path.
- **Evidence:**
  - `src/Hms.Server.Application/Commands/SendDeviceCommandHandler.cs`:
    - `:71-73`, `:95-99` and `:109-112`: the refusals listed above.
    - `:171`: the audit reservation happens only after every gate.
  - `tests/Hms.Server.IntegrationTests/ManagementApiTests.cs:534-543`: `GrantDeviceRole` writes straight to the store and skips `CheckVocabulary`.
- **Suggested fix:**
  - After the `tagViewer` POST, assert that `FindPlan(deviceId, correlationId)` is not null: it exists only after every gate has passed.
  - Assert that the code equals the master-admin's code for the same POST on that device.

## Finding 8: The new class B rows inherit the server's "class B means relay output" behaviour; the plan writes it into tests and docs without listing it as a prerequisite for opening them

- **Severity:** Medium
- **Location:**
  - plan.md: Decisions 6 and 8, "Ngoài phạm vi" (lists only R14-R16).
  - Phase 2 step 1 (TTL test: "Physical or Flash → 10 s").
  - Phase 3, the device-link doc bullet "điều kiện R14-R16 khi mở các row".
- **Flaw:** The server treats every class B row as a relay output. Once `sms.send` or `control.set_config` opens, this applies:
  - **Audit:** the audit row keeps the payload, so SMS bodies and recipient numbers would be stored unredacted. Class D `sms.delete` is kept the same way.
  - **Relay state:** every reply or timeout marks the DO panel's state stale.
  - **Lanes:** these rows bypass the write lane.
  - **TTL:** the cap is 10 s.

  The only second layer, `OutputCommandPolicy`, is written around relay payloads.
- **Failure scenario:**
  1. A later "open SMS" plan follows this plan's docs and adds `sms.send` to `PhysicalOutputActions`, the only allowlist that accepts class B (`PinnedCommandCatalog.cs:252`), with an `OutputCommandPolicy` case.
  2. Every SMS then writes the recipient and body into `command_audits`, marks relay state stale with "output command executed", and skips the write lane.
  3. The TTL test from this plan stays green.
- **Evidence:**
  - `src/Hms.Server.Infrastructure/Persistence/EfCommandAuditStore.cs:276-283`.
  - `src/Hms.Server.Application/DeviceLink/DeviceOutputState.cs:128-137`.
  - `src/Hms.Server.Application/DeviceLink/DeviceLinkHandlers.cs:139`.
  - `src/Hms.Server.Application/Commands/CommandLanes.cs:10-11`.
  - `src/Hms.Server.Application/Commands/SendDeviceCommandHandler.cs:143-145`.
  - `src/Hms.Server.Application/DeviceLink/OutputCommandPolicy.cs:31-50`.
- **Suggested fix:** No code change in this plan. In plan.md "Ngoài phạm vi" and in the Phase 3 device-link doc, list these class B couplings as prerequisites next to R14-R16 for `sms.send`, `sms.delete`, `control.force_*` and `control.set_config`: audit payload retention, the relay-state projection, lanes, the TTL cap, and which allowlist they go in.

---

## Fact check

### plan.md and Phase 1

| # | Claim | Result |
|---|---|---|
| 1 | The generator reads only git objects (`:402-413`). | VERIFIED: `scripts/generate-command-catalog.mjs:400-413` |
| 2 | The count check reads the first "N cặp" and "N namespace" (`:170-171`, `:373-386`). | VERIFIED: `generate-command-catalog.mjs:170-171`, `:374-385` |
| 3 | `parseArgs` (L446-463) is not exported. | VERIFIED: the exports are at `:54`, `:69`, `:75`, `:81`, `:85`, `:177` |
| 4 | The generator test file has 11 tests. | VERIFIED: `generate-command-catalog.test.mjs` has `test(` at `:61` … `:344` (11) |
| 5 | Spec L1351 says "98 cặp, 12 namespace" and L1352 says "100 cặp, 13 namespace". | VERIFIED: `spec@c80797d L1351-1352`. L1351 is labelled as a historical snapshot (Finding 5). |
| 6 | 108 rows and 13 namespaces at c80797d; 94, 12 and `b1d47a78…` at 83e93d77e. | VERIFIED: scratch parse of both exports |
| 7 | Decision 1 "ghi 'pair/namespace count'". | FAILED: the phrase is absent from the brainstorm; grep finds only `plan.md:58` and `:152` |
| 8 | No CI runs the generator, and there is no root `package.json`. | VERIFIED: root listing; `git ls-files` shows no CI file; `CatalogTests.cs:26-29` |
| 9 | `source_blob` is `84ff812f…` and `source_hash` is `8cde6ab4…`. | VERIFIED: `git ls-tree c80797d …` and `git show … \| sha256sum` |
| 10 | No frontend or tool hard-codes the catalog identity. | VERIFIED: the old version and hash appear only in `CatalogTests.cs` and `command-catalog.json` |

### Phase 2

| # | Claim | Result |
|---|---|---|
| 1 | The table of 14 new rows plus the `status.tags` change. | VERIFIED: `spec@c80797d L1410-1474`. The scratch diff shows 14 added, 1 changed, 0 removed, and `config_domains` unchanged. |
| 2 | Counts: R58 W18 C17 D4 B7 F4; TTL 29 (5 with confirm, 21 Wi-Fi) and 79; 5 confirm; 3 dry-run; 53 permissions. | VERIFIED: scratch parse |
| 3 | The ordinal list of 23 high-risk actions. | VERIFIED: matches exactly |
| 4 | The `CatalogTests.cs` line references (`:15`, `:18-19`, `:24`, `:30`, `:107-109`, `:217-222`, `:258-262`, `:271-287`, `:157`). | VERIFIED |
| 5 | `DeviceCommandAuthorizationTests.cs:189-221` (3 confirm rows, 24 rows, Flash-only cap). | VERIFIED: `:189`, `:198-221` |
| 6 | Role checks run in the order organization → text → vocabulary → high-risk (`:54-57`); update checks high-risk after vocabulary (`:94-106`). | VERIFIED: `DeviceAccessHandlers.cs` |
| 7 | `DeviceAccessTestFixture.cs:20` uses the real `PinnedCommandCatalog`. | VERIFIED |
| 8 | `ManagementApiTests.cs:191` (94), `:250-252` (18) and `:1460` (`PinnedCatalogMetadata`). | VERIFIED |
| 9 | The 14 new rows stay closed through the per-class allowlists, class D included. | VERIFIED: `PinnedCommandCatalog.cs:249-257` (`_ => false`) |
| 10 | The rollback error code is `PERMISSION_UNKNOWN`. | FAILED: it is `DEVICE_PERMISSION_UNKNOWN` (`DeviceAccessHandlers.cs:31`) |

### Phase 3

| # | Claim | Result |
|---|---|---|
| 1 | No HTTP endpoint creates or assigns device roles. | VERIFIED: only `DeviceAccessHandlers.cs` and `DependencyInjection.cs:124-127` |
| 2 | The dev seed creates only the master-admin (`Program.cs:58-87`). | VERIFIED |
| 3 | The master-admin bypasses device permission checks (`DeviceAccessPolicy.cs:37-38`). | VERIFIED |
| 4 | Roles live in memory without Postgres and in EF with it (`DependencyInjection.cs:106-117`). | VERIFIED |
| 5 | The SQL column and table names. | VERIFIED: migration `:35`, `:60-66`, `:88-92`; snapshot `:405-412` |
| 6 | The frontend does not read `status.tags` and uses `highRisk` only in tests. | VERIFIED: grep of `frontend/src` |
| 7 | The doc line references (catalog L6, L26, L173, L290, L292; device-link L67, L123, L160, L287; roadmap L16, L114, L116). | VERIFIED |
| 8 | A role loses the read-back of its stored `status.tags` replies. | FAILED in practice: replies are in memory (`DependencyInjection.cs:42`) and the re-pin restart drops them (Finding 4) |
| 9 | The two copies of `catalogVerification.ts` are identical. | VERIFIED: `diff` shows no difference |
| 10 | Existing `CATALOG_STALE` tests: `catalogVerification.test.ts:20`, `DeviceOutputsPanel.test.ts:218`, `useDeviceWrite.test.ts:46`, `rebootPlan.test.ts:254`. | VERIFIED |

## Contract verifier: consumers of each changed interface

### `command-catalog.json` (embedded resource)

- `PinnedCommandCatalog.cs:181-186` (loader).
- `CatalogTests.cs:572-576` (`ReadCommittedCatalog`).
- `generate-command-catalog.mjs:431-438` (`--check`).

### `ICommandCatalog` rows and permissions (now 108 rows; `status.tags` needs `tag.view`)

- `DeviceCommandsController.cs:67-75` (`allowed-commands`) and `:127-129` (`MayReadReply`).
- `MeController.cs:61-71`.
- `SendDeviceCommandHandler.cs:95-118`.
- `DevicePermissionVocabulary.cs:12`, used by `DeviceAccessHandlers.cs:361`.
- Class-based code paths: `EfCommandAuditStore.cs:280-290`, `OutputCommandPolicy.cs:27`, `ConfigCommandPolicy.cs:33`, `DeviceOutputState.cs:128`, `DeviceLinkHandlers.cs:139`, `CommandLanes.cs:10-11`.
- No other `new DeviceCommandRequest(` exists; `DeviceCommandsController.cs:34` is the only place commands are created.

### Catalog version and hash

**Server:**
- `SendDeviceCommandHandler.cs:76-93`.
- `MeController.cs:51-60`.

**Frontend:**
- `App.vue:124`, `:186`, `:235`, `:774-778`.
- `DeviceDetailView.vue:407-430`.
- `OverviewView.vue:56-70`.
- `DeviceCommandModal.vue:129-130`, `:193-194`.
- `DeviceOutputsPanel.vue:122`, `:162-165`.
- `useDeviceReads.ts:54-55`.
- `useDeviceReboot.ts:116-117`, `:178`.
- `useDeviceWrite.ts:111-112`, `:200-201`.
- `DeviceReadCard.vue:57`.
- `useConfigWrite.ts:44`.

**Tools:**
- `tools/device-config-check.js:60`, `:75`.
- `tools/device-read-check.js:55`, `:79`.
- `tools/device-reboot-check.js:164`, `:169-171`, `:183`.

None of these hard-codes a version or hash.

### `DevicePermissionCodes.HighRisk` and `IsHighRisk`

- `DeviceAccessHandlers.cs:57`, `:106`, `:166`, `:324`, `:367-369`.
- `CommandPermissionResolver.cs:36-38`, used by `MeController.cs:71`.
- No frontend consumer outside tests.

### Generator `parseSpecManifest` return shape (adds `warnings`)

- `generate-command-catalog.mjs:415` (`main`).
- The test-file import at `generate-command-catalog.test.mjs:8-15`.
- `catalogHash`, `canonicalize` and `renderCatalog` read only `commands` and `config_domains`.

### Checked and not affected

- SignalR delivery is scoped by organization (`ScopeAuthorization.cs:34-39`; `SignalRDeviceEventDeliveryService.cs` `CanAccessDevice`), not by firmware permission.
- The `status.tags` permission change therefore does not change hub membership.
- Telemetry and heartbeat carry only `tag_count` (`DeviceTelemetryContracts.cs:110-116`; `spec@c80797d L610`, `L1322`).
- Legacy `DeviceActionGrants` (`MeController.cs:27-34`, `PermissionContracts.cs:220-229`) do not take part in command authorization (`DeviceAccessResolver.cs:23-35`).

---

```
Status: DONE_WITH_CONCERNS
Summary: Critical 0, High 1, Medium 7. Phase 2 has no blocker in its numbers: every count, the 14-row diff and the high-risk list check out against both spec exports. The concerns are pin reachability, the unverified §6 contracts of open rows, the rollback hole in the high-risk set, and the deploy cut-over.
```
