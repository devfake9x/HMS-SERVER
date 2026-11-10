# Phase 2 implementation report

Status: DONE

## Step 0 (baseline and source)
- Build: 0 errors, 3 warnings (CS8631 ManagementApiTests, 2x xUnit1031 GrantSerializationTests). Unit 658, integration 127, node 32 (phase 1 total; phase file said 31).
- HMS-IDF (git only, `--no-replace-objects`): `refs/replace` empty; blob `84ff812f68567e07cfd3a2dfeeb862ceb48630f6`; sha256 `8cde6ab454b7148bd29ab5a49c5187ec98b1143b4f11f84de76c95d9451c6406`.
- `branch -r --contains c80797d9d5b8...`: `origin/claude/status-tags-paging`, `origin/feat/next-work-20261019`. For `83e93d77e...`: those two plus `origin/refactor-all-upate-v6`.

## Red run (before generator/catalog/HighRisk change)
- Unit: 686 total, 39 failed (catalog 94 rows, status.tags sys.info_view, vocabulary 50/46, HighRisk 9, ttl/confirm counts, 15 row theory cases, 4 role theory cases, new facts).
- Integration: 143 total, 17 failed (14 closed-row cases `COMMAND_NOT_IN_CATALOG`; `Me_command_catalog_exposes_permission_policy_per_action` 94/18; `Me_permissions_and_catalog_are_no_store...` 94; `Status_tags_needs_tag_view...`). `Device_action_grants_open_no_command_whatever_the_code` green at once (locks existing behaviour).
- `Open_actions_keep_their_reviewed_class_permission_ttl_and_confirm` written first with the old `status.tags Read sys.info_view` line: green on the old catalog; then changed to `tag.view`: red until re-pin.

## Generator
`node scripts/generate-command-catalog.mjs --idf D:/WORKSPACE/PROJECT/HMS-IDF --accept-stated-count 98/12`:
```
warning: spec §11 states 98 pairs and 12 namespaces, which disagrees with its action table; accepted by --accept-stated-count 98/12
wrote ...command-catalog.json
  108 commands (F4 R58 W18 C17 D4 B7), 6 config domains, 53 permission codes
  catalog_hash 658e191a9e66f14a6885183b6ae9d9ec72a9e25a396c3cd30c80bdcd4df3413b
  source blob 84ff812f68567e07cfd3a2dfeeb862ceb48630f6, sha256 8cde6ab4...c6406
```
All expected values matched. `--check --accept-stated-count 98/12`: up to date, exit 0. No flag: exit 1 ("states 98 pairs"; no `108`). `100/13` and `108/13`: exit 1 "does not match the count §11 states (98/12)".

## Independent check
`verify-command-catalog-repin.mjs --rev c80797d... --base 83e93d77e... --expect-pair-set 4286c13c...`: 108 rows, 13 namespaces, pair-set `4286c13cd55ef43ca344ba98d07f7e99b98f111872d408d2afb97ec6017c2b07`, 14 added (exactly the plan's keys), 1 changed (`status.tags` sys.info_view -> tag.view), 0 removed, 0 mismatches vs JSON, exit 0.
JSON vs `HEAD`: keys same order; differing top-level fields: catalog_version, catalog_hash, source_commit, source_blob, source_hash, commands; `config_domains` identical.
Pair-set and catalog hash pinned only after: unit pair-set assertion reported `4286c13c...`; generator, JSON, loader (`Committed_catalog_content_is_pinned`, `Pinned_catalog_metadata_points_at_the_pinned_spec`), checker and plan agree.

## Changes
- `scripts/generate-command-catalog.mjs`: `SOURCE_COMMIT` only.
- `command-catalog.json`: generated.
- `DevicePermissionCodes.cs`: HighRisk + `gpio.do_force`, `gpio.do_config`, `sms.send`, `sms.delete`; comment on write-time check.
- `PinnedCommandCatalog.cs`: comment only (allowlists untouched; EnabledActions still 34).
- Tests: CatalogTests, DevicePermissionVocabularyTests, DevicePermissionCodesTests, CommandPermissionResolverTests, DeviceCommandAuthorizationTests, DeviceAccessHandlerTests, ManagementApiTests (as listed in the phase file).

## Green
- Unit 686/686 (658 + 28). Integration 143/143 (127 + 16). Node 32/32. Build 0 errors, same 3 warnings as baseline (rebuilt with `--no-incremental`). Frontend untouched, so vitest/vue-tsc not run.

## Deviations
- xUnit `Assert.Contains` on `FrozenSet`/`IReadOnlySet` is ambiguous (CS0121) and `Assert.True(set.Contains(..))` raises xUnit2017; used a cast to `IEnumerable<(string Type, string Action)>` for `("status","tags")`, `Assert.True(...Contains(x), x)` on `FirmwarePermissions` and `Assert.False(allowlisted.Contains(x), ...)` for the closed-row check (no new warnings).
- Class letters of new rows asserted as per generator: `sms.send` B -> Physical, `sms.delete` D -> Destructive, force/set_config B -> Physical.

## Not run / notes
- Frontend suites, HMS-IDF working tree, broker, user-secrets: not touched. No commit, stash or push.
- Phase file "As built" filled.
