import { ref, shallowRef, type Ref } from 'vue'
import { applyRealtimeEvent, type RealtimeEvent, type RealtimeState } from './realtimeAuthorization'

export interface HubConnectionLike {
  readonly state?: string
  start(): Promise<void>
  stop(): Promise<void>
  invoke(methodName: string, ...args: unknown[]): Promise<unknown>
  on(methodName: string, handler: (payload: unknown) => void): void
  off(methodName: string, handler?: (payload: unknown) => void): void
  onreconnecting?(handler: (error?: Error) => void): void
  onreconnected?(handler: (connectionId?: string) => void): void
  onclose?(handler: (error?: Error) => void): void
}

export interface DeviceHubEvent {
  deviceId: string
  authorizationEpoch: number
  policyVersion: number
  eventType: string
  payload?: unknown
}

export interface DeviceRevokedEvent {
  deviceId: string
  authorizationEpoch: number
  policyVersion: number
  reason?: string
}

export interface DeviceOperationsHubOptions<T = unknown> {
  createConnection: () => HubConnectionLike | Promise<HubConnectionLike>
  authorizationEpoch: (deviceId: string) => number | Promise<number>
  initialState?: RealtimeState<T>
  onEvent?: (event: DeviceHubEvent) => void
  onRevoked?: (event: DeviceRevokedEvent) => void
}

export type HubConnectionState = 'disconnected' | 'connecting' | 'connected' | 'reconnecting' | 'reauthorization-required'
export const DEVICE_EVENTS_HUB_PATH = '/hubs/device-events'

export interface DeviceOperationsHubClient<T = unknown> {
  connectionState: Ref<HubConnectionState>
  state: Ref<RealtimeState<T>>
  subscribedDevices: Ref<Set<string>>
  requiresReauthorization: Ref<Set<string>>
  connect(): Promise<void>
  disconnect(): Promise<void>
  joinDevice(deviceId: string): Promise<void>
  revalidateDevice(deviceId: string): Promise<void>
  leaveDevice(deviceId: string): Promise<void>
  registerPendingCommand(deviceId: string, controller: AbortController): void
  clearPendingCommand(deviceId: string, controller?: AbortController): void
}

const emptyState = <T>(): RealtimeState<T> => ({ epoch: 0, devices: new Map(), pending: new Map() })

export function createUnavailableHubConnection(): HubConnectionLike {
  const unavailable = async (): Promise<never> => {
    throw new Error('SIGNALR_TRANSPORT_UNAVAILABLE')
  }
  return {
    start: unavailable,
    stop: async () => undefined,
    invoke: unavailable,
    on: () => undefined,
    off: () => undefined,
  }
}

/**
 * Adapter seam for the browser SignalR package. The concrete @microsoft/
 * signalr builder can be supplied once the dependency and hub contract are
 * available; this module intentionally does not invent a wire protocol.
 */
export function useDeviceOperationsHub<T = unknown>(options: DeviceOperationsHubOptions<T>): DeviceOperationsHubClient<T> {
  const connectionState = ref<HubConnectionState>('disconnected')
  const state = shallowRef<RealtimeState<T>>(options.initialState ?? emptyState<T>())
  const subscribedDevices = ref(new Set<string>())
  const requiresReauthorization = ref(new Set<string>())
  let connection: HubConnectionLike | null = null
  let lifecycleRevision = 0
  let registeredHandlers: {
    hub: HubConnectionLike
    deviceEvent: (payload: unknown) => void
    deviceRevoked: (payload: unknown) => void
  } | null = null

  const dispatch = (event: RealtimeEvent<T>) => {
    state.value = applyRealtimeEvent(state.value, event)
  }

  const onDeviceEvent = (raw: unknown) => {
    const event = normalizeDeviceEvent(raw)
    if (!event) return
    const deviceEvent: RealtimeEvent<T> = {
      type: 'DeviceEvent',
      deviceId: event.deviceId,
      authorizationEpoch: event.authorizationEpoch,
      policyVersion: event.policyVersion,
      eventType: event.eventType,
      device: event.payload as T,
    }
    const before = state.value
    dispatch(deviceEvent)
    if (state.value === before) return
    options.onEvent?.(event)
  }

  const onDeviceRevoked = (raw: unknown) => {
    const event = normalizeDeviceRevoked(raw)
    if (!event) return
    const before = state.value
    dispatch({ type: 'DeviceRevoked', ...event })
    if (state.value === before) return
    const subscribed = new Set(subscribedDevices.value)
    subscribed.delete(event.deviceId)
    subscribedDevices.value = subscribed
    requiresReauthorization.value = new Set(requiresReauthorization.value).add(event.deviceId)
    options.onRevoked?.(event)
  }

  const onReconnecting = () => {
    connectionState.value = 'reconnecting'
  }

  const onReconnected = (hub: HubConnectionLike, revision: number) => {
    if (!isCurrent(hub, revision)) return
    connectionState.value = 'connected'
    // Revalidation is intentionally explicit and asynchronous. A reconnect
    // never restores group membership based on stale client state.
    for (const deviceId of subscribedDevices.value) {
      void revalidateDevice(deviceId).catch(() => {
        if (!isCurrent(hub, revision) || !subscribedDevices.value.has(deviceId)) return
        connectionState.value = 'reauthorization-required'
        requiresReauthorization.value = new Set(requiresReauthorization.value).add(deviceId)
      })
    }
  }

  const onClose = () => {
    connectionState.value = 'disconnected'
  }

  const isCurrent = (hub: HubConnectionLike, revision: number) =>
    connection === hub && lifecycleRevision === revision && connectionState.value !== 'disconnected'

  const registerHandlers = (hub: HubConnectionLike, revision: number) => {
    const deviceEvent = (payload: unknown) => {
      if (isCurrent(hub, revision)) onDeviceEvent(payload)
    }
    const deviceRevoked = (payload: unknown) => {
      if (isCurrent(hub, revision)) onDeviceRevoked(payload)
    }
    registeredHandlers = { hub, deviceEvent, deviceRevoked }
    hub.on('DeviceEvent', deviceEvent)
    hub.on('DeviceRevoked', deviceRevoked)
    hub.onreconnecting?.(() => { if (isCurrent(hub, revision)) onReconnecting() })
    hub.onreconnected?.(() => { onReconnected(hub, revision) })
    hub.onclose?.(() => { if (isCurrent(hub, revision)) onClose() })
  }

  const connect = async () => {
    if (connection && connectionState.value === 'connected') return
    const revision = ++lifecycleRevision
    connectionState.value = 'connecting'
    let created: HubConnectionLike
    try {
      created = await options.createConnection()
    } catch (error) {
      if (revision === lifecycleRevision) connectionState.value = 'disconnected'
      throw error
    }
    if (revision !== lifecycleRevision) {
      await created.stop().catch(() => undefined)
      return
    }
    connection = created
    registerHandlers(created, revision)
    try {
      await created.start()
      if (revision !== lifecycleRevision) {
        await created.stop().catch(() => undefined)
        return
      }
      connectionState.value = 'connected'
    } catch (error) {
      if (connection === created) {
        connectionState.value = 'disconnected'
        connection = null
      }
      throw error
    }
  }

  const disconnect = async () => {
    const revision = ++lifecycleRevision
    const hub = connection
    connection = null
    const handlers = registeredHandlers
    registeredHandlers = null
    subscribedDevices.value = new Set()
    for (const controller of state.value.pending.values()) controller.abort()
    requiresReauthorization.value = new Set()
    state.value = { epoch: 0, devices: new Map(), pending: new Map(), deviceEpochs: new Map(), policyVersions: new Map() }
    if (!hub) {
      connectionState.value = 'disconnected'
      return
    }
    if (handlers?.hub === hub) {
      hub.off('DeviceEvent', handlers.deviceEvent)
      hub.off('DeviceRevoked', handlers.deviceRevoked)
    }
    try {
      await hub.stop()
    } finally {
      if (lifecycleRevision === revision && connection === null) {
        connectionState.value = 'disconnected'
      }
    }
  }

  const requireConnection = (): HubConnectionLike => {
    if (!connection || connectionState.value === 'disconnected') throw new Error('SIGNALR_NOT_CONNECTED')
    return connection
  }

  const joinDevice = async (deviceId: string) => {
    const hub = requireConnection()
    const revision = lifecycleRevision
    const startedReauthorization = requiresReauthorization.value.has(deviceId)
    const epoch = await options.authorizationEpoch(deviceId)
    if (!isCurrent(hub, revision)) return
    await hub.invoke('JoinDevice', deviceId, epoch)
    if (!isCurrent(hub, revision) || (!startedReauthorization && requiresReauthorization.value.has(deviceId))) return
    subscribedDevices.value = new Set(subscribedDevices.value).add(deviceId)
    const next = new Set(requiresReauthorization.value)
    next.delete(deviceId)
    requiresReauthorization.value = next
  }

  const revalidateDevice = async (deviceId: string) => {
    const hub = requireConnection()
    const revision = lifecycleRevision
    const startedReauthorization = requiresReauthorization.value.has(deviceId)
    const epoch = await options.authorizationEpoch(deviceId)
    if (!isCurrent(hub, revision)) return
    await hub.invoke('RevalidateDevice', deviceId, epoch)
    if (!isCurrent(hub, revision) || (!startedReauthorization && requiresReauthorization.value.has(deviceId))) return
    subscribedDevices.value = new Set(subscribedDevices.value).add(deviceId)
    const next = new Set(requiresReauthorization.value)
    next.delete(deviceId)
    requiresReauthorization.value = next
  }

  const leaveDevice = async (deviceId: string) => {
    const hub = requireConnection()
    const revision = lifecycleRevision
    await hub.invoke('LeaveDevice', deviceId)
    if (!isCurrent(hub, revision)) return
    const next = new Set(subscribedDevices.value)
    next.delete(deviceId)
    subscribedDevices.value = next
    const reauth = new Set(requiresReauthorization.value)
    reauth.delete(deviceId)
    requiresReauthorization.value = reauth
  }

  const registerPendingCommand = (deviceId: string, controller: AbortController) => {
    const pending = new Map(state.value.pending)
    const existing = pending.get(deviceId)
    if (existing && existing !== controller) existing.abort()
    pending.set(deviceId, controller)
    state.value = { ...state.value, pending }
  }

  const clearPendingCommand = (deviceId: string, controller?: AbortController) => {
    const pending = new Map(state.value.pending)
    if (!controller || pending.get(deviceId) === controller) pending.delete(deviceId)
    state.value = { ...state.value, pending }
  }

  return {
    connectionState,
    state,
    subscribedDevices,
    requiresReauthorization,
    connect,
    disconnect,
    joinDevice,
    revalidateDevice,
    leaveDevice,
    registerPendingCommand,
    clearPendingCommand,
  }
}

function normalizeDeviceEvent(raw: unknown): DeviceHubEvent | null {
  if (!raw || typeof raw !== 'object') return null
  const value = raw as Record<string, unknown>
  const deviceId = normalizeDeviceId(value.deviceId)
  if (!deviceId || typeof value.authorizationEpoch !== 'number' || typeof value.policyVersion !== 'number') return null
  const eventType = typeof value.eventType === 'string' ? value.eventType : 'status'
  return { deviceId, authorizationEpoch: value.authorizationEpoch, policyVersion: value.policyVersion, eventType, payload: value.payload }
}

function normalizeDeviceRevoked(raw: unknown): DeviceRevokedEvent | null {
  if (!raw || typeof raw !== 'object') return null
  const value = raw as Record<string, unknown>
  const deviceId = normalizeDeviceId(value.deviceId)
  if (!deviceId || typeof value.authorizationEpoch !== 'number' || typeof value.policyVersion !== 'number') return null
  return { deviceId, authorizationEpoch: value.authorizationEpoch, policyVersion: value.policyVersion, reason: typeof value.reason === 'string' ? value.reason : undefined }
}

function normalizeDeviceId(raw: unknown): string | null {
  if (typeof raw === 'string' && raw.trim()) return raw
  if (raw && typeof raw === 'object' && typeof (raw as { value?: unknown }).value === 'string') {
    const value = (raw as { value: string }).value
    return value.trim() || null
  }
  return null
}
