import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';

const contractDir = path.join('plans', '260924-2044-hms-server-management', 'contracts', 'provisioning');
const bootstrapSchema = JSON.parse(fs.readFileSync(path.join(contractDir, 'import-bootstrap.schema.json'), 'utf8'));
const bundleSchema = JSON.parse(fs.readFileSync(path.join(contractDir, 'device-bundle-v1.schema.json'), 'utf8'));

const VALID = ['deviceId', 'generation', 'importNonce', 'expiresAt', 'bootstrapProof', 'credentials', 'keyset', 'licenseEnvelope'];
const forbidden = ['adminPassword', 'humanPassword'];
const initial = () => ({ activeGeneration: 4, activeBundleHash: 'old', activeLicenseHash: 'old-license', staged: null, writes: [] });

function importBundle(state, bundle, context = {}) {
  if (!context.local || !context.authenticated) return { ...state, result: 'reject-remote-or-unauthenticated' };
  if (!bundle || forbidden.some((x) => Object.hasOwn(bundle, x))) return { ...state, result: 'reject-forbidden-secret' };
  if (VALID.some((x) => bundle[x] === undefined)) return { ...state, result: 'reject-schema' };
  if (bundle.expiresAt <= context.now || context.usedNonces?.has(bundle.importNonce)) return { ...state, result: 'reject-proof-or-replay' };
  if (bundle.generation < state.activeGeneration) return { ...state, result: 'reject-lower-generation' };
  if (bundle.generation === state.activeGeneration && bundle.bundleHash === state.activeBundleHash) return { ...state, result: 'idempotent' };
  if (bundle.generation === state.activeGeneration) return { ...state, result: 'reject-conflicting-generation' };
  const staged = { bundleHash: bundle.bundleHash, generation: bundle.generation, licenseHash: bundle.licenseHash };
  const next = { ...state, staged, writes: [...state.writes, 'stage-nonlicense', 'commit-nonlicense', 'license-pending'] };
  if (context.powerLossAt === 'before-license-commit') return { ...next, result: 'old-generation-authoritative' };
  const committed = { ...next, activeGeneration: bundle.generation, activeBundleHash: bundle.bundleHash, activeLicenseHash: bundle.licenseHash, writes: [...next.writes, 'license-commit'] };
  if (context.powerLossAt === 'after-license-commit') return { ...committed, result: 'recovery-required' };
  return { ...committed, staged: null, writes: [...committed.writes, 'commit-active'], result: 'applied' };
}

const baseBundle = { deviceId: 'dev-1', generation: 5, bundleHash: 'new', licenseHash: 'new-license', importNonce: 'nonce-5', expiresAt: 2_000, bootstrapProof: 'proof', credentials: {}, keyset: {}, licenseEnvelope: 'opaque' };

test('bootstrap schema delegates complete bundle fields to the canonical v1 schema', () => {
  assert.equal(bootstrapSchema.$ref, 'device-bundle-v1.schema.json');
  assert.deepEqual(bundleSchema.properties.credentials.required, ['mqtt_username', 'mqtt_password']);
  assert.deepEqual(bundleSchema.properties.keyset.required, ['command_key_base64', 'command_key_version']);
  assert.equal(bundleSchema.properties.broker_mode.const, 'PRIMARY_ONLY');
});

test('invalid bundle writes nothing', () => assert.equal(importBundle(initial(), { generation: 5 }, { local: true, authenticated: true, now: 1 }).writes.length, 0));
test('same committed bundle is idempotent', () => assert.equal(importBundle({ ...initial(), activeGeneration: 5, activeBundleHash: 'new' }, baseBundle, { local: true, authenticated: true, now: 1 }).result, 'idempotent'));
test('lower and conflicting generations are rejected', () => {
  assert.equal(importBundle(initial(), { ...baseBundle, generation: 3 }, { local: true, authenticated: true, now: 1 }).result, 'reject-lower-generation');
  assert.equal(importBundle({ ...initial(), activeGeneration: 5 }, baseBundle, { local: true, authenticated: true, now: 1 }).result, 'reject-conflicting-generation');
});
test('replayed, expired, remote, and human-password bundles are rejected', () => {
  assert.equal(importBundle(initial(), baseBundle, { local: true, authenticated: true, now: 1, usedNonces: new Set(['nonce-5']) }).result, 'reject-proof-or-replay');
  assert.equal(importBundle(initial(), { ...baseBundle, expiresAt: 1 }, { local: true, authenticated: true, now: 2 }).result, 'reject-proof-or-replay');
  assert.equal(importBundle(initial(), baseBundle, { local: false, authenticated: true, now: 1 }).result, 'reject-remote-or-unauthenticated');
  assert.equal(importBundle(initial(), { ...baseBundle, adminPassword: 'forbidden' }, { local: true, authenticated: true, now: 1 }).result, 'reject-forbidden-secret');
});
test('power loss preserves old authority before license commit and fails closed after commit', () => {
  const before = importBundle(initial(), baseBundle, { local: true, authenticated: true, now: 1, powerLossAt: 'before-license-commit' });
  assert.equal(before.activeGeneration, 4);
  assert.equal(before.result, 'old-generation-authoritative');
  const after = importBundle(initial(), baseBundle, { local: true, authenticated: true, now: 1, powerLossAt: 'after-license-commit' });
  assert.equal(after.activeGeneration, 5);
  assert.equal(after.result, 'recovery-required');
});
