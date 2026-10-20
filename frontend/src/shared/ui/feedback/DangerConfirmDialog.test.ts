// @vitest-environment jsdom
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import DangerConfirmDialog from '../feedback/DangerConfirmDialog.vue'

describe('DangerConfirmDialog', () => {
  it('renders confirmation message and blocks submit until phrase matches', async () => {
    const wrapper = mount(DangerConfirmDialog, {
      attachTo: document.body,
      props: {
        modelValue: true,
        title: 'Revoke Key',
        message: 'This will invalidate all device tokens.',
        confirmPhrase: 'REVOKE',
      },
    })

    const confirmBtn = document.body.querySelector('[data-test="danger-confirm-btn"]') as HTMLButtonElement
    expect(confirmBtn).not.toBeNull()
    expect(confirmBtn.disabled).toBe(true)

    const input = document.body.querySelector('[data-test="danger-confirm-input"]') as HTMLInputElement
    expect(input).not.toBeNull()
    input.value = 'WRONG'
    input.dispatchEvent(new Event('input'))
    await wrapper.vm.$nextTick()
    expect(confirmBtn.disabled).toBe(true)

    input.value = 'REVOKE'
    input.dispatchEvent(new Event('input'))
    await wrapper.vm.$nextTick()
    expect(confirmBtn.disabled).toBe(false)

    confirmBtn.click()
    expect(wrapper.emitted('confirm')).toBeTruthy()
    wrapper.unmount()
  })
})
