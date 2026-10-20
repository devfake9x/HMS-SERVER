import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';

const idfRoot = process.argv[2];
const buildDir = process.argv[3] || 'build';
const baselinePath = process.argv[4] || path.resolve(
  'plans/260924-2044-hms-server-management/fixtures/catalog-baseline.json'
);
const outputPath = process.argv[5] || path.resolve(
  'plans/260924-2044-hms-server-management/fixtures/catalog-build-artifact-report.json'
);
assert(idfRoot, 'usage: node catalog-build-artifact-fixture.mjs <HMS-IDF-root> [build-dir] [baseline] [output]');

const buildRoot = path.isAbsolute(buildDir) ? buildDir : path.join(idfRoot, buildDir);
const headPath = path.join(buildRoot, 'CMakeFiles', 'git-data', 'HEAD');
const objectPath = path.join(
  buildRoot,
  'esp-idf',
  'command-dispatcher',
  'CMakeFiles',
  '__idf_command-dispatcher.dir',
  'cmd_descriptor.c.obj'
);
assert(fs.existsSync(headPath), `missing build revision marker: ${headPath}`);
assert(fs.existsSync(objectPath), `missing descriptor object: ${objectPath}`);

const baseline = JSON.parse(fs.readFileSync(baselinePath, 'utf8'));
assert(Array.isArray(baseline.commands) && baseline.commands.length > 0, 'baseline commands are required');
const buildRevision = fs.readFileSync(headPath, 'utf8').trim();
const objectBytes = fs.readFileSync(objectPath);
const objectText = objectBytes.toString('ascii');

const rows = baseline.commands.map(({ type, action }) => {
  const typePresent = objectText.includes(type);
  const actionPresent = objectText.includes(action);
  return {
    type,
    action,
    typeStringPresent: typePresent,
    actionStringPresent: actionPresent,
    evidenceStatus: typePresent && actionPresent
      ? 'BUILD_ARTIFACT_STRING_PRESENT'
      : 'BUILD_ARTIFACT_STRING_MISSING',
  };
});

const missing = rows.filter((row) => row.evidenceStatus.endsWith('MISSING'));
const result = {
  evidence: 'CONTRACT_TESTED',
  buildRoot,
  buildRevision,
  objectPath,
  objectSha256: (await import('node:crypto')).createHash('sha256').update(objectBytes).digest('hex'),
  rowCount: rows.length,
  presentCount: rows.length - missing.length,
  missingCount: missing.length,
  status: missing.length === 0
    ? 'BUILD_ARTIFACT_STRING_SET_MATCHES'
    : 'BUILD_ARTIFACT_STRING_SET_DRIFT_DETECTED',
  limitation: 'Object string presence is not a full descriptor pair/order/schema proof. A clean match would still require a reproducible build manifest or an exported catalog from that build.',
  rows,
};

fs.writeFileSync(outputPath, `${JSON.stringify(result, null, 2)}\n`, 'utf8');
console.log(JSON.stringify({
  evidence: result.evidence,
  buildRevision: result.buildRevision,
  objectSha256: result.objectSha256,
  rowCount: result.rowCount,
  presentCount: result.presentCount,
  missingCount: result.missingCount,
  status: result.status,
  outputPath,
  limitation: result.limitation,
}, null, 2));
