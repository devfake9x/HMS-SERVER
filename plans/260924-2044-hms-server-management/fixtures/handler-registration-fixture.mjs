import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';

const idfRoot = process.argv[2];
const matrixPath = process.argv[3] || path.resolve(
  'plans/260924-2044-hms-server-management/fixtures/command-evidence-matrix.json'
);
const outputPath = process.argv[4] || path.resolve(
  'plans/260924-2044-hms-server-management/fixtures/handler-registration-baseline.json'
);
assert(idfRoot, 'usage: node handler-registration-fixture.mjs <HMS-IDF-root> [matrix] [output]');

const matrix = JSON.parse(fs.readFileSync(matrixPath, 'utf8'));
assert(Array.isArray(matrix.rows) && matrix.rows.length > 0, 'matrix rows are required');

const mappings = {
  system: {
    source: 'components/mqtt-cmd-handlers/src/cmd_system.c',
    registration: 'cmd_dispatcher_register("system"',
  },
  config: {
    source: 'components/mqtt-cmd-handlers/src/cmd_config.c',
    registration: 'cmd_dispatcher_register("config"',
  },
  adc: {
    source: 'components/mqtt-cmd-handlers/src/cmd_adc.c',
    registration: 'cmd_dispatcher_register("adc"',
  },
  network: {
    source: 'components/mqtt-cmd-handlers/src/cmd_network.c',
    registration: 'cmd_dispatcher_register("network"',
  },
  mqtt: {
    source: 'components/mqtt-cmd-handlers/src/cmd_mqtt.c',
    registration: 'cmd_dispatcher_register("mqtt"',
  },
  broker: {
    source: 'components/mqtt-cmd-handlers/src/cmd_broker.c',
    registration: 'cmd_dispatcher_register("broker"',
  },
  status: {
    source: 'components/mqtt-cmd-handlers/src/cmd_status.c',
    registration: 'cmd_dispatcher_register("status"',
  },
  battery: {
    source: 'components/mqtt-cmd-handlers/src/cmd_battery.c',
    registration: 'cmd_dispatcher_register("battery"',
  },
  modbus: {
    source: 'components/mqtt-cmd-handlers/src/cmd_modbus.c',
    registration: 'cmd_dispatcher_register("modbus"',
  },
  ota: {
    source: 'components/mqtt-cmd-handlers/src/cmd_ota.c',
    registration: 'cmd_dispatcher_register("ota"',
  },
  sms: {
    source: 'components/mqtt-cmd-handlers/src/cmd_sms.c',
    registration: 'cmd_dispatcher_register("sms"',
  },
  control: {
    source: 'services/do-service/do_service.c',
    registration: 'cmd_dispatcher_register_ctx("control"',
  },
};

const read = (relative) => {
  const absolute = path.join(idfRoot, relative);
  assert(fs.existsSync(absolute), `missing handler source: ${absolute}`);
  return { absolute, text: fs.readFileSync(absolute, 'utf8') };
};

const handlerCache = new Map();
const rows = matrix.rows.map((row) => {
  const mapping = mappings[row.type];
  assert(mapping, `no registration mapping for command namespace: ${row.type}`);
  if (!handlerCache.has(row.type)) handlerCache.set(row.type, read(mapping.source));
  const source = handlerCache.get(row.type);
  const registered = source.text.includes(mapping.registration);
  assert(registered, `registration anchor missing for ${row.type}`);
  const actionLiteralMatches = [
    `"${row.action}"`,
    `'${row.action}'`,
  ].filter((literal) => source.text.includes(literal)).length;
  const actionStatus = actionLiteralMatches > 0
    ? 'SOURCE_PROVEN_NAMESPACE_AND_ACTION_LITERAL'
    : 'SOURCE_PROVEN_NAMESPACE_ONLY';
  return {
    type: row.type,
    action: row.action,
    handlerSource: path.join(idfRoot, mapping.source),
    registrationAnchor: mapping.registration,
    registered,
    actionLiteralMatches,
    evidenceStatus: actionStatus,
    unresolved: [
      'build/runtime condition is not inferred from registration alone',
      'request/response schema and side effects require action-level source review',
      'server policy remains outside firmware descriptor/handler registration',
    ],
  };
});

const counts = rows.reduce((acc, row) => {
  acc[row.evidenceStatus] = (acc[row.evidenceStatus] || 0) + 1;
  return acc;
}, {});

const result = {
  evidence: 'CONTRACT_TESTED',
  source: 'HMS-IDF handler registration call paths',
  mappingCount: rows.length,
  namespaceCount: Object.keys(mappings).length,
  counts,
  limitation: 'This fixture proves namespace registration and action string presence only; it does not prove build conditions, payload schemas, response schemas, runtime capability, or end-to-end execution.',
  rows,
};

fs.writeFileSync(outputPath, `${JSON.stringify(result, null, 2)}\n`, 'utf8');
console.log(JSON.stringify({
  evidence: result.evidence,
  mappingCount: result.mappingCount,
  namespaceCount: result.namespaceCount,
  counts,
  outputPath,
  limitation: result.limitation,
}, null, 2));
