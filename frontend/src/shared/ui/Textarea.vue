<script setup lang="ts">
/**
 * Ô nhập văn bản nhiều dòng (Textarea) chuẩn Design System.
 *
 * Phần tử gốc chính là `<textarea>`, không bọc `<div>` bên ngoài để `id` và
 * `aria-describedby` từ `FormField.vue` gắn thẳng vào phần tử tương tác.
 *
 * Hỗ trợ tự động mở rộng chiều cao theo nội dung (`field-sizing: content`)
 * với ngưỡng chặn tối đa `max-height` để tránh vỡ layout.
 */

import { computed } from 'vue'
import { usePlaceholderTranslation } from '#/shared/i18n/placeholders'

const props = withDefaults(
  defineProps<{
    size?: 'sm' | 'md'
    placeholder?: string
    rows?: number | string
    autoExpand?: boolean
    minHeight?: string
    maxHeight?: string
  }>(),
  {
    size: 'md',
    placeholder: undefined,
    rows: 5,
    autoExpand: true,
    minHeight: undefined,
    maxHeight: '18rem',
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
  <textarea
    v-model="model"
    class="textarea"
    :class="[`textarea--${size}`, { 'textarea--auto-expand': autoExpand }]"
    :rows="rows"
    :placeholder="effectivePlaceholder"
    :style="{
      minHeight: minHeight ?? undefined,
      maxHeight: autoExpand ? maxHeight : undefined,
    }"
  />
</template>

<style scoped>
.textarea {
  width: 100%;
  border: 1px solid var(--border-control, #d1d5db);
  border-radius: 0.375rem;
  background-color: var(--surface-panel);
  color: var(--text-primary);
  font-family: inherit;
  line-height: 1.43;
  transition: border-color 120ms ease;
  resize: vertical;
  display: block;
  box-sizing: border-box;
}

.textarea--sm {
  padding: 0.3rem 0.5rem;
  font-size: 0.8125rem;
  min-height: 5rem;
}

.textarea--md {
  padding: 0.5rem 0.75rem;
  font-size: 0.875rem;
  min-height: 8.5rem;
}

.textarea--auto-expand {
  field-sizing: content;
  overflow-y: auto;
}

.textarea::placeholder {
  color: var(--text-secondary);
}

.textarea:focus-visible {
  outline: none;
  border-color: var(--input-border-focus, #3b82f6);
  box-shadow: var(--input-focus-ring, 0 0 0 2px rgba(59, 130, 246, 0.18));
}

.textarea:disabled {
  color: var(--text-secondary);
  cursor: not-allowed;
  background-color: var(--surface-control-disabled, #f9fafb);
}

.textarea:read-only {
  color: var(--text-secondary);
  background-color: var(--surface-control-disabled, #f9fafb);
}

.textarea[aria-invalid='true'] {
  border-color: var(--status-danger);
}

.textarea[aria-invalid='true']:focus-visible {
  border-color: var(--status-danger);
  box-shadow: 0 0 0 2px var(--status-danger-ring, rgba(239, 68, 68, 0.2));
}
</style>
