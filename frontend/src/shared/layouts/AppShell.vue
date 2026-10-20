<script setup lang="ts">
import { computed, onBeforeUnmount, provide, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import type { AuthSubject } from '#/core/auth/can'
import LocaleSwitcher from '#/shared/i18n/LocaleSwitcher.vue'
import NavTree from '#/shared/navigation/NavTree.vue'
import AlarmBell from '#/shared/notify/AlarmBell.vue'
import ThemeSwitcher from '#/shared/theme/ThemeSwitcher.vue'
import { iconPath } from '#/shared/ui/icons'
import Breadcrumb from '#/shared/ui/Breadcrumb.vue'
import { expandedKeysForRoute, visibleNav, type NavItem } from '#/shared/navigation/nav'
import { provideBreadcrumbsNav, useBreadcrumbs, SHELL_HEADER_KEY } from '#/shared/navigation/useBreadcrumbs'

import AppFooter from './AppFooter.vue'
import { NARROW_MEDIA_QUERY } from './shell-media'
import UserDropdown from './UserDropdown.vue'
import { useSidebarCollapse } from './useSidebarCollapse'

/**
 * Khung ứng dụng: sidebar (Brand logo + Navigation) + header (Status + Tools + User Dropdown) + content.
 */

import { navItems } from '#/app/nav.config'

const props = withDefaults(
  defineProps<{
    items?: readonly NavItem[]
    subject?: AuthSubject
    /** Tên hiển thị của người đang đăng nhập. */
    userName?: string
    /** Chữ viết tắt cho avatar. */
    userInitials?: string
    /** Email người dùng (tuỳ chọn). */
    userEmail?: string
  }>(),
  {
    items: () => navItems,
    subject: () => ({ permissions: null, roles: ['SUPER_ADMIN'] }),
    userName: 'Admin',
    userInitials: 'AD',
    userEmail: 'admin@hms.local',
  },
)

defineEmits<{ signOut: [] }>()

provideBreadcrumbsNav(computed(() => props.items))
provide(SHELL_HEADER_KEY, true)

const router = useRouter()
const route = useRoute()
const sidebarOpen = ref(false)

const { breadcrumbs } = useBreadcrumbs(computed(() => props.items))

// Hiển thị nút lùi khi không phải trang gốc (home/overview)
const canGoBack = computed(() => {
  const name = String(route.name ?? '')
  return Boolean(name) && name !== 'home' && name !== 'monitoring-overview'
})

async function onGoBack(): Promise<void> {
  const trail = breadcrumbs.value
  const currentName = String(route.name ?? '')

  // 1. Tìm mục cha gần nhất trong breadcrumb khác trang hiện tại (ưu tiên phân cấp thực sự)
  if (trail.length > 1) {
    for (let i = trail.length - 2; i >= 0; i--) {
      const ancestor = trail[i]
      if (
        ancestor.routeName &&
        ancestor.routeName !== currentName &&
        router.hasRoute(ancestor.routeName)
      ) {
        await router.push({ name: ancestor.routeName, params: ancestor.routeParams })
        return
      }
    }
  }

  // 2. Quay lại trang trước trong lịch sử điều hướng của Router
  const prevRoute = router.currentRoute.value.fullPath
  router.back()

  // 3. Fallback an toàn nếu không có lịch sử (ví dụ direct URL vào trang con mồ côi)
  if (typeof setTimeout !== 'undefined') {
    setTimeout(() => {
      if (router.currentRoute.value.fullPath === prevRoute && router.currentRoute.value.name !== 'home') {
        void router.push({ name: 'home' })
      }
    }, 50)
  }
}

const items = computed(() => visibleNav(props.items, props.subject))
const expandedKeys = ref<Set<string>>(new Set())

watch(
  () => route.name,
  (name) => {
    for (const key of expandedKeysForRoute(props.items, String(name ?? ''))) {
      expandedKeys.value.add(key)
    }
    sidebarOpen.value = false
  },
  { immediate: true },
)

function toggleGroup(key: string): void {
  const next = new Set(expandedKeys.value)
  if (next.has(key)) next.delete(key)
  else next.add(key)
  expandedKeys.value = next
}

const isDrawerMode = ref(false)
let media: MediaQueryList | null = null

function onMediaChange(event: MediaQueryListEvent): void {
  isDrawerMode.value = event.matches
}

try {
  media = window.matchMedia(NARROW_MEDIA_QUERY)
  isDrawerMode.value = media.matches
  media.addEventListener('change', onMediaChange)
} catch {
  media = null
}

const drawerHidden = computed(() => isDrawerMode.value && !sidebarOpen.value)

const burgerEl = ref<HTMLButtonElement | null>(null)
const sideEl = ref<HTMLElement | null>(null)

// Quản lý trạng thái thu gọn thanh bên (Collapsed Sidebar Rail)
const { isCollapsed, toggle: toggleCollapse } = useSidebarCollapse()

function onKeydown(event: KeyboardEvent): void {
  if (event.key !== 'Escape') return

  const focusWasInside = sideEl.value?.contains(document.activeElement) ?? false
  sidebarOpen.value = false
  if (focusWasInside) burgerEl.value?.focus()
}

function onGlobalKeydown(event: KeyboardEvent): void {
  // Phím tắt toàn cục: Ctrl + [ hoặc Alt + B để đóng/mở thu gọn menu (trên desktop)
  if (
    !isDrawerMode.value &&
    ((event.ctrlKey && event.key === '[') || (event.altKey && (event.key === 'b' || event.key === 'B')))
  ) {
    event.preventDefault()
    toggleCollapse()
  }
}

if (typeof document !== 'undefined') {
  document.addEventListener('keydown', onGlobalKeydown)
}

watch(sidebarOpen, (open) => {
  if (open) document.addEventListener('keydown', onKeydown)
  else document.removeEventListener('keydown', onKeydown)
})

onBeforeUnmount(() => {
  media?.removeEventListener('change', onMediaChange)
  document.removeEventListener('keydown', onKeydown)
  if (typeof document !== 'undefined') {
    document.removeEventListener('keydown', onGlobalKeydown)
  }
})
</script>

<template>
  <div
    class="shell"
    :class="{
      'shell--nav-open': sidebarOpen,
      'shell--nav-collapsed': isCollapsed && !isDrawerMode,
    }"
  >
    <a class="shell__skip" href="#main">Bỏ qua điều hướng, tới nội dung</a>

    <header class="shell__header">
      <div class="shell__header-left">
        <button
          ref="burgerEl"
          type="button"
          class="shell__burger"
          :aria-expanded="sidebarOpen"
          aria-controls="app-nav"
          :aria-label="sidebarOpen ? 'Đóng menu' : 'Mở menu'"
          @click="sidebarOpen = !sidebarOpen"
        >
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true">
            <path d="M3 6h18M3 12h18M3 18h18" />
          </svg>
        </button>

        <!-- Nút mũi tên lùi Datacake Parity -->
        <button
          v-if="canGoBack"
          type="button"
          class="shell__back-btn"
          data-testid="shell-back-btn"
          aria-label="Quay lại"
          title="Quay lại (Alt+Left)"
          @click="onGoBack"
        >
          <svg
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            stroke-linecap="round"
            stroke-linejoin="round"
            class="shell__back-icon"
            aria-hidden="true"
          >
            <polyline points="15 18 9 12 15 6" />
          </svg>
        </button>

        <!-- Breadcrumb ở Header theo thiết kế Datacake -->
        <div class="shell__header-breadcrumb" data-testid="shell-header-breadcrumb">
          <Breadcrumb :items="breadcrumbs" separator="slash" />
        </div>
      </div>

      <div class="shell__tools">
        <!-- Quả chuông cảnh báo -->
        <AlarmBell />

        <!-- Bộ chuyển ngôn ngữ -->
        <LocaleSwitcher />

        <!-- Bộ chuyển giao diện -->
        <ThemeSwitcher />

        <span class="shell__divider" aria-hidden="true" />

        <!-- User Dropdown Menu -->
        <UserDropdown
          :user-name="userName"
          :user-initials="userInitials"
          :user-email="userEmail"
          @sign-out="$emit('signOut')"
        />
      </div>
    </header>

    <div
      id="app-nav"
      ref="sideEl"
      class="shell__side"
      :inert="drawerHidden || undefined"
      :aria-hidden="drawerHidden ? 'true' : undefined"
    >
      <!-- Khối Logo và Nhận diện Thương hiệu ở ĐẦU Sidebar -->
      <div class="shell__brand">
        <div class="shell__brand-logo" aria-hidden="true">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <path v-if="iconPath('cube')" :d="iconPath('cube')!" />
          </svg>
        </div>
        <div class="shell__brand-info">
          <span class="shell__brand-title">Concrete Stress</span>
          <span class="shell__brand-sub">
            Monitoring
            <span class="shell__brand-badge">v2.0</span>
          </span>
        </div>
      </div>

      <nav class="shell__nav" aria-label="Điều hướng chính">
        <NavTree
          :items="items"
          :active-route-name="String(route.name ?? '')"
          :active-route-query="route.query"
          :expanded-keys="expandedKeys"
          :is-collapsed="isCollapsed && !isDrawerMode"
          @toggle="toggleGroup"
        />
      </nav>

      <!-- Nút thu gọn / mở rộng menu (chỉ hiện trên desktop) -->
      <div v-if="!isDrawerMode" class="shell__collapse-footer">
        <button
          type="button"
          class="shell__collapse-toggle"
          :aria-expanded="!isCollapsed"
          aria-controls="app-nav"
          :aria-label="isCollapsed ? 'Mở rộng menu' : 'Thu gọn menu'"
          :title="isCollapsed ? 'Mở rộng menu (Ctrl+[)' : 'Thu gọn menu (Ctrl+[)'"
          @click="toggleCollapse"
        >
          <svg
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="1.75"
            stroke-linecap="round"
            stroke-linejoin="round"
            class="shell__collapse-icon"
            aria-hidden="true"
          >
            <rect width="18" height="18" x="3" y="3" rx="2" />
            <path d="M9 3v18" />
            <path v-if="!isCollapsed" d="m16 15-3-3 3-3" />
            <path v-else d="m14 9 3 3-3 3" />
          </svg>
        </button>
      </div>
    </div>

    <main id="main" class="shell__main" tabindex="-1">
      <slot>
        <router-view />
      </slot>
    </main>

    <AppFooter />
  </div>
</template>

<style scoped src="./AppShell.css"></style>
