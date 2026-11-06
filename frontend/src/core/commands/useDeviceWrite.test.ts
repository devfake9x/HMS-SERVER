import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { effectScope } from 'vue'
import { adminApi } from '@/core/api/adminApi'
import { fakeDevice, type Scenario } from './testing/fakeDevice'
import { runDeviceWrite, useDeviceWrite, isOwnWrite, JOB_WINDOW_MS } from './useDeviceWrite'

const body = { type: 'system', action: 'set_hostname', payload: { hostname: 'gw-1' }, catalogVersion: 'v', catalogHash: 'h' }
const OPS = 'system.get_operations'
const WRITE = 'system.set_hostname'
const accepted = (id: string): Scenario => ({ result: { status: 'Acknowledged', code: 'DEVICE_ACCEPTED', detail: `operation_id=${id}` }, data: {} })
const operation = (data: Record<string, unknown>): Scenario => ({ data })
const writesSent = (sent: { key: string }[]) => sent.filter(s => s.key === WRITE).length

describe('runDeviceWrite', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => {
    vi.restoreAllMocks()
    vi.useRealTimers()
  })

  async function settle(ms = 5000) {
    const run = runDeviceWrite('dev-1', body, {})
    await vi.advanceTimersByTimeAsync(ms)
    return run
  }

  it('reports an executed write with the device reply data', async () => {
    const sent = fakeDevice(() => ({ data: { applied_immediately: true } }))
    const outcome = await settle()

    expect(outcome.kind).toBe('executed')
    expect(outcome.data).toEqual({ applied_immediately: true })
    expect(writesSent(sent)).toBe(1)
  })

  it('classifies a device rejection as rejected, with the device error text', async () => {
    fakeDevice(() => ({ result: { status: 'Failed', code: 'DEVICE_REJECTED', detail: 'INVALID_HOSTNAME' }, data: { error: 'bad' } }))
    const outcome = await settle()

    expect(outcome.kind).toBe('rejected')
    expect(outcome.code).toBe('DEVICE_REJECTED')
    expect(outcome.detail).toBe('INVALID_HOSTNAME')
    expect(outcome.data).toEqual({ error: 'bad' })
  })

  it.each(['DEVICE_WRITE_BUSY', 'DEVICE_WRITE_RATE_LIMITED', 'COMMAND_PAYLOAD_INVALID', 'CATALOG_STALE'])(
    'classifies the server refusal %s as not_sent', async code => {
      const sent = fakeDevice(() => ({ dispatch: { status: 'ContractUnavailable', code } }))
      const outcome = await runDeviceWrite('dev-1', body, {})

      expect(outcome.kind).toBe('not_sent')
      expect(outcome.code).toBe(code)
      expect(writesSent(sent)).toBe(1)
    })

  it('treats a permission refusal (HTTP 200, Failed) as unknown, since it is not proof of nothing sent', async () => {
    fakeDevice(() => ({ dispatch: { status: 'Failed', code: 'DEVICE_PERMISSION_REQUIRED' } }))
    const outcome = await runDeviceWrite('dev-1', body, {})

    expect(outcome.kind).toBe('unknown')
    expect(outcome.code).toBe('DEVICE_PERMISSION_REQUIRED')
  })

  it('classifies a thrown HTTP 4xx as not_sent', async () => {
    fakeDevice(() => ({ throws: new Error('Request failed (403)') }))
    const outcome = await runDeviceWrite('dev-1', body, {})

    expect(outcome.kind).toBe('not_sent')
    expect(outcome.code).toBe('HTTP_403')
  })

  it('classifies HTTP 5xx and a lost connection as unknown', async () => {
    fakeDevice(() => ({ throws: new Error('Request failed (500)') }))
    const fiveHundred = await runDeviceWrite('dev-1', body, {})
    expect(fiveHundred.kind).toBe('unknown')
    expect(fiveHundred.code).toBe('HTTP_500')

    vi.restoreAllMocks()
    fakeDevice(() => ({ throws: new TypeError('Failed to fetch') }))
    const network = await runDeviceWrite('dev-1', body, {})
    expect(network.kind).toBe('unknown')
    expect(network.code).toBe('NETWORK_ERROR')
  })

  it('classifies an acknowledgement without a job id as unknown', async () => {
    fakeDevice(() => ({ result: { status: 'Acknowledged', code: 'DEVICE_ACCEPTED', detail: null } }))

    expect((await settle()).kind).toBe('unknown')
  })

  it('classifies a result poll that ran out as unknown', async () => {
    fakeDevice(() => ({ result: { status: 'Published', code: 'MQTT_PUBLISHED' } }))

    expect((await settle(45_000)).kind).toBe('unknown')
  })

  it('remembers the correlation id of its write so the page can tell its own event apart', async () => {
    const sent = fakeDevice(() => ({}))
    await settle()

    expect(isOwnWrite(sent[0].correlationId)).toBe(true)
    expect(isOwnWrite('someone-else')).toBe(false)
  })

  describe('a job the device accepted', () => {
    it('polls system.get_operations with the id as a string until the job succeeds, sending the write once', async () => {
      const sent = fakeDevice((key, _p, n) => key === WRITE ? accepted('42')
        : key === OPS ? operation(n < 2 ? { operationId: '42', state: 'pending' } : { operationId: '42', state: 'terminal', resultSuccess: true })
          : undefined)
      const outcome = await settle(10_000)

      expect(outcome.kind).toBe('executed')
      const polls = sent.filter(s => s.key === OPS)
      expect(polls.length).toBe(2)
      expect(polls[0].payload).toEqual({ operation_id: '42' })
      expect(writesSent(sent)).toBe(1)
    })

    it('reports job_failed with the job result code and message', async () => {
      fakeDevice(key => key === WRITE ? accepted('7')
        : operation({ operationId: '7', state: 'terminal', resultSuccess: false, resultCode: 'APPLY_FAILED', resultMessage: 'no space' }))
      const outcome = await settle(10_000)

      expect(outcome.kind).toBe('job_failed')
      expect(outcome.code).toBe('APPLY_FAILED')
      expect(outcome.detail).toBe('no space')
    })

    it('is unknown when the device no longer knows the job', async () => {
      fakeDevice(key => key === WRITE ? accepted('7') : operation({ operationId: '7', state: 'unknown' }))

      expect((await settle(10_000)).kind).toBe('unknown')
    })

    it('is unknown when the job is still pending after the window, and never resends the write', async () => {
      const sent = fakeDevice(key => key === WRITE ? accepted('7') : operation({ operationId: '7', state: 'pending' }))
      const outcome = await settle(JOB_WINDOW_MS + 10_000)

      expect(outcome.kind).toBe('unknown')
      expect(writesSent(sent)).toBe(1)
    })

    it('retries a poll the server turned away as busy', async () => {
      const sent = fakeDevice((key, _p, n) => key === WRITE ? accepted('7')
        : n === 1 ? { dispatch: { status: 'ContractUnavailable', code: 'DEVICE_READ_BUSY' } }
          : operation({ operationId: '7', state: 'terminal', resultSuccess: true }))
      const outcome = await settle(10_000)

      expect(outcome.kind).toBe('executed')
      expect(sent.filter(s => s.key === OPS).length).toBe(2)
    })
  })
})

describe('useDeviceWrite', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => {
    vi.restoreAllMocks()
    vi.useRealTimers()
  })
  const context = () => ({ csrfToken: 'csrf', catalog: { catalogVersion: 'v', catalogHash: 'h', trusted: true, actions: [] } })
  const command = { type: 'system', action: 'set_hostname', payload: { hostname: 'gw-1' } }

  it('runs one write, sets writing meanwhile, and stamps the catalog proof on the command', async () => {
    fakeDevice(() => ({}))
    const scope = effectScope()
    const w = scope.run(() => useDeviceWrite(() => 'dev-1', context))!

    const run = w.run(command)
    expect(w.writing.value).toBe(true)
    await vi.advanceTimersByTimeAsync(5000)
    const outcome = await run

    expect(outcome?.kind).toBe('executed')
    expect(w.writing.value).toBe(false)
    expect(vi.mocked(adminApi.sendCommand).mock.calls[0][1]).toMatchObject({ catalogVersion: 'v', catalogHash: 'h' })
    scope.stop()
  })

  it('returns null when cancelled', async () => {
    fakeDevice(() => ({}))
    const scope = effectScope()
    const w = scope.run(() => useDeviceWrite(() => 'dev-1', context))!

    const run = w.run(command)
    w.cancel()
    await vi.advanceTimersByTimeAsync(5000)

    expect(await run).toBeNull()
    expect(w.writing.value).toBe(false)
    scope.stop()
  })

  it('returns null when the page moved to another device meanwhile', async () => {
    fakeDevice(() => ({}))
    let device = 'dev-1'
    const scope = effectScope()
    const w = scope.run(() => useDeviceWrite(() => device, context))!

    const run = w.run(command)
    device = 'dev-2'
    await vi.advanceTimersByTimeAsync(5000)

    expect(await run).toBeNull()
    scope.stop()
  })

  it('cancels with its effect scope', async () => {
    fakeDevice(() => ({}))
    const scope = effectScope()
    const w = scope.run(() => useDeviceWrite(() => 'dev-1', context))!

    const run = w.run(command)
    scope.stop()
    await vi.advanceTimersByTimeAsync(5000)

    expect(await run).toBeNull()
  })
})
