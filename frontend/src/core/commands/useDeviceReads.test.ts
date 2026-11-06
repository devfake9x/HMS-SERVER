import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { effectScope } from 'vue'
import { adminApi, type CommandCatalogSnapshot } from '@/core/api/adminApi'
import { useDeviceReads, RETRY_DELAY_MS } from './useDeviceReads'

const catalog = { catalogVersion: 'v', catalogHash: 'h', trusted: true, actions: [] } as unknown as CommandCatalogSnapshot
const published = (id: string) => ({ status: 'Published', code: 'MQTT_PUBLISHED', correlationId: id })
const executed = (id: string, data: unknown) => ({
  correlationId: id,
  result: { status: 'Executed', code: 'DEVICE_EXECUTED', correlationId: id },
  reply: { type: 'system', action: 'x', success: true, error: null, data, receivedAt: '2026-11-06T00:00:00Z' },
})
const failed = (id: string, code: string, detail: string | null = null) => ({
  correlationId: id, result: { status: 'Failed', code, correlationId: id, detail }, reply: null,
})

// The device has not answered yet: polling keeps going.
const stillPublished = { correlationId: 'x', result: { status: 'Published', code: 'MQTT_PUBLISHED', correlationId: 'x' }, reply: null }

const steps = [
  { type: 'system', action: 'get_time' },
  { type: 'system', action: 'get_hostname' },
  { type: 'system', action: 'get_device_id' },
]

function setup(deviceId: () => string = () => 'dev-1') {
  const scope = effectScope()
  const reads = scope.run(() => useDeviceReads(deviceId, () => ({ csrfToken: 'csrf', catalog })))!
  return { reads, scope }
}

describe('useDeviceReads', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => {
    vi.restoreAllMocks()
    vi.useRealTimers()
  })

  it('reads the steps one after another and keeps going after a failure', async () => {
    const send = vi.spyOn(adminApi, 'sendCommand').mockImplementation(async (_d, body) =>
      body.action === 'get_hostname'
        ? { status: 'ContractUnavailable', code: 'DEVICE_ACTION_UNAVAILABLE', correlationId: 'x' }
        : published(body.action))
    vi.spyOn(adminApi, 'commandResult').mockImplementation(async (_d, id) => executed(id, { value: id }))
    const { reads } = setup()

    const run = reads.read(steps)
    await vi.advanceTimersByTimeAsync(5000)
    await run

    expect(send.mock.calls.map(c => c[1].action)).toEqual(['get_time', 'get_hostname', 'get_device_id'])
    expect(reads.states['system.get_time']).toMatchObject({ kind: 'ok', data: { value: 'get_time' } })
    expect(reads.states['system.get_hostname']).toMatchObject({ kind: 'error', code: 'DEVICE_ACTION_UNAVAILABLE' })
    expect(reads.states['system.get_device_id'].kind).toBe('ok')
    expect(reads.reading.value).toBe(false)
  })

  it('does not send the next step before the previous one has an outcome', async () => {
    const send = vi.spyOn(adminApi, 'sendCommand').mockImplementation(async (_d, body) => published(body.action))
    vi.spyOn(adminApi, 'commandResult').mockImplementation(async (_d, id) => executed(id, {}))
    const { reads } = setup()

    void reads.read(steps)
    await vi.advanceTimersByTimeAsync(10)
    expect(send).toHaveBeenCalledTimes(1)
    await vi.advanceTimersByTimeAsync(1100)
    expect(send).toHaveBeenCalledTimes(2)
  })

  it('retries a busy device once, after a pause, with a new correlation id', async () => {
    const send = vi.spyOn(adminApi, 'sendCommand')
      .mockResolvedValueOnce({ status: 'ContractUnavailable', code: 'DEVICE_READ_BUSY', correlationId: 'a' })
      .mockResolvedValueOnce(published('b'))
    vi.spyOn(adminApi, 'commandResult').mockImplementation(async (_d, id) => executed(id, { ok: true }))
    const { reads } = setup()

    const run = reads.read([steps[0]])
    await vi.advanceTimersByTimeAsync(RETRY_DELAY_MS - 100)
    expect(send).toHaveBeenCalledTimes(1)
    await vi.advanceTimersByTimeAsync(2000)
    await run

    expect(send).toHaveBeenCalledTimes(2)
    expect(send.mock.calls[0][1].correlationId).not.toBe(send.mock.calls[1][1].correlationId)
    expect(reads.states['system.get_time'].kind).toBe('ok')
  })

  it('retries once when the device answers LANE_FULL, then reports it', async () => {
    const send = vi.spyOn(adminApi, 'sendCommand').mockImplementation(async (_d, body) => published(body.correlationId!))
    vi.spyOn(adminApi, 'commandResult').mockImplementation(async (_d, id) => failed(id, 'DEVICE_REJECTED', 'LANE_FULL'))
    const { reads } = setup()

    const run = reads.read([steps[0]])
    await vi.advanceTimersByTimeAsync(10_000)
    await run

    expect(send).toHaveBeenCalledTimes(2)
    expect(reads.states['system.get_time']).toMatchObject({ kind: 'error', code: 'DEVICE_REJECTED', detail: 'LANE_FULL' })
  })

  it('stops at once when cancelled and sends nothing more', async () => {
    const send = vi.spyOn(adminApi, 'sendCommand').mockImplementation(async (_d, body) => published(body.action))
    vi.spyOn(adminApi, 'commandResult').mockResolvedValue(stillPublished)
    const { reads } = setup()

    const run = reads.read(steps)
    await vi.advanceTimersByTimeAsync(500)
    reads.cancel()
    await vi.advanceTimersByTimeAsync(5000)
    await run

    expect(send).toHaveBeenCalledTimes(1)
    expect(reads.states['system.get_time'].kind).toBe('idle')
    expect(reads.reading.value).toBe(false)
  })

  it('reports an executed reply without data as no data', async () => {
    vi.spyOn(adminApi, 'sendCommand').mockResolvedValue(published('a'))
    vi.spyOn(adminApi, 'commandResult').mockResolvedValue({ ...executed('a', null), reply: null })
    const { reads } = setup()

    const run = reads.read([steps[0]])
    await vi.advanceTimersByTimeAsync(2000)
    await run

    expect(reads.states['system.get_time'].kind).toBe('no_data')
  })

  it('drops the old value when a re-read fails', async () => {
    vi.spyOn(adminApi, 'sendCommand').mockResolvedValue(published('a'))
    vi.spyOn(adminApi, 'commandResult')
      .mockResolvedValueOnce(executed('a', { hostname: 'gw' }))
      .mockResolvedValue(failed('a', 'DEVICE_RESPONSE_TIMEOUT', 'outcome unknown'))
    const { reads } = setup()

    let run = reads.read([steps[0]])
    await vi.advanceTimersByTimeAsync(2000)
    await run
    expect(reads.states['system.get_time'].data).toEqual({ hostname: 'gw' })

    run = reads.read([steps[0]])
    await vi.advanceTimersByTimeAsync(2000)
    await run

    expect(reads.states['system.get_time']).toMatchObject({ kind: 'error', code: 'DEVICE_RESPONSE_TIMEOUT' })
    expect(reads.states['system.get_time'].data).toBeUndefined()
    expect(reads.states['system.get_time'].readAt).toBeTruthy()
  })

  it('turns a failed request into the error of that step', async () => {
    vi.spyOn(adminApi, 'sendCommand').mockRejectedValue(new Error('Request failed (500)'))
    const { reads } = setup()

    await reads.read([steps[0]])

    expect(reads.states['system.get_time']).toMatchObject({ kind: 'error', code: 'HTTP_500' })
  })

  it('drops results that arrive after the page moved to another device', async () => {
    let current = 'dev-1'
    vi.spyOn(adminApi, 'sendCommand').mockImplementation(async (_d, body) => published(body.action))
    vi.spyOn(adminApi, 'commandResult').mockImplementation(async (_d, id) => executed(id, {}))
    const { reads } = setup(() => current)

    const run = reads.read(steps)
    await vi.advanceTimersByTimeAsync(500)
    current = 'dev-2'
    await vi.advanceTimersByTimeAsync(5000)
    await run

    expect(reads.states['system.get_time']?.kind ?? 'idle').not.toBe('ok')
    expect(reads.states['system.get_hostname']?.kind ?? 'idle').toBe('idle')
  })

  it('cancels its reads when its owner goes away', async () => {
    const send = vi.spyOn(adminApi, 'sendCommand').mockImplementation(async (_d, body) => published(body.action))
    vi.spyOn(adminApi, 'commandResult').mockResolvedValue(stillPublished)
    const { reads, scope } = setup()

    void reads.read(steps)
    await vi.advanceTimersByTimeAsync(500)
    scope.stop()
    await vi.advanceTimersByTimeAsync(5000)

    expect(send).toHaveBeenCalledTimes(1)
  })
})
