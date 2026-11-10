# Code review: DO channel ownership, phase 3 (tools + docs)

Date: 2026-11-10. Reviewer: code-reviewer. Scope: uncommitted `git diff -- tools docs` in worktree
`ck-project-management-review-dd19d6`. `src/`, `tests/`, `frontend/` were not reviewed: those are covered by
`code-review-server-frontend-report.md`. I read the server parsing code (`DeviceOutputState.cs`) only to check the
doc claims, and I read no frontend file.

## Scope

- Files: `tools/mqtt-broker/spec-device-simulator.js` (+69/-16), `tools/device-reboot-check.js` (+64/-5),
  `docs/architecture-mqtt-device-link.md` (+39/-4), `docs/architecture-device-mqtt-api-catalog.md` (+3/-1). In
  total 175 lines added and 26 removed.
- Contract: HMS-IDF `mqtt-api.md`, read only through `git show HEAD:` (HEAD = `c80797d9d`) for L797, L816-834,
  L847-853, L870-885 and HIL L1054-1057. I also ran `git show 9bfc16854:` (the previous spec commit, the one the
  safe-reboot plan used) and `git show 83e93d77e:` (the catalog pin) on the same file, to check the line numbers and
  the claims about which commit added what. I opened no other HMS-IDF file.
- The diff stats were the same at the start and at the end of the review, so nothing moved mid-review.

## Gates run

| Check | Result |
|---|---|
| `node tools/device-reboot-check.js --self-check` | PASS, exit 0 (`probes: 6 (2 simulator-only)`) |
| `node --check` on both tool files | clean |
| `git diff -- src/Hms.Server.Infrastructure/Commands/command-catalog.json` | empty; `CatalogTests.cs:15` still pins `83e93d77e4a4…` |
| Line endings | index LF for all four files (`git ls-files --eol`), so the diff has no CRLF churn |
| Probe A (scratch `probe-owner.js`) | Loads the tool's pure functions (`relaysOf`, `ruleHolders`, `preflightVerdict`, `BUSY_DETAIL`) without the main IIFE and feeds edge-case owners into them |
| Probe B (scratch `probe-sim.js`) | Runs the simulator's handlers in-process with `require('mqtt')` replaced by a stub (no socket is opened) and a dummy all-zero key. Covers 7 env scenarios: get_status, set_output on DO1-6, pulse on DO3 and DO6, then `scheduleReboot(0)` and get_status after the boot |

I started no broker, API or simulator process, and I did not run vitest.

## Overall assessment

The phase does what the plan asks for in the normal cases:
- The gateway layout matches HIL L1054.
- Refusal shapes match L822/L831.
- At boot, unforced channels go OFF, forced channels go to their force level, and the level rule sets its level
  again (L872-883).
- The default simulator behaves as before.
- `--reboot` still stops on a failing preflight.
- `BUSY_DETAIL` is unchanged.
- The catalog pin is untouched.

The defects are at the edges:
1. The preflight owner check passes (no FAIL, no ASK) on owner values that the server itself classifies as `unknown`.
2. `ASK` cannot stop `--reboot`. Meanwhile the docs now drop the manual "no DO rule" confirmation whenever the device
   reports `owner`, although the tool's own WARN says `owner` cannot rule out a rule that fires at boot.
3. The simulator's `control.pulse` ignores `enabled`/`gpioOk`/`forced`. So the header's statement that
   `SIM_DO_DISABLED` channels are "refused" is false for pulse.

There is no Critical or High finding: the gateway run is deferred and gated on a person in the lab plus the user's
"go".

## Critical

None.

## High

None.

## Medium

### M1. The preflight owner check passes on owner values the server classifies as `unknown`
- **Where:** `tools/device-reboot-check.js:235` (`relaysOf`: a non-string owner becomes `null`), `:242-249`
  (`ruleHolders`: only `level_rule|event_rule|trigger` are held, and only the literal `'unknown'` is unknown), and
  `:328-331` (ASK only when no channel has an owner, or a channel says `'unknown'`).
- **Why it matters:** the server normalizes every owner outside the 6 spec values to `"unknown"`
  (`DeviceOutputState.cs:207-213`, plan Decision 1), and plan Decision 12 asks for an ASK on `unknown` because it
  "does not prove there is no rule". The tool does not apply the same normalization, so the safety gate fails open on
  exactly those values.
- **Probe A output** (DO1 `none`, DO6 ON with the owner shown):

  ```
  future owner string "schedule" on DO6   check=PASS ask=false allPass=true
  case variant "Level_Rule" on DO6        check=PASS ask=false allPass=true
  owner number 2 on DO6                   check=PASS ask=false allPass=true
  owner missing on DO6 only               check=PASS ask=false allPass=true
  owner "unknown" on DO3                  check=PASS ask=true  allPass=true   (correct)
  level_rule on DO6                       check=FAIL ask=false allPass=false  (correct)
  ```
- **Failure scenario:** a later firmware adds an owner class such as `schedule`, or sends a partial table. The server
  panel shows "unknown" for that channel, but `--preflight` prints a plain PASS with no ASK. Per the new gateway
  precondition (M2), nobody is then asked to confirm rules by hand, and a real reboot can switch a rule-driven relay ON.
- **Fix:** classify by whitelist, the same way the server does:

  ```js
  const FREE_OWNERS = new Set(['none', 'force']);
  // held: RULE_OWNERS.has(r.owner); free: FREE_OWNERS.has(r.owner);
  // unknown: everything else, including null on a channel when other channels report an owner.
  ```

  Keep `null` for the whole table (no channel has an owner) as the existing "firmware does not report owner" ASK.
  Add self-check cases for an unrecognized string, a case variant, a non-string owner and a partial table.

### M2. `ASK` cannot stop `--reboot`, and the docs now drop the manual rule confirmation whenever `owner` is present
- **Where:**
  - `tools/device-reboot-check.js:474-477`: `runReboot` checks only `pre.pass`. The ASK lines (`:329-330`) are printed
    and the real reboot follows within seconds, with no pause.
  - `docs/architecture-mqtt-device-link.md:658` (gateway precondition): "user xác nhận không có luật DO
    `SET_HIGH`/`SET_LOW` khi `--preflight` in `ASK`; khi có `owner`, preflight tự kiểm…".
  - `:635`: ASK "không làm preflight FAIL".
- **Problem:** the tool's own always-printed WARN (`:332`, docs `:636`) says that `owner` only names who holds a
  channel at read time, and that a rule holding none can still fire SET_HIGH/SET_LOW at the first evaluation after
  boot (L883). The precondition used to require the manual confirmation unconditionally. It now requires it only on
  ASK, which removes the one mitigation for the residual risk the WARN describes. The plan's risk table ("Preflight
  PASS dù có luật chưa giữ kênh nào", Low × High) lists only the WARN and the reboot dialog as mitigations. Neither
  applies to a tool-driven `--reboot`.
- **Failure scenario 1:** firmware 1.0.108 sends no owner, which the plan rates as High likelihood. The operator runs
  `--reboot` directly instead of running `--preflight` first. The ASK scrolls past and the reboot is sent.
- **Failure scenario 2:** the firmware reports `owner`, and an event rule with a SET_HIGH action holds no channel at
  read time. The preflight passes, nobody confirms by hand, and the relay comes up ON after the boot.
- **Options.** This touches plan Decision 12, which the user approved, so it is not changed silently:
  - (a) Keep as is: accepted risk, with the WARN line only.
  - (b) Make `--reboot` refuse when any ASK line applies, unless the operator passes an explicit flag such as
    `--rules-confirmed`. The preflight verdict stays PASS, so Decision 12 ("không FAIL") holds. About 5 lines plus one
    self-check case.
  - (c) Keep the manual "no DO SET_HIGH/SET_LOW rule" confirmation unconditional in the gateway precondition
    (docs `:658` and `plans/261109-2250-safe-reboot/plan.md:125`). `owner` then becomes an extra automatic FAIL rather
    than a replacement for the manual check.
- **Recommendation:** (b) + (c). Both are cheap and leave the approved decision intact.

### M3. Simulator `control.pulse` ignores `enabled`, `gpioOk` and `forced`
- **Where:** `tools/mqtt-broker/spec-device-simulator.js:188-206` (pulse checks only `mode` and `duration_ms`).
  The header at `:51` says "SIM_DO_DISABLED=<list> channels reported **and refused** as enabled:false", and docs
  `architecture-mqtt-device-link.md:241` say "từ chối `channel_disabled`".
- **Probe B output:**
  - `SIM_DO_DISABLED=3,6` (default layout, DO6 in pulse mode): `pulse DO6 -> {"data":{"channel":6,"state":true,…}}`.
    A disabled channel switches ON.
  - `SIM_FORCED=2:on,6:off`: `pulse DO6 -> state:true`. A channel forced OFF switches ON. This part predates the diff,
    but spec L847 says force blocks every other write source, MQTT included.
- **Impact:** the simulator exists to "catch a server that only agrees with itself" (`:2`). A server or tool bug that
  pulses a disabled or forced channel passes E2E here. The panel locks these channels, so the UI path is not affected
  today.
- **Fix:** before the mode check in pulse, reuse the same three refusals: `gpio_unavailable`, `channel_disabled`,
  `channel_forced`, each with `channel`. Do not add an owner check (L818, question 17). Update the header and docs
  `:230`/`:241` so they say exactly which actions refuse.

## Low

### L1. Spec line references now mix two spec commits
After c80797d added the `owner` text, every number from L831 on moved by one or more lines. Pre-existing references
were not rebased, although the new text calls the spec "HEAD c80797d":
- `tools/device-reboot-check.js:333` (WARN) and `docs/architecture-mqtt-device-link.md:658`, a paragraph this diff
  edited, cite **L850** for the "2026-11-08, about 0.14 s" fact. At c80797d that fact is at **L851**; L850 is now the
  force NVS-order bullet. It was L850 at `9bfc168`.
- `spec-device-simulator.js:166` cites L831 for `busy`. At c80797d, L831 is `owned_by_rule` and `busy` is L832.
- `spec-device-simulator.js:27` cites "§6.4 L866-880" for reboot DO behaviour. At c80797d that is L870-885, which the
  new header line `:44` already uses.
- **Fix:** rebase these to c80797d, or tag them `@9bfc168`.

### L2. Doc wording the code or spec does not back
1. Catalog `:173`: "Spec HEAD `c80797d` … thêm field kênh và lỗi `owned_by_rule`". `enabled/gpioOk/forced` were
   already in L797 at `9bfc168`. What c80797d added is `owner`, `owner_id` and `owned_by_rule` (0 hits for either at
   `9bfc168`).
2. Catalog `:170`: "`owner_id` là id luật khi có". The spec (L797) says it is the rule id when the owner is
   `level_rule`, otherwise `null`.
3. Device-link `:160`: "`null` khi thiết bị không gửi **hoặc gửi sai kiểu**" is stated for all five fields. For
   `owner`, the code maps a wrong type (number, object) to `"unknown"`, not `null` (`DeviceOutputState.cs:211-212`).
4. Device-link `:240` and simulator `:49`: "firmware trước `c80797d`". c80797d is a spec commit, not a firmware build.
   The HIL evidence names ELF `dc1703a9…` (L1054). Say "firmware that does not send `owner`" instead.
5. Device-link `:206`: uses L853 ("rule sets the level again within 1 s") as a general rationale. L853 states that
   bound only for after `force_clear`.
6. Device-link `:211`: "khoá hiện ra ngay sau lần từ chối đầu" is true only when the device reports `owner`. In S2 (no
   owner) no lock appears.

### L3. `SIM_DO_DISABLED` is not validated and can produce a state the spec does not describe
- **Where:** `spec-device-simulator.js:81` (`.map(Number)`, no check).
- `SIM_DO_DISABLED=3;5` silently disables nothing (probe B: every channel `en=true`). In contrast, `SIM_FORCED`
  validates its items and exits with code 2 on a bad one.
- `SIM_DO_LAYOUT=gateway SIM_DO_DISABLED=6` gives a channel with `enabled:false` reporting ON that is driven ON again
  after the boot (probe B).
- **Fix:** accept only integers in 1..N, otherwise `fail()`. Optionally refuse combining a disabled channel with the
  rule-held DO6.

### L4. `channelView` derives `owner_id` from the stored owner instead of the owner it sends
- **Where:** `spec-device-simulator.js:139-142`.
- A channel that is both forced and rule-held would send `owner:"force"` together with `owner_id:<rule>`, which
  breaks L797. No current configuration reaches this state (the gateway table has no forced rule channel, and the
  default layout never has `level_rule`), so it is latent only.
- **Fix:** compute `owner` first, then set `owner_id` only when that computed owner is `level_rule`.

### L5. Accidental edit to the `--plan` output
- **Where:** `device-reboot-check.js:573`. The `--checks` line lost one space and is now misaligned with every other
  mode line (seen in the `--plan` output).
- The diff did not need to touch this line, so it is scope drift. **Fix:** restore the original spacing.

### L6. The ASK and owner-notes logic is not under `--self-check`
- **Where:** `device-reboot-check.js:327-331`.
- The self-check proves what `ruleHolders` returns, not that the ASK line is printed. The ASK decision lives inside
  `runPreflight`, which does I/O. As a smaller point, `relaysOf` turns an object `owner_id` into
  `"[object Object]"`, where the server gives `null`. It only appears in the printed detail.
- **Fix:** extract `ownerNotes(relays)` as a pure function, self-check it for the cases listed in M1, and make it
  return the ASK decision that option (b) of M2 needs.

## What is fine (verified)

### (a) Simulator
- `SIM_DO_LAYOUT=gateway` matches plan Decision 11 and HIL L1054 (probe B):
  - DO1 `none`.
  - DO2/DO3 `gpioOk:false`.
  - DO4/DO5 `forced:true`, `owner:"force"`, `owner_id:null`.
  - DO6 toggle, ON, `owner:"level_rule"`, `owner_id:1`.
  - A bad layout value exits with code 2 (`:79`). Ignored variables are logged (`:82`).
- `SIM_DO_NO_OWNER=1` leaves out only `owner`/`owner_id`. `enabled`, `gpioOk` and `forced` are still sent, and every
  refusal is unchanged, including `owned_by_rule` with `rule_id:1`.
- Refusals match L822/L831: envelope `success:false`, `error:"ESP_ERR_INVALID_STATE"`,
  `data:{error, channel}`, plus `rule_id` only with `owned_by_rule`. They come in the documented order. `channel_forced`
  before `owned_by_rule` agrees with the only order the spec does give (L894, set_config).
- Boot (L872-883):
  - Gateway layout: DO1-DO5 OFF, DO6 ON again.
  - `SIM_FORCED=2:on,6:off`: DO2 ON, everything else OFF.
  - The pulse channel is OFF.
- The default layout is unchanged except for the added fields: same channel states, modes, set_output acceptance and
  boot result. The only shape changes are `owner:"none"|"force"`, `owner_id:null` on get_status and `channel` on the
  `channel_forced` refusal. The server's `ToResult` detail is still `ESP_ERR_INVALID_STATE: channel_forced`.
- Safe-reboot scenarios 2a-9 stay reproducible. 2a now shows 8 preflight checks instead of 7 (the owner check passes
  on default), and scenario 6 boots exactly as before.

### (b) Tool
- `relaysOf` reads `owner`/`owner_id`.
- `ruleHolders` returns `null` when no channel has an owner, and returns `held`/`unknown` otherwise.
- The FAIL detail has the format `DO6 level_rule #1`.
- The WARN about L883 is printed on every preflight (`:332`).
- `--reboot` returns before any send when the preflight fails (`:475`; live S5a confirms 0 `system.reboot` commands).
- `BUSY_DETAIL` (`:182`) is byte-identical to before. All 3 new reasons are asserted as non-busy.
- `--self-check` passes with the new cases.

### (c) Docs
- The catalog is still pinned to `83e93d77e` and `command-catalog.json` is unchanged.
- The field list, the lock table, the translations, the reboot dialog and result text, and the tool owner bullets
  match the code and the phase reports. Exceptions are in L1 and L2. I did not check the frontend file names in the
  docs against code (no frontend reading); the phase 1-2 review covers them.
- The `#simulator` anchor resolves: there is one `### Simulator` heading.
- The brainstorm §3.D section exists (`brainstorm-261108-1700…md:96`).

### (d) Safety
- Neither tool can reach `192.168.1.101:443` or `hms_toan_lab/#` by default:
  - The simulator needs `SIM_MQTT_URL`, `SIM_TOPIC_BASE` and `SIM_KEY_FILE` (`:62-64`) and has no default for any of
    them.
  - The reboot tool talks only to `HMS_API_BASE` (default `127.0.0.1:5099`) and needs `--device <guid>` and
    `--confirm-live`.
  - The only lab strings are a pre-existing example comment (`:9`), the docs' PowerShell example (topic
    `hms_server_selftest/…`) and a provenance comment (`:195`).
- No key, password or token is printed or added. The simulator never logs the key, the credentials come from env
  through `tool-env.js`, and `tools/device-reads/` is gitignored.

## Edge cases found while probing

The M1 and M3 probes, the L3 and L4 states, and the drift in the L1 line numbers.

One pre-existing gap outside this diff: the simulator accepts numeric `value`s other than 0/1 (`2` turns the relay
ON), and uses its own text instead of `invalid_channel`/`invalid_value`. That matches the pinned `83e93d77e` ("Numeric
value cũng được nhận") but not HEAD L818. It is harmless while the server sends only booleans.

## Recommended actions

1. M1: whitelist-based owner classification plus self-check cases.
2. M2: present options (a), (b) and (c) to the user. Recommended: (b) `--rules-confirmed` for `--reboot` on ASK, and
   (c) keep the manual rule confirmation unconditional in the gateway precondition.
3. M3: gpio, disabled and forced refusals on `control.pulse`, and correct the header and docs.
4. L1-L6: rebase line numbers, fix the doc wording, validate `SIM_DO_DISABLED`, derive `owner_id` from the owner that
   is sent, restore the `--plan` spacing, and extract `ownerNotes`.

## Plan follow-ups (for the lead; plan files not edited)

- Phase 3 Success Criteria: the simulator, tool and docs items are met, apart from the findings above. The E2E S1-S5
  items are recorded in `live-verification.md`.
- If M2(c) is accepted, `plans/261109-2250-safe-reboot/plan.md:125` needs the same wording change as docs `:658`.

## Unresolved questions

- M2: whether the user accepts dropping the manual rule confirmation when `owner` is present (plan Decision 12 and the
  risk-table row).

Status: DONE_WITH_CONCERNS
Summary: Phase 3 tools and docs meet the plan in the normal cases (gateway layout, refusal shapes, boot behaviour,
FAIL blocks `--reboot`, BUSY_DETAIL unchanged, catalog pin intact, nothing reaches the lab by default). Findings:
0 Critical, 0 High, 3 Medium (owner check fails open on values the server calls unknown; ASK cannot stop `--reboot`
while the docs drop the manual rule check; simulator pulse ignores disabled/gpio/forced), 6 Low.
Concerns/Blockers: M2 touches a user-approved decision (plan Decision 12); options listed, needs the user's choice.

## Xử lý

Date: 2026-11-10. M2 chưa làm (chờ quyết định của user). Line refs đã kiểm lại với `git show HEAD:` (c80797d).

- M1 done: `ruleHolders` phân loại theo whitelist (`none`/`force` tự do, ba loại luật là held, còn lại là unknown, gồm chuỗi lạ, đổi hoa thường, không phải chuỗi, thiếu owner khi kênh khác có). Cả bảng không có owner vẫn `ASK`. `--self-check` thêm 4 ca (chuỗi lạ, đổi hoa thường, số, thiếu một kênh): đỏ trước (`ownerNotes is not defined`), xanh sau.
- M3 done: `control.pulse` từ chối `gpio_unavailable`, `channel_disabled`, `channel_forced` (cùng dạng và thứ tự với `set_output`, dùng chung `channelBlock`), không kiểm owner. Kiểm bằng stub `require('mqtt')`: DO disabled/forced/gpio đều bị từ chối khi pulse; DO6 level_rule vẫn chạy tới kiểm mode.
- L1 done: WARN L850 -> L851, busy L831 -> L832, reboot DO L866-880 -> L870-885, docs :658 L850 -> L851.
- L2 done: 6 điểm sửa (catalog 2 điểm, device-link 4 điểm; điểm 3 nêu rõ `owner` sai kiểu thành `"unknown"`, thiếu thì `null`).
- L3 done: `SIM_DO_DISABLED` chỉ nhận số nguyên 1..`SIM_CHANNELS`, sai thì thoát mã 2; kết hợp với `SIM_DO_LAYOUT=gateway` bị từ chối (thoát mã 2), chọn cách này vì đơn giản hơn là định nghĩa lại boot cho kênh tắt.
- L4 done: `owner_id` suy từ `owner` thực sự gửi.
- L5 done: khôi phục khoảng trắng của dòng `--checks` trong `--plan`.
- L6 done: `ownerNotes(relays)` thuần, trả `{ holders, ask }`, được `--self-check` phủ; `runPreflight` chỉ in `ask`.
- M2 done (user chose (b) + (c), 2026-11-10): `rebootBlockedByAsk({ pass, ask, rulesConfirmed })` is pure and covered by `--self-check` (red: `rebootBlockedByAsk is not defined`; green after). `--reboot` sends nothing and returns failure when the preflight printed an ASK line unless `--rules-confirmed`; a failed preflight blocks regardless; the verdict stays PASS. Flag is in the usage comment and `--plan`. The manual no-DO-rule confirmation is unconditional again in `docs/architecture-mqtt-device-link.md` (gateway precondition, cites spec L883 at c80797d, verified), `plans/261109-2250-safe-reboot/plan.md`, and plan Quyết định 12 and phase-03 "As built" record the decision.
