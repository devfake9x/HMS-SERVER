import { ref, readonly, type Ref } from 'vue'

export type ThemeMode = 'dark' | 'light'

const STORAGE_KEY = 'hms-theme'

function getInitialTheme(): ThemeMode {
  try {
    if (typeof window !== 'undefined') {
      const saved = localStorage.getItem(STORAGE_KEY)
      if (saved === 'light' || saved === 'dark') return saved
    }
  } catch (_) {}
  return 'dark'
}

const currentTheme = ref<ThemeMode>(getInitialTheme())

function applyTheme(theme: ThemeMode) {
  if (typeof document === 'undefined') return
  document.documentElement.setAttribute('data-theme', theme)
  if (theme === 'dark') {
    document.documentElement.classList.add('dark')
  } else {
    document.documentElement.classList.remove('dark')
  }
}

// Initial application
if (typeof window !== 'undefined') {
  applyTheme(currentTheme.value)
}

export function useTheme(): {
  theme: Readonly<Ref<ThemeMode>>
  setTheme: (theme: ThemeMode) => void
  toggleTheme: () => void
} {
  function setTheme(theme: ThemeMode) {
    currentTheme.value = theme
    try {
      localStorage.setItem(STORAGE_KEY, theme)
    } catch (_) {}
    applyTheme(theme)
  }

  function toggleTheme() {
    setTheme(currentTheme.value === 'dark' ? 'light' : 'dark')
  }

  return {
    theme: readonly(currentTheme),
    setTheme,
    toggleTheme,
  }
}
