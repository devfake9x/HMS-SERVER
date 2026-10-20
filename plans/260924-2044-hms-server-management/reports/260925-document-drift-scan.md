---
title: "HMS-SERVER documentation drift scan"
type: documentation-audit
date: 2026-09-25
status: pass-with-historical-markers
---

# Documentation Drift Scan

## Command

The scan checked each known legacy entry point for an explicit
`SUPERSEDED`/`HISTORICAL` banner and rejected misleading current markers in the
first 25 lines. Reproducible command: `node scripts/documentation-drift-check.mjs`.

## Result

| File | Result |
|---|---|
| `ALIGNMENT.md` | `PASS_HISTORICAL_BANNER` |
| `docs/journal.md` | `PASS_HISTORICAL_BANNER` |
| `docs/architecture-hms-server.md` | `PASS_HISTORICAL_BANNER`; misleading current markers rewritten |
| `brainstorm-report.md` | `PASS_HISTORICAL_BANNER` |
| `research-report.md` | `PASS_HISTORICAL_BANNER` |

The stale terms remain inside those files because the files preserve historical
context, but each file now identifies itself as non-normative and points to the
current plan. Misleading `FROZEN`, `ARCHITECTURE READY`, and `100% Protocol &
Data-Plane Alignment` banner claims were removed. The current normative plan
mentions stale terms only when describing exclusions, rejected assumptions, or
the drift gate itself.

## Limitation

This is a scope-aware pre-cook scan, not the final implementation drift gate.
Phase 8 must replace it with a repository script that fails when a new
unmarked current document reintroduces telemetry infrastructure, HKDF/
master-secret derivation, guessed command topics, or unsupported broker modes.
