# Red team (Assumption Destroyer): plan 261106-2033 remote config, system and ADC

Reviewed: `plan.md`, `phase-01-server-config-writes.md`, `phase-02-config-edit-ui.md`, `phase-03-live-verification-docs.md`, the brainstorm report, and the monitoring-read plan with its live-verification report. Checked against the pinned spec (scratchpad export `mqtt-api-83e93d77e.md`), `command-catalog.json` and the current code. I did not open any firmware source, did not contact any device, broker or API, and did not copy identifiers from `tools/device-reads/`.

Spec line numbers below refer to the scratchpad export `mqtt-api-83e93d77e.md`.

---

## Finding 1: "POST returns 403" is false; a missing permission is HTTP 200 + `DEVICE_PERMISSION_REQUIRED`
- **Severity:** High
- **Location:**
  - plan.md, "Acceptance › Quyền" (line 75: "không có quyền thì không thấy nút sửa, và `POST` trả 403");
  - Phase 1, "Tests first" item 6 (line 89: "`POST set_hostname` trả 403");
  - Phase 2, "Luồng chung › Kết quả" (line 78: "HTTP 403 → Thiếu quyền") and "Tests first" item 1 (line 90).
- **Flaw:** `POST /commands` always answers `Ok(...)`. A missing device permission becomes `status: Failed, code: DEVICE_PERMISSION_REQUIRED` in an HTTP 200 body. The only `Forbid()` is for a null actor (an unauthenticated principal), which never reaches these screens. The controller is not in Phase 1's Modify list, so the plan does not intend to change this. The previous plan's red-team review flagged the same mistake (monitoring plan, assumption-destroyer Finding 7).
- **Failure scenario:**
  - Phase 1 integration test 6b cannot pass: the response is 200. Under the TDD rule, either the test stays red, or someone changes a public contract used by the DO panel, the read cards and the modal (none of which are in the plan).
  - In Phase 2, "Thiếu quyền" never appears. A revoked user gets `Failed/DEVICE_PERMISSION_REQUIRED`, which is not `certainlyNotSent`, so it falls into an unclassified bucket (see Finding 2).
- **Evidence:**
  - `src/Hms.Server.Api/Controllers/DeviceCommandsController.cs:28-32` (`Forbid()` only when `actor is null`) and `:47` (`return Ok(await handler.HandleAsync(...))`).
  - `src/Hms.Server.Application/Commands/SendDeviceCommandHandler.cs:103-107`: `decision.Allowed == false` returns `Failure(request, CommandExecutionStatus.Failed, decision.Code)`.
  - `src/Hms.Server.Domain/Authorization/DeviceAccessPolicy.cs:23` (`PermissionRequired = "DEVICE_PERMISSION_REQUIRED"`).
  - The existing integration tests assert HTTP 200 plus a body code for command refusals: `tests/Hms.Server.IntegrationTests/ManagementApiTests.cs:716-718` and `:1207-1208`.
- **Suggested fix:**
  - Rewrite the acceptance criterion and test 6b as "HTTP 200, `status:Failed`, `code:DEVICE_PERMISSION_REQUIRED`, no publish (audit has no Published row)".
  - In Phase 2, map `DEVICE_PERMISSION_REQUIRED` to "Thiếu quyền" and drop "HTTP 403" from the `not_sent` test list.

## Finding 2: `runDeviceWrite`'s result mapping leaves server refusals, `MqttDeferred`, 5xx and network errors unclassified, and departs from the DO panel's rule
- **Severity:** High
- **Location:** Phase 2, "`useDeviceWrite.ts` › Phân loại kết quả" (lines 28-32) and "Job còn chạy" (line 33).
- **Flaw:** The mapping is a whitelist:
  - `certainlyNotSent` or HTTP 4xx → `not_sent`;
  - `Executed` → `executed`;
  - `Failed` + `DEVICE_REJECTED` → `rejected`;
  - `DEVICE_RESPONSE_TIMEOUT` / `MQTT_PUBLISH_UNCONFIRMED` / timeout → `unknown`.

  Everything else has no defined `kind`:
  - every `Failed` server-side code: `DEVICE_PERMISSION_REQUIRED`, `KEY_GENERATION_FENCE`, `IDEMPOTENCY_IN_PROGRESS`, `IDEMPOTENCY_OUTCOME_UNKNOWN`, `CORRELATION_ID_REUSED`, `COMMAND_EXPIRED`, `COMMAND_TIMESTAMP_IN_FUTURE`, `DEVICE_NOT_FOUND`, `AUDIT_RESULT_MISSING`;
  - status `MqttDeferred` (server without a live link);
  - HTTP 5xx and fetch rejections;
  - `Acknowledged` with no `operation_id`. `ToResult` emits `Detail: null` in that case.

  `runDeviceCommand` reports all `Failed` dispatches as `phase:'not_sent'`, but `certainlyNotSent` is deliberately narrower. Its own comment says an idempotency outcome "may follow a publish, so anything that drives hardware reads the state back unless this holds". The DO panel already implements the safe rule: unlock only on `certainlyNotSent` or HTTP < 500; read back for everything else, including server errors and lost connections. The plan reimplements a parallel, weaker classifier.
- **Failure scenario:**
  - A user double-submits after a reload and gets `IDEMPOTENCY_OUTCOME_UNKNOWN`, or the API returns 502 after the publish. Both are unclassified, so there is no read-back, and the card can stay on "Đang ghi…" or show a raw code while the device may already hold the new hostname.
  - The plan's own invariant ("Chỉ trường hợp này không cần đọc lại") is broken by omission.
- **Evidence:**
  - `frontend/src/core/commands/useDeviceCommand.ts:32-42` (`mayHaveReachedDevice` vs `certainlyNotSent` and the comment at :36-38) and `:78` (any non-Published dispatch → `not_sent`).
  - `frontend/src/features/devices/components/DeviceOutputsPanel.vue:170-219`: `certainlyNotSent` and `status < 500` unlock; everything else goes to `readBackTwice`.
  - `src/Hms.Server.Application/Commands/SendDeviceCommandHandler.cs:43,48,139,146-157` (the `Failed` codes).
  - `src/Hms.Server.Application/Commands/DeferredDeviceCommandTransport.cs:12-13` (`MqttDeferred`).
  - `src/Hms.Server.Application/DeviceLink/DeviceLinkHandlers.cs:150-152` (`operationId is null ? null : ...`).
- **Suggested fix:** Use the DO panel's rule:
  - `not_sent` only for `certainlyNotSent(dispatch)` or HTTP < 500;
  - `executed`, `rejected`, `job` and `unknown` as listed;
  - every other outcome → `unknown` + read-back.

  Add test rows for `IDEMPOTENCY_OUTCOME_UNKNOWN`, HTTP 502, a network error, `MqttDeferred`, and `Acknowledged` without `operation_id`. Consider moving the classifier out of `DeviceOutputsPanel.vue` into `core/commands` so both write paths share it.

## Finding 3: The "intentional" catalog test change does not exist, and the test that will actually break is not listed
- **Severity:** High
- **Location:** Phase 1, "Tests first" item 1 (line 69: "assertion có chủ đích phải sửa: số lệnh mở cũ (28 → 34)"), together with plan.md "Phases" (lines 59-60: "Test cũ chỉ được đổi ở đúng các assertion mà phase liệt kê là có chủ đích").
- **Flaw:**
  - No test asserts 28. The open-row count is checked through two other assertions:
    - the size of `EnabledActions` (11 + 15 = 26), which is unaffected;
    - a set equality between the enabled rows and `EnabledActions ∪ PhysicalOutputActions`, which breaks as soon as any W/C row opens.
  - The test's name, "opens only the closed reads and the two relay outputs", also becomes false.
  - The plan does not list that assertion, and its own rule forbids changing unlisted assertions.
- **Failure scenario:** After step 4 ("Thêm `ConfigWriteActions`"), `Pinned_catalog_opens_only_the_closed_reads_and_the_two_relay_outputs` goes red. The implementer must either break the rule or stop. The "28 → 34" edit has nothing to apply to.
- **Evidence:**
  - `tests/Hms.Server.UnitTests/CatalogTests.cs:138-147`: `expected = EnabledActions.Concat(PhysicalOutputActions)`, then `Assert.Equal(expected..., enabled...)`.
  - `:135` (`Assert.Equal(11 + monitoring.Length, PinnedCommandCatalog.EnabledActions.Count)`).
  - `grep -rn "\b28\b" tests/` finds only `MaitechRtuDeviceIntegrationTests.cs:144` (`"28.4"`, a temperature).
- **Suggested fix:** Replace the "28 → 34" line with: "`Pinned_catalog_opens_only_...` (CatalogTests.cs:138-156): rename it, and add `ConfigWriteActions` to `expected`. Add `Assert.All(ConfigWriteActions, class is Write or Control)`." Keep the 34 total as a new assertion.

## Finding 4: `refresh` through `reads.read` is skipped while the card is reading, so the read-back can be stale or never run
- **Severity:** High
- **Location:** Phase 2, "`DeviceReadCard.vue` › Slot" (line 44) and "Luồng chung › Trong lúc ghi" (line 71: "khoá mọi control ghi của thẻ đó").
- **Flaw:**
  - `useDeviceReads.read()` returns at once when `reading` is true, and returns `Promise<void>`, so the caller cannot tell "skipped" from "done".
  - The plan locks only the card's write controls. The header's "Đọc" and "Dừng" buttons stay live.
  - `refresh(steps)` also bypasses the `allowedCommands` filter that `start()` applies.
- **Failure scenario:** The user presses "Đọc" (a five-step ADC card read takes about 5 s or more with the busy retry), then confirms a rename.
  - The write executes in about 1 s and its `refresh([adc.get_channels, adc.get_values])` is skipped.
  - `useDeviceWrite` then compares against whichever `adc.get_channels` the earlier loop produced. That read may have been published before the write, so the card shows "Thiết bị báo đã ghi nhưng đọc lại ra <old>" for a successful write, or reads a `reading`/`idle` state.
  - Pressing "Dừng" during the read-back aborts it silently, leaving the result undefined.
- **Evidence:**
  - `frontend/src/core/commands/useDeviceReads.ts:92-93` (`async function read(steps): Promise<void> { if (reading.value) return`) and `:113-114` (in-flight states reset to `idle`).
  - `frontend/src/features/devices/components/reads/DeviceReadCard.vue:28-29,45-47` (`start()` reads `allowedSteps` only), `:55-77` (Dừng and Đọc are not tied to any write state), and `:95` (current slot props `states, stale, now`).
- **Suggested fix:**
  - Make `refresh` return a result such as `Promise<'done' | 'skipped' | 'aborted'>`.
  - Have it wait for, or cancel and restart, an in-flight read loop.
  - Disable "Đọc" and "Dừng" while a write is in progress, and filter refresh steps by `allowedCommands`.
  - Base the comparison on a read whose dispatch began after the write's outcome.
  - Add tests for "read in progress when the write completes" and "Dừng during read-back".

## Finding 5: The live tool has no job path, "6/6 Executed" ignores the spec's executor reply, and the final-state comparison includes values that always change
- **Severity:** High
- **Location:**
  - Phase 3, "Tool kiểm live › Kịch bản" (lines 32-45);
  - "Success Criteria" (lines 103-104, 107);
  - plan.md "Acceptance › Gateway thật" (line 84).
- **Flaw:**
  - **No job handling:** the tool's scenario never handles `Acknowledged`. "Dừng khi" (line 45) covers only a read-back mismatch and `unknown`. Yet line 103 requires the tool to work "gồm đường job còn chạy" against `SIM_ADC_JOB=1`. The spec allows any executor mutation to answer `ESP_ERR_TIMEOUT` with `accepted/pending`, and line 480 suggests this happens when a job runs past about 10 s. A pending reply is mapped to `Acknowledged`, not `Executed`, so "cả 6 lệnh `Executed`" is a criterion that correct behaviour can fail.
  - **`sync_ntp` has no expected value:** the spec says `sync_ntp` "chỉ kích hoạt sync, đọc lại get_time", so `ntp_synced` read right after says nothing about this sync. On the lab gateway it is already `true` before the call. Step 5 has no expected value, yet line 45 stops on "lệch giá trị mong đợi".
  - **Final comparison includes volatile fields:** "đọc lại toàn bộ … phải trùng hết" (lines 60, 107) compares `epoch`, `datetime`, `last_sync`, `ntp_synced` and ADC readings, which change on every read.
- **Failure scenario:**
  - On the real gateway, `adc.set_channel` returns pending. The tool reads back at once, sees the old name, and stops with recovery instructions.
  - Seconds later the job finishes and the channel is renamed on the device, which is the "test" state the plan promised to undo.
  - Separately, step 7 and the end-state check fail on the clock fields even when every write was restored.
- **Evidence:**
  - Spec lines 176 and 182 (executor reply; `state`, `resultSuccess`), 200 (`sync_ntp` only triggers a sync), and 480 ("job ghi còn chạy sau 10 giây").
  - `src/Hms.Server.Application/DeviceLink/DeviceLinkHandlers.cs:145-153` (`Acknowledged`, `DEVICE_ACCEPTED`).
  - `frontend/src/features/devices/components/reads/fixtures/lab-gateway-replies.json` (`system.get_time` has `ntp_synced:true` and a recent `last_sync` before any write).
- **Suggested fix:**
  - Reuse the Phase 2 classifier in the tool, including polling `get_operations` until `terminal` before reading back; or put the classifier in a shared plain-JS module.
  - Accept `Executed` or (`Acknowledged` → terminal success) in the success criteria.
  - For `sync_ntp`, expect `last_sync` to move forward within N seconds, polling `get_time`, or record the result as "triggered, not verified".
  - Define the compared field set per action: hostname; timezone; `ntp_servers`/`ntp_mode`; channel `{name,type,enabled}`.

## Finding 6: Read-back checks only the edited field and the stored value, so side effects and deferred application pass as "Đã ghi và xác nhận"
- **Severity:** Medium
- **Location:** Phase 2, "Thẻ Nguồn & ADC" (line 63: `adc.set_channel {ai, name}`), "Luồng chung › Kết quả" (lines 73-74), and "Thẻ Thiết bị & thời gian" (line 59, which handles only `reboot_recommended`).
- **Flaw:**
  - **`adc.set_channel`:** spec input is `{ai, name?, type?, enabled?}`, and the result is only "Cấu hình channel đã cập nhật". The spec does not say whether omitted fields are kept. The plan sends `{ai, name}` and compares only `name`.
  - **`system.set_hostname`:** it returns `applied_immediately`. The plan handles only `reboot_recommended:true`, and `get_hostname` reads the stored value.
  - **Spec §1.5** tells integrators to tell apart "nhận lệnh, hoàn thành job, lưu bền và áp dụng vào runtime".
- **Failure scenario:**
  - **ADC rename side effects:** a firmware that resets omitted fields turns a rename into "4-20mA → default type" or `enabled:false`. The card reports "Đã ghi và xác nhận" because the name matches, and the channel's scaling or telemetry silently changes on a production device.
  - **Hostname not applied:** `applied_immediately:false` with `reboot_recommended:false` shows as confirmed although the runtime hostname has not changed.
- **Evidence:**
  - Spec line 227 (`adc.set_channel` optional fields), line 197 (`applied_immediately, reboot_recommended`), line 34 (§1 step 5).
  - Lab channels carry `type:"4-20mA"` and `enabled:true` (`fixtures/lab-gateway-replies.json`, `adc.get_channels`).
  - `frontend/src/features/devices/components/reads/DevicePowerAdcCard.vue:73-76` (the row already holds name, type and enabled).
- **Suggested fix:**
  - For ADC writes, compare the whole channel row (`name`, `type`, `enabled`) before and after, and warn on any field the user did not change.
  - Show `applied_immediately:false` as "đã lưu, chưa áp dụng".
  - Add both items to the firmware spec-gap list (partial update of `adc.set_channel`; the meaning of `applied_immediately`).

## Finding 7: Job polling depends on `sys.info_view` and on the capped read lane, and the plan does not handle the poll's failures
- **Severity:** Medium
- **Location:** Phase 2, "Job còn chạy" (lines 33-37) and "Luồng chung › Chỉ bật khi" (line 69).
- **Flaw:**
  - **Permission:** `system.get_operations` needs `sys.info_view`. The edit gate checks only the write action and the matching read. A role with `adc.config_edit` and `adc.view` but without `sys.info_view` can start a write it can never follow.
  - **Read lane:** each poll is a read, held to 2 per user and 4 per device. The plan reuses neither `useDeviceReads`' busy retry nor any other handling.
  - **Unhandled poll results:** the mapping covers only `terminal` and `unknown`. Poll results such as `DEVICE_PERMISSION_REQUIRED`, `DEVICE_READ_BUSY`, `CATALOG_STALE` and `not_sent` are not mapped.
  - **`operation_id` type:** the spec requires it to be a decimal-string uint64. The id arrives only as text inside `detail` (`operation_id=<id>`), and parsing it to a JS number either sends the wrong JSON type or loses precision above 2^53.
  - **Reboot:** `operation_id` is valid only within one boot.
- **Failure scenario:**
  - **Role without `sys.info_view`:** a pending reply leads to 40 polls, each `Failed/DEVICE_PERMISSION_REQUIRED`, none classified. The form stays locked for 60 s and then ends with no defined outcome.
  - **Busy read lane:** with another card or tab reading, polls return `DEVICE_READ_BUSY`, which the plan does not handle.
- **Evidence:**
  - `src/Hms.Server.Infrastructure/Commands/command-catalog.json` (`system.get_operations` R `sys.info_view`); spec line 585.
  - `src/Hms.Server.Infrastructure/Mqtt/MqttDeviceCommandTransport.cs:57-66` (per-user `perDevice/2` cap, `DEVICE_READ_BUSY`).
  - `frontend/src/core/commands/useDeviceReads.ts:29-33,104-107` (busy detection and retry live only in the read loop).
  - Spec line 182 (`operation_id` "chuỗi decimal uint64 >0") and line 480 ("chỉ có nghĩa trong lần khởi động đó").
  - `src/Hms.Server.Application/DeviceLink/DeviceLinkHandlers.cs:150-152` (id surfaced only through `detail`).
- **Suggested fix:**
  - Enable edit controls only when `allowedCommands` also contains `system.get_operations`, or document the fallback "pending without job read → unknown + read-back".
  - Map every non-`Executed` poll result: busy → retry within the 60 s budget; anything else → `unknown` + read-back.
  - Keep `operation_id` a string end to end, and add tests for both points.

## Finding 8: The simulator does not implement the reads the flow needs; "reflect written values" assumes handlers that are not there
- **Severity:** Medium
- **Location:** Phase 3, "Simulator" (lines 22-25), "Implementation Steps" 1 (line 81) and "Success Criteria" (line 103).
- **Flaw:**
  - **What exists:** the simulator implements only `control.get_status`, `control.set_output`, `control.pulse`, `system.ping` and `system.get_info`. Every other action gets `unknown_action`.
  - **What the plan assumes:** it asks the simulator to "trả lời 6 lệnh" while `get_hostname`, `get_time` and `get_channels` "phản ánh giá trị đã ghi", and it expects `get_operations` to return `terminal`. None of those reads exist.
  - **What the UI needs:** the Phase 2 gate needs a successful `get_hostname`, `get_time` or `get_channels` before any edit control is enabled. With only six write handlers, no control ever enables on the simulator.
  - **Scope:** the real work is at least 6 write handlers, 4 read handlers (`get_hostname`, `get_time`, `get_channels`, `get_values`), `get_operations` with job state, and timed job completion. The other card steps (battery, `get_config`, `get_device_id`, OTA) will show `unknown_action` errors.
  - **No tests:** "nếu repo có test cho simulator" — there is none.
- **Failure scenario:** Phase 3 step 1 ("Kiểm UI với simulator") either cannot run, or grows without being planned. The job path is untested before the real gateway, which is the riskier place to find out (see Finding 5).
- **Evidence:**
  - `tools/mqtt-broker/spec-device-simulator.js:67-99` (handler table) and `:142` (`{ error: 'unknown_action' }` for anything else).
  - `grep -rln spec-device-simulator` finds no test file.
- **Suggested fix:** List all simulator handlers explicitly, with spec-shaped replies taken from the anonymised lab fixture. Define when the `SIM_ADC_JOB` rename is applied: on `terminal`, not on accept. Make "a write is applied only after the job ends" the case the UI and tool must handle.

## Finding 9: Several planned tests cannot prove what they claim, and one cannot be red first
- **Severity:** Medium
- **Location:** Phase 1, "Tests first" items 5 and 6 (lines 84-90).
- **Flaw:**
  - **(a) Test 6a cannot fail:** a role holding only `sys.hostname_edit` can never see `system.set_time`, because that row needs `sys.time_edit`. "Không có `system.set_time`" would pass whether the catalog opened it or not. To prove that `set_time` stays closed through the catalog (the plan's own residual risk, plan.md:113), the role must hold `sys.time_edit`, and the test must assert that `system.set_timezone` is present while `system.set_time` is absent.
  - **(b) Test 5 is already green:** `ToResult` and `PublishResultAsync` do not depend on the action or its availability, and `DeviceLinkTests` already has the accepted/pending theory row. The plan requires every test to be red before code changes, which this one cannot be.
  - **(c) Test 6c "không publish" is trivially true:** the integration host has no `Mqtt` section in `appsettings`, so it uses `DeferredDeviceCommandTransport`, which never publishes. The adapter also returns `DEVICE_MQTT_BINDING_MISSING` before any payload check, so the device must be bound through the link endpoint, which the test steps do not say.
- **Failure scenario:** The suite goes green while the regression it targets (`set_time` opened by mistake through `ConfigWriteActions`) goes unnoticed. Item 6c fails with `DEVICE_MQTT_BINDING_MISSING` on first run.
- **Evidence:**
  - `command-catalog.json` (`system.set_time` C `sys.time_edit`); `src/Hms.Server.Api/Controllers/DeviceCommandsController.cs:67-69` (allowed list = enabled ∧ caller holds the permission).
  - `tests/Hms.Server.UnitTests/DeviceLinkTests.cs:194-209` (the existing `Reply_outcomes_follow_the_spec_error_levels` row with `operation_id`) and `src/Hms.Server.Application/DeviceLink/DeviceLinkHandlers.cs:139-153,172-173`.
  - `src/Hms.Server.Infrastructure/DependencyInjection.cs:57-61`, `src/Hms.Server.Api/Program.cs:38`, `src/Hms.Server.Infrastructure/Commands/HmsIdfCommandProtocolAdapter.cs:31-33`.
- **Suggested fix:**
  - (a) Use a role with `sys.time_edit` and `sys.hostname_edit`, and assert the exact allowed write set: `set_hostname`, `set_timezone`, `set_ntp_servers`, `sync_ntp`, and not `set_time`.
  - (b) Mark test 5 as a regression guard, not red-first.
  - (c) Bind the device in the test, and assert the audit status `ContractUnavailable/COMMAND_PAYLOAD_INVALID` instead of claiming "không publish".

---

## Verification Results

### Fact Checker

**plan.md**

| # | Claim | Result |
|---|---|---|
| 1 | The 6 actions exist in the pinned catalog with the classes and permissions listed | VERIFIED: `command-catalog.json` rows; spec lines 587-601 |
| 2 | 26 reads + 2 DO + 6 = 34 open | VERIFIED: `PinnedCommandCatalog.cs:50-80` (26), `:86-91` (2) |
| 3 | Catalog JSON and hash unchanged (opening is code-only) | VERIFIED: `PinnedCommandCatalog.cs:110` (hash over rows), `:221-227` (availability set in code) |
| 4 | `sys.time_edit` is also the permission of `system.set_time` | VERIFIED: catalog row `system.set_time C sys.time_edit` |
| 5 | Without permission, `POST` returns 403 | FAILED: `DeviceCommandsController.cs:47`, `SendDeviceCommandHandler.cs:103-107` (Finding 1) |
| 6 | Spec gap "`get_time` returns `ntp_servers`/`ntp_mode` but §6.1 does not list them" | PARTIAL: spec line 198 says "timezone và cấu hình NTP"; the field names, not the existence, are undefined |
| 7 | Lane decision matches the approved brainstorm | FAILED: brainstorm line 71 says the slot ends at "`ExpiresAt` cộng một khoảng nhỏ, giống lane đọc". Phase 1 line 48 holds it until `Deadline`. The change is safer (R4) but is not recorded under "Quyết định". |
| 8 | "kiểm `DEVICE_WRITE_BUSY` trên thiết bị" | MISLABELLED: `DEVICE_WRITE_BUSY` is a server refusal before publish (Phase 1 line 51); a run on the device proves server behaviour only |

**Phase 1**

| # | Claim | Result |
|---|---|---|
| 1 | `TryInteger`, `IsBoolean`, `HasExactly` are private in `OutputCommandPolicy` | VERIFIED: `OutputCommandPolicy.cs:57,61,87` |
| 2 | `ToResult` → `Acknowledged` + `operation_id=` | VERIFIED: `DeviceLinkHandlers.cs:145-153` |
| 3 | `command_result` published for every class except R | VERIFIED: `DeviceLinkHandlers.cs:172-173` |
| 4 | `GET .../commands/{id}` re-checks permission (`MayReadReply`) | VERIFIED: `DeviceCommandsController.cs:102,118-127` |
| 5 | 20 s TTL cap for non-physical commands | VERIFIED: `SendDeviceCommandHandler.cs:20,123-125` |
| 6 | `allowed-commands` lists from the opened catalog | VERIFIED: `DeviceCommandsController.cs:67-72` |
| 7 | Policy runs after `OutputCommandPolicy`, before `secrets.Find` | VERIFIED: `HmsIdfCommandProtocolAdapter.cs:59-65` |
| 8 | The "28 → 34" assertion exists | FAILED: no such literal; the breaking test is `CatalogTests.cs:138-147` (Finding 3) |
| 9 | "Thiết bị bị khoá ghi tối đa 30 s" | PARTIAL: `Deadline = now + ResponseTimeoutSeconds` (`MqttDeviceCommandTransport.cs:56`, default 30 at `MqttLinkOptions.cs:27`), swept every 2 s (`MqttDeviceLinkService.cs:26,121,134-136`), or every `ReconnectDelaySeconds` while disconnected, so up to about 32 s or more |
| 10 | `POST set_hostname` → 403 | FAILED (Finding 1) |

**Phase 2**

| # | Claim | Result |
|---|---|---|
| 1 | `runDeviceCommand` = POST, then poll 40 s | VERIFIED: `useDeviceCommand.ts:6,69-93` |
| 2 | `certainlyNotSent` holds for `ContractUnavailable` (`DEVICE_WRITE_BUSY`, `COMMAND_PAYLOAD_INVALID`, `CATALOG_STALE`) | VERIFIED: `useDeviceCommand.ts:39-42`; `MqttDeviceCommandTransport.cs:48-49` |
| 3 | Slot props today are `states`, `stale`, `now` | VERIFIED: `DeviceReadCard.vue:95` |
| 4 | `refresh` can just call `reads.read` | FAILED as a contract: no-op while reading (`useDeviceReads.ts:93`) (Finding 4) |
| 5 | `PANEL_ONLY_ACTIONS` lives in `DeviceCommandModal.vue` | VERIFIED: `:67,70` |
| 6 | `frontend/src/core/devices/` exists for `timezones.ts` | VERIFIED |
| 7 | Spec §5 uses camelCase `operationId`, `resultSuccess` | VERIFIED: spec line 182 |
| 8 | HTTP 403 → "Thiếu quyền" | FAILED (Finding 1) |
| 9 | ADC rows join `get_channels` × `get_values` | VERIFIED: `DevicePowerAdcCard.vue:64-82` |
| 10 | `available:false` hides controls | VERIFIED with caveat: `available` comes from a separate step, `adc.get_config` (`DevicePowerAdcCard.vue:13,37-41`), not from the row data |

**Phase 3**

| # | Claim | Result |
|---|---|---|
| 1 | The simulator can reflect written values in `get_hostname`, `get_time`, `get_channels` and answer `get_operations` | FAILED: none of those handlers exist (`spec-device-simulator.js:67-99,142`) (Finding 8) |
| 2 | Credentials through `tools/tool-env.js` | VERIFIED for `HMS_ADMIN_EMAIL`/`HMS_ADMIN_PASSWORD` (`tool-env.js:5,19-24`) |
| 3 | `HMS_API_BASE` through `tool-env.js` | FAILED: read directly in `tools/device-read-check.js:22` |
| 4 | `tools/device-reads/` is gitignored | VERIFIED: `.gitignore:18` |
| 5 | `docs/architecture-mqtt-device-link.md`, `docs/architecture-device-mqtt-api-catalog.md`, `docs/journals/` exist | VERIFIED |
| 6 | Lab timezone is in the planned `SupportedTimezones` (`ICT-7`) | VERIFIED: fixture `system.get_time.timezone` |
| 7 | Lab NTP is auto with an empty list, so the restore is `[]` | VERIFIED: fixture `ntp_servers:[]`, `ntp_mode:"auto"` |
| 8 | The lab hostname passes the planned hostname regex, so the restore step will be accepted | VERIFIED: checked locally against the saved replies; value not reproduced. For other devices the restore can be refused, because the tool has a skip guard for timezone (line 35) but none for hostname. |
| 9 | ADC on the lab gateway: 8 rows of `{ai,name,type,enabled}` | VERIFIED: fixture `adc.get_channels` |
| 10 | Simulator tests exist "nếu có" | VERIFIED none |

### Contract Verifier

- **`IPendingDeviceCommandRegistry.TryRegisterWrite`**
  - **Interface:** `src/Hms.Server.Application/DeviceLink/DeviceLinkContracts.cs:260-272`.
  - **Implementers:** only `InMemoryPendingDeviceCommandRegistry` (`:274`). There are no test doubles; tests construct the real class at `tests/Hms.Server.UnitTests/DeviceLaneSafetyTests.cs:32,69,90,109,128` and `PhysicalOutputSafetyTests.cs:224`. The plan's "fake registry (nếu có)" check finds nothing to change.
  - **Callers:** `MqttDeviceCommandTransport.cs:57-71` (register branch), `DeviceLinkHandlers.cs:49,75` (`Find`/`Remove`), `:111` (`TakeExpired`), DI at `DependencyInjection.cs:38`.
  - **Note:** `Register` and `Remove` do not take `_readGate` (`DeviceLinkContracts.cs:279-280,306-307`). That is safe for the write check, because only inserts that check capacity need the lock, but the plan should state it.
- **`OutputCommandPolicy` helper extraction**
  - **External callers:** `HmsIdfCommandProtocolAdapter.cs:19,59` and `MqttLinkOptions.cs:31` (the `DefaultMaxPulseMs` constant), plus tests in `PhysicalOutputSafetyTests.cs:47-89`. The private helpers are used only at `OutputCommandPolicy.cs:36-38,44-46`, so the extraction cannot break callers. An `internal` class works because both policies are in `Hms.Server.Application`.
  - **Missed by the plan:** the three helpers cover integers and booleans only. `ConfigCommandPolicy` also needs string and string-array readers that accept both `JsonElement` (HTTP path; the controller binds `IReadOnlyDictionary<string, object?>`, `DeviceCommandsController.cs:153`) and CLR values (tests pass `string`/`List<string>`, as `PhysicalOutputSafetyTests.cs:76-77` does for ints). Without them, "chuỗi số không được coi là số" and "không đổi kiểu" will be implemented differently in each policy.
- **`runDeviceCommand` reuse**
  - **Callers:** `useDeviceReads.ts:50`, `useDeviceCommand.ts:109` (modal, via `useDeviceCommand` at `DeviceCommandModal.vue:37`) and `DeviceOutputsPanel.vue:124`.
  - **Missed by the plan:** `DeviceOutputsPanel.vue:170-219` already contains the write-outcome classifier, so `useDeviceWrite` would be a second, divergent copy (Finding 2).
- **`DeviceReadCard` slot props (`refresh`)**
  - **Consumers of the slot:** `DeviceInfoTimeCard.vue:57`, `DevicePowerAdcCard.vue:92` and `DeviceNetworkCard.vue:52`. All destructure `{ states, stale, now }`, so adding a prop is source-compatible.
  - **Tests:** `reads/DeviceReadCard.test.ts:25` and `reads/DeviceReadCards.test.ts`.
  - **Missed by the plan:** `DeviceNetworkCard.vue` gets the new prop without using it, which is harmless. The behavioural contract of `read()` is the real gap (Finding 4).
- **`PANEL_ONLY_ACTIONS`**
  - **Definition and use:** `DeviceCommandModal.vue:67` and `:70`. It is the only consumer, and it is a module-level constant, not exported.
  - **Existing test:** `DeviceCommandModal.test.ts:171` (DO actions). Nothing else references it, and nothing in tests or the frontend references the 6 actions today.
  - **Note:** this is a UI filter only; any client can still `POST` the 6 actions, which is acceptable because the server applies the same policy.
