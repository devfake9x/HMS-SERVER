import { describe, expect, it } from 'vitest'
import { canRenderCommand, commandViewState, findCatalogAction, requiresCommandConfirmation } from './commandAvailability'

describe('command availability', () => {
  const catalog = { version: 'v1', hash: 'hash' }
  const action = { type: 'system', action: 'reboot', available: true, dangerous: true, requiresConfirmation: false, featurePermission: 'device.command' }

  it('does not render actions from stale catalogs', () => {
    expect(canRenderCommand(action, catalog, catalog)).toBe(true)
    expect(canRenderCommand(action, catalog, { version: 'v2', hash: 'hash' })).toBe(false)
    expect(canRenderCommand({ ...action, available: false }, catalog, catalog)).toBe(false)
  })

  it('requires explicit confirmation for dangerous actions', () => {
    expect(requiresCommandConfirmation(action)).toBe(true)
    expect(requiresCommandConfirmation({ ...action, dangerous: false })).toBe(false)
  })

  it('keeps deferred delivery distinct from acknowledgement', () => {
    expect(commandViewState('MQTT_DEFERRED')).toBe('deferred')
    expect(commandViewState('ACKNOWLEDGED')).toBe('acknowledged')
    expect(commandViewState('DEVICE_REVOKED')).toBe('revoked')
  })

  it('only resolves actions that the server returned in its catalog', () => {
    expect(findCatalogAction([action], 'system', 'reboot')).toEqual(action)
    expect(findCatalogAction([action], 'device', 'status.read')).toBeNull()
  })
})
