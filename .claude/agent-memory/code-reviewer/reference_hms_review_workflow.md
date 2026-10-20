---
name: hms-review-workflow
description: How to scope, build and probe an HMS-SERVER phase review when the repo has no git history; scratch copy, exact fix-round diffs, reusing the tester's mutation harness, EF DI probes, what never to run in the repo
metadata:
  type: reference
---

HMS-SERVER has no git repository, so a phase's change set cannot be diffed. List it by mtime against the previous phase's PM sync report: `find src tests -type f -newer plans/<plan>/reports/pm-*-phase-NN-sync.md -not -path '*/bin/*' -not -path '*/obj/*'`. For phase 3 of plan 261005-0556 this matched the controller's file list exactly.

- Fix-round reviews: the tester's mutation copy from the previous cycle (e.g. `scratchpad/phase03-mut/`) was byte-identical to the repo before the fixes, so `diff -u --strip-trailing-cr <copy>/<file> <file>` gives the exact fix diff. Check the copy's results.txt INTEGRITY lines first.
- Scratch copy: Git Bash has no rsync; use `tar --exclude='*/bin' --exclude='*/obj' -cf - Hms.Server.slnx src tests | (cd <scratch> && tar -xf -)`. A full build takes about 5 s and the full test run about 10 s, so a 30-mutant campaign takes about 6 minutes.
- Mutation harness: reuse the tester's `mutate.js` (raw-byte restore, baseline, watchdog, integrity) by splicing `const mutants = require("./mutants-x.js");` in place of its array. `--check` writes check.txt; `--only=` appends to results.txt; a full run truncates it. Do not build a second copy while a campaign runs (timing tests).
- Write JS/C# probe and mutant files with the Write tool: a Bash heredoc with mixed quotes failed in this environment. Python on Windows converts LF to CRLF unless `newline=""`; the sources use LF.
- Gate claims: a scratch build of the exact repo tree reported xUnit1031 analyzer warnings that the controller's "0 warnings" omitted. The repo has no Directory.Build.*, .editorconfig or NoWarn, so check `dotnet build` output yourself.
- The EF/Npgsql DI branch can be checked without PostgreSQL: `AddHmsInfrastructure(databaseConnectionString: "Host=localhost;...")`, then `BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true })` and resolve the services in a scope. Constructors never open a connection. Raw SQL and transactions in `EfAuthorizationEpochStore`/`EfPermissionStore` stay review-only (no retrying execution strategy is configured, so `BeginTransaction` is legal).
- Never run the frontend gate in the repo: `npm run build` rewrites `frontend/dist`. The controller runs it.

Related: [[reference-firmware-c-oracle]] (device contract via the spec only, plus read-only probing pitfalls); [[authz-probe-heuristics]].
