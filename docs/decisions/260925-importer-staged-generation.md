---
title: "Importer staged generation and candidate identity"
type: adr
date: 2026-09-25
status: new-firmware-contract
---

# ADR: Importer Staged Generation and Candidate Identity

## Context

The active firmware identity API is getter-oriented, while license evaluation
rejects a signed nonempty device ID when active NVS identity is empty or mismatched.
The current HTTP middleware proves human permission auth, not device-bound importer
trust. A combined identity/credentials/key/license importer therefore cannot be
pretended to exist.

## Decision

Treat the importer as a new firmware contract. It uses a device-bound one-time
bootstrap proof, import nonce, expiry, bounded body, local-only authenticated HTTP,
explicit CSRF behavior, replay protection and redacted response. It stages candidate
identity privately, validates license against that candidate through an explicit
seam, commits non-license material, applies the license journal, then commits the
active generation. Boot recovery never activates mixed generations. Invalid,
replayed, lower-generation, remote or conflicting bundles write nothing; post-marker
uncertainty becomes exact-pending resume or `recovery-required`.

Human admin passwords are forbidden in bundles. The active getter cannot expose a
staged candidate during dry-run validation.

## Verification

`fixtures/importer-contract.test.mjs` and `license-recovery-model.test.mjs` are
model/contract evidence. Phase 5 must add actual HMS-IDF NVS/journal tests for every
partition and marker write before release.
