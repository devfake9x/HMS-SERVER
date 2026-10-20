<script setup lang="ts">
/**
 * Ô tìm kiếm có icon kính lúp bên trái.
 *
 * `[ĐO 2026-09-01]` Datacake:
 * - Wrapper: `relative flex items-stretch`
 * - Icon: `absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none`
 *         → `w-4 h-4` color gray/400
 * - Input: `pl-10` (padding-left 40px cho icon), `focus:ring-0 sm:text-sm`
 *         → fontSize 14px, padding `8px 12px 8px 40px`, height 38px
 *
 * Component này KHÔNG tự debounce. Lý do: consumer biết context — autocomplete
 * remote cần 300ms debounce, filter local cần 0ms. Nếu tự chọn thì một trong hai
 * ca sai. Pattern: consumer dùng `useDebounceFn` từ VueUse hoặc tự viết.
 *
 * Tên slot `#leading-icon` đặt trước cho trường hợp cần đổi icon (ví dụ icon
 * filter khi đang lọc). Mặc định render icon magnifying glass.
 */

import { ref } from 'vue'

withDefaults(defineProps<{
  modelValue: string
  placeholder?: string
  disabled?: boolean
}>(), {
  placeholder: 'Tìm kiếm...',
  disabled: false,
})

const emit = defineEmits<{
  'update:modelValue': [value: string]
  clear: []
}>()

defineOptions({ inheritAttrs: false })

const inputRef = ref<HTMLInputElement | null>(null)

function onInput(e: Event) {
  emit('update:modelValue', (e.target as HTMLInputElement).value)
}

function onClear() {
  emit('update:modelValue', '')
  emit('clear')
  inputRef.value?.focus()
}
</script>

<template>
  <div class="search-input">
    <div class="search-input__icon" aria-hidden="true">
      <slot name="leading-icon">
        <svg
          viewBox="0 0 20 20"
          fill="currentColor"
          aria-hidden="true"
          data-slot="icon"
        >
          <path
            fill-rule="evenodd"
            d="M9 3.5a5.5 5.5 0 1 0 0 11 5.5 5.5 0 0 0 0-11ZM2 9a7 7 0 1 1 12.452 4.391l3.328 3.329a.75.75 0 1 1-1.06 1.06l-3.329-3.328A7 7 0 0 1 2 9Z"
            clip-rule="evenodd"
          />
        </svg>
      </slot>
    </div>
    <input
      ref="inputRef"
      type="search"
      class="search-input__field"
      :class="{ 'search-input__field--has-clear': Boolean(modelValue) && !disabled }"
      :value="modelValue"
      :placeholder="placeholder"
      :disabled="disabled"
      v-bind="$attrs"
      @input="onInput"
    />
    <button
      v-if="Boolean(modelValue) && !disabled"
      type="button"
      class="search-input__clear-btn"
      aria-label="Xóa nội dung tìm kiếm"
      title="Xóa nội dung tìm kiếm"
      @click.stop="onClear"
    >
      <svg
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        stroke-width="2"
        stroke-linecap="round"
        stroke-linejoin="round"
        aria-hidden="true"
      >
        <line x1="18" y1="6" x2="6" y2="18" />
        <line x1="6" y1="6" x2="18" y2="18" />
      </svg>
    </button>
  </div>
</template>

<style scoped>
/*
 * `[ĐO 2026-09-01]` Datacake search input:
 * - Wrapper: `relative flex`, height 38px
 * - Icon container: `absolute inset-y-0 left-0 pl-3 flex items-center`
 *   → icon `w-4 h-4` = 16×16px, color gray/400
 * - Input: `pl-10` = 40px, `sm:text-sm` = 14px, border gray-300, radius 6px
 *
 * Dùng token hệ thống: `--border-control`, `--text-secondary`, `--surface-panel`.
 */
.search-input {
  position: relative;
  display: flex;
  align-items: stretch;
  width: 100%;
  min-width: 0;
  height: 38px;
}

.search-input__icon {
  position: absolute;
  inset-block: 0;
  inset-inline-start: 0;
  display: flex;
  align-items: center;
  padding-inline-start: 0.75rem;
  pointer-events: none;
  color: var(--text-hint, #9ca3af);
}

.search-input__icon svg {
  width: 1rem;
  height: 1rem;
  flex: none;
}

.search-input__field {
  width: 100%;
  height: 38px;
  box-sizing: border-box;
  padding: 0.5rem 0.75rem 0.5rem 2.5rem;
  border: 1px solid var(--border-control, #d1d5db);
  border-radius: 0.375rem;
  background: var(--surface-panel, #ffffff);
  color: var(--text-primary);
  font-family: inherit;
  font-size: 0.875rem;
  line-height: 1.43;
  transition: border-color 120ms ease, box-shadow 120ms ease;
}

.search-input__field:hover:not(:disabled) {
  border-color: #cbd5e1;
}

.search-input__field::placeholder {
  color: var(--text-secondary);
}

.search-input__field:focus {
  outline: none;
  border-color: var(--input-border-focus, #3b82f6);
  box-shadow: var(--input-focus-ring, 0 0 0 2px rgba(59, 130, 246, 0.15));
}

.search-input__field:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.search-input__field--has-clear {
  padding-inline-end: 2.25rem;
}

.search-input__clear-btn {
  position: absolute;
  inset-block: 0;
  inset-inline-end: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  padding-inline: 0.625rem;
  border: none;
  background: transparent;
  color: var(--text-secondary);
  cursor: pointer;
  border-radius: 0.375rem;
  transition: color 120ms ease;
}

.search-input__clear-btn:hover {
  color: var(--text-primary);
}

.search-input__clear-btn:focus-visible {
  outline: none;
  box-shadow: var(--input-focus-ring, 0 0 0 2px rgba(59, 130, 246, 0.18));
}

@media (forced-colors: active) {
  .search-input__field:focus {
    outline: 2px solid Highlight;
    outline-offset: 1px;
  }
  .search-input__clear-btn:focus-visible {
    outline: 2px solid Highlight;
  }
}

.search-input__clear-btn svg {
  width: 0.875rem;
  height: 0.875rem;
  flex: none;
}

/* Ẩn nút X mặc định của WebKit để dùng nút X đồng bộ của hệ thống trên mọi trình duyệt */
.search-input__field::-webkit-search-cancel-button {
  -webkit-appearance: none;
  appearance: none;
  display: none;
}
</style>
