import { onScopeDispose, ref, watch } from 'vue'
import { adminApi, type CommandCatalogSnapshot } from '@/core/api/adminApi'
import {
  channelsOf,
  checkAfterReboot,
  isVerifiedReboot,
  type RebootKind,
  type RelayChannel,
  type RelayCheck,
  type RelaysBefore,
} from './rebootPlan'
export type { RebootKind }
import { httpStatusOf, newCorrelationId, runDeviceCommand } from './useDeviceCommand'
import { runDeviceWrite } from './useDeviceWrite'

/** Steps of one reboot, for the status line: the dry run, the real send, the wait, the read-back. */
export type RebootPhase = 'dry_run' | 'sending' | 'tracking' | 'reading'

export interface RebootOutcome {
  kind: RebootKind
  code: string
  detail: string | null
  /** The signed uptime of system.get_info after the reboot is smaller than the one before. */
  verified: boolean
  uptimeBefore: number | null
  info: { uptimeSeconds: number | null; firmware: string | null } | null
  outputs: RelayChannel[] | null
  check: RelayCheck | null
  time: { ntpSynced: boolean | null; skewSeconds: number | null } | null
}

export interface RunRebootOptions {
  /** `REBOOT-<mqttDeviceId>`, typed by the operator. */
  confirm: string
  /** Uptime from the signed system.get_info read before the dialog; null when it could not be read. */
  uptimeBefore: number | null
  relaysBefore: RelaysBefore | null
  catalog: CommandCatalogSnapshot | null
  allowedCommands: string[] | null
  csrfToken: string | null
  signal: AbortSignal
  onPhase?: (phase: RebootPhase) => void
  /** Called with the correlation id of the real command before it is sent. */
  onRealCommand?: (correlationId: string) => void
  /** Pause between polls of the server record; a caller can cut it short. */
  wait?: (ms: number, signal: AbortSignal) => Promise<void>
}

export const TRACK_POLL_MS = 2000
export const TRACK_WINDOW_MS = 180_000
export const READ_WINDOW_MS = 30_000
export const READ_RETRY_MS = 2000
const READ_RETRY_MIN_MS = 500
const READ_RETRY_MAX_MS = 5000

const OBSERVED = 'DEVICE_REBOOT_OBSERVED'
const END_CODES: Record<string, RebootKind> = {
  DEVICE_REBOOT_NOT_OBSERVED: 'not_observed',
  DEVICE_REBOOT_OUTCOME_UNKNOWN: 'outcome_unknown',
  DEVICE_EXECUTED: 'untracked',
}
// The server still holds the reboot gate and may move the row on to OBSERVED.
const STILL_OPEN = new Set([
  'DEVICE_REBOOT_SCHEDULED', 'MQTT_PUBLISHED', 'DEVICE_RESPONSE_TIMEOUT', 'MQTT_PUBLISH_UNCONFIRMED', 'DEVICE_ACCEPTED',
  'CLIENT_TIMEOUT', 'NETWORK_ERROR',
])
const isOpen = (code: string) => STILL_OPEN.has(code) || code.startsWith('HTTP_5')
// The server answers these with HTTP 200 and Failed, before anything is signed or sent.
const REFUSED_BEFORE_SIGNING = new Set([
  'HIGH_RISK_REQUIRES_MASTER_ADMIN', 'CONFIRM_REQUIRED', 'DEVICE_PERMISSION_REQUIRED',
])

// A device refusal reads `error[: data.error]`: admission errors stand alone (LANE_FULL, UNAUTHORIZED),
// handler errors keep the esp_err_t name first (ESP_ERR_INVALID_STATE: busy).
const BUSY_DETAIL = /(^|:\s*)(LANE_FULL|busy|UNAUTHORIZED)\b/i

const NOTHING_BEFORE: RelaysBefore = { turnOff: [], forcedOff: [], forcedOn: [], readable: false }

function sleep(ms: number, signal: AbortSignal): Promise<void> {
  return new Promise(resolve => {
    const timer = setTimeout(resolve, ms)
    signal.addEventListener('abort', () => { clearTimeout(timer); resolve() }, { once: true })
  })
}

export type ReadResult = { ok: true; data: unknown } | { ok: false; code: string; detail: string | null }

const asRecord = (value: unknown): Record<string, unknown> | null =>
  value && typeof value === 'object' && !Array.isArray(value) ? value as Record<string, unknown> : null

/** The device may wrap a reply one level deeper, as {success, data}. */
function fieldsOf(data: unknown): Record<string, unknown> | null {
  const node = asRecord(data)
  return node && !('success' in node) ? node : asRecord(node?.data) ?? node
}

/**
 * One read, repeated while the server or the device turns it away as busy (server read-busy,
 * device `busy` or LANE_FULL, an UNAUTHORIZED that safe mode can explain) for at most 30 s. A read
 * changes nothing on the device, so repeating it is safe; the reboot itself is never repeated.
 */
export async function readWithRetry(
  deviceId: string,
  step: { type: string; action: string },
  context: { csrfToken: string | null; catalog: CommandCatalogSnapshot | null; signal: AbortSignal; wait?: (ms: number, signal: AbortSignal) => Promise<void> },
): Promise<ReadResult> {
  const pause = context.wait ?? sleep
  const deadline = Date.now() + READ_WINDOW_MS
  for (;;) {
    let failure: { code: string; detail: string | null; retryable: boolean; delay: number }
    try {
      const outcome = await runDeviceCommand(deviceId, {
        type: step.type,
        action: step.action,
        payload: {},
        catalogVersion: context.catalog?.catalogVersion ?? '',
        catalogHash: context.catalog?.catalogHash ?? '',
      }, { csrfToken: context.csrfToken ?? undefined, signal: context.signal })
      if (context.signal.aborted) return { ok: false, code: 'ABORTED', detail: null }

      const result = outcome.final?.result
      if (outcome.phase === 'done' && result?.status === 'Executed') {
        const data = outcome.final?.reply?.data
        return data === null || data === undefined ? { ok: false, code: 'NO_DATA', detail: null } : { ok: true, data }
      }
      const detail = (outcome.phase === 'not_sent' ? outcome.dispatch.detail : result?.detail) ?? null
      const code = outcome.phase === 'not_sent' ? outcome.dispatch.code : outcome.phase === 'timeout' ? 'CLIENT_TIMEOUT' : result?.code ?? 'UNKNOWN'
      const retryAfter = asRecord(outcome.final?.reply?.data)?.retry_after_ms
      const busy = code === 'DEVICE_READ_BUSY' ||
        (code === 'DEVICE_REJECTED' && BUSY_DETAIL.test(detail ?? ''))
      failure = {
        code, detail, retryable: busy,
        delay: typeof retryAfter === 'number' ? Math.min(READ_RETRY_MAX_MS, Math.max(READ_RETRY_MIN_MS, retryAfter)) : READ_RETRY_MS,
      }
    } catch (error) {
      const status = httpStatusOf(error)
      return { ok: false, code: status ? `HTTP_${status}` : 'NETWORK_ERROR', detail: (error as Error)?.message ?? null }
    }
    if (!failure.retryable || context.signal.aborted || Date.now() + failure.delay >= deadline)
      return { ok: false, code: failure.code, detail: failure.detail }
    await pause(failure.delay, context.signal)
    if (context.signal.aborted) return { ok: false, code: 'ABORTED', detail: null }
  }
}

/** Uptime and firmware of a system.get_info read; null when it was not read. */
export function infoOf(read: ReadResult | null): RebootOutcome['info'] {
  if (!read) return null
  const fields = read.ok ? fieldsOf(read.data) : null
  const uptime = fields?.uptime_seconds
  const firmware = fields?.firmware
  return {
    uptimeSeconds: typeof uptime === 'number' && Number.isFinite(uptime) ? uptime : null,
    firmware: typeof firmware === 'string' ? firmware : typeof asRecord(firmware)?.version === 'string' ? asRecord(firmware)!.version as string : null,
  }
}

function timeOf(read: ReadResult | null): RebootOutcome['time'] {
  const fields = read?.ok ? fieldsOf(read.data) : null
  if (!fields) return null
  return {
    ntpSynced: typeof fields.ntp_synced === 'boolean' ? fields.ntp_synced : null,
    skewSeconds: typeof fields.epoch === 'number' ? Math.round(fields.epoch - Date.now() / 1000) : null,
  }
}

/**
 * One reboot, front to back: dry run, the real command (once), the server's record of whether the
 * device came back, then a signed read-back. It sends `system.reboot` at most twice, never again
 * whatever the answers are, and gives the operator one result.
 */
export async function runReboot(deviceId: string, options: RunRebootOptions): Promise<RebootOutcome> {
  const { signal } = options
  const pause = options.wait ?? sleep
  const phase = (value: RebootPhase) => options.onPhase?.(value)
  const allowed = (key: string) => options.allowedCommands?.includes(key) === true
  const readContext = { csrfToken: options.csrfToken, catalog: options.catalog, signal, wait: options.wait }
  const base = { type: 'system', action: 'reboot', payload: {}, confirm: options.confirm, catalogVersion: options.catalog?.catalogVersion ?? '', catalogHash: options.catalog?.catalogHash ?? '' }
  const writeOptions = { csrfToken: options.csrfToken ?? undefined, signal, followJobs: false }

  const outcome = (kind: RebootKind, code: string, detail: string | null = null, extra: Partial<RebootOutcome> = {}): RebootOutcome =>
    ({ kind, code, detail, verified: false, uptimeBefore: options.uptimeBefore, info: null, outputs: null, check: null, time: null, ...extra })
  const readInfoOnce = async () => allowed('system.get_info') ? infoOf(await readWithRetry(deviceId, { type: 'system', action: 'get_info' }, readContext)) : null

  phase('dry_run')
  const dry = await runDeviceWrite(deviceId, { ...base, dryRun: true }, writeOptions)
  if (dry.kind !== 'executed') return outcome('dry_run_failed', dry.code, dry.detail)
  if (signal.aborted) return outcome('dry_run_failed', 'ABORTED')

  phase('sending')
  const realId = newCorrelationId()
  options.onRealCommand?.(realId)
  const real = await runDeviceWrite(deviceId, { ...base, dryRun: false, correlationId: realId }, writeOptions)
  if (real.kind === 'not_sent' || (real.kind === 'unknown' && real.dispatch?.status === 'Failed' && REFUSED_BEFORE_SIGNING.has(real.code)))
    return outcome('not_sent', real.code, real.detail)
  if (real.kind === 'rejected') return outcome('rejected', real.code, real.detail, { info: await readInfoOnce() })

  // The real command may be running whatever came back: follow the server record, by code.
  phase('tracking')
  let code = real.code
  let detail = real.detail
  const deadline = Date.now() + TRACK_WINDOW_MS
  // Whether the server is known to hold the command: its answer to the send, or to an earlier poll.
  let knownToServer = real.dispatch !== null
  for (;;) {
    if (signal.aborted) return outcome('tracking_timeout', 'ABORTED')
    if (code === OBSERVED) break
    if (END_CODES[code]) {
      return outcome(END_CODES[code], code, detail, END_CODES[code] === 'untracked' ? { info: await readInfoOnce() } : {})
    }
    if (code === 'DEVICE_REJECTED') return outcome('rejected', code, detail, { info: await readInfoOnce() })
    if (!isOpen(code)) return outcome('outcome_unknown', code, detail)
    if (Date.now() >= deadline) return outcome('tracking_timeout', code, detail, { info: await readInfoOnce() })

    await pause(TRACK_POLL_MS, signal)
    if (signal.aborted) continue
    try {
      const polled = await adminApi.commandResult(deviceId, realId)
      knownToServer = true
      if (polled.result) { code = polled.result.code; detail = polled.result.detail ?? null }
    } catch (error) {
      if (httpStatusOf(error) === 404)
        return knownToServer ? outcome('server_restarted', 'COMMAND_NOT_FOUND') : outcome('outcome_unknown', 'REBOOT_SEND_UNCONFIRMED')
      // Any other failure says nothing about the reboot: ask again until the window ends.
    }
  }

  phase('reading')
  const info = allowed('system.get_info') ? await readWithRetry(deviceId, { type: 'system', action: 'get_info' }, readContext) : null
  const status = allowed('control.get_status') ? await readWithRetry(deviceId, { type: 'control', action: 'get_status' }, readContext) : null
  const time = allowed('system.get_time') ? await readWithRetry(deviceId, { type: 'system', action: 'get_time' }, readContext) : null

  const infoFields = infoOf(info)
  const statusData = status?.ok ? status.data : null
  const channels = channelsOf(statusData)
  return outcome(channels ? 'observed' : 'relay_unchecked', OBSERVED, null, {
    verified: isVerifiedReboot(options.uptimeBefore, infoFields?.uptimeSeconds),
    info: infoFields,
    outputs: channels,
    check: channels ? checkAfterReboot(options.relaysBefore ?? NOTHING_BEFORE, statusData) : null,
    time: timeOf(time),
  })
}

export interface RebootInput {
  confirm: string
  uptimeBefore: number | null
  relaysBefore: RelaysBefore | null
}

/** Reactive wrapper: one reboot at a time, cancellable, tied to one device. */
export function useDeviceReboot(
  deviceId: () => string,
  context: () => { csrfToken: string | null; catalog: CommandCatalogSnapshot | null; allowedCommands: string[] | null },
) {
  const phase = ref<'idle' | RebootPhase | 'done'>('idle')
  const outcome = ref<RebootOutcome | null>(null)
  /** Correlation id of the real command in flight, for matching its realtime event. */
  const correlationId = ref<string | null>(null)
  let controller: AbortController | null = null
  let wake: (() => void) | null = null
  let nudged = false

  // A poll pause that nudge() can cut short.
  const wait = (ms: number, signal: AbortSignal) => new Promise<void>(resolve => {
    if (nudged) { nudged = false; resolve(); return }
    const done = () => { clearTimeout(timer); signal.removeEventListener('abort', done); wake = null; resolve() }
    const timer = setTimeout(done, ms)
    wake = done
    signal.addEventListener('abort', done, { once: true })
  })

  /** Null when cancelled, when the page moved to another device, or when one is already running. */
  async function run(input: RebootInput): Promise<RebootOutcome | null> {
    if (controller) return null
    const device = deviceId()
    const current = new AbortController()
    controller = current
    outcome.value = null
    correlationId.value = null
    nudged = false
    phase.value = 'dry_run'
    try {
      const { csrfToken, catalog, allowedCommands } = context()
      const result = await runReboot(device, {
        ...input, csrfToken, catalog, allowedCommands, signal: current.signal, wait,
        onPhase: value => { if (!current.signal.aborted) phase.value = value },
        onRealCommand: id => { if (!current.signal.aborted) correlationId.value = id },
      })
      if (current.signal.aborted || deviceId() !== device) return null
      outcome.value = result
      phase.value = 'done'
      return result
    } finally {
      if (controller === current) controller = null
    }
  }

  /** Stops this page following the reboot; the server keeps watching it either way. */
  function cancel() {
    controller?.abort()
    controller = null
    wake?.()
    phase.value = 'idle'
    outcome.value = null
    correlationId.value = null
  }

  /** The server announced a result for `id` (or any, when omitted): ask for it now instead of at the next tick. */
  function nudge(id?: string) {
    if (!controller || (id !== undefined && id !== correlationId.value)) return
    if (wake) wake()
    else nudged = true
  }

  watch(deviceId, cancel)
  onScopeDispose(cancel)

  return { phase, outcome, correlationId, run, cancel, nudge }
}
