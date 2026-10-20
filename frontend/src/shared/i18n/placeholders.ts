import { getActivePinia, piniaSymbol } from 'pinia'
import { computed, hasInjectionContext, inject, ref } from 'vue'
import { readStoredLocale, useLocaleStore, type LocalePreference } from './locale-store'

/**
 * Bộ từ điển placeholder và giá trị mặc định khi chưa điền dữ liệu dùng chung toàn hệ thống (FSD shared/i18n).
 * Hỗ trợ chuyển đổi song ngữ VI / EN và đồng bộ giao diện.
 */
export const COMMON_PLACEHOLDERS = {
  // Trạng thái dữ liệu trống / chưa điền (Readonly & Detail views)
  notUpdated: { vi: 'Chưa cập nhật', en: 'Not updated' },
  notProvided: { vi: 'Chưa cung cấp', en: 'Not provided' },
  noData: { vi: 'Chưa có dữ liệu', en: 'No data' },
  none: { vi: 'Không có', en: 'None' },
  emptyDash: { vi: '—', en: '—' },
  rootUnit: { vi: '— (Đơn vị gốc)', en: '— (Root unit)' },
  unassigned: { vi: 'Chưa gán', en: 'Unassigned' },

  // Gợi ý nhập liệu (Form & Input views)
  enterData: { vi: 'Nhập dữ liệu...', en: 'Enter data...' },
  enterName: { vi: 'Nhập tên...', en: 'Enter name...' },
  enterCode: { vi: 'Nhập mã...', en: 'Enter code...' },
  enterNote: { vi: 'Nhập ghi chú hoặc mô tả...', en: 'Enter note or description...' },
  enterAddress: { vi: 'Nhập địa chỉ...', en: 'Enter address...' },
  enterPhone: { vi: 'Nhập số điện thoại...', en: 'Enter phone number...' },
  enterEmail: { vi: 'Nhập email...', en: 'Enter email...' },
  select: { vi: 'Chọn...', en: 'Select...' },
  search: { vi: 'Tìm kiếm...', en: 'Search...' },
  optional: { vi: '(không bắt buộc)', en: '(optional)' },
} as const

export type PlaceholderKey = keyof typeof COMMON_PLACEHOLDERS
export type PlaceholderTranslation = { readonly vi: string; readonly en: string }

/**
 * Dịch trực tiếp placeholder key sang ngôn ngữ được chọn (pure function).
 * Bảo vệ an toàn trước prototype pollution, null, undefined và giá trị bất thường.
 */
export function translatePlaceholder(
  key: PlaceholderKey | string | null | undefined,
  locale: LocalePreference = 'vi',
): string {
  if (key === null || key === undefined) return ''
  if (typeof key !== 'string') return String(key)

  const k = key.trim()
  if (!k) return ''

  if (Object.prototype.hasOwnProperty.call(COMMON_PLACEHOLDERS, k)) {
    const item = COMMON_PLACEHOLDERS[k as PlaceholderKey]
    return item[locale] || item.vi
  }

  return key
}

/**
 * Composable cung cấp chuỗi placeholder phản ứng theo Pinia useLocaleStore.
 * Tự động fallback an toàn nếu Pinia chưa được khởi tạo (vd môi trường unit test).
 */
export function usePlaceholderTranslation() {
  let localeStore: ReturnType<typeof useLocaleStore> | null = null
  const pinia = (hasInjectionContext() ? inject(piniaSymbol, null) : null) || getActivePinia()
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

  const tPlaceholder = (key: PlaceholderKey | string): string => {
    return translatePlaceholder(key, getCurrentLocale())
  }

  const placeholders = computed(() => {
    const loc = getCurrentLocale()
    const map = {} as Record<PlaceholderKey, string>
    for (const key of Object.keys(COMMON_PLACEHOLDERS) as PlaceholderKey[]) {
      map[key] = COMMON_PLACEHOLDERS[key][loc] || COMMON_PLACEHOLDERS[key].vi
    }
    return map
  })

  return {
    tPlaceholder,
    t: tPlaceholder,
    placeholders,
    translatePlaceholder: (key: PlaceholderKey | string) => translatePlaceholder(key, getCurrentLocale()),
  }
}
