import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';

const root = process.argv[2];
assert(root, 'usage: node framed-command-fixture.mjs <HMS-IDF-root>');

const read = (...parts) => fs.readFileSync(path.join(root, ...parts), 'utf8');
const frameHeader = read('services', 'mqtt-service', 'cmd_frame.h');
const mqttService = read('services', 'mqtt-service', 'mqtt_service.c');
const descriptor = read('components', 'command-dispatcher', 'cmd_descriptor.c');
const dispatcher = read('components', 'command-dispatcher', 'command_dispatcher.c');
const security = read('components', 'command-dispatcher', 'cmd_security.c');

const numberDefine = (source, name) => {
  const match = source.match(new RegExp(`#define\\s+${name}\\s+(\\d+)`));
  assert(match, `missing ${name}`);
  return Number(match[1]);
};

const frameMax = numberDefine(frameHeader, 'CMD_FRAME_MAX');
const headerMax = numberDefine(frameHeader, 'CMD_FRAME_HEADER_MAX');
const deadlineMs = numberDefine(frameHeader, 'CMD_FRAME_DEADLINE_MS');
const commandDataMax = numberDefine(mqttService, 'CMD_DATA_MAX');
const signableMax = numberDefine(security, 'CMD_SIGNABLE_MAX');

assert.equal(frameMax, 131072);
assert.equal(headerMax, 1023);
assert.equal(deadlineMs, 15000);
assert.equal(commandDataMax, 1024);
assert.equal(signableMax, 1024);
assert(mqttService.includes('if (data_len >= CMD_DATA_MAX)'));
assert(mqttService.includes('cmd_frame_classify((const char *)data, data_len, NULL)'));
assert(mqttService.includes('cmd_dispatcher_dispatch_json_with_body'));
assert(descriptor.includes('.action = "check_rtu_config"'));
assert(descriptor.includes('.action = "replace_rtu_config"'));
assert(descriptor.includes('.accepts_body = true'));
assert(dispatcher.includes('cmd_dispatcher_current_body(NULL, NULL)'));

console.log(JSON.stringify({
  evidence: 'CONTRACT_TESTED',
  normalEnvelope: {
    maxAcceptedRawBytes: commandDataMax - 1,
    securitySignableExclusiveLimit: signableMax,
    source: [
      'services/mqtt-service/mqtt_service.c',
      'components/command-dispatcher/cmd_security.c',
    ],
  },
  framedCommand: {
    maxWholeMessageBytes: frameMax,
    maxHeaderLineBytes: headerMax,
    headerIncludesNewline: true,
    idleDeadlineMs: deadlineMs,
    bodyActions: [
      'modbus.check_rtu_config',
      'modbus.replace_rtu_config',
    ],
    source: [
      'services/mqtt-service/cmd_frame.h',
      'services/mqtt-service/cmd_frame.c',
      'services/mqtt-service/mqtt_service.c',
      'components/command-dispatcher/cmd_descriptor.c',
      'components/command-dispatcher/command_dispatcher.c',
    ],
  },
  policy: 'The signed JSON command header remains limited to 1..1023 bytes. A framed command may carry a separate body up to 131072 total bytes only when its descriptor accepts_body=true; all other rows reject a lent body.',
  limitation: 'This is source-anchored evidence, not an on-device fragmentation or broker receive-buffer test.',
}, null, 2));
