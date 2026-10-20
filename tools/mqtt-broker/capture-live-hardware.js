const WebSocket = require('ws');
const fs = require('fs');
const path = require('path');
const http = require('http');

const ARTIFACT_DIR = 'C:/Users/toann/.gemini/antigravity/brain/ebb00cfc-bcd4-439d-b063-ce9916bf5e54';
const DEVICE_UUID = '12133938-d604-4386-a58c-b454a5ef7ae7';
const TARGET_URL = `http://127.0.0.1:5173/devices/${DEVICE_UUID}`;

function getTabs() {
  return new Promise((resolve, reject) => {
    http.get('http://[::1]:9222/json', (res) => {
      let d = '';
      res.on('data', c => d += c);
      res.on('end', () => resolve(JSON.parse(d)));
    }).on('error', reject);
  });
}

function createCDP(wsUrl) {
  const ws = new WebSocket(wsUrl, { headers: { Host: 'localhost:9222' } });
  let id = 1;
  const pending = new Map();

  return new Promise((resolveConnect, rejectConnect) => {
    ws.on('open', () => {
      const client = {
        send: (method, params = {}) => new Promise((resolve, reject) => {
          const reqId = id++;
          pending.set(reqId, { resolve, reject });
          ws.send(JSON.stringify({ id: reqId, method, params }));
        }),
        close: () => ws.close()
      };
      resolveConnect(client);
    });

    ws.on('message', (msg) => {
      const res = JSON.parse(msg.toString());
      if (res.id && pending.has(res.id)) {
        const { resolve, reject } = pending.get(res.id);
        pending.delete(res.id);
        if (res.error) reject(new Error(res.error.message || JSON.stringify(res.error)));
        else resolve(res.result);
      }
    });

    ws.on('error', rejectConnect);
  });
}

async function main() {
  console.log('Fetching CDP targets...');
  let tabs = await getTabs();
  let tab = tabs.find(t => t.type === 'page' && t.url.includes(':5173')) || tabs.find(t => t.type === 'page');
  console.log(`Initial tab: ${tab.title} (${tab.url})`);

  let client = await createCDP(tab.webSocketDebuggerUrl);
  console.log('Connected to CDP. Navigating via window.location...');
  await client.send('Runtime.enable');
  await client.send('Runtime.evaluate', { expression: `window.location.href = '${TARGET_URL}'` });

  console.log('Waiting 5000ms for page to load and live MQTT telemetry to hydrate...');
  await new Promise(r => setTimeout(r, 5000));
  client.close();

  // Re-fetch targets to get updated page session
  tabs = await getTabs();
  tab = tabs.find(t => t.type === 'page' && t.url.includes(DEVICE_UUID)) || tabs.find(t => t.type === 'page' && t.url.includes(':5173'));
  console.log(`Target tab after navigation: ${tab.title} (${tab.url})`);

  client = await createCDP(tab.webSocketDebuggerUrl);
  await client.send('Page.enable');
  await client.send('DOM.enable');
  await client.send('Runtime.enable');
  await client.send('Page.bringToFront');

  // Dark mode screenshot
  console.log('Capturing dark mode screenshot...');
  const darkRes = await client.send('Page.captureScreenshot', { format: 'png', fromSurface: true });
  const darkPath = path.join(ARTIFACT_DIR, 'live_esp32_hardware_telemetry_dark.png');
  fs.writeFileSync(darkPath, Buffer.from(darkRes.data, 'base64'));
  console.log(`Saved dark screenshot to: ${darkPath}`);

  // Switch to light theme
  console.log('Switching to light theme...');
  await client.send('Runtime.evaluate', {
    expression: "document.documentElement.setAttribute('data-theme', 'light')"
  });
  await new Promise(r => setTimeout(r, 1000));

  // Light mode screenshot
  console.log('Capturing light mode screenshot...');
  const lightRes = await client.send('Page.captureScreenshot', { format: 'png', fromSurface: true });
  const lightPath = path.join(ARTIFACT_DIR, 'live_esp32_hardware_telemetry_light.png');
  fs.writeFileSync(lightPath, Buffer.from(lightRes.data, 'base64'));
  console.log(`Saved light screenshot to: ${lightPath}`);

  // Revert to dark theme
  await client.send('Runtime.evaluate', {
    expression: "document.documentElement.setAttribute('data-theme', 'dark')"
  });

  client.close();
  console.log('All screenshots completed successfully!');
  process.exit(0);
}

main().catch(err => {
  console.error('Fatal error:', err);
  process.exit(1);
});
