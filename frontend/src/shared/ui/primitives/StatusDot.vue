<script setup lang="ts">
import { computed } from 'vue'

export type StatusType = 'online' | 'offline' | 'degraded' | 'warning' | 'error' | 'syncing' | 'idle'

const props = withDefaults(
  defineProps<{
    status?: StatusType | string
    size?: 'sm' | 'md' | 'lg'
    pulse?: boolean
    title?: string
  }>(),
  {
    status: 'idle',
    size: 'md',
    pulse: false,
    title: '',
  }
)

const colorClasses = computed(() => {
  const s = props.status?.toLowerCase()
  switch (s) {
    case 'online':
    case 'connected':
    case 'active':
    case 'healthy':
      return {
        dot: 'bg-emerald-500',
        pulse: 'bg-emerald-400',
      }
    case 'warning':
    case 'degraded':
      return {
        dot: 'bg-amber-500',
        pulse: 'bg-amber-400',
      }
    case 'error':
    case 'danger':
    case 'critical':
      return {
        dot: 'bg-rose-500',
        pulse: 'bg-rose-400',
      }
    case 'syncing':
      return {
        dot: 'bg-sky-500',
        pulse: 'bg-sky-400',
      }
    case 'offline':
    case 'disconnected':
    case 'disabled':
      return {
        dot: 'bg-zinc-400 dark:bg-zinc-600',
        pulse: 'bg-zinc-300 dark:bg-zinc-500',
      }
    default:
      return {
        dot: 'bg-zinc-400 dark:bg-zinc-500',
        pulse: 'bg-zinc-300 dark:bg-zinc-400',
      }
  }
})

const sizeClasses = computed(() => {
  switch (props.size) {
    case 'sm':
      return 'w-2 h-2'
    case 'lg':
      return 'w-3.5 h-3.5'
    default:
      return 'w-2.5 h-2.5'
  }
})
</script>

<template>
  <span
    class="relative inline-flex items-center justify-center flex-shrink-0"
    :title="title || status"
    data-test="status-dot"
  >
    <span
      v-if="pulse"
      class="animate-ping absolute inline-flex h-full w-full rounded-full opacity-75"
      :class="[sizeClasses, colorClasses.pulse]"
    />
    <span
      class="relative inline-flex rounded-full"
      :class="[sizeClasses, colorClasses.dot]"
    />
  </span>
</template>
