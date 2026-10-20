export interface DeviceSummary {
  id: string
  name: string
  organization: string
  area: string | null
}

export function filterDevices<T extends DeviceSummary>(devices: readonly T[], search: string): T[] {
  const needle = search.trim().toLowerCase()
  if (!needle) return [...devices]

  return devices.filter((device) =>
    [device.id, device.name, device.organization, device.area ?? '']
      .some((value) => value.toLowerCase().includes(needle)),
  )
}
