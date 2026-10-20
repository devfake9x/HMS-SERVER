// @vitest-environment jsdom
import { describe, it, expect, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import HierarchyView from './HierarchyView.vue'

vi.mock('@/core/api/adminApi', () => ({
  adminApi: {
    listOrganizations: vi.fn().mockResolvedValue([{ id: 'org-1', name: 'Main Organization' }]),
    listProjects: vi.fn().mockResolvedValue([{ id: 'proj-1', name: 'High Speed Rail Project', organizationId: 'org-1' }]),
    listPackages: vi.fn().mockResolvedValue([{ id: 'pkg-1', name: 'Concrete Sensor Package A', projectId: 'proj-1' }]),
    listAreas: vi.fn().mockResolvedValue([{ id: 'area-1', name: 'Pillar Section 4', organizationId: 'org-1' }]),
  },
  getCsrf: vi.fn().mockResolvedValue({ requestToken: 'test-csrf' }),
}))

describe('HierarchyView', () => {
  it('renders hierarchy tree nodes and quick actions', async () => {
    const wrapper = mount(HierarchyView)
    await flushPromises()

    expect(wrapper.find('[data-test="hierarchy-view"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('Main Organization')
    expect(wrapper.text()).toContain('High Speed Rail Project')
    expect(wrapper.text()).toContain('Concrete Sensor Package A')
  })
})
