// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import DeviceDetailView from './DeviceDetailView.vue'
import { adminApi } from '@/core/api/adminApi'
import { runDeviceWrite } from '@/core/commands/useDeviceWrite'

const hubHandlers = vi.hoisted(() => new Map<string, (payload: unknown) => void>())
// Reactive so a test can move the page to another device, as the router does for /devices/:id.
const mockRoute = vi.hoisted(() => ({ value: null as null | { params: { id: string; tab?: string } } }))

const mockRouter = {
  push: vi.fn(),
  replace: vi.fn().mockResolvedValue(undefined),
}

vi.mock('vue-router', async () => {
  const { reactive } = await import('vue')
  mockRoute.value = reactive({ params: { id: 'dev-101' } })
  return {
    useRouter: () => mockRouter,
    useRoute: () => mockRoute.value,
  }
})

vi.mock('@/core/api/adminApi', () => ({
  adminApi: {
    listDevices: vi.fn().mockResolvedValue([
      { id: 'dev-101', name: 'Concrete Sensor Station 1', state: 'Active', ownerOrganizationId: 'org-1', areaId: 'area-1', keyGeneration: 2 },
    ]),
    permissions: vi.fn().mockResolvedValue({
      policyVersion: 1,
      expiresAt: '2099-01-01',
      permissions: ['devices.manage', 'commands.c2.reboot'],
      deniedPermissions: [],
    }),
    commandCatalog: vi.fn().mockResolvedValue({
      catalogVersion: '1.0.0',
      catalogHash: 'hash-abc',
      trusted: true,
      actions: [{ type: 'system', action: 'reboot', featurePermission: 'commands.c2.reboot' }],
    }),
    realtimeAuthorization: vi.fn().mockResolvedValue({ authorizationEpoch: 1 }),
    devicePermissions: vi.fn().mockResolvedValue({
      deviceId: 'dev-101',
      deviceName: 'Concrete Sensor Station 1',
      policyVersion: 1,
      permissions: [
        { key: 'view', title: 'View', description: 'Can view the device and its data' },
      ],
      users: [],
    }),
    deviceTelemetry: vi.fn().mockResolvedValue({
      deviceId: 'dev-101',
      timestamp: '2026-09-30T16:00:00Z',
      uptimeSeconds: 439266,
      freeHeap: 1466815,
      minFreeHeap: 681248,
      psramFree: 1374100,
      psramTotal: 3840256,
      mqttConnected: true,
      networkConnected: true,
      tagCount: 62,
      serviceCount: 23,
      activeUplink: 'Ethernet',
      data: {
        uptime_seconds: 439266,
        free_heap: 1466815,
        network: { active_uplink: 'Ethernet', ethernet: { ip: '192.168.1.127' } },
        services: [{ name: 'mqtt', health: 'HEALTHY' }],
        digital_outputs: [{ channel: 1, state: false }, { channel: 2, state: true }],
      },
    }),
    deviceLink: vi.fn().mockResolvedValue({
      binding: { topicPrefix: 'lab_prefix', mqttDeviceId: 'gw_101', keyVersion: 3 },
      keyGeneration: 2,
      commandKeyProvisioned: false,
      connectivity: 'online',
      connectivityAt: '2026-09-30T16:00:00Z',
      lastHeartbeatAt: '2026-09-30T16:00:00Z',
      online: true,
      capabilitiesAt: null,
      capabilityCount: 0,
      brokerConnected: true,
      allowedOutputChannels: [],
      canControlOutputs: false,
      canViewOutputs: true,
    }),
    deviceOutputs: vi.fn().mockResolvedValue({
      kind: 'ok',
      state: {
        channels: [{ channel: 1, state: true, mode: 'toggle', pulseActive: false }],
        readAt: '2026-09-30T16:00:00Z',
        stale: false,
        staleReason: null,
      },
    }),
    sendCommand: vi.fn(),
    commandResult: vi.fn(),
    allowedCommands: vi.fn().mockResolvedValue({ actions: ['system.get_time', 'adc.get_values'] }),
  },
  getCsrf: vi.fn().mockResolvedValue({ requestToken: 'test-csrf' }),
}))

vi.mock('@/core/realtime/signalRConnection', () => ({
  createSignalRConnection: () => ({
    start: async () => undefined,
    stop: async () => undefined,
    invoke: async () => undefined,
    on: (name: string, handler: (payload: unknown) => void) => { hubHandlers.set(name, handler) },
    off: () => undefined,
  }),
}))

describe('DeviceDetailView', () => {
  it('renders device details header and telemetry by default', async () => {
    const wrapper = mount(DeviceDetailView)
    await flushPromises()

    expect(wrapper.find('[data-test="device-detail-view"]').exists()).toBe(true)
    expect(wrapper.find('[data-test="device-detail-name"]').text()).toContain('Concrete Sensor Station 1')
    expect(wrapper.find('[data-test="real-hardware-telemetry-card"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('ESP32-S3 Physical RTU Hardware Telemetry')
    expect(wrapper.text()).toContain('Last Heartbeat')
    expect(wrapper.text()).toContain('Supply Voltage')
  })

  it('shows the device MQTT binding from the server instead of a fixed broker', async () => {
    const wrapper = mount(DeviceDetailView)
    await flushPromises()

    expect(wrapper.find('[data-test="device-link-topic"]').text()).toBe('lab_prefix/gw_101/status')
    expect(wrapper.find('[data-test="device-link-online"]').text()).toBe('Online')
    expect(wrapper.find('[data-test="device-link-key"]').text()).toBe('not registered')
    expect(wrapper.text()).not.toContain('broker.emqx.io')
  })

  it('shows relay outputs only from the verified outputs read, never from telemetry', async () => {
    const wrapper = mount(DeviceDetailView)
    await flushPromises()

    expect(wrapper.find('[data-test="do-panel"]').exists()).toBe(true)
    expect(wrapper.find('[data-test="do-1-state"]').text()).toBe('ON')
    expect(wrapper.find('[data-test="do-2-state"]').exists()).toBe(false)
    expect(wrapper.find('[data-test="do-1-toggle"]').exists()).toBe(false)
  })

  it('offers the relay panel for a bound device that has sent no telemetry yet', async () => {
    vi.mocked(adminApi.deviceTelemetry).mockRejectedValueOnce(new Error('Request failed (404)'))
    const wrapper = mount(DeviceDetailView)
    await flushPromises()

    expect(wrapper.find('[data-test="do-panel"]').exists()).toBe(true)
    expect(wrapper.find('[data-test="device-link-online"]').exists()).toBe(true)
  })

  it('shows no invented digital inputs', async () => {
    const wrapper = mount(DeviceDetailView)
    await flushPromises()

    expect(wrapper.find('[data-test="di-1-state"]').exists()).toBe(false)
    expect(wrapper.find('[data-test="di-unavailable"]').text()).toContain('DI không đọc được qua MQTT')
  })

  it('reloads the relay outputs when the server announces an outputs change', async () => {
    mount(DeviceDetailView)
    await flushPromises()
    const outputs = vi.mocked(adminApi.deviceOutputs)
    const links = vi.mocked(adminApi.deviceLink)
    const before = outputs.mock.calls.length
    const linksBefore = links.mock.calls.length

    hubHandlers.get('DeviceEvent')?.({
      deviceId: 'dev-101',
      authorizationEpoch: 5,
      policyVersion: 1,
      eventType: 'outputs',
      payload: { readAt: '2026-09-30T16:01:00Z', stale: true, staleReason: 'device_offline' },
    })
    await flushPromises()

    expect(outputs.mock.calls.length).toBe(before + 1)
    // The online and broker flags that decide whether the reading still counts are refreshed too.
    expect(links.mock.calls.length).toBe(linksBefore + 1)
  })

  it('offers the three read cards for a bound device, gated by what the caller may send', async () => {
    const wrapper = mount(DeviceDetailView)
    await flushPromises()

    expect(vi.mocked(adminApi.allowedCommands)).toHaveBeenCalledWith('dev-101', expect.anything())
    const cards = wrapper.find('[data-test="read-cards"]')
    expect(cards.exists()).toBe(true)
    expect(cards.findAll('[data-test="read-card"]')).toHaveLength(3)
  })

  it('shows "Never" rather than an invented heartbeat when the device has sent no telemetry', async () => {
    vi.mocked(adminApi.deviceTelemetry).mockRejectedValueOnce(new Error('Request failed (404)'))
    const wrapper = mount(DeviceDetailView)
    await flushPromises()

    expect(wrapper.find('[data-test="last-heartbeat"]').text()).toBe('Never')
  })

  it('moves "Last Heartbeat" only on heartbeats, not on other device events', async () => {
    const wrapper = mount(DeviceDetailView)
    await flushPromises()
    const heartbeat = () => wrapper.find('[data-test="last-heartbeat"]').text()
    const before = heartbeat()

    hubHandlers.get('DeviceEvent')?.({
      deviceId: 'dev-101', authorizationEpoch: 6, policyVersion: 1, eventType: 'command_result',
      payload: { correlationId: 'c', status: 'Failed', code: 'DEVICE_RESPONSE_TIMEOUT' },
    })
    await flushPromises()
    expect(heartbeat()).toBe(before)

    hubHandlers.get('DeviceEvent')?.({
      deviceId: 'dev-101', authorizationEpoch: 7, policyVersion: 1, eventType: 'status',
      payload: { uptime_seconds: 5 },
    })
    await flushPromises()
    expect(heartbeat()).not.toBe(before)
  })

  it('marks the matching card stale when someone else changes its configuration, but not for its own write', async () => {
    const wrapper = mount(DeviceDetailView)
    await flushPromises()
    const banners = () => wrapper.findAll('[data-test="read-card"]').map(card => card.find('[data-test="read-config-changed"]').exists())
    const send = (correlationId: string, type: string, action: string) => hubHandlers.get('DeviceEvent')?.({
      deviceId: 'dev-101', authorizationEpoch: 8, policyVersion: 1, eventType: 'command_result',
      payload: { correlationId, type, action, status: 'Executed', code: 'DEVICE_EXECUTED' },
    })
    expect(banners()).toEqual([false, false, false])

    vi.mocked(adminApi.sendCommand).mockResolvedValueOnce({ status: 'ContractUnavailable', code: 'DEVICE_WRITE_BUSY', correlationId: 'own-1' })
    await runDeviceWrite('dev-101', { type: 'system', action: 'set_hostname', correlationId: 'own-1' }, {})
    send('own-1', 'system', 'set_hostname')
    await flushPromises()
    expect(banners()).toEqual([false, false, false])

    send('theirs-1', 'system', 'set_hostname')
    await flushPromises()
    expect(banners()).toEqual([true, false, false])

    send('theirs-2', 'adc', 'toggle_channel')
    await flushPromises()
    expect(banners()).toEqual([true, false, true])
  })

  it('reloads everything when the route moves to another device', async () => {
    mount(DeviceDetailView)
    await flushPromises()

    mockRoute.value!.params.id = 'dev-102'
    await flushPromises()

    expect(vi.mocked(adminApi.deviceLink)).toHaveBeenLastCalledWith('dev-102', expect.anything())
    expect(vi.mocked(adminApi.allowedCommands)).toHaveBeenLastCalledWith('dev-102', expect.anything())
    mockRoute.value!.params.id = 'dev-101'
    await flushPromises()
  })

  it('switches between tabs when navigation is clicked', async () => {
    const wrapper = mount(DeviceDetailView)
    await flushPromises()

    const cmdTab = wrapper.find('[data-test="tab-commands"]')
    await cmdTab.trigger('click')
    expect(wrapper.text()).toContain('Command & Control Pipeline')

    const provTab = wrapper.find('[data-test="tab-provisioning"]')
    await provTab.trigger('click')
    expect(wrapper.text()).toContain('Device Provisioning Bundle')

    const licTab = wrapper.find('[data-test="tab-licenses"]')
    await licTab.trigger('click')
    expect(wrapper.text()).toContain('KMS Cryptographic License')

    const permTab = wrapper.find('[data-test="tab-permissions"]')
    await permTab.trigger('click')
    await flushPromises()
    expect(wrapper.find('[data-test="device-permissions-tab"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('Permissions')
  })
})
