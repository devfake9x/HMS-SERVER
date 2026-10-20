<script setup lang="ts">
import { ref } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { adminApi, getCsrf, login } from '@/core/api/adminApi'
import { createSessionFence } from '@/core/auth/sessionFence'
import AuthLayout from '@/shared/layouts/AuthLayout.vue'
import Button from '@/shared/ui/primitives/Button.vue'
import { Shield, Lock, Mail, Key } from '@lucide/vue'

const router = useRouter()
const route = useRoute()

const email = ref('admin@hms.local')
const password = ref('')
const code2fa = ref('')
const loading = ref(false)
const errorMessage = ref('')
const sessionFence = createSessionFence()

async function handleLogin() {
  if (!email.value || !password.value) {
    errorMessage.value = 'Please provide both email and password.'
    return
  }

  loading.value = true
  errorMessage.value = ''
  const lease = sessionFence.rotate()

  try {
    const csrf = await getCsrf({ signal: lease.signal }).catch(() => null)
    if (!sessionFence.isCurrent(lease)) return

    const csrfToken = csrf?.requestToken ?? ''
    await login(email.value, password.value, {
      csrfToken,
      signal: lease.signal,
    })

    if (!sessionFence.isCurrent(lease)) return

    // Store in localStorage for persistence
    localStorage.setItem('hms_auth_user', JSON.stringify({
      email: email.value,
      role: 'Administrator',
      loggedInAt: new Date().toISOString(),
    }))

    const redirect = (route.query.redirect as string) || '/overview'
    await router.push(redirect)
  } catch (err: any) {
    if (!sessionFence.isCurrent(lease)) return
    errorMessage.value = err?.message || 'Invalid credentials or connection error. Please verify server availability.'
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <AuthLayout title="Hệ thống Quản trị HMS" subtitle="Nền tảng Giám sát & Điều khiển IoT Công nghiệp">
    <div class="space-y-6" data-test="login-view">
      <div>
        <h2 class="text-xl font-bold tracking-tight text-[var(--text-heading)]">
          Sign in to your account
        </h2>
        <p class="mt-1.5 text-xs text-[var(--text-hint)]">
          Industrial IoT Command & Control Platform
        </p>
      </div>

      <!-- Error alert -->
      <div
        v-if="errorMessage"
        class="p-3 rounded-lg bg-rose-500/10 border border-rose-500/20 text-xs text-rose-600 dark:text-rose-400 font-medium flex items-center gap-2"
        data-test="login-error"
      >
        <Shield class="w-4 h-4 flex-shrink-0" />
        <span>{{ errorMessage }}</span>
      </div>

      <form @submit.prevent="handleLogin" class="space-y-4">
        <!-- Email -->
        <div>
          <label class="block text-xs font-medium text-[var(--text-secondary)] mb-1.5">
            Email address
          </label>
          <div class="relative">
            <div class="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none text-[var(--text-hint)]">
              <Mail class="w-4 h-4" />
            </div>
            <input
              v-model="email"
              type="email"
              required
              autocomplete="email"
              placeholder="admin@hms.local"
              class="w-full pl-9 pr-3 py-2 text-xs rounded-md bg-[var(--surface-panel)] border border-[var(--border-default)] text-[var(--text-primary)] placeholder-[var(--text-hint)] focus:outline-none focus:border-[var(--color-primary)] transition-colors"
              data-test="login-email"
            />
          </div>
        </div>

        <!-- Password -->
        <div>
          <div class="flex items-center justify-between mb-1.5">
            <label class="block text-xs font-medium text-[var(--text-secondary)]">
              Password
            </label>
            <span class="text-[11px] text-[var(--text-hint)]">Default: AdminPassword123!</span>
          </div>
          <div class="relative">
            <div class="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none text-[var(--text-hint)]">
              <Lock class="w-4 h-4" />
            </div>
            <input
              v-model="password"
              type="password"
              required
              autocomplete="current-password"
              placeholder="••••••••••••"
              class="w-full pl-9 pr-3 py-2 text-xs rounded-md bg-[var(--surface-panel)] border border-[var(--border-default)] text-[var(--text-primary)] placeholder-[var(--text-hint)] focus:outline-none focus:border-[var(--color-primary)] transition-colors"
              data-test="login-password"
            />
          </div>
        </div>

        <!-- Optional 2FA Code -->
        <div>
          <label class="block text-xs font-medium text-[var(--text-secondary)] mb-1.5">
            2FA Security Token <span class="text-[var(--text-hint)] font-normal">(if MFA enabled)</span>
          </label>
          <div class="relative">
            <div class="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none text-[var(--text-hint)]">
              <Key class="w-4 h-4" />
            </div>
            <input
              v-model="code2fa"
              type="text"
              maxlength="6"
              placeholder="6-digit code"
              class="w-full pl-9 pr-3 py-2 text-xs font-mono rounded-md bg-[var(--surface-panel)] border border-[var(--border-default)] text-[var(--text-primary)] placeholder-[var(--text-hint)] focus:outline-none focus:border-[var(--color-primary)] transition-colors tracking-widest"
              data-test="login-2fa"
            />
          </div>
        </div>

        <div class="pt-2">
          <Button
            type="submit"
            variant="primary"
            size="md"
            class="w-full"
            :loading="loading"
            data-test="login-submit"
          >
            Sign in
          </Button>
        </div>
      </form>

      <!-- Security Notice -->
      <div class="pt-4 border-t border-[var(--border-subtle)] text-center">
        <p class="text-[11px] text-[var(--text-hint)]">
          Protected by cryptographic session fences and anti-CSRF tokens.
        </p>
      </div>
    </div>
  </AuthLayout>
</template>
