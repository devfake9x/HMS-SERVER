<script setup lang="ts">
import type { ReadStep } from '@/core/commands/useDeviceReads'
import DeviceReadCard from './DeviceReadCard.vue'
import ReadSection from './ReadSection.vue'
import ReadFieldList, { type FieldSpec } from './ReadFieldList.vue'
import type { ReadCardContext } from './readCard'

const props = defineProps<ReadCardContext>()

const steps: ReadStep[] = [
  { type: 'system', action: 'get_time' },
  { type: 'system', action: 'get_hostname' },
  { type: 'system', action: 'get_device_id' },
  { type: 'ota', action: 'get_status' },
]

// Spec §6: system.get_time, get_hostname, get_device_id. ota.get_status has no defined fields.
const timeFields: FieldSpec[] = [
  { key: 'datetime', label: 'Giờ thiết bị' },
  { key: 'timezone', label: 'Múi giờ' },
  { key: 'ntp_synced', label: 'Đã đồng bộ NTP' },
  { key: 'last_sync', label: 'Lần đồng bộ cuối', format: 'epoch' },
  { key: 'epoch', label: 'Epoch' },
  { key: 'ntp_mode', label: 'Chế độ NTP' },
  { key: 'rtc_available', label: 'Có RTC' },
  { key: 'rtc_model', label: 'RTC' },
]
// ota.get_status on the lab gateway (firmware 1.0.108) nests its fields one level deeper,
// as {success, data: {firmware, partition, ota, rollback}}; the spec does not define them.
const otaFields: FieldSpec[] = [
  { key: 'ota.state', label: 'Trạng thái OTA' },
  { key: 'ota.progress', label: 'Tiến độ', unit: '%' },
  { key: 'ota.error', label: 'Lỗi OTA' },
  { key: 'firmware.version', label: 'Firmware' },
  { key: 'partition.running', label: 'Phân vùng đang chạy' },
  { key: 'partition.state', label: 'Trạng thái phân vùng' },
  { key: 'rollback.possible', label: 'Có thể rollback' },
  { key: 'rollback.previousVersion', label: 'Bản trước' },
]

function unwrapOta(data: unknown): unknown {
  if (data && typeof data === 'object' && !Array.isArray(data)) {
    const outer = data as Record<string, unknown>
    if (typeof outer.success === 'boolean' && outer.data && typeof outer.data === 'object') return outer.data
  }
  return data
}
const hostnameFields: FieldSpec[] = [{ key: 'hostname', label: 'Hostname' }]
const idFields: FieldSpec[] = [
  { key: 'device_id', label: 'Device ID' },
  { key: 'mac', label: 'MAC' },
]
</script>

<template>
  <DeviceReadCard v-bind="props" title="Thiết bị & thời gian" :steps="steps">
    <template #default="{ states, stale, now }">
      <ReadSection title="Thời gian" step-key="system.get_time" :state="states['system.get_time']" :stale="stale" :now="now" :fields="timeFields" />
      <ReadSection title="Hostname" step-key="system.get_hostname" :state="states['system.get_hostname']" :stale="stale" :now="now" :fields="hostnameFields" />
      <ReadSection title="Định danh" step-key="system.get_device_id" :state="states['system.get_device_id']" :stale="stale" :now="now" :fields="idFields" />
      <ReadSection title="OTA" step-key="ota.get_status" :state="states['ota.get_status']" :stale="stale" :now="now">
        <template #default="{ data }">
          <ReadFieldList :data="unwrapOta(data)" :fields="otaFields" />
        </template>
      </ReadSection>
    </template>
  </DeviceReadCard>
</template>
