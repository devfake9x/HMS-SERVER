import { computed, readonly, ref, type ComputedRef, type Ref } from 'vue'

import { toUserMessage } from '#/shared/errors/to-user-message'

/**
 * Hàng đợi thông báo. Base architecture L5.
 *
 * Năm vấn đề file này giải, cả năm đều là lỗi đã quan sát ở bản cũ:
 *
 * 1. **Toast trùng dồn đống.** Một request lỗi lặp mỗi 5 giây tạo 20 toast
 *    giống nhau che kín màn hình. Ở đây message giống nhau được **gộp và đếm**.
 *
 * 2. **Toast rỗng.** `alert(err.message)` với `message` rỗng cho ra một hộp
 *    trống nháy lên. Ở đây message rỗng bị **bỏ hẳn** — xem `push()`.
 *
 * 3. **Lỗi bị tắt quá nhanh.** Success 4 giây là đủ, nhưng lỗi biến mất sau 3
 *    giây thì người dùng chỉ thấy màu đỏ mà không đọc được. Timeout **theo mức
 *    độ**, và lỗi luôn dài nhất.
 *
 * 4. **Đọc dở bị tắt.** Đang đưa chuột vào đọc thì toast tắt. Ở đây hover hoặc
 *    focus **dừng đồng hồ**, rời ra thì chạy tiếp phần còn lại.
 *
 * 5. **Chồng vô hạn.** Không giới hạn thì 50 toast đẩy nhau ra khỏi màn hình.
 *    Giới hạn `MAX_VISIBLE`, quá thì bỏ **cái cũ nhất**.
 *
 * State ở module scope là **cố tình**: một app có đúng một hàng đợi thông báo,
 * và mọi component phải thấy cùng hàng đợi đó. `__resetForTest()` để test không
 * rò state — cùng cách `core/http/token-store.ts` làm.
 */

export type NotifyLevel = 'success' | 'info' | 'warning' | 'error'

export type Notification = {
  readonly id: string
  readonly level: NotifyLevel
  readonly message: string
  /** Chi tiết kỹ thuật. Chỉ có ở dev — xem `shared/errors/to-user-message.ts`. */
  readonly detail?: string
  /** Số lần message này xuất hiện. `> 1` thì UI hiện huy hiệu đếm. */
  readonly count: number
  /** 0 nghĩa là không tự tắt. */
  readonly timeoutMs: number
  readonly createdAt: number
  /** Khoá gộp trùng. Nội bộ, nhưng để readonly ra ngoài cho test kiểm được. */
  readonly key: string
  /** Cho phép đóng nhanh khi nhấp thẻ. Mặc định true. */
  readonly closeOnClick: boolean
}

export type NotifyOptions = {
  detail?: string
  /** Ghi đè thời gian tự tắt, ms. Truyền 0 để toast dính lại tới khi bấm tắt. */
  timeoutMs?: number
  /** Ghi đè khoá gộp. Mặc định là `level|message`. */
  key?: string
  /** Cho phép đóng nhanh khi nhấp thẻ. Mặc định true. */
  closeOnClick?: boolean
}

/**
 * Timeout mặc định theo mức độ, ms.
 *
 * Lỗi được 10 giây chứ không phải vô hạn: một lỗi mạng chớp nhoáng đã tự khỏi
 * mà để toast đỏ dính lại mãi thì người dùng mất niềm tin vào thông báo. Việc
 * cần dính lại thì truyền `timeoutMs: 0` một cách tường minh.
 */
export const DEFAULT_TIMEOUT_MS: Readonly<Record<NotifyLevel, number>> = {
  success: 5_000,
  info: 5_000,
  warning: 8_000,
  error: 10_000,
}

export const MAX_VISIBLE = 5

// ── State ────────────────────────────────────────────────────────────────────

const items = ref<Notification[]>([])

type TimerRecord = {
  timer: ReturnType<typeof setTimeout> | null
  /** epoch ms dự kiến tắt. Dùng để tính phần còn lại khi tạm dừng. */
  expiresAt: number
  remaining: number
}

const timers = new Map<string, TimerRecord>()
const isPaused = ref(false)
let seq = 0

// ── Đồng hồ ──────────────────────────────────────────────────────────────────

/**
 * Đặt đồng hồ tắt cho một thông báo.
 *
 * `paused` được xử lý **ở đây**, không ở chỗ gọi. Bản trước để chỗ gọi tự kiểm
 * `if (!paused) schedule(...)`, và hậu quả là thông báo đến TRONG lúc đang tạm
 * dừng không có `TimerRecord` nào — nên `resumeAll()` không thấy nó, và toast
 * dính lại vĩnh viễn. Chỉ cần hover vào vùng thông báo đúng lúc một lỗi bay về
 * là gặp. Một nơi biết về `paused` thì không có nửa nào để quên.
 */
function schedule(id: string, ms: number): void {
  if (ms <= 0) return

  if (isPaused.value) {
    // Chưa chạy đồng hồ, nhưng PHẢI ghi lại để `resumeAll()` biết có nó.
    timers.set(id, { timer: null, expiresAt: 0, remaining: ms })
    return
  }

  timers.set(id, {
    timer: setTimeout(() => dismiss(id), ms),
    expiresAt: Date.now() + ms,
    remaining: ms,
  })
}

function unschedule(id: string): void {
  const record = timers.get(id)
  if (record?.timer) clearTimeout(record.timer)
  timers.delete(id)
}

// ── Ghi ──────────────────────────────────────────────────────────────────────

function push(level: NotifyLevel, message: string, options: NotifyOptions = {}): string | null {
  const text = message.trim()
  // Toast rỗng tệ hơn không có toast: người dùng thấy có gì nháy lên mà không
  // đọc được, và mất niềm tin vào mọi thông báo sau đó.
  if (text === '') return null

  const key = options.key ?? `${level}|${text}`
  const timeoutMs = options.timeoutMs ?? DEFAULT_TIMEOUT_MS[level]

  const existingIndex = items.value.findIndex((n) => n.key === key)
  if (existingIndex !== -1) {
    const existing = items.value[existingIndex]
    // Gộp: tăng đếm, đặt lại đồng hồ, GIỮ NGUYÊN vị trí. Đẩy nó lên đầu sẽ làm
    // danh sách nhảy trong lúc người dùng đang đọc.
    const merged: Notification = {
      ...existing,
      count: existing.count + 1,
      detail: options.detail ?? existing.detail,
      timeoutMs,
      closeOnClick: options.closeOnClick ?? existing.closeOnClick,
    }
    items.value.splice(existingIndex, 1, merged)
    unschedule(existing.id)
    schedule(existing.id, timeoutMs)
    return existing.id
  }

  const id = `n${++seq}`
  const notification: Notification = {
    id,
    level,
    message: text,
    detail: options.detail,
    count: 1,
    timeoutMs,
    createdAt: Date.now(),
    key,
    closeOnClick: options.closeOnClick ?? true,
  }

  items.value = [...items.value, notification]

  // Quá giới hạn thì bỏ CÁI CŨ NHẤT: cái mới nhất là cái liên quan tới hành
  // động người dùng vừa làm, nên nó có giá trị hơn.
  while (items.value.length > MAX_VISIBLE) {
    const oldest = items.value[0]
    unschedule(oldest.id)
    items.value = items.value.slice(1)
  }

  schedule(id, timeoutMs)
  return id
}

function dismiss(id: string): void {
  unschedule(id)
  items.value = items.value.filter((n) => n.id !== id)
}

function clear(): void {
  for (const id of [...timers.keys()]) unschedule(id)
  items.value = []
}

/** Dừng mọi đồng hồ. Gọi khi con trỏ hoặc focus vào vùng thông báo. */
function pauseAll(): void {
  if (isPaused.value) return
  isPaused.value = true
  const now = Date.now()
  for (const record of timers.values()) {
    if (record.timer === null) continue
    clearTimeout(record.timer)
    record.timer = null
    record.remaining = Math.max(0, record.expiresAt - now)
  }
}

/** Chạy tiếp PHẦN CÒN LẠI, không phải bắt đầu lại từ đầu. */
function resumeAll(): void {
  if (!isPaused.value) return
  isPaused.value = false
  const now = Date.now()
  // Chụp danh sách id trước: `dismiss()` bên trong vòng lặp sẽ sửa `timers`.
  for (const id of [...timers.keys()]) {
    const record = timers.get(id)
    if (!record || record.timer !== null) continue
    if (record.remaining <= 0) {
      dismiss(id)
      continue
    }
    record.expiresAt = now + record.remaining
    record.timer = setTimeout(() => dismiss(id), record.remaining)
  }
}

// ── API công khai ────────────────────────────────────────────────────────────

export const notify = {
  success: (message: string, options?: NotifyOptions) => push('success', message, options),
  info: (message: string, options?: NotifyOptions) => push('info', message, options),
  warning: (message: string, options?: NotifyOptions) => push('warning', message, options),
  error: (message: string, options?: NotifyOptions) => push('error', message, options),

  /**
   * Hiện một `unknown` bắt được từ `catch`. Đây là đường dùng chính.
   *
   * Trả `null` khi lỗi không cần hiện — `AbortError` do đổi trang chẳng hạn.
   * Nhờ vậy chỗ gọi không phải tự phân loại: `catch (e) { notify.fromError(e) }`
   * là đủ đúng, kể cả với lỗi hủy request.
   */
  fromError: (error: unknown, options?: NotifyOptions): string | null => {
    const user = toUserMessage(error)
    if (user.silent) return null
    return push('error', user.message, { detail: user.detail, ...options })
  },

  dismiss,
  clear,
  pauseAll,
  resumeAll,
} as const

/** Danh sách chỉ đọc để render. */
export function useNotifications(): ComputedRef<readonly Notification[]> {
  return computed(() => items.value)
}

/** Trạng thái tạm dừng reactive toàn cục để UI và thanh tiến trình theo dõi. */
export function useNotifyPaused(): Readonly<Ref<boolean>> {
  return readonly(isPaused)
}

/** Chỉ dùng trong test — dọn state module-level giữa các test. */
export function __resetForTest(): void {
  clear()
  isPaused.value = false
  seq = 0
}
