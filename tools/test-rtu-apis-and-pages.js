// tools/test-rtu-apis-and-pages.js
const { getTabs, createCdpSession, evalInPage, captureScreenshot } = require('./cdp-client');

async function main() {
  const tabs = await getTabs();
  const rtuTab = tabs.find(t => t.url.includes('maitech-rtu.local'));
  if (!rtuTab) throw new Error('RTU tab not found');

  const cdp = await createCdpSession(rtuTab.webSocketDebuggerUrl);

  // Enable Network monitoring to capture fetch/XHR endpoints
  await cdp.send('Network.enable');

  // 1. Expand "IO Control" and "Mạng"
  await evalInPage(cdp, `(() => {
    document.querySelectorAll('.nav-item').forEach(el => {
      if (el.innerText.includes('IO Control') || el.innerText.includes('Mạng')) {
        el.click();
      }
    });
  })()`);

  await new Promise(r => setTimeout(r, 600));

  // Get expanded sub-items
  const subItems = await evalInPage(cdp, `(() => {
    return Array.from(document.querySelectorAll('a.nav-child, .nav-item.expanded + * a, .nav-item.expanded a')).map(a => ({
      text: a.innerText.trim(),
      href: a.getAttribute('href')
    }));
  })()`);
  console.log('Expanded Sub-Items:', subItems);

  // Also check all routes registered in Vue Router if accessible
  const routerRoutes = await evalInPage(cdp, `(() => {
    try {
      const app = document.querySelector('#app')?.__vue_app__;
      if (app && app.config.globalProperties.$router) {
        return app.config.globalProperties.$router.getRoutes().map(r => ({ path: r.path, name: r.name }));
      }
    } catch (e) {
      return { error: e.message };
    }
    return null;
  })()`);
  console.log('Vue Router Routes:', routerRoutes);

  cdp.close();
}

main().catch(console.error);
