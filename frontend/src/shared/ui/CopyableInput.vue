<script lang="ts">
export default {
  inheritAttrs: false,
}
</script>

<script setup lang="ts">
import { computed, onBeforeUnmount, ref } from 'vue'
import { usePlaceholderTranslation } from '#/shared/i18n/placeholders'

const props = withDefaults(
  defineProps<{
    size?: 'sm' | 'md'
    readonly?: boolean
    disabled?: boolean
    mono?: boolean
    copyable?: boolean
    copyText?: string
    copyLabel?: string
    copiedLabel?: string
    placeholder?: string
  }>(),
  {
    size: 'md',
    readonly: false,
    disabled: false,
    mono: false,
    copyable: true,
    copyText: undefined,
    copyLabel: 'Sao chép',
    copiedLabel: 'Đã chép',
    placeholder: undefined,
  },
)

const emit = defineEmits<{
  copied: [text: string]
}>()

const model = defineModel<string | number | null>({ default: '' })

const { t: tPlaceholder } = usePlaceholderTranslation()

const effectivePlaceholder = computed(() => {
  if (props.placeholder !== undefined) {
    return props.placeholder
  }
  return props.readonly ? tPlaceholder('notUpdated') : tPlaceholder('enterData')
})

const isCopied = ref(false)
let copyTimer: ReturnType<typeof setTimeout> | null = null

const resolvedCopyText = computed(() => {
  if (props.copyText !== undefined) {
    return props.copyText
  }
  if (model.value === null || model.value === undefined) {
    return ''
  }
  const str = String(model.value).trim()
  return str === '—' ? '' : str
})

const isCopyDisabled = computed(() => {
  return props.disabled || !resolvedCopyText.value
})

async function copyToClipboard(): Promise<void> {
  if (isCopyDisabled.value) return

  const textToCopy = resolvedCopyText.value
  let success = false

  try {
    if (navigator?.clipboard?.writeText) {
      await navigator.clipboard.writeText(textToCopy)
      success = true
    } else {
      throw new Error('Clipboard API unavailable')
    }
  } catch {
    // Fallback qua textarea ẩn
    try {
      const textarea = document.createElement('textarea')
      textarea.value = textToCopy
      textarea.style.position = 'fixed'
      textarea.style.left = '-9999px'
      textarea.style.top = '-9999px'
      textarea.setAttribute('readonly', '')
      document.body.appendChild(textarea)
      textarea.select()
      success = Boolean(document.execCommand('copy'))
      document.body.removeChild(textarea)
    } catch {
      // success remains false
    }
  }

  if (success) {
    isCopied.value = true
    emit('copied', textToCopy)

    if (copyTimer) clearTimeout(copyTimer)
    copyTimer = setTimeout(() => {
      isCopied.value = false
      copyTimer = null
    }, 2000)
  }
}

onBeforeUnmount(() => {
  if (copyTimer) {
    clearTimeout(copyTimer)
    copyTimer = null
  }
})
</script>

<template>
  <div
    class="copyable-input"
    :class="[
      `copyable-input--${size}`,
      {
        'copyable-input--no-copy': !copyable,
        'copyable-input--readonly': readonly,
        'copyable-input--disabled': disabled,
      },
    ]"
  >
    <input
      v-bind="$attrs"
      v-model="model"
      :placeholder="effectivePlaceholder"
      :readonly="readonly"
      :disabled="disabled"
      class="copyable-input__control"
      :class="[
        `copyable-input__control--${size}`,
        {
          'copyable-input__control--mono': mono,
          'copyable-input__control--standalone': !copyable,
        },
      ]"
    />
    <button
      v-if="copyable"
      type="button"
      class="copyable-input__btn"
      :class="[
        `copyable-input__btn--${size}`,
        {
          'copyable-input__btn--copied': isCopied,
          'copyable-input__btn--disabled': isCopyDisabled,
        },
      ]"
      :disabled="isCopyDisabled"
      :aria-label="isCopied ? copiedLabel : copyLabel"
      @click="copyToClipboard"
    >
      <!-- Icon Checkmark khi Copied -->
      <svg
        v-if="isCopied"
        class="copyable-input__icon copyable-input__icon--success"
        viewBox="0 0 20 20"
        fill="currentColor"
        aria-hidden="true"
      >
        <path
          fill-rule="evenodd"
          d="M16.707 5.293a1 1 0 010 1.414l-8 8a1 1 0 01-1.414 0l-4-4a1 1 0 011.414-1.414L8 12.586l7.293-7.293a1 1 0 011.414 0z"
          clip-rule="evenodd"
        />
      </svg>
      <!-- Icon Clipboard mặc định -->
      <svg
        v-else
        class="copyable-input__icon"
        viewBox="0 0 20 20"
        fill="currentColor"
        aria-hidden="true"
      >
        <path
          fill-rule="evenodd"
          d="M13.887 3.182c.396.037.79.08 1.183.128C16.194 3.45 17 4.414 17 5.517V16.75A2.25 2.25 0 0 1 14.75 19h-9.5A2.25 2.25 0 0 1 3 16.75V5.517c0-1.103.806-2.068 1.93-2.207.393-.048.787-.09 1.183-.128A3.001 3.001 0 0 1 9 1h2c1.373 0 2.531.923 2.887 2.182ZM7.5 4A1.5 1.5 0 0 1 9 2.5h2A1.5 1.5 0 0 1 12.5 4v.5h-5V4Z"
          clip-rule="evenodd"
        />
      </svg>
      <span class="copyable-input__label">{{ isCopied ? copiedLabel : copyLabel }}</span>
    </button>
  </div>
</template>

<style scoped>
/*
 * Khung input liền khối (Unified Input Group):
 * Toàn bộ viền 4 cạnh, bo góc và hiệu ứng focus được quản lý ở cấp container .copyable-input,
 * đảm bảo cả ô input lẫn nút copy luôn được bao bọc đồng bộ, không bị vỡ viền hay rách góc.
 */
.copyable-input {
  display: flex;
  position: relative;
  width: 100%;
  border: 1px solid var(--border-control, #d1d5db);
  border-radius: 0.375rem;
  background-color: var(--surface-panel);
  box-shadow: var(--shadow-control);
  transition: border-color 150ms ease, box-shadow 150ms ease, background-color 150ms ease;
  overflow: hidden;
}

.copyable-input:focus-within {
  border-color: var(--input-border-focus, #3b82f6);
  box-shadow: var(--input-focus-ring, 0 0 0 2px rgba(59, 130, 246, 0.18));
}

.copyable-input--readonly {
  background-color: var(--surface-control-disabled, #f9fafb);
}

.copyable-input--disabled {
  background-color: var(--surface-control-disabled, #f9fafb);
  opacity: 0.7;
  cursor: not-allowed;
}

.copyable-input[aria-invalid='true'] {
  border-color: var(--status-danger);
}

.copyable-input__control {
  flex: 1;
  min-width: 0;
  width: 100%;
  border: none;
  outline: none;
  background-color: transparent;
  color: var(--text-primary);
  font-family: inherit;
  line-height: 1.43;
}

/* Triệt tiêu 100% outline mặc định của browser trên cả :focus và :focus-visible */
.copyable-input__control:focus,
.copyable-input__control:focus-visible {
  outline: none;
  border: none;
  box-shadow: none;
}

.copyable-input__control--standalone {
  width: 100%;
}

.copyable-input__control--sm {
  padding: 0.35rem 0.625rem;
  font-size: 0.8125rem;
}

.copyable-input__control--md {
  padding: 0.5rem 0.75rem;
  font-size: 0.875rem;
}

.copyable-input__control--mono {
  font-family: var(--font-mono, monospace);
  font-size: 0.8125rem;
}

.copyable-input__control:disabled,
.copyable-input__control:read-only {
  color: var(--text-primary);
  cursor: default;
  background-color: transparent;
}

.copyable-input__control:disabled {
  color: var(--text-secondary);
  cursor: not-allowed;
}

/* Nút copy gắn liền vào cạnh phải */
.copyable-input__btn {
  flex-shrink: 0;
  display: inline-flex;
  align-items: center;
  gap: 0.375rem;
  margin: 0;
  border: none;
  border-left: 1px solid var(--border-control, #d1d5db);
  border-radius: 0;
  background-color: var(--surface-table-header, #f9fafb);
  color: var(--text-secondary);
  cursor: pointer;
  user-select: none;
  font-family: inherit;
  font-weight: 500;
  transition: all 120ms ease;
  white-space: nowrap;
  outline: none;
}

.copyable-input__btn:focus,
.copyable-input__btn:focus-visible {
  outline: none;
}

.copyable-input:focus-within .copyable-input__btn {
  border-left-color: var(--input-border-focus, #3b82f6);
}

.copyable-input__btn--sm {
  padding: 0.35rem 0.625rem;
  font-size: 0.75rem;
}

.copyable-input__btn--md {
  padding: 0.5rem 0.875rem;
  font-size: 0.875rem;
}

.copyable-input__btn:hover:not(:disabled) {
  background-color: var(--surface-muted, #f3f4f6);
  color: var(--text-primary);
}

.copyable-input__btn--copied {
  background-color: var(--surface-success-subtle, #ecfdf5);
  border-left-color: var(--status-success, #16a34a);
  color: var(--status-success, #16a34a);
}

.copyable-input__btn--disabled,
.copyable-input__btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.copyable-input__icon {
  width: 0.875rem;
  height: 0.875rem;
  flex-shrink: 0;
}

.copyable-input__icon--success {
  color: var(--status-success, #16a34a);
}

@media (prefers-reduced-motion: reduce) {
  .copyable-input,
  .copyable-input__control,
  .copyable-input__btn {
    transition: none;
  }
}
</style>
