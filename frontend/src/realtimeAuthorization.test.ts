import { describe, expect, it } from 'vitest'
import { applyRealtimeEvent, type RealtimeState } from './realtimeAuthorization'

const initial = (): RealtimeState<string> => ({ epoch: 4, devices: new Map([['d1', 'old']]), pending: new Map() })

describe('applyRealtimeEvent', () => {
  it('discards stale events', () => {
    const next = applyRealtimeEvent(initial(), { type: 'DeviceStatus', authorizationEpoch: 3, policyVersion: 1, deviceId: 'd1', device: 'stale' })
    expect(next).toEqual(initial())
  })

  it('applies a reconnect snapshot at the new epoch', () => {
    const next = applyRealtimeEvent(initial(), { type: 'Snapshot', authorizationEpoch: 5, policyVersion: 1, devices: new Map([['d2', 'fresh']]) })
    expect(next.epoch).toBe(5)
    expect([...next.devices.keys()]).toEqual(['d2'])
  })

  it('rejects a snapshot with the same epoch but a lower policy version', () => {
    const state: RealtimeState<string> = {
      ...initial(),
      policyVersions: new Map([['d1', 4]]),
    }
    const next = applyRealtimeEvent(state, {
      type: 'Snapshot', authorizationEpoch: 4, policyVersion: 3,
      devices: new Map([['d2', 'stale']]),
    })
    expect(next).toBe(state)
  })

  it('aborts and removes pending work for devices removed by a snapshot', () => {
    const controller = new AbortController()
    const state = { ...initial(), pending: new Map([['d1', controller]]) }
    const next = applyRealtimeEvent(state, {
      type: 'Snapshot', authorizationEpoch: 5, policyVersion: 1,
      devices: new Map([['d2', 'fresh']]),
    })
    expect(next.pending.has('d1')).toBe(false)
    expect(controller.signal.aborted).toBe(true)
  })

  it('revocation removes membership and aborts pending work', () => {
    const controller = new AbortController()
    const state = { ...initial(), pending: new Map([['d1', controller]]) }
    const next = applyRealtimeEvent(state, { type: 'DeviceRevoked', authorizationEpoch: 4, policyVersion: 1, deviceId: 'd1' })
    expect(next.devices.has('d1')).toBe(false)
    expect(next.pending.has('d1')).toBe(false)
    expect(controller.signal.aborted).toBe(true)
  })

  it('tracks authorization epochs and policy versions per device', () => {
    const state: RealtimeState<string> = {
      ...initial(),
      deviceEpochs: new Map([['d1', 4]]),
      policyVersions: new Map([['d1', 2]]),
    }
    const stale = applyRealtimeEvent(state, { type: 'DeviceStatus', authorizationEpoch: 4, policyVersion: 1, deviceId: 'd1', device: 'stale' })
    expect(stale.devices.get('d1')).toBe('old')
    const fresh = applyRealtimeEvent(state, { type: 'DeviceStatus', authorizationEpoch: 5, policyVersion: 3, deviceId: 'd1', device: 'fresh' })
    expect(fresh.devices.get('d1')).toBe('fresh')
    expect(fresh.deviceEpochs?.get('d1')).toBe(5)
    expect(fresh.policyVersions?.get('d1')).toBe(3)
  })

  it('discards stale revocation events and accepts a newer revocation', () => {
    const controller = new AbortController()
    const state: RealtimeState<string> = { ...initial(), deviceEpochs: new Map([['d1', 4]]), policyVersions: new Map([['d1', 2]]), pending: new Map([['d1', controller]]) }
    const stale = applyRealtimeEvent(state, { type: 'DeviceRevoked', authorizationEpoch: 4, policyVersion: 1, deviceId: 'd1' })
    expect(stale.devices.has('d1')).toBe(true)
    const fresh = applyRealtimeEvent(state, { type: 'DeviceRevoked', authorizationEpoch: 4, policyVersion: 3, deviceId: 'd1' })
    expect(fresh.devices.has('d1')).toBe(false)
    expect(controller.signal.aborted).toBe(true)
  })
})
