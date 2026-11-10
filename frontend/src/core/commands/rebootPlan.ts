import type { DeviceLinkDto } from '@/core/api/adminApi'

/**
 * `dry_run_failed`: the preview did not succeed, nothing real was sent. `not_sent`: the real command
 * was refused before it was signed. `rejected`: the device refused it. `observed`: the server saw the
 * device come back (see `verified`). `relay_unchecked`: observed, but the relay table could not be
 * read back. `not_observed`, `outcome_unknown`, `server_restarted`, `tracking_timeout`: the server
 * record ended without proof either way. `untracked`: the device ran it but the server does not track it.
 */
export type RebootKind =
  | 'dry_run_failed' | 'not_sent' | 'rejected' | 'observed' | 'relay_unchecked'
  | 'not_observed' | 'outcome_unknown' | 'server_restarted' | 'tracking_timeout' | 'untracked'

export const REBOOT_KEY = 'system.reboot'

/** Channels of a control.get_status reply, by what a reboot does to them (spec 6.4, L866-880). */
export interface RelaysBefore {
  /** Not forced and on, or a pulse still running: the driver stops and every line goes OFF. */
  turnOff: number[]
  /** Forced OFF: stays OFF. */
  forcedOff: number[]
  /** Forced ON: may drop OFF while the driver restarts, then comes back ON at the first line write. */
  forcedOn: number[]
  readable: boolean
}
export interface RelayCheck { deviations: string[]; notes: string[] }

export interface RebootGate {
  visible: boolean
  enabled: boolean
  /** Why the control is locked, in words for the operator; null when it is enabled. */
  reason: string | null
}
export interface RebootGateInput {
  allowedCommands: string[] | null
  catalogVerified: boolean
  link: DeviceLinkDto | null
  running: boolean
}

export interface RelayChannel { channel: number; state: boolean; mode: string | null; forced: boolean; pulseActive: boolean }

function asRecord(value: unknown): Record<string, unknown> | null {
  return value && typeof value === 'object' && !Array.isArray(value) ? value as Record<string, unknown> : null
}

/** The channel list of a get_status reply, which the device may wrap one level deeper; null when unreadable. */
export function channelsOf(data: unknown): RelayChannel[] | null {
  let node = asRecord(data)
  if (node && !('channels' in node)) node = asRecord(node.data)
  if (!node || !Array.isArray(node.channels)) return null
  const channels: RelayChannel[] = []
  for (const raw of node.channels) {
    const item = asRecord(raw)
    if (!item || !Number.isInteger(item.channel) || typeof item.state !== 'boolean') return null
    channels.push({
      channel: item.channel as number,
      state: item.state,
      mode: typeof item.mode === 'string' ? item.mode : null,
      forced: item.forced === true,
      pulseActive: item.pulseActive === true,
    })
  }
  return channels
}

/**
 * Whether the server's verified relay state still matches the table a dialog was built from, on what
 * the dialog shows: the channel set, each state, mode and running pulse. (The verified state carries
 * no forced flag; a forced level shows as the state.)
 */
export function relaysMatch(shown: RelayChannel[], current: Array<{ channel: number; state: boolean; mode: string | null; pulseActive: boolean | null }>): boolean {
  if (shown.length !== current.length) return false
  return shown.every(ch => {
    const now = current.find(c => c.channel === ch.channel)
    return !!now && now.state === ch.state && now.mode === ch.mode && Boolean(now.pulseActive) === ch.pulseActive
  })
}

export function relaysBeforeReboot(data: unknown): RelaysBefore {
  const channels = channelsOf(data)
  if (!channels) return { turnOff: [], forcedOff: [], forcedOn: [], readable: false }
  const plan: RelaysBefore = { turnOff: [], forcedOff: [], forcedOn: [], readable: true }
  for (const ch of channels) {
    if (ch.forced) (ch.state ? plan.forcedOn : plan.forcedOff).push(ch.channel)
    else if (ch.state || ch.pulseActive) plan.turnOff.push(ch.channel)
  }
  return plan
}

/**
 * Compares the relay table after the reboot with the plan made before it. A forced channel that
 * is not at its forced level is a fault; an unforced channel that is on is not (a DO rule
 * SET_HIGH/SET_LOW runs at the first evaluation after boot, L880), so it is only reported.
 */
export function checkAfterReboot(before: RelaysBefore, after: unknown): RelayCheck {
  const result: RelayCheck = { deviations: [], notes: [] }
  const channels = channelsOf(after)
  if (!channels) return result
  for (const ch of channels) {
    if (before.forcedOn.includes(ch.channel) && !ch.state)
      result.deviations.push(`DO${ch.channel} đang force BẬT nhưng sau khởi động đang TẮT`)
    else if (before.forcedOff.includes(ch.channel) && ch.state)
      result.deviations.push(`DO${ch.channel} đang force TẮT nhưng sau khởi động đang BẬT`)
    else if (!ch.forced && ch.state)
      result.notes.push(`DO${ch.channel} BẬT sau khởi động, có thể do luật DO SET_HIGH/SET_LOW`)
  }
  return result
}

/** The signed uptime of system.get_info dropped: the only proof the device restarted. */
export function isVerifiedReboot(before: unknown, after: unknown): boolean {
  return typeof before === 'number' && typeof after === 'number' && Number.isFinite(before) && Number.isFinite(after) && after < before
}

export function rebootGate(input: RebootGateInput): RebootGate {
  if (!input.allowedCommands?.includes(REBOOT_KEY)) return { visible: false, enabled: false, reason: null }
  const locked = (reason: string): RebootGate => ({ visible: true, enabled: false, reason })

  if (input.running) return locked('Đang khởi động lại…')
  if (!input.catalogVerified) return locked('Catalog lệnh chưa được xác minh')
  if (!input.link || input.link.online === false || input.link.brokerConnected === false) return locked('Thiết bị offline')
  if (!input.link.binding?.mqttDeviceId) return locked('Chưa có định danh MQTT')
  return { visible: true, enabled: true, reason: null }
}

/** What the operator types: built from the MQTT device id of the binding (spec 7), never the server id. */
export function rebootPhrase(link: DeviceLinkDto | null): string {
  const id = link?.binding?.mqttDeviceId
  return id ? `REBOOT-${id}` : ''
}

const MESSAGES: Record<string, string> = {
  DEVICE_REBOOT_SCHEDULED: 'Đang khởi động lại…',
  DEVICE_REBOOT_NOT_OBSERVED: 'Chưa thấy thiết bị lên lại sau 2 phút; server vẫn chờ thêm 10 phút. Không gửi lại',
  DEVICE_REBOOT_OUTCOME_UNKNOWN: 'Chưa rõ kết quả (server mất theo dõi)',
  DEVICE_REBOOT_IN_PROGRESS: 'Thiết bị đang khởi động lại, chờ kết quả',
  DEVICE_REBOOT_TOO_SOON: 'Chưa đủ 60 s từ lần khởi động lại trước; thử lại sau',
  HIGH_RISK_REQUIRES_MASTER_ADMIN: 'Chỉ master-admin được khởi động lại thiết bị',
  CONFIRM_MISMATCH: 'Mã xác nhận không khớp định danh MQTT của thiết bị',
  CONFIRM_REQUIRED: 'Thiếu mã xác nhận',
  COMMAND_NOT_FOUND: 'Server đã khởi động lại và mất bản ghi lệnh; chưa rõ kết quả',
  REBOOT_SEND_UNCONFIRMED: 'Không rõ lệnh có tới server không; chưa rõ kết quả',
  DEVICE_WRITE_BUSY: 'Thiết bị đang xử lý lệnh ghi khác, thử lại sau',
  DEVICE_WRITE_RATE_LIMITED: 'Gửi lệnh ghi quá nhanh, thử lại sau một phút',
  COMMAND_PAYLOAD_INVALID: 'Server từ chối nội dung lệnh',
  CATALOG_STALE: 'Catalog lệnh trên server đã đổi, hãy tải lại trang',
  DEVICE_PERMISSION_REQUIRED: 'Không đủ quyền khởi động lại thiết bị này',
}

/** A sentence for the operator for a result code of the reboot flow; `verified` is the signed-uptime check. */
export function rebootMessage(code: string, detail: string | null, verified = false): string {
  if (code === 'DEVICE_REBOOT_OBSERVED') return verified ? 'Đã khởi động lại' : 'Thiết bị báo đã lên lại (chưa xác minh)'
  if (code === 'DEVICE_REJECTED') {
    if (detail === 'UNAUTHORIZED') return 'Thiết bị từ chối (UNAUTHORIZED: chống lặp, safe mode hoặc giới hạn 60 s)'
    if (detail === 'confirm_required') return 'Thiết bị từ chối mã xác nhận'
  }
  return MESSAGES[code] ?? (detail ? `${code}: ${detail}` : code)
}

/** What a dry run proves (spec 4.2, L551): the real command can still be refused. */
export const DRY_RUN_NOTE = 'Chạy thử chỉ kiểm chữ ký, khoá, đồng hồ và lệnh được thiết bị nhận; lệnh thật vẫn có thể bị từ chối'

/** The one line the operator reads when a reboot run ends; `kind` is the `RebootKind` of the run. */
export function rebootResultText(kind: RebootKind, code: string, detail: string | null, verified: boolean): string {
  switch (kind) {
    case 'observed': return rebootMessage(code, detail, verified)
    case 'relay_unchecked': return `${rebootMessage(code, detail, verified)}. Chưa kiểm được trạng thái relay`
    case 'dry_run_failed': return `Chạy thử không đạt: ${rebootMessage(code, detail)}`
    case 'tracking_timeout': return 'Chưa có kết quả; server vẫn theo dõi, xem lại sau'
    case 'untracked': return 'Thiết bị đã nhận lệnh nhưng server không theo dõi được việc lên lại'
    case 'server_restarted': return rebootMessage('COMMAND_NOT_FOUND', null)
    default: return rebootMessage(code, detail)
  }
}
