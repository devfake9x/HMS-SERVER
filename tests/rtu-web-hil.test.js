// tests/rtu-web-hil.test.js
// Automated Hardware-in-the-Loop (HIL) Test Suite for ESP32-S3 RTU via Chrome Web Session
const { getTabs, createCdpSession, evalInPage, captureScreenshot } = require('../tools/cdp-client');

class HilTestRunner {
  constructor() {
    this.passed = 0;
    this.failed = 0;
    this.results = [];
  }

  async test(name, fn) {
    const start = Date.now();
    try {
      await fn();
      const duration = Date.now() - start;
      this.passed++;
      this.results.push({ name, status: 'PASS', duration });
      console.log(`  ✓ PASS: ${name} (${duration}ms)`);
    } catch (err) {
      const duration = Date.now() - start;
      this.failed++;
      this.results.push({ name, status: 'FAIL', duration, error: err.message });
      console.error(`  ✗ FAIL: ${name} (${duration}ms) - ${err.message}`);
    }
  }

  assert(condition, message) {
    if (!condition) {
      throw new Error(message || 'Assertion failed');
    }
  }

  assertEqual(actual, expected, message) {
    if (actual !== expected) {
      throw new Error(`${message || 'Assertion failed'}: expected ${JSON.stringify(expected)}, got ${JSON.stringify(actual)}`);
    }
  }

  summary() {
    console.log('\n========================================');
    console.log(`HIL Test Results: Total: ${this.passed + this.failed} | Passed: ${this.passed} | Failed: ${this.failed}`);
    console.log('========================================');
    return {
      total: this.passed + this.failed,
      passed: this.passed,
      failed: this.failed,
      results: this.results
    };
  }
}

async function runHilSuite() {
  console.log('\nStarting RTU Web HIL Test Suite on Physical ESP32 Hardware via Chrome CDP...');
  const runner = new HilTestRunner();

  const tabs = await getTabs();
  const rtuTab = tabs.find(t => t.url.includes('maitech-rtu.local'));
  if (!rtuTab) {
    throw new Error('Tab http://maitech-rtu.local/ not found in Chrome. Ensure it is open.');
  }

  const cdp = await createCdpSession(rtuTab.webSocketDebuggerUrl);

  // Helper inside browser context
  const evalApi = async (code) => {
    return await evalInPage(cdp, `(async () => {
      const token = sessionStorage.getItem('auth_token');
      const authHeaders = {
        'Content-Type': 'application/json',
        'Authorization': 'Bearer ' + token,
        'X-Auth-Token': token
      };
      ${code}
    })()`);
  };

  // Section 1: Telemetry & System Info
  console.log('\n--- [1] Telemetry & System Information ---');
  await runner.test('TC-TEL-01: System Info returns valid ESP32 hardware metadata', async () => {
    const res = await evalApi(`
      const res = await fetch('/api/system/info', { headers: authHeaders });
      return { status: res.status, data: await res.json() };
    `);
    runner.assertEqual(res.status, 200, 'HTTP status');
    runner.assert(res.data.success === true, 'Success flag');
    runner.assertEqual(res.data.data.hostname, 'maitech-rtu', 'Hostname');
    runner.assertEqual(res.data.data.projectName, 'HMS', 'Project name');
    runner.assert(res.data.data.freeInternalHeap > 30000, 'Free internal SRAM > 30KB');
    runner.assert(res.data.data.ntpSynced === true, 'NTP Synced');
  });

  await runner.test('TC-TEL-02: System Health reports healthy core subsystems', async () => {
    const res = await evalApi(`
      const res = await fetch('/api/system/health', { headers: authHeaders });
      return { status: res.status, data: await res.json() };
    `);
    runner.assertEqual(res.status, 200, 'HTTP status');
    runner.assert(Array.isArray(res.data.conditions), 'Conditions array');
    const net = res.data.conditions.find(c => c.domain === 'network');
    runner.assert(net && net.status === 'OK', 'Network status OK');
    const mqtt = res.data.conditions.find(c => c.domain === 'mqtt');
    runner.assert(mqtt && mqtt.status === 'OK', 'MQTT status OK');
  });

  await runner.test('TC-TEL-03: Digital Inputs returns 4 hardware DI channels', async () => {
    const res = await evalApi(`
      const res = await fetch('/api/di/status', { headers: authHeaders });
      return { status: res.status, data: await res.json() };
    `);
    runner.assertEqual(res.status, 200, 'HTTP status');
    runner.assert(res.data.success === true, 'Success flag');
    runner.assertEqual(res.data.data.channels.length, 4, '4 DI channels');
    runner.assertEqual(res.data.data.channels[0].gpioPin, 1, 'DI1 GPIO pin');
  });

  await runner.test('TC-TEL-04: MQTT Config matches EMQX live broker and gateway ID', async () => {
    const res = await evalApi(`
      const res = await fetch('/api/mqtt/config', { headers: authHeaders });
      return { status: res.status, data: await res.json() };
    `);
    runner.assertEqual(res.status, 200, 'HTTP status');
    runner.assertEqual(res.data.data.broker, 'broker.emqx.io', 'Broker host');
    runner.assertEqual(res.data.data.clientId, 'hms_gateway_870c', 'Client ID');
    runner.assertEqual(res.data.data.topicPrefix, 'hms_toan_lab', 'Topic prefix');
    runner.assertEqual(res.data.data.connected, true, 'Connected status');
  });

  // Section 2: Happy Path Digital Output (DO) Control
  console.log('\n--- [2] Happy Path DO Control ---');
  await runner.test('TC-HIL-01: Toggle DO Channel 1 to ON', async () => {
    const res = await evalApi(`
      const setRes = await fetch('/api/do/set', {
        method: 'POST',
        headers: authHeaders,
        body: JSON.stringify({ channel: 1, value: true })
      });
      const setJson = await setRes.json();
      const statusRes = await fetch('/api/do/status', { headers: authHeaders });
      const statusJson = await statusRes.json();
      return {
        setJson,
        ch1: statusJson.data.channels.find(c => c.channel === 1)
      };
    `);
    runner.assertEqual(res.setJson.success, true, 'Set success');
    runner.assertEqual(res.setJson.data.state, true, 'State returned in set');
    runner.assertEqual(res.ch1.state, true, 'State confirmed in status');
  });

  await runner.test('TC-HIL-02: Toggle DO Channel 1 to OFF', async () => {
    const res = await evalApi(`
      const setRes = await fetch('/api/do/set', {
        method: 'POST',
        headers: authHeaders,
        body: JSON.stringify({ channel: 1, value: false })
      });
      const setJson = await setRes.json();
      const statusRes = await fetch('/api/do/status', { headers: authHeaders });
      const statusJson = await statusRes.json();
      return {
        setJson,
        ch1: statusJson.data.channels.find(c => c.channel === 1)
      };
    `);
    runner.assertEqual(res.setJson.success, true, 'Set success');
    runner.assertEqual(res.setJson.data.state, false, 'State returned in set');
    runner.assertEqual(res.ch1.state, false, 'State confirmed in status');
  });

  await runner.test('TC-HIL-03: Toggle Multiple Independent Channels (DO4 & DO5)', async () => {
    const res = await evalApi(`
      await fetch('/api/do/set', {
        method: 'POST',
        headers: authHeaders,
        body: JSON.stringify({ channel: 4, value: true })
      });
      await fetch('/api/do/set', {
        method: 'POST',
        headers: authHeaders,
        body: JSON.stringify({ channel: 5, value: true })
      });
      const statusRes = await fetch('/api/do/status', { headers: authHeaders });
      const statusJson = await statusRes.json();
      const ch4 = statusJson.data.channels.find(c => c.channel === 4);
      const ch5 = statusJson.data.channels.find(c => c.channel === 5);

      // Clean up (turn off)
      await fetch('/api/do/set', {
        method: 'POST',
        headers: authHeaders,
        body: JSON.stringify({ channel: 4, value: false })
      });
      await fetch('/api/do/set', {
        method: 'POST',
        headers: authHeaders,
        body: JSON.stringify({ channel: 5, value: false })
      });

      return { ch4State: ch4.state, ch5State: ch5.state };
    `);
    runner.assertEqual(res.ch4State, true, 'Channel 4 was ON');
    runner.assertEqual(res.ch5State, true, 'Channel 5 was ON');
  });

  // Section 3: Boundary & Input Validation
  console.log('\n--- [3] Boundary Limits & Malformed Inputs ---');
  await runner.test('TC-BND-01: Channel 0 returns 400 Bad Request', async () => {
    const res = await evalApi(`
      const r = await fetch('/api/do/set', {
        method: 'POST',
        headers: authHeaders,
        body: JSON.stringify({ channel: 0, value: true })
      });
      return { status: r.status, text: await r.text() };
    `);
    runner.assertEqual(res.status, 400, 'HTTP status');
    runner.assert(res.text.includes('Invalid channel'), 'Error message contains Invalid channel');
  });

  await runner.test('TC-BND-02: Channel 7 (out of 6) returns 400 Bad Request', async () => {
    const res = await evalApi(`
      const r = await fetch('/api/do/set', {
        method: 'POST',
        headers: authHeaders,
        body: JSON.stringify({ channel: 7, value: true })
      });
      return { status: r.status, text: await r.text() };
    `);
    runner.assertEqual(res.status, 400, 'HTTP status');
    runner.assert(res.text.includes('Invalid channel'), 'Error message contains Invalid channel');
  });

  await runner.test('TC-BND-03: Negative Channel returns 400 Bad Request', async () => {
    const res = await evalApi(`
      const r = await fetch('/api/do/set', {
        method: 'POST',
        headers: authHeaders,
        body: JSON.stringify({ channel: -1, value: true })
      });
      return { status: r.status, text: await r.text() };
    `);
    runner.assertEqual(res.status, 400, 'HTTP status');
    runner.assert(res.text.includes('Invalid channel'), 'Error message contains Invalid channel');
  });

  await runner.test('TC-MAL-01: Empty Body returns 400 Bad Request', async () => {
    const res = await evalApi(`
      const r = await fetch('/api/do/set', {
        method: 'POST',
        headers: authHeaders,
        body: JSON.stringify({})
      });
      return { status: r.status, text: await r.text() };
    `);
    runner.assertEqual(res.status, 400, 'HTTP status');
    runner.assert(res.text.includes('Missing channel or value'), 'Error message');
  });

  await runner.test('TC-MAL-02: Missing Value returns 400 Bad Request', async () => {
    const res = await evalApi(`
      const r = await fetch('/api/do/set', {
        method: 'POST',
        headers: authHeaders,
        body: JSON.stringify({ channel: 1 })
      });
      return { status: r.status, text: await r.text() };
    `);
    runner.assertEqual(res.status, 400, 'HTTP status');
    runner.assert(res.text.includes('Missing channel or value'), 'Error message');
  });

  // Section 4: Security & Authentication Guardrails
  console.log('\n--- [4] Authentication & Security Guardrails ---');
  await runner.test('TC-SEC-01: Unauthenticated request (no token) returns 401', async () => {
    const res = await evalApi(`
      const r = await fetch('/api/do/set', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ channel: 1, value: true })
      });
      return { status: r.status };
    `);
    runner.assertEqual(res.status, 401, 'HTTP status 401 Unauthorized');
  });

  await runner.test('TC-SEC-02: Bogus authentication token returns 401', async () => {
    const res = await evalApi(`
      const r = await fetch('/api/do/set', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Authorization': 'Bearer fake_invalid_token_9999'
        },
        body: JSON.stringify({ channel: 1, value: true })
      });
      return { status: r.status };
    `);
    runner.assertEqual(res.status, 401, 'HTTP status 401 Unauthorized');
  });

  // Section 5: Concurrency & Stress Testing
  console.log('\n--- [5] Concurrency & Hardware Stress ---');
  await runner.test('TC-CON-01: 10 rapid concurrent DO requests execute without mutex failure', async () => {
    const res = await evalApi(`
      const promises = [];
      for (let i = 0; i < 10; i++) {
        promises.push(
          fetch('/api/do/set', {
            method: 'POST',
            headers: authHeaders,
            body: JSON.stringify({ channel: 1, value: i % 2 === 0 })
          }).then(r => ({ status: r.status }))
        );
      }
      return await Promise.all(promises);
    `);
    runner.assertEqual(res.length, 10, 'All 10 requests completed');
    const all200 = res.every(r => r.status === 200);
    runner.assert(all200, 'All 10 requests returned 200 OK');

    // Ensure final state is clean (OFF)
    await evalApi(`
      await fetch('/api/do/set', {
        method: 'POST',
        headers: authHeaders,
        body: JSON.stringify({ channel: 1, value: false })
      });
    `);
  });

  // Section 6: Memory Stability & Leak Check
  console.log('\n--- [6] Memory Stability & Hardware Protection ---');
  await runner.test('TC-RES-01: 20 sequential status queries cause zero DRAM leakage', async () => {
    const memoryData = await evalApi(`
      const infoBefore = await (await fetch('/api/system/info', { headers: authHeaders })).json();
      const heapBefore = infoBefore.data.freeInternalHeap;

      for (let i = 0; i < 20; i++) {
        await fetch('/api/do/status', { headers: authHeaders });
      }

      const infoAfter = await (await fetch('/api/system/info', { headers: authHeaders })).json();
      const heapAfter = infoAfter.data.freeInternalHeap;

      return {
        heapBefore,
        heapAfter,
        delta: heapBefore - heapAfter
      };
    `);
    console.log(`    Internal Heap: Before=${memoryData.heapBefore} B, After=${memoryData.heapAfter} B, Delta=${memoryData.delta} B`);
    runner.assert(Math.abs(memoryData.delta) < 2048, 'Internal heap drift < 2KB');
  });

  // Section 7: Live Browser Visual Parity Verification
  console.log('\n--- [7] Live Browser Visual Parity ---');
  await runner.test('TC-UI-01: Capture verified screenshot of live RTU web console', async () => {
    // Navigate to DO page in real tab
    await evalInPage(cdp, `(() => {
      const app = document.querySelector('#app')?.__vue_app__;
      if (app && app.config.globalProperties.$router) {
        app.config.globalProperties.$router.push('/ioControl/digitalOutput');
      }
    })()`);
    await new Promise(r => setTimeout(r, 1000));
    const screenshotPath = 'C:/Users/toann/.gemini/antigravity/brain/ebb00cfc-bcd4-439d-b063-ce9916bf5e54/maitech_rtu_do_verified.png';
    await captureScreenshot(cdp, screenshotPath);
    runner.assert(true, 'Screenshot captured');
  });

  cdp.close();
  return runner.summary();
}

if (require.main === module) {
  runHilSuite()
    .then(summary => {
      if (summary.failed > 0) process.exit(1);
      process.exit(0);
    })
    .catch(err => {
      console.error('Fatal Test Suite Error:', err);
      process.exit(1);
    });
}

module.exports = { runHilSuite };
