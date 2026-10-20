import { getActivePinia, piniaSymbol } from 'pinia'
import { computed, hasInjectionContext, inject, ref } from 'vue'
import { readStoredLocale, useLocaleStore, type LocalePreference } from './locale-store'

/**
 * Phân loại nhóm trạng thái hệ thống:
 * - 'operational': Trạng thái vận hành / vòng đời thực thể (Hoạt động, Dừng hoạt động).
 * - 'connectivity': Trạng thái kết nối phần cứng / truyền thông (Kết nối, Mất kết nối, Suy giảm).
 * - 'auto': Tự động suy luận theo tên trạng thái.
 */
export type StatusCategory = 'operational' | 'connectivity' | 'auto'

/**
 * Bộ từ điển trạng thái vận hành / vòng đời (Operational Statuses).
 * Chuẩn hóa nhãn tiếng Việt: "Hoạt động" (Active) và "Dừng hoạt động" (Inactive / Suspended).
 */
export const OPERATIONAL_STATUSES = {
  active: { vi: 'Hoạt động', en: 'Active' },
  suspended: { vi: 'Dừng hoạt động', en: 'Inactive' },
  inactive: { vi: 'Dừng hoạt động', en: 'Inactive' },
  locked: { vi: 'Đã khoá', en: 'Locked' },
  unknown: { vi: 'Không rõ', en: 'Unknown' },
} as const

/**
 * Bộ từ điển trạng thái kết nối phần cứng / truyền thông (Connectivity Statuses).
 * Chuẩn hóa nhãn tiếng Việt: "Kết nối" (Connected / Online) và "Mất kết nối" (Disconnected / Offline).
 */
export const CONNECTIVITY_STATUSES = {
  connected: { vi: 'Kết nối', en: 'Connected' },
  online: { vi: 'Kết nối', en: 'Connected' },
  disconnected: { vi: 'Mất kết nối', en: 'Disconnected' },
  offline: { vi: 'Mất kết nối', en: 'Disconnected' },
  degraded: { vi: 'Suy giảm', en: 'Degraded' },
  connecting: { vi: 'Đang kết nối', en: 'Connecting' },
  control_plane_only: { vi: 'Chỉ kênh điều khiển', en: 'Control plane only' },
  unknown: { vi: 'Không rõ', en: 'Unknown' },
} as const

/**
 * Bộ từ điển trạng thái dùng chung toàn hệ thống (FSD shared/i18n).
 * Hỗ trợ đa ngôn ngữ VI / EN và đồng bộ giao diện.
 */
export const COMMON_STATUSES = {
  ...OPERATIONAL_STATUSES,
  ...CONNECTIVITY_STATUSES,
  warning: { vi: 'Cảnh báo', en: 'Warning' },
  error: { vi: 'Lỗi', en: 'Error' },
  imported: { vi: 'Đã nạp', en: 'Imported' },
  missing: { vi: 'Chưa nạp', en: 'Missing' },
  overdue: { vi: 'Quá hạn', en: 'Overdue' },
  paused: { vi: 'Tạm dừng', en: 'Paused' },
} as const

export type CommonStatusKey = keyof typeof COMMON_STATUSES
export type StatusKey = CommonStatusKey
export type OperationalStatusKey = keyof typeof OPERATIONAL_STATUSES
export type ConnectivityStatusKey = keyof typeof CONNECTIVITY_STATUSES
export type StatusTranslation = { readonly vi: string; readonly en: string }

export type StatusTone = 'online' | 'offline' | 'warning' | 'unknown'
export type BadgeTone = 'success' | 'danger' | 'warning' | 'neutral'

export type StandardStatusOption = {
  value: 'active' | 'suspended'
  label: string
  tone: 'success' | 'neutral'
}

/**
 * Dịch trạng thái vận hành sang ngôn ngữ được chọn.
 */
export function getOperationalLabel(
  status: string | null | undefined,
  locale: LocalePreference = 'vi',
): string {
  if (!status) return '—'
  const key = status.trim().toLowerCase() as OperationalStatusKey
  if (Object.prototype.hasOwnProperty.call(OPERATIONAL_STATUSES, key)) {
    return OPERATIONAL_STATUSES[key][locale] || OPERATIONAL_STATUSES[key].vi
  }
  if (Object.prototype.hasOwnProperty.call(COMMON_STATUSES, key)) {
    const item = COMMON_STATUSES[key as CommonStatusKey]
    return item[locale] || item.vi
  }
  return status
}

/**
 * Dịch trạng thái kết nối sang ngôn ngữ được chọn.
 */
export function getConnectivityLabel(
  status: string | null | undefined,
  locale: LocalePreference = 'vi',
): string {
  if (!status) return '—'
  const key = status.trim().toLowerCase() as ConnectivityStatusKey
  if (Object.prototype.hasOwnProperty.call(CONNECTIVITY_STATUSES, key)) {
    return CONNECTIVITY_STATUSES[key][locale] || CONNECTIVITY_STATUSES[key].vi
  }
  if (Object.prototype.hasOwnProperty.call(COMMON_STATUSES, key)) {
    const item = COMMON_STATUSES[key as CommonStatusKey]
    return item[locale] || item.vi
  }
  return status
}

/**
 * Ánh xạ trạng thái và phân loại sang tone màu của Soft Pill Badge (StatusBadge).
 */
export function resolveBadgeTone(
  status: string | null | undefined,
  type: StatusCategory = 'auto',
): BadgeTone {
  if (!status) return 'neutral'
  const s = status.trim().toLowerCase()

  if (type === 'connectivity') {
    if (s === 'online' || s === 'connected' || s === 'in_sync') return 'success'
    if (s === 'offline' || s === 'disconnected') return 'danger'
    if (s === 'degraded' || s === 'connecting' || s === 'control_plane_only') return 'warning'
    return 'neutral'
  }

  if (type === 'operational') {
    if (s === 'active' || s === 'running') return 'success'
    if (s === 'suspended' || s === 'inactive' || s === 'stopped') return 'neutral'
    if (s === 'locked' || s === 'error') return 'danger'
    if (s === 'warning') return 'warning'
    return 'neutral'
  }

  // Auto detect
  if (s === 'active' || s === 'online' || s === 'connected' || s === 'running' || s === 'in_sync' || s === 'imported' || s === 'success') {
    return 'success'
  }
  if (s === 'offline' || s === 'disconnected' || s === 'error' || s === 'overdue' || s === 'danger') {
    return 'danger'
  }
  if (s === 'degraded' || s === 'warning' || s === 'connecting' || s === 'missing') {
    return 'warning'
  }
  if (s === 'paused') {
    return 'neutral'
  }
  return 'neutral'
}

/**
 * Dịch trực tiếp trạng thái sang ngôn ngữ được chọn (pure function).
 * Bảo vệ an toàn trước prototype pollution, null, undefined và giá trị bất thường.
 */
export function translateStatus(
  statusKey: CommonStatusKey | string | null | undefined,
  locale: LocalePreference = 'vi',
): string {
  if (statusKey === null || statusKey === undefined) return ''
  if (typeof statusKey !== 'string') return String(statusKey)

  const key = statusKey.trim()
  if (!key) return ''

  if (Object.prototype.hasOwnProperty.call(COMMON_STATUSES, key)) {
    const item = COMMON_STATUSES[key as CommonStatusKey]
    return item[locale] || item.vi
  }

  return statusKey
}

/**
 * Ánh xạ trạng thái sang tông màu chấm chỉ báo cũ (tương thích ngược).
 */
export function getStatusTone(status: string | null | undefined): StatusTone {
  if (!status || typeof status !== 'string') return 'unknown'
  const s = status.trim().toLowerCase()

  if (s === 'active' || s === 'online' || s === 'connected' || s === 'in_sync') {
    return 'online'
  }
  if (s === 'suspended' || s === 'inactive' || s === 'offline' || s === 'disconnected') {
    return 'offline'
  }
  if (s === 'warning' || s === 'degraded') {
    return 'warning'
  }
  return 'unknown'
}

/**
 * Composable cung cấp nhãn trạng thái phản ứng theo Pinia useLocaleStore.
 * Tự động fallback an toàn nếu Pinia chưa được khởi tạo (vd môi trường unit test).
 */
export function useStatusTranslation() {
  let localeStore: ReturnType<typeof useLocaleStore> | null = null
  const pinia = hasInjectionContext() ? inject(piniaSymbol, null) : getActivePinia()
  if (pinia) {
    try {
      localeStore = useLocaleStore(pinia)
    } catch {
      localeStore = null
    }
  }

  const fallbackLocale = ref<LocalePreference>(readStoredLocale())

  const getCurrentLocale = (): LocalePreference => {
    return localeStore ? localeStore.current : fallbackLocale.value
  }

  const tStatus = (key: CommonStatusKey | string): string => {
    return translateStatus(key, getCurrentLocale())
  }

  const getOperational = (status: string | null | undefined): string => {
    return getOperationalLabel(status, getCurrentLocale())
  }

  const getConnectivity = (status: string | null | undefined): string => {
    return getConnectivityLabel(status, getCurrentLocale())
  }

  const statuses = computed(() => {
    const loc = getCurrentLocale()
    const map = {} as Record<CommonStatusKey, string>
    for (const key of Object.keys(COMMON_STATUSES) as CommonStatusKey[]) {
      map[key] = COMMON_STATUSES[key][loc] || COMMON_STATUSES[key].vi
    }
    return map
  })

  return {
    tStatus,
    getOperationalLabel: getOperational,
    getConnectivityLabel: getConnectivity,
    resolveBadgeTone,
    statuses,
    translateStatus: (key: CommonStatusKey | string) => translateStatus(key, getCurrentLocale()),
  }
}

/**
 * Trả về danh sách tuỳ chọn trạng thái chuẩn cho các biểu mẫu (Form) và bộ lọc (Filter).
 */
export function getStandardStatusOptions(locale: LocalePreference = 'vi'): StandardStatusOption[] {
  return [
    { value: 'active', label: getOperationalLabel('active', locale), tone: 'success' },
    { value: 'suspended', label: getOperationalLabel('suspended', locale), tone: 'neutral' },
  ]
}
