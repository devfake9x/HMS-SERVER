<script setup lang="ts">
import type { ReadState, ReadStep } from '@/core/commands/useDeviceReads'
import DeviceReadCard from './DeviceReadCard.vue'
import ReadSection from './ReadSection.vue'
import type { FieldSpec } from './ReadFieldList.vue'
import AdcChannelTable from './AdcChannelTable.vue'
import { listOf } from './adcData'
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

// A reply whose shape the table cannot read is shown as it is rather than silently dropped.
const readable = (state: ReadState | undefined, wrapper: string) => state?.kind !== 'ok' || listOf(state, wrapper).length > 0
</script>

<template>
  <DeviceReadCard v-bind="props" title="Nguồn & ADC" :steps="steps">
    <template #default="{ states, stale, now, refresh, lockReads }">
      <ReadSection title="Pin" step-key="battery.get_status" :state="states['battery.get_status']" :stale="stale" :now="now" :fields="batteryFields" />
      <ReadSection title="Cảnh báo pin" step-key="battery.get_alerts" :state="states['battery.get_alerts']" :stale="stale" :now="now" :fields="alertFields" />
      <ReadSection title="ADC" step-key="adc.get_config" :state="states['adc.get_config']" :stale="stale" :now="now" :fields="adcConfigFields" />
      <ReadSection title="Kênh ADC" step-key="adc.get_channels" :state="states['adc.get_channels']" :stale="stale" :now="now" :bodyless="readable(states['adc.get_channels'], 'channels')" />
      <ReadSection title="Giá trị ADC" step-key="adc.get_values" :state="states['adc.get_values']" :stale="stale" :now="now" :bodyless="readable(states['adc.get_values'], 'values')" />
      <AdcChannelTable v-bind="props" :states="states" :now="now" :refresh="refresh" :lock-reads="lockReads" />
    </template>
  </DeviceReadCard>
</template>
