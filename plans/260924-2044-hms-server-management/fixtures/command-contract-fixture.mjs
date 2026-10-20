import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';

const root = process.argv[2];
const outputPath = process.argv[3] || path.join(
  'plans',
  '260924-2044-hms-server-management',
  'fixtures',
  'command-evidence-matrix.json',
);
assert(root, 'usage: node command-contract-fixture.mjs <HMS-IDF-root> [output]');

const descriptorPath = path.join(
  root,
  'components',
  'command-dispatcher',
  'cmd_descriptor.c',
);
const registrationPath = path.join(
  process.cwd(),
  'plans',
  '260924-2044-hms-server-management',
  'fixtures',
  'handler-registration-baseline.json',
);
const source = fs.readFileSync(descriptorPath, 'utf8');
const registration = JSON.parse(fs.readFileSync(registrationPath, 'utf8'));

// Replace comments with whitespace so macro and struct scans cannot read
// examples in comments while preserving source offsets for line anchors.
const code = source
  .replace(/\/\*[\s\S]*?\*\//g, (match) => match.replace(/[^\r\n]/g, ' '))
  .replace(/\/\/[^\r\n]*/g, (match) => match.replace(/[^\r\n]/g, ' '));

const lineAt = (offset) => source.slice(0, offset).split(/\r?\n/).length;
const cleanToken = (value) => value == null
  ? null
  : value.trim().replace(/^"|"$/g, '');
const rows = new Map();

function addRow(row) {
  const key = `${row.type}\u0000${row.action}`;
  assert(!rows.has(key), `duplicate descriptor row: ${key}`);
  rows.set(key, row);
}

const macroPattern = /ROW_(READ|MUT|DOMAIN)\s*\(\s*"([^"]+)"\s*,\s*"([^"]+)"\s*(?:,\s*([^,\)]+))?\s*(?:,\s*"([^"]+)")?\s*\)/g;
for (const match of code.matchAll(macroPattern)) {
  const kind = match[1];
  const type = match[2];
  const action = match[3];
  const className = kind === 'READ' ? 'CMD_CLASS_R' : cleanToken(match[4]);
  const permission = kind === 'READ' ? cleanToken(match[4])
    : kind === 'DOMAIN' ? null : cleanToken(match[5]);
  addRow({
    type,
    action,
    descriptorForm: `ROW_${kind}`,
    descriptorLine: lineAt(match.index),
    klass: className.replace('CMD_CLASS_', ''),
    requiredPermission: permission,
    domainPolicy: kind === 'DOMAIN',
    confirmRequired: false,
    confirmTemplate: null,
    acceptsBody: false,
    defaultTtlMs: 0,
    dryRunSupported: false,
    auditPublish: kind === 'MUT',
    auditExempt: false,
    outcomeDeferred: false,
  });
}

const literalPattern = /\{\s*\.type\s*=\s*"([^"]+)"\s*,\s*\.action\s*=\s*"([^"]+)"([\s\S]*?)\n\s*\},/g;
for (const match of code.matchAll(literalPattern)) {
  const body = match[3];
  const field = (name) => body.match(new RegExp(`\\.${name}\\s*=\\s*([^,\\n]+)`))?.[1]?.trim();
  const stringField = (name) => body.match(new RegExp(`\\.${name}\\s*=\\s*"([^"]*)"`))?.[1] ?? null;
  const boolField = (name) => field(name) === 'true';
  const klass = field('klass');
  addRow({
    type: match[1],
    action: match[2],
    descriptorForm: 'explicit-struct',
    descriptorLine: lineAt(match.index),
    klass: klass?.replace('CMD_CLASS_', '') ?? null,
    requiredPermission: stringField('required_perm'),
    domainPolicy: boolField('domain_policy'),
    confirmRequired: boolField('confirm_required'),
    confirmTemplate: stringField('confirm_template'),
    acceptsBody: boolField('accepts_body'),
    defaultTtlMs: Number(field('default_ttl_ms') || 0),
    dryRunSupported: boolField('dry_run_supported'),
    auditPublish: boolField('audit_publish'),
    auditExempt: boolField('audit_exempt'),
    outcomeDeferred: boolField('outcome_deferred'),
  });
}

assert.equal(rows.size, 94, `expected 94 descriptor rows, got ${rows.size}`);
const registrationByKey = new Map(
  registration.rows.map((row) => [`${row.type}\u0000${row.action}`, row]),
);
assert.equal(registrationByKey.size, rows.size, 'registration and descriptor counts differ');

const resultRows = [...rows.values()]
  .sort((a, b) => `${a.type}\u0000${a.action}`.localeCompare(`${b.type}\u0000${b.action}`))
  .map((descriptor) => {
    const key = `${descriptor.type}\u0000${descriptor.action}`;
    const registered = registrationByKey.get(key);
    assert(registered, `missing registration row: ${key}`);
    return {
      type: descriptor.type,
      action: descriptor.action,
      descriptorSource: descriptorPath,
      descriptorLine: descriptor.descriptorLine,
      descriptorForm: descriptor.descriptorForm,
      handler: registered.handlerSource,
      registrationAnchor: registered.registrationAnchor,
      registered: registered.registered,
      handlerActionLiteralMatches: registered.actionLiteralMatches,
      buildCondition: 'DEFERRED_TO_PHASE_2/3 - REQUIRED',
      requestSchema: 'DEFERRED_TO_PHASE_1/3 - ACTION_SOURCE_REVIEW_REQUIRED',
      responseSchema: 'DEFERRED_TO_PHASE_1/3 - ACTION_SOURCE_REVIEW_REQUIRED',
      limits: {
        envelope: 'SOURCE_PROVEN; see command-golden-vector.mjs',
        actionSpecific: 'DEFERRED_TO_PHASE_1/3 - ACTION_SOURCE_REVIEW_REQUIRED',
      },
      sideEffects: 'DEFERRED_TO_PHASE_1/3 - ACTION_SOURCE_REVIEW_REQUIRED',
      klass: descriptor.klass,
      requiredPermission: descriptor.requiredPermission,
      domainPolicy: descriptor.domainPolicy,
      confirmRequired: descriptor.confirmRequired,
      confirmTemplate: descriptor.confirmTemplate,
      acceptsBody: descriptor.acceptsBody,
      defaultTtlMs: descriptor.defaultTtlMs,
      dryRunSupported: descriptor.dryRunSupported,
      auditPublish: descriptor.auditPublish,
      auditExempt: descriptor.auditExempt,
      outcomeDeferred: descriptor.outcomeDeferred,
      dangerousPolicy: descriptor.confirmRequired
        ? 'SOURCE_PROVEN_CONFIRM_REQUIRED'
        : 'DEFERRED_TO_PHASE_2/3 - SERVER_POLICY_REQUIRED',
      serverPolicy: 'DEFERRED_TO_PHASE_2/3 - SERVER_CATALOG_REQUIRED',
      fixture: [
        'command-contract-fixture.mjs',
        'handler-registration-fixture.mjs',
        'command-golden-vector.mjs',
      ],
      evidenceStatus: 'SOURCE_PROVEN_DESCRIPTOR_POLICY_AND_HANDLER_REGISTRATION',
      unresolvedRisk: [
        'payload/response schema is not inferred from descriptor metadata',
        'build/runtime condition is not inferred from registration alone',
        'side effects and actual runtime readiness require action-level review',
        'server authorization policy remains outside firmware',
      ],
    };
  });

for (const row of resultRows) {
  if (row.domainPolicy) assert.equal(row.requiredPermission, null);
  else assert(row.requiredPermission, `${row.type}.${row.action} has no permission`);
}
assert.deepEqual(
  resultRows.filter((row) => row.acceptsBody).map((row) => `${row.type}.${row.action}`),
  ['modbus.check_rtu_config', 'modbus.replace_rtu_config'],
);

const catalog = resultRows.map(({ type, action }) => ({ type, action }));
const catalogHash = crypto.createHash('sha256')
  .update(JSON.stringify(catalog))
  .digest('hex');
const output = {
  evidence: 'CONTRACT_TESTED',
  source: descriptorPath,
  sourceHash: crypto.createHash('sha256').update(source).digest('hex'),
  catalogVersion: 'hms-idf-descriptor-local-2026-09-25',
  catalogHash,
  canonicalization: 'sorted unique JSON rows [{type,action}] UTF-8 SHA-256',
  rowCount: resultRows.length,
  mappedHandlerRows: resultRows.filter((row) => row.registered).length,
  unresolvedActionLevelRows: resultRows.length,
  limitation: 'Descriptor policy and namespace/action registration are source-proven; action payload/response schemas, build conditions, side effects, runtime readiness, and server policy remain deferred.',
  rows: resultRows,
};

fs.mkdirSync(path.dirname(outputPath), { recursive: true });
fs.writeFileSync(outputPath, `${JSON.stringify(output, null, 2)}\n`);
console.log(JSON.stringify({
  evidence: output.evidence,
  rowCount: output.rowCount,
  mappedHandlerRows: output.mappedHandlerRows,
  unresolvedActionLevelRows: output.unresolvedActionLevelRows,
  catalogHash: output.catalogHash,
  outputPath: path.resolve(outputPath),
}, null, 2));
