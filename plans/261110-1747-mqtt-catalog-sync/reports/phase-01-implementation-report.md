# Phase 1 implementation report

Status: DONE

## Baseline (step 0)
- `node --test scripts/generate-command-catalog.test.mjs`: 11 pass.
- `git status --short src/Hms.Server.Infrastructure/Commands/`: empty.
- `--idf D:/WORKSPACE/PROJECT/HMS-IDF --check`: up to date (hash b1d47a78...).

## Red
- Generator tests (25-test file) against the pre-change generator (HEAD copy in scratchpad): module link error `does not provide an export named 'OUTPUT_PATH'` (whole file red). Note: implementation of the generator was started before this red run was captured; red was recorded afterwards against the HEAD copy.
- `verify-command-catalog-repin.test.mjs` before the script existed: `ERR_MODULE_NOT_FOUND`.

## Green
- `node --test scripts/generate-command-catalog.test.mjs scripts/verify-command-catalog-repin.test.mjs`: 32 pass, 0 fail (11 old + 14 new generator + 7 checker). No old assertion changed.
- `--accept-stated-count 0/1`: exit 1, `--accept-stated-count needs <pairs>/<namespaces>...` (before git).
- Old pin `--check`: up to date, no warning. With `--accept-stated-count 94/12`: exit 1 `... agrees with its action table; remove --accept-stated-count`.
- Independent script, `--rev 83e93d77e...`: 94 rows, 12 namespaces, pair-set `dbda8317758e5f25c5a4dee8456841ef3689fea4707cf5a27c30ffad656f78d9`, 0 mismatches vs current `command-catalog.json`, exit 0.
- Independent script, `--rev c80797d9d5b817174cd2cfbcb6ee62effe503ff2 --base 83e93d77e...`: 108 rows, 13 namespaces, pair-set `4286c13cd55ef43ca344ba98d07f7e99b98f111872d408d2afb97ec6017c2b07`; diff 14 added, 1 changed (`status.tags` required_perm `sys.info_view` -> `tag.view`), 0 removed. Versus the not-yet-repinned JSON: 15 mismatches (14 missing + `status.tags`), exit 1, as expected until phase 2.
- `git status --short src/`: empty. `SOURCE_COMMIT` unchanged. `command-catalog.json` not written.

## Files
- Modified: `scripts/generate-command-catalog.mjs`, `scripts/generate-command-catalog.test.mjs`
- Created: `scripts/verify-command-catalog-repin.mjs`, `scripts/verify-command-catalog-repin.test.mjs`
- Phase file "As built" filled.

## Deviations
See phase file "As built": 7 checker tests instead of 6 (total 32 not 31).

## Not run
Generator CLI against `c80797d` (needs the `SOURCE_COMMIT` change, phase 2). HMS-IDF only touched via `git --no-replace-objects` object reads in the two scripts.
