import type { LocalePreference } from './locale-store'

/**
 * Từ điển cốt lõi dùng chung toàn hệ thống (FSD shared/i18n).
 * Đảm bảo 100% Type-Safe và biên dịch an toàn (Compile-time Checked).
 */
export const CORE_DICTIONARY = {
  common: {
    loading: { vi: 'Đang tải...', en: 'Loading...' },
    noData: { vi: 'Không có dữ liệu', en: 'No data' },
    total: { vi: 'Tổng cộng', en: 'Total' },
    actions: { vi: 'Thao tác', en: 'Actions' },
    status: { vi: 'Trạng thái', en: 'Status' },
    success: { vi: 'Thành công', en: 'Success' },
    error: { vi: 'Lỗi', en: 'Error' },
    warning: { vi: 'Cảnh báo', en: 'Warning' },
    info: { vi: 'Thông tin', en: 'Info' },
    all: { vi: 'Tất cả', en: 'All' },
    select: { vi: 'Chọn', en: 'Select' },
    search: { vi: 'Tìm kiếm', en: 'Search' },
    filter: { vi: 'Bộ lọc', en: 'Filter' },
    clear: { vi: 'Xoá bộ lọc', en: 'Clear' },
    confirm: { vi: 'Xác nhận', en: 'Confirm' },
    cancel: { vi: 'Huỷ', en: 'Cancel' },
    save: { vi: 'Lưu', en: 'Save' },
    edit: { vi: 'Chỉnh sửa', en: 'Edit' },
    delete: { vi: 'Xoá', en: 'Delete' },
    detail: { vi: 'Chi tiết', en: 'Detail' },
    refresh: { vi: 'Làm mới', en: 'Refresh' },
    close: { vi: 'Đóng', en: 'Close' },
    back: { vi: 'Quay lại', en: 'Back' },
    next: { vi: 'Tiếp theo', en: 'Next' },
    previous: { vi: 'Trước', en: 'Previous' },
    view: { vi: 'Xem', en: 'View' },
    export: { vi: 'Xuất dữ liệu', en: 'Export' },
    import: { vi: 'Nhập dữ liệu', en: 'Import' },
    download: { vi: 'Tải xuống', en: 'Download' },
    upload: { vi: 'Tải lên', en: 'Upload' },
    na: { vi: 'N/A', en: 'N/A' },
  },
  nav: {
    overview: { vi: 'Tổng quan', en: 'Overview' },
    devices: { vi: 'Thiết bị & Trạm', en: 'Devices & Stations' },
    stations: { vi: 'Trạm quan trắc', en: 'Monitoring Stations' },
    gateways: { vi: 'Thiết bị', en: 'Devices' },
    rtu: { vi: 'Khối RTU', en: 'RTU Units' },
    terminals: { vi: 'Cụm đo', en: 'Measurement Terminals' },
    sensors: { vi: 'Cảm biến', en: 'Sensors' },
    signals: { vi: 'Kênh tín hiệu', en: 'Signals' },
    alarms: { vi: 'Cảnh báo sự cố', en: 'Alarms & Alerts' },
    history: { vi: 'Lịch sử & Phân tích', en: 'History & Analytics' },
    reports: { vi: 'Báo cáo quan trắc', en: 'Reports' },
    reportTemplates: { vi: 'Mẫu báo cáo', en: 'Report Templates' },
    schedule: { vi: 'Lịch đo tự động', en: 'Schedules' },
    rules: { vi: 'Động cơ cảnh báo', en: 'Rule Engine' },
    alertChannels: { vi: 'Kênh cảnh báo', en: 'Alert Channels' },
    modbus: { vi: 'Modbus Slave', en: 'Modbus Slave' },
    system: { vi: 'Quản trị hệ thống', en: 'System Administration' },
    users: { vi: 'Người dùng', en: 'Users' },
    roles: { vi: 'Vai trò & Quyền', en: 'Roles & Permissions' },
    companies: { vi: 'Đơn vị quản lý', en: 'Companies' },
    projects: { vi: 'Dự án', en: 'Projects' },
    areas: { vi: 'Khu vực đo', en: 'Measurement Areas' },
    deviceGroups: { vi: 'Nhóm thiết bị', en: 'Device Groups' },
    auditLogs: { vi: 'Nhật ký hệ thống', en: 'Audit Logs' },
    settings: { vi: 'Cài đặt', en: 'Settings' },
    profile: { vi: 'Hồ sơ cá nhân', en: 'Profile' },
    logout: { vi: 'Đăng xuất', en: 'Logout' },
  },
  units: {
    mpa: { vi: 'MPa', en: 'MPa' },
    kn: { vi: 'kN', en: 'kN' },
    mm: { vi: 'mm', en: 'mm' },
    celsius: { vi: '°C', en: '°C' },
    bar: { vi: 'bar', en: 'bar' },
    hz: { vi: 'Hz', en: 'Hz' },
    percent: { vi: '%', en: '%' },
    ms: { vi: 'ms', en: 'ms' },
    sec: { vi: 'giây', en: 'sec' },
    min: { vi: 'phút', en: 'min' },
    hour: { vi: 'giờ', en: 'hour' },
    day: { vi: 'ngày', en: 'day' },
    records: { vi: 'bản ghi', en: 'records' },
    devices: { vi: 'thiết bị', en: 'devices' },
    points: { vi: 'điểm đo', en: 'points' },
  },
  table: {
    columns: { vi: 'Hiển thị cột', en: 'Columns' },
    filter: { vi: 'Lọc dữ liệu', en: 'Filter' },
    sort: { vi: 'Sắp xếp', en: 'Sort' },
    page: { vi: 'Trang', en: 'Page' },
    pageSize: { vi: 'Số dòng mỗi trang', en: 'Rows per page' },
    emptyTitle: { vi: 'Không có dữ liệu', en: 'No data available' },
    emptyDescription: { vi: 'Không tìm thấy bản ghi nào khớp với điều kiện tìm kiếm.', en: 'No records matched your search criteria.' },
    selectedCount: { vi: 'Đã chọn {count} mục', en: '{count} items selected' },
    totalRows: { vi: 'Tổng cộng: {total} bản ghi', en: 'Total: {total} records' },
    showingRange: { vi: 'Hiển thị {from}-{to} trên {total}', en: 'Showing {from}-{to} of {total}' },
  },
  dialog: {
    confirmTitle: { vi: 'Xác nhận thao tác', en: 'Confirm Action' },
    deleteConfirm: { vi: 'Bạn có chắc chắn muốn xoá mục này không?', en: 'Are you sure you want to delete this item?' },
    unsavedChanges: { vi: 'Bạn có thay đổi chưa lưu. Bạn có muốn rời khỏi trang không?', en: 'You have unsaved changes. Are you sure you want to leave?' },
    actionCannotUndone: { vi: 'Hành động này không thể hoàn tác.', en: 'This action cannot be undone.' },
    confirmText: { vi: 'Đồng ý', en: 'Confirm' },
    cancelText: { vi: 'Huỷ bỏ', en: 'Cancel' },
    dangerAction: { vi: 'Thao tác nguy hiểm', en: 'Dangerous Operation' },
  },
  auth: {
    login: { vi: 'Đăng nhập', en: 'Sign In' },
    logout: { vi: 'Đăng xuất', en: 'Sign Out' },
    username: { vi: 'Tên đăng nhập', en: 'Username' },
    password: { vi: 'Mật khẩu', en: 'Password' },
    rememberMe: { vi: 'Ghi nhớ đăng nhập', en: 'Remember me' },
    forgotPassword: { vi: 'Quên mật khẩu?', en: 'Forgot password?' },
    unauthorized: { vi: 'Yêu cầu đăng nhập', en: 'Authentication required' },
    forbidden: { vi: 'Truy cập bị từ chối', en: 'Access denied' },
  },
} as const

export type CoreDictionary = typeof CORE_DICTIONARY

type Join<K, P> = K extends string | number
  ? P extends string | number
    ? `${K}.${P}`
    : never
  : never

type Paths<T> = T extends { vi: string; en: string }
  ? never
  : T extends Record<string, unknown>
  ? { [K in keyof T]-?: K extends string ? K | Join<K, Paths<T[K]>> : never }[keyof T]
  : never

export type CoreTranslationKey = Paths<CoreDictionary>

/**
 * Tra cứu giá trị dịch phân cấp an toàn (chống Prototype Pollution).
 */
export function getNestedTranslation(
  dict: Record<string, unknown>,
  path: string,
  locale: LocalePreference = 'vi',
  fallback?: string,
): string {
  if (!path || typeof path !== 'string') {
    return fallback ?? ''
  }

  const parts = path.split('.')
  let current: unknown = dict

  for (const part of parts) {
    if (
      part === '__proto__' ||
      part === 'prototype' ||
      part === 'constructor' ||
      current == null ||
      typeof current !== 'object'
    ) {
      return fallback ?? path
    }

    if (!Object.prototype.hasOwnProperty.call(current, part)) {
      return fallback ?? path
    }

    current = (current as Record<string, unknown>)[part]
  }

  if (current && typeof current === 'object') {
    const record = current as Record<string, unknown>
    if (locale in record && typeof record[locale] === 'string') {
      return record[locale] as string
    }
    if ('vi' in record && typeof record.vi === 'string') {
      return record.vi as string
    }
    if ('en' in record && typeof record.en === 'string') {
      return record.en as string
    }
  }

  return fallback ?? path
}
