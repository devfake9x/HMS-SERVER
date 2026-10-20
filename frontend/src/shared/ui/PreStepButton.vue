<script setup lang="ts">
import { computed, onMounted, onUnmounted } from 'vue'
import { iconPath } from './icons'

export interface PreStepButtonProps {
  /** Nhãn hiển thị chính của nút (mặc định: 'Bước trước'). */
  label?: string
  /** Biến thể giao diện: secondary | ghost | primary | outline. */
  variant?: 'secondary' | 'ghost' | 'primary' | 'outline'
  /** Kích thước: sm | md. */
  size?: 'sm' | 'md'
  /** Trạng thái vô hiệu hoá do bên ngoài truyền vào. */
  disabled?: boolean
  /** Vị trí bước hiện tại (1-indexed). Nếu stepIndex <= 1 sẽ tự động disabled. */
  stepIndex?: number
  /** Tổng số bước trong quy trình. */
  totalSteps?: number
  /** Tên bước đích khi lùi lại (ví dụ: 'Thông tin chi tiết'). */
  targetStepName?: string
  /** Gợi ý hiển thị tooltip khi nút bị disabled. */
  disabledHint?: string
  /** Icon hiển thị phía trước (mặc định: 'chevron-left'). */
  icon?: 'chevron-left' | 'arrow-left'
  /** Cho phép phím tắt Alt + ArrowLeft để lùi bước. */
  enableShortcut?: boolean
  /** Thuộc tính aria-label phục vụ trợ năng. */
  ariaLabel?: string
}

const props = withDefaults(defineProps<PreStepButtonProps>(), {
  label: 'Bước trước',
  variant: 'secondary',
  size: 'md',
  disabled: false,
  stepIndex: undefined,
  totalSteps: undefined,
  targetStepName: '',
  disabledHint: 'Đang ở bước đầu tiên',
  icon: 'chevron-left',
  enableShortcut: true,
  ariaLabel: 'Lùi lại bước trước',
})

const emit = defineEmits<{
  click: [event: MouseEvent]
}>()

const isAtFirstStep = computed(() => {
  return typeof props.stepIndex === 'number' && props.stepIndex <= 1
})

const isDisabled = computed(() => {
  return props.disabled || isAtFirstStep.value
})

const titleHint = computed(() => {
  if (isDisabled.value) {
    return props.disabledHint
  }
  if (props.targetStepName) {
    return `Lùi về: ${props.targetStepName}`
  }
  return props.label
})

function handleClick(event: MouseEvent): void {
  if (isDisabled.value) {
    event.preventDefault()
    return
  }
  emit('click', event)
}

function handleKeydown(event: KeyboardEvent): void {
  if (!props.enableShortcut || isDisabled.value) return

  // Phím tắt Alt + ArrowLeft
  if (event.altKey && event.key === 'ArrowLeft') {
    const activeEl = document.activeElement
    const isInput =
      activeEl instanceof HTMLInputElement ||
      activeEl instanceof HTMLTextAreaElement ||
      activeEl instanceof HTMLSelectElement

    if (!isInput) {
      event.preventDefault()
      emit('click', new MouseEvent('click'))
    }
  }
}

onMounted(() => {
  if (props.enableShortcut && typeof window !== 'undefined') {
    window.addEventListener('keydown', handleKeydown)
  }
})

onUnmounted(() => {
  if (typeof window !== 'undefined') {
    window.removeEventListener('keydown', handleKeydown)
  }
})
</script>

<template>
  <button
    type="button"
    class="pre-btn"
    :class="[
      `pre-btn--${variant}`,
      `pre-btn--${size}`,
      { 'pre-btn--disabled': isDisabled },
    ]"
    :disabled="isDisabled"
    :aria-disabled="isDisabled ? 'true' : undefined"
    :aria-label="ariaLabel"
    :title="titleHint"
    @click="handleClick"
  >
    <svg
      v-if="iconPath(icon)"
      class="pre-btn__icon"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      stroke-width="2"
      stroke-linecap="round"
      stroke-linejoin="round"
      aria-hidden="true"
    >
      <path :d="iconPath(icon) ?? ''" />
    </svg>

    <span class="pre-btn__text">
      <slot>
        {{ label }}
        <span v-if="targetStepName" class="pre-btn__target-name">
          : {{ targetStepName }}
        </span>
      </slot>
    </span>
  </button>
</template>

<style scoped>
.pre-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 0.4rem;
  border-radius: 0.375rem;
  font-family: inherit;
  font-weight: 500;
  line-height: 1.43;
  box-shadow: var(--shadow-control);
  cursor: pointer;
  transition: background-color 120ms ease, border-color 120ms ease, color 120ms ease;
  white-space: nowrap;
}

.pre-btn--md {
  padding: 0.5rem 0.875rem;
  font-size: 0.875rem;
  min-height: 2.375rem;
}

.pre-btn--sm {
  padding: 0.35rem 0.625rem;
  font-size: 0.8125rem;
  min-height: 2rem;
}

.pre-btn__icon {
  width: 1.125rem;
  height: 1.125rem;
  flex-shrink: 0;
}

.pre-btn--sm .pre-btn__icon {
  width: 1rem;
  height: 1rem;
}

.pre-btn__text {
  display: inline-flex;
  align-items: center;
}

.pre-btn__target-name {
  color: var(--text-secondary);
  margin-left: 0.25rem;
  font-weight: 400;
}

/* Biến thể màu sắc */
.pre-btn--secondary {
  background: var(--surface-panel);
  border: 1px solid var(--border-subtle);
  color: var(--text-primary);
}

.pre-btn--secondary:hover:not(:disabled) {
  background: var(--surface-overlay);
  border-color: var(--border-control);
}

.pre-btn--ghost {
  background: transparent;
  border: 1px solid transparent;
  color: var(--text-secondary);
  box-shadow: none;
}

.pre-btn--ghost:hover:not(:disabled) {
  background: var(--surface-overlay);
  color: var(--text-primary);
}

.pre-btn--outline {
  background: transparent;
  border: 1px solid var(--border-subtle);
  color: var(--text-primary);
}

.pre-btn--outline:hover:not(:disabled) {
  background: var(--surface-overlay);
}

.pre-btn--primary {
  background: var(--color-primary, #2563eb);
  border: 1px solid transparent;
  color: #ffffff;
}

.pre-btn--primary:hover:not(:disabled) {
  filter: brightness(1.08);
}

/* Trạng thái disabled */
.pre-btn:disabled,
.pre-btn--disabled {
  opacity: 0.55;
  cursor: not-allowed;
  pointer-events: none;
  box-shadow: none;
}
</style>
