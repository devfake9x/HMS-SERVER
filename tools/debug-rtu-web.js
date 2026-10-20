// tools/debug-rtu-web.js
const { getTabs, createCdpSession, evalInPage } = require('./cdp-client');

async function main() {
  const tabs = await getTabs();
  const rtuTab = tabs.find(t => t.url.includes('maitech-rtu.local'));
  if (!rtuTab) throw new Error('RTU tab not found');

  const cdp = await createCdpSession(rtuTab.webSocketDebuggerUrl);

  // Monitor Console and Network
  const logs = [];
  const requests = [];

  // Enable events
  await cdp.send('Console.enable');
  await cdp.send('Runtime.enable');
  await cdp.send('Network.enable');

  // Let's inspect what APIs exist by checking window / fetch
  const testApis = await evalInPage(cdp, `(async () => {
    const results = {};
    const endpoints = [
      '/api/v1/system/status',
      '/api/v1/io/do',
      '/api/v1/io/di',
      '/api/v1/io/ai',
      '/api/v1/mqtt/config',
      '/api/v1/mqtt/status',
      '/api/v1/modbus/status',
      '/api/v1/network/status'
    ];
    for (const ep of endpoints) {
      try {
        const res = await fetch(ep);
        results[ep] = { status: res.status, ok: res.ok, data: res.ok ? await res.json() : await res.text() };
      } catch (err) {
        results[ep] = { error: err.message };
      }
    }
    return results;
  })()`);

  console.log('RTU REST API Probe Results:');
  console.log(JSON.stringify(testApis, null, 2));

  cdp.close();
}

main().catch(console.error);
