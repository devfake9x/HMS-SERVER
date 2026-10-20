import type { LocalePreference } from './locale-store'

/**
 * Từ điển ánh xạ mã lỗi máy (Machine Error Codes) sang thông điệp bản địa hóa.
 * Hỗ trợ nội suy tham số an toàn (ví dụ: {id}, {code}, {field}).
 */
export const ERROR_DICTIONARY: Record<string, { vi: string; en: string }> = {
  UNAUTHORIZED: {
    vi: 'Phiên làm việc hết hạn hoặc chưa đăng nhập.',
    en: 'Session expired or authentication required.',
  },
  FORBIDDEN: {
    vi: 'Bạn không có quyền thực hiện hành động này.',
    en: 'You do not have permission to perform this action.',
  },
  NOT_FOUND: {
    vi: 'Không tìm thấy dữ liệu yêu cầu.',
    en: 'Requested data was not found.',
  },
  VALIDATION_FAILED: {
    vi: 'Dữ liệu đầu vào không hợp lệ, vui lòng kiểm tra lại.',
    en: 'Invalid input data, please check your submission.',
  },
  GATEWAY_OFFLINE: {
    vi: 'Trạm truyền tin đang mất kết nối.',
    en: 'Gateway is currently offline.',
  },
  DEVICE_NOT_FOUND: {
    vi: 'Không tìm thấy thiết bị {id}.',
    en: 'Device {id} not found.',
  },
  STATION_NOT_FOUND: {
    vi: 'Không tìm thấy trạm quan trắc {id}.',
    en: 'Monitoring station {id} not found.',
  },
  RATE_LIMITED: {
    vi: 'Quá nhiều yêu cầu, vui lòng thử lại sau giây lát.',
    en: 'Too many requests, please try again in a moment.',
  },
  INTERNAL_ERROR: {
    vi: 'Lỗi máy chủ nội bộ. Vui lòng liên hệ quản trị viên.',
    en: 'Internal server error. Please contact system administrator.',
  },
  NETWORK_ERROR: {
    vi: 'Lỗi kết nối mạng. Vui lòng kiểm tra đường truyền.',
    en: 'Network connection error. Please check your network.',
  },
  ALARM_NOT_FOUND: {
    vi: 'Không tìm thấy cảnh báo yêu cầu.',
    en: 'Requested alarm not found.',
  },
  INVALID_CREDENTIALS: {
    vi: 'Tên đăng nhập hoặc mật khẩu không chính xác.',
    en: 'Invalid username or password.',
  },
  SESSION_EXPIRED: {
    vi: 'Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.',
    en: 'Session has expired. Please log in again.',
  },
  DUPLICATE_CODE: {
    vi: 'Mã định danh {code} đã tồn tại trong hệ thống.',
    en: 'Code {code} already exists in the system.',
  },
}

/**
 * Phân giải mã lỗi máy thành thông điệp bản địa hóa với tham số nội suy an toàn.
 */
export function resolveErrorMessage(
  codeOrMessage: unknown,
  params?: Record<string, unknown>,
  locale: LocalePreference = 'vi',
): string {
  const raw = String(codeOrMessage ?? '').trim()
  if (!raw) {
    return locale === 'vi' ? 'Đã xảy ra lỗi không xác định.' : 'An unknown error occurred.'
  }

  const entry = ERROR_DICTIONARY[raw]
  let template = entry ? (entry[locale] || entry.vi) : raw

  if (params && typeof params === 'object') {
    for (const [key, val] of Object.entries(params)) {
      if (
        key === '__proto__' ||
        key === 'prototype' ||
        key === 'constructor' ||
        val === undefined
      ) {
        continue
      }
      template = template.replaceAll(`{${key}}`, String(val))
    }
  }

  return template
}
