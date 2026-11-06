import type { DeviceLinkDto } from '@/core/api/adminApi'
import type { ReadState } from '@/core/commands/useDeviceReads'

/** A reading older than this no longer counts as what the device holds now. */
export const FRESH_READ_MS = 60_000

/** Always needed beside the write and its re-read steps: a write the device runs as a job is followed with it. */
export const OPERATIONS_KEY = 'system.get_operations'

export interface GateInput {
  allowed: string[] | null
  writeKey: string
  /** The reads that confirm the write; the control needs every one of them. */
  rereadKeys: string[]
  /** The read whose value the control shows and edits from. */
  readKey: string
  catalogVerified: boolean
  catalogTrusted: boolean
  link: DeviceLinkDto | null
  states: Record<string, ReadState>
  now: number
  configChangedAt: number | null | undefined
  writing: boolean
}

export interface Gate {
  enabled: boolean
  /** Why the control is locked, in words for the operator; null when it is enabled. */
  reason: string | null
}

/**
 * A config control is on only when the caller may send the write, every read that confirms it and
 * the job poll; the catalog is verified and the link is online; the value it edits was read
 * within a minute and after any change the page saw from someone else; and the card is not
 * already writing.
 */
export function configGate(input: GateInput): Gate {
  const locked = (reason: string): Gate => ({ enabled: false, reason })

  if (input.writing) return locked('Đang ghi…')

  const missing = [input.writeKey, ...input.rereadKeys, OPERATIONS_KEY].find(key => !input.allowed?.includes(key))
  if (missing) return locked(`Thiếu quyền ${missing}`)

  if (!input.catalogVerified || !input.catalogTrusted) return locked('Catalog lệnh chưa được xác minh')
  if (!input.link || input.link.online === false || input.link.brokerConnected === false) return locked('Thiết bị offline')

  const read = input.states[input.readKey]
  const readAt = read?.kind === 'ok' ? Date.parse(read.readAt ?? '') : NaN
  const fresh = Number.isFinite(readAt) && input.now - readAt <= FRESH_READ_MS
  const unchanged = input.configChangedAt == null || readAt >= input.configChangedAt
  if (!fresh || !unchanged) return locked('Đọc lại trước khi sửa')

  return { enabled: true, reason: null }
}
