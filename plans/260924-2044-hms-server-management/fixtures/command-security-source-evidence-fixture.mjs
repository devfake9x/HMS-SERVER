import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';

const root = process.argv[2];
const outputPath = process.argv[3] || path.join(
  'plans',
  '260924-2044-hms-server-management',
  'fixtures',
  'command-security-source-evidence.json',
);
assert(root, 'usage: node command-security-source-evidence-fixture.mjs <HMS-IDF-root> [output]');

const files = {
  dedup: path.join(root, 'components', 'command-dispatcher', 'cmd_dedup.c'),
  dedupHeader: path.join(root, 'components', 'command-dispatcher', 'include', 'cmd_dedup.h'),
  security: path.join(root, 'components', 'command-dispatcher', 'cmd_security.c'),
  dispatcher: path.join(root, 'components', 'command-dispatcher', 'command_dispatcher.c'),
  response: path.join(root, 'components', 'command-dispatcher', 'cmd_response.c'),
};
const source = Object.fromEntries(Object.entries(files).map(([key, file]) => {
  assert(fs.existsSync(file), `missing source: ${file}`);
  return [key, fs.readFileSync(file, 'utf8')];
}));

const lineOf = (text, needle) => {
  const index = text.indexOf(needle);
  assert(index >= 0, `missing source anchor: ${needle}`);
  return text.slice(0, index).split(/\r?\n/).length;
};
const digest = (text) => crypto.createHash('sha256').update(text).digest('hex');

assert(/CMD_DEDUP_MAX_ENTRIES\s+128/.test(source.dedupHeader));
assert(source.dedup.includes('strncmp(s_cache[i].cmd_id, cmd_id, CMD_DEDUP_ID_MAX_LEN) == 0'));
assert(source.dedup.includes('sntp_get_sync_status() == SNTP_SYNC_STATUS_COMPLETED'));
assert(source.security.includes('"type", "id", "ts", "data", "key_ver", "ttl_ms"'));
assert(source.security.includes('CMD_SIGNING_KEY_SIZE'));
assert(source.security.includes('#define CMD_SIGNABLE_MAX          1024'));
assert(source.dispatcher.includes('CMD_TTL_MAX_MS         (24u * 60u * 60u * 1000u)'));
assert(source.dispatcher.includes('CMD_CLOCK_SKEW_SEC     (60)'));
assert(source.dispatcher.includes('CMD_TTL_DEFAULT_MS     (120u * 1000u)'));
assert(source.dispatcher.indexOf('if (req->dry_run)') < source.dispatcher.indexOf('// ── CONFIRM'));
assert(source.dispatcher.indexOf('// ── CONFIRM') < source.dispatcher.indexOf('// ── PHASE 2: dedup'));
assert(source.security.includes('CMD_RESPONSE_PREFIX       "res:"'));
assert(source.response.includes('publish_fn(response_topic, json, strlen(json), 1, false)'));
assert(source.dispatcher.includes('n > 127'));
assert(source.dispatcher.includes('"status", "audit", "capabilities", "cmd", "control"'));

const output = {
  evidence: 'CONTRACT_TESTED',
  evidenceClass: 'SOURCE_PROVEN_COMMAND_SECURITY_ORDER_AND_LIMITS',
  sourceDigests: Object.fromEntries(Object.entries(files).map(([key, file]) => [key, digest(source[key])])),
  sourceAnchors: {
    dedupCapacity: { file: files.dedupHeader, line: lineOf(source.dedupHeader, '#define CMD_DEDUP_MAX_ENTRIES'), value: 128 },
    dedupIdentity: { file: files.dedup, line: lineOf(source.dedup, 'strncmp(s_cache[i].cmd_id'), value: 'exact cmd_id string; volatile RAM LRU; not device/key-generation scoped' },
    dedupClockGate: { file: files.dedup, line: lineOf(source.dedup, 'sntp_get_sync_status()'), value: 'expiry sweep requires completed SNTP; reboot/deinit loses cache' },
    rawHeaderLimit: { file: files.security, line: lineOf(source.security, 'CMD_SIGNABLE_MAX          1024'), value: 1023 },
    ttl: { file: files.dispatcher, line: lineOf(source.dispatcher, 'CMD_TTL_MAX_MS'), maxMs: 86400000, futureSkewSeconds: 60, defaultMs: 120000 },
    dryRunOrder: { file: files.dispatcher, line: lineOf(source.dispatcher, 'if (req->dry_run)'), value: 'dry_run precedes confirm and dedup/security verification' },
    responseSigning: { file: files.security, line: lineOf(source.security, 'CMD_RESPONSE_PREFIX'), prefix: 'res:', noKey: 'plaintext response is allowed when no command key exists' },
    responseTopicValidator: { file: files.dispatcher, line: lineOf(source.dispatcher, 'static bool validate_response_topic'), maxLength: 127, deviceBinding: 'not proven by validator; broker ACL/server binding required' },
    responsePublish: { file: files.response, line: lineOf(source.response, 'publish_fn(response_topic'), qos: 1, retain: false },
  },
  serverPolicyRequired: [
    'Server deduplication is durable/server-owned; firmware cache is only a volatile 128-entry id-only safety net.',
    'Server rejects response_topic overrides unless ACL binds the resulting topic to the authenticated device namespace.',
    'Server models dry_run as a separate non-mutating operation because firmware evaluates it before confirm and dedup.',
    'Server must not claim every response is signed; firmware emits plaintext when no command key exists.',
  ],
  limitations: [
    'This fixture is source evidence, not on-device, broker SUBACK, or runtime reconnect proof.',
    'Resolved MQTT config precedence and canonical LWT values remain fixture/soak work.',
  ],
};
fs.mkdirSync(path.dirname(outputPath), { recursive: true });
fs.writeFileSync(outputPath, `${JSON.stringify(output, null, 2)}\n`);
console.log(JSON.stringify(output, null, 2));
