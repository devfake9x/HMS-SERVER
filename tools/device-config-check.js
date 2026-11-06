// tools/device-config-check.js
// Live check of the five opened config writes on one device, through the HMS-SERVER API (the
// server signs; this tool never needs the device command key). For each field it records the
// original value, writes a test value, reads it back, writes the original back and reads again.
// Whatever happens, it reads the state before deciding anything (spec R4: no reply does not mean
// a write did not run) and never resends a write blindly.
//
//   node tools/device-config-check.js --device <guid> --plan
//   node tools/device-config-check.js --device <guid> --confirm-live
//   node tools/device-config-check.js --device <guid> --confirm-live --restore <original.json>
//
// Env: HMS_ADMIN_EMAIL / HMS_ADMIN_PASSWORD (see tool-env.js), HMS_API_BASE (default
// http://127.0.0.1:5099). Output: tools/device-reads/ (gitignored): the original values before
// any write, and a report of every step. Nothing secret is printed or written.
const fs = require('fs');
const path = require('path');
const { hmsAdminCredentials } = require('./tool-env');

const base = process.env.HMS_API_BASE || 'http://127.0.0.1:5099';
const outDir = path.join(__dirname, 'device-reads');
// Server: ConfigCommandPolicy.SupportedTimezones.
const SUPPORTED_TIMEZONES = ['ICT-7', 'UTC0'];
// Server: MqttDeviceCommandTransport.NeverSentCodes.
const NEVER_SENT = new Set(['MQTT_NOT_CONNECTED', 'MQTT_RESPONSE_SUBSCRIPTION_MISSING', 'MQTT_COMMAND_TOPIC_INVALID']);
// The server takes 10 writes per device per minute; keep well under it.
const WRITE_SPACING_MS = 7000;
const JOB_LIMIT_MS = 60_000;

const jar = new Map();
const cookie = () => [...jar].map(([k, v]) => `${k}=${v}`).join('; ');
const sleep = ms => new Promise(r => setTimeout(r, ms));
let csrf = null;
let catalog = null;
let lastWriteAt = 0;
const steps = [];

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
  const { email, password } = hmsAdminCredentials();
  csrf = (await call('GET', '/api/v1/auth/csrf')).json.requestToken;
  const res = await call('POST', '/api/v1/auth/login', { email, password, rememberMe: false });
  if (res.status >= 300) throw new Error(`login failed (${res.status})`);
  csrf = (await call('GET', '/api/v1/auth/csrf')).json.requestToken;
  catalog = (await call('GET', '/api/v1/me/command-catalog')).json;
}

// Same rule as the UI (frontend mayHaveRun): only a refusal that proves nothing was published
// counts as "not sent". Everything else may have reached the device and is read back.
function certainlyNotSent(httpStatus, dispatch) {
  if (httpStatus >= 400 && httpStatus < 500) return true;
  if (!dispatch || typeof dispatch !== 'object') return false;
  return dispatch.status === 'ContractUnavailable' || dispatch.status === 'TransportNotConfigured' || NEVER_SENT.has(dispatch.code);
}

/** Sends one command and waits for the server's recorded outcome. */
async function command(deviceId, pair, payload) {
  const [type, action] = pair.split('.');
  const sent = await call('POST', `/api/v1/devices/${deviceId}/commands`,
    { type, action, payload, catalogVersion: catalog.catalogVersion, catalogHash: catalog.catalogHash });
  const dispatch = sent.json;
  if (certainlyNotSent(sent.status, dispatch))
    return { kind: 'not_sent', code: dispatch?.code ?? `HTTP_${sent.status}`, dispatch };
  const waitable = dispatch?.status === 'Published' || dispatch?.code === 'MQTT_PUBLISH_UNCONFIRMED';
  if (!waitable) return { kind: 'unknown', code: dispatch?.code ?? `HTTP_${sent.status}`, dispatch };

  let polled = null;
  for (let i = 0; i < 45; i++) {
    await sleep(1000);
    polled = (await call('GET', `/api/v1/devices/${deviceId}/commands/${dispatch.correlationId}`)).json;
    if (polled?.result && polled.result.status !== 'Published') break;
  }
  const result = polled?.result;
  const reply = polled?.reply ?? null;
  if (!result || result.status === 'Published') return { kind: 'unknown', code: 'CLIENT_TIMEOUT', dispatch, reply };
  if (result.status === 'Executed') return { kind: 'executed', code: result.code, reply };
  if (result.status === 'Acknowledged') {
    const operationId = /operation_id=([0-9]+)/.exec(result.detail ?? '')?.[1];
    return operationId ? await followJob(deviceId, operationId, reply) : { kind: 'unknown', code: result.code, reply };
  }
  if (result.code === 'DEVICE_REJECTED') return { kind: 'rejected', code: result.code, detail: result.detail, reply };
  return { kind: 'unknown', code: result.code, detail: result.detail, reply };
}

/** spec §5: poll the job until it is terminal; "unknown" (lost, rebooted) stays unknown. */
async function followJob(deviceId, operationId, reply) {
  const until = Date.now() + JOB_LIMIT_MS;
  while (Date.now() < until) {
    await sleep(1500);
    const reply = await read(deviceId, 'system.get_operations', { operation_id: operationId });
    // Read one level down too, as the UI does: the OTA reply nests its fields under data.
    const job = reply && typeof reply.state === 'string' ? reply : reply?.data;
    if (job?.state === 'terminal')
      return job.resultSuccess
        ? { kind: 'executed', code: 'JOB_DONE', operationId, reply }
        : { kind: 'job_failed', code: String(job.resultCode), detail: job.resultMessage, operationId, reply };
    if (job?.state === 'unknown') return { kind: 'unknown', code: 'JOB_UNKNOWN', operationId, reply };
  }
  return { kind: 'unknown', code: 'JOB_TIMEOUT', operationId, reply };
}

/** A read's reply data, retried while the device read lane is busy; null when unreadable. */
async function read(deviceId, pair, payload = {}) {
  for (let attempt = 0; attempt < 4; attempt++) {
    const outcome = await command(deviceId, pair, payload);
    if (outcome.kind === 'executed') return outcome.reply?.data ?? null;
    if (outcome.code !== 'DEVICE_READ_BUSY') return null;
    await sleep(2000);
  }
  return null;
}

// Refusals the server makes before publishing anything. Retrying them cannot double a write; the
// lane stays closed up to 60 s after a write the device ran as a job, and ~30 s after a timeout.
const LANE_REFUSALS = new Set(['DEVICE_WRITE_BUSY', 'DEVICE_WRITE_RATE_LIMITED']);
const LANE_WAIT_LIMIT_MS = 90_000;

async function write(deviceId, pair, payload) {
  const until = Date.now() + LANE_WAIT_LIMIT_MS;
  for (;;) {
    const wait = lastWriteAt + WRITE_SPACING_MS - Date.now();
    if (wait > 0) await sleep(wait);
    lastWriteAt = Date.now();
    const outcome = await command(deviceId, pair, payload);
    if (outcome.kind !== 'not_sent' || !LANE_REFUSALS.has(outcome.code) || Date.now() > until) return outcome;
    console.log(`  ${pair}: ${outcome.code}, nothing sent; waiting for the write lane`);
  }
}

// The server's rules (ConfigCommandPolicy): a value outside them cannot be written back.
const HOSTNAME_RULE = /^[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?$/;
const TYPE_RULE = /^[A-Za-z0-9_.-]+$/;
const writableHostname = h => typeof h === 'string' && Buffer.byteLength(h, 'utf8') <= 63 && HOSTNAME_RULE.test(h);
const writableChannel = c => typeof c.name === 'string' && c.name.trim() !== '' && Buffer.byteLength(c.name, 'utf8') <= 32 &&
  !/[\p{Cc}\p{Cf}\p{Zl}\p{Zp}\p{Co}\p{Cs}\p{Cn}͏ᅟᅠ឴឵⠀ㅤﾠ᠋-᠏︀-️]/u.test(c.name) &&
  typeof c.type === 'string' && Buffer.byteLength(c.type, 'utf8') <= 32 && TYPE_RULE.test(c.type) && typeof c.enabled === 'boolean';

async function snapshot(deviceId) {
  const time = await read(deviceId, 'system.get_time');
  const hostname = (await read(deviceId, 'system.get_hostname'))?.hostname ?? null;
  const adcConfig = await read(deviceId, 'adc.get_config');
  const channels = adcConfig?.available ? await read(deviceId, 'adc.get_channels') : null;
  const channel = Array.isArray(channels) ? channels.find(c => Number.isInteger(c.ai)) ?? null : null;
  const mqtt = await read(deviceId, 'mqtt.get_config');
  return {
    takenAt: new Date().toISOString(),
    hostname,
    timezone: time?.timezone ?? null,
    ntpMode: time?.ntp_mode ?? null,
    ntpServers: time?.ntp_servers ?? null,
    channel: channel ? { ai: channel.ai, name: channel.name, type: channel.type, enabled: channel.enabled } : null,
    // Only a client_id actually read and non-empty counts as set; unknown means do not touch the hostname.
    mqttClientIdSet: typeof mqtt?.client_id === 'string' && mqtt.client_id.length > 0,
  };
}

const same = (a, b) => JSON.stringify(a) === JSON.stringify(b);

function fields(deviceId, original) {
  const list = [];
  if (original.hostname) {
    const stem = original.hostname.slice(0, 61).replace(/-+$/, '');
    list.push({
      name: 'hostname',
      skip: original.mqttClientIdSet !== true
        ? 'mqtt client_id not read or empty: the device may use the hostname as its MQTT client id'
        : !writableHostname(original.hostname) ? 'the original hostname is outside the server rules and could not be written back' : null,
      original: original.hostname,
      test: `${stem}-t`,
      write: value => write(deviceId, 'system.set_hostname', { hostname: value }),
      read: async () => (await read(deviceId, 'system.get_hostname'))?.hostname ?? null,
      deferred: outcome => outcome.reply?.data?.applied_immediately === false,
      watchLink: true,
    });
  }
  if (SUPPORTED_TIMEZONES.includes(original.timezone)) {
    list.push({
      name: 'timezone',
      original: original.timezone,
      test: original.timezone === 'UTC0' ? 'ICT-7' : 'UTC0',
      write: value => write(deviceId, 'system.set_timezone', { timezone: value }),
      read: async () => (await read(deviceId, 'system.get_time'))?.timezone ?? null,
    });
  }
  if (original.channel) {
    const c = original.channel;
    const channelSkip = writableChannel(c) ? null : 'the original channel record is outside the server rules and could not be written back';
    const bytes = s => Buffer.byteLength(s, 'utf8');
    let stem = c.name;
    while (bytes(`${stem}-t`) > 32) stem = stem.slice(0, -1);
    const record = async () => {
      const list = await read(deviceId, 'adc.get_channels');
      const now = Array.isArray(list) ? list.find(x => x.ai === c.ai) : null;
      return now ? { name: now.name, type: now.type, enabled: now.enabled } : null;
    };
    list.push({
      name: `adc.ai${c.ai}.name`,
      skip: channelSkip,
      original: { name: c.name, type: c.type, enabled: c.enabled },
      test: { name: `${stem}-t`, type: c.type, enabled: c.enabled },
      // Type and enabled are sent back as read, so an omitted field cannot be reset.
      write: value => write(deviceId, 'adc.set_channel', { ai: c.ai, name: value.name, type: value.type, enabled: value.enabled }),
      read: record,
    });
    list.push({
      name: `adc.ai${c.ai}.enabled`,
      skip: channelSkip,
      original: { name: c.name, type: c.type, enabled: c.enabled },
      test: { name: c.name, type: c.type, enabled: !c.enabled },
      write: value => write(deviceId, 'adc.toggle_channel', { ai: c.ai, enabled: value.enabled }),
      read: record,
    });
  }
  return list;
}

async function watchLink(deviceId, seconds) {
  const drops = [];
  for (let i = 0; i < seconds; i += 3) {
    await sleep(3000);
    const link = (await call('GET', `/api/v1/devices/${deviceId}/link`)).json;
    if (link && (link.online === false || link.brokerConnected === false)) drops.push(new Date().toISOString());
  }
  return drops;
}

/** Test value, read back, original back, read back. Returns false when the run must stop. */
async function exercise(deviceId, field) {
  const step = { field: field.name, original: field.original, test: field.test };
  steps.push(step);
  if (field.skip) { step.skipped = field.skip; console.log(`SKIP ${field.name}: ${field.skip}`); return true; }

  const forward = await field.write(field.test);
  step.forward = { kind: forward.kind, code: forward.code, detail: forward.detail ?? null, reply: forward.reply?.data ?? null };
  if (field.watchLink && forward.kind !== 'not_sent') step.linkDrops = await watchLink(deviceId, 15);
  const seen = await field.read();
  step.afterForward = seen;
  const deferred = forward.kind === 'executed' && field.deferred?.(forward) === true && same(seen, field.original);
  if (deferred) step.deferred = true;
  const forwardOk = forward.kind === 'executed' && (same(seen, field.test) || deferred);
  console.log(`${field.name.padEnd(18)} → ${forward.kind.padEnd(10)} ${forward.code ?? ''} read=${JSON.stringify(seen)}${deferred ? ' (saved, not applied)' : ''}`);

  if (!forwardOk && same(seen, field.original)) {
    step.stop = 'write did not take effect; nothing to undo';
    return false;
  }
  if (!forwardOk && !same(seen, field.test)) {
    step.stop = 'read-back matches neither the original nor the test value';
    return false;
  }

  const back = await field.write(field.original);
  step.back = { kind: back.kind, code: back.code, detail: back.detail ?? null, reply: back.reply?.data ?? null };
  const restored = await field.read();
  step.afterBack = restored;
  console.log(`${field.name.padEnd(18)} ← ${back.kind.padEnd(10)} ${back.code ?? ''} read=${JSON.stringify(restored)}`);
  if (!same(restored, field.original) && !(field.deferred?.(back) && same(restored, field.original))) {
    step.stop = 'the original value did not come back';
    return false;
  }
  return forwardOk;
}

function compare(original, final) {
  const keys = ['hostname', 'timezone', 'ntpMode', 'ntpServers', 'channel'];
  return keys.filter(k => !same(original[k], final[k])).map(k => ({ field: k, original: original[k], now: final[k] }));
}

async function restore(deviceId, original) {
  for (const field of fields(deviceId, original)) {
    // Read each field fresh: an earlier restore step may already have fixed it.
    const current = await field.read();
    if (current === null || same(current, field.original)) continue;
    const outcome = await field.write(field.original);
    const seen = await field.read();
    console.log(`RESTORE ${field.name}: ${outcome.kind} ${outcome.code ?? ''} read=${JSON.stringify(seen)}`);
  }
}

function save(name, value) {
  fs.mkdirSync(outDir, { recursive: true });
  const file = path.join(outDir, name);
  fs.writeFileSync(file, JSON.stringify(value, null, 2));
  return file;
}

(async () => {
  const args = process.argv.slice(2);
  const arg = name => { const i = args.indexOf(name); return i >= 0 ? args[i + 1] : null; };
  const deviceId = arg('--device');
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(deviceId ?? ''))
    throw new Error('usage: --device <guid> (--plan | --confirm-live [--restore <original.json>])');
  const stamp = new Date().toISOString().replace(/[:.]/g, '-');

  if (args.includes('--plan')) {
    console.log('Plan (nothing is sent):');
    console.log('  1. read hostname, time, ADC channels, mqtt.get_config; save the originals');
    console.log('  2. hostname: <orig> -> <orig>-t -> <orig>   (skipped when mqtt client_id is empty; link watched 15 s)');
    console.log(`  3. timezone: <orig> -> UTC0|ICT-7 -> <orig> (only when the original is one of ${SUPPORTED_TIMEZONES.join(', ')})`);
    console.log('  4. sync_ntp, then expect ntp_synced and a device clock within 30 s');
    console.log('  5. first ADC channel: name -> <name>-t -> name (type/enabled sent back as read); enabled -> !enabled -> enabled');
    console.log(`  writes are spaced ${WRITE_SPACING_MS / 1000} s apart; any surprise reads the state, undoes what changed, and stops`);
    return;
  }
  if (!args.includes('--confirm-live')) throw new Error('refusing to write without --confirm-live (try --plan)');

  await login();
  const restoreFile = arg('--restore');
  if (restoreFile) {
    await restore(deviceId, JSON.parse(fs.readFileSync(restoreFile, 'utf8')));
    const final = await snapshot(deviceId);
    console.log('Differences after restore:', JSON.stringify(compare(JSON.parse(fs.readFileSync(restoreFile, 'utf8')), final)));
    return;
  }

  const original = await snapshot(deviceId);
  const originalFile = save(`${stamp}-config-original-${deviceId}.json`, original);
  console.log(`Originals saved: ${originalFile}`);

  let ok = true;
  for (const field of fields(deviceId, original)) {
    ok = await exercise(deviceId, field);
    if (!ok) break;
  }
  if (ok) {
    const sync = await write(deviceId, 'system.sync_ntp', {});
    const time = await read(deviceId, 'system.get_time');
    const skew = time?.epoch ? Math.abs(time.epoch - Math.floor(Date.now() / 1000)) : null;
    steps.push({ field: 'sync_ntp', forward: { kind: sync.kind, code: sync.code }, ntpSynced: time?.ntp_synced ?? null, clockSkewSeconds: skew });
    console.log(`sync_ntp           → ${sync.kind.padEnd(10)} ${sync.code ?? ''} ntp_synced=${time?.ntp_synced} skew=${skew}s`);
    if (sync.kind !== 'executed' || time?.ntp_synced !== true || skew === null || skew > 30) ok = false;
  }

  const final = await snapshot(deviceId);
  const differences = compare(original, final);
  const report = save(`${stamp}-config-check-${deviceId}.json`, { original, final, differences, steps, ok });
  console.log(`Report: ${report}`);
  if (differences.length) {
    console.log('NOT RESTORED:', JSON.stringify(differences));
    console.log(`Run: node tools/device-config-check.js --device ${deviceId} --confirm-live --restore ${originalFile}`);
    process.exitCode = 1;
  } else {
    console.log(ok ? 'All steps passed; configuration matches the originals.' : 'Stopped early; configuration matches the originals.');
    if (!ok) process.exitCode = 1;
  }
})().catch(e => { console.error(e.message); process.exitCode = 2; });
