<script setup lang="ts">
import { computed, onScopeDispose, ref, watch } from 'vue'
import { Power } from '@lucide/vue'
import Button from '@/shared/ui/primitives/Button.vue'
import DangerConfirmDialog from '@/shared/ui/feedback/DangerConfirmDialog.vue'
import { infoOf, readWithRetry, type ReadResult, type useDeviceReboot } from '@/core/commands/useDeviceReboot'
import type { ReadCardContext } from '../reads/readCard'
import RebootConfirmBody from './RebootConfirmBody.vue'
import RebootResult from './RebootResult.vue'
import { channelsOf, rebootGate, rebootPhrase, rebootResultText, relaysBeforeReboot, relaysMatch, type RelayChannel, type RelaysBefore } from '@/core/commands/rebootPlan'
import { adminApi } from '@/core/api/adminApi'

// `reboot` is the page's run, not the panel's: it goes on when the panel is unmounted (another tab)
// and is found again, with its phase and result, when the panel comes back.
const props = defineProps<ReadCardContext & {
  reboot: ReturnType<typeof useDeviceReboot>
  commandEvent: { correlationId: string } | null
  outputsKey: number
}>()
const emit = defineEmits<{ 'refresh-link': [] }>()

const reboot = props.reboot

/** The relay list and uptime shown in the dialog must be this fresh when the operator confirms. */
const MAX_READ_AGE_MS = 30_000


interface Preflight { uptime: number | null; plan: RelaysBefore; channels: RelayChannel[] | null; readAt: number }
const preflight = ref<Preflight | null>(null)
const reading = ref(false)
const dialogOpen = ref(false)
const notice = ref('')
let readController: AbortController | null = null

const active = computed(() => reboot.phase.value !== 'idle' && reboot.phase.value !== 'done')
const gateInput = () => ({ allowedCommands: props.allowedCommands, catalogVerified: props.catalogVerified, link: props.link })
const gate = computed(() => rebootGate({ ...gateInput(), running: active.value }))

const STATUS: Record<string, string> = {
  dry_run: 'Đang chạy thử…', sending: 'Đang gửi lệnh…', tracking: 'Đang khởi động lại…', reading: 'Đang đọc lại thiết bị…',
}
const outcome = reboot.outcome
const statusText = computed(() => {
  if (reading.value) return 'Đang đọc trạng thái thiết bị…'
  const done = outcome.value
  if (reboot.phase.value === 'done' && done) return rebootResultText(done.kind, done.code, done.detail, done.verified)
  return STATUS[reboot.phase.value] ?? ''
})
const statusTone = computed(() => {
  const done = outcome.value
  if (reboot.phase.value !== 'done' || !done) return 'text-sky-600 dark:text-sky-400'
  if (done.kind === 'observed' && done.verified && !done.check?.deviations.length) return 'text-emerald-600 dark:text-emerald-400'
  return done.kind === 'observed' || done.kind === 'relay_unchecked' || done.kind === 'tracking_timeout' || done.kind === 'not_observed'
    ? 'text-amber-600 dark:text-amber-400'
    : 'text-rose-600 dark:text-rose-400'
})

const readOf = (read: ReadResult | null) => (read?.ok ? read.data : undefined)

/** Reads the signed uptime and the relay table, then opens the dialog; the reboot itself is not sent here. */
async function prepare() {
  readController?.abort()
  const controller = readController = new AbortController()
  const device = props.deviceId
  // A new attempt starts clean: the last attempt's result and notice are not this attempt's.
  if (!active.value) reboot.cancel()
  notice.value = ''
  reading.value = true
  try {
    const context = { csrfToken: props.csrfToken, catalog: props.catalog, signal: controller.signal }
    const can = (key: string) => props.allowedCommands?.includes(key) === true
    const info = can('system.get_info') ? await readWithRetry(device, { type: 'system', action: 'get_info' }, context) : null
    const status = can('control.get_status') ? await readWithRetry(device, { type: 'control', action: 'get_status' }, context) : null
    if (controller.signal.aborted || props.deviceId !== device) return
    preflight.value = { uptime: infoOf(info)?.uptimeSeconds ?? null, plan: relaysBeforeReboot(readOf(status)), channels: channelsOf(readOf(status)), readAt: Date.now() }
    dialogOpen.value = true
  } finally {
    if (readController === controller) { readController = null; reading.value = false }
  }
}

/** Runs when the operator confirms: the gate and the age of the readings are checked again, not trusted from when the dialog opened. */
async function stillValid(): Promise<boolean> {
  // A relay comparison started by an outputs event may be about to close this dialog: let it finish.
  await pendingCheck
  if (!dialogOpen.value) return false
  const now = rebootGate({ ...gateInput(), running: false })
  if (!now.enabled) {
    dialogOpen.value = false
    notice.value = `Không gửi lệnh: ${now.reason ?? 'thiết bị không còn đủ điều kiện'}`
    return false
  }
  if (!preflight.value || Date.now() - preflight.value.readAt > MAX_READ_AGE_MS) {
    dialogOpen.value = false
    void prepare()
    return false
  }
  return true
}

async function start() {
  const before = preflight.value
  dialogOpen.value = false
  if (!before) return
  const result = await reboot.run({
    confirm: rebootPhrase(props.link),
    uptimeBefore: before.uptime,
    relaysBefore: before.plan.readable ? before.plan : null,
  })
  if (result && (result.kind === 'observed' || result.kind === 'relay_unchecked')) emit('refresh-link')
}

const CHANGED_NOTICE = 'Trạng thái relay vừa đổi; mở lại để xem danh sách mới'
const UNTRUSTED_NOTICE = 'Danh sách relay không còn đáng tin (trạng thái cũ hoặc không so sánh được); mở lại để đọc lại'
let outputsCheck = 0
let pendingCheck: Promise<void> | null = null

// An outputs event only says "look again": the dialog closes when the verified state differs from
// the table it shows, is marked stale, or cannot be fetched; a read that changed nothing keeps it open.
async function compareOutputs() {
  const check = ++outputsCheck
  const shown = preflight.value?.channels ?? null
  let reason: string | null = null
  try {
    const result = await adminApi.deviceOutputs(props.deviceId)
    if (result.kind !== 'ok' || result.state.stale) reason = UNTRUSTED_NOTICE
    else if (shown && !relaysMatch(shown, result.state.channels)) reason = CHANGED_NOTICE
  } catch {
    reason = UNTRUSTED_NOTICE
  }
  if (check !== outputsCheck || !dialogOpen.value || !reason) return
  dialogOpen.value = false
  notice.value = reason
}

watch(() => props.outputsKey, () => {
  if (!dialogOpen.value) return
  const current = pendingCheck = compareOutputs().finally(() => { if (pendingCheck === current) pendingCheck = null })
})
watch(() => props.commandEvent, event => { if (event) reboot.nudge(event.correlationId) })
// Reads before the dialog belong to this panel: leaving the tab stops them (the run itself goes on).
onScopeDispose(() => {
  readController?.abort()
  readController = null
})

watch(() => props.deviceId, () => {
  readController?.abort()
  readController = null
  reading.value = false
  dialogOpen.value = false
  preflight.value = null
  notice.value = ''
})
</script>

<template>
  <div v-if="gate.visible" class="pt-2 border-t border-[var(--border-subtle)] space-y-1.5" data-test="reboot-panel">
    <div class="flex items-center justify-between gap-2">
      <span class="text-[11px] text-[var(--text-hint)]">Khởi động lại thiết bị</span>
      <Button variant="danger" size="xs" :disabled="!gate.enabled || reading" data-test="reboot-btn" @click="prepare">
        <Power class="w-3 h-3" />
        <span class="ml-1">Khởi động lại</span>
      </Button>
    </div>
    <p v-if="gate.reason && !active" class="text-[11px] text-[var(--text-hint)]" data-test="reboot-lock-reason">{{ gate.reason }}</p>
    <p v-if="notice" class="text-[11px] text-amber-600 dark:text-amber-400" data-test="reboot-notice">{{ notice }}</p>
    <p v-if="statusText" class="text-[11px] font-semibold" :class="statusTone" aria-live="polite" data-test="reboot-status">{{ statusText }}</p>
    <RebootResult v-if="reboot.phase.value === 'done' && outcome" :outcome="outcome" />

    <DangerConfirmDialog
      v-model="dialogOpen"
      title="Khởi động lại thiết bị"
      message="Thiết bị sẽ khởi động lại ngay sau lệnh này. Relay sẽ đổi như sau:"
      danger-action-text="Khởi động lại"
      cancel-text="Huỷ"
      prompt-text="Gõ"
      prompt-suffix="để xác nhận:"
      input-placeholder="Nhập mã xác nhận"
      :confirm-phrase="rebootPhrase(link)"
      :before-confirm="stillValid"
      @confirm="start"
    >
      <RebootConfirmBody v-if="preflight" :plan="preflight.plan" :read-at="preflight.readAt" :uptime-known="preflight.uptime !== null" />
    </DangerConfirmDialog>
  </div>
</template>
