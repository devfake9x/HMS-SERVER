// @vitest-environment jsdom
import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import DeviceCommandModal from './DeviceCommandModal.vue'

describe('DeviceCommandModal', () => {
  const catalog = {
    catalogVersion: '1.0.0',
    catalogHash: 'hash-abc',
    trusted: true,
    actions: [
      { type: 'telemetry', action: 'poll', featurePermission: 'commands.read', available: true, dangerous: false, requiresConfirmation: false, idempotent: true, defaultTtlMs: 5000 },
      { type: 'system', action: 'reboot', featurePermission: 'commands.admin', available: true, dangerous: true, requiresConfirmation: true, idempotent: false, defaultTtlMs: 5000 },
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
})
