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
const channelCount = Number(env.SIM_CHANNELS || 6)
const pulseChannels = new Set((env.SIM_PULSE_CHANNELS || '6').split(',').map(Number))

const outputs = Array.from({ length: channelCount }, (_, i) => ({
  channel: i + 1,
  state: false,
  mode: pulseChannels.has(i + 1) ? 'pulse' : 'toggle',
  totalCommands: 0,
  pulseCount: 0,
  pulseActive: false,
  timer: null,
}))

function required(name) {
  if (!env[name]) fail(`missing ${name}`)
  return env[name]
}

function fail(message) {
  console.error(`spec-device-simulator: ${message}`)
  process.exit(2)
}

function channelView(o) {
  return { channel: o.channel, state: o.state, mode: o.mode, totalCommands: o.totalCommands, pulseCount: o.pulseCount, pulseActive: o.pulseActive }
}

function findChannel(data) {
  const channel = data && data.channel
  if (!Number.isInteger(channel) || channel < 1) return { error: 'ESP_ERR_INVALID_ARG', data: { error: 'channel must be an integer >= 1' } }
  const o = outputs[channel - 1]
  if (!o) return { error: 'ESP_ERR_INVALID_ARG', data: { error: `channel ${channel} not present` } }
  return { o }
}

// §6.4 control actions. Returns { data } on success or { error, data } on a handler error.
const handlers = {
  'control.get_status': () => ({ data: { channels: outputs.map(channelView) } }),
  'control.set_output': (data) => {
    const found = findChannel(data)
    if (!found.o) return found
    const value = data.value
    // The spec accepts a number too (0 = false, else true); clients should send a boolean.
    if (typeof value !== 'boolean' && typeof value !== 'number') return { error: 'ESP_ERR_INVALID_ARG', data: { error: 'value must be a boolean' } }
    const o = found.o
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
  'system.get_info': () => ({ data: { device_id: base.split('/').pop(), firmware: 'spec-simulator' } }),
}

const client = mqtt.connect(url, {
  clientId: `hms_sim_${crypto.randomBytes(3).toString('hex')}`,
  clean: true,
  ca: env.SIM_CA_PATH ? fs.readFileSync(env.SIM_CA_PATH) : undefined,
})

const startedAt = Date.now()
const heartbeatSeconds = Number(env.SIM_HEARTBEAT_SECONDS ?? 5)
let heartbeat = null

client.on('connect', () => {
  console.log(`sim connected; base=${base} channels=${channelCount} silent=[${[...silent].join(',')}] heartbeat=${heartbeatSeconds}s`)
  client.subscribe(`${base}/cmd/#`, { qos: 1 })
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
  if (silent.has(name)) return log(`SILENT ${name}`)

  const handler = handlers[name]
  const result = handler ? handler((envelope.data && envelope.data.data) || {}) : { error: 'unknown_action' }
  const body = { id: envelope.id, type: envelope.type, action: envelope.data.action, success: !result.error, ts: Math.floor(Date.now() / 1000) }
  if (result.error) body.error = result.error
  if (result.data) body.data = result.data
  const bodyText = JSON.stringify(body)
  const sig = crypto.createHmac('sha256', key).update(Buffer.concat([Buffer.from('res:'), Buffer.from(bodyText)])).digest('hex')
  client.publish(`${base}/cmd/res`, `${bodyText.slice(0, -1)},"sig":"${sig}"}`, { qos: 1, retain: false })
  log(`RES ${name} success=${body.success}${body.error ? ` error=${body.error}` : ''}`)
})

client.on('error', e => log('error', e.message))

function log(...parts) {
  console.log(`[${new Date().toISOString().slice(11, 19)}]`, ...parts)
}

const seconds = Number(env.SIM_SECONDS || 0)
if (seconds > 0) setTimeout(() => { client.end(true); process.exit(0) }, seconds * 1000)
