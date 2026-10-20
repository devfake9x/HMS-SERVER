import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';

const root = process.cwd();
const entrypoints = ['ALIGNMENT.md', 'brainstorm-report.md', 'research-report.md', 'docs/journal.md', 'docs/architecture-hms-server.md'];
const stale = /Node\/Fastify|Fastify|TimescaleDB|Redis Streams|React|SCADA|digital twin|HKDF|master-secret|ARCHITECTURE READY|FROZEN|100% Protocol & Data-Plane Alignment/i;
const results = [];
for (const relative of entrypoints) {
  const file = path.join(root, relative);
  assert(fs.existsSync(file), `missing documentation entrypoint: ${relative}`);
  const text = fs.readFileSync(file, 'utf8');
  const head = text.split(/\r?\n/).slice(0, 25).join('\n');
  const historical = /SUPERSEDED|HISTORICAL/i.test(head);
  const misleadingCurrentMarker = /ARCHITECTURE READY|FROZEN|100% Protocol & Data-Plane Alignment/i.test(head);
  assert(historical, `${relative} lacks a historical/superseded banner`);
  assert(!misleadingCurrentMarker, `${relative} has a misleading current marker in its banner`);
  results.push({ file: relative, historical, staleTermsPresent: stale.test(text), status: 'PASS_HISTORICAL_BANNER' });
}
console.log(JSON.stringify({
  evidence: 'CONTRACT_TESTED',
  status: 'PASS_HISTORICAL_BANNERS',
  results,
  limitation: 'This pre-cook script does not prove implementation documentation is drift-free; Phase 8 must scan generated/current docs after cook.',
}, null, 2));
