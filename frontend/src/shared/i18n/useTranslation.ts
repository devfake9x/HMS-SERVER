import { getActivePinia, piniaSymbol } from 'pinia'
import { computed, hasInjectionContext, inject, ref } from 'vue'
import { COMMON_ACTIONS, translateAction, type ActionKey } from './actions'
import { CORE_DICTIONARY, getNestedTranslation } from './core'
import { resolveErrorMessage } from './errors'
import {
  formatDateTime,
  formatNumber,
  formatTelemetryValue,
  type DateTimeFormatStyle,
} from './formatters'
import { readStoredLocale, useLocaleStore, type LocalePreference } from './locale-store'
import { COMMON_PLACEHOLDERS, type PlaceholderKey } from './placeholders'
import { COMMON_STATUSES, translateStatus, type StatusKey } from './status'

/**
 * Composable cung cấp ngữ cảnh dịch thuật toàn diện, type-safe và phản ứng linh hoạt.
 * Hoạt động trơn tru trong môi trường ứng dụng Vue 3 cũng như các test case cô lập không có Pinia.
 */
export function useTranslation() {
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

  const current = computed<LocalePreference>(() => getCurrentLocale())
  const isVi = computed<boolean>(() => current.value === 'vi')
  const isEn = computed<boolean>(() => current.value === 'en')

  function setLocale(next: LocalePreference): void {
    if (localeStore) {
      localeStore.setLocale(next)
    } else {
      fallbackLocale.value = next
    }
  }

  /**
   * Tra cứu chuỗi dịch theo khóa phân cấp ('common.loading', 'nav.overview', ...)
   * Hỗ trợ tự động fallback qua ActionKey, StatusKey, PlaceholderKey và nội suy an toàn {param}.
   */
  function t(
    key: string,
    params?: Record<string, unknown>,
    fallback?: string,
  ): string {
    if (!key || typeof key !== 'string') {
      return fallback ?? ''
    }

    const loc = current.value
    let translated: string | null = null

    // 1. Tra cứu trong CORE_DICTIONARY
    const coreResult = getNestedTranslation(CORE_DICTIONARY, key, loc, '__NOT_FOUND__')
    if (coreResult !== '__NOT_FOUND__') {
      translated = coreResult
    }

    // 2. Tra cứu trong COMMON_ACTIONS nếu key khớp ActionKey
    if (translated === null && key in COMMON_ACTIONS) {
      translated = translateAction(key as ActionKey, loc)
    }

    // 3. Tra cứu trong COMMON_STATUSES nếu key khớp StatusKey
    if (translated === null && key in COMMON_STATUSES) {
      translated = translateStatus(key as StatusKey, loc)
    }

    // 4. Tra cứu trong COMMON_PLACEHOLDERS nếu key khớp PlaceholderKey
    if (translated === null && key in COMMON_PLACEHOLDERS) {
      const pEntry = COMMON_PLACEHOLDERS[key as PlaceholderKey]
      translated = pEntry[loc] || pEntry.vi
    }

    // 5. Fallback nếu không tìm thấy bất kỳ đâu
    let result = translated ?? fallback ?? key

    // 6. Nội suy tham số an toàn (Parameter Interpolation)
    if (params && typeof params === 'object') {
      for (const [pKey, pVal] of Object.entries(params)) {
        if (
          pKey === '__proto__' ||
          pKey === 'prototype' ||
          pKey === 'constructor' ||
          pVal === undefined
        ) {
          continue
        }
        result = result.replaceAll(`{${pKey}}`, String(pVal))
      }
    }

    return result
  }

  return {
    current,
    isVi,
    isEn,
    setLocale,
    t,
    formatNumber: (val: number | string | null | undefined, options?: Intl.NumberFormatOptions) =>
      formatNumber(val, current.value, options),
    formatDateTime: (date: Date | string | number | null | undefined, style?: DateTimeFormatStyle) =>
      formatDateTime(date, current.value, style),
    formatTelemetryValue: (val: number | string | null | undefined, unit: string, decimals?: number) =>
      formatTelemetryValue(val, unit, current.value, decimals),
    resolveError: (codeOrMessage: unknown, params?: Record<string, unknown>) =>
      resolveErrorMessage(codeOrMessage, params, current.value),
  }
}

/**
 * Alias thân thuộc theo chuẩn vue-i18n
 */
export const useI18n = useTranslation
