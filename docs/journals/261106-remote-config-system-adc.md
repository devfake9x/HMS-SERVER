# 2026-11-06 — Remote config writes: system and ADC

Plan: `plans/261106-2033-remote-config-system-adc/` (TDD, red-teamed, then code-reviewed). Opens 5 configuration
writes of the pinned catalog: hostname, timezone, NTP sync, ADC channel rename and ADC channel enable. The writes go
through forms on the device page's two read cards, not through the generic C2 modal.

## Decisions that changed the plan

- **`system.set_ntp_servers` stays closed.** The first plan opened it under `sys.time_edit`. Red-team showed that a
  wrong NTP server sets the device clock wrong. Then every later command is refused as `expired` or
  `future_timestamp`, and a clock before 2020 turns off the command age check (spec §2, §3), so old commands can be
  replayed. `system.sync_ntp` stays open because it only syncs with the configured servers. `system.set_time` was
  closed from the start for the same reason. The opened count went from 34 to 33.
- **Write lane for every class except reads and relays.** W, C, D and F share one lane per device: one pending
  write, at most 10 writes per minute, and the slot is held until the reply or the sweep. Red-team found that a
  write the device accepted as a job (`Acknowledged`) freed the lane while the job could still run. That reply now
  holds the lane for 60 s more.
- **Server time for writes.** A client may send `RequestedAt` up to 60 s ahead, which keeps a signed command valid
  on the device long after the lane reopens. Writes now always use server time. Reads and relays still take the
  client value; that residual risk is in the architecture doc.
- **`adc.set_channel` always sends all four fields.** The spec does not say whether an omitted `type` or `enabled`
  is kept or reset. The UI and the tool send the values read just before, and the server accepts `type` and
  `enabled` as optional only so it can echo them back. The read-back compares the whole channel record, not just
  the name.
- **Permission refusal stays HTTP 200.** The plan first said 403. Red-team showed the existing command contract
  returns 200 with `DEVICE_PERMISSION_REQUIRED`, and the UI already follows that.
- **Same read-back rule as the DO panel.** Only `certainlyNotSent` or HTTP 4xx counts as "not sent". Anything else
  re-reads the state and never resends the write.

## Live (lab gateway, firmware 1.0.108)

- 5 of 5 writes `Executed`. Each read back the new value, and restore brought the original back.
- Final state (hostname, timezone, NTP mode, AI-1 name, type and enabled) equals the values read before the run.
- Sniffer over the whole session: 48 commands, 48 replies (none `success:false`), 11 writes (9 from the script and
  2 from the UI rename), `ttl_ms` 20000 on every command.
- No write was answered with a job. Firmware 1.0.108 answers directly, so the job path is covered by the simulator
  only.
- A hostname change did not drop the MQTT session while `client_id` was set.
- Details: `plans/261106-2033-remote-config-system-adc/reports/live-verification.md`.

## Surprises

- **Lone-surrogate escapes returned HTTP 500.** A payload with `"\uD800"` failed inside System.Text.Json while the
  request fingerprint was computed, before any guard ran. It now returns `COMMAND_PAYLOAD_INVALID`. Channel names
  with a lone surrogate are refused by the policy too.
- **Confirm could send a stale edit.** Review found that the confirm box stayed open after the gate closed. If
  another operator disabled a channel meanwhile, the first operator's confirm re-enabled it and still reported
  success, because the read-back matched the old record. Confirm now re-checks freshness, the source reading and
  config changes before it sends.
- **The restore was blocked by the job hold.** A write accepted as a job holds the lane for 60 s. The simulator in
  `SIM_ADC_JOB=1` mode showed the tool's restore refused with `DEVICE_WRITE_BUSY`, which the tool first treated as a
  stop. The tool now waits and sends again only the refusals the server makes before publishing.
- **Invisible channel names passed the first rule.** Hangul filler, braille blank and variation selectors are not
  whitespace, so a plain check let them through. Such names render blank or look like another channel. The policy
  refuses them now.

## Open

- **Firmware team (through the user):**
  - whether `adc.set_channel` keeps or resets `type`/`enabled` when they are omitted;
  - the TZ format (only `ICT-7` and `UTC0` are accepted for now);
  - the valid `type` values and the `name` length limit;
  - the hostname effect on the MQTT `client_id` and session when `client_id` is empty (untested), and what
    `applied_immediately: false` means.
- **Job path:** untested on real firmware. No config write got an `accepted, still running` reply from 1.0.108.
- **In-memory lane:** a server restart drops the lane. Pending writes become `OUTCOME_UNKNOWN_AFTER_RESTART`. This
  assumes one server instance.
- **Audit:** `Acknowledged` rows for writes are never closed, because the server does not follow the job.
- **Clock:** the lane hold assumes the device clock is within 2 s of the server. A slow device clock can run an old
  write after the lane reopens. This needs a backed-up queue as well, so it is rare.
- **Roles:** the dev environment has no device roles, so `reports/affected-roles.md` has no names yet. Run its
  read-only query on each Postgres environment before deploy.
- **Git:** not committed.
