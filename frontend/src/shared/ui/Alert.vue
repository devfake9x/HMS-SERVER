<script setup lang="ts">
import { computed } from 'vue'

import { iconPath } from './icons'

export type AlertVariant = 'warning' | 'danger' | 'info' | 'success'

const props = withDefaults(
  defineProps<{
    variant?: AlertVariant
    title?: string
    closable?: boolean
    icon?: string
  }>(),
  {
    variant: 'warning',
    title: '',
    closable: false,
    icon: '',
  },
)

const emit = defineEmits<{
  close: []
}>()

const resolvedIconName = computed(() => {
  if (props.icon) return props.icon
  switch (props.variant) {
    case 'danger':
      return 'x-circle'
    case 'info':
      return 'info'
    case 'success':
      return 'check-circle'
    case 'warning':
    default:
      return 'alert-triangle'
  }
})

const resolvedSvgPath = computed(() => iconPath(resolvedIconName.value))

const alertRole = computed(() => {
  return props.variant === 'danger' || props.variant === 'warning' ? 'alert' : 'status'
})

const ariaLive = computed(() => {
  return props.variant === 'danger' || props.variant === 'warning' ? 'assertive' : 'polite'
})

function handleClose() {
  emit('close')
}
</script>

<template>
  <div
    class="alert"
    :class="`alert--${variant}`"
    :role="alertRole"
    :aria-live="ariaLive"
  >
    <div v-if="resolvedSvgPath" class="alert__icon-wrap" aria-hidden="true">
      <svg
        class="alert__icon"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        stroke-width="2"
        stroke-linecap="round"
        stroke-linejoin="round"
      >
        <path :d="resolvedSvgPath" />
      </svg>
    </div>

    <div class="alert__content">
      <div v-if="title || $slots.title" class="alert__title">
        <slot name="title">
          {{ title }}
        </slot>
      </div>
      <div class="alert__message">
        <slot />
      </div>
    </div>

    <button
      v-if="closable"
      type="button"
      class="alert__close-btn"
      aria-label="Đóng cảnh báo"
      @click="handleClose"
    >
      <svg
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        stroke-width="2"
        stroke-linecap="round"
        stroke-linejoin="round"
        width="16"
        height="16"
      >
        <path d="M18 6 6 18M6 6l12 12" />
      </svg>
    </button>
  </div>
</template>

<style scoped>
.alert {
  display: flex;
  align-items: flex-start;
  gap: 0.75rem;
  padding: 0.75rem 1rem;
  border-radius: 0.375rem;
  border: 1px solid;
  font-size: 0.875rem;
  line-height: 1.5;
  transition: background-color 150ms ease, border-color 150ms ease;
}

.alert__icon-wrap {
  flex-shrink: 0;
  display: flex;
  align-items: center;
  margin-top: 0.125rem;
}

.alert__icon {
  width: 1.125rem;
  height: 1.125rem;
}

.alert__content {
  flex: 1;
  min-width: 0;
}

.alert__title {
  font-weight: 600;
  margin-bottom: 0.25rem;
}

.alert__message {
  color: inherit;
  word-break: break-word;
}

.alert__close-btn {
  flex-shrink: 0;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  background: transparent;
  border: none;
  padding: 0.2rem;
  border-radius: 0.25rem;
  cursor: pointer;
  color: inherit;
  opacity: 0.7;
  transition: opacity 120ms ease, background-color 120ms ease;
}

.alert__close-btn:hover {
  opacity: 1;
  background-color: rgba(0, 0, 0, 0.06);
}

/* Variant: warning */
.alert--warning {
  background-color: rgba(245, 158, 11, 0.1);
  border-color: rgba(245, 158, 11, 0.3);
  color: #92400e;
}

.alert--warning .alert__icon {
  color: #d97706;
}

/* Variant: danger / error */
.alert--danger {
  background-color: rgba(239, 68, 68, 0.1);
  border-color: rgba(239, 68, 68, 0.3);
  color: #991b1b;
}

.alert--danger .alert__icon {
  color: #dc2626;
}

/* Variant: info */
.alert--info {
  background-color: rgba(14, 165, 233, 0.1);
  border-color: rgba(14, 165, 233, 0.3);
  color: #075985;
}

.alert--info .alert__icon {
  color: #0284c7;
}

/* Variant: success */
.alert--success {
  background-color: rgba(16, 185, 129, 0.1);
  border-color: rgba(16, 185, 129, 0.3);
  color: #065f46;
}

.alert--success .alert__icon {
  color: #059669;
}

/* Dark theme overrides */
:global(html[data-theme='dark']) .alert--warning {
  background-color: rgba(251, 191, 36, 0.12);
  border-color: rgba(251, 191, 36, 0.3);
  color: #fde68a;
}

:global(html[data-theme='dark']) .alert--warning .alert__icon {
  color: #fbbf24;
}

:global(html[data-theme='dark']) .alert--danger {
  background-color: rgba(248, 113, 113, 0.12);
  border-color: rgba(248, 113, 113, 0.3);
  color: #fca5a5;
}

:global(html[data-theme='dark']) .alert--danger .alert__icon {
  color: #f87171;
}

:global(html[data-theme='dark']) .alert--info {
  background-color: rgba(56, 189, 248, 0.12);
  border-color: rgba(56, 189, 248, 0.3);
  color: #bae6fd;
}

:global(html[data-theme='dark']) .alert--info .alert__icon {
  color: #38bdf8;
}

:global(html[data-theme='dark']) .alert--success {
  background-color: rgba(52, 211, 153, 0.12);
  border-color: rgba(52, 211, 153, 0.3);
  color: #a7f3d0;
}

:global(html[data-theme='dark']) .alert--success .alert__icon {
  color: #34d399;
}
</style>
