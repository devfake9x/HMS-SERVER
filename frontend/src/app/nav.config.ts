import { computed, type ComputedRef } from 'vue'
import type { NavItem } from '#/shared/navigation/nav'

export const navItems: NavItem[] = [
  {
    kind: 'group',
    label: 'Giám sát & Vận hành',
    collapsible: false,
    children: [
      {
        kind: 'link',
        label: 'Tổng quan hệ thống',
        routeName: 'monitoring-overview',
        icon: 'home',
      },
      {
        kind: 'link',
        label: 'Bảng điều khiển Live',
        routeName: 'monitoring-dashboard',
        icon: 'line-chart',
      },
      {
        kind: 'link',
        label: 'Bản đồ hạm đội RTU',
        routeName: 'monitoring-live-map',
        icon: 'map-pin',
      },
    ],
  },
  {
    kind: 'group',
    label: 'Hạ tầng thiết bị',
    collapsible: false,
    children: [
      {
        kind: 'link',
        label: 'Danh sách thiết bị RTU',
        routeName: 'devices-list',
        icon: 'cpu',
      },
      {
        kind: 'link',
        label: 'Cấu trúc phân cấp',
        routeName: 'data-declaration',
        icon: 'folder',
      },
    ],
  },
  {
    kind: 'group',
    label: 'Cảnh báo & Sự kiện',
    collapsible: false,
    children: [
      {
        kind: 'link',
        label: 'Sự kiện cảnh báo',
        routeName: 'alarms',
        icon: 'bell',
      },
      {
        kind: 'link',
        label: 'Nhật ký thao tác',
        routeName: 'audit-logs',
        icon: 'file-text',
      },
    ],
  },
  {
    kind: 'group',
    label: 'Quản trị hệ thống',
    collapsible: false,
    children: [
      {
        kind: 'link',
        label: 'Người dùng',
        routeName: 'admin-users',
        icon: 'users',
      },
      {
        kind: 'link',
        label: 'Vai trò & Phân quyền',
        routeName: 'admin-roles',
        icon: 'lock',
      },
      {
        kind: 'link',
        label: 'Bản quyền & Giấy phép',
        routeName: 'admin-licenses',
        icon: 'key',
      },
    ],
  },
]

export function useNavConfig(): ComputedRef<NavItem[]> {
  return computed(() => navItems)
}
