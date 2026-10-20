import { computed, ref } from 'vue'

import type { NavBadge } from '#/shared/navigation/nav'

/**
 * Hàng đợi báo động trung tâm (Alarm Queue & Registry). Base architecture L5 & ISA-18.2.
 *
 * Tương tự như `shared/notify/notify.ts`, file này quản lý danh sách các sự cố
 * cần sự chú ý của người vận hành (Gateway mất kết nối, cảm biến vượt ngưỡng, kênh RTU suy giảm).
 *
 * Đặt ở `shared/notify/` để:
 * 1. Các component dùng chung như `AlarmBell.vue` và `AppShell.vue` đọc được trực tiếp
 *    mà KHÔNG vi phạm ranh giới FSD (Gate 5).
 * 2. Mọi feature (như `features/gateway-device`, `features/monitoring`) có thể đóng
 *    vai trò Producer đẩy sự cố vào mà không tạo cyclic dependency.
 */

export type AlarmSeverity = 'critical' | 'warning' | 'info'
export type AlarmState = 'active_unack' | 'active_ack' | 'cleared'

export type AlarmItem = {
  readonly id: string
  /** Khóa nhận dạng duy nhất cho từng nguồn sự cố (VD: `gw:gw-01:offline`). */
  readonly key: string
  readonly title: string
  readonly location: string
  readonly severity: AlarmSeverity
  readonly state: AlarmState
  readonly occurredAt: number
  readonly acknowledgedBy?: string
  readonly acknowledgedAt?: number
  readonly note?: string
  readonly details?: Record<string, unknown>
}

export type RaiseAlarmInput = {
  readonly key: string
  readonly title: string
  readonly location: string
  readonly severity: AlarmSeverity
  readonly note?: string
  readonly details?: Record<string, unknown>
}

let nextId = 1
const alarms = ref<AlarmItem[]>([])
const silencedUntil = ref<number>(0)

/**
 * Danh sách toàn bộ báo động đang hoạt động (chưa được giải tỏa hoàn toàn).
 */
export const activeAlarms = computed(() =>
  alarms.value.filter((a) => a.state !== 'cleared'),
)

/**
 * Số lượng sự cố chưa được tiếp nhận xử lý (`active_unack`).
 */
export const unackCount = computed(() =>
  alarms.value.filter((a) => a.state === 'active_unack').length,
)

/**
 * Số lượng sự cố cấp nguy cấp (Critical) chưa tiếp nhận.
 */
export const criticalUnackCount = computed(() =>
  alarms.value.filter((a) => a.state === 'active_unack' && a.severity === 'critical').length,
)

/**
 * Số lượng sự cố cấp cảnh báo (Warning) chưa tiếp nhận.
 */
export const warningUnackCount = computed(() =>
  alarms.value.filter((a) => a.state === 'active_unack' && a.severity === 'warning').length,
)

/**
 * Kiểm tra xem cảnh báo có đang ở chế độ tạm tắt âm/im lặng (Silenced) không.
 */
export function isSilenced(now = Date.now()): boolean {
  return silencedUntil.value > now
}

/**
 * Tạm tắt âm và trạng thái khẩn cấp trong một khoảng thời gian (ms).
 */
export function silenceAlarms(durationMs: number, now = Date.now()): void {
  silencedUntil.value = now + Math.max(0, durationMs)
}

/**
 * Kích hoạt hoặc cập nhật một sự cố vào hàng đợi.
 *
 * Deduplication theo `key`: Nếu sự cố cho nguồn này đã tồn tại và đang active,
 * cập nhật mốc thời gian và thông tin chi tiết. Nếu đã bị clear trước đó, khôi phục
 * lại về `active_unack`.
 */
export function raiseAlarm(input: RaiseAlarmInput, now = Date.now()): AlarmItem {
  const existingIdx = alarms.value.findIndex((a) => a.key === input.key)

  if (existingIdx >= 0) {
    const existing = alarms.value[existingIdx]
    const updated: AlarmItem = {
      ...existing,
      title: input.title,
      location: input.location,
      severity: input.severity,
      occurredAt: now,
      // Nếu đã từng ack nhưng sự cố tái diễn sau khi clear -> chuyển về unack
      state: existing.state === 'cleared' ? 'active_unack' : existing.state,
      note: input.note ?? existing.note,
      details: input.details ?? existing.details,
    }
    const nextList = [...alarms.value]
    nextList[existingIdx] = updated
    alarms.value = nextList
    return updated
  }

  const newItem: AlarmItem = {
    id: `alarm-${nextId++}`,
    key: input.key,
    title: input.title,
    location: input.location,
    severity: input.severity,
    state: 'active_unack',
    occurredAt: now,
    note: input.note,
    details: input.details,
  }

  alarms.value = [newItem, ...alarms.value]
  return newItem
}

/**
 * Xác nhận tiếp nhận sự cố (Acknowledge) theo chuẩn ISA-18.2.
 */
export function acknowledgeAlarm(
  id: string,
  operatorName = 'Kỹ thuật viên',
  note?: string,
  now = Date.now(),
): boolean {
  const idx = alarms.value.findIndex((a) => a.id === id)
  if (idx < 0) return false

  const target = alarms.value[idx]
  if (target.state === 'active_ack' || target.state === 'cleared') return false

  const updated: AlarmItem = {
    ...target,
    state: 'active_ack',
    acknowledgedBy: operatorName,
    acknowledgedAt: now,
    note: note ?? target.note,
  }

  const nextList = [...alarms.value]
  nextList[idx] = updated
  alarms.value = nextList
  return true
}

/**
 * Tiếp nhận tất cả các sự cố chưa xử lý.
 */
export function acknowledgeAllAlarms(operatorName = 'Kỹ thuật viên', now = Date.now()): void {
  alarms.value = alarms.value.map((a) =>
    a.state === 'active_unack'
      ? {
          ...a,
          state: 'active_ack',
          acknowledgedBy: operatorName,
          acknowledgedAt: now,
        }
      : a,
  )
}

/**
 * Giải tỏa một sự cố khi thiết bị khôi phục bình thường (VD: Gateway online trở lại).
 */
export function clearAlarm(key: string): void {
  const idx = alarms.value.findIndex((a) => a.key === key)
  if (idx < 0) return

  const target = alarms.value[idx]
  if (target.state === 'cleared') return

  // Đổi trạng thái sang cleared
  const updated: AlarmItem = { ...target, state: 'cleared' }
  const nextList = [...alarms.value]
  nextList[idx] = updated
  alarms.value = nextList
}

/**
 * Tạo đối tượng NavBadge sẵn sàng cho `nav.config.ts`.
 */
export function getAlarmNavBadge(prefixKey?: string): NavBadge | null {
  const matchedAlarms = alarms.value.filter((a) => {
    if (a.state === 'cleared') return false
    if (prefixKey && !a.key.startsWith(prefixKey)) return false
    return true
  })

  if (matchedAlarms.length === 0) return null

  const hasCriticalUnack = matchedAlarms.some(
    (a) => a.severity === 'critical' && a.state === 'active_unack',
  )
  const hasCritical = matchedAlarms.some((a) => a.severity === 'critical')
  const hasWarning = matchedAlarms.some((a) => a.severity === 'warning')

  const level = hasCritical ? 'critical' : hasWarning ? 'warning' : 'info'
  const count = matchedAlarms.length

  return {
    count,
    level,
    pulse: hasCriticalUnack,
    label: `${count} cảnh báo kết nối`,
  }
}

/**
 * Xóa trắng toàn bộ hàng đợi báo động, phục vụ cho unit test cô lập.
 */
export function __resetAlarmsForTest(): void {
  alarms.value = []
  silencedUntil.value = 0
  nextId = 1
}
