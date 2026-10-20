import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';

const root = process.argv[2];
assert(root, 'usage: node mqtt-publish-path-evidence-fixture.mjs <HMS-IDF-root>');

const files = {
  service: path.join(root, 'services', 'mqtt-service', 'mqtt_service.c'),
  response: path.join(root, 'components', 'command-dispatcher', 'cmd_response.c'),
  responseHeader: path.join(root, 'components', 'command-dispatcher', 'include', 'cmd_response.h'),
  publish: path.join(root, 'components', 'mqtt-message-publish', 'src', 'mqtt_message_publish.c'),
  publishHeader: path.join(root, 'components', 'mqtt-message-publish', 'include', 'mqtt_message_publish.h'),
};

const source = Object.fromEntries(
  Object.entries(files).map(([name, file]) => [name, fs.readFileSync(file, 'utf8')]),
);

// The service owns the response topic; the generic publisher only accepts a
// caller-supplied topic and therefore cannot prove a command leaf.
assert(source.service.includes('identity->device_id, "/cmd/#"'));
assert(source.service.includes('identity->device_id, "/cmd/res"'));
assert(source.service.includes('cmd_response_init(mqtt_svc_publish, s_res_topic)'));
assert(source.service.includes('static bool mqtt_topic_is_response'));
assert(source.service.includes('if (mqtt_topic_is_response(topic))'));
assert(source.response.includes('override_topic ? override_topic : s_res_topic'));
assert(source.response.includes('publish_fn(response_topic, json, strlen(json), 1, false)'));
assert(source.responseHeader.includes('cmd_response_init(cmd_publish_fn_t publish_fn'));
assert(source.publish.includes('mqtt_manager_publish(to_broker(target)'));
assert(source.publish.includes('if (req->retain) return false'));
assert(source.publish.includes("if (t[i] == '#' || t[i] == '+') return false"));
assert(source.publishHeader.includes('topic[MQTT_MSG_TOPIC_MAX]'));
assert(source.publishHeader.includes('target;'));

function sha256(text) {
  return crypto.createHash('sha256').update(text).digest('hex');
}

const result = {
  evidence: 'CONTRACT_TESTED',
  commandIngress: {
    subscriptionFilter: '<prefix>/<device_id>/cmd/#',
    responseSuffixExcluded: '/cmd/res',
    canonicalServerLeaf: null,
    status: 'NEW_FIRMWARE_CONTRACT',
    reason: 'The firmware forwards every non-response topic under the wildcard filter and does not select one server command leaf.',
  },
  commandResponse: {
    defaultTopic: '<prefix>/<device_id>/cmd/res',
    qos: 1,
    retain: false,
    override: 'Accepted only through the firmware response_topic validator; server must not use an unvalidated override.',
    source: [files.service, files.response, files.responseHeader],
  },
  genericOutboundPublisher: {
    role: 'Explicit-topic transport service, not command-leaf selection.',
    acceptsPrimaryOrSecondaryTarget: true,
    serverRelease1Policy: 'PRIMARY_ONLY; server rejects SECONDARY_ONLY, BOTH and FAILOVER until a separate fixture-backed contract passes.',
    rejectsWildcardPublishTopic: true,
    rejectsRetainedMessagesInV1: true,
    source: [files.publish, files.publishHeader],
    limitation: 'This API proves outbound validation only. It does not prove that any particular server leaf reaches the command dispatcher.',
  },
  sourceDigests: Object.fromEntries(
    Object.entries(source).map(([name, text]) => [name, sha256(text)]),
  ),
};

console.log(JSON.stringify(result, null, 2));
