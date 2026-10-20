import { describe, expect, it, vi } from 'vitest'
import { adminApi, getCsrf, login, logout } from './adminApi'

function response(value: unknown, status = 200) {
  return new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } })
}

describe('adminApi', () => {
  it('sends same-origin credentials and CSRF on mutations without exposing secret payloads', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(response({ id: 'd1', name: 'Pump', ownerOrganizationId: 'o1', state: 'Provisioning', keyGeneration: 1 }))
    await adminApi.createDevice({ name: 'Pump', ownerOrganizationId: 'o1', areaId: null }, { fetchImpl, csrfToken: 'csrf' })
    expect(fetchImpl).toHaveBeenCalledWith('/api/v1/devices', expect.objectContaining({ credentials: 'same-origin', method: 'POST', headers: expect.any(Headers) }))
    const init = fetchImpl.mock.calls[0][1] as RequestInit
    const headers = init.headers as Headers
    expect(headers.get('X-CSRF-TOKEN')).toBe('csrf')
    expect(String(init.body)).toContain('ownerOrganizationId')
    expect(String(init.body)).not.toContain('password')
  })

  it('does not treat a failed authorization response as an empty resource list', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(response({ code: 'FORBIDDEN' }, 403))
    await expect(adminApi.listOrganizations({ fetchImpl })).rejects.toThrow('403')
  })

  it('uses server permission and catalog snapshots', async () => {
    const fetchImpl = vi.fn()
      .mockResolvedValueOnce(response({ policyVersion: 4, expiresAt: '2030-01-01T00:00:00Z', permissions: ['hms.devices.read'], deniedPermissions: [] }))
      .mockResolvedValueOnce(response({ catalogVersion: 'idf-pinned', catalogHash: 'abc', trusted: true }))
    await expect(adminApi.permissions({ fetchImpl })).resolves.toMatchObject({ policyVersion: 4 })
    await expect(adminApi.commandCatalog({ fetchImpl })).resolves.toMatchObject({ trusted: true })
  })

  it('revalidates the command catalog with a no-store request', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(response({ catalogVersion: 'v2', catalogHash: 'new', trusted: true }))
    await expect(adminApi.revalidateCommandCatalog({ fetchImpl })).resolves.toMatchObject({ catalogVersion: 'v2' })
    expect(fetchImpl).toHaveBeenCalledWith('/api/v1/me/command-catalog', expect.objectContaining({ cache: 'no-store', credentials: 'same-origin' }))
  })

  it('loads the current realtime authorization proof with no-store semantics', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(response({ deviceId: 'd1', authorizationEpoch: 7, policyVersion: 2 }))
    await expect(adminApi.realtimeAuthorization('d1', { fetchImpl })).resolves.toEqual({ deviceId: 'd1', authorizationEpoch: 7, policyVersion: 2 })
    expect(fetchImpl).toHaveBeenCalledWith('/api/v1/devices/d1/realtime-authorization', expect.objectContaining({ cache: 'no-store', credentials: 'same-origin' }))
  })

  it('gets CSRF from the dedicated endpoint', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(response({ requestToken: 'csrf' }))
    await expect(getCsrf({ fetchImpl })).resolves.toEqual({ requestToken: 'csrf' })
    expect(fetchImpl).toHaveBeenCalledWith('/api/v1/auth/csrf', expect.objectContaining({ credentials: 'same-origin' }))
  })

  it('propagates the session abort signal to authenticated reads', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(response({ requestToken: 'csrf' }))
    const controller = new AbortController()
    await getCsrf({ fetchImpl, signal: controller.signal })
    expect((fetchImpl.mock.calls[0][1] as RequestInit).signal).toBe(controller.signal)
  })

  it('serializes strong identifier request fields as { value }', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(response({ id: 'p1', name: 'P', organizationId: 'o1' }))
    await adminApi.createProject('P', 'o1', { fetchImpl, csrfToken: 'csrf' })
    const body = JSON.parse(String((fetchImpl.mock.calls[0][1] as RequestInit).body))
    expect(body.organizationId).toEqual({ value: 'o1' })
  })

  it('sends classification and device edits as scoped PUT mutations', async () => {
    const fetchImpl = vi.fn().mockImplementation(() => Promise.resolve(response({ id: 'x1', name: 'Renamed' })))
    await adminApi.updateOrganization('o1', 'Org 2', { fetchImpl, csrfToken: 'csrf' })
    await adminApi.updateProject('p1', 'Project 2', { fetchImpl, csrfToken: 'csrf' })
    await adminApi.updatePackage('k1', 'Package 2', { fetchImpl, csrfToken: 'csrf' })
    await adminApi.updateArea('a1', 'Area 2', { fetchImpl, csrfToken: 'csrf' })
    await adminApi.updateDevice('d1', { name: 'Device 2', projectId: null, packageId: 'k1', areaId: null }, { fetchImpl, csrfToken: 'csrf' })
    expect(fetchImpl).toHaveBeenCalledTimes(5)
    for (const [url, init] of fetchImpl.mock.calls) {
      expect(url).toMatch(/\/api\/v1\/(organizations|projects|packages|areas|devices)\/\w+/)
      expect(init.method).toBe('PUT')
      expect((init.headers as Headers).get('X-CSRF-TOKEN')).toBe('csrf')
    }
    expect(JSON.parse(String(fetchImpl.mock.calls[4][1].body))).toMatchObject({
      name: 'Device 2', projectId: null, packageId: 'k1', areaId: null,
    })
  })

  it('uses CSRF for login and logout and never returns password data', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
    await login('operator@example.test', 'secret', { fetchImpl, csrfToken: 'csrf' })
    await logout({ fetchImpl, csrfToken: 'csrf' })
    const loginBody = JSON.parse(String((fetchImpl.mock.calls[0][1] as RequestInit).body))
    expect(loginBody).toEqual({ email: 'operator@example.test', password: 'secret', rememberMe: false })
    expect((fetchImpl.mock.calls[0][1] as RequestInit).credentials).toBe('same-origin')
  })

  it('loads users and sends scoped permission grants through the server authority', async () => {
    const fetchImpl = vi.fn()
      .mockResolvedValueOnce(response([{ id: 'u1', email: 'u@example.test', emailConfirmed: true, roles: ['operator'] }]))
      .mockResolvedValueOnce(response({ userId: 'u1', permission: 'devices.manage', deny: false }))
    await expect(adminApi.listUsers({ fetchImpl })).resolves.toHaveLength(1)
    await adminApi.grantFeature({ userId: 'u1', permission: 'devices.manage' }, { fetchImpl, csrfToken: 'csrf' })
    const init = fetchImpl.mock.calls[1][1] as RequestInit
    expect(init.method).toBe('POST')
    expect((init.headers as Headers).get('X-CSRF-TOKEN')).toBe('csrf')
    expect(JSON.parse(String(init.body))).toEqual({ userId: 'u1', permission: 'devices.manage' })
  })

  it('loads the target permission grant snapshot including explicit denies', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(response({
      userId: 'u1',
      featureGrants: [{ userId: 'u1', permission: 'devices.manage', deny: true, policyVersion: 9 }],
      resourceGrants: [{ userId: 'u1', organizationId: 'o1', deny: false, policyVersion: 9 }],
      deviceActionGrants: [{ userId: 'u1', deviceId: 'd1', action: 'status.read', deny: true, policyVersion: 9 }],
      policyVersion: 9,
    }))
    await expect(adminApi.permissionGrants('u1', { fetchImpl })).resolves.toMatchObject({
      policyVersion: 9,
      featureGrants: [{ permission: 'devices.manage', deny: true }],
      deviceActionGrants: [{ action: 'status.read', deny: true }],
    })
    expect(fetchImpl).toHaveBeenCalledWith('/api/v1/permissions/u1', expect.objectContaining({ credentials: 'same-origin', cache: 'no-store' }))
  })

  it('submits license metadata and transient base64 envelope with CSRF', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(response({ state: 'Applied', code: 'LICENSE_APPLIED', licenseId: 'l1', rawSha256: 'hash' }))
    const rawEnvelope = 'AQID'
    await expect(adminApi.applyLicense('d1', { kid: 'kid-1', issuer: 'hms', generation: 2, rawEnvelope }, { fetchImpl, csrfToken: 'csrf' })).resolves.toMatchObject({ state: 'Applied' })
    const init = fetchImpl.mock.calls[0][1] as RequestInit
    expect(fetchImpl.mock.calls[0][0]).toBe('/api/v1/devices/d1/licenses/apply')
    expect((init.headers as Headers).get('X-CSRF-TOKEN')).toBe('csrf')
    expect(JSON.parse(String(init.body))).toEqual({ kid: 'kid-1', issuer: 'hms', generation: 2, rawEnvelope })
  })

  it('submits commands through the deferred transport contract and preserves correlation', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(response({ status: 'MqttDeferred', code: 'MQTT_DEFERRED', correlationId: 'corr-1' }))
    await expect(adminApi.sendCommand('d1', { type: 'device', action: 'status.read', payload: { verbose: true }, correlationId: 'corr-1', dryRun: true, catalogVersion: 'idf-pinned', catalogHash: 'abc' }, { fetchImpl, csrfToken: 'csrf' })).resolves.toMatchObject({ code: 'MQTT_DEFERRED' })
    const init = fetchImpl.mock.calls[0][1] as RequestInit
    expect(fetchImpl.mock.calls[0][0]).toBe('/api/v1/devices/d1/commands')
    expect((init.headers as Headers).get('X-CSRF-TOKEN')).toBe('csrf')
    expect(JSON.parse(String(init.body)).correlationId).toBe('corr-1')
    expect(JSON.parse(String(init.body)).catalogVersion).toBe('idf-pinned')
    expect(JSON.parse(String(init.body)).catalogHash).toBe('abc')
  })

  it('builds a same-origin one-time provisioning download endpoint without reading secret bytes', () => {
    expect(adminApi.downloadBundle('d1', 'b1')).toBe('/api/v1/devices/d1/provisioning-bundles/b1/download')
  })

  it('fetches device permissions matrix with no-store cache policy', async () => {
    const mockMatrix = {
      deviceId: 'dev-1',
      deviceName: 'Station-1',
      policyVersion: 12,
      permissions: [
        { key: 'view', title: 'View', description: 'Can view device' },
      ],
      users: [],
    }
    const fetchImpl = vi.fn().mockResolvedValue(response(mockMatrix))
    const result = await adminApi.devicePermissions('dev-1', { fetchImpl })
    expect(result).toEqual(mockMatrix)
    expect(fetchImpl).toHaveBeenCalledWith('/api/v1/devices/dev-1/permissions', expect.objectContaining({
      cache: 'no-store',
      credentials: 'same-origin',
    }))
  })

  it('updates device permissions with CSRF token and optimistic policy version', async () => {
    const mockUpdated = {
      deviceId: 'dev-1',
      deviceName: 'Station-1',
      policyVersion: 13,
      permissions: [],
      users: [],
    }
    const fetchImpl = vi.fn().mockResolvedValue(response(mockUpdated))
    const payload = {
      expectedPolicyVersion: 12,
      grants: [
        { userId: 'u1', action: 'edit_basics', granted: true },
      ],
    }
    const result = await adminApi.updateDevicePermissions('dev-1', payload, { fetchImpl, csrfToken: 'csrf-secret' })
    expect(result).toEqual(mockUpdated)
    const [url, init] = fetchImpl.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/api/v1/devices/dev-1/permissions')
    expect(init.method).toBe('PUT')
    expect((init.headers as Headers).get('X-CSRF-TOKEN')).toBe('csrf-secret')
    expect(JSON.parse(String(init.body))).toEqual(payload)
  })
})
