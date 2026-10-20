import type { RouteRecordRaw } from 'vue-router'

import type { PermissionCode } from '#/core/auth/permissions'

import type { RouteAccess } from './guard'
import type { NavItem, NavLink } from './nav'

/**
 * Đối chiếu **menu** với **bảng route**. Base architecture D-E, FR-012.
 *
 * ## Vì sao là hàm thuần, không phải script Node
 *
 * Bản kế hoạch dự tính một `scripts/check-nav.mjs` đọc `nav.config.ts` và
 * `routes.ts` bằng AST. Đã thử và bỏ, lý do đo được:
 *
 * - Node 24 **strip type được** và phân giải `#/` qua `package.json` `imports`,
 *   nhưng dừng ở import **không có đuôi**: `#/core/auth/permissions` cần thành
 *   `permissions.ts` mới resolve. Cả 70 file trong `src/` viết không đuôi vì
 *   Vite lo việc đó. Sửa hết chỉ để phục vụ một gate là cái giá sai.
 * - Đường AST thì phải tự phân giải `PERMISSION.USERS_READ` → `'users.read'`,
 *   tức là viết lại một phần trình biên dịch TypeScript. Một gate mà bản thân nó
 *   có thể sai thì không nói được gì về thứ nó kiểm.
 *
 * Nên: **hàm thuần ở đây, nhận dữ liệu đã phân giải**, và một test ở `app/`
 * import `navConfig` cùng `routes` THẬT rồi gọi hàm này. Không parse gì, không
 * đoán gì. Cùng lý do đã chuyển T012 từ script regex sang test.
 *
 * Hàm này không tự import `app/nav.config` hay `app/router/routes` — `shared/`
 * không được import `app/`. Dữ liệu đi vào qua tham số.
 */

/** Giới hạn số cấp menu. data-model §1. */
export const MAX_NAV_DEPTH = 3

export type NavAuditRule =
  /** Mục menu trỏ tới tên route không tồn tại → bấm vào không đi đâu. */
  | 'unknown-route'
  /** Menu sâu hơn `MAX_NAV_DEPTH`. */
  | 'too-deep'
  /** Menu đòi ít quyền hơn route → mục hiện ra rồi người dùng ăn 403. */
  | 'nav-weaker'
  /** Menu đòi nhiều quyền hơn route → người có quyền vào lại không thấy đường. */
  | 'nav-stronger'
  /** `anyPermission` hai bên lệch nhau. */
  | 'nav-any-mismatch'
  /** Mục menu trỏ tới route `guestOnly` → chỉ khách chưa đăng nhập vào được. */
  | 'nav-to-guest-route'

export type NavFinding = {
  rule: NavAuditRule
  /** Đường dẫn nhãn, để người đọc tìm được chỗ sửa trong `nav.config.ts`. */
  where: string
  detail: string
}

const LABEL_SEP = ' › '

/** `name` → `meta.access`. Đi cả `children` vì route lồng nhau là bình thường. */
function indexRoutes(routes: readonly RouteRecordRaw[]): Map<string, RouteAccess | undefined> {
  const map = new Map<string, RouteAccess | undefined>()

  const visit = (list: readonly RouteRecordRaw[]): void => {
    for (const record of list) {
      if (record.name != null) map.set(String(record.name), record.meta?.access)
      if (record.children) visit(record.children)
    }
  }

  visit(routes)
  return map
}

function sortedList(values: Iterable<PermissionCode>): string {
  return [...values].sort().join(', ') || '(không có)'
}

function sameSet(a: readonly PermissionCode[], b: readonly PermissionCode[]): boolean {
  if (a.length !== b.length) return false
  const other = new Set<string>(b)
  return a.every((v) => other.has(v))
}

/**
 * Đối chiếu quyền của một mục menu với `meta.access` của route nó trỏ tới.
 *
 * Ẩn menu **không phải** kiểm quyền — route vẫn tự gate bằng `meta.access`, và
 * người dùng vẫn gõ được URL. Việc của hàm này là bắt hai khai báo đó **lệch
 * nhau**, vì mỗi chiều lệch là một lỗi khác nhau và đều thật:
 *
 * - Menu lỏng hơn route: mục hiện ra, bấm vào ăn 403. Đây là chiều tệ hơn, và
 *   `ARCHITECTURE.concrete-stress §4.2` đã ghi đúng một ca như vậy: gate UI theo
 *   `configs.view` trong khi backend đòi `configs.manage`.
 * - Menu chặt hơn route: người có quyền dùng trang lại không thấy đường vào.
 */
function checkPermissions(link: NavLink, access: RouteAccess | undefined, where: string): NavFinding[] {
  const out: NavFinding[] = []

  const navAll: PermissionCode[] = link.permission ? [link.permission] : []
  const navAny: readonly PermissionCode[] = link.anyPermission ?? []
  const routeAll: readonly PermissionCode[] = access?.permissions ?? []
  const routeAny: readonly PermissionCode[] = access?.anyPermission ?? []

  const navAllSet = new Set<string>(navAll)
  const routeAllSet = new Set<string>(routeAll)

  for (const code of routeAll) {
    if (!navAllSet.has(code)) {
      out.push({
        rule: 'nav-weaker',
        where,
        detail: `route đòi '${code}' nhưng mục menu không đòi (menu: ${sortedList(navAll)}). Mục sẽ hiện ra rồi người dùng ăn 403. NavLink chỉ khai được MỘT quyền all-of; nếu route cần nhiều, đổi 'permission' thành mảng ở NavLink trước.`,
      })
    }
  }

  for (const code of navAll) {
    if (!routeAllSet.has(code)) {
      out.push({
        rule: 'nav-stronger',
        where,
        detail: `mục menu đòi '${code}' nhưng route không đòi (route: ${sortedList(routeAll)}). Người có quyền dùng trang sẽ không thấy đường vào — sửa một trong hai chỗ, đừng để lệch.`,
      })
    }
  }

  if (!sameSet(navAny, routeAny)) {
    out.push({
      rule: 'nav-any-mismatch',
      where,
      detail: `anyPermission lệch — menu: ${sortedList(navAny)}; route: ${sortedList(routeAny)}.`,
    })
  }

  if (access?.guestOnly) {
    out.push({
      rule: 'nav-to-guest-route',
      where,
      detail: `route '${link.routeName}' là guestOnly, chỉ khách chưa đăng nhập vào được. Mục menu này nằm trong khung đã đăng nhập nên bấm vào sẽ bị đẩy về trang chủ.`,
    })
  }

  return out
}

/**
 * Mọi điểm lệch giữa menu và bảng route. Mảng rỗng = khớp.
 *
 * Trả findings thay vì throw: một lần chạy phải kể hết mọi lỗi, không phải lỗi
 * đầu tiên. Sửa từng lỗi rồi chạy lại sáu lần là cách nhanh nhất để người ta
 * bỏ gate.
 */
export function auditNav(
  items: readonly NavItem[],
  routes: readonly RouteRecordRaw[],
): NavFinding[] {
  const routeAccess = indexRoutes(routes)
  const out: NavFinding[] = []

  const walk = (list: readonly NavItem[], ancestors: readonly string[], depth: number): void => {
    if (depth > MAX_NAV_DEPTH) {
      if (list.length > 0) {
        out.push({
          rule: 'too-deep',
          where: ancestors.join(LABEL_SEP),
          detail: `menu sâu ${depth} cấp, tối đa ${MAX_NAV_DEPTH} (data-model §1). Sâu hơn thì trên màn hình hẹp mục cuối bị thụt lề hết chỗ.`,
        })
      }
      // Dừng tại đây: đi sâu tiếp sẽ sinh một báo cho mỗi cấp của cùng một lỗi.
      return
    }

    for (const item of list) {
      const path = [...ancestors, item.label]

      if (item.kind === 'group') {
        walk(item.children, path, depth + 1)
        continue
      }

      const where = path.join(LABEL_SEP)

      if (!routeAccess.has(item.routeName)) {
        out.push({
          rule: 'unknown-route',
          where,
          detail: `trỏ tới route '${item.routeName}' không có trong bảng route. 'routeName' là chuỗi, nên gõ sai là một mục bấm vào không đi đâu và không có gì báo.`,
        })
        continue
      }

      out.push(...checkPermissions(item, routeAccess.get(item.routeName), where))
    }
  }

  walk(items, [], 1)
  return out
}

/**
 * Số cấp thật của cây.
 *
 * Không phải để gate — `auditNav` tự đếm khi đi cây vì nó cần cả đường dẫn nhãn
 * cho thông báo. Hàm này để **khẳng định chiều ngược lại**: menu thật có đúng 3
 * cấp. Không có nó thì `NavTree` đệ quy có thể chưa bao giờ chạy quá 2 cấp mà
 * mọi test vẫn xanh.
 */
export function navDepth(items: readonly NavItem[]): number {
  let max = 0
  for (const item of items) {
    const depth = item.kind === 'group' ? 1 + navDepth(item.children) : 1
    if (depth > max) max = depth
  }
  return max
}

/** In findings ra dạng đọc được. Dùng trong thông báo lỗi của test. */
export function formatFindings(findings: readonly NavFinding[]): string {
  if (findings.length === 0) return '(không có)'
  return findings.map((f) => `[${f.rule}] ${f.where}\n    ${f.detail}`).join('\n')
}
