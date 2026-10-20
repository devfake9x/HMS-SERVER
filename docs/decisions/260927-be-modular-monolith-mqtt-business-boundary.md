---
title: "Backend modular monolith and MQTT business boundary"
type: architecture-decision
date: 2026-09-27
status: accepted-for-plan
---

# Backend Modular Monolith and MQTT Business Boundary

## Decision

HMS-SERVER uses an ASP.NET Core modular monolith with Ports-and-Adapters
(Hexagonal/Clean Architecture) boundaries and vertical slices in the
Application layer. MQTT is an Infrastructure transport adapter. MQTT command
semantics are Application/Domain business behavior.

The first release does not split the backend into microservices. The ports and
module boundaries must remain extraction-friendly if a later scale or
operational requirement justifies a separate command worker.

## Layer responsibilities

```text
Api (HTTP/SignalR adapters)
  -> Application (use cases and ports)
      -> Domain (entities, value objects, policies)
      -> Infrastructure adapters (PostgreSQL, EMQX/MQTT, KMS, SignalR)
```

- **Api** maps HTTP/SignalR requests to application requests. It does not
  render MQTT JSON, choose topics, or make authorization decisions by itself.
- **Application** owns `SendDeviceCommand`, `ProvisionDevice`, `ApplyLicense`,
  `RevokeDevice`, and command lifecycle orchestration. It resolves the
  server-owned command catalog, checks feature/resource/device-action policy,
  intersects capability availability, applies idempotency/key-generation
  policy, and requests signing/publishing through ports.
- **Domain** owns device enrollment state, permission scope, command definition
  policy, dangerous-action rules, generation fences, and invariants. It has no
  dependency on MQTT, EF Core, ASP.NET Core, or a broker client.
- **Infrastructure** implements EF Core/PostgreSQL repositories, EMQX
  authentication/ACL/webhook adapters, MQTT publishing/subscription, direct
  HMAC signing, KMS/external signer integration, Data Protection, and SignalR
  event delivery.

## Explicit type boundaries

These types remain separate even when their fields overlap:

- `DeviceCommandRequest`: business request from an authorized operator/use
  case.
- `FirmwareCommandEnvelope`: exact HMS-IDF wire JSON and signing bytes.
- `MqttPublishRequest`: broker transport request containing topic, QoS, retain,
  broker target and payload bytes.
- `BrokerSessionEnvelope`: trusted inbound broker metadata and payload.
- `DeviceCommandResponse`: application result after response validation and
  correlation.

No MQTT client type may cross into Domain. No firmware wire DTO is accepted as
the authorization model.

## Command extensibility rule

`MqttHostedService` and the MQTT adapter must not contain an action switch or
business handler table. A command is enabled only when its server catalog row
has a closed request schema, response/error schema, build-condition evidence,
side-effect class, dangerous policy, idempotency policy, and firmware catalog
compatibility. Until then its status is `DENIED_UNTIL_ACTION_CONTRACT`; the
application returns a stable denial, performs no signing, and performs no MQTT
publish.

Adding or changing a command therefore changes the command definition/schema,
catalog hash, authorization policy and fixtures. It does not require changes to
MQTT connection lifecycle, EMQX identity propagation, topic subscription, or
SignalR transport code.

## Transport-independent use-case flow

```text
authorized DeviceCommandRequest
  -> catalog/policy/capability resolution
  -> action schema and idempotency validation
  -> FirmwareCommandEnvelope rendering
  -> ICommandSigner
  -> IDeviceCommandTransport
  -> command lifecycle/audit persistence
  -> authorized SignalR event
```

The transport adapter owns firmware-proven topic/QoS/retain rules, broker
identity, reconnect and bounded queues. The use case owns whether an action is
allowed and what the action means.

## Alternatives rejected

1. **Microservices from day one:** adds distributed failure and deployment
   complexity without a demonstrated need for the 1,000-device target.
2. **MQTT-first handlers:** couples business rules to transport callbacks and
   makes command changes unsafe and difficult to test.

## Verification

- Unit tests invoke `SendDeviceCommand` with an in-memory transport port and
  prove authorization, catalog, capability, idempotency and zero-publish
  failures without EMQX.
- Infrastructure contract tests prove the MQTT adapter maps only the approved
  wire fields, topic, QoS and retain values.
- A compile-time/project-reference check keeps Domain free of MQTT/EF/ASP.NET
  dependencies.
- A command extension fixture adds a new catalog row without changing the MQTT
  hosted service and proves the new row remains denied until its action contract
  is complete.

## Consequences

- The first implementation remains one deployable backend with clear internal
  seams and one database.
- MQTT protocol changes stay localized to Infrastructure and wire-contract
  fixtures.
- Command schema/policy work is explicit and cannot be hidden in a transport
  callback.
- A future command worker can implement the same application ports without
  changing Domain or Api contracts.
