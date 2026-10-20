// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import DeviceDetailView from './DeviceDetailView.vue'

const mockRouter = {
  push: vi.fn(),
  replace: vi.fn().mockResolvedValue(undefined),
}

vi.mock('vue-router', () => ({
  useRouter: () => mockRouter,
  useRoute: () => ({ params: { id: 'dev-101' } }),
}))

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
      },
    }),
  },
  getCsrf: vi.fn().mockResolvedValue({ requestToken: 'test-csrf' }),
}))

vi.mock('@/core/realtime/signalRConnection', () => ({
  createSignalRConnection: () => ({
    start: async () => undefined,
    stop: async () => undefined,
    invoke: async () => undefined,
    on: () => undefined,
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
