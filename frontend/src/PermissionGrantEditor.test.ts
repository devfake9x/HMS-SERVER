// @vitest-environment jsdom
import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi, beforeEach } from 'vitest'
import PermissionGrantEditor from './PermissionGrantEditor.vue'
import { adminApi } from './adminApi'

vi.mock('./adminApi', () => ({ adminApi: { permissionGrants: vi.fn(), grantFeature: vi.fn(), grantResourceScope: vi.fn(), grantDeviceAction: vi.fn() } }))
const snapshot = (permission = 'devices.read') => ({ userId: 'u1', featureGrants: [{ permission, deny: true, policyVersion: 2 }], resourceGrants: [], deviceActionGrants: [], policyVersion: 2 })
beforeEach(() => vi.resetAllMocks())
const create = () => mount(PermissionGrantEditor, { props: { allowed: true, csrfToken: 'csrf', sessionSignal: new AbortController().signal } })

describe('PermissionGrantEditor', () => {
  it('loads grants, renders deny and refreshes after explicit deny submission', async () => {
    vi.mocked(adminApi.permissionGrants).mockResolvedValue(snapshot() as never)
    const wrapper = create()
    await wrapper.get('[data-test=target]').setValue('u1')
    await wrapper.get('[data-test=load]').trigger('click')
    await flushPromises()
    expect(wrapper.text()).toContain('devices.read')
    expect(wrapper.text()).toContain('Deny')
    await wrapper.get('[data-test=permission]').setValue('devices.manage')
    await wrapper.get('[data-test=deny]').setValue(true)
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(adminApi.grantFeature).toHaveBeenCalledWith({ userId: 'u1', permission: 'devices.manage', deny: true }, expect.objectContaining({ csrfToken: 'csrf' }))
    expect(adminApi.permissionGrants).toHaveBeenCalledTimes(2)
    expect(wrapper.emitted('changed')).toHaveLength(1)
    wrapper.unmount()
  })

  it('ignores a response after target replacement', async () => {
    let resolve!: (value: never) => void
    vi.mocked(adminApi.permissionGrants).mockReturnValue(new Promise(r => { resolve = r }))
    const wrapper = create()
    await wrapper.get('[data-test=target]').setValue('u1')
    await wrapper.get('[data-test=load]').trigger('click')
    await wrapper.get('[data-test=target]').setValue('u2')
    resolve(snapshot('old-user-secret') as never)
    await flushPromises()
    expect(wrapper.text()).not.toContain('old-user-secret')
    wrapper.unmount()
  })

  it('clears grants and disables submission on session abort', async () => {
    vi.mocked(adminApi.permissionGrants).mockResolvedValue(snapshot() as never)
    const session = new AbortController()
    const wrapper = mount(PermissionGrantEditor, { props: { allowed: true, csrfToken: 'csrf', sessionSignal: session.signal } })
    await wrapper.get('[data-test=target]').setValue('u1')
    await wrapper.get('[data-test=load]').trigger('click')
    await flushPromises()
    session.abort()
    await flushPromises()
    expect(wrapper.text()).not.toContain('devices.read')
    expect(wrapper.get('[data-test=submit]').attributes('disabled')).toBeDefined()
    wrapper.unmount()
  })

  it('submits resource and device-action denies through their own grant contracts', async () => {
    vi.mocked(adminApi.permissionGrants).mockResolvedValue(snapshot() as never)
    vi.mocked(adminApi.grantResourceScope).mockResolvedValue({} as never)
    vi.mocked(adminApi.grantDeviceAction).mockResolvedValue({} as never)
    const wrapper = create()
    await wrapper.get('[data-test=target]').setValue('u1')
    await wrapper.get('[data-test=organization]').setValue('o1')
    await wrapper.get('[data-test=resource-deny]').setValue(true)
    await wrapper.findAll('form')[1].trigger('submit')
    await flushPromises()
    expect(adminApi.grantResourceScope).toHaveBeenCalledWith(
      { userId: 'u1', organizationId: 'o1', deny: true },
      expect.objectContaining({ csrfToken: 'csrf' }),
    )

    await wrapper.get('[data-test=device]').setValue('d1')
    await wrapper.get('[data-test=action]').setValue('status.read')
    await wrapper.get('[data-test=device-deny]').setValue(true)
    await wrapper.findAll('form')[2].trigger('submit')
    await flushPromises()
    expect(adminApi.grantDeviceAction).toHaveBeenCalledWith(
      { userId: 'u1', deviceId: 'd1', action: 'status.read', deny: true },
      expect.objectContaining({ csrfToken: 'csrf' }),
    )
    wrapper.unmount()
  })
})
