// tools/inspect-rtu-network.js
const { getTabs, createCdpSession, evalInPage } = require('./cdp-client');

async function main() {
  const tabs = await getTabs();
  const rtuTab = tabs.find(t => t.url.includes('maitech-rtu.local'));
  if (!rtuTab) throw new Error('RTU tab not found');

  const cdp = await createCdpSession(rtuTab.webSocketDebuggerUrl);

  const networkEntries = await evalInPage(cdp, `(() => {
    return window.performance.getEntriesByType('resource').map(r => ({
      name: r.name,
      initiatorType: r.initiatorType,
      duration: Math.round(r.duration),
      transferSize: r.transferSize
    }));
  })()`);

  console.log('RTU Resource Entries count:', networkEntries.length);
  console.log('Resource Entries:', JSON.stringify(networkEntries.slice(-30), null, 2));

  // Check if WebSocket is open on the page
  const wsCheck = await evalInPage(cdp, `(() => {
    return {
      hasWebSocketInstances: !!window._ws || !!window.socket,
      localStorage: Object.keys(localStorage).map(k => ({ key: k, value: localStorage.getItem(k) })),
      sessionStorage: Object.keys(sessionStorage).map(k => ({ key: k, value: sessionStorage.getItem(k) }))
    };
  })()`);
  console.log('Storage info:', wsCheck);

  cdp.close();
}

main().catch(console.error);
