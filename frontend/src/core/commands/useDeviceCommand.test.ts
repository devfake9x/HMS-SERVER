import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { adminApi } from '@/core/api/adminApi'
import { runDeviceCommand, mayHaveReachedDevice, certainlyNotSent, httpStatusOf, useDeviceCommand, mayHaveRun, type DeviceCommandOutcome } from './useDeviceCommand'

const body = { type: 'control', action: 'set_output', payload: { channel: 1, value: true }, catalogVersion: 'v', catalogHash: 'h' }

describe('runDeviceCommand', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => {
    vi.restoreAllMocks()
    vi.useRealTimers()
  })

  it('polls a published command until the device outcome arrives', async () => {
    vi.spyOn(adminApi, 'sendCommand').mockResolvedValue({ status: 'Published', code: 'MQTT_PUBLISHED', correlationId: 'c-1' })
    const read = vi.spyOn(adminApi, 'commandResult')
      .mockResolvedValueOnce({ correlationId: 'c-1', result: { status: 'Published', code: 'MQTT_PUBLISHED', correlationId: 'c-1' }, reply: null })
      .mockResolvedValueOnce({ correlationId: 'c-1', result: { status: 'Executed', code: 'DEVICE_EXECUTED', correlationId: 'c-1' }, reply: null })

    const run = runDeviceCommand('dev-1', body, {})
    await vi.advanceTimersByTimeAsync(2100)
    const outcome = await run

    expect(outcome.phase).toBe('done')
    expect(outcome.final?.result?.status).toBe('Executed')
    expect(read).toHaveBeenCalledTimes(2)
    expect(read).toHaveBeenCalledWith('dev-1', 'c-1')
  })

  it('stops at once for a command that certainly never left the server', async () => {
    vi.spyOn(adminApi, 'sendCommand').mockResolvedValue({ status: 'ContractUnavailable', code: 'DO_CHANNEL_NOT_ALLOWED', correlationId: 'c-2' })
    const read = vi.spyOn(adminApi, 'commandResult')

    const outcome = await runDeviceCommand('dev-1', body, {})

    expect(outcome.phase).toBe('not_sent')
    expect(read).not.toHaveBeenCalled()
  })

  it('keeps polling an unconfirmed publish, since the device may still run it', async () => {
    vi.spyOn(adminApi, 'sendCommand').mockResolvedValue({ status: 'Failed', code: 'MQTT_PUBLISH_UNCONFIRMED', correlationId: 'c-3' })
    vi.spyOn(adminApi, 'commandResult')
      .mockResolvedValueOnce({ correlationId: 'c-3', result: { status: 'Failed', code: 'MQTT_PUBLISH_UNCONFIRMED', correlationId: 'c-3' }, reply: null })
      .mockResolvedValueOnce({ correlationId: 'c-3', result: { status: 'Failed', code: 'DEVICE_RESPONSE_TIMEOUT', correlationId: 'c-3' }, reply: null })

    const run = runDeviceCommand('dev-1', body, {})
    await vi.advanceTimersByTimeAsync(2100)

    expect((await run).final?.result?.code).toBe('DEVICE_RESPONSE_TIMEOUT')
  })

  it('reports a client timeout when no outcome arrives in time', async () => {
    vi.spyOn(adminApi, 'sendCommand').mockResolvedValue({ status: 'Published', code: 'MQTT_PUBLISHED', correlationId: 'c-4' })
    vi.spyOn(adminApi, 'commandResult').mockResolvedValue({ correlationId: 'c-4', result: { status: 'Published', code: 'MQTT_PUBLISHED', correlationId: 'c-4' }, reply: null })

    const run = runDeviceCommand('dev-1', body, { limitMs: 3000 })
    await vi.advanceTimersByTimeAsync(4000)

    expect((await run).phase).toBe('timeout')
  })

  it('stops polling when cancelled', async () => {
    vi.spyOn(adminApi, 'sendCommand').mockResolvedValue({ status: 'Published', code: 'MQTT_PUBLISHED', correlationId: 'c-5' })
    const read = vi.spyOn(adminApi, 'commandResult').mockResolvedValue({ correlationId: 'c-5', result: { status: 'Published', code: 'MQTT_PUBLISHED', correlationId: 'c-5' }, reply: null })
    const command = useDeviceCommand(() => 'dev-1')

    const run = command.run(body)
    await vi.advanceTimersByTimeAsync(1100)
    command.cancel()
    await vi.advanceTimersByTimeAsync(5000)
    await run

    expect(read).toHaveBeenCalledTimes(1)
    expect(command.phase.value).toBe('idle')
  })
})

describe('certainlyNotSent', () => {
  it('holds only for refusals that prove nothing was published', () => {
    expect(certainlyNotSent({ status: 'ContractUnavailable', code: 'DO_CHANNEL_NOT_ALLOWED', correlationId: 'x' })).toBe(true)
    expect(certainlyNotSent({ status: 'Failed', code: 'MQTT_NOT_CONNECTED', correlationId: 'x' })).toBe(true)
    expect(certainlyNotSent({ status: 'Failed', code: 'IDEMPOTENCY_OUTCOME_UNKNOWN', correlationId: 'x' })).toBe(false)
    expect(certainlyNotSent({ status: 'Failed', code: 'MQTT_PUBLISH_UNCONFIRMED', correlationId: 'x' })).toBe(false)
    expect(certainlyNotSent({ status: 'Published', code: 'MQTT_PUBLISHED', correlationId: 'x' })).toBe(false)
  })

  it('reads the HTTP status of a failed API call', () => {
    expect(httpStatusOf(new Error('Request failed (403)'))).toBe(403)
    expect(httpStatusOf(new TypeError('Failed to fetch'))).toBeNull()
  })
})

describe('mayHaveReachedDevice', () => {
  it('treats only published or unconfirmed dispatches as possibly sent', () => {
    expect(mayHaveReachedDevice({ status: 'Published', code: 'MQTT_PUBLISHED', correlationId: 'x' })).toBe(true)
    expect(mayHaveReachedDevice({ status: 'Failed', code: 'MQTT_PUBLISH_UNCONFIRMED', correlationId: 'x' })).toBe(true)
    expect(mayHaveReachedDevice({ status: 'Failed', code: 'MQTT_NOT_CONNECTED', correlationId: 'x' })).toBe(false)
    expect(mayHaveReachedDevice({ status: 'Failed', code: 'DEVICE_PERMISSION_REQUIRED', correlationId: 'x' })).toBe(false)
    expect(mayHaveReachedDevice({ status: 'ContractUnavailable', code: 'COMMAND_PAYLOAD_INVALID', correlationId: 'x' })).toBe(false)
  })
})

describe('mayHaveRun', () => {
  const outcome = (phase: DeviceCommandOutcome['phase'], status: string, code: string, final: DeviceCommandOutcome['final'] = null): DeviceCommandOutcome =>
    ({ phase, dispatch: { status, code, correlationId: 'c' }, final })
  const done = (status: string, code: string) =>
    outcome('done', 'Published', 'MQTT_PUBLISHED', { correlationId: 'c', result: { status, code, correlationId: 'c' }, reply: null })

  it.each([
    ['ContractUnavailable', 'DEVICE_WRITE_BUSY'],
    ['TransportNotConfigured', 'X'],
    ['Failed', 'MQTT_NOT_CONNECTED'],
    ['Failed', 'MQTT_RESPONSE_SUBSCRIPTION_MISSING'],
    ['Failed', 'MQTT_COMMAND_TOPIC_INVALID'],
  ])('is false when %s/%s proves nothing was sent', (status, code) => {
    expect(mayHaveRun(outcome('not_sent', status, code))).toBe(false)
  })

  it.each([400, 403, 409])('is false for an HTTP %i refusal at the API boundary', status => {
    expect(mayHaveRun(new Error(`Request failed (${status})`))).toBe(false)
  })

  it.each([
    ['DEVICE_PERMISSION_REQUIRED'],
    ['IDEMPOTENCY_OUTCOME_UNKNOWN'],
    ['OUTCOME_UNKNOWN_AFTER_RESTART'],
  ])('is true for a refused dispatch coded %s, which a publish may precede', code => {
    expect(mayHaveRun(outcome('not_sent', 'Failed', code))).toBe(true)
  })

  it('is true for every device outcome, including a rejection and an acknowledgement without a job id', () => {
    expect(mayHaveRun(done('Failed', 'DEVICE_REJECTED'))).toBe(true)
    expect(mayHaveRun(done('Failed', 'DEVICE_RESPONSE_TIMEOUT'))).toBe(true)
    expect(mayHaveRun(done('Acknowledged', 'DEVICE_ACCEPTED'))).toBe(true)
    expect(mayHaveRun(done('Executed', 'DEVICE_EXECUTED'))).toBe(true)
    expect(mayHaveRun(outcome('not_sent', 'MqttDeferred', 'MQTT_DEFERRED'))).toBe(true)
  })

  it('is true when polling ran out, on HTTP 5xx and on a network error', () => {
    expect(mayHaveRun(outcome('timeout', 'Published', 'MQTT_PUBLISHED'))).toBe(true)
    expect(mayHaveRun(new Error('Request failed (500)'))).toBe(true)
    expect(mayHaveRun(new Error('Request failed (503)'))).toBe(true)
    expect(mayHaveRun(new TypeError('Failed to fetch'))).toBe(true)
  })
})
