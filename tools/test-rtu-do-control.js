// tools/test-rtu-do-control.js
const { getTabs, createCdpSession, evalInPage, captureScreenshot } = require('./cdp-client');

async function main() {
  const tabs = await getTabs();
  const rtuTab = tabs.find(t => t.url.includes('maitech-rtu.local'));
  if (!rtuTab) throw new Error('RTU tab not found');

  const cdp = await createCdpSession(rtuTab.webSocketDebuggerUrl);

  const testResult = await evalInPage(cdp, `(async () => {
    const token = sessionStorage.getItem('auth_token');
    const headers = {
      'Content-Type': 'application/json',
      'Authorization': 'Bearer ' + token,
      'X-Auth-Token': token
    };

    // Step 1: Read initial status
    const initialRes = await fetch('/api/do/status', { headers });
    const initialJson = await initialRes.json();
    const ch1Initial = initialJson.data.channels.find(c => c.channel === 1);

    // Step 2: Set Channel 1 ON (true)
    const turnOnRes = await fetch('/api/do/set', {
      method: 'POST',
      headers,
      body: JSON.stringify({ channel: 1, value: true })
    });
    const turnOnJson = await turnOnRes.json();

    // Step 3: Verify Channel 1 is ON
    const verifyOnRes = await fetch('/api/do/status', { headers });
    const verifyOnJson = await verifyOnRes.json();
    const ch1On = verifyOnJson.data.channels.find(c => c.channel === 1);

    // Wait 500ms
    await new Promise(r => setTimeout(r, 500));

    // Step 4: Set Channel 1 OFF (false)
    const turnOffRes = await fetch('/api/do/set', {
      method: 'POST',
      headers,
      body: JSON.stringify({ channel: 1, value: false })
    });
    const turnOffJson = await turnOffRes.json();

    // Step 5: Verify Channel 1 is OFF
    const verifyOffRes = await fetch('/api/do/status', { headers });
    const verifyOffJson = await verifyOffRes.json();
    const ch1Off = verifyOffJson.data.channels.find(c => c.channel === 1);

    return {
      initialState: ch1Initial?.state,
      turnOnResult: turnOnJson,
      stateAfterOn: ch1On?.state,
      turnOffResult: turnOffJson,
      stateAfterOff: ch1Off?.state
    };
  })()`);

  console.log('RTU DO Control Live Test Result:');
  console.log(JSON.stringify(testResult, null, 2));

  cdp.close();
}

main().catch(console.error);
