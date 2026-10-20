import {
  computed,
  inject,
  isRef,
  provide,
  ref,
  unref,
  watch,
  type ComputedRef,
  type InjectionKey,
  type Ref,
} from 'vue'
import { routeLocationKey } from 'vue-router'

import { useSubject } from '#/shared/auth/subject'
import type { BreadcrumbItem } from '#/shared/ui/Breadcrumb.vue'
import { resolveBreadcrumbTrail } from './breadcrumbs'
import type { NavItem } from './nav'

export const NAV_ITEMS_KEY = Symbol('nav-items') as InjectionKey<
  ComputedRef<readonly NavItem[]> | Ref<readonly NavItem[]>
>

export const SHELL_HEADER_KEY = Symbol('shell-header-breadcrumb') as InjectionKey<boolean>

/** Trạng thái ghi đè singleton cho breadcrumbs toàn ứng dụng */
const terminalLabel = ref<string | null>(null)
const boundRoute = ref<string | null>(null)
const customTrail = ref<readonly BreadcrumbItem[] | null>(null)
let lastRouteName: string | null = null
let defaultNavItems: readonly NavItem[] = []

/**
 * Xóa sạch trạng thái ghi đè breadcrumbs (dành riêng cho kiểm thử tự động).
 */
export function __resetBreadcrumbsForTest(): void {
  terminalLabel.value = null
  boundRoute.value = null
  customTrail.value = null
  lastRouteName = null
  defaultNavItems = []
}

/**
 * Thiết lập danh sách menu mặc định khi không có provider (dành riêng cho kiểm thử hoặc fallback).
 */
export function setDefaultNavItems(items: readonly NavItem[]): void {
  defaultNavItems = items
}

/**
 * Cung cấp cây menu Navigation cho breadcrumb ở tầng root/shell.
 */
export function provideBreadcrumbsNav(
  items: ComputedRef<readonly NavItem[]> | Ref<readonly NavItem[]> | readonly NavItem[],
): void {
  const itemsRef = isRef(items) ? items : computed(() => items)
  provide(NAV_ITEMS_KEY, itemsRef)
}

export { provideBreadcrumbsNav as provideBreadcrumbs }

/**
 * Composable quản lý và đồng bộ Breadcrumbs với Navigation.
 */
export function useBreadcrumbs(
  fallbackItems?: ComputedRef<readonly NavItem[]> | Ref<readonly NavItem[]> | readonly NavItem[],
) {
  const subject = useSubject()
  const injectedNav = inject(NAV_ITEMS_KEY, null)
  const route = inject(routeLocationKey, null)

  // Tự động dọn dẹp ghi đè khi route thay đổi
  watch(
    () => route?.name,
    (newName, oldName) => {
      if (newName !== oldName) {
        terminalLabel.value = null
        boundRoute.value = null
        customTrail.value = null
      }
    },
  )

  function setTerminalLabel(label: string | null | undefined, boundRouteName?: string): void {
    terminalLabel.value = label ?? null
    boundRoute.value = boundRouteName ?? (String(route?.name ?? '') || null)
  }

  function setCustomTrail(items: readonly BreadcrumbItem[]): void {
    customTrail.value = items
  }

  function resetCustomTrail(): void {
    customTrail.value = null
  }

  function reset(): void {
    terminalLabel.value = null
    boundRoute.value = null
    customTrail.value = null
  }

  const breadcrumbs = computed<readonly BreadcrumbItem[]>(() => {
    const currentRouteName = String(route?.name ?? '')

    // Kiểm tra đồng bộ khi route thay đổi để reset ngay trong cùng render tick
    if (lastRouteName !== null && lastRouteName !== currentRouteName) {
      terminalLabel.value = null
      boundRoute.value = null
      customTrail.value = null
    }
    lastRouteName = currentRouteName

    // 1. Nếu có customTrail thì ưu tiên hàng đầu
    if (customTrail.value !== null) {
      return customTrail.value
    }

    // 2. Chống race condition: Chỉ áp dụng terminalLabel nếu khớp route bound
    let effectiveTerminalLabel: string | undefined = undefined
    if (terminalLabel.value !== null) {
      if (!boundRoute.value || boundRoute.value === currentRouteName) {
        effectiveTerminalLabel = terminalLabel.value
      }
    }

    const metaTitle = typeof route?.meta?.title === 'string' ? route.meta.title : undefined
    const routeTitles = metaTitle ? { [currentRouteName]: metaTitle } : undefined
    const fallback = fallbackItems ? unref(fallbackItems) : defaultNavItems
    const items = unref(injectedNav) ?? fallback
    return resolveBreadcrumbTrail({
      navItems: items,
      activeRouteName: currentRouteName,
      routeParams: route?.params as Record<string, string | number> | undefined,
      routeTitles,
      subject: subject?.value,
      customTerminalLabel: effectiveTerminalLabel,
    })
  })

  return {
    breadcrumbs,
    trail: breadcrumbs, // Alias thuận tiện
    setTerminalLabel,
    setCustomTrail,
    resetCustomTrail,
    reset,
  }
}

