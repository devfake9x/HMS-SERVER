---
title: "Control-plane security hardening"
date: 2026-09-28
status: active
---

# Control-plane security hardening

## Context

The active goal permits implementation of the HMS-SERVER control plane while
live MQTT/EMQX and firmware-side importer behavior remain deferred. The audit
identified P0/P1 authorization, idempotency, CSRF, KMS/Data Protection, and
realtime epoch gaps that were independent of MQTT.

## What Happened

- Reserved `master-admin` role creation and assignment now require the
  master-admin boundary at both API and user-management service seams.
- Permission grants now validate target-user existence, target organization,
  device existence, caller scope, and explicit feature permission.
- Wildcard device-action denies now override exact and wildcard allows.
- Command audits reserve `(device, correlationId)` before transport, persist a
  SHA-256 request fingerprint, reject correlation collisions, and return an
  in-flight result for concurrent retries. EF migrations add the fingerprint
  and persistent device authorization epoch tables.
- Device command mutations and provisioning bundle downloads require CSRF;
  bundle downloads are POST-only so cross-site prefetch cannot consume a
  one-time artifact.
- Production infrastructure now fails closed without an injected,
  non-ephemeral Data Protection provider at the KMS boundary.
- PostgreSQL deployments use a persistent authorization epoch store, and the
  bounded event publisher filters stale epochs before delivery snapshots.
- The frontend now includes typed user/role/three-layer permission flows and
  clears the password field after user creation.

## Reflection

The changes reinforce the chosen modular-monolith boundary: authorization and
idempotency stay in Application, persistence guarantees stay in Infrastructure,
and controllers only provide request/scope enforcement. The live MQTT adapter
was intentionally untouched; no firmware behavior was inferred or claimed.

## Decisions

- A real KMS/HSM provider and external signer remain deployment adapters, so a
  production process without them fails startup rather than silently using a
  profile key ring.
- SignalR delivery is still contract-gated; stale events are rejected by the
  publisher seam, but live connection delivery and two-replica behavior remain
  release limitations.
- PostgreSQL migrations are the source of truth for command fingerprint and
  epoch persistence; in-memory stores remain test/development seams only.

## Next

Run the full CK plan gates, frontend verification, contract fixtures, and drift
scan. Then continue with SignalR delivery/reconnect tests, complete CRUD and
operational frontend evidence, and keep live EMQX/importer/load/HA work in the
explicit deferred release profile.
