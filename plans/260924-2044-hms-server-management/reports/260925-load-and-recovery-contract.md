---
title: "HMS-SERVER load operations and recovery contract"
type: operations-contract
date: 2026-09-25
status: design-target
---

# Load, Operations, RTO, and RPO

The reproducible profile uses one application replica, PostgreSQL, and a pinned
EMQX image for CI, exactly 1,000 MQTT clients, one client per device, reconnect
waves of 100 clients/second with jitter, and 0.1 commands/device/second. It records
webhook QPS/cache hit ratio, broker inflight and keepalive/session expiry, DB pool
and latest-state latency, worker/channel depth, SignalR queue/drop metrics, CPU,
RSS, error rate, broker loss and reconnect authorization latency. Firmware-derived
QoS/retain behavior comes from `mqtt-topic-fixture.mjs`; no retained count is
assumed.

Design targets: initial auth success >=99.9% within 120s; status/capability p95 <=2s
and p99 <=5s; command ACK p95 <=3s and p99 <=5s; error/retry <1%; broker loss
<0.1%; CPU <70%; RSS <1GiB; DB pool <80%; bounded queues; state republished
within 60s after reconnect. These are targets, not measured results.

Production HA is not claimed until a two-replica soak proves shared PostgreSQL
authority, duplicate publish control, shared Data Protection, SignalR epoch/revoke
propagation, worker failover, EMQX session behavior, TLS rotation, backup/restore,
and rolling N-to-N+1 compatibility. The release must document RTO/RPO and KMS,
trust-store and encrypted-material recovery. Multi-region active-active is out of
scope.

Status: `DEFERRED_TO_PHASE_8 - REQUIRED`; this document cannot prove capacity or
recovery without the implementation and pinned runtime.
