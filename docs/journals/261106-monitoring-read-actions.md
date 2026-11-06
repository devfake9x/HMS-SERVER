# 2026-11-06 — Monitoring read actions over MQTT

Plan: `plans/261106-1040-monitoring-read-actions/` (TDD, red-teamed, then code-reviewed). Opens 15 read actions of
the pinned catalog and shows them on three on-demand cards on the device page: device & time, network, power &
ADC.

## Decisions that changed the plan

- **Spec re-pin dropped.** The first plan re-pinned the catalog to HMS-IDF `18a63e8ed` for `tags.*` and the
  Modbus paged reads. Red-team found that the generator rejects that spec: §11 prose says 98 pairs, its table has
  104. The new rows are an unreleased firmware working-tree overlay. User decision: drop Modbus and tags,
  keep the pin.
- **Device lane protection.** The device has one 16-slot queue and a rate bucket shared with relay commands:
  - reads are capped per device (4) and per user (half of that);
  - a read stops counting once past its expiry;
  - the relay read-back (`control.get_status`) has a reserved slot, because the code review showed the cap
    would otherwise block verifying a relay write.
- **TTL.** First set to 10 s for every command. Review pointed out that a device clock a few seconds ahead would
  then reject every read, including the diagnostic ones. Now 20 s for reads and other classes, 10 s for relay
  writes (both below the 30 s reply timeout).
- **Realtime.** Reads no longer emit `command_result` to every viewer (they poll). The device page counts only
  real heartbeats as "Last Heartbeat", and no longer invents one.

## Live (lab gateway, firmware 1.0.108)

- 15/15 reads `Executed`.
- 4 spec gaps:
  - `ota.get_status` nests `{success, data}`;
  - `wifi_ap_status` omits `channel`/`client_count` while off;
  - `adc.get_status` does carry `ai`;
  - `system.get_time` has extra `ntp_servers`/`ntp_mode`.
- One user firing 6 reads at once: 2 executed, 4 refused by the server (`DEVICE_READ_BUSY`), nothing dropped.
- Sniffer: message counts equal the reads sent, every read carries `ttl_ms` 20000, no write command.
- Real replies (anonymised) are now the card test fixture.

## Surprises

- The gateway command key changed again mid-day; signed commands time out silently when it does.
- The OTA reply nests one level deeper than the other reads.
- The device page reused the component on `/devices/:id` changes and could show device A's data under device
  B. Fixed on both the page and the relay panel.

## Open

- **Firmware team:** must fix §11's count and commit the overlay before Modbus live values and tags can be
  opened.
- **Read cap:** in-process only (single server instance assumed).
- **Audit:** no retention yet (every read writes a row).
- **Broker:** anonymous; replies are readable plaintext.
- **Git:** not committed.
