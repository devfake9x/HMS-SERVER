<script setup lang="ts">
import type { ReadStep } from '@/core/commands/useDeviceReads'
import DeviceReadCard from './DeviceReadCard.vue'
import ReadSection from './ReadSection.vue'
import type { FieldSpec } from './ReadFieldList.vue'
import type { ReadCardContext } from './readCard'

const props = defineProps<ReadCardContext>()

const steps: ReadStep[] = [
  { type: 'network', action: 'get_priority' },
  { type: 'network', action: 'get_ethernet' },
  { type: 'network', action: 'get_modem' },
  { type: 'network', action: 'wifi_sta_status' },
  { type: 'network', action: 'wifi_ap_status' },
]

// Spec §6 network rows. network.get_modem is a projection (§6.5) without a fixed field list.
const priorityFields: FieldSpec[] = [
  { key: 'active_uplink', label: 'Uplink đang dùng' },
  { key: 'internet_uplink', label: 'Uplink internet' },
  { key: 'has_connectivity', label: 'Có kết nối' },
]
const ethernetFields: FieldSpec[] = [
  { key: 'connected', label: 'Đã kết nối' },
  { key: 'active', label: 'Đang dùng' },
  { key: 'ip', label: 'IP' },
  { key: 'gateway', label: 'Gateway' },
  { key: 'netmask', label: 'Netmask' },
  { key: 'type', label: 'Loại' },
]
const staFields: FieldSpec[] = [
  { key: 'enabled', label: 'Bật' },
  { key: 'connected', label: 'Đã kết nối' },
  { key: 'ssid', label: 'SSID' },
  { key: 'ip', label: 'IP' },
  { key: 'rssi', label: 'RSSI', unit: 'dBm' },
  { key: 'channel', label: 'Kênh' },
  { key: 'auth', label: 'Bảo mật' },
]
const apFields: FieldSpec[] = [
  { key: 'enabled', label: 'Bật' },
  { key: 'ssid', label: 'SSID' },
  { key: 'channel', label: 'Kênh' },
  { key: 'client_count', label: 'Số client' },
  { key: 'pending_restart', label: 'Chờ khởi động lại' },
]
</script>

<template>
  <DeviceReadCard v-bind="props" title="Mạng" :steps="steps">
    <template #default="{ states, stale, now }">
      <ReadSection title="Uplink" step-key="network.get_priority" :state="states['network.get_priority']" :stale="stale" :now="now" :fields="priorityFields" />
      <ReadSection title="Ethernet" step-key="network.get_ethernet" :state="states['network.get_ethernet']" :stale="stale" :now="now" :fields="ethernetFields" />
      <ReadSection title="Modem" step-key="network.get_modem" :state="states['network.get_modem']" :stale="stale" :now="now" />
      <ReadSection title="Wi-Fi STA" step-key="network.wifi_sta_status" :state="states['network.wifi_sta_status']" :stale="stale" :now="now" :fields="staFields" />
      <ReadSection title="Wi-Fi AP" step-key="network.wifi_ap_status" :state="states['network.wifi_ap_status']" :stale="stale" :now="now" :fields="apFields" />
    </template>
  </DeviceReadCard>
</template>
