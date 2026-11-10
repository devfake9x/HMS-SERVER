// Locks the catalog generator's spec parser, canonicalizer and serializer. Runs without HMS-IDF:
//   node --test scripts/generate-command-catalog.test.mjs
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import crypto from 'node:crypto';
import test from 'node:test';

import {
  assertPolicyCharset,
  canonicalJson,
  canonicalize,
  catalogHash,
  gitCommand,
  OUTPUT_PATH,
  parseArgs,
  parseSpecManifest,
  readSpecAtCommit,
  renderCatalog,
  run,
  SOURCE_COMMIT,
  SOURCE_PATH,
} from './generate-command-catalog.mjs';

const ROW_FIELDS = [
  'type', 'action', 'class', 'required_perm', 'domain_policy', 'ttl_ms', 'confirm_required', 'confirm_template', 'dry_run',
];
const DOMAIN_FIELDS = ['domain', 'view_perm', 'edit_perm'];
const TOP_LEVEL_FIELDS = [
  'catalog_version', 'catalog_hash', 'canonicalization', 'source_repo', 'source_commit', 'source_path',
  'source_blob', 'source_hash', 'config_domains', 'commands',
];
const BACKSLASH = String.fromCharCode(92);

const row = (overrides = {}) => ({
  type: 'system',
  action: 'get_info',
  class: 'R',
  required_perm: 'sys.info_view',
  domain_policy: false,
  ttl_ms: 120000,
  confirm_required: false,
  confirm_template: null,
  dry_run: false,
  ...overrides,
});
const domain = (overrides = {}) => ({ domain: 'adc', view_perm: 'adc.view', edit_perm: 'adc.config_edit', ...overrides });
const key = (command) => `${command.type}.${command.action}`;

// Shared with tests/Hms.Server.UnitTests/CatalogTests.cs: the same input and the same canonical
// text. Rows and domains are listed out of order; "Zeta" sorts before "config" only under
// ordinal comparison; the template exercises the angle-bracket escape.
const GOLDEN_INPUT = {
  commands: [
    row({
      type: 'system', action: 'reboot', class: 'F', required_perm: 'sys.reboot', ttl_ms: 60000,
      confirm_required: true, confirm_template: 'REBOOT-<DEVID>', dry_run: true,
    }),
    row({ type: 'config', action: 'set', class: 'W', required_perm: null, domain_policy: true }),
    row({ type: 'Zeta', action: 'b', class: 'R', required_perm: 'z.view' }),
  ],
  config_domains: [
    domain({ domain: 'wifi_ap', view_perm: 'wifi.ap_view', edit_perm: 'wifi.ap_edit' }),
    domain(),
  ],
};
const GOLDEN_CANONICAL = String.raw`{"commands":[{"type":"Zeta","action":"b","class":"R","required_perm":"z.view","domain_policy":false,"ttl_ms":120000,"confirm_required":false,"confirm_template":null,"dry_run":false},{"type":"config","action":"set","class":"W","required_perm":null,"domain_policy":true,"ttl_ms":120000,"confirm_required":false,"confirm_template":null,"dry_run":false},{"type":"system","action":"reboot","class":"F","required_perm":"sys.reboot","domain_policy":false,"ttl_ms":60000,"confirm_required":true,"confirm_template":"REBOOT-\u003CDEVID\u003E","dry_run":true}],"config_domains":[{"domain":"adc","view_perm":"adc.view","edit_perm":"adc.config_edit"},{"domain":"wifi_ap","view_perm":"wifi.ap_view","edit_perm":"wifi.ap_edit"}]}`;

test('assertPolicyCharset rejects characters outside the policy alphabet', () => {
  const outside = ['sys+info', 'a&b', "it's", 'a`b', 'a"b', `a${BACKSLASH}b`, `caf${String.fromCharCode(0xe9)}`,
    `a${String.fromCharCode(1)}b`, 'a b', ''];
  for (const value of outside) {
    assert.throws(
      () => assertPolicyCharset({ commands: [row({ required_perm: value })], config_domains: [domain()] }),
      (error) => error.message.includes('system.get_info') && error.message.includes('required_perm'),
      `required_perm ${JSON.stringify(value)} must be rejected`);
  }
  assert.throws(
    () => assertPolicyCharset({ commands: [row()], config_domains: [domain({ edit_perm: 'adc config' })] }),
    (error) => error.message.includes('adc') && error.message.includes('edit_perm'));
  assert.doesNotThrow(() => assertPolicyCharset({
    commands: [
      row({ type: 'system', action: 'reboot', confirm_required: true, confirm_template: 'REBOOT-<DEVID>' }),
      row({ required_perm: 'wifi.sta_edit' }),
      row({ type: 'config', action: 'get', required_perm: null, domain_policy: true }),
    ],
    config_domains: [domain({ domain: 'wifi_sta', view_perm: 'wifi.sta_view', edit_perm: 'wifi.sta_edit' })],
  }));
});

test('canonicalJson escapes angle brackets like System.Text.Json', () => {
  assert.equal(canonicalJson('REBOOT-<DEVID>'), `"REBOOT-${BACKSLASH}u003CDEVID${BACKSLASH}u003E"`);
  assert.equal(canonicalJson({ code: 'wifi.sta_edit-_.9Z' }), '{"code":"wifi.sta_edit-_.9Z"}');
  assert.equal(canonicalJson({ none: null, yes: true, no: false, ttl: 60000 }),
    '{"none":null,"yes":true,"no":false,"ttl":60000}');
});

test('canonicalize sorts commands by (type, action) and domains by domain, ordinal', () => {
  const input = {
    commands: [
      row({ type: 'a', action: 'x' }),
      row({ type: 'z', action: 'b' }),
      row({ type: 'B', action: 'x' }),
      row({ type: 'a', action: 'Y' }),
      row({ type: 'Z', action: 'a' }),
    ],
    config_domains: [domain({ domain: 'b' }), domain({ domain: 'a' }), domain({ domain: 'A' })],
  };

  const canonical = canonicalize(input);
  const parsed = JSON.parse(canonical);
  const ordinal = ['B.x', 'Z.a', 'a.Y', 'a.x', 'z.b'];
  assert.deepEqual(parsed.commands.map(key), ordinal);
  assert.deepEqual(parsed.config_domains.map((x) => x.domain), ['A', 'a', 'b']);

  const byLocale = [...input.commands]
    .sort((l, r) => l.type.localeCompare(r.type) || l.action.localeCompare(r.action))
    .map(key);
  assert.notDeepEqual(byLocale, ordinal);

  const reversed = { commands: [...input.commands].reverse(), config_domains: [...input.config_domains].reverse() };
  assert.equal(canonicalize(reversed), canonical);
});

test('canonicalize writes the fixed field order', () => {
  const shuffledRow = Object.fromEntries(Object.entries(row()).reverse());
  const shuffledDomain = Object.fromEntries(Object.entries(domain()).reverse());

  const parsed = JSON.parse(canonicalize({ commands: [shuffledRow], config_domains: [shuffledDomain] }));
  assert.deepEqual(Object.keys(parsed), ['commands', 'config_domains']);
  assert.deepEqual(Object.keys(parsed.commands[0]), ROW_FIELDS);
  assert.deepEqual(Object.keys(parsed.config_domains[0]), DOMAIN_FIELDS);

  const { dry_run: _missing, ...withoutDryRun } = row();
  assert.throws(() => canonicalize({ commands: [withoutDryRun], config_domains: [domain()] }), /dry_run/);
  assert.throws(() => canonicalize({ commands: [row({ audit_publish: true })], config_domains: [domain()] }), /audit_publish/);
  assert.throws(() => canonicalize({ commands: [row({ ttl_ms: '60000' })], config_domains: [domain()] }), /ttl_ms/);
  assert.throws(() => canonicalize({ commands: [row({ ttl_ms: null })], config_domains: [domain()] }), /ttl_ms/);
  assert.throws(() => canonicalize({ commands: [row({ dry_run: 1 })], config_domains: [domain()] }), /dry_run/);
  assert.throws(
    () => canonicalize({ commands: [row()], config_domains: [{ domain: 'adc', view_perm: 'adc.view' }] }),
    /edit_perm/);
});

test('golden vector matches the C# hash test', () => {
  assert.equal(canonicalize(GOLDEN_INPUT), GOLDEN_CANONICAL);
  assert.equal(catalogHash(GOLDEN_INPUT), crypto.createHash('sha256').update(GOLDEN_CANONICAL, 'utf8').digest('hex'));
  assert.match(catalogHash(GOLDEN_INPUT), /^[0-9a-f]{64}$/);
});

test('renderCatalog output is LF-only and deterministic', () => {
  const doc = {
    catalog_version: 'mqtt-api-83e93d77e',
    catalog_hash: catalogHash(GOLDEN_INPUT),
    canonicalization: 'v2',
    source_repo: 'HMS-IDF',
    source_commit: '83e93d77e4a433efce665d669b567b3acc51b735',
    source_path: 'engineering-docs/api/mqtt-api.md',
    source_blob: '6485e00f4c609a1ba57063cfd7e7aef75955c58b',
    source_hash: 'ef41c113b001459b2cc0d0e6bb8c3fd00f8008c22bd583af8a2e72d6f33e5274',
    config_domains: GOLDEN_INPUT.config_domains,
    commands: GOLDEN_INPUT.commands,
  };

  const text = renderCatalog(doc);
  assert.ok(!text.includes('\r'), 'output must be LF-only');
  assert.ok(text.endsWith('}\n') && !text.endsWith('\n\n'), 'output must end with exactly one newline');
  const parsed = JSON.parse(text);
  assert.deepEqual(Object.keys(parsed), TOP_LEVEL_FIELDS);
  assert.deepEqual(parsed.commands.map(key), ['Zeta.b', 'config.set', 'system.reboot']);
  assert.deepEqual(parsed.config_domains.map((x) => x.domain), ['adc', 'wifi_ap']);
  assert.deepEqual(Object.keys(parsed.commands[0]), ROW_FIELDS);

  assert.equal(renderCatalog(doc), text);
  const shuffled = Object.fromEntries(Object.entries({
    ...doc,
    commands: [...doc.commands].reverse(),
    config_domains: [...doc.config_domains].reverse(),
  }).reverse());
  assert.equal(renderCatalog(shuffled), text);

  assert.throws(() => renderCatalog({ ...doc, extra: true }), /extra/);
  assert.throws(() => renderCatalog({ ...doc, dispatcher_default_ttl_ms: 120000 }), /dispatcher_default_ttl_ms/);
  const { source_blob: _missing, ...withoutBlob } = doc;
  assert.throws(() => renderCatalog(withoutBlob), /source_blob/);
});

// A cut-down mqtt-api.md around §11, in the spec's own table layout. The tables in §10 and
// §12 are decoys: only §11 may be read.
const ACTION_HEADER = '| Action | Class | Permission server | TTL default ms | Dry run | Confirm |\n|---|---|---|---|---|---|';
const DOMAIN_TABLE = [
  '| Domain | config.get | config.set |',
  '|---|---|---|',
  '| `adc` | `adc.view` | `adc.config_edit` |',
  '| `mqtt` | `mqtt.status_view` | `mqtt.config_edit` |',
].join('\n');
const SPEC_FIXTURE = [
  '# Đặc tả tích hợp MQTT của thiết bị HMS-IDF',
  '',
  '## 10. Status, sự kiện và giới hạn tích hợp',
  '',
  ACTION_HEADER,
  '| `decoy.row` | R | `decoy.view` | 120000 | Không | `—` |',
  '',
  '## 11. Manifest permission để server thực thi',
  '',
  'Permission là metadata policy cho server, **không giới hạn authority của command key ở firmware**.',
  '',
  '6 cặp, 4 namespace trong snapshot. TTL dưới đây là default khi không gửi `ttl_ms`; client có thể override hợp lệ.',
  '',
  ACTION_HEADER,
  '| `system.reboot` | F | `sys.reboot` | 60000 | Có | `REBOOT-<DEVID>` |',
  '| `system.get_info` | R | `sys.info_view` | 120000 | Không | `—` |',
  '| `adc.set_channel` | W | `adc.config_edit` | 120000 | Không | `—` |',
  '| `ota.apply` | F | `sys.ota` | 60000 | Không | `OTA-APPLY-<DEVID>` |',
  '| `config.get` | R | `Theo domain (bảng sau)` | 120000 | Không | `—` |',
  '| `config.set` | W | `Theo domain (bảng sau)` | 120000 | Không | `—` |',
  '',
  DOMAIN_TABLE,
  '',
  '',
  '## 12. Bằng chứng nguồn và tự kiểm chứng',
  '',
  ACTION_HEADER,
  '| `decoy.after` | R | `decoy.view` | 120000 | Không | `—` |',
  '',
].join('\n');

function mutateSpec(from, to) {
  assert.ok(SPEC_FIXTURE.includes(from), `fixture lacks ${JSON.stringify(from)}`);
  return SPEC_FIXTURE.replace(from, to);
}

test('parseSpecManifest reads the action and domain tables of section 11 only', () => {
  const { commands, config_domains } = parseSpecManifest(SPEC_FIXTURE);

  assert.deepEqual(config_domains, [
    domain(),
    domain({ domain: 'mqtt', view_perm: 'mqtt.status_view', edit_perm: 'mqtt.config_edit' }),
  ]);
  assert.deepEqual(commands, [
    row({
      type: 'system', action: 'reboot', class: 'F', required_perm: 'sys.reboot', ttl_ms: 60000,
      confirm_required: true, confirm_template: 'REBOOT-<DEVID>', dry_run: true,
    }),
    row(),
    row({ type: 'adc', action: 'set_channel', class: 'W', required_perm: 'adc.config_edit' }),
    // Confirmed but without dry run: confirm and dry run are read from their own columns.
    row({
      type: 'ota', action: 'apply', class: 'F', required_perm: 'sys.ota', ttl_ms: 60000,
      confirm_required: true, confirm_template: 'OTA-APPLY-<DEVID>', dry_run: false,
    }),
    row({ type: 'config', action: 'get', class: 'R', required_perm: null, domain_policy: true }),
    row({ type: 'config', action: 'set', class: 'W', required_perm: null, domain_policy: true }),
  ]);
});

test('parseSpecManifest accepts a TTL at the spec cap', () => {
  const atCap = SPEC_FIXTURE.replace('| `system.get_info` | R | `sys.info_view` | 120000 |', '| `system.get_info` | R | `sys.info_view` | 86400000 |');

  const info = parseSpecManifest(atCap).commands.find((command) => key(command) === 'system.get_info');
  assert.equal(info.ttl_ms, 86400000);
});

test('parseSpecManifest reads CRLF and NFD documents the same way', () => {
  const expected = parseSpecManifest(SPEC_FIXTURE);

  assert.deepEqual(parseSpecManifest(SPEC_FIXTURE.replace(/\n/g, '\r\n')), expected);
  assert.deepEqual(parseSpecManifest(SPEC_FIXTURE.normalize('NFD')), expected);
});

test('parseSpecManifest fails loudly on a table it cannot vouch for', () => {
  const rebootRow = '| `system.reboot` | F | `sys.reboot` | 60000 | Có | `REBOOT-<DEVID>` |';
  const infoRow = '| `system.get_info` | R | `sys.info_view` | 120000 | Không | `—` |';
  const cases = [
    ['missing section heading', mutateSpec('## 11. Manifest permission để server thực thi', '## 11. Manifest'), /section 11/],
    ['renamed column', mutateSpec(`${ACTION_HEADER}\n| \`system.reboot\``,
      `${ACTION_HEADER.replace('Permission server', 'Permission')}\n| \`system.reboot\``), /header/],
    ['extra cell', mutateSpec(infoRow, `${infoRow} x |`), /cells/],
    ['unknown class', mutateSpec(infoRow, infoRow.replace('| R |', '| X |')), /class/],
    ['permission outside the charset', mutateSpec(infoRow, infoRow.replace('`sys.info_view`', '`sys info_view`')), /permission/],
    ['permission without backticks', mutateSpec(infoRow, infoRow.replace('`sys.info_view`', 'sys.info_view')), /backticks/],
    ['zero TTL', mutateSpec(infoRow, infoRow.replace('120000', '0')), /TTL/],
    ['TTL in exponent form', mutateSpec(infoRow, infoRow.replace('120000', '1.2e5')), /TTL/],
    ['TTL with a leading zero', mutateSpec(infoRow, infoRow.replace('120000', '0120000')), /TTL/],
    ['unknown dry-run value', mutateSpec(infoRow, infoRow.replace('Không', 'Yes')), /Dry run/],
    ['confirm template outside the charset', mutateSpec(rebootRow, rebootRow.replace('`REBOOT-<DEVID>`', '`REBOOT <DEVID>`')), /Confirm/],
    ['duplicate action', mutateSpec(infoRow, `${infoRow}\n${infoRow}`), /duplicate/],
    ['duplicate domain', mutateSpec('| `mqtt` |', '| `adc` |'), /duplicate/],
    ['stated pair count differs', mutateSpec('6 cặp, 4 namespace', '7 cặp, 4 namespace'), /states 7 pairs/],
    ['stated namespace count differs', mutateSpec('6 cặp, 4 namespace', '6 cặp, 5 namespace'), /states 5 namespaces/],
    ['stated namespace count differs, plural', mutateSpec('6 cặp, 4 namespace', '6 cặp, 5 namespaces'), /states 5 namespaces/],
    ['pair count no longer stated', mutateSpec('6 cặp, 4 namespace trong snapshot.', 'Snapshot.'), /pair count/],
    ['namespace count no longer stated', mutateSpec('6 cặp, 4 namespace trong snapshot.', '6 cặp trong snapshot.'), /namespace count/],
    ['domain table missing', mutateSpec(DOMAIN_TABLE, 'Domain table removed.'), /2 tables/],
    ['third table', mutateSpec(DOMAIN_TABLE, `${DOMAIN_TABLE}\n\n${DOMAIN_TABLE.replaceAll('adc', 'sms')}`), /2 tables/],
    ['stray table line', mutateSpec(DOMAIN_TABLE, DOMAIN_TABLE.replace('| Domain | config.get | config.set |\n', '')), /stray table line/],
    ['action without a namespace', mutateSpec('`adc.set_channel`', '`adcset_channel`'), /type\.action/],
    ['domain header changed', mutateSpec('| Domain | config.get | config.set |', '| Domain | config.get | config.put |'), /header/],
    ['action header missing a column', mutateSpec(`${ACTION_HEADER}\n| \`system.reboot\``,
      `| Action | Class | Permission server | TTL default ms | Dry run |\n|---|---|---|---|---|\n| \`system.reboot\``), /header/],
    ['row missing a cell', mutateSpec(infoRow, '| `system.get_info` | R | `sys.info_view` | 120000 | Không |'), /cells/],
    ['negative TTL', mutateSpec(infoRow, infoRow.replace('120000', '-1')), /TTL/],
    ['empty TTL', mutateSpec(infoRow, infoRow.replace('120000', '')), /TTL/],
    ['TTL above the spec cap', mutateSpec(infoRow, infoRow.replace('120000', '86400001')), /TTL/],
    ['domain cell without backticks', mutateSpec('`adc.view` |', 'adc.view |'), /backticks/],
    ['domain permission outside the charset', mutateSpec('`adc.view` |', '`adc view` |'), /outside the policy charset/],
    ['permission that only NFC turns into the charset', mutateSpec(infoRow,
      infoRow.replace('`sys.info_view`', `\`sys.info_vie${String.fromCharCode(0x212a)}\``)), /outside the policy charset/],
    ['confirm template without <DEVID>', mutateSpec(rebootRow, rebootRow.replace('`REBOOT-<DEVID>`', '`REBOOT-NOW`')), /DEVID/],
    ['hyphen in the Confirm column', mutateSpec(rebootRow, rebootRow.replace('`REBOOT-<DEVID>`', '`-`')), /DEVID/],
    ['empty domain table with by-domain rows', mutateSpec(DOMAIN_TABLE, '| Domain | config.get | config.set |\n|---|---|---|'),
      /empty config-domain table/],
    ['by-domain row outside config.get and config.set', mutateSpec('| `adc.set_channel` | W | `adc.config_edit` |',
      '| `adc.set_channel` | W | `Theo domain (bảng sau)` |'), /config\.get and config\.set/],
    ['config.set classified as a read', mutateSpec('| `config.set` | W |', '| `config.set` | R |'), /config\.set/],
    ['row without a leading pipe after a table', mutateSpec('| `mqtt` | `mqtt.status_view` | `mqtt.config_edit` |',
      '`mqtt` | `mqtt.status_view` | `mqtt.config_edit` |'), /blank line/],
    ['indented table line', mutateSpec('| `mqtt` |', '    | `mqtt` |'), /column 0/],
    ['delimiter row shorter than the header', mutateSpec('| Domain | config.get | config.set |\n|---|---|---|',
      '| Domain | config.get | config.set |\n|---|---|'), /delimiter row/],
    ['table inside a code fence', mutateSpec(DOMAIN_TABLE, `\`\`\`\n${DOMAIN_TABLE}\n\`\`\``), /code fence/],
    ['section heading appears twice', `${SPEC_FIXTURE}\n## 11. Manifest permission để server thực thi\n`, /more than once/],
    ['indented section heading', mutateSpec('## 11. Manifest permission để server thực thi', '    ## 11. Manifest permission để server thực thi'),
      /section 11/],
    ['action outside the charset only before NFC', mutateSpec('`adc.set_channel`', `\`adc.set_channe${String.fromCharCode(0x212a)}\``),
      /outside the policy charset/],
    ['domain name outside the charset only before NFC', mutateSpec('| `mqtt` |', `| \`mqt${String.fromCharCode(0x212a)}\` |`),
      /outside the policy charset/],
    ['confirm template outside the charset only before NFC', mutateSpec(rebootRow,
      rebootRow.replace('`REBOOT-<DEVID>`', `\`REBOO${String.fromCharCode(0x212a)}-<DEVID>\``)), /outside the policy charset/],
    ['domain name outside the charset', mutateSpec('| `mqtt` |', '| `mq tt` |'), /outside the policy charset/],
    ['tilde code fence', mutateSpec(DOMAIN_TABLE, `~~~\n${DOMAIN_TABLE}\n~~~`), /code fence/],
    ['config.get classified as a write', mutateSpec('| `config.get` | R |', '| `config.get` | W |'), /config\.get/],
    ['domain table hidden in an HTML comment', mutateSpec(DOMAIN_TABLE, `<!--\n\n${DOMAIN_TABLE}\n\n-->`), /HTML/],
    ['HTML block above the action table', mutateSpec(`${ACTION_HEADER}\n| \`system.reboot\``,
      `<div>\n${ACTION_HEADER}\n| \`system.reboot\``), /HTML/],
    ['table written as HTML', mutateSpec(DOMAIN_TABLE, `${DOMAIN_TABLE}\n\n<table><tr><td>x</td></tr></table>`), /HTML/],
    ['table inside a blockquote', mutateSpec(DOMAIN_TABLE, `${DOMAIN_TABLE}\n\n> | Domain | config.get | config.set |`), /blockquote/],
    ['setext heading inside the section', mutateSpec('Permission là metadata policy cho server',
      'Aside\n---\n\nPermission là metadata policy cho server'), /setext/],
    ['heading with a tab ends the section', mutateSpec(DOMAIN_TABLE, `##\tAside\n\n${DOMAIN_TABLE}`), /2 tables/],
    ['empty action table', mutateSpec('6 cặp, 4 namespace', '0 cặp, 0 namespace').replace(
      /\| `system\.reboot`[\s\S]*?\| `config\.set` \| W \| `Theo domain \(bảng sau\)` \| 120000 \| Không \| `—` \|\n/, ''), /no rows/],
  ];

  for (const [name, source, pattern] of cases) {
    assert.throws(() => parseSpecManifest(source), pattern, name);
  }
});

test('importing the module never runs the CLI, whatever argv holds', () => {
  const moduleUrl = new URL('./generate-command-catalog.mjs', import.meta.url).href;
  const output = execFileSync(process.execPath, [
    '--input-type=module', '-e', `await import(${JSON.stringify(moduleUrl)}); console.log('imported');`, 'not-a-real-path',
  ], { encoding: 'utf8' });

  assert.equal(output.trim(), 'imported');
});

// ---------------------------------------------------------------------------------------------
// Stated-count flag, safe git reads and the CLI entry (run)
// ---------------------------------------------------------------------------------------------

// The first count sentence is the stale one, the way the real spec words it; the second is current.
const STALE_COUNTS = mutateSpec('6 cặp, 4 namespace trong snapshot.',
  'Snapshot cũ có 5 cặp, 3 namespace. Kiểm lại: 7 cặp, 4 namespace trong snapshot.');
// Only the second sentence is fixed to agree with the table; the first still states 5/3.
const SECOND_FIXED = mutateSpec('6 cặp, 4 namespace trong snapshot.',
  'Snapshot cũ có 5 cặp, 3 namespace. Hiện tại: 6 cặp, 4 namespace trong snapshot.');
const ACCEPT_5_3 = { acceptStatedCount: { pairs: 5, namespaces: 3 } };
const TABLE_NUMBERS = /\b6\b|\b4\b/;

const gitBlobOf = (bytes) => crypto.createHash('sha1')
  .update(Buffer.concat([Buffer.from(`blob ${bytes.length}`), Buffer.from([0]), bytes])).digest('hex');

// A git double that answers the three object reads for `text` and records each call.
function fakeGit(text, { commit = SOURCE_COMMIT, blobOverride } = {}) {
  const bytes = Buffer.from(text, 'utf8');
  const blob = blobOverride ?? gitBlobOf(bytes);
  const calls = [];
  const answer = (args) => {
    if (args[0] === 'rev-parse' && args[1] === '--verify') return Buffer.from(`${commit}\n`);
    if (args[0] === 'rev-parse') return Buffer.from(`${blob}\n`);
    if (args[0] === 'show') return bytes;
    throw new Error(`unexpected git ${args.join(' ')}`);
  };
  return {
    calls, blob, bytes,
    readOnly: (...args) => { calls.push(args); return answer(args); },
    withRepo: (repo, args) => { calls.push([repo, args]); return answer(args); },
  };
}

const expectedReads = (commit = SOURCE_COMMIT) => [
  ['rev-parse', '--verify', `${commit}^{commit}`],
  ['rev-parse', `${commit}:${SOURCE_PATH}`],
  ['show', `${commit}:${SOURCE_PATH}`],
];

function fakeFs(initial) {
  const writes = [];
  return {
    writes,
    existsSync: () => initial !== undefined,
    readFileSync: () => initial,
    writeFileSync: (file, text) => { writes.push([file, text]); },
  };
}

function runWith(argv, text, extra = {}) {
  const git = fakeGit(text);
  const out = [];
  const files = extra.fs ?? fakeFs();
  const code = run(argv, {
    git: git.withRepo, fs: files, stdout: (m) => out.push(['stdout', m]), stderr: (m) => out.push(['stderr', m]),
    env: {}, ...extra.deps,
  });
  return { code, git, out, files };
}

test('parseSpecManifest returns no warnings when the stated counts match the table', () => {
  assert.deepEqual(parseSpecManifest(SPEC_FIXTURE).warnings, []);
});

test('parseSpecManifest accepts the stated counts named by --accept-stated-count, and warns', () => {
  const expected = parseSpecManifest(SPEC_FIXTURE);
  for (const source of [STALE_COUNTS, SECOND_FIXED]) {
    const parsed = parseSpecManifest(source, ACCEPT_5_3);
    assert.deepEqual(parsed.commands, expected.commands);
    assert.deepEqual(parsed.config_domains, expected.config_domains);
    assert.deepEqual(parsed.warnings, [
      'spec §11 states 5 pairs and 3 namespaces, which disagrees with its action table; accepted by --accept-stated-count 5/3',
    ]);
    assert.doesNotMatch(parsed.warnings[0], TABLE_NUMBERS);
  }
});

test('parseSpecManifest rejects stated counts that disagree with the table when no flag names them', () => {
  assert.throws(() => parseSpecManifest(STALE_COUNTS), (error) => {
    assert.equal(error.message,
      'spec §11 states 5 pairs, which disagrees with its action table; review the table, then rerun with --accept-stated-count 5/3');
    assert.doesNotMatch(error.message, TABLE_NUMBERS);
    return true;
  });
  assert.throws(() => parseSpecManifest(mutateSpec('6 cặp, 4 namespace', '6 cặp, 5 namespace')),
    /states 5 namespaces, which disagrees with its action table; review the table, then rerun with --accept-stated-count 6\/5/);
});

test('parseSpecManifest rejects an --accept-stated-count that is not what §11 states', () => {
  for (const [pairs, namespaces] of [[7, 4], [6, 4], [5, 4], [98, 12]]) {
    assert.throws(() => parseSpecManifest(STALE_COUNTS, { acceptStatedCount: { pairs, namespaces } }),
      /does not match the count §11 states \(5\/3\); update or remove the flag/, `${pairs}/${namespaces}`);
  }
  assert.throws(() => parseSpecManifest(SPEC_FIXTURE, ACCEPT_5_3), /does not match the count §11 states \(6\/4\)/);
});

test('parseSpecManifest asks to remove --accept-stated-count once §11 agrees with its table', () => {
  assert.throws(() => parseSpecManifest(SPEC_FIXTURE, { acceptStatedCount: { pairs: 6, namespaces: 4 } }),
    /which agrees with its action table; remove --accept-stated-count/);
  assert.throws(() => parseSpecManifest(mutateSpec('6 cặp, 4 namespace trong snapshot.', 'Snapshot.'), ACCEPT_5_3), /pair count/);
});

test('parseArgs reads --accept-stated-count as <pairs>/<namespaces>', () => {
  assert.deepEqual(parseArgs(['--accept-stated-count', '98/12']).acceptStatedCount, { pairs: 98, namespaces: 12 });
  assert.deepEqual(parseArgs(['--accept-stated-count=98/12']).acceptStatedCount, { pairs: 98, namespaces: 12 });
  assert.equal(parseArgs([]).acceptStatedCount, undefined);
  assert.deepEqual(parseArgs(['--check', '--idf', 'x', '--accept-stated-count', '9/2']),
    { check: true, idf: 'x', acceptStatedCount: { pairs: 9, namespaces: 2 } });
  for (const bad of [undefined, '98', '98/', '/12', '0/12', '98/0', '098/12', '98:12', '98/12/1', '-1/2', '1e2/3', 'a/b']) {
    const argv = bad === undefined ? ['--accept-stated-count'] : ['--accept-stated-count', bad];
    assert.throws(() => parseArgs(argv), /--accept-stated-count needs <pairs>\/<namespaces>/, String(bad));
  }
});

test('readSpecAtCommit reads the spec only from git objects of the pinned commit', () => {
  const git = fakeGit(SPEC_FIXTURE);

  const { bytes, sourceBlob } = readSpecAtCommit(git.readOnly);

  assert.deepEqual(git.calls, expectedReads());
  assert.equal(bytes.toString('utf8'), SPEC_FIXTURE);
  assert.equal(sourceBlob, git.blob);
  assert.match(SOURCE_COMMIT, /^[0-9a-f]{40}$/);
  assert.equal(SOURCE_PATH, 'engineering-docs/api/mqtt-api.md');
});

test('readSpecAtCommit refuses bytes that do not hash to the commit blob', () => {
  const git = fakeGit(SPEC_FIXTURE, { blobOverride: '0'.repeat(40) });
  assert.throws(() => readSpecAtCommit(git.readOnly), /textconv or filter/);
});

test('readSpecAtCommit refuses a commit id that is abbreviated, symbolic or resolves elsewhere', () => {
  for (const commit of ['HEAD', 'c80797d', SOURCE_COMMIT.toUpperCase()]) {
    const git = fakeGit(SPEC_FIXTURE);
    assert.throws(() => readSpecAtCommit(git.readOnly, commit), /full 40-hex commit id/, commit);
    assert.equal(git.calls.length, 0, `git must not run for ${commit}`);
  }
  const elsewhere = fakeGit(SPEC_FIXTURE, { commit: 'f'.repeat(40) });
  assert.throws(() => readSpecAtCommit(elsewhere.readOnly), /resolved to/);
});

test('gitCommand reads without replace objects and without inherited GIT_ variables', () => {
  const env = {
    PATH: 'p', HOME: 'h', GIT_DIR: 'x', git_object_directory: 'y', GIT_ALTERNATE_OBJECT_DIRECTORIES: 'z',
    GIT_REPLACE_REF_BASE: 'r', GIT_CONFIG_COUNT: '1',
  };
  const before = { ...env };

  const command = gitCommand('R', ['show', 'a:b'], env);

  assert.equal(command.file, 'git');
  assert.deepEqual(command.args, ['--no-replace-objects', '-c', 'core.fsmonitor=false', '-C', 'R', 'show', 'a:b']);
  assert.deepEqual(command.env, { PATH: 'p', HOME: 'h' });
  assert.deepEqual(env, before);
});

test('run writes the catalog from the pinned commit through three git object reads', () => {
  const { code, git, out, files } = runWith(['--idf', 'R'], SPEC_FIXTURE);

  assert.equal(code, 0);
  assert.deepEqual(git.calls, expectedReads().map((args) => ['R', args]));
  assert.equal(files.writes.length, 1);
  assert.equal(files.writes[0][0], OUTPUT_PATH);
  const doc = JSON.parse(files.writes[0][1]);
  assert.equal(doc.source_commit, SOURCE_COMMIT);
  assert.equal(doc.source_blob, git.blob);
  assert.equal(doc.catalog_version, `mqtt-api-${SOURCE_COMMIT.slice(0, 9)}`);
  assert.equal(out[0][0], 'stdout');
  assert.match(out[0][1], /^wrote /);

  const fromEnv = runWith([], SPEC_FIXTURE, { deps: { env: { HMS_IDF_REPO: 'E' } } });
  assert.deepEqual(fromEnv.git.calls.map(([repo]) => repo), ['E', 'E', 'E']);
});

test('run prints the accepted count warning on stderr before anything on stdout', () => {
  const { code, out } = runWith(['--accept-stated-count', '5/3'], STALE_COUNTS);

  assert.equal(code, 0);
  assert.equal(out[0][0], 'stderr');
  assert.match(out[0][1], /^generate-command-catalog: warning: spec §11 states 5 pairs and 3 namespaces, /);
  assert.ok(out.slice(1).every(([stream]) => stream === 'stdout'));
  assert.ok(out.some(([stream]) => stream === 'stdout'));
});

test('run --check reports a stale catalog with the rerun hint and writes nothing', () => {
  const written = runWith(['--accept-stated-count', '5/3'], STALE_COUNTS);
  const text = written.files.writes[0][1];

  const stale = runWith(['--check', '--accept-stated-count', '5/3'], STALE_COUNTS, { fs: fakeFs(`${text}x`) });
  assert.equal(stale.code, 1);
  assert.ok(stale.out.some(([stream, message]) => stream === 'stderr' && message.includes('--accept-stated-count 5/3')));
  assert.equal(stale.files.writes.length, 0);

  for (const committed of [text, text.replace(/\n/g, '\r\n')]) {
    const fresh = runWith(['--check', '--accept-stated-count', '5/3'], STALE_COUNTS, { fs: fakeFs(committed) });
    assert.equal(fresh.code, 0);
    assert.ok(fresh.out.some(([stream, message]) => stream === 'stdout' && message.includes('is up to date')));
    assert.equal(fresh.files.writes.length, 0);
  }
});

test('run refuses a commit that is not a full id before calling git', () => {
  for (const commit of ['HEAD', 'c80797d']) {
    const git = fakeGit(SPEC_FIXTURE);
    assert.throws(() => run([], { git: git.withRepo, fs: fakeFs(), stdout() {}, stderr() {}, env: {}, commit }),
      /full 40-hex commit id/, commit);
    assert.equal(git.calls.length, 0);
  }
});
