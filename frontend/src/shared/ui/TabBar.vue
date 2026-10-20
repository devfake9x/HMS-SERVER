<script setup lang="ts">
/**
 * Thanh tab ngang — `border-b-2` cho mục đang chọn.
 *
 * `[ĐO 2026-09-01]` Datacake:
 * - Tab inactive: gray/500 border-transparent border-b-2 py-4 px-1 text-sm font-medium
 * - Tab active: blue/600 text + blue/500 border-b-2
 * - Icon: `w-5 h-5 -ml-0.5 mr-2 color gray/400`
 * - Height: 54px, lineHeight: 20px
 *
 * Cố tình KHÔNG dùng `<nav>` + `<a>` cho tab: đây là tab content **trong trang**,
 * không phải navigation giữa các trang. `<button role="tab">` với `aria-selected`
 * là đúng semantic — WAI-ARIA tabs pattern. `<a>` thay đổi URL, tab thay đổi panel.
 *
 * Khi dùng với router (tab = route con): component **không biết** routing — caller
 * watch `modelValue` và tự `router.push()`. Lý do: component chứa `RouterLink` thì
 * `shared/ui/` phụ thuộc `vue-router`, và test phải mock router. Một button không
 * cần mock gì.
 */

import { iconPath } from './icons'

export type TabItem = {
  /** Giá trị để so khớp với `modelValue`. */
  value: string
  /** Nhãn hiển thị. */
  label: string
  /** Tên icon (tra trong ICON_PATHS). Không bắt buộc. */
  icon?: string
  /** Huy hiệu số đếm hoặc trạng thái. Không bắt buộc. */
  badge?: string | number
}

defineProps<{
  tabs: readonly TabItem[]
  modelValue: string
}>()

const emit = defineEmits<{ 'update:modelValue': [value: string] }>()
</script>

<template>
  <div class="tabbar" role="tablist">
    <button
      v-for="tab in tabs"
      :key="tab.value"
      type="button"
      role="tab"
      class="tabbar__tab"
      :class="{ 'tabbar__tab--active': tab.value === modelValue }"
      :aria-selected="tab.value === modelValue"
      :tabindex="tab.value === modelValue ? 0 : -1"
      @click="emit('update:modelValue', tab.value)"
    >
      <svg
        v-if="iconPath(tab.icon)"
        class="tabbar__icon"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        stroke-width="2"
        stroke-linecap="round"
        stroke-linejoin="round"
        aria-hidden="true"
      >
        <path :d="iconPath(tab.icon) ?? ''" />
      </svg>
      <span>{{ tab.label }}</span>
      <span v-if="tab.badge !== undefined" class="tabbar__badge">
        {{ tab.badge }}
      </span>
    </button>
  </div>
</template>

<style scoped>
/*
 * `[ĐO 2026-09-01]` Datacake tab bar:
 * - Container: `border-b` gray/200 — viền dưới toàn thanh
 * - Tab: `py-4 px-1 border-b-2 font-medium text-sm` = pad 16px 4px
 * - Active: blue/500 border + blue/600 text
 * - Inactive: `border-transparent` color gray/500
 * - Icon: `w-5 h-5 -ml-0.5 mr-2 color gray/400`
 *
 * Dùng `--status-info` thay blue-500/600 cứng: token đã có trong `PAIRS` của
 * `check-contrast.mjs`, nên cổng 9 đo được. Hard-code hex thì ngoài vùng đo.
 */
.tabbar {
  display: flex;
  gap: 1.5rem;
  border-bottom: 1px solid var(--border-subtle);
}

.tabbar__tab {
  display: inline-flex;
  align-items: center;
  /* py-4 của Datacake = 16px dọc, chiều cao tab 54px (đo live 2026-09-02;
     bản cũ 12px làm tab cao ~46px — sai số S2, plan 260902-1315). */
  padding: 1rem 0.25rem;
  border: 0;
  border-bottom: 2px solid transparent;
  background: transparent;
  color: var(--text-secondary);
  font-family: inherit;
  font-size: 0.875rem;
  font-weight: 500;
  line-height: 1.43;
  white-space: nowrap;
  cursor: pointer;
}

.tabbar__tab:hover {
  color: var(--text-primary);
  border-bottom-color: var(--border-subtle);
}

.tabbar__tab:focus-visible {
  outline: 2px solid var(--status-info);
  outline-offset: -2px;
}

.tabbar__tab--active {
  color: var(--status-info);
  border-bottom-color: var(--status-info);
}

.tabbar__icon {
  flex: none;
  width: 1.25rem;
  height: 1.25rem;
  margin-right: 0.5rem;
  margin-left: -0.125rem;
}

.tabbar__badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  margin-left: 0.5rem;
  padding: 0.0625rem 0.4375rem;
  font-size: 0.75rem;
  font-weight: 600;
  line-height: 1.25;
  border-radius: 9999px;
  background-color: var(--surface-muted, rgba(148, 163, 184, 0.18));
  color: var(--text-secondary);
  border: 1px solid var(--border-subtle);
}

.tabbar__tab--active .tabbar__badge {
  background-color: var(--status-info-dim, rgba(37, 99, 235, 0.12));
  color: var(--status-info);
  border-color: rgba(37, 99, 235, 0.25);
}

@media (prefers-reduced-motion: reduce) {
  .tabbar__tab {
    transition: none;
  }
}
</style>
