---
type: journal
date: 2026-09-24
topic: hms-server-management-design
status: approved
---

# HMS-SERVER Management Design Journal

## Context

HMS-SERVER contained design documents but no executable backend or frontend. The requested product is a management and control plane for up to 1,000 ESP32 devices, with organization/project/package/area classification, user management, device provisioning, license handling, and command-level authorization.

The reference project supplied a useful hierarchy and authorization model. HMS-IDF supplied the authoritative device protocol and license behavior.

## What happened

- Scanned HMS-SERVER, the reference backend/frontend, and HMS-IDF before proposing architecture.
- Confirmed the reference frontend uses Vue 3/TypeScript/Vite and feature modules for organizations, projects, packages, areas, gateways, users, roles, and audit.
- Confirmed the reference backend separates feature permissions, resource grants, and device scopes.
- Confirmed HMS-IDF separates vendor license material in NVS `vendor` from operator enrollment in NVS `enroll`.
- Confirmed the current command key is `crypto/cmd_key` used directly for HMAC; the old HMS-SERVER HKDF/master-secret design is not the current firmware contract.
- Confirmed command payloads are limited to 1,023 bytes and require nested `data.action`/`data.data`.
- Confirmed the firmware exposes license request/apply routes but no complete combined enrollment-bundle importer.

## Decisions

1. Use ASP.NET Core 10, Vue 3, PostgreSQL, EF Core, EMQX, and SignalR.
2. Keep backend and frontend as separate deployable applications.
3. Model organizations as a multi-level tree rooted at one master company.
4. Require an owner organization on every device while allowing project/package/area classification links to be null.
5. Enforce three permission layers: feature permission, resource scope, and per-device action grant.
6. Store only latest operational status and capabilities; do not implement telemetry ingestion or historical sensor storage.
7. Separate `VendorLicense` from `DeviceEnrollment`, credentials, and keysets.
8. Support external vendor signing and managed signing through a license signer adapter.
9. Export a versioned identity/license bundle, but treat the HMS-IDF importer as a prerequisite for automatic device import.

## Impact

The old telemetry-centric architecture needs to be superseded for this scope. TimescaleDB, Redis Streams, telemetry replay, and SCADA UI are removed from the first release. The first implementation risk moves to the provisioning contract: the server can generate and export credentials and keys, but firmware must provide a safe, tested path to write them into the correct NVS partitions.

## Next

- Convert the approved report into an implementation plan.
- Freeze OpenAPI, MQTT, capabilities, command, license, and bundle contracts.
- Define the HMS-IDF importer before claiming end-to-end one-click provisioning.
- Implement authorization and provisioning tests before broad UI work.
