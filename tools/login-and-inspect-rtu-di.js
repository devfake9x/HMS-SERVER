// tools/login-and-inspect-rtu-di.js
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

  // 1. Perform login in browser context and set auth_token + auth_user
  console.log('Logging into RTU...');
  const loginResult = await evalInPage(cdp, `(async () => {
    try {
      const res = await fetch('/api/auth/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username: 'admin', password: 'admin123' })
      });
      const data = await res.json();
      if (data.token) {
        const user = {
          username: 'admin',
          role: data.role || 'administrator',
          role_id: data.role_id ?? 2,
          permissions: data.permissions || [0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF]
        };
        sessionStorage.setItem('auth_token', data.token);
        sessionStorage.setItem('auth_user', JSON.stringify(user));
        localStorage.setItem('auth_token', data.token);
        localStorage.setItem('auth_user', JSON.stringify(user));
      }
      return data;
    } catch (e) {
      return { error: e.message };
    }
  })()`);
  console.log('Login result:', loginResult);

  // 2. Navigate to /ioControl/digitalInput
  console.log('Navigating to /ioControl/digitalInput...');
  await evalInPage(cdp, `window.location.href = 'http://maitech-rtu.local/ioControl/digitalInput'`);
  await new Promise(r => setTimeout(r, 4000));

  // 3. Inspect page details
  const pageDetails = await evalInPage(cdp, `(() => {
    return {
      url: window.location.href,
      title: document.title,
      text: document.body.innerText.substring(0, 1500),
      cards: Array.from(document.querySelectorAll('.io-card')).map(c => ({
        text: c.innerText.trim()
      }))
    };
  })()`);
  console.log('RTU DI Page:', JSON.stringify(pageDetails, null, 2));

  // 4. Capture screenshot
  const screenshotPath = 'C:/Users/toann/.gemini/antigravity/brain/ebb00cfc-bcd4-439d-b063-ce9916bf5e54/maitech_rtu_di_page.png';
  await captureScreenshot(cdp, screenshotPath);
  console.log('Saved screenshot to:', screenshotPath);

  cdp.close();
}

main().catch(console.error);
