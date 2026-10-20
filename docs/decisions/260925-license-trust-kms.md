---
title: "License trust store and KMS boundary"
type: adr
date: 2026-09-25
status: accepted-for-plan
---

# ADR: License Trust Store and KMS Boundary

## Context

HMS-IDF validates signed license bytes and binding fields locally. HMS-SERVER
must request signing without loading a raw private PEM into the web process, and
must preserve immutable binding fields, replay counters and journal semantics.

## Decision

The server keeps a trust store containing `kid`, public key, issuer, status,
`effective_from`, `effective_until`, rotation and revocation audit. Production
signing uses an external signer or KMS/HSM adapter. Envelope encryption and ASP.NET
Data Protection keys use a KMS-backed secret root. A signer/KMS outage fails closed;
no private material is copied into application configuration, JSON DTOs, logs,
backups or frontend state.

Firmware `license_apply()` remains the authority for device journal ordering. The
server contract models pre-marker old-generation survival, exact-pending resume,
and post-marker roll-forward or `recovery-required`; it never promises an
unconditional rollback that firmware cannot provide.

## Verification

Phase 4 signer/trust-store contract tests, Phase 5 actual NVS crash matrix, and
Phase 8 KMS outage/backup/restore tests. Mocked KMS is only application contract
evidence and is not production KMS proof.
