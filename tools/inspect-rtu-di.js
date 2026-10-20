// tools/inspect-rtu-di.js
const { getTabs, createCdpSession, evalInPage, captureScreenshot } = require('./cdp-client');

async function main() {
  const tabs = await getTabs();
  const rtuTab = tabs.find(t => t.url.includes('maitech-rtu.local'));
  if (!rtuTab) {
    console.error('No RTU tab found!');
    return;
  }

  console.log('Connecting to RTU tab:', rtuTab.url);
  const cdp = await createCdpSession(rtuTab.webSocketDebuggerUrl);

  // Navigate to digital input page
  console.log('Navigating to DI page...');
  await evalInPage(cdp, `window.location.href = 'http://maitech-rtu.local/ioControl/digitalInput'`);
  await new Promise(r => setTimeout(r, 3000));

  // Inspect page details
  const pageDetails = await evalInPage(cdp, `(() => {
    return {
      url: window.location.href,
      title: document.title,
      bodyText: document.body.innerText.substring(0, 1500),
      links: Array.from(document.querySelectorAll('a')).map(a => ({ text: a.innerText.trim(), href: a.href })),
      tables: Array.from(document.querySelectorAll('table')).map(t => t.innerText.trim())
    };
  })()`);

  console.log('RTU DI Page Details:');
  console.log(JSON.stringify(pageDetails, null, 2));

  // Capture screenshot
  const screenshotPath = 'C:/Users/toann/.gemini/antigravity/brain/ebb00cfc-bcd4-439d-b063-ce9916bf5e54/maitech_rtu_di_page.png';
  await captureScreenshot(cdp, screenshotPath);
  console.log('Saved DI screenshot to:', screenshotPath);

  // Also query the DI API with session token
  const apiTest = await evalInPage(cdp, `(async () => {
    const token = sessionStorage.getItem('auth_token');
    const headers = token ? { 'Authorization': 'Bearer ' + token, 'X-Auth-Token': token } : {};
    try {
      const res = await fetch('/api/di/status', { headers });
      return { status: res.status, data: await res.json() };
    } catch (e) {
      return { error: e.message };
    }
  })()`);

  console.log('RTU In-Page /api/di/status:', JSON.stringify(apiTest, null, 2));

  cdp.close();
}

main().catch(console.error);
