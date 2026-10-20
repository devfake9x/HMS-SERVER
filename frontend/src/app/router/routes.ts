import type { RouteRecordRaw } from 'vue-router'

export const routes: RouteRecordRaw[] = [
  {
    path: '/login',
    name: 'login',
    component: () => import('@/features/auth/views/LoginView.vue'),
    meta: { public: true, title: 'Đăng nhập hệ thống' },
  },
  {
    path: '/',
    redirect: '/monitoring/overview',
  },
  {
    path: '/monitoring/overview',
    name: 'monitoring-overview',
    component: () => import('@/features/monitoring/views/OverviewView.vue'),
    meta: { title: 'Tổng quan hệ thống' },
  },
  {
    path: '/monitoring/dashboard',
    name: 'monitoring-dashboard',
    component: () => import('@/app/views/PlaceholderView.vue'),
    props: { title: 'Bảng điều khiển Live SCADA', description: 'Quan trắc telemetry trực tiếp từ các trạm RTU' },
    meta: { title: 'Bảng điều khiển Live' },
  },
  {
    path: '/monitoring/live-map',
    name: 'monitoring-live-map',
    component: () => import('@/app/views/PlaceholderView.vue'),
    props: { title: 'Bản đồ hạm đội RTU', description: 'Vị trí địa lý và cường độ sóng 4G/LTE của các trạm' },
    meta: { title: 'Bản đồ hạm đội' },
  },
  {
    path: '/devices',
    name: 'devices-list',
    component: () => import('@/features/devices/views/DeviceListView.vue'),
    meta: { title: 'Danh sách thiết bị RTU' },
  },
  {
    path: '/devices/:id/:tab?',
    name: 'device-detail',
    component: () => import('@/features/devices/views/DeviceDetailView.vue'),
    meta: { title: 'Chi tiết thiết bị RTU' },
  },
  {
    path: '/data-declaration',
    name: 'data-declaration',
    component: () => import('@/features/data-declaration/views/HierarchyView.vue'),
    meta: { title: 'Cấu trúc phân cấp hạ tầng' },
  },
  {
    path: '/alarms',
    name: 'alarms',
    component: () => import('@/app/views/PlaceholderView.vue'),
    props: { title: 'Sự kiện cảnh báo', description: 'Nhật ký các sự cố vượt ngưỡng và mất kết nối' },
    meta: { title: 'Sự kiện cảnh báo' },
  },
  {
    path: '/audit-logs',
    name: 'audit-logs',
    component: () => import('@/app/views/PlaceholderView.vue'),
    props: { title: 'Nhật ký thao tác', description: 'Lịch sử phát lệnh điều khiển C2 và cấp phát khóa' },
    meta: { title: 'Nhật ký thao tác' },
  },
  {
    path: '/admin/users',
    name: 'admin-users',
    component: () => import('@/features/security/views/UserManagementView.vue'),
    meta: { title: 'Quản trị người dùng', requiredPermission: 'users.manage' },
  },
  {
    path: '/admin/roles-permissions',
    name: 'admin-roles',
    component: () => import('@/features/security/views/RolePermissionView.vue'),
    meta: { title: 'Vai trò & Phân quyền', requiredPermission: 'roles.manage' },
  },
  {
    path: '/admin/licenses',
    name: 'admin-licenses',
    component: () => import('@/app/views/PlaceholderView.vue'),
    props: { title: 'Bản quyền & Giấy phép KMS', description: 'Quản lý khóa chữ ký và thời hạn bản quyền thiết bị' },
    meta: { title: 'Bản quyền & Giấy phép', requiredPermission: 'licenses.manage' },
  },
  {
    path: '/:pathMatch(.*)*',
    name: 'not-found',
    component: () => import('@/app/views/NotFoundView.vue'),
    meta: { title: 'Không tìm thấy trang' },
  },
]
