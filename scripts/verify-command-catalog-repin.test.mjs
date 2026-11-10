// Locks the independent §11 checker. Runs without HMS-IDF:
//   node --test scripts/verify-command-catalog-repin.test.mjs
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import fs from 'node:fs';
import test from 'node:test';

import {
  compareWithCatalog,
  diffRows,
  extractActionRows,
  gitCommand,
  pairSetSha256,
  readSpecAt,
} from './verify-command-catalog-repin.mjs';

const ACTION_HEADER = '| Action | Class | Permission server | TTL default ms | Dry run | Confirm |\n|---|---|---|---|---|---|';

// Decoy action tables sit in §10 and §12; the domain table inside §11 has dotted permission cells
// but no dotted first cell, so only the §11 action table may be read.
const SPEC = [
  '# Spec',
  '',
  '## 10. Before',
  '',
  ACTION_HEADER,
  '| `decoy.before` | R | `decoy.view` | 120000 | Không | `—` |',
  '',
  '## 11. Manifest permission để server thực thi',
  '',
  '4 cặp, 3 namespace.',
  '',
  ACTION_HEADER,
  '| `system.reboot` | F | `sys.reboot` | 60000 | Có | `REBOOT-<DEVID>` |',
  '| `system.get_info` | R | `sys.info_view` | 120000 | Không | `—` |',
  '| `config.get` | R | `Theo domain (bảng sau)` | 120000 | Không | `—` |',
  '| `adc.set_channel` | W | `adc.config_edit` | 30000 | Không | `—` |',
  '',
  '| Domain | config.get | config.set |',
  '|---|---|---|',
  '| `adc` | `adc.view` | `adc.config_edit` |',
  '',
  '## 12. After',
  '',
  ACTION_HEADER,
  '| `decoy.after` | R | `decoy.view` | 120000 | Không | `—` |',
  '',
].join('\n');

const keyOf = (r) => `${r.type}.${r.action}`;

test('extractActionRows reads only the §11 action table', () => {
  const rows = extractActionRows(SPEC);

  assert.deepEqual(rows.map(keyOf), ['system.reboot', 'system.get_info', 'config.get', 'adc.set_channel']);
  assert.equal(new Set(rows.map((r) => r.type)).size, 3);
  assert.deepEqual(rows[0], {
    type: 'system', action: 'reboot', class: 'F', required_perm: 'sys.reboot', ttl_ms: 60000, dry_run: true,
    confirm_template: 'REBOOT-<DEVID>',
  });
});

test('extractActionRows maps the by-domain permission and the empty confirm to null', () => {
  const rows = extractActionRows(SPEC);
  const get = rows.find((r) => keyOf(r) === 'config.get');

  assert.equal(get.required_perm, null);
  assert.equal(get.confirm_template, null);
  assert.equal(get.dry_run, false);
  assert.deepEqual(extractActionRows(SPEC.normalize('NFD')), rows);
  assert.deepEqual(extractActionRows(SPEC.replace(/\n/g, '\r\n')), rows);
});

test('extractActionRows refuses a duplicate row and a document without section 11', () => {
  const dup = SPEC.replace('| `system.get_info` |', '| `system.reboot` | R | `sys.reboot` | 1 | Không | `—` |\n| `system.get_info` |');
  assert.throws(() => extractActionRows(dup), /duplicate/);
  assert.throws(() => extractActionRows('# nothing here'), /section 11/);
});

test('extractActionRows refuses a section 11 action row that does not have the exact row shape', () => {
  const nearMiss = SPEC.replace('| `system.get_info` | R |', '| `system.get_info` | r |');
  assert.notEqual(nearMiss, SPEC);
  assert.throws(() => extractActionRows(nearMiss), /system\.get_info/);
});

test('pairSetSha256 hashes the sorted pairs like the server test helper', () => {
  const pair = (type, action) => ({ type, action });
  const rows = [pair('a', 'x'), pair('Zeta', 'b'), pair('a', 'Y')];
  const expected = crypto.createHash('sha256')
    .update('[{"type":"Zeta","action":"b"},{"type":"a","action":"Y"},{"type":"a","action":"x"}]', 'utf8').digest('hex');

  assert.equal(pairSetSha256(rows), expected);
  assert.equal(pairSetSha256([...rows].reverse()), expected);
});

test('diffRows lists added, removed and changed rows by field', () => {
  const base = extractActionRows(SPEC);
  const head = base
    .filter((r) => keyOf(r) !== 'system.get_info')
    .map((r) => (keyOf(r) === 'adc.set_channel' ? { ...r, required_perm: 'tag.view', ttl_ms: 1000 } : r))
    .concat([{ type: 'sms', action: 'send', class: 'B', required_perm: 'sms.send', ttl_ms: 60000, dry_run: false, confirm_template: null }]);

  const diff = diffRows(base, head);

  assert.deepEqual(diff.added.map(keyOf), ['sms.send']);
  assert.deepEqual(diff.removed.map(keyOf), ['system.get_info']);
  assert.deepEqual(diff.changed, [
    { key: 'adc.set_channel', fields: { required_perm: ['adc.config_edit', 'tag.view'], ttl_ms: [30000, 1000] } },
  ]);
  assert.deepEqual(diffRows(base, base), { added: [], removed: [], changed: [] });
});

function catalogFor(rows) {
  return {
    commands: rows.map((r) => ({
      type: r.type, action: r.action, class: r.class, required_perm: r.required_perm,
      domain_policy: r.required_perm === null, ttl_ms: r.ttl_ms, confirm_required: r.confirm_template !== null,
      confirm_template: r.confirm_template, dry_run: r.dry_run,
    })),
  };
}

test('compareWithCatalog reports missing, extra and differing rows', () => {
  const rows = extractActionRows(SPEC);
  assert.deepEqual(compareWithCatalog(rows, catalogFor(rows)), []);

  const differing = catalogFor(rows);
  differing.commands.find((c) => c.action === 'set_channel').required_perm = 'tag.view';
  assert.equal(compareWithCatalog(rows, differing).length, 1);
  assert.match(compareWithCatalog(rows, differing)[0], /adc\.set_channel.*required_perm/);

  const missing = catalogFor(rows);
  missing.commands = missing.commands.filter((c) => c.action !== 'get_info');
  assert.match(compareWithCatalog(rows, missing).join('\n'), /system\.get_info.*missing from the catalog/);

  const extra = catalogFor(rows);
  extra.commands.push({ ...extra.commands[0], type: 'zzz', action: 'extra' });
  assert.match(compareWithCatalog(rows, extra).join('\n'), /zzz\.extra.*not in the spec/);

  const confirmMismatch = catalogFor(rows);
  confirmMismatch.commands.find((c) => c.action === 'reboot').confirm_required = false;
  assert.match(compareWithCatalog(rows, confirmMismatch).join('\n'), /system\.reboot.*confirm_required/);
});

test('the checker reads git without replace objects or inherited GIT_ variables and does not import the generator', () => {
  const command = gitCommand('R', ['show', 'a:b'], { PATH: 'p', GIT_DIR: 'x', git_replace_ref_base: 'r', GIT_CONFIG_COUNT: '1' });
  assert.equal(command.file, 'git');
  assert.deepEqual(command.args, ['--no-replace-objects', '-c', 'core.fsmonitor=false', '-C', 'R', 'show', 'a:b']);
  assert.deepEqual(command.env, { PATH: 'p' });

  let called = 0;
  const git = () => { called += 1; return Buffer.from(''); };
  for (const rev of ['HEAD', 'c80797d', 'C'.repeat(40)]) {
    assert.throws(() => readSpecAt(git, rev), /full 40-hex/, rev);
  }
  assert.equal(called, 0);

  const source = fs.readFileSync(new URL('./verify-command-catalog-repin.mjs', import.meta.url), 'utf8');
  assert.doesNotMatch(source, /from\s+['"][^'"]*generate-command-catalog/);
  assert.doesNotMatch(source, /import\(\s*['"][^'"]*generate-command-catalog/);
});
