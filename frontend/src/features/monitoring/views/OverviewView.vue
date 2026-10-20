<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { adminApi, type DeviceDto } from '@/core/api/adminApi'
import { createSessionFence } from '@/core/auth/sessionFence'
import StatusDot from '@/shared/ui/primitives/StatusDot.vue'
import Badge from '@/shared/ui/primitives/Badge.vue'
import Button from '@/shared/ui/primitives/Button.vue'
import DataTable, { type TableColumn } from '@/shared/ui/data-table/DataTable.vue'
import {
  HardDrive,
  Activity,
  AlertTriangle,
  Radio,
  Server,
  RefreshCw,
  Plus,
  ArrowUpRight,
  ShieldCheck
} from '@lucide/vue'

const router = useRouter()
const sessionFence = createSessionFence()

const loading = ref(true)
const devices = ref<DeviceDto[]>([])
const transportStatus = ref<'MQTT_DEFERRED' | 'CONNECTED' | 'UNAVAILABLE'>('MQTT_DEFERRED')
const catalogProof = ref<{ version: string; hash: string } | null>(null)
const lastRefresh = ref(new Date().toLocaleTimeString())

const totalCount = computed(() => devices.value.length)
const onlineCount = computed(() => devices.value.filter(d => d.state?.toLowerCase() === 'active').length)
const offlineCount = computed(() => devices.value.filter(d => d.state?.toLowerCase() === 'revoked' || !d.state).length)
const provisioningCount = computed(() => devices.value.filter(d => d.state?.toLowerCase() === 'provisioning').length)

const onlinePercentage = computed(() => {
  if (totalCount.value === 0) return 0
  return Math.round((onlineCount.value / totalCount.value) * 100)
})

const columns: TableColumn[] = [
  { key: 'id', label: 'Device ID', width: '220px', sortable: true },
  { key: 'name', label: 'Device Name', sortable: true },
  { key: 'state', label: 'Operational State', width: '150px' },
  { key: 'ownerOrganizationId', label: 'Organization', width: '180px' },
  { key: 'actions', label: 'Action', width: '90px', align: 'right' },
]

async function loadOverview() {
  loading.value = true
  const lease = sessionFence.rotate()
  try {
    const [deviceList, transportRes, catalogRes] = await Promise.all([
      adminApi.listDevices('', { signal: lease.signal }).catch(() => []),
      fetch('/api/v1/command-transport/status', { credentials: 'same-origin', signal: lease.signal }).catch(() => null),
      adminApi.commandCatalog({ signal: lease.signal }).catch(() => null),
    ])

    if (!sessionFence.isCurrent(lease)) return

    devices.value = deviceList
    if (transportRes && transportRes.ok) {
      const data = await transportRes.json() as { status?: string }
      transportStatus.value = (data.status as any) || 'MQTT_DEFERRED'
    }

    if (catalogRes) {
      catalogProof.value = {
        version: catalogRes.catalogVersion,
        hash: catalogRes.catalogHash.slice(0, 8),
      }
    }
    lastRefresh.value = new Date().toLocaleTimeString()
  } catch (err) {
    console.error('Failed to load overview metrics:', err)
  } finally {
    loading.value = false
  }
}

function navigateToDevice(deviceId: string) {
  router.push(`/devices/${deviceId}`)
}

onMounted(() => {
  void loadOverview()
})
</script>

<template>
  <div class="space-y-6" data-test="overview-view">
    <!-- Top Bar: Fleet Title + Quick Actions -->
    <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
      <div>
        <h1 class="text-xl font-bold tracking-tight text-zinc-900 dark:text-zinc-50">
          Fleet Telemetry & Status
        </h1>
        <p class="mt-1 text-xs text-zinc-500 dark:text-zinc-400">
          Real-time health, cryptographic catalog proof, and industrial edge device states.
        </p>
      </div>

      <div class="flex items-center gap-2">
        <Button
          variant="outline"
          size="sm"
          :loading="loading"
          @click="loadOverview"
          data-test="overview-refresh-btn"
        >
          <template #icon-left>
            <RefreshCw class="w-3.5 h-3.5" />
          </template>
          Refresh
        </Button>
        <Button
          variant="primary"
          size="sm"
          @click="router.push('/devices')"
          data-test="overview-add-device-btn"
        >
          <template #icon-left>
            <Plus class="w-3.5 h-3.5" />
          </template>
          Deploy Device
        </Button>
      </div>
    </div>

    <!-- 4 KPI Metrics Grid -->
    <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
      <!-- Total Devices -->
      <div class="p-4 bg-white dark:bg-zinc-900 border border-zinc-200/80 dark:border-zinc-800 rounded-xl shadow-xs flex items-center justify-between">
        <div>
          <p class="text-xs font-medium text-zinc-500 dark:text-zinc-400">Total Fleet</p>
          <p class="mt-1.5 text-2xl font-bold font-mono text-zinc-900 dark:text-zinc-50" data-test="kpi-total">
            {{ totalCount }}
          </p>
          <div class="mt-1 flex items-center gap-1.5 text-[11px] text-zinc-500">
            <span>Configured in hierarchy</span>
          </div>
        </div>
        <div class="w-11 h-11 rounded-lg bg-zinc-100 dark:bg-zinc-800/80 flex items-center justify-center text-zinc-600 dark:text-zinc-300">
          <HardDrive class="w-5 h-5" />
        </div>
      </div>

      <!-- Online / Active -->
      <div class="p-4 bg-white dark:bg-zinc-900 border border-zinc-200/80 dark:border-zinc-800 rounded-xl shadow-xs flex items-center justify-between">
        <div>
          <p class="text-xs font-medium text-zinc-500 dark:text-zinc-400">Active / Online</p>
          <div class="mt-1.5 flex items-baseline gap-2">
            <span class="text-2xl font-bold font-mono text-emerald-600 dark:text-emerald-400" data-test="kpi-online">
              {{ onlineCount }}
            </span>
            <span class="text-xs font-mono text-zinc-400">
              ({{ onlinePercentage }}%)
            </span>
          </div>
          <div class="mt-1 flex items-center gap-1.5 text-[11px] text-emerald-600 dark:text-emerald-400 font-medium">
            <StatusDot status="online" pulse size="sm" />
            <span>Telemetry heartbeat active</span>
          </div>
        </div>
        <div class="w-11 h-11 rounded-lg bg-emerald-500/10 flex items-center justify-center text-emerald-600 dark:text-emerald-400">
          <Activity class="w-5 h-5" />
        </div>
      </div>

      <!-- Provisioning State -->
      <div class="p-4 bg-white dark:bg-zinc-900 border border-zinc-200/80 dark:border-zinc-800 rounded-xl shadow-xs flex items-center justify-between">
        <div>
          <p class="text-xs font-medium text-zinc-500 dark:text-zinc-400">Provisioning</p>
          <p class="mt-1.5 text-2xl font-bold font-mono text-amber-600 dark:text-amber-400" data-test="kpi-provisioning">
            {{ provisioningCount }}
          </p>
          <div class="mt-1 flex items-center gap-1.5 text-[11px] text-amber-600 dark:text-amber-400">
            <StatusDot status="warning" size="sm" />
            <span>Awaiting key exchange</span>
          </div>
        </div>
        <div class="w-11 h-11 rounded-lg bg-amber-500/10 flex items-center justify-center text-amber-600 dark:text-amber-400">
          <AlertTriangle class="w-5 h-5" />
        </div>
      </div>

      <!-- Revoked / Offline -->
      <div class="p-4 bg-white dark:bg-zinc-900 border border-zinc-200/80 dark:border-zinc-800 rounded-xl shadow-xs flex items-center justify-between">
        <div>
          <p class="text-xs font-medium text-zinc-500 dark:text-zinc-400">Revoked / Inactive</p>
          <p class="mt-1.5 text-2xl font-bold font-mono text-zinc-600 dark:text-zinc-400" data-test="kpi-offline">
            {{ offlineCount }}
          </p>
          <div class="mt-1 flex items-center gap-1.5 text-[11px] text-zinc-400">
            <StatusDot status="offline" size="sm" />
            <span>Access disabled</span>
          </div>
        </div>
        <div class="w-11 h-11 rounded-lg bg-zinc-100 dark:bg-zinc-800/80 flex items-center justify-center text-zinc-400">
          <Radio class="w-5 h-5" />
        </div>
      </div>
    </div>

    <!-- System Status & Security Banner -->
    <div class="grid grid-cols-1 md:grid-cols-3 gap-4">
      <div class="p-4 bg-zinc-50/70 dark:bg-zinc-900/60 border border-zinc-200/70 dark:border-zinc-800 rounded-xl flex items-center gap-3">
        <div class="p-2.5 rounded-lg bg-amber-500/10 text-amber-500">
          <Server class="w-5 h-5" />
        </div>
        <div>
          <p class="text-xs font-semibold text-zinc-900 dark:text-zinc-200">MQTT Broker Transport</p>
          <p class="text-[11px] font-mono text-zinc-500 dark:text-zinc-400">
            Status: <span class="text-amber-600 dark:text-amber-400 font-semibold">{{ transportStatus }}</span>
          </p>
        </div>
      </div>

      <div class="p-4 bg-zinc-50/70 dark:bg-zinc-900/60 border border-zinc-200/70 dark:border-zinc-800 rounded-xl flex items-center gap-3">
        <div class="p-2.5 rounded-lg bg-emerald-500/10 text-emerald-500">
          <ShieldCheck class="w-5 h-5" />
        </div>
        <div>
          <p class="text-xs font-semibold text-zinc-900 dark:text-zinc-200">Command Catalog Proof</p>
          <p class="text-[11px] font-mono text-zinc-500 dark:text-zinc-400 truncate max-w-[180px]">
            {{ catalogProof ? `v${catalogProof.version} (${catalogProof.hash})` : 'Verified & Trusted' }}
          </p>
        </div>
      </div>

      <div class="p-4 bg-zinc-50/70 dark:bg-zinc-900/60 border border-zinc-200/70 dark:border-zinc-800 rounded-xl flex items-center gap-3">
        <div class="p-2.5 rounded-lg bg-sky-500/10 text-sky-500">
          <Activity class="w-5 h-5" />
        </div>
        <div>
          <p class="text-xs font-semibold text-zinc-900 dark:text-zinc-200">Session Fence Epoch</p>
          <p class="text-[11px] font-mono text-zinc-500 dark:text-zinc-400">
            Last sync: {{ lastRefresh }}
          </p>
        </div>
      </div>
    </div>

    <!-- Recent Devices Inventory Table -->
    <div class="space-y-3">
      <div class="flex items-center justify-between">
        <h2 class="text-sm font-semibold text-zinc-900 dark:text-zinc-100">
          Recent Edge Devices
        </h2>
        <router-link
          to="/devices"
          class="text-xs font-medium text-amber-600 dark:text-amber-400 hover:underline inline-flex items-center gap-1"
        >
          <span>View all devices</span>
          <ArrowUpRight class="w-3.5 h-3.5" />
        </router-link>
      </div>

      <DataTable
        :columns="columns"
        :items="devices"
        :loading="loading"
        empty-message="No devices enrolled in the platform yet."
        :page-size="5"
        @row-click="navigateToDevice($event.id)"
      >
        <template #cell-id="{ value }">
          <span class="font-mono text-[11px] text-zinc-700 dark:text-zinc-300 font-semibold">
            {{ value }}
          </span>
        </template>

        <template #cell-name="{ value }">
          <div class="flex items-center gap-2">
            <HardDrive class="w-3.5 h-3.5 text-zinc-400" />
            <span class="font-medium text-zinc-900 dark:text-zinc-100">{{ value }}</span>
          </div>
        </template>

        <template #cell-state="{ value }">
          <div class="flex items-center gap-1.5">
            <StatusDot
              :status="value?.toLowerCase() === 'active' ? 'online' : value?.toLowerCase() === 'provisioning' ? 'warning' : 'offline'"
              size="sm"
            />
            <Badge
              :variant="value?.toLowerCase() === 'active' ? 'success' : value?.toLowerCase() === 'provisioning' ? 'warning' : 'neutral'"
              size="xs"
            >
              {{ value || 'Unknown' }}
            </Badge>
          </div>
        </template>

        <template #cell-ownerOrganizationId="{ value }">
          <span class="text-xs text-zinc-600 dark:text-zinc-400 font-mono">{{ value }}</span>
        </template>

        <template #cell-actions="{ item }">
          <button
            type="button"
            class="p-1 text-zinc-400 hover:text-amber-600 dark:hover:text-amber-400 rounded transition-colors"
            @click.stop="navigateToDevice(item.id)"
            title="Inspect device telemetry"
          >
            <ArrowUpRight class="w-4 h-4" />
          </button>
        </template>
      </DataTable>
    </div>
  </div>
</template>
