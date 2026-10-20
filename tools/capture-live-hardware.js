const WebSocket = require('ws');
const fs = require('fs');
const path = require('path');

const ARTIFACT_DIR = 'C:/Users/toann/.gemini/antigravity/brain/ebb00cfc-bcd4-439d-b063-ce9916bf5e54';
const DEVICE_UUID = '12133938-d604-4386-a58c-b454a5ef7ae7';
const TARGET_URL = `http://127.0.0.1:5173/devices/${DEVICE_UUID}`;

async function main() {
  const http = require('http');
  const targets = await new Promise((resolve, reject) => {
    http.get('http://[::1]:9222/json', (res) => {
      let d = '';
      res.on('data', c => d += c);
      res.on('end', () => resolve(JSON.parse(d)));
    }).on('error', reject);
  });

  const tab = targets.find(t => t.type === 'page' && t.url.includes(':5173')) || targets.find(t => t.type === 'page');
  if (!tab) {
    throw new Error('No page tab found in Chrome CDP');
  }

  console.log(`Connecting to tab: ${tab.title} (${tab.url})`);
  console.log(`WebSocket URL: ${tab.webSocketDebuggerUrl}`);

  const ws = new WebSocket(tab.webSocketDebuggerUrl);

  let idCounter = 1;
  const pending = new Map();

  function send(method, params = {}) {
    return new Promise((resolve, reject) => {
      const id = idCounter++;
      pending.set(id, { resolve, reject });
      ws.send(JSON.stringify({ id, method, params }));
    });
  }

  ws.on('message', (msg) => {
    const res = JSON.parse(msg.toString());
    if (res.id && pending.has(res.id)) {
      const { resolve, reject } = pending.get(res.id);
      pending.delete(res.id);
      if (res.error) reject(res.error);
      else resolve(res.result);
    }
  });

  ws.on('open', async () => {
    try {
      console.log('CDP connected. Navigating to:', TARGET_URL);
      await send('Page.enable');
      await send('Runtime.enable');

      await send('Page.navigate', { url: TARGET_URL });
      console.log('Navigated. Waiting 4000ms for page hydration and live telemetry...');
      await new Promise(r => setTimeout(r, 4500));

      // Capture screenshot
      console.log('Capturing screenshot of live physical telemetry...');
      const screenshot = await send('Page.captureScreenshot', { format: 'png' });
      const buffer = Buffer.from(screenshot.data, 'base64');
      const outPath = path.join(ARTIFACT_DIR, 'live_esp32_hardware_telemetry_dark.png');
      fs.writeFileSync(outPath, buffer);
      console.log(`Saved screenshot to: ${outPath} (${buffer.length} bytes)`);

      ws.close();
      process.exit(0);
    } catch (err) {
      console.error('Error during CDP operation:', err);
      ws.close();
      process.exit(1);
    }
  });
}

main().catch(err => {
  console.error('Fatal error:', err);
  process.exit(1);
});
