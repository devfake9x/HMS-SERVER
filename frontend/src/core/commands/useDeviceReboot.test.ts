import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { effectScope } from 'vue'
import { adminApi } from '@/core/api/adminApi'
import { relaysBeforeReboot } from './rebootPlan'
import { fakeDevice, type Scenario, type SentCommand } from './testing/fakeDevice'
import { runReboot, useDeviceReboot, type RebootPhase, type RunRebootOptions } from './useDeviceReboot'

const PHRASE = 'REBOOT-gateway_870c'
const REBOOT = 'system.reboot'
const INFO = 'system.get_info'
const STATUS = 'control.get_status'
const TIME = 'system.get_time'
const catalog = { catalogVersion: 'v', catalogHash: 'h', trusted: true, actions: [] }
const allowed = [REBOOT, INFO, STATUS, TIME]

const channel = (n: number, state: boolean, forced = false) => ({ channel: n, state, mode: 'toggle', enabled: true, gpioOk: true, forced })
const relaysAfter = { channels: [channel(1, false), channel(2, true, true), channel(3, false)] }
const relaysBefore = relaysBeforeReboot({ channels: [channel(1, true), channel(2, true, true), channel(3, false)] })

const scheduled = { status: 'Executed', code: 'DEVICE_REBOOT_SCHEDULED' }
const observed = { status: 'Executed', code: 'DEVICE_REBOOT_OBSERVED' }
const executed = (code: string) => ({ status: 'Executed', code })

interface Rig {
  /** Answer for the real (second) system.reboot send. */
  real?: Scenario
  /** Answer for the dry-run (first) send. */
  dry?: Scenario
  uptimeAfter?: number | null
  status?: (n: number) => Scenario | undefined
}

function startDevice(rig: Rig = {}): SentCommand[] {
  return fakeDevice((key, _payload, n) => {
    if (key === REBOOT) return n === 1 ? (rig.dry ?? { data: { would_apply: true } }) : (rig.real ?? { results: [scheduled, observed] })
    if (key === INFO) return { data: rig.uptimeAfter === null ? {} : { uptime_seconds: rig.uptimeAfter ?? 4, firmware: '1.0.108' } }
    if (key === STATUS) return rig.status?.(n) ?? { data: relaysAfter }
    if (key === TIME) return { data: { epoch: Math.floor(Date.now() / 1000) + 2, ntp_synced: true } }
    return undefined
  })
}

const reboots = (sent: SentCommand[]) => sent.filter(s => s.key === REBOOT)
const reads = (sent: SentCommand[], key: string) => sent.filter(s => s.key === key).length
const pollsOf = (sent: SentCommand[], index: number) =>
  vi.mocked(adminApi.commandResult).mock.calls.filter(call => call[1] === reboots(sent)[index]?.correlationId).length

function options(extra: Partial<RunRebootOptions> = {}): RunRebootOptions {
  return {
    confirm: PHRASE, uptimeBefore: 500, relaysBefore, catalog, allowedCommands: allowed,
    csrfToken: 'csrf', signal: new AbortController().signal, ...extra,
  }
}

async function finish<T>(run: Promise<T>, ms = 400_000): Promise<T> {
  await vi.advanceTimersByTimeAsync(ms)
  return run
}

describe('runReboot', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-11-10T00:00:00Z'))
  })
  afterEach(() => {
    vi.restoreAllMocks()
    vi.useRealTimers()
  })

  describe('the usual path', () => {
    it('sends a dry run, then the real command, then reads the device back and verifies the signed uptime', async () => {
      const sent = startDevice({ uptimeAfter: 4 })
      const phases: RebootPhase[] = []
      const outcome = await finish(runReboot('dev-1', options({ onPhase: p => phases.push(p) })))

      expect(reboots(sent)).toHaveLength(2)
      expect(reboots(sent)[0]).toMatchObject({ dryRun: true, confirm: PHRASE, payload: {} })
      expect(reboots(sent)[1]).toMatchObject({ dryRun: false, confirm: PHRASE, payload: {} })
      expect(reboots(sent)[0].correlationId).not.toBe(reboots(sent)[1].correlationId)
      expect(reads(sent, INFO)).toBe(1)
      expect(reads(sent, STATUS)).toBe(1)
      expect(reads(sent, TIME)).toBe(1)

      expect(outcome.kind).toBe('observed')
      expect(outcome.code).toBe('DEVICE_REBOOT_OBSERVED')
      expect(outcome.verified).toBe(true)
      expect(outcome.info).toMatchObject({ uptimeSeconds: 4, firmware: '1.0.108' })
      expect(outcome.outputs?.map(o => o.channel)).toEqual([1, 2, 3])
      expect(outcome.check).toEqual({ deviations: [], notes: [] })
      expect(outcome.time).toMatchObject({ ntpSynced: true })
      expect(Math.abs(outcome.time?.skewSeconds ?? 99)).toBeLessThanOrEqual(5)
      expect(phases).toEqual(['dry_run', 'sending', 'tracking', 'reading'])
    })

    it('does not call the reboot verified when the signed uptime after is larger than before', async () => {
      startDevice({ uptimeAfter: 600 })
      const outcome = await finish(runReboot('dev-1', options()))

      expect(outcome.kind).toBe('observed')
      expect(outcome.verified).toBe(false)
    })

    it('is not verified when the uptime could not be read, and still reports the relay table', async () => {
      startDevice({ uptimeAfter: null })
      const outcome = await finish(runReboot('dev-1', options()))

      expect(outcome.verified).toBe(false)
      expect(outcome.info?.uptimeSeconds).toBeNull()
      expect(outcome.outputs).not.toBeNull()
    })

    it('reports a forced channel that is not at its level, and an unforced one that is on', async () => {
      startDevice({ status: () => ({ data: { channels: [channel(1, true), channel(2, false, true), channel(3, false)] } }) })
      const outcome = await finish(runReboot('dev-1', options()))

      expect(outcome.check?.deviations).toHaveLength(1)
      expect(outcome.check?.deviations[0]).toContain('DO2')
      expect(outcome.check?.notes[0]).toContain('DO1')
    })

    it('skips the reads the caller may not send', async () => {
      const sent = startDevice()
      const outcome = await finish(runReboot('dev-1', options({ allowedCommands: [REBOOT, INFO] })))

      expect(reads(sent, STATUS)).toBe(0)
      expect(reads(sent, TIME)).toBe(0)
      expect(outcome.kind).toBe('relay_unchecked')
      expect(outcome.info?.uptimeSeconds).toBe(4)
    })
  })

  describe('the dry run', () => {
    it.each([
      ['rejected by the device', { dry: { result: { status: 'Failed', code: 'DEVICE_REJECTED', detail: 'UNAUTHORIZED' } } }, 'DEVICE_REJECTED'],
      ['of unknown outcome', { dry: { result: { status: 'Failed', code: 'DEVICE_RESPONSE_TIMEOUT' } } }, 'DEVICE_RESPONSE_TIMEOUT'],
      ['refused before sending', { dry: { dispatch: { status: 'ContractUnavailable', code: 'DEVICE_REBOOT_IN_PROGRESS' } } }, 'DEVICE_REBOOT_IN_PROGRESS'],
    ] as Array<[string, Rig, string]>)('stops when it is %s, without sending the real command', async (_name, rig, code) => {
      const sent = startDevice(rig)
      const outcome = await finish(runReboot('dev-1', options()))

      expect(reboots(sent)).toHaveLength(1)
      expect(outcome.kind).toBe('dry_run_failed')
      expect(outcome.code).toBe(code)
      expect(reads(sent, INFO) + reads(sent, STATUS) + reads(sent, TIME)).toBe(0)
    })

    it('stops when the server fails the request', async () => {
      const sent = fakeDevice(() => ({ throws: new Error('Request failed (403)') }))
      const outcome = await finish(runReboot('dev-1', options()))

      expect(reboots(sent)).toHaveLength(1)
      expect(outcome.kind).toBe('dry_run_failed')
      expect(outcome.code).toBe('HTTP_403')
    })
  })

  describe('the real command', () => {
    it.each([
      ['DEVICE_REBOOT_IN_PROGRESS', { status: 'ContractUnavailable' }],
      ['DEVICE_REBOOT_TOO_SOON', { status: 'ContractUnavailable' }],
      ['HIGH_RISK_REQUIRES_MASTER_ADMIN', { status: 'Failed' }],
      ['CONFIRM_REQUIRED', { status: 'Failed' }],
    ])('%s means it was refused before signing: nothing is read back', async (code, status) => {
      const sent = startDevice({ real: { dispatch: { ...status, code } } })
      const outcome = await finish(runReboot('dev-1', options()))

      expect(reboots(sent)).toHaveLength(2)
      expect(outcome.kind).toBe('not_sent')
      expect(outcome.code).toBe(code)
      expect(reads(sent, INFO) + reads(sent, STATUS) + reads(sent, TIME)).toBe(0)
    })

    it('reads system.get_info once when the device refuses it (UNAUTHORIZED), and never sends it again', async () => {
      const sent = startDevice({ real: { result: { status: 'Failed', code: 'DEVICE_REJECTED', detail: 'UNAUTHORIZED' } } })
      const outcome = await finish(runReboot('dev-1', options()))

      expect(reboots(sent)).toHaveLength(2)
      expect(outcome.kind).toBe('rejected')
      expect(outcome.detail).toBe('UNAUTHORIZED')
      expect(reads(sent, INFO)).toBe(1)
      expect(reads(sent, STATUS)).toBe(0)
      expect(outcome.info?.uptimeSeconds).toBe(4)
    })
  })

  describe('following the server record', () => {
    it('takes OBSERVED on the very first answer to the real command', async () => {
      const sent = startDevice({ real: { results: [observed] } })
      const outcome = await finish(runReboot('dev-1', options()))

      expect(outcome.kind).toBe('observed')
      expect(reads(sent, INFO)).toBe(1)
      expect(pollsOf(sent, 1)).toBe(1)
    })

    it('ends at NOT_OBSERVED without reading the relays', async () => {
      const sent = startDevice({ real: { results: [executed('DEVICE_REBOOT_NOT_OBSERVED')] } })
      const outcome = await finish(runReboot('dev-1', options()))

      expect(outcome.kind).toBe('not_observed')
      expect(reads(sent, STATUS)).toBe(0)
    })

    it('ends at OUTCOME_UNKNOWN without reading the relays', async () => {
      const sent = startDevice({ real: { results: [scheduled, executed('DEVICE_REBOOT_OUTCOME_UNKNOWN')] } })
      const outcome = await finish(runReboot('dev-1', options()))

      expect(outcome.kind).toBe('outcome_unknown')
      expect(reads(sent, STATUS)).toBe(0)
    })

    it('reads system.get_info once and stops when the server does not track the reboot (DEVICE_EXECUTED)', async () => {
      const sent = startDevice({ real: { results: [executed('DEVICE_EXECUTED')] } })
      const outcome = await finish(runReboot('dev-1', options()))

      expect(outcome.kind).toBe('untracked')
      expect(reads(sent, INFO)).toBe(1)
      expect(reads(sent, STATUS)).toBe(0)
    })

    it('does not poll system.get_operations on Acknowledged, and goes on waiting for the server', async () => {
      const ack = { status: 'Acknowledged', code: 'DEVICE_ACCEPTED', detail: 'operation_id=9' }
      const sent = startDevice({ real: { results: [ack, ack, observed] } })
      const outcome = await finish(runReboot('dev-1', options()))

      expect(sent.filter(s => s.key === 'system.get_operations')).toHaveLength(0)
      expect(outcome.kind).toBe('observed')
      expect(reads(sent, INFO)).toBe(1)
    })

    it('goes on after DEVICE_RESPONSE_TIMEOUT, since the server can still upgrade the row to OBSERVED', async () => {
      const timeout = { status: 'Failed', code: 'DEVICE_RESPONSE_TIMEOUT' }
      const sent = startDevice({ real: { results: [timeout, timeout, observed] } })
      const outcome = await finish(runReboot('dev-1', options()))

      expect(outcome.kind).toBe('observed')
      expect(reboots(sent)).toHaveLength(2)
    })

    it('swallows failed polls and takes the answer that follows', async () => {
      const sent = startDevice({ real: { results: [scheduled, executed('DEVICE_REBOOT_OUTCOME_UNKNOWN')], pollError: 3 } })
      const outcome = await finish(runReboot('dev-1', options()))

      expect(outcome.kind).toBe('outcome_unknown')
      expect(pollsOf(sent, 1)).toBeGreaterThanOrEqual(5)
    })

    it('says the server restarted when the record is gone (HTTP 404)', async () => {
      startDevice({ real: { results: [scheduled, new Error('Request failed (404)')] } })
      const outcome = await finish(runReboot('dev-1', options()))

      expect(outcome.kind).toBe('server_restarted')
      expect(outcome.code).toBe('COMMAND_NOT_FOUND')
    })

    it('does not call a lost real send a server restart when the record is not found', async () => {
      const sent = startDevice({ real: { throws: new TypeError('Failed to fetch') } })
      let realId = ''
      const original = vi.mocked(adminApi.commandResult).getMockImplementation()!
      vi.mocked(adminApi.commandResult).mockImplementation(async (device, id, opts) => {
        if (id === realId) throw new Error('Request failed (404)')
        return original(device, id, opts)
      })
      const outcome = await finish(runReboot('dev-1', options({ onRealCommand: id => { realId = id } })))

      expect(outcome.kind).toBe('outcome_unknown')
      expect(outcome.code).toBe('REBOOT_SEND_UNCONFIRMED')
      expect(reboots(sent)).toHaveLength(2)
    })

    it('gives up after 180 s with a result of its own, reading system.get_info once, and does not resend', async () => {
      const sent = startDevice({ real: { results: [scheduled] } })
      const phases: RebootPhase[] = []
      const outcome = await finish(runReboot('dev-1', options({ onPhase: p => phases.push(p) })))

      expect(outcome.kind).toBe('tracking_timeout')
      expect(reads(sent, INFO)).toBe(1)
      expect(reads(sent, STATUS)).toBe(0)
      expect(reboots(sent)).toHaveLength(2)
      // 180 s at one poll per 2 s, plus the polls inside the real send.
      expect(pollsOf(sent, 1)).toBeLessThanOrEqual(100)
    })
  })

  describe('reading the device back', () => {
    it('retries control.get_status while the device answers busy, waiting as long as it asks', async () => {
      const sent = startDevice({
        status: n => n <= 2
          ? { result: { status: 'Failed', code: 'DEVICE_REJECTED', detail: 'busy' }, data: { retry_after_ms: 1000 } }
          : undefined,
      })
      const outcome = await finish(runReboot('dev-1', options()))

      expect(reads(sent, STATUS)).toBe(3)
      expect(outcome.kind).toBe('observed')
      expect(outcome.outputs).not.toBeNull()
    })

    it('retries on the exact form a real device busy takes: ESP_ERR_INVALID_STATE: busy', async () => {
      const sent = startDevice({
        status: n => n <= 2
          ? { result: { status: 'Failed', code: 'DEVICE_REJECTED', detail: 'ESP_ERR_INVALID_STATE: busy' }, data: { error: 'busy', retry_after_ms: 1000 } }
          : undefined,
      })
      const outcome = await finish(runReboot('dev-1', options()))

      expect(reads(sent, STATUS)).toBe(3)
      expect(outcome.kind).toBe('observed')
    })

    it('retries on the server read-busy refusal too', async () => {
      const sent = startDevice({ status: n => n === 1 ? { dispatch: { status: 'ContractUnavailable', code: 'DEVICE_READ_BUSY' } } : undefined })
      const outcome = await finish(runReboot('dev-1', options()))

      expect(reads(sent, STATUS)).toBe(2)
      expect(outcome.outputs).not.toBeNull()
    })

    it('reports relay_unchecked when LANE_FULL does not clear within 30 s, with the uptime check intact', async () => {
      const sent = startDevice({ status: () => ({ result: { status: 'Failed', code: 'DEVICE_REJECTED', detail: 'LANE_FULL' } }) })
      const outcome = await finish(runReboot('dev-1', options()))

      expect(outcome.kind).toBe('relay_unchecked')
      expect(outcome.outputs).toBeNull()
      expect(outcome.verified).toBe(true)
      expect(reads(sent, STATUS)).toBeGreaterThan(2)
      expect(reads(sent, STATUS)).toBeLessThan(20)
    })

    it('reports relay_unchecked when the relay table cannot be understood', async () => {
      startDevice({ status: () => ({ data: { channels: 'nope' } }) })
      const outcome = await finish(runReboot('dev-1', options()))

      expect(outcome.kind).toBe('relay_unchecked')
    })
  })

  describe('whatever happens', () => {
    it.each([
      ['usual', {}],
      ['dry run rejected', { dry: { result: { status: 'Failed', code: 'DEVICE_REJECTED' } } }],
      ['real rejected', { real: { result: { status: 'Failed', code: 'DEVICE_REJECTED', detail: 'UNAUTHORIZED' } } }],
      ['real lost', { real: { results: [{ status: 'Failed', code: 'DEVICE_RESPONSE_TIMEOUT' }] } }],
      ['real unconfirmed publish', { real: { dispatch: { status: 'Failed', code: 'MQTT_PUBLISH_UNCONFIRMED' }, results: [scheduled, observed] } }],
      ['server error on the real send', { real: { throws: new Error('Request failed (500)') } }],
      ['lost connection on the real send', { real: { throws: new TypeError('Failed to fetch') } }],
    ] as Array<[string, Rig]>)('sends system.reboot at most twice (%s)', async (_name, rig) => {
      const sent = startDevice(rig)
      await finish(runReboot('dev-1', options()))

      expect(reboots(sent).length).toBeLessThanOrEqual(2)
      expect(reboots(sent).filter(s => s.dryRun === false)).toHaveLength(rig.dry ? 0 : 1)
    })
  })
})

describe('useDeviceReboot', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => {
    vi.restoreAllMocks()
    vi.useRealTimers()
  })

  const context = () => ({ csrfToken: 'csrf', catalog, allowedCommands: allowed })
  const input = { confirm: PHRASE, uptimeBefore: 500, relaysBefore }

  function mountReboot(device: () => string = () => 'dev-1') {
    const scope = effectScope()
    const reboot = scope.run(() => useDeviceReboot(device, context))!
    return { reboot, scope }
  }

  it('runs one reboot at a time, exposing the phase and the outcome', async () => {
    startDevice()
    const { reboot, scope } = mountReboot()

    const first = reboot.run(input)
    expect(reboot.phase.value).toBe('dry_run')
    expect(await reboot.run(input)).toBeNull()
    const outcome = await finish(first)

    expect(outcome?.kind).toBe('observed')
    expect(reboot.outcome.value?.kind).toBe('observed')
    expect(reboot.phase.value).toBe('done')
    scope.stop()
  })

  it('polls at once when nudged with the id of the reboot it follows, and ignores another id', async () => {
    const sent = startDevice({ real: { results: [scheduled] } })
    const { reboot, scope } = mountReboot()
    const run = reboot.run(input)
    await vi.advanceTimersByTimeAsync(2500)
    expect(reboot.correlationId.value).toBe(reboots(sent)[1].correlationId)
    const polls = pollsOf(sent, 1)

    reboot.nudge('someone-else')
    await vi.advanceTimersByTimeAsync(0)
    expect(pollsOf(sent, 1)).toBe(polls)

    reboot.nudge(reboot.correlationId.value!)
    await vi.advanceTimersByTimeAsync(0)
    expect(pollsOf(sent, 1)).toBe(polls + 1)

    reboot.cancel()
    await finish(run)
    scope.stop()
  })

  it('returns null and polls no more once cancelled', async () => {
    const sent = startDevice({ real: { results: [scheduled] } })
    const { reboot, scope } = mountReboot()
    const run = reboot.run(input)
    await vi.advanceTimersByTimeAsync(6000)
    const polls = pollsOf(sent, 1)

    reboot.cancel()
    await vi.advanceTimersByTimeAsync(60_000)

    expect(await run).toBeNull()
    expect(pollsOf(sent, 1)).toBe(polls)
    expect(reboot.phase.value).toBe('idle')
    expect(reboot.outcome.value).toBeNull()
    expect(reboots(sent)).toHaveLength(2)
    scope.stop()
  })

  it('returns null when the page moved to another device, and keeps no outcome', async () => {
    startDevice()
    let device = 'dev-1'
    const { reboot, scope } = mountReboot(() => device)
    const run = reboot.run(input)
    await vi.advanceTimersByTimeAsync(1500)

    device = 'dev-2'
    await vi.advanceTimersByTimeAsync(60_000)

    expect(await run).toBeNull()
    expect(reboot.outcome.value).toBeNull()
    scope.stop()
  })

  it('cancels with its effect scope', async () => {
    const sent = startDevice({ real: { results: [scheduled] } })
    const { reboot, scope } = mountReboot()
    const run = reboot.run(input)
    await vi.advanceTimersByTimeAsync(6000)
    scope.stop()
    await vi.advanceTimersByTimeAsync(60_000)

    expect(await run).toBeNull()
    expect(reboots(sent)).toHaveLength(2)
  })
})
