import { readonly, ref, type DeepReadonly, type Ref } from 'vue'

const STORAGE_KEY = 'concrete_stress_sidebar_collapsed'
const RESIZE_DELAY_MS = 210

function readInitialState(): boolean {
  try {
    return typeof window !== 'undefined' && localStorage.getItem(STORAGE_KEY) === 'true'
  } catch {
    return false
  }
}

function persistState(val: boolean): void {
  try {
    if (typeof window !== 'undefined') {
      localStorage.setItem(STORAGE_KEY, String(val))
    }
  } catch {
    // Tránh crash khi duyệt web ở chế độ ẩn danh hoặc storage đầy
  }
}

const isCollapsedInternal = ref<boolean>(readInitialState())
let resizeTimer: ReturnType<typeof setTimeout> | null = null

function triggerReflowEvent(): void {
  if (typeof window === 'undefined') return

  if (resizeTimer !== null) {
    clearTimeout(resizeTimer)
  }

  resizeTimer = setTimeout(() => {
    try {
      window.dispatchEvent(new Event('resize'))
    } catch {
      // Bỏ qua lỗi trong môi trường test không có window.dispatchEvent
    }
    resizeTimer = null
  }, RESIZE_DELAY_MS)
}

/**
 * Đặt lại trạng thái từ localStorage (dùng trong test hoặc khi storage thay đổi bên ngoài).
 */
export function syncSidebarCollapseFromStorage(): void {
  isCollapsedInternal.value = readInitialState()
}

export function useSidebarCollapse(): {
  isCollapsed: DeepReadonly<Ref<boolean>>
  toggle: () => void
  setCollapsed: (val: boolean) => void
} {
  function toggle(): void {
    setCollapsed(!isCollapsedInternal.value)
  }

  function setCollapsed(val: boolean): void {
    isCollapsedInternal.value = val
    persistState(val)
    triggerReflowEvent()
  }

  return {
    isCollapsed: readonly(isCollapsedInternal),
    toggle,
    setCollapsed,
  }
}
