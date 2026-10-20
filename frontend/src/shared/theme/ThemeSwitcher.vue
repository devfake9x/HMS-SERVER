<script setup lang="ts">
import { computed } from 'vue'

import { iconPath } from '#/shared/ui/icons'
import { useThemeStore } from './theme-store'

/**
 * Nút chuyển đổi giao diện 1-Click (Sáng ☀️ ⇄ Tối 🌙).
 *
 * Click 1 lần chuyển ngay lập tức giữa Sáng và Tối, không cần mở dropdown.
 */

import { ref } from 'vue'
import { readStoredPreference } from './theme-store'

let theme: any
try {
  theme = useThemeStore()
} catch {
  const fallback = ref(readStoredPreference())
  theme = {
    resolved: fallback,
    preference: fallback,
    setPreference: (next: any) => { fallback.value = next },
  }
}

function toggle(): void {
  const next = theme.resolved === 'dark' ? 'light' : 'dark'
  theme.setPreference(next)
}

const isDark = computed(() => theme.resolved === 'dark')
const currentIcon = computed(() => (isDark.value ? 'sun' : 'moon'))
const label = computed(() =>
  isDark.value ? 'Chuyển sang giao diện sáng' : 'Chuyển sang giao diện tối',
)
</script>

<template>
  <button
    class="themeswitch"
    type="button"
    :aria-label="label"
    :title="label"
    @click="toggle"
  >
    <svg
      class="themeswitch__icon"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      stroke-width="2"
      aria-hidden="true"
    >
      <path v-if="iconPath(currentIcon)" :d="iconPath(currentIcon)!" />
    </svg>
  </button>
</template>

<style src="./ThemeSwitcher.css"></style>
