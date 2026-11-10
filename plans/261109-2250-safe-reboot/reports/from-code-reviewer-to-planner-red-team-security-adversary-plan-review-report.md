# Red-team plan review: Security Adversary, safe reboot (`system.reboot`)

- Reviewer: code-reviewer (Security Adversary; Fact Checker + Contract Verifier roles)
- Plan: `plans/261109-2250-safe-reboot/` (plan.md, phase-01..03) + brainstorm `plans/reports/brainstorm-261109-2244-safe-reboot.md`
- Contract: `HMS-IDF/engineering-docs/api/mqtt-api.md` read only via `git show HEAD:` at 9bfc168 (§0, §2, §3, §5, §6.1, §6.4, §7, §10, §11). No firmware source opened. No broker, device or API contacted. No file changed except this report.
- Branch state: `45f5c14`, plan files untracked. A sibling failure-mode report exists in this folder. Overlaps are noted where they occur. This report covers the attacker view.
- Date: 2026-11-09

---

## Finding 1: The send-time rule quietly replaces approved acceptance criterion 2, and the open question does not block the code that encodes the change
- **Severity:** High
- **Location:** plan.md "Quyết định" 10, "Acceptance" → "Quyền", "Open questions" 1, "Rủi ro còn lại" → "Role cũ". Phase 1, "Tests Before" item 9 (allowed-commands role case). Phase 3, "Role bị ảnh hưởng" / Implement step 3.
- **Flaw:** The user-approved criterion 2 says: "Người không phải master-admin, hoặc thiếu `sys.reboot` trên thiết bị, thì bị từ chối." The plan's acceptance swaps it for a weaker rule: missing `sys.reboot` is refused, and a non-master cannot *grant* the code. Under that rule a non-master who holds a role with `sys.reboot` can reboot. OQ1 asks about this, but `blockedBy: []`. Phase 1 test 9 also asserts the weaker model ("role có `sys.reboot` → có `system.reboot`"), so the code and tests are fixed before the user answers. Today no HTTP endpoint creates device roles or memberships, so in practice only a master-admin can reboot. Enforcing the approved rule therefore costs nothing now. The weaker rule goes live without notice on the day role endpoints ship. The role inventory runs in phase 3, after the server change.
- **Failure scenario:** Phase 1 merges with OQ1 unanswered. Later the role and membership endpoints land. A master-admin builds a shift "Operator" role from the vocabulary picker, which lists every firmware code including `sys.reboot`. Every member can now reboot each device they are assigned to. `allowed-commands` shows them the button. The user approved this capability as master-only, and nobody re-reads OQ1.
- **Evidence:**
  - Brainstorm line 94: criterion 2. plan.md Acceptance "Quyền", Decision 10, OQ1 (the plan says it keeps the current model).
  - `src/Hms.Server.Domain/Authorization/DeviceAccessPolicy.cs:37-43`: master → allow; otherwise `access.Permissions.Contains(requiredPermission)`.
  - `src/Hms.Server.Application/Authorization/DeviceAccessResolver.cs:23-35`: the permissions are the membership role's.
  - `DeviceAccessHandlers.cs:57, 106, 166, 324-325, 367-369`: the high-risk check sits on the grant side only.
  - `DeviceCommandsController.cs:67-72`: `allowed-commands` evaluates the same rule.
  - `src/Hms.Server.Application/Commands/DevicePermissionVocabulary.cs:11-12`: the vocabulary includes every firmware code.
  - No role or membership controller exists in `src/Hms.Server.Api/Controllers/`. The handlers are only registered (`DependencyInjection.cs:121-125`).
  - `tests/Hms.Server.IntegrationTests/PersistenceModelTests.cs:535` already models an `Operator` role holding `["device.view","sys.reboot"]`.
- **Suggested fix:**
  - Either make Phase 1 `blockedBy` OQ1, or implement the approved criterion by default: right after the permission check in `SendDeviceCommandHandler`, return `Failed` / `HIGH_RISK_REQUIRES_MASTER_ADMIN` when `definition.Class == Flash && !access.IsMasterAdmin`, and have `allowed-commands` apply the same rule.
  - Relaxing it later is a one-line change, made once the user approves.
  - Restore the brainstorm wording in Acceptance and test 9.
  - Run the role inventory (read-only SQL, as in plan 261106-2033 `affected-roles.md`) before merge, not in phase 3.

## Finding 2: A reboot bypasses the master-admin relay allowlist, and the confirm dialog misstates what happens to relays
- **Severity:** High
- **Location:** plan.md "Quyết định" 10 ("Giống `gpio.do_write`"), OQ2. Phase 2, sections `rebootPlan.ts` (`relaysAfterReboot`, `checkAfterReboot`, `rebootGate`) and Panel (dialog text). Overlaps sibling Finding 3 on rule outputs.
- **Flaw:**
  - `AllowedOutputChannels` is the master-admin interlock between the server and real relays. Empty means none may be driven. The adapter enforces it only for class B. A `system.reboot` sets every non-forced ON relay to OFF (spec §6.4), including channels the master-admin deliberately left out of the list or on devices where the list is empty.
  - The plan compares `sys.reboot` to `gpio.do_write`. Its only relay question (OQ2) is about `gpio.do_view` for *display*. Neither the gate nor the server considers the allowlist.
  - Spec §6.4 also says DO rules `SET_HIGH`/`SET_LOW` fire at the first evaluation after boot. So the dialog's "mọi relay không bị force sẽ TẮT" is false: a rule-driven channel can energize at boot. `checkAfterReboot` would then report that channel as a spec deviation, not as an expected rule action.
- **Failure scenario:** A master-admin leaves DO3 (feeding a pump) out of the allowlist so that no server command can ever switch it. A `sys.reboot` holder reboots, and DO3 drops OFF. A `SET_HIGH` rule turns DO2 ON at the first evaluation. The operator approved a dialog that said "DO3 sẽ TẮT; mọi relay không force sẽ TẮT". The panel then shows "lệch §6.4" for DO2. The relay interlock the server advertises was bypassed through another permission.
- **Evidence:**
  - `src/Hms.Server.Api/Controllers/DeviceLinkController.cs:86-87`: "Master-admin only: this list is the interlock between a command and a real relay."
  - `src/Hms.Server.Application/DeviceLink/DeviceLinkContracts.cs:14-15`: null or empty allows none.
  - `src/Hms.Server.Application/DeviceLink/OutputCommandPolicy.cs:18, 53`.
  - `HmsIdfCommandProtocolAdapter.cs:58-60`: the allowlist is checked only through `OutputCommandPolicy`.
  - `src/Hms.Server.Api/Program.cs:139`: "Left out = no output channel may be driven (fail closed)".
  - Spec 9bfc168 L872-876 (non-forced channels start OFF) and L880 ("Luật SET_HIGH/SET_LOW vẫn bắn ở lần đó").
  - phase-02 dialog text and `checkAfterReboot` ("kênh không force mà vẫn `state:true` → lệch").
- **Suggested fix:** Put these options to the user; this is a policy decision.
  - (a) Before signing, refuse a reboot from a non-master when the last verified `control.get_status` (`IDeviceOutputStateStore`) shows an ON non-forced channel outside `AllowedOutputChannels`, or is missing or stale.
  - (b) Require `gpio.do_write` in addition to `sys.reboot` whenever a non-forced channel is ON.
  - In both cases, fix the dialog text and `checkAfterReboot`: DO rules may drive channels at boot (spec L880), so an ON channel after reboot is "rule or unexpected", not "lệch §6.4".

## Finding 3: The second-reboot gate exists only after a success reply; a `sys.reboot` holder can loop reboots through the untracked paths
- **Severity:** High
- **Location:** plan.md "Quyết định" 6 ("Bắt đầu: reply `success`") and 9, "Ngoài phạm vi" ("Theo dõi khi reply bị mất … server không theo dõi"), "Rủi ro còn lại" → "Reply mất". Phase 1, "Cổng reboot thứ hai" and "Luật payload" (`delay_ms` 0..60000). Sibling Finding 1 covers the failure-mode side; this finding covers deliberate abuse.
- **Flaw:** `IsTracking` is armed only by `Executed`. These outcomes leave no gate:
  - `DEVICE_RESPONSE_TIMEOUT` (R4: the reboot may have run);
  - `Acknowledged`;
  - `MQTT_PUBLISH_UNCONFIRMED`;
  - a reply dropped by the full inbound queue;
  - any server restart.

  With the TTL cut to 10 s, the timeout sweep's lane hold ends at `ExpiresAt` + 2 s = T+12 s, before the 30 s sweep runs. The lane is therefore free the moment the timeout is recorded. The API accepts `delay_ms: 0`, which the plan's own spec gap names as the case where the reply may not leave before the reboot. After `OBSERVED` nothing stops the next reboot either. The only brakes are 10 writes/min and the device throttle, and the plan lists the throttle's survival across reboot as unknown. "Không có đường nào gửi lại" covers automatic resends only.
- **Failure scenario:** A `sys.reboot` session runs a script, or a stolen master-admin cookie does. Each cycle POSTs `system.reboot {delay_ms:0}` with `confirm` = `REBOOT-<mqttDeviceId>`, which any in-scope viewer can read from `GET /link`.
  - Replies die with the reboot: every attempt ends `DEVICE_RESPONSE_TIMEOUT` after 30 s, untracked, lane free, so the script sends again.
  - Replies arrive: the script waits for `OBSERVED` (about 10-20 s) and sends again.

  Either way the gateway stays in a reboot loop, every non-forced relay drops OFF each cycle, and the audit shows a series of individually "valid" commands.
- **Evidence:**
  - plan.md Decisions 6 and 9, "Ngoài phạm vi", "Spec gap" ("Reply trước reboot … nhất là với `delay_ms:0`", "Throttle … có giữ qua reboot không").
  - `DeviceLinkHandlers.cs:83-84`: `Acknowledged` gives only a 60 s hold, with no tracker. `:118-146`: the timeout path, with no tracker.
  - `DeviceLinkContracts.cs:372-375`: hold until `ExpiresAt` + 2 s. `SendDeviceCommandHandler.cs:24`: 10 s. `MqttLinkOptions.cs:27`: 30 s timeout.
  - `MqttDeviceCommandTransport.cs:47`: 10/min.
  - `DeviceLinkController.cs:61-71`: `mqttDeviceId` is returned to any in-scope viewer.
  - Spec L483 (shared 60 s throttle) and L571 (throttle → `UNAUTHORIZED`).
- **Suggested fix:** Arm a per-device reboot hold at publish (`Published` or `PUBLISH_UNCONFIRMED`) until publish + `delay_ms` + 120 s.
  - A success reply upgrades the hold to tracking.
  - Only `DEVICE_REJECTED` or a certain not-sent result clears it.
  - Add a server-side minimum interval of at least 60 s between class F publishes per device, matching the device throttle, whatever the outcome.
  - Narrow `delay_ms` to `{}` only (the UI never sends anything else), or to at least 1000.
  - Because the approved brainstorm starts tracking at the success reply, present this as an additive gate for the user to accept.

## Finding 4: The 10 s TTL does not bound post-reboot replay or redelivery; the live run creates the replay artifact and measures nothing
- **Severity:** High
- **Location:** plan.md "Quyết định" 4 (TTL rationale), "Rủi ro còn lại" → "Phát lại trên broker không ACL", OQ3. Phase 3, "Tiền điều kiện live", "Kịch bản" (sniffer), "Simulator".
- **Flaw:** Decision 4 says the 10 s cap shortens the replay window once dedup is lost. Per the spec it does not, in three ways:
  - **Clock lag.** The device accepts `ts` up to 60 s in its future and expires a command `ttl_ms` after `ts`. A device clock that lags by L ≤ 60 s keeps a signed reboot valid until server time `ts` + 10 s + L, up to 70 s. The server's own TTL comment accounts only for clocks ahead.
  - **Invalid clock.** If the clock is invalid after boot, the age check is skipped and a captured reboot is valid at every boot until SNTP sync.
  - **Redelivery (R10).** With `clean_session` off, the broker can redeliver the old command when the device reconnects. Dedup is gone, so the same signed reboot can run again, with no attacker needed. The spec notes that RTU frames are protected by `if_generation`; reboot has no such guard.

  The plan has no precondition on `clean_session`. The post-reboot reads (`get_info`, `get_status`) do not include `system.get_time`, so OQ3 will be accepted without data. The sniffer captures the signed real reboot on `B/#`, and the plan says nothing about dropping `sig` or deleting the log; the last run kept "Sniffer log in the session scratchpad". The simulator checks neither `ts`/TTL nor dedup and connects with `clean: true`, so no test anywhere can show a post-reboot replay or redelivery loop.
- **Failure scenario:** The gateway restores an RTC time 40 s behind and has `clean_session=false`. It reboots in about 8 s and reconnects. The broker redelivers the unacknowledged reboot, which is still valid by the lagging clock, so the gateway reboots again. This continues until the lag window passes or SNTP corrects the clock. Alternatively, with the clock invalid at boot, anyone holding the sniffer log, or any client on the no-ACL lab broker, replays the reboot whenever retained `connectivity` turns `online`. The plan's "có người ở lab" covers a device that does not come back, not one that keeps coming back and rebooting.
- **Evidence:**
  - Spec 9bfc168 L481 ("Timestamp tương lai quá 60 giây … Khi đồng hồ chưa hợp lệ, age check bỏ qua"), L455 (dedup is in RAM), L414 (R10 redelivery), L1115 (`if_generation` protects RTU only), L1340.
  - `SendDeviceCommandHandler.cs:17-19`: the comment considers only a device clock ahead.
  - `PinnedCommandCatalog.cs:54, 65`: `mqtt.get_config` and `system.get_time` are already open reads.
  - `tools/mqtt-broker/spec-device-simulator.js:207-211` (`clean: true`) and `:232-249` (HMAC, then the handler; no `ts`/TTL/dedup).
  - phase-03 L54, L72 (sniffer on `B/#`). `plans/261106-2033-remote-config-system-adc/reports/live-verification.md:76`.
- **Suggested fix:**
  - Add live preconditions: `mqtt.get_config` shows `clean_session: true`, and `system.get_time` skew against server UTC is within ±5 s. Otherwise do not ask for "go".
  - After `OBSERVED`, read `system.get_time` (`ntp_synced`, `epoch` skew) in the tool and the UI. That gives OQ3 an answer from data.
  - The sniffer records only `id`, `type.action`, `ttl_ms`, `dry_run` and the presence of `confirm`, never `sig` or raw bytes. Delete the log after counting.
  - Give the simulator `ts`/TTL/dedup checks and an `SIM_CLEAN_SESSION=0` mode.
  - Restate Decision 4 accurately: 10 s narrows the window only for a synced clock.

## Finding 5: `OBSERVED` rests on unauthenticated input, and the plan wires the tracker to the shared store it says it avoids
- **Severity:** Medium
- **Location:** plan.md "Quyết định" 6 ("Tracker tự giữ uptime … không đọc `IDeviceTelemetryStore`"). Phase 1, "Inbound" (Status bullet). Phase 3, "Tool kiểm live" `--dry-run`.
- **Flaw:**
  - **Self-contradiction.** Decision 6 says the tracker never reads `IDeviceTelemetryStore`, because master-admin `POST /telemetry` writes it. Phase 1 then passes `telemetry.GetLatest(deviceId)?.UptimeSeconds`, read from that same store after `Record`.
  - **The bridge.** The repo ships `tools/mqtt-broker/hms-telemetry-bridge.js`. It logs in as the configured admin and POSTs telemetry with `uptime_seconds` for exactly `870c870c-…` on every `/status` message and every 2 s from the RTU web API. Phase 3 does not say to stop it. Phase 3's dry-run proof ("uptime không giảm") reads `GET /devices/{id}/telemetry`, the store the bridge writes.
  - **Unauthenticated heartbeats.** `B/status` is unsigned; spec §10 says JSON on the right topic is not authenticated. On the no-ACL broker the plan already assumes, any client can publish a low `uptime_seconds` and end tracking early. That releases the gate and makes the UI say "Đã khởi động lại".
  - **Rebinding.** A non-master `devices.manage` admin can rebind the device to another topic base. The handler treats that as "another physical device" and clears outputs, but the tracker keeps the old uptime baseline, so the next heartbeat from the new device can read as a reboot.
- **Failure scenario:** During the live gateway run, a broker client publishes `{"uptime_seconds":3}` on `hms_toan_lab/gateway_870c/status` one second after the reply. The audit records `DEVICE_REBOOT_OBSERVED` and the gate opens, even though the device never rebooted, for example because it hung in the delay. The bridge's 2 s POSTs, meanwhile, decide what `--dry-run` reports.
- **Evidence:**
  - plan.md Decision 6 versus phase-01 "Inbound": `reboots?.ObserveHeartbeat(deviceId, telemetry.GetLatest(deviceId)?.UptimeSeconds, receivedAt)`.
  - `DevicesController.cs:96-119` (POST writes the shared store). `src/Hms.Server.Application/Realtime/DeviceTelemetryContracts.cs:45-53, 160` (last writer wins, string uptime parsed).
  - `tools/mqtt-broker/hms-telemetry-bridge.js:12, 232, 276-277, 327-344, 362`.
  - Spec L1333. `DeviceLinkHandlers.cs:212-213` ("the broker identity of a message is not proof of the device's identity") and `:334-341` (rebind = new device).
  - `DeviceLinkController.cs:130` (rebind needs `devices.manage` only).
  - phase-03 L43.
- **Suggested fix:**
  - Parse `uptime_seconds` from the inbound `status` dictionary itself, the object passed to `Record`, never from the store.
  - Reset tracker state on rebind and on revoke.
  - Record the evidence class in the detail (`evidence=heartbeat`) and have the UI's signed `system.get_info` uptime confirm it before it shows "Đã khởi động lại".
  - Require the bridge to be stopped during live runs.
  - Have `--dry-run` read uptime with signed `system.get_info`, not `GET /telemetry`.

## Finding 6: The audit cannot tell a failed or unknown dry-run from a real reboot, against the approved design
- **Severity:** Medium
- **Location:** plan.md "Quyết định" 7 (Dry-run bullet). Phase 1, "Correlator" → `ToResult`, and Tests item 6.
- **Flaw:** The brainstorm requires the audit to record "người gửi, dry-run hay thật, `delay_ms`, kết quả theo dõi". The plan marks `dry_run=true` only in the detail of a *successful* dry-run reply. Every other outcome of a dry-run carries no marker:
  - `DEVICE_REJECTED`;
  - `DEVICE_RESPONSE_TIMEOUT`;
  - `MQTT_PUBLISH_UNCONFIRMED`;
  - `DEVICE_REBOOT_IN_PROGRESS`;
  - `CONFIRM_MISMATCH`;
  - `OUTCOME_UNKNOWN_AFTER_RESTART`.

  The audit row has no dry-run column, and `PayloadSummary` serializes only `Payload`. The flag survives only inside the SHA-256 fingerprint.
- **Failure scenario:** A gateway reboots unexpectedly. The investigator finds `system.reboot` by user X, `Failed`/`DEVICE_RESPONSE_TIMEOUT`, outcome unknown. That row could be a harmless dry-run or a real reboot that ran (R4), and the audit cannot say which.
- **Evidence:**
  - Brainstorm L62. plan.md Decision 7.
  - `PersistenceEntities.cs:80-106`: no dry-run field.
  - `EfCommandAuditStore.cs:221-237`: `Serialize(plan.Request.Payload)`.
  - `DeviceLinkHandlers.cs:129-134` (timeout detail) and `:170-171` (rejected detail).
  - `EfCommandAuditStore.cs:44-71`: the restart detail.
  - `CommandContracts.cs:166-178` (`CommandRequestFingerprint`): `DryRun` appears only in the hash.
- **Suggested fix:** At `ReserveAsync`, for class F, store `PayloadSummary` as `{"dry_run":<bool>,"payload":{…}}`. This is set once, needs no schema change, and stays within 512 characters. Add a test that a timed-out dry-run row is distinguishable from a timed-out real one.

## Finding 7: The `IN_PROGRESS` gate burns write-rate slots, so blocked probes can lock all config writes at no cost
- **Severity:** Medium
- **Location:** Phase 1, "Cổng reboot thứ hai" ("sau khi `TryRegisterWrite` trả null … `pending.Remove`"; "Lệnh bị chặn vẫn chiếm 1 slot"). plan.md "Quyết định" 9.
- **Flaw:** `TryRegisterWrite` enqueues the rate timestamp and registers the command inside the lane lock. The plan's gate runs afterwards and removes the command at once. A refused reboot therefore uses one of the device's 10 writes per minute while freeing the lane immediately. The precedent this cites (plan 261106-2033) counted slots only for `NeverSent`, which happens only when the broker is down. Here the refusal is triggered at will, and dry-run probes are harmless to send.
- **Failure scenario:** During the up to 122 s tracking window, a `sys.reboot` holder fires 10 `dryRun:true` reboots in a few milliseconds. Each one is refused `IN_PROGRESS`, nothing is published, and all 10 slots are used. For the next 60 s every config write on the device gets `DEVICE_WRITE_RATE_LIMITED`: `set_hostname`, `set_timezone`, `sync_ntp`, `adc.*`, and the next legitimate reboot after `NOT_OBSERVED`. The attacker can repeat. Relays are not affected (class B is outside the write lane).
- **Evidence:**
  - `DeviceLinkContracts.cs:324-347`: the rate check, `recent.Enqueue(now)` and `Register`, all under `_laneGate`.
  - `MqttDeviceCommandTransport.cs:78-86`.
  - `CommandLanes.cs:10-11`.
  - phase-01 "Cổng reboot thứ hai".
  - `plans/261106-2033-remote-config-system-adc/phase-01-server-config-writes.md:195`.
- **Suggested fix:** Run the tracker check inside the registry's atomic step, before `recent.Enqueue`. For example, pass a refusal predicate into `TryRegisterWrite`; the lock order laneGate → tracker has no reverse path. This keeps the plan's race-freedom argument and costs no slot. Test: 11 refused reboots, then `set_hostname` publishes.

---

## Fact check (sampled)

Plan.md / Phase 1
- `SendDeviceCommandHandler.cs:107-117` permission, `:119-122` CONFIRM_REQUIRED, `:129-135` TTL (`:133` Physical only), `:144-145` server time: VERIFIED.
- `HmsIdfCommandProtocolAdapter.cs:46-53` confirm from `binding.MqttDeviceId`, `:55-56` DRY_RUN_UNSUPPORTED, `:62-64` config policy, `:66-84` key then sign: VERIFIED.
- `HmsIdfCommandCodec.cs:57-61` root `confirm`/`dry_run`: VERIFIED (:58-61).
- `PinnedCommandCatalog.cs:236-242` switch; Flash falls to `_ => false` (:241); `ConfigWriteActions` :98-106: VERIFIED.
- `command-catalog.json` other F rows at 814-822 / 847-855 / 924-932: VERIFIED (ota.apply :816, ota.remote_start :849, factory_reset :926; reboot :1014).
- `DeviceLinkContracts.cs:372-375` hold `ExpiresAt` + 2 s: VERIFIED.
- `EfCommandAuditStore.cs:149-160` overwrite unless revoked, `:44-71` FailUnfinished, `:98` IssuedBy, `:223-225` Flash payload kept: VERIFIED.
- `DeviceAccessPolicy.cs:31-44`, `DevicePermissionCodes.cs:21-28`, `DeviceAccessHandlers.cs:318-326, 366-369`: VERIFIED. They describe grant-side control only (see Finding 1).
- `DependencyInjection.cs:37-38, 46-47, 57` lifetimes: VERIFIED.
- Decision 6 "tracker does not read `IDeviceTelemetryStore`": FAILED. Phase-01 "Inbound" passes `telemetry.GetLatest(...)`.
- Phase-01 "store không ghi đè `DEVICE_REVOKED` (`EfCommandAuditStore.cs:155`)", applied to a tracked reboot row on revoke: FAILED. Revoke only cancels `IN_FLIGHT` rows (`PermissionContracts.cs:243` → `EfCommandAuditStore.cs:28-42`). A SCHEDULED row is `Executed`, so the sweep overwrites it with `NOT_OBSERVED`, and test 6's "row đã `DEVICE_REVOKED`" does not exist.
- "Inbound xử lý tuần tự (`MqttDeviceLinkService.cs:321-325`)": VERIFIED for inbound. The sweep runs on the separate housekeeping loop (:110-121) concurrently. The plan's tracker lock covers that.
- Test refs: `CatalogTests.cs:138-161` (33 at :155, reboot closed at :160), `:164-187` (:175, :185-186); `ConfigCommandPolicyTests.cs:141`; `DeviceLinkTests.cs:119-136` (ActionUnsupported at :130); `DeviceCommandAuthorizationTests.cs:196-214` (24); `PersistenceModelTests.cs:276` (2); `ManagementApiTests.cs:196-203`; `DeviceAccessHandlerTests.cs:569-573`: VERIFIED.

Phase 2
- `adminApi.ts:63-74` has no `class`; `:234` `sendCommand` body has no `confirm`; `:248-249` `commandResult`; `:102-118` `binding.mqttDeviceId`: VERIFIED.
- `useDeviceCommand.ts:8-15` (no `confirm`/`dryRun`), `:92-94` spreads the body: VERIFIED.
- `useDeviceWrite.ts:138-176` `runDeviceWrite`: VERIFIED.
- `DangerConfirmDialog.vue` has no slot; trim at :44-47: VERIFIED.
- `DeviceCommandModal.vue:74-75, 102-107, 138-143`: VERIFIED.
- `DeviceDetailView.vue:284-287` (`command_result` → `noteConfigChange` only), `:344-351` readCardContext, `:895-930` column 1: VERIFIED.
- `configWriteSpecs.ts:33-35`: VERIFIED. `useDeviceReads.ts:25` `RETRY_DELAY_MS = 2000`: VERIFIED.
- `MeController.cs:57-71` returns `Class`; `Program.cs:27-29` string enums: VERIFIED.

Phase 3
- Simulator passes only `data.data` to handlers (:248); `get_info` has no `uptime_seconds` (:115); no LWT and `clean: true` (:207-211); `SIM_SILENT` exists (:14-15): VERIFIED.
- The simulator has no `ts`/TTL/dedup check (:232-249): VERIFIED. The plan does not mention it (Finding 4).
- Tool login via `tool-env.js:19-24` and CSRF: VERIFIED. It runs as whatever `HMS_ADMIN_EMAIL` is, normally the dev-seed master-admin (`Program.cs:71-84`). No live step exercises a non-master path.
- `tools/device-reads/` gitignored: VERIFIED (`.gitignore:18`).
- `GET /devices/{id}/telemetry` (`DevicesController.cs:79-94`) as uptime evidence: VERIFIED as an endpoint. It reads the store that master-admin POST and the bridge also write (Finding 5).
- Scenario 7 (stop the server mid-tracking → `GET /commands/{id}` = `OUTCOME_UNKNOWN`): FAILED on the lab setup. Without a Postgres connection string the audit store is the in-memory singleton (`DependencyInjection.cs:65-68`), so the row is gone after restart and `GET` returns 404 `COMMAND_NOT_FOUND` (`DeviceCommandsController.cs:104-105`). The dev API had no Postgres in the last run (`261106-2033/reports/affected-roles.md`).
- Spec line citations (§6.1 L604, §7 L1087, §11 L1363, §6.4 L868-882, §2 L455, §3 L481/L483, §10 L1333/L1340): VERIFIED at 9bfc168.
- `docs/architecture-mqtt-device-link.md` line refs (:66, :166, :241, :461): UNVERIFIED (not sampled). `docs/project-roadmap.md` §5 at :102: VERIFIED.

## Contract verification (callers and consumers)

- **`ConfigCommandPolicy.Check`:** 12 call sites (1 in `HmsIdfCommandProtocolAdapter.cs`, 11 in `ConfigCommandPolicyTests.cs`). The invariant test `Every_opened_config_write_has_a_payload_rule` (`ConfigCommandPolicyTests.cs:156-167`) iterates `ConfigWriteActions` only. The plan does not extend it to `FlashActions`, so "every opened write has a rule" stops covering the one class F row.
- **Catalog sets:** used in `EfCommandAuditStore.cs:226`, `ManagementApiTests.cs:200-202` (listed), `CatalogTests` (listed) and `ConfigCommandPolicyTests.cs:161` (not listed).
- **TTL constants:** handler :133; `MqttLinkOptions.cs:53`; tests `DeviceCommandAuthorizationTests.cs:213, 232`, `DeviceLaneSafetyTests.cs:222, 352, 354`, `PhysicalOutputSafetyTests.cs:121-124`. Only :213 needs to change (listed); the `<=` assertions still hold. `MeController.cs:70` will keep advertising `TtlMs` 60000 for reboot while the server signs 10000.
- **`MqttDeviceCommandTransport` ctor:** 13 test sites (`DeviceLaneSafetyTests` 11, `PhysicalOutputSafetyTests` 2) plus DI. With an optional parameter the tests compile without the tracker, so the gate is silent in them (sibling Finding 7).
- **Correlator and inbound ctors:** 2 test sites each (`DeviceLinkTests`, `DeviceOutputStateTests`). `DeviceOutputStateTests` is not listed.
- **`FailUnfinished`:** interface `CommandContracts.cs:129`; implementations `CommandContracts.cs:314`, `EfCommandAuditStore.cs:44`, and the test decorator `DeviceOutputStateTests.cs:218`; callers `Program.cs:162`, `PersistenceModelTests.cs:276`, `PhysicalOutputSafetyTests.cs:253` (not listed; the count stays 2). The EF integration test uses `UseInMemoryDatabase`, so the new relational `ExecuteUpdate` branch is never executed by any test.
- **`IDeviceCommandTransport` implementations:** `MqttDeviceCommandTransport` (gate), `DeferredDeviceCommandTransport` (`DependencyInjection.cs:62`, never publishes), `RecordingDeviceCommandTransport` (tests). There is no other publish path (`PublishCommandAsync` is called only from `MqttDeviceCommandTransport.cs:95`).
- **`DangerConfirmDialog`:** 2 consumers (`DeviceCommandModal.vue`, `DeviceListView.vue`); the defaults keep both unchanged.
- **`DeviceCommandModal`:** 1 consumer (`DeviceDetailView.vue`). The class F filter is cosmetic and fails open if `class` is missing. The real boundary is server-side: no confirm field means `CONFIRM_REQUIRED` (`SendDeviceCommandHandler.cs:119-122`).
- **Other UI send paths:** legacy `App.vue:772` `sendDeferredCommand` sends no payload or confirm, so class F gets `CONFIRM_REQUIRED`. It is not a bypass. Any API client holding the permission plus the derivable confirm can send by design.
- **`command_result` consumers:** frontend `DeviceDetailView.vue:284-287` only (`CONFIG_WRITE_CARD` map); tools poll `GET /commands/{id}`. The new codes flow through `status === 'Executed'` → `executed` (`useDeviceWrite.ts:170`). Literal `DEVICE_EXECUTED` checks exist only in `fakeDevice.ts:45` and the job path `useDeviceWrite.ts:126`.

Checked and not findings:
- `/me/command-catalog` exposes row availability and the HighRisk flag to every authenticated user, but no device data.
- `allowed-commands` is scope- and permission-filtered (`DeviceCommandsController.cs:59-72`).
- `mqttDeviceId` is visible to any viewer, but confirm is not an authorization factor: the permission check comes first (`SendDeviceCommandHandler.cs:113-122`), and spec §7 makes it a typed device-id token, not a secret.
- Realtime `command_result` detail (uptime, delay) reveals nothing beyond telemetry that viewers already see.
- The plan's race argument for the gate ("Start → Remove → register → check") holds, because `_pending.Values` and `TryRemove` order through the dictionary's locks.

Status: DONE
Summary: 7 findings (4 High, 3 Medium). Top issues: the plan locks in a weaker send-time rule than approved criterion 2 before OQ1 is answered, a reboot bypasses the master-admin relay allowlist, `sys.reboot` holders can loop reboots through untracked paths, and the 10 s TTL does not bound post-reboot replay or redelivery while the live run creates the replay artifact.
Concerns/Blockers: Findings 1-3 change approved design or user-facing policy, so they need the user's decision. Agent memory was not updated because the brief allowed edits to this report only.
