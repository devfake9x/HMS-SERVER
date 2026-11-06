<script setup lang="ts">
import { AlertTriangle } from '@lucide/vue'
import type { WriteResult } from './useConfigWrite'

/** What a config write ended in, taken from the re-read. Announced to screen readers. */
defineProps<{ result: WriteResult | null; clock: string | null }>()

const TONE_CLASS = {
  ok: 'text-emerald-600 dark:text-emerald-400',
  warn: 'text-amber-600 dark:text-amber-400',
  error: 'text-rose-500',
} as const
</script>

<template>
  <div aria-live="polite" class="space-y-0.5" data-test="config-result" :data-tone="result?.tone ?? ''">
    <template v-if="result">
      <p
        v-for="(line, index) in result.lines"
        :key="index"
        class="text-[11px] break-words"
        :class="index === 0 ? TONE_CLASS[result.tone] : 'text-[var(--text-secondary)]'"
      >{{ line }}</p>
    </template>
    <p
      v-if="clock"
      class="flex items-center gap-1 text-[11px] text-amber-600 dark:text-amber-400"
      title="So với giờ của trình duyệt này; nếu máy bạn lệch giờ thì cảnh báo có thể sai."
      data-test="clock-warning"
    >
      <AlertTriangle class="w-3 h-3" />
      {{ clock }}
    </p>
  </div>
</template>
