const WebSocket = require('ws');
const fs = require('fs');
const path = require('path');

const ARTIFACT_DIR = 'C:/Users/toann/.gemini/antigravity/brain/ebb00cfc-bcd4-439d-b063-ce9916bf5e54';
const WS_URL = 'ws://[::1]:9222/devtools/page/B47102B50909BE01F769720153E97E7B';

const ws = new WebSocket(WS_URL, {
  headers: { Host: 'localhost:9222' }
});

let id = 1;
const pending = new Map();

function send(method, params = {}) {
  return new Promise((resolve, reject) => {
    const reqId = id++;
    pending.set(reqId, { resolve, reject });
    ws.send(JSON.stringify({ id: reqId, method, params }));
  });
}

ws.on('message', (data) => {
  const msg = JSON.parse(data.toString());
  if (msg.id && pending.has(msg.id)) {
    const { resolve, reject } = pending.get(msg.id);
    pending.delete(msg.id);
    if (msg.error) reject(new Error(msg.error.message || JSON.stringify(msg.error)));
    else resolve(msg.result);
  }
});

ws.on('open', async () => {
  try {
    console.log('Connected to device detail page tab!');
    await send('Page.enable');
    await send('Runtime.enable');
    await send('Page.bringToFront');

    console.log('Capturing dark mode screenshot...');
    const dark = await send('Page.captureScreenshot', { format: 'png', fromSurface: true });
    const darkPath = path.join(ARTIFACT_DIR, 'live_esp32_hardware_telemetry_dark.png');
    fs.writeFileSync(darkPath, Buffer.from(dark.data, 'base64'));
    console.log(`Saved dark screenshot to: ${darkPath} (${dark.data.length} bytes base64)`);

    console.log('Switching to light mode...');
    await send('Runtime.evaluate', {
      expression: "document.documentElement.setAttribute('data-theme', 'light')"
    });
    await new Promise(r => setTimeout(r, 600));

    console.log('Capturing light mode screenshot...');
    const light = await send('Page.captureScreenshot', { format: 'png', fromSurface: true });
    const lightPath = path.join(ARTIFACT_DIR, 'live_esp32_hardware_telemetry_light.png');
    fs.writeFileSync(lightPath, Buffer.from(light.data, 'base64'));
    console.log(`Saved light screenshot to: ${lightPath} (${light.data.length} bytes base64)`);

    console.log('Switching back to dark mode...');
    await send('Runtime.evaluate', {
      expression: "document.documentElement.setAttribute('data-theme', 'dark')"
    });

    ws.close();
    console.log('SUCCESS! Both screenshots captured.');
    process.exit(0);
  } catch (err) {
    console.error('Error during screenshot capture:', err);
    ws.close();
    process.exit(1);
  }
});

ws.on('error', (err) => {
  console.error('WS Connection error:', err);
  process.exit(1);
});
