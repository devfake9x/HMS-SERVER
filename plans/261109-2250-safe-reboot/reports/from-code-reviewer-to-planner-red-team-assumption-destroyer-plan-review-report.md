# Red-team review: safe reboot plan (Assumption Destroyer)

- Plan: `plans/261109-2250-safe-reboot/` (plan.md, phase-01..03), brainstorm `plans/reports/brainstorm-261109-2244-safe-reboot.md`
- Tree: worktree `ck-project-management-review-dd19d6` @ `45f5c14` (plan files untracked). Spec read via `git -C HMS-IDF show HEAD:engineering-docs/api/mqtt-api.md` (HEAD `9bfc16854`). No firmware source opened, no broker/device/API contacted.
- Tier: Standard (Fact Checker + Contract Verifier).

## Finding 1: The spec already answers three of the "spec gaps", and the simulator design contradicts that answer
- **Severity:** High
- **Location:** plan.md "Spec gap" items 1-3 and Open question 5; Phase 3 "Simulator / system.reboot" and gateway scenario step 3; Phase 2 "Luồng" step 1
- **Flaw:** The plan says the dry-run reply shape is undefined, and asks whether dry-run counts toward the 60 s throttle and whether it checks `confirm`. Spec §4.2 L551 already answers all three: "Không dùng `dry_run` như bảo đảm thực thi: preview chạy trước confirm/dedup/safe-mode/throttle và không gọi handler, nên `would_apply:true` chỉ là preview descriptor." So:
  - a dry-run does not check the token;
  - it runs before throttle and safe mode;
  - the reply carries a `would_apply` descriptor.
  Phase 3 still builds the simulator to check `confirm` before `dry_run` and to reply `data:{dry_run:true, delay_ms}`. The simulator header says it exists to catch "a server that only agrees with itself" (`tools/mqtt-broker/spec-device-simulator.js:1-2`), but this design would put non-spec behaviour into it.
- **Failure scenario:**
  1. The user sends the firmware team three questions the spec already answers.
  2. On the simulator, a dry-run with a wrong token is refused, but on `gateway_870c` it succeeds. Simulator PASS then says nothing about the gateway.
  3. The UI presents "chạy thử" as a safety step. On the real device, a successful dry-run does not show the token, throttle or safe mode will pass, so the usual failure (`confirm_required` or `UNAUTHORIZED` on the real command) is not caught by it.
  4. Gateway step 3 ("Đợi ≥ 65 s") and Open question 5 rest on a throttle question the spec already settles.
- **Evidence:** spec L551 (quoted above). Spec L474: "`dry_run` | Chỉ 3 row hỗ trợ". Spec L571: one `UNAUTHORIZED` code covers dedup, safe mode and throttle. plan.md "Spec gap": "Reply dry-run: shape không có trong spec", "Dry-run và throttle 60 s (§3 L483) …", "Dry-run có kiểm `confirm` không". Phase 3: "root `confirm` khác `REBOOT-<device_id>` → envelope error `confirm_required`" is listed before "`dry_run:true` → reply `success:true`, `data:{dry_run:true, delay_ms}` (shape do simulator chọn, spec gap)".
- **Suggested fix:**
  - Cite L551 and drop the three gaps and Open question 5.
  - Simulator: evaluate `dry_run` first, with no confirm, throttle or `delay_ms` check, and reply `{would_apply:true,…}`.
  - Reword the UI and acceptance: the dry-run proves signature, key and clock only; the real command can still fail `confirm_required` or `UNAUTHORIZED`.
  - Keep the 65 s wait only as a guard against a previous real reboot.

## Finding 2: "Kênh force giữ mức" is not what §6.4 says; force-ON relays may drop during the boot
- **Severity:** High
- **Location:** plan.md Acceptance "UI" ("kênh force giữ mức"); Phase 2 `relaysAfterReboot` / panel slot ("DO4 đang force TẮT, giữ nguyên"), `checkAfterReboot`; Phase 3 `--after` ("kênh force giữ mức")
- **Flaw:** §6.4 says a forced channel takes its force level "ở lần ghi line đầu tiên" (at the first line write) after boot. It does not say the level is held through the reset. The same section says that on a service stop `do_deinit` writes OFF to every line, "nên relay đang force ON tắt trong lúc đó". The plan treats every forced channel as "giữ nguyên" (unchanged). It tests only a force-OFF channel (DO4 `forced:true,state:false`), so the force-ON wording is never checked.
- **Failure scenario:**
  1. DO2 is force-ON and drives a pump or interlock.
  2. The dialog lists it under "kênh force giữ nguyên", so the operator confirms.
  3. During the boot window (8 s on the simulator, unknown on the gateway) the line sits at its reset state, then returns to ON at the first line write.
  4. The equipment drops out, though the safety dialog promised it would not.
  5. `checkAfterReboot` reads only after boot, so it cannot see the gap. It also never checks that a forced channel came back at its force level.
- **Evidence:** spec L870 "kênh đang force lấy mức force"; L876 "TOGGLE đang force | mức force, ở lần ghi line đầu tiên"; L866 "lúc dừng `do_deinit` ghi OFF mọi line, nên relay đang force ON tắt trong lúc đó". Phase 2 test 1 uses only "DO4 `forced:true,state:false` → `keepForced`". plan.md: "hộp xác nhận liệt kê relay đang BẬT sẽ TẮT và kênh force giữ mức".
- **Suggested fix:**
  - Split `keepForced` into force-OFF ("giữ TẮT") and force-ON ("có thể TẮT trong lúc khởi động, BẬT lại theo force khi thiết bị lên").
  - Add a force-ON fixture to the tests.
  - Make `checkAfterReboot` also flag forced channels that are not at their force level.
  - Add this to the firmware spec-gap list: is a force-ON line held across `esp_restart`?

## Finding 3: The TDD tests for the safety gate cannot fail when the gate is broken
- **Severity:** High
- **Location:** Phase 1 "Tests Before" items 7, 8 and 9; plan.md Test matrix
- **Flaw:**
  - **(a) Phantom race test.** Item 7, "10 reboot song song, tracker `Start` giữa chừng → tối đa 1 publish", passes without the gate. `Start` does not release the lane; only `Remove` does. The existing lane already lets exactly one of 10 concurrent writes publish. The interleaving that Decision 9 argues about (reply-side `Start` then `Remove`, against the second command's `TryRegisterWrite` then `IsTracking`) is not exercised.
  - **(b) DI wiring of the gate is never tested.** The tracker is an optional `= null` parameter on the transport. The transport is registered only when `enableLiveMqtt` is true, and no test sets it. The DI test in item 9 only checks that two scopes return the same tracker instance. If the transport is built without the tracker, the IN_PROGRESS gate is silently off (fail-open), and every test still passes.
  - **(c) The production database path is not tested.** The "integration" `FailUnfinished` test sits next to a test that uses `UseInMemoryDatabase`, so only the in-memory branch runs. The new relational `ExecuteUpdate` rule (`Code == Scheduled`, keep `Status`) that Postgres runs has no test. The same is true of the relational `RecordAsync` overwrite that `CompleteRebootAsync` depends on.
- **Failure scenario:**
  - A later change drops `reboots` from the transport constructor, or registers the tracker under `if (enableLiveMqtt)` after the transport. All tests stay green and a second reboot goes out while the first is still being followed.
  - A typo in the relational `ExecuteUpdate` predicate leaves `SCHEDULED` rows unfinished forever on Postgres, and all tests stay green.
- **Evidence:**
  - `tests/Hms.Server.UnitTests/DeviceLaneSafetyTests.cs:152-169` (`A_burst_of_config_writes_publishes_exactly_one`: one publish, nine `WriteBusy`). `DeviceLinkContracts.cs:332-333`: any pending write gives `WriteBusy`.
  - `src/Hms.Server.Infrastructure/DependencyInjection.cs:51-58`: the transport is registered only `if (enableLiveMqtt)`. Grepping tests for `enableLiveMqtt` finds nothing; `MqttDeviceCommandTransport` is only ever built by hand (`DeviceLaneSafetyTests.cs:38,69,143,157`).
  - `tests/Hms.Server.IntegrationTests/PersistenceModelTests.cs:249-251` (`UseInMemoryDatabase`); Npgsql appears only in the model-snapshot test (:78-80). The relational branch is `EfCommandAuditStore.cs:50-58` and :150-160.
- **Suggested fix:**
  - (a) Write a deterministic interleaving test. Wrap the registry, and inside `Remove` of command 1 run command 2's `SendAsync`. Expect `DEVICE_REBOOT_IN_PROGRESS`. Then a mutant that moves `Start` after `Remove`, or drops the gate, must fail it.
  - (b) Make the tracker a required parameter on the transport, or add a DI test with `AddHmsInfrastructure(enableLiveMqtt: true, …)` that resolves `IDeviceCommandTransport` in a scope and blocks a reboot while tracking.
  - (c) Either state that the relational branch is review-only, or run it against a real Postgres. Do not call the in-memory-provider test "integration coverage".

## Finding 4: Completing a reboot writes the audit row with no state check; a lost race or a revoke leaves a false final result
- **Severity:** Medium
- **Location:** Phase 1 "Correlator" (`Start` before `pending.Remove`, `CompleteRebootAsync`), "Inbound" (revoked note), Tests Before 6 (revoke case)
- **Flaw:**
  - **(a) Lost race leaves a tracker behind.** `Find` runs outside the lane lock. The sweep's `TakeExpired` can remove the same entry between `Find` and `Remove`. The plan calls `Start` first and has no rollback when `Remove` returns false. A tracker is left running for a command already recorded as `Failed/DEVICE_RESPONSE_TIMEOUT`.
  - **(b) The overwrite is unconditional.** `CompleteRebootAsync` calls `RecordAsync`, which overwrites Status, Code and Detail for any row except `DEVICE_REVOKED`. A late tracker therefore turns `Failed/DEVICE_RESPONSE_TIMEOUT` into `Executed/DEVICE_REBOOT_OBSERVED` or `Executed/DEVICE_REBOOT_NOT_OBSERVED`.
  - **(c) The revoke test is self-contradictory.** It expects "sweep ghi `NOT_OBSERVED`; row đã `DEVICE_REVOKED` giữ nguyên". But revoke only cancels `IN_FLIGHT` / unfinished rows, so a `SCHEDULED` (`Executed`) row is never `DEVICE_REVOKED`. The real outcome is that a revoked device ends as `NOT_OBSERVED`, which the UI shows as "Chưa thấy thiết bị lên lại … kiểm tra thiết bị tại chỗ". That sends someone to the lab for a device the server is merely ignoring.
- **Failure scenario:**
  1. A reply arrives at the 30 s deadline, when the device ran a 10 s-TTL command late because its clock is invalid (spec L481).
  2. The sweep records a timeout and frees the lane.
  3. The correlator has already called `Start`, then its `Remove` returns false.
  4. For 122 s every reboot is refused with `DEVICE_REBOOT_IN_PROGRESS`, and the audit row then flips from Failed to Executed.
- **Evidence:**
  - `DeviceLinkHandlers.cs:53` (`pending.Find` with no lock), :86-87 (`if (!pending.Remove(...)) return Ignored`).
  - `DeviceLinkContracts.cs:356-360` (Find and Remove are lock-free), :362-371 (`TakeExpired` removes under `_laneGate`).
  - `EfCommandAuditStore.cs:152-160`: the only fence is `x.Code != "DEVICE_REVOKED"`.
  - Revoke: `EfCommandAuditStore.cs:28-42` (`Status == "IN_FLIGHT"` only); `CommandContracts.cs:298-299` (skips rows with a result).
- **Suggested fix:**
  - Call `Start` only after `Remove` returns true. Close the gap with a tracker "pending start" entry claimed under the same lock, or undo `Start` when `Remove` fails.
  - Make `CompleteRebootAsync` a compare-and-set (`WHERE Code = 'DEVICE_REBOOT_SCHEDULED'`).
  - Give a revoked device a distinct terminal code (or no NOT_OBSERVED at all), and fix the test text.

## Finding 5: The final audit result and the release of the reboot gate rest on an unauthenticated heartbeat
- **Severity:** Medium
- **Location:** plan.md Decision 6 ("Bằng chứng chính"); Phase 1 Tracker `ObserveHeartbeat`; Phase 2 post-OBSERVED reads
- **Flaw:**
  - `B/status` is unsigned. The lab broker allows anonymous clients, and the code base itself states that broker identity is not proof of the device.
  - The plan lets a single heartbeat with a lower `uptime_seconds` write the final audit result `DEVICE_REBOOT_OBSERVED` and end tracking, which releases the `DEVICE_REBOOT_IN_PROGRESS` gate.
  - The UI then runs a signed `system.get_info`, but the plan only displays its uptime. It never compares it with `uptime_before`.
- **Failure scenario:**
  1. Any broker client publishes `{"uptime_seconds":0}` on `hms_toan_lab/gateway_870c/status` two seconds after the reply.
  2. The audit says "đã khởi động lại" and the UI shows "Đã khởi động lại", even if the scheduled reboot never ran.
  3. The gate opens, so a second reboot can go out while the first is still scheduled. The only remaining barrier is the device throttle, whose behaviour across a reboot is unknown.
- **Evidence:** `DeviceLinkHandlers.cs:211-214` ("the broker identity of a message is not proof of the device's identity"); `docs/architecture-mqtt-device-link.md:67` ("Broker lab cho phép anonymous, nên identity trên broker không phải bằng chứng"), :241; spec L1333 ("Không coi bất kỳ JSON trên topic tự chọn là đã được xác thực … ACL và TLS broker vẫn cần thiết").
- **Suggested fix:**
  - Treat the heartbeat as a trigger only.
  - Record `OBSERVED` only after a signed `system.get_info` whose `uptime_seconds` is lower than `uptime_before`. That read can be server-initiated, or come from the UI's own read reported back.
  - Otherwise, write `OBSERVED_UNVERIFIED` and keep the gate closed until the signed read.
  - At the least, the UI must compare the signed uptime and say "chưa xác minh" when it disagrees.

## Finding 6: Relay state stays "verified" for the whole boot window; the simulator hides this
- **Severity:** Medium
- **Location:** Phase 1 "Phần có sẵn" (heartbeat/connectivity mark-stale treated as sufficient); Phase 3 simulator ("huỷ socket không gửi DISCONNECT (để broker phát LWT)")
- **Flaw:** The reply projection returns early for every class except Physical. A `SCHEDULED` reboot (class F) therefore does not mark the outputs stale, even though the plan itself says the non-forced relays will turn OFF. Staleness comes only from the first post-boot heartbeat or an `offline` connectivity message.
  - On the real gateway, a chip reset sends no TCP FIN. The broker publishes the LWT only after 1.5 × keepalive, or at session takeover when the device reconnects. LWT is also optional ("khi connectivity/LWT được bật").
  - The simulator, which destroys its socket, produces an immediate LWT. So phase 3 shows prompt `offline` that the gateway will not.
- **Failure scenario:**
  1. The reboot reply arrives, the relays turn OFF on the hardware, and the device boots.
  2. For the whole boot, `GET /devices/{id}/outputs` and every viewer's DO panel show "DO1 ON" as fresh and verified.
  3. A second operator decides from that view.
  4. With `NOT_OBSERVED` and no LWT, the stale "ON" is never flagged.
- **Evidence:** `src/Hms.Server.Application/DeviceLink/DeviceOutputState.cs:101-102` (`if (plan.Definition.Class != CommandClass.Physical) return;`); `DeviceLinkHandlers.cs:251-253`, :262-263 (the only other stale triggers); `DeviceLinkController.cs:38-55` (serves `stale` to `gpio.do_view` holders); spec L448 (LWT conditional); `spec-device-simulator.js:207-211` (no will today, `clean: true`).
- **Suggested fix:**
  - Mark outputs stale on `SCHEDULED` with a new reason such as `device_reboot_scheduled`, and add a test.
  - In the simulator, model the real device: drop the connection without closing it, or delay the will. Otherwise state in the live report that simulator `offline` timing does not transfer to the gateway.
  - Also: with mqtt.js 5 the default `reconnectPeriod` is 1000 ms, so a "boot" made by destroying the socket lasts about 1 s, not `SIM_BOOT_SECONDS`, unless reconnect is disabled. `startedAt` is also `const` (`spec-device-simulator.js:213`). (Library defaults UNVERIFIED in this worktree: `node_modules` is absent.)

## Finding 7: The post-reboot check (the plan's main acceptance proof) quietly does nothing in its most likely conditions
- **Severity:** Medium
- **Location:** Phase 2 "Luồng" step 4 and `checkAfterReboot`; plan.md Acceptance "Simulator và gateway_870c … đọc lại thấy relay không force đã OFF"
- **Flaw:** The reads run right after the first post-boot heartbeat, which is when the DO driver is least likely to be ready. The plan retries only the server-side `DEVICE_READ_BUSY`, three times.
  - The spec says `control.get_status` "có thể từ chối `busy`", meaning the driver is not ready, with `retry_after_ms`.
  - In safe mode only `status.*` and `system.ping/get_info/get_heap` are admitted.
  - The existing read composable that the plan claims to copy also retries `DEVICE_REJECTED: LANE_FULL`; the plan's rule does not.
- **Failure scenario:**
  1. The first `control.get_status` after boot returns `DEVICE_REJECTED` with "busy", or `UNAUTHORIZED` in safe mode.
  2. The UI shows "Đã khởi động lại" with no DO table and no §6.4 check.
  3. Acceptance item 4 ("đọc lại … thấy relay không force đã OFF") is marked PASS on uptime alone, or the run stalls.
- **Evidence:** spec L797 ("có thể từ chối `busy`"), L860 ("`busy` (+`retry_after_ms`) | driver chưa sẵn sàng"), L483 (safe-mode allowlist); `frontend/src/core/commands/useDeviceReads.ts:30-33` (`isBusy` includes `LANE_FULL`); Phase 2: "mỗi lệnh đọc bị `DEVICE_READ_BUSY` thì thử lại sau 2 s, tối đa 3 lần (giống `useDeviceReads`)".
- **Suggested fix:**
  - Retry on device `busy` (honour `retry_after_ms`) and on `LANE_FULL`, for a bounded time of about 30 s.
  - Show "chưa kiểm được trạng thái relay" as its own outcome.
  - In acceptance, require the DO table to be read, not just the uptime.

## Finding 8: The relay list in the confirm dialog can go stale while the dialog is open, and confirming does not re-check it
- **Severity:** Medium
- **Location:** Phase 2 "Panel" ("Bấm nút: … đọc trước (một lần) … rồi mở `DangerConfirmDialog`"); Risk table "Danh sách relay cũ so với lúc bấm"
- **Flaw:**
  - The relay list and the gate are computed once, when the dialog opens.
  - The page already receives an `outputs` realtime event when anyone changes a relay, but the plan routes it only to `refreshOutputs()`, not to the open dialog.
  - `DangerConfirmDialog.onConfirm` emits with no re-validation hook.
  - The listed mitigation ("ghi rõ thời điểm đọc") does not bound how long the dialog stays open.
  - The 2026-11-06 config-card review found this same defect (gate checked only when the confirm opened).
- **Failure scenario:**
  1. The dialog says "Không có relay nào đang BẬT".
  2. A second operator turns DO2 ON from the DO panel.
  3. The first operator types the phrase and confirms.
  4. DO2 goes OFF on reboot without anyone having been warned.
- **Evidence:** `frontend/src/features/devices/views/DeviceDetailView.vue:278-282` (`outputs` event only calls `refreshOutputs()`); `frontend/src/shared/ui/feedback/DangerConfirmDialog.vue:54-58` (no re-check on confirm).
- **Suggested fix:**
  - On an `outputs` event for this device while the dialog is open, re-read `control.get_status` and re-render the list, or close the dialog with "trạng thái relay đã đổi".
  - Re-check `rebootGate` and the age of the read (for example ≤ 15 s) when the operator clicks confirm, before the dry-run.
  - Add a vitest for "open, then the outputs event arrives, then confirm".

## Finding 9: The 120 s window is unmeasured, and NOT_OBSERVED is final even if the device comes back later
- **Severity:** Medium
- **Location:** plan.md Decision 6 ("Hạn: … + 120 s"); Phase 1 Tracker tests ("`TakeExpired` … heartbeat sau đó không trả gì"; "chưa có heartbeat trước `Start` → heartbeat đầu chỉ đặt mốc"); Phase 2 `NOT_OBSERVED` message
- **Flaw:**
  - Nothing in the spec, the repo or past live sessions bounds how long the gateway takes to reconnect. The plan lists it as a gap ("thời gian tối đa để kết nối lại"), yet hard-codes 120 s.
  - The gateway has Ethernet and a 4G modem, and the spec describes uplink failover phases with `failover_time_ms`.
  - Once the sweep takes the record, a heartbeat that arrives later is ignored, so the audit stays `NOT_OBSERVED` permanently. Meanwhile the existing code marks the relays `device_rebooted` for the same heartbeat.
  - The same false negative happens when the tracker has no baseline: for example, the server restarted and the first heartbeat seen carries no `uptime_seconds`.
  - The UI message then sends a person on site.
- **Failure scenario:**
  1. The gateway comes back on 4G after 140 s.
  2. The audit is `NOT_OBSERVED` forever and the UI says "kiểm tra thiết bị tại chỗ".
  3. Gateway acceptance step 5 ("UI 'Đã khởi động lại'") fails, and the plan has no fallback except a manual check.
- **Evidence:** plan.md "Spec gap: Thứ tự khi lên lại … thời gian tối đa để kết nối lại"; `docs/journals/261001-maitech-rtu-hardware-integration.md:73` (Ethernet plus "4G Viettel"); `DeviceLinkHandlers.cs:251-253` (the same heartbeat marks outputs stale, but the plan's tracker no longer matches it).
- **Suggested fix:**
  - Keep the device's tracker record (or just `uptime_before`) for a longer grace period, for example 10 min.
  - Let a late uptime drop upgrade `NOT_OBSERVED` to `DEVICE_REBOOT_OBSERVED_LATE`, and send a `command_result`.
  - Word the UI as "chưa thấy … trong 2 phút; server vẫn chờ".
  - Measure the gateway's boot-to-heartbeat time in phase 3 before treating 120 s as final.

## Fact check

### Phase 1 / plan.md
| Claim | Result |
|---|---|
| Flash class falls to `_ => false` in the switch | VERIFIED `PinnedCommandCatalog.cs:236-242` (default at :241) |
| `ConfigWriteActions` at :98-106 | VERIFIED `PinnedCommandCatalog.cs:98-106` |
| reboot row: F, `sys.reboot`, 60000, `REBOOT-<DEVID>`, dry_run true | VERIFIED `command-catalog.json:1011-1021` |
| Other F rows at :814-822, :847-855, :924-932 | VERIFIED `command-catalog.json:815`, :848, :925 |
| `ConfigCommandPolicy` gates W/C/D/F and fails closed | VERIFIED `ConfigCommandPolicy.cs:8-12`, :31-78 |
| `TryInteger` rejects strings, fractions and booleans | VERIFIED `CommandPayloadRules.cs:18-40` |
| TTL cap: Physical only gets 10 s; others 20 s | VERIFIED `SendDeviceCommandHandler.cs:20`, :24, :133 |
| Writes signed with server time | VERIFIED `SendDeviceCommandHandler.cs:144-145` |
| Adapter `CONFIRM_MISMATCH` / `DRY_RUN_UNSUPPORTED` / policy / key | VERIFIED `HmsIdfCommandProtocolAdapter.cs:46-53`, :55-56, :62-64, :66-84 |
| Codec writes root `confirm` / `dry_run` | VERIFIED `HmsIdfCommandCodec.cs:58-61` |
| `HoldWriteLane` before `Remove` at :81-86 | VERIFIED `DeviceLinkHandlers.cs:81-87` |
| Heartbeat parses `uptime_seconds` | VERIFIED `DeviceTelemetryContracts.cs:47-53`; read at `DeviceLinkHandlers.cs:248-252` |
| Decision 6: "tracker không đọc `IDeviceTelemetryStore`" | FAILED: the Phase 1 Inbound wiring passes `telemetry.GetLatest(deviceId)?.UptimeSeconds` into `ObserveHeartbeat` |
| Sweep every 2 s, and it also runs while the broker is down | VERIFIED `MqttDeviceLinkService.cs:26`, :117-136, :290-295 (5 s plus up to a 10 s connect timeout while down, :134-136; `WithTimeout(10)`) |
| Inbound handled one message at a time | VERIFIED `MqttDeviceLinkService.cs:321-337`, channel `SingleReader` :33-34 |
| `RecordAsync` overwrites Code/Detail for unrevoked rows | VERIFIED `EfCommandAuditStore.cs:149-160` (Status is overwritten too) |
| "store không ghi đè DEVICE_REVOKED" protects SCHEDULED rows | FAILED: revoke touches only `IN_FLIGHT` rows (`EfCommandAuditStore.cs:30-31`; `CommandContracts.cs:298-299`) |
| `FailUnfinished` locations; Program logs only | VERIFIED `EfCommandAuditStore.cs:44-71`, `CommandContracts.cs:314-336`, `Program.cs:156-164` |
| "integration (PersistenceModelTests) … với EfCommandAuditStore" covers the relational path | FAILED: `UseInMemoryDatabase` (`PersistenceModelTests.cs:249-251`) |
| TTL test: 24 rows with TTL 60000 | VERIFIED `DeviceCommandAuthorizationTests.cs:196-214`; 24 rows by count; F rows with 60000: reboot, factory_reset, ota.apply |
| `/me` catalog `available` set | VERIFIED `ManagementApiTests.cs:195-204` |
| Non-master admin cannot grant `sys.reboot` | VERIFIED `DeviceAccessHandlerTests.cs:569-573`, `DevicePermissionCodes.cs:21-28`, `DeviceAccessHandlers.cs:322-326`, :366-369 |
| Correlator, inbound and transport are scoped; the transport only with live MQTT | VERIFIED `DependencyInjection.cs:46-47`, :51-58 |

### Phase 2
| Claim | Result |
|---|---|
| `sendCommand` body has `dryRun?` but no `confirm` | VERIFIED `adminApi.ts:234` |
| `CommandCatalogAction` has no `class`; the server sends `class` as an enum string | VERIFIED `adminApi.ts:63-74`; `MeController.cs:63`; `ManagementApiTests.cs:223`; `Program.cs:27-29` |
| `DeviceCommandBody` at :8-15 | VERIFIED `useDeviceCommand.ts:8-15` |
| `runDeviceWrite` sends once and classifies `Executed` → `executed` | VERIFIED `useDeviceWrite.ts:138-176` |
| `ContractUnavailable` (IN_PROGRESS) → `not_sent` | VERIFIED `useDeviceCommand.ts:39-42`, :61-63 |
| `binding.mqttDeviceId` in `/link` | VERIFIED `adminApi.ts:102-118`; `DeviceLinkController.cs:57-83` (needs org scope; the device page uses org-scoped `listDevices`, so membership-only users never reach it) |
| `configGate` pattern | VERIFIED `reads/configGate.ts:38-56` |
| Retry "giống useDeviceReads" | FAILED: `useDeviceReads.ts:30-33` also retries `LANE_FULL` |
| `DangerConfirmDialog` has no slot; phrase compared after `trim` | VERIFIED `DangerConfirmDialog.vue:44-47`, :61-116 |
| C2 modal `confirm_token` branches and `sms.list` | VERIFIED `DeviceCommandModal.vue:138-145`; `catalogActions` :74-75; `isDestructive` :102-107 |
| DeviceDetailView hooks | VERIFIED `DeviceDetailView.vue:276-296`, :284-287, :344-351, :355-365, column 1 :895-930 |
| `reboot_recommended` note and test | VERIFIED `configWriteSpecs.ts:33-35`; `DeviceReadCards.test.ts:496` |

### Phase 3
| Claim | Result |
|---|---|
| Simulator handlers get only `data.data` | VERIFIED `spec-device-simulator.js:247-248` |
| `get_info` has no uptime; DO view has no `forced`/`enabled`/`gpioOk`; no LWT; uptime from `startedAt` | VERIFIED :115, :71-73, :207-211, :213-226 |
| `SIM_SILENT` exists | VERIFIED :14-15 |
| `device-config-check.js` template parts | VERIFIED `tool-env.js:19-24`; `device-config-check.js:35-59`, :62-67 |
| `tools/device-reads/` is gitignored | VERIFIED `.gitignore:18` |
| Telemetry GET | VERIFIED `DevicesController.cs:79-94` |
| Doc anchors | VERIFIED `architecture-mqtt-device-link.md:66`, :166, :241, :461; `architecture-device-mqtt-api-catalog.md:162`, :307-309; `project-roadmap.md:102-115` |
| Simulator dry-run order and reply shape follow the spec | FAILED: spec L551 |

### Spec (HEAD 9bfc168)
| Claim | Result |
|---|---|
| §6.1 L604 row | VERIFIED |
| §7 L1087 and L1093 | VERIFIED |
| §11 L1363 | VERIFIED |
| §3 L481 (clock), L483 (throttle) | VERIFIED |
| §2 L455 (dedup) | VERIFIED |
| `B/status` 1/false, 3000 ms | VERIFIED L446 |
| Connectivity retained/LWT "khi … được bật" | VERIFIED L448 (LWT optional) |
| §5 L571 (shared `UNAUTHORIZED`) | VERIFIED |
| §6.4 "DO khi khởi động" L868-882 | VERIFIED |
| "kênh force giữ mức" | FAILED (L876) |
| §10 L1318: heartbeat = `status.dump` | VERIFIED (`uptime_seconds` at L610) |
| §10 L1333 and L1340 | VERIFIED |
| R10 `clean_session` | VERIFIED L414 |
| Gaps "shape không có / throttle? / confirm?" | FAILED (L551) |
| Reboot release status | Not stated by the plan: `system.reboot` has no SLICE_PASS or HIL evidence in the spec's campaign tables (L34-88; L2149 "No reboot … case ran"). Opening it is ahead of the firmware campaign, like `set_output`. |

## Contract Verifier (consumers of interfaces the plan changes)

- **`command_result`.** One producer: `DeviceLinkHandlers.cs:38`, :174-208. One frontend consumer: `DeviceDetailView.vue:284`. None in `tools/`. The plan covers the consumer.
- **Audit `Code` overwrite after `Executed`.**
  - Readers: `GET /commands/{id}` (`DeviceCommandsController.cs:96`) and the idempotent replay (`SendDeviceCommandHandler.cs:63`, :126-127, :162-166). Both return the current code, so neither treats `Executed` as final. OK.
  - `runDeviceCommand` stops at the first status that is not `Published` (`useDeviceCommand.ts:68-70`, :105). That is why the plan's second poll loop is required. OK.
- **`FailUnfinished`.** Four call sites:
  - `Program.cs:162`.
  - `PersistenceModelTests.cs:276` (expects 2).
  - `PhysicalOutputSafetyTests.cs:253` (expects 2, in-memory). Not listed by the plan; it stays green only while it seeds no `SCHEDULED` row.
  - `DeviceOutputStateTests.cs:218` (wrapper; stays fine while the interface signature is unchanged).
- **`DangerConfirmDialog`.** Two users: `DeviceListView.vue:680` and `DeviceCommandModal.vue:431`. New props with defaults leave both unchanged.
- **The "33" action count.** Four places:
  - `CatalogTests.cs:155` (planned).
  - `docs/project-roadmap.md:114` (planned).
  - `docs/architecture-mqtt-device-link.md:61` (missed; Phase 3 lists only :66).
  - `docs/architecture-device-mqtt-api-catalog.md:137` (missed).
- **Tests that assert class F is closed or reboot is refused.**
  - Listed by the plan: `CatalogTests.cs:160`, :175, :185-186; `ConfigCommandPolicyTests.cs:141`; `DeviceLinkTests.cs:131`; `ManagementApiTests.cs:196-203`.
  - Unaffected: `DeviceCommandAuthorizationTests.cs:121-128`, :144-150 and :152-163 (all-enabled test catalog, publishable adapter); `ManagementApiTests.cs:1288-1322` (overrides the catalog itself); `CommandPermissionResolverTests.cs:143` (high-risk list, unchanged).
- **Optional `reboots = null` parameter.** Three constructors: the correlator, the inbound handler and the transport. The tests that build them by position (`DeviceLinkTests.cs:363-366`, `DeviceLaneSafetyTests.cs:38`, …) still compile. No test resolves the transport from DI (Finding 3b).

Status: DONE_WITH_CONCERNS
Summary: 9 findings (3 High, 6 Medium). The biggest are: spec L551 already answers three "spec gaps" and contradicts the planned simulator; the "force holds its level" promise in the confirm dialog is not backed by §6.4; and the TDD tests for the reboot gate and the relational audit path cannot go red.
Concerns/Blockers: The mqtt.js reconnect default is unverified (no node_modules in the worktree). Whether a force-ON line glitches during `esp_restart` is inferred from §6.4 wording and should go to the firmware team as a question.
