// @vitest-environment jsdom
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import StatusDot from './StatusDot.vue'
import Badge from './Badge.vue'
import Button from './Button.vue'

describe('Shared UI Primitives', () => {
  describe('StatusDot', () => {
    it('renders with default idle state', () => {
      const wrapper = mount(StatusDot)
      expect(wrapper.find('[data-test="status-dot"]').exists()).toBe(true)
      expect(wrapper.html()).toContain('bg-zinc-400')
    })

    it('renders online state with pulse animation', () => {
      const wrapper = mount(StatusDot, {
        props: {
          status: 'online',
          pulse: true,
          size: 'lg',
        },
      })
      expect(wrapper.html()).toContain('bg-emerald-500')
      expect(wrapper.html()).toContain('animate-ping')
      expect(wrapper.html()).toContain('w-3.5')
    })

    it('renders warning and error statuses correctly', () => {
      const warningWrapper = mount(StatusDot, { props: { status: 'warning' } })
      expect(warningWrapper.html()).toContain('bg-amber-500')

      const errorWrapper = mount(StatusDot, { props: { status: 'error' } })
      expect(errorWrapper.html()).toContain('bg-rose-500')
    })
  })

  describe('Badge', () => {
    it('renders default slot and variant classes', () => {
      const wrapper = mount(Badge, {
        props: { variant: 'success', rounded: true },
        slots: { default: 'Active Device' },
      })
      expect(wrapper.text()).toContain('Active Device')
      expect(wrapper.classes()).toContain('rounded-full')
      expect(wrapper.html()).toContain('text-emerald-700')
    })
  })

  describe('Button', () => {
    it('renders primary button and handles clicks', async () => {
      const wrapper = mount(Button, {
        props: { variant: 'primary' },
        slots: { default: 'Deploy' },
      })
      expect(wrapper.text()).toContain('Deploy')
      expect(wrapper.html()).toContain('bg-orange-600')
      await wrapper.trigger('click')
      expect(wrapper.emitted('click')).toBeTruthy()
    })

    it('disables button when loading is true', () => {
      const wrapper = mount(Button, {
        props: { loading: true },
        slots: { default: 'Processing' },
      })
      const btn = wrapper.find('button')
      expect(btn.attributes('disabled')).toBeDefined()
      expect(wrapper.find('svg.animate-spin').exists()).toBe(true)
    })
  })
})
