---
title: "Late-bound MQTT adapter for mutable firmware protocol"
type: architecture-decision
date: 2026-09-27
status: accepted-for-plan
---

# Late-Bound MQTT Adapter for Mutable Firmware Protocol

## Decision

HMS-SERVER implements command intent, authorization, catalog policy,
idempotency, expiry, generation fencing, audit, and lifecycle in the
Application/Domain boundary. The firmware wire envelope and broker delivery
are replaceable Infrastructure adapters.

The core control plane is allowed to ship with a
`RecordingDeviceCommandTransport` or `DeferredDeviceCommandTransport`. The
live MQTT/EMQX adapter is deferred until a later integration phase because the
ESP32 protocol may change. Deferred transport must never report publish,
acknowledgement, or execution.

## Stable ports

- `ICommandProtocolAdapter` maps a neutral `CommandExecutionPlan` to a
  versioned firmware protocol representation.
- `IDeviceCommandTransport` delivers an already prepared outbound message or
  records it for a deferred transport.
- `IBrokerCredentialProvider` isolates broker-specific credential creation and
  rotation from the device aggregate.

The Domain project references none of these implementations and no MQTT type.
The Application project owns the port contracts and business use cases. The
Infrastructure project owns the current HMS-IDF protocol profile, future MQTT
adapter, EMQX integration, and any other transport implementation.

## Provisional firmware profile

The current profile may validate only source-proven facts already captured by
the plan fixtures: direct 32-byte HMAC, the current allowed command fields,
the 1,023-byte signed-header limit, TTL/skew/key-version rules, and proven
inbound topic facts. The canonical server publish leaf remains unresolved.
Until a later server/ACL fixture selects and proves that leaf, live publish
returns `CONTRACT_UNAVAILABLE` and makes zero broker calls. `/cmd/execute` is
not introduced.

## Consequences

- Organization, device, permission, provisioning, license, audit, and frontend
  work can proceed without a live broker.
- Firmware wire changes are localized to a protocol profile/adapter, catalog
  compatibility data, and fixtures unless the business meaning of an action
  changes.
- A deferred command is recorded as `MQTT_DEFERRED` or
  `TRANSPORT_NOT_CONFIGURED`; it is never represented as delivered.
- Live EMQX ACL, publisher identity, reconnect, QoS, retain, load, and soak
  tests remain Phase 8 obligations.

## Verification

- Application tests use a recording transport and prove zero broker calls for
  denied or unresolved commands.
- Contract tests prove the provisional envelope limits without claiming device
  execution.
- A later integration fixture must prove the canonical publish leaf and ACL
  before the MQTT adapter can report delivery.
