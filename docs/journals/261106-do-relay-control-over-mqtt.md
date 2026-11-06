# 2026-11-06 — DO relay control over MQTT

Plan: `plans/261106-0735-do-relay-control/` (TDD, red-teamed before code). First write path on the live MQTT link:
`control.set_output` and `control.pulse`, with the relay state always confirmed by `control.get_status`.

## What shipped

- **Server gate before signing:** exact payload check, per-device output-channel allowlist (fail-closed,
  master-admin endpoint), 10 s TTL for physical commands (shorter than the 30 s reply timeout), pulse cap 60 s.
- **Verified DO state:** written only from a signed `get_status` reply, behind `gpio.do_view`. It goes stale
  after a write, an unknown outcome, a reboot or an offline device. The `outputs` realtime event carries no
  relay data, and telemetry no longer stores `digital_outputs`.
- **Audit:** issuer, detail and payload summary. On restart, unfinished rows (`Published` and `IN_FLIGHT`)
  become `OUTCOME_UNKNOWN_AFTER_RESTART`.
- **UI:** the DO panel is the only write path. A channel stays locked until its state is read back; the fake
  DO/DI data is gone.

## What the reviews caught

- **Tester:** four acceptance criteria had no test (telemetry 403, `OUTPUTS_NOT_READ`, CSRF on the allowlist,
  MQTT option validation). Tests were added for all four.
- **Code review, High:** the panel treated every thrown error as "never sent". A 500 that arrives after the
  publish would have unlocked DO1 with the relay already switched. The unlock rule now needs proof that nothing
  was sent: a contract refusal, a transport never-sent code, or HTTP 4xx.
- **Code review, Medium (spec R3):** an error reply does not prove a write did not run. Only refusals made
  before the handler ran keep the known state.
- **Smaller fixes:** a late "publish unconfirmed" result no longer overwrites the device's reply; marking the
  state stale no longer depends on the audit write; a re-bind can no longer restore an allowlist that a
  master-admin had narrowed.

## Live

- **Gateway:** DO1 ON and OFF, each read back in about 2 s. The sniffer and the device's own per-channel
  counters show nothing reached DO2–DO6. DO1 ended OFF and the allowlist was closed again.
- **Simulator (6 channels):** pulse is read twice (the second read shows the relay released). A silent device
  times out at 30 s and the panel reads back on its own.

## Surprises

- **Error code not in the spec:** a pulse on a `toggle` channel returns `ESP_ERR_INVALID_ARG` with text. The
  spec names no code; the simulator now copies the device.
- **Panel hidden without telemetry:** the panel sat in a card that only appeared after telemetry arrived, so a
  bound device with no heartbeat had no relay control. The simulator also sent no heartbeat, so once the panel
  treated "offline" as "unknown", every simulator channel showed as unknown. The fix was simulator heartbeats,
  not a looser rule.
- **Restart step not testable live:** dev runs on the in-memory DB, so a restart wipes the audit. That step is
  covered by unit and EF tests only.

## Open

- **Broker:** the anonymous lab broker allows replay of signed commands; it needs broker ACL and auth.
- **Low review items:** client-supplied `RequestedAt` skew, the dev seed re-opening the allowlist on each
  start, no backoff on `LANE_FULL`, payload-summary redaction before OTA actions open, and a few early refusals
  without a structured log.
- **Git:** not committed or pushed.
