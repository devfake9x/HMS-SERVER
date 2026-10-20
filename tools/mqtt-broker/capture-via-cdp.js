const WebSocket = require('ws');
const fs = require('fs');
const path = require('path');
const http = require('http');

const ARTIFACT_DIR = 'C:/Users/toann/.gemini/antigravity/brain/ebb00cfc-bcd4-439d-b063-ce9916bf5e54';
const DEVICE_UUID = '12133938-d604-4386-a58c-b454a5ef7ae7';
const TARGET_URL = `http://127.0.0.1:5173/devices/${DEVICE_UUID}`;

http.get('http://[::1]:9222/json', (res) => {
  let d = '';
  res.on('data', c => d += c);
  res.on('end', async () => {
    const tabs = JSON.parse(d);
    // Find the tab that is 5173 or the first page tab
    const tab = tabs.find(t => t.type === 'page' && t.url.includes(':5173')) || tabs.find(t => t.type === 'page');
    console.log(`Connecting to tab: ${tab.title} (${tab.url})`);

    const ws = new WebSocket(tab.webSocketDebuggerUrl, {
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
        if (msg.error) reject(new Error(msg.error.message));
        else resolve(msg.result);
      }
    });

    ws.on('open', async () => {
      try {
        console.log('Navigating to target URL:', TARGET_URL);
        await send('Page.navigate', { url: TARGET_URL });
        console.log('Navigated. Waiting 4000ms for live telemetry...');
        await new Promise(r => setTimeout(r, 4500));

        // Dark mode screenshot
        console.log('Capturing dark mode screenshot...');
        const dark = await send('Page.captureScreenshot', { format: 'png', fromSurface: true });
        const darkPath = path.join(ARTIFACT_DIR, 'live_esp32_hardware_telemetry_dark.png');
        fs.writeFileSync(darkPath, Buffer.from(dark.data, 'base64'));
        console.log(`Saved dark screenshot to: ${darkPath}`);

        // Switch to light theme
        console.log('Switching to light theme...');
        await send('Runtime.evaluate', {
          expression: "document.documentElement.setAttribute('data-theme', 'light')"
        });
        await new Promise(r => setTimeout(r, 800));

        // Light mode screenshot
        console.log('Capturing light mode screenshot...');
        const light = await send('Page.captureScreenshot', { format: 'png', fromSurface: true });
        const lightPath = path.join(ARTIFACT_DIR, 'live_esp32_hardware_telemetry_light.png');
        fs.writeFileSync(lightPath, Buffer.from(light.data, 'base64'));
        console.log(`Saved light screenshot to: ${lightPath}`);

        // Revert to dark theme
        await send('Runtime.evaluate', {
          expression: "document.documentElement.setAttribute('data-theme', 'dark')"
        });

        ws.close();
        console.log('Done!');
        process.exit(0);
      } catch (err) {
        console.error('Error during CDP operations:', err);
        ws.close();
        process.exit(1);
      }
    });
  });
});
