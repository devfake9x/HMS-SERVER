import { vi } from 'vitest'
import { adminApi, type CommandExecutionResult } from '@/core/api/adminApi'

/** What the fake device does with one command: the dispatch answer, the later outcome, the reply. */
export interface Scenario {
  dispatch?: Partial<CommandExecutionResult>
  result?: Partial<CommandExecutionResult>
  /**
   * Successive answers to the result polls of this command (the last one repeats). An `Error`
   * entry makes that poll fail, as an HTTP error would. Without it every poll gets `result`.
   */
  results?: Array<Partial<CommandExecutionResult> | Error>
  /** This many polls fail (after the first answer) before the answers in `results` go on. */
  pollError?: number
  data?: unknown
  replyError?: string | null
  /** sendCommand itself fails (HTTP error, lost connection). */
  throws?: Error
}

export interface SentCommand {
  key: string
  payload: Record<string, unknown> | undefined
  correlationId: string
  confirm?: string
  dryRun?: boolean
}

/**
 * Stands in for the admin API in tests: every command is answered by `handler(key, payload, n)`,
 * where n counts the sends of that same `type.action`. Returns the log of what was sent.
 */
export function fakeDevice(
  handler: (key: string, payload: Record<string, unknown> | undefined, n: number) => Scenario | undefined,
): SentCommand[] {
  const sent: SentCommand[] = []
  const answers = new Map<string, Scenario>()
  const polls = new Map<string, number>()
  const counts: Record<string, number> = {}

  vi.spyOn(adminApi, 'sendCommand').mockImplementation(async (_device, body) => {
    const key = `${body.type}.${body.action}`
    counts[key] = (counts[key] ?? 0) + 1
    const correlationId = body.correlationId ?? `auto-${sent.length}`
    sent.push({ key, payload: body.payload, correlationId, confirm: body.confirm, dryRun: body.dryRun })
    const scenario = handler(key, body.payload, counts[key]) ?? {}
    if (scenario.throws) throw scenario.throws
    answers.set(correlationId, scenario)
    return { status: 'Published', code: 'MQTT_PUBLISHED', correlationId, ...scenario.dispatch }
  })
  vi.spyOn(adminApi, 'commandResult').mockImplementation(async (_device, id) => {
    const scenario = answers.get(id) ?? {}
    const call = polls.get(id) ?? 0
    polls.set(id, call + 1)
    const failing = scenario.pollError ?? 0
    if (call >= 1 && call <= failing) throw new Error('Request failed (503)')
    const index = call > failing ? call - failing : call
    const answer = scenario.results ? scenario.results[Math.min(index, scenario.results.length - 1)] : scenario.result
    if (answer instanceof Error) throw answer
    return {
      correlationId: id,
      result: { status: 'Executed', code: 'DEVICE_EXECUTED', correlationId: id, ...answer },
      reply: scenario.data === undefined ? null : {
        type: 'x', action: 'y', success: !scenario.replyError, error: scenario.replyError ?? null,
        data: scenario.data, receivedAt: '2026-11-06T00:00:00Z',
      },
    }
  })
  return sent
}
