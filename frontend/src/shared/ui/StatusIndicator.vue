<script setup lang="ts">
import { computed, useSlots } from 'vue'

import { translateStatus } from '#/shared/i18n/status'
import StatusDot, { type DotStatus } from './StatusDot.vue'

export type IndicatorTone = DotStatus | 'active' | 'suspended'

const props = withDefaults(
  defineProps<{
    status?: IndicatorTone | string
    label?: string
    autoLabel?: boolean
  }>(),
  {
    status: 'online',
    label: '',
    autoLabel: false,
  },
)

const slots = useSlots()

const resolvedDotStatus = computed<DotStatus>(() => {
  if (props.status === 'active' || props.status === 'online') {
    return 'online'
  }
  if (props.status === 'suspended' || props.status === 'offline' || props.status === 'inactive') {
    return 'offline'
  }
  if (props.status === 'warning' || props.status === 'degraded') {
    return 'warning'
  }
  return 'unknown'
})

const displayLabel = computed(() => {
  if (props.label) return props.label
  if (props.autoLabel && !slots.default) {
    if (props.status === 'suspended') return 'Tạm dừng'
    return translateStatus(props.status)
  }
  return ''
})
</script>

<template>
  <div class="status-indicator">
    <StatusDot :status="resolvedDotStatus" />
    <span class="status-indicator__label">
      <slot>{{ displayLabel }}</slot>
    </span>
  </div>
</template>

<style scoped>
.status-indicator {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 0.8125rem;
  color: var(--text-secondary);
  font-weight: 500;
  line-height: 1.25rem;
}

.status-indicator__label {
  white-space: nowrap;
}
</style>
