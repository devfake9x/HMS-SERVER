# Code review: catalog re-pin to c80797d (working tree vs ed01757)

Reviewer: code-reviewer. Date: 2026-11-10. Read-only review; no source, test, script or doc was changed.

## Scope

- Files: `scripts/generate-command-catalog.mjs` (+ test), `scripts/verify-command-catalog-repin.mjs` (+ test),
  `src/Hms.Server.Infrastructure/Commands/command-catalog.json`, `PinnedCommandCatalog.cs`,
  `src/Hms.Server.Domain/Authorization/DevicePermissionCodes.cs`, six unit test files, `ManagementApiTests.cs`.
- Docs prose skipped as instructed.
- Not run: integration suite (left to the tester), frontend suites (frontend untouched).

## Overall assessment

No blocking defect found. All six acceptance criteria hold on the evidence below. Non-blocking findings: one
latent risk for whoever opens `sms.*` later, and four gaps in the independent checker and the tests.

## What was run (verified facts)

| Check | Result |
|---|---|
| `node --test` on both script test files | 32 pass, 0 fail |
| Scratch copy `dotnet build --no-incremental` | 0 errors, 3 warnings, none on lines this change added (CS8631 `ManagementApiTests.cs:366`, xUnit1031 x2 `GrantSerializationTests.cs`) |
| Scratch copy unit tests | 686 pass, 0 fail |
| Generator `--check --accept-stated-count 98/12` | up to date, hash `658e191a...3413b`, exit 0 |
| Generator `--check` with no flag | exit 1, "states 98 pairs, which disagrees with its action table" |
| Generator `--check` with `108/13`, with `98/13`, and with `98/12` followed by `94/12` | exit 1 each, "does not match the count §11 states (98/12)" |
| `verify-command-catalog-repin.mjs --rev c80797d... --base 83e93d77e... --expect-pair-set 4286c13c...` | 108 rows, 13 namespaces, 14 added, 1 changed (`status.tags` `sys.info_view` -> `tag.view`), 0 removed, 0 catalog mismatches, exit 0 |
| Raw recount of §11 at c80797d (`git --no-replace-objects show`, `awk`, `grep -c`) | 108 dotted action rows; 118 table lines (108 + 6 domain rows + two 2-line headers) |
| `git diff` of `command-catalog.json` | only metadata, 14 added rows and `status.tags.required_perm`; `config_domains` untouched |
| 5 mutants on the scratch copy (unit tests only) | 5 of 5 killed |

Mutants:

| Mutant | Killed by |
|---|---|
| add `tags.list` to `EnabledActions` | 4 `CatalogTests`, including the new closed-row and open-action tests |
| drop `gpio.do_config` from `HighRisk` | 4 tests, including the new `DeviceAccessHandlerTests` theory |
| drop `sms.delete` from `HighRisk` | 4 tests, same set |
| `_ => false` to `_ => true` in `ToDefinition` (opens class D) | 3 `CatalogTests` |
| class B opens for any `control.*` row | 2 `CatalogTests` |

## Acceptance criteria

**(a) Catalog content and closed rows: met.**
- 108 rows, 13 namespaces, version `mqtt-api-c80797d9d`: confirmed three ways (generator, checker, raw recount).
- The four allowlists in `PinnedCommandCatalog.cs` are untouched (the diff is comment-only). The open set is 34
  rows in total across the four lists, pinned line by line in
  `Open_actions_keep_their_reviewed_class_permission_ttl_and_confirm`.
- The 14 new rows resolve to `DeniedUntilActionContract` (`PinnedCommandCatalog.cs:249-259`).
- `SendDeviceCommandHandler.cs:101-104` refuses them before the permission check, the master-admin check, the
  audit reservation and signing. No other code in `src` names these actions.

**(b) `status.tags` needs `tag.view`: met.**
- The permission comes only from the catalog row; `DeviceAccessPolicy.Evaluate` checks the member's role.
- No migration, seed or built-in role is in the change (`git status src` shows three files). `DeviceRole.Create`
  is called only from the role-create handler.
- Consequence, as designed: every non-master holder of `sys.info_view` loses `status.tags` at deploy until a role
  gains `tag.view`.

**(c) High-risk grant paths: met.**
- All five write paths go through `DevicePermissionCodes.IsHighRisk`: role create (`DeviceAccessHandlers.cs:57`),
  update (`:106`, old and new codes), delete (`:166`), membership assign and remove
  (`DelegationRules.CheckRoleChange`, `:324`, new and current role).
- No existing role can already hold the four codes: they were outside the old vocabulary, and `CheckVocabulary`
  runs on create and update.
- The legacy `POST /api/v1/permissions/device-actions` accepts any action string from a non-master admin, but
  `DeviceActionGrants` is read only by three controllers for display. Command authorization never reads it.

**(d) Generator: met.**
- Without the flag, or with a value other than what the sentence states, the run fails (table above).
- The flag also fails once the sentence agrees with the table.
- The spec is read through `readSpecAtCommit`: 40-hex commit only, `rev-parse --verify`, blob id, `show`, and the
  bytes must hash to the blob; `--no-replace-objects`; `GIT_*` variables stripped. No working-tree read exists.
- Limit to be aware of: once the flag is accepted, the count sentence checks nothing about the table. The table
  is held by the pinned commit, `PinnedCatalogHash` and the checker, not by the flag.

**(e) Regressions and contracts: met.**
- No caller in `src` or `frontend/src` hard-codes `status.tags`, the new codes, row counts or the old hash.
- The public contract change is catalog content, version and hash only. Clients holding the old hash get
  `CATALOG_STALE` until they refetch, as on every re-pin.
- No new warnings.

**(f) Tests: met.**
- No assertion was removed; changed literals follow the new catalog.
- New tests assert behaviour (refusal codes, `FindPlan` null or non-null, exact allowed-command lists) and die
  under the five mutants.

## Findings

### Medium

**M1. Class B and D are no longer relay-only; three code paths assume they are (latent, closed rows only).**
- `sms.send` is class B (`Physical`) and `sms.delete` is class D.
- `src/Hms.Server.Infrastructure/Persistence/EfCommandAuditStore.cs:280`: `SummarizePayload` stores the payload of
  every Physical and Destructive command. For `sms.send` that is a phone number and message text in the audit table.
- `src/Hms.Server.Application/DeviceLink/DeviceOutputState.cs:128` and `DeviceLinkHandlers.cs:139`: any Physical
  reply or timeout marks the relay outputs stale.
- `src/Hms.Server.Application/Commands/CommandLanes.cs:11`: Physical is kept out of the write lane.
- Not reachable today: the rows are refused at `SendDeviceCommandHandler.cs:101`, and `OutputCommandPolicy`
  fails closed for any other class B row.
- Failure scenario: a later change adds `("sms","send")` to `PhysicalOutputActions` and extends
  `OutputCommandPolicy`. SMS text then lands in command audit rows and every SMS flips the output panel to stale.
- Fix: record it as a precondition next to `PhysicalOutputActions` or in the plan's follow-ups. No code change is
  needed for this re-pin.

### Low

**L1. The independent checker skips near-miss rows silently.**
- `scripts/verify-command-catalog-repin.mjs:53-54`: a §11 line that does not match `ROW_LINE` exactly (two spaces,
  a lower-case class, a new column) is dropped with `continue`.
- It surfaces only indirectly ("in the catalog but not in the spec"), and only if the generator parsed that row.
  The printed `rows N` is then a lower bound, not a count.
- Verified not to bite at c80797d: the raw recount gives 108.
- Fix: throw on any line in the section whose first cell is a backticked `x.y` and that does not match `ROW_LINE`.

**L2. The checker does not tie the catalog to the revision it checked.**
- `compareWithCatalog` (`:95-125`) compares `commands` only. It ignores `source_commit`, `source_blob`,
  `source_hash` and `config_domains`. `readSpecAt` (`:133-138`) does not hash the bytes against the blob.
- Failure scenario: `--rev` given a commit other than the catalog's `source_commit`, with equal rows, exits 0.
- `config_domains` drift between pins is covered only by the generator and the catalog hash (unchanged this time,
  per the JSON diff).
- Fix: compare `catalog.source_commit` with `--rev`, and print or compare the domain rows.

**L3. The checker CLI has no tests.**
- `parseArgs` and `main` of the checker are neither exported nor tested: exit code on a mismatch, on a wrong
  `--expect-pair-set`, on bad arguments.
- The pure helpers are tested, but the exit code is what the re-pin procedure relies on. This review exercised
  the passing case only.

**L4. The TTL-cap test mirrors the handler's predicate.**
- `tests/Hms.Server.UnitTests/DeviceCommandAuthorizationTests.cs:215-219` computes the expected cap with the same
  `Physical or Flash` expression as `SendDeviceCommandHandler.cs:143`.
- It proves that 60 s rows are capped, but not which rows get the tighter cap. The pattern existed for Flash;
  this change widened it.
- Fix: pin two rows by name (`control.force_set` tight cap, `sms.delete` normal cap).

### Informational

- The no-flag error prints the exact flag to paste (`rerun with --accept-stated-count 98/12`). The exact-value
  rule therefore detects a later change of the sentence, not a reviewer's independent count. That is fine while
  the procedure keeps the checker run mandatory.
- `PAIR_COUNT` and `NAMESPACE_COUNT` (`generate-command-catalog.mjs:173-174`) each take their first match in the
  section independently, so "the first count sentence" holds only while both numbers sit in one sentence.
- The legacy device-action grant endpoint still stores high-risk action names for non-masters (inert). The new
  integration test locks that it opens nothing; `canDeviceAction` in the frontend has no caller.
- `CommandPermissionResolver.cs:42` still says "no HTTP endpoint creates roles or memberships yet". Not part of
  this change.

## Plan status

- Phase 1 (generator flag, checker) and phase 2 (re-pin, HighRisk, tests): complete as reported. The reported
  numbers were reproduced (32 node tests, 686 unit tests, hashes, 3 baseline warnings).
- Phase 3 (docs): in progress by another agent, not reviewed.

## Unresolved questions

- Should M1 be written down as a gate for opening any non-`control` class B or D row, and where: the plan's
  follow-ups, or a comment at `PhysicalOutputActions`?
- Will the pinned commit be tagged in HMS-IDF? Both pins still sit only on feature branches (phase 2 report),
  and `--check` has no fallback if a branch is rewritten.

Status: DONE_WITH_CONCERNS
Summary: No blocking defect; all six acceptance criteria hold, backed by a scratch build, 686 unit tests, 32 node tests, live generator and checker runs, a raw §11 recount and 5 killed mutants.
Concerns/Blockers: M1 (class B/D assumptions before any `sms.*` row opens) and L1-L4 (checker strictness and test gaps) are non-blocking. The integration suite was not run by this review.
