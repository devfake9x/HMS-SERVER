const mqtt = require('mqtt');

const brokerUrl = 'mqtt://broker.emqx.io:1883';
const client = mqtt.connect(brokerUrl, {
  clientId: 'hms_server_listener_' + Math.random().toString(16).substring(2, 8),
  clean: true,
  connectTimeout: 10000
});

client.on('connect', () => {
  console.log(`[HMS INGESTION LISTENER] Connected to broker ${brokerUrl}`);
  client.subscribe(['hms_toan_lab/#', 'hms/#'], (err) => {
    if (err) {
      console.error('[HMS INGESTION LISTENER] Subscribe error:', err);
    } else {
      console.log('[HMS INGESTION LISTENER] Subscribed successfully to hms_toan_lab/# and hms/#');
    }
  });
});

client.on('message', (topic, payload) => {
  console.log(`\n\x1b[32m[TELEMETRY RECEIVED]\x1b[0m Topic: \x1b[33m${topic}\x1b[0m`);
  try {
    const json = JSON.parse(payload.toString());
    console.log(JSON.stringify(json, null, 2));
  } catch {
    console.log(payload.toString());
  }
});

client.on('error', (err) => {
  console.error('[HMS INGESTION LISTENER] MQTT error:', err);
});
