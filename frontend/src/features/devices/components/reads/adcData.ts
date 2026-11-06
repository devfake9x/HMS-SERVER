import type { ReadState } from '@/core/commands/useDeviceReads'

export interface AdcRow {
  ai: number
  /** True when the row came from adc.get_channels, the only read that carries a channel's record. */
  inChannels: boolean
  name?: unknown
  type?: unknown
  enabled?: unknown
  value?: unknown
  unit?: unknown
  quality?: unknown
}

// The spec allows `data` to be an array; accept the same list wrapped in an object too.
export function listOf(state: ReadState | undefined, wrapper: string): Record<string, unknown>[] {
  if (state?.kind !== 'ok') return []
  const data = state.data as unknown
  const items = Array.isArray(data) ? data
    : data && typeof data === 'object' ? (data as Record<string, unknown>)[wrapper] : null
  return Array.isArray(items) ? items.filter(x => x && typeof x === 'object') as Record<string, unknown>[] : []
}

/** Channel names from get_channels joined to values from get_values by `ai`; a channel present
 *  in only one of them is still shown. */
export function adcRows(states: Record<string, ReadState>): AdcRow[] {
  const rows = new Map<number, AdcRow>()
  const row = (ai: unknown) => {
    // Only a real channel number: null or '' would otherwise become channel 0.
    const key = typeof ai === 'number' ? ai : typeof ai === 'string' && ai.trim() !== '' ? Number(ai) : NaN
    if (!Number.isInteger(key)) return null
    if (!rows.has(key)) rows.set(key, { ai: key, inChannels: false })
    return rows.get(key)!
  }
  for (const ch of listOf(states['adc.get_channels'], 'channels')) {
    const r = row(ch.ai)
    if (r) Object.assign(r, { inChannels: true, name: ch.name, type: ch.type, enabled: ch.enabled })
  }
  for (const v of listOf(states['adc.get_values'], 'values')) {
    const r = row(v.ai)
    if (r) Object.assign(r, { value: v.value, unit: v.unit, quality: v.quality })
  }
  return [...rows.values()].sort((a, b) => a.ai - b.ai)
}

/** The whole channel record as get_channels last reported it, for the channel numbered `ai`. */
export function channelRecord(state: ReadState | undefined, ai: number): Record<string, unknown> | null {
  return listOf(state, 'channels').find(ch => ch.ai === ai) ?? null
}

/** The ADC reports no hardware: nothing of it can be configured. */
export function adcUnavailable(states: Record<string, ReadState>): boolean {
  return ['adc.get_config', 'adc.get_channels'].some(key => {
    const state = states[key]
    return state?.kind === 'ok' && state.data !== null && typeof state.data === 'object' &&
      (state.data as Record<string, unknown>).available === false
  })
}

/** Fields whose value differs between the record the write should have produced and the one read back. */
export function differingFields(expected: Record<string, unknown>, actual: Record<string, unknown>): string[] {
  const keys = new Set([...Object.keys(expected), ...Object.keys(actual)])
  return [...keys].filter(key => JSON.stringify(expected[key]) !== JSON.stringify(actual[key]))
}
