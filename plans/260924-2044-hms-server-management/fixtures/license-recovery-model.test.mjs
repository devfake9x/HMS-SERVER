import test from 'node:test';
import assert from 'node:assert/strict';

const apply = (state, envelope, fault = null) => {
  if (!envelope.valid) return { ...state, writes: state.writes, result: 'reject-invalid' };
  if (envelope.generation < state.generation) {
    return { ...state, result: 'reject-lower-generation' };
  }
  if (state.pending && state.pending.envelope !== envelope.bytes) {
    return { ...state, result: 'recovery-required' };
  }
  if (state.pending && state.pending.envelope === envelope.bytes) {
    if (fault === 'clock-clear') return { ...state, result: 'still-pending' };
    return {
      active: envelope.bytes,
      generation: envelope.generation,
      pending: null,
      writes: state.writes + 1,
      result: 'resumed',
    };
  }
  if (fault === 'before-commit') return { ...state, result: 'reject-write' };
  const committed = {
    active: envelope.bytes,
    generation: envelope.generation,
    pending: { envelope: envelope.bytes },
    writes: state.writes + 1,
  };
  if (fault === 'after-commit') return { ...committed, result: 'recovery-required' };
  return {
    active: envelope.bytes,
    generation: envelope.generation,
    pending: null,
    writes: committed.writes + 1,
    result: 'applied',
  };
};

test('invalid bundle writes nothing', () => {
  const before = { active: 'old', generation: 1, pending: null, writes: 0 };
  assert.deepEqual(apply(before, { valid: false, generation: 2, bytes: 'new' }), {
    ...before,
    result: 'reject-invalid',
  });
});

test('lower generation is rejected without mutation', () => {
  const before = { active: 'old', generation: 3, pending: null, writes: 0 };
  assert.deepEqual(apply(before, { valid: true, generation: 2, bytes: 'older' }), {
    ...before,
    result: 'reject-lower-generation',
  });
});

test('power loss before durable commit preserves old authority', () => {
  const before = { active: 'old', generation: 1, pending: null, writes: 0 };
  const after = apply(before, { valid: true, generation: 2, bytes: 'new' }, 'before-commit');
  assert.equal(after.active, 'old');
  assert.equal(after.pending, null);
});

test('power loss after durable commit never silently restores old authority', () => {
  const before = { active: 'old', generation: 1, pending: null, writes: 0 };
  const after = apply(before, { valid: true, generation: 2, bytes: 'new' }, 'after-commit');
  assert.equal(after.active, 'new');
  assert.equal(after.result, 'recovery-required');
  assert.deepEqual(after.pending, { envelope: 'new' });
});

test('only exact pending envelope may resume', () => {
  const pending = {
    active: 'new',
    generation: 2,
    pending: { envelope: 'new' },
    writes: 1,
  };
  assert.equal(apply(pending, { valid: true, generation: 2, bytes: 'other' }).result,
    'recovery-required');
  assert.equal(apply(pending, { valid: true, generation: 2, bytes: 'new' }).result,
    'resumed');
});

