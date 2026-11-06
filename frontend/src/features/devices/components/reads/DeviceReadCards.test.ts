// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import type { Component } from 'vue'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import { adminApi, type DeviceLinkDto } from '@/core/api/adminApi'
import DeviceInfoTimeCard from './DeviceInfoTimeCard.vue'
import DeviceNetworkCard from './DeviceNetworkCard.vue'
import DevicePowerAdcCard from './DevicePowerAdcCard.vue'
import ReadFieldList from './ReadFieldList.vue'
import labReplies from './fixtures/lab-gateway-replies.json'

const catalog = { catalogVersion: 'v', catalogHash: 'h', trusted: true, actions: [] }
const link: DeviceLinkDto = {
  binding: { topicPrefix: 'lab', mqttDeviceId: 'gw', keyVersion: null },
  keyGeneration: 1, commandKeyProvisioned: true, connectivity: 'online', connectivityAt: null,
  lastHeartbeatAt: null, online: true, capabilitiesAt: null, capabilityCount: 0, brokerConnected: true,
  allowedOutputChannels: [], canControlOutputs: false, canViewOutputs: true,
}

// Reply data shaped like spec §6 (with lab-style values, already anonymised).
const replies: Record<string, unknown> = {
  'system.get_time': { epoch: 1793930000, datetime: '2026-11-06 09:13:20', ntp_synced: true, last_sync: 1793929000, rtc_available: false, timezone: 'ICT-7' },
  'system.get_hostname': { hostname: 'maitech-rtu' },
  'system.get_device_id': { device_id: 'gateway_test', mac: '02:00:00:00:00:01' },
  'ota.get_status': { state: 'idle', progress: 0 },
  'network.get_priority': { internet_uplink: 'ethernet', active_uplink: 'ethernet', has_connectivity: true },
  'network.get_ethernet': { type: 'W5500', connected: true, active: true, ip: '192.168.1.127', gateway: '192.168.1.1', netmask: '255.255.255.0' },
  'network.get_modem': { operator: 'TestNet', rssi: -71, registered: true },
  'network.wifi_sta_status': { enabled: false, connected: false },
  'network.wifi_ap_status': { enabled: true, pending_restart: false, ssid: 'TEST-AP', channel: 6, client_count: 0 },
  'battery.get_status': { available: false },
  'battery.get_alerts': { available: false },
  'adc.get_config': { available: true, channel_count: 3, hardware: 'ads1115' },
  'adc.get_channels': [
    { ai: 1, name: 'Áp suất', type: '4-20mA', enabled: true },
    { ai: 2, name: 'Nhiệt độ', type: '0-10V', enabled: false },
  ],
  'adc.get_values': [
    { ai: 1, value: 12.5, unit: 'mA', quality: 'good' },
    { ai: 3, value: 1.2, unit: 'V', quality: 'bad' },
  ],
}

const allowed = Object.keys(replies)

function mockDevice() {
  vi.spyOn(adminApi, 'sendCommand').mockImplementation(async (_d, body) =>
    ({ status: 'Published', code: 'MQTT_PUBLISHED', correlationId: `${body.type}.${body.action}` }))
  vi.spyOn(adminApi, 'commandResult').mockImplementation(async (_d, id) => ({
    correlationId: id,
    result: { status: 'Executed', code: 'DEVICE_EXECUTED', correlationId: id },
    reply: { type: 'x', action: 'y', success: true, error: null, data: replies[id], receivedAt: '2026-11-06T00:00:00Z' },
  }))
}

async function readAll(component: Component): Promise<VueWrapper> {
  const props: Record<string, unknown> = { deviceId: 'dev-1', csrfToken: 'csrf', catalog, catalogVerified: true, allowedCommands: allowed, link }
  const wrapper = mount(component, { props })
  await wrapper.find('[data-test="read-start"]').trigger('click')
  await vi.advanceTimersByTimeAsync(20_000)
  await flushPromises()
  return wrapper
}

describe('read cards', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    mockDevice()
  })
  afterEach(() => {
    vi.restoreAllMocks()
    vi.useRealTimers()
  })

  it('device & time card shows time, NTP, hostname, id and OTA state', async () => {
    const wrapper = await readAll(DeviceInfoTimeCard)
    const text = wrapper.text()

    expect(text).toContain('2026-11-06 09:13:20')
    expect(text).toContain('maitech-rtu')
    expect(text).toContain('gateway_test')
    expect(wrapper.find('[data-test="section-ota.get_status"]').text()).toContain('idle')
    expect(wrapper.find('[data-test="section-system.get_time"]').text()).toContain('Có')
  })

  it('network card shows uplink, ethernet, modem and Wi-Fi', async () => {
    const wrapper = await readAll(DeviceNetworkCard)
    const text = wrapper.text()

    expect(text).toContain('192.168.1.127')
    expect(text).toContain('TestNet')
    expect(text).toContain('TEST-AP')
    expect(wrapper.find('[data-test="section-network.get_priority"]').text()).toContain('ethernet')
  })

  it('power & ADC card says when hardware is missing and joins ADC channels by ai', async () => {
    const wrapper = await readAll(DevicePowerAdcCard)

    expect(wrapper.find('[data-test="section-battery.get_status"]').text()).toContain('Không có phần cứng')
    expect(wrapper.find('[data-test="adc-row-1"]').text()).toContain('Áp suất')
    expect(wrapper.find('[data-test="adc-row-1"]').text()).toContain('12.5')
    expect(wrapper.find('[data-test="adc-row-1"]').text()).toContain('mA')
    expect(wrapper.find('[data-test="adc-row-2"]').text()).toContain('Nhiệt độ')
    expect(wrapper.find('[data-test="adc-row-3"]').text()).toContain('1.2')
  })
})

describe('read cards with the lab gateway replies (anonymised)', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    vi.spyOn(adminApi, 'sendCommand').mockImplementation(async (_d, body) =>
      ({ status: 'Published', code: 'MQTT_PUBLISHED', correlationId: `${body.type}.${body.action}` }))
    vi.spyOn(adminApi, 'commandResult').mockImplementation(async (_d, id) => ({
      correlationId: id,
      result: { status: 'Executed', code: 'DEVICE_EXECUTED', correlationId: id },
      reply: { type: 'x', action: 'y', success: true, error: null, data: (labReplies as Record<string, unknown>)[id], receivedAt: '2026-11-06T00:00:00Z' },
    }))
  })
  afterEach(() => {
    vi.restoreAllMocks()
    vi.useRealTimers()
  })

  it('unwraps the OTA reply the device nests one level deeper than other reads', async () => {
    const wrapper = await readAll(DeviceInfoTimeCard)
    const ota = wrapper.find('[data-test="section-ota.get_status"]').text()

    expect(ota).toContain('Trạng thái OTA')
    expect(ota).toContain('idle')
    expect(ota).toContain('1.0.108')
    expect(ota).toContain('recovery')
    expect(ota).not.toContain('"success"')
    expect(wrapper.find('[data-test="section-system.get_time"]').text()).toContain('auto')
  })

  it('rounds long readings and shows each ADC channel with its quality', async () => {
    const wrapper = await readAll(DevicePowerAdcCard)

    expect(wrapper.find('[data-test="section-battery.get_status"]').text()).toContain('7.757 V')
    expect(wrapper.find('[data-test="section-battery.get_status"]').text()).toContain('60.74 %')
    expect(wrapper.findAll('[data-test^="adc-row-"]')).toHaveLength(8)
    expect(wrapper.find('[data-test="adc-row-1"]').text()).toContain('WIRE_BREAK')
    expect(wrapper.find('[data-test="adc-row-1"]').text()).toContain('-0.0007813')
  })

  it('shows the modem and the active uplink from the real reply', async () => {
    const wrapper = await readAll(DeviceNetworkCard)

    expect(wrapper.find('[data-test="section-network.get_priority"]').text()).toContain('4G/Modem')
    expect(wrapper.find('[data-test="section-network.get_modem"]').text()).toContain('10.0.0.25')
  })
})

describe('ADC data the card does not expect', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => {
    vi.restoreAllMocks()
    vi.useRealTimers()
  })

  it('skips channels without a usable ai and shows an unknown reply shape as it is', async () => {
    const odd: Record<string, unknown> = {
      ...replies,
      'adc.get_channels': [{ ai: null, name: 'Không số' }, { ai: 2, name: 'Hai' }],
      'adc.get_values': { readings: { first: 1 } },
    }
    vi.spyOn(adminApi, 'sendCommand').mockImplementation(async (_d, body) =>
      ({ status: 'Published', code: 'MQTT_PUBLISHED', correlationId: `${body.type}.${body.action}` }))
    vi.spyOn(adminApi, 'commandResult').mockImplementation(async (_d, id) => ({
      correlationId: id,
      result: { status: 'Executed', code: 'DEVICE_EXECUTED', correlationId: id },
      reply: { type: 'x', action: 'y', success: true, error: null, data: odd[id], receivedAt: '2026-11-06T00:00:00Z' },
    }))

    const wrapper = await readAll(DevicePowerAdcCard)

    expect(wrapper.find('[data-test="adc-row-0"]').exists()).toBe(false)
    expect(wrapper.find('[data-test="adc-row-2"]').text()).toContain('Hai')
    expect(wrapper.find('[data-test="section-adc.get_values"]').text()).toContain('readings')
  })
})

describe('ReadFieldList', () => {
  it('labels known fields and lists the rest without breaking on odd values', () => {
    const wrapper = mount(ReadFieldList, {
      props: {
        data: { hostname: 'gw', uptime: 12, nested: { a: 1 }, flag: true },
        fields: [{ key: 'hostname', label: 'Hostname' }],
      },
    })
    const text = wrapper.text()

    expect(text).toContain('Hostname')
    expect(text).toContain('gw')
    expect(wrapper.find('[data-test="fields-extra"]').text()).toContain('uptime')
    expect(wrapper.find('[data-test="fields-extra"]').text()).toContain('"a":1')
  })

  it('shows an epoch field as a date and a reading with its unit', () => {
    const wrapper = mount(ReadFieldList, {
      props: {
        data: { last_sync: 1793960419, value: 12.3456 },
        fields: [{ key: 'last_sync', label: 'Sync', format: 'epoch' }, { key: 'value', label: 'V', unit: 'mA' }],
      },
    })

    expect(wrapper.text()).not.toContain('1793960419')
    expect(wrapper.text()).toContain('12.35 mA')
  })

  it('shows a non-object reply as raw JSON', () => {
    const wrapper = mount(ReadFieldList, { props: { data: [1, 2], fields: [] } })

    expect(wrapper.text()).toContain('[1,2]')
  })
})
