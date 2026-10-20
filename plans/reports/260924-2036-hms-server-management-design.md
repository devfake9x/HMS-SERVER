---
type: brainstorm-report
status: approved-design
date: 2026-09-24
repository: HMS-SERVER
scope: backend-frontend-architecture
decision: aspnet-core-vue-postgresql-emqx-signalr
---

# HMS-SERVER Management Platform Design

## Summary

HMS-SERVER is a multi-company management and control platform for up to 1,000 ESP32 devices. The platform provisions device identity and credentials, groups devices for search and administration, enforces three independent permission layers, sends signed MQTT commands, receives operational status/capabilities/acknowledgements, and exports the identity/license material required by the device.

The platform does not ingest or retain sensor telemetry in this scope. The existing HMS-SERVER documents describe a SCADA and telemetry ingestion system; they are superseded for this scope by this design. PostgreSQL is sufficient. TimescaleDB, Redis Streams, telemetry hypertables, replay ingestion, digital-twin screens, and historical trend screens are out of scope.

The approved technology direction is:

- Backend: ASP.NET Core 10 modular monolith, separated into Domain, Application, Infrastructure, and API projects.
- Frontend: Vue 3 + TypeScript + Vite, feature-sliced in the same style as the reference frontend.
- Database: PostgreSQL with Entity Framework Core.
- MQTT broker: EMQX with dynamic authentication and per-device ACL.
- Web realtime events: SignalR.
- License signing: an adapter boundary supporting an external vendor signer or a controlled managed signer; the HMS-SERVER web process must not contain an uncontrolled private key.

The design is approved as the implementation baseline. It intentionally includes a firmware contract change: the current HMS-IDF source does not expose a complete importer for a combined identity/key/license bundle, so that importer must be specified and implemented in HMS-IDF before the exported bundle can be consumed automatically.

## Problem-First Analysis

### Solution-jumping diagnosis

The initial idea combines company management, device inventory, MQTT provisioning, license handling, user management, and command permissions. The underlying signal is operational: many field devices must be assigned to different companies and projects while remaining safe to configure and control. A device can be physically present before it has been classified, credentials must be generated centrally, and a leaked device credential must not expose the entire fleet.

The old solution documents jumped directly to a SCADA data platform. That would spend complexity on telemetry ingestion even though the current requirement is a management and control plane.

### Underlying problem

Operators need one authoritative system that can answer all of these questions:

1. Who owns this device and how is it classified?
2. Which company, user, role, and device action is allowed to access it?
3. Which exact MQTT identity and cryptographic keys belong to it?
4. Which license is bound to its hardware and logical identity?
5. What command was sent, by whom, under which permission, and what did the device report?

### Assumption challenges

| Assumption | Risk if wrong | Validation or mitigation |
|---|---|---|
| The server does not need telemetry | Future product work may require history | Keep MQTT message routing modular; add telemetry later as a separate bounded context |
| A device may have no project/package/area | Authorization becomes ambiguous if ownership is also nullable | Require `owner_organization_id`; make classification foreign keys nullable |
| One command key is sufficient for firmware authentication | Possession of the key is full device authority | Keep human authorization in the backend; rotate and revoke keysets; audit every command |
| The server can edit a license envelope | Signed bytes become invalid after mutation | Edit an unsigned draft and sign again or send it to the external vendor signer |
| The existing firmware can import a server bundle | Credentials and keys may never reach NVS | Add a versioned importer contract to HMS-IDF and test it against the bundle |
| The old HKDF/master-secret design still applies | Commands would fail against current firmware | Use `crypto/cmd_key` directly until firmware changes the contract |

### Problem statement

Field operators and company administrators need to manage a hierarchy of organizations and projects while controlling up to 1,000 ESP32 devices. Devices may be unclassified, intermittently connected, or at different license/configuration generations. The system must isolate companies, restrict users to assigned devices and commands, provision unique credentials and keys, and leave an auditable record of every security-sensitive operation.

Success means a newly created device can be assigned an owner, optionally classified, provisioned once, licensed through the correct signer, connected to EMQX with a device-specific ACL, and controlled only by authorized users. The UI must make unassigned devices, license state, connection state, command state, and permission state visible without pretending to be a telemetry dashboard.

### Alternative framings

1. **Fleet administration**: the central problem is inventory, ownership, and lifecycle. The solution is a tenant-aware device registry with provisioning workflows.
2. **Secure command plane**: the central problem is safe control over MQTT. The solution is capabilities-driven commands, backend authorization, direct firmware-compatible signing, and audit.
3. **License and enrollment operations**: the central problem is separating vendor authority from operator enrollment. The solution is separate license and enrollment aggregates plus a versioned export/import contract.

### Evidence status

Evidence is strong for the firmware constraints and medium-to-strong for the product structure. The firmware behavior was read from source. The domain and permission structure was compared against `concrete-stress-monitoring`. HMS-SERVER itself contains documentation but no executable BE/FE implementation.

### Validation plan

- Add contract tests that generate a bundle and verify every field against the HMS-IDF importer.
- Run a real-device test for `device_id` enrollment, MQTT credential authentication, capabilities publication, command signing, response handling, and license apply.
- Test authorization with users across parent and child organizations, unassigned devices, direct device grants, inherited scopes, and explicit denies.
- Test rotation and revocation by replaying old MQTT credentials and command keys.
- Treat an inability to write `device_id`, `crypto/cmd_key`, or `vendor/license` on a clean device as a release blocker.

### Stakeholder message

We can build the management platform around the existing firmware and the proven reference application's hierarchy and permission patterns. The first release should focus on ownership, provisioning, license lifecycle, and safe command control. Telemetry ingestion would add a separate storage and scaling problem that the current requirement does not need. One firmware-side importer contract is required before identity export can become a one-click operation.

## Verified Firmware Contract

### Identity partitions

HMS-IDF separates immutable/vendor material from operator enrollment:

- `vendor`: signed license, board serial, product model, vendor binding, and license journal.
- `enroll`: operator-issued logical `device_id`, which can be revoked or replaced.

The server must represent these as separate aggregates. A license record must not be deleted or overwritten as a side effect of enrollment revocation.

### MQTT topics

The current device builds these topics from its configured prefix and logical device ID:

```text
hms/<device_id>/cmd/#
hms/<device_id>/cmd/res
hms/<device_id>/status
hms/<device_id>/status/connectivity
hms/<device_id>/audit
hms/<device_id>/capabilities
```

Device ACL:

- Subscribe only to `hms/<device_id>/cmd/#`.
- Publish only to its own `/cmd/res`, `/status`, `/status/connectivity`, `/audit`, and `/capabilities` topics.
- Deny every other device namespace.

The server publishes command envelopes on a stable subtopic such as `hms/<device_id>/cmd/execute`. The firmware already subscribes to the wildcard command namespace. The exact subtopic is a shared contract and must be fixed before implementation.

### Command security

The firmware currently states that `crypto/cmd_key` is the direct 32-byte HMAC key. There is no HKDF step in the current command-security implementation. The backend must therefore:

1. Load the active command key for the device.
2. Build canonical JSON with `data.action` and `data.data`.
3. Include the firmware-compatible fields (`type`, `id`, `ts`, `data`, `kid`, `generation`, and optional TTL/confirm/response fields).
4. Sign the exact raw JSON bytes with HMAC-SHA256 using `crypto/cmd_key`.
5. Reject the envelope when its final byte length is greater than 1,023.
6. Publish only after all backend authorization checks succeed.

The device authenticates possession of the key; it does not know which web account issued the command. Human authorization remains a server concern.

### Capabilities

The retained capabilities document is the source for the device's available action catalog. It contains action metadata such as action type, action name, required permission, dangerous class, confirmation requirement, TTL, dry-run support, and availability.

The backend stores the latest manifest, its hash, version, and received timestamp. The UI builds forms from the backend's validated catalog. A client cannot cause the backend to sign an arbitrary action just by posting an action string.

### License request and apply

The device emits an unsigned protobuf `LicenseRequest` containing values such as MAC, logical device ID, product model, board serial, firmware version, current license ID, issue counter, nonce, timestamp, and customer note. The device accepts a raw protobuf `LicenseEnvelope` through the license apply route.

A signed envelope cannot be edited in place. The backend may decode it for display, build a draft, and preserve the original bytes, but any changed payload must be signed again with a key corresponding to the public key embedded in the firmware. The license signer abstraction supports either:

- `ExternalVendorIssuer`: produce/export a request or draft, then import the vendor-signed envelope.
- `ManagedSignerIssuer`: sign through a protected KMS/HSM or tightly controlled service when the deployment is explicitly authorized to hold the signing key.

## Domain Model

### Organization hierarchy

```text
organizations
  id UUID primary key
  parent_id UUID nullable self-reference
  kind MASTER | COMPANY
  code unique within parent
  name
  status ACTIVE | SUSPENDED

projects
  id UUID primary key
  organization_id UUID not null
  code
  name
  status

packages
  id UUID primary key
  project_id UUID not null
  code
  name
  status

areas
  id UUID primary key
  organization_id UUID not null
  project_id UUID nullable
  code
  name
  status
```

The master organization is the single root. Child organizations can be nested. A parent user's visibility of descendants is a policy decision; the recommended default is inherited visibility when a parent scope is granted, with explicit deny taking precedence.

### Device registry

```text
devices
  id UUID primary key
  owner_organization_id UUID not null
  device_id VARCHAR unique not null
  device_code VARCHAR unique not null
  name VARCHAR not null
  mac_address VARCHAR unique not null
  board_serial VARCHAR nullable
  product_model VARCHAR nullable
  hardware_version VARCHAR nullable
  firmware_version VARCHAR nullable
  project_id UUID nullable
  package_id UUID nullable
  area_id UUID nullable
  lifecycle_status DRAFT | PROVISIONED | ACTIVE | SUSPENDED | REVOKED
  connection_status UNKNOWN | ONLINE | OFFLINE | DEGRADED
  last_seen_at TIMESTAMPTZ nullable
  last_capabilities_at TIMESTAMPTZ nullable
  created_at TIMESTAMPTZ
  updated_at TIMESTAMPTZ
```

`owner_organization_id` is required for tenant isolation. Project, package, and area assignments are nullable classification links. A device can be displayed as unassigned without becoming ownerless.

### Device runtime state

```text
device_status_snapshots
  device_id primary key
  connectivity_payload JSONB
  status_payload JSONB
  received_at TIMESTAMPTZ
  source_message_id nullable

device_capabilities
  device_id primary key
  manifest JSONB
  manifest_hash
  schema_version
  received_at TIMESTAMPTZ
```

These are latest-state operational records, not telemetry history. If future requirements need history, add a separate bounded context and retention policy.

### Credentials and keysets

```text
device_mqtt_credentials
  id UUID primary key
  device_id unique
  username unique
  password_hash
  encrypted_password
  active
  created_at
  revoked_at nullable

device_keysets
  id UUID primary key
  device_id
  generation
  key_id
  encrypted_cmd_key
  encrypted_tlm_sign_key nullable
  encrypted_tlm_data_key nullable
  status ACTIVE | REVOKED
  created_at
  revoked_at nullable
```

Secrets are encrypted with an application envelope key from a secret store or KMS. Raw credentials are returned only through the one-time provisioning flow. The database never logs raw passwords or key bytes.

### License records

```text
vendor_licenses
  id UUID primary key
  device_id
  license_id
  signer_kid
  envelope_bytes encrypted/blob
  envelope_hash
  payload_metadata JSONB
  mode OFF | TRIAL | LICENSED | GRACE | EXPIRED | PROVISIONAL
  reason
  bound_mac
  bound_device_id nullable
  issued_at nullable
  not_before nullable
  not_after nullable
  imported_at
  revoked_at nullable

license_drafts
  id UUID primary key
  device_id
  source_request_bytes encrypted/blob nullable
  proposed_payload JSONB
  state DRAFT | SENT_TO_VENDOR | SIGNED | REJECTED
  created_by
  created_at

provisioning_bundles
  id UUID primary key
  device_id
  version
  manifest_hash
  encrypted_bundle_reference
  issued_by
  expires_at
  downloaded_at nullable
  revoked_at nullable
```

## Permission Model

### Feature permissions

```text
roles
permissions
role_permissions
user_roles
```

Feature permission keys are stable backend policy identifiers. The frontend receives a permission snapshot for navigation and guard display, but every API endpoint checks permissions again.

### Resource scopes

```text
resource_scopes
  id UUID primary key
  principal_type USER | ROLE
  principal_id UUID
  node_type ORGANIZATION | PROJECT | PACKAGE | AREA | DEVICE
  node_id UUID
  access_mask BIGINT
  effect ALLOW | DENY
  created_at
  created_by
```

Resource scope evaluation uses the organization/project/package/area/device graph. Direct device scope is more specific than an ancestor scope. An explicit deny wins at the same or broader scope.

### Device action grants

```text
device_action_grants
  id UUID primary key
  principal_type USER | ROLE
  principal_id UUID
  device_id UUID nullable
  device_group_id UUID nullable
  action_code VARCHAR
  effect ALLOW | DENY
  expires_at nullable
  created_at
  created_by
```

The first release can use direct `device_id` grants. A device-group abstraction may be added when repeated one-by-one assignments become operationally expensive. Action codes should match the normalized capabilities namespace, for example `system.reboot` or `modbus.set_rtu_config`.

### Authorization decision

The command endpoint evaluates:

```text
feature permission
AND resource scope for target device
AND action grant for target device/action
AND action exists and is available in stored capabilities
AND license/operational policy allows the action
```

The decision, matched grants, capability version, and resulting outcome are recorded in the command audit record.

## MQTT and Command APIs

### REST endpoints

```text
GET/POST/PATCH /api/v1/organizations
GET/POST/PATCH /api/v1/projects
GET/POST/PATCH /api/v1/packages
GET/POST/PATCH /api/v1/areas

GET/POST        /api/v1/devices
GET/PATCH       /api/v1/devices/{id}
PATCH           /api/v1/devices/{id}/classification
GET             /api/v1/devices/{id}/status
GET             /api/v1/devices/{id}/capabilities

POST            /api/v1/devices/{id}/provisioning
POST            /api/v1/devices/{id}/provisioning/rotate
POST            /api/v1/devices/{id}/provisioning/bundle
GET             /api/v1/provisioning/bundles/{one_time_token}

POST            /api/v1/devices/{id}/license/request
POST            /api/v1/devices/{id}/license/draft
POST            /api/v1/devices/{id}/license/sign
POST            /api/v1/devices/{id}/license/apply
POST            /api/v1/devices/{id}/license/remove

POST            /api/v1/devices/{id}/commands
GET             /api/v1/devices/{id}/commands

GET/PATCH       /api/v1/users/{id}/resource-scopes
GET/PATCH       /api/v1/users/{id}/device-actions
POST            /api/v1/internal/emqx/auth
GET             /api/v1/audit
```

Command responses should expose `PENDING`, `PUBLISHED`, `ACKNOWLEDGED`, `REJECTED`, `TIMEOUT`, or `DEVICE_ERROR`. The API must return a correlation ID and never expose the HMAC key or a raw stored secret.

### EMQX authentication webhook

The internal webhook receives the device username and password, verifies the credential hash, checks active enrollment, and returns a device-specific ACL. It is protected by TLS, an internal secret/header, network restrictions, and rate limiting.

The browser never receives broker credentials for general use and never connects directly to EMQX.

### Server-to-device command contract

The backend owns canonical serialization. The envelope contains at least:

```json
{
  "type": "system",
  "id": "command-uuid",
  "ts": 0,
  "ttl_ms": 10000,
  "data": {
    "action": "get_info",
    "data": {}
  },
  "kid": "key-id",
  "generation": 1,
  "sig": "hex-hmac-sha256"
}
```

The exact field ordering and signature assembly must be covered by shared contract tests against the firmware implementation. No command is published if its canonical raw bytes exceed 1,023 bytes.

## Frontend Design

The frontend follows the reference project's Vue 3 feature structure:

```text
features/
  organizations/
  projects/
  packages/
  areas/
  devices/
  provisioning/
  licenses/
  users/
  roles/
  device-permissions/
  commands/
  audit/
```

### Main screens

1. **Organization tree**: master/child company navigation, project/package/area CRUD, inherited scope visibility.
2. **Device inventory**: filter by organization, project, package, area, connection, lifecycle, license, sync, and unassigned state.
3. **Device detail**: identity, hardware, MQTT connection, capabilities, latest status, license lifecycle, key generation, and configuration sync.
4. **Provisioning wizard**: hardware identity, classification, credential/key generation, license request upload, signer workflow, bundle preview, and one-time download.
5. **License center**: request metadata, editable draft, signed envelope hash, signer key ID, binding, lifecycle, apply/remove operations.
6. **User and role center**: feature permissions, organization/resource scopes, and device action grants.
7. **Command console**: capability-driven forms, dangerous-command confirmation, pending/ack/timeout state, and audit link.
8. **Audit log**: filter by user, device, action, license, credential rotation, bundle download, and result.

There is no telemetry chart or SCADA digital twin in this release. Status and command events are shown as operational state, not as historical sensor data.

### Frontend safety rules

- Navigation and buttons are hidden or disabled when the permission snapshot denies access, but this is only a UX layer.
- API errors such as `403`, stale capabilities, revoked enrollment, expired license, and command timeout are shown as explicit states.
- Provisioning secrets are displayed once, with a deliberate download action and warning that a new bundle must be generated after loss.
- Dangerous commands require the server-side policy plus UI confirmation text derived from the device capability manifest.

## Security and Operations

- Hash user passwords with a modern adaptive password hash; never reuse device MQTT password hashes for human accounts.
- Encrypt device passwords, key material, and license blobs at rest using envelope encryption.
- Keep the EMQX webhook private to the broker network and require a shared internal secret.
- Rotate MQTT credentials and keysets by generation; revoke old generations before or immediately after successful re-enrollment.
- Make bundle downloads one-time and expiring; record IP, user, device, bundle hash, and timestamp in audit.
- Use idempotency keys for device creation, key rotation, license apply, and command submission.
- Do not place license private keys in the repository, frontend, ordinary environment files, or application logs.
- Treat a device command key as full device authority. Human-level restrictions are enforced only before signing and publishing.
- Store only latest status/capabilities in the first release. Do not quietly create an unbounded event or telemetry table.

## Out of Scope

- Sensor telemetry ingestion or historical storage.
- TimescaleDB, Redis Streams, Kafka, or a telemetry replay pipeline.
- Alarm history, trends, digital twin, SCADA map, or real-time tag grid.
- Firmware OTA artifact distribution, unless later added as a capability-controlled action.
- Automatic device-side import until the HMS-IDF importer contract is implemented.
- Multi-region active-active deployment.

## Implementation Touchpoints

### HMS-SERVER

The repository currently has no source code. The old documents that need replacement or explicit supersession are:

- `ALIGNMENT.md`
- `research-report.md`
- `brainstorm-report.md`
- `docs/architecture-hms-server.md`
- `docs/journal.md`

The future backend should be split into the standard ASP.NET projects:

```text
src/
  Hms.Server.Api
  Hms.Server.Application
  Hms.Server.Domain
  Hms.Server.Infrastructure
```

The future frontend should be a separate Vue application with generated OpenAPI contracts and feature modules.

### HMS-IDF

The firmware work required before automatic import is available:

1. Define a versioned bundle/import manifest.
2. Add a controlled importer for `enroll/device_info/device_id`.
3. Add a controlled importer for MQTT broker credentials and topic prefix.
4. Add key import/rotation for `crypto/cmd_key`, `crypto/tlm_sign`, and `crypto/tlm_data`.
5. Route `license-envelope.pb` to the existing license apply logic.
6. Return an import result and current identity/capability snapshot to the server.
7. Add negative tests for MAC mismatch, device ID mismatch, stale key generation, malformed bundle, and invalid license signature.

## Acceptance Criteria

The design is considered implemented only when all of these behaviors are demonstrated:

1. Device creation atomically creates a device identity, MQTT credential, keyset, and auditable provisioning record.
2. A device may have null project/package/area classifications while retaining a required owner organization.
3. Organization, project, package, area, and device filters return correct results, including an unassigned view.
4. A user outside a resource scope receives `403` even if the UI is bypassed.
5. A user with device scope but without the action grant cannot send that action.
6. An action absent from capabilities is rejected before MQTT publish.
7. The command is signed with the active direct `crypto/cmd_key`, remains at most 1,023 bytes, and produces an audit record.
8. Status, capabilities, command responses, and connectivity state update the device detail screen.
9. MQTT credentials permit a device to access only its own topic namespace.
10. License request bytes and signed envelope bytes are preserved; changed drafts require a new signature.
11. Bundle download is one-time and auditable; rotation invalidates the previous credential/key generation.
12. No telemetry data is persisted by the first-release backend.

## Risks and Unresolved Questions

1. **Firmware importer**: the current source does not expose a complete combined bundle import path. This is a dependency, not something the server can solve alone.
2. **Signer ownership**: the deployment must choose whether the server sends requests to a vendor or invokes a protected managed signer. The application contract supports both.
3. **License binding order**: if a license binds both MAC and device ID, the server must generate the logical device ID before creating the final license request.
4. **MQTT command subtopic**: `/cmd/execute` is proposed; the exact string must be frozen in the shared contract.
5. **Parent visibility policy**: the recommended default is descendant visibility from an organization scope, with explicit deny. This must be covered by authorization tests.
6. **Device group grants**: direct device grants are enough for the first release. Groups should be added only when manual assignment becomes a measurable operational burden.

## Next Steps

1. Create the implementation plan from this approved design.
2. Replace the old telemetry-centric architecture document with this management/control-plane scope.
3. Define OpenAPI, protobuf/license parsing, MQTT topic, capabilities, command, and provisioning contracts.
4. Scaffold the ASP.NET Core solution and Vue application separately.
5. Design and implement the HMS-IDF importer contract before claiming one-click identity export.
6. Build authorization and provisioning tests before wiring the full UI.

## References

- `D:/WORKSPACE/PROJECT/HMS-IDF/components/command-dispatcher/include/cmd_security.h`
- `D:/WORKSPACE/PROJECT/HMS-IDF/services/mqtt-service/mqtt_service.c`
- `D:/WORKSPACE/PROJECT/HMS-IDF/components/util-device-identity/src/device_identity.c`
- `D:/WORKSPACE/PROJECT/HMS-IDF/components/hms-license/include/hms_license.h`
- `D:/WORKSPACE/PROJECT/HMS-IDF/components/hms-http-api/license/license_api.c`
- `D:/WORKSPACE/PROJECT/HMS-IDF/tools/license-tool/license_manager.py`
- `D:/WORKSPACE/PROJECT/concrete-stress-monitoring/sources/backend/src/ConcreteStress.Domain/Entities/Company.cs`
- `D:/WORKSPACE/PROJECT/concrete-stress-monitoring/sources/backend/src/ConcreteStress.Domain/Entities/Gateway.cs`
- `D:/WORKSPACE/PROJECT/concrete-stress-monitoring/sources/backend/src/ConcreteStress.Domain/Entities/Grant.cs`
- `D:/WORKSPACE/PROJECT/concrete-stress-monitoring/sources/backend/src/ConcreteStress.Domain/Entities/UserGatewayScope.cs`
- `D:/WORKSPACE/PROJECT/concrete-stress-monitoring/sources/frontend/package.json`
