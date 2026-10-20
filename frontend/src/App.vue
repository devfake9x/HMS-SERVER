<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { filterDevices, type DeviceSummary } from './deviceFilters'
import { adminApi, getCsrf, login, logout, type CommandCatalogSnapshot, type PermissionSnapshot, type ManagedUserDto } from './adminApi'
import { downloadSecretBlob } from './secureDownload'
import { canRenderCommand, findCatalogAction, type CatalogAction } from './commandAvailability'
import { can as canPermission } from './permissions'
import { readDeviceInventoryQuery, writeDeviceInventoryQuery } from './deviceInventoryState'
import { createCatalogVerification, invalidateCatalogVerification, verifyCatalog } from './catalogVerification'
import { createSessionFence, type SessionLease } from './sessionFence'
import { createRequestRevision } from './requestRevision'
import { useDeviceOperationsHub } from './useDeviceOperationsHub'
import { createSignalRConnection } from './signalRConnection'
import PermissionGrantEditor from './PermissionGrantEditor.vue'
import { getCurrentInstance } from 'vue'
import AppShell from './shared/layouts/AppShell.vue'

const isRouterActive = computed(() => {
  const instance = getCurrentInstance()
  return !!instance?.appContext.components['RouterView']
})
const currentRoute = computed(() => {
  const instance = getCurrentInstance()
  return (instance?.proxy as any)?.$route
})

type DeviceState = 'Active' | 'Provisioning' | 'Revoked'

interface DeviceRow extends DeviceSummary {
  id: string
  name: string
  organization: string
  area: string | null
  projectId: string | null
  packageId: string | null
  state: DeviceState
  lastSeen: string
}

const activeSection = ref('Overview')
const search = ref('')
const inventoryOrganization = ref('')
const inventoryProject = ref('')
const inventoryPackage = ref('')
const inventoryArea = ref('')
const devices = ref<DeviceRow[]>([])
const transportStatus = ref<'loading' | 'deferred' | 'unavailable'>('loading')
const inventoryStatus = ref<'loading' | 'ready' | 'unavailable'>('loading')
const resourceItems = ref<Array<{ id: string; name: string; context?: string }>>([])
const resourceStatus = ref<'idle' | 'loading' | 'ready' | 'unavailable'>('idle')
const csrfToken = ref<string | null>(null)
const permissions = ref<PermissionSnapshot | null>(null)
const catalog = ref<CommandCatalogSnapshot | null>(null)
const catalogFresh = ref(false)
const catalogProof = ref<{ version: string; hash: string } | null>(null)
const catalogVerification = createCatalogVerification<CommandCatalogSnapshot>()
const sessionFence = createSessionFence()
const sessionSignal = ref<AbortSignal>(sessionFence.capture().signal)
const sectionRequestRevision = createRequestRevision()
const inventoryRequestRevision = createRequestRevision()
const realtimeStatus = ref<'disabled' | 'connecting' | 'connected' | 'unavailable'>('disabled')
let operationsHub: ReturnType<typeof useDeviceOperationsHub<unknown>>
const actionMessage = ref('')
const showLogin = ref(false)
const loginEmail = ref('')
const loginPassword = ref('')
const loginError = ref('')
const createName = ref('')
const createParent = ref('')
const createOrganization = ref('')
const createProject = ref('')
const createPackage = ref('')
const createArea = ref('')
const createDeviceName = ref('')
const createDeviceOrganization = ref('')
const createDeviceProject = ref('')
const createDevicePackage = ref('')
const createDeviceArea = ref('')
const editResourceId = ref('')
const editResourceName = ref('')
const editDeviceId = ref('')
const editDeviceName = ref('')
const editDeviceProject = ref('')
const editDevicePackage = ref('')
const editDeviceArea = ref('')
const organizations = ref<Array<{ id: string; name: string }>>([])
const projects = ref<Array<{ id: string; name: string; organizationId: string }>>([])
const packages = ref<Array<{ id: string; name: string; projectId: string }>>([])
const areas = ref<Array<{ id: string; name: string; organizationId: string }>>([])
const users = ref<ManagedUserDto[]>([])
const usersStatus = ref<'idle' | 'loading' | 'ready' | 'unavailable'>('idle')
const userEmail = ref('')
const userPassword = ref('')
const userOrganization = ref('')
const roleName = ref('')
const roleAssignment = ref<Record<string, string>>({})
const grantUserId = ref('')
const grantPermission = ref('')
const grantOrganization = ref('')
const grantDevice = ref('')
const grantAction = ref('')
const provisioningDeviceId = ref('')
const provisioningBrokerHost = ref('')
const provisioningBrokerPort = ref(1883)
const provisioningBundle = ref<{ bundleId: string; deviceId: { value: string }; expiresAt: string; used: boolean } | null>(null)
const licenseDeviceId = ref('')
const licenseKid = ref('')
const licenseIssuer = ref('')
const licenseGeneration = ref(1)
const licenseFile = ref<File | null>(null)
const licenseInput = ref<HTMLInputElement | null>(null)
const commandDeviceId = ref('')
const commandType = ref('')
const commandAction = ref('')
const commandCorrelation = ref('')
const commandResult = ref<{ status: string; code: string; correlationId: string } | null>(null)
const commandCatalogActions = computed<CatalogAction[]>(() => catalog.value?.actions ?? [])
const selectedCommandAction = computed(() => findCatalogAction(commandCatalogActions.value, commandType.value, commandAction.value))
const commandCanSubmit = computed(() => {
  const snapshot = catalog.value
  if (!snapshot?.trusted || !selectedCommandAction.value) return false
  return catalogFresh.value && catalogProof.value !== null && canRenderCommand(selectedCommandAction.value,
    catalogProof.value,
    { version: snapshot.catalogVersion, hash: snapshot.catalogHash }) &&
    can(selectedCommandAction.value.featurePermission)
})
const resourceRoutes: Record<string, string> = {
  Organizations: '/api/v1/organizations',
  Projects: '/api/v1/projects',
  Packages: '/api/v1/packages',
  Areas: '/api/v1/areas',
}

operationsHub = useDeviceOperationsHub({
  createConnection: createSignalRConnection,
  authorizationEpoch: async deviceId => {
    const lease = sessionFence.capture()
    const proof = await adminApi.realtimeAuthorization(deviceId, { signal: lease.signal })
    if (!sessionFence.isCurrent(lease)) throw new Error('SESSION_CHANGED')
    return proof.authorizationEpoch
  },
  onEvent: event => {
    if (event.eventType.toLowerCase() === 'status') {
      const device = devices.value.find(entry => entry.id === event.deviceId)
      if (device) device.lastSeen = new Date().toISOString()
    }
  },
  onRevoked: event => {
    devices.value = devices.value.filter(entry => entry.id !== event.deviceId)
    if (commandDeviceId.value === event.deviceId) commandResult.value = null
    actionMessage.value = `Device ${event.deviceId} access was revoked by the server.`
  },
})

async function loadSessionState(lease: SessionLease = sessionFence.capture()) {
  const inventoryRevision = inventoryRequestRevision.next()
  try {
    const urlState = readDeviceInventoryQuery(window.location.search)
    search.value = urlState.q ?? ''
    inventoryOrganization.value = urlState.organizationId ?? ''
    inventoryProject.value = urlState.projectId ?? ''
    inventoryPackage.value = urlState.packageId ?? ''
    inventoryArea.value = urlState.areaId ?? ''
    const csrf = await getCsrf({ signal: lease.signal }).catch(() => null)
    if (!sessionFence.isCurrent(lease)) return
    csrfToken.value = csrf?.requestToken ?? null
    const [transportResponse, deviceResponse, organizationPayload, projectPayload, packagePayload, areaPayload] = await Promise.all([
      fetch('/api/v1/command-transport/status', { credentials: 'same-origin', signal: lease.signal }),
      adminApi.listDevices(writeDeviceInventoryQuery({ q: search.value, organizationId: inventoryOrganization.value, projectId: inventoryProject.value, packageId: inventoryPackage.value, areaId: inventoryArea.value }), { signal: lease.signal }),
      adminApi.listOrganizations({ signal: lease.signal }).catch(() => []),
      adminApi.listProjects({ signal: lease.signal }).catch(() => []),
      adminApi.listPackages({ signal: lease.signal }).catch(() => []),
      adminApi.listAreas({ signal: lease.signal }).catch(() => []),
    ])
    if (!sessionFence.isCurrent(lease)) return
    const [permissionSnapshot, catalogSnapshot] = await Promise.all([
      adminApi.permissions({ signal: lease.signal }).catch(() => null),
      adminApi.commandCatalog({ signal: lease.signal }).catch(() => null),
    ])
    if (!sessionFence.isCurrent(lease)) return
    permissions.value = permissionSnapshot
    catalog.value = catalogSnapshot
    catalogFresh.value = false
    catalogProof.value = null
    if (catalogSnapshot) {
      await verifyCatalog(catalogVerification, null, async () => catalogSnapshot, true)
      if (!sessionFence.isCurrent(lease)) return
      catalogProof.value = catalogVerification.verified
      catalogFresh.value = catalogVerification.verified !== null
    }

    if (transportResponse.ok) {
      const payload = await transportResponse.json() as { status?: string }
      if (!sessionFence.isCurrent(lease)) return
      transportStatus.value = payload.status === 'MQTT_DEFERRED' ? 'deferred' : 'unavailable'
    } else {
      transportStatus.value = 'unavailable'
    }

    if (deviceResponse && inventoryRequestRevision.isCurrent(inventoryRevision)) {
      devices.value = deviceResponse.map((device) => ({
        id: device.id,
        name: device.name,
        organization: device.ownerOrganizationId,
        area: device.areaId,
        projectId: device.projectId,
        packageId: device.packageId,
        state: device.state as DeviceState,
        lastSeen: 'not loaded',
      }))
      inventoryStatus.value = 'ready'
    } else if (inventoryRequestRevision.isCurrent(inventoryRevision)) {
      inventoryStatus.value = 'unavailable'
    }
    organizations.value = organizationPayload
    projects.value = projectPayload
    packages.value = packagePayload
    areas.value = areaPayload
  } catch {
    if (!sessionFence.isCurrent(lease)) return
    transportStatus.value = 'unavailable'
    if (inventoryRequestRevision.isCurrent(inventoryRevision)) {
      inventoryStatus.value = 'unavailable'
    }
  }
}

async function revalidateCatalog(lease: SessionLease = sessionFence.capture()) {
  const previous = catalog.value
  if (!previous?.trusted) {
    catalogFresh.value = false
    catalogProof.value = null
    return false
  }
  const current = await verifyCatalog(catalogVerification, previous, () => adminApi.revalidateCommandCatalog({ signal: lease.signal }))
  if (!sessionFence.isCurrent(lease)) return false
  catalogFresh.value = current
  catalogProof.value = catalogVerification.verified
  if (!current) {
    catalogFresh.value = false
    catalogProof.value = null
    catalog.value = catalog.value ? { ...catalog.value, trusted: false } : null
    commandType.value = ''
    commandAction.value = ''
    actionMessage.value = 'The command catalog changed or could not be verified. Reload the catalog before submitting again.'
  }
  return current
}

function onWindowFocus() { void revalidateCatalog() }

async function syncCommandDeviceSubscription(deviceId: string, previousDeviceId?: string) {
  if (realtimeStatus.value !== 'connected') return
  if (previousDeviceId && previousDeviceId !== deviceId) {
    await operationsHub.leaveDevice(previousDeviceId).catch(() => undefined)
  }
  if (!deviceId || operationsHub.requiresReauthorization.value.has(deviceId)) return
  await operationsHub.joinDevice(deviceId).catch(() => undefined)
}

async function connectRealtime(lease: SessionLease = sessionFence.capture()): Promise<void> {
  if (!sessionFence.isCurrent(lease)) return
  realtimeStatus.value = 'connecting'
  try {
    await operationsHub.connect()
    if (!sessionFence.isCurrent(lease)) return
    realtimeStatus.value = 'connected'
    await syncCommandDeviceSubscription(commandDeviceId.value)
  } catch {
    if (sessionFence.isCurrent(lease)) realtimeStatus.value = 'unavailable'
  }
}

onMounted(() => {
  const lease = sessionFence.capture()
  void loadSessionState(lease)
  void connectRealtime(lease)
  window.addEventListener('focus', onWindowFocus)
})

onUnmounted(() => {
  window.removeEventListener('focus', onWindowFocus)
  void operationsHub.disconnect()
  sessionFence.invalidate()
})

watch(commandDeviceId, (deviceId, previousDeviceId) => {
  void syncCommandDeviceSubscription(deviceId.trim(), previousDeviceId?.trim())
})

watch(activeSection, async (section) => {
  const lease = sessionFence.capture()
  const requestRevision = sectionRequestRevision.next()
  const isCurrentSectionRequest = () =>
    sessionFence.isCurrent(lease) &&
    sectionRequestRevision.isCurrent(requestRevision) &&
    activeSection.value === section
  if (section === 'Users' || section === 'Permissions') {
    usersStatus.value = 'loading'
    try {
      const loadedUsers = await adminApi.listUsers({ signal: lease.signal })
      if (!isCurrentSectionRequest()) return
      users.value = loadedUsers
      usersStatus.value = 'ready'
    } catch {
      if (!isCurrentSectionRequest()) return
      users.value = []
      usersStatus.value = 'unavailable'
    }
  }
  const route = resourceRoutes[section]
  if (!route) {
    resourceItems.value = []
    resourceStatus.value = 'idle'
    return
  }

  resourceStatus.value = 'loading'
  try {
    const payload = section === 'Organizations'
      ? await adminApi.listOrganizations({ signal: lease.signal })
      : section === 'Projects'
        ? await adminApi.listProjects({ signal: lease.signal })
        : section === 'Packages'
          ? await adminApi.listPackages({ signal: lease.signal })
          : await adminApi.listAreas({ signal: lease.signal })
    if (!isCurrentSectionRequest()) return
    resourceItems.value = payload.map((item) => ({
      id: item.id,
      name: item.name,
      context: 'organizationId' in item ? item.organizationId : 'projectId' in item ? item.projectId : undefined,
    }))
    resourceStatus.value = 'ready'
  } catch {
    if (!isCurrentSectionRequest()) return
    resourceItems.value = []
    resourceStatus.value = 'unavailable'
  }
})

async function revokeDevice(deviceId: string) {
  actionMessage.value = ''
  const lease = sessionFence.capture()
  try {
    await adminApi.revokeDevice(deviceId, { csrfToken: csrfToken.value ?? undefined, signal: lease.signal })
    if (!sessionFence.isCurrent(lease)) return
    const device = devices.value.find((entry) => entry.id === deviceId)
    if (device) device.state = 'Revoked'
    actionMessage.value = 'Device revoked. The server will enforce the revoked state for future operations.'
  } catch {
    if (sessionFence.isCurrent(lease)) actionMessage.value = 'Device revoke failed. The server remains the final authority.'
  }
}

const filteredDevices = computed(() => {
  return filterDevices(devices.value, search.value)
})

const navigation = ['Overview', 'Organizations', 'Devices', 'Permissions', 'Provisioning', 'Licenses']
navigation.splice(2, 0, 'Projects', 'Packages', 'Areas')
navigation.splice(navigation.indexOf('Permissions'), 0, 'Users')

function can(permission: string) {
  return canPermission(permissions.value, permission)
}

const sectionPermissions: Record<string, string | undefined> = {
  Overview: undefined,
  Organizations: 'organizations.manage',
  Projects: 'projects.manage',
  Packages: 'packages.manage',
  Areas: 'areas.manage',
  Devices: 'devices.manage',
  Users: 'users.manage',
  Permissions: 'permissions.manage',
  Provisioning: 'provisioning.manage',
  Licenses: 'licenses.manage',
}

function sectionAllowed(section: string) {
  const permission = sectionPermissions[section]
  return !permission || can(permission)
}

async function performLogin() {
  if (authBusy.value) return
  authBusy.value = true
  const email = loginEmail.value
  const password = loginPassword.value
  clearSessionState()
  loginError.value = ''
  const lease = sessionFence.rotate()
  sessionSignal.value = lease.signal
  try {
    const csrf = await getCsrf({ signal: lease.signal })
    if (!sessionFence.isCurrent(lease)) return
    csrfToken.value = csrf.requestToken
    await login(email, password, { csrfToken: csrfToken.value, signal: lease.signal })
    if (!sessionFence.isCurrent(lease)) return
    loginPassword.value = ''
    showLogin.value = false
    await loadSessionState(lease)
    if (!sessionFence.isCurrent(lease)) return
    await connectRealtime(lease)
    if (!sessionFence.isCurrent(lease)) return
    actionMessage.value = 'Signed in.'
  } catch {
    if (!sessionFence.isCurrent(lease)) return
    loginError.value = 'Sign in failed. Check credentials or server availability.'
  } finally {
    authBusy.value = false
  }
}

const authBusy = ref(false)
async function performLogout() {
  if (authBusy.value) return
  authBusy.value = true
  const csrf = csrfToken.value ?? undefined
  const lease = sessionFence.rotate()
  sessionSignal.value = lease.signal
  clearSessionState()
  showLogin.value = true
  try {
    await logout({ csrfToken: csrf, signal: lease.signal })
  } catch {
    if (sessionFence.isCurrent(lease)) loginError.value = 'Local state cleared. Server sign out could not be confirmed; retry before sharing this browser.'
  } finally {
    if (sessionFence.isCurrent(lease)) sessionFence.invalidate()
    authBusy.value = false
  }
}

function clearSessionState() {
  void operationsHub.disconnect().catch(() => undefined)
  realtimeStatus.value = 'disabled'
  devices.value = []
  users.value = []
  organizations.value = []
  projects.value = []
  packages.value = []
  areas.value = []
  csrfToken.value = null
  loginPassword.value = ''
  userPassword.value = ''
  userEmail.value = ''
  userOrganization.value = ''
  roleAssignment.value = {}
  roleName.value = ''
  grantUserId.value = ''
  grantPermission.value = ''
  grantOrganization.value = ''
  grantDevice.value = ''
  grantAction.value = ''
  createName.value = ''
  createParent.value = ''
  createOrganization.value = ''
  createProject.value = ''
  createPackage.value = ''
  createArea.value = ''
  createDeviceName.value = ''
  createDeviceOrganization.value = ''
  createDeviceProject.value = ''
  createDevicePackage.value = ''
  createDeviceArea.value = ''
  editResourceId.value = ''
  editResourceName.value = ''
  editDeviceId.value = ''
  editDeviceName.value = ''
  editDeviceProject.value = ''
  editDevicePackage.value = ''
  editDeviceArea.value = ''
  provisioningDeviceId.value = ''
  provisioningBrokerHost.value = ''
  licenseDeviceId.value = ''
  licenseKid.value = ''
  licenseIssuer.value = ''
  commandDeviceId.value = ''
  commandAction.value = ''
  commandType.value = ''
  commandCorrelation.value = ''
  actionMessage.value = ''
  resourceItems.value = []
  provisioningBundle.value = null
  commandResult.value = null
  licenseFile.value = null
  if (licenseInput.value) licenseInput.value.value = ''
  usersStatus.value = 'idle'
  resourceStatus.value = 'idle'
  inventoryStatus.value = 'unavailable'
  permissions.value = null
  catalog.value = null
  catalogFresh.value = false
  invalidateCatalogVerification(catalogVerification)
  catalogProof.value = null
  sectionRequestRevision.next()
  inventoryRequestRevision.next()
  showLogin.value = true
}

async function createClassification(kind: 'organization' | 'project' | 'package' | 'area') {
  const lease = sessionFence.capture()
  const requestedSection = kind === 'organization' ? 'Organizations' : `${kind[0].toUpperCase()}${kind.slice(1)}s`
  const option = { csrfToken: csrfToken.value ?? undefined, signal: lease.signal }
  try {
    if (kind === 'organization') {
      if (!can('organizations.manage')) throw new Error('permission')
      await adminApi.createOrganization(createName.value.trim(), createParent.value.trim() || null, option)
    } else if (kind === 'project') {
      if (!can('projects.manage')) throw new Error('permission')
      await adminApi.createProject(createProject.value.trim(), createOrganization.value.trim(), option)
    } else if (kind === 'package') {
      if (!can('packages.manage')) throw new Error('permission')
      await adminApi.createPackage(createPackage.value.trim(), createProject.value.trim(), option)
    } else {
      if (!can('areas.manage')) throw new Error('permission')
      await adminApi.createArea(createArea.value.trim(), createOrganization.value.trim(), option)
    }
    if (!sessionFence.isCurrent(lease)) return
    actionMessage.value = `${kind} created.`
    if (activeSection.value === requestedSection) activeSection.value = requestedSection
  } catch {
    if (sessionFence.isCurrent(lease)) actionMessage.value = 'Create failed or permission is expired/denied.'
  }
}

function beginResourceEdit(item: { id: string; name: string }) {
  editResourceId.value = item.id
  editResourceName.value = item.name
}

function cancelResourceEdit() {
  editResourceId.value = ''
  editResourceName.value = ''
}

async function updateClassification() {
  const lease = sessionFence.capture()
  const requestedSection = activeSection.value
  const id = editResourceId.value
  const name = editResourceName.value.trim()
  if (!id || !name) return
  const option = { csrfToken: csrfToken.value ?? undefined, signal: lease.signal }
  try {
    if (activeSection.value === 'Organizations') {
      if (!can('organizations.manage')) throw new Error('permission')
      await adminApi.updateOrganization(id, name, option)
    } else if (activeSection.value === 'Projects') {
      if (!can('projects.manage')) throw new Error('permission')
      await adminApi.updateProject(id, name, option)
    } else if (activeSection.value === 'Packages') {
      if (!can('packages.manage')) throw new Error('permission')
      await adminApi.updatePackage(id, name, option)
    } else {
      if (!can('areas.manage')) throw new Error('permission')
      await adminApi.updateArea(id, name, option)
    }
    if (!sessionFence.isCurrent(lease) || activeSection.value !== requestedSection || editResourceId.value !== id) return
    const current = resourceItems.value.find(item => item.id === id)
    if (current) current.name = name
    cancelResourceEdit()
    actionMessage.value = `${activeSection.value.slice(0, -1)} updated.`
  } catch {
    if (sessionFence.isCurrent(lease)) actionMessage.value = 'Update failed or permission is expired/denied.'
  }
}

function beginDeviceEdit(device: DeviceRow) {
  editDeviceId.value = device.id
  editDeviceName.value = device.name
  editDeviceProject.value = device.projectId ?? ''
  editDevicePackage.value = device.packageId ?? ''
  editDeviceArea.value = device.area ?? ''
}

async function updateDevice() {
  const lease = sessionFence.capture()
  const requestedSection = activeSection.value
  const id = editDeviceId.value
  if (!id || !editDeviceName.value.trim() || !can('devices.manage')) return
  try {
    await adminApi.updateDevice(id, {
      name: editDeviceName.value.trim(),
      projectId: editDeviceProject.value || null,
      packageId: editDevicePackage.value || null,
      areaId: editDeviceArea.value || null,
    }, { csrfToken: csrfToken.value ?? undefined, signal: lease.signal })
    if (!sessionFence.isCurrent(lease) || activeSection.value !== requestedSection || editDeviceId.value !== id) return
    const current = devices.value.find(device => device.id === id)
    if (current) {
      current.name = editDeviceName.value.trim()
      current.projectId = editDeviceProject.value || null
      current.packageId = editDevicePackage.value || null
      current.area = editDeviceArea.value || null
    }
    editDeviceId.value = ''
    actionMessage.value = 'Device updated.'
  } catch {
    if (sessionFence.isCurrent(lease)) actionMessage.value = 'Device update failed or permission is expired/denied.'
  }
}

async function createDevice() {
  const lease = sessionFence.capture()
  try {
    if (!can('devices.manage')) throw new Error('permission')
    await adminApi.createDevice({
      name: createDeviceName.value.trim(),
      ownerOrganizationId: createDeviceOrganization.value.trim(),
      projectId: createDeviceProject.value || null,
      packageId: createDevicePackage.value || null,
      areaId: createDeviceArea.value || null,
    }, { csrfToken: csrfToken.value ?? undefined, signal: lease.signal })
    if (!sessionFence.isCurrent(lease)) return
    actionMessage.value = 'Device created.'
    await loadInventory()
  } catch { if (sessionFence.isCurrent(lease)) actionMessage.value = 'Create failed or permission is expired/denied.' }
}

async function loadInventory() {
  const lease = sessionFence.capture()
  const requestRevision = inventoryRequestRevision.next()
  try {
    const payload = await adminApi.listDevices(writeDeviceInventoryQuery({ q: search.value, organizationId: inventoryOrganization.value, projectId: inventoryProject.value, packageId: inventoryPackage.value, areaId: inventoryArea.value }), { signal: lease.signal })
    if (!sessionFence.isCurrent(lease) || !inventoryRequestRevision.isCurrent(requestRevision)) return
    devices.value = payload.map((device) => ({ id: device.id, name: device.name, organization: device.ownerOrganizationId, area: device.areaId, projectId: device.projectId, packageId: device.packageId, state: device.state as DeviceState, lastSeen: 'not loaded' }))
    inventoryStatus.value = 'ready'
  } catch {
    if (sessionFence.isCurrent(lease) && inventoryRequestRevision.isCurrent(requestRevision)) {
      inventoryStatus.value = 'unavailable'
    }
  }
}

watch([search, inventoryOrganization, inventoryProject, inventoryPackage, inventoryArea], () => {
  const query = writeDeviceInventoryQuery({ q: search.value, organizationId: inventoryOrganization.value, projectId: inventoryProject.value, packageId: inventoryPackage.value, areaId: inventoryArea.value })
  window.history.replaceState(null, '', `${window.location.pathname}${query}`)
  void loadInventory()
})

async function createUser() {
  const lease = sessionFence.capture()
  try {
    if (!can('users.manage')) throw new Error('permission')
    await adminApi.createUser({ email: userEmail.value.trim(), password: userPassword.value, organizationId: userOrganization.value.trim() || null }, { csrfToken: csrfToken.value ?? undefined, signal: lease.signal })
    if (!sessionFence.isCurrent(lease)) return
    userPassword.value = ''
    const loadedUsers = await adminApi.listUsers({ signal: lease.signal })
    if (!sessionFence.isCurrent(lease)) return
    users.value = loadedUsers
    actionMessage.value = 'User created. The password is not retained by the client.'
  } catch { if (sessionFence.isCurrent(lease)) actionMessage.value = 'User creation failed or permission is expired/denied.' }
}

async function createRole() {
  const lease = sessionFence.capture()
  try {
    if (!can('users.manage')) throw new Error('permission')
    await adminApi.createRole(roleName.value.trim(), { csrfToken: csrfToken.value ?? undefined, signal: lease.signal })
    if (!sessionFence.isCurrent(lease)) return
    actionMessage.value = 'Role created.'
    roleName.value = ''
  } catch { if (sessionFence.isCurrent(lease)) actionMessage.value = 'Role creation failed or reserved-role policy denied it.' }
}

async function assignRole(user: ManagedUserDto) {
  const role = roleAssignment.value[user.id]?.trim()
  if (!role) return
  const lease = sessionFence.capture()
  try {
    await adminApi.assignRole(user.id, role, { csrfToken: csrfToken.value ?? undefined, signal: lease.signal })
    if (!sessionFence.isCurrent(lease)) return
    const loadedUsers = await adminApi.listUsers({ signal: lease.signal })
    if (!sessionFence.isCurrent(lease)) return
    users.value = loadedUsers
    actionMessage.value = 'Role assignment submitted to the server.'
  } catch { if (sessionFence.isCurrent(lease)) actionMessage.value = 'Role assignment failed or scope was denied.' }
}

async function grant(kind: 'feature' | 'resource' | 'device') {
  const lease = sessionFence.capture()
  try {
    if (!can('permissions.manage')) throw new Error('permission')
    const options = { csrfToken: csrfToken.value ?? undefined, signal: lease.signal }
    if (kind === 'feature') await adminApi.grantFeature({ userId: grantUserId.value.trim(), permission: grantPermission.value.trim() }, options)
    if (kind === 'resource') await adminApi.grantResourceScope({ userId: grantUserId.value.trim(), organizationId: grantOrganization.value.trim() }, options)
    if (kind === 'device') await adminApi.grantDeviceAction({ userId: grantUserId.value.trim(), deviceId: grantDevice.value.trim(), action: grantAction.value.trim() }, options)
    if (!sessionFence.isCurrent(lease)) return
    actionMessage.value = 'Permission grant submitted. The backend remains the final authority.'
  } catch { if (sessionFence.isCurrent(lease)) actionMessage.value = 'Permission grant failed or target scope was denied.' }
}

async function createProvisioningBundle() {
  const lease = sessionFence.capture()
  try {
    if (!can('provisioning.manage')) throw new Error('permission')
    const bundle = await adminApi.createBundle(provisioningDeviceId.value.trim(), {
      brokerHost: provisioningBrokerHost.value.trim(), brokerPort: provisioningBrokerPort.value, brokerMode: 'PRIMARY_ONLY',
    }, { csrfToken: csrfToken.value ?? undefined, signal: lease.signal })
    if (!sessionFence.isCurrent(lease)) return
    provisioningBundle.value = bundle
    actionMessage.value = 'Bundle metadata created. Download it once; the server consumes it on download.'
  } catch { if (sessionFence.isCurrent(lease)) actionMessage.value = 'Provisioning bundle creation failed or permission was denied.' }
}

async function downloadProvisioningBundle() {
  const bundle = provisioningBundle.value
  if (!bundle) return
  const lease = sessionFence.capture()
  try {
    await downloadSecretBlob({
      endpoint: adminApi.downloadBundle(bundle.deviceId.value, bundle.bundleId),
      filename: `hms-${bundle.deviceId.value.replaceAll('-', '')}.bundle.json`,
      csrfToken: csrfToken.value ?? undefined,
      signal: lease.signal,
      expectedContentType: 'application/json',
      maxBytes: 64 * 1024,
    })
    if (!sessionFence.isCurrent(lease)) return
    provisioningBundle.value = { ...bundle, used: true }
    actionMessage.value = 'Bundle downloaded once. Keep it under operator control and import it locally into the device.'
  } catch { if (sessionFence.isCurrent(lease)) actionMessage.value = 'Bundle download failed; no secret bytes were retained by the page.' }
}

function onLicenseFile(event: Event) {
  licenseFile.value = (event.target as HTMLInputElement).files?.[0] ?? null
}

function bytesToBase64(bytes: Uint8Array): string {
  let binary = ''
  for (let offset = 0; offset < bytes.length; offset += 0x8000) {
    binary += String.fromCharCode(...bytes.subarray(offset, offset + 0x8000))
  }
  return btoa(binary)
}

async function applyLicense() {
  const file = licenseFile.value
  if (!file) return
  const lease = sessionFence.capture()
  try {
    if (!can('licenses.manage')) throw new Error('permission')
    const bytes = new Uint8Array(await file.arrayBuffer())
    if (!sessionFence.isCurrent(lease)) return
    const rawEnvelope = bytesToBase64(bytes)
    const result = await adminApi.applyLicense(licenseDeviceId.value.trim(), {
      kid: licenseKid.value.trim(), issuer: licenseIssuer.value.trim(), generation: licenseGeneration.value, rawEnvelope,
    }, { csrfToken: csrfToken.value ?? undefined, signal: lease.signal })
    if (!sessionFence.isCurrent(lease)) return
    actionMessage.value = `License result: ${result.code}. Raw license bytes were discarded from page state.`
  } catch { if (sessionFence.isCurrent(lease)) actionMessage.value = 'License apply failed or permission was denied.' }
  finally {
    if (sessionFence.isCurrent(lease) && licenseFile.value === file) {
      licenseFile.value = null
      if (licenseInput.value) licenseInput.value.value = ''
    }
  }
}

async function sendDeferredCommand() {
  const lease = sessionFence.capture()
  const commandDevice = commandDeviceId.value.trim()
  if (!commandDevice) return
  const commandAbort = new AbortController()
  operationsHub.registerPendingCommand(commandDevice, commandAbort)
  try {
    if (!await revalidateCatalog(lease)) throw new Error('catalog-stale')
    if (!sessionFence.isCurrent(lease)) return
    if (!commandCanSubmit.value) throw new Error('catalog')
    const currentCatalog = catalog.value
    if (!currentCatalog) throw new Error('catalog')
    commandResult.value = await adminApi.sendCommand(commandDevice, {
      type: commandType.value, action: commandAction.value, correlationId: commandCorrelation.value.trim() || undefined,
      catalogVersion: currentCatalog.catalogVersion,
      catalogHash: currentCatalog.catalogHash,
    }, { csrfToken: csrfToken.value ?? undefined, signal: AbortSignal.any([lease.signal, commandAbort.signal]) })
    if (!sessionFence.isCurrent(lease)) return
    if (commandResult.value.code === 'CATALOG_STALE') {
      catalog.value = { ...currentCatalog, trusted: false }
      catalogFresh.value = false
      catalogProof.value = null
      invalidateCatalogVerification(catalogVerification)
      actionMessage.value = 'The command catalog changed on the server. Reload the catalog before submitting again.'
    } else {
      actionMessage.value = commandResult.value.code === 'MQTT_DEFERRED'
        ? 'Command validated and recorded; MQTT delivery remains deferred.'
        : `Command result: ${commandResult.value.code}.`
    }
  } catch { if (sessionFence.isCurrent(lease) && !commandAbort.signal.aborted) actionMessage.value = 'Command was rejected by the server policy or transport boundary.' }
  finally {
    operationsHub.clearPendingCommand(commandDevice, commandAbort)
  }
}
</script>

<template>
  <div v-if="isRouterActive" class="h-screen w-screen overflow-hidden">
    <component :is="'router-view'" v-if="currentRoute?.name === 'login' || currentRoute?.path === '/login'" />
    <AppShell v-else @sign-out="performLogout" />
  </div>

  <main v-else class="shell">
    <aside class="rail">
      <div class="brand">
        <span class="brand-mark">H</span>
        <div>
          <strong>HMS</strong>
          <small>CONTROL PLANE</small>
        </div>
        <p v-if="actionMessage" class="action-message" role="status">{{ actionMessage }}</p>
      </div>

      <nav aria-label="Primary navigation">
        <button
          v-for="item in navigation"
          :key="item"
          class="nav-item"
          :class="{ selected: activeSection === item }"
          type="button"
          :disabled="!sectionAllowed(item)"
          @click="sectionAllowed(item) && (activeSection = item)"
        >
          <span class="nav-dot" />
          {{ item }}
        </button>
      </nav>

      <div class="rail-footer">
        <span class="status-dot" />
        <span>Core services ready</span>
      </div>
    </aside>

    <section class="content">
      <header class="topbar">
        <div>
          <p class="eyebrow">MASTER COMPANY / {{ activeSection.toUpperCase() }}</p>
          <h1>{{ activeSection }}</h1>
        </div>
        <div class="operator">
          <span class="avatar">AD</span>
          <span>Admin operator</span>
          <button class="quiet-button" type="button" @click="showLogin = true">Sign in</button>
          <button class="quiet-button" type="button" @click="performLogout">Sign out</button>
        </div>
      </header>

      <section v-if="showLogin" class="workspace-card login-card" aria-label="Sign in">
        <div class="card-heading"><div><p class="eyebrow">AUTHENTICATED CONTROL PLANE</p><h2>Sign in</h2></div></div>
        <form class="form-grid" @submit.prevent="performLogin">
          <label>Email<input v-model="loginEmail" type="email" autocomplete="username" required /></label>
          <label>Password<input v-model="loginPassword" type="password" autocomplete="current-password" required /></label>
          <button class="primary-button" type="submit">Sign in</button>
        </form>
        <p v-if="loginError" class="empty">{{ loginError }}</p>
      </section>

      <section class="deferred-banner" aria-label="MQTT status">
        <span class="signal-icon">~</span>
        <div>
          <strong>{{ transportStatus === 'unavailable' ? 'Command transport is unavailable' : 'MQTT delivery is deferred' }}</strong>
          <p>{{ transportStatus === 'unavailable' ? 'The server status endpoint is unavailable. No command is considered delivered.' : 'Commands are validated and audited by the control plane. Live device delivery will be enabled after the firmware and broker contract is proven.' }}</p>
          <small>Realtime authorization channel: {{ realtimeStatus }}</small>
        </div>
        <span class="banner-code">{{ transportStatus === 'unavailable' ? 'TRANSPORT_NOT_CONFIGURED' : 'MQTT_DEFERRED' }}</span>
      </section>

      <section v-if="activeSection === 'Overview' || activeSection === 'Devices'" class="metric-grid" aria-label="System overview">
        <article class="metric-card accent-coral">
          <span class="metric-label">Managed devices</span>
          <strong>—</strong>
          <small>awaiting server inventory</small>
        </article>
        <article class="metric-card accent-sky">
          <span class="metric-label">Active now</span>
          <strong>—</strong>
          <small>no live status loaded</small>
        </article>
        <article class="metric-card accent-amber">
          <span class="metric-label">Pending setup</span>
          <strong>—</strong>
          <small>no live status loaded</small>
        </article>
        <article class="metric-card accent-mint">
          <span class="metric-label">Policy epoch</span>
          <strong>—</strong>
          <small>server policy not loaded</small>
        </article>
      </section>

      <section v-if="activeSection === 'Overview' || activeSection === 'Devices'" class="workspace-card">
        <div class="card-heading">
          <div>
            <p class="eyebrow">CONTROLLED INVENTORY</p>
            <h2>Devices</h2>
          </div>
          <button class="primary-button" type="button" @click="actionMessage = 'Use the fields below to create a device.'">Create device</button>
        </div>

        <label class="search-box">
          <span>Search</span>
          <input v-model="search" type="search" placeholder="Name, ID, company or area" />
        </label>
        <div class="form-grid compact-form">
          <label>Organization filter<select v-model="inventoryOrganization"><option value="">All organizations</option><option v-for="item in organizations" :key="item.id" :value="item.id">{{ item.name }}</option></select></label>
          <label>Project filter<select v-model="inventoryProject"><option value="">All projects</option><option v-for="item in projects" :key="item.id" :value="item.id">{{ item.name }}</option></select></label>
          <label>Package filter<select v-model="inventoryPackage"><option value="">All packages</option><option v-for="item in packages" :key="item.id" :value="item.id">{{ item.name }}</option></select></label>
          <label>Area filter<select v-model="inventoryArea"><option value="">All areas</option><option v-for="item in areas" :key="item.id" :value="item.id">{{ item.name }}</option></select></label>
        </div>
        <form class="form-grid compact-form" @submit.prevent="createDevice">
          <label>Name<input v-model="createDeviceName" required /></label>
          <label>Organization ID<input v-model="createDeviceOrganization" required /></label>
          <label>Project (optional)<select v-model="createDeviceProject"><option value="">Unassigned</option><option v-for="item in projects" :key="item.id" :value="item.id">{{ item.name }}</option></select></label>
          <label>Package (optional)<select v-model="createDevicePackage"><option value="">Unassigned</option><option v-for="item in packages" :key="item.id" :value="item.id">{{ item.name }}</option></select></label>
          <label>Area (optional)<select v-model="createDeviceArea"><option value="">Unassigned</option><option v-for="item in areas" :key="item.id" :value="item.id">{{ item.name }}</option></select></label>
          <button class="primary-button" type="submit" :disabled="!can('devices.manage')">Create device</button>
        </form>
        <form v-if="editDeviceId" class="form-grid compact-form" @submit.prevent="updateDevice">
          <label>Edit device name<input v-model="editDeviceName" required /></label>
          <label>Project<select v-model="editDeviceProject"><option value="">Unassigned</option><option v-for="item in projects" :key="item.id" :value="item.id">{{ item.name }}</option></select></label>
          <label>Package<select v-model="editDevicePackage"><option value="">Unassigned</option><option v-for="item in packages" :key="item.id" :value="item.id">{{ item.name }}</option></select></label>
          <label>Area<select v-model="editDeviceArea"><option value="">Unassigned</option><option v-for="item in areas" :key="item.id" :value="item.id">{{ item.name }}</option></select></label>
          <button class="primary-button" type="submit" :disabled="!can('devices.manage')">Save device</button>
          <button class="quiet-button" type="button" @click="editDeviceId = ''">Cancel</button>
        </form>

        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Device</th>
                <th>Organization</th>
                <th>Area</th>
                <th>State</th>
                <th>Last seen</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="device in filteredDevices" :key="device.id">
                <td><strong>{{ device.name }}</strong><small>{{ device.id }}</small></td>
                <td>{{ device.organization }}</td>
                <td>{{ device.area ?? 'Unassigned' }}</td>
                <td><span class="state" :class="device.state.toLowerCase()">{{ device.state }}</span></td>
                <td>{{ device.lastSeen }}</td>
                <td><button class="quiet-button" type="button" :disabled="!can('devices.manage')" @click="beginDeviceEdit(device)">Edit</button> <button v-if="device.state !== 'Revoked'" class="quiet-button" type="button" :disabled="!can('devices.manage')" @click="revokeDevice(device.id)">Revoke</button></td>
              </tr>
              <tr v-if="filteredDevices.length === 0">
                <td colspan="6" class="empty">{{ inventoryStatus === 'unavailable' ? 'Sign in to load the server inventory.' : 'No devices are registered yet.' }}</td>
              </tr>
            </tbody>
          </table>
        </div>
        <div class="card-heading command-heading"><div><p class="eyebrow">CONTROL PLANE COMMAND</p><h2>Deferred command</h2></div></div>
        <p class="empty">Commands are authorized, fingerprinted and audited by the server. Live MQTT delivery remains deferred until the firmware contract is fixed.</p>
        <form class="form-grid compact-form" @submit.prevent="sendDeferredCommand">
          <label>Device ID<input v-model="commandDeviceId" required /></label>
          <label>Type<select v-model="commandType" :disabled="!catalog?.trusted"><option value="" disabled>Select a server catalog action</option><option v-for="entry in commandCatalogActions" :key="`${entry.type}:${entry.action}`" :value="entry.type">{{ entry.type }}{{ entry.available ? '' : ' (contract unavailable)' }}</option></select></label>
          <label>Action<select v-model="commandAction" :disabled="!catalog?.trusted"><option value="" disabled>Select an action</option><option v-for="entry in commandCatalogActions.filter(item => item.type === commandType)" :key="entry.action" :value="entry.action" :disabled="!entry.available">{{ entry.action }}{{ entry.available ? '' : ' (contract unavailable)' }}</option></select></label>
          <label>Correlation ID (optional)<input v-model="commandCorrelation" /></label>
          <button class="primary-button" type="submit" :disabled="!commandCanSubmit">Validate command</button>
        </form>
        <p v-if="commandResult" class="empty">{{ commandResult.status }} · {{ commandResult.code }} · {{ commandResult.correlationId }}</p>
      </section>

      <section v-else-if="resourceRoutes[activeSection]" class="workspace-card resource-card">
        <div class="card-heading">
          <div>
            <p class="eyebrow">SERVER INVENTORY</p>
            <h2>{{ activeSection }}</h2>
          </div>
          <button class="primary-button" type="button" disabled title="Create flow requires an authenticated session">Create {{ activeSection.slice(0, -1).toLowerCase() }}</button>
        </div>
        <form v-if="activeSection === 'Organizations'" class="form-grid compact-form" @submit.prevent="createClassification('organization')">
          <label>Name<input v-model="createName" required /></label><label>Parent ID (optional)<input v-model="createParent" /></label><button class="primary-button" type="submit" :disabled="!can('organizations.manage')">Create organization</button>
        </form>
        <form v-else-if="activeSection === 'Projects'" class="form-grid compact-form" @submit.prevent="createClassification('project')">
          <label>Name<input v-model="createProject" required /></label><label>Organization ID<input v-model="createOrganization" required /></label><button class="primary-button" type="submit" :disabled="!can('projects.manage')">Create project</button>
        </form>
        <form v-else-if="activeSection === 'Packages'" class="form-grid compact-form" @submit.prevent="createClassification('package')">
          <label>Name<input v-model="createPackage" required /></label><label>Project ID<input v-model="createProject" required /></label><button class="primary-button" type="submit" :disabled="!can('packages.manage')">Create package</button>
        </form>
        <form v-else-if="activeSection === 'Areas'" class="form-grid compact-form" @submit.prevent="createClassification('area')">
          <label>Name<input v-model="createArea" required /></label><label>Organization ID<input v-model="createOrganization" required /></label><button class="primary-button" type="submit" :disabled="!can('areas.manage')">Create area</button>
        </form>
        <form v-if="editResourceId" class="form-grid compact-form" @submit.prevent="updateClassification">
          <label>Edit name<input v-model="editResourceName" required /></label>
          <button class="primary-button" type="submit" :disabled="!sectionAllowed(activeSection)">Save changes</button>
          <button class="quiet-button" type="button" @click="cancelResourceEdit">Cancel</button>
        </form>
        <p v-if="resourceStatus === 'unavailable'" class="empty">Sign in to load this server-owned resource.</p>
        <p v-else-if="resourceStatus === 'loading'" class="empty">Loading from the server...</p>
        <div v-else-if="resourceItems.length" class="resource-list">
          <article v-for="item in resourceItems" :key="item.id" class="resource-row">
            <strong>{{ item.name }}</strong>
            <small>{{ item.context ?? item.id }}</small>
            <button class="quiet-button" type="button" :disabled="!sectionAllowed(activeSection)" @click="beginResourceEdit(item)">Edit</button>
          </article>
        </div>
        <p v-else class="empty">No {{ activeSection.toLowerCase() }} are registered yet.</p>
      </section>

      <section v-else-if="activeSection === 'Users'" class="workspace-card resource-card">
        <div class="card-heading"><div><p class="eyebrow">IDENTITY ADMINISTRATION</p><h2>Users and roles</h2></div></div>
        <form class="form-grid compact-form" @submit.prevent="createUser">
          <label>Email<input v-model="userEmail" type="email" autocomplete="off" required /></label>
          <label>Temporary password<input v-model="userPassword" type="password" autocomplete="new-password" required /></label>
          <label>Organization ID (optional)<input v-model="userOrganization" autocomplete="off" /></label>
          <button class="primary-button" type="submit" :disabled="!can('users.manage')">Create user</button>
        </form>
        <form class="form-grid compact-form" @submit.prevent="createRole">
          <label>New role name<input v-model="roleName" autocomplete="off" required /></label>
          <button class="quiet-button" type="submit" :disabled="!can('users.manage')">Create role</button>
        </form>
        <p v-if="usersStatus === 'unavailable'" class="empty">Sign in with user-management permission to load users.</p>
        <div v-else class="resource-list"><article v-for="user in users" :key="user.id" class="resource-row"><strong>{{ user.email }}</strong><small>{{ user.id }} · {{ user.roles.join(', ') || 'No roles' }}</small><input v-model="roleAssignment[user.id]" placeholder="role" autocomplete="off" @keyup.enter="assignRole(user)" /><button class="quiet-button" type="button" :disabled="!can('users.manage')" @click="assignRole(user)">Assign role</button></article></div>
      </section>

      <section v-else-if="activeSection === 'Permissions'" class="workspace-card resource-card">
        <div class="card-heading"><div><p class="eyebrow">AUTHORIZATION POLICY</p><h2>Permission grants</h2></div></div>
        <p class="empty">The editor reads the target policy from the server and submits explicit allow/deny grants. The backend remains the final authority.</p>
        <PermissionGrantEditor
          :allowed="can('permissions.manage')"
          :csrf-token="csrfToken"
          :session-signal="sessionSignal"
          @authorization-failed="actionMessage = 'Permission session expired or was revoked by the server.'"
        />
      </section>

      <section v-else-if="activeSection === 'Provisioning'" class="workspace-card resource-card">
        <div class="card-heading"><div><p class="eyebrow">DEVICE ENROLLMENT</p><h2>Provisioning bundle</h2></div></div>
        <p class="empty">Release 1 creates a primary broker bundle only. The server returns metadata first; secret bytes are available once through a CSRF-protected, no-store download.</p>
        <form class="form-grid compact-form" @submit.prevent="createProvisioningBundle">
          <label>Device ID<input v-model="provisioningDeviceId" required /></label>
          <label>Broker host<input v-model="provisioningBrokerHost" required /></label>
          <label>Broker port<input v-model.number="provisioningBrokerPort" type="number" min="1" max="65535" required /></label>
          <button class="primary-button" type="submit" :disabled="!can('provisioning.manage')">Create bundle metadata</button>
        </form>
        <article v-if="provisioningBundle" class="resource-row">
          <strong>{{ provisioningBundle.bundleId }}</strong><small>expires {{ provisioningBundle.expiresAt }} · {{ provisioningBundle.used ? 'used' : 'ready for one download' }}</small>
          <button class="quiet-button" type="button" :disabled="provisioningBundle.used" @click="downloadProvisioningBundle">Download once</button>
        </article>
      </section>

      <section v-else-if="activeSection === 'Licenses'" class="workspace-card resource-card">
        <div class="card-heading"><div><p class="eyebrow">LICENSE WORKFLOW</p><h2>Apply signed license</h2></div></div>
        <p class="empty">License bytes are read only for this request and are never stored in Pinia, URLs, logs or analytics. The backend validates the envelope, trust and recovery policy.</p>
        <form class="form-grid compact-form" @submit.prevent="applyLicense">
          <label>Device ID<input v-model="licenseDeviceId" required /></label><label>Key ID<input v-model="licenseKid" required /></label><label>Issuer<input v-model="licenseIssuer" required /></label>
          <label>Generation<input v-model.number="licenseGeneration" type="number" min="0" required /></label><label>Signed license file<input ref="licenseInput" type="file" accept="application/octet-stream,.bin,.json" required @change="onLicenseFile" /></label>
          <button class="primary-button" type="submit" :disabled="!licenseFile || !can('licenses.manage')">Apply license</button>
        </form>
      </section>

      <section v-else class="workspace-card resource-card">
        <div class="card-heading">
          <div>
            <p class="eyebrow">CONTROL PLANE</p>
            <h2>{{ activeSection }}</h2>
          </div>
        </div>
        <p class="empty">This surface is connected to the server contract but has no fabricated data. It will become available after authentication and its backend workflow is enabled.</p>
      </section>
    </section>
  </main>
</template>
