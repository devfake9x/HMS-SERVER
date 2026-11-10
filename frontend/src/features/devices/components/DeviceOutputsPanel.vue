<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref, watch } from 'vue'
import {
  adminApi,
  type CommandCatalogSnapshot,
  type DeviceLinkDto,
  type DeviceOutputsDto,
} from '@/core/api/adminApi'
import {
  httpStatusOf,
  mayHaveReachedDevice,
  mayHaveRun,
  runDeviceCommand,
  type DeviceCommandBody,
  type DeviceCommandOutcome,
} from '@/core/commands/useDeviceCommand'
import Button from '@/shared/ui/primitives/Button.vue'
import Badge from '@/shared/ui/primitives/Badge.vue'
import { formatReadTime } from '@/core/time/readAge'
import { outputOwnerNote, outputWriteLock, stateRefusal } from '@/core/commands/outputLocks'
import { Layers, RefreshCw, AlertTriangle } from '@lucide/vue'

/**
 * Relay outputs (DO) of one device: the only place the UI drives them. State comes only from the
 * server's verified control.get_status read; every command that may have reached the device keeps
 * its channel locked until the state has been read again (MQTT spec R4: no reply proves nothing).
 * Controls are locked, with the device's reason, on channels the last read says will refuse a manual write.
 */
const props = defineProps<{
  deviceId: string
  csrfToken: string | null
  link: DeviceLinkDto | null
  catalog: CommandCatalogSnapshot | null
  catalogVerified: boolean
  /** Bumped by the parent when the server announces an outputs change. */
  refreshKey: number
}>()

const DEFAULT_PULSE_MS = 1000
// Server default for Mqtt:MaxPulseMs; the server enforces the configured value.
const MAX_PULSE_MS = 60_000
// Time after a pulse's end before the second read, so the read sees the relay released.
const PULSE_SETTLE_MS = 1000
// Pause before the second read-back attempt, e.g. after the device lane was busy.
const READ_BACK_RETRY_MS = 1500

const outputs = ref<DeviceOutputsDto | null>(null)
const loadState = ref<'loading' | 'ready' | 'not_read' | 'forbidden' | 'error'>('loading')
const readingAll = ref(false)
const readError = ref<string | null>(null)
const channelBusy = reactive<Record<number, string>>({})
const channelError = reactive<Record<number, string>>({})
// The device's own code, shown next to a translated refusal.
const channelErrorCode = reactive<Record<number, string>>({})
const pulseMs = reactive<Record<number, number>>({})

const allowedChannels = computed(() => props.link?.allowedOutputChannels ?? [])
const canView = computed(() => props.link?.canViewOutputs === true)

function available(type: string, action: string): boolean {
  return props.catalog?.trusted === true &&
    props.catalog.actions.some(a => a.type === type && a.action === action && a.available)
}

const canRead = computed(() => canView.value && props.catalogVerified && available('control', 'get_status'))

function canControl(channel: number): boolean {
  return props.link?.canControlOutputs === true && canView.value && props.catalogVerified &&
    allowedChannels.value.includes(channel) && available('control', 'set_output')
}

function canPulse(channel: number, mode: string | null): boolean {
  return mode === 'pulse' && props.link?.canControlOutputs === true && canView.value && props.catalogVerified &&
    allowedChannels.value.includes(channel) && available('control', 'pulse')
}

// A device that stopped reporting, or a server that lost its broker link, would not tell us that
// a relay changed (spec: the LWT is optional), so the last read no longer counts as current.
const linkDown = computed(() => props.link?.online === false || props.link?.brokerConnected === false)
const unknown = computed(() => outputs.value?.stale === true || linkDown.value)

const now = ref(Date.now())
const clock = setInterval(() => { now.value = Date.now() }, 15_000)
onUnmounted(() => clearInterval(clock))

const readAtText = computed(() => outputs.value ? formatReadTime(outputs.value.readAt, now.value) : null)

const STALE_REASONS: Record<string, string> = {
  device_offline: 'thiết bị mất kết nối',
  device_rebooted: 'thiết bị vừa khởi động lại',
  device_reboot_scheduled: 'thiết bị đang khởi động lại',
  output_command_executed: 'vừa có lệnh đổi relay, chưa đọc lại',
  output_command_outcome_unknown: 'một lệnh relay không rõ kết quả',
}

let loadSequence = 0

/** Fetches the verified state. The newest call wins; a failure clears the state (and with it the
 *  controls) rather than leaving an old reading on screen. Resolves true when a state was read. */
async function load(): Promise<boolean> {
  const sequence = ++loadSequence
  try {
    const result = await adminApi.deviceOutputs(props.deviceId)
    if (sequence !== loadSequence) return result.kind === 'ok'
    outputs.value = result.kind === 'ok' ? result.state : null
    loadState.value = result.kind === 'ok' ? 'ready' : result.kind
    return result.kind === 'ok'
  } catch {
    if (sequence === loadSequence) {
      outputs.value = null
      loadState.value = 'error'
    }
    return false
  }
}

function body(action: string, payload: Record<string, unknown>): DeviceCommandBody {
  return {
    type: 'control',
    action,
    payload,
    catalogVersion: props.catalog?.catalogVersion ?? '',
    catalogHash: props.catalog?.catalogHash ?? '',
  }
}

// Always to the device a write was made on, even if the page has moved on since.
async function send(command: DeviceCommandBody, device: string, onDispatched?: () => void): Promise<DeviceCommandOutcome> {
  return runDeviceCommand(device, command, {
    csrfToken: props.csrfToken ?? undefined,
    onDispatched: sent => { if (mayHaveReachedDevice(sent)) onDispatched?.() },
  })
}

const DEVICE_CHANGED = 'DEVICE_CHANGED'

/** One get_status; the server projects the verified reply, then the state is fetched. Null when
 *  the state was read back, otherwise why not. */
async function readBack(device: string): Promise<string | null> {
  try {
    const outcome = await send(body('get_status', {}), device)
    if (props.deviceId !== device) return DEVICE_CHANGED
    if (outcome.phase === 'done' && outcome.final?.result?.status === 'Executed')
      return (await load()) ? null : 'OUTPUTS_NOT_LOADED'
    if (outcome.phase === 'not_sent') return outcome.dispatch.code
    if (outcome.phase === 'timeout') return 'CLIENT_TIMEOUT'
    return outcome.final?.result?.code ?? 'UNKNOWN'
  } catch (error) {
    const status = httpStatusOf(error)
    return status !== null ? `HTTP_${status}` : 'NETWORK_ERROR'
  }
}

/** Two attempts, with a pause between them: a busy device lane frees up in a moment. */
async function readBackTwice(device: string): Promise<string | null> {
  const first = await readBack(device)
  if (first === null || first === DEVICE_CHANGED) return first
  await new Promise(resolve => setTimeout(resolve, READ_BACK_RETRY_MS))
  return props.deviceId === device ? readBack(device) : DEVICE_CHANGED
}

function readFailureText(code: string): string {
  return code === 'CATALOG_STALE'
    ? 'Catalog lệnh trên server đã đổi, hãy tải lại trang'
    : `Chưa đọc lại được trạng thái (${code}), hãy bấm Đọc trạng thái`
}

/** The text for a failed outcome; `code` is the device's own code when `text` is a translation of it. */
function describe(outcome: DeviceCommandOutcome): { text: string; code: string | null } | null {
  if (outcome.phase === 'timeout') return { text: 'Không có kết quả từ thiết bị trong 40 s', code: null }
  const final = outcome.final?.result
  if (final && final.status !== 'Executed' && final.status !== 'Acknowledged') {
    const raw = final.detail ? `${final.code}: ${final.detail}` : final.code
    const refusal = final.code === 'DEVICE_REJECTED' ? stateRefusal(final.detail, outcome.final?.reply?.data) : null
    return refusal ? { text: refusal, code: raw } : { text: raw, code: null }
  }
  return null
}

/** `stopped`: nothing to follow up (refused before sending, or the page moved on). `unchanged`: the
 *  device refused because of the channel's state, so the relay did not change. `sent`: it may have run. */
type WriteEnd = 'stopped' | 'unchanged' | 'sent'

/** Sends a write. Only a refusal that proves nothing was published unlocks the channel at once;
 *  anything else (device outcome, timeout, server error, lost connection) keeps it locked and
 *  reads the state back (two attempts) first. */
async function operate(channel: number, command: DeviceCommandBody): Promise<WriteEnd> {
  const device = props.deviceId
  channelBusy[channel] = 'Đang gửi…'
  delete channelError[channel]
  delete channelErrorCode[channel]
  let problem: string | null
  let deviceCode: string | null = null
  try {
    const outcome = await send(command, device, () => {
      if (props.deviceId === device) channelBusy[channel] = 'Chờ thiết bị…'
    })
    // The page moved to another device: its channels were reset; nothing here applies to them.
    if (props.deviceId !== device) return 'stopped'
    if (outcome.phase === 'not_sent') {
      const sent = outcome.dispatch
      problem = sent.code === 'CATALOG_STALE'
        ? 'Catalog lệnh trên server đã đổi, hãy tải lại trang'
        : sent.detail ? `${sent.code}: ${sent.detail}` : sent.code
      if (!mayHaveRun(outcome)) {
        channelError[channel] = problem
        delete channelBusy[channel]
        return 'stopped'
      }
    } else {
      const failure = describe(outcome)
      problem = failure?.text ?? null
      deviceCode = failure?.code ?? null
    }
  } catch (error) {
    if (props.deviceId !== device) return 'stopped'
    const status = httpStatusOf(error)
    if (!mayHaveRun(error)) {
      // The request was refused at the API boundary, before any command was built.
      channelError[channel] = `HTTP ${status}: yêu cầu bị từ chối, lệnh chưa được gửi`
      delete channelBusy[channel]
      return 'stopped'
    }
    // A server error or a lost connection can come after the publish.
    problem = status !== null
      ? `HTTP ${status}: không rõ lệnh đã tới thiết bị chưa`
      : 'Mất kết nối tới server: không rõ lệnh đã tới thiết bị chưa'
  }

  if (problem) channelError[channel] = problem
  if (deviceCode) channelErrorCode[channel] = deviceCode
  channelBusy[channel] = 'Đang đọc lại…'
  const failure = await readBackTwice(device)
  if (failure === DEVICE_CHANGED) return 'stopped'
  if (failure !== null)
    channelError[channel] = `${problem ? `${problem}. ` : ''}${readFailureText(failure)}`
  delete channelBusy[channel]
  return deviceCode ? 'unchanged' : 'sent'
}

async function setOutput(channel: number, value: boolean) {
  await operate(channel, body('set_output', { channel, value }))
}

async function pulse(channel: number) {
  const typed = pulseMs[channel] as number | string | undefined
  const duration = typed === undefined || typed === '' ? DEFAULT_PULSE_MS : Number(typed)
  if (!Number.isInteger(duration) || duration < 1 || duration > MAX_PULSE_MS) {
    channelError[channel] = `Thời gian xung phải là số nguyên từ 1 đến ${MAX_PULSE_MS} ms`
    delete channelErrorCode[channel]
    return
  }
  const device = props.deviceId
  // A pulse refused for the channel's state never drove the relay, so there is no pulse end to wait for.
  if ((await operate(channel, body('pulse', { channel, duration_ms: duration }))) !== 'sent') return
  // The first read lands inside the pulse; read again once the relay has dropped.
  channelBusy[channel] = 'Chờ hết xung…'
  await new Promise(resolve => setTimeout(resolve, duration + PULSE_SETTLE_MS))
  if (props.deviceId !== device) return
  await readBack(device)
  delete channelBusy[channel]
}

async function readAll() {
  readingAll.value = true
  readError.value = null
  try {
    const failure = await readBack(props.deviceId)
    if (failure !== null && failure !== DEVICE_CHANGED) readError.value = readFailureText(failure)
  } finally {
    readingAll.value = false
  }
}

function stateLabel(on: boolean): string {
  return unknown.value ? 'Không rõ' : on ? 'ON' : 'OFF'
}

onMounted(load)
watch(() => props.refreshKey, load)
// Another device: nothing of the previous one (locks, errors, state) may show under it.
watch(() => props.deviceId, () => {
  for (const key of Object.keys(channelBusy)) delete channelBusy[Number(key)]
  for (const key of Object.keys(channelError)) delete channelError[Number(key)]
  for (const key of Object.keys(channelErrorCode)) delete channelErrorCode[Number(key)]
  readError.value = null
  outputs.value = null
  void load()
})
</script>

<template>
  <div class="p-3.5 rounded-lg bg-[var(--surface-overlay)] border border-[var(--border-subtle)] space-y-2" data-test="do-panel">
    <div class="flex flex-wrap items-center justify-between gap-x-2 gap-y-1 text-xs font-semibold text-[var(--text-heading)] border-b border-[var(--border-subtle)] pb-1.5">
      <span class="flex items-center gap-1.5 whitespace-nowrap">
        <Layers class="w-3.5 h-3.5 text-purple-500" />
        <span>Relay (DO)</span>
        <Badge v-if="outputs" variant="neutral" size="xs">{{ outputs.channels.length }} kênh</Badge>
      </span>
      <Button
        v-if="canRead"
        variant="ghost"
        size="xs"
        class="whitespace-nowrap"
        :loading="readingAll"
        data-test="do-read-status"
        @click="readAll"
      >
        <RefreshCw class="w-3 h-3" />
        <span class="ml-1">Đọc trạng thái</span>
      </Button>
    </div>

    <p v-if="loadState === 'forbidden'" class="text-[11px] text-[var(--text-hint)]">
      Không có quyền xem DO (cần gpio.do_view trên thiết bị này).
    </p>
    <p v-else-if="loadState === 'not_read'" class="text-[11px] text-[var(--text-hint)]" data-test="do-not-read">
      Chưa đọc trạng thái relay từ thiết bị.
    </p>
    <p v-else-if="loadState === 'error'" class="text-[11px] text-rose-500">Không tải được trạng thái DO.</p>
    <p v-if="readError" class="text-[11px] text-rose-500" data-test="do-read-error">{{ readError }}</p>

    <template v-if="outputs">
      <p class="text-[10px] text-[var(--text-hint)]">Đọc lúc {{ readAtText }}</p>
      <p
        v-if="linkDown"
        class="flex items-center gap-1 text-[11px] text-amber-600 dark:text-amber-400"
        data-test="do-offline"
      >
        <AlertTriangle class="w-3 h-3" />
        {{ link?.brokerConnected === false ? 'Server mất kết nối broker' : 'Thiết bị đang offline' }}: relay có thể đã khác lần đọc này.
      </p>
      <p
        v-if="outputs.stale"
        class="flex items-center gap-1 text-[11px] text-amber-600 dark:text-amber-400"
        data-test="do-stale"
      >
        <AlertTriangle class="w-3 h-3" />
        Trạng thái có thể đã cũ: {{ STALE_REASONS[outputs.staleReason ?? ''] ?? outputs.staleReason }}
      </p>

      <div class="grid grid-cols-3 gap-1.5 text-center">
        <div
          v-for="ch in outputs.channels"
          :key="ch.channel"
          class="p-1.5 rounded border bg-[var(--surface-card)] flex flex-col items-center gap-1 min-w-0"
          :class="channelError[ch.channel] ? 'border-rose-400' : 'border-[var(--border-subtle)]'"
        >
          <span class="text-[11px] font-mono font-medium text-[var(--text-primary)]">DO{{ ch.channel }}</span>
          <span class="text-[9px] text-[var(--text-hint)]">{{ ch.mode ?? '—' }}{{ ch.pulseActive ? ' · đang xung' : '' }}</span>
          <span v-if="outputWriteLock(ch)" class="text-[9px] text-amber-600 dark:text-amber-400" :data-test="`do-${ch.channel}-lock`">
            {{ outputWriteLock(ch) }}
          </span>
          <span v-else-if="outputOwnerNote(ch)" class="text-[9px] text-[var(--text-hint)]" :data-test="`do-${ch.channel}-owner-note`">
            {{ outputOwnerNote(ch) }}
          </span>
          <span
            class="text-[10px] font-bold px-1.5 py-0.5 rounded font-mono"
            :class="!unknown && ch.state ? 'bg-emerald-500/10 text-emerald-600 dark:text-emerald-400' : 'bg-neutral-500/10 text-[var(--text-hint)]'"
            :data-test="`do-${ch.channel}-state`"
          >{{ stateLabel(ch.state) }}</span>

          <span v-if="channelBusy[ch.channel]" class="text-[9px] text-sky-600" :data-test="`do-${ch.channel}-busy`">
            {{ channelBusy[ch.channel] }}
          </span>

          <template v-if="canControl(ch.channel)">
            <div v-if="unknown" class="flex gap-1">
              <Button variant="primary" size="xs" :disabled="!!channelBusy[ch.channel] || !!outputWriteLock(ch)" :data-test="`do-${ch.channel}-on`" @click="setOutput(ch.channel, true)">Bật</Button>
              <Button variant="danger" size="xs" :disabled="!!channelBusy[ch.channel] || !!outputWriteLock(ch)" :data-test="`do-${ch.channel}-off`" @click="setOutput(ch.channel, false)">Tắt</Button>
            </div>
            <Button
              v-else
              :variant="ch.state ? 'danger' : 'primary'"
              size="xs"
              :disabled="!!channelBusy[ch.channel] || !!outputWriteLock(ch)"
              :data-test="`do-${ch.channel}-toggle`"
              @click="setOutput(ch.channel, !ch.state)"
            >{{ ch.state ? 'Tắt' : 'Bật' }}</Button>
          </template>

          <div v-if="canPulse(ch.channel, ch.mode)" class="flex flex-col items-stretch gap-1 w-full">
            <input
              v-model.number="pulseMs[ch.channel]"
              type="number"
              min="1"
              :max="MAX_PULSE_MS"
              :placeholder="`${DEFAULT_PULSE_MS} ms`"
              :aria-label="`Thời gian xung DO${ch.channel} (ms)`"
              :disabled="!!outputWriteLock(ch)"
              class="w-full min-w-0 px-1 py-0.5 text-[10px] font-mono rounded border border-[var(--border-default)] bg-[var(--surface-card)]"
              :data-test="`do-${ch.channel}-pulse-ms`"
            />
            <Button variant="secondary" size="xs" :disabled="!!channelBusy[ch.channel] || !!outputWriteLock(ch)" :data-test="`do-${ch.channel}-pulse`" @click="pulse(ch.channel)">Xung</Button>
          </div>
        </div>
      </div>
    </template>

    <!-- Outside the state block: an error must stay visible when the state could not be reloaded. -->
    <ul v-if="Object.keys(channelError).length" class="space-y-1 text-[10px] text-rose-500 break-words">
      <li v-for="(message, ch) in channelError" :key="ch" :data-test="`do-${ch}-error`">
        <span class="font-mono font-semibold">DO{{ ch }}:</span> {{ message }}
        <span v-if="channelErrorCode[Number(ch)]" class="font-mono" :data-test="`do-${ch}-error-code`">({{ channelErrorCode[Number(ch)] }})</span>
      </li>
    </ul>
  </div>
</template>
