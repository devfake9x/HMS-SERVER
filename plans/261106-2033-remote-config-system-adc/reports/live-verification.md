# Live verification — config writes (Hệ thống & ADC)

Date: 2026-11-06.

## Setup

| | |
|---|---|
| Device | lab gateway `hms_toan_lab/gateway_870c` (seed device `870c870c-870c-870c-870c-870c870c870c`) |
| Firmware | 1.0.108 (IDF v5.5.5), from `system.get_info` |
| Command key | generation 3, re-imported after the API restart; signed reads `Executed` before the run |
| Broker | local TLS Mosquitto (`192.168.1.101:443`) |
| Firmware team | told beforehand by the user |
| Sniffer | on `hms_toan_lab/gateway_870c/cmd/#` for the whole session |

## Simulator first

The simulator ran on `hms_server_selftest/sim_001`, with no gateway involved.

- **Tool in normal mode:** every step passed; the final state equals the originals.
- **Tool in `SIM_ADC_JOB=1` mode:**
  - the rename reply was "accepted, still running"; the tool polled `system.get_operations` until `terminal`;
  - the restore was refused with `DEVICE_WRITE_BUSY` during the server's 60 s job hold, nothing sent; the tool waited and then sent it;
  - the final state equals the originals.
- **UI (hostname, ADC toggle, ADC rename in Vietnamese, ADC rename as a job):**
  - every write showed "Đã ghi và xác nhận";
  - the simulator received each write exactly once;
  - `adc.set_channel` carried the `type`/`enabled` values read before the write.

## Gateway: `tools/device-config-check.js --confirm-live`

| Field | Write | Outcome | Read back | Restore | Read back |
|---|---|---|---|---|---|
| hostname | `system.set_hostname` `<orig>-t` | `Executed` | `<orig>-t` | `Executed` | `<orig>` |
| timezone | `system.set_timezone` `UTC0` | `Executed` | `UTC0` | `Executed` | `ICT-7` |
| AI-1 name | `adc.set_channel {ai:1, name:"AI-1-t", type:"4-20mA", enabled:true}` | `Executed` | name changed, type and enabled unchanged | `Executed` | `AI-1` |
| AI-1 enabled | `adc.toggle_channel {ai:1, enabled:false}` | `Executed` | `enabled:false` | `Executed` | `enabled:true` |
| NTP | `system.sync_ntp` | `Executed` | `ntp_synced:true`, device clock 0 s off | — | — |

Observations:
- **Hostname:** MQTT `client_id` is set on the gateway, so the hostname step ran. The link stayed online for the 15 s watch after each hostname write, with no heartbeat or connectivity drop.
- **Write replies:** every reply came back directly; no job reply on this firmware.
- **Restore check:** `--restore` with the saved originals found no difference (`[]`) and sent no write.

## Gateway: UI

- **Rename through the "Nguồn & ADC" card:** AI-1 → `AI-1 ui` → `AI-1`.
- **Each change:** a confirm showing old → new; "Đã ghi và xác nhận" with the re-read value.
- **Screenshot:** [screens/adc-rename-lab-gateway.jpg](screens/adc-rename-lab-gateway.jpg), ADC card only. The time card holds the hostname, so it is not kept.

## Sniffer

| | Count |
|---|---|
| Commands | 48 |
| Replies | 48, none with `success:false` |
| Writes | 11: 9 from the tool script + 2 from the UI rename, all listed in the two sections above |
| `ttl_ms` | 20000 on every command |

No write outside the script was seen.

## Final state

Hostname, timezone, `ntp_mode` (auto), and AI-1 name/type/enabled equal the values read before the run.

## Spec gaps seen live

- **`adc.set_channel`:** the server always sends `type` and `enabled` back. Whether the device would reset an omitted field is still unanswered (asked of the firmware team).
- **Hostname and MQTT session:** a hostname change did not drop the MQTT session while `client_id` is set. Behaviour with an empty `client_id` is untested; the tool skips that case.
- **Job replies:** none of the five writes produced a job ("accepted, still running") reply on firmware 1.0.108. The job path is checked against the simulator only.

## Files (gitignored, not committed)

- `tools/device-reads/2026-11-06T15-52-51-843Z-config-original-870c870c-….json` (originals)
- `tools/device-reads/2026-11-06T15-52-51-843Z-config-check-870c870c-….json` (step report)
- Sniffer log in the session scratchpad.
