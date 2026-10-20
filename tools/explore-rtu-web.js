// tools/explore-rtu-web.js
const { getTabs, createCdpSession, evalInPage, captureScreenshot } = require('./cdp-client');

async function main() {
  const tabs = await getTabs();
  const rtuTab = tabs.find(t => t.url.includes('maitech-rtu.local'));
  if (!rtuTab) throw new Error('RTU tab not found');

  const cdp = await createCdpSession(rtuTab.webSocketDebuggerUrl);

  // 1. Get all navigation links/buttons in sidebar
  const navItems = await evalInPage(cdp, `(() => {
    const items = [];
    document.querySelectorAll('a, button, li, .nav-item, [role="button"]').forEach(el => {
      const text = el.innerText?.trim();
      const href = el.getAttribute('href');
      if (text && text.length < 30 && !text.includes('\\n')) {
        items.push({ tag: el.tagName, text, href, className: el.className });
      }
    });
    return items;
  })()`);
  console.log('Sidebar Navigation Items:', JSON.stringify(navItems, null, 2));

  // 2. Check current route / hash / state
  const routeState = await evalInPage(cdp, `({
    hash: window.location.hash,
    pathname: window.location.pathname,
    search: window.location.search
  })`);
  console.log('Current Route State:', routeState);

  // 3. Inspect registered Vue/React or global app state if any
  const appInfo = await evalInPage(cdp, `(() => {
    return {
      hasVue: !!window.__VUE__,
      hasReact: !!window.React,
      scripts: Array.from(document.querySelectorAll('script')).map(s => s.src || 'inline').filter(s => s !== 'inline')
    };
  })()`);
  console.log('App Framework / Scripts:', appInfo);

  cdp.close();
}

main().catch(console.error);
