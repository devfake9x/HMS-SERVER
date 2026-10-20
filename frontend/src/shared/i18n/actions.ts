import { getActivePinia, piniaSymbol } from 'pinia'
import { computed, hasInjectionContext, inject, ref } from 'vue'
import { readStoredLocale, useLocaleStore, type LocalePreference } from './locale-store'

/**
 * Bộ từ điển động từ CRUD dùng chung trên toàn bộ hệ thống (FSD shared/i18n).
 * Đảm bảo nhãn hành động không bị phân mảnh theo thực thể (F3).
 */
export const COMMON_ACTIONS = {
  create: { vi: 'Thêm mới', en: 'New' },
  edit: { vi: 'Chỉnh sửa', en: 'Edit' },
  delete: { vi: 'Xoá', en: 'Delete' },
  detail: { vi: 'Chi tiết', en: 'Detail' },
  save: { vi: 'Lưu', en: 'Save' },
  saveChanges: { vi: 'Lưu thay đổi', en: 'Save Changes' },
  cancel: { vi: 'Huỷ', en: 'Cancel' },
  search: { vi: 'Tìm kiếm', en: 'Search' },
  reset: { vi: 'Đặt lại', en: 'Reset' },
  filter: { vi: 'Bộ lọc', en: 'Filter' },
  apply: { vi: 'Áp dụng', en: 'Apply' },
  refresh: { vi: 'Làm mới', en: 'Refresh' },
  back: { vi: 'Quay lại', en: 'Back' },
  prevStep: { vi: 'Bước trước', en: 'Previous step' },
  nextStep: { vi: 'Bước tiếp theo', en: 'Next step' },
  previous: { vi: 'Trước', en: 'Previous' },
  confirm: { vi: 'Xác nhận', en: 'Confirm' },
  close: { vi: 'Đóng', en: 'Close' },
  addChild: { vi: 'Thêm nhánh con', en: 'Add child' },
} as const

export type ActionKey = keyof typeof COMMON_ACTIONS
export type ActionTranslation = { readonly vi: string; readonly en: string }

/**
 * Dịch trực tiếp action key theo locale được chỉ định (pure function).
 */
export function translateAction(key: ActionKey, locale: LocalePreference = 'vi'): string {
  const item = COMMON_ACTIONS[key]
  if (!item) return key
  return item[locale] || item.vi
}

/**
 * Composable cung cấp bản dịch hành động phản ứng theo Pinia useLocaleStore.
 * Tự động dự phòng an toàn (fallback) nếu Pinia chưa được khởi tạo trong môi trường kiểm thử.
 */
export function useActionTranslation() {
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

  const t = (key: ActionKey): string => {
    return translateAction(key, getCurrentLocale())
  }

  const actions = computed(() => {
    const loc = getCurrentLocale()
    const map = {} as Record<ActionKey, string>
    for (const key of Object.keys(COMMON_ACTIONS) as ActionKey[]) {
      map[key] = COMMON_ACTIONS[key][loc] || COMMON_ACTIONS[key].vi
    }
    return map
  })

  return {
    t,
    actions,
    translateAction: (key: ActionKey) => translateAction(key, getCurrentLocale()),
  }
}
