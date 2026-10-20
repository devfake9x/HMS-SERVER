export interface PermissionSnapshot {
  policyVersion: number
  expiresAt: string
  permissions: readonly string[]
  deniedPermissions?: readonly string[]
}

export const PERMISSION = {
  MONITORING_REALTIME: 'monitoring.realtime',
  MONITORING_HISTORY: 'monitoring.history',
  MONITORING_EXPORT: 'monitoring.export',
  REPORTS_VIEW: 'reports.view',
  REPORTS_EXPORT: 'reports.export',
  ALARMS_READ: 'alarms.read',
  ALARMS_ACKNOWLEDGE: 'alarms.acknowledge',
  STATIONS_READ: 'stations.read',
  STATIONS_WRITE: 'stations.write',
  DEVICES_READ: 'devices.read',
  DEVICES_WRITE: 'devices.write',
  DEVICES_MANAGE: 'devices.manage',
  USERS_READ: 'users.read',
  USERS_WRITE: 'users.write',
  USERS_MANAGE: 'users.manage',
  ROLES_MANAGE: 'roles.manage',
  AUDITS_READ: 'audits.read',
  CONFIGS_VIEW: 'configs.view',
  CONFIGS_MANAGE: 'configs.manage',
  ORGANIZATIONS_READ: 'organizations.read',
  ORGANIZATIONS_WRITE: 'organizations.write',
  ORGANIZATIONS_MANAGE: 'organizations.manage',
  PROJECTS_READ: 'projects.read',
  PROJECTS_WRITE: 'projects.write',
  PROJECTS_MANAGE: 'projects.manage',
  PACKAGES_READ: 'packages.read',
  PACKAGES_WRITE: 'packages.write',
  PACKAGES_MANAGE: 'packages.manage',
  AREAS_READ: 'areas.read',
  AREAS_WRITE: 'areas.write',
  AREAS_MANAGE: 'areas.manage',
  PROVISIONING_MANAGE: 'provisioning.manage',
  LICENSES_MANAGE: 'licenses.manage',
  PERMISSIONS_MANAGE: 'permissions.manage',
} as const

export type PermissionCode = (typeof PERMISSION)[keyof typeof PERMISSION] | string

export const ROLE = {
  SUPER_ADMIN: 'SUPER_ADMIN',
  ADMIN: 'admin',
  OPERATOR: 'operator',
  GUEST: 'guest',
  USER: 'USER',
} as const

export type RoleCode = (typeof ROLE)[keyof typeof ROLE]

export function can(
  snapshot: PermissionSnapshot | null | undefined,
  permission: string,
  now = new Date(),
): boolean {
  if (!snapshot || !permission || !Number.isFinite(snapshot.policyVersion)) return false
  const expiry = Date.parse(snapshot.expiresAt)
  if (!Number.isFinite(expiry) || expiry <= now.getTime()) return false
  if (!Array.isArray(snapshot.permissions)) return false
  if (snapshot.deniedPermissions !== undefined && !Array.isArray(snapshot.deniedPermissions)) return false
  const denied = snapshot.deniedPermissions ?? []
  if (denied.includes('*') || denied.includes(permission)) return false
  return snapshot.permissions.includes(permission) || snapshot.permissions.includes('*')
}

export function canDeviceAction(
  snapshot: PermissionSnapshot | null | undefined,
  deviceId: string,
  action: string,
  now = new Date(),
): boolean {
  if (!can(snapshot, 'device.commands', now)) return false
  return can(snapshot, `device:${deviceId}:action:${action}`, now) ||
    can(snapshot, `device:${deviceId}:action:*`, now)
}

export function catalogIsCurrent(
  expected: { version: string; hash: string } | null | undefined,
  actual: { version: string; hash: string } | null | undefined,
): boolean {
  return !!expected && !!actual &&
    expected.version === actual.version && expected.hash === actual.hash
}
