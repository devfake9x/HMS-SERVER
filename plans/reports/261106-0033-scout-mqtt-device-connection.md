---
type: scout-report
date: 2026-11-06
scope: Kết nối MQTT server ↔ thiết bị HMS-IDF, ưu tiên làm nhanh các tính năng MQTT
contract: HMS-IDF/engineering-docs/api/mqtt-api.md (chỉ đọc spec, không đọc firmware source)
---

# Scout Report — Kết nối MQTT với thiết bị HMS-IDF

## Tóm tắt

Server .NET 10 đã có đủ phần "xung quanh": pipeline lệnh, phân quyền, catalog 94 lệnh, audit, secret provisioning, SignalR và UI. **Phần lõi MQTT trong .NET thì chưa có gì.** Không có thư viện MQTT nào, cũng không có client hay config.

Có 3 chốt chặn cứng trong code:
1. `DependencyInjection.cs:32-36` throw lỗi khi `enableLiveMqtt=true`, nên DI luôn đăng ký `DeferredDeviceCommandTransport`.
2. `PinnedCommandCatalog.cs:174` gán cứng `DeniedUntilActionContract` cho mọi lệnh, nên không lệnh nào tới được adapter.
3. `HmsIdfProvisionalProtocolAdapter` dựng envelope **sai spec và không ký**, luôn trả `CanPublish:false`.

Telemetry hiện chỉ vào server qua bridge Node (`tools/mqtt-broker/hms-telemetry-bridge.js`). Bridge này đăng nhập bằng cookie admin, cào API web của RTU, tự bịa thêm vài field, rồi POST vào REST.

## Relevant Files

### Pipeline lệnh (giữ nguyên, cắm MQTT vào đây)
- `src/Hms.Server.Application/Commands/CommandContracts.cs` — các port `ICommandProtocolAdapter`, `IDeviceCommandTransport` (`:91`), `ICommandAuditStore`, `ICommandCatalog`. Enum `Acknowledged/Executed/Failed` (`:38-41`) có sẵn nhưng chưa dùng.
- `src/Hms.Server.Application/Commands/SendDeviceCommandHandler.cs` — chạy các bước kiểm tra theo thứ tự: actor → idempotency/key-generation fence → device scope → catalog trust/hash → availability (`:80`) → permission → policy → confirm → TTL → audit reserve. Sau đó gọi adapter → transport → audit (`:150-152`).
- `src/Hms.Server.Application/Commands/DeferredDeviceCommandTransport.cs` — stub luôn trả `MQTT_DEFERRED`. Đây là transport đang được DI đăng ký.
- `src/Hms.Server.Application/Commands/RecordingDeviceCommandTransport.cs` — test double.
- `src/Hms.Server.Application/Commands/CommandPermissionResolver.cs`, `DevicePermissionVocabulary.cs` — map catalog row hoặc config domain sang permission (lấy theo spec §11).
- `src/Hms.Server.Api/Controllers/DeviceCommandsController.cs` — `POST api/v1/devices/{id}/commands`.

### Envelope / catalog (phải sửa)
- `src/Hms.Server.Infrastructure/Commands/HmsIdfProvisionalProtocolAdapter.cs` — envelope `{req_id, client_id, ts, data{action:"type.action", params}, ttl_ms, dry_run}` không có `sig`. Topic `hms_toan_lab/{gateway_870c|guid:N}/cmd` bị hardcode (`:50-53`).
- `src/Hms.Server.Infrastructure/Commands/PinnedCommandCatalog.cs` — nạp `command-catalog.json` (94 lệnh, 6 config domain, version `mqtt-api-83e93d77e`, có kiểm tra SHA-256). Availability bị hardcode ở `:174`.
- `scripts/generate-command-catalog.mjs` — sinh catalog từ spec §11 tại commit được pin (`--check` để chỉ so sánh).
- `src/Hms.Server.Infrastructure/DependencyInjection.cs` — nơi đăng ký transport/adapter (`:32-39`).
- `src/Hms.Server.Api/Program.cs` — `:35` gọi `AddHmsInfrastructure` mà không truyền cờ MQTT. `/health` báo `mqtt="deferred"` (`:49`). Device `870c870c-…` được seed ở `:104-114`.

### Telemetry / realtime (giữ, thêm nguồn vào từ MQTT)
- `src/Hms.Server.Api/Controllers/DevicesController.cs:96-128` — endpoint ingest `POST /devices/{id}/telemetry`: ghi store, chuyển trạng thái Provisioning→Active, publish event `status`. Logic này nên tách thành service để MQTT subscriber gọi được.
- `src/Hms.Server.Application/Realtime/DeviceTelemetryContracts.cs` — `IDeviceTelemetryStore` (in-memory, mất khi restart).
- `src/Hms.Server.Application/Realtime/RealtimeContracts.cs` — `BoundedDeviceEventPublisher` (coalesce các event `status`/`capabilities`).
- `src/Hms.Server.Api/Hubs/DeviceEventsHub.cs`, `SignalRDeviceEventDeliveryService.cs` — hub `/hubs/device-events`, group `device:{id:N}`, re-authorize theo epoch.

### Provisioning / secret
- `src/Hms.Server.Application/Provisioning/ProvisioningContracts.cs` — bundle chỉ hỗ trợ `PRIMARY_ONLY`. Broker username có dạng `hms-{deviceId:N}`, password 32 B, `CommandKey` 32 B.
- `src/Hms.Server.Infrastructure/Persistence/EfProvisioningSecretGenerator.cs` — `DeviceKeysets` được bảo vệ bằng DataProtection.
- `src/Hms.Server.Infrastructure/Persistence/EfProvisioningBundleStore.cs` — bundle TTL 15 phút, chỉ tải được 1 lần.
- `src/Hms.Server.Domain/Devices/Device.cs` — **chưa có** `topic_prefix` hay MQTT `device_id`.

### API báo trạng thái sai
- `src/Hms.Server.Api/Controllers/CommandTransportController.cs` — hardcode `MQTT_ACTIVE`, `canPublish=true`, broker `broker.emqx.io:1883`. Thực tế không có kết nối nào. Controller này cũng không có `[Authorize]`.

### Tests
- `tests/Hms.Server.UnitTests/CommandBoundaryTests.cs` — contract suite cho transport: Recorded/Deferred không bao giờ báo Published, các bước kiểm tra phải từ chối trước transport, idempotency, key-generation fence. Dùng lại được cho transport MQTT.
- `tests/Hms.Server.UnitTests/CatalogTests.cs`, `DeviceTelemetryTests.cs`, `RealtimeTests.cs`.
- `tests/Hms.Server.IntegrationTests/MaitechRtuDeviceIntegrationTests.cs` — topic mapping, REST telemetry, giữ `digital_outputs` khi nhận partial status.
- Chưa có test nào cho MQTT client, publish/ack, `cmd/res`, hay telemetry đi qua MQTT.

### Frontend (Vue)
- `frontend/src/features/**/DeviceDetailView.vue` — load `/me/command-catalog` và telemetry, nhận live qua SignalR (`JoinDevice`/`DeviceEvent`), badge DO/DI.
- `frontend/src/features/**/DeviceCommandModal.vue` — form lệnh JSON chung, gửi `{type, action, payload, correlationId, catalogVersion, catalogHash}`.
- `frontend/src/core/realtime/useDeviceOperationsHub.ts`, `core/api/adminApi.ts:87` — đã xử lý sẵn các trạng thái `MQTT_DEFERRED`/`Published`/`Acknowledged`/`Executed`.
- `frontend/src/rtuHardwareHil.test.ts` — bản phác spec, không import code production.

### Tools
- `tools/mqtt-broker/broker.js` — broker Aedes `0.0.0.0:8088`, không auth, không TLS.
- `tools/mqtt-broker/hms-telemetry-bridge.js` — bridge tạm (sẽ thay).
- `tools/mqtt-broker/listener.js` — sniff `hms_toan_lab/#` trên broker public. Hữu ích khi làm HIL.
- `tools/*.js` — script CDP điều khiển UI web RTU/HMS, không dùng MQTT.

### Docs / quyết định (authoritative: ADR + spec)
- `docs/decisions/260927-be-modular-monolith-mqtt-business-boundary.md` — MQTT chỉ nằm trong Infrastructure. `MqttHostedService` không được chứa switch theo action.
- `docs/decisions/260927-mqtt-late-bound-adapter.md` — các port. Live publish chỉ được bật khi đã có fixture chứng minh topic leaf dưới `/cmd/#`.
- `docs/decisions/260925-emqx-trusted-identity.md` — ACL EMQX theo từng device, envelope identity fail-closed.
- `docs/architecture-device-mqtt-api-catalog.md` — topic taxonomy + 94 action. §3 superseded từ 2026-10-05.
- `docs/architecture-real-device-mqtt-integration.md` — luồng thiết bị thật `gateway_870c`.
- `plans/261001-0030-full-hardware-mqtt-apis-c2/phase-02-live-mqtt-transport.md` — thiết kế transport + `DeviceCommandResponseConsumer` (envelope ở đây cũng sai spec).
- `plans/261005-0556-device-permissions-what-where/plan.md` — phase 1-2 xong. Phase 3-6 pending, đang chặn plan 261001. Quyết định Q14 tạm dừng các việc phụ thuộc MQTT.
- `docs/architecture-hms-server.md` — **SUPERSEDED**, không dùng.

### Spec (hợp đồng duy nhất)
- `HMS-IDF/engineering-docs/api/mqtt-api.md` — §1 kết nối, §2 topic, §3 envelope, §4 ký, §5 response/operation, §7 confirm, §9 telemetry, §11 manifest quyền.
- `HMS-IDF/engineering-docs/api/status/{capability-matrix,gaps-and-issues,mqtt-http-api-grouped-checklist}.md`.

## Contract từ spec — những gì cần cho bản build đầu

| Hạng mục | Spec nói | Nguồn |
|---|---|---|
| Base topic | `B = <topic_prefix>/<device_id>`. **Không được mặc định `hms`**, phân biệt hoa thường | §2 L435, L92 |
| Lệnh | publish `B/cmd/<type>`, QoS 1, retain=false. Device subscribe `B/cmd/#` và route theo `type` + `data.action` | §2 L448 |
| Response | `B/cmd/res`, match theo `id`. **Phải subscribe trước khi publish** | §2, R2 |
| Status | `B/status` heartbeat 3 s, `B/status/events`, `B/status/connectivity` (LWT, retained) | §2, §10 |
| Capabilities | `B/capabilities` retained, chỉ trên primary broker | §2 L450 |
| Envelope | `type`, `id` (≤39 B), `ts` (Unix **giây**), `data:{action, data?}`, `ttl_ms?`, `key_ver?`, `confirm?`, `dry_run?`, `response_topic?`, `trace_id?`, `sig` **đặt cuối**. Tổng ≤ **1023 B kể cả sig** | §3 |
| Ký | `HMAC-SHA256(key32, compact_utf8_json_không_sig)`. Không KDF, không sort key. Bỏ `}` cuối rồi nối `,"sig":"<hex>"}`. Gửi đúng byte đó, không serialize lại | §4.1-4.2 |
| Verify response | `HMAC(key, "res:" + response_không_sig)` | §4 |
| Lỗi | (a) admission: `expired`, `future_timestamp`, `UNAUTHORIZED`, `LANE_FULL`, `confirm_required`, `schema:*`…; (b) handler: `esp_err_t` + `data.error`; (c) RTU: `commitOutcome`. **Ký sai thì device im lặng, không trả gì** | §5 |
| Lệnh chạy lâu | `ESP_ERR_TIMEOUT` + `{accepted, pending, operation_id}` → poll `system.get_operations` | §5 |
| Retry | Sau 30 s không có trả lời: đọc lại state hoặc job trước, rồi gửi lại với `id`/`ts` mới | R4 |
| Confirm | Chuỗi `REBOOT-<device_id>`, `OTA-APPLY-…`, `SMS-SEND-…`… | §7 |
| Telemetry | plaintext `{id,v,q,u,ts}` hoặc batch. SIGN: HMAC với `tlm_sign` trên `id+t+n+d`. ENCRYPT: AES-256-GCM với `tlm_data`. Server tự chống replay | §9 |
| Quyền | Device **không** tự kiểm quyền: ai giữ key là có quyền owner. Server phải kiểm quyền theo §11 **trước khi ký** | §0 R11, §11 |

**Action đã đóng `SLICE_PASS`, dùng tốt cho slice đầu (read-only):** `system.get_info`, `system.get_heap`, `mqtt.get_status`, `mqtt.get_config`, `status.dump`, `status.services`, `status.tags`, `control.get_status`. Ghi chú thêm:
- `system.ping` là smoke test được spec khuyên dùng, nhưng **chưa có receipt**.
- Lệnh ghi đã `SLICE_PASS` duy nhất là `control.pulse`.
- Trạng thái release của sản phẩm: **0/14 release-qualified**.

## Code đang lệch spec (phải sửa trước khi publish)

| Code hiện tại | Spec |
|---|---|
| `req_id`, `client_id`, `data.params` | `id`, `type`, `data.data` |
| `data.action = "type.action"` | `data.action` chỉ là tên action, còn `type` nằm ở root |
| Không có `sig`, `key_ver` | Mặc định `signed`: lệnh không ký bị từ chối, kể cả ping |
| Topic `…/cmd` | `B/cmd/<type>` |
| Prefix `hms_toan_lab` và token `gateway_870c` hardcode | Lấy `topic_prefix` + `device_id` từ provisioning của từng thiết bị |
| Chặn khi ≥1024 B **trước** khi ký | Giới hạn ≤1023 B **sau** khi gắn sig |
| Không có ai nhận `cmd/res` | Correlate theo `id` → audit + SignalR |

## Đề xuất các lát cắt nhanh (MQTT trước)

Thứ tự: inbound trước (rủi ro thấp, thấy ngay dữ liệu thật), outbound read-only sau, lệnh ghi cuối cùng. Toàn bộ nằm sau các port đã có, nên đúng ADR modular monolith.

1. **Slice 0 — nền tảng.**
   - Thêm `MQTTnet` vào `Hms.Server.Infrastructure`.
   - Thêm config `Mqtt:{Enabled,Host,Port,UseTls,ClientId,Username,Password}`.
   - Thêm MQTT identity cho từng device (`TopicPrefix`, `MqttDeviceId`) kèm migration.
   - Bỏ cái `throw` trong DI, thay bằng cờ config.
   - Sửa `CommandTransportController` để báo trạng thái thật.
2. **Slice 1 — inbound.**
   - Tạo hosted service subscribe `+/+/status`, `status/connectivity`, `capabilities` → `IDeviceTelemetryStore` + `IAuthorizedEventPublisher` (tách logic từ `DevicesController.cs:96-128` thành service dùng chung).
   - UI hiện có hiển thị luôn, không cần sửa.
   - Bỏ được `hms-telemetry-bridge.js`.
3. **Slice 2 — outbound read-only.**
   - Viết lại adapter đúng §3/§4 với HMAC dùng `CommandKey` theo `KeyGeneration`.
   - Tạo `MqttDeviceCommandTransport` (QoS 1).
   - Tạo consumer `cmd/res`: verify `res:` HMAC → audit `Acknowledged/Executed/Failed` → event SignalR; timeout 30 s.
   - Availability chuyển thành allowlist cho các action `SLICE_PASS` read-only ở trên.
4. **Slice 3 — lệnh ghi.**
   - `control.pulse`, confirm flow (§7), poll `system.get_operations`.
   - Đối chiếu với `B/capabilities` lúc runtime.
5. **Sau đó:** telemetry SIGN/ENCRYPT (§9), dual broker, các action còn lại theo tiến độ release gate của spec.

## Hygiene (ngoài scope, chỉ ghi nhận)
- `AGENTS.md` ở root là file của **dự án khác** (admin monorepo Bun/Hono/fe-mono), không mô tả repo .NET này. Tool `codebase-retrieval` mà nó nhắc tới cũng không có trong session.
- Credential bị hardcode trong `tools/mqtt-broker/hms-telemetry-bridge.js`, `tools/test-query-telemetry.js`, `tools/login-and-*.js` và integration test.
- Các file bị track nhầm trong git:
  - `cookies.txt`
  - `tools/mqtt-broker/node_modules` (1479 file)
  - `frontend/.npm-cache` (391 file)
  - `src/*/.tmp-unit-out` (121 file)
  - `.tmp-backend-obj`
- Frontend có bản sao trùng: `src/useDeviceOperationsHub.ts`, `src/signalRConnection.ts`, `src/adminApi.ts` (bản cũ).
- Docs mâu thuẫn nhau:
  - GUID device: `a74041f1-…` hay `870c870c-…`?
  - Số kênh DO: 4 hay 6? Số kênh ADC: 16 hay 8?
  - Roadmap 2026-09-30 đã cũ.

## Unresolved Questions

1. **Gỡ tạm dừng Q14?** Yêu cầu "xây MQTT trước" ngược với quyết định 2026-10-05 (dừng việc phụ thuộc MQTT) và với blocker plan 261005 (phase 3-6 chưa xong). Cần xác nhận: làm MQTT trước các phase đó.
2. **Command key của thiết bị thật** *(spec gap G15: spec không định nghĩa cách cấp key)*. Server tự sinh `CommandKey`, nhưng `gateway_870c` đang giữ key nào? Có 3 phương án:
   - (a) Admin nhập key hex 64 của device vào `DeviceKeysets`.
   - (b) Device lab chạy security posture `open` (chấp nhận lệnh không ký) cho slice 2.
   - (c) Chờ firmware có kênh provisioning.
3. **Broker dev:** dùng broker public `broker.emqx.io:1883` (device thật đang ở đó, nhưng không TLS, không auth) hay dựng EMQX local (docker) rồi trỏ device sang?
4. **Mapping `topic_prefix`/`device_id`:** lưu trên `Device` hay trong provisioning bundle? `ALIGNMENT.md` ghi `hms/<device_id>`, trái với spec ("không mặc định `hms`").
5. **Credential MQTT của chính server, ACL template, cách học `key_ver`:** spec chưa định nghĩa (gap).
6. **Trạng thái trong spec tự mâu thuẫn:**
   - `tags.list`/`tags.get`: L60, L1909 và L2063 ghi khác nhau.
   - `network.get_all_status`: `SLICE_PASS` (L50) nhưng lại `SLICE_BLOCKED_ENV` (L1945).
   - C13-C15 bị chặn rồi được waiver.
   - Nguồn sự thật: L403 và L13 nói khác nhau.

   → Chỉ đưa vào allowlist những action không bị tranh chấp.

Spec gaps ở trên được giữ lại để user quyết định. Không gửi cho team firmware trừ khi user yêu cầu.
