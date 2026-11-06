import { ref } from 'vue'
import { adminApi, type CommandExecutionResult, type CommandResultDto } from '@/core/api/adminApi'

export const POLL_INTERVAL_MS = 1000
// The server records an outcome-unknown timeout after 30 s without a reply (spec R4).
export const POLL_LIMIT_MS = 40_000

export interface DeviceCommandBody {
  type: string
  action: string
  payload?: Record<string, unknown>
  correlationId?: string
  catalogVersion?: string
  catalogHash?: string
}

/** `not_sent`: refused before anything reached the broker. `done`: the server recorded a device
 *  outcome (which may itself be "outcome unknown"). `timeout`: no outcome within the poll limit. */
export type DeviceCommandPhase = 'not_sent' | 'done' | 'timeout'

export interface DeviceCommandOutcome {
  phase: DeviceCommandPhase
  dispatch: CommandExecutionResult
  final: CommandResultDto | null
}

const PUBLISH_UNCONFIRMED = 'MQTT_PUBLISH_UNCONFIRMED'
// Transport refusals before the publish was attempted (server MqttDeviceCommandTransport).
const NEVER_SENT_CODES = new Set(['MQTT_NOT_CONNECTED', 'MQTT_RESPONSE_SUBSCRIPTION_MISSING', 'MQTT_COMMAND_TOPIC_INVALID'])

/** A dispatch the broker took, or may have taken: the device reply is worth waiting for. */
export function mayHaveReachedDevice(result: CommandExecutionResult): boolean {
  return result.status === 'Published' || result.code === PUBLISH_UNCONFIRMED
}

/** Proof that nothing was signed or handed to the broker. Narrower than `!mayHaveReachedDevice`:
 *  some server refusals (an idempotency outcome it cannot vouch for, say) may follow a publish, so
 *  anything that drives hardware reads the state back unless this holds. */
export function certainlyNotSent(result: CommandExecutionResult): boolean {
  return result.status === 'ContractUnavailable' || result.status === 'TransportNotConfigured' ||
    NEVER_SENT_CODES.has(result.code)
}

/** HTTP status of a failed admin API call (`Request failed (NNN)`), or null for network errors. */
export function httpStatusOf(error: unknown): number | null {
  const match = /Request failed \((\d{3})\)/.exec((error as Error)?.message ?? '')
  return match ? Number(match[1]) : null
}

function stillWaiting(result: CommandExecutionResult | null): boolean {
  return !result || result.status === 'Published' || result.code === PUBLISH_UNCONFIRMED
}

/** Correlation ids are at most 39 bytes on the device (spec §3). */
export function newCorrelationId(): string {
  return `c2-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`
}

function sleep(ms: number, signal?: AbortSignal): Promise<void> {
  return new Promise(resolve => {
    const timer = setTimeout(resolve, ms)
    signal?.addEventListener('abort', () => {
      clearTimeout(timer)
      resolve()
    }, { once: true })
  })
}

export async function runDeviceCommand(
  deviceId: string,
  body: DeviceCommandBody,
  options: { csrfToken?: string; signal?: AbortSignal; limitMs?: number; onDispatched?: (result: CommandExecutionResult) => void },
): Promise<DeviceCommandOutcome> {
  const dispatch = await adminApi.sendCommand(deviceId,
    { ...body, correlationId: body.correlationId ?? newCorrelationId() },
    { csrfToken: options.csrfToken })
  options.onDispatched?.(dispatch)
  if (!mayHaveReachedDevice(dispatch)) return { phase: 'not_sent', dispatch, final: null }

  const deadline = Date.now() + (options.limitMs ?? POLL_LIMIT_MS)
  let final: CommandResultDto | null = null
  while (Date.now() < deadline && !options.signal?.aborted) {
    await sleep(POLL_INTERVAL_MS, options.signal)
    if (options.signal?.aborted) break
    try {
      final = await adminApi.commandResult(deviceId, dispatch.correlationId)
      if (!stillWaiting(final.result)) return { phase: 'done', dispatch, final }
    } catch {
      // A transient read failure keeps polling until the deadline.
    }
  }
  return { phase: 'timeout', dispatch, final }
}

/** Reactive wrapper for components: one command at a time, cancellable. */
export function useDeviceCommand(deviceId: () => string, csrfToken: () => string | null = () => null) {
  const phase = ref<'idle' | 'sending' | 'waiting' | DeviceCommandPhase>('idle')
  const dispatch = ref<CommandExecutionResult | null>(null)
  const outcome = ref<DeviceCommandOutcome | null>(null)
  let controller: AbortController | null = null

  async function run(body: DeviceCommandBody): Promise<DeviceCommandOutcome | null> {
    controller?.abort()
    const current = new AbortController()
    controller = current
    phase.value = 'sending'
    dispatch.value = null
    outcome.value = null
    const result = await runDeviceCommand(deviceId(), body, {
      csrfToken: csrfToken() ?? undefined,
      signal: current.signal,
      onDispatched: sent => {
        if (current.signal.aborted) return
        dispatch.value = sent
        if (mayHaveReachedDevice(sent)) phase.value = 'waiting'
      },
    })
    if (current.signal.aborted) return null
    outcome.value = result
    phase.value = result.phase
    return result
  }

  function cancel() {
    controller?.abort()
    controller = null
    phase.value = 'idle'
  }

  return { phase, dispatch, outcome, run, cancel }
}
