export interface NavLinkItem {
  kind: 'link'
  key: string
  label: string
  path: string
  icon: string
  badge?: () => number | string | null
  requiredPermission?: string
}

export interface NavGroupItem {
  kind: 'group'
  key: string
  label: string
  children: NavLinkItem[]
}

export type NavItem = NavGroupItem | NavLinkItem

export const navigationConfig: NavGroupItem[] = [
  {
    kind: 'group',
    key: 'monitoring',
    label: 'Giám Sát & Vận Hành',
    children: [
      {
        kind: 'link',
        key: 'overview',
        label: 'Tổng Quan Hệ Thống',
        path: '/monitoring/overview',
        icon: 'LayoutDashboard',
      },
      {
        kind: 'link',
        key: 'dashboard',
        label: 'Bảng Điều Khiển Live',
        path: '/monitoring/dashboard',
        icon: 'Gauge',
      },
      {
        kind: 'link',
        key: 'live-map',
        label: 'Bản Đồ Hạm Đội RTU',
        path: '/monitoring/live-map',
        icon: 'MapPin',
      },
    ],
  },
  {
    kind: 'group',
    key: 'infrastructure',
    label: 'Hạ Tầng Thiết Bị',
    children: [
      {
        kind: 'link',
        key: 'devices',
        label: 'Danh Sách RTU',
        path: '/devices',
        icon: 'Cpu',
      },
      {
        kind: 'link',
        key: 'hierarchy',
        label: 'Cấu Trúc Phân Cấp',
        path: '/data-declaration',
        icon: 'FolderTree',
      },
    ],
  },
  {
    kind: 'group',
    key: 'events',
    label: 'Cảnh Báo & Sự Kiện',
    children: [
      {
        kind: 'link',
        key: 'alarms',
        label: 'Sự Kiện Cảnh Báo',
        path: '/alarms',
        icon: 'BellRing',
      },
      {
        kind: 'link',
        key: 'audit-logs',
        label: 'Nhật Ký Thao Tác',
        path: '/audit-logs',
        icon: 'ClipboardList',
      },
    ],
  },
  {
    kind: 'group',
    key: 'administration',
    label: 'Quản Trị Hệ Thống',
    children: [
      {
        kind: 'link',
        key: 'users',
        label: 'Người Dùng',
        path: '/admin/users',
        icon: 'Users',
        requiredPermission: 'users.manage',
      },
      {
        kind: 'link',
        key: 'roles',
        label: 'Vai Trò & Phân Quyền',
        path: '/admin/roles-permissions',
        icon: 'ShieldAlert',
        requiredPermission: 'roles.manage',
      },
      {
        kind: 'link',
        key: 'licenses',
        label: 'Bản Quyền & Giấy Phép',
        path: '/admin/licenses',
        icon: 'KeyRound',
        requiredPermission: 'licenses.manage',
      },
    ],
  },
]
