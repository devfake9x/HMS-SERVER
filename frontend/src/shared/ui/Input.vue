<script setup lang="ts">
/**
 * Ô nhập một dòng.
 *
 * Phần tử gốc **chính là `<input>`**, không phải một `<div>` bọc ngoài. Nhờ vậy
 * mọi attribute truyền vào (`id`, `type`, `placeholder`, `required`,
 * `aria-describedby`, `aria-invalid`, `autocomplete`…) rơi thẳng xuống input mà
 * không cần khai lại từng cái. Bọc thêm một lớp `div` là chỗ để `id` gắn sai
 * phần tử và `<label for>` mất tác dụng.
 *
 * KHÔNG có prop `label` hay `error`. Nhãn và lỗi thuộc `FormField.vue`: có ba
 * loại control (input, select, textarea) và nhiều control tự viết, nên nhồi
 * nhãn vào từng cái là nhân bản ba lần cùng một logic `aria-describedby`.
 *
 * Trạng thái sai được lấy từ `aria-invalid` chứ không từ một prop riêng — xem
 * selector trong style. Một nguồn duy nhất thì hình và ngữ nghĩa không lệch nhau
 * được.
 */

import { computed } from 'vue'
import { usePlaceholderTranslation } from '#/shared/i18n/placeholders'

const props = withDefaults(
  defineProps<{
    size?: 'sm' | 'md'
    placeholder?: string
  }>(),
  {
    size: 'md',
    placeholder: undefined,
  },
)

const { t: tPlaceholder } = usePlaceholderTranslation()

const effectivePlaceholder = computed(() => {
  if (props.placeholder !== undefined) {
    return props.placeholder
  }
  return tPlaceholder('enterData')
})

const model = defineModel<string | number | null>({ default: '' })
</script>

<template>
  <input v-model="model" class="input" :class="`input--${size}`" :placeholder="effectivePlaceholder" />
</template>

<style scoped>
.input {
  width: 100%;
  border: 1px solid var(--border-control, #d1d5db);
  border-radius: 0.375rem;
  background-color: var(--surface-panel);
  color: var(--text-primary);
  font-family: inherit;
  line-height: 1.43;
  transition: border-color 120ms ease;
}

.input--sm {
  padding: 0.3rem 0.5rem;
  font-size: 0.8125rem;
}

/* `[ĐO]` Datacake `padding: 8px 12px` cho input cỡ thường. */
.input--md {
  padding: 0.5rem 0.75rem;
  font-size: 0.875rem;
}

.input::placeholder {
  color: var(--text-secondary);
}

.input:hover:not(:disabled):not(:read-only) {
  border-color: #cbd5e1;
}

/* [ĐO source Datacake account/profile] focus: viền đậm lên gray, KHÔNG
 * outline xanh — viền focus dùng token border-focus. */
.input:focus-visible {
  outline: none;
  border-color: var(--input-border-focus, #3b82f6);
  box-shadow: var(--input-focus-ring, 0 0 0 2px rgba(59, 130, 246, 0.15));
}

.input:disabled,
.input:read-only {
  color: var(--text-secondary);
  cursor: not-allowed;
  /* [ĐO source] disabled: nền xám nhạt + cursor cấm */
  background-color: var(--surface-control-disabled, #f9fafb);
}

/* Viền đỏ đi theo `aria-invalid`, không theo một prop riêng: hình và thứ screen
   reader nghe được luôn khớp nhau. */
.input[aria-invalid='true'] {
  border-color: var(--status-danger);
}

/* Focus trên input invalid: giữ viền đỏ đậm và vòng ring đỏ cảnh báo */
.input[aria-invalid='true']:focus-visible {
  outline: none;
  border-color: var(--status-danger);
  box-shadow: var(--input-focus-ring-danger, 0 0 0 2px rgba(239, 68, 68, 0.18));
}

.input:read-only {
  cursor: default;
}

@media (forced-colors: active) {
  .input:focus-visible {
    outline: 2px solid Highlight;
    outline-offset: 1px;
  }
}

@media (prefers-reduced-motion: reduce) {
  .input {
    transition: none;
  }
}
</style>
