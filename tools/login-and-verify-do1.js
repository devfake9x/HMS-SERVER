// tools/login-and-verify-do1.js
const { getTabs, createCdpSession, evalInPage, captureScreenshot } = require('./cdp-client');

async function main() {
  const tabs = await getTabs();
  const hmsTab = tabs.find(t => t.url.includes('127.0.0.1:5173') || t.url.includes('localhost:5173'));
  if (!hmsTab) {
    throw new Error('No HMS tab found');
  }

  console.log('Connecting to HMS tab:', hmsTab.url);
  const cdp = await createCdpSession(hmsTab.webSocketDebuggerUrl);

  // 1. Perform in-browser login
  console.log('Authenticating session in browser...');
  const loginResult = await evalInPage(cdp, `(async () => {
    try {
      const csrfRes = await fetch('/api/v1/auth/csrf');
      const csrf = await csrfRes.json();
      const loginRes = await fetch('/api/v1/auth/login', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'X-CSRF-TOKEN': csrf.requestToken
        },
        body: JSON.stringify({ email: 'admin@hms.local', password: 'AdminPassword123!', rememberMe: true })
      });
      return { status: loginRes.status, ok: loginRes.ok };
    } catch (e) {
      return { error: e.message };
    }
  })()`);
  console.log('Login result:', loginResult);

  // 2. Navigate to device detail page
  const targetUrl = 'http://127.0.0.1:5173/devices/870c870c-870c-870c-870c-870c870c870c';
  console.log('Navigating to:', targetUrl);
  await evalInPage(cdp, `window.location.href = '${targetUrl}'`);

  console.log('Waiting 5s for data loading and telemetry ingestion...');
  await new Promise(r => setTimeout(r, 5000));

  // 3. Inspect DOM for DO states
  const domInfo = await evalInPage(cdp, `(() => {
    const card = document.querySelector('[data-test="rtu-operating-config-card"]');
    if (card) {
      card.scrollIntoView({ behavior: 'instant', block: 'center' });
    }
    const doBadges = Array.from(document.querySelectorAll('[data-test^="do-"][data-test$="-state"]')).map(el => ({
      test: el.getAttribute('data-test'),
      text: el.innerText.trim(),
      className: el.className
    }));
    const diBadges = Array.from(document.querySelectorAll('[data-test^="di-"][data-test$="-state"]')).map(el => ({
      test: el.getAttribute('data-test'),
      text: el.innerText.trim(),
      className: el.className
    }));
    const devName = document.querySelector('[data-test="device-detail-name"]')?.innerText;
    return {
      title: document.title,
      devName,
      hasCard: !!card,
      doBadges,
      diBadges
    };
  })()`);

  console.log('\nDOM Evaluation:');
  console.log(JSON.stringify(domInfo, null, 2));

  await new Promise(r => setTimeout(r, 1000));

  // 4. Capture screenshot
  const screenshotPath = 'C:/Users/toann/.gemini/antigravity/brain/ebb00cfc-bcd4-439d-b063-ce9916bf5e54/maitech_rtu_do1_on_verified.png';
  await captureScreenshot(cdp, screenshotPath);
  console.log('Screenshot saved to:', screenshotPath);

  cdp.close();
}

main().catch(err => {
  console.error('Error:', err);
  process.exit(1);
});
