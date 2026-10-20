import { describe, expect, it } from 'vitest'
import { can, canDeviceAction, catalogIsCurrent } from './permissions'

const snapshot = (permissions: string[], expiresAt = '2030-01-01T00:00:00.000Z') => ({
  policyVersion: 4,
  expiresAt,
  permissions,
})

describe('permission snapshot', () => {
  it('fails closed for absent or expired snapshots', () => {
    expect(can(null, 'device.read')).toBe(false)
    expect(can(snapshot(['device.read'], '2020-01-01T00:00:00.000Z'), 'device.read')).toBe(false)
  })

  it('supports explicit permissions and master wildcard', () => {
    expect(can(snapshot(['device.read']), 'device.read')).toBe(true)
    expect(can(snapshot(['*']), 'device.read')).toBe(true)
    expect(can(snapshot(['device.read']), 'device.write')).toBe(false)
  })

  it('fails closed when a persisted deny overrides an allow', () => {
    expect(can({ ...snapshot(['device.read']), deniedPermissions: ['device.read'] }, 'device.read')).toBe(false)
    expect(can({ ...snapshot(['*']), deniedPermissions: ['*'] }, 'device.read')).toBe(false)
  })

  it('fails closed for malformed permission snapshots', () => {
    expect(can({ ...snapshot(['device.read']), permissions: null as unknown as string[] }, 'device.read')).toBe(false)
    expect(can({ ...snapshot(['device.read']), deniedPermissions: null as unknown as string[] }, 'device.read')).toBe(false)
  })

  it('keeps device action authority separate from feature authority', () => {
    expect(canDeviceAction(snapshot(['device.commands', 'device:d-1:action:reboot']), 'd-1', 'reboot')).toBe(true)
    expect(canDeviceAction(snapshot(['device.commands']), 'd-1', 'reboot')).toBe(false)
  })

  it('rejects stale catalog metadata', () => {
    expect(catalogIsCurrent({ version: 'v1', hash: 'a' }, { version: 'v1', hash: 'a' })).toBe(true)
    expect(catalogIsCurrent({ version: 'v1', hash: 'a' }, { version: 'v2', hash: 'a' })).toBe(false)
    expect(catalogIsCurrent(null, { version: 'v1', hash: 'a' })).toBe(false)
  })
})
