// tools/device-reboot-check.js
// Live check of `system.reboot` on one device, through the HMS-SERVER API (the server signs; this
// tool never needs the device command key). Needs a master-admin account: the server refuses class F
// commands for everybody else. It never resends `system.reboot`: a reboot whose outcome is not known
// is only followed up with a read of the signed `system.get_info`.
//
//   node tools/device-reboot-check.js --plan                      print the steps, send nothing
//   node tools/device-reboot-check.js --self-check                check the probe table, send nothing
//   node tools/device-reboot-check.js --device <guid> --confirm-live --preflight
//   node tools/device-reboot-check.js --device <guid> --confirm-live --checks [--sim]
//   node tools/device-reboot-check.js --device <guid> --confirm-live --dry-run
//   node tools/device-reboot-check.js --device <guid> --confirm-live --reboot [--rules-confirmed] [--expect <kind>] [--correlation-id <id>]
//                                     [--window-s 180] [--tail-s 0] [--sim]
//   node tools/device-reboot-check.js --device <guid> --confirm-live --probe-in-progress --watch <correlationId> [--after-s 3]
//   node tools/device-reboot-check.js --device <guid> --confirm-live --after <before.json>
//   node tools/device-reboot-check.js --device <guid> --confirm-live --restore-do <before.json>
//
// --expect (reboot): observed (default) | observed_reply_none | not_observed | open | rejected | server_restarted
// --sim adds the probes that only make sense on the spec simulator (wrong confirm, payload with
//   delay_ms): they are dry runs refused before signing, so they cannot reach a device either.
//
// Env: HMS_ADMIN_EMAIL / HMS_ADMIN_PASSWORD (see tool-env.js), HMS_API_BASE (default
// http://127.0.0.1:5099). Output: tools/device-reads/ (gitignored). Nothing secret is printed or written.
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const base = process.env.HMS_API_BASE || 'http://127.0.0.1:5099';
const outDir = path.join(__dirname, 'device-reads');
// Server: MqttDeviceCommandTransport.NeverSentCodes.
const NEVER_SENT = new Set(['MQTT_NOT_CONNECTED', 'MQTT_RESPONSE_SUBSCRIPTION_MISSING', 'MQTT_COMMAND_TOPIC_INVALID']);
// Codes of a row that is still open: the server holds the reboot gate or may still move the row on.
const OPEN_CODES = new Set(['DEVICE_REBOOT_SCHEDULED', 'MQTT_PUBLISHED', 'DEVICE_RESPONSE_TIMEOUT', 'MQTT_PUBLISH_UNCONFIRMED', 'DEVICE_ACCEPTED']);
const FINAL_CODES = new Set(['DEVICE_REBOOT_OBSERVED', 'DEVICE_REBOOT_NOT_OBSERVED', 'DEVICE_REBOOT_OUTCOME_UNKNOWN', 'DEVICE_REJECTED']);
const CLOSED_ROWS = ['system.factory_reset', 'ota.apply', 'ota.remote_start'];
const MAX_CLOCK_SKEW_S = 5;

/**
 * Probes the server must refuse before it signs anything. Every one of them sends `dryRun:true`, so
 * even if the gate were broken the device would only see a harmless dry run (spec 4.2). The live ones
 * carry no confirm and an empty payload; the simulator-only ones carry the one field under test.
 */
const PROBES = [
  { name: 'reboot without confirm', pair: 'system.reboot', dryRun: true, payload: {}, expect: 'CONFIRM_REQUIRED' },
  { name: 'factory_reset', pair: 'system.factory_reset', dryRun: true, payload: {}, expect: 'DENIED_UNTIL_ACTION_CONTRACT' },
  { name: 'ota.apply', pair: 'ota.apply', dryRun: true, payload: {}, expect: 'DENIED_UNTIL_ACTION_CONTRACT' },
  { name: 'ota.remote_start', pair: 'ota.remote_start', dryRun: true, payload: {}, expect: 'DENIED_UNTIL_ACTION_CONTRACT' },
  { name: 'reboot with a wrong confirm', pair: 'system.reboot', dryRun: true, payload: {}, confirm: 'REBOOT-not-this-device', expect: 'CONFIRM_MISMATCH', simOnly: true },
  { name: 'reboot with delay_ms', pair: 'system.reboot', dryRun: true, payload: { delay_ms: 2000 }, confirmFor: true, expect: 'COMMAND_PAYLOAD_INVALID', simOnly: true },
];

function selfCheck() {
  const problems = [];
  for (const p of PROBES) {
    if (p.dryRun !== true) problems.push(`${p.name}: dryRun must be true`);
    if (!p.simOnly) {
      if ('confirm' in p || p.confirmFor) problems.push(`${p.name}: a live probe must not carry a confirm`);
      if (Object.keys(p.payload).length) problems.push(`${p.name}: a live probe must have an empty payload`);
    }
    if (p.pair === 'system.reboot' && p.dryRun !== true) problems.push(`${p.name}: a reboot probe must be a dry run`);
  }
  // A possibly-published answer must never count as "not sent".
  const cases = [
    [200, { status: 'Failed', code: 'MQTT_PUBLISH_UNCONFIRMED' }, false],
    [200, { status: 'Failed', code: 'DEVICE_REJECTED' }, false],
    [200, { status: 'Failed', code: 'DEVICE_RESPONSE_TIMEOUT' }, false],
    [200, { status: 'Published', code: 'MQTT_PUBLISHED' }, false],
    [200, { status: 'Failed', code: 'CONFIRM_REQUIRED' }, true],
    [200, { status: 'Failed', code: 'DEVICE_REBOOT_IN_PROGRESS' }, true],
    [200, { status: 'Failed', code: 'MQTT_NOT_CONNECTED' }, true],
    [200, { status: 'ContractUnavailable', code: 'CONFIRM_MISMATCH' }, true],
    [403, null, true],
  ];
  for (const [http, dispatch, expected] of cases)
    if (refusedResult(http, dispatch) !== expected) problems.push(`refusedResult(${http}, ${dispatch?.status}/${dispatch?.code}) should be ${expected}`);
  for (const [detail, expected] of [['ESP_ERR_INVALID_STATE: busy', true], ['busy', true], ['LANE_FULL', true], ['UNAUTHORIZED', true], ['ESP_ERR_INVALID_ARG: channel must be an integer >= 1', false], ['ESP_ERR_INVALID_STATE: channel_forced', false],
    ['ESP_ERR_INVALID_STATE: owned_by_rule', false], ['ESP_ERR_INVALID_STATE: gpio_unavailable', false], ['ESP_ERR_INVALID_STATE: channel_disabled', false]])
    if (BUSY_DETAIL.test(detail) !== expected) problems.push(`BUSY_DETAIL on "${detail}" should be ${expected}`);

  // Channel ownership (control.get_status owner / owner_id).
  const holders = relays => JSON.stringify(ruleHolders(relays));
  const names = list => (list ?? []).map(c => c.channel).join(',');
  const base0 = () => ({ link: { online: true }, info: { uptimeSeconds: 5 }, cleanSession: true, time: { skewSeconds: 0 }, configTrialState: 'none', ota: { known: true, idle: true } });
  // Anything outside the spec's six values is unknown: case variants, non-strings, and a channel without owner next to channels that report one.
  const odd = [
    ['future owner string', [{ channel: 1, owner: 'none' }, { channel: 6, owner: 'schedule' }]],
    ['case variant', [{ channel: 1, owner: 'none' }, { channel: 6, owner: 'Level_Rule' }]],
    ['non-string owner', relaysOf({ channels: [{ channel: 1, owner: 'none' }, { channel: 6, owner: 2 }] })],
    ['owner missing on one channel', [{ channel: 1, owner: 'none' }, { channel: 6, owner: null }]],
  ];
  for (const [label, relays] of odd) {
    const h = ruleHolders(relays);
    if (!h || names(h.unknown) !== '6' || h.held.length) problems.push(`ruleHolders ${label} should be unknown on DO6, got ${holders(relays)}`);
    if (!String(ownerNotes(relays).ask).startsWith('ASK')) problems.push(`ownerNotes ${label} should ASK`);
    const v = preflightVerdict({ ...base0(), relays }).find(x => x.name === OWNER_CHECK);
    if (!v || v.pass !== true) problems.push(`preflightVerdict ${label} should keep the owner check passing (ASK, not FAIL)`);
  }
  if (!String(ownerNotes([{ channel: 1, owner: null }]).ask).includes('does not report owner')) problems.push('ownerNotes without any owner should ASK that the firmware does not report owner');
  if (ownerNotes([{ channel: 1, owner: 'none' }, { channel: 4, owner: 'force' }]).ask !== null) problems.push('ownerNotes none/force should not ASK');
  if (ownerNotes([{ channel: 6, owner: 'level_rule', ownerId: '1' }]).ask !== null) problems.push('ownerNotes on a held channel should not ASK (the check FAILs instead)');
  if (ownerNotes([{ channel: 3, owner: 'unknown' }]).ask === null) problems.push('ownerNotes unknown should ASK');
  if (ownerNotes(null).ask !== null) problems.push('ownerNotes of unreadable relays should not ASK');
  // --reboot gate: an ASK blocks the real reboot unless --rules-confirmed; a failed preflight blocks regardless.
  const gate = (pass, ask, rulesConfirmed) => rebootBlockedByAsk({ pass, ask, rulesConfirmed });
  if (!gate(true, 'ASK x', false)) problems.push('rebootBlockedByAsk: ASK without --rules-confirmed should block');
  if (gate(true, 'ASK x', true)) problems.push('rebootBlockedByAsk: ASK with --rules-confirmed should allow');
  if (gate(true, null, false)) problems.push('rebootBlockedByAsk: no ASK should allow');
  if (!gate(false, null, true) || !gate(false, 'ASK x', true)) problems.push('rebootBlockedByAsk: a failed preflight should block even with --rules-confirmed');
  if (ruleHolders([{ channel: 1, owner: null }, { channel: 2, owner: null }]) !== null) problems.push('ruleHolders without any owner should be null');
  if (ruleHolders(null) !== null) problems.push('ruleHolders of a non-array should be null');
  const free = ruleHolders([{ channel: 1, owner: 'none' }, { channel: 4, owner: 'force' }]);
  if (!free || free.held.length !== 0 || free.unknown.length !== 0) problems.push(`ruleHolders none/force should hold nothing, got ${holders([{ channel: 1, owner: 'none' }, { channel: 4, owner: 'force' }])}`);
  const level = ruleHolders([{ channel: 6, owner: 'level_rule', ownerId: '1' }]);
  if (names(level?.held) !== '6') problems.push('ruleHolders level_rule on DO6 should be held');
  const mixed = ruleHolders([{ channel: 2, owner: 'event_rule' }, { channel: 3, owner: 'trigger' }]);
  if (names(mixed?.held) !== '2,3') problems.push('ruleHolders event_rule and trigger should both be held');
  const unknown = ruleHolders([{ channel: 3, owner: 'unknown' }]);
  if (!unknown || unknown.held.length !== 0 || names(unknown.unknown) !== '3') problems.push('ruleHolders unknown should list DO3 as unknown, not held');
  const owned = relaysOf({ channels: [{ channel: 6, state: true, owner: 'level_rule', owner_id: 1 }] })?.[0];
  if (owned?.owner !== 'level_rule' || owned?.ownerId !== '1') problems.push(`relaysOf should read owner and owner_id, got ${JSON.stringify(owned)}`);
  const bare = relaysOf({ channels: [{ channel: 1, state: false }] })?.[0];
  if (bare?.owner !== null || bare?.ownerId !== null) problems.push(`relaysOf without owner should give null, got ${JSON.stringify(bare)}`);
  const base = { link: { online: true }, info: { uptimeSeconds: 5 }, cleanSession: true, time: { skewSeconds: 0 }, configTrialState: 'none', ota: { known: true, idle: true } };
  const ownerName = OWNER_CHECK;
  const withOwner = preflightVerdict({ ...base, relays: [{ channel: 6, state: true, owner: 'level_rule', ownerId: '1' }] }).find(v => v.name === ownerName);
  if (!withOwner || withOwner.pass !== false || !(withOwner.detail ?? '').includes('DO6 level_rule #1')) problems.push('preflightVerdict should fail on a level_rule channel and name DO6 level_rule #1');
  const clean = preflightVerdict({ ...base, relays: [{ channel: 1, state: false, owner: 'none' }] }).find(v => v.name === ownerName);
  if (!clean || clean.pass !== true) problems.push('preflightVerdict should pass the owner check when no rule holds a channel');
  if (preflightVerdict({ ...base, relays: [{ channel: 1, state: false, owner: null }] }).some(v => v.name === ownerName)) problems.push('preflightVerdict should have no owner check when the device sends no owner');
  return problems;
}

const jar = new Map();
const cookie = () => [...jar].map(([k, v]) => `${k}=${v}`).join('; ');
const sleep = ms => new Promise(r => setTimeout(r, ms));
const now = () => new Date().toISOString();
let csrf = null;
let catalog = null;

async function call(method, route, body) {
  const res = await fetch(base + route, {
    method,
    headers: { 'Content-Type': 'application/json', Cookie: cookie(), ...(csrf && method !== 'GET' ? { 'X-CSRF-TOKEN': csrf } : {}) },
    body: body ? JSON.stringify(body) : undefined,
  });
  for (const c of res.headers.getSetCookie?.() ?? []) {
    const [kv] = c.split(';');
    const i = kv.indexOf('=');
    jar.set(kv.slice(0, i), kv.slice(i + 1));
  }
  const text = await res.text();
  let json;
  try { json = JSON.parse(text); } catch { json = text; }
  return { status: res.status, json };
}

async function login() {
  const { hmsAdminCredentials } = require('./tool-env');
  const { email, password } = hmsAdminCredentials();
  csrf = (await call('GET', '/api/v1/auth/csrf')).json.requestToken;
  const res = await call('POST', '/api/v1/auth/login', { email, password, rememberMe: false });
  if (res.status >= 300) throw new Error(`login failed (${res.status})`);
  csrf = (await call('GET', '/api/v1/auth/csrf')).json.requestToken;
  catalog = (await call('GET', '/api/v1/me/command-catalog')).json;
}

/** The three closed class F rows must be unavailable before anything is sent. */
function assertClosedRows() {
  const open = CLOSED_ROWS.filter(key => catalog.actions.find(a => `${a.type}.${a.action}` === key)?.available !== false);
  if (open.length) throw new Error(`catalog check failed: ${open.join(', ')} is not unavailable; nothing was sent`);
  const reboot = catalog.actions.find(a => a.type === 'system' && a.action === 'reboot');
  if (!reboot?.available) throw new Error('catalog check failed: system.reboot is not available for this server');
  if (!catalog.trusted) throw new Error('catalog check failed: the pinned catalog is not trusted');
}

const fieldsOf = data => {
  const node = data && typeof data === 'object' && !Array.isArray(data) ? data : null;
  return node && !('success' in node) ? node : (node?.data && typeof node.data === 'object' ? node.data : node);
};

async function send(deviceId, pair, { payload = {}, confirm, dryRun = false, correlationId } = {}) {
  const [type, action] = pair.split('.');
  const body = { type, action, payload, dryRun, catalogVersion: catalog.catalogVersion, catalogHash: catalog.catalogHash };
  if (confirm !== undefined) body.confirm = confirm;
  if (correlationId) body.correlationId = correlationId;
  const sent = await call('POST', `/api/v1/devices/${deviceId}/commands`, body);
  return { http: sent.status, dispatch: sent.json, at: now() };
}

// Codes the server answers (HTTP 200, status Failed) for checks made before anything is signed or published.
const REFUSED_BEFORE_SIGNING = new Set([
  'HIGH_RISK_REQUIRES_MASTER_ADMIN', 'DEVICE_PERMISSION_REQUIRED', 'CONFIRM_REQUIRED', 'CONFIRM_MISMATCH', 'COMMAND_PAYLOAD_INVALID',
  'DEVICE_REBOOT_IN_PROGRESS', 'DEVICE_REBOOT_TOO_SOON', 'DEVICE_WRITE_BUSY', 'DEVICE_WRITE_RATE_LIMITED', 'DEVICE_READ_BUSY',
  'CATALOG_STALE', 'DENIED_UNTIL_ACTION_CONTRACT',
]);

// A refusal that proves nothing was published (same rule as certainlyNotSent in device-config-check.js).
// Any other answer, including Failed/MQTT_PUBLISH_UNCONFIRMED, may have reached the device: follow the row.
function refusedResult(http, dispatch) {
  if (http >= 400 && http < 500) return true;
  if (!dispatch || typeof dispatch !== 'object') return false;
  return dispatch.status === 'ContractUnavailable' || dispatch.status === 'TransportNotConfigured' ||
    NEVER_SENT.has(dispatch.code) || (dispatch.status === 'Failed' && REFUSED_BEFORE_SIGNING.has(dispatch.code));
}
const refused = sent => refusedResult(sent.http, sent.dispatch);

// The device's own refusal as the server reports it: "error[: data.error]", e.g. "ESP_ERR_INVALID_STATE: busy".
const BUSY_DETAIL = /(^|:\s*)(LANE_FULL|busy|UNAUTHORIZED)\b/i;

const getCommand = async (deviceId, id) => call('GET', `/api/v1/devices/${deviceId}/commands/${id}`);
const getLink = async deviceId => (await call('GET', `/api/v1/devices/${deviceId}/link`)).json;

/** One command and its recorded outcome, polled for at most 45 s. Not for `system.reboot` itself. */
async function execute(deviceId, pair, options = {}) {
  const sent = await send(deviceId, pair, options);
  const dispatch = sent.dispatch;
  if (refused(sent)) return { kind: 'refused', code: dispatch?.code ?? `HTTP_${sent.http}`, detail: dispatch?.detail ?? null };
  const waitable = dispatch?.status === 'Published' || dispatch?.code === 'MQTT_PUBLISH_UNCONFIRMED';
  if (!waitable) return { kind: 'unknown', code: dispatch?.code ?? `HTTP_${sent.http}`, detail: dispatch?.detail ?? null };
  let polled = null;
  for (let i = 0; i < 45; i++) {
    await sleep(1000);
    polled = (await getCommand(deviceId, dispatch.correlationId)).json;
    if (polled?.result && polled.result.status !== 'Published') break;
  }
  const result = polled?.result;
  const reply = polled?.reply ?? null;
  if (!result || result.status === 'Published') return { kind: 'unknown', code: 'CLIENT_TIMEOUT', reply };
  if (result.status === 'Executed') return { kind: 'executed', code: result.code, detail: result.detail ?? null, reply, receivedAt: reply?.receivedAt ?? null };
  if (result.code === 'DEVICE_REJECTED') return { kind: 'rejected', code: result.code, detail: result.detail ?? null, reply };
  return { kind: 'unknown', code: result.code, detail: result.detail ?? null, reply };
}

/** A read's reply data, retried while the server or the device turns it away as busy (30 s). */
async function read(deviceId, pair) {
  const until = Date.now() + 30_000;
  for (;;) {
    const outcome = await execute(deviceId, pair);
    if (outcome.kind === 'executed') return { ok: true, data: outcome.reply?.data ?? null, receivedAt: outcome.receivedAt };
    const busy = outcome.code === 'DEVICE_READ_BUSY' ||
      (outcome.code === 'DEVICE_REJECTED' && BUSY_DETAIL.test(outcome.detail ?? ''));
    if (!busy || Date.now() > until) return { ok: false, code: outcome.code, detail: outcome.detail };
    const wait = Number(fieldsOf(outcome.reply?.data)?.retry_after_ms);
    await sleep(Number.isFinite(wait) ? Math.min(5000, Math.max(500, wait)) : 2000);
  }
}

async function linkOf(deviceId) {
  const link = await getLink(deviceId);
  if (!link || typeof link !== 'object' || !link.binding) throw new Error('device has no MQTT binding');
  return link;
}

const phraseOf = link => `REBOOT-${link.binding.mqttDeviceId}`;

function relaysOf(data) {
  const node = fieldsOf(data);
  if (!node || !Array.isArray(node.channels)) return null;
  return node.channels.map(c => ({
    channel: c.channel, state: c.state === true, forced: c.forced === true, pulseActive: c.pulseActive === true,
    owner: typeof c.owner === 'string' ? c.owner : null,
    ownerId: c.owner_id == null ? null : String(c.owner_id),
  }));
}

// Who holds a channel at read time (spec c80797d L797: none | force | level_rule | event_rule | trigger | unknown).
// null when the device sends no owner at all, so nothing can be checked.
// Whitelist, like the server: only none and force are free, only the three rule classes are held, and everything
// else (another string, a case variant, a non-string, a channel without owner next to channels that report one)
// is unknown, because it does not prove that no rule holds the channel.
const RULE_OWNERS = new Set(['level_rule', 'event_rule', 'trigger']);
const FREE_OWNERS = new Set(['none', 'force']);
function ruleHolders(relays) {
  if (!Array.isArray(relays) || !relays.some(r => typeof r.owner === 'string')) return null;
  return {
    held: relays.filter(r => RULE_OWNERS.has(r.owner)),
    unknown: relays.filter(r => !RULE_OWNERS.has(r.owner) && !FREE_OWNERS.has(r.owner)),
  };
}
const OWNER_CHECK = 'no DO channel held by a rule or trigger (control.get_status owner)';

/** True when the real reboot must not be sent: a failed preflight always blocks; an ASK blocks unless the operator passed --rules-confirmed. Pure. */
const rebootBlockedByAsk = ({ pass, ask, rulesConfirmed }) => !pass || (Boolean(ask) && !rulesConfirmed);

/** The manual-confirmation line the preflight prints for these relays, or null when none is needed. Pure. */
function ownerNotes(relays) {
  if (!Array.isArray(relays)) return { holders: null, ask: null };
  const holders = ruleHolders(relays);
  if (!holders) return { holders, ask: 'ASK   firmware does not report owner on control.get_status: confirm by hand that no DO rule (SET_HIGH/SET_LOW) or trigger drives a relay on this device; this does not fail the preflight' };
  if (holders.unknown.length) return { holders, ask: `ASK   owner unknown or unrecognized on ${holders.unknown.map(r => `DO${r.channel}`).join(', ')}: the device cannot tell who holds it; confirm by hand that no DO rule or trigger drives it` };
  return { holders, ask: null };
}
const describeHolder = r => `DO${r.channel} ${r.owner}${r.ownerId != null ? ` #${r.ownerId}` : ''}`;

// ---- preflight ---------------------------------------------------------------------------------

function otaPending(data) {
  // The reply shape is not in the spec: it is only trusted when it names an OTA state and none of
  // the flags that mean an image is staged or waiting for verification.
  const root = fieldsOf(data);
  const ota = root?.ota ?? fieldsOf(root?.data)?.ota;
  if (!ota || typeof ota.state !== 'string') return { known: false };
  const flagged = Object.entries(ota).filter(([k, v]) => /pending|staged|verify|rollback/i.test(k) && v === true).map(([k]) => k);
  return { known: true, state: ota.state, idle: ['idle', 'none'].includes(ota.state.toLowerCase()) && flagged.length === 0, flagged };
}

async function snapshot(deviceId) {
  const link = await linkOf(deviceId);
  const reads = {};
  for (const pair of ['system.get_info', 'control.get_status', 'system.get_time', 'mqtt.get_config', 'mqtt.get_status', 'ota.get_status'])
    reads[pair] = await read(deviceId, pair);
  const info = reads['system.get_info'].ok ? fieldsOf(reads['system.get_info'].data) : null;
  const time = reads['system.get_time'].ok ? fieldsOf(reads['system.get_time'].data) : null;
  const config = reads['mqtt.get_config'].ok ? fieldsOf(reads['mqtt.get_config'].data) : null;
  const status = reads['mqtt.get_status'].ok ? fieldsOf(reads['mqtt.get_status'].data) : null;
  const trial = config?.config_trial ?? status?.config_trial ?? null;
  return {
    takenAt: now(),
    link: { mqttDeviceId: link.binding.mqttDeviceId, online: link.online, connectivity: link.connectivity, brokerConnected: link.brokerConnected },
    info: info ? { uptimeSeconds: info.uptime_seconds ?? null, firmware: typeof info.firmware === 'string' ? info.firmware : info.firmware?.version ?? null, hostname: info.hostname ?? null } : null,
    relays: reads['control.get_status'].ok ? relaysOf(reads['control.get_status'].data) : null,
    // Skew against the instant the server received the reply, so the poll delay does not count.
    time: time ? { epoch: time.epoch ?? null, ntpSynced: time.ntp_synced ?? null, skewSeconds: typeof time.epoch === 'number' && reads['system.get_time'].receivedAt ? time.epoch - Date.parse(reads['system.get_time'].receivedAt) / 1000 : null } : null,
    cleanSession: config?.clean_session ?? null,
    configTrialState: trial?.state ?? null,
    ota: otaPending(reads['ota.get_status'].ok ? reads['ota.get_status'].data : null),
    readFailures: Object.entries(reads).filter(([, r]) => !r.ok).map(([k, r]) => `${k}:${r.code}`),
  };
}

function preflightVerdict(s) {
  const checks = [
    ['link online', s.link.online === true],
    ['signed system.get_info readable (uptime_seconds)', typeof s.info?.uptimeSeconds === 'number'],
    ['control.get_status readable', Array.isArray(s.relays)],
    ['clean_session === true', s.cleanSession === true],
    [`device clock within ${MAX_CLOCK_SKEW_S} s of server UTC`, typeof s.time?.skewSeconds === 'number' && Math.abs(s.time.skewSeconds) <= MAX_CLOCK_SKEW_S],
    ['config_trial.state === "none"', s.configTrialState === 'none'],
    ['no staged or unverified OTA image (ota.get_status)', s.ota.known === true && s.ota.idle === true],
  ];
  const verdict = checks.map(([name, pass]) => ({ name, pass }));
  const holders = ruleHolders(s.relays);
  if (holders) {
    const held = holders.held.map(describeHolder);
    verdict.push({
      name: OWNER_CHECK,
      pass: held.length === 0,
      detail: held.length ? `held: ${held.join(', ')}; remove the rule on the device (HTTP /api/do-rule/remove) before a reboot test` : undefined,
    });
  }
  return verdict;
}

// ---- modes -------------------------------------------------------------------------------------

function save(name, value) {
  fs.mkdirSync(outDir, { recursive: true });
  const file = path.join(outDir, name);
  fs.writeFileSync(file, JSON.stringify(value, null, 2));
  return file;
}

const stamp = () => new Date().toISOString().replace(/[:.]/g, '-');
const report = (label, pass, extra = '') => console.log(`${pass ? 'PASS' : 'FAIL'}  ${label}${extra ? `  ${extra}` : ''}`);

async function runPreflight(deviceId) {
  const snap = await snapshot(deviceId);
  const verdict = preflightVerdict(snap);
  verdict.forEach(v => report(v.name, v.pass, v.detail));
  const { ask } = ownerNotes(snap.relays);
  if (ask) console.log(ask);
  console.log('WARN  owner names who holds a channel at read time; a DO rule that holds none can still fire SET_HIGH/SET_LOW at the first evaluation after boot (spec L883, HEAD c80797d)');
  console.log(`WARN  firmware ${snap.info?.firmware ?? 'unknown'}: "force OFF stays OFF" holds only for firmware with the 2026-11-08 change (spec L851); before it a force-OFF relay could switch ON for about 0.14 s at each restart. Check the firmware version with the team before a real device run.`);
  if (snap.readFailures.length) console.log(`      reads that failed: ${snap.readFailures.join(', ')}`);
  const file = save(`${stamp()}-reboot-before.json`, { snapshot: snap, verdict });
  console.log(`Before-state saved: ${file}`);
  return { snap, pass: verdict.every(v => v.pass), ask, file };
}

async function runChecks(deviceId, link, sim) {
  let pass = true;
  for (const p of PROBES.filter(x => sim || !x.simOnly)) {
    const confirm = p.confirmFor ? phraseOf(link) : p.confirm;
    const sent = await send(deviceId, p.pair, { payload: p.payload, confirm, dryRun: p.dryRun });
    const code = sent.dispatch?.code ?? `HTTP_${sent.http}`;
    const ok = refused(sent) && code === p.expect;
    pass = pass && ok;
    report(`probe: ${p.name}`, ok, `expected ${p.expect}, got ${sent.dispatch?.status ?? ''} ${code}`);
  }
  return pass;
}

async function runDryRun(deviceId, link) {
  const before = (await read(deviceId, 'system.get_info'));
  const uptime0 = fieldsOf(before.data)?.uptime_seconds;
  const dry = await execute(deviceId, 'system.reboot', { payload: {}, confirm: phraseOf(link), dryRun: true });
  const dryOk = dry.kind === 'executed' && fieldsOf(dry.reply?.data)?.would_apply === true;
  report('dry run executed (would_apply:true)', dryOk, `${dry.kind} ${dry.code}${dry.detail ? ` (${dry.detail})` : ''}`);
  // 20 s of signed uptime reads and link checks: a dry run must change nothing.
  const uptimes = [uptime0];
  let offline = false;
  for (let i = 0; i < 4; i++) {
    await sleep(5000);
    const l = await getLink(deviceId);
    if (l.online === false || l.connectivity === 'offline') offline = true;
    const info = await read(deviceId, 'system.get_info');
    uptimes.push(fieldsOf(info.data)?.uptime_seconds);
  }
  const rising = uptimes.every((u, i) => typeof u === 'number' && (i === 0 || u >= uptimes[i - 1]));
  report('uptime never went down in 20 s', rising, JSON.stringify(uptimes));
  report('link never offline in 20 s', !offline);
  return dryOk && rising && !offline;
}

/** Follows the audit row of one real reboot; never sends anything but reads of the row and the link. */
async function follow(deviceId, correlationId, windowS, tailS) {
  const t0 = Date.now();
  const milestones = {};
  const codes = [];
  let lastCode = null;
  let lastDetail = null;
  let reply = null;
  let maxBeatBefore = null;
  let offlineAt = null;
  let bootBeatAt = null;
  let finalAt = null;
  let tick = 0;
  for (;;) {
    const elapsed = (Date.now() - t0) / 1000;
    let link;
    try { link = await getLink(deviceId); } catch { link = null; }
    const down = link?.online === false || link?.connectivity === 'offline';
    if (down && !offlineAt) { offlineAt = now(); milestones.offlineSeenAt = offlineAt; }
    const beat = link?.lastHeartbeatAt ?? null;
    if (!offlineAt && beat) maxBeatBefore = beat;
    if (offlineAt && !bootBeatAt && beat && beat > offlineAt) { bootBeatAt = beat; milestones.firstHeartbeatAfterBootAt = beat; }

    if (tick % 2 === 0) {
      let polled;
      try { polled = await getCommand(deviceId, correlationId); } catch { polled = { status: 0, json: null }; }
      if (polled.status === 401) {
        // A restarted dev server forgets its sessions (ephemeral data protection keys): sign in again.
        try { await login(); } catch { /* the server may still be starting */ }
        polled = { status: 0, json: null };
      }
      if (polled.status === 404) { codes.push({ at: now(), t: elapsed, code: 'COMMAND_NOT_FOUND' }); lastCode = 'COMMAND_NOT_FOUND'; break; }
      const result = polled.json?.result;
      reply = polled.json?.reply ?? reply;
      if (result && result.code !== lastCode) {
        codes.push({ at: now(), t: Number(elapsed.toFixed(1)), code: result.code, status: result.status, detail: result.detail ?? null });
        lastCode = result.code;
        console.log(`  +${elapsed.toFixed(0).padStart(3)} s  ${result.code}${result.detail ? `  (${result.detail})` : ''}`);
      }
      lastDetail = result?.detail ?? lastDetail;
      if (FINAL_CODES.has(lastCode) && lastCode !== 'DEVICE_REBOOT_NOT_OBSERVED') { finalAt = now(); break; }
      if (lastCode === 'DEVICE_REBOOT_NOT_OBSERVED' && !(tailS > 0 && elapsed < tailS)) { finalAt = now(); break; }
    }
    if (elapsed > Math.max(windowS, lastCode === 'DEVICE_REBOOT_NOT_OBSERVED' ? tailS : 0)) break;
    tick++;
    await sleep(1000);
  }
  milestones.replyAt = reply?.receivedAt ?? null;
  milestones.maxHeartbeatBeforeGap = maxBeatBefore;
  if (milestones.replyAt && bootBeatAt)
    milestones.replyToFirstHeartbeatSeconds = Number(((Date.parse(bootBeatAt) - Date.parse(milestones.replyAt)) / 1000).toFixed(1));
  return { codes, lastCode, lastDetail, milestones, finalAt, seconds: Number(((Date.now() - t0) / 1000).toFixed(1)) };
}

function compareAfter(before, after) {
  const results = [];
  const up0 = before.info?.uptimeSeconds;
  const up1 = after.info?.uptimeSeconds;
  // The UI says "verified" only when the signed uptime is smaller than before. A reboot seen late
  // (reply lost, hold expiry after ~2 min) can leave a larger number when the before-read came soon
  // after the previous boot; the clock-adjusted check then still proves a reset.
  const elapsed = (Date.parse(after.takenAt) - Date.parse(before.takenAt)) / 1000;
  const numeric = typeof up0 === 'number' && typeof up1 === 'number';
  const strict = numeric && up1 < up0;
  const reset = numeric && up1 < up0 + elapsed - 5;
  results.push({ name: 'signed uptime shows a reset', pass: reset, detail: `${up0} -> ${up1} after ${Math.round(elapsed)} s; ${strict ? 'strictly smaller (the UI says verified)' : 'not strictly smaller (the UI says not verified)'}` });
  results.push({ name: 'firmware unchanged', pass: before.info?.firmware === after.info?.firmware, detail: `${before.info?.firmware} -> ${after.info?.firmware}` });
  const notes = [];
  if (Array.isArray(before.relays) && Array.isArray(after.relays)) {
    for (const b of before.relays) {
      const a = after.relays.find(x => x.channel === b.channel);
      if (!a) { results.push({ name: `DO${b.channel} present after reboot`, pass: false }); continue; }
      if (b.forced) results.push({ name: `DO${b.channel} forced ${b.state ? 'ON' : 'OFF'} is ${b.state ? 'ON' : 'OFF'} after`, pass: a.state === b.state, detail: `after state=${a.state}` });
      else if (a.state) notes.push(`DO${b.channel} is ON after reboot, may be a DO rule (not counted as a failure)`);
      else results.push({ name: `DO${b.channel} (not forced) is OFF after`, pass: true });
    }
  } else results.push({ name: 'relay table readable before and after', pass: false });
  const skew = typeof after.time?.skewSeconds === 'number' ? Math.round(after.time.skewSeconds) : null;
  notes.push(`ntp_synced=${after.time?.ntpSynced}, clock skew ${skew} s`);
  return { results, notes };
}

async function runAfter(deviceId, beforeFile) {
  const saved = JSON.parse(fs.readFileSync(beforeFile, 'utf8'));
  const before = saved.snapshot ?? saved;
  const after = await snapshot(deviceId);
  const { results, notes } = compareAfter(before, after);
  results.forEach(r => report(r.name, r.pass, r.detail ?? ''));
  notes.forEach(n => console.log(`      note: ${n}`));
  return { pass: results.every(r => r.pass), after, results, notes };
}

async function runReboot(deviceId, args, arg, sim) {
  const expect = arg('--expect') ?? 'observed';
  const windowS = Number(arg('--window-s') ?? 180);
  const tailS = Number(arg('--tail-s') ?? 0);
  const correlationId = arg('--correlation-id') ?? crypto.randomBytes(16).toString('hex');
  const link = await linkOf(deviceId);

  const pre = await runPreflight(deviceId);
  if (!pre.pass) { console.log('Preflight failed: nothing was sent to the device.'); return false; }
  if (rebootBlockedByAsk({ pass: pre.pass, ask: pre.ask, rulesConfirmed: args.includes('--rules-confirmed') })) {
    console.log('Preflight printed an ASK line: nothing was sent to the device. Confirm by hand that no DO rule (SET_HIGH/SET_LOW, level or event) or trigger is on the device, then run again with --rules-confirmed.');
    return false;
  }
  if (!(await runChecks(deviceId, link, sim))) { console.log('Probe checks failed: the reboot was not sent.'); return false; }
  if (!(await runDryRunQuick(deviceId, link))) { console.log('Dry run failed: the reboot was not sent.'); return false; }

  console.log(`Sending the real reboot once. correlationId=${correlationId}`);
  const sentAt = now();
  const sent = await send(deviceId, 'system.reboot', { payload: {}, confirm: phraseOf(link), dryRun: false, correlationId });
  console.log(`  dispatch: HTTP ${sent.http} ${sent.dispatch?.status ?? ''} ${sent.dispatch?.code ?? ''}`);
  if (refused(sent)) { console.log('The server refused the reboot before sending it.'); report('reboot sent', false); return false; }
  if (sent.dispatch?.code === 'MQTT_PUBLISH_UNCONFIRMED') console.log('  publish not confirmed: the device may still run it; following the row, never resending');

  const followed = await follow(deviceId, correlationId, windowS, tailS);
  const rec = { correlationId, sentAt, dispatch: { status: sent.dispatch?.status, code: sent.dispatch?.code }, ...followed };

  let pass;
  if (expect === 'observed') pass = followed.lastCode === 'DEVICE_REBOOT_OBSERVED';
  else if (expect === 'observed_reply_none') pass = followed.lastCode === 'DEVICE_REBOOT_OBSERVED' && /reply=none/.test(followed.lastDetail ?? '');
  else if (expect === 'not_observed') pass = followed.lastCode === 'DEVICE_REBOOT_NOT_OBSERVED';
  else if (expect === 'open') pass = OPEN_CODES.has(followed.lastCode);
  else if (expect === 'rejected') pass = followed.lastCode === 'DEVICE_REJECTED';
  else if (expect === 'server_restarted') pass = followed.lastCode === 'COMMAND_NOT_FOUND';
  else throw new Error(`unknown --expect ${expect}`);
  report(`final code matches --expect ${expect}`, pass, `last=${followed.lastCode} after ${followed.seconds} s`);
  console.log(`  milestones: ${JSON.stringify(followed.milestones)}`);

  if (followed.lastCode === 'DEVICE_REBOOT_OBSERVED') {
    const after = await runAfter(deviceId, pre.file);
    rec.after = after;
    pass = pass && after.pass;
  } else {
    // Unknown outcome: one read of the signed uptime, then instructions. The reboot is never resent.
    const info = await read(deviceId, 'system.get_info');
    rec.uptimeNow = fieldsOf(info.data)?.uptime_seconds ?? null;
    console.log(`  signed uptime now: ${rec.uptimeNow} (before: ${pre.snap.info?.uptimeSeconds}). Do not resend; check the device by hand if it stays unreachable.`);
  }
  const file = save(`${stamp()}-reboot-report-${deviceId}.json`, { expect, pass, before: pre.snap, ...rec });
  console.log(`Report: ${file}`);
  return pass;
}

// The dry run inside --reboot is the same as --dry-run without the 20 s watch.
async function runDryRunQuick(deviceId, link) {
  const dry = await execute(deviceId, 'system.reboot', { payload: {}, confirm: phraseOf(link), dryRun: true });
  const ok = dry.kind === 'executed' && fieldsOf(dry.reply?.data)?.would_apply === true;
  report('dry run executed (would_apply:true)', ok, `${dry.kind} ${dry.code}${dry.detail ? ` (${dry.detail})` : ''}`);
  return ok;
}

async function runProbeInProgress(deviceId, watchId, afterS) {
  const link = await linkOf(deviceId);
  // The row appears once the browser (or --reboot) has sent the real command.
  const until = Date.now() + 120_000;
  let polled;
  for (;;) {
    polled = await getCommand(deviceId, watchId);
    if (polled.status === 200 && polled.json?.result) break;
    if (Date.now() > until) { console.log('SKIPPED  the watched command never appeared'); return true; }
    await sleep(500);
  }
  await sleep(afterS * 1000);
  polled = await getCommand(deviceId, watchId);
  const code = polled.json?.result?.code ?? 'COMMAND_NOT_FOUND';
  const readAt = now();
  if (!OPEN_CODES.has(code)) {
    console.log(`SKIPPED  the watched command is already at ${code}; not a failure`);
    save(`${stamp()}-reboot-probe-in-progress.json`, { watchId, readAt, codeBefore: code, skipped: true });
    return true;
  }
  const sentAt = now();
  const sent = await send(deviceId, 'system.reboot', { payload: {}, confirm: phraseOf(link), dryRun: true });
  const got = sent.dispatch?.code ?? `HTTP_${sent.http}`;
  const pass = got === 'DEVICE_REBOOT_IN_PROGRESS' && refused(sent);
  report('probe during the hold refused with DEVICE_REBOOT_IN_PROGRESS', pass, `watched=${code}, got ${sent.dispatch?.status ?? ''} ${got}`);
  save(`${stamp()}-reboot-probe-in-progress.json`, { watchId, readAt, codeBefore: code, sentAt, got, status: sent.dispatch?.status });
  return pass;
}

async function runRestoreDo(deviceId, beforeFile) {
  const saved = JSON.parse(fs.readFileSync(beforeFile, 'utf8'));
  const before = (saved.snapshot ?? saved).relays?.find(r => r.channel === 1);
  if (!before) { console.log('The before-state has no DO1: nothing to restore'); return true; }
  const status = await read(deviceId, 'control.get_status');
  const now1 = relaysOf(status.data)?.find(r => r.channel === 1);
  if (!now1) { report('DO1 readable', false); return false; }
  if (now1.state === before.state) { report('DO1 already matches the before-state', true, `state=${now1.state}`); return true; }
  const out = await execute(deviceId, 'control.set_output', { payload: { channel: 1, value: before.state } });
  report('control.set_output DO1', out.kind === 'executed', `${out.kind} ${out.code}`);
  await sleep(1500);
  const again = relaysOf((await read(deviceId, 'control.get_status')).data)?.find(r => r.channel === 1);
  report('DO1 matches the before-state', again?.state === before.state, `state=${again?.state}`);
  return again?.state === before.state;
}

function printPlan() {
  console.log('Plan (nothing is sent):');
  console.log('  --preflight         reads: link, system.get_info, control.get_status, system.get_time, mqtt.get_config, mqtt.get_status, ota.get_status');
  console.log('                      PASS only when clean_session=true, clock within 5 s, config_trial.state=none, no staged OTA image');
  console.log('                      PASS also needs no DO channel held by a rule or trigger when the device reports owner; ASK when it does not');
  console.log('  --checks            unsigned probes, all dryRun:true: reboot without confirm, factory_reset, ota.apply, ota.remote_start');
  console.log('                      (--sim adds: wrong confirm, delay_ms payload); nothing leaves the server');
  console.log('  --dry-run           one dry run with the right confirm, then 20 s of signed uptime reads and link checks');
  console.log('  --reboot            preflight + checks + dry run + ONE real reboot; polls the audit row every 2 s up to --window-s;');
  console.log('                      records reply, offline, first heartbeat after boot; then --after; never resends');
  console.log('                      --reboot refuses when the preflight printed an ASK line, unless --rules-confirmed says a person checked that no DO rule or trigger is on the device');
  console.log('  --probe-in-progress --watch <id>   a dry-run reboot during the hold; expects DEVICE_REBOOT_IN_PROGRESS, or skipped when late');
  console.log('  --after <before.json>   uptime smaller, firmware same, forced channels at their level, unforced OFF (ON noted)');
  console.log('  --restore-do <before.json>   one control.set_output DO1 back to the before-state if it differs');
  console.log('  Every mode that talks to a device needs --device <guid> --confirm-live and a master-admin account.');
}

(async () => {
  const args = process.argv.slice(2);
  const arg = name => { const i = args.indexOf(name); return i >= 0 ? args[i + 1] : null; };
  if (args.includes('--plan')) return printPlan();
  if (args.includes('--self-check')) {
    const problems = selfCheck();
    console.log(`probes: ${PROBES.length} (${PROBES.filter(p => p.simOnly).length} simulator-only)`);
    problems.forEach(p => console.log(`FAIL  ${p}`));
    if (problems.length) process.exitCode = 1; else console.log('PASS  every probe is a dry run; live probes carry no confirm and an empty payload');
    return;
  }

  const deviceId = arg('--device');
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(deviceId ?? ''))
    throw new Error('usage: --device <guid> --confirm-live (--preflight | --checks | --dry-run | --reboot | --probe-in-progress --watch <id> | --after <file> | --restore-do <file>); --plan and --self-check send nothing');
  if (!args.includes('--confirm-live')) throw new Error('refusing to talk to a device without --confirm-live (try --plan)');
  const sim = args.includes('--sim');

  await login();
  assertClosedRows();
  console.log('Catalog check: factory_reset, ota.apply, ota.remote_start unavailable; system.reboot available.');

  let pass = true;
  if (args.includes('--preflight')) pass = (await runPreflight(deviceId)).pass;
  else if (args.includes('--checks')) pass = await runChecks(deviceId, await linkOf(deviceId), sim);
  else if (args.includes('--dry-run')) pass = await runDryRun(deviceId, await linkOf(deviceId));
  else if (args.includes('--reboot')) pass = await runReboot(deviceId, args, arg, sim);
  else if (args.includes('--probe-in-progress')) {
    if (!arg('--watch')) throw new Error('--probe-in-progress needs --watch <correlationId>');
    pass = await runProbeInProgress(deviceId, arg('--watch'), Number(arg('--after-s') ?? 3));
  } else if (arg('--after')) pass = (await runAfter(deviceId, arg('--after'))).pass;
  else if (arg('--restore-do')) pass = await runRestoreDo(deviceId, arg('--restore-do'));
  else throw new Error('no mode given (try --plan)');
  if (!pass) process.exitCode = 1;
})().catch(e => { console.error(e.message); process.exitCode = 2; });
