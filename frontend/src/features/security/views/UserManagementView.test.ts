// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import UserManagementView from './UserManagementView.vue'

vi.mock('@/core/api/adminApi', () => ({
  adminApi: {
    listUsers: vi.fn().mockResolvedValue([
      { id: 'usr-1', email: 'alice@company.com', role: 'Administrator', organizationId: 'org-1' },
      { id: 'usr-2', email: 'bob@company.com', role: 'Operator', organizationId: 'org-2' },
    ]),
    listOrganizations: vi.fn().mockResolvedValue([{ id: 'org-1', name: 'Org Alpha' }]),
  },
  getCsrf: vi.fn().mockResolvedValue({ requestToken: 'test-csrf' }),
}))

describe('UserManagementView', () => {
  it('renders user list and displays user records', async () => {
    const wrapper = mount(UserManagementView)
    await flushPromises()

    expect(wrapper.find('[data-test="user-management-view"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('alice@company.com')
    expect(wrapper.text()).toContain('bob@company.com')
    expect(wrapper.text()).toContain('Administrator')
  })
})
