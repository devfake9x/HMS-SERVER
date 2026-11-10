import { onScopeDispose, ref } from 'vue'
import type { CommandCatalogSnapshot, CommandExecutionResult, CommandResultDto } from '@/core/api/adminApi'
import {
  httpStatusOf,
  mayHaveRun,
  newCorrelationId,
  runDeviceCommand,
  type DeviceCommandBody,
  type DeviceCommandOutcome,
} from './useDeviceCommand'

/** How long a write the device accepted as a running job is followed (the server holds the write lane 60 s). */
export const JOB_WINDOW_MS = 60_000
export const JOB_POLL_MS = 1500
export const JOB_BUSY_RETRY_MS = 2000

/**
 * `not_sent`: nothing reached the device, so no read-back is needed. `executed`: the device ran it.
 * `rejected`: the device refused it. `job_failed`: an accepted job ended in failure.
 * `unknown`: no usable answer; the write may or may not have run.
 */
export type WriteKind = 'not_sent' | 'executed' | 'rejected' | 'job_failed' | 'unknown'

export interface WriteOutcome {
  kind: WriteKind
  dispatch: CommandExecutionResult | null
  final: CommandResultDto | null
  code: string
  detail: string | null
  /** Reply data of the write itself (for example set_hostname's applied_immediately). */
  data: unknown
}

// Correlation ids of writes this browser sent: their realtime echo is not "someone else changed it".
const ownWrites: string[] = []
const OWN_WRITES_KEPT = 100

export function isOwnWrite(correlationId: unknown): boolean {
  return typeof correlationId === 'string' && ownWrites.includes(correlationId)
}

function rememberOwnWrite(correlationId: string) {
  ownWrites.push(correlationId)
  if (ownWrites.length > OWN_WRITES_KEPT) ownWrites.shift()
}

function sleep(ms: number, signal?: AbortSignal): Promise<void> {
  return new Promise(resolve => {
    const timer = setTimeout(resolve, ms)
    signal?.addEventListener('abort', () => { clearTimeout(timer); resolve() }, { once: true })
  })
}

const OPERATION_ID = /^operation_id=(.+)$/

/** The job `state` of a get_operations reply (spec §5); the reply may carry it one level deeper. */
interface OperationReply {
  state: string
  resultSuccess?: boolean
  resultCode?: string
  resultMessage?: string
}

function parseOperation(data: unknown): OperationReply | null {
  let node = data
  if (node && typeof node === 'object' && !('state' in node) && 'data' in node) node = (node as { data: unknown }).data
  if (!node || typeof node !== 'object') return null
  const record = node as Record<string, unknown>
  if (typeof record.state !== 'string') return null
  return {
    state: record.state,
    resultSuccess: typeof record.resultSuccess === 'boolean' ? record.resultSuccess : undefined,
    resultCode: typeof record.resultCode === 'string' ? record.resultCode : undefined,
    resultMessage: typeof record.resultMessage === 'string' ? record.resultMessage : undefined,
  }
}

function outcomeOf(
  kind: WriteKind,
  sent: DeviceCommandOutcome | null,
  code: string,
  detail: string | null = null,
  data: unknown = sent?.final?.reply?.data,
): WriteOutcome {
  return { kind, dispatch: sent?.dispatch ?? null, final: sent?.final ?? null, code, detail, data }
}

/**
 * Follows a job the device accepted with `system.get_operations`, never touching the write
 * itself. A job the device lost, or one still running after the window, is `unknown`.
 */
async function followJob(
  deviceId: string,
  jobId: string,
  accepted: DeviceCommandOutcome,
  body: DeviceCommandBody,
  options: { csrfToken?: string; signal?: AbortSignal },
): Promise<WriteOutcome> {
  const deadline = Date.now() + JOB_WINDOW_MS
  let wait = JOB_POLL_MS
  while (Date.now() < deadline && !options.signal?.aborted) {
    await sleep(wait, options.signal)
    if (options.signal?.aborted) break
    wait = JOB_POLL_MS
    let polled: DeviceCommandOutcome
    try {
      polled = await runDeviceCommand(deviceId, {
        type: 'system',
        action: 'get_operations',
        payload: { operation_id: jobId },
        catalogVersion: body.catalogVersion,
        catalogHash: body.catalogHash,
      }, { csrfToken: options.csrfToken, signal: options.signal, limitMs: Math.max(1000, deadline - Date.now()) })
    } catch {
      continue // a poll that failed in transit says nothing about the job; the window still bounds us
    }
    if (polled.phase === 'not_sent') {
      if (polled.dispatch.code === 'DEVICE_READ_BUSY') { wait = JOB_BUSY_RETRY_MS; continue }
      return outcomeOf('unknown', accepted, polled.dispatch.code, polled.dispatch.detail ?? null)
    }
    const operation = polled.phase === 'done' && polled.final?.result?.status === 'Executed'
      ? parseOperation(polled.final.reply?.data)
      : null
    if (!operation || operation.state === 'pending') continue
    if (operation.state === 'terminal' && operation.resultSuccess === true)
      return outcomeOf('executed', accepted, 'DEVICE_EXECUTED')
    if (operation.state === 'terminal' && operation.resultSuccess === false)
      return outcomeOf('job_failed', accepted, operation.resultCode ?? 'JOB_FAILED', operation.resultMessage ?? null)
    return outcomeOf('unknown', accepted, 'JOB_UNKNOWN')
  }
  return outcomeOf('unknown', accepted, 'JOB_POLL_TIMEOUT')
}

/**
 * Sends one write, exactly once, and classifies what is known afterwards. It never sends the write
 * again, whatever the outcome; every kind except `not_sent` means the caller must read the state back.
 * With `followJobs: false` an accepted job stays `unknown` instead of being polled with
 * system.get_operations (a device that is restarting cannot answer).
 */
export async function runDeviceWrite(
  deviceId: string,
  body: DeviceCommandBody,
  options: { csrfToken?: string; signal?: AbortSignal; followJobs?: boolean },
): Promise<WriteOutcome> {
  const correlationId = body.correlationId ?? newCorrelationId()
  rememberOwnWrite(correlationId)

  let sent: DeviceCommandOutcome
  try {
    sent = await runDeviceCommand(deviceId, { ...body, correlationId }, options)
  } catch (error) {
    const status = httpStatusOf(error)
    return {
      kind: mayHaveRun(error) ? 'unknown' : 'not_sent',
      dispatch: null,
      final: null,
      code: status !== null ? `HTTP_${status}` : 'NETWORK_ERROR',
      detail: (error as Error)?.message ?? null,
      data: undefined,
    }
  }

  const { dispatch } = sent
  if (sent.phase === 'not_sent')
    return outcomeOf(mayHaveRun(sent) ? 'unknown' : 'not_sent', sent, dispatch.code, dispatch.detail ?? null)
  if (sent.phase === 'timeout') return outcomeOf('unknown', sent, 'CLIENT_TIMEOUT')

  const result = sent.final?.result
  const code = result?.code ?? 'UNKNOWN'
  const detail = result?.detail ?? null
  if (result?.status === 'Executed') return outcomeOf('executed', sent, code, detail)
  if (result?.status === 'Failed' && code === 'DEVICE_REJECTED') return outcomeOf('rejected', sent, code, detail)
  if (result?.status === 'Acknowledged') {
    const jobId = OPERATION_ID.exec(detail ?? '')?.[1]
    return jobId && options.followJobs !== false ? followJob(deviceId, jobId, sent, body, options) : outcomeOf('unknown', sent, code, detail)
  }
  return outcomeOf('unknown', sent, code, detail)
}

/** Reactive wrapper for components: one write at a time, cancellable, tied to one device. */
export function useDeviceWrite(
  deviceId: () => string,
  context: () => { csrfToken: string | null; catalog: CommandCatalogSnapshot | null },
) {
  const writing = ref(false)
  let controller: AbortController | null = null

  /** Null when the write was cancelled, the page moved to another device, or one is already running. */
  async function run(command: { type: string; action: string; payload?: Record<string, unknown> }): Promise<WriteOutcome | null> {
    if (writing.value) return null
    const device = deviceId()
    const current = new AbortController()
    controller = current
    writing.value = true
    try {
      const { csrfToken, catalog } = context()
      const outcome = await runDeviceWrite(device, {
        ...command,
        payload: command.payload ?? {},
        catalogVersion: catalog?.catalogVersion ?? '',
        catalogHash: catalog?.catalogHash ?? '',
      }, { csrfToken: csrfToken ?? undefined, signal: current.signal })
      return current.signal.aborted || deviceId() !== device ? null : outcome
    } finally {
      if (controller === current) {
        controller = null
        writing.value = false
      }
    }
  }

  function cancel() {
    controller?.abort()
    controller = null
    writing.value = false
  }

  onScopeDispose(cancel)

  return { writing, run, cancel }
}
