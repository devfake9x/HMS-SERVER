---
name: catalog-repin-review
description: Failure-mode checks that found real gaps when reviewing an HMS-SERVER command-catalog re-pin to a new HMS-IDF spec commit (pin reachability, §6 drift of open rows, HighRisk shrink on rollback, restart side effects)
metadata:
  type: reference
---

Use these checks when a plan moves `SOURCE_COMMIT` / `command-catalog.json` to a new spec commit (first seen 2026-11-10, plan 261110-1747). For the scratch recount, see [[hms-review-workflow]]. For the grant-time-only HighRisk rule, see [[authz-probe-heuristics]].

- Pin reachability: run `git -C HMS-IDF branch -a --contains <sha>` and `for-each-ref refs/remotes/origin`. The pins have sat on unmerged feature or `claude/*` branches, while `origin/main` and `origin/develop` were months old. If someone squash-merges or force-pushes, the pin is gone, and `--check` has no fallback.
- Plans tend to diff only §11. Also diff the §6 row (the first `| \`type.action\` |`) of every action in the four `PinnedCommandCatalog` allowlists between the two pins. In 2026-11, 7 open rows changed, and `status.tags` changed from an array to a paged reply (default limit 10). Also check the spec's C-table receipts (SLICE_PASS): the allowlist comments use them to justify opening a row.
- `DevicePermissionCodesTests.High_risk_set_is_a_subset_of_firmware_vocabulary` forces `HighRisk` to shrink with the catalog. A rollback therefore demotes codes, and anything a non-master delegates in that window survives the roll-forward. `device_memberships.GrantedBy` lets you audit it.
- `ICommandResponseStore` is in memory (`DependencyInjection.cs:42`). The restart that a re-pin requires drops every stored reply, so any plan text about "old stored replies under the new catalog" is wrong. Ask for a cut-over rule: in-flight writes become OUTCOME_UNKNOWN_AFTER_RESTART.
- The error code is `DEVICE_PERMISSION_UNKNOWN`, not `PERMISSION_UNKNOWN`. Integration `GrantDeviceRole` skips `CheckVocabulary`, so a positive grant test needs `FindPlan` non-null, not a "code not in {…}" assertion.
- Spec class B is not relay-only (2026-11-10: `sms.send` is B, `sms.delete` is D). Server code treats `CommandClass.Physical` as a relay output: `DeviceOutputState` marks outputs stale, `EfCommandAuditStore.SummarizePayload` stores Physical and Destructive payloads (SMS number and text), `CommandLanes` keeps it out of the write lane. Harmless while the row is closed; flag it whenever a plan opens a non-`control` class B or D row.
- Fast implementation gate for a re-pin (about 4 minutes): generator `--check` with and without the flag; `verify-command-catalog-repin.mjs --rev --base --expect-pair-set`; a raw `awk`/`grep -c` recount of §11 (108 dotted rows, 118 table lines at c80797d); then sed mutants on a scratch copy (open a closed row, drop a HighRisk code, `_ => true` in `ToDefinition`). The checker skips near-miss rows silently and ignores `source_commit` and `config_domains`.
