# Red-team plan review: Security Adversary, remote config (System & ADC)

- Reviewer: code-reviewer (Security Adversary perspective; Fact Checker + Contract Verifier roles)
- Plan: `plans/261106-2033-remote-config-system-adc/` (plan.md, phase-01..03) + brainstorm `plans/reports/261106-2029-brainstorm-remote-config-system-adc.md`
- Contract: pinned `mqtt-api.md` @ 83e93d77e (scratchpad export; §0, §3, §5, §6.1, §6.2, §11 read). No firmware source opened. No device, broker or API contacted.
- Date: 2026-11-06

---

## Finding 1: `set_ntp_servers` + `sync_ntp` gives any `sys.time_edit` holder the device clock, which is the replay and expiry defence for every signed command
- **Severity:** Critical
- **Location:** plan.md, sections "6 lệnh mở" (rows `system.set_ntp_servers`, `system.sync_ntp`), "Quyết định" (no HighRisk) and "Rủi ro còn lại" (NTP server sai). Phase 1, section "Kiểm payload" (`system.set_ntp_servers` rule).
- **Flaw:** The plan treats NTP as a low-impact setting ("đồng hồ thiết bị trôi dần"). It is not. The device age-checks every command against its own clock, and NTP is unauthenticated. Phase 1 accepts any hostname or dotted IPv4 as an NTP server: attacker domains, loopback, broadcast, multicast and LAN hosts all pass. The plan closes `system.set_time` because "nhảy đồng hồ làm lệnh sau bị `expired` hoặc `future_timestamp`" (plan.md:92). `set_ntp_servers(["ntp.attacker.example"])` followed by `sync_ntp` causes the same clock jump, and the attacker chooses the time. Both commands are opened under the same `sys.time_edit` code. Org admins can delegate that code because it is not HighRisk.
- **Failure scenario:**
  1. **Lockout.** A user holding `sys.time_edit` points NTP at a host that returns a time hours ahead and presses "Đồng bộ NTP". Every later signed command is then refused with `expired` or `future_timestamp` (spec §3, line 81). That includes the "Về tự động" revert and `sync_ntp`. The plan names "Về tự động" as the mitigation (plan.md:109), but it is refused by the same check. `set_time` is closed, so the server has no remote way back. This is the "lose the control path" outcome the plan uses to keep network/MQTT writes out of scope.
  2. **Replay.** The NTP host instead returns a time before 2020. Spec §3 (line 81, [M35] line 806): "Khi đồng hồ chưa hợp lệ, age check bỏ qua". Dedup holds only 128 IDs in RAM and is lost on reboot or eviction (line 55). Anyone who captured signed traffic on `B/cmd/#` can then replay an old signed command. Examples are a broker user with read ACL or the sniffer the plan itself runs on `hms_toan_lab/gateway_870c/cmd/#` (phase-03:53). A replayed `control.set_output {channel, value:true}` drives a relay. So a non-HighRisk code (`sys.time_edit`) defeats the one device-side protection for a HighRisk one (`gpio.do_write`, DevicePermissionCodes.cs:27).
- **Evidence:**
  - Spec line 81: `TTL xét khi command ra khỏi hàng đợi; đồng hồ thiết bị hợp lệ khi time(NULL)>1577836800 ... Khi đồng hồ chưa hợp lệ, age check bỏ qua, HMAC và cache dedup vẫn áp dụng.`
  - Spec line 55: `Thiết bị giữ dedup 128 ID trong RAM; ... reboot/eviction có thể quên ID.`
  - Spec line 201: `system.set_ntp_servers {servers:string[]}`, with no constraint on server identity.
  - `src/Hms.Server.Infrastructure/Commands/command-catalog.json`: `set_ntp_servers`, `sync_ntp`, `set_time` and `set_timezone` all use `required_perm: "sys.time_edit"`.
  - `src/Hms.Server.Domain/Authorization/DevicePermissionCodes.cs:21-28`: HighRisk does not contain `sys.time_edit`.
  - `src/Hms.Server.Application/Authorization/DeviceAccessHandlers.cs:322-326`: a non-master may delegate any non-HighRisk code they hold.
  - plan.md:44 rule "0..3 hostname hoặc IPv4"; plan.md:92 set_time rationale; plan.md:109 mitigation "có nút Về tự động"; plan.md:113 admits `sys.time_edit` is shared with set_time.
- **Suggested fix:** Pick one and record it as a user decision. The choice reverses the delegated "not HighRisk" call, so it needs the trade-off shown to the user.
  - (a) Keep `set_ntp_servers` closed alongside `set_time`, and open only `sync_ntp` against the device's current servers.
  - (b) Accept only entries from a master-admin-managed server-side NTP allowlist (for example a vendor or pool list) instead of free hostnames or IPs, and move `sys.time_edit` into `HighRisk`.
  - In both cases, after any time write, read back `system.get_time.epoch` and compare it with server UTC. If skew exceeds about 10 s, raise an alarm. Do not report "Đã ghi và xác nhận".
  - Fix the "Rủi ro còn lại" text: the revert button does not work once the clock is wrong.

## Finding 2: Opening the six writes activates permission codes already sitting in roles, and the only mitigation is documentation
- **Severity:** High
- **Location:** plan.md, sections "Quyết định" ("tài liệu liệt kê các role được ghi ngay") and "Rủi ro còn lại" ("Role cũ được ghi ngay"). Phase 3, section "Tài liệu" ("Role có thêm quyền ghi").
- **Flaw:** The permission vocabulary already includes the codes of closed commands. A role can therefore have been given `sys.time_edit`, `sys.hostname_edit`, `adc.config_edit` or `adc.enable` while those codes did nothing. Org admins could hand them out freely because none of them is HighRisk. Phase 1 turns them into live device-write power at deploy with no gate. The plan says it will "list the roles", but phase 3:69 only lists the four codes. No step inventories the role and membership rows that actually hold them, and the repo cannot do that because they live in the production DB.
- **Failure scenario:** Months ago, a tenant admin added `adc.enable` and `sys.time_edit` to a broad "Operator" role because the role editor offered them and they did nothing. On deploy, every member of that role can toggle ADC channels and repoint NTP (Finding 1) on every in-scope device, and nobody is told.
- **Evidence:**
  - `src/Hms.Server.Application/Commands/DevicePermissionVocabulary.cs:11-12`: `catalog.FirmwarePermissions.Concat(...)` takes every firmware code, whether or not its command is open.
  - `src/Hms.Server.Infrastructure/Commands/PinnedCommandCatalog.cs:129-133`: `FirmwarePermissions` is built from all rows.
  - `src/Hms.Server.Application/Authorization/DeviceAccessHandlers.cs:56-57, 96, 106, 365-368`: role create/update runs only the vocabulary and HighRisk checks.
  - `src/Hms.Server.Domain/Authorization/DevicePermissionCodes.cs:21-28`.
  - plan.md:35-36, 112; phase-03:69.
- **Suggested fix:**
  - Add a pre-deploy step: a read-only SQL or EF query, kept in `tools/`, that lists every role, membership and org holding any of the four codes. Get a master-admin sign-off on that list before the server deploys.
  - Or gate the opening behind a per-organization enablement flag that only a master-admin can set.
  - Write the inventory into the phase-3 doc in place of the bare list of codes.

## Finding 3: The audit records who wrote config but not what was written
- **Severity:** High
- **Location:** Brainstorm "Hiện trạng" ("PayloadSummary lưu nguyên payload", "Audit đã tách các class W/C/D/F") and "Server" ("Audit: payload của 6 lệnh không chứa secret, chưa cần redaction"). The plan never mentions audit.
- **Flaw:** The premise is false. `SummarizePayload` stores the payload only for classes B, D and F and returns `null` for W and C. All six new commands are W or C. Their audit rows will show `system.set_ntp_servers by user X` but not which server, hostname, timezone or channel name was set. In this system the device trusts whatever the server signs (R11), so the server audit is the only forensic record. No phase-1 test asserts audit content for a write.
- **Failure scenario:** After the lockout or replay in Finding 1, or a hostname collision (Finding 9), investigators can see that someone sent `set_ntp_servers`. They cannot see the hostile server name, cannot prove intent and cannot rebuild the previous config.
- **Evidence:**
  - `src/Hms.Server.Infrastructure/Persistence/EfCommandAuditStore.cs:218-223`: `if (plan.Definition.Class is not (Physical or Destructive or Flash)) return null;`
  - `EfCommandAuditStore.cs:99`: `PayloadSummary = SummarizePayload(plan)`.
  - Brainstorm lines 22, 32, 75.
- **Suggested fix:**
  - Store `PayloadSummary` for the six opened W/C actions. Use an allowlist per action rather than "all W/C", so later Wi-Fi or MQTT writes with passwords stay excluded until redaction exists.
  - Add a `ManagementApiTests` assertion that the audit row holds `{"servers":[...]}` / `{"hostname":...}`.
  - The 512-char cap (`PersistenceEntities.cs:95`) is enough for these payloads.

## Finding 4: The plan specifies "POST returns 403" for missing permission, but the API returns 200, and the UI's outcome mapping is built on that wrong assumption
- **Severity:** High
- **Location:** plan.md, section "Acceptance" → "Quyền" ("POST trả 403"). Phase 1, section "Tests first" item 6 ("POST set_hostname trả 403"). Phase 2, section `useDeviceWrite.ts` → "Phân loại kết quả" and "Kết quả" (`HTTP 403 → "Thiếu quyền"`).
- **Flaw:** A permission refusal comes back as HTTP 200 with `status: Failed, code: DEVICE_PERMISSION_REQUIRED`. `Forbid()` is returned only when no actor can be built. As written, the phase-1 integration test can never go green. That leaves two outcomes: someone weakens the test, or someone changes the controller to 403, which breaks every existing caller that reads `code` from a 200 body (for example the `CATALOG_STALE` handling).
  - Phase 2 then puts four codes into `kind`, and the many other pre-publish `Failed` codes fall through: `DEVICE_PERMISSION_REQUIRED`, `DEVICE_REVOKED`, `DEVICE_NOT_FOUND`, `COMMAND_EXPIRED`, `COMMAND_TIMESTAMP_IN_FUTURE`, `KEY_GENERATION_FENCE`, `IDEMPOTENCY_IN_PROGRESS`, `CORRELATION_ID_REUSED`, `CONFIRM_REQUIRED`.
  - One of them, `IDEMPOTENCY_OUTCOME_UNKNOWN`, may follow a real publish.
  - An HTTP 5xx or network error after the publish (for example the audit `RecordAsync` throwing after `SendAsync`) is not classified at all.
- **Failure scenario:**
  - A user's role is revoked mid-session and they click "Sửa". The UI never sees a 403 and shows a generic or "unknown" result, or a 403-specific branch that never fires. Tests pass against mocks returning 403.
  - Worse, an implementer who reads "only certainlyNotSent/4xx means not sent" can classify an unlisted `Failed` code as `not_sent`. That skips the read-back for `IDEMPOTENCY_OUTCOME_UNKNOWN`, the exact R4 case the frontend already guards against.
- **Evidence:**
  - `src/Hms.Server.Application/Commands/SendDeviceCommandHandler.cs:103-107`: `return await RecordAsync(..., Failure(request, CommandExecutionStatus.Failed, decision.Code), ...)`.
  - `SendDeviceCommandHandler.cs:147-157`: the idempotency codes.
  - `src/Hms.Server.Api/Controllers/DeviceCommandsController.cs:29-31, 47`: `Forbid()` only for a null actor, otherwise `Ok(await handler.HandleAsync(...))`.
  - `src/Hms.Server.Domain/Authorization/DeviceAccessPolicy.cs:23`: `PermissionRequired = "DEVICE_PERMISSION_REQUIRED"`.
  - `tests/Hms.Server.IntegrationTests/ManagementApiTests.cs:707-711`: refusals asserted as `HttpStatusCode.OK` plus a body `code`.
  - `frontend/src/core/commands/useDeviceCommand.ts:36-42` and `useDeviceCommand.test.ts:82`: `IDEMPOTENCY_OUTCOME_UNKNOWN` is explicitly not "certainly not sent".
- **Suggested fix:**
  - Rewrite acceptance and test 6 as "200, `status=Failed`, `code=DEVICE_PERMISSION_REQUIRED`, publisher received nothing".
  - In `runDeviceWrite`, map `not_sent` from an explicit allowlist: `certainlyNotSent(dispatch)` plus the named pre-reservation codes from `SendDeviceCommandHandler`. Everything else, including any 5xx or network error after the POST left the browser, maps to `unknown` with a read-back.
  - Map `DEVICE_PERMISSION_REQUIRED` to "Thiếu quyền".

## Finding 5: The write lane limits concurrency, not frequency, so a low-tier user can hammer device config
- **Severity:** Medium
- **Location:** Phase 1, section "Lane ghi" ("Không thêm option cấu hình: giới hạn cố định là 1 (YAGNI)"). plan.md, section "Khung an toàn chung".
- **Flaw:** One write in flight per device means the next write may go as soon as the reply arrives, typically in well under a second. Nothing in the API limits request rates. The device's own bucket is shared and refills at 10 commands/s. A scripted client holding only `adc.enable` (not HighRisk) can loop `toggle_channel` / `set_channel`. Spec §1 step 5 separates "lưu bền" from runtime apply, so these writes plausibly persist to flash (NVS) each time.
- **Failure scenario:** An insider runs a loop of `adc.toggle_channel {ai:1, enabled:!x}` against an in-scope gateway:
  - several thousand persisted writes per hour, so flash wear and a channel that flaps its telemetry;
  - every other operator, including the `sys.time_edit` holder trying to revert Finding 1, mostly gets `DEVICE_WRITE_BUSY`.
- **Evidence:**
  - `grep -rn "RateLimit|AddRateLimiter|EnableRateLimiting" src` returns no matches.
  - `src/Hms.Server.Infrastructure/Mqtt/MqttDeviceCommandTransport.cs:57-71`: only reads are capped (`TryRegisterRead`); everything else goes through `Register`.
  - Spec line 83: `Rate limit dùng bucket chung burst 20/refill 10 mỗi giây`.
  - phase-01:54.
- **Suggested fix:** Add a server-side token bucket for W/C, per device and per user, for example at most 6 writes per minute per device and 3 per user. Check it in the transport next to `TryRegisterWrite`, and return a distinct code such as `DEVICE_WRITE_RATE_LIMITED` (status `ContractUnavailable`, so `certainlyNotSent` stays true). Add a `DeviceLaneSafetyTests` case.

## Finding 6: The write-lane hold assumes the device drops a command by `Deadline`, but the client controls `RequestedAt`
- **Severity:** Medium
- **Location:** Phase 1, section "Lane ghi" ("Nhả slot: ... khi sweep hết hạn (`TakeExpired` theo `Deadline`)") and "Risk Assessment" ("thiết bị bị khoá ghi tối đa 30 s").
- **Flaw:** The slot is released at `Deadline = publish time + ResponseTimeoutSeconds`. The default is 30 s and it can be set anywhere from 21 to 600 s. On the device, though, the command stays valid until `RequestedAt + TTL(≤20 s)`, and `RequestedAt` comes from the API body. The handler accepts values up to now+60 s, and the device accepts timestamps up to 60 s in its future. So a forward-dated write can still run up to about 80 s after publish. That is after the slot is released, after the sweep records `DEVICE_RESPONSE_TIMEOUT`, and after the UI's "unknown → read-back" has already shown the old value. If `clean_session` was turned off, the broker delivers the old message on reconnect (R10). A device clock running behind (Finding 1) widens the window further.
- **Failure scenario:**
  1. An API client sends `set_hostname` with `requestedAt = now+59s` while the gateway is briefly reconnecting.
  2. At 30 s the slot is released, and the UI reads back the old hostname and reports "unknown".
  3. The operator sends a corrective write.
  4. At about 60 s the device reconnects and runs the stale write after the correction. The lane's "at most one write" promise is broken.
- **Evidence:**
  - `src/Hms.Server.Api/Controllers/DeviceCommandsController.cs:41`: `request.RequestedAt ?? DateTimeOffset.UtcNow`.
  - `src/Hms.Server.Application/Commands/SendDeviceCommandHandler.cs:131, 136`: `RequestedAt > UtcNow.AddSeconds(60)` is the only upper bound; `expiresAt = RequestedAt.Add(ttl)`.
  - `src/Hms.Server.Infrastructure/Mqtt/MqttDeviceCommandTransport.cs:55-56`: `Deadline = now.AddSeconds(ResponseTimeoutSeconds)`.
  - `src/Hms.Server.Infrastructure/Mqtt/MqttLinkOptions.cs:27, 50-53`.
  - Spec line 81 (60 s future window) and line 18 (R10).
- **Suggested fix:** For classes W and C (and B), either:
  - ignore the client `RequestedAt` and stamp it on the server; or
  - hold the write slot until `max(Deadline, plan.ExpiresAt + skew margin)` and reject `RequestedAt` more than about 2 s from server time for writes.
  - Add a lane test: "forward-dated write still blocks until its device-side expiry". Correct the "tối đa 30 s" claim, since the timeout is configurable up to 600 s.

## Finding 7: Job polling depends on `system.get_operations`, and the plan never checks the permission or the data path it needs
- **Severity:** Medium
- **Location:** Phase 1, section "Giữ nguyên" ("Realtime: `command_result` ... chỉ mang outcome"). Phase 2, sections `useDeviceWrite.ts` → "Job còn chạy" and "Luồng chung" → "Chỉ bật khi".
- **Flaw:**
  - (a) `system.get_operations` requires `sys.info_view`. Phase 2 enables a write control when the user has the write action and the matching read step. It never checks that `system.get_operations` is in `allowedCommands`. A user with `adc.config_edit` + `adc.view` but no `sys.info_view` gets every pending-job write reported as `unknown` or `job_failed`.
  - (b) The "outcome only" claim is false. The realtime payload carries `detail`. For writes, `detail` holds `operation_id=<id>` or the device's `data.error` text. It goes to every subscriber in scope, because delivery checks scope only, not `sys.info_view` or the write permission.
  - (c) Any `sys.info_view` holder can poll any `operation_id`. IDs are decimal uint64 values, and the simulator uses `"<n>"`. That user can read `resultCode` / `resultMessage` for jobs they could not start.
  - (d) Class-R payloads are not validated on the server (ConfigCommandPolicy returns null for R). The FE copies an `operation_id` it parsed out of a device-controlled `detail` string into a payload the server signs.
- **Failure scenario:** A viewer with only scope and `sys.info_view` subscribes to the device group and collects `operation_id`s from `command_result` events. They then poll `system.get_operations` for their own and neighbouring IDs and read job results of other users' config writes. Separately, a `job_failed` UI result appears for legitimate editors who simply lack `sys.info_view`.
- **Evidence:**
  - `command-catalog.json` row `system.get_operations`: `"required_perm":"sys.info_view"`.
  - `src/Hms.Server.Application/DeviceLink/DeviceLinkHandlers.cs:150-152`: `$"operation_id={operationId}"`, where `op.ToString()` accepts any JSON kind.
  - `DeviceLinkHandlers.cs:157-162`: `data.error` goes into `reason`.
  - `DeviceLinkHandlers.cs:177-185`: `["detail"] = result.Detail` in the realtime payload.
  - `src/Hms.Server.Api/Hubs/SignalRDeviceEventDeliveryService.cs:77-80`: authorization is `ScopeAuthorization.CanAccessDevice` only.
  - Spec line 182: `operation_id đầu vào là chuỗi decimal uint64 >0`.
  - phase-01:59; phase-02:33-37, 69.
- **Suggested fix:**
  - Gate the write controls on `system.get_operations` being allowed, or show "Thiếu quyền theo dõi job".
  - Add a server rule for `system.get_operations`: payload exactly `{operation_id}`, matching `^[1-9][0-9]{0,19}$`, applied in both FE and policy.
  - For non-R events, drop `detail` from the realtime payload (the issuer reads it through `GET .../commands/{id}`), or send `code` only.
  - Fix the "chỉ mang outcome" claim.

## Finding 8: The new `refresh(steps)` slot prop is a silent no-op whenever any read is in flight, so the read-back that replaces auto-resend can quietly skip
- **Severity:** Medium
- **Location:** Phase 2, sections `DeviceReadCard.vue` → "Slot: thêm `refresh(steps)` ... (gọi `reads.read`)" and "Trong lúc ghi" (locks only the write controls).
- **Flaw:**
  - `reads.read` returns at once, doing nothing, when `reading` is true. It returns `void`, not the fresh states.
  - Phase 2 locks only the write controls during a write. The card's own "Đọc" and "Dừng" buttons stay live, and so do reads started earlier.
  - If the read-back collides with a user read, `refresh` silently does nothing. The card then compares against whatever `states` already holds, which can be a reply to a read sent before the write.
  - Pressing "Dừng" aborts the read-back and leaves the step `idle`.
- **Failure scenario:**
  1. The user clicks "Đọc", then edits the hostname while `get_time` / `get_hostname` are still in flight.
  2. The write executes, and `refresh([get_hostname])` returns at once because a read is running.
  3. The UI compares against the pre-write value and shows "Thiết bị báo đã ghi nhưng đọc lại ra `<old>`". With different timing it shows "Đã ghi và xác nhận" based on a read that never ran after the write.
- **Evidence:**
  - `frontend/src/core/commands/useDeviceReads.ts:92-93`: `async function read(steps): Promise<void> { if (reading.value) return`.
  - `frontend/src/features/devices/components/reads/DeviceReadCard.vue:45-47, 55-77`: the "Đọc" and "Dừng" buttons.
  - `DeviceReadCard.vue:95`: slot props are `states, stale, now`.
- **Suggested fix:**
  - `refresh` must wait for (or cancel and restart) any in-flight read, then read the given steps, and return the resulting `ReadState`s.
  - Count a state as the read-back only if its `readAt` is later than the write's completion.
  - Disable "Đọc" and "Dừng" while `writing`.
  - Add a `DeviceReadCards.test.ts` case: "write during an in-flight read still performs a post-write read".

## Finding 9: Identity strings (hostname, ADC name) are opened fleet-wide with no collision or spoofing controls, and the plan's own client_id gap is left unresolved
- **Severity:** Medium
- **Location:** plan.md, sections "Spec gap" ("Hostname: đổi hostname có ảnh hưởng MQTT `client_id` hay session không") and "6 lệnh mở". Phase 1, section "Kiểm payload" (`adc.set_channel` "không có ký tự điều khiển"). Phase 3, section "Risk Assessment".
- **Flaw:**
  - The plan admits nobody knows whether the hostname feeds the MQTT `client_id`. It still opens `set_hostname` for every device. The only mitigation, phase 3's heartbeat check, covers the lab gateway alone.
  - The server already signs `mqtt.get_config`, which returns the session config. It could refuse `set_hostname` when `client_id` is empty or equals the current hostname, but the plan does not.
  - There is no check that a hostname is unique across devices.
  - The ADC name rule bans only control characters (`char.IsControl`, Unicode Cc). Unicode format characters still pass: U+202E (bidi override), U+200B (zero-width space), U+FEFF and U+2028.
- **Failure scenario:**
  - **Hostname collision.** Firmware derives `client_id` from the hostname, which the spec does not rule out. A user with `sys.hostname_edit` on device A sets A's hostname to device B's hostname, where B belongs to another org on the same broker. The broker drops the older session whenever the other connects (MQTT client-id takeover). Both gateways flap offline and both orgs lose their control path.
  - **Name spoofing.** A user with `adc.config_edit` renames AI3 to `"‮)rab( erusserP"`. It renders as "Pressure (bar)" for every viewer. Or they rename it to a zero-width string that passes "không chỉ toàn khoảng trắng". Operators then misread live measurements.
- **Evidence:**
  - plan.md:104 (spec gap).
  - `src/Hms.Server.Infrastructure/Commands/PinnedCommandCatalog.cs:54`: `("mqtt", "get_config")` is enabled.
  - Spec line 241: the MQTT config includes `client_id`.
  - phase-01:37 (name rule).
  - phase-03:112-114: the session risk is handled only for the lab.
- **Suggested fix:**
  - Until firmware answers the gap, have `ConfigCommandPolicy` (or a pre-check in the handler) refuse `set_hostname` unless the last `mqtt.get_config` read shows an explicit `client_id` different from the current hostname. Otherwise keep `set_hostname` behind a master-admin-only flag.
  - For ADC names, also refuse Unicode categories Cf, Zl, Zp and Cs, and require at least one visible character (letter, number, punctuation or symbol), not just non-whitespace.

---

## Verification Results

### Fact Checker

**Phase 1 (`phase-01-server-config-writes.md`)**

| # | Claim | Result |
|---|---|---|
| 1 | `ToDefinition` opens only class R (`EnabledActions`) and B (`PhysicalOutputActions`) | VERIFIED `src/Hms.Server.Infrastructure/Commands/PinnedCommandCatalog.cs:221-226` |
| 2 | `TryInteger`, `IsBoolean`, `HasExactly` are private in `OutputCommandPolicy` | VERIFIED `src/Hms.Server.Application/DeviceLink/OutputCommandPolicy.cs:57, 61, 87` |
| 3 | Adapter calls `OutputCommandPolicy` then `secrets.Find` (insertion point for `ConfigCommandPolicy`) | VERIFIED `src/Hms.Server.Infrastructure/Commands/HmsIdfCommandProtocolAdapter.cs:59-63` |
| 4 | `DeviceLinkHandlers.ToResult` maps accepted/pending to `Acknowledged` + `operation_id=` | VERIFIED (class is `DeviceCommandResponseCorrelator`, file `DeviceLinkHandlers.cs:139-153`) |
| 5 | `command_result` is published for non-R and "chỉ mang outcome" | FAILED: the payload includes `detail` (`DeviceLinkHandlers.cs:184`), which carries device `data.error` / `operation_id` (Finding 7) |
| 6 | `GET .../commands/{id}` re-checks permission (`MayReadReply`) | VERIFIED `src/Hms.Server.Api/Controllers/DeviceCommandsController.cs:102, 118-127` |
| 7 | `allowed-commands` lists open catalog rows the caller may send | VERIFIED `DeviceCommandsController.cs:67-72` |
| 8 | TTL ceiling 20 s for non-physical commands | VERIFIED `src/Hms.Server.Application/Commands/SendDeviceCommandHandler.cs:20, 123-125` |
| 9 | Role with view only gets 403 on `POST set_hostname` | FAILED: 200 + `Failed`/`DEVICE_PERMISSION_REQUIRED` (`SendDeviceCommandHandler.cs:103-107`; `DeviceCommandsController.cs:47`; `ManagementApiTests.cs:707-711`) |
| 10 | "Assertion có chủ đích: số lệnh mở cũ (28 → 34)" | FAILED: no literal 28. The tests that must change are `tests/Hms.Server.UnitTests/CatalogTests.cs:141-150` (set equality of Enabled with `EnabledActions ∪ PhysicalOutputActions`, plus per-set class asserts). The plan does not name this test. |
| 11 | "Sweep ... khoá ghi tối đa 30 s" | PARTIAL: 30 s is the default only; `ResponseTimeoutSeconds` can be 21-600 (`src/Hms.Server.Infrastructure/Mqtt/MqttLinkOptions.cs:27, 50-53`) |
| 12 | Six actions present with the stated permission and class | VERIFIED `command-catalog.json` (adc.set_channel W adc.config_edit; adc.toggle_channel C adc.enable; system.set_hostname W sys.hostname_edit; set_timezone W, set_ntp_servers C, sync_ntp C, set_time C, all `sys.time_edit`). Totals: 94 rows (R49/W18/C17/D3/B3/F4); currently open 26 + 2 = 28, so 34 after. |

**Phase 2 (`phase-02-config-edit-ui.md`)**

| # | Claim | Result |
|---|---|---|
| 1 | `runDeviceCommand` does POST then polls for 40 s | VERIFIED `frontend/src/core/commands/useDeviceCommand.ts:6, 69-93` |
| 2 | `certainlyNotSent` exists and covers `ContractUnavailable` (so `DEVICE_WRITE_BUSY` maps correctly) | VERIFIED `useDeviceCommand.ts:39-42` |
| 3 | `DeviceReadCard.vue` slot can take an extra prop | VERIFIED: current props `states, stale, now` at `DeviceReadCard.vue:95` |
| 4 | `refresh` can simply "gọi `reads.read`" | FAILED as a contract: `read` is a no-op while reading and returns void (`useDeviceReads.ts:92-93`) (Finding 8) |
| 5 | `PANEL_ONLY_ACTIONS` exists in `DeviceCommandModal.vue` | VERIFIED `frontend/src/features/devices/components/DeviceCommandModal.vue:67, 70` |
| 6 | `DeviceInfoTimeCard.vue`, `DevicePowerAdcCard.vue`, `DeviceReadCards.test.ts`, `DeviceCommandModal.test.ts` exist | VERIFIED (glob of `frontend/src/features/devices/components/` and `reads/`) |
| 7 | `frontend/src/core/devices/` exists for `timezones.ts` | VERIFIED (`deviceFilters.ts`, `deviceInventoryState.ts`) |
| 8 | "HTTP 403 → Thiếu quyền" | FAILED (see Phase 1 #9) |
| 9 | Spec §5 job fields are camelCase `operationId`, `resultSuccess` | VERIFIED spec line 182 |
| 10 | `get_time` card already shows `ntp_mode`/`timezone`/`epoch` | VERIFIED `DeviceInfoTimeCard.vue:18-27` (`ntp_servers` is not listed today) |

**Phase 3 (`phase-03-live-verification-docs.md`)**

| # | Claim | Result |
|---|---|---|
| 1 | `tools/mqtt-broker/spec-device-simulator.js` exists | VERIFIED |
| 2 | `tools/tool-env.js` provides `HMS_ADMIN_EMAIL`/`HMS_ADMIN_PASSWORD` | VERIFIED `tools/tool-env.js:5, 21-22` |
| 3 | `HMS_API_BASE` comes via `tool-env.js` | FAILED: it is read in `tools/device-read-check.js:22` (default `http://127.0.0.1:5099`), not in `tool-env.js` |
| 4 | `tools/device-reads/` is gitignored | VERIFIED `.gitignore:18` |
| 5 | `docs/architecture-mqtt-device-link.md`, `docs/architecture-device-mqtt-api-catalog.md` exist | VERIFIED |
| 6 | `reports/screens/*` follows earlier plans | VERIFIED precedent `plans/261106-0735-do-relay-control/reports/screens`, `plans/261106-1040-monitoring-read-actions/reports/screens` |
| 7 | "Tài liệu liệt kê các role được ghi ngay" | FAILED: phase 3 lists codes, not roles (Finding 2) |

**Brainstorm**

| # | Claim | Result |
|---|---|---|
| 1 | "PayloadSummary lưu nguyên payload" / "Audit đã tách W/C/D/F" | FAILED: W/C get `null` (`EfCommandAuditStore.cs:218-223`) (Finding 3) |
| 2 | HighRisk contains mqtt/eth/net/wifi/sys.reboot/factory_reset/ota | VERIFIED `DevicePermissionCodes.cs:21-28` (also contains `gpio.do_write`) |
| 3 | 18 W / 17 C / 3 D / 4 F | VERIFIED catalog JSON counts and `CatalogTests.cs:165-166` |

### Contract Verifier

**`IPendingDeviceCommandRegistry.TryRegisterWrite` (new interface member)**
- Interface: `src/Hms.Server.Application/DeviceLink/DeviceLinkContracts.cs:260-272`.
- Implementers: only `InMemoryPendingDeviceCommandRegistry`, `DeviceLinkContracts.cs:274`. There are no test doubles, so the plan's "fake registry (nếu có)" check comes back empty.
- Production consumers:
  - `src/Hms.Server.Infrastructure/Mqtt/MqttDeviceCommandTransport.cs:22, 57-71`;
  - `src/Hms.Server.Application/DeviceLink/DeviceLinkHandlers.cs:27, 49, 75, 111` (`Find`, `Remove`, `TakeExpired`);
  - DI at `src/Hms.Server.Infrastructure/DependencyInjection.cs:38`.
- Tests that construct the concrete type:
  - `tests/Hms.Server.UnitTests/DeviceLaneSafetyTests.cs:32, 69, 90, 109, 128`;
  - `DeviceLinkTests.cs:325, 356`;
  - `DeviceOutputStateTests.cs:247, 272`;
  - `PhysicalOutputSafetyTests.cs:224`.
- Note: `Register` (`DeviceLinkContracts.cs:279`) does not take `_readGate`. The atomicity in the plan holds only if no W/C path ever calls `Register`. Have the transport branch assert this. `DeviceLinkTests.cs:356` and `DeviceOutputStateTests.cs:272` call `Register` directly with arbitrary plans and will not exercise the write gate.

**`OutputCommandPolicy` helper extraction to `CommandPayloadRules`**
- Helpers: `OutputCommandPolicy.cs:57-88`.
- Callers of `OutputCommandPolicy`:
  - `HmsIdfCommandProtocolAdapter.cs:19, 59`;
  - `MqttLinkOptions.cs:31` (`DefaultMaxPulseMs`);
  - `PhysicalOutputSafetyTests.cs:33-39+` (`PayloadInvalid`).
- The public constants must stay on `OutputCommandPolicy`. The plan covers this through "PhysicalOutputSafetyTests xanh". No other caller is missed.

**`PANEL_ONLY_ACTIONS`**
- Only `DeviceCommandModal.vue:67, 70`; the test is `DeviceCommandModal.test.ts`.
- `frontend/src/App.vue` has a legacy free-form sender that lists every catalog action unfiltered: `App.vue:117, 958-959, 772`. It is unreachable while the router is installed (`App.vue:18-21, 797-802`; `frontend/src/main.ts:10`). This is low risk, but it should be deleted or filtered so it does not come back.
- The server accepts the six writes from any client, so the claim "chỉ ghi được qua form có bước đọc lại" is a UI convention, not something the server enforces.

**`DeviceReadCard` slot props (`refresh` added)**
- Provider: `DeviceReadCard.vue:95`.
- Consumers:
  - `DeviceInfoTimeCard.vue:57`;
  - `DeviceNetworkCard.vue:52` (unchanged, still compatible because the addition is additive);
  - `DevicePowerAdcCard.vue:92`;
  - `DeviceReadCard.test.ts`.
- Behavioural contract gap: see Finding 8.

**`ConfigCommandPolicy` insertion and catalog opening**
- `PinnedCommandCatalog.ToDefinition` consumers: `MeController.cs:61-71` (catalog snapshot `Available`), `DeviceCommandsController.cs:67-72`, `SendDeviceCommandHandler.cs:90`.
- Tests that must change: `CatalogTests.cs:141-150`. The plan names `CatalogTests` but not this test or its per-set class asserts.

---

Status: DONE_WITH_CONCERNS
Summary: 9 findings (1 Critical, 3 High, 5 Medium). The critical one: the plan opens NTP-server writes under a non-HighRisk code, and that gives control of the device clock, which the device uses to expire and de-duplicate signed commands (lockout or replay). Several plan facts are wrong: 403 vs 200, audit payload, realtime "outcome only", "28→34".
Concerns/Blockers: Finding 1 needs a user decision because it reverses the delegated "not HighRisk" call. Spec R11 means the server is the only gate, so fix Findings 1-4 before phase 1 starts.
