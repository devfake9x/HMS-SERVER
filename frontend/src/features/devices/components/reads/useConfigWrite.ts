import { ref, watch } from 'vue'
import type { CommandCatalogSnapshot } from '@/core/api/adminApi'
import { stepKey, type ReadState, type ReadStep } from '@/core/commands/useDeviceReads'
import { useDeviceWrite, type WriteOutcome } from '@/core/commands/useDeviceWrite'
import { FRESH_READ_MS } from './configGate'
import type { RefreshReads } from './readCard'

/** How far the device clock may drift from the browser's before the operator is told. */
export const CLOCK_SKEW_WARN_S = 30

/**
 * What the re-read said about a write. `match`: the device holds the new value. `mismatch`: it
 * holds something else (`text` says what). `pending_apply`: the device said it saved the value
 * without applying it, so the old value is expected. `unavailable`: the re-read gave nothing usable.
 */
export interface Verification {
  status: 'match' | 'mismatch' | 'pending_apply' | 'unavailable'
  text: string
}

/** One write of the config cards: what to send, what to confirm, how to check it afterwards. */
export interface WriteSpec {
  command: { type: string; action: string; payload: Record<string, unknown> }
  confirm: { title: string; from: string; to: string }
  rereadSteps: ReadStep[]
  /** Compares the states re-read after the write (only those read after it ended) with the intended value. */
  verify: (states: Record<string, ReadState>, outcome: WriteOutcome) => Verification
  /** Extra lines taken from the write's own reply data. */
  notes?: (outcome: WriteOutcome) => string[]
  /** Check the device clock against the browser's after the re-read of system.get_time. */
  checkClock?: boolean
}

export interface WriteResult {
  tone: 'ok' | 'warn' | 'error'
  lines: string[]
}

function notSentText(outcome: WriteOutcome): string {
  switch (outcome.code) {
    case 'DEVICE_WRITE_BUSY': return 'Thiết bị đang xử lý một lệnh ghi khác, thử lại sau'
    case 'DEVICE_WRITE_RATE_LIMITED': return 'Đã ghi quá nhiều lần trong một phút, thử lại sau'
    case 'COMMAND_PAYLOAD_INVALID': return 'Giá trị không hợp lệ'
    case 'CATALOG_STALE': return 'Catalog lệnh trên server đã đổi, hãy tải lại trang'
    default:
      return outcome.code.startsWith('HTTP_4') ? 'Yêu cầu bị từ chối' : `Chưa gửi được lệnh (${outcome.code})`
  }
}

function readBackLine(verification: Verification | null): string {
  if (!verification || verification.status === 'unavailable') return 'Không đọc lại được trạng thái thiết bị'
  return `Giá trị đọc lại: ${verification.text}`
}

function deviceError(outcome: WriteOutcome): string | null {
  const data = outcome.data
  const error = data && typeof data === 'object' ? (data as Record<string, unknown>).error : null
  return typeof error === 'string' && error !== '' ? error : null
}

export function buildResult(outcome: WriteOutcome, verification: Verification | null, notes: string[]): WriteResult {
  const detail = outcome.detail ? ` (${outcome.detail})` : ''
  switch (outcome.kind) {
    case 'not_sent':
      return { tone: 'error', lines: [notSentText(outcome)] }
    case 'executed':
      if (verification?.status === 'match') return { tone: 'ok', lines: ['Đã ghi và xác nhận', verification.text, ...notes] }
      if (verification?.status === 'pending_apply')
        return { tone: 'warn', lines: ['Đã lưu, chưa áp dụng', readBackLine(verification), ...notes] }
      if (verification?.status === 'mismatch')
        return { tone: 'warn', lines: [`Đã ghi nhưng đọc lại khác: ${verification.text}`, ...notes] }
      return { tone: 'warn', lines: ['Đã ghi nhưng chưa đọc lại được để xác nhận', ...notes] }
    case 'rejected':
      return {
        tone: 'error',
        lines: [`Thiết bị từ chối: ${outcome.code}${detail}`, ...[deviceError(outcome)].filter((x): x is string => x !== null), readBackLine(verification)],
      }
    case 'job_failed':
      return { tone: 'error', lines: [`Tác vụ trên thiết bị thất bại: ${outcome.code}${detail}`, readBackLine(verification)] }
    default:
      return {
        tone: 'warn',
        lines: [outcome.code === 'DEVICE_PERMISSION_REQUIRED' ? 'Thiếu quyền' : 'Chưa rõ kết quả; đã đọc lại trạng thái', readBackLine(verification)],
      }
  }
}

function clockWarning(states: Record<string, ReadState>): string | null {
  const time = states['system.get_time']
  const epoch = time?.kind === 'ok' && time.data && typeof time.data === 'object' ? (time.data as Record<string, unknown>).epoch : null
  if (typeof epoch !== 'number') return null
  const skew = Math.round(Math.abs(epoch * 1000 - Date.now()) / 1000)
  return skew > CLOCK_SKEW_WARN_S ? `Đồng hồ thiết bị lệch ${skew} s; lệnh sau có thể bị từ chối` : null
}

/** The reading a write was built from: the old value shown in the confirm, and for an ADC rename
 *  the type and enabled state sent back. */
export interface WriteSource {
  key: string
  readAt: string | null
}

/**
 * Why a confirmed write must not go, judged at the moment of the confirm: the reading it was built
 * from was replaced (someone may have changed the value), turned older than a minute, or predates
 * a config change the page saw from someone else. Null when it may go.
 */
export function staleReason(
  source: WriteSource,
  states: Record<string, ReadState>,
  configChangedAt: number | null | undefined,
  now: number,
): string | null {
  const state = states[source.key]
  if (state?.kind !== 'ok' || (state.readAt ?? null) !== source.readAt)
    return 'Dữ liệu vừa được đọc lại; hãy xem giá trị mới rồi sửa lại'
  const readAt = Date.parse(state.readAt ?? '')
  if (!Number.isFinite(readAt) || now - readAt > FRESH_READ_MS) return 'Dữ liệu đã cũ hơn 60 s; hãy đọc lại trước khi sửa'
  if (configChangedAt != null && readAt < configChangedAt) return 'Cấu hình vừa đổi; hãy đọc lại trước khi sửa'
  return null
}

/**
 * The flow every config write follows: confirm old to new, send once, wait for a job the device
 * accepted, read the state back with the card's own reads, then show what the re-read says. The
 * write is never sent again; only `not_sent` skips the re-read. State is dropped when the page
 * moves to another device.
 */
export function useConfigWrite(deps: {
  deviceId: () => string
  context: () => { csrfToken: string | null; catalog: CommandCatalogSnapshot | null }
  refresh: () => RefreshReads
  lockReads: () => (on: boolean) => void
  states: () => Record<string, ReadState>
  configChangedAt: () => number | null | undefined
}) {
  const write = useDeviceWrite(deps.deviceId, deps.context)
  const active = ref(false)
  const pending = ref<WriteSpec | null>(null)
  const result = ref<WriteResult | null>(null)
  const clock = ref<string | null>(null)
  let source: WriteSource | null = null

  function reset() {
    write.cancel()
    active.value = false
    pending.value = null
    result.value = null
    clock.value = null
    source = null
  }
  watch(deps.deviceId, reset)

  function request(spec: WriteSpec) {
    if (active.value) return
    result.value = null
    clock.value = null
    pending.value = spec
    // The first re-read step is the reading the control edits from.
    const key = stepKey(spec.rereadSteps[0])
    source = { key, readAt: deps.states()[key]?.readAt ?? null }
  }

  function dismiss() {
    pending.value = null
  }

  async function confirm() {
    const spec = pending.value
    if (!spec || active.value) return
    pending.value = null
    const stale = source ? staleReason(source, deps.states(), deps.configChangedAt(), Date.now()) : 'Đọc lại trước khi sửa'
    if (stale) {
      result.value = { tone: 'error', lines: [stale] }
      return
    }
    const device = deps.deviceId()
    active.value = true
    deps.lockReads()(true)
    try {
      const outcome = await write.run(spec.command)
      if (outcome === null || deps.deviceId() !== device) return

      let fresh: Record<string, ReadState> | null = null
      if (outcome.kind !== 'not_sent') {
        const endedAt = Date.now()
        const reread = await deps.refresh()(spec.rereadSteps)
        if (deps.deviceId() !== device) return
        // Only a reading taken after the write ended says anything about its effect.
        fresh = Object.fromEntries(Object.entries(reread).map(([key, state]) =>
          [key, state.kind === 'ok' && Date.parse(state.readAt ?? '') >= endedAt ? state : { kind: 'idle' as const }]))
      }
      const verification = fresh ? spec.verify(fresh, outcome) : null
      result.value = buildResult(outcome, verification, spec.notes?.(outcome) ?? [])
      clock.value = fresh && spec.checkClock ? clockWarning(fresh) : null
    } finally {
      if (deps.deviceId() === device) {
        active.value = false
        deps.lockReads()(false)
      }
    }
  }

  return { active, pending, result, clock, request, dismiss, confirm }
}
