<script setup lang="ts">
import { computed } from 'vue'
import type { ReadState } from '@/core/commands/useDeviceReads'
import { formatReadTime } from '@/core/time/readAge'
import ReadFieldList, { type FieldSpec } from './ReadFieldList.vue'

/** One read inside a card: its own status line, then its data (or the slot). */
const props = withDefaults(defineProps<{
  title: string
  stepKey: string
  state?: ReadState
  stale: boolean
  now: number
  fields?: FieldSpec[]
  /** Status line only; the card renders the data itself. */
  bodyless?: boolean
}>(), { state: undefined, fields: () => [], bodyless: false })

const noHardware = computed(() => {
  const data = props.state?.data
  return props.state?.kind === 'ok' && data !== null && typeof data === 'object' &&
    (data as Record<string, unknown>).available === false
})
</script>

<template>
  <section class="space-y-1" :data-test="`section-${stepKey}`">
    <div class="flex items-baseline justify-between gap-2">
      <h4 class="text-[11px] font-semibold text-[var(--text-heading)]">{{ title }}</h4>
      <span class="text-[10px] text-[var(--text-hint)] text-right">
        <template v-if="!state || state.kind === 'idle'">Chưa đọc</template>
        <template v-else-if="state.kind === 'reading'">Đang đọc…</template>
        <template v-else-if="state.readAt">
          {{ formatReadTime(state.readAt, now) }}<template v-if="stale"> · có thể đã cũ</template>
        </template>
      </span>
    </div>
    <p v-if="state?.kind === 'error'" class="text-[11px] text-rose-500 break-words">
      Không đọc được: <span class="font-mono">{{ state.code }}</span><template v-if="state.detail"> ({{ state.detail }})</template>
    </p>
    <p v-else-if="state?.kind === 'no_data'" class="text-[11px] text-amber-600 dark:text-amber-400">
      Thiết bị không trả dữ liệu, hãy đọc lại.
    </p>
    <p v-else-if="noHardware" class="text-[11px] text-[var(--text-hint)]">Không có phần cứng.</p>
    <template v-else-if="state?.kind === 'ok' && !bodyless">
      <slot :data="state.data">
        <ReadFieldList :data="state.data" :fields="fields" />
      </slot>
    </template>
  </section>
</template>
