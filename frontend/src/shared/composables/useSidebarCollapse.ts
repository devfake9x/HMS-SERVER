import { readonly, ref, type DeepReadonly, type Ref } from 'vue'

const STORAGE_KEY = 'hms_sidebar_collapsed'
const RESIZE_DELAY_MS = 200

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
    // Ignore storage quota or private mode errors
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
      // Ignore
    }
    resizeTimer = null
  }, RESIZE_DELAY_MS)
}

export function syncSidebarCollapseFromStorage(): void {
  isCollapsedInternal.value = readInitialState()
}

export function useSidebarCollapse(): {
  isCollapsed: DeepReadonly<Ref<boolean>>
  toggle: () => void
  setCollapsed: (val: boolean) => void
} {
  function setCollapsed(val: boolean): void {
    if (isCollapsedInternal.value === val) return
    isCollapsedInternal.value = val
    persistState(val)
    triggerReflowEvent()
  }

  function toggle(): void {
    setCollapsed(!isCollapsedInternal.value)
  }

  return {
    isCollapsed: readonly(isCollapsedInternal),
    toggle,
    setCollapsed,
  }
}
