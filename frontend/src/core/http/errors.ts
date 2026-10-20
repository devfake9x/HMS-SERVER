/**
 * Phân loại lỗi HTTP. Base architecture L5.
 */

export abstract class HttpError extends Error {
  abstract readonly kind: string
  abstract readonly userMessage: string

  constructor(message: string) {
    super(message)
    this.name = new.target.name
  }
}

export class NetworkError extends HttpError {
  readonly kind = 'network' as const
  readonly userMessage = 'Không kết nối được tới server. Kiểm tra đường mạng.'

  constructor(cause?: unknown) {
    super('Network request failed')
    if (cause !== undefined) this.cause = cause
  }
}

export class AbortError extends HttpError {
  readonly kind = 'abort' as const
  readonly userMessage = ''

  constructor() {
    super('Request aborted')
  }
}

export class ApiError extends HttpError {
  readonly kind = 'api' as const

  constructor(
    readonly status: number,
    readonly body: ErrorBody,
    readonly url: string,
  ) {
    super(`HTTP ${status} ${url}`)
  }

  get userMessage(): string {
    if (this.body.kind === 'json' && this.body.message) return this.body.message
    return STATUS_MESSAGE[this.status] ?? `Server trả lỗi ${this.status}.`
  }
}

export class UnauthorizedError extends ApiError {
  override get userMessage(): string {
    if (this.body.kind === 'json' && this.body.message) return this.body.message
    return 'Phiên đăng nhập đã hết. Vui lòng đăng nhập lại.'
  }
}

export class ForbiddenError extends ApiError {
  override get userMessage(): string {
    if (this.body.kind === 'json' && this.body.message) return this.body.message
    return 'Bạn không có quyền thực hiện việc này.'
  }
}

export class SchemaError extends HttpError {
  readonly kind = 'schema' as const
  readonly userMessage =
    'Dữ liệu server trả về không đúng định dạng. Có thể backend đã thay đổi.'

  constructor(
    readonly url: string,
    readonly issues: readonly string[],
  ) {
    super(`Schema mismatch at ${url}: ${issues.join('; ')}`)
  }
}

const STATUS_MESSAGE: Record<number, string> = {
  400: 'Dữ liệu gửi lên không hợp lệ.',
  404: 'Không tìm thấy dữ liệu.',
  408: 'Server phản hồi quá lâu.',
  409: 'Dữ liệu đã bị thay đổi bởi người khác.',
  429: 'Bạn thao tác quá nhanh. Thử lại sau một phút.',
  503: 'Server đang từ chối tạm thời. Thử lại sau một phút.',
  500: 'Server gặp lỗi nội bộ.',
  502: 'Server không phản hồi.',
  504: 'Server phản hồi quá lâu.',
}

export type ErrorBody =
  | { kind: 'json'; message?: string; raw: unknown }
  | { kind: 'text'; text: string }
  | { kind: 'html' }
  | { kind: 'empty' }

export async function parseErrorBody(response: Response): Promise<ErrorBody> {
  let text: string
  try {
    text = await response.text()
  } catch {
    return { kind: 'empty' }
  }

  const trimmed = text.trim()
  if (trimmed === '') return { kind: 'empty' }

  const contentType = response.headers.get('content-type') ?? ''
  const looksJson = contentType.includes('json') || /^[[{]/.test(trimmed)

  if (looksJson) {
    try {
      const raw: unknown = JSON.parse(trimmed)
      return { kind: 'json', message: extractMessage(raw), raw }
    } catch {
      return { kind: 'text', text: truncate(trimmed) }
    }
  }

  if (contentType.includes('html') || /^<!doctype|^<html/i.test(trimmed)) {
    return { kind: 'html' }
  }

  return { kind: 'text', text: truncate(trimmed) }
}

function extractMessage(raw: unknown): string | undefined {
  if (typeof raw !== 'object' || raw === null) return undefined
  const o = raw as Record<string, unknown>
  if (typeof o.message === 'string' && o.message.trim() !== '') return o.message
  if (typeof o.title === 'string' && o.title.trim() !== '') return o.title
  if (typeof o.detail === 'string' && o.detail.trim() !== '') return o.detail
  if (typeof o.errors === 'object' && o.errors !== null) {
    const first = Object.values(o.errors as Record<string, unknown>)[0]
    if (Array.isArray(first) && typeof first[0] === 'string') return first[0]
  }
  return undefined
}

function truncate(s: string, max = 300): string {
  return s.length <= max ? s : `${s.slice(0, max)}…`
}

export function toApiError(status: number, body: ErrorBody, url: string): ApiError {
  if (status === 401) return new UnauthorizedError(status, body, url)
  if (status === 403) return new ForbiddenError(status, body, url)
  return new ApiError(status, body, url)
}

export function isRetryable(error: unknown): boolean {
  if (error instanceof NetworkError) return true
  if (error instanceof AbortError) return false
  if (error instanceof SchemaError) return false
  if (error instanceof ApiError) {
    if (error.status === 408 || error.status === 429) return true
    return error.status >= 500
  }
  return false
}
