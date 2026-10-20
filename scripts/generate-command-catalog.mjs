// Generates src/Hms.Server.Infrastructure/Commands/command-catalog.json (catalog v2) from §11
// "Manifest permission để server thực thi" of the HMS-IDF MQTT spec
// (engineering-docs/api/mqtt-api.md) at the pinned commit. Server and device talk over MQTT,
// so the spec is the contract: this script reads that document through git, never firmware
// source and never a working tree.
//
//   node scripts/generate-command-catalog.mjs [--idf <path>] [--check]
//
// The HMS-IDF repository is --idf, else env HMS_IDF_REPO, else D:/WORKSPACE/PROJECT/HMS-IDF.
// --check writes nothing and exits 1 when the committed catalog differs from the output.
// The canonicalizer, serializer and spec parser are exported for
// generate-command-catalog.test.mjs, which runs without HMS-IDF; importing this module never
// calls git.
import { execFileSync } from 'node:child_process';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const SOURCE_REPO = 'HMS-IDF';
const SOURCE_COMMIT = '83e93d77e4a433efce665d669b567b3acc51b735';
const SOURCE_PATH = 'engineering-docs/api/mqtt-api.md';
const DEFAULT_IDF_REPO = 'D:/WORKSPACE/PROJECT/HMS-IDF';
// Must equal CommandCatalogHash.Canonicalization; the .NET loader rejects anything else.
const CANONICALIZATION =
  'v2: commands sorted ordinal (type,action) with fixed field order; config_domains sorted ordinal by domain; ' +
  '{commands,config_domains} compact System.Text.Json; UTF-8; SHA-256 lowercase hex';
const OUTPUT_PATH = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)), '..', 'src', 'Hms.Server.Infrastructure', 'Commands', 'command-catalog.json');

const TOP_LEVEL_FIELDS = [
  'catalog_version', 'catalog_hash', 'canonicalization', 'source_repo', 'source_commit', 'source_path',
  'source_blob', 'source_hash', 'config_domains', 'commands',
];
// Exactly what a §11 row states; nothing the spec does not define.
const ROW_FIELDS = [
  'type', 'action', 'class', 'required_perm', 'domain_policy', 'ttl_ms', 'confirm_required', 'confirm_template', 'dry_run',
];
const DOMAIN_FIELDS = ['domain', 'view_perm', 'edit_perm'];
const ROW_STRING_FIELDS = ['type', 'action', 'class', 'required_perm', 'confirm_template'];
const NULLABLE_STRING_FIELDS = new Set(['required_perm', 'confirm_template']);
const BOOLEAN_FIELDS = new Set(['domain_policy', 'confirm_required', 'dry_run']);

// Every string that feeds the hash stays inside this alphabet. Within it, default
// System.Text.Json escapes only < and >, so JSON.stringify plus those two replacements
// reproduces its bytes without emulating the whole escape table.
const POLICY_CHARSET = /^[A-Za-z0-9_.<>-]+$/;
const BACKSLASH = String.fromCharCode(92);

// ---------------------------------------------------------------------------------------------
// Canonicalizer and serializer (exported, pure)
// ---------------------------------------------------------------------------------------------

export function assertPolicyCharset({ commands, config_domains }) {
  for (const command of commands) {
    for (const field of ROW_STRING_FIELDS) {
      const value = command[field];
      if (value === null && NULLABLE_STRING_FIELDS.has(field)) continue;
      assertPolicyString(`command ${command.type}.${command.action}`, field, value);
    }
  }
  for (const entry of config_domains) {
    for (const field of DOMAIN_FIELDS) assertPolicyString(`config domain ${entry.domain}`, field, entry[field]);
  }
}

// Compact JSON with < and > escaped the way default System.Text.Json writes them. Only
// equivalent for strings inside the policy charset.
export function canonicalJson(value) {
  return JSON.stringify(value)
    .replaceAll('<', `${BACKSLASH}u003C`)
    .replaceAll('>', `${BACKSLASH}u003E`);
}

export function canonicalize(input) {
  const tables = orderedTables(input);
  assertPolicyCharset(tables);
  return canonicalJson({ commands: tables.commands, config_domains: tables.config_domains });
}

export function catalogHash(input) {
  return crypto.createHash('sha256').update(canonicalize(input), 'utf8').digest('hex');
}

export function renderCatalog(doc) {
  const ordered = orderFields(doc, TOP_LEVEL_FIELDS, 'catalog', () => true);
  const tables = orderedTables(doc);
  assertPolicyCharset(tables);
  ordered.config_domains = tables.config_domains;
  ordered.commands = tables.commands;
  return `${JSON.stringify(ordered, null, 2)}\n`;
}

function orderedTables({ commands, config_domains }) {
  if (!Array.isArray(commands) || !Array.isArray(config_domains)) {
    throw new Error('commands and config_domains must be arrays');
  }
  return {
    commands: commands
      .map((row) => orderFields(row, ROW_FIELDS, `command ${row?.type}.${row?.action}`, isValidRowValue))
      .sort((l, r) => compareOrdinal(l.type, r.type) || compareOrdinal(l.action, r.action)),
    config_domains: config_domains
      .map((entry) => orderFields(entry, DOMAIN_FIELDS, `config domain ${entry?.domain}`, isValidDomainValue))
      .sort((l, r) => compareOrdinal(l.domain, r.domain)),
  };
}

// Code-unit comparison, the same order as StringComparer.Ordinal (never localeCompare).
function compareOrdinal(left, right) {
  if (left < right) return -1;
  return left > right ? 1 : 0;
}

function orderFields(source, fields, label, isValidValue) {
  if (source === null || typeof source !== 'object' || Array.isArray(source)) {
    throw new Error(`${label}: expected an object`);
  }
  const missing = fields.filter((field) => !Object.hasOwn(source, field));
  const unknown = Object.keys(source).filter((field) => !fields.includes(field));
  if (missing.length > 0 || unknown.length > 0) {
    throw new Error(`${label}: missing fields [${missing.join(', ')}], unknown fields [${unknown.join(', ')}]`);
  }
  const ordered = {};
  for (const field of fields) {
    const value = source[field];
    if (!isValidValue(field, value)) throw new Error(`${label}: field ${field} has the wrong type (${JSON.stringify(value)})`);
    ordered[field] = value;
  }
  return ordered;
}

function isValidRowValue(field, value) {
  if (BOOLEAN_FIELDS.has(field)) return typeof value === 'boolean';
  if (field === 'ttl_ms') return Number.isInteger(value);
  if (value === null) return NULLABLE_STRING_FIELDS.has(field);
  return typeof value === 'string';
}

function isValidDomainValue(_field, value) {
  return typeof value === 'string';
}

function assertPolicyString(label, field, value) {
  if (typeof value !== 'string' || !POLICY_CHARSET.test(value)) {
    throw new Error(`${label}: field ${field} = ${JSON.stringify(value)} ` +
      `is outside the policy charset ${POLICY_CHARSET.source}`);
  }
}

// ---------------------------------------------------------------------------------------------
// mqtt-api.md §11 parser
// ---------------------------------------------------------------------------------------------

// The spec is Vietnamese. Its headings, prose and fixed words are compared in NFC, so an NFD
// copy reads the same. Identifiers (actions, permissions, templates) are checked as written and
// never normalized, so normalization cannot turn a character outside the policy charset into one
// inside it.
const nfc = (text) => text.normalize('NFC');
const SECTION_HEADING = nfc('## 11. Manifest permission để server thực thi');
const ACTION_TABLE_HEADER = ['Action', 'Class', 'Permission server', 'TTL default ms', 'Dry run', 'Confirm'];
const DOMAIN_TABLE_HEADER = ['Domain', 'config.get', 'config.set'];
const BY_DOMAIN = nfc('Theo domain (bảng sau)');
const NO_CONFIRM = '—';
// Every confirm token is a template around the device id (spec §7, §11).
const DEVICE_ID_PLACEHOLDER = '<DEVID>';
const DRY_RUN = new Map([[nfc('Có'), true], [nfc('Không'), false]]);
const CLASS_LETTERS = new Set(['R', 'W', 'C', 'D', 'B', 'F']);
// Spec §3: ttl_ms is 1..86400000.
const MAX_TTL_MS = 86400000;
const PAIR_COUNT = new RegExp(String.raw`(\d+)\s+${nfc('cặp')}\b`);
const NAMESPACE_COUNT = /(\d+)\s+namespaces?\b/;
const TABLE_SEPARATOR = /^\|(\s*:?-+:?\s*\|)+$/;

// Exported for generate-command-catalog.test.mjs, which feeds it synthetic documents. Reads
// only the section between the §11 heading and the next heading of any level, and fails loudly on
// any layout it was not written for rather than guessing.
export function parseSpecManifest(markdown) {
  const lines = markdown.replace(/\r\n/g, '\n').split('\n');
  const headings = lines.flatMap((line, index) => (nfc(line.trimEnd()) === SECTION_HEADING ? [index] : []));
  if (headings.length === 0) throw new Error(`spec section 11 heading not found at column 0: "${SECTION_HEADING}"`);
  if (headings.length > 1) {
    throw new Error(`spec section 11 heading appears more than once (lines ${headings.map((index) => index + 1).join(', ')})`);
  }
  const start = headings[0];
  // §11 has no subsections, so any ATX heading (any level, space or tab after the #s) ends it.
  const next = lines.findIndex((line, index) => index > start && /^ {0,3}#{1,6}(?:[ \t]|$)/.test(line));
  const { tables, prose } = splitSection(lines, start + 1, next === -1 ? lines.length : next);

  if (tables.length !== 2) {
    throw new Error(`spec §11 must hold exactly 2 tables (actions, config domains), found ${tables.length}`);
  }
  const [actionTable, domainTable] = tables;
  assertHeader(actionTable, ACTION_TABLE_HEADER, 'action table');
  assertHeader(domainTable, DOMAIN_TABLE_HEADER, 'config-domain table');
  if (actionTable.rows.length === 0) throw new Error(`spec line ${actionTable.line}: §11 action table has no rows`);

  const commands = actionTable.rows.map(({ cells, line }) => toCommandRow(cells, line));
  const config_domains = domainTable.rows.map(({ cells, line }) => toDomainRow(cells, line));
  assertUnique(commands.map((row) => `${row.type}.${row.action}`), 'action');
  assertUnique(config_domains.map((row) => row.domain), 'config domain');
  assertDomainBinding(commands, config_domains);
  assertStatedCounts(nfc(prose.join('\n')), commands);
  return { commands, config_domains };
}

// Separates the section into Markdown tables and prose, accepting only the layout §11 uses:
// table lines start at column 0, a header is followed by a delimiter row with as many cells, a
// table ends at a blank line or the section end, and no code fence, HTML block, blockquote or
// setext underline appears. Anything else is an error, because a Markdown renderer could read it
// differently. Constructs outside §11 (an unclosed fence or comment in an earlier section) are
// not modelled; the pinned catalog hash and the review of a spec re-pin cover them.
function splitSection(lines, from, to) {
  const tables = [];
  const prose = [];
  const isTableLine = (line) => line.trimStart().startsWith('|');
  const assertColumnZero = (index) => {
    if (!lines[index].startsWith('|')) throw new Error(`spec line ${index + 1}: table lines must start at column 0`);
  };
  let index = from;
  while (index < to) {
    const line = lines[index];
    if (/^\s*(```|~~~)/.test(line)) throw new Error(`spec line ${index + 1}: code fence inside §11`);
    if (line.trimStart().startsWith('<')) throw new Error(`spec line ${index + 1}: HTML block inside §11`);
    if (line.trimStart().startsWith('>')) throw new Error(`spec line ${index + 1}: blockquote inside §11`);
    if (/^ {0,3}(=+|-+)[ \t]*$/.test(line)) {
      throw new Error(`spec line ${index + 1}: setext heading underline or thematic break inside §11`);
    }
    if (!isTableLine(line)) {
      prose.push(line);
      index += 1;
      continue;
    }
    assertColumnZero(index);
    if (index + 1 >= to || !TABLE_SEPARATOR.test(lines[index + 1].trimEnd())) {
      throw new Error(`spec line ${index + 1}: stray table line outside a table with a header and delimiter row`);
    }
    const header = cellsOf(lines[index], index + 1);
    const delimiterCells = cellsOf(lines[index + 1], index + 2).length;
    if (delimiterCells !== header.length) {
      throw new Error(`spec line ${index + 2}: delimiter row has ${delimiterCells} cells, its header has ${header.length}`);
    }
    const table = { header, line: index + 1, rows: [] };
    for (index += 2; index < to && isTableLine(lines[index]); index += 1) {
      assertColumnZero(index);
      table.rows.push({ cells: cellsOf(lines[index], index + 1), line: index + 1 });
    }
    if (index < to && lines[index].trim() !== '') {
      throw new Error(`spec line ${index + 1}: a table must be followed by a blank line`);
    }
    tables.push(table);
  }
  return { tables, prose };
}

function cellsOf(line, lineNumber) {
  const trimmed = line.trim();
  if (!trimmed.startsWith('|') || !trimmed.endsWith('|') || trimmed.length < 2) {
    throw new Error(`spec line ${lineNumber}: a table line must start and end with |`);
  }
  return trimmed.slice(1, -1).split('|').map((cell) => cell.trim());
}

function assertHeader(table, expected, name) {
  if (table.header.length !== expected.length || table.header.some((cell, index) => cell !== expected[index])) {
    throw new Error(`spec line ${table.line}: §11 ${name} header changed; expected "| ${expected.join(' | ')} |", ` +
      `found "| ${table.header.join(' | ')} |"`);
  }
}

function toCommandRow(cells, line) {
  const where = `spec line ${line}`;
  if (cells.length !== ACTION_TABLE_HEADER.length) {
    throw new Error(`${where}: expected ${ACTION_TABLE_HEADER.length} cells, found ${cells.length}`);
  }
  const [actionCell, classCell, permissionCell, ttlCell, dryRunCell, confirmCell] = cells;

  const pair = codeOf(actionCell, `${where}: Action`);
  const dot = pair.indexOf('.');
  if (dot <= 0 || dot === pair.length - 1) throw new Error(`${where}: Action must be \`type.action\`, found ${actionCell}`);
  const type = pair.slice(0, dot);
  const action = pair.slice(dot + 1);
  requirePolicyString(type, `${where}: action type`);
  requirePolicyString(action, `${where}: action name`);

  if (!CLASS_LETTERS.has(classCell)) throw new Error(`${where}: unknown class ${classCell} (expected one of R W C D B F)`);

  const permission = codeOf(permissionCell, `${where}: Permission server`);
  const byDomain = nfc(permission) === BY_DOMAIN;
  if (!byDomain) requirePolicyString(permission, `${where}: permission`);

  if (!/^[1-9]\d*$/.test(ttlCell) || Number(ttlCell) > MAX_TTL_MS) {
    throw new Error(`${where}: TTL default ms must be a positive decimal integer up to ${MAX_TTL_MS} (spec §3), found ${ttlCell}`);
  }
  const dryRun = DRY_RUN.get(nfc(dryRunCell));
  if (dryRun === undefined) throw new Error(`${where}: Dry run must be Có or Không, found ${dryRunCell}`);

  const confirm = codeOf(confirmCell, `${where}: Confirm`);
  const template = confirm === NO_CONFIRM ? null : confirm;
  if (template !== null) {
    requirePolicyString(template, `${where}: Confirm template`);
    if (!template.includes(DEVICE_ID_PLACEHOLDER)) {
      throw new Error(`${where}: Confirm template must contain ${DEVICE_ID_PLACEHOLDER}, found ${template}`);
    }
  }

  return {
    type,
    action,
    class: classCell,
    required_perm: byDomain ? null : permission,
    domain_policy: byDomain,
    ttl_ms: Number(ttlCell),
    confirm_required: template !== null,
    confirm_template: template,
    dry_run: dryRun,
  };
}

function toDomainRow(cells, line) {
  const where = `spec line ${line}`;
  if (cells.length !== DOMAIN_TABLE_HEADER.length) {
    throw new Error(`${where}: expected ${DOMAIN_TABLE_HEADER.length} cells, found ${cells.length}`);
  }
  const [domain, view_perm, edit_perm] = cells.map((cell, index) => codeOf(cell, `${where}: ${DOMAIN_TABLE_HEADER[index]}`));
  requirePolicyString(domain, `${where}: domain`);
  requirePolicyString(view_perm, `${where}: config.get permission`);
  requirePolicyString(edit_perm, `${where}: config.set permission`);
  return { domain, view_perm, edit_perm };
}

// Identifiers in §11 are written as inline code.
function codeOf(cell, label) {
  const code = /^`([^`]+)`$/.exec(cell);
  if (!code) throw new Error(`${label} must be wrapped in backticks, found ${cell}`);
  return code[1];
}

function requirePolicyString(value, label) {
  if (!POLICY_CHARSET.test(value)) {
    throw new Error(`${label} ${JSON.stringify(value)} is outside the policy charset ${POLICY_CHARSET.source}`);
  }
}

function assertUnique(values, label) {
  const seen = new Set();
  for (const value of values) {
    if (seen.has(value)) throw new Error(`spec §11: duplicate ${label} ${value}`);
    seen.add(value);
  }
}

// §11 takes a permission from the config-domain table only for the two actions that are its
// columns. The server picks the view or the edit column by class (design contract §5), which
// equals that binding only while config.get is a read and config.set is not.
function assertDomainBinding(commands, configDomains) {
  const byDomain = commands.filter((row) => row.domain_policy);
  if (byDomain.length === 0) return;
  const actions = byDomain.map((row) => `${row.type}.${row.action}`).sort().join(', ');
  if (actions !== 'config.get, config.set') {
    throw new Error(`spec §11: only config.get and config.set may take their permission from the config-domain table, found ${actions}`);
  }
  for (const row of byDomain) {
    if ((row.action === 'get') !== (row.class === 'R')) {
      throw new Error(`spec §11: config.${row.action} is class ${row.class}; the config-domain columns map to ` +
        'the server\'s view/edit choice only while config.get is R and config.set is not');
    }
  }
  if (configDomains.length === 0) {
    throw new Error('spec §11 has rows permissioned by domain but an empty config-domain table');
  }
}

// §11 states its own size ("94 cặp, 12 namespace"); the tables must agree with it.
function assertStatedCounts(prose, commands) {
  const pairs = PAIR_COUNT.exec(prose);
  if (!pairs) throw new Error('spec §11 no longer states the pair count ("N cặp"); update the generator');
  if (Number(pairs[1]) !== commands.length) {
    throw new Error(`spec §11 states ${pairs[1]} pairs but its action table has ${commands.length} rows`);
  }
  const namespaces = NAMESPACE_COUNT.exec(prose);
  if (!namespaces) throw new Error('spec §11 no longer states the namespace count ("N namespace"); update the generator');
  const typeCount = new Set(commands.map((row) => row.type)).size;
  if (Number(namespaces[1]) !== typeCount) {
    throw new Error(`spec §11 states ${namespaces[1]} namespaces but its action table has ${typeCount}`);
  }
}

// ---------------------------------------------------------------------------------------------
// CLI
// ---------------------------------------------------------------------------------------------

function main(argv) {
  const options = parseArgs(argv);
  const idfRepo = options.idf ?? process.env.HMS_IDF_REPO ?? DEFAULT_IDF_REPO;
  const git = (...args) => execFileSync('git', ['-C', idfRepo, ...args], {
    maxBuffer: 16 * 1024 * 1024,
    stdio: ['ignore', 'pipe', 'pipe'],
  });

  let resolved;
  try {
    resolved = git('rev-parse', '--verify', `${SOURCE_COMMIT}^{commit}`).toString('utf8').trim();
  } catch (error) {
    throw new Error(`cannot resolve ${SOURCE_REPO} commit ${SOURCE_COMMIT} in ${idfRepo} ` +
      `(pass --idf <path> or set HMS_IDF_REPO): ${error.stderr?.toString().trim() || error.message}`);
  }
  if (resolved !== SOURCE_COMMIT) throw new Error(`${idfRepo}: ${SOURCE_COMMIT} resolved to ${resolved}`);

  const sourceBlob = git('rev-parse', `${SOURCE_COMMIT}:${SOURCE_PATH}`).toString('utf8').trim();
  const bytes = git('show', `${SOURCE_COMMIT}:${SOURCE_PATH}`);
  if (gitBlobId(bytes) !== sourceBlob) {
    throw new Error(`git show bytes do not hash to blob ${sourceBlob}; a textconv or filter altered them`);
  }

  const tables = parseSpecManifest(bytes.toString('utf8'));
  const doc = {
    catalog_version: `mqtt-api-${SOURCE_COMMIT.slice(0, 9)}`,
    catalog_hash: catalogHash(tables),
    canonicalization: CANONICALIZATION,
    source_repo: SOURCE_REPO,
    source_commit: SOURCE_COMMIT,
    source_path: SOURCE_PATH,
    source_blob: sourceBlob,
    source_hash: crypto.createHash('sha256').update(bytes).digest('hex'),
    config_domains: tables.config_domains,
    commands: tables.commands,
  };
  const text = renderCatalog(doc);
  const relativeOutput = path.relative(process.cwd(), OUTPUT_PATH);

  if (options.check) {
    const committed = fs.existsSync(OUTPUT_PATH) ? fs.readFileSync(OUTPUT_PATH, 'utf8').replace(/\r\n/g, '\n') : null;
    if (committed !== text) {
      console.error(`${relativeOutput} is stale; run: node scripts/generate-command-catalog.mjs`);
      return 1;
    }
    console.log(`${relativeOutput} is up to date (hash ${doc.catalog_hash})`);
    return 0;
  }

  fs.writeFileSync(OUTPUT_PATH, text, 'utf8');
  console.log(summary(doc, relativeOutput));
  return 0;
}

function parseArgs(argv) {
  const options = { check: false, idf: undefined };
  for (let index = 0; index < argv.length; index += 1) {
    const arg = argv[index];
    if (arg === '--check') {
      options.check = true;
    } else if (arg === '--idf') {
      options.idf = argv[index + 1];
      index += 1;
      if (!options.idf) throw new Error('--idf needs a path');
    } else if (arg.startsWith('--idf=')) {
      options.idf = arg.slice('--idf='.length);
    } else {
      throw new Error(`unknown argument ${arg}`);
    }
  }
  return options;
}

function gitBlobId(bytes) {
  const header = Buffer.from(`blob ${bytes.length}`, 'utf8');
  return crypto.createHash('sha1').update(Buffer.concat([header, Buffer.from([0]), bytes])).digest('hex');
}

function summary(doc, relativeOutput) {
  const classCounts = {};
  for (const row of doc.commands) classCounts[row.class] = (classCounts[row.class] ?? 0) + 1;
  const classes = Object.entries(classCounts).map(([letter, count]) => `${letter}${count}`).join(' ');
  const permissions = new Set([
    ...doc.commands.map((row) => row.required_perm).filter((perm) => perm !== null),
    ...doc.config_domains.flatMap((entry) => [entry.view_perm, entry.edit_perm]),
  ]);
  return [
    `wrote ${relativeOutput}`,
    `  ${doc.commands.length} commands (${classes}), ` +
      `${doc.config_domains.length} config domains, ${permissions.size} permission codes`,
    `  catalog_hash ${doc.catalog_hash}`,
    `  source ${doc.source_path} blob ${doc.source_blob}, sha256 ${doc.source_hash}`,
  ].join('\n');
}

// argv[1] is not always a path (node -e, embedders), so any lookup failure means "imported".
function isEntryPoint() {
  if (!process.argv[1]) return false;
  try {
    const self = fs.realpathSync(fileURLToPath(import.meta.url));
    const entry = fs.realpathSync(path.resolve(process.argv[1]));
    return process.platform === 'win32' ? self.toLowerCase() === entry.toLowerCase() : self === entry;
  } catch {
    return false;
  }
}

if (isEntryPoint()) {
  try {
    process.exitCode = main(process.argv.slice(2));
  } catch (error) {
    console.error(`generate-command-catalog: ${error.message}`);
    process.exitCode = 1;
  }
}
