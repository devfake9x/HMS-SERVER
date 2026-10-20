---
title: "HMS-SERVER availability, recovery and rolling compatibility contract"
type: operations-contract
date: 2026-09-25
status: design-target-not-measured
---

# Availability and Recovery Contract

## Scope

This is a control-plane contract for device management, commands, latest status,
capabilities and audit. It does not introduce telemetry/history storage, Kafka,
Redis Streams, TimescaleDB, SCADA dashboards or multi-region active-active.

## Readiness states

- `DESIGN_CONTRACT_READY`: contracts, decisions, fixtures and phase gates are
  internally consistent; no runtime claim.
- `IMPLEMENTATION_VERIFIED`: backend, frontend, firmware importer, EMQX and
  signer tests pass on the implementation.
- `RELEASE_READY`: deployment recovery, load, security, backup/restore, rolling
  upgrade and any claimed HA evidence pass.

## Capacity profile

The current target profile is in `contracts/operations/load-profile-v1.json`:

- 1,000 devices;
- one CI application instance;
- EMQX and PostgreSQL configuration recorded with the measured test run;
- command, status, authentication, reconnect, queue, CPU, RSS and DB-pool
  budgets recorded as targets until measured;
- reconnect jitter must use a pinned seed and documented distribution;
- latest status/capabilities may coalesce; terminal command/revoke events may
  not be silently lost.

No value in the profile is a measured production result. A two-replica soak is
required before claiming HA.

## Recovery contract

- PostgreSQL restore evidence must record backup timestamp, restore point, RPO,
  RTO and verification result.
- KMS availability is a dependency for secret root, Data Protection keys and
  signing. KMS outage fails closed; it must not expose raw private material.
- EMQX session expiry, ACL state, webhook key overlap and broker reconnect
  behavior must be included in recovery tests.
- Device importer recovery is staged generation recovery, not a cross-partition
  atomic transaction. Boot must never activate mixed generations.
- Before a durable target license marker, the old valid authority remains the
  reference state. After that marker, firmware-supported exact resume or an
  explicit `recovery-required` fail-closed state is required; unconditional
  rollback is not promised.

## Rolling upgrade contract

- Server N to N+1 must accept the current persisted catalog/trust-store schema
  or perform an explicit migration before enabling commands.
- Unknown command catalog version/hash denies execution.
- Bundle schema version, license schema version, importer generation and key
  version must be checked before activation.
- Rollback must not re-enable a retired signer key, old command key version or
  stale authorization epoch.
- A rolling upgrade cannot claim HA until command ownership, duplicate publish
  control, shared Data Protection, authorization epoch propagation and migration
  compatibility are tested with two replicas.

## Verification

- Pre-cook: load profile/schema and plan consistency fixtures.
- Phase 8: measured 1,000-device run, recovery drill, backup/restore, rolling
  upgrade and two-replica soak if HA is claimed.

