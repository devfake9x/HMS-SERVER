// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { h } from 'vue'
import { mount, flushPromises } from '@vue/test-utils'
import { adminApi, type DeviceLinkDto } from '@/core/api/adminApi'
import DeviceReadCard from './DeviceReadCard.vue'

const catalog = { catalogVersion: 'v', catalogHash: 'h', trusted: true, actions: [] }
const link = (overrides: Partial<DeviceLinkDto> = {}): DeviceLinkDto => ({
  binding: { topicPrefix: 'lab', mqttDeviceId: 'gw', keyVersion: null },
  keyGeneration: 1, commandKeyProvisioned: true, connectivity: 'online', connectivityAt: null,
  lastHeartbeatAt: null, online: true, capabilitiesAt: null, capabilityCount: 0, brokerConnected: true,
  allowedOutputChannels: [], canControlOutputs: false, canViewOutputs: true,
  ...overrides,
})
const steps = [{ type: 'system', action: 'get_time' }, { type: 'system', action: 'get_hostname' }]
const published = (id: string) => ({ status: 'Published', code: 'MQTT_PUBLISHED', correlationId: id })
const executed = (id: string, data: unknown) => ({
  correlationId: id,
  result: { status: 'Executed', code: 'DEVICE_EXECUTED', correlationId: id },
  reply: { type: 'system', action: 'x', success: true, error: null, data, receivedAt: '2026-11-06T00:00:00Z' },
})

function mountCard(props: Record<string, unknown> = {}) {
  return mount(DeviceReadCard, {
    props: {
      title: 'Thiết bị', steps, deviceId: 'dev-1', csrfToken: 'csrf', catalog, catalogVerified: true,
      allowedCommands: ['system.get_time', 'system.get_hostname'], link: link(), ...props,
    },
    slots: {
      default: (scope: { states: Record<string, { kind: string; data?: unknown }> }) =>
        h('pre', { 'data-test': 'slot' }, JSON.stringify(scope.states)),
    },
  })
}

describe('DeviceReadCard', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => {
    vi.restoreAllMocks()
    vi.useRealTimers()
  })

  it('sends nothing until the read button is pressed', async () => {
    const send = vi.spyOn(adminApi, 'sendCommand')
    const wrapper = mountCard()
    await flushPromises()

    expect(send).not.toHaveBeenCalled()
    expect(wrapper.find('[data-test="read-start"]').exists()).toBe(true)
  })

  it('locks the button while reading, offers stop, and fills the slot afterwards', async () => {
    vi.spyOn(adminApi, 'sendCommand').mockImplementation(async (_d, body) => published(body.action))
    vi.spyOn(adminApi, 'commandResult').mockImplementation(async (_d, id) => executed(id, { id }))
    const wrapper = mountCard()

    await wrapper.find('[data-test="read-start"]').trigger('click')
    expect(wrapper.find('[data-test="read-start"]').attributes('disabled')).toBeDefined()
    expect(wrapper.find('[data-test="read-stop"]').exists()).toBe(true)
    await vi.advanceTimersByTimeAsync(5000)
    await flushPromises()

    expect(wrapper.find('[data-test="slot"]').text()).toContain('"kind":"ok"')
    expect(wrapper.find('[data-test="read-start"]').attributes('disabled')).toBeUndefined()
  })

  it('never sends a command the caller may not send, and says so', async () => {
    const send = vi.spyOn(adminApi, 'sendCommand').mockImplementation(async (_d, body) => published(body.action))
    vi.spyOn(adminApi, 'commandResult').mockImplementation(async (_d, id) => executed(id, {}))
    const wrapper = mountCard({ allowedCommands: ['system.get_time'] })

    await wrapper.find('[data-test="read-start"]').trigger('click')
    await vi.advanceTimersByTimeAsync(3000)
    await flushPromises()

    expect(send.mock.calls.map(c => c[1].action)).toEqual(['get_time'])
    expect(wrapper.find('[data-test="read-missing-system.get_hostname"]').exists()).toBe(true)
  })

  it('hides the read button when nothing may be sent or the catalog is not trusted', async () => {
    expect(mountCard({ allowedCommands: [] }).find('[data-test="read-start"]').exists()).toBe(false)
    expect(mountCard({ allowedCommands: null }).find('[data-test="read-start"]').exists()).toBe(false)
    expect(mountCard({ catalogVerified: false }).find('[data-test="read-start"]').exists()).toBe(false)
  })

  it('tells the operator to reload when the server catalog changed', async () => {
    vi.spyOn(adminApi, 'sendCommand').mockResolvedValue({ status: 'ContractUnavailable', code: 'CATALOG_STALE', correlationId: 'x' })
    const wrapper = mountCard()

    await wrapper.find('[data-test="read-start"]').trigger('click')
    await vi.advanceTimersByTimeAsync(100)
    await flushPromises()

    expect(wrapper.find('[data-test="read-catalog-stale"]').text()).toContain('tải lại trang')
  })

  it('warns that data may be old while the device or the broker link is offline', async () => {
    expect(mountCard({ link: link({ online: false }) }).find('[data-test="read-offline"]').exists()).toBe(true)
    expect(mountCard({ link: link({ brokerConnected: false }) }).find('[data-test="read-offline"]').exists()).toBe(true)
    expect(mountCard().find('[data-test="read-offline"]').exists()).toBe(false)
  })

  it('forgets everything and stops reading when the device changes', async () => {
    const send = vi.spyOn(adminApi, 'sendCommand').mockImplementation(async (_d, body) => published(body.action))
    vi.spyOn(adminApi, 'commandResult').mockImplementation(async (_d, id) => executed(id, {}))
    const wrapper = mountCard()
    await wrapper.find('[data-test="read-start"]').trigger('click')
    await vi.advanceTimersByTimeAsync(5000)
    await flushPromises()

    await wrapper.setProps({ deviceId: 'dev-2' })
    await flushPromises()

    expect(wrapper.find('[data-test="slot"]').text()).toBe('{}')
    expect(send).toHaveBeenCalledTimes(2)
  })
})

describe('DeviceReadCard refresh and lockReads', () => {
  type Scope = {
    states: Record<string, { kind: string; data?: unknown }>
    refresh: (list: { type: string; action: string }[]) => Promise<Record<string, { kind: string; data?: unknown; readAt?: string }>>
    lockReads: (on: boolean) => void
  }
  let scope: Scope
  function mountCapturing(props: Record<string, unknown> = {}) {
    return mount(DeviceReadCard, {
      props: {
        title: 'Thiết bị', steps, deviceId: 'dev-1', csrfToken: 'csrf', catalog, catalogVerified: true,
        allowedCommands: ['system.get_time', 'system.get_hostname'], link: link(), ...props,
      },
      slots: { default: (s: Scope) => { scope = s; return h('span') } },
    })
  }

  beforeEach(() => vi.useFakeTimers())
  afterEach(() => {
    vi.restoreAllMocks()
    vi.useRealTimers()
  })

  it('refresh reads again even while the card is reading, and returns the new states', async () => {
    let n = 0
    const send = vi.spyOn(adminApi, 'sendCommand').mockImplementation(async (_d, body) => published(`${body.action}-${++n}`))
    vi.spyOn(adminApi, 'commandResult').mockImplementation(async (_d, id) => executed(id, { id }))
    const wrapper = mountCapturing()
    await wrapper.find('[data-test="read-start"]').trigger('click')
    await vi.advanceTimersByTimeAsync(100)

    const refreshing = scope.refresh(steps)
    await vi.advanceTimersByTimeAsync(5000)
    const states = await refreshing

    expect(send.mock.calls.map(c => c[1].action)).toEqual(['get_time', 'get_time', 'get_hostname'])
    expect(states['system.get_time'].kind).toBe('ok')
    expect(states['system.get_hostname'].kind).toBe('ok')
    expect((states['system.get_time'].data as { id: string }).id).toBe('get_time-2')
    expect(states['system.get_time'].readAt).toBeDefined()
  })

  it('refresh only sends the steps the caller may send', async () => {
    const send = vi.spyOn(adminApi, 'sendCommand').mockImplementation(async (_d, body) => published(body.action))
    vi.spyOn(adminApi, 'commandResult').mockImplementation(async (_d, id) => executed(id, {}))
    const wrapper = mountCapturing({ allowedCommands: ['system.get_hostname'] })
    await flushPromises()

    const refreshing = scope.refresh(steps)
    await vi.advanceTimersByTimeAsync(5000)
    const states = await refreshing

    expect(send.mock.calls.map(c => c[1].action)).toEqual(['get_hostname'])
    expect(Object.keys(states)).toEqual(['system.get_hostname'])
    wrapper.unmount()
  })

  it('lockReads(true) locks the read and stop buttons, lockReads(false) frees them', async () => {
    const wrapper = mountCapturing()

    scope.lockReads(true)
    await flushPromises()
    expect(wrapper.find('[data-test="read-start"]').attributes('disabled')).toBeDefined()

    scope.lockReads(false)
    await flushPromises()
    expect(wrapper.find('[data-test="read-start"]').attributes('disabled')).toBeUndefined()
  })

  it('says the configuration changed when the page marks a change newer than every read', async () => {
    const wrapper = mountCapturing({ configChangedAt: Date.now() })
    await flushPromises()

    expect(wrapper.find('[data-test="read-config-changed"]').text()).toContain('Cấu hình vừa đổi, hãy đọc lại')
  })
})
