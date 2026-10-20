import { describe, expect, it } from 'vitest'
import { filterDevices, type DeviceSummary } from './deviceFilters'

const devices: DeviceSummary[] = [
  { id: 'dev-001', name: 'North Gate', organization: 'Master company', area: 'Gate A' },
  { id: 'dev-002', name: 'Pump Room', organization: 'BuildCo East', area: null },
]

describe('filterDevices', () => {
  it('matches device identity and nullable classifications', () => {
    expect(filterDevices(devices, 'gate')).toHaveLength(1)
    expect(filterDevices(devices, 'dev-002')[0]?.name).toBe('Pump Room')
  })

  it('returns a copy for an empty query', () => {
    const result = filterDevices(devices, '  ')
    expect(result).toEqual(devices)
    expect(result).not.toBe(devices)
  })
})
