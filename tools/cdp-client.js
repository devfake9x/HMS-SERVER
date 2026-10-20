// tools/cdp-client.js
// Lightweight native WebSocket CDP client for Chrome Remote Debugging
const fs = require('fs');

async function getTabs() {
  const res = await fetch('http://[::1]:9222/json/list');
  return await res.json();
}

function createCdpSession(wsUrl) {
  return new Promise((resolve, reject) => {
    const ws = new WebSocket(wsUrl);
    let id = 1;
    const callbacks = new Map();

    ws.onopen = () => {
      resolve({
        send(method, params = {}) {
          return new Promise((res, rej) => {
            const reqId = id++;
            callbacks.set(reqId, { res, rej });
            ws.send(JSON.stringify({ id: reqId, method, params }));
          });
        },
        close() {
          ws.close();
        }
      });
    };

    ws.onmessage = (event) => {
      try {
        const msg = JSON.parse(event.data);
        if (msg.id && callbacks.has(msg.id)) {
          const { res, rej } = callbacks.get(msg.id);
          callbacks.delete(msg.id);
          if (msg.error) rej(new Error(msg.error.message || JSON.stringify(msg.error)));
          else res(msg.result);
        }
      } catch (err) {
        console.error('Error handling CDP message:', err);
      }
    };

    ws.onerror = (err) => reject(err);
  });
}

async function evalInPage(cdp, expression) {
  const result = await cdp.send('Runtime.evaluate', {
    expression,
    returnByValue: true,
    awaitPromise: true
  });
  return result?.result?.value;
}

async function captureScreenshot(cdp, outputPath) {
  const result = await cdp.send('Page.captureScreenshot', {
    format: 'png',
    quality: 90
  });
  if (result && result.data) {
    fs.writeFileSync(outputPath, Buffer.from(result.data, 'base64'));
    console.log(`Saved screenshot to ${outputPath}`);
  }
}

module.exports = { getTabs, createCdpSession, evalInPage, captureScreenshot };

if (require.main === module) {
  (async () => {
    try {
      const tabs = await getTabs();
      console.log('Available tabs:');
      tabs.forEach(t => console.log(`- [${t.type}] ${t.title}: ${t.url} (${t.id})`));
      
      const rtuTab = tabs.find(t => t.url.includes('maitech-rtu.local'));
      if (!rtuTab) {
        console.log('No maitech-rtu.local tab found!');
        return;
      }
      
      console.log('\nConnecting to RTU tab:', rtuTab.url);
      const cdp = await createCdpSession(rtuTab.webSocketDebuggerUrl);
      
      const pageInfo = await evalInPage(cdp, `({
        url: window.location.href,
        title: document.title,
        bodyText: document.body.innerText.substring(0, 500),
        inputs: Array.from(document.querySelectorAll('input')).map(i => ({ name: i.name, type: i.type, placeholder: i.placeholder, id: i.id }))
      })`);
      
      console.log('RTU Page Info:', JSON.stringify(pageInfo, null, 2));
      
      // Save screenshot to brain directory
      const screenshotPath = 'C:/Users/toann/.gemini/antigravity/brain/ebb00cfc-bcd4-439d-b063-ce9916bf5e54/maitech_rtu_initial.png';
      await captureScreenshot(cdp, screenshotPath);
      
      cdp.close();
    } catch (e) {
      console.error('Error:', e);
    }
  })();
}
