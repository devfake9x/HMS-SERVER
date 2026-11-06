---
name: spec-repin-hazards
description: HMS-IDF mqtt-api.md re-pins - §11 prose counts can lag the table and rows may describe uncommitted firmware; check before trusting a catalog re-pin plan
metadata:
  type: project
---

When reviewing a plan that re-pins `scripts/generate-command-catalog.mjs` to a new HMS-IDF commit, run the
generator's exported `parseSpecManifest` (scratch copy) against `git show <commit>:engineering-docs/api/mqtt-api.md`
first. At `18a63e8ed` (2026-11-05) §11 prose said "98 cặp, 12 namespace" while the table had 104 rows / 13
namespaces, so `assertStatedCounts` throws; the spec also labels tags.* and the Modbus paged reads as
working-tree / NOT_COMMITTED firmware.

**Why:** the catalog is the server's only permission boundary (spec R11); a failing count check invites
weakening the generator guard, and working-tree rows can change permission before release.

**How to apply:** in plan reviews, verify the stated-count check passes, diff old vs new rows (permission/class
changes of already-open actions), and recount high-risk actions by permission (e.g. `sms.send_status` R row carries
`sms.send`). Related: [[device-permission-widening]].
