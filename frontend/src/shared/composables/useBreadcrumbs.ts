import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'

export interface BreadcrumbItem {
  label: string
  to?: string
}

export function useBreadcrumbs() {
  const route = useRoute()
  const router = useRouter()

  const breadcrumbs = computed<BreadcrumbItem[]>(() => {
    const items: BreadcrumbItem[] = [
      { label: 'Trang chủ', to: '/monitoring/overview' }
    ]

    const path = route.path
    if (path.startsWith('/monitoring')) {
      items.push({ label: 'Giám sát & Vận hành', to: '/monitoring/overview' })
      if (path.includes('/dashboard')) items.push({ label: 'Bảng điều khiển Live' })
      else if (path.includes('/live-map')) items.push({ label: 'Bản đồ hạm đội RTU' })
      else items.push({ label: 'Tổng quan hệ thống' })
    } else if (path.startsWith('/devices')) {
      items.push({ label: 'Hạ tầng thiết bị', to: '/devices' })
      items.push({ label: 'Danh sách RTU', to: '/devices' })
      if (route.params.id) {
        items.push({ label: `Thiết bị ${route.params.id}` })
      }
    } else if (path.startsWith('/data-declaration')) {
      items.push({ label: 'Hạ tầng thiết bị', to: '/devices' })
      items.push({ label: 'Cấu trúc phân cấp' })
    } else if (path.startsWith('/alarms')) {
      items.push({ label: 'Cảnh báo & Sự kiện', to: '/alarms' })
      items.push({ label: 'Sự kiện cảnh báo' })
    } else if (path.startsWith('/audit-logs')) {
      items.push({ label: 'Cảnh báo & Sự kiện', to: '/alarms' })
      items.push({ label: 'Nhật ký thao tác' })
    } else if (path.startsWith('/admin/users')) {
      items.push({ label: 'Quản trị hệ thống', to: '/admin/users' })
      items.push({ label: 'Người dùng' })
    } else if (path.startsWith('/admin/roles')) {
      items.push({ label: 'Quản trị hệ thống', to: '/admin/users' })
      items.push({ label: 'Vai trò & Phân quyền' })
    } else if (path.startsWith('/admin/licenses')) {
      items.push({ label: 'Quản trị hệ thống', to: '/admin/users' })
      items.push({ label: 'Bản quyền & Giấy phép' })
    }

    return items
  })

  const canGoBack = computed(() => {
    return breadcrumbs.value.length > 2
  })

  function goBack() {
    if (window.history.length > 1) {
      router.back()
    } else {
      router.push('/monitoring/overview')
    }
  }

  return {
    breadcrumbs,
    canGoBack,
    goBack,
  }
}
