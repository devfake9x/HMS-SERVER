import { defineStore } from 'pinia'
import { computed, onScopeDispose, ref } from 'vue'

/**
 * Quản lý lựa chọn ngôn ngữ giao diện (VI / EN).
 *
 * Lưu vào localStorage theo khoá `csm.locale`.
 * Cập nhật thuộc tính `lang` trên thẻ <html>.
 */

export type LocalePreference = 'vi' | 'en'

export const LOCALE_STORAGE_KEY = 'csm.locale'
export const LOCALE_PREFERENCES: readonly LocalePreference[] = ['vi', 'en']

export function isLocalePreference(value: unknown): value is LocalePreference {
  return value === 'vi' || value === 'en'
}

/**
 * Đọc lựa chọn ngôn ngữ đã lưu.
 * Mặc định trả về 'vi' nếu chưa chọn hoặc giá trị lạ.
 */
export function readStoredLocale(): LocalePreference {
  try {
    const raw = localStorage.getItem(LOCALE_STORAGE_KEY)
    return isLocalePreference(raw) ? raw : 'vi'
  } catch {
    return 'vi'
  }
}

export const useLocaleStore = defineStore('locale', () => {
  const current = ref<LocalePreference>(readStoredLocale())

  function applyToDom(): void {
    if (typeof document !== 'undefined') {
      document.documentElement.lang = current.value
    }
  }

  function onStorage(event: StorageEvent): void {
    if (event.key !== LOCALE_STORAGE_KEY) return
    current.value = isLocalePreference(event.newValue) ? event.newValue : 'vi'
    applyToDom()
  }

  if (typeof window !== 'undefined') {
    window.addEventListener('storage', onStorage)
  }

  onScopeDispose(() => {
    if (typeof window !== 'undefined') {
      window.removeEventListener('storage', onStorage)
    }
  })

  function setLocale(next: LocalePreference): void {
    current.value = next
    try {
      localStorage.setItem(LOCALE_STORAGE_KEY, next)
    } catch {
      // Bỏ qua lỗi nếu localStorage bị chặn
    }
    applyToDom()
  }

  applyToDom()

  return {
    current: computed(() => current.value),
    setLocale,
  }
})
