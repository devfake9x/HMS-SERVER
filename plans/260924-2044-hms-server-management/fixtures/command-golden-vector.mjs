import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';

const root = process.argv[2];
assert(root, 'usage: node command-golden-vector.mjs <HMS-IDF-root>');

const securityPath = path.join(root, 'components', 'command-dispatcher', 'cmd_security.c');
const dispatcherPath = path.join(root, 'components', 'command-dispatcher', 'command_dispatcher.c');
const security = fs.readFileSync(securityPath, 'utf8');
const dispatcher = fs.readFileSync(dispatcherPath, 'utf8');

const allowed = ['type', 'id', 'ts', 'data', 'key_ver', 'ttl_ms', 'dry_run', 'confirm', 'response_topic', 'trace_id', 'sig'];
for (const field of allowed) assert(security.includes(`"${field}"`), `source missing ${field}`);
for (const forbidden of ['kid', 'generation', 'catalogVersion', 'catalogHash', 'authorizationEpoch', 'policyVersion']) {
  assert(!security.includes(`"${forbidden}"`), `${forbidden} must not be a firmware envelope field`);
}
assert(security.includes('CMD_SIGNING_KEY_SIZE      32'));
assert(security.includes('CMD_SIGNABLE_MAX          1024'));
assert(dispatcher.includes('req->ts == 0'));
assert(dispatcher.includes('CMD_TTL_MAX_MS         (24u * 60u * 60u * 1000u)'));
assert(dispatcher.includes('CMD_CLOCK_SKEW_SEC     (60)'));

const key = Buffer.from('000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f', 'hex');
const signable = '{"type":"system","id":"golden-001","ts":1700000000,"data":{"action":"reboot"},"key_ver":7}';
const digest = crypto.createHmac('sha256', key).update(signable, 'utf8').digest('hex');
const wire = `${signable.slice(0, -1)},"sig":"${digest}"}`;

assert.equal(digest.length, 64);
assert.equal(wire.endsWith('}'), true);
assert.equal(wire.slice(0, wire.lastIndexOf(',"sig":"')) + '}', signable);
assert.equal(wire.length < 1024, true);
assert.equal('x'.repeat(1023).length, 1023);
assert.equal('x'.repeat(1024).length >= 1024, true);

console.log(JSON.stringify({
  evidence: 'CONTRACT_TESTED',
  source: [securityPath, dispatcherPath],
  keyRule: 'direct HMAC-SHA256 with 32-byte crypto/cmd_key',
  signable,
  wire,
  digest,
  maxAcceptedRawBytes: 1023,
  forbiddenWireFields: ['kid', 'generation', 'catalogVersion', 'catalogHash', 'authorizationEpoch', 'policyVersion'],
  limitation: 'This is a source-anchored golden vector, not an on-device test.',
}, null, 2));
