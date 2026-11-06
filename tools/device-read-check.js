// tools/device-read-check.js
// Sends read commands to one device through the HMS-SERVER API (the server signs them; this tool
// never needs the device command key) and saves every full reply for comparison with the spec.
//
//   node tools/device-read-check.js <deviceId> [type.action ...]       read one at a time
//   node tools/device-read-check.js <deviceId> --burst <n> <type.action>  n reads at once (cap check)
//
// Env: HMS_ADMIN_EMAIL / HMS_ADMIN_PASSWORD (see tool-env.js), HMS_API_BASE (default
// http://127.0.0.1:5099). Output: tools/device-reads/<time>-<deviceId>.json (gitignored).
// Nothing secret is printed or written.
const fs = require('fs');
const path = require('path');
const { hmsAdminCredentials } = require('./tool-env');

const MONITORING_READS = [
  'system.get_time', 'system.get_hostname', 'system.get_device_id', 'ota.get_status',
  'network.get_priority', 'network.get_ethernet', 'network.get_modem', 'network.wifi_sta_status',
  'network.wifi_ap_status', 'battery.get_status', 'battery.get_alerts', 'adc.get_config',
  'adc.get_channels', 'adc.get_status', 'adc.get_values',
];

const base = process.env.HMS_API_BASE || 'http://127.0.0.1:5099';
const jar = new Map();
const cookie = () => [...jar].map(([k, v]) => `${k}=${v}`).join('; ');

async function call(method, route, body, csrf) {
  const res = await fetch(base + route, {
    method,
    headers: { 'Content-Type': 'application/json', Cookie: cookie(), ...(csrf ? { 'X-CSRF-TOKEN': csrf } : {}) },
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
  const first = await call('GET', '/api/v1/auth/csrf');
  const res = await call('POST', '/api/v1/auth/login', { email, password, rememberMe: false }, first.json.requestToken);
  if (res.status >= 300) throw new Error(`login failed (${res.status})`);
  return (await call('GET', '/api/v1/auth/csrf')).json.requestToken;
}

async function send(deviceId, pair, csrf, catalog) {
  const [type, action] = pair.split('.');
  const started = Date.now();
  const sent = await call('POST', `/api/v1/devices/${deviceId}/commands`,
    { type, action, payload: {}, catalogVersion: catalog.catalogVersion, catalogHash: catalog.catalogHash }, csrf);
  const dispatch = sent.json;
  if (dispatch.status !== 'Published') return { pair, dispatch, result: null, reply: null, ms: Date.now() - started };
  let res;
  for (let i = 0; i < 45; i++) {
    await new Promise(r => setTimeout(r, 1000));
    res = (await call('GET', `/api/v1/devices/${deviceId}/commands/${dispatch.correlationId}`)).json;
    if (res.result && res.result.status !== 'Published') break;
  }
  return { pair, dispatch, result: res?.result ?? null, reply: res?.reply ?? null, ms: Date.now() - started };
}

function summary(r) {
  const data = r.reply?.data;
  const keys = data && typeof data === 'object' ? (Array.isArray(data) ? `[${data.length}] ${Object.keys(data[0] ?? {}).join(',')}` : Object.keys(data).join(',')) : '';
  const status = r.result ? `${r.result.status} ${r.result.code}` : `${r.dispatch.status} ${r.dispatch.code}`;
  const size = data === undefined || data === null ? 0 : JSON.stringify(data).length;
  return `${r.pair.padEnd(26)} ${status.padEnd(36)} ${String(size).padStart(5)} B  ${r.ms} ms  ${keys}`;
}

(async () => {
  const [deviceId, ...rest] = process.argv.slice(2);
  if (!deviceId) throw new Error('usage: node tools/device-read-check.js <deviceId> [type.action ...] | --burst <n> <type.action>');
  const csrf = await login();
  const catalog = (await call('GET', '/api/v1/me/command-catalog')).json;
  let results;
  if (rest[0] === '--burst') {
    const n = Number(rest[1]);
    const pair = rest[2] || 'system.get_info';
    results = await Promise.all(Array.from({ length: n }, () => send(deviceId, pair, csrf, catalog)));
  } else {
    results = [];
    for (const pair of rest.length ? rest : MONITORING_READS) {
      const r = await send(deviceId, pair, csrf, catalog);
      results.push(r);
      console.log(summary(r));
    }
  }
  if (rest[0] === '--burst') results.forEach(r => console.log(summary(r)));
  const dir = path.join(__dirname, 'device-reads');
  fs.mkdirSync(dir, { recursive: true });
  const file = path.join(dir, `${new Date().toISOString().replace(/[:.]/g, '-')}-${deviceId}.json`);
  fs.writeFileSync(file, JSON.stringify(results, null, 2));
  console.log(`saved ${results.length} result(s) to ${path.relative(process.cwd(), file)}`);
})().catch(e => { console.error(e.message); process.exit(1); });
