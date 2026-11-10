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
import { fakeDevice, type Scenario } from '@/core/commands/testing/fakeDevice'

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

describe('config writes on the read cards', () => {
  const WRITES = ['system.set_hostname', 'system.set_timezone', 'system.sync_ntp', 'adc.set_channel', 'adc.toggle_channel']
  const configAllowed = [...allowed, ...WRITES, 'system.get_operations']

  interface Sim {
    hostname: string
    timezone: string
    epochSkewS: number
    channels: Record<string, unknown>[]
    adcConfig: Record<string, unknown>
  }
  let sim: Sim
  let sent: ReturnType<typeof fakeDevice>
  const count = (key: string) => sent.filter(s => s.key === key).length

  /** A device that applies writes unless `override` says otherwise. */
  function startDevice(override?: (key: string, payload: Record<string, unknown> | undefined) => Scenario | undefined) {
    sim = {
      hostname: 'maitech-rtu', timezone: 'ICT-7', epochSkewS: 0, adcConfig: { available: true, channel_count: 2, hardware: 'ads1115' },
      channels: [{ ai: 1, name: 'Áp suất', type: '4-20mA', enabled: true }, { ai: 2, name: 'Nhiệt độ', type: '0-10V', enabled: false }],
    }
    sent = fakeDevice((key, payload) => {
      const forced = override?.(key, payload)
      if (forced) return forced
      switch (key) {
        case 'system.get_time':
          return { data: { ...(replies['system.get_time'] as object), timezone: sim.timezone, epoch: Math.floor(Date.now() / 1000) - sim.epochSkewS } }
        case 'system.get_hostname': return { data: { hostname: sim.hostname } }
        case 'adc.get_config': return { data: sim.adcConfig }
        case 'adc.get_channels': return { data: sim.channels.map(c => ({ ...c })) }
        case 'system.set_hostname': sim.hostname = String(payload?.hostname); return { data: {} }
        case 'system.set_timezone': sim.timezone = String(payload?.timezone); return { data: {} }
        case 'system.sync_ntp': return { data: {} }
        case 'adc.set_channel': {
          Object.assign(sim.channels.find(c => c.ai === payload?.ai)!, { name: payload?.name })
          return { data: {} }
        }
        case 'adc.toggle_channel': {
          sim.channels.find(c => c.ai === payload?.ai)!.enabled = payload?.enabled
          return { data: {} }
        }
        default: return { data: replies[key] }
      }
    })
  }

  async function mountRead(component: Component, props: Record<string, unknown> = {}): Promise<VueWrapper> {
    const wrapper = mount(component, {
      props: { deviceId: 'dev-1', csrfToken: 'csrf', catalog, catalogVerified: true, allowedCommands: configAllowed, link, ...props },
    })
    await wrapper.find('[data-test="read-start"]').trigger('click')
    await vi.advanceTimersByTimeAsync(20_000)
    await flushPromises()
    return wrapper
  }
  async function settle(ms = 20_000) {
    await vi.advanceTimersByTimeAsync(ms)
    await flushPromises()
  }
  const result = (w: VueWrapper) => w.find('[data-test="config-result"]').text()

  async function editHostname(w: VueWrapper, value: string) {
    await w.find('[data-test="hostname-edit"]').trigger('click')
    await w.find('[data-test="hostname-input"]').setValue(value)
    await w.find('[data-test="hostname-submit"]').trigger('click')
  }
  const confirm = (w: VueWrapper) => w.find('[data-test="config-confirm-ok"]').trigger('click')

  beforeEach(() => vi.useFakeTimers())
  afterEach(() => {
    vi.restoreAllMocks()
    vi.useRealTimers()
  })

  describe('when a control is enabled', () => {
    it('is enabled with fresh read, permissions, catalog and an online link', async () => {
      startDevice()
      const wrapper = await mountRead(DeviceInfoTimeCard)

      expect(wrapper.find('[data-test="hostname-edit"]').attributes('disabled')).toBeUndefined()
      expect(wrapper.find('[data-test="lock-reason-hostname"]').exists()).toBe(false)
    })

    it.each([
      ['the write command', 'system.set_hostname'],
      ['a re-read step', 'system.get_hostname'],
      ['system.get_operations', 'system.get_operations'],
    ])('is locked, with the reason, when %s is not allowed', async (_name, missing) => {
      startDevice()
      const wrapper = await mountRead(DeviceInfoTimeCard, { allowedCommands: configAllowed.filter(k => k !== missing) })

      expect(wrapper.find('[data-test="hostname-edit"]').attributes('disabled')).toBeDefined()
      expect(wrapper.find('[data-test="lock-reason-hostname"]').text()).toContain(`Thiếu quyền ${missing}`)
    })

    it('asks to read again before editing when nothing was read yet', async () => {
      startDevice()
      const wrapper = mount(DeviceInfoTimeCard, {
        props: { deviceId: 'dev-1', csrfToken: 'csrf', catalog, catalogVerified: true, allowedCommands: configAllowed, link },
      })
      await flushPromises()

      expect(wrapper.find('[data-test="hostname-edit"]').attributes('disabled')).toBeDefined()
      expect(wrapper.find('[data-test="lock-reason-hostname"]').text()).toContain('Đọc lại trước khi sửa')
    })

    it('locks again when the last read is older than 60 s', async () => {
      startDevice()
      const wrapper = await mountRead(DeviceInfoTimeCard)

      await vi.advanceTimersByTimeAsync(61_000)
      await flushPromises()

      expect(wrapper.find('[data-test="hostname-edit"]').attributes('disabled')).toBeDefined()
      expect(wrapper.find('[data-test="lock-reason-hostname"]').text()).toContain('Đọc lại trước khi sửa')
    })

    it('locks when the page saw a configuration change newer than the read', async () => {
      startDevice()
      const wrapper = await mountRead(DeviceInfoTimeCard)

      await wrapper.setProps({ configChangedAt: Date.now() + 1 })

      expect(wrapper.find('[data-test="hostname-edit"]').attributes('disabled')).toBeDefined()
      expect(wrapper.find('[data-test="lock-reason-hostname"]').text()).toContain('Đọc lại trước khi sửa')
    })

    it('says the device is offline', async () => {
      startDevice()
      const wrapper = await mountRead(DeviceInfoTimeCard, { link: { ...link, online: false } })

      expect(wrapper.find('[data-test="lock-reason-hostname"]').text()).toContain('Thiết bị offline')
    })

    it('rejects an invalid hostname before the confirm box', async () => {
      startDevice()
      const wrapper = await mountRead(DeviceInfoTimeCard)

      await wrapper.find('[data-test="hostname-edit"]').trigger('click')
      await wrapper.find('[data-test="hostname-input"]').setValue('bad_name')

      expect(wrapper.find('[data-test="hostname-error"]').exists()).toBe(true)
      expect(wrapper.find('[data-test="hostname-submit"]').attributes('disabled')).toBeDefined()
    })
  })

  describe('device and time card', () => {
    it('confirms old to new, writes once, re-reads the hostname and says it is confirmed', async () => {
      startDevice()
      const wrapper = await mountRead(DeviceInfoTimeCard)
      const readsBefore = count('system.get_hostname')

      await editHostname(wrapper, 'new-gw')
      const box = wrapper.find('[data-test="config-confirm"]').text()
      expect(box).toContain('maitech-rtu')
      expect(box).toContain('new-gw')
      expect(count('system.set_hostname')).toBe(0)

      await confirm(wrapper)
      await settle()

      expect(count('system.set_hostname')).toBe(1)
      expect(sent.find(s => s.key === 'system.set_hostname')!.payload).toEqual({ hostname: 'new-gw' })
      expect(count('system.get_hostname')).toBe(readsBefore + 1)
      expect(result(wrapper)).toContain('Đã ghi và xác nhận')
      expect(wrapper.find('[data-test="section-system.get_hostname"]').text()).toContain('new-gw')
    })

    it('closes the confirm box with Esc and sends nothing', async () => {
      startDevice()
      const wrapper = await mountRead(DeviceInfoTimeCard)

      await editHostname(wrapper, 'new-gw')
      await wrapper.find('[data-test="config-confirm"]').trigger('keydown', { key: 'Escape' })

      expect(wrapper.find('[data-test="config-confirm"]').exists()).toBe(false)
      expect(count('system.set_hostname')).toBe(0)
    })

    it('warns with the value read back when the device did not apply an executed write', async () => {
      startDevice(key => key === 'system.set_hostname' ? { data: {} } : undefined)
      const wrapper = await mountRead(DeviceInfoTimeCard)

      await editHostname(wrapper, 'new-gw')
      await confirm(wrapper)
      await settle()

      expect(result(wrapper)).not.toContain('Đã ghi và xác nhận')
      expect(result(wrapper)).toContain('maitech-rtu')
      expect(wrapper.find('[data-test="config-result"]').attributes('data-tone')).toBe('warn')
    })

    it('reads back and offers no resend when the outcome is unknown', async () => {
      startDevice(key => key === 'system.set_hostname' ? { throws: new Error('Request failed (500)') } : undefined)
      const wrapper = await mountRead(DeviceInfoTimeCard)
      const readsBefore = count('system.get_hostname')

      await editHostname(wrapper, 'new-gw')
      await confirm(wrapper)
      await settle()

      expect(result(wrapper)).toContain('Chưa rõ kết quả; đã đọc lại trạng thái')
      expect(count('system.get_hostname')).toBe(readsBefore + 1)
      expect(count('system.set_hostname')).toBe(1)
      expect(wrapper.text()).not.toContain('Gửi lại')
    })

    it.each([
      ['DEVICE_WRITE_BUSY', 'Thiết bị đang xử lý một lệnh ghi khác, thử lại sau'],
      ['DEVICE_WRITE_RATE_LIMITED', 'Đã ghi quá nhiều lần trong một phút, thử lại sau'],
      ['COMMAND_PAYLOAD_INVALID', 'Giá trị không hợp lệ'],
      ['CATALOG_STALE', 'Catalog lệnh trên server đã đổi, hãy tải lại trang'],
    ])('does not read back a write the server refused (%s)', async (code, text) => {
      startDevice(key => key === 'system.set_hostname' ? { dispatch: { status: 'ContractUnavailable', code } } : undefined)
      const wrapper = await mountRead(DeviceInfoTimeCard)
      const readsBefore = count('system.get_hostname')

      await editHostname(wrapper, 'new-gw')
      await confirm(wrapper)
      await settle()

      expect(result(wrapper)).toContain(text)
      expect(count('system.get_hostname')).toBe(readsBefore)
    })

    it('says the request was refused for an HTTP 4xx and does not read back', async () => {
      startDevice(key => key === 'system.set_hostname' ? { throws: new Error('Request failed (403)') } : undefined)
      const wrapper = await mountRead(DeviceInfoTimeCard)
      const readsBefore = count('system.get_hostname')

      await editHostname(wrapper, 'new-gw')
      await confirm(wrapper)
      await settle()

      expect(result(wrapper)).toContain('Yêu cầu bị từ chối')
      expect(count('system.get_hostname')).toBe(readsBefore)
    })

    it('says permission is missing for a permission refusal and still reads back', async () => {
      startDevice(key => key === 'system.set_hostname' ? { dispatch: { status: 'Failed', code: 'DEVICE_PERMISSION_REQUIRED' } } : undefined)
      const wrapper = await mountRead(DeviceInfoTimeCard)
      const readsBefore = count('system.get_hostname')

      await editHostname(wrapper, 'new-gw')
      await confirm(wrapper)
      await settle()

      expect(result(wrapper)).toContain('Thiếu quyền')
      expect(count('system.get_hostname')).toBe(readsBefore + 1)
    })

    it('shows the device error of a rejection together with the value read back', async () => {
      startDevice(key => key === 'system.set_hostname'
        ? { result: { status: 'Failed', code: 'DEVICE_REJECTED', detail: 'INVALID_HOSTNAME' }, data: { error: 'hostname refused' } } : undefined)
      const wrapper = await mountRead(DeviceInfoTimeCard)

      await editHostname(wrapper, 'new-gw')
      await confirm(wrapper)
      await settle()

      expect(result(wrapper)).toContain('DEVICE_REJECTED')
      expect(result(wrapper)).toContain('hostname refused')
      expect(result(wrapper)).toContain('maitech-rtu')
    })

    it('says saved-not-applied when the device did not apply at once, without calling it a mismatch', async () => {
      startDevice(key => key === 'system.set_hostname' ? { data: { applied_immediately: false } } : undefined)
      const wrapper = await mountRead(DeviceInfoTimeCard)

      await editHostname(wrapper, 'new-gw')
      await confirm(wrapper)
      await settle()

      expect(result(wrapper)).toContain('Đã lưu, chưa áp dụng')
      expect(result(wrapper)).not.toContain('khác')
    })

    it('tells that the device recommends a reboot, which a master-admin does from the reboot button', async () => {
      startDevice((key, payload) => {
        if (key !== 'system.set_hostname') return undefined
        sim.hostname = String(payload?.hostname)
        return { data: { applied_immediately: true, reboot_recommended: true } }
      })
      const wrapper = await mountRead(DeviceInfoTimeCard)

      await editHostname(wrapper, 'new-gw')
      await confirm(wrapper)
      await settle()

      expect(result(wrapper)).toContain('Thiết bị khuyên khởi động lại; master-admin dùng nút Khởi động lại trên trang')
    })

    it('changes the timezone and re-reads system.get_time', async () => {
      startDevice()
      const wrapper = await mountRead(DeviceInfoTimeCard)
      const readsBefore = count('system.get_time')

      await wrapper.find('[data-test="timezone-select"]').setValue('UTC0')
      await wrapper.find('[data-test="timezone-submit"]').trigger('click')
      expect(wrapper.find('[data-test="config-confirm"]').text()).toContain('ICT-7')
      await confirm(wrapper)
      await settle()

      expect(sent.find(s => s.key === 'system.set_timezone')!.payload).toEqual({ timezone: 'UTC0' })
      expect(count('system.get_time')).toBe(readsBefore + 1)
      expect(result(wrapper)).toContain('Đã ghi và xác nhận')
    })

    it('shows a device timezone outside the list as it is', async () => {
      startDevice()
      const wrapper = mount(DeviceInfoTimeCard, {
        props: { deviceId: 'dev-1', csrfToken: 'csrf', catalog, catalogVerified: true, allowedCommands: configAllowed, link },
      })
      sim.timezone = 'CET-1'
      await wrapper.find('[data-test="read-start"]').trigger('click')
      await settle()

      expect(wrapper.find('[data-test="timezone-select"]').text()).toContain('CET-1')
    })

    it('syncs NTP with an empty payload, re-reads system.get_time and shows ntp_synced', async () => {
      startDevice()
      const wrapper = await mountRead(DeviceInfoTimeCard)
      const readsBefore = count('system.get_time')

      await wrapper.find('[data-test="ntp-sync"]').trigger('click')
      await confirm(wrapper)
      await settle()

      expect(sent.find(s => s.key === 'system.sync_ntp')!.payload).toEqual({})
      expect(count('system.get_time')).toBe(readsBefore + 1)
      expect(result(wrapper)).toContain('Đã đồng bộ NTP')
    })

    it('shows the NTP servers but offers no way to edit them', async () => {
      startDevice()
      const wrapper = await mountRead(DeviceInfoTimeCard)

      expect(wrapper.find('[data-test="ntp-servers"]').exists()).toBe(true)
      expect(wrapper.find('[data-test="ntp-servers"] input').exists()).toBe(false)
      expect(wrapper.find('[data-test="ntp-servers"] button').exists()).toBe(false)
    })

    it('warns when the device clock is 45 s off the browser clock after a write', async () => {
      startDevice()
      const wrapper = await mountRead(DeviceInfoTimeCard)
      sim.epochSkewS = 45

      await wrapper.find('[data-test="ntp-sync"]').trigger('click')
      await confirm(wrapper)
      await settle()

      expect(wrapper.find('[data-test="clock-warning"]').text()).toMatch(/Đồng hồ thiết bị lệch \d+ s; lệnh sau có thể bị từ chối/)
    })

    it('does not warn about the clock when it is close', async () => {
      startDevice()
      const wrapper = await mountRead(DeviceInfoTimeCard)

      await wrapper.find('[data-test="ntp-sync"]').trigger('click')
      await confirm(wrapper)
      await settle()

      expect(wrapper.find('[data-test="clock-warning"]').exists()).toBe(false)
    })

    it('shows nothing of the old device when the page changes device during a write', async () => {
      startDevice()
      const wrapper = await mountRead(DeviceInfoTimeCard)

      await editHostname(wrapper, 'new-gw')
      await confirm(wrapper)
      await vi.advanceTimersByTimeAsync(500)
      await wrapper.setProps({ deviceId: 'dev-2' })
      await settle()

      expect(result(wrapper)).toBe('')
      expect(wrapper.find('[data-test="config-confirm"]').exists()).toBe(false)
    })
  })

  describe('power and ADC card', () => {
    async function renameChannel(w: VueWrapper, ai: number, name: string) {
      await w.find(`[data-test="adc-rename-${ai}"]`).trigger('click')
      await w.find(`[data-test="adc-name-input-${ai}"]`).setValue(name)
      await w.find(`[data-test="adc-name-submit-${ai}"]`).trigger('click')
    }

    it('renames with the whole channel record: ai, name, and type and enabled from the last read', async () => {
      startDevice()
      const wrapper = await mountRead(DevicePowerAdcCard)

      await renameChannel(wrapper, 1, 'Áp suất bồn')
      expect(wrapper.find('[data-test="config-confirm"]').text()).toContain('Áp suất')
      expect(wrapper.find('[data-test="config-confirm"]').text()).toContain('Áp suất bồn')
      await confirm(wrapper)
      await settle()

      expect(count('adc.set_channel')).toBe(1)
      expect(sent.find(s => s.key === 'adc.set_channel')!.payload)
        .toEqual({ ai: 1, name: 'Áp suất bồn', type: '4-20mA', enabled: true })
      expect(result(wrapper)).toContain('Đã ghi và xác nhận')
      expect(wrapper.find('[data-test="adc-row-1"]').text()).toContain('Áp suất bồn')
    })

    it('warns and names the field when the re-read channel differs in a field the user did not change', async () => {
      startDevice((key, payload) => {
        if (key !== 'adc.set_channel') return undefined
        Object.assign(sim.channels.find(c => c.ai === payload?.ai)!, { name: payload?.name, type: '0-10V' })
        return { data: {} }
      })
      const wrapper = await mountRead(DevicePowerAdcCard)

      await renameChannel(wrapper, 1, 'Áp suất bồn')
      await confirm(wrapper)
      await settle()

      expect(result(wrapper)).toContain('type')
      expect(wrapper.find('[data-test="config-result"]').attributes('data-tone')).toBe('warn')
    })

    it('toggles with {ai, enabled} and re-reads channels and values', async () => {
      startDevice()
      const wrapper = await mountRead(DevicePowerAdcCard)
      const channelReads = count('adc.get_channels')
      const valueReads = count('adc.get_values')

      await wrapper.find('[data-test="adc-toggle-2"]').trigger('click')
      await confirm(wrapper)
      await settle()

      expect(sent.find(s => s.key === 'adc.toggle_channel')!.payload).toEqual({ ai: 2, enabled: true })
      expect(count('adc.get_channels')).toBe(channelReads + 1)
      expect(count('adc.get_values')).toBe(valueReads + 1)
      expect(result(wrapper)).toContain('Đã ghi và xác nhận')
    })

    it('has no controls when the ADC reports no hardware', async () => {
      startDevice()
      sim.adcConfig = { available: false }
      const wrapper = await mountRead(DevicePowerAdcCard)

      expect(wrapper.find('[data-test="adc-rename-1"]').exists()).toBe(false)
      expect(wrapper.find('[data-test="adc-toggle-1"]').exists()).toBe(false)
    })

    it('locks the controls with a reason while the write permission is missing', async () => {
      startDevice()
      const wrapper = await mountRead(DevicePowerAdcCard, { allowedCommands: configAllowed.filter(k => k !== 'adc.set_channel') })

      expect(wrapper.find('[data-test="adc-rename-1"]').attributes('disabled')).toBeDefined()
      expect(wrapper.find('[data-test="lock-reason-adc-rename"]').text()).toContain('Thiếu quyền adc.set_channel')
      expect(wrapper.find('[data-test="adc-toggle-1"]').attributes('disabled')).toBeUndefined()
    })
  })
})
