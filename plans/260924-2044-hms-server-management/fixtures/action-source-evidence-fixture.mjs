import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';

const root = process.argv[2];
const matrixPath = process.argv[3] || path.join(
  'plans',
  '260924-2044-hms-server-management',
  'fixtures',
  'command-evidence-matrix.json',
);
const outputPath = process.argv[4] || path.join(
  'plans',
  '260924-2044-hms-server-management',
  'fixtures',
  'action-source-evidence.json',
);

assert(root, 'usage: node action-source-evidence-fixture.mjs <HMS-IDF-root> [matrix] [output]');
const resolvedRoot = path.resolve(root);
assert(
  path.basename(resolvedRoot).toLowerCase() === 'hms-idf' &&
    fs.existsSync(path.join(resolvedRoot, 'components')),
  `refusing to write source evidence: expected the HMS-IDF root, got ${resolvedRoot}`,
);
const matrix = JSON.parse(fs.readFileSync(matrixPath, 'utf8'));
assert.equal(matrix.rowCount, 94, 'the source matrix must contain 94 descriptor rows');

function lineAt(source, offset) {
  return source.slice(0, offset).split(/\r?\n/).length;
}

function matchingBrace(source, openIndex) {
  let depth = 0;
  let quote = null;
  let escaped = false;
  let lineComment = false;
  let blockComment = false;

  for (let i = openIndex; i < source.length; i += 1) {
    const c = source[i];
    const n = source[i + 1];

    if (lineComment) {
      if (c === '\n') lineComment = false;
      continue;
    }
    if (blockComment) {
      if (c === '*' && n === '/') {
        blockComment = false;
        i += 1;
      }
      continue;
    }
    if (quote) {
      if (escaped) {
        escaped = false;
      } else if (c === '\\') {
        escaped = true;
      } else if (c === quote) {
        quote = null;
      }
      continue;
    }
    if (c === '/' && n === '/') {
      lineComment = true;
      i += 1;
      continue;
    }
    if (c === '/' && n === '*') {
      blockComment = true;
      i += 1;
      continue;
    }
    if (c === '"' || c === '\'') {
      quote = c;
      continue;
    }
    if (c === '{') depth += 1;
    if (c === '}') {
      depth -= 1;
      if (depth === 0) return i;
    }
  }
  return -1;
}

function unique(values) {
  return [...new Set(values)].sort((a, b) => a.localeCompare(b));
}

function extractBranches(source) {
  const rows = [];
  const pattern = /if\s*\(\s*strcmp\s*\(\s*action\s*,\s*"([^"]+)"\s*\)\s*==\s*0\s*\)\s*\{/g;
  for (const match of source.matchAll(pattern)) {
    const openIndex = match.index + match[0].lastIndexOf('{');
    const closeIndex = matchingBrace(source, openIndex);
    assert(closeIndex > openIndex, `unclosed action branch for ${match[1]}`);
    const body = source.slice(openIndex + 1, closeIndex);
    const inputFields = [];
    const inputPattern = /cJSON_GetObjectItem(?:CaseSensitive)?\s*\(\s*[^,]+,\s*"([^"]+)"\s*\)/g;
    for (const field of body.matchAll(inputPattern)) inputFields.push(field[1]);
    const responseFields = [];
    const responsePattern = /cJSON_Add[A-Za-z0-9_]*ToObject\s*\(\s*[^,]+,\s*"([^"]+)"/g;
    for (const field of body.matchAll(responsePattern)) responseFields.push(field[1]);
    const helperCalls = [];
    const helperPattern = /\b(?:adc|battery|broker|config|hms|modbus|mqtt|network|ops|ota|sms|status|system)_[A-Za-z0-9_]+\s*\(/g;
    for (const call of body.matchAll(helperPattern)) {
      helperCalls.push(call[0].replace(/\s*\($/, ''));
    }
    rows.push({
      action: match[1],
      sourceLine: lineAt(source, match.index),
      inputFieldReferences: unique(inputFields),
      responseFieldReferences: unique(responseFields),
      helperCalls: unique(helperCalls),
      branchSha256: crypto.createHash('sha256').update(body).digest('hex'),
    });
  }
  return rows;
}

const handlerCache = new Map();
const outputRows = [];
const missing = [];
const duplicateBranches = [];

for (const row of matrix.rows) {
  const handlerPath = row.handler;
  let source = handlerCache.get(handlerPath);
  if (!source) {
    source = fs.readFileSync(handlerPath, 'utf8');
    handlerCache.set(handlerPath, source);
  }
  const branches = extractBranches(source).filter((branch) => branch.action === row.action);
  if (branches.length === 0) missing.push(`${row.type}.${row.action}`);
  if (branches.length > 1) duplicateBranches.push(`${row.type}.${row.action}`);
  const branch = branches[0] || null;
  outputRows.push({
    type: row.type,
    action: row.action,
    handlerSource: handlerPath,
    handlerSourceHash: crypto.createHash('sha256').update(source).digest('hex'),
    branch: branch ? {
      ...branch,
      evidence: 'SOURCE_PROVEN_STATIC_HANDLER_BRANCH_AND_FIELD_REFERENCES',
    } : null,
    requestSchema: 'DEFERRED: static field references do not prove requiredness, type, nesting, or accepted range',
    responseSchema: 'DEFERRED: static field references do not prove complete response shape or error variants',
    sideEffects: 'DEFERRED: helper references do not prove runtime side effects or durability',
    buildCondition: 'DEFERRED: source branch presence does not prove build/runtime availability',
    limitation: 'This fixture is source evidence only; it is not an end-to-end firmware execution test.',
  });
}

assert.equal(missing.length, 0, `missing handler action branches: ${missing.join(', ')}`);
assert.equal(duplicateBranches.length, 0, `duplicate handler action branches: ${duplicateBranches.join(', ')}`);
assert.equal(outputRows.length, matrix.rows.length);

const output = {
  evidence: 'CONTRACT_TESTED',
  evidenceClass: 'SOURCE_PROVEN_STATIC_HANDLER_REFERENCES',
  sourceRoot: resolvedRoot,
  matrixSource: path.resolve(matrixPath),
  rowCount: outputRows.length,
  handlerFileCount: handlerCache.size,
  rows: outputRows,
  limitations: [
    'Does not prove cJSON type checks, requiredness, nesting, ranges, or default values.',
    'Does not prove helper side effects, persistence, build conditions, or runtime readiness.',
    'Does not define server authorization policy or a canonical MQTT publish leaf.',
    'Actual firmware execution and negative-path tests remain implementation/release gates.',
  ],
};

fs.mkdirSync(path.dirname(outputPath), { recursive: true });
fs.writeFileSync(outputPath, `${JSON.stringify(output, null, 2)}\n`);
console.log(JSON.stringify({
  evidence: output.evidence,
  evidenceClass: output.evidenceClass,
  rowCount: output.rowCount,
  handlerFileCount: output.handlerFileCount,
  outputPath: path.resolve(outputPath),
}, null, 2));

