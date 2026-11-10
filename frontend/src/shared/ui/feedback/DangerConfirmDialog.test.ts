// @vitest-environment jsdom
import { describe, it, expect, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
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

  const base = { modelValue: true, title: 'Reboot', message: 'Relays change.', confirmPhrase: 'GO' }
  const typePhrase = async (wrapper: ReturnType<typeof mount>) => {
    const input = document.body.querySelector('[data-test="danger-confirm-input"]') as HTMLInputElement
    input.value = 'GO'
    input.dispatchEvent(new Event('input'))
    await wrapper.vm.$nextTick()
  }
  const confirmButton = () => document.body.querySelector('[data-test="danger-confirm-btn"]') as HTMLButtonElement

  it('renders its default slot in the dialog, below the message', () => {
    const wrapper = mount(DangerConfirmDialog, {
      attachTo: document.body,
      props: base,
      slots: { default: '<ul data-test="slot-body"><li>DO1 sẽ TẮT</li></ul>' },
    })

    const slot = document.body.querySelector('[data-test="slot-body"]')
    expect(slot?.textContent).toContain('DO1 sẽ TẮT')
    expect(document.body.querySelector('[data-test="danger-confirm-input"]')).not.toBeNull()
    wrapper.unmount()
  })

  it('keeps the English labels unless the caller supplies its own', () => {
    const wrapper = mount(DangerConfirmDialog, { attachTo: document.body, props: base })
    expect(document.body.querySelector('[data-test="danger-cancel-btn"]')?.textContent?.trim()).toBe('Cancel')
    expect(document.body.textContent).toContain('Please type')
    wrapper.unmount()
  })

  it('takes cancelText and promptText from the caller', () => {
    const wrapper = mount(DangerConfirmDialog, {
      attachTo: document.body,
      props: { ...base, cancelText: 'Huỷ', promptText: 'Gõ', promptSuffix: 'để xác nhận:', inputPlaceholder: 'Nhập mã xác nhận' },
    })

    expect(document.body.querySelector('[data-test="danger-cancel-btn"]')?.textContent?.trim()).toBe('Huỷ')
    expect(document.body.textContent).toContain('Gõ')
    expect(document.body.textContent).not.toContain('Please type')
    expect(document.body.textContent).toContain('để xác nhận:')
    expect(document.body.textContent).not.toContain('to confirm:')
    expect(document.body.querySelector('[data-test="danger-confirm-input"]')?.getAttribute('placeholder')).toBe('Nhập mã xác nhận')
    wrapper.unmount()
  })

  it('does not emit confirm when beforeConfirm says no, even with the right phrase', async () => {
    const beforeConfirm = vi.fn().mockResolvedValue(false)
    const wrapper = mount(DangerConfirmDialog, { attachTo: document.body, props: { ...base, beforeConfirm } })
    await typePhrase(wrapper)

    confirmButton().click()
    await flushPromises()

    expect(beforeConfirm).toHaveBeenCalledOnce()
    expect(wrapper.emitted('confirm')).toBeUndefined()
    wrapper.unmount()
  })

  it('emits confirm once beforeConfirm says yes, and never asks while the phrase is wrong', async () => {
    const beforeConfirm = vi.fn().mockReturnValue(true)
    const wrapper = mount(DangerConfirmDialog, { attachTo: document.body, props: { ...base, beforeConfirm } })

    confirmButton().click()
    await flushPromises()
    expect(beforeConfirm).not.toHaveBeenCalled()

    await typePhrase(wrapper)
    confirmButton().click()
    await flushPromises()

    expect(beforeConfirm).toHaveBeenCalledOnce()
    expect(wrapper.emitted('confirm')).toHaveLength(1)
    wrapper.unmount()
  })
})
