import { canAll, canAny, type AuthSubject } from '#/core/auth/can'
import type { PermissionCode } from '#/core/auth/permissions'

/**
 * Quyết định điều hướng, viết dưới dạng **hàm thuần**. Base architecture §6.2.
 *
 * Vì sao tách khỏi `router.beforeEach`: logic trong một `beforeEach` cần dựng
 * cả router, cả app, cả store mới test được — nên trên thực tế nó **không được
 * test**. Và guard là chỗ một lỗi có nghĩa là người không quyền vào được trang.
 *
 * Ở đây `beforeEach` chỉ còn ba dòng: đọc state, gọi hàm này, thi hành kết quả.
 */

export type RouteAccess = {
  /**
   * Cho khách chưa đăng nhập. **Phải khai tường minh** — mặc định là cần đăng
   * nhập. Xem `resolveNavigation`.
   */
  public?: boolean
  /** Chỉ cho khách CHƯA đăng nhập: trang login, forgot-password. */
  guestOnly?: boolean
  /** Cần TẤT CẢ các quyền này. */
  permissions?: readonly PermissionCode[]
  /** Cần ÍT NHẤT một trong các quyền này. */
  anyPermission?: readonly PermissionCode[]
}

export type GuardContext = {
  hasSession: boolean
  subject: AuthSubject
  access: RouteAccess
  /** Path đầy đủ của route đích, để quay lại sau khi đăng nhập. */
  targetFullPath: string
  /** Đã tải xong thông tin user chưa. `false` = đang bootstrap. */
  subjectReady: boolean
}

export type GuardDecision =
  | { kind: 'allow' }
  | { kind: 'redirect'; name: string; query?: Record<string, string> }
  /** Đợi tải xong user rồi quyết định lại. Router phải giữ điều hướng lại. */
  | { kind: 'wait' }

export const ROUTE = {
  login: 'login',
  home: 'home',
  forbidden: 'forbidden',
} as const

/**
 * ## Bốn luật, mỗi luật sửa một lỗi thật
 *
 * 1. **Mặc định là CẦN đăng nhập.** Route phải khai `public: true` để mở. Bản cũ
 *    làm ngược — route nào không khai `meta` thì mở, và kết quả là 7/11 route
 *    không có gate nào. Quên khai `meta` phải là lỗi "không vào được", không
 *    phải "ai cũng vào được".
 *
 * 2. **Thiếu quyền thì đi trang 403, KHÔNG đi trang login.** Đẩy về login khi đã
 *    đăng nhập tạo vòng lặp: đăng nhập lại → vẫn thiếu quyền → lại về login.
 *    Người dùng nghĩ mật khẩu sai.
 *
 * 3. **Chưa biết quyền thì ĐỢI, không đoán.** Lúc bootstrap `subjectReady` là
 *    false. Cho qua thì UI nhấp nháy nội dung rồi bị đẩy ra; chặn thẳng thì
 *    F5 trên một trang sâu sẽ luôn ném về login dù có quyền.
 *
 * 4. **Đang đăng nhập thì không vào được trang login.** Không chặn thì bấm Back
 *    sau khi đăng nhập là về form login, và form đó submit sẽ tạo phiên thứ hai.
 */
export function resolveNavigation(ctx: GuardContext): GuardDecision {
  const { access, hasSession, subject, subjectReady, targetFullPath } = ctx

  // Luật 4 — xử lý trước, vì guestOnly không quan tâm quyền.
  if (access.guestOnly) {
    return hasSession ? { kind: 'redirect', name: ROUTE.home } : { kind: 'allow' }
  }

  if (access.public) return { kind: 'allow' }

  // Luật 1 — mặc định cần đăng nhập.
  if (!hasSession) {
    return {
      kind: 'redirect',
      name: ROUTE.login,
      // Quay lại đúng chỗ sau khi đăng nhập. Không có nó thì người dùng bị đưa
      // về trang chủ và phải tự tìm lại đường.
      query: { redirect: targetFullPath },
    }
  }

  // Luật 3 — chưa biết chủ thể thì ĐỢI, không đoán.
  // Khi đã có phiên đăng nhập, mọi route nội bộ (không phải public hay guestOnly)
  // đều cần nạp đầy đủ thông tin người dùng và quyền hạn trước khi cho phép vào,
  // đảm bảo AppShell render chính xác cây điều hướng và view không bị thiếu quyền.
  if (!subjectReady) return { kind: 'wait' }

  if (access.permissions?.length && !canAll(subject, access.permissions)) {
    // Luật 2.
    return { kind: 'redirect', name: ROUTE.forbidden }
  }

  if (access.anyPermission?.length && !canAny(subject, access.anyPermission)) {
    return { kind: 'redirect', name: ROUTE.forbidden }
  }

  return { kind: 'allow' }
}

/**
 * Đích quay lại sau khi đăng nhập, lấy từ query `redirect`.
 *
 * **Chỉ nhận path nội bộ.** Không lọc thì `?redirect=https://kẻ-xấu` biến trang
 * login thành một open redirect: link trông như của hệ thống, đăng nhập xong bị
 * đẩy sang site khác. Đây là lỗ hổng thật, không phải giả thuyết.
 */
export function safeRedirectTarget(raw: unknown, fallback = '/'): string {
  if (typeof raw !== 'string' || raw === '') return fallback

  // Phải bắt đầu bằng đúng MỘT dấu `/`. `//host` là URL tương đối protocol —
  // browser coi nó là tên miền ngoài.
  if (!raw.startsWith('/') || raw.startsWith('//')) return fallback

  // `\` bị một số browser chuẩn hoá thành `/`, nên `/\kẻ-xấu` cũng ra ngoài.
  if (raw.includes('\\')) return fallback

  // Chặn `javascript:` và tương tự lọt qua bằng cách viết lạ.
  if (/^\/[\s]*[a-z][a-z0-9+.-]*:/i.test(raw)) return fallback

  return raw
}
