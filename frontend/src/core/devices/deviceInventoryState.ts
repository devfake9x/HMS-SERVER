export interface DeviceInventoryQuery {
  q?: string
  organizationId?: string
  projectId?: string
  packageId?: string
  areaId?: string
}

export function readDeviceInventoryQuery(search: string): DeviceInventoryQuery {
  const params = new URLSearchParams(search)
  const value = (key: string) => params.get(key)?.trim() || undefined
  return { q: value('q'), organizationId: value('organizationId'), projectId: value('projectId'), packageId: value('packageId'), areaId: value('areaId') }
}

export function writeDeviceInventoryQuery(query: DeviceInventoryQuery): string {
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(query)) {
    if (value?.trim()) params.set(key, value.trim())
  }
  const result = params.toString()
  return result ? `?${result}` : ''
}
