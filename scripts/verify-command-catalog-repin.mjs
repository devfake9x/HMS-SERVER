// Independent check of a catalog re-pin. generate-command-catalog.mjs parses §11 of the HMS-IDF
// MQTT spec with a structural Markdown parser; this script reads the same section a different way
// (one regex per table line) and never imports the generator, so a parser mistake in one cannot
// hide in the other. It prints the row and namespace counts and the pair-set SHA-256 of a spec
// commit, the row-by-row diff against a base commit, and every field that differs from
// command-catalog.json.
//
//   node scripts/verify-command-catalog-repin.mjs --rev <sha40> [--base <sha40>] [--idf <repo>]
//        [--catalog <path>] [--expect-pair-set <hex>]
//
// Exits 1 when the catalog differs from the spec rows, when --expect-pair-set differs, or on bad
// arguments. Like the generator it reads git objects only: --no-replace-objects, no inherited
// GIT_* variables, never a working tree. The git helper is copied, not imported, on purpose.
import { execFileSync } from 'node:child_process';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const SPEC_PATH = 'engineering-docs/api/mqtt-api.md';
const DEFAULT_IDF_REPO = 'D:/WORKSPACE/PROJECT/HMS-IDF';
const DEFAULT_CATALOG = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)), '..', 'src', 'Hms.Server.Infrastructure', 'Commands', 'command-catalog.json');
const FULL_COMMIT = /^[0-9a-f]{40}$/;

const nfc = (text) => text.normalize('NFC');
const SECTION_START = /^## 11\. /;
const NEXT_SECTION = /^## /;
const BY_DOMAIN = nfc('Theo domain (bảng sau)');
const ROW_LINE = new RegExp(
  '^\\| `([A-Za-z0-9_]+)\\.([A-Za-z0-9_]+)` \\| ([RWCDBF]) \\| `([^`]+)` \\| (\\d+) \\| (Có|Không) \\| `([^`]+)` \\|$', 'u');
// Any line whose first cell is a backticked `type.action` is meant to be an action row.
const ROW_CANDIDATE = /^\|\s*`[^`|]*\.[^`|]*`\s*\|/u;
const ROW_FIELDS = ['class', 'required_perm', 'ttl_ms', 'dry_run', 'confirm_template'];

const compareOrdinal = (left, right) => (left < right ? -1 : left > right ? 1 : 0);
const keyOf = (row) => `${row.type}.${row.action}`;
const byKey = (left, right) => compareOrdinal(left.type, right.type) || compareOrdinal(left.action, right.action);

// ---------------------------------------------------------------------------------------------
// Pure helpers (exported for the tests)
// ---------------------------------------------------------------------------------------------

// Rows of the §11 action table: the lines between the "## 11. " heading and the next "## " heading
// that have the exact shape of an action row. The config-domain table and every other section are
// never read.
export function extractActionRows(markdown) {
  const lines = nfc(markdown).replace(/\r\n/g, '\n').split('\n');
  const start = lines.findIndex((line) => SECTION_START.test(line));
  if (start === -1) throw new Error('spec section 11 heading ("## 11. ") not found');
  const end = lines.findIndex((line, index) => index > start && NEXT_SECTION.test(line));
  const rows = [];
  const seen = new Set();
  for (const line of lines.slice(start + 1, end === -1 ? lines.length : end)) {
    const match = ROW_LINE.exec(line);
    if (!match) {
      if (ROW_CANDIDATE.test(line)) throw new Error(`spec §11: action row does not have the exact row shape: ${line.trim()}`);
      continue;
    }
    const [, type, action, cls, permission, ttl, dryRun, confirm] = match;
    const key = `${type}.${action}`;
    if (seen.has(key)) throw new Error(`spec §11: duplicate action ${key}`);
    seen.add(key);
    rows.push({
      type, action, class: cls,
      required_perm: permission === BY_DOMAIN ? null : permission,
      ttl_ms: Number(ttl),
      dry_run: dryRun === nfc('Có'),
      confirm_template: confirm === '—' ? null : confirm,
    });
  }
  return rows;
}

// Same algorithm as CatalogTests.PairSetSha256: ordinal-sorted (type, action) pairs as compact JSON.
export function pairSetSha256(rows) {
  const pairs = [...rows].sort(byKey).map(({ type, action }) => ({ type, action }));
  return crypto.createHash('sha256').update(JSON.stringify(pairs), 'utf8').digest('hex');
}

export function diffRows(base, head) {
  const baseByKey = new Map(base.map((row) => [keyOf(row), row]));
  const headByKey = new Map(head.map((row) => [keyOf(row), row]));
  const changed = [];
  for (const [key, after] of headByKey) {
    const before = baseByKey.get(key);
    if (!before) continue;
    const fields = {};
    for (const field of ROW_FIELDS) if (before[field] !== after[field]) fields[field] = [before[field], after[field]];
    if (Object.keys(fields).length > 0) changed.push({ key, fields });
  }
  return {
    added: head.filter((row) => !baseByKey.has(keyOf(row))).sort(byKey),
    removed: base.filter((row) => !headByKey.has(keyOf(row))).sort(byKey),
    changed: changed.sort((l, r) => compareOrdinal(l.key, r.key)),
  };
}

// One line per difference between the spec rows and a parsed command-catalog.json.
export function compareWithCatalog(rows, catalog) {
  const problems = [];
  const catalogByKey = new Map(catalog.commands.map((command) => [keyOf(command), command]));
  const specKeys = new Set(rows.map(keyOf));
  for (const row of [...rows].sort(byKey)) {
    const key = keyOf(row);
    const command = catalogByKey.get(key);
    if (!command) {
      problems.push(`${key}: missing from the catalog`);
      continue;
    }
    const expected = {
      class: row.class,
      required_perm: row.required_perm,
      domain_policy: row.required_perm === null,
      ttl_ms: row.ttl_ms,
      dry_run: row.dry_run,
      confirm_template: row.confirm_template,
      confirm_required: row.confirm_template !== null,
    };
    for (const [field, value] of Object.entries(expected)) {
      if (command[field] !== value) {
        problems.push(`${key}: ${field} is ${JSON.stringify(command[field])} in the catalog, ${JSON.stringify(value)} in the spec`);
      }
    }
  }
  for (const key of [...catalogByKey.keys()].sort(compareOrdinal)) {
    if (!specKeys.has(key)) problems.push(`${key}: in the catalog but not in the spec`);
  }
  return problems;
}

export function gitCommand(repo, args, env = process.env) {
  const cleanEnv = Object.fromEntries(Object.entries(env).filter(([name]) => !/^GIT_/i.test(name)));
  return { file: 'git', args: ['--no-replace-objects', '-c', 'core.fsmonitor=false', '-C', repo, ...args], env: cleanEnv };
}

// `git` is (...args) => Buffer. Reads the spec of one full commit id, objects only.
export function readSpecAt(git, rev) {
  if (!FULL_COMMIT.test(rev)) throw new Error(`revision must be a full 40-hex commit id, found ${rev}`);
  const resolved = git('rev-parse', '--verify', `${rev}^{commit}`).toString('utf8').trim();
  if (resolved !== rev) throw new Error(`${rev} resolved to ${resolved}`);
  return git('show', `${rev}:${SPEC_PATH}`).toString('utf8');
}

// ---------------------------------------------------------------------------------------------
// CLI
// ---------------------------------------------------------------------------------------------

function parseArgs(argv) {
  const options = {};
  const names = new Map([
    ['--rev', 'rev'], ['--base', 'base'], ['--idf', 'idf'], ['--catalog', 'catalog'], ['--expect-pair-set', 'expectPairSet'],
  ]);
  for (let index = 0; index < argv.length; index += 2) {
    const name = names.get(argv[index]);
    if (!name) throw new Error(`unknown argument ${argv[index]}`);
    const value = argv[index + 1];
    if (!value) throw new Error(`${argv[index]} needs a value`);
    options[name] = value;
  }
  if (!options.rev) throw new Error('--rev <sha40> is required');
  if (options.expectPairSet !== undefined && !/^[0-9a-f]{64}$/.test(options.expectPairSet)) {
    throw new Error('--expect-pair-set must be 64 lowercase hex characters');
  }
  return options;
}

function runGit(repo, args) {
  const { file, args: gitArgs, env } = gitCommand(repo, args);
  return execFileSync(file, gitArgs, { env, maxBuffer: 16 * 1024 * 1024, stdio: ['ignore', 'pipe', 'pipe'] });
}

function describeRow(row) {
  return `${keyOf(row)} ${row.class} ${row.required_perm ?? 'by-domain'} ttl ${row.ttl_ms}`;
}

function main(argv) {
  const options = parseArgs(argv);
  const repo = options.idf ?? process.env.HMS_IDF_REPO ?? DEFAULT_IDF_REPO;
  const git = (...args) => runGit(repo, args);
  const rows = extractActionRows(readSpecAt(git, options.rev));
  const pairSet = pairSetSha256(rows);
  let failed = false;

  console.log(`rev ${options.rev}`);
  console.log(`rows ${rows.length}, namespaces ${new Set(rows.map((row) => row.type)).size}`);
  console.log(`pair-set sha256 ${pairSet}`);

  if (options.base) {
    const diff = diffRows(extractActionRows(readSpecAt(git, options.base)), rows);
    console.log(`diff vs ${options.base}: ${diff.added.length} added, ${diff.changed.length} changed, ${diff.removed.length} removed`);
    for (const row of diff.added) console.log(`  + ${describeRow(row)}`);
    for (const row of diff.removed) console.log(`  - ${describeRow(row)}`);
    for (const { key, fields } of diff.changed) {
      const parts = Object.entries(fields).map(([field, [before, after]]) => `${field} ${JSON.stringify(before)} -> ${JSON.stringify(after)}`);
      console.log(`  ~ ${key}: ${parts.join(', ')}`);
    }
  }

  const catalogPath = options.catalog ? path.resolve(options.catalog) : DEFAULT_CATALOG;
  const problems = compareWithCatalog(rows, JSON.parse(fs.readFileSync(catalogPath, 'utf8')));
  console.log(`catalog ${path.relative(process.cwd(), catalogPath)}: ${problems.length} mismatches`);
  for (const problem of problems) console.log(`  ! ${problem}`);
  if (problems.length > 0) failed = true;

  if (options.expectPairSet !== undefined && options.expectPairSet !== pairSet) {
    console.error(`pair-set sha256 is ${pairSet}, expected ${options.expectPairSet}`);
    failed = true;
  }
  return failed ? 1 : 0;
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
    console.error(`verify-command-catalog-repin: ${error.message}`);
    process.exitCode = 1;
  }
}
