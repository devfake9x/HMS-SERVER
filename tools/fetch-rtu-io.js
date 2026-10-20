// tools/fetch-rtu-io.js
const { getTabs, createCdpSession, evalInPage } = require('./cdp-client');

async function main() {
  const tabs = await getTabs();
  const rtuTab = tabs.find(t => t.url.includes('maitech-rtu.local'));
  if (!rtuTab) throw new Error('RTU tab not found');

  const cdp = await createCdpSession(rtuTab.webSocketDebuggerUrl);

  const ioData = await evalInPage(cdp, `(async () => {
    const token = sessionStorage.getItem('auth_token');
    const headers = { 'Content-Type': 'application/json' };
    if (token) {
      headers['Authorization'] = 'Bearer ' + token;
      headers['X-Auth-Token'] = token;
    }

    async function call(url) {
      try {
        const res = await fetch(url, { headers });
        return { status: res.status, data: await res.json() };
      } catch (err) {
        return { error: err.message };
      }
    }

    return {
      di: await call('/api/di/status'),
      ai: await call('/api/ai/status'),
      battery: await call('/api/system/battery'),
      audit: await call('/api/audit/logs?limit=5'),
      modbus: await call('/api/modbus/status')
    };
  })()`);

  console.log('RTU Additional IO & System APIs:');
  console.log(JSON.stringify(ioData, null, 2));

  cdp.close();
}

main().catch(console.error);
