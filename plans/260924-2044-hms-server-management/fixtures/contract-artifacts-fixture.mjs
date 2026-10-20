import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';

const planDir = path.resolve(process.argv[2] || 'plans/260924-2044-hms-server-management');
const firmwareRoot = process.argv[3] || 'D:/WORKSPACE/PROJECT/HMS-IDF';
const outputPath = process.argv[4] || path.join(planDir, 'fixtures', 'contract-artifacts-report.json');

const contractDir = path.join(planDir, 'contracts');
const jsonFiles = [
  'mqtt/broker-session-envelope.schema.json',
  'mqtt/command-catalog-manifest.schema.json',
  'mqtt/command-envelope.schema.json',
  'mqtt/capabilities.schema.json',
  'mqtt/command-catalog.json',
  'operations/load-profile-v1.json',
  'provisioning/import-bootstrap.schema.json',
  'provisioning/device-bundle-v1.schema.json',
  'security/license-trust-store.schema.json',
];

const readJson = (relative) => {
  const file = path.join(contractDir, relative);
  assert(fs.existsSync(file), `missing contract artifact: ${file}`);
  return { file, value: JSON.parse(fs.readFileSync(file, 'utf8')) };
};
const sha256 = (value) => crypto.createHash('sha256').update(value).digest('hex');

const parsed = Object.fromEntries(jsonFiles.map((relative) => [relative, readJson(relative)]));
const envelope = parsed['mqtt/command-envelope.schema.json'].value;
assert.deepEqual(envelope.required, ['type', 'id', 'ts', 'data', 'sig']);
assert.equal(envelope.additionalProperties, false);
for (const forbidden of ['kid', 'generation', 'catalogVersion', 'catalogHash', 'authorizationEpoch', 'policyVersion']) {
  assert(envelope['x-forbidden-wire-fields'].includes(forbidden), `wire forbidden field missing: ${forbidden}`);
}
assert.equal(envelope.properties.ttl_ms.maximum, 86400000);
assert.equal(envelope.properties.sig.pattern, '^[0-9a-fA-F]{64}$');

const topicContract = fs.readFileSync(path.join(contractDir, 'mqtt/topics.md'), 'utf8');
for (const requiredText of [
  '<prefix>/<device_id>/cmd/#',
  '<prefix>/<device_id>/cmd/res',
  '<prefix>/<device_id>/capabilities',
  'CONTRACT_UNAVAILABLE',
  '/cmd/execute',
]) {
  assert(topicContract.includes(requiredText), `topic contract text missing: ${requiredText}`);
}
assert(topicContract.includes('must not guess `/cmd/execute`'));

const bundle = parsed['provisioning/device-bundle-v1.schema.json'].value;
assert.equal(bundle.properties.broker_mode.const, 'PRIMARY_ONLY');
for (const forbidden of ['admin_password', 'human_password', 'secondary_broker', 'telemetry_key']) {
  assert(bundle['x-forbidden-fields'].includes(forbidden), `bundle forbidden field missing: ${forbidden}`);
}
assert.equal(bundle.properties.keyset.required.includes('command_key_base64'), true);

const trustStore = parsed['security/license-trust-store.schema.json'].value;
for (const required of ['kid', 'public_key', 'issuer', 'status', 'effective_from', 'rotation_audit', 'revocation_audit']) {
  assert(trustStore.required.includes(required), `trust-store field missing: ${required}`);
}
assert(trustStore['x-production-boundary'].includes('external signer or KMS/HSM'));

const catalog = parsed['mqtt/command-catalog.json'].value;
const baseline = JSON.parse(fs.readFileSync(path.join(planDir, 'fixtures', 'catalog-baseline.json'), 'utf8'));
const catalogRows = [...catalog.commands].sort((a, b) => `${a.type}\u0000${a.action}`.localeCompare(`${b.type}\u0000${b.action}`));
assert.equal(catalogRows.length, 94);
assert.deepEqual(catalogRows, baseline.commands);
assert.equal(catalog.catalog_hash, 'dbda8317758e5f25c5a4dee8456841ef3689fea4707cf5a27c30ffad656f78d9');
assert.equal(sha256(JSON.stringify(catalogRows)), catalog.catalog_hash);

const load = parsed['operations/load-profile-v1.json'].value;
assert.equal(load.clients, 1000);
assert.equal(load.evidence, 'DESIGN_TARGET_NOT_MEASURED');
assert.equal(load.ha.multiRegionActiveActive, false);

const protoSource = path.join(firmwareRoot, 'protobuf', 'proto', 'license.proto');
const protoPinned = path.join(contractDir, 'license', 'license.proto');
assert(fs.existsSync(protoSource) && fs.existsSync(protoPinned), 'license proto source/pin missing');
assert.deepEqual(fs.readFileSync(protoSource), fs.readFileSync(protoPinned));

const output = {
  evidence: 'CONTRACT_TESTED',
  planDir,
  firmwareRoot,
  jsonArtifacts: jsonFiles.map((relative) => ({
    path: parsed[relative].file,
    sha256: sha256(fs.readFileSync(parsed[relative].file)),
  })),
  commandCatalogRows: catalogRows.length,
  commandCatalogHash: catalog.catalog_hash,
  licenseProtoByteExact: true,
  limitations: [
    'This fixture validates contract artifact structure and selected invariants; it does not validate JSON Schema with a full external validator.',
    'The contracts marked NEW_FIRMWARE_CONTRACT remain unavailable until firmware/importer implementation and integration fixtures pass.',
    'The license proto byte match is against the current workspace source, not a Git commit or released firmware image.',
  ],
};
fs.mkdirSync(path.dirname(outputPath), { recursive: true });
fs.writeFileSync(outputPath, `${JSON.stringify(output, null, 2)}\n`);
console.log(JSON.stringify(output, null, 2));

