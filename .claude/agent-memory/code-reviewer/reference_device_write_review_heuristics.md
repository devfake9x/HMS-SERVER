---
name: device-write-review-heuristics
description: Probe heuristics that found real defects in HMS-SERVER device config-write reviews (UI confirm step bypassing the gate, live restore tools blocked by server-side lane holds, gate clocks that tick coarsely, invisible non-Cf names); use when reviewing any server-signed device write flow, its UI gate, or a live check/restore tool
metadata:
  type: reference
---

- Gate checked at "open confirm" but not at "confirm": the config cards (2026-11-06) ran `configGate` only when the confirm box opened; `confirm()` sent the frozen spec. Combined with "send back the fields you read" payloads (adc.set_channel echoes type/enabled), a confirm left open across another operator's change silently reverts it and the re-read reports "match" (expected = old record + edit). Always probe: open confirm, then make the gate go false (stale, configChangedAt, offline), then confirm.
- Live check/restore tools vs server lane holds: when the server holds a device write lane after an Acknowledged job reply (60 s), a tool that restores right after the job is terminal gets a lane refusal (classified not_sent) and leaves the device changed. Walk every server-side hold/rate rule against the tool's spacing; also check the tool pre-checks that the ORIGINAL value passes the server payload rule before writing a test value.
- Freshness gates fed by a component `now` ref: the read cards tick `now` every 15 s, so a "60 s" gate is really 60-75 s. Grep the interval feeding the gate.
- Name rules that block Unicode categories Cc/Cf/Zl/Zp/Co/Cn still pass invisible fillers (U+3164, U+115F: Lo; U+2800: So; U+034F, U+FE0F: Mn). Probe them with a file-based `dotnet run` app.
- A lone-surrogate JSON escape (`"\uD800"`) in a command payload 500s at `CommandRequestFingerprint.Compute` (STJ Serialize throws JsonException) before any policy runs; guards added later in the adapter do not change the HTTP outcome.

Related: [[hms-review-workflow]], [[authz-probe-heuristics]], [[reference-firmware-c-oracle]].
