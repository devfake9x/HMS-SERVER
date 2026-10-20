// tools/verify-live-do1.js
const { getTabs, createCdpSession, evalInPage } = require('./cdp-client');

async function main() {
  const tabs = await getTabs();
  let hmsTab = tabs.find(t => t.url.includes('127.0.0.1:5173') || t.url.includes('localhost:5173'));
  const cdp = await createCdpSession(hmsTab.webSocketDebuggerUrl);

  const fetchDetails = await evalInPage(cdp, `(async () => {
    try {
      const res = await fetch('/api/v1/devices/870c870c-870c-870c-870c-870c870c870c/telemetry', {
        headers: { 'Accept': 'application/json' },
        credentials: 'include'
      });
      const text = await res.text();
      return { status: res.status, headers: Object.fromEntries(res.headers.entries()), text };
    } catch (e) {
      return { error: e.message };
    }
  })()`);

  console.log('Fetch Details:', JSON.stringify(fetchDetails, null, 2));

  // Let's also check auth state in pinia or auth store
  const piniaState = await evalInPage(cdp, `(() => {
    const el = document.querySelector('#app');
    return {
      sessionStorage: Object.keys(sessionStorage).map(k => ({ key: k, val: sessionStorage.getItem(k)?.substring(0, 50) })),
      localStorage: Object.keys(localStorage).map(k => ({ key: k, val: localStorage.getItem(k)?.substring(0, 50) }))
    };
  })()`);

  console.log('Storage:', JSON.stringify(piniaState, null, 2));

  cdp.close();
}

main().catch(console.error);
