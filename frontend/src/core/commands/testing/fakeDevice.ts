import { vi } from 'vitest'
import { adminApi, type CommandExecutionResult } from '@/core/api/adminApi'

/** What the fake device does with one command: the dispatch answer, the later outcome, the reply. */
export interface Scenario {
  dispatch?: Partial<CommandExecutionResult>
  result?: Partial<CommandExecutionResult>
  data?: unknown
  replyError?: string | null
  /** sendCommand itself fails (HTTP error, lost connection). */
  throws?: Error
}

export interface SentCommand {
  key: string
  payload: Record<string, unknown> | undefined
  correlationId: string
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
  const counts: Record<string, number> = {}

  vi.spyOn(adminApi, 'sendCommand').mockImplementation(async (_device, body) => {
    const key = `${body.type}.${body.action}`
    counts[key] = (counts[key] ?? 0) + 1
    const correlationId = body.correlationId ?? `auto-${sent.length}`
    sent.push({ key, payload: body.payload, correlationId })
    const scenario = handler(key, body.payload, counts[key]) ?? {}
    if (scenario.throws) throw scenario.throws
    answers.set(correlationId, scenario)
    return { status: 'Published', code: 'MQTT_PUBLISHED', correlationId, ...scenario.dispatch }
  })
  vi.spyOn(adminApi, 'commandResult').mockImplementation(async (_device, id) => {
    const scenario = answers.get(id) ?? {}
    return {
      correlationId: id,
      result: { status: 'Executed', code: 'DEVICE_EXECUTED', correlationId: id, ...scenario.result },
      reply: scenario.data === undefined ? null : {
        type: 'x', action: 'y', success: !scenario.replyError, error: scenario.replyError ?? null,
        data: scenario.data, receivedAt: '2026-11-06T00:00:00Z',
      },
    }
  })
  return sent
}
