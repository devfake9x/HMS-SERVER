import { describe, expect, it, vi } from 'vitest'
import { applyRealtimeEvent } from './realtimeAuthorization'
import {
  createUnavailableHubConnection,
  DEVICE_EVENTS_HUB_PATH,
  useDeviceOperationsHub,
  type HubConnectionLike,
} from './useDeviceOperationsHub'

class FakeHub implements HubConnectionLike {
  state = 'Disconnected'
  private readonly handlers = new Map<string, Set<(payload: unknown) => void>>()
  readonly invocations: Array<{ method: string; args: unknown[] }> = []
  startCount = 0
  stopCount = 0
  private reconnectHandler?: () => void

  async start(): Promise<void> {
    this.startCount++
    this.state = 'Connected'
  }

  async stop(): Promise<void> {
    this.stopCount++
    this.state = 'Disconnected'
  }

  async invoke(method: string, ...args: unknown[]): Promise<void> {
    this.invocations.push({ method, args })
  }

  on(method: string, handler: (payload: unknown) => void): void {
    const handlers = this.handlers.get(method) ?? new Set()
    handlers.add(handler)
    this.handlers.set(method, handlers)
  }

  off(method: string, handler?: (payload: unknown) => void): void {
    if (!handler) {
      this.handlers.delete(method)
      return
    }
    this.handlers.get(method)?.delete(handler)
  }

  onreconnected(handler: () => void): void {
    this.reconnectHandler = handler
  }

  emitReconnect(): void {
    this.reconnectHandler?.()
  }

  emit(method: string, payload: unknown): void {
    for (const handler of this.handlers.get(method) ?? []) handler(payload)
  }
}

describe('useDeviceOperationsHub', () => {
  it('pins the server hub endpoint without opening a browser MQTT connection', () => {
    expect(DEVICE_EVENTS_HUB_PATH).toBe('/hubs/device-events')
  })

  it('connects, joins, revalidates after reconnect, and leaves devices', async () => {
    const hub = new FakeHub()
    const client = useDeviceOperationsHub({
      createConnection: async () => hub,
      authorizationEpoch: () => 7,
    })

    await client.connect()
    await client.joinDevice('d1')
    await client.revalidateDevice('d1')
    await client.leaveDevice('d1')
    await client.disconnect()

    expect(hub.invocations.map(item => item.method)).toEqual([
      'JoinDevice', 'RevalidateDevice', 'LeaveDevice',
    ])
    expect(hub.invocations.slice(0, 2).every(item => item.args[1] === 7)).toBe(true)
    expect(client.connectionState.value).toBe('disconnected')
  })

  it('dispatches DeviceEvent and tears down pending work on DeviceRevoked', async () => {
    const hub = new FakeHub()
    const client = useDeviceOperationsHub({
      createConnection: async () => hub,
      authorizationEpoch: () => 3,
    })
    await client.connect()
    await client.joinDevice('d1')
    const controller = new AbortController()
    client.registerPendingCommand('d1', controller)

    hub.emit('DeviceEvent', {
      deviceId: { value: 'd1' }, authorizationEpoch: 3, policyVersion: 2,
      eventType: 'status', payload: { online: true },
    })
    hub.emit('DeviceRevoked', {
      deviceId: 'd1', authorizationEpoch: 4, policyVersion: 3, reason: 'AUTHORIZATION_REVOKED',
    })

    expect(client.state.value.devices.has('d1')).toBe(false)
    expect(controller.signal.aborted).toBe(true)
    expect(client.requiresReauthorization.value.has('d1')).toBe(true)
  })

  it('reauthorizes existing subscriptions after a transport reconnect', async () => {
    const hub = new FakeHub()
    const client = useDeviceOperationsHub({
      createConnection: async () => hub,
      authorizationEpoch: () => 9,
    })
    await client.connect()
    await client.joinDevice('d1')
    hub.emitReconnect()
    await Promise.resolve()
    expect(hub.invocations.map(item => item.method)).toEqual(['JoinDevice', 'RevalidateDevice'])
  })

  it('keeps stale events from restoring revoked state', async () => {
    const hub = new FakeHub()
    const client = useDeviceOperationsHub({
      createConnection: async () => hub,
      authorizationEpoch: () => 1,
    })
    await client.connect()
    await client.joinDevice('d1')
    hub.emit('DeviceRevoked', { deviceId: 'd1', authorizationEpoch: 2, policyVersion: 1 })
    hub.emit('DeviceEvent', {
      deviceId: 'd1', authorizationEpoch: 1, policyVersion: 1,
      eventType: 'status', payload: { online: true },
    })
    expect(client.state.value.devices.has('d1')).toBe(false)
  })

  it('does not notify application callbacks for stale events', async () => {
    const hub = new FakeHub()
    const onEvent = vi.fn()
    const onRevoked = vi.fn()
    const client = useDeviceOperationsHub({
      createConnection: async () => hub,
      authorizationEpoch: () => 3,
      onEvent,
      onRevoked,
    })
    await client.connect()
    await client.joinDevice('d1')
    hub.emit('DeviceEvent', { deviceId: 'd1', authorizationEpoch: 3, policyVersion: 3, eventType: 'status', payload: { online: true } })
    hub.emit('DeviceEvent', { deviceId: 'd1', authorizationEpoch: 3, policyVersion: 2, eventType: 'status', payload: { online: false } })
    hub.emit('DeviceRevoked', { deviceId: 'd1', authorizationEpoch: 2, policyVersion: 1 })
    expect(onEvent).toHaveBeenCalledTimes(1)
    expect(onRevoked).not.toHaveBeenCalled()
  })

  it('fails closed when no browser transport is configured', async () => {
    const connection = createUnavailableHubConnection()
    await expect(connection.start()).rejects.toThrow('SIGNALR_TRANSPORT_UNAVAILABLE')
  })

  it('clears state, aborts pending commands, and detaches handlers on disconnect', async () => {
    const hub = new FakeHub()
    const client = useDeviceOperationsHub({
      createConnection: async () => hub,
      authorizationEpoch: () => 1,
    })
    await client.connect()
    await client.joinDevice('d1')
    const controller = new AbortController()
    client.registerPendingCommand('d1', controller)
    hub.emit('DeviceEvent', { deviceId: 'd1', authorizationEpoch: 1, policyVersion: 1, eventType: 'status', payload: { online: true } })

    await client.disconnect()
    hub.emit('DeviceEvent', { deviceId: 'd1', authorizationEpoch: 2, policyVersion: 2, eventType: 'status', payload: { online: false } })

    expect(controller.signal.aborted).toBe(true)
    expect(client.state.value.devices.size).toBe(0)
    expect(client.state.value.pending.size).toBe(0)
  })

  it('does not resurrect a connection when disconnect wins a pending connect', async () => {
    const hub = new FakeHub()
    let resolveConnection!: (connection: HubConnectionLike) => void
    const connection = new Promise<HubConnectionLike>(resolve => { resolveConnection = resolve })
    const client = useDeviceOperationsHub({
      createConnection: () => connection,
      authorizationEpoch: () => 1,
    })
    const connecting = client.connect()
    await client.disconnect()
    resolveConnection(hub)
    await connecting
    expect(hub.startCount).toBe(0)
    expect(client.connectionState.value).toBe('disconnected')
  })

  it('clears local connection state even if transport shutdown fails', async () => {
    const hub = new FakeHub()
    const client = useDeviceOperationsHub({ createConnection: () => hub, authorizationEpoch: () => 1 })
    await client.connect()
    await client.joinDevice('d1')
    vi.spyOn(hub, 'stop').mockRejectedValue(new Error('stop failed'))
    await expect(client.disconnect()).rejects.toThrow('stop failed')
    expect(client.connectionState.value).toBe('disconnected')
    expect(client.subscribedDevices.value.size).toBe(0)
  })

  it('does not let a reconnect rejection after disconnect resurrect reauthorization state', async () => {
    const hub = new FakeHub()
    let rejectReconnect!: (error: Error) => void
    const epoch = vi.fn()
      .mockResolvedValueOnce(1)
      .mockImplementationOnce(() => new Promise<number>((_, reject) => { rejectReconnect = reject }))
    const client = useDeviceOperationsHub({ createConnection: () => hub, authorizationEpoch: epoch })

    await client.connect()
    await client.joinDevice('d1')
    hub.emitReconnect()
    await Promise.resolve()
    await client.disconnect()
    rejectReconnect(new Error('authorization lookup failed'))
    await Promise.resolve()

    expect(client.connectionState.value).toBe('disconnected')
    expect(client.requiresReauthorization.value.size).toBe(0)
  })

  it('does not let an old disconnect clobber a replacement connection', async () => {
    const first = new FakeHub()
    const second = new FakeHub()
    const createConnection = vi.fn().mockReturnValueOnce(first).mockReturnValueOnce(second)
    let finishStop!: () => void
    vi.spyOn(first, 'stop').mockImplementation(() => new Promise<void>(resolve => { finishStop = resolve }))
    const client = useDeviceOperationsHub({ createConnection, authorizationEpoch: () => 1 })

    await client.connect()
    const disconnecting = client.disconnect()
    await Promise.resolve()
    await client.connect()

    expect(client.connectionState.value).toBe('connected')
    finishStop()
    await disconnecting
    expect(client.connectionState.value).toBe('connected')
  })

  it.each(['joinDevice', 'revalidateDevice'] as const)('ignores a late %s completion after logout', async method => {
    const hub = new FakeHub()
    const client = useDeviceOperationsHub({ createConnection: () => hub, authorizationEpoch: () => 1 })
    await client.connect()
    let finish!: () => void
    vi.spyOn(hub, 'invoke').mockImplementation(() => new Promise<void>(resolve => { finish = resolve }))
    const pending = client[method]('d1')
    await Promise.resolve()
    await client.disconnect()
    finish()
    await pending
    expect(client.subscribedDevices.value.size).toBe(0)
    expect(client.connectionState.value).toBe('disconnected')
  })

  it('ignores reconnect callbacks from a disconnected old hub', async () => {
    const first = new FakeHub()
    const second = new FakeHub()
    const createConnection = vi.fn().mockReturnValueOnce(first).mockReturnValueOnce(second)
    const client = useDeviceOperationsHub({ createConnection, authorizationEpoch: () => 1 })
    await client.connect()
    await client.disconnect()
    first.emitReconnect()
    expect(client.connectionState.value).toBe('disconnected')
    await client.connect()
    await client.joinDevice('d2')
    first.emitReconnect()
    await Promise.resolve()
    expect(second.invocations.map(entry => entry.method)).toEqual(['JoinDevice'])
  })

  it('does not let a delayed join undo a newer revoke for the same device', async () => {
    const hub = new FakeHub()
    const client = useDeviceOperationsHub({ createConnection: () => hub, authorizationEpoch: () => 1 })
    await client.connect()
    let finish!: () => void
    vi.spyOn(hub, 'invoke').mockImplementation(() => new Promise<void>(resolve => { finish = resolve }))
    const pending = client.joinDevice('d1')
    await Promise.resolve()
    hub.emit('DeviceRevoked', { deviceId: 'd1', authorizationEpoch: 2, policyVersion: 2 })
    finish()
    await pending
    expect(client.subscribedDevices.value.has('d1')).toBe(false)
    expect(client.requiresReauthorization.value.has('d1')).toBe(true)
  })
})
