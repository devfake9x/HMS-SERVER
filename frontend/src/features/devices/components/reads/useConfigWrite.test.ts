import { describe, expect, it } from 'vitest'
import type { ReadState } from '@/core/commands/useDeviceReads'
import { staleReason } from './useConfigWrite'

const at = (iso: string) => Date.parse(iso)
const ok = (readAt: string, data: unknown = {}): ReadState => ({ kind: 'ok', data, readAt })

describe('staleReason (checked again when the operator confirms)', () => {
  const source = { key: 'adc.get_channels', readAt: '2026-11-06T10:00:00.000Z' }

  it('lets the write go when the value it was built from is still the latest, fresh reading', () => {
    const states = { 'adc.get_channels': ok(source.readAt) }
    expect(staleReason(source, states, null, at('2026-11-06T10:00:59.000Z'))).toBeNull()
  })

  it('stops it when the card read the value again while the confirm was open', () => {
    // Someone disabled the channel; the new reading must not be overwritten with the frozen one.
    const states = { 'adc.get_channels': ok('2026-11-06T10:00:30.000Z', [{ ai: 1, enabled: false }]) }
    expect(staleReason(source, states, null, at('2026-11-06T10:00:40.000Z'))).toMatch(/đọc lại/)
  })

  it('stops it when the reading turned older than a minute, measured at confirm time', () => {
    const states = { 'adc.get_channels': ok(source.readAt) }
    expect(staleReason(source, states, null, at('2026-11-06T10:01:00.001Z'))).toMatch(/60 s/)
  })

  it('stops it when another operator changed the config after the reading', () => {
    const states = { 'adc.get_channels': ok(source.readAt) }
    expect(staleReason(source, states, at('2026-11-06T10:00:10.000Z'), at('2026-11-06T10:00:20.000Z'))).toMatch(/vừa đổi/)
  })

  it('stops it when the reading is gone or failed', () => {
    expect(staleReason(source, {}, null, at('2026-11-06T10:00:10.000Z'))).not.toBeNull()
    expect(staleReason(source, { 'adc.get_channels': { kind: 'error', readAt: source.readAt } }, null,
      at('2026-11-06T10:00:10.000Z'))).not.toBeNull()
  })
})
