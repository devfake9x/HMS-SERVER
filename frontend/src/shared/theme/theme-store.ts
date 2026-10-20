import { defineStore } from 'pinia'
import { computed, onScopeDispose, ref } from 'vue'

/**
 * Giao diện sáng / tối. data-model §4, FR-026…FR-033.
 *
 * ## Hai kiểu, không phải một — đây là chỗ dễ gộp sai nhất
 *
 * | | Lưu vào storage | Ghi lên DOM | Hiện trên switcher |
 * | --- | --- | --- | --- |
 * | `ThemePreference` | ✅ | ❌ | ✅ 3 lựa chọn |
 * | `ResolvedTheme` | ❌ | ✅ `data-theme` | ❌ |
 *
 * Gộp lại thì `'system'` bị giải thành `'dark'` **rồi lưu**, và lần sau người
 * dùng không còn ở chế độ Theo hệ thống nữa — họ chưa hề chọn thoát khỏi nó.
 * Đây là **bug im lặng**: không có thông báo nào, không có gì đỏ, chỉ là một
 * cài đặt tự thay đổi. `theme-store.test.ts` ghim đúng ca này.
 *
 * ## Có bản sao ở `index.html`, và đó là chủ đích
 *
 * Inline script trong `<head>` đặt `data-theme` **trước khi CSS áp**, vì SPA
 * không SSR thì để `main.ts` làm là đã có khung hình sai màu (research R-5).
 * Nó nhân bản đúng hai thứ: **khoá storage** và **luật giải `'system'`**.
 * Sửa một bên phải sửa bên kia — không có cách nào để máy bắt việc này, nên nó
 * được ghi ở cả hai chỗ trỏ vào nhau.
 *
 * Phân vai: inline script **chỉ đọc**, store **đọc và ghi**.
 */

export type ThemePreference = 'light' | 'dark' | 'system'
export type ResolvedTheme = 'light' | 'dark'

/** Phải khớp `index.html`. */
export const THEME_STORAGE_KEY = 'csm.theme'
const DARK_QUERY = '(prefers-color-scheme: dark)'

export const THEME_PREFERENCES: readonly ThemePreference[] = ['light', 'dark', 'system']

export function isThemePreference(value: unknown): value is ThemePreference {
  return value === 'light' || value === 'dark' || value === 'system'
}

/**
 * Lựa chọn đã lưu. Giá trị lạ → `'system'`.
 *
 * `localStorage` có thể **ném lỗi**, không chỉ trả `null`: Chrome chặn nó khi
 * cookie bị tắt. Không bắt thì store chết lúc khởi tạo và app trắng trơn.
 */
export function readStoredPreference(): ThemePreference {
  try {
    const raw = localStorage.getItem(THEME_STORAGE_KEY)
    return isThemePreference(raw) ? raw : 'system'
  } catch {
    // Không đọc được thì coi như chưa chọn gì.
    return 'system'
  }
}

/** Theme hệ điều hành đang dùng. */
export function systemTheme(): ResolvedTheme {
  try {
    return window.matchMedia(DARK_QUERY).matches ? 'dark' : 'light'
  } catch {
    // Rơi về `'dark'` — trùng khối `:root` của main.css và nhánh catch của
    // inline script, nên ba nơi cho cùng một kết quả khi mọi thứ hỏng.
    return 'dark'
  }
}

/** Hàm thuần: lựa chọn + theme hệ thống → theme thực tế áp. */
export function resolveTheme(
  preference: ThemePreference,
  system: ResolvedTheme,
): ResolvedTheme {
  return preference === 'system' ? system : preference
}

export const useThemeStore = defineStore('theme', () => {
  const preference = ref<ThemePreference>(readStoredPreference())
  const system = ref<ResolvedTheme>(systemTheme())

  const resolved = computed<ResolvedTheme>(() => resolveTheme(preference.value, system.value))

  function applyToDom(): void {
    document.documentElement.dataset.theme = resolved.value
    if (resolved.value === 'dark') {
      document.documentElement.classList.add('dark')
    } else {
      document.documentElement.classList.remove('dark')
    }
  }

  // ── Theo hệ điều hành ─────────────────────────────────────────────────────
  // Chỉ đổi `system`, KHÔNG đổi `preference`. Nếu người dùng đang chọn 'light'
  // thì OS đổi sang tối cũng không được kéo họ theo — `resolveTheme` lo việc đó.
  let media: MediaQueryList | null = null

  function onSystemChange(event: MediaQueryListEvent): void {
    system.value = event.matches ? 'dark' : 'light'
    applyToDom()
  }

  try {
    media = window.matchMedia(DARK_QUERY)
    media.addEventListener('change', onSystemChange)
  } catch {
    // Không có matchMedia thì mất tính năng theo-hệ-thống-động, nhưng chế độ
    // Sáng/Tối vẫn dùng được. Không ném lỗi ra ngoài.
    media = null
  }

  // ── Đồng bộ giữa các tab ──────────────────────────────────────────────────
  // Dùng `storage` event, KHÔNG dùng BroadcastChannel: theme không có race —
  // trạng thái là một giá trị duy nhất, đến sau thắng, và không có thao tác nào
  // cần gộp. `storage` event có sẵn và bắn đúng khi tab khác ghi vào cùng khoá.
  // (Token thì khác, nó CÓ race — xem `core/http/token-store.ts`.)
  function onStorage(event: StorageEvent): void {
    if (event.key !== THEME_STORAGE_KEY) return
    preference.value = isThemePreference(event.newValue) ? event.newValue : 'system'
    applyToDom()
  }

  window.addEventListener('storage', onStorage)

  /**
   * Gỡ listener khi store bị huỷ.
   *
   * `matchMedia` listener sống ngoài Vue nên không tự chết theo component. Trong
   * app thật store sống hết đời trang, nhưng trong **test** thì mỗi ca tạo một
   * store mới — không gỡ thì listener tích lại và ca sau nhận event của ca trước.
   * Đó là loại test đỏ ngẫu nhiên tốn nhiều giờ nhất để tìm.
   */
  onScopeDispose(() => {
    media?.removeEventListener('change', onSystemChange)
    window.removeEventListener('storage', onStorage)
  })

  /**
   * Đổi lựa chọn. **Lưu `preference`, không lưu `resolved`** — xem đầu file.
   */
  function setPreference(next: ThemePreference): void {
    preference.value = next
    try {
      localStorage.setItem(THEME_STORAGE_KEY, next)
    } catch {
      // Không lưu được thì lựa chọn chỉ sống trong tab này. Vẫn áp lên DOM —
      // thà đúng màu ngay bây giờ còn hơn không làm gì cả.
    }
    applyToDom()
  }

  // Đồng bộ lại với inline script. Bình thường không đổi gì; cần khi inline
  // script đã rơi vào nhánh catch mà store thì đọc được storage.
  applyToDom()

  return {
    /** Chỉ đọc. Đổi bằng `setPreference` để lựa chọn được lưu. */
    preference: computed(() => preference.value),
    resolved,
    setPreference,
  }
})
