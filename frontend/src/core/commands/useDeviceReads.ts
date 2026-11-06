import { onScopeDispose, reactive, ref } from 'vue'
import type { CommandCatalogSnapshot } from '@/core/api/adminApi'
import { httpStatusOf, runDeviceCommand } from './useDeviceCommand'

/** One read command of a card. */
export interface ReadStep {
  type: string
  action: string
  payload?: Record<string, unknown>
}

/**
 * What a card knows about one read. `readAt` is when this read finished, ok or not: a failed
 * re-read replaces the old value instead of leaving it on screen as if it were current.
 */
export interface ReadState {
  kind: 'idle' | 'reading' | 'ok' | 'error' | 'no_data'
  data?: unknown
  readAt?: string
  code?: string
  detail?: string | null
}

/** Pause before the single retry of a read the server or the device turned away as busy. */
export const RETRY_DELAY_MS = 2000

export const stepKey = (step: ReadStep) => `${step.type}.${step.action}`

// Server: too many reads waiting on this device. Device: its command lane is full (spec §5).
function isBusy(state: ReadState): boolean {
  return state.code === 'DEVICE_READ_BUSY' ||
    (state.code === 'DEVICE_REJECTED' && (state.detail ?? '').startsWith('LANE_FULL'))
}

function sleep(ms: number, signal: AbortSignal): Promise<void> {
  return new Promise(resolve => {
    const timer = setTimeout(resolve, ms)
    signal.addEventListener('abort', () => { clearTimeout(timer); resolve() }, { once: true })
  })
}

async function readOnce(
  deviceId: string,
  step: ReadStep,
  context: { csrfToken: string | null; catalog: CommandCatalogSnapshot | null },
  signal: AbortSignal,
): Promise<ReadState> {
  const readAt = () => new Date().toISOString()
  try {
    const outcome = await runDeviceCommand(deviceId, {
      type: step.type,
      action: step.action,
      payload: step.payload ?? {},
      catalogVersion: context.catalog?.catalogVersion ?? '',
      catalogHash: context.catalog?.catalogHash ?? '',
    }, { csrfToken: context.csrfToken ?? undefined, signal })
    if (signal.aborted) return { kind: 'idle' }

    if (outcome.phase === 'not_sent')
      return { kind: 'error', code: outcome.dispatch.code, detail: outcome.dispatch.detail ?? null, readAt: readAt() }
    if (outcome.phase === 'timeout')
      return { kind: 'error', code: 'CLIENT_TIMEOUT', detail: 'Không có kết quả từ server trong thời gian chờ', readAt: readAt() }

    const result = outcome.final?.result
    if (result?.status !== 'Executed')
      return { kind: 'error', code: result?.code ?? 'UNKNOWN', detail: result?.detail ?? null, readAt: readAt() }
    // Spec R3: success without data is possible; the server may also no longer hold the reply.
    const data = outcome.final?.reply?.data
    return data === null || data === undefined
      ? { kind: 'no_data', readAt: readAt() }
      : { kind: 'ok', data, readAt: readAt() }
  } catch (error) {
    if (signal.aborted) return { kind: 'idle' }
    const status = httpStatusOf(error)
    return { kind: 'error', code: status ? `HTTP_${status}` : 'NETWORK_ERROR', detail: (error as Error)?.message ?? null, readAt: readAt() }
  }
}

/**
 * Sends a card's reads one at a time (the device has one small command queue shared with relay
 * commands), retries a busy refusal once, and can be cancelled. Results are kept only while the
 * page still shows the device they were read from.
 */
export function useDeviceReads(
  deviceId: () => string,
  context: () => { csrfToken: string | null; catalog: CommandCatalogSnapshot | null },
) {
  const states = reactive<Record<string, ReadState>>({})
  const reading = ref(false)
  let controller: AbortController | null = null

  async function read(steps: ReadStep[]): Promise<void> {
    if (reading.value) return
    const current = new AbortController()
    controller = current
    const device = deviceId()
    reading.value = true
    try {
      for (const step of steps) {
        if (current.signal.aborted || deviceId() !== device) break
        const key = stepKey(step)
        states[key] = { kind: 'reading' }
        let state = await readOnce(device, step, context(), current.signal)
        if (isBusy(state) && !current.signal.aborted) {
          await sleep(RETRY_DELAY_MS, current.signal)
          if (!current.signal.aborted) state = await readOnce(device, step, context(), current.signal)
        }
        // A late answer for a device the page no longer shows is dropped.
        if (deviceId() !== device) break
        states[key] = current.signal.aborted ? { kind: 'idle' } : state
      }
    } finally {
      for (const key of Object.keys(states))
        if (states[key].kind === 'reading') states[key] = { kind: 'idle' }
      if (controller === current) {
        controller = null
        reading.value = false
      }
    }
  }

  function cancel() {
    controller?.abort()
  }

  function reset() {
    cancel()
    for (const key of Object.keys(states)) delete states[key]
  }

  onScopeDispose(cancel)

  return { states, reading, read, cancel, reset }
}
