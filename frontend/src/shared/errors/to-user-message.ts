import { AbortError, HttpError } from '#/core/http/errors'

/**
 * Dịch một `unknown` bắt được thành thứ hiện được cho người dùng.
 * Base architecture L5, §4.2.
 *
 * Vì sao cần tầng này khi `HttpError` đã có `userMessage`:
 * `catch (e)` cho ra `unknown`, không phải `HttpError`. Thứ rơi vào đó gồm cả
 * `TypeError` do lỗi code, lỗi của thư viện thứ ba, và cả `throw 'chuỗi'`.
 * Không có tầng dịch thì UI hoặc phải `e.message` — hiện chữ tiếng Anh kèm
 * thuật ngữ nội bộ cho người vận hành — hoặc phải nói "Có lỗi xảy ra" cho mọi
 * thứ, kể cả những lỗi đã biết chính xác nguyên nhân.
 *
 * Hai thứ hàm này bảo đảm:
 *   1. **Không bao giờ trả chuỗi rỗng** cho lỗi cần hiện. Toast rỗng tệ hơn
 *      không có toast: người dùng thấy có gì đó nháy lên mà không đọc được.
 *   2. **Không bao giờ để lọt message thô** ra `message`. Chi tiết kỹ thuật đi
 *      vào `detail`, và `detail` chỉ có ở môi trường dev.
 */

export type UserFacingError = {
  /** Câu tiếng Việt hiện cho người dùng. Luôn khác rỗng khi `silent` là false. */
  message: string
  /** Chi tiết kỹ thuật để chẩn đoán. CHỈ có ở dev. Không hiện ở production. */
  detail?: string
  /** Đúng nghĩa là đừng hiện gì cả. Xem `AbortError`. */
  silent: boolean
}

const FALLBACK = 'Đã xảy ra lỗi không xác định. Thử lại, hoặc tải lại trang.'

export function toUserMessage(error: unknown): UserFacingError {
  // Hủy request có chủ đích: đổi trang, đổi filter, component unmount. Đây là
  // hoạt động BÌNH THƯỜNG. Hiện toast cho nó là báo động giả, và bản cũ làm
  // đúng như vậy — đổi filter nhanh vài lần là ra một chuỗi toast đỏ.
  if (error instanceof AbortError) {
    return { message: '', silent: true }
  }

  if (error instanceof HttpError) {
    // `userMessage` rỗng cũng là ý "đừng hiện" — tôn trọng nó.
    if (error.userMessage === '') return { message: '', silent: true }
    return {
      message: error.userMessage,
      detail: devDetail(error.message),
      silent: false,
    }
  }

  // Lỗi validate của Zod dùng ngoài tầng HTTP, ví dụ validate form.
  if (isZodLike(error)) {
    return {
      message: 'Dữ liệu không hợp lệ. Kiểm tra lại các trường đã nhập.',
      detail: devDetail(zodSummary(error)),
      silent: false,
    }
  }

  if (error instanceof Error) {
    return {
      message: FALLBACK,
      // Message của Error là chữ tiếng Anh do lập trình viên hoặc thư viện
      // viết. Nó thuộc `detail`, không bao giờ thuộc `message`.
      detail: devDetail(`${error.name}: ${error.message}`),
      silent: false,
    }
  }

  // `throw 'chuỗi'`, `throw { code: 1 }`, `throw undefined` — đều có thật.
  return {
    message: FALLBACK,
    detail: devDetail(safeStringify(error)),
    silent: false,
  }
}

/** Chỉ trả detail ở dev. Ở production, chi tiết kỹ thuật đi vào log, không lên UI. */
function devDetail(text: string): string | undefined {
  if (!import.meta.env.DEV) return undefined
  const trimmed = text.trim()
  return trimmed === '' ? undefined : trimmed
}

type ZodLike = { issues: { path?: unknown[]; message?: string }[] }

function isZodLike(error: unknown): error is ZodLike {
  return (
    typeof error === 'object' &&
    error !== null &&
    Array.isArray((error as { issues?: unknown }).issues)
  )
}

function zodSummary(error: ZodLike): string {
  return error.issues
    .slice(0, 5)
    .map((i) => {
      const path = Array.isArray(i.path) && i.path.length > 0 ? i.path.join('.') : '(root)'
      return `${path}: ${i.message ?? 'không hợp lệ'}`
    })
    .join('; ')
}

function safeStringify(value: unknown): string {
  if (value === undefined) return 'undefined'
  if (value === null) return 'null'
  if (typeof value === 'string') return value
  try {
    return JSON.stringify(value) ?? String(value)
  } catch {
    // Object có vòng lặp tham chiếu, hoặc getter throw.
    return Object.prototype.toString.call(value)
  }
}
