---
title: "HMS-SERVER provisioning bundle field matrix"
type: provisioning-contract
date: 2026-09-25
status: source-proven-fields-plus-new-contract-fields
---

# Provisioning Bundle Field Matrix

The table separates fields proven by HMS-IDF from fields the server wants to
issue. A desired field is not accepted as firmware behavior until its importer
setter, storage location, length and activation path are source-proven.

| Field | Required | Current firmware evidence | Proposed source/generator | Limit/encoding | Status |
|---|---|---|---|---|---|
| `schema_version` | yes | No combined importer schema | Server contract | ASCII version | `NEW_FIRMWARE_CONTRACT` |
| `device_id` | yes | `device_identity_get_id` reads `enroll/device_info`; no combined setter | Server device creation | bounded UTF-8; exact firmware max pending | `SOURCE_PROVEN_GETTER_ONLY` |
| `generation` | yes | License journal has generations; combined enrollment generation not proven | Server monotonic fence | unsigned integer | `NEW_FIRMWARE_CONTRACT` |
| `import_nonce` | yes | License request/replay nonce exists; importer nonce not proven | CSPRNG server value | opaque bounded bytes | `NEW_FIRMWARE_CONTRACT` |
| `expires_at` | yes | License clock/lifecycle validates times | Server UTC timestamp | ISO-8601 | `SOURCE_PROVEN_LICENSE_TIME_ONLY` |
| `bootstrap_proof` | yes | Human bearer/session auth exists; device-bound proof absent | Device-bound one-time challenge | opaque bounded bytes | `NEW_FIRMWARE_CONTRACT` |
| `primary_broker` | yes | MQTT config supports primary; release 1 is primary-only | Server config | URI/TLS profile | `SOURCE_PROVEN_PRIMARY; IMPORTER_NEW` |
| `secondary_broker` | forbidden in v1 | Firmware has dual-broker code, but server release 1 does not provision it | N/A | reject PRIMARY/SECONDARY/BOTH/FAILOVER unsupported modes explicitly | `APPROVED_RELEASE_RULE` |
| `mqtt_username` | yes | Runtime MQTT config/auth path exists; combined importer setter not proven | Server-generated unique identity | exact firmware max pending | `NEW_FIRMWARE_CONTRACT` |
| `mqtt_password` | yes | Runtime credential storage path requires source verification | CSPRNG secret, hash/opaque bundle only | never human admin password | `NEW_FIRMWARE_CONTRACT` |
| `command_key` | yes for signed commands | `crypto/cmd_key`, exactly 32 bytes, direct HMAC | CSPRNG 32 bytes, KMS envelope at rest | 32 raw bytes | `SOURCE_PROVEN_CRYPTO_RULE; IMPORTER_NEW` |
| `command_key_version` | yes | Version increments on set/clear and fences stale frames | Firmware/import transaction | unsigned integer | `SOURCE_PROVEN_KEY_FENCE` |
| `telemetry_key` | not used by HMS-SERVER control plane | Firmware has telemetry key concepts; server does not ingest telemetry | Do not issue unless importer source proves need | n/a | `OUT_OF_SCOPE_UNLESS_PROVEN` |
| `tls_ca/cert/key` | conditional | MQTT manager uses TLS material; exact import storage/setter not proven | Server enrollment adapter | opaque bytes; no ordinary JSON response | `NEW_FIRMWARE_CONTRACT` |
| `license_envelope` | conditional | `license_apply` validates signed opaque bytes and binding | External signer/KMS adapter | firmware-specific limits; preserve bytes | `SOURCE_PROVEN_LICENSE_API` |
| `license_kid` | inside signed license, not command JSON | License trust/evaluator uses vendor key identity | External signer/trust store | immutable after signing | `SOURCE_PROVEN_LICENSE_MODEL` |
| `license_nonce/counter` | inside license/replay state | Durable and volatile replay paths exist | Firmware journal, never hand-edited by server | firmware-defined | `SOURCE_PROVEN_REPLAY_MODEL` |
| `active_marker` | yes for staged recovery | Combined importer marker not proven | Firmware staged-generation journal | opaque durable marker | `NEW_FIRMWARE_CONTRACT` |
| `admin_password` | forbidden | Human HTTP auth is separate | Never include | n/a | `FORBIDDEN` |

## Bundle rules

- Raw private keys, human admin passwords and unredacted bundle bytes do not enter
  ordinary API JSON, frontend state, logs, analytics or backups.
- Invalid, expired, replayed, lower-generation or remote-import requests write no
  active state. Same committed bundle hash is idempotent.
- Candidate identity validation must be explicit because a nonempty signed
  `device_id` is rejected when active NVS identity is empty/mismatched.
- This matrix does not prove a combined importer exists. Phase 5 must provide the
  setter/storage/marker implementation and actual power-loss tests.
