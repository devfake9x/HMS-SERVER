// Superseded by scripts/generate-command-catalog.mjs (catalog v2); kept as a historical fixture.
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';

const root = process.argv[2];
assert(root, 'usage: node catalog-fixture.mjs <HMS-IDF-root>');

const descriptorPath = path.join(
  root,
  'components',
  'command-dispatcher',
  'cmd_descriptor.c',
);
const source = fs.readFileSync(descriptorPath, 'utf8');

const entries = [];
const macroPattern = /ROW_(?:READ|MUT|DOMAIN)\s*\(\s*"([^"]+)"\s*,\s*"([^"]+)"/g;
for (const match of source.matchAll(macroPattern)) {
  entries.push({ type: match[1], action: match[2] });
}

const literalPattern = /\.type\s*=\s*"([^"]+)"\s*,\s*\.action\s*=\s*"([^"]+)"/g;
for (const match of source.matchAll(literalPattern)) {
  entries.push({ type: match[1], action: match[2] });
}

const catalog = [...new Map(
  entries.map((entry) => [`${entry.type}\u0000${entry.action}`, entry]),
).values()].sort((a, b) =>
  `${a.type}\u0000${a.action}`.localeCompare(`${b.type}\u0000${b.action}`),
);

assert(catalog.length > 0, 'descriptor extraction produced no commands');
assert(catalog.some((x) => x.type === 'system' && x.action === 'reboot'));
assert(catalog.some((x) => x.type === 'mqtt' && x.action === 'get_status'));
assert(!source.includes('"kid"'), 'kid must not become a firmware command field');
assert(!source.includes('"generation"'), 'generation must not become a firmware command field');

const canonical = JSON.stringify(catalog);
const catalogHash = crypto.createHash('sha256').update(canonical).digest('hex');
const sourceHash = crypto.createHash('sha256').update(source).digest('hex');

console.log(JSON.stringify({
  evidence: 'CONTRACT_TESTED',
  source: descriptorPath,
  sourceHash,
  catalogVersion: 'hms-idf-descriptor-local',
  catalogHash,
  commandCount: catalog.length,
  commands: catalog,
}, null, 2));
