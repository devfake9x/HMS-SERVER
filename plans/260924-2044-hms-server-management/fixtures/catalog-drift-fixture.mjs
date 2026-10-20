import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';

const baselinePath = process.argv[2];
const descriptorPath = process.argv[3];
assert(baselinePath && descriptorPath, 'usage: node catalog-drift-fixture.mjs <baseline.json> <cmd_descriptor.c>');

const baseline = JSON.parse(fs.readFileSync(baselinePath, 'utf8'));
const source = fs.readFileSync(descriptorPath, 'utf8');
const rows = [];
for (const match of source.matchAll(/ROW_(?:READ|MUT|DOMAIN)\s*\(\s*"([^"]+)"\s*,\s*"([^"]+)"/g)) {
  rows.push({ type: match[1], action: match[2] });
}
for (const match of source.matchAll(/\.type\s*=\s*"([^"]+)"\s*,\s*\.action\s*=\s*"([^"]+)"/g)) {
  rows.push({ type: match[1], action: match[2] });
}
const candidate = [...new Map(rows.map((x) => [`${x.type}\u0000${x.action}`, x])).values()]
  .sort((a, b) => `${a.type}\u0000${a.action}`.localeCompare(`${b.type}\u0000${b.action}`));
const canonical = (items) => JSON.stringify(items);
const hash = (items) => crypto.createHash('sha256').update(canonical(items)).digest('hex');
const compare = (left, right) => {
  const l = new Set(left.map((x) => `${x.type}\u0000${x.action}`));
  const r = new Set(right.map((x) => `${x.type}\u0000${x.action}`));
  return {
    added: [...r].filter((x) => !l.has(x)),
    removed: [...l].filter((x) => !r.has(x)),
    same: canonical(left) === canonical(right),
  };
};

const current = compare(baseline.commands, candidate);
const synthetic = [...candidate, { type: 'fixture', action: 'synthetic_drift' }]
  .sort((a, b) => `${a.type}\u0000${a.action}`.localeCompare(`${b.type}\u0000${b.action}`));
const syntheticDiff = compare(candidate, synthetic);
assert.equal(current.same, true, 'current source must match the pinned baseline before cook');
assert.equal(syntheticDiff.same, false, 'drift fixture must detect an added action');

console.log(JSON.stringify({
  evidence: 'CONTRACT_TESTED',
  baseline: { path: path.resolve(baselinePath), catalogVersion: baseline.catalogVersion, catalogHash: hash(baseline.commands) },
  candidate: { path: path.resolve(descriptorPath), catalogHash: hash(candidate) },
  currentComparison: current,
  syntheticDriftComparison: syntheticDiff,
  policy: 'Unknown or changed catalog hash denies command execution; no silent fallback.',
  limitation: 'The pinned baseline is a dated source snapshot; a second firmware build manifest is still required for release compatibility proof.',
}, null, 2));
