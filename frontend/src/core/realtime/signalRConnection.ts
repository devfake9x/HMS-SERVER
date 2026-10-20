import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr'
import { DEVICE_EVENTS_HUB_PATH, type HubConnectionLike } from './useDeviceOperationsHub'

export function createSignalRConnection(): HubConnectionLike {
  const origin = globalThis.location?.origin ?? 'http://localhost'
  const connection: HubConnection = new HubConnectionBuilder()
    .withUrl(new URL(DEVICE_EVENTS_HUB_PATH, origin).toString(), { withCredentials: true })
    .withAutomaticReconnect([0, 2_000, 5_000, 15_000])
    .configureLogging(import.meta.env.DEV ? LogLevel.Warning : LogLevel.Error)
    .build()
  return connection
}
