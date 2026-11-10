# 2026-11-09 — Safe reboot over MQTT (`system.reboot`)

Plan: `plans/261109-2250-safe-reboot/` (TDD, red-teamed with 15 accepted findings, then validated). Opens one class F
command, `system.reboot`, for master-admins only. The server checks before it signs, holds the gate from the moment
it publishes, watches for the device to come back, and never resends. The device page has its own button with a
dry-run first and a confirmation that lists what happens to the relays.

## Decisions that changed the plan

- **Master-admin at send time.** The first plan let any holder of `sys.reboot` send. Red-team showed that this
  weakened the approved criterion, so class F now fails with `HIGH_RISK_REQUIRES_MASTER_ADMIN` before the audit slot
  and before signing. It costs nothing today because no HTTP endpoint creates roles or memberships. Standards
  (K8s, AWS, GCP, Azure) limit who may *grant* a permission, not who may use it at run time; this is stricter than
  they are, on the user's decision.
- **Payload `{}` only.** `delay_ms:0` could make the reply miss the reboot and hid the deadline from the server.
  The device default (2000 ms) is used and the server knows it.
- **The gate is armed at publish, not at the success reply.** The first design armed it on a success reply, so a
  lost, late or `Acknowledged` reply left reboots ungated. The registry now runs an `admit` function inside the
  atomic step, so a refused reboot does not use a write slot either. A 60 s minimum between real reboots matches
  the device throttle.
- **TTL 10 s is about a late reboot, not replay.** It does not stop replay on a slow device clock or broker
  redelivery. Those risks stay with the live preconditions (`clean_session`, clock within 5 s) and, before
  production, broker ACLs.
- **Uptime comes from the heartbeat message**, not from the telemetry store that an HTTP route and the bridge also
  write. Heartbeats are unsigned, so the audit says `evidence=heartbeat` and the UI proves the restart with the
  signed `system.get_info` uptime.
- **Conditional audit writes.** `TryCompleteAsync(plan, result, fromCodes)` writes only when the current code is in
  the allowed set, so a late result never overwrites a row that already moved on.

## Simulator run (2026-11-10)

All against `hms_server_selftest/sim_001` on a loopback Mosquitto, with `tools/device-reboot-check.js`:

- Normal: `SCHEDULED`, then `OBSERVED` at 10 s; a dry-run probe during the hold got `DEVICE_REBOOT_IN_PROGRESS`.
- Never comes back: `NOT_OBSERVED` at 124 s, nothing resent; when the simulator returned, the row went to
  `OBSERVED` (`late_s=48`).
- Reboot without a reply: `DEVICE_RESPONSE_TIMEOUT` at 31 s, probe refused during the hold, `OBSERVED` with
  `reply=none` at 124 s.
- Real command silent and no reboot: `DEVICE_RESPONSE_TIMEOUT`, row left as it was.
- Forced channels: DO1 off, DO2 (forced on) on, DO4 (forced off) off after the reboot.
- Device throttle: second reboot rejected as `UNAUTHORIZED`; the server hold was released.
- Server restart during tracking: 404 `COMMAND_NOT_FOUND` on the in-memory store.
- Details: `plans/261109-2250-safe-reboot/reports/live-verification.md`.

## Things that surprised us

- **The repo's dev broker does not work with the server.** `tools/mqtt-broker/broker.js` (Aedes) is MQTT 3.1.1
  only and the server's MQTTnet 5 client asks for 5.0; the server loops on `MQTT connect refused: 1`. The run used
  a loopback Mosquitto. Not fixed.
- **A late reboot can look "not verified".** If the reply is lost and the restart is seen only at the end of the
  hold, a device that had just booted before can show a larger uptime than the one read earlier. The audit still
  says `OBSERVED`; the UI says "chưa xác minh". The tool reports a clock-adjusted check next to the strict one.
- **The simulator's boot timing does not transfer.** It closes the socket (so the last will arrives at once) and
  boots in 8 s. A real chip may hold the TCP session half open until the broker's 1.5 x keepalive.

## Gateway

Deferred. Nobody is at the lab and another session is running HIL on the bench; a gateway that does not come back
cannot be left unattended. Next step, only with someone at the lab, the bench free and the user's "go": preflight,
redacted sniffer, UI reboot, `--after`, `--restore-do`, measure the boot time. Open spec questions for the firmware
team are listed in `docs/architecture-mqtt-device-link.md`, section "Khởi động lại an toàn".

## Before production

Broker ACLs. The lab broker is anonymous, so a captured signed reboot can be replayed.

## After review

The server and tools review found that the hold could end before a long reply timeout returned, so it now lasts to the later of accept + 122 s and the reply deadline + 2 s. A drop heard before the reply is now carried into the watch, a rebind or revoke between the reply and the SCHEDULED write no longer leaves a row stuck, and an `Acknowledged` reply is recorded as `reply=accepted`. The tool now counts a refusal as "not sent" only when the server cannot have published, and the busy retry matches the spec-shaped `ESP_ERR_INVALID_STATE: busy`. The UI review and a walkthrough found that the dialog closed on any relay event, that a previous attempt's notice persisted, and that a lost send followed by a 404 read as a restart. These are fixed: the run lives on the device page so a tab switch keeps it, the dialog closes only on a real relay change or an untrusted list, a 404 after an unconfirmed send reads as unconfirmed, and a C2 action without a class is hidden. After the tool fixes, the normal and forced-channel simulator scenarios were re-run and passed; the tab-switch case was checked in the UI. Final tests: unit 640, integration 126, vitest 475. A closed class F row is refused for master-admin too, and a test covers it. Deferred as residual: a short broker-link drop is never seen by the tracker because the link reconnects before the sweep; a new real reboot inside the 10-minute tail replaces the earlier record; frontend residuals (L1, L4 to L8, L11) are listed in the phase 2 report. One existing flaky metrics test (`SignalRConnectionBacklogTests`) failed once in about 35 runs and is unrelated. The gateway run stays deferred, and broker ACLs remain a precondition for production.
