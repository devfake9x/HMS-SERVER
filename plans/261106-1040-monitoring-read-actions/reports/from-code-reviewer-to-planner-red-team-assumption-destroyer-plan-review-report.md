# Red-team review: monitoring read actions plan (assumption destroyer)

Date: 2026-11-06. Reviewer: code-reviewer (hostile, plan-only). Scope: `plan.md`, phases 1-4, brainstorm
`plans/reports/261106-1029-brainstorm-monitoring-read-actions.md`. Spec read at the pin only:
`git -C D:/WORKSPACE/PROJECT/HMS-IDF show 18a63e8ed:engineering-docs/api/mqtt-api.md` (blob
`d0e6c3152a54030bbe81f71148a2bc8978bbdf6b`, verified). "spec:N" below = line N of that blob.

Checks that ran (no code changed): `git diff 83e93d77e 18a63e8ed -- engineering-docs/api/mqtt-api.md`.
I also ran the exported pure `parseSpecManifest` from `scripts/generate-command-catalog.mjs` on both
spec blobs. Separately, I parsed the new blob after an in-memory prose patch, only so I could diff its rows
against the old pin.

---

## Finding 1: The generator rejects the pinned spec, so Phase 1 cannot run as written
- **Severity:** Critical
- **Location:** Phase 1, section "Related Code Files" (only change listed for the generator is `SOURCE_COMMIT`) and "Implementation Steps" step 2
- **Flaw:** The plan assumes that once `SOURCE_COMMIT` changes, the generator produces a 104-row catalog. It does not. `parseSpecManifest` ends with `assertStatedCounts`, which takes the first "N cặp" and "N namespace" in the §11 prose and requires them to equal the table. At `18a63e8ed` the prose states "98 cặp, 12 namespace" for HEAD, then "100 cặp, 13 namespace" for the G01 overlay. The table has 104 rows and 13 types, so neither stated count matches.
- **Failure scenario:** Step 2 exits 1 with `spec §11 states 98 pairs but its action table has 104 rows`. No JSON or hashes are produced, and Phases 2-4 are blocked. To go further, the executor must either weaken a security-boundary guard or hand-edit the generated file. The guard is pinned by its own tests (`generate-command-catalog.test.mjs:282-286`), and neither option is in the plan's authorized files or its TDD rule.
- **Evidence:**
  - `scripts/generate-command-catalog.mjs:202` `assertStatedCounts(nfc(prose.join('\n')), commands);`
  - `scripts/generate-command-catalog.mjs:374-379`: the first `(\d+)\s+cặp` must equal `commands.length`.
  - spec:745-747 "Snapshot nền tại `HEAD` có 98 cặp, 12 namespace; working-tree G01 overlay bổ sung `tags.list`/`tags.get` thành 100 cặp, 13 namespace".
  - spec:19 "phân biệt baseline `98/12` ở commit nền với working-tree `100/13`".
  - spec:1059-1060: the four Modbus rows "are new in the working tree; they are not in HEAD `dc3ecc198`".
  - Run result: old pin `OK rows 94 types 12`; new pin `FAIL: spec §11 states 98 pairs but its action table has 104 rows`.
- **Suggested fix:** Add a gating decision before Phase 1, then put whichever path is chosen into Phase 1.
  - **Option (a):** Log a spec gap with HMS-IDF (the §11 prose count is stale and contradicts itself). Re-pin to a commit whose prose says 104/13.
  - **Option (b):** Deliberately change the count rule, for example by trusting the pair-set and catalog-hash pins instead of the prose. This needs a new generator test, and the change must be recorded as a weakened guard.
  - **Either way:** Add `generate-command-catalog.mjs` and its test to the modify list. Also record that the spec calls 6 of the 10 new rows working-tree/unreleased (spec:14-19, 1059-1060), and confirm the owner accepts pinning them.

## Finding 2: The high-risk count is 21, not 20, and `sms.send_status` silently becomes high-risk
- **Severity:** High
- **Location:** Phase 1, "Requirements → Rủi ro cao" and "Test có assertion đổi có chủ đích" (`highRisk` 18 → 20; `CommandPermissionResolverTests` "thêm `sms.delete`, `sms.send`"). Same claim in plan.md "Acceptance".
- **Flaw:** `HighRisk` is a set of permission codes, and `IsHighRisk(row)` checks each row's permission. The read `sms.send_status` requires `sms.send`, so adding `sms.send` to `HighRisk` flags three rows, not two.
- **Failure scenario:**
  - The tests the plan prescribes (`Assert.Equal(20, …)` and a list with two SMS rows) can never pass.
  - Product effect: granting a read-only status query now needs a master-admin (`RoleRules.CheckHighRisk`).
  - `/me/command-catalog` shows `highRisk:true` on a class-R row. Nobody decided this; it is a side effect.
- **Evidence:**
  - spec:823 `| \`sms.send_status\` | R | \`sms.send\` | 120000 |`
  - `src/Hms.Server.Application/Commands/CommandPermissionResolver.cs:36-38`
  - `src/Hms.Server.Application/Authorization/DeviceAccessHandlers.cs:367-369`
  - `tests/Hms.Server.IntegrationTests/ManagementApiTests.cs:249-250` `// 15 rows plus the three relay output rows` / `Assert.Equal(18, …)`
  - `tests/Hms.Server.UnitTests/CommandPermissionResolverTests.cs:140-147`
  - Computed from the parsed pin: 21 high-risk rows, including `sms.delete`, `sms.send`, `sms.send_status`.
- **Suggested fix:** Expect 18 → 21 and add `sms.send_status` to the expected list. Write down the decision that granting `sms.send_status` needs a master-admin, or justify a different gate.

## Finding 3: The list of intentionally changed assertions misses at least 10 that will break
- **Severity:** High
- **Location:** Phase 1, "Test có assertion đổi có chủ đích". The rule in plan.md "Test cũ chỉ đổi ở đúng các assertion mà phase liệt kê là có chủ đích" makes the gap blocking.
- **Flaw:** After regeneration the following existing assertions fail. None of them are listed. The "test ma trận quyền … nếu có" hedge is not a list.
- **Failure scenario:** `dotnet test` is red right after regeneration. Fixing it breaks the plan's own rule; obeying the rule leaves the executor stuck.
- **Evidence (new value computed from the parsed pin):**

| Assertion | Old | New |
|---|---|---|
| `CatalogTests.cs:139` Read rows | 49 | 57 |
| `CatalogTests.cs:142` Destructive | 3 | 4 |
| `CatalogTests.cs:143` Physical | 3 | 4 |
| `CatalogTests.cs:180` rows with TTL 60000 | 24 | 26 |
| `CatalogTests.cs:181` confirm rows among those | 3 | 5 |
| `CatalogTests.cs:184` rows with TTL 120000 | 70 | 78 |
| `CatalogTests.cs:193-200` confirm templates | 3 | 5 (+`sms.delete=SMS-DELETE-<DEVID>`, +`sms.send=SMS-SEND-<DEVID>`) |
| `CatalogTests.cs:155` FirmwarePermissions (test name says 46) | 46 | 51 |
| `DevicePermissionVocabularyTests.cs:15` vocabulary (name "fifty") | 50 | 55 |
| `DevicePermissionVocabularyTests.cs:28` firmware part | 46 | 51 |

  The brainstorm (line 19) lists six new permissions. There are five: `mb_rtu.config_view` already exists.
- **Suggested fix:** List every assertion above with its new value, and rename the count-bearing test names.

## Finding 4: The ADC card joins on a key the spec does not give to `adc.get_status`
- **Severity:** High
- **Location:** Phase 3, "Từng thẻ" (Nguồn & ADC row: "ghép theo `ai`"), the note "`adc.get_values` … thẻ không gọi", and "Tests first" item 4.
- **Flaw:** The spec shape of `adc.get_status` is "available, channels gồm value/unit/quality/raw_adc/raw_voltage", with no `ai`. Only `adc.get_channels` and `adc.get_values` document `ai`. The plan drops the one read that carries both `ai` and the value.
- **Failure scenario:**
  - On a spec-conformant device, the join matches nothing. If the executor falls back to a positional join, values get the wrong labels when a channel is disabled or skipped.
  - Phase 3 requires spec-shaped fixtures, so the fixture has to invent `ai` inside `get_status`. That makes a phantom test: green in CI, wrong on the device.
  - Phase 4 only catches this after the UI is built.
- **Evidence:** spec:281 `adc.get_channels … Array {ai,name,type,enabled}`; spec:282 `adc.get_status … channels gồm value/unit/quality/raw_adc/raw_voltage`; spec:283 `adc.get_values … Array {ai,value,unit,quality}`.
- **Suggested fix:** Join `get_channels` with `get_values` by `ai`. Use `get_status` only for `available` and raw fields, once a live reply proves its key. Alternatively, run a one-command live probe of `adc.get_status` before writing Phase 3 fixtures.

## Finding 5: The Modbus card contradicts the pinned paged-read contract in five places
- **Severity:** High
- **Location:** Phase 3, "Modbus: đọc theo trang" and "Tests first" item 5; Phase 4, "Lệnh Modbus" ("nếu lab không có slave (`total = 0`)").
- **Flaws:**
  - **(a) Empty state.** The plan says "Không có slave (`total = 0`): Chưa cấu hình". The spec says that without an RTU config, devices and tags return an error (`success:false`, an `esp_err_t` name, no `data`), not an empty list. The planned empty state never fires, and an unconfigured gateway shows a stack of `DEVICE_REJECTED` rows.
  - **(b) Restarting a traversal.** The spec says pages are not a snapshot: re-read from offset 0 when `total` changes or `snapshotUptimeMs` decreases. `readPages` only follows `truncated`, so a reboot or config change during a traversal mixes pages from two configs.
  - **(c) Stale catalog cache.** The cache of devices and tags is refreshed only when an unknown key appears. A `replace_rtu_config` that keeps `(channelIndex, slaveAddr, cid)` but changes `name`, `unit` or `dataType` leaves stale labels and units indefinitely. The card already reads `get_rtu_status` every time, which has `durableGeneration`, and tags/devices carry `generation`. The plan never compares them.
  - **(d) Restart errors.** During an RTU restart, devices and live return `ESP_ERR_INVALID_STATE`. The plan does not handle this, so it shows as a generic rejection.
  - **(e) Tag age.** The plan says "Tuổi cập nhật ≈ `snapshotUptimeMs − timestampUs/1000` … đồng hồ uptime". The spec does not state which clock `timestampUs` uses. If it is epoch-based, or 0 for a tag never polled, the age is nonsense.
- **Evidence:**
  - spec:399-402 (item shapes; live "Quality và timestamp là của cache tag", with no clock stated)
  - spec:421-431 "Các trang không phải một snapshot … Đọc lại từ offset 0 khi `total` đổi hoặc `snapshotUptimeMs` giảm"
  - spec:434-439 (restart → `ESP_ERR_INVALID_STATE`)
  - spec:454-455 "Tags và devices khi thiết bị chưa có cấu hình RTU trả lỗi của bước nạp cấu hình, không trả danh sách rỗng"
- **Suggested fix:**
  - Map the config-load error to "Chưa cấu hình" and `ESP_ERR_INVALID_STATE` to "Đang khởi động lại RTU".
  - Restart a traversal once when `total` or `snapshotUptimeMs` changes, with a bounded retry.
  - Drop the cache when `durableGeneration` differs from the cached `generation`.
  - Make Phase 4 confirm the `timestampUs` clock before the age is shown.
  - Add a test for each case.

## Finding 6: In `allowed-commands`, the mandated reuse of `Allows` costs about 4 DB reads per action
- **Severity:** Medium
- **Location:** Phase 2, "Quyết định quyền", "DRY: tách `Allows(device, permission)` … dùng chung", and the Risk "Policy chạy trong bộ nhớ nên rẻ".
- **Flaw:** Each call to `Allows` rebuilds the actor (`organizations.List()` plus the permission-grant read) and resolves access (a membership read plus a role read) before `Evaluate`. The plan requires reusing it for about 34 actions. Its mitigation ("nạp actor một lần") contradicts its DRY instruction.
- **Failure scenario:**
  - With a database configured, the stores are scoped EF. One page open costs about 34 × 4 ≈ 130+ queries.
  - Integration tests run without a connection string (in-memory stores), so the tests never show it.
- **Evidence:**
  - `src/Hms.Server.Api/Controllers/DeviceLinkController.cs:33-40` (actor and access rebuilt per call), also called twice per `GET /link` at :87-88
  - `src/Hms.Server.Api/Authorization/AccessActorFactory.cs:16-28` and :31-40
  - `src/Hms.Server.Application/Authorization/DeviceAccessResolver.cs:23`
  - `src/Hms.Server.Infrastructure/DependencyInjection.cs:99-107` (EF `IPermissionStore`, `IDeviceRoleStore`, `IDeviceMembershipStore`)
- **Suggested fix:** Have the shared helper return a `DeviceAccess?` resolved once per request. Loop over `DeviceAccessPolicy.Evaluate(access, device, perm)` in both controllers.

## Finding 7: The per-command error UI assumes data the client never receives, and the 403 test covers the wrong path
- **Severity:** Medium
- **Location:** Phase 3, "Kết quả từng lệnh" ("HTTP lỗi … kèm mã và detail") and "Tests first" item 1 ("HTTP 403 thành lỗi của lệnh đó").
- **Flaw:**
  - **HTTP errors:** `request()` throws `Request failed (NNN)` and throws away the body, so no code or detail is available.
  - **Missing permission:** this is not an HTTP 403. The handler returns HTTP 200 with `{status:"Failed", code:"DEVICE_PERMISSION_REQUIRED"}`, which `runDeviceCommand` reports as `phase:'not_sent'`. A 403 happens only when the actor is null.
  - **Stale catalog:** after the re-pin, a tab with the old catalog gets HTTP 200 `ContractUnavailable/CATALOG_STALE`. The plan has no handling for it.
- **Failure scenario:** The specified test passes against a path users rarely hit. The real denial and stale-catalog paths go untested and show raw codes instead of "Thiếu quyền" or "Tải lại trang".
- **Evidence:**
  - `frontend/src/core/api/adminApi.ts:189`
  - `src/Hms.Server.Application/Commands/SendDeviceCommandHandler.cs:76` (`CATALOG_STALE`) and :98-101 (`decision.Code` as `Failed`)
  - `src/Hms.Server.Api/Controllers/DeviceCommandsController.cs:28-32` and :47
  - `frontend/src/core/commands/useDeviceCommand.ts:78`
- **Suggested fix:** Test the HTTP 200 `Failed/DEVICE_PERMISSION_REQUIRED` path and `CATALOG_STALE` (prompt a reload). For HTTP errors show only the status, or list a change to `request()` that parses `{code}` as a cross-cutting change.

## Finding 8: Read sequences cannot be cancelled, and every read refreshes all viewers' "last heartbeat"
- **Severity:** Medium
- **Location:** Phase 3, "Chung" ("Gửi lần lượt", "Lệnh sau vẫn chạy khi lệnh trước lỗi", offline "vẫn cho đọc") and "Architecture" (`useDeviceReads`).
- **Flaw:**
  - Nothing aborts a sequence on unmount, on a tab switch, or on a second click, although `runDeviceCommand` accepts a signal. The Telemetry tab is rendered with `v-if`.
  - Offline, every command polls for up to 40 s, and the plan keeps going after timeouts.
  - Every result also publishes `command_result` to the device's realtime group. `DeviceDetailView.onEvent` treats any event other than `outputs` as a heartbeat.
- **Failure scenario:**
  - On an offline device, "Đọc" on the Network card takes 5 × 30-40 s ≈ 2.5-3.3 min and cannot be stopped.
  - If the user switches tab, comes back and clicks again, two signed, audited sequences run in parallel.
  - Meanwhile each `DEVICE_RESPONSE_TIMEOUT` event sets "last heartbeat" to now on every open page, so an offline device looks alive.
- **Evidence:**
  - `frontend/src/core/commands/useDeviceCommand.ts:6` (`POLL_LIMIT_MS = 40_000`), :72 (signal option) and :80-92
  - `frontend/src/features/devices/views/DeviceDetailView.vue:614` (`v-if="activeTab === 'telemetry'"`) and :266-277 (`lastHeartbeat.value = new Date()…` for every non-`outputs` event)
  - `src/Hms.Server.Application/DeviceLink/DeviceLinkHandlers.cs:182-186` (`command_result` published to the device group)
- **Suggested fix:**
  - Give each card an `AbortController`, aborted on unmount and before a new run.
  - Stop the sequence after the first `DEVICE_RESPONSE_TIMEOUT` or `MQTT_NOT_CONNECTED`, and mark the remaining commands "Bỏ qua".
  - Only refresh the heartbeat for telemetry/status event types.
  - Add tests for all three.

## Finding 9: Phase 4 relies on session-only scratchpad tools that truncate the data it must check
- **Severity:** Medium
- **Location:** Phase 4, "Requirements → Tiền điều kiện" and "Tool".
- **Flaw:**
  - The key file, `setup-dev-secrets.ps1`, `import-gw-key.js` and `do-run.js` exist only in one session's scratchpad. None is in the repo, and the plan gives no path.
  - `do-run.js` depends on `seed-env.js` and `api.js` (credentials).
  - `do-run.js` cuts reply data to 400 characters, while Modbus pages run up to about 8 KB.
- **Failure scenario:**
  - A later session running Phase 4 has a different scratchpad, so it has no key and no driver.
  - If `do-run.js` is used as written, the "so khoá trong data với spec" step and the real-reply fixtures are built from truncated JSON.
- **Evidence:**
  - Current session scratchpad: `do-run.js:3` `require('./seed-env.js')`; `do-run.js:29` `JSON.stringify(res.reply?.data ?? null).slice(0, 400)`
  - `git ls-files | grep -c "do-run.js\|import-gw-key.js\|setup-dev-secrets.ps1"` returns 0.
  - spec:459-462: signed-reply cap 8192; worst pages 7944 / 8145 / 3676 / 7740 bytes.
- **Suggested fix:** Commit a credential-free driver under `scripts/` that reads credentials from env or user-secrets and writes the full reply JSON to files. Document a key re-import procedure that does not depend on a past session.

---

## Fact check

| Claim (location) | Result |
|---|---|
| `SOURCE_COMMIT` lives in the generator (P1) | VERIFIED `scripts/generate-command-catalog.mjs:21` |
| Pin `18a63e8ed…`, blob `d0e6c315…` (P1) | VERIFIED (`git rev-parse`) |
| Exactly 10 new §11 rows; no old row's TTL, permission, class or confirm changed; config domains unchanged (P1) | VERIFIED (parsed old vs new rows: added 10, removed 0, changed 0) |
| The generator reads the pin "như cũ" (P1 step 2) | FAILED (Finding 1) |
| Count lines 94 at `CatalogTests.cs:107,108,294` | VERIFIED |
| `ManagementApiTests.cs:191` = 94, `:250` = 18 | VERIFIED; the target 20 FAILED (21, Finding 2) |
| FirmwarePermissions 46 → "giá trị mới" | VERIFIED: new value is 51 (+`tag.view`, `mb_rtu.slave_view`, `mb_rtu.tag_view`, `sms.send`, `sms.delete`) |
| `OutputCommandPolicy` refuses `sms.send` with `PHYSICAL_ACTION_UNSUPPORTED`, before signing | VERIFIED `OutputCommandPolicy.cs:50-51`, `HmsIdfCommandProtocolAdapter.cs:58-59` |
| The 10 s physical TTL clamp would apply to `sms.send` | VERIFIED `SendDeviceCommandHandler.cs:19,113-115` |
| `DEVICE_ACTION_UNAVAILABLE` arrives as `ContractUnavailable` | VERIFIED adapter `:42-44`, `MqttDeviceCommandTransport.cs:42-43` |
| `~/…/allowed-commands` is needed to avoid `commands/{correlationId}` | FAILED as a rationale, harmless: sibling segments, no collision (`DeviceCommandsController.cs:14,52`) |
| `DeviceLinkController.Allows` exists | VERIFIED `:33-40` |
| "Policy chạy trong bộ nhớ nên rẻ" (P2 risk) | FAILED for EF stores (Finding 6) |
| `runDeviceCommand(deviceId, body, {csrfToken, signal, limitMs, onDispatched})` | VERIFIED `useDeviceCommand.ts:69-73` |
| Only the sender (or a master-admin) can read reply data | VERIFIED `DeviceCommandsController.cs:68-70` |
| `adc.get_status` carries `ai` (P3) | FAILED spec:282 (Finding 4) |
| Modbus page block and `offset = offset + returned` | VERIFIED spec:405-407 area ("`truncated = offset + returned < total`") |
| Unconfigured Modbus gives `total = 0` (P3, P4) | FAILED spec:454-455 |
| `timestampUs` uses the uptime clock (P3) | UNVERIFIED (spec silent, spec:402) |
| `network.get_modem` gives operator (P3 table) | FAILED spec:475 (says it is not the full identity/operator/phone DTO); harmless, since it renders key-value |
| P2 comment text "Spec chỉ đóng 9 lệnh SLICE_PASS" | UNVERIFIED at the pin: the only `SLICE_PASS` line in `engineering-docs/api` at `18a63e8ed` is spec:1085, and it marks the four Modbus paged reads `SLICE_PASS`. A code comment should not cite this as spec fact. |
| `docs/architecture-mqtt-device-link.md`, `docs/architecture-device-mqtt-api-catalog.md` exist | VERIFIED |

## Contract consumers of the changed interfaces

- **`PinnedCommandCatalog.EnabledActions`:** `PinnedCommandCatalog.cs:205`; `CatalogTests.cs:118,124`; `ManagementApiTests.cs:200`.
- **`DevicePermissionCodes.HighRisk` / `IsHighRisk`:**
  - Server: `CommandPermissionResolver.cs:36-38`, then `MeController.cs:71` (catalog `highRisk`); `DeviceAccessHandlers.cs:57,106,166,324,367-369` (role create, update and assign gates).
  - Tests: `DevicePermissionCodesTests.cs:29-32`; `CommandPermissionResolverTests.cs:146`; `ManagementApiTests.cs:250`; `PhysicalOutputSafetyTests.cs:291`.
- **`FirmwarePermissions` (grows by 5):**
  - Server: `DevicePermissionVocabulary.cs:12`.
  - Tests: `DevicePermissionVocabularyTests.cs:15,28`; `CatalogTests.cs:155`; `DevicePermissionCodesTests.cs:31`.
- **`DeviceLinkController.Allows` (to be extracted):** `DeviceLinkController.cs:51,87,88`.
- **Catalog hash/version (changes):**
  - `SendDeviceCommandHandler.cs:72-76` (`CATALOG_STALE` for any open tab holding the old hash).
  - `frontend/src/features/devices/views/DeviceDetailView.vue:309-331` (catalog load and verify).
  - `DeviceCommandModal.vue` and `DeviceOutputsPanel.vue` (send `catalogVersion`/`catalogHash`).
- **New `adminApi.allowedCommands`:** `DeviceDetailView.vue:309-313` (`Promise.all` load; must `.catch(() => null)` like its siblings, or one failure aborts the whole page load); `DeviceDetailView.test.ts` mocks.

Status: DONE_WITH_CONCERNS
Summary: Phase 1 cannot run as written. The generator's stated-count guard rejects spec `18a63e8ed`, because its §11 prose says 98 pairs and 12 namespaces while the table has 104 rows. Even with that fixed, the test changes are miscounted (high-risk is 21, about 10 assertions are missing), and the ADC and Modbus card designs contradict the pinned spec.
