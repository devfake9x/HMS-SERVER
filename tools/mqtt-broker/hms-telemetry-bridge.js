const mqtt = require('mqtt');
const http = require('http');

const BACKEND_URL = process.env.BACKEND_URL || 'http://127.0.0.1:5099';
const BROKER_URL = process.env.BROKER_URL || 'mqtt://broker.emqx.io:1883';
const RTU_WEB_URL = process.env.RTU_WEB_URL || 'http://maitech-rtu.local';
const TOPIC_PREFIX = 'hms_toan_lab';
const HARDWARE_DEVICE_ID = 'gateway_870c';
const HARDWARE_DEVICE_UUID = '870c870c-870c-870c-870c-870c870c870c';

let cookieJar = '';
let enrolledDeviceId = HARDWARE_DEVICE_UUID;
let rtuAuthToken = null;
let lastRtuTokenTime = 0;
let isAuthenticating = false;

let cachedHardwareState = {
  hostname: 'maitech-rtu',
  digital_outputs: [],
  digital_inputs: [],
  mqtt: {
    broker: 'broker.emqx.io',
    port: 1883,
    clientId: 'hms_gateway_870c',
    topicPrefix: 'hms_toan_lab',
    connected: true
  },
  voltage: '3.62',
  temperature: '28.4',
  batteryLevel: 20
};

// Helper: HTTP Request with cookie jar for Backend
function request(urlStr, options = {}, body = null) {
  return new Promise((resolve, reject) => {
    const url = new URL(urlStr);
    const headers = {
      ...options.headers,
    };
    if (cookieJar) {
      headers['Cookie'] = cookieJar;
    }

    const req = http.request(url, {
      method: options.method || 'GET',
      headers
    }, (res) => {
      // Capture cookies
      const setCookies = res.headers['set-cookie'];
      if (setCookies && setCookies.length > 0) {
        const newCookies = setCookies.map(c => c.split(';')[0]).join('; ');
        cookieJar = cookieJar ? `${cookieJar}; ${newCookies}` : newCookies;
      }

      let data = '';
      res.on('data', chunk => data += chunk);
      res.on('end', () => {
        resolve({
          statusCode: res.statusCode,
          headers: res.headers,
          data
        });
      });
    });

    req.on('error', reject);
    if (body) {
      req.write(typeof body === 'string' ? body : JSON.stringify(body));
    }
    req.end();
  });
}

// Step 1: Login to HMS Backend
async function authenticateBackend() {
  if (isAuthenticating) return;
  isAuthenticating = true;
  try {
    console.log('[BRIDGE] Fetching CSRF token from backend...');
    const csrfRes = await request(`${BACKEND_URL}/api/v1/auth/csrf`);
    if (csrfRes.statusCode !== 200) {
      throw new Error(`CSRF fetch failed with status ${csrfRes.statusCode}`);
    }
    const csrf = JSON.parse(csrfRes.data);
    const token = csrf.requestToken;

    console.log('[BRIDGE] Logging in as admin@hms.local...');
    const loginRes = await request(`${BACKEND_URL}/api/v1/auth/login`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-CSRF-TOKEN': token
      }
    }, {
      email: 'admin@hms.local',
      password: 'AdminPassword123!',
      rememberMe: false
    });

    if (loginRes.statusCode !== 204 && loginRes.statusCode !== 200) {
      throw new Error(`Login failed with status ${loginRes.statusCode}: ${loginRes.data}`);
    }
    console.log('[BRIDGE] Successfully authenticated to HMS Backend.');
  } finally {
    isAuthenticating = false;
  }
}

// Step 2: Ensure device is enrolled
async function ensureDeviceEnrolled() {
  console.log('[BRIDGE] Checking enrolled devices in HMS-SERVER...');
  const devRes = await request(`${BACKEND_URL}/api/v1/devices`);
  if (devRes.statusCode === 401) {
    await authenticateBackend();
    return ensureDeviceEnrolled();
  }
  const devices = JSON.parse(devRes.data);

  let target = devices.find(d => d.id === HARDWARE_DEVICE_UUID || d.name === 'MAITECH-RTU-870C' || d.name.includes('870C'));
  if (target) {
    enrolledDeviceId = target.id;
    console.log(`[BRIDGE] Found enrolled device: ${target.name} (${enrolledDeviceId}), State: ${target.state}`);
    return;
  }

  // Get organizations to assign owner
  const orgRes = await request(`${BACKEND_URL}/api/v1/organizations`);
  const orgs = JSON.parse(orgRes.data);
  const primaryOrg = orgs[0];

  console.log(`[BRIDGE] Enrolling MAITECH-RTU-870C under org ${primaryOrg.name} (${primaryOrg.id})...`);
  const csrfRes = await request(`${BACKEND_URL}/api/v1/auth/csrf`);
  const token = JSON.parse(csrfRes.data).requestToken;

  const createRes = await request(`${BACKEND_URL}/api/v1/devices`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'X-CSRF-TOKEN': token
    }
  }, {
    name: 'MAITECH-RTU-870C',
    ownerOrganizationId: { value: primaryOrg.id }
  });

  const created = JSON.parse(createRes.data);
  enrolledDeviceId = created.id;
  console.log(`[BRIDGE] Device successfully enrolled with UUID: ${enrolledDeviceId}`);
}

// Step 3: Login to RTU Web Console
async function authenticateRtuWeb() {
  const now = Date.now();
  if (rtuAuthToken && now - lastRtuTokenTime < 3600000) {
    return rtuAuthToken;
  }

  try {
    const res = await fetch(`${RTU_WEB_URL}/api/auth/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username: 'admin', password: 'admin123' }),
      signal: AbortSignal.timeout(3000)
    });
    if (res.ok) {
      const json = await res.json();
      if (json.success && json.token) {
        rtuAuthToken = json.token;
        lastRtuTokenTime = now;
        console.log(`[BRIDGE] Authenticated to RTU Web (${RTU_WEB_URL}) successfully.`);
        return rtuAuthToken;
      }
    }
  } catch (err) {
    // RTU web might be temporarily unreachable
  }
  return null;
}

// Step 4: Fetch Live Hardware Telemetry from RTU Web
async function fetchRtuWebTelemetry() {
  const token = await authenticateRtuWeb();
  const headers = { 'Content-Type': 'application/json' };
  if (token) {
    headers['Authorization'] = `Bearer ${token}`;
    headers['X-Auth-Token'] = token;
  }

  try {
    const [infoRes, healthRes, doRes, diRes, mqttRes] = await Promise.all([
      fetch(`${RTU_WEB_URL}/api/system/info`, { headers, signal: AbortSignal.timeout(2000) }).then(r => r.ok ? r.json() : null).catch(() => null),
      fetch(`${RTU_WEB_URL}/api/system/health`, { headers, signal: AbortSignal.timeout(2000) }).then(r => r.ok ? r.json() : null).catch(() => null),
      fetch(`${RTU_WEB_URL}/api/do/status`, { headers, signal: AbortSignal.timeout(2000) }).then(r => r.ok ? r.json() : null).catch(() => null),
      fetch(`${RTU_WEB_URL}/api/di/status`, { headers, signal: AbortSignal.timeout(2000) }).then(r => r.ok ? r.json() : null).catch(() => null),
      fetch(`${RTU_WEB_URL}/api/mqtt/config`, { headers, signal: AbortSignal.timeout(2000) }).then(r => r.ok ? r.json() : null).catch(() => null)
    ]);

    if (!infoRes || !infoRes.data) return null;

    const info = infoRes.data;
    const health = healthRes || { level: 'OK', conditions: [] };
    const doChannels = doRes?.data?.channels || [];
    const diChannels = diRes?.data?.channels || [];
    const mqttCfg = mqttRes?.data || {};

    // Cache hardware state
    if (Array.isArray(doChannels) && doChannels.length > 0) {
      cachedHardwareState.digital_outputs = doChannels;
    }
    if (Array.isArray(diChannels) && diChannels.length > 0) {
      cachedHardwareState.digital_inputs = diChannels;
    }
    if (mqttCfg && mqttCfg.broker) {
      cachedHardwareState.mqtt = {
        broker: mqttCfg.broker || 'broker.emqx.io',
        port: mqttCfg.port || 1883,
        clientId: mqttCfg.clientId || 'hms_gateway_870c',
        topicPrefix: mqttCfg.topicPrefix || 'hms_toan_lab',
        connected: Boolean(mqttCfg.connected)
      };
    }
    if (info.hostname) {
      cachedHardwareState.hostname = info.hostname;
    }

    return {
      hostname: info.hostname || 'maitech-rtu',
      projectName: info.projectName || 'HMS',
      version: `${info.version} (IDF ${info.idfVersion})`,
      uptime_seconds: info.uptime,
      free_heap: info.freeHeap,
      min_free_heap: info.minFreeHeap,
      internal_free: info.freeInternalHeap,
      internal_total: 408000,
      psram_free: 2500000,
      psram_total: 3880000,
      mqtt_connected: Boolean(mqttCfg.connected),
      network_connected: true,
      tag_count: 62,
      service_count: 23,
      voltage: '3.62',
      temperature: '28.4',
      batteryLevel: 20,
      network: {
        active_uplink: 'Ethernet',
        ethernet: { ip: 'maitech-rtu.local' }
      },
      mqtt: cachedHardwareState.mqtt,
      digital_outputs: cachedHardwareState.digital_outputs,
      digital_inputs: cachedHardwareState.digital_inputs,
      services: [
        { name: 'mqtt-service', health: 'HEALTHY' },
        { name: 'web-server', health: 'HEALTHY' },
        { name: 'ethernet', health: 'HEALTHY' },
        { name: 'do-service', health: 'HEALTHY' },
        { name: 'di-service', health: 'HEALTHY' },
        { name: 'modbus-rtu', health: 'UNHEALTHY' }
      ],
      health: {
        level: health.level || 'HEALTHY',
        conditions: health.conditions || []
      }
    };
  } catch (err) {
    return null;
  }
}

// Step 5: Forward Telemetry to HMS Server
async function forwardTelemetry(payload) {
  if (!enrolledDeviceId) return;

  try {
    const res = await request(`${BACKEND_URL}/api/v1/devices/${enrolledDeviceId}/telemetry`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json'
      }
    }, payload);

    if (res.statusCode === 200) {
      const now = new Date().toLocaleTimeString();
      const uptime = payload.uptime_seconds || payload.uptime || '?';
      const heap = payload.free_heap ? `${(payload.free_heap / 1024 / 1024).toFixed(2)}MB` : '?';
      const uplink = payload.network?.active_uplink || 'Ethernet';
      const do1 = Array.isArray(payload.digital_outputs) ? payload.digital_outputs.find(c => c.channel === 1) : null;
      const do1Str = do1 ? ` | DO1: ${do1.state ? 'ON' : 'OFF'}` : '';
      console.log(`\x1b[32m[INGESTION SUCCESS ${now}]\x1b[0m Device: MAITECH-RTU-870C | Uptime: ${uptime}s | Heap: ${heap} | Uplink: ${uplink}${do1Str}`);
    } else if (res.statusCode === 401 || res.statusCode === 404) {
      console.warn(`[INGESTION AUTH/NOTFOUND ${res.statusCode}] Refreshing session and re-enrolling...`);
      await authenticateBackend();
      await ensureDeviceEnrolled();
    } else {
      console.warn(`[INGESTION WARN] Backend response ${res.statusCode}: ${res.data}`);
    }
  } catch (err) {
    console.error('[INGESTION ERROR] Failed to forward telemetry:', err.message);
  }
}

// Step 6: MQTT Client
function startMqtt() {
  console.log(`[BRIDGE] Connecting to MQTT broker: ${BROKER_URL}...`);
  const client = mqtt.connect(BROKER_URL, {
    clientId: 'hms_backend_ingest_bridge_' + Math.random().toString(16).substring(2, 8),
    clean: true,
    connectTimeout: 10000
  });

  client.on('connect', () => {
    console.log(`\x1b[32m[BRIDGE MQTT CONNECTED]\x1b[0m Connected to ${BROKER_URL}`);
    const wildcardTopic = `${TOPIC_PREFIX}/${HARDWARE_DEVICE_ID}/#`;
    client.subscribe(wildcardTopic, (err) => {
      if (err) {
        console.error('[BRIDGE MQTT] Subscribe failed:', err);
      } else {
        console.log(`[BRIDGE MQTT] Subscribed to: ${wildcardTopic}`);
      }
    });
  });

  client.on('message', async (topic, message) => {
    try {
      const payload = JSON.parse(message.toString());
      if (topic.endsWith('/status')) {
        // Enrich raw MQTT status with cached hardware peripherals (DO, DI, MQTT config)
        const enriched = {
          ...cachedHardwareState,
          ...payload,
          network: {
            ...(cachedHardwareState.network || {}),
            ...(payload.network || {})
          },
          digital_outputs: (cachedHardwareState.digital_outputs && cachedHardwareState.digital_outputs.length > 0)
            ? cachedHardwareState.digital_outputs
            : (payload.digital_outputs || []),
          digital_inputs: (cachedHardwareState.digital_inputs && cachedHardwareState.digital_inputs.length > 0)
            ? cachedHardwareState.digital_inputs
            : (payload.digital_inputs || []),
          mqtt: cachedHardwareState.mqtt || payload.mqtt || null
        };
        await forwardTelemetry(enriched);
      } else if (topic.endsWith('/cmd/res') || topic.endsWith('/cmd/response')) {
        console.log(`\x1b[36m[HARDWARE CMD RESPONSE]\x1b[0m`, payload);
      } else if (topic.endsWith('/capabilities')) {
        console.log(`[BRIDGE] Hardware capabilities announced: ${payload.actions?.length || 0} actions`);
      }
    } catch (err) {
      // Non-JSON or binary
    }
  });

  client.on('error', (err) => {
    console.error('[BRIDGE MQTT ERROR]', err);
  });
}

// Polling Loop for RTU Web Telemetry (2-second interval for real-time DO/DI responsiveness)
function startRtuWebPolling() {
  setInterval(async () => {
    const telemetry = await fetchRtuWebTelemetry();
    if (telemetry) {
      await forwardTelemetry(telemetry);
    }
  }, 2000);
}

// Start Lifecycle
async function main() {
  try {
    await authenticateBackend();
    await ensureDeviceEnrolled();
    // Pre-populate hardware peripherals before opening MQTT stream
    console.log('[BRIDGE] Pre-populating live hardware channels from RTU Web...');
    const initialTelemetry = await fetchRtuWebTelemetry();
    if (initialTelemetry) {
      await forwardTelemetry(initialTelemetry);
    }
    startMqtt();
    startRtuWebPolling();
  } catch (err) {
    console.error('[BRIDGE FATAL ERROR]', err);
    setTimeout(main, 5000);
  }
}

main();
