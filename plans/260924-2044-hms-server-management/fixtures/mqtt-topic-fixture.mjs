import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';

const root = process.argv[2];
assert(root, 'usage: node mqtt-topic-fixture.mjs <HMS-IDF-root>');

const mqttServicePath = path.join(root, 'services', 'mqtt-service', 'mqtt_service.c');
const configPath = path.join(root, 'components', 'mqtt-manager', 'mqtt_config.c');
const managerPath = path.join(root, 'components', 'mqtt-manager', 'mqtt_manager.c');
const statusPath = path.join(root, 'components', 'mqtt-status-plane', 'src', 'status_plane.c');
const mqttService = fs.readFileSync(mqttServicePath, 'utf8');
const config = fs.readFileSync(configPath, 'utf8');
const manager = fs.readFileSync(managerPath, 'utf8');
const status = fs.readFileSync(statusPath, 'utf8');

assert(mqttService.includes('"/cmd/#"'));
assert(mqttService.includes('"/cmd/res"'));
assert(mqttService.includes('"/audit"'));
assert(mqttService.includes('"/capabilities"'));
assert(mqttService.includes('static bool mqtt_topic_is_response'));
assert(mqttService.includes('if (mqtt_topic_is_response(topic))'));
assert(mqttService.includes('xQueueSend(s_cmd_queue, &item, 0)'));
assert(mqttService.includes('mqtt_manager_publish(MQTT_BROKER_PRIMARY'));
assert(mqttService.includes('strlen(caps_json), 1, true'));
assert(config.includes('config->mode = hms_mqtt_BrokerMode_BROKER_PRIMARY_ONLY'));
assert(config.includes('config->session.keepalive_sec = 30'));
assert(config.includes('config->session.default_qos = hms_mqtt_QosLevel_QOS_1'));
assert(config.includes('config->session.default_retain = false'));
assert(config.includes('config->session.clean_session = true'));
assert(config.includes('config->topics.lwt_qos = hms_mqtt_QosLevel_QOS_1'));
assert(config.includes('config->topics.lwt_retain = true'));
assert(status.includes('MQTT_CONNECTIVITY_ONLINE_PAYLOAD'));
assert(manager.includes('.network.disable_auto_reconnect = true'));
assert(manager.includes('.session.keepalive = s_proto_cfg.has_session ? s_proto_cfg.session.keepalive_sec : 120'));
assert(manager.includes('vTaskDelay(pdMS_TO_TICKS(1000))'));
assert(manager.includes('uint32_t backoff_ms = 2000'));
assert(manager.includes('if (backoff_ms < 30000)'));
assert(manager.includes('for (int retry = 0; retry < 20'));
assert(mqttService.indexOf('status_plane_update_topics(') <
       mqttService.indexOf('rebuild_topics_and_subscribe(broker, &identity)'));
assert(mqttService.indexOf('rebuild_topics_and_subscribe(broker, &identity)') <
       mqttService.indexOf('status_plane_on_connect(broker)'));

const result = {
  evidence: 'CONTRACT_TESTED',
  subscription: '<prefix>/<device_id>/cmd/#',
  knownTopics: [
    { suffix: '/cmd/res', qos: 1, retain: false, source: mqttServicePath },
    { suffix: '/audit', qos: 1, retain: false, source: mqttServicePath },
    { suffix: '/capabilities', qos: 1, retain: true, source: mqttServicePath },
    { suffix: '/status', qos: 1, retain: false, source: statusPath },
    { suffix: '/status/connectivity', qos: 1, retain: true, source: statusPath },
  ],
  sessionDefaults: {
    mode: 'PRIMARY_ONLY',
    keepaliveDefaults: {
      configDefaultSeconds: 30,
      clientFallbackWithoutSessionConfigSeconds: 120,
      status: 'CONFIG_PRECEDENCE_REQUIRES_RESOLVED_PROTO_FIXTURE',
    },
    qos: 1,
    retain: false,
    cleanSession: true,
    lwtQos: 1,
    lwtRetain: true,
  },
  commandIngress: {
    status: 'SOURCE_PROVEN',
    subscriptionFilter: '<prefix>/<device_id>/cmd/#',
    responseSuffixExcluded: '/cmd/res',
    forwardingRule: 'The MQTT callback forwards every non-response payload it receives to the command queue; it does not select or validate one fixed command leaf.',
    source: mqttServicePath,
    limitation: 'This proves the firmware ingress filter, not a server-owned canonical publish leaf.',
  },
  reconnect: {
    status: 'SOURCE_PROVEN',
    implementation: 'mqtt_manager disables ESP-MQTT auto-reconnect and owns a reconnect task',
    wakeDebounceSeconds: 1,
    backoffSeconds: [2, 4, 8, 16, 30],
    maxAttemptsPerCycle: 20,
    randomJitter: 'NOT_SOURCE_PROVEN',
    connectedOrdering: [
      'status_plane_update_topics',
      'rebuild_topics_and_subscribe',
      'status_plane_on_connect',
    ],
    source: [managerPath, mqttServicePath],
    limitation: 'The source contains an exponential 2/4/8/16/30-second task path, but resolved protobuf/session precedence, broker timing, SUBACK acceptance and runtime jitter still require fixtures/soak evidence.',
  },
  serverCommandPublishLeaf: {
    status: 'NEW_FIRMWARE_CONTRACT',
    value: null,
    reason: 'Firmware proves wildcard-plus-response-exclusion ingress, not a server-selected canonical publish leaf.',
  },
  forbiddenAssumption: '/cmd/execute is not current firmware proof',
};

console.log(JSON.stringify(result, null, 2));
