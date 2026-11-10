// @vitest-environment jsdom
import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import DeviceCommandModal from './DeviceCommandModal.vue'
import { adminApi } from '@/core/api/adminApi'

describe('DeviceCommandModal', () => {
  const catalog = {
    catalogVersion: '1.0.0',
    catalogHash: 'hash-abc',
    trusted: true,
    actions: [
      { type: 'telemetry', action: 'poll', class: 'Read', featurePermission: 'commands.read', available: true, dangerous: false, requiresConfirmation: false, idempotent: true, defaultTtlMs: 5000 },
      { type: 'system', action: 'reboot', class: 'Flash', featurePermission: 'commands.admin', available: true, dangerous: true, requiresConfirmation: true, idempotent: false, defaultTtlMs: 5000 },
      { type: 'system', action: 'factory_reset', class: 'Flash', featurePermission: 'commands.admin', available: true, dangerous: true, requiresConfirmation: true, idempotent: false, defaultTtlMs: 5000 },
      { type: 'ota', action: 'apply', class: 'Flash', featurePermission: 'commands.admin', available: true, dangerous: true, requiresConfirmation: true, idempotent: false, defaultTtlMs: 5000 },
      { type: 'system', action: 'get_info', class: 'Read', featurePermission: 'commands.read', available: true, dangerous: false, requiresConfirmation: false, idempotent: true, defaultTtlMs: 5000 },
    ],
  }

  const permissions = {
    policyVersion: 1,
    expiresAt: '2099-01-01',
    permissions: ['commands.read', 'commands.admin'],
    deniedPermissions: [],
  }

  it('renders modal with warning when catalog is not verified', () => {
    const wrapper = mount(DeviceCommandModal, {
      attachTo: document.body,
      props: {
        modelValue: true,
        deviceId: 'dev-1',
        deviceName: 'Device Alpha',
        catalog,
        catalogProof: null,
        catalogFresh: false,
        permissions,
        csrfToken: 'csrf',
      },
    })

    const warning = document.body.querySelector('[data-test="catalog-unverified-warning"]')
    expect(warning).not.toBeNull()
    wrapper.unmount()
  })

  it('disables submit button when subsystem/action is not selected', () => {
    const wrapper = mount(DeviceCommandModal, {
      attachTo: document.body,
      props: {
        modelValue: true,
        deviceId: 'dev-1',
        deviceName: 'Device Alpha',
        catalog,
        catalogProof: { version: '1.0.0', hash: 'hash-abc' },
        catalogFresh: true,
        permissions,
        csrfToken: 'csrf',
      },
    })

    const submitBtn = document.body.querySelector('[data-test="command-submit-btn"]') as HTMLButtonElement
    expect(submitBtn.disabled).toBe(true)
    expect(submitBtn.className).toContain('!bg-orange-500')
    wrapper.unmount()
  })

  function mountVerified() {
    return mount(DeviceCommandModal, {
      attachTo: document.body,
      props: {
        modelValue: true,
        deviceId: 'dev-1',
        deviceName: 'Device Alpha',
        catalog,
        catalogProof: { version: '1.0.0', hash: 'hash-abc' },
        catalogFresh: true,
        permissions,
        csrfToken: 'csrf',
      },
    })
  }

  async function dispatchPoll(wrapper: ReturnType<typeof mountVerified>) {
    const vm = wrapper.vm as unknown as { selectedType: string; selectedAction: string }
    vm.selectedType = 'telemetry'
    vm.selectedAction = 'poll'
    await wrapper.vm.$nextTick()
    ;(document.body.querySelector('[data-test="command-submit-btn"]') as HTMLButtonElement).click()
    await vi.advanceTimersByTimeAsync(0)
  }

  it('gates on the requiredPermission the catalog API returns', async () => {
    const wrapper = mount(DeviceCommandModal, {
      attachTo: document.body,
      props: {
        modelValue: true,
        deviceId: 'dev-1',
        deviceName: 'Device Alpha',
        catalog: {
          ...catalog,
          // featurePermission is empty, as in what the server sends today.
          actions: [{ type: 'system', action: 'get_info', class: 'Read', requiredPermission: 'sys.info_view', featurePermission: '', available: true, dangerous: false, requiresConfirmation: false, idempotent: true, defaultTtlMs: 120000 }],
        },
        catalogProof: { version: '1.0.0', hash: 'hash-abc' },
        catalogFresh: true,
        permissions: { ...permissions, permissions: ['sys.info_view'] },
        csrfToken: 'csrf',
      },
    })
    const vm = wrapper.vm as unknown as { selectedType: string; selectedAction: string }
    vm.selectedType = 'system'
    vm.selectedAction = 'get_info'
    await wrapper.vm.$nextTick()

    expect(document.body.textContent).toContain('sys.info_view')
    expect((document.body.querySelector('[data-test="command-submit-btn"]') as HTMLButtonElement).disabled).toBe(false)
    wrapper.unmount()
  })

  it('reports a refused command as not sent instead of dispatched', async () => {
    vi.useFakeTimers()
    const send = vi.spyOn(adminApi, 'sendCommand').mockResolvedValue({
      status: 'ContractUnavailable', code: 'COMMAND_KEY_NOT_PROVISIONED', correlationId: 'c-1', isSuccess: false,
    })
    const wrapper = mountVerified()

    await dispatchPoll(wrapper)

    expect(send).toHaveBeenCalledOnce()
    expect(document.body.querySelector('[data-test="command-success-result"]')).toBeNull()
    expect(document.body.querySelector('[data-test="command-not-sent-result"]')?.textContent).toContain('COMMAND_KEY_NOT_PROVISIONED')
    wrapper.unmount()
    vi.restoreAllMocks()
    vi.useRealTimers()
  })

  it('waits for the device reply after a published command and shows its data', async () => {
    vi.useFakeTimers()
    vi.spyOn(adminApi, 'sendCommand').mockResolvedValue({
      status: 'Published', code: 'MQTT_PUBLISHED', correlationId: 'c-2', isSuccess: true,
    })
    const read = vi.spyOn(adminApi, 'commandResult')
      .mockResolvedValueOnce({ correlationId: 'c-2', result: { status: 'Published', code: 'MQTT_PUBLISHED', correlationId: 'c-2' }, reply: null })
      .mockResolvedValueOnce({
        correlationId: 'c-2',
        result: { status: 'Executed', code: 'DEVICE_EXECUTED', correlationId: 'c-2' },
        reply: { type: 'telemetry', action: 'poll', success: true, error: null, data: { chip: 'esp32s3' }, receivedAt: '2026-11-06T00:00:00Z' },
      })
    const wrapper = mountVerified()

    await dispatchPoll(wrapper)
    expect(document.body.querySelector('[data-test="command-device-outcome"]')?.textContent).toContain('Waiting for device reply')
    await vi.advanceTimersByTimeAsync(2100)

    expect(read).toHaveBeenCalledTimes(2)
    expect(read).toHaveBeenCalledWith('dev-1', 'c-2')
    expect(document.body.querySelector('[data-test="command-device-outcome"]')?.textContent).toContain('Device outcome: Executed')
    expect(document.body.querySelector('[data-test="command-reply-data"]')?.textContent).toContain('esp32s3')
    wrapper.unmount()
    vi.restoreAllMocks()
    vi.useRealTimers()
  })

  it('offers no class F action, which only the reboot panel may send', async () => {
    const wrapper = mountVerified()
    const vm = wrapper.vm as unknown as { selectedType: string; availableTypes: string[]; actionOptions: Array<{ value: string }> }

    expect(vm.availableTypes).not.toContain('ota')
    vm.selectedType = 'system'
    await wrapper.vm.$nextTick()
    expect(vm.actionOptions.map(o => o.value)).toEqual(['get_info'])
    wrapper.unmount()
  })

  it('fails closed: an action whose catalog class is missing is hidden too', async () => {
    const wrapper = mount(DeviceCommandModal, {
      attachTo: document.body,
      props: {
        modelValue: true, deviceId: 'dev-1', deviceName: 'Device Alpha',
        catalog: {
          ...catalog,
          actions: [
            { type: 'x', action: 'no_class', featurePermission: '', available: true, dangerous: true, requiresConfirmation: true, idempotent: false, defaultTtlMs: 5000 },
            { type: 'x', action: 'plain', class: 'Read', featurePermission: '', available: true, dangerous: false, requiresConfirmation: false, idempotent: true, defaultTtlMs: 5000 },
          ],
        },
        catalogProof: { version: '1.0.0', hash: 'hash-abc' }, catalogFresh: true, permissions, csrfToken: 'csrf',
      },
    })
    const vm = wrapper.vm as unknown as { selectedType: string; actionOptions: Array<{ value: string }> }
    vm.selectedType = 'x'
    await wrapper.vm.$nextTick()

    expect(vm.actionOptions.map(o => o.value)).toEqual(['plain'])
    wrapper.unmount()
  })

  it('hides class F by the catalog class, not by the action name', async () => {
    const wrapper = mount(DeviceCommandModal, {
      attachTo: document.body,
      props: {
        modelValue: true, deviceId: 'dev-1', deviceName: 'Device Alpha',
        catalog: {
          ...catalog,
          actions: [
            { type: 'x', action: 'new_flash', class: 'Flash', featurePermission: '', available: true, dangerous: true, requiresConfirmation: true, idempotent: false, defaultTtlMs: 5000 },
            { type: 'x', action: 'plain', class: 'Read', featurePermission: '', available: true, dangerous: false, requiresConfirmation: false, idempotent: true, defaultTtlMs: 5000 },
          ],
        },
        catalogProof: { version: '1.0.0', hash: 'hash-abc' }, catalogFresh: true, permissions, csrfToken: 'csrf',
      },
    })
    const vm = wrapper.vm as unknown as { selectedType: string; actionOptions: Array<{ value: string }> }
    vm.selectedType = 'x'
    await wrapper.vm.$nextTick()

    expect(vm.actionOptions.map(o => o.value)).toEqual(['plain'])
    wrapper.unmount()
  })

  it.each([['system', 'reboot'], ['system', 'factory_reset'], ['ota', 'apply'], ['system', 'get_info']])(
    'never puts a confirm_token in the parameters of %s.%s', async (type, action) => {
      const wrapper = mountVerified()
      const vm = wrapper.vm as unknown as { selectedType: string; selectedAction: string; rawParamsJson: string }
      vm.selectedType = type
      vm.selectedAction = action
      await wrapper.vm.$nextTick()

      expect(vm.rawParamsJson).toBe('{}')
      expect(vm.rawParamsJson).not.toContain('confirm_token')
      wrapper.unmount()
    })

  it('offers no relay writes, which only the DO panel may send', async () => {
    const control = (action: string) => ({ type: 'control', action, class: 'Control', featurePermission: '', available: true, dangerous: false, requiresConfirmation: false, idempotent: false, defaultTtlMs: 10000 })
    const wrapper = mount(DeviceCommandModal, {
      attachTo: document.body,
      props: {
        modelValue: true,
        deviceId: 'dev-1',
        deviceName: 'Device Alpha',
        catalog: { ...catalog, actions: [control('get_status'), control('set_output'), control('pulse'), control('set_all')] },
        catalogProof: { version: '1.0.0', hash: 'hash-abc' },
        catalogFresh: true,
        permissions,
        csrfToken: 'csrf',
      },
    })
    const vm = wrapper.vm as unknown as { selectedType: string; actionOptions: Array<{ value: string }> }
    vm.selectedType = 'control'
    await wrapper.vm.$nextTick()

    expect(vm.actionOptions.map(o => o.value)).toEqual(['get_status'])
    expect(document.body.querySelector('[data-test="do-panel-only-note"]')?.textContent).toContain('Điều khiển DO trên panel DO')
    wrapper.unmount()
  })

  it('offers no config writes, which only the read cards may send', async () => {
    const act = (type: string, action: string) => ({ type, action, class: 'Write', featurePermission: '', available: true, dangerous: false, requiresConfirmation: false, idempotent: false, defaultTtlMs: 10000 })
    const wrapper = mount(DeviceCommandModal, {
      attachTo: document.body,
      props: {
        modelValue: true,
        deviceId: 'dev-1',
        deviceName: 'Device Alpha',
        catalog: {
          ...catalog,
          actions: [
            act('system', 'get_time'), act('system', 'set_hostname'), act('system', 'set_timezone'), act('system', 'sync_ntp'),
            act('adc', 'get_channels'), act('adc', 'set_channel'), act('adc', 'toggle_channel'),
          ],
        },
        catalogProof: { version: '1.0.0', hash: 'hash-abc' },
        catalogFresh: true,
        permissions,
        csrfToken: 'csrf',
      },
    })
    const vm = wrapper.vm as unknown as { selectedType: string; actionOptions: Array<{ value: string }> }

    vm.selectedType = 'system'
    await wrapper.vm.$nextTick()
    expect(vm.actionOptions.map(o => o.value)).toEqual(['get_time'])
    vm.selectedType = 'adc'
    await wrapper.vm.$nextTick()
    expect(vm.actionOptions.map(o => o.value)).toEqual(['get_channels'])
    wrapper.unmount()
  })
})
