// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import DeviceListView from './DeviceListView.vue'

const mockRouter = {
  push: vi.fn(),
}

vi.mock('vue-router', () => ({
  useRouter: () => mockRouter,
  useRoute: () => ({ query: {} }),
}))

vi.mock('#/shared/i18n/placeholders', () => ({
  usePlaceholderTranslation: () => ({
    t: (key: string) => key,
  }),
}))

vi.mock('@/core/api/adminApi', () => ({
  adminApi: {
    listDevices: vi.fn().mockResolvedValue([
      { id: 'dev-1', name: 'Sensor 1', state: 'Active', ownerOrganizationId: 'org-1', areaId: 'area-1', keyGeneration: 1 },
      { id: 'dev-2', name: 'Sensor 2', state: 'Provisioning', ownerOrganizationId: 'org-1', areaId: 'area-1', keyGeneration: 2 },
    ]),
    listOrganizations: vi.fn().mockResolvedValue([{ id: 'org-1', name: 'Test Org' }]),
    listProjects: vi.fn().mockResolvedValue([{ id: 'proj-1', name: 'Test Project', organizationId: 'org-1' }]),
    listPackages: vi.fn().mockResolvedValue([{ id: 'pkg-1', name: 'Test Package', projectId: 'proj-1' }]),
    listAreas: vi.fn().mockResolvedValue([{ id: 'area-1', name: 'North Area', organizationId: 'org-1' }]),
    createDevice: vi.fn().mockResolvedValue({ id: 'dev-3', name: 'Sensor 3' }),
  },
  getCsrf: vi.fn().mockResolvedValue({ requestToken: 'test-csrf' }),
}))

describe('DeviceListView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders device list in table mode by default', async () => {
    const wrapper = mount(DeviceListView)
    await flushPromises()

    expect(wrapper.find('[data-test="device-list-view"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('Sensor 1')
    expect(wrapper.text()).toContain('Sensor 2')
  })

  it('switches to hierarchy tree mode when toggle is clicked', async () => {
    const wrapper = mount(DeviceListView)
    await flushPromises()

    const treeToggle = wrapper.find('[data-test="toggle-tree-mode"]')
    await treeToggle.trigger('click')

    expect(wrapper.find('[data-test="tree-data-table"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('Test Org')
  })

  it('opens enroll device modal on enroll button click and validates required fields', async () => {
    const wrapper = mount(DeviceListView, {
      attachTo: document.body,
    })
    await flushPromises()

    const enrollBtn = wrapper.find('[data-test="enroll-device-btn"]')
    expect(enrollBtn.exists()).toBe(true)
    await enrollBtn.trigger('click')

    expect(document.body.textContent).toContain('Enroll New Edge Device')

    // Submit without filling name/org shows error
    const form = document.body.querySelector('form')
    expect(form).not.toBeNull()
    form?.dispatchEvent(new Event('submit', { cancelable: true }))
    await flushPromises()
    expect(document.body.textContent).toContain('Device name and organization are required.')

    wrapper.unmount()
  })
})
