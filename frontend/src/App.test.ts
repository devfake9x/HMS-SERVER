// @vitest-environment jsdom
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import App from './App.vue'

const mocks = vi.hoisted(() => ({
  listOrganizations: vi.fn(),
  listProjects: vi.fn(),
  listPackages: vi.fn(),
  listAreas: vi.fn(),
  listDevices: vi.fn(),
  permissions: vi.fn(),
  commandCatalog: vi.fn(),
  revalidateCommandCatalog: vi.fn(),
  realtimeAuthorization: vi.fn(),
  getCsrf: vi.fn(),
  login: vi.fn(),
  logout: vi.fn(),
}))

vi.mock('./adminApi', () => ({
  adminApi: mocks,
  getCsrf: mocks.getCsrf,
  login: mocks.login,
  logout: mocks.logout,
}))
vi.mock('./secureDownload', () => ({ downloadSecretBlob: vi.fn() }))
vi.mock('./signalRConnection', () => ({
  createSignalRConnection: () => ({
    start: async () => undefined,
    stop: async () => undefined,
    invoke: async () => undefined,
    on: () => undefined,
    off: () => undefined,
  }),
}))

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (error: Error) => void
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise
    reject = rejectPromise
  })
  return { promise, resolve, reject }
}

const permissions = {
  policyVersion: 1,
  expiresAt: '2099-01-01T00:00:00Z',
  permissions: ['organizations.manage', 'projects.manage', 'packages.manage', 'areas.manage', 'devices.manage', 'users.manage', 'permissions.manage', 'provisioning.manage', 'licenses.manage'],
  deniedPermissions: [],
}
const catalog = { catalogVersion: 'test', catalogHash: 'test-hash', trusted: true, actions: [] }

beforeEach(() => {
  vi.resetAllMocks()
  mocks.getCsrf.mockResolvedValue({ requestToken: 'csrf' })
  mocks.listOrganizations.mockResolvedValue([{ id: 'org-1', name: 'Initial organization', parentId: null }])
  mocks.listProjects.mockResolvedValue([{ id: 'project-1', name: 'Initial project', organizationId: 'org-1' }])
  mocks.listPackages.mockResolvedValue([])
  mocks.listAreas.mockResolvedValue([])
  mocks.listDevices.mockResolvedValue([])
  mocks.permissions.mockResolvedValue(permissions)
  mocks.commandCatalog.mockResolvedValue(catalog)
  mocks.revalidateCommandCatalog.mockResolvedValue(catalog)
  mocks.realtimeAuthorization.mockResolvedValue({ deviceId: 'd1', authorizationEpoch: 1, policyVersion: 1 })
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ status: 'MQTT_DEFERRED' }), { status: 200 })))
  window.history.replaceState(null, '', '/')
})

describe('App request fencing', () => {
  it('does not apply an older section response after navigation', async () => {
    const wrapper = mount(App)
    await flushPromises()

    const oldProjects = deferred<Array<{ id: string; name: string; organizationId: string }>>()
    const newOrganizations = deferred<Array<{ id: string; name: string; parentId: string | null }>>()
    mocks.listProjects.mockReturnValueOnce(oldProjects.promise)
    mocks.listOrganizations.mockReturnValueOnce(newOrganizations.promise)

    const button = (name: string) => wrapper.findAll('button.nav-item').find(item => item.text().trim() === name)!
    await button('Projects').trigger('click')
    await button('Organizations').trigger('click')

    newOrganizations.resolve([{ id: 'org-new', name: 'New organization', parentId: null }])
    await flushPromises()
    oldProjects.resolve([{ id: 'project-old', name: 'Old project', organizationId: 'org-1' }])
    await flushPromises()

    expect(wrapper.text()).toContain('New organization')
    expect(wrapper.text()).not.toContain('Old project')
    wrapper.unmount()
  })

  it('does not apply an older inventory response after a newer filter request', async () => {
    const wrapper = mount(App)
    await flushPromises()

    const oldInventory = deferred<Array<{ id: string; name: string; ownerOrganizationId: string; projectId: string | null; packageId: string | null; areaId: string | null; state: string; keyGeneration: number }>>()
    const newInventory = deferred<Array<{ id: string; name: string; ownerOrganizationId: string; projectId: string | null; packageId: string | null; areaId: string | null; state: string; keyGeneration: number }>>()
    mocks.listDevices.mockReturnValueOnce(oldInventory.promise).mockReturnValueOnce(newInventory.promise)

    const search = wrapper.get('input[type="search"]')
    await search.setValue('old-filter')
    await flushPromises()
    await search.setValue('new-filter')
    await flushPromises()

    newInventory.resolve([{ id: 'd-new', name: 'New device new-filter', ownerOrganizationId: 'org-1', projectId: null, packageId: null, areaId: null, state: 'Active', keyGeneration: 1 }])
    await flushPromises()
    oldInventory.resolve([{ id: 'd-old', name: 'Old device new-filter', ownerOrganizationId: 'org-1', projectId: null, packageId: null, areaId: null, state: 'Active', keyGeneration: 1 }])
    await flushPromises()

    expect(wrapper.text()).toContain('New device')
    expect(wrapper.text()).not.toContain('Old device')
    wrapper.unmount()
  })
})
