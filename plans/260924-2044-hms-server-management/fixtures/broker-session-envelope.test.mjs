import test from 'node:test';
import assert from 'node:assert/strict';

const windowMs = 30_000;
const state = { seen: new Set(), activeKeys: new Set(['key-a', 'key-b']) };

function verify(envelope, expected, now) {
  if (!envelope || envelope.authenticated !== true) return 'missing-trust';
  if (envelope.keyId && !state.activeKeys.has(envelope.keyId)) return 'unknown-key';
  if (!envelope.clientId || !envelope.username || !envelope.originalTopic ||
      !envelope.messageId || !Number.isFinite(envelope.receivedAt)) return 'missing-field';
  if (Math.abs(now - envelope.receivedAt) > windowMs) return 'expired';
  if (envelope.clientId !== expected.clientId || envelope.username !== expected.username) return 'identity-mismatch';
  if (!envelope.originalTopic.startsWith(expected.topicPrefix)) return 'topic-mismatch';
  if (state.seen.has(envelope.messageId)) return 'replay';
  state.seen.add(envelope.messageId);
  return 'accepted';
}

const base = {
  authenticated: true,
  clientId: 'device-001',
  username: 'device-001',
  originalTopic: 'hms/device-001/status',
  payload: '{"ok":true}',
  receivedAt: 1_000_000,
  messageId: 'msg-001',
  keyId: 'key-a',
};
const expected = { clientId: 'device-001', username: 'device-001', topicPrefix: 'hms/device-001/' };

test('accepts authenticated broker envelope once', () => {
  assert.equal(verify(base, expected, 1_000_001), 'accepted');
});
test('rejects missing or unauthenticated metadata', () => {
  assert.equal(verify({ ...base, messageId: 'msg-002', authenticated: false }, expected, 1_000_001), 'missing-trust');
  assert.equal(verify({ ...base, messageId: 'msg-003', username: undefined }, expected, 1_000_001), 'missing-field');
});
test('rejects topic/client/enrollment mismatch', () => {
  assert.equal(verify({ ...base, messageId: 'msg-004', originalTopic: 'hms/device-002/status' }, expected, 1_000_001), 'topic-mismatch');
  assert.equal(verify({ ...base, messageId: 'msg-005', clientId: 'device-002' }, expected, 1_000_001), 'identity-mismatch');
});
test('rejects expired and replayed messages', () => {
  assert.equal(verify({ ...base, messageId: 'msg-006' }, expected, 1_040_001), 'expired');
  assert.equal(verify({ ...base, messageId: 'msg-001' }, expected, 1_000_002), 'replay');
});
test('supports key rotation overlap and denies retired key', () => {
  assert.equal(verify({ ...base, messageId: 'msg-007', keyId: 'key-b' }, expected, 1_000_003), 'accepted');
  state.activeKeys.delete('key-a');
  assert.equal(verify({ ...base, messageId: 'msg-008', keyId: 'key-a' }, expected, 1_000_004), 'unknown-key');
});

