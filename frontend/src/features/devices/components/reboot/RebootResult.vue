<script setup lang="ts">
import { computed } from 'vue'
import { AlertTriangle } from '@lucide/vue'
import type { RebootOutcome } from '@/core/commands/useDeviceReboot'
import type { RelayChannel } from '@/core/commands/rebootPlan'

const tag = (ch: RelayChannel) =>
  ch.forced ? ' (force)' : ch.owner === 'level_rule' ? (ch.ownerId ? ` (luật #${ch.ownerId})` : ' (luật)') : ''

/** What a finished reboot run read back from the device: signed uptime, firmware, clock, relay table. */
const props = defineProps<{ outcome: RebootOutcome }>()

const uptime = computed(() => {
  const after = props.outcome.info?.uptimeSeconds
  if (typeof after !== 'number') return null
  const before = props.outcome.uptimeBefore
  return `Uptime: ${typeof before === 'number' ? `${before} s → ` : ''}${after} s`
})
const clock = computed(() => {
  const time = props.outcome.time
  if (!time) return null
  const sync = time.ntpSynced === null ? '' : time.ntpSynced ? 'NTP đã đồng bộ' : 'NTP chưa đồng bộ'
  const skew = time.skewSeconds === null ? '' : `lệch ${time.skewSeconds > 0 ? '+' : ''}${time.skewSeconds} s so với trình duyệt`
  return [sync, skew].filter(Boolean).join(', ') || null
})
</script>

<template>
  <div class="space-y-1 text-[11px] text-[var(--text-secondary)]" data-test="reboot-result">
    <p v-if="uptime" class="font-mono" data-test="reboot-uptime">{{ uptime }}</p>
    <p v-if="outcome.info?.firmware" class="font-mono" data-test="reboot-firmware">Firmware: {{ outcome.info.firmware }}</p>
    <p v-if="clock" data-test="reboot-time">Đồng hồ thiết bị: {{ clock }}</p>
    <div v-if="outcome.outputs" class="flex flex-wrap gap-1" data-test="reboot-relays">
      <span
        v-for="ch in outcome.outputs"
        :key="ch.channel"
        class="px-1.5 py-0.5 rounded border border-[var(--border-subtle)] bg-[var(--surface-card)] font-mono"
      >DO{{ ch.channel }} {{ ch.state ? 'BẬT' : 'TẮT' }}{{ tag(ch) }}</span>
    </div>
    <ul v-if="outcome.check?.deviations.length" class="space-y-0.5 text-rose-600 dark:text-rose-400" data-test="reboot-deviations">
      <li v-for="line in outcome.check.deviations" :key="line" class="flex items-start gap-1">
        <AlertTriangle class="w-3 h-3 mt-px shrink-0" />{{ line }}
      </li>
    </ul>
    <ul v-if="outcome.check?.notes.length" class="space-y-0.5" data-test="reboot-notes">
      <li v-for="line in outcome.check.notes" :key="line">{{ line }}</li>
    </ul>
  </div>
</template>
