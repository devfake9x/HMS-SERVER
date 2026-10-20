---
title: "HMS-SERVER API database UI contract"
type: contract-spec
date: 2026-09-25
status: design-contract
---

# API, Database, and UI Contract

## Database boundary

PostgreSQL/EF Core stores organizations, memberships, projects, packages, areas,
devices, enrollments, keysets, credentials, vendor-license lifecycle, catalog
mappings, latest status/capabilities, commands, response deduplication, audit,
and authorization policy versions. `owner_organization_id` is required; project,
package and area are nullable classifications. No telemetry or historical sensor
tables exist. Every query carries organization scope; explicit deny wins.

Concurrency uses version/fence columns for grants, revocations, key rotation,
license drafts, enrollment generations and one-time downloads. Signed license
binding fields are immutable. Firmware NVS writes are never represented as one
cross-partition database transaction.

## API boundary

OpenAPI endpoints are versioned under `/api/v1`. Ordinary JSON responses contain
redacted metadata only. Bundle/license raw bytes use one-time short-lived
`application/octet-stream` downloads with `Cache-Control: no-store`, `Pragma:
no-cache`, safe disposition and audit. Commands are submitted to the backend;
there is no browser MQTT connection or client signing. Unsupported primary/secondary
broker modes return a stable unsupported-contract error.

Every command request rechecks feature permission, resource/device scope, action
grant, license feature, current catalog hash/version and device availability.
Unknown catalog or stale authorization epoch fails closed. Idempotency keys and
wire command `id` are separate; retry of the same wire command reuses exact signed
bytes.

## UI boundary

Vue consumes redacted DTOs and authorized SignalR events. It filters by the
organization/project/package/area hierarchy and shows an explicit unclassified
state. It never treats capabilities as permission, stores raw bundle/license bytes
in Pinia/query cache/URL/logging/analytics, or signs commands. Blob URLs are revoked
on success, failure and unmount; the operator is told that a filesystem copy is
outside server control. `DeviceRevoked` removes group membership, cancels
cancellable pending commands and forces backend reauthorization.

## Evidence and limits

This is a design contract, not an implementation test. Database, API, browser,
SignalR, download and load behavior are `DEFERRED_TO_PHASE_N - REQUIRED` until
their owning implementation tests pass.
