// Device simulator written from the HMS-IDF MQTT spec (engineering-docs/api/mqtt-api.md §2-§6.4),
// independently of the server code, so it can catch a server that only agrees with itself.
//
// It verifies the command HMAC over the exact received bytes, drops what a real device drops
// (bad signature, > 1023 bytes), and replies on <base>/cmd/res signed over "res:" + body.
// Relay outputs: 6 channels, DO6 in pulse mode, the rest in toggle mode.
//
// Configuration (env only; no key or credential lives in this file):
//   SIM_MQTT_URL     broker URL, e.g. mqtts://192.168.1.101:443
//   SIM_CA_PATH      CA certificate for the broker's TLS (optional for mqtt://)
//   SIM_TOPIC_BASE   <prefix>/<device_id>, e.g. hms_server_selftest/sim_001
//   SIM_KEY_FILE     file holding the 64-hex-char command key
//   SIM_SECONDS      run time in seconds; 0 or unset = until stopped
//   SIM_SILENT       comma list of type.action that get no reply (e.g. control.set_output),
//                    to exercise the server's outcome-unknown timeout (spec R4)
//   SIM_CHANNELS     number of relay channels (default 6)
//   SIM_PULSE_CHANNELS comma list of channels in pulse mode (default 6)
//   SIM_HEARTBEAT_SECONDS status heartbeat period on <base>/status (default 5; 0 = none, which
//                    the server shows as offline)
//   SIM_ADC_CHANNELS number of analog inputs (default 8)
// Failure modes for the config-write flow (§5, §6.1, §6.2):
//   SIM_ADC_JOB=1          adc.set_channel answers "accepted, still running" and applies after 10 s;
//                          system.get_operations reports the job
//   SIM_HOSTNAME_DEFERRED=1 set_hostname answers applied_immediately:false; get_hostname keeps the old name
//   SIM_ADC_DROP_FIELDS=1  set_channel resets type/enabled when they are not sent
//   SIM_CLOCK_SKEW_S=<n>   system.get_time reports an epoch n seconds off
// system.reboot (§6.1 L604, §7 L1087, §3 L483, §4.2 L551, §6.4 L866-880):
//   dry_run:true answers {would_apply:true} before every other check; a real reboot needs the root
//   confirm REBOOT-<device_id>, is throttled (UNAUTHORIZED), replies, then drops the connection like
//   a chip reset (no DISCONNECT, so the broker publishes the last will), stays down for
//   SIM_BOOT_SECONDS, comes back with uptime 0, every unforced channel OFF and forced channels at
//   their forced level. Where the chip would keep the TCP session half open, the simulator closes the
//   socket, so the "offline" instant differs from a real device and does not transfer to it.
//   SIM_FORCED=<ch>:on|off,...  channels with a force override, e.g. 2:on,4:off
//   SIM_BOOT_SECONDS=<n>        time between the drop and the reconnect (default 8)
//   SIM_THROTTLE_S=<n>          a second real reboot within n seconds gets UNAUTHORIZED (default 60)
//   SIM_THROTTLE_SURVIVES=1     the throttle survives the boot (default: lost, like RAM)
//   SIM_DO_WARMUP_S=<n>         control.get_status answers "busy" for n seconds after boot (default 2)
//   SIM_REBOOT_NO_RETURN=1      reply, drop, and never come back
//   SIM_REBOOT_NO_REPLY=1       reboot without a reply
//   SIM_SILENT_REAL=system.reboot  like SIM_SILENT but dry runs still get their reply: the real command
//                               is silent and nothing reboots
//   (SIM_SILENT=system.reboot  no reply and no reboot, dry run included)
'use strict'
const fs = require('fs')
const crypto = require('crypto')
const mqtt = require('mqtt')

const env = process.env
const url = required('SIM_MQTT_URL')
const base = required('SIM_TOPIC_BASE')
const key = Buffer.from(fs.readFileSync(required('SIM_KEY_FILE'), 'utf8').trim(), 'hex')
if (key.length !== 32) fail('SIM_KEY_FILE must hold 64 hex characters')
const silent = new Set((env.SIM_SILENT || '').split(',').map(s => s.trim()).filter(Boolean))
const silentReal = new Set((env.SIM_SILENT_REAL || '').split(',').map(s => s.trim()).filter(Boolean))
const channelCount = Number(env.SIM_CHANNELS || 6)
const pulseChannels = new Set((env.SIM_PULSE_CHANNELS || '6').split(',').map(Number))

// SIM_FORCED=2:on,4:off -> Map(channel -> forced level)
const forcedLevels = new Map((env.SIM_FORCED || '').split(',').map(s => s.trim()).filter(Boolean).map(item => {
  const [channel, level] = item.split(':')
  if (!Number.isInteger(Number(channel)) || !['on', 'off'].includes(level)) fail(`SIM_FORCED item "${item}" must be <channel>:on|off`)
  return [Number(channel), level === 'on']
}))

const outputs = Array.from({ length: channelCount }, (_, i) => ({
  channel: i + 1,
  state: forcedLevels.get(i + 1) ?? false,
  forced: forcedLevels.has(i + 1),
  mode: pulseChannels.has(i + 1) ? 'pulse' : 'toggle',
  totalCommands: 0,
  pulseCount: 0,
  pulseActive: false,
  timer: null,
}))

const flag = name => env[name] === '1'
const clockSkewSeconds = Number(env.SIM_CLOCK_SKEW_S || 0)
const nowSeconds = () => Math.floor(Date.now() / 1000)
const system = { hostname: 'sim-rtu', pendingHostname: null, timezone: 'ICT-7', ntpServers: [], lastSync: nowSeconds() }
const adcChannels = Array.from({ length: Number(env.SIM_ADC_CHANNELS || 8) }, (_, i) => ({
  ai: i + 1, name: `AI-${i + 1}`, type: '4-20mA', enabled: true,
}))
const jobs = new Map()
let nextJob = 100

function required(name) {
  if (!env[name]) fail(`missing ${name}`)
  return env[name]
}

function fail(message) {
  console.error(`spec-device-simulator: ${message}`)
  process.exit(2)
}

function channelView(o) {
  return {
    channel: o.channel, state: o.state, mode: o.mode, forced: o.forced, enabled: true, gpioOk: true,
    totalCommands: o.totalCommands, pulseCount: o.pulseCount, pulseActive: o.pulseActive,
  }
}

const bootSeconds = Number(env.SIM_BOOT_SECONDS ?? 8)
const throttleSeconds = Number(env.SIM_THROTTLE_S ?? 60)
const warmupSeconds = Number(env.SIM_DO_WARMUP_S ?? 2)
let startedAt = Date.now()
let lastRebootAt = null
let warmupUntil = 0
let rebooting = false

function findChannel(data) {
  const channel = data && data.channel
  if (!Number.isInteger(channel) || channel < 1) return { error: 'ESP_ERR_INVALID_ARG', data: { error: 'channel must be an integer >= 1' } }
  const o = outputs[channel - 1]
  if (!o) return { error: 'ESP_ERR_INVALID_ARG', data: { error: `channel ${channel} not present` } }
  return { o }
}

// §6.4 control actions. Returns { data } on success or { error, data } on a handler error.
const handlers = {
  'control.get_status': () => {
    // Right after a boot the relay driver is still starting: the device answers "busy" with a hint.
    // Spec §6.4 (L822, L831): ESP_ERR_INVALID_STATE with data.error "busy" and retry_after_ms.
    if (Date.now() < warmupUntil) return { error: 'ESP_ERR_INVALID_STATE', data: { error: 'busy', retry_after_ms: 1000 } }
    return { data: { channels: outputs.map(channelView) } }
  },
  'control.set_output': (data) => {
    const found = findChannel(data)
    if (!found.o) return found
    const value = data.value
    // The spec accepts a number too (0 = false, else true); clients should send a boolean.
    if (typeof value !== 'boolean' && typeof value !== 'number') return { error: 'ESP_ERR_INVALID_ARG', data: { error: 'value must be a boolean' } }
    const o = found.o
    // Spec §6.4 (L828): a forced channel refuses writes with data.error "channel_forced".
    if (o.forced) return { error: 'ESP_ERR_INVALID_STATE', data: { error: 'channel_forced' } }
    o.state = typeof value === 'boolean' ? value : value !== 0
    o.totalCommands++
    return { data: { channel: o.channel, state: o.state, mode: o.mode, source: 'mqtt' } }
  },
  'control.pulse': (data) => {
    const found = findChannel(data)
    if (!found.o) return found
    const o = found.o
    // The spec only says the channel must be in pulse mode; this code and text are what the lab
    // gateway (hms_toan_lab/gateway_870c) answered on 2026-11-06.
    if (o.mode !== 'pulse') return { error: 'ESP_ERR_INVALID_ARG', data: { error: 'Channel is in toggle mode, use set_output' } }
    const duration = data.duration_ms
    if (!Number.isInteger(duration) || duration < 1) return { error: 'ESP_ERR_INVALID_ARG', data: { error: 'duration_ms must be a positive integer' } }
    clearTimeout(o.timer)
    o.state = true
    o.pulseActive = true
    o.pulseCount++
    o.totalCommands++
    o.timer = setTimeout(() => { o.state = false; o.pulseActive = false }, duration)
    return { data: { channel: o.channel, duration_ms: duration, state: true, source: 'mqtt' } }
  },
  'system.ping': () => ({ data: { status: 'alive' } }),
  'system.get_info': () => ({
    data: { device_id: base.split('/').pop(), firmware: 'spec-simulator', hostname: system.hostname, uptime_seconds: Math.floor((Date.now() - startedAt) / 1000) },
  }),

  // §6.1 system and time.
  'system.get_device_id': () => ({ data: { device_id: base.split('/').pop(), mac: '02:00:00:00:00:09' } }),
  'system.get_hostname': () => ({ data: { hostname: system.hostname } }),
  'system.set_hostname': (data) => {
    if (typeof data.hostname !== 'string' || !data.hostname) return invalid('hostname must be a non-empty string')
    if (flag('SIM_HOSTNAME_DEFERRED')) {
      system.pendingHostname = data.hostname
      return { data: { hostname: data.hostname, applied_immediately: false, reboot_recommended: true } }
    }
    system.hostname = data.hostname
    return { data: { hostname: data.hostname, applied_immediately: true, reboot_recommended: false } }
  },
  'system.get_time': () => {
    const epoch = nowSeconds() + clockSkewSeconds
    return {
      data: {
        epoch, datetime: new Date(epoch * 1000).toISOString().slice(0, 19), ntp_synced: clockSkewSeconds === 0,
        last_sync: system.lastSync, rtc_available: false, rtc_model: 'none', timezone: system.timezone,
        ntp_servers: system.ntpServers, ntp_mode: system.ntpServers.length ? 'manual' : 'auto',
      },
    }
  },
  'system.set_timezone': (data) => {
    if (typeof data.timezone !== 'string' || !data.timezone) return invalid('timezone must be a non-empty string')
    system.timezone = data.timezone
    return { data: { timezone: system.timezone, datetime: new Date().toISOString().slice(0, 19), message: 'Timezone updated' } }
  },
  'system.sync_ntp': () => {
    system.lastSync = nowSeconds()
    return { data: { message: 'NTP sync started', was_synced: true } }
  },
  // §5: job read-back, camelCase fields.
  'system.get_operations': (data) => {
    const id = data.operation_id
    if (typeof id !== 'string' || !/^[1-9][0-9]*$/.test(id)) return invalid('operation_id must be a decimal string')
    const job = jobs.get(id)
    if (!job) return { data: { operationId: id, state: 'unknown' } }
    if (Date.now() < job.doneAt) return { data: { operationId: id, state: 'pending' } }
    return { data: { operationId: id, state: 'terminal', terminal: true, resultSuccess: true, resultCode: 0, resultMessage: 'applied' } }
  },
  'ota.get_status': () => ({ data: { success: true, data: { firmware: { version: 'spec-simulator' }, ota: { state: 'idle', progress: 0 } } } }),
  'mqtt.get_config': () => ({ data: { broker: 'simulator', port: 443, client_id: 'hms_sim', use_tls: true, has_password: false, clean_session: true } }),
  'mqtt.get_status': () => ({ data: { connected: true, config_trial: { state: 'none' } } }),

  // §6.1 L604: schedules a reboot (default delay 2000 ms). Order: preview (L551), then the admission
  // throttle (§3 L483), then the handler's own checks (confirm R3a, payload).
  'system.reboot': (data, envelope) => {
    if (envelope.dry_run === true) return { data: { would_apply: true } }
    if (lastRebootAt !== null && Date.now() - lastRebootAt < throttleSeconds * 1000) return { error: 'UNAUTHORIZED' }
    if (envelope.confirm !== `REBOOT-${base.split('/').pop()}`) return { error: 'confirm_required' }
    const delay = data.delay_ms === undefined ? 2000 : data.delay_ms
    if (!Number.isInteger(delay) || delay < 0 || delay > 60000) return invalid('delay_ms must be an integer 0..60000')
    lastRebootAt = Date.now()
    scheduleReboot(delay)
    return flag('SIM_REBOOT_NO_REPLY') ? { noReply: true } : { data: { delay_ms: delay } }
  },

  // §6.2 battery and ADC.
  'battery.get_status': () => ({ data: { available: false } }),
  'battery.get_alerts': () => ({ data: { available: false } }),
  'adc.get_config': () => ({ data: { available: true, channel_count: adcChannels.length, hardware: { num_devices: 1 } } }),
  'adc.get_channels': () => ({ data: adcChannels.map(c => ({ ...c })) }),
  'adc.get_values': () => ({
    data: adcChannels.map(c => ({ ai: c.ai, value: c.enabled ? 4 + c.ai / 10 : 0, unit: 'mA', quality: c.enabled ? 'GOOD' : 'DISABLED' })),
  }),
  'adc.set_channel': (data) => {
    const channel = findAnalog(data)
    if (!channel.ai) return channel
    if (data.name !== undefined && typeof data.name !== 'string') return invalid('name must be a string')
    const apply = () => {
      if (data.name !== undefined) channel.name = data.name
      if (typeof data.type === 'string') channel.type = data.type
      else if (flag('SIM_ADC_DROP_FIELDS')) channel.type = ''
      if (typeof data.enabled === 'boolean') channel.enabled = data.enabled
      else if (flag('SIM_ADC_DROP_FIELDS')) channel.enabled = false
    }
    if (flag('SIM_ADC_JOB')) {
      const id = String(nextJob++)
      jobs.set(id, { doneAt: Date.now() + 10_000 })
      setTimeout(apply, 10_000)
      return { error: 'ESP_ERR_TIMEOUT', data: { accepted: true, pending: true, operation_id: id } }
    }
    apply()
    return { data: { ...channel } }
  },
  'adc.toggle_channel': (data) => {
    const channel = findAnalog(data)
    if (!channel.ai) return channel
    if (typeof data.enabled !== 'boolean') return invalid('enabled must be a boolean')
    channel.enabled = data.enabled
    return { data: { ai: channel.ai, enabled: channel.enabled } }
  },
}

function invalid(message) {
  return { error: 'ESP_ERR_INVALID_ARG', data: { error: message } }
}

function findAnalog(data) {
  const ai = data && data.ai
  if (!Number.isInteger(ai) || ai < 1 || ai > 16) return invalid('ai must be an integer 1..16')
  return adcChannels[ai - 1] || invalid(`ai ${ai} not present`)
}

const client = mqtt.connect(url, {
  clientId: `hms_sim_${crypto.randomBytes(3).toString('hex')}`,
  clean: true,
  keepalive: 10,
  will: { topic: `${base}/status/connectivity`, payload: '{"state":"offline"}', qos: 1, retain: true },
  ca: env.SIM_CA_PATH ? fs.readFileSync(env.SIM_CA_PATH) : undefined,
})

const heartbeatSeconds = Number(env.SIM_HEARTBEAT_SECONDS ?? 5)
let heartbeat = null

client.on('connect', () => {
  console.log(`sim connected; base=${base} channels=${channelCount} silent=[${[...silent].join(',')}] heartbeat=${heartbeatSeconds}s`)
  client.subscribe(`${base}/cmd/#`, { qos: 1 })
  client.publish(`${base}/status/connectivity`, '{"state":"online"}', { qos: 1, retain: true })
  clearInterval(heartbeat)
  if (heartbeatSeconds > 0) {
    const beat = () => client.publish(`${base}/status`, JSON.stringify({
      uptime_seconds: Math.floor((Date.now() - startedAt) / 1000),
      free_heap: 1_400_000,
      mqtt_connected: true,
    }), { qos: 0, retain: false })
    beat()
    heartbeat = setInterval(beat, heartbeatSeconds * 1000)
  }
})

client.on('message', (topic, wire) => {
  if (topic.endsWith('/cmd/res')) return
  const text = wire.toString('utf8')
  const match = text.match(/,"sig":"([0-9a-f]{64})"}$/)
  if (!match) return log('DROP unsigned', topic)
  const signed = Buffer.from(text.slice(0, match.index) + '}', 'utf8')
  const valid = crypto.timingSafeEqual(crypto.createHmac('sha256', key).update(signed).digest(), Buffer.from(match[1], 'hex'))
  if (!valid || wire.length > 1023) return log(`DROP ${valid ? 'oversize' : 'bad-signature'}`, topic) // devices drop silently

  let envelope
  try { envelope = JSON.parse(signed) } catch { return log('DROP bad-json', topic) }
  const name = `${envelope.type}.${envelope.data && envelope.data.action}`
  log(`CMD ${name} id=${envelope.id} data=${JSON.stringify(envelope.data && envelope.data.data)}`)
  if (silent.has(name) || (silentReal.has(name) && envelope.dry_run !== true)) return log(`SILENT ${name}`)

  const handler = handlers[name]
  const result = handler ? handler((envelope.data && envelope.data.data) || {}, envelope) : { error: 'unknown_action' }
  if (result.noReply) return log(`NO-REPLY ${name}`)
  const body = { id: envelope.id, type: envelope.type, action: envelope.data.action, success: !result.error, ts: Math.floor(Date.now() / 1000) }
  if (result.error) body.error = result.error
  if (result.data) body.data = result.data
  const bodyText = JSON.stringify(body)
  const sig = crypto.createHmac('sha256', key).update(Buffer.concat([Buffer.from('res:'), Buffer.from(bodyText)])).digest('hex')
  client.publish(`${base}/cmd/res`, `${bodyText.slice(0, -1)},"sig":"${sig}"}`, { qos: 1, retain: false })
  log(`RES ${name} success=${body.success}${body.error ? ` error=${body.error}` : ''}`)
})

// Reset like a chip: the TCP connection goes away with no DISCONNECT, so the broker publishes the
// last will; nothing reconnects until the boot time has passed (mqtt.js would retry after 1 s).
function scheduleReboot(delayMs) {
  if (rebooting) return
  rebooting = true
  setTimeout(() => {
    log(`REBOOT: dropping the connection; down for ${flag('SIM_REBOOT_NO_RETURN') ? 'good' : `${bootSeconds}s`}`)
    clearInterval(heartbeat)
    client.options.reconnectPeriod = 0
    for (const o of outputs) { clearTimeout(o.timer); o.state = false; o.pulseActive = false }
    client.stream.destroy()
    if (flag('SIM_REBOOT_NO_RETURN')) return
    setTimeout(() => {
      startedAt = Date.now()
      if (!flag('SIM_THROTTLE_SURVIVES')) lastRebootAt = null
      for (const o of outputs) if (o.forced) o.state = forcedLevels.get(o.channel)
      warmupUntil = Date.now() + warmupSeconds * 1000
      rebooting = false
      log('REBOOT: booted; reconnecting')
      client.options.reconnectPeriod = 1000
      client.reconnect()
    }, bootSeconds * 1000)
  }, delayMs)
}

client.on('error', e => log('error', e.message))

function log(...parts) {
  console.log(`[${new Date().toISOString().slice(11, 19)}]`, ...parts)
}

const seconds = Number(env.SIM_SECONDS || 0)
if (seconds > 0) setTimeout(() => { client.end(true); process.exit(0) }, seconds * 1000)
