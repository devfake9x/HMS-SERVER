// @vitest-environment jsdom
import { describe, it, expect, beforeEach } from 'vitest'
import { useSidebarCollapse, syncSidebarCollapseFromStorage } from './useSidebarCollapse'

describe('useSidebarCollapse', () => {
  beforeEach(() => {
    localStorage.clear()
    syncSidebarCollapseFromStorage()
  })

  it('initializes to false when nothing is in localStorage', () => {
    const { isCollapsed } = useSidebarCollapse()
    expect(isCollapsed.value).toBe(false)
  })

  it('toggles collapse state and updates localStorage', () => {
    const { isCollapsed, toggle } = useSidebarCollapse()
    expect(isCollapsed.value).toBe(false)
    toggle()
    expect(isCollapsed.value).toBe(true)
    expect(localStorage.getItem('hms_sidebar_collapsed')).toBe('true')
    toggle()
    expect(isCollapsed.value).toBe(false)
    expect(localStorage.getItem('hms_sidebar_collapsed')).toBe('false')
  })

  it('sets explicit collapse state', () => {
    const { isCollapsed, setCollapsed } = useSidebarCollapse()
    setCollapsed(true)
    expect(isCollapsed.value).toBe(true)
    expect(localStorage.getItem('hms_sidebar_collapsed')).toBe('true')
  })
})
