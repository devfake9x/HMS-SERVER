import type { LocalePreference } from './locale-store'

/**
 * Định dạng số học theo chuẩn văn hóa (Locale-aware Number Formatting).
 * - Tiếng Việt (vi-VN): Phân cách thập phân bằng dấu phẩy ',', hàng nghìn bằng dấu chấm '.'.
 * - Tiếng Anh (en-US): Phân cách thập phân bằng dấu chấm '.', hàng nghìn bằng dấu phẩy ','.
 */
export function formatNumber(
  value: number | string | null | undefined,
  locale: LocalePreference = 'vi',
  options?: Intl.NumberFormatOptions,
): string {
  if (value === null || value === undefined || value === '') {
    return '-'
  }

  const num = typeof value === 'number' ? value : Number(value)
  if (!Number.isFinite(num)) {
    return '-'
  }

  const intlLocale = locale === 'vi' ? 'vi-VN' : 'en-US'
  try {
    return new Intl.NumberFormat(intlLocale, options).format(num)
  } catch {
    return String(num)
  }
}

export type DateTimeFormatStyle = 'short' | 'full' | 'date' | 'time'

function padZero(n: number): string {
  return n < 10 ? `0${n}` : String(n)
}

/**
 * Định dạng thời gian theo chuẩn công nghiệp SCADA và Locale:
 * - 'full': 'dd/MM/yyyy HH:mm:ss' (VI) vs 'yyyy-MM-dd HH:mm:ss' (EN)
 * - 'date': 'dd/MM/yyyy' (VI) vs 'yyyy-MM-dd' (EN)
 * - 'time': 'HH:mm:ss' (cả hai)
 * - 'short': 'dd/MM/yy HH:mm' (VI) vs 'yyyy-MM-dd HH:mm' (EN)
 */
export function formatDateTime(
  dateInput: Date | string | number | null | undefined,
  locale: LocalePreference = 'vi',
  style: DateTimeFormatStyle = 'full',
): string {
  if (dateInput === null || dateInput === undefined || dateInput === '') {
    return '-'
  }

  const d = dateInput instanceof Date ? dateInput : new Date(dateInput)
  if (Number.isNaN(d.getTime())) {
    return '-'
  }

  const day = padZero(d.getDate())
  const month = padZero(d.getMonth() + 1)
  const year = String(d.getFullYear())
  const hours = padZero(d.getHours())
  const minutes = padZero(d.getMinutes())
  const seconds = padZero(d.getSeconds())

  if (style === 'time') {
    return `${hours}:${minutes}:${seconds}`
  }

  if (locale === 'vi') {
    if (style === 'date') return `${day}/${month}/${year}`
    if (style === 'short') return `${day}/${month}/${year.slice(-2)} ${hours}:${minutes}`
    return `${day}/${month}/${year} ${hours}:${minutes}:${seconds}`
  }

  // en-US / ISO 8601 SCADA standard
  if (style === 'date') return `${year}-${month}-${day}`
  if (style === 'short') return `${year}-${month}-${day} ${hours}:${minutes}`
  return `${year}-${month}-${day} ${hours}:${minutes}:${seconds}`
}

/**
 * Định dạng đại lượng đo đạc kỹ thuật quan trắc ứng suất (TCVN 7870 & ISO 80000):
 * - Giữ nguyên ký hiệu đơn vị SI chuẩn: 'MPa', 'kN', 'mm', '°C', 'bar', 'Hz'.
 * - Định dạng số theo Locale đã chọn với số chữ số thập phân chỉ định (mặc định 2).
 */
export function formatTelemetryValue(
  value: number | string | null | undefined,
  unit: string,
  locale: LocalePreference = 'vi',
  decimals: number = 2,
): string {
  if (value === null || value === undefined || value === '') {
    return '-'
  }

  const num = typeof value === 'number' ? value : Number(value)
  if (!Number.isFinite(num)) {
    return '-'
  }

  const formattedNum = formatNumber(num, locale, {
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals,
  })

  const trimmedUnit = (unit || '').trim()
  return trimmedUnit ? `${formattedNum} ${trimmedUnit}` : formattedNum
}
