<script setup lang="ts">
import { computed } from 'vue'
import OptionDropdown, { type OptionDropdownItem } from './OptionDropdown.vue'

/**
 * Dropdown chọn cột hiển thị trên bảng theo phong cách Datacake (Column Picker).
 *
 * Được xây dựng trên nền tảng component đa năng tái sử dụng `OptionDropdown.vue`.
 *
 * ## Thiết kế & Khả năng tiếp cận (Accessibility)
 *
 * - Nút trigger: Chiều cao 38px chuẩn Datacake, style secondary, viền `var(--border-control, #d1d5db)`,
 *   icon chevron nét mảnh xoay 180° khi mở menu.
 * - Menu dropdown: Chiều rộng 222px, `role="menu"`, `aria-orientation="vertical"`, shadow chuẩn Datacake.
 * - Mục chọn cột: `role="menuitemcheckbox"`, `aria-checked`, custom checkbox vector 16x16px chuẩn Datacake,
 *   icon reorder selector `↕` xám nhạt `#9ca3af` bên phải.
 * - Quản lý sự kiện: Đóng khi nhấn Escape (hoàn trả focus về trigger) hoặc click-outside (`pointerdown`).
 * - Dọn dẹp đầy đủ event listener khi component unmount.
 *
 * Mọi màu sắc dùng biến CSS tokens (`var(--surface-panel)`, `var(--border-control)`, `var(--text-primary)`...).
 */

export interface ColumnItem {
  key: string
  label: string
  mandatory?: boolean
  disabled?: boolean
}

export interface ColumnPickerProps {
  columns: readonly ColumnItem[]
  modelValue?: readonly string[]
  label?: string
  disabled?: boolean
  placement?: 'left' | 'right'
  menuWidth?: string | number
  collapsible?: boolean
  showLeadingIcon?: boolean
}

const props = withDefaults(
  defineProps<ColumnPickerProps>(),
  {
    modelValue: () => [],
    // Gốc Datacake dùng chữ "Columns" (đo live qua Chrome Remote Debugging port 9222 tại https://app.datacake.de/atin/devices);
    // mặc định "Cột" vì base là UI tiếng Việt. Nút trigger Datacake KHÔNG có icon dẫn đầu (showLeadingIcon = false).
    label: 'Cột',
    disabled: false,
    placement: 'right',
    menuWidth: 260,
    collapsible: false,
    showLeadingIcon: false,
  },
)

const emit = defineEmits<{
  'update:modelValue': [visibleKeys: string[]]
  change: [payload: { key: string; visible: boolean }]
}>()

const items = computed<OptionDropdownItem[]>(() =>
  props.columns.map((c) => ({
    key: c.key,
    label: c.label,
    mandatory: c.mandatory,
    disabled: c.disabled,
    rightIcon: 'selector',
  }))
)

function onUpdateModelValue(nextKeys: string[]): void {
  emit('update:modelValue', nextKeys)
}

function onChange(payload: { key: string; selected: boolean }): void {
  emit('change', { key: payload.key, visible: payload.selected })
}
</script>

<template>
  <OptionDropdown
    class-prefix="column-picker"
    :items="items"
    :model-value="modelValue"
    :label="label"
    :disabled="disabled"
    :placement="placement"
    :menu-width="menuWidth"
    :collapsible="collapsible"
    variant="checkbox"
    @update:model-value="onUpdateModelValue"
    @change="onChange"
  >
    <template v-if="showLeadingIcon || $slots['leading-icon']" #leading-icon>
      <slot name="leading-icon">
        <svg
          class="column-picker__icon"
          viewBox="0 0 20 20"
          fill="currentColor"
          aria-hidden="true"
        >
          <path
            d="M2 4.75C2 3.784 2.784 3 3.75 3h12.5c.966 0 1.75.784 1.75 1.75v10.5A1.75 1.75 0 0116.25 17H3.75A1.75 1.75 0 012 15.25V4.75zM3.5 6.5v8.75c0 .138.112.25.25.25h3.75V6.5H3.5zm5.5 9h7.25a.25.25 0 00.25-.25V6.5H9v9zM3.75 4.5a.25.25 0 00-.25.25V5h13v-.25a.25.25 0 00-.25-.25H3.75z"
          />
        </svg>
      </slot>
    </template>
  </OptionDropdown>
</template>

<style scoped>
.column-picker__icon {
  width: 1rem;
  height: 1rem;
  margin-left: -0.125rem;
  margin-right: 0.375rem;
  color: var(--text-secondary, #6b7280);
  flex-shrink: 0;
}
</style>
