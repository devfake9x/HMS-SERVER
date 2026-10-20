// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import LoginView from './LoginView.vue'

const mockRouter = {
  push: vi.fn(),
}

vi.mock('vue-router', () => ({
  useRouter: () => mockRouter,
  useRoute: () => ({ query: {} }),
}))

const mockLogin = vi.fn().mockResolvedValue({ success: true })
const mockGetCsrf = vi.fn().mockResolvedValue({ requestToken: 'mock-csrf' })

vi.mock('@/core/api/adminApi', () => ({
  adminApi: {},
  login: (...args: any[]) => mockLogin(...args),
  getCsrf: (...args: any[]) => mockGetCsrf(...args),
}))

describe('LoginView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.clear()
  })

  it('renders login form inputs and defaults', () => {
    const wrapper = mount(LoginView)
    expect(wrapper.find('[data-test="login-view"]').exists()).toBe(true)
    const emailInput = wrapper.find('[data-test="login-email"]') as any
    expect(emailInput.element.value).toBe('admin@hms.local')
  })

  it('submits login and redirects on success', async () => {
    const wrapper = mount(LoginView)
    const passwordInput = wrapper.find('[data-test="login-password"]')
    await passwordInput.setValue('SecretPassword123!')

    const form = wrapper.find('form')
    await form.trigger('submit.prevent')
    await flushPromises()

    expect(mockGetCsrf).toHaveBeenCalled()
    expect(mockLogin).toHaveBeenCalledWith(
      'admin@hms.local',
      'SecretPassword123!',
      expect.objectContaining({ csrfToken: 'mock-csrf' })
    )
    expect(mockRouter.push).toHaveBeenCalledWith('/overview')
  })

  it('displays error message when login fails', async () => {
    mockLogin.mockRejectedValueOnce(new Error('Invalid credentials'))
    const wrapper = mount(LoginView)
    const passwordInput = wrapper.find('[data-test="login-password"]')
    await passwordInput.setValue('WrongPassword!')

    const form = wrapper.find('form')
    await form.trigger('submit.prevent')
    await flushPromises()

    expect(wrapper.find('[data-test="login-error"]').text()).toContain('Invalid credentials')
  })
})
