export interface AdminApiOptions {
  fetchImpl?: typeof fetch
  csrfToken?: string
  signal?: AbortSignal
}

export interface CsrfResponse { requestToken: string }
export interface OrganizationDto { id: string; name: string; parentId: string | null }
export interface ProjectDto { id: string; name: string; organizationId: string }
export interface PackageDto { id: string; name: string; projectId: string }
export interface AreaDto { id: string; name: string; organizationId: string }
export interface DeviceDto {
  id: string
  name: string
  ownerOrganizationId: string
  projectId: string | null
  packageId: string | null
  areaId: string | null
  state: string
  keyGeneration: number
}

export interface PermissionSnapshot {
  policyVersion: number
  expiresAt: string
  permissions: string[]
  deniedPermissions: string[]
}

export interface FeaturePermissionGrantDto {
  userId: string | { value: string }
  permission: string
  deny: boolean
  policyVersion: number
}
export interface ResourceScopeGrantDto {
  userId: string | { value: string }
  organizationId: string | { value: string }
  deny: boolean
  policyVersion: number
}
export interface DeviceActionGrantDto {
  userId: string | { value: string }
  deviceId: string | { value: string }
  action: string
  deny: boolean
  policyVersion: number
}
export interface PermissionGrantSnapshot {
  userId: string | { value: string }
  featureGrants: FeaturePermissionGrantDto[]
  resourceGrants: ResourceScopeGrantDto[]
  deviceActionGrants: DeviceActionGrantDto[]
  policyVersion: number
}

export interface CommandCatalogSnapshot {
  catalogVersion: string
  catalogHash: string
  trusted: boolean
  actions: CommandCatalogAction[]
}
export interface CommandCatalogAction {
  type: string
  action: string
  available: boolean
  dangerous: boolean
  requiresConfirmation: boolean
  featurePermission: string
  idempotent: boolean
  defaultTtlMs: number | null
}

export interface ProvisioningBundleMetadata {
  bundleId: string
  deviceId: { value: string }
  expiresAt: string
  used: boolean
}
export interface LicenseApplyResult {
  state: 'Draft' | 'Signed' | 'Applied' | 'RecoveryRequired' | 'Rejected' | string
  code: string
  licenseId: string | null
  rawSha256: string | null
}
export interface CommandExecutionResult {
  status: 'Validated' | 'Recorded' | 'MqttDeferred' | 'TransportNotConfigured' | 'ContractUnavailable' | 'Published' | 'Acknowledged' | 'Executed' | 'Failed' | string
  code: string
  correlationId: string
  detail?: string | null
  keyGeneration?: number | null
}
export interface ManagedUserDto { id: string; email: string; emailConfirmed: boolean; roles: string[] }
export interface RealtimeAuthorizationDto { deviceId: string; authorizationEpoch: number; policyVersion: number }

export interface DevicePermissionDefinitionDto {
  key: string
  title: string
  description: string
}

export interface DevicePermissionGrantDetailDto {
  granted: boolean
  inherited: boolean
  inheritedReason?: string | null
}

export interface DeviceUserPermissionRowDto {
  userId: string
  displayName: string
  email: string
  role: string
  grants: Record<string, DevicePermissionGrantDetailDto>
}

export interface DevicePermissionMatrixDto {
  deviceId: string
  deviceName: string
  policyVersion: number
  permissions: DevicePermissionDefinitionDto[]
  users: DeviceUserPermissionRowDto[]
}

export interface UpdateDevicePermissionsRequest {
  expectedPolicyVersion?: number
  grants: Array<{
    userId: string
    action: string
    granted: boolean
  }>
}

async function request<T>(endpoint: string, init: RequestInit = {}, options: AdminApiOptions = {}): Promise<T> {
  const fetchImpl = options.fetchImpl ?? fetch
  const headers = new Headers(init.headers)
  headers.set('Accept', 'application/json')
  if (init.body !== undefined) headers.set('Content-Type', 'application/json')
  if (options.csrfToken) headers.set('X-CSRF-TOKEN', options.csrfToken)
  const response = await fetchImpl(endpoint, { ...init, credentials: 'same-origin', headers, signal: options.signal })
  if (!response.ok) throw new Error(`Request failed (${response.status})`)
  if (response.status === 204) return undefined as T
  return await response.json() as T
}

export function getCsrf(options?: AdminApiOptions) {
  return request<CsrfResponse>('/api/v1/auth/csrf', {}, options)
}

export function login(email: string, password: string, options: AdminApiOptions = {}) {
  return request<void>('/api/v1/auth/login', { method: 'POST', body: JSON.stringify({ email, password, rememberMe: false }) }, options)
}

export function logout(options: AdminApiOptions = {}) {
  return request<void>('/api/v1/auth/logout', { method: 'POST' }, options)
}

const strongId = (value: string | null | undefined) => value ? { value } : null

export const adminApi = {
  listOrganizations: (options?: AdminApiOptions) => request<OrganizationDto[]>('/api/v1/organizations', {}, options),
  listProjects: (options?: AdminApiOptions) => request<ProjectDto[]>('/api/v1/projects', {}, options),
  listPackages: (options?: AdminApiOptions) => request<PackageDto[]>('/api/v1/packages', {}, options),
  listAreas: (options?: AdminApiOptions) => request<AreaDto[]>('/api/v1/areas', {}, options),
  listDevices: (query = '', options?: AdminApiOptions) => request<DeviceDto[]>(`/api/v1/devices${query}`, {}, options),
  permissions: (options?: AdminApiOptions) => request<PermissionSnapshot>('/api/v1/me/permissions', {}, options),
  permissionGrants: (userId: string, options?: AdminApiOptions) => request<PermissionGrantSnapshot>(`/api/v1/permissions/${encodeURIComponent(userId)}`, { cache: 'no-store' }, options),
  commandCatalog: (options?: AdminApiOptions) => request<CommandCatalogSnapshot>('/api/v1/me/command-catalog', {}, options),
  realtimeAuthorization: (deviceId: string, options?: AdminApiOptions) => request<RealtimeAuthorizationDto>(`/api/v1/devices/${encodeURIComponent(deviceId)}/realtime-authorization`, { cache: 'no-store' }, options),
  revalidateCommandCatalog: (options?: AdminApiOptions) => request<CommandCatalogSnapshot>('/api/v1/me/command-catalog', { cache: 'no-store' }, options),
  createOrganization: (name: string, parentId: string | null, options: AdminApiOptions) => request<OrganizationDto>('/api/v1/organizations', { method: 'POST', body: JSON.stringify({ name, parentId: strongId(parentId) }) }, options),
  updateOrganization: (id: string, name: string, options: AdminApiOptions) => request<OrganizationDto>(`/api/v1/organizations/${encodeURIComponent(id)}`, { method: 'PUT', body: JSON.stringify({ name }) }, options),
  createProject: (name: string, organizationId: string, options: AdminApiOptions) => request<ProjectDto>('/api/v1/projects', { method: 'POST', body: JSON.stringify({ name, organizationId: strongId(organizationId) }) }, options),
  updateProject: (id: string, name: string, options: AdminApiOptions) => request<ProjectDto>(`/api/v1/projects/${encodeURIComponent(id)}`, { method: 'PUT', body: JSON.stringify({ name }) }, options),
  createPackage: (name: string, projectId: string, options: AdminApiOptions) => request<PackageDto>('/api/v1/packages', { method: 'POST', body: JSON.stringify({ name, projectId: strongId(projectId) }) }, options),
  updatePackage: (id: string, name: string, options: AdminApiOptions) => request<PackageDto>(`/api/v1/packages/${encodeURIComponent(id)}`, { method: 'PUT', body: JSON.stringify({ name }) }, options),
  createArea: (name: string, organizationId: string, options: AdminApiOptions) => request<AreaDto>('/api/v1/areas', { method: 'POST', body: JSON.stringify({ name, organizationId: strongId(organizationId) }) }, options),
  updateArea: (id: string, name: string, options: AdminApiOptions) => request<AreaDto>(`/api/v1/areas/${encodeURIComponent(id)}`, { method: 'PUT', body: JSON.stringify({ name }) }, options),
  createDevice: (body: { name: string; ownerOrganizationId: string; projectId?: string | null; packageId?: string | null; areaId?: string | null }, options: AdminApiOptions) => request<DeviceDto>('/api/v1/devices', { method: 'POST', body: JSON.stringify({ ...body, ownerOrganizationId: strongId(body.ownerOrganizationId) }) }, options),
  // Update DTOs use nullable Guid properties, unlike create DTOs that use value objects.
  updateDevice: (id: string, body: { name: string; projectId?: string | null; packageId?: string | null; areaId?: string | null }, options: AdminApiOptions) => request<DeviceDto>(`/api/v1/devices/${encodeURIComponent(id)}`, { method: 'PUT', body: JSON.stringify(body) }, options),
  revokeDevice: (deviceId: string, options: AdminApiOptions) => request<void>(`/api/v1/devices/${encodeURIComponent(deviceId)}/revoke`, { method: 'POST' }, options),
  createBundle: (deviceId: string, body: { brokerHost: string; brokerPort: number; brokerMode: string }, options: AdminApiOptions) => request<ProvisioningBundleMetadata>(`/api/v1/devices/${encodeURIComponent(deviceId)}/provisioning-bundles`, { method: 'POST', body: JSON.stringify(body) }, options),
  downloadBundle: (deviceId: string, bundleId: string, options: AdminApiOptions = {}) => `/api/v1/devices/${encodeURIComponent(deviceId)}/provisioning-bundles/${encodeURIComponent(bundleId)}/download`,
  applyLicense: (deviceId: string, body: { kid: string; issuer: string; generation: number; rawEnvelope: string }, options: AdminApiOptions) => request<LicenseApplyResult>(`/api/v1/devices/${encodeURIComponent(deviceId)}/licenses/apply`, { method: 'POST', body: JSON.stringify(body) }, options),
  sendCommand: (deviceId: string, body: { type: string; action: string; payload?: Record<string, unknown>; correlationId?: string; requestedAt?: string; dryRun?: boolean; catalogVersion?: string; catalogHash?: string }, options: AdminApiOptions) => request<CommandExecutionResult>(`/api/v1/devices/${encodeURIComponent(deviceId)}/commands`, { method: 'POST', body: JSON.stringify(body) }, options),
  listUsers: (options?: AdminApiOptions) => request<ManagedUserDto[]>('/api/v1/users', {}, options),
  createUser: (body: { email: string; password: string; emailConfirmed?: boolean; organizationId?: string | null }, options: AdminApiOptions) => request<ManagedUserDto>('/api/v1/users', { method: 'POST', body: JSON.stringify({ ...body, organizationId: strongId(body.organizationId) }) }, options),
  createRole: (name: string, options: AdminApiOptions) => request<void>('/api/v1/users/roles', { method: 'POST', body: JSON.stringify({ name }) }, options),
  assignRole: (userId: string, role: string, options: AdminApiOptions) => request<void>(`/api/v1/users/${encodeURIComponent(userId)}/roles/${encodeURIComponent(role)}`, { method: 'PUT' }, options),
  grantFeature: (body: { userId: string; permission: string; deny?: boolean }, options: AdminApiOptions) => request<unknown>('/api/v1/permissions/features', { method: 'POST', body: JSON.stringify(body) }, options),
  grantResourceScope: (body: { userId: string; organizationId: string; deny?: boolean }, options: AdminApiOptions) => request<unknown>('/api/v1/permissions/resource-scopes', { method: 'POST', body: JSON.stringify(body) }, options),
  grantDeviceAction: (body: { userId: string; deviceId: string; action: string; deny?: boolean }, options: AdminApiOptions) => request<unknown>('/api/v1/permissions/device-actions', { method: 'POST', body: JSON.stringify(body) }, options),
  devicePermissions: (deviceId: string, options?: AdminApiOptions) =>
    request<DevicePermissionMatrixDto>(`/api/v1/devices/${encodeURIComponent(deviceId)}/permissions`, { cache: 'no-store' }, options),
  updateDevicePermissions: (deviceId: string, body: UpdateDevicePermissionsRequest, options: AdminApiOptions) =>
    request<DevicePermissionMatrixDto>(`/api/v1/devices/${encodeURIComponent(deviceId)}/permissions`, { method: 'PUT', body: JSON.stringify(body) }, options),
}
