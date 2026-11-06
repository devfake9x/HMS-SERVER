# Red team: failure-mode analysis of plan 261106-2033-remote-config-system-adc

Reviewer: code-reviewer, acting as failure-mode analyst (with Fact Checker and Contract Verifier roles). Date: 2026-11-06.
Scope: `plan.md` and phases 01–03, checked against the codebase in worktree `mqtt-device-connection-36f705` and against the pinned spec export `mqtt-api-83e93d77e.md` (`spec Lnnn` = line in that export). I did not open any firmware source. I did not contact any device, broker or running API.

Ten findings: 1 Critical, 3 High, 6 Medium. A fact-check table and a contract-consumer inventory follow the findings.

---

## Finding 1: The live tool stops without reverting in exactly the cases where the gateway most likely did change
- **Severity:** Critical
- **Location:** Phase 3, section "Tool kiểm live" (bullet "Dừng khi"), section "Kiểm live" (bullet "Trạng thái cuối"), and section "Risk Assessment" (hostname/MQTT session and key-rotation bullets)
- **Flaw:**
  1. The tool stops when a result is `unknown` or when a read-back does not match. It runs the revert only when the previous step was confirmed by read-back. R4 says that no reply does not mean the command did not run, and that the client should read state first. The tool never reads back after `unknown`, and never reverts after a mismatch. So the two outcomes where the device most likely did change (the reply was lost after the handler ran, or the device stored a normalised or deferred value) both end the run with the test value still on the gateway.
  2. `set_hostname` returns `applied_immediately` and `reboot_recommended` (spec L197). Suppose the device returns `applied_immediately:false`. `get_hostname` can then still return the old runtime name, which counts as a mismatch, so the tool stops. The stored `<gốc>-t` becomes active at the next reboot, possibly weeks later.
  3. The plan's main hostname risk is that the change restarts the MQTT session (this is a spec gap). That leads exactly to: reply lost → `DEVICE_RESPONSE_TIMEOUT` → `unknown` → tool stops. The only remedy is manual ("chờ thiết bị kết nối lại, đặt về hostname gốc"). The tool has no restore mode. "In hướng dẫn khôi phục" is just text.
  4. The command key was rotated mid-session twice on 2026-11-06 (Phase 3 Risk Assessment). If that happens between a set and its revert, the device drops the revert and every later read without a reply. The printed recovery steps cannot run until someone re-imports the key, and the plan does not say so.
  5. "Đọc lại toàn bộ, so với file giá trị gốc; phải trùng hết" cannot pass as written. In `get_time`, `epoch`, `datetime` and `last_sync` always change, and `sync_ntp` moves `last_sync` for good. The set of fields to compare is never defined, so nobody can tell "restored" apart from "drifted".
  6. Step 7 sends two `sync_ntp` "liền nhau". That conflicts with the scenario rule "mỗi bước chờ kết quả rồi mới sang bước sau". Reads on this gateway finish in about 1 s, so two sequential POSTs will not hit `DEVICE_WRITE_BUSY`. The test fails, or the second command runs on the device and the sniffer count no longer matches.
- **Failure scenario:** In step 2, the tool sends `set_hostname "maitech-rtu-t"`. The firmware applies it and restarts its MQTT client, so the reply is lost. About 30 s later the sweep records `DEVICE_RESPONSE_TIMEOUT`. The tool classifies `unknown`, stops and prints instructions. The lab gateway keeps `maitech-rtu-t`, with whatever MQTT identity the firmware derives from it. A second scenario: the device echoes `UTC0` back in another form. The TZ format is a listed spec gap. The tool sees a mismatch and stops with the gateway on UTC.
- **Evidence:**
  - Phase 3 lines 45–47: "Dừng khi: … lệch giá trị mong đợi, hoặc kết quả `unknown` … Chỉ chạy lệnh 'đặt lại' khi bước trước đã được đọc lại xác nhận".
  - Phase 3 line 60 ("phải trùng hết"), lines 104–107, lines 112–117.
  - Phase 3 line 32 vs line 44.
  - spec L12 (R4); spec L197 (`applied_immediately, reboot_recommended`).
  - `DeviceLinkHandlers.cs:117-124`: timeout detail "Read device state before retrying".
  - `plans/261106-1040-monitoring-read-actions/reports/live-verification.md:10` ("about 1 s each").
  - `tools/device-read-check.js:84`: the earlier cap test used `Promise.all`.
- **Suggested fix:**
  - Replace "stop" with a recovery state machine. After `unknown`, read back (retrying), and revert if the value is not the original. After a mismatch following `Executed`, always revert to the original. Stop only the forward steps.
  - Add `--restore <original.json>`: it writes only the fields that differ, then verifies each one.
  - Before each step, check both the test value and the original against `ConfigCommandPolicy`.
  - Treat `applied_immediately:false` as pending: revert immediately.
  - Run a key check (`system.get_info`) before each revert.
  - Define the comparison set: hostname; timezone; `ntp_mode` + `ntp_servers`; for every channel, name, type and enabled.
  - Send step 7 as concurrent POSTs.

## Finding 2: `adc.set_channel {ai,name}` may reset `type`/`enabled` on the device, and the server can never send them back
- **Severity:** High
- **Location:** Phase 1, section "Kiểm payload" (row `adc.set_channel`); plan.md sections "Ngoài phạm vi" (ADC `type`) and "Spec gap"; Phase 3, scenario step 6
- **Flaw:** The spec gives `adc.set_channel` an input of `{ai:1..16, name?:string, type?:string, enabled?:bool}` and a result of "Cấu hình channel đã cập nhật". It does not say whether omitted fields are kept or set to defaults.
  - The plan sends exactly `{ai, name}` (`HasExactly`) and bans `type` (editing `type` is out of scope).
  - The tool and the UI compare only the renamed value.
  - The spec-gap list does not include merge semantics.

  If the firmware rebuilds the channel from the payload, the rename silently changes the channel's measurement type or disables it. The revert path cannot repair this, because the server policy refuses `type`.
- **Failure scenario:** On the lab gateway, AI-1 is `"4-20mA"`. The tool renames it to `"AI-1-t"`, and the firmware writes a default `type`. Read-back: the name matches, so the step shows "Đã ghi và xác nhận". The tool renames it back, the name matches again, and the tool reports success. AI-1 now scales its 4–20 mA loop as some other type, and no field the plan compares shows it.
- **Evidence:**
  - spec L227–L228 (`adc.set_channel` / `adc.toggle_channel` rows).
  - Phase 1 line 37 (`{ai, name}`).
  - plan.md line 96 ("sửa `type` … ngoài phạm vi"); plan.md lines 99–105 (gap list has no merge semantics).
  - `frontend/src/features/devices/components/reads/fixtures/lab-gateway-replies.json` → `adc.get_channels`: 8 channels, all `"type":"4-20mA","enabled":true`.
  - Phase 3 lines 41–43 (only the renamed value is checked).
- **Suggested fix:**
  - Add "set_channel merge semantics for omitted fields" to the spec gaps, and get the firmware team's answer before the live run.
  - Until then, the tool and the UI compare the full row of every channel (`name`, `type`, `enabled`) before and after. On any unexpected difference they stop and alert.
  - Run the first live rename on one channel, with this full-row diff in place.

## Finding 3: An "accepted/pending" reply frees the write lane exactly while the device is known to be mid-write
- **Severity:** High
- **Location:** Phase 1, section "Lane ghi" (bullet "Nhả slot"); plan.md "Rủi ro còn lại" (bullet "Job vẫn chạy sau khi slot đã nhả"); Phase 2, sections "useDeviceWrite", "Trong lúc ghi" and "Đổi thiết bị"
- **Flaw:**
  - The correlator calls `pending.Remove` on every verified reply. That happens before `ToResult` classifies the reply as `Acknowledged`. The spec calls such a reply "đã nhận và còn chạy". So the lane opens in the one state where the device is known to be running a write, and R4 asks for a read before the next write.
  - The guards left are all weak:
    - the UI lock covers only "thẻ đó", so the other card's write controls stay enabled;
    - `cancel()`, `onScopeDispose` and a device switch all abort the job poll and unlock the form;
    - other tabs, other users and the tool are not covered at all.
  - Nothing on the server ever records the job's result. The audit row stays `Acknowledged` forever: `FailUnfinished` only touches `Published`/`IN_FLIGHT` rows. So the audit cannot say whether a configuration change actually happened.
- **Failure scenario:** User A renames an ADC channel, and the device replies `pending` after its executor wait. A switches to another device, which aborts the poll. A comes back, or user B arrives, and sends `adc.toggle_channel` or a second rename on the same channel. The lane is free, so it is published. A second ADC write now runs while the first job is still in progress, and the final state depends on their order. A's job result is never read, and the audit shows "Acknowledged" for A's command with no end state.
- **Evidence:**
  - `src/Hms.Server.Application/DeviceLink/DeviceLinkHandlers.cs:75`: `if (!pending.Remove(deviceId, id))`, before `:78` `ToResult`.
  - `DeviceLinkHandlers.cs:146-152`: `Acknowledged` / `DEVICE_ACCEPTED`.
  - `src/Hms.Server.Infrastructure/Persistence/EfCommandAuditStore.cs:44-60`: `FailUnfinished` filters `published || inFlight` only.
  - spec L176: "Đây là **đã nhận và còn chạy**"; spec L182: "Job result không trả lại toàn bộ body … đọc trạng thái domain".
  - plan.md line 111; Phase 2 lines 40–41, 71 and 79.
- **Suggested fix:**
  - When a reply is `Acknowledged` and carries an `operation_id`, keep the device's write slot as a "job hold" keyed by (device, operation_id) instead of removing it.
  - Release the hold when the correlator sees a `system.get_operations` reply for that id with `terminal` or `unknown`, or when a bounded job deadline passes. At release, write the job outcome to the original audit row.
  - While the hold is active, refuse new writes with `DEVICE_WRITE_BUSY` and the detail "job đang chạy".
  - At minimum, lock both cards' write controls during any write on the page.

## Finding 4: The `useDeviceWrite` outcome table is closed and incomplete; there is no default of "unknown + read back", and the 403 path does not exist
- **Severity:** High
- **Location:** Phase 2, section "Phân loại kết quả" and section "Kết quả" (`not_sent` messages); plan.md "Acceptance → Quyền"; Phase 3, tool (no classification defined)
- **Flaw:** The plan lists five kinds. Several outcomes the server really produces fall into none of them:
  - **Permission denied is HTTP 200, not 403.** The server returns status `Failed`, code `DEVICE_PERMISSION_REQUIRED`. `certainlyNotSent` is false for it (it covers only `ContractUnavailable`, `TransportNotConfigured` and the three never-sent codes), and it is not an HTTP 4xx. So the "HTTP 403 → Thiếu quyền" mapping can never fire.
  - **Dispatch codes that can follow a real publish:** `IDEMPOTENCY_OUTCOME_UNKNOWN`, `IDEMPOTENCY_IN_PROGRESS` and `KEY_GENERATION_FENCE`. The handler comment says "A crashed owner may have reached the transport". `runDeviceCommand` returns `phase:'not_sent'` for every non-`Published` dispatch, which invites mapping them to `not_sent` with no read-back.
  - **`Failed` + `OUTCOME_UNKNOWN_AFTER_RESTART`**, written at startup: not classified.
  - **HTTP 5xx or a network error on the POST.** These can come after the publish, for example when the audit write fails after the broker accepted the message. The plan names only 4xx.
  - **`Acknowledged` without `operation_id`.** The server sets detail to null in that case. Not classified.
  - **Phase 3 tool:** no classification is defined. The existing tool returns as soon as the dispatch is not `Published`, so `MQTT_PUBLISH_UNCONFIRMED` (status `Failed`) would count as "not sent" if that code is copied.

  The DO panel already follows the rule this table needs: "Only a refusal that proves nothing was published unlocks the channel at once; anything else … reads the state back".
- **Failure scenario:** The API restarts while the UI is polling a `set_timezone` (`dotnet watch` during the live session). The poll returns `Failed/OUTCOME_UNKNOWN_AFTER_RESTART`. The implementer's switch has no case for it, so it ends as a generic error with no read-back. The card keeps showing `ICT-7` although the device is on `UTC0`, and the user tries again.

  Separately, the phase 1 integration test "POST set_hostname trả 403" can only pass if the API contract changes. That change would also break the DO panel, which reads `status < 500` as "not sent".
- **Evidence:**
  - `src/Hms.Server.Api/Controllers/DeviceCommandsController.cs:47`: `return Ok(await handler.HandleAsync(command, actor, cancellationToken));`.
  - `src/Hms.Server.Application/Commands/SendDeviceCommandHandler.cs:103-106`, `:146-157`.
  - `src/Hms.Server.Domain/Authorization/DeviceAccessPolicy.cs:23` (`"DEVICE_PERMISSION_REQUIRED"`).
  - `src/Hms.Server.Api/Program.cs:162` (`OUTCOME_UNKNOWN_AFTER_RESTART`).
  - `frontend/src/core/commands/useDeviceCommand.ts:39-42`, `:78`.
  - `DeviceLinkHandlers.cs:150-152`.
  - `frontend/src/features/devices/components/DeviceOutputsPanel.vue:170-212`.
  - `tools/device-read-check.js:55`: `if (dispatch.status !== 'Published') return …`.
  - Phase 2 lines 28–32 and 78; plan.md line 75; Phase 1 line 89.
- **Suggested fix:**
  - Define `not_sent` as: `certainlyNotSent(dispatch)`, or the POST threw a 4xx, or the dispatch code is in an explicit allowlist of refusals made before the audit reservation (`DEVICE_PERMISSION_REQUIRED`, `DEVICE_NOT_FOUND`, `CATALOG_STALE`, …).
  - Map everything else to `unknown` and read back. That includes unknown codes, 5xx, network errors, `IDEMPOTENCY_*`, `OUTCOME_UNKNOWN_AFTER_RESTART` and `Acknowledged` without an id.
  - Map `DEVICE_PERMISSION_REQUIRED` to "Thiếu quyền".
  - Rewrite the 403 assertions to expect 200 + `DEVICE_PERMISSION_REQUIRED` with no publish.
  - Make the tool use the same classification.

## Finding 5: The read-back through `refresh → reads.read` silently does nothing when the card is already reading, and "Đọc"/"Dừng" stay active during a write
- **Severity:** Medium
- **Location:** Phase 2, section "DeviceReadCard.vue" (bullet "Slot"), section "Luồng chung" (bullets "Trong lúc ghi" and "Kết quả")
- **Flaw:**
  - `read()` returns at once while `reading` is true, and its `Promise<void>` gives no sign that it was skipped.
  - The card's "Đọc" button is disabled only by `reads.reading`, and the plan locks only the write controls.
  - "Dừng" aborts any read, including the read-back, and sets the step to `idle`.
  - The result logic compares against `states[...]`, but nothing ties those states to the moment the write finished (`readAt`).
- **Failure scenario:** The user confirms a hostname change. While waiting, they click "Đọc" to check the time. The manual sequence reads `get_hostname` before the device has applied the write. The write then finishes and calls `refresh`, which does nothing because a read is running. The comparison sees the old hostname and shows "Thiết bị báo đã ghi nhưng đọc lại ra maitech-rtu", which is false, and the user submits again. The other path: the user presses "Dừng" during the read-back, and the result shows neither confirmation nor mismatch.
- **Evidence:**
  - `frontend/src/core/commands/useDeviceReads.ts:93` (`if (reading.value) return`), `:110` (aborted → `idle`), `:126-130` (`reset`).
  - `frontend/src/features/devices/components/reads/DeviceReadCard.vue:55-77` (Đọc/Dừng depend only on `reads.reading`), `:95` (slot).
  - Phase 2 lines 44 and 71–77.
- **Suggested fix:**
  - Disable "Đọc" and "Dừng" while `writing`.
  - Make `refresh` queue behind a read in progress, or await it and then run its own steps, and return the resulting states.
  - Accept read-back states only when `readAt` is later than the write's completion.
  - Show "chưa đọc lại được" when the read-back is aborted or idle.

## Finding 6: Job polling needs `sys.info_view` and a free read slot; the plan checks neither and does not say how a failed poll is handled
- **Severity:** Medium
- **Location:** Phase 2, section "useDeviceWrite" (bullet "Job còn chạy") and section "Luồng chung" (bullet "Chỉ bật khi")
- **Flaw:**
  - `system.get_operations` needs `sys.info_view`. That permission is separate from `adc.config_edit`, `adc.enable`, `sys.time_edit` and `sys.hostname_edit`.
  - The write controls are enabled when the write action is in `allowedCommands`; nothing checks `system.get_operations`.
  - Every poll is a class R command that goes through `TryRegisterRead`, where one user gets 2 of the 4 slots. `DEVICE_READ_BUSY` is therefore likely when another card of the same user is reading.
  - The plan maps only `state:unknown` and the 60 s limit to `unknown`. It does not say what happens when the poll command itself is refused (`DEVICE_PERMISSION_REQUIRED`, `DEVICE_READ_BUSY`, `CATALOG_STALE`).
- **Failure scenario:** An ADC operator role has `{adc.view, adc.config_edit}` and no `sys.info_view`. The operator renames a channel and the device replies `pending`. The first poll returns `Failed/DEVICE_PERMISSION_REQUIRED`. Treated as "job unknown", it triggers an immediate read-back while the job is still running. The card shows the old name with "Chưa rõ kết quả". The operator renames again; under Finding 3 the lane is free, so a second write runs during the first job.
- **Evidence:**
  - `src/Hms.Server.Infrastructure/Commands/command-catalog.json:978-987` (`get_operations` → `"required_perm": "sys.info_view"`).
  - spec L585.
  - `src/Hms.Server.Infrastructure/Mqtt/MqttDeviceCommandTransport.cs:59-66` (per-user cap `perDevice / 2`); `src/Hms.Server.Infrastructure/Mqtt/MqttLinkOptions.cs:36` (`MaxPendingReadsPerDevice = 4`).
  - Phase 2 lines 33–37 and 69.
- **Suggested fix:**
  - Do one of these:
    - enable a write control only when `system.get_operations` is also in `allowedCommands`;
    - track jobs on the server (Finding 3), which removes the permission dependency.
  - Handle refusals of the poll command explicitly:
    - on BUSY, back off and retry within the limit;
    - on a permission refusal, show "không theo dõi được job" and delay the read-back.
  - Add tests for these cases.

## Finding 7: Releasing the write slot at `Deadline` does not bound when the device actually runs the write
- **Severity:** Medium
- **Location:** Phase 1, section "Lane ghi" (bullet "Nhả slot") and "Risk Assessment" ("khoá ghi tối đa 30 s"); plan.md "Rủi ro còn lại" (bullet "Lane ghi chỉ trong bộ nhớ")
- **Flaw:** The plan holds the slot until `Deadline` because of R4, but several paths let the device run, or reply, after the slot is gone:
  1. **Late executor reply.** `Deadline` = publish time + `ResponseTimeoutSeconds` (30 s). The device can dequeue the command up to `ts + ttl` (20 s) by its own clock. The executor sends its `pending` reply only after it has run for a while; the only figure the spec gives is about 10 s (L480, RTU). A reply that lands after the sweep is `Ignored` (`pending.Find` → null), so the `operation_id` is lost. The UI then reads back while the job is still running and sees the old value, with the slot already free.
  2. **Clock.** The plan itself changes NTP settings, and the lab gateway has no RTC (`rtc_available:false`). A device clock that lags extends its TTL window. A clock that is invalid (after a reboot without NTP) disables the age check entirely (spec L81).
  3. **Client-supplied `RequestedAt`.** The server accepts any value up to now + 60 s and signs it as `ts`. That stretches the device-side window to about now + 80 s, while the slot is released at now + 30 s.
  4. **Restart.** The registry is an in-memory singleton. `FailUnfinished` marks rows as unknown but does not re-create any hold, so the first write after a restart is accepted while the pre-restart write can still be dequeued.
  5. **"Tối đa 30 s" holds only by default.** `ResponseTimeoutSeconds` can be set from 21 to 600 s, while the UI's poll limit is fixed at 40 s. During a broker outage the sweep runs only every `ReconnectDelaySeconds` (up to 300 s) plus the 10 s connect timeout.
- **Failure scenario:**
  - A rename is dequeued near its TTL edge on a gateway whose clock lags about 2 s, and the executor replies `pending` about 10 s later. The reply arrives after the sweep and is ignored. The UI shows `DEVICE_RESPONSE_TIMEOUT` → "unknown" and reads back the old name, so the user renames again. The device then has two renames in flight, and nobody ever learns the first job's result.
  - With `ResponseTimeoutSeconds=60`, the UI reports "unknown" at 40 s while the server keeps refusing writes with `DEVICE_WRITE_BUSY` for another 20 s.
- **Evidence:**
  - `MqttDeviceCommandTransport.cs:56` (`now.AddSeconds(options.Value.ResponseTimeoutSeconds)`).
  - `DeviceLinkHandlers.cs:49-51` (unknown id → `Ignored`).
  - `DeviceCommandsController.cs:41` (`request.RequestedAt ?? DateTimeOffset.UtcNow`).
  - `SendDeviceCommandHandler.cs:131-136`.
  - `src/Hms.Server.Infrastructure/Commands/HmsIdfCommandProtocolAdapter.cs:73-74` (`ts` = `RequestedAt`).
  - `MqttLinkOptions.cs:27`, `:50-54`; `frontend/src/core/commands/useDeviceCommand.ts:6` (`POLL_LIMIT_MS = 40_000`).
  - `src/Hms.Server.Infrastructure/Mqtt/MqttDeviceLinkService.cs:134-136`, `:174`.
  - `src/Hms.Server.Infrastructure/DependencyInjection.cs:38`; `Program.cs:158-164`.
  - spec L81, L176, L480.
  - Lab fixture `system.get_time` (`"rtc_available":false`).
  - Phase 1 line 133.
- **Suggested fix:**
  - Keep a short "recently swept" map. When a late reply arrives for a swept command, record it: upgrade the audit row from timeout to `Acknowledged`/`Executed`, save the reply, and re-hold the lane if the reply is a pending job.
  - For W/C commands, ignore any client `RequestedAt` and use the server's time.
  - On startup, re-seed write holds from audit rows of W/C class published within `ResponseTimeoutSeconds`.
  - Take the client poll limit from server configuration, or validate `ResponseTimeoutSeconds` so that it stays below the poll limit minus the sweep interval.
  - Correct the "30 s" claim.

## Finding 8: An edit is allowed from any earlier successful read, with no age limit, so concurrent editors overwrite each other with stale "old" values
- **Severity:** Medium
- **Location:** Phase 2, section "Luồng chung" (bullets "Chỉ bật khi" and "Hộp xác nhận"); plan.md "Ngoài phạm vi" (pushing new values to other viewers)
- **Flaw:**
  - Edit controls are enabled when the "bước đọc tương ứng gần nhất là ok", with no limit on how old that read is.
  - The confirm dialog shows that stored value as "giá trị cũ".
  - The server already sends `command_result` (type, action, status) to every viewer when someone writes to the device. The frontend has no consumer for it, and the plan explicitly decides not to update other viewers.
- **Failure scenario:**
  - User A reads at 10:00 and user B reads at 10:01. A renames AI-1 to "Bơm 1". At 10:20, B renames AI-1 based on the stale "AI-1": B's dialog shows "AI-1 → Bồn 2", and A's change is overwritten without either of them knowing.
  - The same happens with the NTP list: B adds a server to a stale list and drops the server A had set.
- **Evidence:**
  - `DeviceLinkHandlers.cs:165-193` (`command_result` sent to the device's realtime group, payload `type/action/status/code`).
  - Grep over `frontend/src` finds no `command_result` consumer.
  - `DeviceReadCard.vue:12-22` (no realtime input); `DeviceReadCard.vue:39-41` (a `now` clock exists, used only for display).
  - Phase 2 lines 69–70; plan.md line 97.
- **Suggested fix:**
  - Re-read the target step right before opening the confirm dialog (one read), so "giá trị cũ" is fresh.
  - When another session's `command_result` arrives for a matching write action on this device, mark the related read states stale ("đọc lại trước khi sửa").
  - Put a maximum age on the "read ok" condition.

## Finding 9: The simulator plan cannot run the UI flow or any of the failure modes above before the real gateway is touched
- **Severity:** Medium
- **Location:** Phase 3, section "Simulator"; Success Criteria "UI và tool chạy đúng với simulator, gồm đường job còn chạy"
- **Flaw:**
  - Today the simulator answers only `control.get_status`, `control.set_output`, `control.pulse`, `system.ping` and `system.get_info`. Every other action gets `unknown_action`.
  - The plan adds only `get_hostname`, `get_time`, `get_channels`, the 6 writes and `get_operations`. The ADC card also reads `adc.get_config` (source of `available`, which decides whether controls show) and `adc.get_values`, and the plan uses `get_values` as an ADC read-back step. So on the simulator the ADC read-back always reports an error, and control visibility is undefined.
  - The simulator replies instantly. `SIM_ADC_JOB` returns `pending` at once rather than after about 10 s.
  - The simulator has no TTL/`expired` check, no dedup, no `LANE_FULL`, no normalised echo, no `applied_immediately:false`, and no way to reset `type` on `set_channel`. So Findings 1, 2, 3 and 7 are first exercised on the lab gateway.
- **Failure scenario:** Simulator testing passes the happy path but shows ADC read-back errors, so someone works around them by hand. The first `pending` job, the first late reply and the first normalised echo then all happen on `gateway_870c`, where the tool's stop-without-revert behaviour (Finding 1) leaves the gateway changed.
- **Evidence:**
  - `tools/mqtt-broker/spec-device-simulator.js:66-98` (handler table), `:142` (`{ error: 'unknown_action' }`).
  - `frontend/src/features/devices/components/reads/DevicePowerAdcCard.vue` step list (`adc.get_config`, `adc.get_channels`, `adc.get_values`).
  - Phase 2 lines 65–66; Phase 3 lines 22–25 and 103.
- **Suggested fix:**
  - Start the simulator from the anonymised lab fixture (`lab-gateway-replies.json`) and answer every card read.
  - Add switches:
    - `SIM_JOB_DELAY_MS` (default 10000);
    - `SIM_LATE_REPLY_MS`;
    - `SIM_NORMALIZE_TZ`;
    - `SIM_HOSTNAME_DEFERRED` (`applied_immediately:false`);
    - `SIM_SET_CHANNEL_RESETS_TYPE`;
    - `SIM_SILENT` for writes.
  - Run the tool's recovery paths against these switches before the live run.

## Finding 10: The TDD "intended assertion" list names an assertion that does not exist, and misses the one that will actually fail
- **Severity:** Medium
- **Location:** Phase 1, section "Tests first" item 1 ("assertion có chủ đích phải sửa: số lệnh mở cũ (28 → 34)") and item 6; plan.md "Phases" (the rule about changing old tests)
- **Flaw:**
  - No test asserts 28. The test that breaks when `ConfigWriteActions` is opened is a set-equality check: `Pinned_catalog_opens_only_the_closed_reads_and_the_two_relay_outputs` asserts that the enabled set equals `EnabledActions ∪ PhysicalOutputActions`.
  - The plan's own rule says old tests may change only at the assertions the phase lists. The implementer must therefore either break the rule or weaken a test the plan never mentions.
  - Item 6 asks for `POST set_hostname` to return 403. As Finding 4 shows, that test can only pass if the API contract changes; the existing unit tests already expect 200 with `DEVICE_PERMISSION_REQUIRED`.
- **Failure scenario:** At step 6 ("Sửa assertion có chủ đích … Chạy toàn bộ test"), the catalog test fails at an unlisted assertion and the 403 integration test cannot go green. The implementer then either loosens the set-equality test, which removes the guard that "a typo cannot silently open nothing", or changes the POST status code, which breaks the DO panel's "4xx = not sent" logic.
- **Evidence:**
  - `tests/Hms.Server.UnitTests/CatalogTests.cs:139-150` (expected = `EnabledActions.Concat(PhysicalOutputActions)`).
  - Grep for `\b28\b` in the test projects finds only an unrelated `"28.4"` (`MaitechRtuDeviceIntegrationTests.cs:144`).
  - `tests/Hms.Server.UnitTests/DeviceCommandAuthorizationTests.cs:48`, `PhysicalOutputSafetyTests.cs:287` (permission denial asserted as a result code).
  - Phase 1 lines 69 and 89; plan.md lines 59–60.
- **Suggested fix:**
  - List the set-equality assertion explicitly: extend `expected` with `ConfigWriteActions` and rename the test.
  - Add an assertion that every `ConfigWriteActions` row is class W or C.
  - Change item 6 to expect HTTP 200 + `DEVICE_PERMISSION_REQUIRED` with no publish.

---

## Verification Results

### Fact Checker

**Phase 1**

| # | Claim | Result |
|---|---|---|
| 1 | `TryInteger`, `IsBoolean`, `HasExactly` are private in `OutputCommandPolicy` | VERIFIED `OutputCommandPolicy.cs:57,61,87` |
| 2 | Policy runs in `HmsIdfCommandProtocolAdapter.PrepareAsync`, `Fail(plan, code)`, before the key lookup | VERIFIED `HmsIdfCommandProtocolAdapter.cs:59-65,102-104` |
| 3 | `ToDefinition` opens only R via `EnabledActions` and B via `PhysicalOutputActions` | VERIFIED `PinnedCommandCatalog.cs:221-227` |
| 4 | 26 R + 2 B open today (28) | VERIFIED `PinnedCommandCatalog.cs:50-77` (26 entries), `:86-91` (2) |
| 5 | The 6 actions exist with the stated class and permission | VERIFIED `command-catalog.json` rows (e.g. `:89` set_channel W/adc.config_edit, `:1024` set_hostname W/sys.hostname_edit; set_ntp_servers C, sync_ntp C, set_timezone W, toggle_channel C/adc.enable) |
| 6 | `ToResult` returns `Acknowledged` + `operation_id=` for accepted/pending | VERIFIED `DeviceLinkHandlers.cs:146-152` |
| 7 | `command_result` is sent for classes other than R, outcome only | VERIFIED `DeviceLinkHandlers.cs:172-185` |
| 8 | GET reply re-checks the permission (`MayReadReply`) | VERIFIED `DeviceCommandsController.cs:102,118-127` |
| 9 | TTL cap 20 s for non-physical commands | VERIFIED `SendDeviceCommandHandler.cs:20,123-125` |
| 10 | "assertion có chủ đích: 28 → 34" | FAILED: no such assertion; the breaking one is `CatalogTests.cs:143-147` |
| 11 | "POST set_hostname trả 403" | FAILED: `DeviceCommandsController.cs:47` returns 200; denial is `Failed/DEVICE_PERMISSION_REQUIRED` (`SendDeviceCommandHandler.cs:103-106`) |
| 12 | "Thiết bị bị khoá ghi tối đa 30 s" | FAILED (default only): `MqttLinkOptions.cs:27,50` (21–600 s); sweep cadence `MqttDeviceLinkService.cs:134-136` |
| 13 | Read lane has a lock that the write lane can share | VERIFIED `DeviceLinkContracts.cs:277,290` (`_readGate`) |

**Phase 2**

| # | Claim | Result |
|---|---|---|
| 1 | `runDeviceCommand` = POST, then poll for 40 s | VERIFIED `useDeviceCommand.ts:6,69-93` |
| 2 | `certainlyNotSent` is true for a `ContractUnavailable` `DEVICE_WRITE_BUSY` | VERIFIED `useDeviceCommand.ts:39-42` |
| 3 | `DeviceReadCard` slot can carry `refresh` that calls `reads.read` | VERIFIED `DeviceReadCard.vue:95`. Caveat: `read` does nothing while reading (`useDeviceReads.ts:93`), see Finding 5 |
| 4 | `PANEL_ONLY_ACTIONS` exists in `DeviceCommandModal.vue` | VERIFIED `DeviceCommandModal.vue:67` |
| 5 | `get_operations` reply uses camelCase `operationId`, `resultSuccess` | VERIFIED spec L182 |
| 6 | `get_time` carries `ntp_servers`/`ntp_mode` | VERIFIED on the lab fixture (`lab-gateway-replies.json`: `"ntp_servers":[]`, `"ntp_mode":"auto"`). Spec L198 only says "cấu hình NTP" |
| 7 | "HTTP 403 → Thiếu quyền" | FAILED: permission denial is HTTP 200 (see Phase 1 row 11) |
| 8 | Card and test files to modify exist | VERIFIED `reads/DeviceInfoTimeCard.vue`, `reads/DevicePowerAdcCard.vue`, `reads/DeviceReadCards.test.ts`, `DeviceCommandModal.test.ts` |
| 9 | Write controls only need the write action in `allowedCommands` | UNSAFE: job poll needs `sys.info_view` (`command-catalog.json:978-987`), see Finding 6 |

**Phase 3**

| # | Claim | Result |
|---|---|---|
| 1 | Simulator exists and can be extended | VERIFIED `tools/mqtt-broker/spec-device-simulator.js` (handlers `:66-98`). PARTIAL scope, see Finding 9 |
| 2 | Credentials come from `tools/tool-env.js` | VERIFIED `tool-env.js:21-22` (`HMS_ADMIN_EMAIL`/`HMS_ADMIN_PASSWORD`) |
| 3 | `HMS_API_BASE` comes from `tool-env.js` | FAILED: it is read in `tools/device-read-check.js:22`, not in `tool-env.js` |
| 4 | `tools/device-reads/` is gitignored | VERIFIED `.gitignore:18` |
| 5 | The docs to update exist | VERIFIED `docs/architecture-mqtt-device-link.md`, `docs/architecture-device-mqtt-api-catalog.md`, `docs/journals/` |
| 6 | Lab values pass the planned policy (hostname, TZ `ICT-7`, NTP auto, ADC names) | VERIFIED on the fixture (`"maitech-rtu"`, `"ICT-7"`, `"ntp_mode":"auto"`, names `AI-1..8`). UNVERIFIED on the live device today |

### Contract Verifier

**`IPendingDeviceCommandRegistry` (gains `TryRegisterWrite`)**

- **Implementers:** only `InMemoryPendingDeviceCommandRegistry` (`DeviceLinkContracts.cs:274`), registered as a singleton in `DependencyInjection.cs:38`.
- **Test doubles:** none implement the interface. The tests create the real class at `DeviceLaneSafetyTests.cs:32,69,90,109,128`, `DeviceLinkTests.cs:325`, `DeviceOutputStateTests.cs:247` and `PhysicalOutputSafetyTests.cs:224`. Phase 1's "kiểm các fake registry khác" has nothing to update.
- **`Register`:** `MqttDeviceCommandTransport.cs:70` (the else branch, so it also catches D/F if they are ever opened; the write lane covers only W/C). In tests: `DeviceLaneSafetyTests.cs:116`, `DeviceLinkTests.cs:356`, `DeviceOutputStateTests.cs:272`. The test fixtures register W/C plans directly and bypass `TryRegisterWrite`, so the phase 1 item 5 W-reply tests will not exercise the lane.
- **`TryRegisterRead`:** `MqttDeviceCommandTransport.cs:64`.
- **`Find`:** `DeviceLinkHandlers.cs:49` (the correlator: a late reply after the sweep is ignored, see Finding 7). In tests: `DeviceLaneSafetyTests.cs:52`, `PhysicalOutputSafetyTests.cs:235`.
- **`Remove` (frees the slot):**
  - `DeviceLinkHandlers.cs:75`, on any verified reply, including `Acknowledged` (see Finding 3).
  - `MqttDeviceCommandTransport.cs:88`, the never-sent rollback. **The plan's "Nhả slot" list leaves this path out.** The behaviour is correct, but no planned test covers a write whose publish returns `MQTT_NOT_CONNECTED` or `MQTT_RESPONSE_SUBSCRIPTION_MISSING`.
  - In tests: `DeviceLaneSafetyTests.cs:61`.
- **`TakeExpired`:** `DeviceLinkHandlers.cs:111`, inside `SweepExpiredAsync`, which `MqttDeviceLinkService.cs:294` calls every 2 s while connected, or every `ReconnectDelaySeconds` when not (`:134-136`). In tests: `PhysicalOutputSafetyTests.cs:237`.
- **Paths that keep the slot:**
  - A rejected reply (bad signature, or wrong type/action) keeps the entry (`DeviceLinkHandlers.cs:56-73`).
  - An `OperationCanceledException` from `PublishCommandAsync` is not caught (`MqttDeviceCommandTransport.cs:78`), so the entry stays until `Deadline`.

  Both are safe. Neither is in the planned tests.

**`ICommandProtocolAdapter` (gains the `ConfigCommandPolicy` call)**

- **Production:** `HmsIdfCommandProtocolAdapter` only (`DependencyInjection.cs:35`).
- **Test double:** `PublishableTestProtocolAdapter` (`tests/Hms.Server.UnitTests/PublishableTestProtocolAdapter.cs:7`) does not run the policy. Tests that use it cannot prove payload refusal, so phase 1 item 3 must use the real adapter, as `DeviceLaneSafetyTests.Setup` does at `:210`.

**`runDeviceCommand` (reused by `useDeviceWrite` and the job poll)**

- **Callers today:** `useDeviceCommand.ts:109` (used by `DeviceCommandModal.vue:37`), `useDeviceReads.ts:50`, `DeviceOutputsPanel.vue:124`.
- **Signal:** the POST is not given the abort signal (`useDeviceCommand.ts:74-76`). Cancelling stops only the polling. A cancelled write is still published, and the server holds its slot until a reply arrives or the `Deadline` passes.
- **`not_sent` phase:** returned for every non-`Published` dispatch (`:78`). It is broader than `certainlyNotSent`, which is the root of Finding 4.

Status: DONE_WITH_CONCERNS
Summary: Ten findings (1 Critical, 3 High, 6 Medium). The live-test recovery rules are the biggest risk: on an `unknown` result or a mismatch, the tool stops instead of reading back and reverting. The write lane also leaks or misses in-flight writes in several places: `Acknowledged`, late reply, restart, and client `RequestedAt`.
Concerns/Blockers: The `adc.set_channel` merge semantics (whether omitted fields are kept) and the hostname/MQTT session coupling need firmware-team answers before the live run. Neither can be checked from the spec.
