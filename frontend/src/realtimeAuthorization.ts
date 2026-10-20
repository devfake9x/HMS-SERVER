export interface RealtimeState<T> {
  epoch: number
  devices: Map<string, T>
  pending: Map<string, AbortController>
  /** Per-device fences prevent one device's policy change from invalidating another. */
  deviceEpochs?: Map<string, number>
  policyVersions?: Map<string, number>
}

export type RealtimeEvent<T> =
  | { type: 'DeviceStatus'; authorizationEpoch: number; policyVersion: number; deviceId: string; device: T }
  | { type: 'DeviceEvent'; authorizationEpoch: number; policyVersion: number; deviceId: string; eventType: string; device: T }
  | { type: 'DeviceRevoked'; authorizationEpoch: number; policyVersion: number; deviceId: string }
  | { type: 'Snapshot'; authorizationEpoch: number; policyVersion?: number; devices: ReadonlyMap<string, T> }

export function applyRealtimeEvent<T>(state: RealtimeState<T>, event: RealtimeEvent<T>): RealtimeState<T> {
  const deviceEpochs = new Map(state.deviceEpochs ?? [])
  const policyVersions = new Map(state.policyVersions ?? [])
  if (event.type !== 'Snapshot') {
    // Legacy callers only have the aggregate epoch; use it as a conservative baseline
    // until the first per-device event/snapshot establishes a precise fence.
    const knownEpoch = deviceEpochs.get(event.deviceId) ?? (state.deviceEpochs ? 0 : state.epoch)
    const knownPolicy = policyVersions.get(event.deviceId) ?? 0
    if (event.authorizationEpoch < knownEpoch ||
      (event.authorizationEpoch === knownEpoch && event.policyVersion < knownPolicy)) return state
    deviceEpochs.set(event.deviceId, event.authorizationEpoch)
    policyVersions.set(event.deviceId, event.policyVersion)
  } else {
    if (event.authorizationEpoch < state.epoch) return state
    if (event.authorizationEpoch === state.epoch && state.policyVersions && state.policyVersions.size > 0) {
      const highestPolicy = Math.max(...state.policyVersions.values())
      if ((event.policyVersion ?? 0) < highestPolicy) return state
    }
  }
  const devices = new Map(state.devices)
  const pending = new Map(state.pending)
  if (event.type === 'Snapshot') {
    for (const [deviceId, controller] of pending) {
      if (!event.devices.has(deviceId)) {
        controller.abort()
        pending.delete(deviceId)
      }
    }
    const snapshotPolicy = event.policyVersion ?? 0
    const snapshotEpochs = new Map<string, number>()
    const snapshotPolicies = new Map<string, number>()
    for (const deviceId of event.devices.keys()) {
      snapshotEpochs.set(deviceId, event.authorizationEpoch)
      snapshotPolicies.set(deviceId, snapshotPolicy)
    }
    return { epoch: event.authorizationEpoch, devices: new Map(event.devices), pending, deviceEpochs: snapshotEpochs, policyVersions: snapshotPolicies }
  }
  if (event.type === 'DeviceRevoked') {
    devices.delete(event.deviceId)
    pending.get(event.deviceId)?.abort()
    pending.delete(event.deviceId)
  } else {
    devices.set(event.deviceId, event.device)
  }
  return { epoch: Math.max(state.epoch, event.authorizationEpoch), devices, pending, deviceEpochs, policyVersions }
}
