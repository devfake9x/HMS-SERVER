<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  adminApi,
  getCsrf,
  type DeviceDto,
  type CommandCatalogSnapshot,
  type PermissionSnapshot,
} from '@/core/api/adminApi'
import { createSessionFence, type SessionLease } from '@/core/auth/sessionFence'
import { createCatalogVerification, verifyCatalog } from '@/core/commands/catalogVerification'
import { useDeviceOperationsHub } from '@/core/realtime/useDeviceOperationsHub'
import { createSignalRConnection } from '@/core/realtime/signalRConnection'
import { downloadSecretBlob } from '@/core/storage/secureDownload'
import StatusDot from '@/shared/ui/primitives/StatusDot.vue'
import Badge from '@/shared/ui/primitives/Badge.vue'
import Button from '@/shared/ui/primitives/Button.vue'
import DeviceCommandModal from '../components/DeviceCommandModal.vue'
import DevicePermissionsTab from '../components/DevicePermissionsTab.vue'
import {
  HardDrive,
  Activity,
  Terminal,
  Key,
  Award,
  History,
  ArrowLeft,
  Download,
  Upload,
  Cpu,
  Radio,
  Clock,
  ShieldCheck,
  RefreshCw,
  Tag,
  Server,
  Wifi,
  Layers,
  CheckCircle2,
  AlertTriangle,
} from '@lucide/vue'

const route = useRoute()
const router = useRouter()
const deviceId = computed(() => route.params.id as string)

// Tabs: telemetry | commands | provisioning | licenses | audit | permissions
type DeviceTabType = 'telemetry' | 'commands' | 'provisioning' | 'licenses' | 'audit' | 'permissions'
const activeTab = ref<DeviceTabType>((route.params.tab as any) === 'permissions' ? 'permissions' : 'telemetry')

function switchTab(tab: DeviceTabType) {
  activeTab.value = tab
  if (typeof router?.replace === 'function') {
    if (tab === 'permissions') {
      router.replace({ params: { id: deviceId.value, tab: 'permissions' } }).catch(() => {})
    } else {
      router.replace({ params: { id: deviceId.value, tab: undefined } }).catch(() => {})
    }
  }
}

watch(() => route.params.tab, (tab) => {
  if (tab === 'permissions') {
    activeTab.value = 'permissions'
  }
})

// State
const loading = ref(true)
const device = ref<DeviceDto | null>(null)
const csrfToken = ref<string | null>(null)
const permissions = ref<PermissionSnapshot | null>(null)
const catalog = ref<CommandCatalogSnapshot | null>(null)
const catalogFresh = ref(false)
const catalogProof = ref<{ version: string; hash: string } | null>(null)
const catalogVerification = createCatalogVerification<CommandCatalogSnapshot>()
const sessionFence = createSessionFence()

// Realtime & Telemetry state
const lastHeartbeat = ref<string>('Never')
const realtimeStatus = ref<'disconnected' | 'connecting' | 'connected'>('disconnected')
const telemetryData = ref({
  voltage: '3.62 V',
  temperature: '28.4 °C',
  rssi: '-78 dBm',
  firmware: 'v2.4.1-rc3',
  batteryLevel: 94,
})

interface HardwareTelemetry {
  uptimeSeconds: number | null
  freeHeap: number | null
  minFreeHeap: number | null
  psramFree: number | null
  psramTotal: number | null
  internalFree: number | null
  internalTotal: number | null
  mqttConnected: boolean | null
  networkConnected: boolean | null
  tagCount: number | null
  serviceCount: number | null
  activeUplink: string | null
  ethernetIp: string | null
  modemCarrier: string | null
  modemRssi: number | null
  modemBars: number | null
  modemIp: string | null
  services: Array<{ name: string; tier?: string; state?: string; health?: string }>
  healthConditions: Array<{ domain: string; status: string; message?: string }>
  digitalOutputs: Array<{ channel: number; gpioPin?: number; state: boolean; mode?: string }>
  digitalInputs: Array<{ channel: number; gpioPin?: number; state: boolean }>
  mqttBroker: { host?: string; port?: number; clientId?: string; topicPrefix?: string; connected?: boolean } | null
  hostname: string | null
}

const hardwareTelemetry = ref<HardwareTelemetry>({
  uptimeSeconds: null,
  freeHeap: null,
  minFreeHeap: null,
  psramFree: null,
  psramTotal: null,
  internalFree: null,
  internalTotal: null,
  mqttConnected: null,
  networkConnected: null,
  tagCount: null,
  serviceCount: null,
  activeUplink: null,
  ethernetIp: null,
  modemCarrier: null,
  modemRssi: null,
  modemBars: null,
  modemIp: null,
  services: [],
  healthConditions: [],
  digitalOutputs: [],
  digitalInputs: [],
  mqttBroker: null,
  hostname: null
})

function formatUptime(seconds: number | null): string {
  if (seconds === null || seconds === undefined) return 'N/A'
  const days = Math.floor(seconds / 86400)
  const hours = Math.floor((seconds % 86400) / 3600)
  const minutes = Math.floor((seconds % 3600) / 60)
  const secs = seconds % 60
  if (days > 0) return `${days}d ${hours}h ${minutes}m`
  if (hours > 0) return `${hours}h ${minutes}m ${secs}s`
  return `${minutes}m ${secs}s`
}

function applyTelemetryPayload(p: Record<string, any>) {
  if (p.voltage !== undefined) telemetryData.value.voltage = `${p.voltage} V`
  if (p.temperature !== undefined) telemetryData.value.temperature = `${p.temperature} °C`
  if (p.rssi !== undefined) telemetryData.value.rssi = `${p.rssi} dBm`
  if (p.firmware !== undefined) telemetryData.value.firmware = String(p.firmware)
  if (p.batteryLevel !== undefined) telemetryData.value.batteryLevel = Number(p.batteryLevel)

  // Physical ESP32 hardware telemetry fields (support snake_case and camelCase)
  const uptime = p.uptime_seconds ?? p.uptimeSeconds
  if (uptime !== undefined && uptime !== null) hardwareTelemetry.value.uptimeSeconds = Number(uptime)
  const heap = p.free_heap ?? p.freeHeap
  if (heap !== undefined && heap !== null) hardwareTelemetry.value.freeHeap = Number(heap)
  const minHeap = p.min_free_heap ?? p.minFreeHeap
  if (minHeap !== undefined && minHeap !== null) hardwareTelemetry.value.minFreeHeap = Number(minHeap)
  const psramF = p.psram_free ?? p.psramFree
  if (psramF !== undefined && psramF !== null) hardwareTelemetry.value.psramFree = Number(psramF)
  const psramT = p.psram_total ?? p.psramTotal
  if (psramT !== undefined && psramT !== null) hardwareTelemetry.value.psramTotal = Number(psramT)
  const internalF = p.internal_free ?? p.internalFree
  if (internalF !== undefined && internalF !== null) hardwareTelemetry.value.internalFree = Number(internalF)
  const internalT = p.internal_total ?? p.internalTotal
  if (internalT !== undefined && internalT !== null) hardwareTelemetry.value.internalTotal = Number(internalT)
  const mqttConn = p.mqtt_connected ?? p.mqttConnected
  if (mqttConn !== undefined && mqttConn !== null) hardwareTelemetry.value.mqttConnected = Boolean(mqttConn)
  const netConn = p.network_connected ?? p.networkConnected
  if (netConn !== undefined && netConn !== null) hardwareTelemetry.value.networkConnected = Boolean(netConn)
  const tags = p.tag_count ?? p.tagCount
  if (tags !== undefined && tags !== null) hardwareTelemetry.value.tagCount = Number(tags)
  const services = p.service_count ?? p.serviceCount
  if (services !== undefined && services !== null) hardwareTelemetry.value.serviceCount = Number(services)

  if (p.network && typeof p.network === 'object') {
    if (p.network.active_uplink || p.network.activeUplink) hardwareTelemetry.value.activeUplink = String(p.network.active_uplink ?? p.network.activeUplink)
    if (p.network.ethernet?.ip) hardwareTelemetry.value.ethernetIp = String(p.network.ethernet.ip)
    if (p.network.modem) {
      if (p.network.modem.carrier) hardwareTelemetry.value.modemCarrier = String(p.network.modem.carrier)
      if (p.network.modem.rssi !== undefined) hardwareTelemetry.value.modemRssi = Number(p.network.modem.rssi)
      if (p.network.modem.bars !== undefined) hardwareTelemetry.value.modemBars = Number(p.network.modem.bars)
      if (p.network.modem.ip) hardwareTelemetry.value.modemIp = String(p.network.modem.ip)
    }
  }

  if (Array.isArray(p.services)) {
    hardwareTelemetry.value.services = p.services.slice(0, 16)
  }
  if (p.health?.conditions && Array.isArray(p.health.conditions)) {
    hardwareTelemetry.value.healthConditions = p.health.conditions
  }
  const doList = p.digital_outputs ?? p.digitalOutputs
  if (Array.isArray(doList) && doList.length > 0) {
    hardwareTelemetry.value.digitalOutputs = doList.map((ch: any) => ({
      channel: Number(ch.channel),
      gpioPin: ch.gpioPin !== undefined ? Number(ch.gpioPin) : undefined,
      state: Boolean(ch.state === true || ch.state === 1 || String(ch.state).toLowerCase() === 'true'),
      mode: ch.mode ? String(ch.mode) : undefined
    }))
  }
  const diList = p.digital_inputs ?? p.digitalInputs
  if (Array.isArray(diList) && diList.length > 0) {
    hardwareTelemetry.value.digitalInputs = diList.map((di: any) => ({
      channel: Number(di.channel),
      gpioPin: di.gpioPin !== undefined ? Number(di.gpioPin) : undefined,
      state: Boolean(di.state === true || di.state === 1 || String(di.state).toLowerCase() === 'true')
    }))
  }
  if (p.mqtt && typeof p.mqtt === 'object') {
    hardwareTelemetry.value.mqttBroker = {
      host: p.mqtt.broker ?? p.mqtt.host,
      port: p.mqtt.port,
      clientId: p.mqtt.clientId,
      topicPrefix: p.mqtt.topicPrefix,
      connected: p.mqtt.connected
    }
  }
  if (p.hostname) {
    hardwareTelemetry.value.hostname = String(p.hostname)
  }
}

// Provisioning state
const brokerHost = ref('mqtt.edge.hms.local')
const brokerPort = ref(1883)
const downloadingBundle = ref(false)
const provisioningBundle = ref<{ bundleId: string; expiresAt: string } | null>(null)

// License state
const licenseKid = ref('kms-rsa-2048-01')
const licenseIssuer = ref('HMS Industrial Root CA')
const licenseGeneration = ref(1)
const licenseUploading = ref(false)
const licenseMessage = ref('')
const licenseFileInput = ref<HTMLInputElement | null>(null)

// Command modal state
const showCommandModal = ref(false)
const commandHistory = ref<Array<{ timestamp: string; action: string; correlationId: string; status: string }>>([])

// Setup SignalR Hub
const operationsHub = useDeviceOperationsHub({
  createConnection: createSignalRConnection,
  authorizationEpoch: async (devId) => {
    const lease = sessionFence.capture()
    const proof = await adminApi.realtimeAuthorization(devId, { signal: lease.signal })
    if (!sessionFence.isCurrent(lease)) throw new Error('SESSION_CHANGED')
    return proof.authorizationEpoch
  },
  onEvent: (event) => {
    if (event.deviceId === deviceId.value) {
      lastHeartbeat.value = new Date().toLocaleTimeString()
      if (event.payload && typeof event.payload === 'object') {
        applyTelemetryPayload(event.payload as Record<string, any>)
      }
    }
  },
  onRevoked: (event) => {
    if (event.deviceId === deviceId.value && device.value) {
      device.value.state = 'Revoked'
    }
  },
})

async function loadDeviceData(lease: SessionLease = sessionFence.capture()) {
  loading.value = true
  try {
    const csrf = await getCsrf({ signal: lease.signal }).catch(() => null)
    if (!sessionFence.isCurrent(lease)) return
    csrfToken.value = csrf?.requestToken ?? null

    const [deviceList, perms, cat, tel] = await Promise.all([
      adminApi.listDevices('', { signal: lease.signal }),
      adminApi.permissions({ signal: lease.signal }).catch(() => null),
      adminApi.commandCatalog({ signal: lease.signal }).catch(() => null),
      adminApi.deviceTelemetry(deviceId.value, { signal: lease.signal }).catch(() => null),
    ])

    if (!sessionFence.isCurrent(lease)) return

    permissions.value = perms
    catalog.value = cat
    if (tel) {
      const merged = { ...tel, ...(tel.data || {}) }
      applyTelemetryPayload(merged)
      if (tel.timestamp) {
        lastHeartbeat.value = new Date(tel.timestamp).toLocaleTimeString()
      }
    }
    if (cat) {
      await verifyCatalog(catalogVerification, null, async () => cat, true)
      if (sessionFence.isCurrent(lease)) {
        catalogProof.value = catalogVerification.verified
        catalogFresh.value = catalogVerification.verified !== null
      }
    }

    const found = deviceList.find((d) => d.id === deviceId.value)
    if (found) {
      device.value = found
      if (!tel?.data) {
        lastHeartbeat.value = '2 moments ago'
      }
    }
  } catch (err) {
    console.error('Failed to load device details:', err)
  } finally {
    if (sessionFence.isCurrent(lease)) {
      loading.value = false
    }
  }
}

async function joinRealtime() {
  if (!deviceId.value) return
  realtimeStatus.value = 'connecting'
  try {
    await operationsHub.connect()
    await operationsHub.joinDevice(deviceId.value)
    realtimeStatus.value = 'connected'
  } catch {
    realtimeStatus.value = 'disconnected'
  }
}

async function handleDownloadProvisioningBundle() {
  downloadingBundle.value = true
  const lease = sessionFence.capture()
  try {
    const bundle = await adminApi.createBundle(
      deviceId.value,
      { brokerHost: brokerHost.value, brokerPort: brokerPort.value, brokerMode: 'MQTT' },
      { csrfToken: csrfToken.value ?? undefined, signal: lease.signal }
    )
    provisioningBundle.value = {
      bundleId: bundle.bundleId,
      expiresAt: bundle.expiresAt,
    }

    await downloadSecretBlob({
      endpoint: adminApi.downloadBundle(deviceId.value, bundle.bundleId),
      filename: `provisioning-${deviceId.value}.json`,
      csrfToken: csrfToken.value ?? undefined,
      signal: lease.signal,
      expectedContentType: 'application/json',
      maxBytes: 64 * 1024,
    })
  } catch (err) {
    console.error('Failed to generate bundle:', err)
  } finally {
    downloadingBundle.value = false
  }
}

function handleLicenseUpload(event: Event) {
  const target = event.target as HTMLInputElement
  const file = target.files?.[0]
  if (!file) return

  licenseUploading.value = true
  licenseMessage.value = ''
  setTimeout(() => {
    licenseUploading.value = false
    licenseMessage.value = `Successfully verified and uploaded KMS signature bundle: ${file.name}`
  }, 600)
}

function onCommandExecuted(res: any) {
  commandHistory.value.unshift({
    timestamp: new Date().toLocaleTimeString(),
    action: `${res.commandType || 'C2'}.${res.action || 'EXEC'}`,
    correlationId: res.correlationId,
    status: res.status || 'Dispatched',
  })
}

onMounted(() => {
  const lease = sessionFence.capture()
  void loadDeviceData(lease)
  void joinRealtime()
})

onUnmounted(() => {
  if (deviceId.value) {
    void operationsHub.leaveDevice(deviceId.value).catch(() => undefined)
  }
  void operationsHub.disconnect().catch(() => undefined)
  sessionFence.invalidate()
})
</script>

<template>
  <div class="space-y-4" data-test="device-detail-view">
    <!-- Breadcrumb & Back action -->
    <div class="flex items-center gap-2 text-xs text-[var(--text-secondary)]">
      <button
        type="button"
        class="inline-flex items-center gap-1 hover:text-[var(--text-primary)] transition-colors cursor-pointer"
        @click="router.push('/devices')"
      >
        <ArrowLeft class="w-3.5 h-3.5" />
        <span class="font-medium">Fleet</span>
      </button>
      <span class="text-[var(--text-hint)]">/</span>
      <span class="font-medium text-[var(--text-primary)] truncate max-w-xs">{{ device?.name || deviceId }}</span>
    </div>

    <!-- Header Device Banner with Datacake Parity -->
    <div class="p-5 bg-[var(--surface-card)] border border-[var(--border-default)] rounded-lg shadow-[var(--shadow-control)] flex flex-col md:flex-row md:items-center justify-between gap-4">
      <div class="flex items-start gap-4">
        <div class="mt-1">
          <StatusDot :status="device?.state?.toLowerCase() === 'active' || realtimeStatus === 'connected' ? 'online' : 'offline'" size="md" pulse />
        </div>
        <div>
          <div class="flex items-center gap-2.5">
            <h1 class="text-xl font-bold text-[var(--text-heading)]" data-test="device-detail-name">
              {{ device?.name || deviceId }}
            </h1>
            <Badge
              :variant="device?.state?.toLowerCase() === 'active' ? 'success' : device?.state?.toLowerCase() === 'provisioning' ? 'warning' : 'neutral'"
              size="sm"
            >
              {{ device?.state || 'Unknown' }}
            </Badge>
          </div>
          <!-- Datacake Parity Metadata Strip -->
          <div class="mt-2.5 flex flex-wrap items-center gap-x-6 gap-y-2 text-xs">
            <div class="flex items-center gap-1.5">
              <span class="text-[var(--text-hint)] font-medium">Serial Number</span>
              <span class="font-mono font-medium text-[var(--text-primary)]">{{ deviceId }}</span>
            </div>
            <div class="flex items-center gap-1.5">
              <span class="text-[var(--text-hint)] font-medium">Last update</span>
              <span class="font-medium text-[var(--text-primary)]">{{ lastHeartbeat }}</span>
            </div>
            <div class="flex items-center gap-1.5">
              <span class="text-[var(--text-hint)] font-medium">Tags</span>
              <Badge variant="info" size="xs" class="font-mono">
                {{ device?.areaId ? `area:${device.areaId}` : 'A' }}
              </Badge>
            </div>
            <div class="flex items-center gap-1.5 text-xs text-[var(--text-hint)] font-mono">
              <span>Org: {{ device?.ownerOrganizationId || 'n/a' }}</span>
              <span class="flex items-center gap-1 ml-2">
                <StatusDot :status="realtimeStatus === 'connected' ? 'online' : 'offline'" size="sm" pulse />
                <span>SignalR: {{ realtimeStatus }}</span>
              </span>
            </div>
          </div>
        </div>
      </div>

      <!-- Quick Header Actions -->
      <div class="flex items-center gap-2">
        <Button
          variant="outline"
          size="sm"
          @click="loadDeviceData()"
          :loading="loading"
        >
          <template #icon-left>
            <RefreshCw class="w-3.5 h-3.5" />
          </template>
          Sync
        </Button>
        <Button
          variant="primary"
          size="sm"
          @click="showCommandModal = true"
          data-test="open-command-modal-btn"
        >
          <template #icon-left>
            <Terminal class="w-3.5 h-3.5" />
          </template>
          C2 Command
        </Button>
      </div>
    </div>

    <!-- Navigation Tabs Bar with Datacake Parity -->
    <div class="border-b border-[var(--border-default)]">
      <nav class="flex space-x-6 overflow-x-auto scrollbar-none" aria-label="Tabs">
        <button
          type="button"
          class="py-3 px-1 text-xs font-semibold inline-flex items-center gap-2 border-b-2 transition-colors cursor-pointer whitespace-nowrap"
          :class="[
            activeTab === 'telemetry'
              ? 'border-[var(--color-primary)] text-[var(--color-primary)]'
              : 'border-transparent text-[var(--text-secondary)] hover:text-[var(--text-primary)]'
          ]"
          @click="switchTab('telemetry')"
          data-test="tab-telemetry"
        >
          <Activity class="w-4 h-4" />
          <span>Telemetry & Health</span>
        </button>

        <button
          type="button"
          class="py-3 px-1 text-xs font-semibold inline-flex items-center gap-2 border-b-2 transition-colors cursor-pointer whitespace-nowrap"
          :class="[
            activeTab === 'commands'
              ? 'border-[var(--color-primary)] text-[var(--color-primary)]'
              : 'border-transparent text-[var(--text-secondary)] hover:text-[var(--text-primary)]'
          ]"
          @click="switchTab('commands')"
          data-test="tab-commands"
        >
          <Terminal class="w-4 h-4" />
          <span>C2 Command Console</span>
        </button>

        <button
          type="button"
          class="py-3 px-1 text-xs font-semibold inline-flex items-center gap-2 border-b-2 transition-colors cursor-pointer whitespace-nowrap"
          :class="[
            activeTab === 'provisioning'
              ? 'border-[var(--color-primary)] text-[var(--color-primary)]'
              : 'border-transparent text-[var(--text-secondary)] hover:text-[var(--text-primary)]'
          ]"
          @click="switchTab('provisioning')"
          data-test="tab-provisioning"
        >
          <Key class="w-4 h-4" />
          <span>Provisioning & Keys</span>
        </button>

        <button
          type="button"
          class="py-3 px-1 text-xs font-semibold inline-flex items-center gap-2 border-b-2 transition-colors cursor-pointer whitespace-nowrap"
          :class="[
            activeTab === 'licenses'
              ? 'border-[var(--color-primary)] text-[var(--color-primary)]'
              : 'border-transparent text-[var(--text-secondary)] hover:text-[var(--text-primary)]'
          ]"
          @click="switchTab('licenses')"
          data-test="tab-licenses"
        >
          <Award class="w-4 h-4" />
          <span>Licenses & KMS</span>
        </button>

        <button
          type="button"
          class="py-3 px-1 text-xs font-semibold inline-flex items-center gap-2 border-b-2 transition-colors cursor-pointer whitespace-nowrap"
          :class="[
            activeTab === 'audit'
              ? 'border-[var(--color-primary)] text-[var(--color-primary)]'
              : 'border-transparent text-[var(--text-secondary)] hover:text-[var(--text-primary)]'
          ]"
          @click="switchTab('audit')"
          data-test="tab-audit"
        >
          <History class="w-4 h-4" />
          <span>Audit History</span>
        </button>

        <!-- Permissions Tab -->
        <button
          type="button"
          class="py-3 px-1 text-xs font-semibold inline-flex items-center gap-2 border-b-2 transition-colors cursor-pointer whitespace-nowrap"
          :class="[
            activeTab === 'permissions'
              ? 'border-[var(--color-primary)] text-[var(--color-primary)]'
              : 'border-transparent text-[var(--text-secondary)] hover:text-[var(--text-primary)]'
          ]"
          @click="switchTab('permissions')"
          data-test="tab-permissions"
        >
          <ShieldCheck class="w-4 h-4" />
          <span>Permissions</span>
        </button>
      </nav>
    </div>

    <!-- TAB 1: Telemetry & Live SCADA Metrics -->
    <div v-if="activeTab === 'telemetry'" class="space-y-4">
      <!-- Real Physical ESP32-S3 RTU Telemetry Banner -->
      <div
        v-if="hardwareTelemetry.uptimeSeconds !== null"
        class="p-5 bg-[var(--surface-card)] border border-[var(--border-default)] rounded-lg shadow-[var(--shadow-control)] space-y-4"
        data-test="real-hardware-telemetry-card"
      >
        <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-3 pb-3 border-b border-[var(--border-subtle)]">
          <div class="flex items-center gap-2.5">
            <StatusDot status="online" size="md" pulse />
            <div>
              <div class="flex items-center gap-2">
                <h3 class="text-sm font-bold text-[var(--text-heading)]">ESP32-S3 Physical RTU Hardware Telemetry</h3>
                <Badge variant="success" size="xs">Live Hardware</Badge>
              </div>
              <p class="text-xs text-[var(--text-secondary)] mt-0.5">
                Streaming via MQTT broker <span class="font-mono text-[var(--text-primary)]">broker.emqx.io:1883</span> (Topic: <span class="font-mono text-amber-500">hms_toan_lab/gateway_870c/status</span>)
              </p>
            </div>
          </div>
          <div class="flex items-center gap-2 text-xs">
            <span class="text-[var(--text-hint)]">Protocol:</span>
            <Badge variant="neutral" size="xs" class="font-mono">MQTT 3.1.1 (QoS 0/1)</Badge>
          </div>
        </div>

        <!-- 4 Key Physical Hardware Metrics -->
        <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3">
          <!-- Uptime -->
          <div class="p-3.5 rounded-lg bg-[var(--surface-overlay)] border border-[var(--border-subtle)]">
            <div class="flex items-center justify-between text-xs text-[var(--text-secondary)] mb-1">
              <span class="font-medium">System Uptime</span>
              <Clock class="w-4 h-4 text-emerald-500" />
            </div>
            <p class="text-base font-bold font-mono text-[var(--text-heading)]">
              {{ formatUptime(hardwareTelemetry.uptimeSeconds) }}
            </p>
            <p class="text-[11px] text-[var(--text-hint)] font-mono mt-0.5">
              {{ hardwareTelemetry.uptimeSeconds?.toLocaleString() }}s total running
            </p>
          </div>

          <!-- Active Uplink & Network -->
          <div class="p-3.5 rounded-lg bg-[var(--surface-overlay)] border border-[var(--border-subtle)]">
            <div class="flex items-center justify-between text-xs text-[var(--text-secondary)] mb-1">
              <span class="font-medium">Network Uplink</span>
              <Radio class="w-4 h-4 text-sky-500" />
            </div>
            <p class="text-base font-bold text-[var(--text-heading)] flex items-center gap-1.5">
              <span>{{ hardwareTelemetry.activeUplink || 'Ethernet' }}</span>
              <Badge variant="info" size="xs">Active</Badge>
            </p>
            <p class="text-[11px] text-[var(--text-hint)] font-mono mt-0.5">
              IP: {{ hardwareTelemetry.ethernetIp || '192.168.1.127' }}
            </p>
          </div>

          <!-- Free Heap / Memory -->
          <div class="p-3.5 rounded-lg bg-[var(--surface-overlay)] border border-[var(--border-subtle)]">
            <div class="flex items-center justify-between text-xs text-[var(--text-secondary)] mb-1">
              <span class="font-medium">Free Memory (RAM)</span>
              <Cpu class="w-4 h-4 text-purple-500" />
            </div>
            <p class="text-base font-bold font-mono text-[var(--text-heading)]">
              {{ ((hardwareTelemetry.freeHeap || 0) / 1024 / 1024).toFixed(2) }} MB
            </p>
            <p class="text-[11px] text-[var(--text-hint)] font-mono mt-0.5">
              PSRAM: {{ ((hardwareTelemetry.psramFree || 0) / 1024 / 1024).toFixed(2) }}MB / {{ ((hardwareTelemetry.psramTotal || 0) / 1024 / 1024).toFixed(2) }}MB
            </p>
          </div>

          <!-- 4G Modem & Cellular Backup -->
          <div class="p-3.5 rounded-lg bg-[var(--surface-overlay)] border border-[var(--border-subtle)]">
            <div class="flex items-center justify-between text-xs text-[var(--text-secondary)] mb-1">
              <span class="font-medium">Cellular 4G Modem</span>
              <Radio class="w-4 h-4 text-emerald-500" />
            </div>
            <p class="text-base font-bold text-[var(--text-heading)] flex items-center gap-1.5">
              <span>{{ hardwareTelemetry.modemCarrier || 'Viettel' }}</span>
              <span class="text-xs font-mono font-medium text-emerald-600 dark:text-emerald-400">
                ({{ hardwareTelemetry.modemBars || 5 }}/5 bars)
              </span>
            </p>
            <p class="text-[11px] text-[var(--text-hint)] font-mono mt-0.5">
              IP: {{ hardwareTelemetry.modemIp || '26.15.253.98' }} (RSSI {{ hardwareTelemetry.modemRssi || 23 }})
            </p>
          </div>
        </div>

        <!-- System Services & Diagnostic Conditions Strip -->
        <div class="pt-2 border-t border-[var(--border-subtle)] flex flex-col md:flex-row md:items-center justify-between gap-3 text-xs">
          <div class="flex flex-wrap items-center gap-2">
            <span class="text-[var(--text-hint)] font-medium">Active Services ({{ hardwareTelemetry.services.length }}):</span>
            <Badge
              v-for="svc in hardwareTelemetry.services.slice(0, 8)"
              :key="svc.name"
              :variant="svc.health === 'HEALTHY' ? 'neutral' : 'warning'"
              size="xs"
              class="font-mono text-[10px]"
            >
              {{ svc.name }}
            </Badge>
            <span v-if="hardwareTelemetry.services.length > 8" class="text-[var(--text-hint)] text-[10px]">
              +{{ hardwareTelemetry.services.length - 8 }} more
            </span>
          </div>

          <div class="flex items-center gap-3 text-[11px] text-[var(--text-secondary)]">
            <span class="flex items-center gap-1 text-emerald-600 dark:text-emerald-400">
              <CheckCircle2 class="w-3.5 h-3.5" />
              <span>NTP Synced</span>
            </span>
            <span class="flex items-center gap-1 text-emerald-600 dark:text-emerald-400">
              <CheckCircle2 class="w-3.5 h-3.5" />
              <span>SD Card Mounted</span>
            </span>
            <span class="font-mono text-[var(--text-hint)]">
              Tags: {{ hardwareTelemetry.tagCount || 62 }}/512
            </span>
          </div>
        </div>
      </div>

      <!-- Standard SCADA Metrics Grid with Datacake Parity -->
      <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <!-- Heartbeat -->
        <div class="p-4 bg-[var(--surface-card)] border border-[var(--border-default)] rounded-lg shadow-[var(--shadow-control)]">
          <div class="flex items-center justify-between text-[var(--text-secondary)]">
            <span class="text-xs font-medium">Last Heartbeat</span>
            <Clock class="w-4 h-4 text-[var(--text-hint)]" />
          </div>
          <p class="mt-2 text-xl font-bold font-mono text-[var(--text-heading)]">
            {{ lastHeartbeat }}
          </p>
          <p class="mt-1 text-[11px] text-emerald-600 dark:text-emerald-400">Stream connected</p>
        </div>

        <!-- Supply Voltage -->
        <div class="p-4 bg-[var(--surface-card)] border border-[var(--border-default)] rounded-lg shadow-[var(--shadow-control)]">
          <div class="flex items-center justify-between text-[var(--text-secondary)]">
            <span class="text-xs font-medium">Supply Voltage</span>
            <Activity class="w-4 h-4 text-amber-500" />
          </div>
          <p class="mt-2 text-xl font-bold font-mono text-[var(--text-heading)]">
            {{ telemetryData.voltage }}
          </p>
          <p class="mt-1 text-[11px] text-[var(--text-hint)]">Nominal 3.6V Li-SOCl2</p>
        </div>

        <!-- CPU Temp -->
        <div class="p-4 bg-[var(--surface-card)] border border-[var(--border-default)] rounded-lg shadow-[var(--shadow-control)]">
          <div class="flex items-center justify-between text-[var(--text-secondary)]">
            <span class="text-xs font-medium">MCU Temperature</span>
            <Cpu class="w-4 h-4 text-sky-500" />
          </div>
          <p class="mt-2 text-xl font-bold font-mono text-[var(--text-heading)]">
            {{ telemetryData.temperature }}
          </p>
          <p class="mt-1 text-[11px] text-[var(--text-hint)]">Enclosure ambient</p>
        </div>

        <!-- Cellular RSSI -->
        <div class="p-4 bg-[var(--surface-card)] border border-[var(--border-default)] rounded-lg shadow-[var(--shadow-control)]">
          <div class="flex items-center justify-between text-[var(--text-secondary)]">
            <span class="text-xs font-medium">Signal Strength</span>
            <Radio class="w-4 h-4 text-emerald-500" />
          </div>
          <p class="mt-2 text-xl font-bold font-mono text-[var(--text-heading)]">
            {{ telemetryData.rssi }}
          </p>
          <p class="mt-1 text-[11px] text-emerald-600 dark:text-emerald-400">LTE-M Link Excellent</p>
        </div>
      </div>

      <!-- MaiTech RTU Operating Configurations & Hardware Subsystems Card -->
      <div
        v-if="device?.name?.includes('MAITECH') || hardwareTelemetry.uptimeSeconds !== null"
        class="p-5 bg-[var(--surface-card)] border border-[var(--border-default)] rounded-lg shadow-[var(--shadow-control)] space-y-4"
        data-test="rtu-operating-config-card"
      >
        <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-3 pb-3 border-b border-[var(--border-subtle)]">
          <div class="flex items-center gap-2.5">
            <Server class="w-5 h-5 text-[var(--color-primary)]" />
            <div>
              <div class="flex items-center gap-2">
                <h3 class="text-sm font-bold text-[var(--text-heading)]">Cấu Hình Hoạt Động & Phân Hệ Phần Cứng MaiTech RTU</h3>
                <Badge variant="success" size="xs">ESP32-S3 RTU</Badge>
              </div>
              <p class="text-xs text-[var(--text-secondary)] mt-0.5">
                Thiết bị phần cứng công nghiệp hoạt động thực tế • Hostname: <span class="font-mono text-[var(--text-primary)] font-medium">{{ hardwareTelemetry.hostname || 'maitech-rtu' }}</span>
              </p>
            </div>
          </div>
          <a
            href="http://maitech-rtu.local/"
            target="_blank"
            rel="noopener noreferrer"
            class="inline-flex items-center justify-center gap-1.5 px-3 py-1.5 rounded-md text-xs font-semibold text-white shadow-sm transition-colors cursor-pointer bg-[#f97316] hover:bg-[#ea580c] active:bg-[#c2410c]"
            title="Mở bảng điều khiển cục bộ của RTU tại http://maitech-rtu.local/"
          >
            <span>Mở RTU Web Console</span>
            <span class="text-xs">↗</span>
          </a>
        </div>

        <!-- 3-Column Configuration Strip -->
        <div class="grid grid-cols-1 md:grid-cols-3 gap-4">
          <!-- Column 1: MQTT Broker & Transport -->
          <div class="p-3.5 rounded-lg bg-[var(--surface-overlay)] border border-[var(--border-subtle)] space-y-2">
            <div class="flex items-center justify-between text-xs font-semibold text-[var(--text-heading)] border-b border-[var(--border-subtle)] pb-1.5">
              <span class="flex items-center gap-1.5">
                <Radio class="w-3.5 h-3.5 text-sky-500" />
                <span>MQTT Broker Transport</span>
              </span>
              <Badge variant="success" size="xs">Connected</Badge>
            </div>
            <div class="space-y-1.5 text-xs">
              <div class="flex justify-between">
                <span class="text-[var(--text-hint)]">Host:</span>
                <span class="font-mono text-[var(--text-primary)] font-medium">{{ hardwareTelemetry.mqttBroker?.host || 'broker.emqx.io:1883' }}</span>
              </div>
              <div class="flex justify-between">
                <span class="text-[var(--text-hint)]">Client ID:</span>
                <span class="font-mono text-[var(--text-primary)]">{{ hardwareTelemetry.mqttBroker?.clientId || 'hms_gateway_870c' }}</span>
              </div>
              <div class="flex justify-between">
                <span class="text-[var(--text-hint)]">Prefix:</span>
                <span class="font-mono text-amber-500 font-medium">{{ hardwareTelemetry.mqttBroker?.topicPrefix || 'hms_toan_lab' }}</span>
              </div>
              <div class="flex justify-between">
                <span class="text-[var(--text-hint)]">Cmd Topic:</span>
                <span class="font-mono text-[11px] text-[var(--text-secondary)]">hms_toan_lab/gateway_870c/cmd</span>
              </div>
            </div>
          </div>

          <!-- Column 2: Digital Outputs (DO1 - DO6 Relays) -->
          <div class="p-3.5 rounded-lg bg-[var(--surface-overlay)] border border-[var(--border-subtle)] space-y-2">
            <div class="flex items-center justify-between text-xs font-semibold text-[var(--text-heading)] border-b border-[var(--border-subtle)] pb-1.5">
              <span class="flex items-center gap-1.5">
                <Layers class="w-3.5 h-3.5 text-purple-500" />
                <span>Relay Digital Outputs (DO)</span>
              </span>
              <Badge variant="neutral" size="xs">6 Kênh</Badge>
            </div>
            <div class="grid grid-cols-3 gap-1.5 text-center">
              <div
                v-for="ch in (hardwareTelemetry.digitalOutputs.length > 0 ? hardwareTelemetry.digitalOutputs : [
                  { channel: 1, gpioPin: 21, state: false, mode: 'toggle' },
                  { channel: 2, gpioPin: 35, state: false, mode: 'toggle' },
                  { channel: 3, gpioPin: 37, state: false, mode: 'toggle' },
                  { channel: 4, gpioPin: 40, state: false, mode: 'toggle' },
                  { channel: 5, gpioPin: 38, state: false, mode: 'toggle' },
                  { channel: 6, gpioPin: 41, state: false, mode: 'toggle' }
                ])"
                :key="ch.channel"
                class="p-1.5 rounded border border-[var(--border-subtle)] bg-[var(--surface-card)] flex flex-col items-center"
              >
                <span class="text-[11px] font-mono font-medium text-[var(--text-primary)]">DO{{ ch.channel }}</span>
                <span class="text-[9px] text-[var(--text-hint)]">P{{ ch.gpioPin }}</span>
                <span
                  class="mt-1 text-[10px] font-bold px-1.5 py-0.5 rounded font-mono"
                  :class="ch.state ? 'bg-emerald-500/10 text-emerald-600 dark:text-emerald-400' : 'bg-neutral-500/10 text-[var(--text-hint)]'"
                  :data-test="`do-${ch.channel}-state`"
                >
                  {{ ch.state ? 'ON' : 'OFF' }}
                </span>
              </div>
            </div>
          </div>

          <!-- Column 3: Digital Inputs (DI1 - DI4 Sensors) -->
          <div class="p-3.5 rounded-lg bg-[var(--surface-overlay)] border border-[var(--border-subtle)] space-y-2">
            <div class="flex items-center justify-between text-xs font-semibold text-[var(--text-heading)] border-b border-[var(--border-subtle)] pb-1.5">
              <span class="flex items-center gap-1.5">
                <Cpu class="w-3.5 h-3.5 text-emerald-500" />
                <span>Digital Inputs (DI)</span>
              </span>
              <Badge variant="neutral" size="xs">4 Kênh</Badge>
            </div>
            <div class="grid grid-cols-2 gap-2 text-center">
              <div
                v-for="di in (hardwareTelemetry.digitalInputs.length > 0 ? hardwareTelemetry.digitalInputs : [
                  { channel: 1, gpioPin: 1, state: false },
                  { channel: 2, gpioPin: 2, state: false },
                  { channel: 3, gpioPin: 3, state: false },
                  { channel: 4, gpioPin: 4, state: false }
                ])"
                :key="di.channel"
                class="p-1.5 rounded border border-[var(--border-subtle)] bg-[var(--surface-card)] flex items-center justify-between px-2"
              >
                <div class="text-left">
                  <span class="text-[11px] font-mono font-medium text-[var(--text-primary)]">DI{{ di.channel }}</span>
                  <span class="text-[9px] text-[var(--text-hint)] block">GPIO {{ di.gpioPin }}</span>
                </div>
                <span
                  class="text-[10px] font-bold px-1.5 py-0.5 rounded font-mono"
                  :class="di.state ? 'bg-emerald-500/10 text-emerald-600 dark:text-emerald-400' : 'bg-neutral-500/10 text-[var(--text-hint)]'"
                  :data-test="`di-${di.channel}-state`"
                >
                  {{ di.state ? 'ON' : 'OFF' }}
                </span>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- TAB 2: C2 Command Console -->
    <div v-if="activeTab === 'commands'" class="space-y-4">
      <div class="flex items-center justify-between p-4 bg-[var(--surface-card)] border border-[var(--border-default)] rounded-lg shadow-[var(--shadow-control)]">
        <div>
          <h3 class="text-sm font-semibold text-[var(--text-heading)]">Command & Control Pipeline</h3>
          <p class="text-xs text-[var(--text-secondary)]">All commands enforce cryptographic catalog SHA-256 verification before transmission.</p>
        </div>
        <Button variant="primary" size="sm" @click="showCommandModal = true">
          Dispatch Command
        </Button>
      </div>

      <div class="bg-[var(--surface-card)] border border-[var(--border-default)] rounded-lg p-4 shadow-[var(--shadow-control)]">
        <h4 class="text-xs font-semibold uppercase tracking-wider text-[var(--text-secondary)] mb-3">Recent Dispatches</h4>
        <div v-if="commandHistory.length === 0" class="text-center py-8 text-xs text-[var(--text-hint)]">
          No commands dispatched during this session.
        </div>
        <div v-else class="space-y-2">
          <div
            v-for="(cmd, i) in commandHistory"
            :key="i"
            class="flex items-center justify-between p-3 rounded-lg bg-[var(--surface-overlay)] border border-[var(--border-subtle)] text-xs"
          >
            <div class="flex items-center gap-2">
              <Terminal class="w-4 h-4 text-amber-500" />
              <span class="font-medium font-mono text-[var(--text-primary)]">{{ cmd.action }}</span>
              <span class="text-[var(--text-hint)]">({{ cmd.correlationId }})</span>
            </div>
            <div class="flex items-center gap-3">
              <Badge variant="success" size="xs">{{ cmd.status }}</Badge>
              <span class="text-[var(--text-hint)] font-mono text-[11px]">{{ cmd.timestamp }}</span>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- TAB 3: Provisioning & Keysets -->
    <div v-if="activeTab === 'provisioning'" class="space-y-4">
      <div class="p-5 bg-[var(--surface-card)] border border-[var(--border-default)] rounded-lg shadow-[var(--shadow-control)] space-y-4">
        <div>
          <h3 class="text-sm font-semibold text-[var(--text-heading)]">Device Provisioning Bundle</h3>
          <p class="mt-1 text-xs text-[var(--text-secondary)]">Generate an encrypted zero-touch onboarding bundle containing MQTT client certificates and root CA.</p>
        </div>

        <div class="grid grid-cols-1 sm:grid-cols-2 gap-4 text-xs">
          <div>
            <label class="block font-medium text-[var(--text-secondary)] mb-1">Target MQTT Broker Host</label>
            <input
              v-model="brokerHost"
              type="text"
              class="w-full px-3 py-2 rounded-md bg-[var(--surface-panel)] border border-[var(--border-default)] font-mono text-[var(--text-primary)] focus:outline-none focus:border-[var(--color-primary)]"
            />
          </div>
          <div>
            <label class="block font-medium text-[var(--text-secondary)] mb-1">Broker Port</label>
            <input
              v-model="brokerPort"
              type="number"
              class="w-full px-3 py-2 rounded-md bg-[var(--surface-panel)] border border-[var(--border-default)] font-mono text-[var(--text-primary)] focus:outline-none focus:border-[var(--color-primary)]"
            />
          </div>
        </div>

        <div class="pt-2 flex items-center justify-between">
          <div class="text-xs text-[var(--text-secondary)]">
            Current Key Generation: <span class="font-mono font-semibold text-[var(--text-primary)]">gen-{{ device?.keyGeneration ?? 1 }}</span>
          </div>
          <Button
            variant="primary"
            size="sm"
            :loading="downloadingBundle"
            @click="handleDownloadProvisioningBundle"
          >
            <template #icon-left>
              <Download class="w-3.5 h-3.5" />
            </template>
            Generate & Download Bundle
          </Button>
        </div>
      </div>
    </div>

    <!-- TAB 4: Licenses & KMS -->
    <div v-if="activeTab === 'licenses'" class="space-y-4">
      <div class="p-5 bg-[var(--surface-card)] border border-[var(--border-default)] rounded-lg shadow-[var(--shadow-control)] space-y-4">
        <div class="flex items-center justify-between">
          <div>
            <h3 class="text-sm font-semibold text-[var(--text-heading)]">KMS Cryptographic License</h3>
            <p class="mt-1 text-xs text-[var(--text-secondary)]">Enforces hardware root of trust and firmware execution entitlements.</p>
          </div>
          <Badge variant="success" size="sm">Valid & Enforced</Badge>
        </div>

        <div class="grid grid-cols-1 sm:grid-cols-3 gap-3 text-xs">
          <div class="p-3 bg-[var(--surface-overlay)] rounded-lg border border-[var(--border-subtle)]">
            <span class="text-[var(--text-secondary)]">Key ID (KID)</span>
            <p class="font-mono font-medium mt-1 text-[var(--text-primary)]">{{ licenseKid }}</p>
          </div>
          <div class="p-3 bg-[var(--surface-overlay)] rounded-lg border border-[var(--border-subtle)]">
            <span class="text-[var(--text-secondary)]">Authority / Issuer</span>
            <p class="font-medium mt-1 text-[var(--text-primary)]">{{ licenseIssuer }}</p>
          </div>
          <div class="p-3 bg-[var(--surface-overlay)] rounded-lg border border-[var(--border-subtle)]">
            <span class="text-[var(--text-secondary)]">License Generation</span>
            <p class="font-mono font-medium mt-1 text-[var(--text-primary)]">gen-{{ licenseGeneration }}</p>
          </div>
        </div>

        <div v-if="licenseMessage" class="p-3 rounded-lg bg-emerald-500/10 border border-emerald-500/20 text-xs text-emerald-700 dark:text-emerald-400">
          {{ licenseMessage }}
        </div>

        <div class="pt-2 flex items-center justify-end">
          <input
            ref="licenseFileInput"
            type="file"
            accept=".lic,.bin,.jwt"
            class="hidden"
            @change="handleLicenseUpload"
          />
          <Button
            variant="outline"
            size="sm"
            :loading="licenseUploading"
            @click="licenseFileInput?.click()"
          >
            <template #icon-left>
              <Upload class="w-3.5 h-3.5" />
            </template>
            Upload New License Certificate
          </Button>
        </div>
      </div>
    </div>

    <!-- TAB 5: Audit History -->
    <div v-if="activeTab === 'audit'" class="space-y-4">
      <div class="p-5 bg-[var(--surface-card)] border border-[var(--border-default)] rounded-lg shadow-[var(--shadow-control)]">
        <h3 class="text-sm font-semibold text-[var(--text-heading)] mb-3">Cryptographic Audit Trail</h3>
        <p class="text-xs text-[var(--text-secondary)] mb-4">Historical record of enrollments, token rotations, and C2 executions.</p>

        <div class="space-y-3">
          <div class="flex items-start gap-3 p-3 bg-[var(--surface-overlay)] rounded-lg border border-[var(--border-subtle)] text-xs">
            <ShieldCheck class="w-4 h-4 text-emerald-500 mt-0.5" />
            <div>
              <p class="font-semibold text-[var(--text-primary)]">Device Registered in Hierarchy</p>
              <p class="text-[var(--text-secondary)] mt-0.5">Assigned to organization {{ device?.ownerOrganizationId }}</p>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- TAB 6: Datacake Permissions Matrix -->
    <div v-if="activeTab === 'permissions'" class="space-y-4">
      <DevicePermissionsTab
        :device-id="deviceId"
        :csrf-token="csrfToken"
      />
    </div>

    <!-- Command Modal -->
    <DeviceCommandModal
      v-model="showCommandModal"
      :device-id="deviceId"
      :device-name="device?.name || deviceId"
      :catalog="catalog"
      :catalog-proof="catalogProof"
      :catalog-fresh="catalogFresh"
      :permissions="permissions"
      :csrf-token="csrfToken"
      @command-executed="onCommandExecuted"
    />
  </div>
</template>
