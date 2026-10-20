---
title: "EMQX trusted identity and command boundary"
type: adr
date: 2026-09-25
status: accepted-for-plan
---

# ADR: EMQX Trusted Identity and Command Boundary

## Context

HMS-IDF proves that the device subscribes to `<prefix>/<device_id>/cmd/#` and
validates a signed command envelope, but it does not provide the backend with an
authenticated broker session object. A raw topic or payload can be forged or
misrouted and cannot be the identity authority.

## Decision

EMQX ACL isolates each device namespace. A trusted internal bridge/rule output
must carry authenticated `client_id`, username, original topic, payload,
`received_at`, message identity and an overlap-capable key identifier. The
backend rejects missing, expired, replayed, forged or inconsistent metadata. The
server command publisher remains blocked until a firmware fixture pins the exact
publish leaf under `/cmd/#`; `/cmd/execute` is not current firmware evidence.

## Consequences

This adds a broker-to-application trust seam and negative tests, but prevents
cross-device state acceptance and avoids inventing a topic. Unsupported command
publishing returns a stable contract-unavailable result rather than guessing.

## Verification

`fixtures/broker-session-envelope.test.mjs`, EMQX ACL/webhook integration in
Phase 3, and the Phase 8 negative identity/load gate. The fixture is
`CONTRACT_TESTED`; it does not prove a live EMQX bridge.
