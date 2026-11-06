// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { adminApi, type DeviceLinkDto, type DeviceOutputsDto } from '@/core/api/adminApi'
import DeviceOutputsPanel from './DeviceOutputsPanel.vue'

const link = (overrides: Partial<DeviceLinkDto> = {}): DeviceLinkDto => ({
  binding: { topicPrefix: 'lab', mqttDeviceId: 'gw', keyVersion: null },
  keyGeneration: 1,
  commandKeyProvisioned: true,
  connectivity: 'online',
  connectivityAt: null,
  lastHeartbeatAt: null,
  online: true,
  capabilitiesAt: null,
  capabilityCount: 0,
  brokerConnected: true,
  allowedOutputChannels: [1],
  canControlOutputs: true,
  canViewOutputs: true,
  ...overrides,
})

const action = (type: string, name: string) => ({ type, action: name, available: true, dangerous: false, requiresConfirmation: false, featurePermission: '', idempotent: false, defaultTtlMs: 10000 })
const catalog = {
  catalogVersion: 'v', catalogHash: 'h', trusted: true,
  actions: [action('control', 'set_output'), action('control', 'pulse'), action('control', 'get_status')],
}

const state = (channels: Array<[number, boolean, string]>, stale = false): DeviceOutputsDto => ({
  channels: channels.map(([channel, on, mode]) => ({ channel, state: on, mode, pulseActive: false })),
  readAt: '2026-11-06T08:00:00Z',
  stale,
  staleReason: stale ? 'device_offline' : null,
})

const sixOff = state([[1, false, 'toggle'], [2, false, 'toggle'], [3, false, 'toggle'], [4, false, 'toggle'], [5, false, 'toggle'], [6, false, 'pulse']])

function mountPanel(props: Record<string, unknown> = {}) {
  return mount(DeviceOutputsPanel, {
    props: { deviceId: 'dev-1', csrfToken: 'csrf', link: link(), catalog, catalogVerified: true, refreshKey: 0, ...props },
  })
}

const published = (id: string) => ({ status: 'Published', code: 'MQTT_PUBLISHED', correlationId: id })
const result = (id: string, status: string, code: string, detail: string | null = null) =>
  ({ correlationId: id, result: { status, code, correlationId: id, detail }, reply: null })

describe('DeviceOutputsPanel', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => {
    vi.restoreAllMocks()
    vi.useRealTimers()
  })

  it('shows no invented channels before the first read, only a read button', async () => {
    vi.spyOn(adminApi, 'deviceOutputs').mockResolvedValue({ kind: 'not_read' })
    const wrapper = mountPanel()
    await flushPromises()

    expect(wrapper.find('[data-test="do-1-state"]').exists()).toBe(false)
    expect(wrapper.find('[data-test="do-read-status"]').exists()).toBe(true)
  })

  it('offers controls only on allowed channels and only with both device permissions', async () => {
    vi.spyOn(adminApi, 'deviceOutputs').mockResolvedValue({ kind: 'ok', state: sixOff })
    const allowed = mountPanel()
    const noWrite = mountPanel({ link: link({ canControlOutputs: false }) })
    const noView = mountPanel({ link: link({ canViewOutputs: false }) })
    await flushPromises()

    expect(allowed.find('[data-test="do-1-toggle"]').exists()).toBe(true)
    expect(allowed.findAll('[data-test$="-toggle"]')).toHaveLength(1)
    expect(noWrite.find('[data-test="do-1-toggle"]').exists()).toBe(false)
    expect(noView.find('[data-test="do-1-toggle"]').exists()).toBe(false)
  })

  it('locks the channel, reads back after the device acts, and only then shows ON', async () => {
    const outputs = vi.spyOn(adminApi, 'deviceOutputs')
      .mockResolvedValueOnce({ kind: 'ok', state: sixOff })
      .mockResolvedValue({ kind: 'ok', state: state([[1, true, 'toggle']]) })
    const send = vi.spyOn(adminApi, 'sendCommand')
      .mockResolvedValueOnce(published('w1'))
      .mockResolvedValueOnce(published('r1'))
    vi.spyOn(adminApi, 'commandResult').mockImplementation(async (_d, id) =>
      result(id, 'Executed', 'DEVICE_EXECUTED'))
    const wrapper = mountPanel()
    await flushPromises()

    await wrapper.find('[data-test="do-1-toggle"]').trigger('click')
    await flushPromises()
    expect(send.mock.calls[0][1]).toMatchObject({ type: 'control', action: 'set_output', payload: { channel: 1, value: true } })
    expect(wrapper.find('[data-test="do-1-busy"]').exists()).toBe(true)
    expect(wrapper.find('[data-test="do-1-state"]').text()).toBe('OFF')

    await vi.advanceTimersByTimeAsync(1100)
    await flushPromises()
    expect(send.mock.calls[1][1]).toMatchObject({ type: 'control', action: 'get_status' })
    await vi.advanceTimersByTimeAsync(1100)
    await flushPromises()

    expect(outputs).toHaveBeenCalledTimes(2)
    expect(wrapper.find('[data-test="do-1-state"]').text()).toBe('ON')
    expect(wrapper.find('[data-test="do-1-busy"]').exists()).toBe(false)
  })

  it('reads back even when the device outcome is unknown', async () => {
    vi.spyOn(adminApi, 'deviceOutputs').mockResolvedValue({ kind: 'ok', state: sixOff })
    const send = vi.spyOn(adminApi, 'sendCommand')
      .mockResolvedValueOnce(published('w2'))
      .mockResolvedValueOnce(published('r2'))
    vi.spyOn(adminApi, 'commandResult').mockImplementation(async (_d, id) =>
      id === 'w2' ? result(id, 'Failed', 'DEVICE_RESPONSE_TIMEOUT', 'outcome unknown') : result(id, 'Executed', 'DEVICE_EXECUTED'))
    const wrapper = mountPanel()
    await flushPromises()

    await wrapper.find('[data-test="do-1-toggle"]').trigger('click')
    await vi.advanceTimersByTimeAsync(2500)
    await flushPromises()

    expect(send).toHaveBeenCalledTimes(2)
    expect(send.mock.calls[1][1]).toMatchObject({ action: 'get_status' })
    expect(wrapper.find('[data-test="do-1-error"]').text()).toContain('DEVICE_RESPONSE_TIMEOUT')
  })

  it('unlocks at once and skips the read-back when the server refused to send', async () => {
    vi.spyOn(adminApi, 'deviceOutputs').mockResolvedValue({ kind: 'ok', state: sixOff })
    const send = vi.spyOn(adminApi, 'sendCommand')
      .mockResolvedValue({ status: 'ContractUnavailable', code: 'DO_CHANNEL_NOT_ALLOWED', correlationId: 'w3' })
    const wrapper = mountPanel()
    await flushPromises()

    await wrapper.find('[data-test="do-1-toggle"]').trigger('click')
    await flushPromises()

    expect(send).toHaveBeenCalledTimes(1)
    expect(wrapper.find('[data-test="do-1-busy"]').exists()).toBe(false)
    expect(wrapper.find('[data-test="do-1-error"]').text()).toContain('DO_CHANNEL_NOT_ALLOWED')
    expect(wrapper.find('[data-test="do-1-state"]').text()).toBe('OFF')
  })

  it.each([
    ['a server error', () => Promise.reject(new Error('Request failed (500)'))],
    ['a network failure', () => Promise.reject(new TypeError('Failed to fetch'))],
    ['an idempotency outcome the server cannot vouch for',
      () => Promise.resolve({ status: 'Failed', code: 'IDEMPOTENCY_OUTCOME_UNKNOWN', correlationId: 'w4' })],
  ])('keeps the channel locked and reads back after %s', async (_label, dispatch) => {
    vi.spyOn(adminApi, 'deviceOutputs').mockResolvedValue({ kind: 'ok', state: sixOff })
    const send = vi.spyOn(adminApi, 'sendCommand')
      .mockImplementationOnce(dispatch as () => Promise<never>)
      .mockResolvedValueOnce(published('r4'))
    vi.spyOn(adminApi, 'commandResult').mockImplementation(async (_d, id) => result(id, 'Executed', 'DEVICE_EXECUTED'))
    const wrapper = mountPanel()
    await flushPromises()

    await wrapper.find('[data-test="do-1-toggle"]').trigger('click')
    await flushPromises()
    expect(wrapper.find('[data-test="do-1-busy"]').exists()).toBe(true)
    await vi.advanceTimersByTimeAsync(1100)
    await flushPromises()

    expect(send).toHaveBeenCalledTimes(2)
    expect(send.mock.calls[1][1]).toMatchObject({ action: 'get_status' })
  })

  it('waits before the second read-back when the first is turned away as busy', async () => {
    vi.spyOn(adminApi, 'deviceOutputs').mockResolvedValue({ kind: 'ok', state: sixOff })
    const send = vi.spyOn(adminApi, 'sendCommand')
      .mockResolvedValueOnce(published('w6'))
      .mockResolvedValueOnce({ status: 'ContractUnavailable', code: 'DEVICE_READ_BUSY', correlationId: 'r6' })
      .mockResolvedValueOnce(published('r7'))
    vi.spyOn(adminApi, 'commandResult').mockImplementation(async (_d, id) => result(id, 'Executed', 'DEVICE_EXECUTED'))
    const wrapper = mountPanel()
    await flushPromises()

    await wrapper.find('[data-test="do-1-toggle"]').trigger('click')
    await vi.advanceTimersByTimeAsync(1100)
    expect(send).toHaveBeenCalledTimes(2)
    await vi.advanceTimersByTimeAsync(500)
    expect(send).toHaveBeenCalledTimes(2)
    await vi.advanceTimersByTimeAsync(2500)
    await flushPromises()

    expect(send).toHaveBeenCalledTimes(3)
    expect(wrapper.find('[data-test="do-1-busy"]').exists()).toBe(false)
    expect(wrapper.find('[data-test="do-1-error"]').exists()).toBe(false)
  })

  it('keeps a write and its read-back on the device it was sent to, and forgets it on a device switch', async () => {
    vi.spyOn(adminApi, 'deviceOutputs').mockResolvedValue({ kind: 'ok', state: sixOff })
    const send = vi.spyOn(adminApi, 'sendCommand').mockImplementation(async (_d, body) => published(body.action))
    vi.spyOn(adminApi, 'commandResult').mockImplementation(async (_d, id) => result(id, 'Executed', 'DEVICE_EXECUTED'))
    const wrapper = mountPanel()
    await flushPromises()

    await wrapper.find('[data-test="do-1-toggle"]').trigger('click')
    await flushPromises()
    await wrapper.setProps({ deviceId: 'dev-2' })
    await vi.advanceTimersByTimeAsync(5000)
    await flushPromises()

    expect(send.mock.calls.every(c => c[0] === 'dev-1')).toBe(true)
    expect(wrapper.find('[data-test="do-1-busy"]').exists()).toBe(false)
    expect(wrapper.find('[data-test="do-1-error"]').exists()).toBe(false)
  })

  it('says why a manual status read failed', async () => {
    vi.spyOn(adminApi, 'deviceOutputs').mockResolvedValue({ kind: 'not_read' })
    vi.spyOn(adminApi, 'sendCommand').mockResolvedValue({ status: 'ContractUnavailable', code: 'CATALOG_STALE', correlationId: 'r8' })
    const wrapper = mountPanel()
    await flushPromises()

    await wrapper.find('[data-test="do-read-status"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-test="do-read-error"]').text()).toContain('tải lại trang')
  })

  it('tells the operator to reload when the server catalog changed under the page', async () => {
    vi.spyOn(adminApi, 'deviceOutputs').mockResolvedValue({ kind: 'ok', state: sixOff })
    vi.spyOn(adminApi, 'sendCommand').mockResolvedValue({ status: 'ContractUnavailable', code: 'CATALOG_STALE', correlationId: 'w5' })
    const wrapper = mountPanel()
    await flushPromises()

    await wrapper.find('[data-test="do-1-toggle"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-test="do-1-error"]').text()).toContain('tải lại trang')
  })

  it('unlocks at once on a client error, which never reaches the broker', async () => {
    vi.spyOn(adminApi, 'deviceOutputs').mockResolvedValue({ kind: 'ok', state: sixOff })
    const send = vi.spyOn(adminApi, 'sendCommand').mockRejectedValue(new Error('Request failed (403)'))
    const wrapper = mountPanel()
    await flushPromises()

    await wrapper.find('[data-test="do-1-toggle"]').trigger('click')
    await flushPromises()

    expect(send).toHaveBeenCalledTimes(1)
    expect(wrapper.find('[data-test="do-1-busy"]').exists()).toBe(false)
    expect(wrapper.find('[data-test="do-1-error"]').text()).toContain('403')
  })

  it('treats the state as unknown while the device or the broker link is offline', async () => {
    vi.spyOn(adminApi, 'deviceOutputs').mockResolvedValue({ kind: 'ok', state: state([[1, true, 'toggle']]) })
    const deviceOff = mountPanel({ link: link({ online: false }) })
    const brokerOff = mountPanel({ link: link({ brokerConnected: false }) })
    await flushPromises()

    for (const wrapper of [deviceOff, brokerOff]) {
      expect(wrapper.find('[data-test="do-1-state"]').text()).toBe('Không rõ')
      expect(wrapper.find('[data-test="do-offline"]').exists()).toBe(true)
      expect(wrapper.find('[data-test="do-1-off"]').exists()).toBe(true)
    }
  })

  it('hides the controls when the state can no longer be loaded', async () => {
    vi.spyOn(adminApi, 'deviceOutputs')
      .mockResolvedValueOnce({ kind: 'ok', state: sixOff })
      .mockRejectedValueOnce(new Error('Request failed (500)'))
    const wrapper = mountPanel()
    await flushPromises()

    await wrapper.setProps({ refreshKey: 1 })
    await flushPromises()

    expect(wrapper.find('[data-test="do-1-toggle"]').exists()).toBe(false)
    expect(wrapper.find('[data-test="do-1-state"]').exists()).toBe(false)
  })

  it('keeps the newest state when an older load answers last', async () => {
    let answerOld!: (v: unknown) => void
    vi.spyOn(adminApi, 'deviceOutputs')
      .mockResolvedValueOnce({ kind: 'ok', state: sixOff })
      .mockImplementationOnce(() => new Promise(resolve => { answerOld = resolve as (v: unknown) => void }) as never)
      .mockResolvedValueOnce({ kind: 'ok', state: state([[1, true, 'toggle']]) })
    const wrapper = mountPanel()
    await flushPromises()

    await wrapper.setProps({ refreshKey: 1 })
    await wrapper.setProps({ refreshKey: 2 })
    await flushPromises()
    answerOld({ kind: 'ok', state: sixOff })
    await flushPromises()

    expect(wrapper.find('[data-test="do-1-state"]').text()).toBe('ON')
  })

  it('refuses a pulse duration outside the allowed range without sending', async () => {
    vi.spyOn(adminApi, 'deviceOutputs').mockResolvedValue({ kind: 'ok', state: state([[6, false, 'pulse']]) })
    const send = vi.spyOn(adminApi, 'sendCommand')
    const wrapper = mountPanel({ link: link({ allowedOutputChannels: [6] }) })
    await flushPromises()

    await wrapper.find('[data-test="do-6-pulse-ms"]').setValue('0')
    await wrapper.find('[data-test="do-6-pulse"]').trigger('click')
    await flushPromises()

    expect(send).not.toHaveBeenCalled()
    expect(wrapper.find('[data-test="do-6-error"]').exists()).toBe(true)
  })

  it('shows unknown state with both ON and OFF when the state is stale', async () => {
    vi.spyOn(adminApi, 'deviceOutputs').mockResolvedValue({ kind: 'ok', state: state([[1, true, 'toggle']], true) })
    const wrapper = mountPanel()
    await flushPromises()

    expect(wrapper.find('[data-test="do-1-state"]').text()).toBe('Không rõ')
    expect(wrapper.find('[data-test="do-1-on"]').exists()).toBe(true)
    expect(wrapper.find('[data-test="do-1-off"]').exists()).toBe(true)
    expect(wrapper.find('[data-test="do-stale"]').exists()).toBe(true)
  })

  it('offers pulse only in pulse mode and reads back again after the pulse ends', async () => {
    vi.spyOn(adminApi, 'deviceOutputs').mockResolvedValue({ kind: 'ok', state: state([[1, false, 'toggle'], [6, false, 'pulse']]) })
    const send = vi.spyOn(adminApi, 'sendCommand').mockImplementation(async (_d, body) => published(`${body.action}-${send.mock.calls.length}`))
    vi.spyOn(adminApi, 'commandResult').mockImplementation(async (_d, id) => result(id, 'Executed', 'DEVICE_EXECUTED'))
    const wrapper = mountPanel({ link: link({ allowedOutputChannels: [1, 6] }) })
    await flushPromises()

    expect(wrapper.find('[data-test="do-1-pulse"]').exists()).toBe(false)
    await wrapper.find('[data-test="do-6-pulse"]').trigger('click')
    await vi.advanceTimersByTimeAsync(2500)
    await flushPromises()
    expect(send.mock.calls.map(x => x[1].action)).toEqual(['pulse', 'get_status'])
    expect(send.mock.calls[0][1]).toMatchObject({ payload: { channel: 6, duration_ms: 1000 } })

    await vi.advanceTimersByTimeAsync(3500)
    await flushPromises()
    expect(send.mock.calls.map(x => x[1].action)).toEqual(['pulse', 'get_status', 'get_status'])
  })

  it('reloads the state when the server announces an outputs change', async () => {
    const outputs = vi.spyOn(adminApi, 'deviceOutputs').mockResolvedValue({ kind: 'ok', state: sixOff })
    const wrapper = mountPanel()
    await flushPromises()

    await wrapper.setProps({ refreshKey: 1 })
    await flushPromises()

    expect(outputs).toHaveBeenCalledTimes(2)
  })
})
