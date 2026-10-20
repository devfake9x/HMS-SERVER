// tools/fetch-rtu-authenticated.js
const { getTabs, createCdpSession, evalInPage } = require('./cdp-client');

async function main() {
  const tabs = await getTabs();
  const rtuTab = tabs.find(t => t.url.includes('maitech-rtu.local'));
  if (!rtuTab) throw new Error('RTU tab not found');

  const cdp = await createCdpSession(rtuTab.webSocketDebuggerUrl);

  const apiData = await evalInPage(cdp, `(async () => {
    const token = sessionStorage.getItem('auth_token');
    const headers = {
      'Content-Type': 'application/json'
    };
    if (token) {
      headers['Authorization'] = 'Bearer ' + token;
      headers['X-Auth-Token'] = token;
    }

    async function call(url) {
      try {
        const res = await fetch(url, { headers });
        const text = await res.text();
        try {
          return { status: res.status, json: JSON.parse(text) };
        } catch {
          return { status: res.status, text };
        }
      } catch (err) {
        return { error: err.message };
      }
    }

    return {
      systemInfo: await call('/api/system/info'),
      systemHealth: await call('/api/system/health'),
      doStatus: await call('/api/do/status'),
      doForceStatus: await call('/api/do/force/status'),
      mqttConfig: await call('/api/mqtt/config'),
      networkPriority: await call('/api/network/priority')
    };
  })()`);

  console.log('RTU Authenticated APIs:');
  console.log(JSON.stringify(apiData, null, 2));

  cdp.close();
}

main().catch(console.error);
