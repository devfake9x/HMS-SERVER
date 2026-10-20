// tools/inspect-hms-server-tab.js
const { getTabs, createCdpSession, evalInPage, captureScreenshot } = require('./cdp-client');

async function main() {
  const tabs = await getTabs();
  const hmsTab = tabs.find(t => t.url.includes('5173/devices/12133938-d604-4386-a58c-b454a5ef7ae7') && !t.url.includes('permissions'));
  if (!hmsTab) {
    console.log('HMS Device tab not found');
    return;
  }

  const cdp = await createCdpSession(hmsTab.webSocketDebuggerUrl);

  const pageInfo = await evalInPage(cdp, `({
    url: window.location.href,
    title: document.title,
    bodyText: document.body.innerText.substring(0, 1000)
  })`);

  console.log('HMS Server Device Tab Info:');
  console.log(JSON.stringify(pageInfo, null, 2));

  const screenshotPath = 'C:/Users/toann/.gemini/antigravity/brain/ebb00cfc-bcd4-439d-b063-ce9916bf5e54/hms_server_device_detail_live.png';
  await captureScreenshot(cdp, screenshotPath);

  cdp.close();
}

main().catch(console.error);
