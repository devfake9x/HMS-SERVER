# Red team: failure-mode analysis of plan 261109-2250-safe-reboot

Reviewer: code-reviewer, acting as failure-mode analyst (with the Fact Checker and Contract Verifier roles). Date: 2026-11-09.

Scope:
- `plan.md` and phases 01–03, plus the approved brainstorm `plans/reports/brainstorm-261109-2244-safe-reboot.md`.
- Checked against the code in worktree `ck-project-management-review-dd19d6` (branch `claude/mqtt-device-connection-36f705`, HEAD `45f5c14`).
- Checked against the spec export `git -C HMS-IDF show 9bfc168:engineering-docs/api/mqtt-api.md`. In this report, `spec Lnnn` means line nnn of that export.

I did not open any firmware source. I did not contact any broker, device or API. I edited no file other than this report.

Eight findings: 0 Critical, 3 High, 5 Medium. A fact-check table and a consumer inventory follow the findings.

---

## Finding 1: The reboot gate is armed on one path only; every R4 path leaves a reboot untracked, and that path is never simulated
- **Severity:** High
- **Location:**
  - Phase 1, section "Correlator" (`HandleAsync` bullet) and section "Luật payload".
  - plan.md, "Ngoài phạm vi" ("Theo dõi khi reply bị mất") and "Rủi ro còn lại" ("Reply mất").
  - Phase 3, section "Kịch bản", simulator step 4.
- **Flaw:** `Start` runs only when four things hold: the reply is `success`, it is processed before the 30 s server deadline, `result.Status == Executed`, and `pending.Remove` then succeeds. The device can reboot through at least four other paths. On each of them the tracker is never armed and `DEVICE_REBOOT_IN_PROGRESS` is never raised:
  1. **The reply is lost.**
     - The policy accepts `delay_ms: 0`. With 0, the device may restart before its QoS 1 reply leaves the outbox.
     - The plan itself lists "reply trước reboot … nhất là với `delay_ms:0`" as a spec gap, yet it still allows the value.
  2. **The reply arrives after the 30 s deadline.**
     - `pending.Find` returns null, so the reply is `Ignored` (`DeviceLinkHandlers.cs:53-55`).
     - The deadline is `PublishedAt + ResponseTimeoutSeconds` (30 s) (`MqttDeviceCommandTransport.cs:66`, `MqttLinkOptions.cs:27`).
  3. **The reply is the executor "accepted, still running" shape** (spec L577: `success:false, ESP_ERR_TIMEOUT, data:{accepted:true,pending:true}`).
     - `ToResult` maps it to `Acknowledged` (`DeviceLinkHandlers.cs:155-161`). The plan starts tracking only on `Executed`.
     - The UI then runs `followJob` (see Finding 6).
  4. **The sweep races the correlator.** The sweep runs on the `ExecuteAsync` loop (`MqttDeviceLinkService.cs:121`). Inbound messages run on a separate task (`:98`, `:325`). So `TakeExpired` can remove the command between `Start` and `pending.Remove`.
     - `Remove` returns false and the reply is `Ignored` (`DeviceLinkHandlers.cs:86-87`).
     - The tracker stays armed, but the audit row says `DEVICE_RESPONSE_TIMEOUT`.
     - A later `CompleteRebootAsync(OBSERVED)` overwrites the `Failed` row with `Executed`, because `RecordAsync` guards only `DEVICE_REVOKED` and transport-only results (`EfCommandAuditStore.cs:149-160`).
     - The plan's statement "Inbound xử lý tuần tự … nên SCHEDULED luôn được ghi trước OBSERVED" ignores this second thread.

  On paths 1–3 the lane reopens when the pending entry is swept, 30 s after the publish. The plan says the lane is held until "`ExpiresAt` + 2 s" (plan.md:39, :126). With a 10 s TTL, `ExpiresAt` + 2 s = RequestedAt + 12 s. That is already in the past when the sweep sets the hold (`DeviceLinkContracts.cs:375`). The real hold is just the pending entry (`:332-333`).
- **Failure scenario:**
  1. The gateway replies late, or not at all, but does reboot.
  2. At about 30 s the audit says `DEVICE_RESPONSE_TIMEOUT` and the lane is free.
  3. The UI shows "chưa rõ", reads `system.get_info` once, and re-enables the button.
  4. A second operator, or the same one in another tab, sends `system.reboot` while the device is still booting.
  5. The server has no tracker, so it publishes. With `clean_session=true` the command is lost. With `clean_session=false` (allowed, spec R10 L414) the broker hands it to the freshly booted device.
  6. The device has lost dedup (spec L455, L1340) and may not have a valid clock yet, so it skips the age check (spec L481). It reboots a second time.

  The simulator cannot reproduce this. `SIM_SILENT` returns before the handler runs (`tools/mqtt-broker/spec-device-simulator.js:245-247`). Scenario 4 therefore tests "no reply and no reboot", which is the harmless case.
- **Evidence:**
  - Phase 1 line 60: "nếu `IsReboot`, không `DryRun`, `result.Status == Executed`, có tracker → `Start`".
  - Phase 1 line 15 / plan.md:36: "số nguyên 0..60000".
  - plan.md:107: "server không theo dõi".
  - Phase 3 line 61: "`SIM_SILENT=system.reboot` → `DEVICE_RESPONSE_TIMEOUT` … không theo dõi".
  - Code: `DeviceLinkHandlers.cs:53-55`, `:83-87`, `:155-161`; `MqttDeviceLinkService.cs:98`, `:121`, `:325`; `DeviceLinkContracts.cs:332-333`, `:369-375`.
- **Suggested fix:**
  - For `system.reboot`, arm a "suspected" tracking on `DEVICE_RESPONSE_TIMEOUT`, on `PublishUnconfirmed`, on `Acknowledged`, and on a late reply (look up the audit plan when `Find` is null). It closes the gate for the same window and ends in `OBSERVED` or `OUTCOME_UNKNOWN`, never `NOT_OBSERVED`.
  - Undo `Start` when `Remove` returns false, or make the two one atomic registry operation.
  - Require `delay_ms >= 1000` on the server (the default is 2000; the UI never sends 0).
  - Add a simulator mode `SIM_REBOOT_NO_REPLY=1` that reboots without replying, with a scenario that shows a second reboot is refused while the device boots.

## Finding 2: `NOT_OBSERVED` is final, and the sweep writes it even while the server cannot hear the device
- **Severity:** Medium
- **Location:** Phase 1, section "Tracker" (`TakeExpired`, "một bản ghi chỉ kết thúc một lần") and section "Correlator" (`SweepExpiredAsync`, `CompleteRebootAsync`: "Lỗi được log, không ném ra").
- **Flaw:**
  1. **The sweep runs while the broker link is down.** It runs on purpose while the server's own broker link is down (`MqttDeviceLinkService.cs:117-121`), every `ReconnectDelaySeconds` (5 s).
  2. **Heartbeats from the outage are gone for good.** The server's session is `clean` (`:172`), so heartbeats published during the outage are never delivered. A server-side outage that overlaps the 122 s window therefore yields `NOT_OBSERVED`, which tells the operator to go to the site, for a reboot that happened.
  3. **Late evidence is thrown away.** Once the record is taken, a later heartbeat with lower uptime is ignored. A gateway that needs 130 s (slow Wi-Fi/DHCP/TLS) stays "Chưa thấy thiết bị lên lại" in the audit forever.
  4. **A failed audit write is never retried.** The tracker removes the record before the audit write. If `RecordAsync` throws (database blip), the error is only logged and the row stays `SCHEDULED`. The UI polls until its own limit (`tracking_timeout`). The next restart then rewrites the row to `OUTCOME_UNKNOWN` with `AfterRestartDetail`: "The server restarted before the device replied" (`CommandContracts.cs:50-51`). That text is false for a row whose reply was received.
- **Failure scenario:**
  1. The lab broker at 192.168.1.101 restarts during a gateway reboot.
  2. The server reconnects after 90 s. The gateway's first post-boot heartbeat was published during the outage and is lost.
  3. Later heartbeats show uptime around 100 s. That is lower than the last uptime seen before, so the evidence is real, but it arrives at 125 s, after the sweep has already written `NOT_OBSERVED`.
- **Evidence:**
  - Phase 1 lines 53, 61–62.
  - `MqttDeviceLinkService.cs:117-121`, `:134-136`, `:172`; `CommandContracts.cs:50-51`.
- **Suggested fix:**
  - Record the intervals when the link is down. If the server's link was down at any point in the window, finish with `OUTCOME_UNKNOWN` (detail `server_link_down`) instead of `NOT_OBSERVED`, or extend the deadline by the outage length.
  - Keep a short tail after expiry (for example 10 min) in which an uptime decrease upgrades the row to `OBSERVED` (late).
  - Remove the record only after the audit write succeeds, or retry on the next sweep.
  - Use a dedicated detail string for `SCHEDULED` → `OUTCOME_UNKNOWN`.

## Finding 3: The live gateway run checks relays only, on a shared bench where a reboot can change firmware, broker config and rule outputs; it has no abort criteria and no restore step
- **Severity:** High
- **Location:**
  - Phase 3, section "Tiền điều kiện live (gateway)" and section "Kịch bản → Gateway" steps 1–6.
  - Phase 3, "Risk Assessment" row "Gateway không lên lại".
  - plan.md, "Rollback → Phase 3".
- **Flaw:** `--checks` saves only `/link`, `system.get_info` and `control.get_status`. A reboot also applies whatever the device has staged, and the plan neither reads that state nor aborts on it. Every item below is readable with reads the server already opened (`PinnedCommandCatalog.cs` `EnabledActions`: `ota.get_status`, `mqtt.get_status`, `mqtt.get_config`).
  1. **Staged firmware.** An OTA image staged by `ota.remote_start`, or an image awaiting verification, boots on restart. The spec mentions rollback at boot (spec L880). The bench is reflashed by the firmware team from a working tree ("the bench now runs `c12cf4be…`", flashed application-only; spec L2359). A different image may also run different key or catalog behaviour.
  2. **MQTT config trial.** A pending trial is resolved by a reboot ("Mất điện giữa chừng: lần khởi động sau quay về cấu hình cũ với lý do `reboot`", spec L708). The spec also says not to use `system.reboot` as the trial's keep-alive command (spec L707).
  3. **`clean_session=false`.** It makes Finding 1's redelivery real (R10, spec L414).
  4. **Device rules.** `SET_HIGH`/`SET_LOW` rules fire on the first good evaluation after boot (spec L881). DO1 is wired back to DI1. A rule keyed on DI1 or DO1 can switch a relay ON right after boot. Then `--after` and `checkAfterReboot` report a false "lệch §6.4", and a relay the operator was told "sẽ TẮT" is ON.

  Other gaps:
  - There is no wait limit before someone acts on site, and no recovery path when a power cycle does not help. A power cycle boots the same staged image or config, so "cấp nguồn lại" does not recover from items 1–2. Recovery would need the local web UI or the firmware team.
  - Nothing restores DO1 to its pre-reboot state. The rollback forbids any further command ("không gửi lệnh nào thêm"), so a DO1 that was ON stays OFF after the session.
  - The bench is shared ("another client had changed the bench command key", spec L2347). Only one HMS-SERVER instance can hold the in-memory gate (plan.md "Giả định một instance"). Nothing checks that no other server or worktree instance is bound to `gateway_870c` during the run.
- **Failure scenario:**
  1. The firmware team left a downloaded image staged after an `ota.remote_start` HIL step.
  2. The UI reboot boots that image. `--after` reports "firmware khác", but the gateway is already on new firmware with no plan to go back.
  3. The person in the lab power-cycles it, which boots the same image.
  4. The bench stays on an unreviewed image, and the next firmware HIL run starts from a state nobody chose.
- **Evidence:**
  - Phase 3 line 42 (the `--checks` read set); line 51 ("có người ở lab nếu thiết bị không lên lại"); Risk row line 152; plan.md:99 ("người ở lab cấp nguồn lại; không gửi lệnh nào thêm").
  - Spec L707-708, L880-881, L2347, L2359, L414.
  - `src/Hms.Server.Infrastructure/Commands/PinnedCommandCatalog.cs` (`EnabledActions` include `("ota","get_status")`, `("mqtt","get_status")`, `("mqtt","get_config")`).
- **Suggested fix:**
  - Extend `--checks` to read `ota.get_status`, `mqtt.get_status` and `mqtt.get_config`. Hard-abort (no dry-run, no UI step) on any of: an image staged or pending verification, `config_trial.state == pending`, `clean_session == false`.
  - Before "go", get written confirmation from the firmware team that no rule targets DO1 or DI1 and that they will not reflash during the window.
  - Define T+180 s as the wait limit and name the recovery path beyond a power cycle.
  - After `--after`, add one restore step: `control.set_output` DO1 back to its `before.json` state, with a read-back. Count it in the sniffer expectations.
  - Confirm that exactly one HMS-SERVER instance is bound to the device (check the sniffer for `cmd/#` publishers before starting).

## Finding 4: The `--checks` negative probes on the live gateway are real, non-dry-run commands, and the `factory_reset` probe is unspecified
- **Severity:** High (low likelihood, irreversible impact)
- **Location:** Phase 3, section "Tool kiểm live", bullet `--checks`; Phase 3 "Kịch bản → Gateway" step 1.
- **Flaw:**
  - The plan applies "use a dry-run so a broken gate is harmless" to `--probe-in-progress` only (Phase 3 line 45).
  - The `--checks` probes are described only by the code each one should get. `system.factory_reset` → `DENIED_UNTIL_ACTION_CONTRACT` gives no payload, `confirm` or `dryRun`.
  - The only thing standing between that probe and the device is the `FlashActions` switch, which this plan edits (`PinnedCommandCatalog.cs:236-242`), on whatever server build happens to answer at `HMS_API_BASE` (default `http://127.0.0.1:5099`, `tools/device-config-check.js:19`).
  - Suppose the tool author copies the reboot probe shape: a correct confirm (`FACTORY-RESET-gateway_870c`) and `{confirmed:true, level:1}`. Then a mis-edited allowlist, or a stale or other build on port 5099, turns a "nothing leaves the server" check into an NVS wipe. Level 1 erases NVS, which holds keys and the connection config (spec §7 L1095: "Reset có thể xóa khóa/kết nối đang dùng").
- **Failure scenario:**
  1. During phase 3, another worktree's server build is running on :5099, with `FlashActions` edited to `{reboot, factory_reset}` while someone experiments.
  2. `--checks` sends `system.factory_reset` with a valid confirm.
  3. The server signs and publishes it. The gateway wipes its key and broker settings and never comes back to the lab broker.
  4. A power cycle does not help (Finding 3).
- **Evidence:**
  - Phase 3 line 42: "`system.factory_reset` → `DENIED_UNTIL_ACTION_CONTRACT`" (no payload or confirm given).
  - Phase 3 line 45: dry-run reasoning applied to `--probe-in-progress` only.
  - `SendDeviceCommandHandler.cs:100-103`: DENIED is checked before CONFIRM_REQUIRED (`:119-122`). A probe without `confirm` therefore still proves DENIED.
- **Suggested fix:**
  - Pin every live negative probe to the shape that is harmless at the device even if the server gate fails:
    - `system.factory_reset`: no `confirm`, `dryRun:true`, payload `{}`.
    - The `{delay_ms:"x"}` and wrong-confirm probes: `dryRun:true`.
  - Before any `--confirm-live` mode, the tool reads `/api/v1/me/command-catalog` and refuses to run unless `system.factory_reset`, `ota.apply` and `ota.remote_start` show `available:false`.
  - Add a unit check of the tool's probe table.

## Finding 5: The 65 s wait does not separate the UI's own dry-run from its real command; the throttle question gets one irreversible try, and the simulator cannot exercise the `UNAUTHORIZED` path
- **Severity:** Medium
- **Location:**
  - Phase 3, "Kịch bản → Gateway" step 3 and "Risk Assessment" row "Dry-run tính vào throttle".
  - Phase 2, section "Luồng" steps 1–2.
  - Phase 3, simulator throttle bullet.
- **Flaw:**
  1. **The wait is in the wrong place.** `runReboot` sends the real command straight after the dry-run succeeds. The 65 s wait sits between the tool's `--dry-run` and the UI flow. If a dry-run counts toward the shared 60 s throttle (spec L483, an open spec gap), the UI's own dry-run still triggers it. The real gateway reboot is then refused with `UNAUTHORIZED`, which also covers dedup and safe mode (spec L571), so it cannot be told apart. The plan then stops the session (Open question 5). The main gateway acceptance criterion cannot be met in that session, and the risk table lists a mitigation that does nothing.
  2. **The simulator's throttle is dead code through the server.**
     - It lives in RAM and is cleared by every simulated reboot (Phase 3 line 33).
     - The server's `DEVICE_REBOOT_IN_PROGRESS` gate blocks any second real reboot inside the window.
     - So no simulator scenario can produce a throttled real reboot, and the UI branch "UNAUTHORIZED → đọc `system.get_info` một lần + nhắc 60 s" is tested only against `fakeDevice`.
- **Failure scenario:** On the gateway, the tool's dry-run runs at T. At T+65 s the UI's dry-run succeeds, and at T+66 s the real reboot gets `UNAUTHORIZED`. The session ends without a gateway reboot, after the lab was told DO1 would drop and the firmware team was asked to stand by.
- **Evidence:**
  - Phase 3 line 68 ("Đợi ≥ 65 s … rồi reboot qua UI (gồm dry-run của UI)"); Risk row line 154.
  - Phase 2 lines 50-53 (dry-run, then the real command straight away).
  - Phase 3 line 33; spec L483, L571.
- **Suggested fix:**
  - First run a harmless probe on the gateway: two dry-runs 5 s apart. If the second gets `UNAUTHORIZED`, the dry-run is throttled or counted, so set the UI flow to wait 60 s (with a visible countdown) between the dry-run and the real command before attempting the reboot.
  - Make the spacing a constant in `useDeviceReboot` that is tested.
  - Add a simulator switch that keeps the throttle across reboots, plus an E2E scenario that drives the UI into the `UNAUTHORIZED` branch.

## Finding 6: The UI flow names one code out of several that the real write can end in, and the page's online flag can stay stale after the reboot
- **Severity:** Medium
- **Location:** Phase 2, section "Luồng (`useDeviceReboot.ts`)" steps 2–4, and section "Trang thiết bị (`DeviceDetailView.vue`)".
- **Flaw:**
  1. **Codes other than `SCHEDULED` are unspecified.**
     - `runDeviceWrite` returns at the first poll that is no longer `Published` (`useDeviceCommand.ts:68-70`, `:105`). The code at that moment can already be `DEVICE_REBOOT_OBSERVED` or `NOT_OBSERVED`: a hidden tab throttles timers, or a GET failed transiently and the loop slept.
     - It can also be `DEVICE_EXECUTED` (a server build without the tracker; see Finding 7).
     - Step 2 defines only "`executed` với code `DEVICE_REBOOT_SCHEDULED` → bước 3". An `executed` result with any other code has no branch, so the UI may show success with no tracking or no re-read.
  2. **`Acknowledged` starts a job follow-up against a rebooting device.**
     - The job reply shape (Finding 1, path 3) sends `runDeviceWrite` into `followJob` (`useDeviceWrite.ts:171-173`). That loop polls `system.get_operations` for 60 s (`:13`, `:99-129`) at a device that is restarting.
     - Each read is lost and costs a read-lane slot until its 30 s timeout. The flow then ends `unknown` and never reaches the tracking poll.
  3. **The tracking poll's error handling is unspecified.** Step 3 does not say what happens when a poll fails. Simulator step 7 stops the server, so every poll fails until it is back, and the session cookie may be lost. A thrown poll aborts the flow instead of reaching `OUTCOME_UNKNOWN`.
  4. **The online flag can stay false after the reboot.**
     - `rebootGate` locks on `link.online === false`.
     - `Online` needs a heartbeat no older than 15 s (`DeviceLinkController.cs:28`, `:67`, `:77`).
     - The page re-fetches the link only on `outputs` or `connectivity` events (`DeviceDetailView.vue:278-283`).
     - The device publishes `online` at connect, before its first heartbeat. If boot plus Wi-Fi, TLS and MQTT takes more than about 13 s, that fetch returns `Online:false`.
     - The next trigger is the `outputs` event raised by `MarkStale(DeviceRebooted)`. It fires only when an output state already exists (`DeviceOutputState.cs:57-67`), which is not the case for a user without `control.get_status`.
     - The page then shows "Thiết bị offline" after "Đã khởi động lại" until reload.
- **Failure scenario:**
  1. The operator starts the reboot and switches tabs. Chrome throttles the background timers.
  2. The first poll after the reply sees `OBSERVED`. `runReboot` has no branch for it, the re-read in step 4 never runs, and the panel shows a generic success.
  3. Other cards keep pre-reboot data. The two read cards are marked stale only through the `command_result` event, which may have been dropped (`REALTIME_QUEUE_FULL`).
- **Evidence:**
  - Phase 2 lines 53-56 and line 80.
  - Code: `useDeviceCommand.ts:68-70`, `:105`; `useDeviceWrite.ts:13`, `:99-129`, `:169-175`; `DeviceLinkController.cs:28`, `:67`, `:77`; `DeviceDetailView.vue:278-287`; `DeviceOutputState.cs:57-67`.
- **Suggested fix:**
  - Branch on the code, not on `kind`. Any `executed` with a `DEVICE_REBOOT_*` code enters step 3, and step 3 returns at once when the code is already final. `DEVICE_EXECUTED` is treated as "tracking unavailable": read `get_info` once and stop.
  - Pass a flag so the reboot write does not call `followJob`. Treat `Acknowledged` like `unknown`.
  - Make step 3 swallow transient errors until its deadline, as `runDeviceCommand` does.
  - On `OBSERVED` (from the event or the poll), call `refreshOutputs()` so the link is re-fetched, and re-fetch it again after the step-4 reads.
  - Add vitest cases: first poll already `OBSERVED`; `DEVICE_EXECUTED`; `Acknowledged`; poll throws three times and then returns `OUTCOME_UNKNOWN`.

## Finding 7: The second-reboot gate fails open when DI does not inject the tracker, and no test proves the live transport receives it
- **Severity:** Medium
- **Location:**
  - Phase 1, section "Cổng reboot thứ hai" (ctor bullet and branch condition).
  - Phase 1, "Tests Before" item 9 (DI bullet).
  - Phase 1, "Risk Assessment" row "Tham số tuỳ chọn mà DI không tiêm".
- **Flaw:**
  - The gate is `reboots?.IsTracking(deviceId) == true`, and the tracker is an optional constructor parameter defaulting to `null` in the transport, the correlator and the inbound handler. A missing injection therefore disables both tracking and the gate without any error.
  - The mitigation is a DI test that resolves the tracker from two scopes. It does not resolve `MqttDeviceCommandTransport`. That type is registered only when `enableLiveMqtt` is true (`DependencyInjection.cs:51-58`); otherwise `DeferredDeviceCommandTransport` is used (`:59-63`).
  - The unit tests build the transport by hand (`DeviceLaneSafetyTests.cs:38`, `:69`, `:157`, …) and pass the tracker themselves.
  - So all phase 1 tests stay green when the registration is missing, placed in the wrong branch, or later removed. This is a safety gate that fails open, unlike `ConfigCommandPolicy`, which fails closed for any write without a rule (`ConfigCommandPolicy.cs:8-12`).
- **Failure scenario:** A later refactor moves `AddSingleton<IDeviceRebootTracker,…>` next to the in-memory stores, inside the `else` of the database branch. Production (EF) runs without it. Then:
  - every reboot audit ends at `DEVICE_EXECUTED`;
  - the UI never enters tracking (Finding 6, item 1);
  - two operators can reboot back to back;
  - every test still passes.
- **Evidence:**
  - Phase 1 lines 58, 65, 71-72 (`IDeviceRebootTracker? reboots = null`, `reboots?.IsTracking(deviceId) == true`).
  - Phase 1 line 170 (DI test).
  - `DependencyInjection.cs:51-63`; `DeviceLaneSafetyTests.cs:38`, `:69`, `:157`.
- **Suggested fix:**
  - Make the tracker a required constructor parameter of `MqttDeviceCommandTransport` and of the correlator. The unit tests already construct those types by hand.
  - Add one integration test that builds the provider with `enableLiveMqtt: true` (the constructors do not connect), resolves `IDeviceCommandTransport` and `DeviceCommandResponseCorrelator`, and checks that a reboot is refused with `DEVICE_REBOOT_IN_PROGRESS` while a tracking entry exists.

## Finding 8: "In progress" cannot be observed from outside the server and does not survive a restart, so the gateway probe and the post-restart behaviour depend on timing
- **Severity:** Medium
- **Location:**
  - Phase 3, "Kịch bản → Gateway" step 4 and simulator steps 2 and 7.
  - plan.md, "Ngoài phạm vi" (no endpoint; tracking lost on restart).
  - Phase 2, "Risk Assessment" row "Tab đóng giữa chừng".
- **Flaw:**
  1. **The probe cannot tell whether tracking is active.** `--probe-in-progress` must hit the window between the reply and the first post-boot heartbeat. The tool has no way to check that tracking is active; it can only infer it from the UI's correlation id. On a gateway that returns quickly, the tool logs in, fetches the CSRF token and catalog, and sends its dry-run after `OBSERVED`. The dry-run is then published (`--probe-in-progress` has no fallback). That makes a fourth `system.reboot`-class command on the device, which breaks the sniffer criterion ("2 dry-run + 1 thật"; "`--probe-in-progress` không publish gì") and may count toward the device throttle.
  2. **A restart drops the gate.** After a server restart, `FailUnfinished` turns `SCHEDULED` into `OUTCOME_UNKNOWN`, but the in-memory gate is gone while the device may still be inside its window. The UI shows "Server khởi động lại … chưa rõ kết quả" and re-enables the button (`rebootGate` has no tracking input). The `DEVICE_REBOOT_IN_PROGRESS` protection that Phase 2 cites for a closed tab ("reboot sau đó bị `DEVICE_REBOOT_IN_PROGRESS` tới khi xong") no longer holds.
- **Failure scenario:**
  1. The gateway boots and reconnects in about 9 s.
  2. The operator starts `--probe-in-progress` when the UI shows "Đang khởi động lại…". The tool's login takes about 2 s, so its POST lands at about 11 s, after `OBSERVED`.
  3. The dry-run is published. The sniffer count becomes 3 dry-runs + 1 real, and Phase 3 acceptance fails ambiguously: was the gate broken, or was the probe late?
- **Evidence:**
  - Phase 3 lines 45, 69, 144.
  - plan.md:105-108.
  - Phase 2 Risk row "Tab đóng giữa chừng … reboot sau đó bị `DEVICE_REBOOT_IN_PROGRESS` tới khi xong".
  - `Program.cs:157-165` (startup only); `MqttDeviceLinkService.cs:121` (sweep).
- **Suggested fix:**
  - The tool reads `GET /commands/{uiCorrelationId}` right before the probe and probes only while the code is `DEVICE_REBOOT_SCHEDULED`. It records both timestamps; a late probe is reported as "skipped", not "fail".
  - On the gateway, run the probe from the same tool process that already holds a session, prepared before the UI click.
  - On startup, seed the gate from audit rows that were `SCHEDULED` (now `OUTCOME_UNKNOWN`) and are younger than `delay_ms + 120 s`. Alternatively, expose a read-only "tracking" flag on `/link` so `rebootGate` can lock the button.

---

## Fact check

`VERIFIED (file:line)` = the code says what the plan claims. `FAILED` = it does not. `PARTIAL` = correct in part.

### plan.md
| Claim | Result |
|---|---|
| Permission check `SendDeviceCommandHandler.cs:107-117`; `CONFIRM_REQUIRED` :119-122; TTL cap :129-135; server time for writes :144-145 | VERIFIED (`SendDeviceCommandHandler.cs:107-122`, `:129-135`, `:144-145`) |
| Adapter: `CONFIRM_MISMATCH` :46-53, `DRY_RUN_UNSUPPORTED` :55-56, payload rule :62-64, key read and sign :66-84 | VERIFIED (`HmsIdfCommandProtocolAdapter.cs:46-84`) |
| Lane `TryRegisterWrite` at transport :78-86 | VERIFIED (`MqttDeviceCommandTransport.cs:78-86`) |
| Class F goes through the write lane (`CommandLanes.cs:10-11`) | VERIFIED (`CommandLanes.cs:10-11`: everything except Read and Physical) |
| "giữ lane tới `ExpiresAt` + 2 s khi không có reply" (`DeviceLinkContracts.cs:372-375`) | PARTIAL: the pending entry holds the lane until the 30 s deadline (`DeviceLinkContracts.cs:332-333`, `MqttDeviceCommandTransport.cs:66`). The `ExpiresAt` + 2 s hold set at `:375` is already in the past for a 10 s TTL. |
| Switch falls to `_ => false` at `PinnedCommandCatalog.cs:236-242`/:241 | VERIFIED (`PinnedCommandCatalog.cs:236`, `:241`) |
| `factory_reset`, `ota.remote_start`, `ota.apply` at `command-catalog.json:814-822`, :847-855, :924-932 | PARTIAL: the line ranges exist, but the names are in a different order. `:815` is `ota.apply`, `:848` is `ota.remote_start`, `:925` is `factory_reset`. |
| "Inbound xử lý tuần tự (`MqttDeviceLinkService.cs:321-325`), nên SCHEDULED luôn được ghi trước OBSERVED" | PARTIAL: inbound runs on a single reader (`:33-34`, `:325`), but the sweep runs on another loop (`:121`). See Finding 1, path 4. |
| `RecordAsync` overwrites `Code`/`Detail` for rows that are not revoked (`EfCommandAuditStore.cs:149-160`) | VERIFIED (it also overwrites `Status`, with no status guard for non-transport results) |
| Decision 6: "Tracker … không đọc `IDeviceTelemetryStore`" | FAILED against Phase 1 line 66, which passes `telemetry.GetLatest(deviceId)?.UptimeSeconds` (the shared store that `POST /devices/{id}/telemetry` also writes, `DevicesController.cs:98`). The plan contradicts itself; parse `uptime_seconds` from the `status` dictionary instead. |
| `FailUnfinished` called from `Program.cs:157-165` | VERIFIED (`Program.cs:157-165`, before `app.Run()`) |
| `command_result` at `DeviceLinkHandlers.cs:38`, publishing at :174-208, `REALTIME_QUEUE_FULL` at :204-207 | VERIFIED |
| Only master-admin can grant `sys.reboot` (`DevicePermissionCodes.cs:21-28`; `DeviceAccessHandlers.cs:318-326`, :366-369) | VERIFIED |

### Phase 1
| Claim | Result |
|---|---|
| `ConfigWriteActions` at `PinnedCommandCatalog.cs:98-106` | VERIFIED |
| `ConfigCommandPolicy` switch at :36-70, gate doc at :8-16 | VERIFIED |
| `CommandPayloadRules.HasKeys`/`TryInteger` at :14-40 reject strings, fractions and booleans | VERIFIED (`CommandPayloadRules.cs:14-40`) |
| `MaxPhysicalCommandTtlMs` at :22-24, cap at :133 | VERIFIED |
| Heartbeat uptime decrease marks DO stale (`DeviceLinkHandlers.cs:247-253`); connectivity `offline` does too (:262-263) | VERIFIED |
| Detail ≤ 1024 (`PersistenceEntities.cs:94`) | VERIFIED |
| DI: registry singleton :38; correlator and inbound scoped :46-47; transport scoped :57 | VERIFIED (`DependencyInjection.cs:38`, `:46-47`, `:57`; the transport is registered only under `enableLiveMqtt`) |
| `CatalogTests.cs:138-161` (33 at :155, reboot denied at :160), :164-187 (closed list at :175, D/F at :185-186) | VERIFIED |
| `ConfigCommandPolicyTests.cs:135-146`, `InlineData` reboot at :141 | VERIFIED |
| `DeviceLinkTests.cs:119-136` (:131 expects `CONFIG_ACTION_UNSUPPORTED`); `Link` fixture :354-371 | VERIFIED |
| `DeviceCommandAuthorizationTests.cs:196-214`, 24 rows | VERIFIED |
| `DeviceAccessHandlerTests.cs:569-573` `HIGH_RISK_REQUIRES_MASTER_ADMIN` | VERIFIED |
| "row đã `DEVICE_REVOKED` giữ nguyên" when a device is revoked during tracking (test 6) | FAILED as a scenario. Revoke flips only `IN_FLIGHT` rows (`EfCommandAuditStore.cs:28-41`, called from `PermissionContracts.cs:243`). A `SCHEDULED` row is `Executed`, so revoke never produces `DEVICE_REVOKED` on it. The test can only be built by hand (a phantom test), and the sweep will in fact overwrite the row to `NOT_OBSERVED`. |

### Phase 2
| Claim | Result |
|---|---|
| `sendCommand` body at `adminApi.ts:234` has `dryRun` but no `confirm` | VERIFIED |
| `CommandCatalogAction` at `adminApi.ts:63-74` has no `class`; server sends `Class` as a string enum (`MeController.cs:65`, `Program.cs:27-29`) | VERIFIED |
| `commandResult` at `adminApi.ts:248-249` | VERIFIED |
| `DeviceCommandModal.vue:138-143` builds `confirm_token` from `props.deviceId`; `sms.list` at :144-145; `catalogActions` at :74-75 | VERIFIED |
| `DangerConfirmDialog` has no slot; phrase check at :44-47 uses `trim` | VERIFIED (`grep "<slot"` finds nothing; `:44-47`) |
| `RETRY_DELAY_MS` at `useDeviceReads.ts:25` | VERIFIED |
| `configWriteSpecs.ts:33-35` note; `DeviceReadCards.test.ts:496` test name | VERIFIED |
| `readCardContext` at `DeviceDetailView.vue:344-351`; `command_result` branch at :284-287 | VERIFIED |

### Phase 3
| Claim | Result |
|---|---|
| Simulator handlers receive only `data.data` (:247-248); `get_info` has no `uptime_seconds` (:115); `get_status` has no `forced`/`enabled`/`gpioOk` (:71-73); no LWT (:207-211); uptime from `startedAt` (:213-226) | VERIFIED (`spec-device-simulator.js:71-73`, `:115`, `:207-213`, `:224`, `:247-248`) |
| `SIM_SILENT` simulates "no reply" | PARTIAL: it also skips the handler (`:245`), so the simulated device never reboots (Finding 1) |
| Tool pattern `device-config-check.js` login via `tool-env.js`, cookie and CSRF | VERIFIED (`tools/device-config-check.js:17-59`) |
| `tools/device-reads/` is gitignored | VERIFIED (`.gitignore:18`) |
| `GET /devices/{id}/telemetry` at `DevicesController.cs:79-94` | VERIFIED (`:79`) |

## Contract consumers (Contract Verifier)

- **`DeviceCommandResponseCorrelator` constructor** (new optional parameter). Consumers: DI (`DependencyInjection.cs:46`) and the hand-built test fixtures (`DeviceLinkTests.cs:367` and `DeviceOutputStateTests.cs`). Nothing breaks, because the parameter is optional, and that same optionality is the fail-open path (Finding 7).
- **`MqttDeviceCommandTransport` constructor.** 13 constructions in the unit tests (`DeviceLaneSafetyTests.cs` ×11, `PhysicalOutputSafetyTests.cs` ×2) and one DI registration (`DependencyInjection.cs:57`). None of them will pass a tracker unless the tests are edited.
- **`ICommandAuditStore.FailUnfinished`.** Two implementations: `EfCommandAuditStore.cs:44-71` and `InMemoryCommandAuditStore` (`CommandContracts.cs:314-336`). One caller: `Program.cs:157-165`. The plan covers both implementations.
- **`command_result` event payload** (new codes under the same keys). Consumers:
  - `DeviceDetailView.vue:284-287` (`noteConfigChange`);
  - the phase 2 panel;
  - any client of the hub.
  The shape does not change. Repeated events for one `correlationId` are new behaviour. `noteConfigChange` ignores reboot (it is not in `CONFIG_WRITE_CARD`), so it is safe.
- **`CommandCatalogAction.class` (frontend type).** Consumers: `DeviceCommandModal.vue` (new filter) and the phase 2 panel. The server already sends it.

Status: DONE_WITH_CONCERNS
Summary: Eight findings (3 High, 5 Medium). The main risks: the reboot gate is armed only on the happy path and no simulator mode reproduces "rebooted without a reply"; the live gateway run does not check staged firmware, MQTT trial, clean_session or rule state, and has no restore step; the live negative probes are not pinned to shapes that stay harmless if the server gate fails.
Concerns/Blockers: Some of the risks depend on spec gaps the plan already lists (reply ordering for `delay_ms:0`, how dry-run counts toward the throttle, the reboot reply shape). The fixes above are written to be safe whichever way those gaps are answered.
