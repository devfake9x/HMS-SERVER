# Red-team plan review: Security Adversary, MQTT catalog sync (re-pin to `c80797d`)

- Reviewer: code-reviewer (Security Adversary; Fact Checker + Contract Verifier roles)
- Plan: `plans/261110-1747-mqtt-catalog-sync/` (plan.md, phase-01..03) + brainstorm `plans/reports/brainstorm-261110-1745-mqtt-api-completeness-and-catalog-sync.md`
- Contract: `HMS-IDF/engineering-docs/api/mqtt-api.md` read only via `git show 83e93d77e:` and `git show c80797d:` (into scratch). I read §0 (R11, R13-R16), §6 "`status.tags` theo trang" and §11. No firmware source opened. Git metadata only otherwise: `rev-parse`, `ls-tree`, `branch -a --contains`, `merge-base --is-ancestor`, `for-each-ref refs/replace`.
- Probes: scratch only (independent awk extraction of §11; a throwaway git repo to test `git replace`). I did not run the generator, any tests or any builds. No file changed except this report.
- Branch state: `ed01757`, plan files untracked. During the review another session modified `.claude/agent-memory/code-reviewer/*.md`. I did not touch those files.
- Date: 2026-11-10

Plan facts I re-derived on my own all hold. The diff is 14 rows added, 1 changed (`status.tags` `sys.info_view` → `tag.view`), 0 removed. Counts are 108 rows / 13 namespaces, R58 W18 C17 D4 B7 F4, TTL 60000 = 29, confirm = 5. Firmware codes go from 46 to 53 (the 7 the plan lists, none removed). High-risk rows go from 18 to 23. `source_blob` is `84ff812f…` and `source_hash` is `8cde6ab4…`. The findings below are about what the plan's checks fail to prove, and about trust gaps in the re-pin path.

---

## Finding 1: Permission changes on open rows ride a re-pin with no durable guard; the plan's review of them is an uncommitted scratch diff
- **Severity:** High
- **Location:** Phase 2, "Implementation Steps" 2 (`Status_tags_needs_tag_view_and_sys_info_view_alone_is_not_enough`) and 4 ("script node chỉ đọc, trong scratchpad, không commit"). plan.md "Rủi ro" rows 1-2. Phase 3 "Docs" (the re-pin command with `--table-count 108` becomes the documented procedure).
- **Flaw:** The §11 permission column is the only authorization boundary. The device checks no permission, and anyone holding the command key has full authority. This re-pin changes the permission of an **open** row (`status.tags`). The plan finds that change only through a manual diff, and the script for it is thrown away. Durable tests pin the permission of only some of the 34 open rows. `Committed_catalog_content_is_pinned` gets updated in the same commit by design, so it does not act as a guard. A later re-pin that widens an open read (or a non-high-risk write) passes every committed test once the count and hash literals are updated, which the documented procedure tells the operator to do anyway.
- **Failure scenario:** A later §11 revision moves `network.get_all_status` (open, `net.priority_view`) or `battery.get_status` (open, `sys.battery_view`) to `sys.info_view`. The cause could be a typo on a feature branch; both pins live only on feature branches (Finding 2). The operator reruns the documented command. If the row count also moved, the error prints the new number to paste (Finding 5). Then they update the count literals, `PinnedCatalogHash` and the pair-set literal. Nothing pins the `sys.info_view` audience, so CI stays green. Every holder of `sys.info_view` can now read network or battery state, and the device signs and runs it (R11).
- **Evidence:**
  - Spec `c80797d` L415 (R11): "Thiết bị **không kiểm permission**: ai có khoá lệnh là có toàn quyền. Server phân quyền người dùng theo cột permission ở §11 trước khi ký". L1349: "không giới hạn authority của command key ở firmware". L1362: "Thiết bị không kiểm tên quyền này: server kiểm trước khi ký". `src/Hms.Server.Infrastructure/Commands/PinnedCommandCatalog.cs:9-12` calls the mapping "a security boundary".
  - `tests/Hms.Server.UnitTests/CatalogTests.cs:26-30`: the hash literal is "update[d] … in the same change". `:83-87` is the only content pin.
  - Durable per-row permission pins with the real catalog cover only part of the open set:
    - High-risk rows only: `CommandPermissionResolverTests.cs:133-150`.
    - The Theory rows `control.set_output`, `adc.get_status` and `mqtt.get_status`: `CatalogTests.cs:297,300,301`.
    - `system.get_info`: `ManagementApiTests.cs:233`.
    - Audience lists for `adc.view` / `sys.time_view` (`ManagementApiTests.cs:349-357`) and `sys.time_*` / `sys.hostname_*` (`:375-392`).
    - Nothing pins the audience of `sys.info_view`, `net.priority_view`, `sys.battery_view`, `eth.view`, `modem.view`, `wifi.sta_view`, `wifi.ap_view`, `gpio.do_view`, or `mqtt.status_view` for `mqtt.get_config`.
  - Under §11 @ `c80797d` ∩ the allowlists, `sys.info_view` opens exactly 8 actions: `ota.get_status, status.dump, status.services, system.get_device_id, system.get_heap, system.get_info, system.get_operations, system.ping`. The plan's new test asserts only `Contains(status.dump)`, `Contains(system.get_info)` and `DoesNotContain(status.tags)`.
- **Suggested fix:**
  - Add one committed fact that maps all 34 enabled rows as `type.action → class, required_perm, ttl_ms, confirm_template`. Copy it once from the reviewed JSON; after that it is maintained by hand and never regenerated. This re-pin's diff to that fact must then be exactly one line (`status.tags: sys.info_view → tag.view`). Any later change to an open row then needs its own reviewable edit, separate from the hash literals.
  - Commit the step-4 diff script (for example `scripts/diff-command-catalog.mjs`), or at least its full output in the phase-2 report.
  - Change the `infoViewer` assertion to `Assert.Equal` on the 8-action list above.

## Finding 2: "Only the objects of the 40-hex commit" is not true. Replace refs let a forged spec pass every check, and Phase 1 locks the unsafe git invocation into a test
- **Severity:** Medium
- **Location:** Phase 1, "Requirements → `readSpecAtCommit`" and Step 1 test `readSpecAtCommit reads the spec only from git objects of the pinned commit` (argv deepEqual). Phase 2 step 0 (cross-check via `ls-tree` and `git show | sha256sum`). plan.md Decision 2 and "Rủi ro" row 3 (HIL session in HMS-IDF, rated Medium × High).
- **Flaw:** The generator runs `git -C <idfRepo> …`, which inherits the environment and HMS-IDF's repo-local refs and config. Git honours `refs/replace/*` by default, so `rev-parse --verify <sha>^{commit}` still returns the pinned sha while reads of its tree resolve through the replacement. The blob check compares the returned bytes with a blob id that came from the same replaced tree, so it passes. The Phase 2 step 0 cross-check (`ls-tree`, `git show | sha256sum`) goes through the same replacement. Neither check is independent of git's object lookup.
- **Failure scenario:** Another session with write access to HMS-IDF's `.git` (the HIL session the plan names) runs `git replace <pinned> <other-commit>`. The next re-pin, following the procedure that phase 3 documents, regenerates the catalog from different §11 bytes. `source_blob` and `source_hash` match what `ls-tree` and `git show` report, and the operator pins them. This re-pin is shielded only because the plan carries literals (`84ff812f…`, `8cde6ab4…`) computed today. Future re-pins compute those literals with the same replace-honouring git.
- **Evidence:**
  - `scripts/generate-command-catalog.mjs:395-398` (`execFileSync('git', ['-C', idfRepo, ...args])`, default env), `:402`, `:409-413`.
  - Probe in a scratch repo (git 2.55.0.windows.3), after `git replace <genuine> <forged>`:
    - `rev-parse --verify <genuine>^{commit}` returns `<genuine>`.
    - `rev-parse <genuine>:d/spec.md` and `ls-tree` both return the forged blob `fb4057c0…`.
    - `git show` returns the forged bytes, and their git blob hash is `fb4057c0…`, so the generator's L411 check passes.
    - `git --no-replace-objects` returns the genuine blob `6fc350c5…`.
  - HMS-IDF today: `for-each-ref refs/replace` is empty, so the current re-pin is not affected.
  - Provenance: `c80797d` sits only on `feat/next-work-20261019` and `claude/status-tags-paging` (plus their origin copies). It is not an ancestor of `origin/main` or `origin/develop`. Neither is `83e93d77e`. A rebase or squash at merge can orphan the pinned commit; it can then be GC'd and `--check` can no longer be reproduced.
- **Suggested fix:**
  - In `readSpecAtCommit`, call `git --no-replace-objects -c core.fsmonitor=false -C <repo> …`. Pass an env without `GIT_DIR`, `GIT_OBJECT_DIRECTORY`, `GIT_ALTERNATE_OBJECT_DIRECTORIES`, `GIT_REPLACE_REF_BASE` and `GIT_CONFIG_*`.
  - Make the Phase 1 argv test assert the flag.
  - Run the Phase 2 step 0 cross-check with `--no-replace-objects`.
  - In the docs, record that the pinned commit is on unmerged branches. Ask the firmware owner to tag it (protected tag) so it survives a rebase.

## Finding 3: High-risk is enforced only when a grant is written. The rollback is not "inert", and deferring `sms.delete`/`sms.view` cannot be undone later
- **Severity:** Medium
- **Location:** plan.md "Rollback" (Phase 2 bullet: "mã không còn trong từ vựng thì không khớp quyền nào"), "Tương thích ngược → Role đang có", Decision 5 + Open question 10. Phase 2 "Risk Assessment" last row. Phase 3 "Báo cáo role" item 6.
- **Flaw:** `IsHighRisk` is consulted only when a role or membership is written. Nothing re-checks it when a command is sent, and existing grants are never re-validated. Assigning an existing role runs no vocabulary check, and the "own permissions" ceiling is built from the raw role set, unknown codes included. The plan's rollback reverts `HighRisk` together with the catalog, and the master-only rule on the 3 codes disappears with it. Memberships created in that window survive the roll-forward.
- **Failure scenario:**
  1. Phase 2 ships. A master-admin creates role R `{device.view, gpio.do_force}` and assigns it to admin A (who has `permissions.manage`).
  2. Phase 2 is reverted. `gpio.do_force` leaves both the vocabulary and `HighRisk`. A assigns R to user B. `AssignDeviceMembershipHandler` runs no `CheckVocabulary`. `CheckRoleChange` no longer sees a high-risk code, and A's `own` set contains `gpio.do_force`. The result is OK.
  3. Phase 2 is re-applied. B now holds `gpio.do_force` from a non-master, and nothing removes it.
  4. When `control.force_set` opens, B can force relays. The force persists across reboot (spec R15, L419).

  The same mechanism defeats "xem lại khi mở SMS". Spec R13 (L417) gates `sms.get`, `sms.send` and `sms.delete` identically (primary only, signed posture, synced clock, TTL as ceiling), yet only `sms.send` becomes high-risk. Any `sms.delete`/`sms.view` delegation made before the SMS plan stays valid after a later promotion.
- **Evidence:**
  - `IsHighRisk` consumers (grep `src`): only `DeviceAccessHandlers.cs:57, 106, 166, 324, 367-369` and `CommandPermissionResolver.cs:36-38` → `MeController.cs:71`.
  - Send path: only `RequiresMasterAdmin` (Flash), `SendDeviceCommandHandler.cs:123`.
  - Assign: `DeviceAccessHandlers.cs:192-254`, with no `CheckVocabulary` (contrast `:56`, `:96`). `CheckRoleChange` is at `:315-327`.
  - `own` comes from `DeviceAccessResolver.cs:35` (raw `role.Permissions`) → `DelegationChain.cs:68-75`. The chain stops at a master appointer (`DelegationChain.cs:31-33`), so own = A's raw role.
  - Reachability is limited today: no HTTP endpoint for roles or memberships (handlers only registered, `DependencyInjection.cs:124-127`). Hence Medium.
- **Suggested fix:**
  - Correct the rollback text.
  - Make the `HighRisk` addition survive a catalog revert, either as its own commit or with the rollback instruction "revert JSON + L21, keep `HighRisk`". A high-risk code outside the vocabulary is harmless, but the subset test `DevicePermissionCodesTests.cs:30-32` would need a rollback note.
  - For non-masters, have `CheckRoleChange` (or Assign) fail closed on codes outside the current vocabulary.
  - Decision 3 is user-approved. Present the R13 parity of `sms.delete`/`sms.get` and the "not retroactive" property to the user as a trade-off now, rather than deferring it to the SMS plan.

## Finding 4: The pair-set "second path" goes through the same parser twice, so it does not show the 108 pairs are right
- **Severity:** Medium
- **Location:** Phase 2, step 5. Phase 2 "Success Criteria" ("hash và pair-set có bằng chứng từ hai đường"). plan.md "Rủi ro" row 2.
- **Flaw:** Step 5 hashes `parseSpecManifest` output. The C# side hashes the pairs of the JSON, which the generator wrote from the same `parseSpecManifest` call. Equality only shows that `JSON.stringify` and STJ serialize one list identically. A parser defect (a row skipped, or a wrong `type.action` split) gives equal values on both sides.
- **Failure scenario:** A future spec layout makes the parser read N-1 rows. If the prose count is also off, `--table-count N-1` is accepted (Finding 5). Step 5 "matches", and the success criterion is ticked.
- **Evidence:**
  - `CatalogTests.cs:101-111` (`PairSetSha256(catalog.List())`) and `:561-570`.
  - The generator writes those pairs: `generate-command-catalog.mjs:415-427`.
  - A parser-independent extraction (awk over lines matching ``^| `type.action` `` inside §11 of `git show <rev>:…`) reproduces today's pin `dbda8317…` for `83e93d77e` (94 pairs). For `c80797d` (108 pairs, 13 types) it gives `4286c13cd55ef43ca344ba98d07f7e99b98f111872d408d2afb97ec6017c2b07`. The same extraction diffs to exactly 14 added and 1 changed, which matches the plan's table.
- **Suggested fix:** In step 5, use the awk/grep extraction (not `parseSpecManifest`) and put the expected value above into the plan as the second path. Also diff the 6 columns of each extracted row against the JSON.

## Finding 5: The flag keeps first-match counting on a sentence the spec marks as history, so it never retires and the count check becomes a self-check
- **Severity:** Medium
- **Location:** plan.md Decision 1. Phase 1 "Requirements → `assertStatedCounts`" (error tail "rerun with --table-count <row count>"). Phase 3 "Docs" ("bỏ cờ khi spec sửa câu đếm (bộ sinh sẽ báo 'is not needed')"). plan.md "Rủi ro" row 1.
- **Flaw:** The first "N cặp" in §11 sits in a sentence labelled as history: "Snapshot nền lịch sử trước G01 có 98 cặp, 12 namespace." The firmware owner can answer spec question 8 by fixing the "Kiểm lại" sentence and still have no reason to edit history. In that case the first match stays 98 and the "is not needed" warning, the plan's main safeguard against the flag becoming a habit, never fires. The new error tail also tells the operator to paste the row count, and the companion error prints `(M rows)`. With `N` taken from the parser's own count, the check accepts any table the parser reads, added rows included.
- **Failure scenario:** The spec gains 3 rows. The operator's `--table-count 108` fails with "(111 rows)". They rerun with 111 and see the same two "accepted by" warnings they see on every re-pin. The only automated tripwire against unannounced rows is gone for good.
- **Evidence:**
  - Spec `c80797d` L1351: "Snapshot nền lịch sử trước G01 có 98 cặp, 12 namespace." L1352 states 100/13. The table has 108/13.
  - `generate-command-catalog.mjs:170-171` (non-global regexes) and `:375` (`PAIR_COUNT.exec(prose)`, first match).
  - Question 8 (`plans/reports/brainstorm-261108-1700-mqtt-spec-coverage-and-requests.md:78-81`) asks for "một câu đếm duy nhất". The plan does not make removing the flag depend on the historical sentence going away.
- **Suggested fix:**
  - Bind the override to both numbers, for example `--accept-stated-count 98:108` (prose:table). It fails if either side changes, so a corrected sentence or new rows both force a fresh review.
  - Alternatively, require that every count sentence is either consistent or explicitly accepted.
  - State in the docs that the flag retires only when no count sentence disagrees.

## Finding 6: The "`tag.view` passes the permission check" criterion is backed only by a negative-set assertion
- **Severity:** Medium
- **Location:** Phase 2, step 2, `Status_tags_needs_tag_view_and_sys_info_view_alone_is_not_enough` ("code không thuộc {DEVICE_PERMISSION_REQUIRED, DENIED_UNTIL_ACTION_CONTRACT, CATALOG_STALE}"). plan.md Acceptance "`status.tags`".
- **Flaw:** The assertion holds for many outcomes that never reach the permission check, or that refuse after it. So the positive half of the boundary is not proven at the send path; only `allowed-commands` covers it.
- **Failure scenario:** The fixture puts the role in the wrong organization, so the role is dropped and the result is `DEVICE_NOT_FOUND`. Or a later refactor maps `status.tags` to `PERMISSION_UNMAPPED`. Either way the test stays green while no `tag.view` holder can read tags.
- **Evidence:**
  - Codes that satisfy "not in the set": `SendDeviceCommandHandler.cs:46` (`COMMAND_PAYLOAD_INVALID`), `:59` (`KEY_GENERATION_FENCE`), `:71-74` (`DEVICE_NOT_FOUND`), `:95-99` (`COMMAND_NOT_IN_CATALOG`), `:108-112` (`PERMISSION_UNMAPPED`).
  - The reservation is made only after every pre-sign gate (`:171`).
  - Existing tests assert exact codes (`ManagementApiTests.cs:398-405`). Role org wiring: `DeviceAccessResolver.cs:25-26`.
- **Suggested fix:** For the `tagViewer` send, assert `FindPlan(deviceId, correlationId)` is non-null (this proves the permission check and every pre-sign gate passed) and assert the exact code the harness returns. Keep `FindPlan` null for `infoViewer`.

## Finding 7: High-risk codes have a second grant channel that is reachable over HTTP and unchecked, and the plan does not list it
- **Severity:** Medium
- **Location:** plan.md Decision 9, Open question 4, Acceptance "High-risk". Phase 3 "Báo cáo role" item 5.
- **Flaw:** The plan treats role and membership handlers as the only way to grant device codes. Device-action grants accept any string over HTTP from any `permissions.manage` holder: no vocabulary, `HighRisk` or own-permission check. Today they do not authorize commands, but nothing locks that in. `/me/permissions` already returns them, and the frontend already has an (unused) helper that treats them as authorization.
- **Failure scenario:** A non-master admin POSTs `{action:"gpio.do_force"}` for a user and device. The call returns 200, and `/me/permissions` now shows `device:<id>:action:gpio.do_force`. A later change wires `canDeviceAction`, or a server path, to these grants. At that point the master-only rule on the new codes is bypassed without any test failing.
- **Evidence:**
  - `PermissionGrantsController.cs:81-101` and `DevicePermissionsController.cs:46-89` (`:87`) → `GrantDeviceActionHandler` (`PermissionContracts.cs:220-229`, only `IsNullOrWhiteSpace`).
  - `MeController.cs:30` returns these grants.
  - `frontend/src/core/auth/permissions.ts:72-81` (`canDeviceAction`, no callers).
  - Inert for commands today: `DeviceAccessResolver.cs:23-35` reads only membership roles.
- **Suggested fix:**
  - Add an integration test: a non-master device-action grant of `gpio.do_write` or `sys.reboot` must not add anything to that user's `allowed-commands`, and a send must stay `DEVICE_PERMISSION_REQUIRED`. This locks "device-action grants never authorize commands".
  - Alternatively, reject firmware and high-risk codes in `GrantDeviceActionHandler`.
  - Mention the channel in `affected-roles.md`.

---

## Fact check (sampled claims)

| Phase | Claim | Result |
|---|---|---|
| 1 | Spec is read via git objects at `:402-413` | VERIFIED `generate-command-catalog.mjs:402,409,410,411-413`. It is **not** limited to "only objects of the sha" (replace refs; Finding 2). |
| 1 | `PAIR_COUNT`/`NAMESPACE_COUNT` `:170-171`, `assertStatedCounts` `:373-386`, `parseArgs` `:446-463` not exported, `parseSpecManifest` `:177-204` called at `:415` | VERIFIED |
| 1 | 11 generator tests | VERIFIED by count of `test(` (`test.mjs:61,83,90,117,137,143,226,250,257,264,344`); not run |
| 1 | No CI runs the generator; no root `package.json` | VERIFIED (no `.github`/`.gitlab-ci.yml`; `CatalogTests.cs:26-29`) |
| 1 | Spec L1351 98/12, L1352 100/13; table 108 rows / 13 types | VERIFIED (independent awk count) |
| 1 | Fixture text `'6 cặp, 4 namespace trong snapshot.'`, `mutateSpec` | VERIFIED `test.mjs:201,221` |
| 2 | 14 added / 1 changed / 0 removed, columns as tabled | VERIFIED (independent diff) |
| 2 | Counts R58 W18 C17 D4 B7 F4; TTL60000 29; TTL120000 79; confirm 5; firmware codes 53 (+7); high-risk rows 23 | VERIFIED |
| 2 | `source_blob` `84ff812f…`, `source_hash` `8cde6ab4…` | VERIFIED (`ls-tree`, sha256 of `git show`), both replace-honouring (Finding 2) |
| 2 | Test lines `ManagementApiTests.cs:191, 250-253, 302-337, 340-358, 460-491, 534-543, 923, 1460`; `DeviceCommandAuthorizationTests.cs:189-221` (all-open `InMemoryCommandCatalog` at `:25-26`); `DeviceAccessTestFixture.cs:20` | VERIFIED |
| 2 | `DeviceAccessHandlers.cs:54-57`, `:94-106` order | VERIFIED |
| 2 | Second layer fails closed for the other B/W/C rows | VERIFIED `OutputCommandPolicy.cs:49-50`, `ConfigCommandPolicy.cs:74-81` (also covers D). There is **no** second layer for R rows; the 9 new reads rest on `EnabledActions` alone (the plan's new closed-rows fact covers this). |
| 3 | No HTTP endpoint creates or assigns device roles | VERIFIED (`DependencyInjection.cs:124-127` only). Device-action grants are a separate HTTP channel (Finding 7). |
| 3 | `Program.cs:58-87` seeds master-admin only; `DeviceAccessPolicy.cs:37-38` master bypass | VERIFIED |
| 3 | Migration tables and columns for the SQL | VERIFIED `20261005143949_AddDeviceRolesAndMemberships.cs:35-41,60-66,88-92` |
| 3 | Frontend: no `status.tags`, no non-test `highRisk`, two identical `catalogVerification.ts`, `App.vue:778-783`, `rebootPlan.ts:168` | VERIFIED |
| 3 | `MayReadReply` uses the current catalog permission | VERIFIED `DeviceCommandsController.cs:105,121-130` |

## Contract consumers (all, file:line)

- **Generator CLI / `parseArgs` (+`--table-count`):**
  - Code: `generate-command-catalog.mjs:393`.
  - Docs and comments: `docs/architecture-device-mqtt-api-catalog.md:290`, `docs/journals/261005-device-permissions-phase-01-catalog-from-spec.md:25`, `CatalogTests.cs:26-29`, `CommandCatalogHash.cs:25`.
  - Error-text regexes: `test.mjs:282-286`.
  - No CI, no package script, no `tools/` caller.
- **`parseSpecManifest` return shape (+`warnings`):**
  - Callers: `generate-command-catalog.mjs:415`; `test.mjs` (import `:13`, tests `:226-341`).
  - The CRLF/NFD test compares whole objects (`:257`).
  - `canonicalize`/`renderCatalog` read only the two tables (`orderedTables` `:94-106`).
- **New exports `readSpecAtCommit`, `SOURCE_COMMIT`, `SOURCE_PATH`:** `main` and the test only.
- **`DevicePermissionCodes.HighRisk` / `IsHighRisk`:**
  - Create `DeviceAccessHandlers.cs:57`.
  - Update `:106`, plus its per-device loop via `:128`.
  - Delete `:166`.
  - `CheckRoleChange` `:324`, used by assign `:225-228` and remove `:289-292`. For the 3 new codes, non-masters can also no longer *remove* such memberships or *delete* such roles; the plan's tests cover create, update and assign only.
  - `RoleRules.CheckHighRisk` `:367-369`.
  - `CommandPermissionResolver.cs:36-38` → `MeController.cs:71`.
  - Tests: `DevicePermissionCodesTests.cs:21-44`, `CommandPermissionResolverTests.cs:133-150`, `DeviceAccessHandlerTests.cs:173-204, 308-316, 564-581`, `ManagementApiTests.cs:250-253`, `PhysicalOutputSafetyTests.cs:294`.
  - **Not** consumed by: `GrantDeviceActionHandler` (`PermissionContracts.cs:220-229`), or the send path (`SendDeviceCommandHandler.cs:123`).
- **`command-catalog.json`:**
  - Code and build: `Hms.Server.Infrastructure.csproj:31` (EmbeddedResource), `PinnedCommandCatalog.cs:15,181-186`.
  - Generator `--check`: `generate-command-catalog.mjs:431-439`.
  - Tests: `CatalogTests` (`ReadCommittedCatalog`), `DeviceAccessTestFixture.cs:20`, `DevicePermissionVocabularyTests.cs:11-29`, `DevicePermissionCodesTests.cs:30-32`, `CommandPermissionResolverTests`, `ManagementApiTests` (via DI and `PinnedCatalogMetadata()` `:1460`).
- **`GET /api/v1/me/command-catalog`:**
  - Endpoint: `MeController.cs:48-72`.
  - Frontend: `frontend/src/adminApi.ts:167,169`, `frontend/src/core/api/adminApi.ts:231,233` (catalog load and `verifyCatalog`). `requiredPermission` is read at `DeviceCommandModal.vue:115-121` and `commandAvailability.ts:10`; `highRisk` has 0 non-test consumers.
  - Tools: `tools/device-config-check.js:75`, `device-read-check.js:55`, `device-reboot-check.js:183` (version and hash pass-through).
  - Tests: `ManagementApiTests.cs:191,250-253`.
- **`GET /devices/{id}/allowed-commands`:** `DeviceCommandsController.cs:52-78`; `frontend/src/core/api/adminApi.ts:270` → `DeviceReadCard.vue:19`.

Status: DONE_WITH_CONCERNS
Summary: Critical 0, High 1, Medium 6. Plan facts verified. The concerns are a re-pin path that does not guard open-row permissions, replace-ref spoofing of the "pinned commit only" read, high-risk rules that are not retroactive across rollback, a circular pair-set check, a flag that can never retire, a negative-set test, and an unlisted grant channel.
