<script setup lang="ts">
import { computed } from 'vue'

export type BadgeVariant = 'default' | 'success' | 'warning' | 'danger' | 'info' | 'neutral' | 'outline'
export type BadgeSize = 'xs' | 'sm' | 'md'

const props = withDefaults(
  defineProps<{
    variant?: BadgeVariant
    size?: BadgeSize
    rounded?: boolean
  }>(),
  {
    variant: 'default',
    size: 'sm',
    rounded: false,
  }
)

const variantClasses = computed(() => {
  switch (props.variant) {
    case 'success':
      return 'bg-emerald-500/10 text-emerald-700 dark:text-emerald-400 border border-emerald-500/20'
    case 'warning':
      return 'bg-amber-500/10 text-amber-700 dark:text-amber-400 border border-amber-500/20'
    case 'danger':
      return 'bg-rose-500/10 text-rose-700 dark:text-rose-400 border border-rose-500/20'
    case 'info':
      return 'bg-sky-500/10 text-sky-700 dark:text-sky-400 border border-sky-500/20'
    case 'neutral':
      return 'bg-zinc-100 text-zinc-700 dark:bg-zinc-800 dark:text-zinc-300 border border-zinc-200 dark:border-zinc-700'
    case 'outline':
      return 'bg-transparent text-zinc-700 dark:text-zinc-300 border border-zinc-300 dark:border-zinc-700'
    case 'default':
    default:
      return 'bg-zinc-100 text-zinc-800 dark:bg-zinc-800 dark:text-zinc-200 border border-zinc-200/80 dark:border-zinc-700'
  }
})

const sizeClasses = computed(() => {
  switch (props.size) {
    case 'xs':
      return 'px-1.5 py-0.5 text-[10px] font-medium leading-none'
    case 'md':
      return 'px-2.5 py-1 text-xs font-medium'
    case 'sm':
    default:
      return 'px-2 py-0.5 text-[11px] font-medium'
  }
})
</script>

<template>
  <span
    class="inline-flex items-center gap-1.5 font-medium transition-colors"
    :class="[
      variantClasses,
      sizeClasses,
      rounded ? 'rounded-full' : 'rounded-md',
    ]"
    data-test="badge"
  >
    <slot name="prefix" />
    <slot />
    <slot name="suffix" />
  </span>
</template>
