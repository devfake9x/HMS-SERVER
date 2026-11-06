# Red team: failure-mode analysis of plan 261106-1040-monitoring-read-actions

Reviewer: code-reviewer, acting as failure-mode analyst. Date: 2026-11-06.
Scope: `plan.md` and phases 01–04, read against the codebase at worktree `mqtt-device-connection-36f705` and against spec `HMS-IDF@18a63e8ed:engineering-docs/api/mqtt-api.md` (blob `d0e6c315…`, verified with `git rev-parse`). Spec line numbers below (`spec Lnnn`) are lines of that blob.

Ten findings: 1 Critical, 4 High, 5 Medium. A fact-check table and a list of contract consumers follow the findings.

---

## Finding 1: The Modbus table can put the saved configuration's names and units on values from the running configuration, and pages from different configurations can be merged into one table
- **Severity:** Critical
- **Location:** Phase 3, section "Modbus: đọc theo trang" (bullets "Ghép bảng", "Danh mục", "Không có slave"); Phase 4, section "Lệnh Modbus"
- **Flaw:**
  1. `get_rtu_tags` and `get_rtu_devices` describe the **saved** configuration: their `generation` is the generation of the saved config. `get_rtu_live` returns the **runtime** cache. After `replace_rtu_config` with no `restart_rtu` yet (R9: "Ghi xong chưa chạy"), or after a failed restart, the two disagree. A key `(channelIndex, slaveAddr, cid)` can stay the same while its name, unit or dataType changes. The plan reads `get_rtu_status` on every click and lists `configDirty`, `runtimeDiverged` and `durableGeneration` as fields to show. It never uses them to decide whether the join is valid.
  2. The device and tag lists are read once ("chỉ đọc lần đầu"). They are read again only when a live key is missing from them. A reconfiguration that keeps the keys but changes names or units never triggers a re-read.
  3. The spec requires restarting the traversal from offset 0 "khi `total` đổi hoặc `snapshotUptimeMs` giảm". It also says to detect a restart between live pages by comparing `generation` before and after the traversal. The plan's loop only follows `truncated`, stops on `returned=0` or the page cap, and never restarts. It never compares `generation` across tags or devices pages.
  4. Error semantics are wrong. On a device with no RTU config, tags and devices return a config-load error, not an empty list (spec L454–455). The plan maps `total=0` to "Chưa cấu hình", and phase 4 expects `total=0` on the lab if it has no slave. During a restart, devices and live return `ESP_ERR_INVALID_STATE`, and `restartStuck` can keep that error up until the next reboot (spec L434–439). The plan has no mapping for either case and does not say what happens to pages already read when a later page fails.
- **Failure scenario:** An engineer replaces the RTU config so that slave 1, cid 3 changes from "Nhiệt độ (°C)" to "Áp suất (bar)", and has not restarted RTU yet. The operator presses "Đọc". Live still returns the runtime value 85.2, which is a temperature. The card joins it with the saved catalog and shows "Áp suất 85.2 bar", with "Đọc lúc … vừa xong" and no warning. In a second case, a restart lands between live page 2 and page 3. The table merges pages from two runtime layouts and presents them as one current snapshot.
- **Evidence:**
  - spec L400–401: tags and devices carry `generation`; tags lists only rows where the channel, device and param are all enabled and the param is not metadata-only.
  - spec L402: live comes "từ cache".
  - spec L422–432: "Các trang không phải một snapshot… Đọc lại từ offset 0 khi `total` đổi hoặc `snapshotUptimeMs` giảm… gọi `modbus.get_rtu_diagnostics` với `{"limit":1}` trước… và sau… so `generation`".
  - spec L443–445: `runtimePresent` does not prove that the saved device is the running one; L452–453: "`generation`… ở tags và devices là generation của cấu hình đã lưu".
  - spec L454–455: "Tags và devices khi thiết bị chưa có cấu hình RTU trả lỗi của bước nạp cấu hình, không trả danh sách rỗng".
  - spec L378: `get_rtu_status` returns `configDirty,runtimeDiverged,durableGeneration`.
  - spec L53 (R9).
  - Plan phase 3: "Danh mục… chỉ đọc lần đầu, hoặc khi `get_rtu_live` có khoá không thấy trong danh mục"; "Không có slave (`total = 0`): hiện 'Chưa cấu hình thiết bị Modbus'".
- **Suggested fix:**
  - Key the cached device and tag lists by `generation`. On every read, compare `get_rtu_status.durableGeneration` with the cached generation and re-read the lists when they differ.
  - When `configDirty` or `runtimeDiverged` is true, show a banner and do not put saved names or units on live values (or label them clearly as "theo cấu hình đã lưu, chưa chạy").
  - Restart the traversal once when `total` changes, when `generation` differs between pages, or when `snapshotUptimeMs` goes down. If it happens a second time, fail the card.
  - Consider opening `modbus.get_rtu_diagnostics` (class R, `mb_rtu.config_view`) only for the before/after generation check that the spec prescribes.
  - If any page fails, discard the whole table rather than rendering part of it.
  - Map "not configured" from the actual load-error code that phase 4 observes, not from `total=0`. Map `ESP_ERR_INVALID_STATE` to "RTU đang khởi động lại".
  - Add tests for each of these paths.

## Finding 2: The "unknown key, so re-read the lists" rule, combined with the page caps, can make every click a full 17-command read, indefinitely
- **Severity:** High
- **Location:** Phase 3, section "Modbus: đọc theo trang" (page-limit table and the "Danh mục" bullet); `plan.md` "Rủi ro → Lane lệnh"
- **Flaw:** The device and tag lists are capped at 34 devices and 108 tag rows. The spec allows up to 504 Modbus tag params (R12). Live order is not specified to follow the configuration order that tags uses. Tags also drops disabled and metadata-only rows. So live can contain keys that will never appear in the cached lists: keys outside the 108-row window, runtime-only keys while `configDirty` is true, or cache entries of disabled devices. Every one of those counts as a "khoá lạ" and triggers a full re-read (2 + 9 pages). The re-read cannot add the missing keys, so the next click triggers it again.
- **Failure scenario:** A site has 150 tags. The first 100 live items include about 40 keys that are not in the first 108 tag rows. Every press of "Đọc" sends 1 + 2 + 9 + 5 = 17 commands, takes 17 s or more, and never gets down to the "≈ 1 + 5" the plan promises. With three operators on that gateway, that is more than 50 signed commands per minute from monitoring alone. This feeds directly into Finding 3.
- **Evidence:**
  - Plan phase 3, page table: `get_rtu_tags` 12 × 9 = 108; `get_rtu_devices` 17 × 2 = 34.
  - Plan phase 3, "Danh mục" bullet: re-read "khi `get_rtu_live` có khoá không thấy trong danh mục".
  - Plan phase 3 risk: "lần đọc sau chỉ còn khoảng 1 + 5".
  - spec L56 (R12: "Tối đa 504 tham số tag Modbus"); spec L401 (enabled-only, not metadata-only).
- **Suggested fix:**
  - Decide re-reads by `generation` (Finding 1), not by unknown keys.
  - Re-read the lists at most once per generation per card.
  - After that, render a key that is still missing as "chưa có tên (ngoài phạm vi danh mục đã đọc)" without re-reading.
  - Add a test with more live keys than the list window holds, and assert the number of commands sent on the second click.

## Finding 3: Nothing coordinates cards, browser tabs, viewers and the relay (DO) panel on one device; `LANE_FULL` is treated as final, and monitoring reads can cause other users' relay commands to be dropped
- **Severity:** High
- **Location:** Phase 3, section "Chung (mọi thẻ)" (bullet "Gửi lần lượt…, Lệnh sau vẫn chạy khi lệnh trước lỗi"); `plan.md` "Rủi ro → Lane lệnh"
- **Flaw:**
  - The plan sends commands one at a time **within one card** only. Each of the four cards has its own `useDeviceReads`. Nothing serializes cards with each other, with other tabs, with other viewers, or with the relay (DO) panel.
  - The server has no per-device admission control: the pending registry is an unbounded dictionary and there is no rate limiter.
  - The device queue has 16 slots and returns `LANE_FULL` when nearly full. When it is completely full, it drops commands without a reply. Its rate bucket (burst 20, refill 10/s) is shared by every client, and commands refused by the rate limit get no reply at all.
  - The plan turns `LANE_FULL` into a per-command error and moves straight on to the next command. The spec says to back off and resend with a new id.
  - A timeout does not stop the rest of the sequence. On an offline device a card spends about 30–40 s per command, roughly 2.5 minutes per card.
  - The relay panel's two read-backs run back to back with no delay.
- **Failure scenario:** Three operators open the same gateway and press all four cards, about 4 commands/s each, so about 12/s against a 10/s refill. Operator D toggles a relay at the same moment.
  - D's `set_output` is dropped by the rate limit with no reply. After 30 s the server records `DEVICE_RESPONSE_TIMEOUT`, and the relay state is marked stale (`output_command_outcome_unknown`).
  - The two immediate `get_status` read-backs hit `LANE_FULL` or are dropped too. The panel shows "Chưa đọc lại được trạng thái".
  - The relay state stays unknown for 1–2 minutes, caused purely by monitoring reads.
  - The cards meanwhile render a mix of data and `DEVICE_REJECTED: LANE_FULL`.
- **Evidence:**
  - spec L120: "Queue command có 16 slot; gần đầy có thể trả `LANE_FULL`, đầy hẳn… bị drop không response. Rate limit dùng bucket chung burst 20/refill 10 mỗi giây, không phải quota riêng từng client".
  - spec L200: rate admission → "Thường im lặng, không response".
  - spec L209: `LANE_FULL` → "Backoff rồi gửi lại bằng `id` mới".
  - `src/Hms.Server.Application/DeviceLink/DeviceLinkHandlers.cs:63-65`: `LANE_FULL` is accepted as a reply; `:156-161` turns it into `DEVICE_REJECTED`; `:117-118` marks outputs stale on timeout.
  - `src/Hms.Server.Infrastructure/Mqtt/MqttDeviceCommandTransport.cs:47-49`: registers the pending command with no per-device cap.
  - `src/Hms.Server.Application/DeviceLink/DeviceLinkContracts.cs:268-291`: unbounded `ConcurrentDictionary`.
  - `frontend/src/features/devices/components/DeviceOutputsPanel.vue:188`: `(await readBack()) || (await readBack())`.
  - `grep AddRateLimiter|RateLimit src/`: no matches.
- **Suggested fix:**
  - Use one command lane per device page, shared by the four cards and the relay panel, and give relay writes and their read-backs priority.
  - On `LANE_FULL`, wait 1–2 s with jitter and retry once with a new correlation id.
  - On the first `DEVICE_RESPONSE_TIMEOUT` in a card, abort the rest of its sequence and show "thiết bị không trả lời".
  - On the server, cap in-flight class R commands per device (for example 4) and refuse more before signing (`DEVICE_BUSY`, ContractUnavailable), so relay commands always have room in the lane.

## Finding 4: Reads have no cancellation, no guard against starting twice, and no check that results still belong to the device on screen
- **Severity:** High
- **Location:** Phase 3, section "Architecture" (`useDeviceReads: readSequence(steps) và readPages(...)`) and "Tests first"
- **Flaw:**
  - `useDeviceReads` is specified without an `AbortSignal`, without cancel on unmount, without disabling "Đọc" while a read runs, and without a run token or `deviceId` check.
  - `runDeviceCommand` only stops early when it is given a signal. The relay panel, which the plan names as its model, never passes one.
  - `DeviceDetailView` is reused when the route `/devices/:id/:tab?` changes its `id` parameter: the `<router-view>` has no key, the view loads data only in `onMounted`, and nothing watches `deviceId`.
  - None of the phase 3 tests cover unmount, abort or a double click.
- **Failure scenario:**
  - An operator presses "Đọc" on the Modbus card and leaves the page after 3 s. The remaining 14 or so commands are still signed and published, and each one is polled for up to 40 s. That means more audit rows and more load on the device lane.
  - When the operator comes back and presses again, a second sequence overlaps the first.
  - A double click starts two sequences, 34 commands in all.
  - If the route changes from device A to device B (in-app link or history), A's late replies are rendered in B's cards, and A's cached Modbus lists are joined with B's live keys.
- **Evidence:**
  - `frontend/src/core/commands/useDeviceCommand.ts:72` (signal optional), `:82` (the loop exits only on deadline or abort).
  - `frontend/src/features/devices/components/DeviceOutputsPanel.vue:125-130`: no signal is passed.
  - `frontend/src/app/router/routes.ts:41` `path: '/devices/:id/:tab?'`; `frontend/src/shared/layouts/AppShell.vue:321` `<router-view />` without a key.
  - `frontend/src/features/devices/views/DeviceDetailView.vue:414-418`: loads only `onMounted`. The only watches are on `route.params.tab` (`:65`) and the hub state (`:296`).
- **Suggested fix:**
  - Give each card run its own `AbortController`. Abort it on unmount, on a `deviceId` change, and when a new run starts.
  - Drop any result whose run token or `deviceId` is no longer current.
  - Disable "Đọc" while a read is running.
  - Add tests for unmount mid-sequence (no further POSTs), double click (one sequence), and a `deviceId` change (old results discarded).

## Finding 5: Empty, partial or old data is shown as if it were current
- **Severity:** High
- **Location:** Phase 3, sections "Chung" ("Kết quả từng lệnh: `Executed` thì lấy `reply.data`", "Tuổi dữ liệu") and "Modbus" ("Tuổi cập nhật")
- **Flaw:**
  1. **R3, success with no data.** The device can reply `success:true` without `data` when it runs out of memory, and the spec says to read again rather than treat that as empty. The server maps any success to `Executed` and stores `DataJson=null`. With "Executed thì lấy reply.data", the card renders nothing, or "Không có phần cứng", or "Chưa cấu hình" (the page block is missing, so `total` is undefined). Modbus pages run close to the 8192-byte reply cap (devices: 8145 B, spec L459–462), so they are the most likely to hit this.
  2. **`Executed` with a missing reply.** The reply store is in memory and is a single 2048-entry FIFO shared by all devices. It is empty after an API restart. The plan does not say what the card does in that case.
  3. **One timestamp for the whole card.** A card shows one "Đọc lúc", but its commands span 6–17 s, or minutes when some time out. The plan does not say whether a failed re-read keeps or clears that command's previous value. So a battery voltage from two hours ago can sit beside fresh values under a new "Đọc lúc". The relay panel deliberately clears its state when a read fails.
  4. **No reaction to offline or reboot.** The relay panel switches to "Không rõ" when the link drops and shows the server's stale reasons, including reboot. The cards keep showing pre-reboot or pre-offline readings as current.
  5. **Misleading tag age.** `snapshotUptimeMs − timestampUs/1000` is frozen at read time while "Đọc lúc" keeps ticking. A tag that has never been polled (`updateCount=0` or `timestampUs=0`) gets an "age" equal to the whole uptime. Each page has its own `snapshotUptimeMs`, but the plan does not say to use the per-page value.
- **Failure scenario:** The device is short on memory, and the reply to `get_rtu_devices` page 1 arrives as success with no data. The card shows "Chưa cấu hình thiết bị Modbus" for a gateway that has 12 slaves. Separately, a battery card re-read times out on `battery.get_status` but keeps the previous voltage, now under the new "Đọc lúc 14:02:10 (vừa xong)".
- **Evidence:**
  - spec L47 (R3: "`success:true` không có `data` có thể xảy ra khi thiết bị hết bộ nhớ…: đọc lại, đừng coi là rỗng").
  - `src/Hms.Server.Application/DeviceLink/DeviceLinkHandlers.cs:141-142` (success → `Executed`), `:101` (`Data?.GetRawText()`, null).
  - `src/Hms.Server.Api/Controllers/DeviceCommandsController.cs:78-82` (reply null, or `Data` null).
  - `src/Hms.Server.Application/DeviceLink/DeviceLinkContracts.cs:332-348` with `src/Hms.Server.Infrastructure/DependencyInjection.cs:39` (global FIFO of 2048).
  - `DeviceOutputsPanel.vue:96-97`: "a failure clears the state… rather than leaving an old reading on screen"; `:71-72` and `:87-92`: link-down and stale reasons.
  - `DeviceLinkHandlers.cs:232-238`: the server already detects reboot from uptime going backwards.
- **Suggested fix:**
  - Keep a timestamp per command, using `reply.receivedAt`.
  - Treat `Executed` with null or absent data as "trả lời rỗng (R3)" and retry once.
  - Treat `Executed` with no reply as an error, never as empty.
  - On a failed re-read, either clear that command's previous value or mark it visibly as old.
  - Mark all card data "có thể đã cũ" when the link goes offline or the server reports a reboot.
  - Compute tag age from each page's own snapshot. Show "chưa cập nhật" when `updateCount=0` or `timestampUs=0`. Label the age "≈ x s trước (tại lúc đọc)".

## Finding 6: Reads stay valid on the device for 120 s, but the server gives up after 30 s, so abandoned reads keep occupying the lane
- **Severity:** Medium
- **Location:** Phase 2, section "Tập lệnh mở" (no TTL decision); Phase 3, section "Thiết bị offline" ("lệnh sẽ hết giờ thành lỗi")
- **Flaw:** Only physical commands have their TTL clamped, to 10 s. Reads are signed with the row default, 120000 ms (60000 ms for the Wi-Fi rows). The server gives up at 30 s and the UI at 40 s, and the card then moves on to its next command. The device checks TTL when a command leaves its queue. So on a slow or backlogged device, reads the server has already reported as timed out still run and hold queue slots, which causes `LANE_FULL` for later commands, relay commands included. If a device has `clean_session=false` (R10; it can be set with `mqtt.set_config`), the broker redelivers every read queued during an outage at reconnect, within the 120 s TTL. That burst can exceed the 16-slot queue.
- **Failure scenario:** The gateway is slow for a minute. Four cards across two viewers each push a read every ~30 s, and all of them time out on the server. When the gateway recovers, it works through stale reads for up to 2 minutes while new relay and read commands get `LANE_FULL` or are dropped.
- **Evidence:**
  - `src/Hms.Server.Application/Commands/SendDeviceCommandHandler.cs:114-120`: only `CommandClass.Physical` is clamped.
  - `src/Hms.Server.Infrastructure/Commands/HmsIdfCommandProtocolAdapter.cs:74`: `ttl_ms = ExpiresAt − RequestedAt`.
  - `src/Hms.Server.Infrastructure/Mqtt/MqttLinkOptions.cs:27` (30 s); `frontend/src/core/commands/useDeviceCommand.ts:6` (40 s).
  - spec L118 ("TTL xét khi command ra khỏi hàng đợi"); spec L54 (R10).
- **Suggested fix:** Clamp the TTL of class R commands to less than the server's reply timeout (for example 20 s), next to the existing physical clamp, and add a unit test. Reads are idempotent, so a short TTL costs nothing.

## Finding 7: Every read pushes a `command_result` to every viewer of the device, which crowds out relay notifications, and the page treats those events as heartbeats
- **Severity:** Medium
- **Location:** Phase 3, section "Architecture" (the plan reuses the flow but does not touch event handling in `DeviceDetailView`); `plan.md` "Đường dữ liệu"
- **Flaw:**
  - Every reply and every timeout publishes `command_result` to the device's realtime group.
  - `command_result` cannot be merged with other events, unlike `status` and `outputs`.
  - Each connection has a 64-event backlog, and the delivery loop sends one event per connection every 100 ms, awaiting each send in turn with a 2 s deadline.
  - When a backlog is full of non-mergeable events, new `outputs` and `status` events are dropped. One more non-mergeable event revokes the subscription with `REALTIME_RESYNC_REQUIRED`.
  - The plan multiplies `command_result` volume by about 17 per click per viewer.
  - On the client, `DeviceDetailView` treats any event other than `outputs`, including a `command_result` carrying `DEVICE_RESPONSE_TIMEOUT`, as a heartbeat: it sets "Last Heartbeat" / "Last update" to the current time.
- **Failure scenario:** The device is offline, and an operator presses all four cards. For 2–3 minutes a timeout `command_result` arrives about every 30 s per sequence, and every viewer sees "Last Heartbeat" showing the current time for an offline device. In a second case, several viewers are reading while one has a slow connection: that connection's backlog fills with `command_result` events, the relay panel's `outputs` refresh is dropped, and the subscription is revoked.
- **Evidence:**
  - `src/Hms.Server.Application/DeviceLink/DeviceLinkHandlers.cs:103`, `:126`, `:164-187`.
  - `src/Hms.Server.Api/Hubs/SignalRConnectionBacklog.cs:19` (capacity 64), `:191-195` (`command_result` is not in the mergeable set), `:60-66` (a mergeable event is dropped when the backlog is full), `:69-72` (resync).
  - `src/Hms.Server.Api/Hubs/SignalRDeviceEventDeliveryService.cs:21` (100 ms), `:22` (2 s deadline), `:87-88` (revoke on overflow), `:100-149` (one event per connection per loop).
  - `frontend/src/features/devices/views/DeviceDetailView.vue:266-277` (`lastHeartbeat.value = new Date()...` for every non-`outputs` event), shown at `:471` and `:750`.
- **Suggested fix:**
  - In `DeviceDetailView`, handle `command_result` on its own path; never let it update heartbeat or telemetry state. Add a test for this.
  - Either stop broadcasting class R `command_result` events (the issuer already polls), or make `command_result` mergeable per issuer.

## Finding 8: Every read writes a permanent audit row, nothing ever deletes them, and startup scans that table without an index
- **Severity:** Medium
- **Location:** Phase 3 / `plan.md`, "Đường dữ liệu: dùng lại luồng… (`runDeviceCommand`)"; the brainstorm's "every read audited"; Phase 2, "Rollback"
- **Flaw:**
  - Each read reserves and then completes a persisted `CommandAudits` row.
  - There is no retention or pruning anywhere in `src/`.
  - The only index is the unique `(DeviceId, CorrelationId)`.
  - At every startup, `FailUnfinished` filters by `Status`, with no index on it.
  - The send path issues about 7 DB statements per command (FindPlan; Reserve: SELECT + INSERT; Record: SELECT + UPDATE, twice), plus one audit SELECT per 1 s poll.
  - A first Modbus read is 17 rows and about 150 statements per viewer click. Later reads are about 6 rows.
  - The plan treats auditing reads as free and does not discuss this at all.
- **Failure scenario:** 20 operators × 40 clicks a day × about 10 commands ≈ 8k rows a day, roughly 3M a year of low-value read audit. Startup gets slower as the startup scan has to read the whole growing table. Real write and relay audit rows are lost among read rows for anyone investigating an incident.
- **Evidence:**
  - `src/Hms.Server.Infrastructure/Persistence/EfCommandAuditStore.cs:85-105` (insert), `:126-181` (update), `:52-57` (`FailUnfinished` filter on `Status`).
  - `src/Hms.Server.Api/Program.cs:162` (runs at startup).
  - `src/Hms.Server.Infrastructure/Persistence/ApplicationDbContext.cs:161` (the only audit index).
  - `src/Hms.Server.Application/Commands/SendDeviceCommandHandler.cs:32`, `:138`, `:163`; `src/Hms.Server.Api/Controllers/DeviceCommandsController.cs:66`.
  - `grep -rn "Retention|Prune|ExecuteDelete" src`: no matches.
- **Suggested fix:** Make an explicit decision in the plan: keep auditing reads with a retention job (for example 30 days for class R) plus an index on `(Status)` or `(Status, CreatedAt)`, or log reads somewhere lighter. Either option needs a migration, which contradicts "không có migration", so state that openly.

## Finding 9: Rollout and rollback gaps: rolling back the catalog leaves roles un-editable, and an open device page never notices that the catalog changed
- **Severity:** Medium
- **Location:** Phase 1, section "Risk Assessment" ("Catalog hash đổi…", "Rollback: … không có migration"); Phase 2, section "Rollback"
- **Flaw:**
  1. **Rollback is not data-free.** After phase 1, admins add `tag.view`, `mb_rtu.slave_view` and `mb_rtu.tag_view` to persisted roles, as the plan's own risk section tells them to; master-admins may also add `sms.send` / `sms.delete`. Restoring the old JSON removes those codes from the permission vocabulary. `UpdateDeviceRoleHandler` validates the submitted codes against that vocabulary, so any edit that resubmits them fails with `DEVICE_PERMISSION_UNKNOWN`. Those roles cannot be edited until someone cleans the data by hand.
  2. **The device page never re-checks the catalog.** The re-pin changes both `catalog_version` (`mqtt-api-18a63e8ed`) and the hash. `DeviceDetailView` "verifies" the catalog it has just fetched against itself, so `outputsCatalogVerified` stays true until the page is reloaded. There is no `CATALOG_STALE` handler outside `App.vue`. A relay panel in use shows a bare `CATALOG_STALE`. Each card would show one `CATALOG_STALE` per command, because "lệnh sau vẫn chạy khi lệnh trước lỗi". Each of those is also a warning logged before the audit row is created.
  3. **Opening reads does not change the hash.** Which actions are open is not part of the catalog hash, so opening the reads in phase 2, or rolling that back, is invisible to the hash check. Browsers keep the old open/closed flags until they reload. The server still fails closed, but the plan's "catalog trusted" gate cannot see the change.
- **Failure scenario:** Phase 1 and 2 ship, and an org admin adds `mb_rtu.tag_view` to the "Vận hành" role. A regression forces a rollback of the catalog. The next time anyone edits "Vận hành" (for example to rename it), the request is rejected with `DEVICE_PERMISSION_UNKNOWN`. Separately, an operator with the device page open across the deploy presses relay ON and gets `CATALOG_STALE` with no hint to reload.
- **Evidence:**
  - `src/Hms.Server.Application/Commands/DevicePermissionVocabulary.cs:11-12`.
  - `src/Hms.Server.Application/Authorization/DeviceAccessHandlers.cs:96` (update validates codes), `:359-364` (`CheckVocabulary`).
  - `src/Hms.Server.Infrastructure/Persistence/EfDeviceAccessStores.cs:9` (roles are persisted).
  - `scripts/generate-command-catalog.mjs:417` (version derived from the commit).
  - `frontend/src/features/devices/views/DeviceDetailView.vue:249-251` and `:328` (`verifyCatalog(…, null, async () => cat, true)`).
  - `frontend/src/App.vue:778` (the only `CATALOG_STALE` handler).
  - `frontend/src/features/devices/components/DeviceOutputsPanel.vue:161-167`.
  - `src/Hms.Server.Application/Commands/SendDeviceCommandHandler.cs:76`, `:176-181`.
  - `src/Hms.Server.Infrastructure/Commands/PinnedCommandCatalog.cs:92` (hash over rows and domains only), `:203-209` (open/closed set outside the hash).
- **Suggested fix:**
  - Add a rollback runbook step to remove codes that are no longer known from roles, or make `CheckVocabulary` accept codes the role already holds.
  - In the cards and the relay panel, on `CATALOG_STALE`: stop the sequence, invalidate the catalog check, and show "Catalog đã đổi, tải lại trang". Add a test.
  - Document that phase 2's open/closed set is not covered by the hash.

## Finding 10: Phase 1's test list has wrong numbers and is incomplete, and one of its "must be red first" tests cannot be written first
- **Severity:** Medium
- **Location:** Phase 1, sections "Related Code Files → Test có assertion đổi có chủ đích" and "Tests first (viết trước, phải đỏ)", item 5
- **Flaw:**
  1. **The high-risk count is 21, not 20.** `HighRisk` works on permission codes, and an action counts as high-risk when its required permission does. `sms.send_status` (class R) requires `sms.send` (spec L823), so adding `sms.send` makes three rows high-risk, not two. The resolver test list must gain `sms.delete`, `sms.send` and `sms.send_status`; the plan names only two. It also leaves unmentioned that a read row becomes something only a master-admin may grant.
  2. **Assertions the plan does not list will break,** which contradicts plan.md's rule that only listed assertions change:
     - class counts: Read 49 → 57, Destructive 3 → 4, Physical 3 → 4;
     - TTL counts: 60000-ms rows 24 → 26, confirm rows among them 3 → 5, 120000-ms rows 70 → 78;
     - the confirm-template list gains `SMS-DELETE` and `SMS-SEND`.
     The new `FirmwarePermissions` count is left as "giá trị mới". It is 51: the new codes are `tag.view`, `mb_rtu.slave_view`, `mb_rtu.tag_view`, `sms.send` and `sms.delete`, while `mb_rtu.config_view` and `sms.view` already exist.
  3. **Test 5 is circular.** It is listed under "viết trước, phải đỏ", but its expected values come from the generator's output. Only the source pins can be derived independently and written first: blob `d0e6c3152a54030bbe81f71148a2bc8978bbdf6b` and SHA-256 `85f256f30fb4c080345aa03849db3f5ce44fdfaa3f931b596158ccccb721909a` (from `git show 18a63e8ed:… | sha256sum`; the same method reproduces the current pin `ef41c113…` for `83e93d77e`). `ManifestPairSetSha256` and `PinnedCatalogHash` are snapshots of the generator's output.
- **Failure scenario:** The implementer follows the plan, writes `Assert.Equal(20, …)`, and it never turns green. Or the implementer "fixes" unlisted assertions that the plan says must not change. Either way the TDD gate loses its meaning.
- **Evidence:**
  - `src/Hms.Server.Application/Commands/CommandPermissionResolver.cs:36-38`.
  - `src/Hms.Server.Domain/Authorization/DevicePermissionCodes.cs:21-30`.
  - `tests/Hms.Server.IntegrationTests/ManagementApiTests.cs:250`.
  - `tests/Hms.Server.UnitTests/CommandPermissionResolverTests.cs:134-149`.
  - `tests/Hms.Server.UnitTests/CatalogTests.cs:139-143`, `:180-184`, `:189-200`, `:155`.
  - spec §11 diff `83e93d77e..18a63e8ed`: exactly 10 new rows and no changed rows (verified by diffing the sorted tables).
  - `scripts/generate-command-catalog.mjs:423` (`source_hash` = SHA-256 of the `git show` bytes).
- **Suggested fix:**
  - Change the count to 21 and add `sms.send_status` to the resolver list.
  - List every assertion above with its new value, and set `FirmwarePermissions` to 51.
  - Move test 5's hash pins out of "phải đỏ". Write the blob and SHA-256 pins first, then snapshot the generated hashes after the step-3 diff.

---

## Fact check

| Plan claim | Result |
|---|---|
| `scripts/generate-command-catalog.mjs` `SOURCE_COMMIT` | VERIFIED `:21` |
| Generator test fixture `source_commit` / `catalog_version` | VERIFIED `generate-command-catalog.test.mjs:145,149` |
| Spec commit / blob `18a63e8ed9a6…` / `d0e6c3152a54…` | VERIFIED (`git rev-parse`) |
| §11 at the new pin = 104 rows, 10 new, no old row changed | VERIFIED (110 backticked rows − 6 domain rows; sorted diff) |
| `CatalogTests` pins at lines 15/18/19/24/30; 94 at 107/108/294; FirmwarePermissions 46 | VERIFIED `CatalogTests.cs:15-30,107,108,155,294` |
| `ManagementApiTests` 94 at line 191, highRisk 18 at line 250 | VERIFIED; **FAILED** "18 → 20" (should be 21) |
| `CommandPermissionResolverTests`: add `sms.delete`, `sms.send` | **FAILED**: `sms.send_status` is missing |
| Assertions listed in phase 1 are the only ones that change | **FAILED** (class, TTL and confirm tests; Finding 10) |
| `PinnedCommandCatalog.EnabledActions` (11 reads) | VERIFIED `PinnedCommandCatalog.cs:48-62` |
| `OutputCommandPolicy` refuses class B other than `set_output` / `pulse` | VERIFIED `OutputCommandPolicy.cs:28-51` |
| `DeviceLinkController.Allows` / `CanViewOutputs` | VERIFIED `DeviceLinkController.cs:33-40,87-88,217` |
| `In_scope_device_command_reaches_the_catalog_lookup` | VERIFIED `ManagementApiTests.cs:551` |
| `src/Hms.Server.Api/Authorization/` exists | VERIFIED |
| Reply timeout 30 s; frontend polls 1 s for up to 40 s | VERIFIED `MqttLinkOptions.cs:27`; `useDeviceCommand.ts:4,6` |
| "Client tải catalog mới và kiểm lại hash khi mở trang" | PARTIAL: the device page checks the catalog against itself (`DeviceDetailView.vue:328`) and never checks again |
| "Không tự đọc… server không lưu bản đọc" | PARTIAL: replies are kept in a global in-memory FIFO of 2048 (`DependencyInjection.cs:39`) and every read is a persisted audit row |
| Modbus "`total = 0`" means not configured | **FAILED** against spec L454–455 (tags and devices return a load error) |
| Phase 4 tools `setup-dev-secrets.ps1`, `import-gw-key.js`, `do-run.js` | VERIFIED, but only in this session's scratchpad (not in the repo; a new session will not have them) |
| Docs `docs/architecture-mqtt-device-link.md`, `docs/architecture-device-mqtt-api-catalog.md` | VERIFIED (both exist) |
| Rollback "không có migration" | **FAILED** in practice (Finding 9: persisted role codes; Finding 8: an index or retention will need a migration) |

## Consumers of the interfaces this plan changes

- **`PinnedCommandCatalog.EnabledActions` / `PhysicalOutputActions`:** `PinnedCommandCatalog.cs:205-206`; `ManagementApiTests.cs:200-201`; `CatalogTests.cs:118,124-125`.
- **`DevicePermissionCodes.HighRisk` / `IsHighRisk`:**
  - `CommandPermissionResolver.cs:36-38`, reaching `MeController.cs:71` (catalog `highRisk` flag);
  - `DeviceAccessHandlers.cs:57,106,166,324,367-369` (master-admin gate on role create, update and assign).
  - Adding `sms.send` puts `sms.send_status` behind that gate as well.
- **`FirmwarePermissions` (vocabulary):** `DevicePermissionVocabulary.cs:11-12`, used by `DeviceAccessHandlers.cs:56,96,359-364` (role create and update validation).
- **Catalog version and hash:**
  - server: `SendDeviceCommandHandler.cs:70-77`, `MeController.cs:57-59`;
  - client: `App.vue:124,774-783`, `DeviceCommandModal.vue:120-121,190-191`, `DeviceOutputsPanel.vue:120-121`, `DeviceDetailView.vue:249-251,328`.
- **`runDeviceCommand` / `useDeviceCommand`:** `DeviceOutputsPanel.vue:126`; `DeviceCommandModal.vue:37` (through `useDeviceCommand`); the new `useDeviceReads`.
- **`DeviceLinkController.Allows`** (to be extracted): `DeviceLinkController.cs:51,87,88`. As written it rebuilds the actor on every call (`:35`), which contradicts phase 2's "nạp actor một lần" for a ~35-action loop. The extracted helper must take an already-resolved actor and access.
- **`command_result` realtime event:** `DeviceLinkHandlers.cs:164-187`, consumed by `DeviceDetailView.vue:266-277` (and misread there as a heartbeat; Finding 7).

---

Status: DONE_WITH_CONCERNS
Summary: The plan's flow is sound for one user reading a small, stable gateway. It breaks under spec-documented conditions: saved and running Modbus configurations differing, pages straddling a restart, more tags than the list window, out-of-memory empty replies, a shared device lane, navigation away mid-read, and a rollback after roles were updated. Findings 1–5 should be resolved in phase 3 before implementation starts, and phase 1's counts (Finding 10) corrected before writing its tests.
