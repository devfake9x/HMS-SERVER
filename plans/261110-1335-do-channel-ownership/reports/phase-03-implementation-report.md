# Phase 3 report

- Baseline (step 0): `--self-check` PASS (1 PASS line, 0 FAIL), `node --check` on the simulator clean.
- Red: after adding the new cases `--self-check` died with `ruleHolders is not defined`. Old simulator: `get_status` had no `owner`, DO2 accepted a write.
- Green: `--self-check` PASS (new cases: ruleHolders x5, relaysOf x2, preflightVerdict x3, BUSY_DETAIL x3); `node --check` clean; `SIM_DO_LAYOUT=bogus` exits 2 with the message.
- E2E: see `live-verification.md` (S1/S2/S3 data and refusals, S5a FAIL / S5b ASK / S5c PASS, `--reboot` stops at a failing preflight, one real reboot on the simulator with DO6 ON after boot).
- Files: `tools/device-reboot-check.js`, `tools/mqtt-broker/spec-device-simulator.js`, `docs/architecture-mqtt-device-link.md`, `docs/architecture-device-mqtt-api-catalog.md`, `plans/261109-2250-safe-reboot/plan.md` (one line), `plans/reports/brainstorm-261108-1700-mqtt-spec-coverage-and-requests.md` (§3.D), the plan's `plan.md`, the phase file, reports. `src/`, `tests/`, `frontend/` untouched; `command-catalog.json` diff empty.
- Processes started and stopped: Mosquitto 11248, API 43724 (child of dotnet run 30444), simulators 36552 then 13692. Ports 8088 and 5099 free afterwards; lab broker PID 34804 untouched.
- Open: UI walkthrough S1-S4 (controller); questions 17-19 not sent; follow-up (a) gateway read.
