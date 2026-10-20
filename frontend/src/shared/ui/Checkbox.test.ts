// @vitest-environment jsdom
import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import { nextTick } from 'vue'

import Checkbox from './Checkbox.vue'
import checkboxSource from './Checkbox.vue?raw'

function box(wrapper: ReturnType<typeof mount>): HTMLInputElement {
  return wrapper.find('input[type="checkbox"]').element as HTMLInputElement
}

describe('Checkbox — trạng thái và event', () => {
  it('modelValue=false thì ô không được đánh dấu', () => {
    const w = mount(Checkbox, { props: { modelValue: false, label: 'Chọn' } })
    expect(box(w).checked).toBe(false)
  })

  it('modelValue=true thì ô được đánh dấu', () => {
    const w = mount(Checkbox, { props: { modelValue: true, label: 'Chọn' } })
    expect(box(w).checked).toBe(true)
  })

  it('bấm vào ô chưa đánh dấu phát update:modelValue = true', async () => {
    const w = mount(Checkbox, { props: { modelValue: false, label: 'Chọn' } })
    await w.find('input').setValue(true)
    expect(w.emitted('update:modelValue')?.[0]).toEqual([true])
  })

  it('bấm vào ô đã đánh dấu phát update:modelValue = false', async () => {
    const w = mount(Checkbox, { props: { modelValue: true, label: 'Chọn' } })
    await w.find('input').setValue(false)
    expect(w.emitted('update:modelValue')?.[0]).toEqual([false])
  })

  it('phát cả change cùng giá trị với update:modelValue', async () => {
    const w = mount(Checkbox, { props: { modelValue: false, label: 'Chọn' } })
    await w.find('input').setValue(true)
    expect(w.emitted('change')?.[0]).toEqual([true])
  })
})

describe('Checkbox — indeterminate là property, không phải attribute', () => {
  it('indeterminate=true đặt property trên DOM node thật', async () => {
    const w = mount(Checkbox, {
      props: { modelValue: false, indeterminate: true, label: 'Chọn tất cả' },
    })
    await nextTick()
    expect(box(w).indeterminate).toBe(true)
  })

  it('indeterminate KHÔNG kéo theo checked — ba trạng thái phân biệt được', async () => {
    const w = mount(Checkbox, {
      props: { modelValue: false, indeterminate: true, label: 'Chọn tất cả' },
    })
    await nextTick()
    expect(box(w).indeterminate).toBe(true)
    expect(box(w).checked).toBe(false)
  })

  it('mặc định không indeterminate', async () => {
    const w = mount(Checkbox, { props: { modelValue: false, label: 'Chọn' } })
    await nextTick()
    expect(box(w).indeterminate).toBe(false)
  })

  it('đổi prop từ true sang false thì property theo kịp', async () => {
    const w = mount(Checkbox, {
      props: { modelValue: false, indeterminate: true, label: 'Chọn tất cả' },
    })
    await nextTick()
    expect(box(w).indeterminate).toBe(true)

    await w.setProps({ indeterminate: false })
    await nextTick()
    expect(box(w).indeterminate).toBe(false)
  })

  it('chọn hết: checked=true và indeterminate=false', async () => {
    const w = mount(Checkbox, {
      props: { modelValue: false, indeterminate: true, label: 'Chọn tất cả' },
    })
    await w.setProps({ modelValue: true, indeterminate: false })
    await nextTick()

    expect(box(w).checked).toBe(true)
    expect(box(w).indeterminate).toBe(false)
  })
})

describe('Checkbox — nhãn', () => {
  it('nhãn hiện ra khi labelHidden không bật', () => {
    const w = mount(Checkbox, { props: { modelValue: false, label: 'Hiện cột' } })
    expect(w.text()).toContain('Hiện cột')
    expect(w.find('.sr-only').exists()).toBe(false)
  })

  it('labelHidden giấu nhãn về thị giác nhưng GIỮ trong cây DOM', () => {
    const w = mount(Checkbox, {
      props: { modelValue: false, label: 'Chọn dòng', labelHidden: true },
    })
    expect(w.find('.sr-only').exists()).toBe(true)
    expect(w.text()).toContain('Chọn dòng')
  })

  it('nhãn nằm trong <label> bọc input nên bấm chữ cũng bật/tắt', () => {
    const w = mount(Checkbox, { props: { modelValue: false, label: 'Chọn' } })
    expect(w.element.tagName).toBe('LABEL')
    expect(w.find('label input[type="checkbox"]').exists()).toBe(true)
  })
})

describe('Checkbox — disabled', () => {
  it('disabled đặt thuộc tính lên input thật', () => {
    const w = mount(Checkbox, {
      props: { modelValue: false, label: 'Chọn', disabled: true },
    })
    expect(box(w).disabled).toBe(true)
  })

  it('disabled vẫn giữ nguyên nhãn đọc được', () => {
    const w = mount(Checkbox, {
      props: { modelValue: false, label: 'Chọn', disabled: true },
    })
    expect(w.text()).toContain('Chọn')
  })

  it('rule CSS: disabled fade TOÀN BỘ box (opacity < 1), áp cho cả checked lẫn chưa check', () => {
    const source = checkboxSource

    const disabledRule = source.match(/\.checkbox--disabled \.checkbox__box\s*\{[^}]*\}/)
    expect(disabledRule).not.toBeNull()

    const opacity = disabledRule![0].match(/opacity:\s*([\d.]+)/)
    expect(opacity).not.toBeNull()
    expect(Number(opacity![1])).toBeLessThan(1)

    expect(source).not.toContain(':not(.checkbox__box--checked)')
  })
})
