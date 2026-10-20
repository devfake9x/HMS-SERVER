<script setup lang="ts">
import {
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuPortal,
  DropdownMenuRoot,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from 'reka-ui'
import { useRouter } from 'vue-router'

import { iconPath } from '#/shared/ui/icons'
import { useTranslation } from '#/shared/i18n'

/**
 * Menu tài khoản người dùng tại góc phải Header.
 *
 * Tích hợp thông tin người dùng, vai trò, và nút Đăng xuất (FR-6 compliant).
 */

defineProps<{
  userName: string
  userInitials: string
  userEmail?: string
  userRole?: string
}>()

defineEmits<{ signOut: [] }>()

const router = useRouter()
const { t } = useTranslation()

function goToAccount(): void {
  void router.push({ name: 'account-settings' })
}
</script>

<template>
  <DropdownMenuRoot>
    <DropdownMenuTrigger
      class="userdropdown__trigger"
      type="button"
      :aria-label="`Tài khoản: ${userName}. Bấm để mở menu.`"
    >
      <span class="userdropdown__avatar" aria-hidden="true">{{ userInitials }}</span>
      <span class="userdropdown__name">{{ userName }}</span>
      <svg
        class="userdropdown__chevron"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        stroke-width="2"
        aria-hidden="true"
      >
        <path v-if="iconPath('chevron-down')" :d="iconPath('chevron-down')!" />
      </svg>
    </DropdownMenuTrigger>

    <DropdownMenuPortal>
      <DropdownMenuContent
        class="userdropdown__content"
        :side-offset="6"
        align="end"
      >
        <div class="userdropdown__header">
          <span class="userdropdown__header-avatar" aria-hidden="true">{{ userInitials }}</span>
          <div class="userdropdown__header-info">
            <span class="userdropdown__header-name">{{ userName }}</span>
            <span v-if="userEmail" class="userdropdown__header-email">{{ userEmail }}</span>
            <span class="userdropdown__header-badge">{{ userRole || 'SUPER_ADMIN' }}</span>
          </div>
        </div>

        <DropdownMenuSeparator class="userdropdown__separator" />

        <DropdownMenuItem class="userdropdown__item" @select="goToAccount">
          <svg
            class="userdropdown__item-icon"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            aria-hidden="true"
          >
            <path v-if="iconPath('user')" :d="iconPath('user')!" />
          </svg>
          <span>{{ t('nav.profile', undefined, 'Thông tin tài khoản') }}</span>
        </DropdownMenuItem>

        <DropdownMenuItem class="userdropdown__item">
          <svg
            class="userdropdown__item-icon"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            aria-hidden="true"
          >
            <path v-if="iconPath('settings')" :d="iconPath('settings')!" />
          </svg>
          <span>{{ t('nav.settings', undefined, 'Cài đặt hệ thống') }}</span>
        </DropdownMenuItem>

        <DropdownMenuSeparator class="userdropdown__separator" />

        <DropdownMenuItem
          class="userdropdown__item userdropdown__item--danger"
          @select="$emit('signOut')"
        >
          <svg
            class="userdropdown__item-icon"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            aria-hidden="true"
          >
            <path v-if="iconPath('log-out')" :d="iconPath('log-out')!" />
          </svg>
          <span>{{ t('nav.logout', undefined, 'Đăng xuất') }}</span>
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenuPortal>
  </DropdownMenuRoot>
</template>

<style src="./UserDropdown.css"></style>
