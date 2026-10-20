import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';

const planDir = process.argv[2] || path.join(
  process.cwd(),
  'plans',
  '260924-2044-hms-server-management',
);

const requiredSections = [
  'Tests Before',
  'Smallest seam',
  'Implementation scope',
  'Negative/regression tests',
  'Phase regression gate',
  'Input/output contract',
  'Dependencies',
  'Ownership',
  'Risk',
  'Evidence classification',
  'Release gate',
  'Definition of done',
];

const files = fs.readdirSync(planDir)
  .filter((name) => /^phase-\d{2}-.*\.md$/.test(name))
  .sort();
assert.equal(files.length, 8, `expected 8 phase files, got ${files.length}`);

const rows = files.map((file) => {
  const text = fs.readFileSync(path.join(planDir, file), 'utf8');
  const missing = requiredSections.filter((section) => !text.includes(`### ${section}`));
  assert.deepEqual(missing, [], `${file} is missing: ${missing.join(', ')}`);
  return { file, sectionCount: requiredSections.length, status: 'PASS' };
});

console.log(JSON.stringify({
  evidence: 'CONTRACT_TESTED',
  phaseCount: rows.length,
  requiredSectionCount: requiredSections.length,
  rows,
}, null, 2));
