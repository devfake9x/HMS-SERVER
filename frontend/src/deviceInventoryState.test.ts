import { describe, expect, it } from 'vitest'
import { readDeviceInventoryQuery, writeDeviceInventoryQuery } from './deviceInventoryState'

describe('device inventory URL state', () => {
  it('round trips server filter fields and drops blank values', () => {
    const query = readDeviceInventoryQuery('?q= pump &organizationId=o1&projectId=p1&areaId=')
    expect(query).toEqual({ q: 'pump', projectId: 'p1', organizationId: 'o1', packageId: undefined, areaId: undefined })
    expect(writeDeviceInventoryQuery(query)).toBe('?q=pump&organizationId=o1&projectId=p1')
  })
})
