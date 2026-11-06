<script setup lang="ts">
import { computed, onUnmounted, ref, watch } from 'vue'
import type { CommandCatalogSnapshot, DeviceLinkDto } from '@/core/api/adminApi'
import { stepKey, useDeviceReads, type ReadState, type ReadStep } from '@/core/commands/useDeviceReads'
import Button from '@/shared/ui/primitives/Button.vue'
import { AlertTriangle, RefreshCw, Square } from '@lucide/vue'

/**
 * Frame of a read card: reads on demand only, one command at a time, cancellable, and only the
 * commands the caller may send on this device. The slot renders the data of each read.
 */
const props = defineProps<{
  title: string
  steps: ReadStep[]
  deviceId: string
  csrfToken: string | null
  catalog: CommandCatalogSnapshot | null
  catalogVerified: boolean
  /** From GET allowed-commands; null until loaded (or when it failed): nothing is sent. */
  allowedCommands: string[] | null
  link: DeviceLinkDto | null
  /** Set by the page when someone else changed this card's configuration; older reads are stale. */
  configChangedAt?: number | null
}>()

const reads = useDeviceReads(
  () => props.deviceId,
  () => ({ csrfToken: props.csrfToken, catalog: props.catalog }))

const allowedSteps = computed(() =>
  props.allowedCommands ? props.steps.filter(s => props.allowedCommands!.includes(stepKey(s))) : [])
const missing = computed(() =>
  props.allowedCommands ? props.steps.filter(s => !props.allowedCommands!.includes(stepKey(s))).map(stepKey) : [])
const canRead = computed(() =>
  props.catalogVerified && props.catalog?.trusted === true && allowedSteps.value.length > 0)

// While a config write runs, the card's own read buttons are locked: the write owns the device lane.
const locked = ref(false)
function lockReads(on: boolean) {
  locked.value = on
}

/** Reads again after a write: stops a read in progress first. Only steps the caller may send. */
async function refresh(list: ReadStep[]): Promise<Record<string, ReadState>> {
  const allowed = new Set(allowedSteps.value.map(stepKey))
  const wanted = canRead.value ? list.filter(step => allowed.has(stepKey(step))) : []
  return wanted.length ? reads.refresh(wanted) : {}
}

const configChanged = computed(() => {
  const changedAt = props.configChangedAt
  return changedAt != null && allowedSteps.value.some(step => !(Date.parse(reads.states[stepKey(step)]?.readAt ?? '') >= changedAt))
})

// A device that stopped reporting, or a server without its broker link, may have changed since.
const offline = computed(() => props.link?.online === false || props.link?.brokerConnected === false)
const catalogStale = computed(() => Object.values(reads.states).some(s => s.code === 'CATALOG_STALE'))

const now = ref(Date.now())
const clock = setInterval(() => { now.value = Date.now() }, 15_000)
onUnmounted(() => clearInterval(clock))

watch(() => props.deviceId, () => {
  reads.reset()
  locked.value = false
})

function start() {
  void reads.read(allowedSteps.value)
}
</script>

<template>
  <div class="p-3.5 rounded-lg bg-[var(--surface-overlay)] border border-[var(--border-subtle)] space-y-2.5" data-test="read-card">
    <div class="flex flex-wrap items-center justify-between gap-2 border-b border-[var(--border-subtle)] pb-1.5">
      <h3 class="text-xs font-semibold text-[var(--text-heading)]">{{ title }}</h3>
      <span v-if="canRead" class="flex items-center gap-1.5">
        <Button
          v-if="reads.reading.value"
          variant="ghost"
          size="xs"
          :disabled="locked"
          :aria-label="`Dừng đọc ${title}`"
          data-test="read-stop"
          @click="reads.cancel()"
        >
          <Square class="w-3 h-3" />
          <span class="ml-1">Dừng</span>
        </Button>
        <Button
          variant="secondary"
          size="xs"
          :disabled="reads.reading.value || locked"
          :loading="reads.reading.value"
          :aria-label="`Đọc ${title} từ thiết bị`"
          data-test="read-start"
          @click="start"
        >
          <RefreshCw class="w-3 h-3" />
          <span class="ml-1">Đọc</span>
        </Button>
      </span>
    </div>

    <p v-if="offline" class="flex items-center gap-1 text-[11px] text-amber-600 dark:text-amber-400" data-test="read-offline">
      <AlertTriangle class="w-3 h-3" />
      {{ link?.brokerConnected === false ? 'Server mất kết nối broker' : 'Thiết bị đang offline' }}: dữ liệu có thể đã cũ, lệnh đọc có thể hết giờ.
    </p>
    <p v-if="configChanged" class="flex items-center gap-1 text-[11px] text-amber-600 dark:text-amber-400" data-test="read-config-changed">
      <AlertTriangle class="w-3 h-3" />
      Cấu hình vừa đổi, hãy đọc lại.
    </p>
    <p v-if="catalogStale" class="text-[11px] text-rose-500" data-test="read-catalog-stale">
      Catalog lệnh trên server đã đổi, hãy tải lại trang.
    </p>
    <ul v-if="missing.length" class="text-[10px] text-[var(--text-hint)]">
      <li v-for="key in missing" :key="key" :data-test="`read-missing-${key}`">
        Thiếu quyền: <span class="font-mono">{{ key }}</span>
      </li>
    </ul>

    <div aria-live="polite">
      <slot
        :states="reads.states"
        :stale="offline"
        :now="now"
        :refresh="refresh"
        :lockReads="lockReads"
        :configChangedAt="configChangedAt ?? null"
      />
    </div>
  </div>
</template>
