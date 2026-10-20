// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import OverviewView from './OverviewView.vue'

const mockRouter = {
  push: vi.fn(),
}

vi.mock('vue-router', () => ({
  useRouter: () => mockRouter,
  RouterLink: {
    template: '<a><slot /></a>',
  },
}))

vi.mock('@/core/api/adminApi', () => ({
  adminApi: {
    listDevices: vi.fn().mockResolvedValue([
      { id: 'dev-1', name: 'RTU-Alpha', state: 'Active', ownerOrganizationId: 'org-1' },
      { id: 'dev-2', name: 'RTU-Beta', state: 'Provisioning', ownerOrganizationId: 'org-1' },
      { id: 'dev-3', name: 'RTU-Gamma', state: 'Revoked', ownerOrganizationId: 'org-2' },
    ]),
    commandCatalog: vi.fn().mockResolvedValue({
      catalogVersion: '1.0.4',
      catalogHash: 'abcdef123456',
      trusted: true,
      actions: [],
    }),
  },
}))

describe('OverviewView', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ status: 'MQTT_DEFERRED' }), { status: 200 })))
  })

  it('renders fleet metric cards with correct counts', async () => {
    const wrapper = mount(OverviewView, {
      global: {
        stubs: {
          'router-link': { template: '<a><slot /></a>' },
        },
      },
    })
    await flushPromises()

    expect(wrapper.find('[data-test="overview-view"]').exists()).toBe(true)
    expect(wrapper.find('[data-test="kpi-total"]').text()).toBe('3')
    expect(wrapper.find('[data-test="kpi-online"]').text()).toBe('1')
    expect(wrapper.find('[data-test="kpi-provisioning"]').text()).toBe('1')
    expect(wrapper.find('[data-test="kpi-offline"]').text()).toBe('1')
  })

  it('navigates to device details on action click', async () => {
    const wrapper = mount(OverviewView, {
      global: {
        stubs: {
          'router-link': { template: '<a><slot /></a>' },
        },
      },
    })
    await flushPromises()

    const deviceRows = wrapper.findAll('[data-test="table-row"]')
    expect(deviceRows.length).toBe(3)

    await deviceRows[0].trigger('click')
    expect(mockRouter.push).toHaveBeenCalledWith('/devices/dev-1')
  })
})
