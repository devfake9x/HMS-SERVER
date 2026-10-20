---
title: "SignalR authorization epochs and bounded events"
type: adr
date: 2026-09-25
status: accepted-for-plan
---

# ADR: SignalR Authorization Epochs and Bounded Events

## Context

Device revocation can race with reconnects, queued events and commands already
published to a device. An unbounded per-connection queue can also turn 1,000-device
status fan-in into a memory failure.

## Decision

Every operational event carries `authorizationEpoch` and `policyVersion`. The
server revalidates on subscribe, reconnect, revoke and delivery. `DeviceRevoked`
removes membership and cancels only cancellable pending commands; it never claims
rollback of a side effect already executed on the device. Latest status and
capabilities coalesce per device. Per-connection and global queues are bounded;
status/capability events may drop with metrics, while revoke and terminal command
outcomes are retained or resynchronized from durable state. The client discards
stale epochs, but the backend remains authoritative.

## Verification

Phase 3/7 tests cover epoch ordering, revoke races, slow clients and drop metrics;
Phase 8 covers reconnect bursts and two-replica propagation. No frontend or
SignalR implementation exists in the pre-cook workspace.
