import { can, canAny, type AuthSubject } from '#/core/auth/can'
import type { BreadcrumbItem } from '#/shared/ui/Breadcrumb.vue'
import { isRouteActiveForLink, type NavItem, type NavLink } from './nav'

/**
 * Cấu hình điểm neo fallback cho các route không xuất hiện trực tiếp trong menu chính.
 * Ví dụ: modbus-slaves (ẩn khỏi nav), account-settings, system-configs...
 */
export type FallbackParent = {
  /** Tên nhóm cha (nếu có), ví dụ 'Quản lý', 'Quản trị'. Để trống nếu ở cấp gốc. */
  groupLabel?: string
  /** Tên route cha (nếu có), ví dụ 'modbus-slaves' */
  parentRouteName?: string
  /** Nhãn hiển thị của mục cha */
  parentLabel: string
}

export const DEFAULT_FALLBACK_PARENTS: Record<string, FallbackParent> = {
  companies: {
    groupLabel: 'Quản lý',
    parentRouteName: 'companies',
    parentLabel: 'Đơn vị',
  },
  projects: {
    groupLabel: 'Quản lý',
    parentRouteName: 'projects',
    parentLabel: 'Dự án',
  },
  packages: {
    groupLabel: 'Quản lý',
    parentRouteName: 'projects',
    parentLabel: 'Gói thầu',
  },
  areas: {
    groupLabel: 'Quản lý',
    parentRouteName: 'areas',
    parentLabel: 'Khu vực',
  },
  'gateway-devices': {
    groupLabel: 'Quản lý',
    parentRouteName: 'gateway-devices',
    parentLabel: 'Thiết bị',
  },
  'device-groups': {
    groupLabel: 'Quản lý',
    parentRouteName: 'gateway-devices',
    parentLabel: 'Nhóm thiết bị',
  },
  devices: {
    groupLabel: 'Quản lý',
    parentRouteName: 'devices',
    parentLabel: 'Thiết bị',
  },
  rtus: {
    groupLabel: 'Quản lý',
    parentRouteName: 'rtus',
    parentLabel: 'Bộ thu thập RTU',
  },
  schedules: {
    groupLabel: 'Quản lý',
    parentRouteName: 'schedules',
    parentLabel: 'Lịch phát dữ liệu',
  },
  schedule: {
    groupLabel: 'Quản lý',
    parentRouteName: 'schedules',
    parentLabel: 'Lịch phát dữ liệu',
  },
  alarms: {
    groupLabel: 'Cảnh báo',
    parentRouteName: 'alarms',
    parentLabel: 'Sự kiện cảnh báo',
  },
  rules: {
    groupLabel: 'Cảnh báo',
    parentRouteName: 'rules',
    parentLabel: 'Quy tắc cảnh báo',
  },
  'alert-templates': {
    groupLabel: 'Cảnh báo',
    parentRouteName: 'alert-templates',
    parentLabel: 'Mẫu cảnh báo',
  },
  'alert-channels': {
    groupLabel: 'Cảnh báo',
    parentRouteName: 'alert-channels',
    parentLabel: 'Cấu hình Email & Zalo',
  },
  reports: {
    groupLabel: 'Quản lý',
    parentRouteName: 'reports',
    parentLabel: 'Báo cáo',
  },
  signals: {
    groupLabel: 'Quản lý',
    parentRouteName: 'signals',
    parentLabel: 'Tín hiệu đo lường',
  },
  'history-analytics': {
    groupLabel: 'Quản lý',
    parentRouteName: 'history-analytics',
    parentLabel: 'Đồ thị lịch sử',
  },
  users: {
    groupLabel: 'Quản trị',
    parentRouteName: 'users',
    parentLabel: 'Người dùng',
  },
  roles: {
    groupLabel: 'Quản trị',
    parentRouteName: 'roles',
    parentLabel: 'Vai trò & Phân quyền',
  },
  'audit-logs': {
    groupLabel: 'Quản trị',
    parentRouteName: 'audit-logs',
    parentLabel: 'Nhật ký thao tác',
  },
  'mqtt-broker-config': {
    groupLabel: 'Quản trị',
    parentRouteName: 'mqtt-broker-config',
    parentLabel: 'Cấu hình MQTT Broker',
  },
  'modbus-slaves': {
    groupLabel: 'Quản lý',
    parentRouteName: 'modbus-slaves',
    parentLabel: 'Cảm biến Modbus',
  },
  'account-settings': {
    parentLabel: 'Thông tin tài khoản',
  },
  'system-configs': {
    groupLabel: 'Quản trị',
    parentLabel: 'Cấu hình hệ thống',
  },
  forbidden: {
    parentLabel: 'Không có quyền truy cập',
  },
  'not-found': {
    parentLabel: 'Không tìm thấy trang',
  },
}

export type BreadcrumbTrailOptions = {
  /** Danh sách cây menu từ nav.config.ts */
  navItems: readonly NavItem[]
  /** Tên route đang mở, ví dụ 'projects-detail' */
  activeRouteName: string
  /** Bảng ánh xạ tiêu đề của các route con, lấy từ route.meta.title */
  routeTitles?: Record<string, string>
  /** Tham số route hiện tại (params), dùng để điền vào routeParams của link trung gian */
  routeParams?: Record<string, string | number>
  /** Chủ thể người dùng đang đăng nhập, dùng để kiểm tra quyền của mục cha */
  subject?: AuthSubject
  /** Bảng fallback cho các route ẩn khỏi nav */
  fallbackParents?: Record<string, FallbackParent>
  /** Nhãn tuỳ biến cho mục cuối cùng (ví dụ tên thực thể 'Cầu Rồng' thay vì 'Chi tiết') */
  customTerminalLabel?: string
}

/**
 * Kiểm tra xem chủ thể có quyền xem link này hay không.
 */
function isLinkAuthorized(link: NavLink, subject?: AuthSubject): boolean {
  if (!subject) return true
  if (link.permission && !can(subject, link.permission)) return false
  if (link.anyPermission && !canAny(subject, link.anyPermission)) return false
  return true
}

/**
 * Tìm kiếm trang con đầu tiên mà chủ thể có quyền xem trong một nhóm (First Authorized Child).
 */
function findFirstAccessibleChildRoute(
  items: readonly NavItem[],
  subject?: AuthSubject,
): string | undefined {
  for (const item of items) {
    if (item.kind === 'link') {
      if (isLinkAuthorized(item, subject)) {
        return item.routeName
      }
    } else {
      const nested = findFirstAccessibleChildRoute(item.children, subject)
      if (nested) return nested
    }
  }
  return undefined
}

type AncestorGroup = {
  label: string
  routeName?: string
}

/**
 * Tìm kiếm link khớp với activeRouteName trong cây NavItem, trả về chuỗi nhóm tổ tiên và NavLink.
 */
function findMatchingNavLink(
  items: readonly NavItem[],
  activeRouteName: string,
  ancestorGroups: AncestorGroup[] = [],
  subject?: AuthSubject,
): { link: NavLink; groups: AncestorGroup[] } | null {
  for (const item of items) {
    if (item.kind === 'link') {
      if (isRouteActiveForLink(item.routeName, activeRouteName)) {
        return { link: item, groups: ancestorGroups }
      }
    } else {
      const groupRoute = findFirstAccessibleChildRoute(item.children, subject)
      const found = findMatchingNavLink(
        item.children,
        activeRouteName,
        [...ancestorGroups, { label: item.label, routeName: groupRoute }],
        subject,
      )
      if (found) return found
    }
  }
  return null
}

/**
 * Suy đoán nhãn hành động cho route con khi không tìm thấy trong từ điển routeTitles.
 */
function inferSubRouteLabel(activeRouteName: string): string {
  if (activeRouteName.endsWith('-create')) return 'Thêm mới'
  if (activeRouteName.endsWith('-edit')) return 'Chỉnh sửa'
  if (activeRouteName.endsWith('-detail')) return 'Chi tiết'
  return activeRouteName
}

/**
 * Động cơ phân giải đường dẫn: Chiếu cây điều hướng sang chuỗi BreadcrumbItem chuẩn mực.
 */
export function resolveBreadcrumbTrail(options: BreadcrumbTrailOptions): BreadcrumbItem[] {
  const {
    navItems,
    activeRouteName,
    routeTitles,
    routeParams,
    subject,
    fallbackParents = DEFAULT_FALLBACK_PARENTS,
    customTerminalLabel,
  } = options

  // Trang chủ hoặc route rỗng: luôn trả về Tổng quan duy nhất (không link)
  if (!activeRouteName || activeRouteName === 'home') {
    return [{ label: customTerminalLabel ?? 'Tổng quan' }]
  }

  // 1. Tìm trong cây Navigation chính (SSOT)
  const matched = findMatchingNavLink(navItems, activeRouteName, [], subject)

  if (matched) {
    const { link, groups } = matched
    const trail: BreadcrumbItem[] = []

    // Thêm các nhóm tổ tiên (render dạng link có thể click nếu có routeName)
    for (const group of groups) {
      trail.push({
        label: group.label,
        routeName: group.routeName,
      })
    }

    // Trường hợp 1: Khớp chính xác route danh sách gốc (ví dụ 'projects')
    if (link.routeName === activeRouteName) {
      trail.push({ label: customTerminalLabel ?? link.label })
      return trail
    }

    // Trường hợp 2: Route con nghiệp vụ (ví dụ 'projects-create', 'projects-detail')
    // Mục cha được phép click nếu người dùng có quyền; nếu thiếu quyền thì tước routeName để tránh 403
    const authorized = isLinkAuthorized(link, subject)
    trail.push({
      label: link.label,
      routeName: authorized ? link.routeName : undefined,
      ...(routeParams && Object.keys(routeParams).length > 0 && authorized ? { routeParams } : {}),
    })

    // Mục lá con hiện tại
    const terminalLabel =
      customTerminalLabel ?? routeTitles?.[activeRouteName] ?? inferSubRouteLabel(activeRouteName)
    trail.push({ label: terminalLabel })

    return trail
  }

  // 2. Tra cứu trong bảng Fallback nếu không có trong menu
  for (const [fallbackKey, fallback] of Object.entries(fallbackParents)) {
    // Khớp chính xác route fallback (ví dụ 'modbus-slaves' hoặc 'account-settings')
    if (fallbackKey === activeRouteName) {
      const trail: BreadcrumbItem[] = []
      if (fallback.groupLabel) {
        const groupRoute = findFirstAccessibleChildRoute(
          navItems.filter((i) => i.kind === 'group' && i.label === fallback.groupLabel).flatMap((g) => (g.kind === 'group' ? g.children : [])),
          subject,
        )
        trail.push({ label: fallback.groupLabel, routeName: groupRoute })
      }
      trail.push({ label: fallback.parentLabel })
      return trail
    }

    // Khớp route con của fallback (ví dụ 'modbus-slaves-create')
    if (activeRouteName.startsWith(`${fallbackKey}-`)) {
      const trail: BreadcrumbItem[] = []
      if (fallback.groupLabel) {
        const groupRoute = findFirstAccessibleChildRoute(
          navItems.filter((i) => i.kind === 'group' && i.label === fallback.groupLabel).flatMap((g) => (g.kind === 'group' ? g.children : [])),
          subject,
        )
        trail.push({ label: fallback.groupLabel, routeName: groupRoute })
      }
      trail.push({
        label: fallback.parentLabel,
        routeName: fallback.parentRouteName ?? fallbackKey,
      })
      const terminalLabel =
        customTerminalLabel ?? routeTitles?.[activeRouteName] ?? inferSubRouteLabel(activeRouteName)
      trail.push({ label: terminalLabel })
      return trail
    }
  }

  // 3. Fallback cuối cùng: dùng routeTitles hoặc tên route
  const fallbackTitle = customTerminalLabel ?? routeTitles?.[activeRouteName] ?? activeRouteName
  return [{ label: fallbackTitle }]
}
