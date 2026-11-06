# Red team review: monitoring read actions (security adversary)

Plan: `plans/261106-1040-monitoring-read-actions/` (plan.md, phase-01..04). Spec read via
`git -C D:/WORKSPACE/PROJECT/HMS-IDF show 18a63e8ed:engineering-docs/api/mqtt-api.md` (engineering-docs only).
The catalog diff was computed by importing the generator's exported `parseSpecManifest` from a scratch copy;
the repo was not modified.

## Finding 1: The pinned spec fails the generator's own integrity check, and it describes uncommitted firmware
- **Severity:** Critical
- **Location:** Phase 1, "Requirements" (Nguồn spec) and "Implementation Steps" step 2; Phase 2, "Tập lệnh mở" (comment text)
- **Flaw:** Step 2 says to change `SOURCE_COMMIT`, run the generator, then get `--check` exit 0. At `18a63e8ed` the generator throws before it writes anything. §11 prose still says "98 cặp, 12 namespace", but the table has 104 rows in 13 namespaces. The plan never mentions this. The spec also says the new rows are not released capabilities. It says `tags.list/get` are "chưa là capability đã phát hành" and the four Modbus paged reads are "new in the working tree; not in HEAD `dc3ecc198`". The firmware is "`NOT_COMMITTED / NOT_PUSHED`" and the product is "`release-qualified = 0/14`". Because of spec R11, the catalog is the only permission boundary. Yet four of the five newly opened rows take their permission column from firmware code anchors marked "verified: 2026-11-05@working-tree".
- **Failure scenario:** The implementer hits `spec §11 states 98 pairs but its action table has 104 rows`. The easy way out is to comment out or loosen `assertStatedCounts`, which is the guard that catches a truncated or half-edited §11 table. The tests that pin that guard (`generate-command-catalog.test.mjs:282-286`) then get "fixed" too. Every future re-pin loses that protection. Separately, once that firmware is committed it may rename `mb_rtu.tag_view` or change a reply shape. The server would not notice until someone re-pins by hand. Meanwhile it keeps signing for the permission mapping from the working tree.
- **Evidence:**
  - `scripts/generate-command-catalog.mjs:373-385`: `throw new Error(\`spec §11 states ${pairs[1]} pairs but its action table has ${commands.length} rows\`)`. Reproduced: "spec §11 states 98 pairs but its action table has 104 rows".
  - Spec @18a63e8ed line 745-747: "Snapshot nền tại `HEAD` có 98 cặp, 12 namespace; working-tree G01 overlay bổ sung `tags.list`/`tags.get` thành 100 cặp, 13 namespace nhưng chưa là capability đã phát hành".
  - Spec line 1060: "they are not in HEAD `dc3ecc198`". Lines 1097-1098: "`release-qualified = 0/14`; Git remains `NOT_COMMITTED / NOT_PUSHED`". Line 39: "tài liệu không chứng minh firmware này đã được build, flash hay chạy trên thiết bị triển khai".
  - Phase 2's comment claim "Spec chỉ đóng 9 lệnh `SLICE_PASS`" is wrong too. Spec line 1085 says the four Modbus reads "are `SLICE_PASS` at action level", but on a working-tree image whose provenance audit is `EVIDENCE_AUDIT_FAIL` (line 1091-1096).
- **Suggested fix:** Add a Phase 1 precondition that §11 prose and table agree at the pinned commit. Either ask the HMS-IDF doc owner for a commit whose prose states 104/13, or add an explicit, reviewed override in the generator: a constant for the expected count with the reason and the commit it applies to, plus a test. Never delete the check. Record in `catalog-diff.md` that the rows come from working-tree firmware. Get an owner decision to open them on that basis, as was done for `set_output`. Fix the `EnabledActions` comment wording.

## Finding 2: Read traffic from any viewer can block or drop an operator's relay command
- **Severity:** High
- **Location:** plan.md "Rủi ro" (Lane lệnh); Phase 3, "Chung" (Gửi lần lượt, "không cần khoá") and "Modbus: đọc theo trang"
- **Flaw:** The only lane protection in the plan is client-side: each card sends its commands one at a time. The device has one 16-slot command queue. Its rate bucket (burst 20, refill 10/s) is shared by all clients, not given per client. Near full it answers `LANE_FULL`; when full it drops commands with no reply. The server has no rate limiter, no per-device in-flight cap, and no priority for physical commands. The plan adds a lot more read traffic: a first Modbus read is up to 17 commands, there are four cards per viewer, and many existing roles now unlock reads (Finding 3).
- **Failure scenario:** Three viewers press "Đọc" on the Modbus card, or one user scripts `POST /commands` with `modbus.get_rtu_live`. An operator then sends `control.set_output` to switch off a relay. The device answers `LANE_FULL` or drops it. With no reply, the server waits 30 s, records `DEVICE_RESPONSE_TIMEOUT`, and marks outputs stale with an unknown outcome. The relay stays on. Every read also adds a row to the persisted audit table, with no retention limit in this plan.
- **Evidence:**
  - Spec line 120: "Queue command có 16 slot; gần đầy có thể trả `LANE_FULL`, đầy hẳn ... có thể bị drop không response. Rate limit dùng bucket chung burst 20/refill 10 mỗi giây, không phải quota riêng từng client."
  - Grep for `RateLimit|AddRateLimiter|Throttle|concurren` in `src/Hms.Server.Api/Program.cs`, `Infrastructure/DependencyInjection.cs` and `Infrastructure/Mqtt/*`: no match.
  - `InMemoryPendingDeviceCommandRegistry` (`DeviceLinkContracts.cs:268-291`) has no per-device cap.
  - `DeviceLinkHandlers.cs:117-125`: on timeout, physical commands are marked `OutputCommandOutcomeUnknown`.
  - `EfCommandAuditStore.cs:85-100`: one audit row per command.
- **Suggested fix:** Before Phase 3, add a server-side limit per device (and ideally per user) on in-flight reads, for example at most 1-2 pending reads per device. Refuse or queue reads while a physical command is pending on that device, so relay commands always get a slot. Add an integration test: with a read pending, `control.set_output` is not refused because of the server's own reads.

## Finding 3: Existing device roles silently gain 17 reads; the plan only considers the new permission codes
- **Severity:** High
- **Location:** plan.md "Rủi ro" (Quyền mới); Phase 2, "Tập lệnh mở"
- **Flaw:** The risk section only says old roles lack `tag.view`/`mb_rtu.*`. It misses the reverse. Of the 21 newly opened actions, 17 use permission codes that roles can already hold, including `modbus.get_rtu_status`, which uses the existing `mb_rtu.config_view`. Org admins could delegate these codes freely, because they are not high-risk, while they unlocked no device action at all (`eth.view`, `modem.view`, `sys.time_view`, `sys.hostname_view`, `sys.battery_view`, `adc.view`, `wifi.sta_view`, `wifi.ap_view`, `mb_rtu.config_view`). The opening also widens what `sys.info_view` grants: it now includes `system.get_device_id` (MAC) and `ota.get_status`. Nothing in the plan lists affected roles, tells the owners, or migrates anything.
- **Failure scenario:** An org admin once gave a contractor role `sys.info_view` + `modem.view` + `wifi.sta_view` "for later". Those codes currently unlock only basic system info. After deploy, that contractor can read the device MAC, OTA state, the modem projection, the connected SSID/IP, and the Ethernet gateway/netmask, with no role change and no audit trail of a grant. Nothing tells the people who approved the role what it now grants.
- **Evidence:**
  - Catalog rows sharing permissions (diff output): `system.get_device_id`/`ota.get_status` → `sys.info_view`; `network.get_modem` → `modem.view`; `network.wifi_ap_status` → `wifi.ap_view` (shared with closed `wifi_ap_clients`, which returns MACs, spec line 336/842); `modbus.get_rtu_status` → `mb_rtu.config_view`.
  - `DeviceAccessHandlers.cs:315-326` (`DelegationRules.CheckRoleChange`): a non-master may delegate any non-high-risk code they hold.
  - `DeviceAccessHandlers.cs:357-363` (`CheckVocabulary`): any code in the catalog vocabulary is accepted, whether or not an action is open.
  - plan.md:83 mentions only the new codes.
- **Suggested fix:** Add a Phase 2 step: query `DeviceRolePermissions` for the 11 reused codes, list the affected roles and organizations in the phase report, and get owner sign-off before deploy. Also add a doc note that opening an action widens every role that holds its permission.

## Finding 4: Reads with an undefined reply shape are shown as raw dumps, and real replies are committed as fixtures
- **Severity:** High
- **Location:** Phase 3, "Từng thẻ" (`ota.get_status`, `network.get_modem` rows) and "Hiển thị"; Phase 4, "Ẩn danh dữ liệu" and "Related Code Files" (fixtures)
- **Flaw:** `ota.get_status` and `network.get_modem` are opened even though the spec does not define their fields. The cards would render "toàn bộ dạng danh sách khoá–giá trị". For the modem, the spec says only that the projection is not the full "IMEI/ICCID/operator/phone" DTO, so some identity fields may be present. For OTA, the closely related `ota.remote_config_get` is high-risk (`sys.ota`) because it may hold a URL/token. Nobody has checked whether `ota.get_status` echoes the source URL while a remote OTA is running. It is gated only by `sys.info_view`. Phase 4 then commits real replies to the repo as fixtures, and the only redaction rule is "không chứa secret". IMSI, ICCID, phone number, SSID and OTA URL are not covered.
- **Failure scenario:** During a remote OTA, a viewer with only `sys.info_view` presses "Đọc" and gets the OTA source URL, possibly a presigned or tokenized link. They can then fetch the firmware image. Separately, the lab SIM's identity fields and SSID end up in git history through `*.fixture.json`.
- **Evidence:**
  - Spec line 297: "`ota.get_status` | — | Trạng thái OTA; poll khi download/staging".
  - `engineering-docs/api/http-api.md` line 452 @18a63e8ed: `GET /api/ota/status` "Không có key JSON/query literal trực tiếp".
  - Spec line 327 and line 475: `network.get_modem` "không đầy đủ IMEI/ICCID/operator/phone của HTTP modem status".
  - Brainstorm line 28: `ota.remote_config_get` "(may hold URL/token)".
  - Phase 4: "IP, MAC và hostname của lab giữ nguyên được".
- **Suggested fix:** Keep `ota.get_status` and `network.get_modem` out of `EnabledActions` until Phase 4 has captured their real field set, including during an OTA download. Alternatively, have the cards render only an allowlist of known fields and drop unknown keys instead of dumping them. Extend the fixture redaction rule to IMEI/IMSI/ICCID/phone/SSID/URL/any token-like string.

## Finding 5: The high-risk update is miscounted, test changes are missing, and SMS content reads stay delegable
- **Severity:** Medium
- **Location:** Phase 1, "Related Code Files" (assertions to change) and "Rủi ro cao"; plan.md Acceptance
- **Flaw:**
  - (a) `sms.send_status` is a class R row whose permission is `sms.send`. Once `sms.send` is high-risk, the number of high-risk actions goes from 18 to **21**, not 20. `CommandPermissionResolverTests` must also list `sms.send_status`; the plan lists only `sms.delete` and `sms.send`.
  - (b) The plan misses three other assertions that will change: `CatalogTests.cs:109` (namespaces 12 → 13), `CatalogTests.cs:155` (46 → 51) and `DevicePermissionVocabularyTests.cs:28` (46 → 51).
  - (c) The plan's rule is "Test cũ chỉ đổi ở đúng các assertion mà phase liệt kê". The implementer will therefore hit red tests the plan does not allow them to change. The tempting fix is to bend the policy (for example, keep `sms.send` out of `HighRisk`) rather than the numbers.
  - (d) The plan calls SMS sensitive, yet leaves `sms.view` delegable. It guards `sms.list`/`sms.get`, which return phone numbers and full message content, including any OTP/2FA codes the gateway SIM receives. By the time SMS reads open, org admins may already have handed it out widely. Separately, class B payloads are persisted in the audit `PayloadSummary`, so an opened `sms.send` would store phone number and message text in the database.
- **Failure scenario:** The tests-first step for (a)-(c) cannot be met as written. On (d), a non-master org admin delegates `sms.view` today. When SMS reads open later, every role with that code reads the inbox.
- **Evidence:**
  - Recomputed from §11 @18a63e8ed: high-risk actions `[..., sms.delete, sms.send, sms.send_status]` = 21.
  - `ManagementApiTests.cs:250` `Assert.Equal(18, ...)`; `CommandPermissionResolverTests.cs:136-148`; `CatalogTests.cs:109`; `CatalogTests.cs:155`; `DevicePermissionVocabularyTests.cs:28`.
  - `EfCommandAuditStore.cs:218-226`: `SummarizePayload` serializes the payload for `Physical`/`Destructive`/`Flash`.
  - Spec line 306: `sms.list` "trả số điện thoại và tới 160 byte nội dung mỗi tin".
- **Suggested fix:** Correct the numbers (21; 13 namespaces; 51 permissions) and list every assertion that changes. Decide now whether `sms.view` belongs in `HighRisk`. Record that class B audit summaries must redact `phone`/`message` before `sms.send` can ever open.

## Finding 6: `modbus.get_rtu_tags` and `tags.get` expose configuration that the plan keeps closed elsewhere
- **Severity:** Medium
- **Location:** plan.md "Quyết định" (self-approved `modbus.get_rtu_tags`) and "Ngoài phạm vi" (`modbus.get_rtu_config` closed); Phase 3, "Modbus"
- **Flaw:** The plan keeps `modbus.get_rtu_config` closed as configuration, which needs `mb_rtu.config_view`. But it opens `get_rtu_tags`, whose items include `registerAddress, functionCode, dataType, byteOrder, tagIdRaw`, under the view-level `mb_rtu.tag_view`. That is the field-bus register map. `tags.get` returns "binding/EU range". The card uses only `name`/`unit`, yet the browser and the response store receive the whole reply. The plan's own justification ("Tên tag, đơn vị và tên thiết bị nằm ở `get_rtu_tags`") never weighs the register-map exposure.
- **Failure scenario:** A monitoring-only user with `mb_rtu.tag_view` reads the full register and function-code map of every enabled slave. That is the reconnaissance needed for a targeted Modbus write by anyone with bus or LAN access to an RTU gateway, and the operator never granted `config_view`.
- **Evidence:**
  - Spec line 401: `get_rtu_tags` item "`channelIndex,channelId,slaveAddr,deviceName,cid,name,unit,registerAddress,functionCode,dataType,byteOrder,tagIdRaw`".
  - Spec line 263: `tags.get` "detail có binding/EU range".
  - Spec line 743: the §11 permission is "metadata policy cho server", so the server may require more than §11 does.
- **Suggested fix:** Either require `mb_rtu.config_view` in addition to `mb_rtu.tag_view` for `get_rtu_tags`/`tags.get` (a server-side extra check), or get an explicit owner decision recorded in plan.md that tag-view holders may see the register map. Do not let it be implied by a self-approved scope addition.

## Finding 7: Reply data is retained and released without a permission re-check; "server không lưu bản đọc" is false
- **Severity:** Medium
- **Location:** plan.md "Quyết định" (Đường dữ liệu: "server không lưu bản đọc"); Phase 3, "Kết quả từng lệnh"
- **Flaw:** Every verified reply's `data` is stored in `InMemoryCommandResponseStore`. That store is a single FIFO of 2048 entries shared across all devices and users, with no TTL. `GET /commands/{correlationId}` returns the stored data to the issuer based on `IssuedBy` and org scope only. It does not re-check the device permission that allowed the read. And because the FIFO is shared, a burst from one user evicts others' replies. Phase 3 says "`Executed` thì lấy `reply.data`" but does not cover `result=Executed` with `reply=null`.
- **Failure scenario:** (a) An admin removes a user's `modem.view` after they read the modem projection. The user keeps fetching that reply until it is evicted. (b) A scripted user runs about 2048 reads across devices. Other operators' cards and C2 dialogs then get `Executed` with no data and show an empty card or a wrong error, although the device did answer.
- **Evidence:**
  - `DeviceLinkHandlers.cs:95-102`: `responses.Save(... response.Data?.GetRawText() ...)`.
  - `DeviceLinkContracts.cs:332-349`: global `Queue` capacity 2048, no expiry.
  - `DeviceCommandsController.cs:60-70`: the only filter is `response.IssuedBy != actor.UserId && !actor.IsMasterAdmin`, with no `DeviceAccessPolicy.Evaluate` on the action's permission.
  - `DeviceCommandsController.cs:66,75-77`: `result` (including the device's `data.error` detail) goes to any in-scope viewer.
- **Suggested fix:** Correct the plan wording. On read-back, re-evaluate `DeviceAccessPolicy` for the stored action's permission. Add a TTL (for example 5 min) and a per-device or per-user cap to the response store. Add a Phase 3 test for the "Executed but reply missing" state.

## Finding 8: `allowed-commands` as specified becomes a ~100+ query endpoint (the DRY rule conflicts with the cost note)
- **Severity:** Medium
- **Location:** Phase 2, "`GET /api/v1/devices/{deviceId}/allowed-commands`" (DRY bullet) and "Risk Assessment" (Chi phí endpoint)
- **Flaw:** Phase 2 asks for `Allows(device, permission)` to be moved into a shared helper and reused. That helper builds a new `AccessActor` on every call, which loads the full organizations list plus grants, then resolves the membership and role again. Calling it for about 34 actions means roughly 34 × (org list + grants + membership + role-with-permissions) EF queries per request. The cost note says "Policy chạy trong bộ nhớ nên rẻ", which is wrong, and makes the fix conditional. Any authenticated in-scope user can call the endpoint, and nothing rate-limits it.
- **Failure scenario:** Every device page load fires about 100+ queries. A user looping on this endpoint applies a multiplied load to the DB, so a single cheap request costs the server far more than it costs the attacker.
- **Evidence:**
  - `DeviceLinkController.cs:33-40`: `Allows` calls `AccessActorFactory.FromPrincipal` and `accessResolver.Resolve` on each call.
  - `AccessActorFactory.cs:24-41`: `organizations.List()`.
  - `EfManagementStores.cs:35`: `db.Organizations.AsNoTracking()...`.
  - `EfDeviceAccessStores.cs:11-16,153-157`: per-call `SingleOrDefault`.
- **Suggested fix:** The helper should take an already resolved `DeviceAccess`. Resolve the actor and access once per request, then evaluate the ~34 permissions in memory. Add a test that counts resolver calls, or at least assert in the design that resolution happens once.

## Finding 9: The broker risk is assessed only as replay; reply confidentiality and command-key handling are ignored
- **Severity:** Medium
- **Location:** plan.md "Rủi ro" (Broker lab anonymous); Phase 4, "Tiền điều kiện" and "Tool"
- **Flaw:** The plan treats the anonymous broker only as a replay risk ("lệnh đọc bị replay thì vô hại"). But each newly opened read publishes its reply as signed plaintext on `B/cmd/res`, and anyone on the anonymous broker can subscribe to that. The replies carry device MAC/IPs/gateway/netmask, the SSID, the modem projection and the Modbus register map. Phase 4 also depends on untracked scripts in a session scratchpad (`setup-dev-secrets.ps1`, `import-gw-key.js`, `do-run.js`) and a key file there. That key is the gateway command key: under spec R11, whoever holds it has full owner authority. The plan has no rule for storing or deleting it.
- **Failure scenario:** Anyone connected to the lab broker subscribes to `hms_toan_lab/+/cmd/res` and collects the network and field-bus map every time an operator presses "Đọc". A leftover key file in a temp folder gives full owner authority (reboot, factory reset, OTA) over the gateway.
- **Evidence:**
  - `docs/architecture-mqtt-device-link.md:235,247`: anonymous broker, "Danh tính/ACL broker cho server chưa được định nghĩa".
  - Spec line 55 (R11): "ai có khoá lệnh là có toàn quyền".
  - `git ls-files | grep setup-dev-secrets|import-gw-key|do-run`: no result (UNVERIFIED, not in repo).
- **Suggested fix:** Add the confidentiality risk to plan.md. Before production, require broker ACLs so that only the server identity can subscribe to `+/cmd/res`. In Phase 4, delete the key file after verification and state where the key may live (user-secrets only).

---

## Fact check (Standard tier)

| Claim | Result |
|---|---|
| HMS-IDF `18a63e8ed9a6fb09101c253bc6c6fcff3eafb952`, blob `d0e6c3152a54030bbe81f71148a2bc8978bbdf6b` | VERIFIED (`git rev-parse`) |
| Generator reads §11 and `--check` exits 0 after re-pin | **FAILED**: throws at `generate-command-catalog.mjs:378` (Finding 1) |
| 10 new rows with class/perm/TTL/confirm as listed | VERIFIED (parsed §11) |
| Re-pin changes no existing row | VERIFIED: 0 changed rows, `config_domains` identical, so no permission changes for already-open commands |
| `SOURCE_COMMIT` | VERIFIED `scripts/generate-command-catalog.mjs:21` |
| `DevicePermissionCodes.HighRisk` | VERIFIED `DevicePermissionCodes.cs:21-28` |
| CatalogTests pins and lines 107, 108, 294; `FirmwarePermissions` 46 | VERIFIED `CatalogTests.cs:15,18,19,24,30,107,108,155,294`; **missed** `:109` (12 namespaces) |
| `ManagementApiTests` 94 (l.191), highRisk 18 → 20 (l.250) | Lines VERIFIED; target **FAILED** (21) |
| `DevicePermissionVocabularyTests` count | **MISSED** by plan: `:28` asserts 46 |
| `PhysicalOutputSafetyTests`, `OutputCommandPolicy` `PHYSICAL_ACTION_UNSUPPORTED` | VERIFIED `OutputCommandPolicy.cs:15,50-51` |
| `PinnedCommandCatalog.EnabledActions` | VERIFIED `PinnedCommandCatalog.cs:48-62,205` |
| `DeviceLinkController.Allows` | VERIFIED `DeviceLinkController.cs:33-40` |
| `In_scope_device_command_reaches_the_catalog_lookup` | VERIFIED `ManagementApiTests.cs:551` |
| `canViewOutputs` tests | VERIFIED `ManagementApiTests.cs:359-361` |
| `runDeviceCommand` | VERIFIED `frontend/src/core/commands/useDeviceCommand.ts:69` |
| `frontend/src/core/api/adminApi.ts`, `DeviceDetailView.vue/.test.ts`, both docs files | VERIFIED |
| "Spec chỉ đóng 9 lệnh SLICE_PASS" | **FAILED**: spec line 1085 says the 4 Modbus reads are SLICE_PASS (working-tree image) |
| "server không lưu bản đọc" | **FAILED**: `InMemoryCommandResponseStore` (Finding 7) |
| `setup-dev-secrets.ps1`, `import-gw-key.js`, `do-run.js` | UNVERIFIED: not tracked in repo |

## Contract consumers (Contract Verifier)

- **`EnabledActions`**
  - Consumers: `PinnedCommandCatalog.cs:205` (availability), `CatalogTests.cs:118,124`, `ManagementApiTests.cs:200` (dynamic, needs no change), `docs/architecture-mqtt-device-link.md:61-62` (Phase 4 updates it).
  - **Missed:** the legacy root command panel, `frontend/src/App.vue:4-7,772`, which uses the legacy `frontend/src/adminApi.ts` with feature-permission gating. It and `DeviceCommandModal.vue:106-112` (gated on `props.permissions`, not device roles) will offer all 21 reads with free-form payloads and never consult `allowed-commands`. `tags.*` is "chỉ qua modal", so its button visibility will not match the device-role decision.
- **`Allows` extraction:** `DeviceLinkController.cs:51,87,88`. Per-call cost: Finding 8.
- **`DeviceLinkDto`/`adminApi`:** the DTO is unchanged (`DeviceLinkController.cs:204-217`, `frontend/src/core/api/adminApi.ts:102-117`). Only a new `allowedCommands` method is added. The legacy `frontend/src/adminApi.ts` is not updated (see above).
- **Catalog JSON / hash consumers:** `CatalogTests.cs:15-30`, `generate-command-catalog.test.mjs:145-152` (render fixture, independent), `docs/architecture-device-mqtt-api-catalog.md:280`, `/api/v1/me/command-catalog` (`MeController.cs:48-72`). On deploy, clients with the old hash get `CATALOG_STALE` (`SendDeviceCommandHandler.cs:70-77`), which fails closed.
- **`HighRisk`:**
  - Consumers: `CommandPermissionResolver.cs:36-38` → `MeController.cs:71` (catalog `highRisk` flag; the frontend does not read it), `DeviceAccessHandlers.cs:57,106,166,324,367-369` (role create/update/assign).
  - New role writes that include `sms.send`/`sms.delete` will need master-admin. No existing role can hold them, because the vocabulary rejected them before.
  - Test consumers: `DevicePermissionCodesTests.cs:22-33`, `CommandPermissionResolverTests.cs:136-148`, `ManagementApiTests.cs:250`.
- **Scope gate:**
  - The handler treats a user as in scope if their organization is in scope or they hold a membership on the device (`DeviceAccessResolver.cs:30`). `GET /commands/{id}` and `/link` use org scope only (`ScopeAuthorization.cs:34-39`).
  - So a user with a device membership but no org scope can POST reads but can never read the replies back. Phase 2 should state which scope gate `allowed-commands` uses.

Status: DONE_WITH_CONCERNS
Summary: Phase 1 cannot run as written: the generator rejects spec `18a63e8ed` (§11 prose says 98 pairs, the table has 104), and the spec itself marks the new rows as uncommitted working-tree firmware. Seven High/Medium security gaps also need plan changes before implementation: no server-side limit protects relay commands from read traffic, existing roles silently widen, undefined reply shapes are dumped raw and committed as fixtures, the high-risk count and test list are wrong, the Modbus register map leaks under a view-level permission, stored replies are released without a permission re-check, and `allowed-commands` runs N+1 queries. A Medium finding covers broker confidentiality and command-key handling.
