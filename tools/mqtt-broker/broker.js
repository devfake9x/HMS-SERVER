const { Aedes } = require('aedes');
const net = require('net');

const aedes = new Aedes();
const server = net.createServer((socket) => {
  console.log(`[RAW TCP] Inbound socket from ${socket.remoteAddress}:${socket.remotePort}`);
  aedes.handle(socket);
});
const port = 8088;

aedes.on('client', function (client) {
  console.log(`[MQTT BROKER] Client Connected: \x1b[32m${client ? client.id : client}\x1b[0m`);
});

aedes.on('clientError', function (client, err) {
  console.log(`[MQTT BROKER] Client Error [${client ? client.id : 'unknown'}]:`, err.message);
});

aedes.on('connectionError', function (client, err) {
  console.log(`[MQTT BROKER] Connection Error [${client ? client.id : 'unknown'}]:`, err.message);
});

aedes.on('clientDisconnect', function (client) {
  console.log(`[MQTT BROKER] Client Disconnected: \x1b[31m${client ? client.id : client}\x1b[0m`);
});

aedes.on('publish', function (packet, client) {
  if (client) {
    console.log(`[MQTT BROKER] Publish from [${client.id}] on topic [${packet.topic}]:`, packet.payload.toString());
  }
});

aedes.on('subscribe', function (subscriptions, client) {
  if (client) {
    console.log(`[MQTT BROKER] Client [${client.id}] subscribed to:`, subscriptions.map(s => s.topic).join(', '));
  }
});

server.listen(port, '0.0.0.0', function () {
  console.log(`[MQTT BROKER] Server successfully listening on 0.0.0.0:${port}`);
});
