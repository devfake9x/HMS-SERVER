// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import DevicePermissionsTab from './DevicePermissionsTab.vue'
import { adminApi, getCsrf } from '@/core/api/adminApi'

vi.mock('@/core/api/adminApi', () => ({
  adminApi: {
    devicePermissions: vi.fn(),
    updateDevicePermissions: vi.fn(),
  },
  getCsrf: vi.fn().mockResolvedValue({ requestToken: 'test-csrf-token' }),
}))

describe('DevicePermissionsTab', () => {
  const mockMatrix = {
    deviceId: 'dev-b4b6fe7a',
    deviceName: 'Concrete-Sensor-Station-North-01',
    policyVersion: 42,
    permissions: [
      { key: 'view', title: 'View', description: 'Can view the device and its data' },
      { key: 'edit_basics', title: 'Metadata', description: 'Can edit device metadata' },
      { key: 'edit_product', title: 'Definition', description: 'Can edit the device definition' },
      { key: 'record_measurements', title: 'Measurements', description: 'Can record measurements' },
    ],
    users: [
      {
        userId: 'user-toan',
        displayName: 'Toan Nguyen',
        email: 'toannm.work@gmail.com',
        role: 'master-admin',
        grants: {
          view: {
            granted: true,
            inherited: true,
            inheritedReason: 'The user has set this permission for all devices in this workspace.',
          },
          edit_basics: {
            granted: false,
            inherited: false,
          },
          edit_product: {
            granted: false,
            inherited: false,
          },
          record_measurements: {
            granted: false,
            inherited: false,
          },
        },
      },
      {
        userId: 'user-bob',
        displayName: 'Bob Smith',
        email: 'bob@example.test',
        role: 'operator',
        grants: {
          view: {
            granted: true,
            inherited: true,
            inheritedReason: 'The user has set this permission for all devices in this workspace.',
          },
          edit_basics: {
            granted: true,
            inherited: false,
          },
          edit_product: {
            granted: false,
            inherited: false,
          },
          record_measurements: {
            granted: false,
            inherited: false,
          },
        },
      },
    ],
  }

  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(adminApi.devicePermissions).mockResolvedValue(JSON.parse(JSON.stringify(mockMatrix)))
  })

  it('renders Permissions card and 4 Datacake columns with titles and descriptions', async () => {
    const wrapper = mount(DevicePermissionsTab, {
      props: { deviceId: 'dev-b4b6fe7a' },
    })
    await flushPromises()

    expect(wrapper.find('[data-test="device-permissions-tab"]').exists()).toBe(true)
    expect(wrapper.find('.card-header-title').text()).toBe('Permissions')

    // 4 Column headers
    expect(wrapper.text()).toContain('View')
    expect(wrapper.text()).toContain('Can view the device and its data')
    expect(wrapper.text()).toContain('Metadata')
    expect(wrapper.text()).toContain('Can edit device metadata')
    expect(wrapper.text()).toContain('Definition')
    expect(wrapper.text()).toContain('Can edit the device definition')
    expect(wrapper.text()).toContain('Measurements')
    expect(wrapper.text()).toContain('Can record measurements')
  })

  it('renders user details matching Datacake (Name bold, email muted)', async () => {
    const wrapper = mount(DevicePermissionsTab, {
      props: { deviceId: 'dev-b4b6fe7a' },
    })
    await flushPromises()

    const toanRow = wrapper.find('[data-test="user-row-user-toan"]')
    expect(toanRow.exists()).toBe(true)
    expect(toanRow.text()).toContain('Toan Nguyen')
    expect(toanRow.text()).toContain('toannm.work@gmail.com')
  })

  it('renders inherited permission as disabled and checked with tooltip', async () => {
    const wrapper = mount(DevicePermissionsTab, {
      props: { deviceId: 'dev-b4b6fe7a' },
    })
    await flushPromises()

    const toanRow = wrapper.find('[data-test="user-row-user-toan"]')
    const inheritedCell = toanRow.find('[data-test="inherited-permission-cell"]')
    expect(inheritedCell.exists()).toBe(true)
    expect(inheritedCell.attributes('data-tooltip')).toBe('The user has set this permission for all devices in this workspace.')

    const input = inheritedCell.find('input[type="checkbox"]')
    expect(input.attributes('disabled')).toBeDefined()
    expect((input.element as HTMLInputElement).checked).toBe(true)
  })

  it('toggles configurable permissions, tracks unsaved changes and enables Save button', async () => {
    const wrapper = mount(DevicePermissionsTab, {
      props: { deviceId: 'dev-b4b6fe7a' },
    })
    await flushPromises()

    const saveBtn = wrapper.find('[data-test="save-permissions-btn"]')
    expect(saveBtn.attributes('disabled')).toBeDefined()

    // Toggle edit_basics for Toan
    const input = wrapper.find('input[data-test="perm-checkbox-user-toan-edit_basics"]')
    expect(input.exists()).toBe(true)
    expect((input.element as HTMLInputElement).checked).toBe(false)

    // Trigger toggle
    await input.setValue(true)
    await flushPromises()

    // Verify unsaved changes badge and Save button active
    expect(wrapper.text()).toContain('1 unsaved')
    expect(saveBtn.attributes('disabled')).toBeUndefined()
  })

  it('submits updated permissions on Save Changes with CSRF and expectedPolicyVersion', async () => {
    vi.mocked(adminApi.updateDevicePermissions).mockResolvedValue({
      ...mockMatrix,
      policyVersion: 43,
    })

    const wrapper = mount(DevicePermissionsTab, {
      props: { deviceId: 'dev-b4b6fe7a', csrfToken: 'explicit-csrf' },
    })
    await flushPromises()

    const input = wrapper.find('input[data-test="perm-checkbox-user-toan-edit_basics"]')
    await input.setValue(true)
    await flushPromises()

    const saveBtn = wrapper.find('[data-test="save-permissions-btn"]')
    await saveBtn.trigger('click')
    await flushPromises()

    expect(adminApi.updateDevicePermissions).toHaveBeenCalledWith(
      'dev-b4b6fe7a',
      {
        expectedPolicyVersion: 42,
        grants: [
          { userId: 'user-toan', action: 'edit_basics', granted: true },
        ],
      },
      expect.objectContaining({ csrfToken: 'explicit-csrf' })
    )

    expect(wrapper.text()).toContain('Permissions saved successfully (Policy v43).')
  })

  it('shows error banner when policy version conflict occurs (409)', async () => {
    vi.mocked(adminApi.updateDevicePermissions).mockRejectedValue(new Error('Request failed (409): POLICY_VERSION_STALE'))

    const wrapper = mount(DevicePermissionsTab, {
      props: { deviceId: 'dev-b4b6fe7a' },
    })
    await flushPromises()

    const input = wrapper.find('input[data-test="perm-checkbox-user-toan-edit_basics"]')
    await input.setValue(true)
    await flushPromises()

    const saveBtn = wrapper.find('[data-test="save-permissions-btn"]')
    await saveBtn.trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('Conflict: Permissions were updated by another session.')
  })
})
