<script setup lang="ts">
import { computed } from 'vue'

export type ButtonVariant = 'primary' | 'secondary' | 'outline' | 'ghost' | 'danger' | 'danger-outline'
export type ButtonSize = 'xs' | 'sm' | 'md' | 'lg'

const props = withDefaults(
  defineProps<{
    variant?: ButtonVariant
    size?: ButtonSize
    loading?: boolean
    disabled?: boolean
    type?: 'button' | 'submit' | 'reset'
  }>(),
  {
    variant: 'secondary',
    size: 'sm',
    loading: false,
    disabled: false,
    type: 'button',
  }
)

const isDisabled = computed(() => props.disabled || props.loading)

const variantClasses = computed(() => {
  switch (props.variant) {
    case 'primary':
      return 'bg-orange-600 hover:bg-orange-500 active:bg-orange-700 text-white font-medium shadow-xs border border-orange-600'
    case 'danger':
      return 'bg-rose-600 hover:bg-rose-500 active:bg-rose-700 text-white font-medium shadow-xs border border-rose-500/50'
    case 'danger-outline':
      return 'bg-transparent hover:bg-rose-500/10 text-rose-600 dark:text-rose-400 border border-rose-300 dark:border-rose-800'
    case 'outline':
      return 'bg-transparent hover:bg-zinc-100 dark:hover:bg-zinc-800 text-zinc-700 dark:text-zinc-200 border border-zinc-300 dark:border-zinc-700'
    case 'ghost':
      return 'bg-transparent hover:bg-zinc-100 dark:hover:bg-zinc-800 text-zinc-600 hover:text-zinc-900 dark:text-zinc-400 dark:hover:text-zinc-100 border border-transparent'
    case 'secondary':
    default:
      return 'bg-zinc-100 hover:bg-zinc-200 active:bg-zinc-300 dark:bg-zinc-800 dark:hover:bg-zinc-700 dark:active:bg-zinc-600 text-zinc-800 dark:text-zinc-100 border border-zinc-200 dark:border-zinc-700/80'
  }
})

const sizeClasses = computed(() => {
  switch (props.size) {
    case 'xs':
      return 'h-7 px-2 text-xs gap-1.5 rounded'
    case 'md':
      return 'h-10 px-4 text-sm gap-2 rounded-lg'
    case 'lg':
      return 'h-11 px-5 text-base gap-2.5 rounded-lg'
    case 'sm':
    default:
      return 'h-8.5 px-3 text-xs font-medium gap-2 rounded-md'
  }
})
</script>

<template>
  <button
    :type="type"
    :disabled="isDisabled"
    class="inline-flex items-center justify-center font-medium transition-all select-none cursor-pointer focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-amber-500/40 disabled:opacity-50 disabled:cursor-not-allowed disabled:pointer-events-none"
    :class="[variantClasses, sizeClasses]"
    :data-test="($attrs['data-test'] as string | undefined) ?? 'button'"
  >
    <!-- Spinner for loading state -->
    <svg
      v-if="loading"
      class="animate-spin -ml-0.5 h-3.5 w-3.5 text-current"
      xmlns="http://www.w3.org/2000/svg"
      fill="none"
      viewBox="0 0 24 24"
    >
      <circle
        class="opacity-25"
        cx="12"
        cy="12"
        r="10"
        stroke="currentColor"
        stroke-width="4"
      />
      <path
        class="opacity-75"
        fill="currentColor"
        d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"
      />
    </svg>

    <slot name="icon-left" />
    <slot />
    <slot name="icon-right" />
  </button>
</template>
