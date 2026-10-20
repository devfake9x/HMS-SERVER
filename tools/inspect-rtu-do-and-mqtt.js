// tools/inspect-rtu-do-and-mqtt.js
const { getTabs, createCdpSession, evalInPage, captureScreenshot } = require('./cdp-client');

async function main() {
  const tabs = await getTabs();
  const rtuTab = tabs.find(t => t.url.includes('maitech-rtu.local'));
  if (!rtuTab) throw new Error('RTU tab not found');

  const cdp = await createCdpSession(rtuTab.webSocketDebuggerUrl);

  // Listen to network requests to discover REST APIs
  await cdp.send('Network.enable');

  // 1. Navigate to /ioControl/digitalOutput
  console.log('Navigating to /ioControl/digitalOutput...');
  await evalInPage(cdp, `(() => {
    const app = document.querySelector('#app')?.__vue_app__;
    if (app && app.config.globalProperties.$router) {
      app.config.globalProperties.$router.push('/ioControl/digitalOutput');
    } else {
      window.location.href = '/ioControl/digitalOutput';
    }
  })()`);

  await new Promise(r => setTimeout(r, 1200));

  // Take screenshot of DO page
  const doScreenshot = 'C:/Users/toann/.gemini/antigravity/brain/ebb00cfc-bcd4-439d-b063-ce9916bf5e54/maitech_rtu_do_page.png';
  await captureScreenshot(cdp, doScreenshot);

  // Inspect DO page elements
  const doPageInfo = await evalInPage(cdp, `(() => {
    return {
      title: document.title,
      heading: document.querySelector('h1, h2, h3, .page-title')?.innerText,
      buttons: Array.from(document.querySelectorAll('button')).map(b => ({ text: b.innerText.trim(), disabled: b.disabled, class: b.className })),
      switches: Array.from(document.querySelectorAll('input[type="checkbox"], .switch, .el-switch, [role="switch"]')).map(s => ({
        id: s.id,
        checked: s.checked,
        class: s.className,
        parentText: s.closest('tr, .card, .item, .do-item, div')?.innerText?.substring(0, 100)
      })),
      text: document.body.innerText.substring(0, 800)
    };
  })()`);
  console.log('DO Page Info:', JSON.stringify(doPageInfo, null, 2));

  // 2. Navigate to /communication/mqtt
  console.log('\nNavigating to /communication/mqtt...');
  await evalInPage(cdp, `(() => {
    const app = document.querySelector('#app')?.__vue_app__;
    if (app && app.config.globalProperties.$router) {
      app.config.globalProperties.$router.push('/communication/mqtt');
    } else {
      window.location.href = '/communication/mqtt';
    }
  })()`);

  await new Promise(r => setTimeout(r, 1500));

  const mqttScreenshot = 'C:/Users/toann/.gemini/antigravity/brain/ebb00cfc-bcd4-439d-b063-ce9916bf5e54/maitech_rtu_mqtt_page.png';
  await captureScreenshot(cdp, mqttScreenshot);

  const mqttPageInfo = await evalInPage(cdp, `(() => {
    return {
      inputs: Array.from(document.querySelectorAll('input, select')).map(i => ({
        label: i.closest('.form-group, .field, tr, div')?.querySelector('label')?.innerText?.trim() || i.name || i.id,
        name: i.name,
        id: i.id,
        value: i.type === 'password' ? '***' : i.value,
        type: i.type
      })).filter(i => i.value),
      text: document.body.innerText.substring(0, 1000)
    };
  })()`);
  console.log('MQTT Page Info:', JSON.stringify(mqttPageInfo, null, 2));

  cdp.close();
}

main().catch(console.error);
