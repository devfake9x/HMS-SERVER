export interface PermissionSnapshot {
  policyVersion: number
  expiresAt: string
  permissions: readonly string[]
  deniedPermissions?: readonly string[]
}

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
