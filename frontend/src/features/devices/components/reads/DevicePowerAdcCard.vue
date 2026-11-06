<script setup lang="ts">
import type { ReadState, ReadStep } from '@/core/commands/useDeviceReads'
import DeviceReadCard from './DeviceReadCard.vue'
import ReadSection from './ReadSection.vue'
import { showValue, type FieldSpec } from './ReadFieldList.vue'
import type { ReadCardContext } from './readCard'

const props = defineProps<ReadCardContext>()

const steps: ReadStep[] = [
  { type: 'battery', action: 'get_status' },
  { type: 'battery', action: 'get_alerts' },
  { type: 'adc', action: 'get_config' },
  { type: 'adc', action: 'get_channels' },
  { type: 'adc', action: 'get_values' },
]

// Spec §6 battery and ADC rows. adc.get_status is not used here: its channels carry no `ai`.
const batteryFields: FieldSpec[] = [
  { key: 'available', label: 'Có phần cứng' },
  { key: 'voltage', label: 'Điện áp', unit: 'V' },
  { key: 'soc', label: 'Dung lượng', unit: '%' },
  { key: 'charge_rate', label: 'Tốc độ sạc' },
  { key: 'is_charging', label: 'Đang sạc' },
  { key: 'is_hibernating', label: 'Ngủ đông' },
  { key: 'alert_flags', label: 'Cờ cảnh báo' },
]
const alertFields: FieldSpec[] = [
  { key: 'voltage_low', label: 'Điện áp thấp' },
  { key: 'voltage_high', label: 'Điện áp cao' },
  { key: 'soc_low', label: 'Dung lượng thấp' },
  { key: 'soc_change', label: 'Dung lượng đổi' },
  { key: 'voltage_reset', label: 'Reset điện áp' },
  { key: 'reset_indicator', label: 'Chỉ báo reset' },
  { key: 'raw_flags', label: 'Cờ thô' },
]
const adcConfigFields: FieldSpec[] = [
  { key: 'available', label: 'Có phần cứng' },
  { key: 'channel_count', label: 'Số kênh' },
  { key: 'hardware', label: 'Phần cứng' },
]

interface AdcRow {
  ai: number
  name?: unknown
  type?: unknown
  enabled?: unknown
  value?: unknown
  unit?: unknown
  quality?: unknown
}

// The spec allows `data` to be an array; accept the same list wrapped in an object too.
function list(state: ReadState | undefined, wrapper: string): Record<string, unknown>[] {
  if (state?.kind !== 'ok') return []
  const data = state.data as unknown
  const items = Array.isArray(data) ? data
    : data && typeof data === 'object' ? (data as Record<string, unknown>)[wrapper] : null
  return Array.isArray(items) ? items.filter(x => x && typeof x === 'object') as Record<string, unknown>[] : []
}

/** Channel names from get_channels joined to values from get_values by `ai`; a channel present
 *  in only one of them is still shown. */
function adcRows(states: Record<string, ReadState>): AdcRow[] {
  const rows = new Map<number, AdcRow>()
  const row = (ai: unknown) => {
    // Only a real channel number: null or '' would otherwise become channel 0.
    const key = typeof ai === 'number' ? ai : typeof ai === 'string' && ai.trim() !== '' ? Number(ai) : NaN
    if (!Number.isInteger(key)) return null
    if (!rows.has(key)) rows.set(key, { ai: key })
    return rows.get(key)!
  }
  for (const ch of list(states['adc.get_channels'], 'channels')) {
    const r = row(ch.ai)
    if (r) Object.assign(r, { name: ch.name, type: ch.type, enabled: ch.enabled })
  }
  for (const v of list(states['adc.get_values'], 'values')) {
    const r = row(v.ai)
    if (r) Object.assign(r, { value: v.value, unit: v.unit, quality: v.quality })
  }
  return [...rows.values()].sort((a, b) => a.ai - b.ai)
}

// A reply whose shape the table cannot read is shown as it is rather than silently dropped.
const readable = (state: ReadState | undefined, wrapper: string) => state?.kind !== 'ok' || list(state, wrapper).length > 0

const cell = (value: unknown) => showValue(value)
</script>

<template>
  <DeviceReadCard v-bind="props" title="Nguồn & ADC" :steps="steps">
    <template #default="{ states, stale, now }">
      <ReadSection title="Pin" step-key="battery.get_status" :state="states['battery.get_status']" :stale="stale" :now="now" :fields="batteryFields" />
      <ReadSection title="Cảnh báo pin" step-key="battery.get_alerts" :state="states['battery.get_alerts']" :stale="stale" :now="now" :fields="alertFields" />
      <ReadSection title="ADC" step-key="adc.get_config" :state="states['adc.get_config']" :stale="stale" :now="now" :fields="adcConfigFields" />
      <ReadSection title="Kênh ADC" step-key="adc.get_channels" :state="states['adc.get_channels']" :stale="stale" :now="now" :bodyless="readable(states['adc.get_channels'], 'channels')" />
      <ReadSection title="Giá trị ADC" step-key="adc.get_values" :state="states['adc.get_values']" :stale="stale" :now="now" :bodyless="readable(states['adc.get_values'], 'values')" />
      <table v-if="adcRows(states).length" class="w-full text-[11px] font-mono" data-test="adc-table">
        <thead class="text-[10px] text-[var(--text-hint)] text-left">
          <tr><th>AI</th><th>Tên</th><th>Loại</th><th>Bật</th><th>Giá trị</th><th>Chất lượng</th></tr>
        </thead>
        <tbody>
          <tr v-for="r in adcRows(states)" :key="r.ai" :data-test="`adc-row-${r.ai}`" class="border-t border-[var(--border-subtle)]">
            <td>{{ r.ai }}</td>
            <td class="font-sans">{{ cell(r.name) }}</td>
            <td>{{ cell(r.type) }}</td>
            <td>{{ cell(r.enabled) }}</td>
            <td>{{ showValue(r.value, typeof r.unit === 'string' ? r.unit : undefined) }}</td>
            <td>{{ cell(r.quality) }}</td>
          </tr>
        </tbody>
      </table>
    </template>
  </DeviceReadCard>
</template>
