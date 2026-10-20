// tools/capture-live-maitech.js
const { getTabs, createCdpSession, evalInPage, captureScreenshot } = require('./cdp-client');

async function testHmsPage() {
  const tabs = await getTabs();
  const hmsTab = tabs.find(t => t.url.includes('127.0.0.1:5173'));
  if (!hmsTab) throw new Error('No 127.0.0.1:5173 tab found');

  const cdp = await createCdpSession(hmsTab.webSocketDebuggerUrl);

  // Scroll to bottom element
  await evalInPage(cdp, `
    const card = document.querySelector('[data-test="rtu-operating-config-card"]');
    if (card) {
      card.scrollIntoView({ behavior: 'instant', block: 'center' });
    }
  `);
  await new Promise(r => setTimeout(r, 1000));

  const screenshotPath = 'C:/Users/toann/.gemini/antigravity/brain/ebb00cfc-bcd4-439d-b063-ce9916bf5e54/maitech_rtu_hms_server_full_card_scrolled.png';
  await captureScreenshot(cdp, screenshotPath);
  console.log('Saved scrolled view to:', screenshotPath);

  cdp.close();
}

testHmsPage().catch(console.error);
