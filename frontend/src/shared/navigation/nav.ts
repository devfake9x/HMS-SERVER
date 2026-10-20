import { can, canAny, type AuthSubject } from '#/core/auth/can'
import type { PermissionCode } from '#/core/auth/permissions'

/**
 * Menu là **dữ liệu**, không phải markup. Base architecture §6.
 *
 * Vì sao: menu khác nhau theo khách hàng và theo quyền. Viết menu bằng
 * `<router-link>` lồng `v-if` thì mỗi lần thêm một mục là sửa template, và điều
 * kiện hiển thị nằm rải rác trong JSX/template — không có chỗ nào đọc được "ai
 * thấy được gì".
 *
 * Ở đây: **file này định nghĩa hình dạng và luật lọc** (phần base, dùng cho mọi
 * dự án), còn **dữ liệu menu thật nằm ở `app/nav.config.ts`** (phần per-project).
 * Đó cũng là lý do nó ở `shared/` chứ không ở `app/`: luật lọc đi theo base, dữ
 * liệu thì không.
 */

export type NavBadgeLevel = 'critical' | 'warning' | 'info' | 'neutral' | 'success'

export type NavBadge = {
  readonly count: number
  readonly level?: NavBadgeLevel
  readonly pulse?: boolean
  readonly label?: string
}

export type NavBadgeResolver = () => NavBadge | null

export type NavLink = {
  kind: 'link'
  label: string
  /**
   * **Tên route**, không phải path. Đổi path không phải sửa menu, và tên route
   * được vue-router kiểm lúc điều hướng.
   */
  routeName: string
  /** Tên icon. `shared/layouts` map tên này sang component. */
  icon?: string
  /** Cần quyền này. Thiếu quyền thì mục **biến mất**, không phải bị disable. */
  permission?: PermissionCode
  /** Cần ÍT NHẤT một trong các quyền. Dùng cho mục gộp nhiều view. */
  anyPermission?: readonly PermissionCode[]
  /** Tham số truy vấn URL phục vụ lọc nhanh (VD: { connectionStatus: 'offline' }). */
  query?: Record<string, string>
  /** Hàm phản ứng giải quyết số lượng và cấp độ cảnh báo. */
  badge?: NavBadgeResolver
  /** Cấp độ cảnh báo tĩnh nếu không dùng resolver. */
  badgeLevel?: NavBadgeLevel
  /** Cờ đánh dấu nút tổ tiên là vỏ siêu dữ liệu chỉ đọc (Ancestor Shell Projection). */
  isShell?: boolean
}

export type NavGroup = {
  kind: 'group'
  label: string
  icon?: string
  children: readonly NavItem[]
  /**
   * Nhóm có thu gọn được hay không. **Mặc định `true`** — bỏ trống là hành vi cũ.
   *
   * `false` biến nhóm thành **nhãn tĩnh**: nhãn không bấm được, không chevron, và
   * các con luôn hiện. `[ĐO]` Đây là dạng Datacake dùng cho bốn nhóm cấp một
   * (`Dashboards`, `Devices`, `Workspace`, `Organization`) — chúng chỉ phân vùng thị
   * giác, không phải thứ để mở ra đóng vào. Họ vẫn giữ dạng thu gọn được cho mục
   * `All Devices`, nên hai dạng cùng tồn tại và đó là lý do đây là **cờ**, không phải
   * thay thế.
   *
   * ## Vì sao là một field, KHÔNG phải `kind: 'section'`
   *
   * Ba phương án đã cân. Thêm `kind` thứ ba đụng **năm** hàm trong file này
   * (`visibleNav`, `flattenLinks`, `activeGroupLabels`, `expandedKeysForRoute`,
   * `navKeyOf`) cộng **sáu** rule của `auditNav` và `MAX_NAV_DEPTH` — mỗi chỗ là một
   * nhánh mới phải nghiệm, và một nhánh bị quên sẽ làm mục menu biến mất hoặc
   * `auditNav` bỏ qua cả một nhánh cây. Đổi CSS cho nhóm cấp một thì rẻ hơn nhưng
   * nhãn **vẫn bấm được**, tức không đạt.
   *
   * Cờ này giữ **hình dạng cây không đổi**, nên không hàm nào trong file này phải
   * sửa: nhóm tĩnh vẫn là nhóm, vẫn bị bỏ khi rỗng, vẫn được `flattenLinks` đi vào,
   * và `auditNav` vẫn đếm nó là một cấp. Chỉ `NavTree.vue` đọc cờ để chọn cách vẽ.
   *
   * ## Cái nó KHÔNG bao phủ
   *
   * Nhóm tĩnh không thu gọn được, nên một nhóm tĩnh có hai mươi con làm sidebar dài
   * ra và người dùng không có cách nào rút lại. Đó là quyết định của chỗ khai dữ
   * liệu (`app/nav.config.ts`), không phải của component — và không có cổng nào bắt
   * được, vì "bao nhiêu con là quá nhiều" không phải một ngưỡng đo được.
   */
  collapsible?: boolean
  /** Cờ đánh dấu nhóm tổ tiên là vỏ siêu dữ liệu chỉ đọc (Ancestor Shell Projection). */
  isShell?: boolean
}

export type NavItem = NavLink | NavGroup

/**
 * Mục này có được hiện cho chủ thể không.
 *
 * Không khai điều kiện nào → hiện. Khai cả `permission` và `anyPermission` →
 * phải thoả **cả hai**. Cố tình: hai field khai cùng lúc là ý "bắt buộc có X, và
 * có một trong Y/Z", không phải "hoặc".
 */
function linkVisible(link: NavLink, subject: AuthSubject): boolean {
  if (link.permission && !can(subject, link.permission)) return false
  if (link.anyPermission && !canAny(subject, link.anyPermission)) return false
  return true
}

/**
 * Lọc menu theo quyền của chủ thể.
 *
 * Ba luật:
 *
 * 1. Link thiếu quyền thì **bị bỏ hẳn**, không hiện dạng disabled. Một mục xám
 *    mà bấm không được chỉ nói với người dùng rằng có thứ họ không được xem —
 *    thông tin đó vô dụng và gây khó chịu.
 *
 * 2. **Group rỗng bị bỏ.** Đây là lỗi hay gặp nhất: lọc từng link nhưng vẫn hiện
 *    tiêu đề group, để lại một mục bấm vào không ra gì. Group chỉ tồn tại khi
 *    còn ít nhất một con nhìn thấy được.
 *
 * 3. Đệ quy: group lồng group cũng phải rỗng-thì-bỏ, tính từ trong ra ngoài.
 *
 * `can()` fail closed khi `permissions === null` (chưa fetch xong), nên trong
 * lúc đang tải, menu sẽ rỗng chứ không hiện đầy đủ rồi co lại. Nhấp nháy một
 * menu đầy đủ rồi biến mất còn tệ hơn menu rỗng.
 */
export function visibleNav(items: readonly NavItem[], subject: AuthSubject): NavItem[] {
  const out: NavItem[] = []

  for (const item of items) {
    if (item.kind === 'link') {
      if (linkVisible(item, subject)) out.push(item)
      continue
    }

    // Lọc con TRƯỚC, rồi mới quyết định group có tồn tại hay không.
    const children = visibleNav(item.children, subject)
    if (children.length > 0) {
      out.push({ ...item, children })
    }
  }

  return out
}

/**
 * Ký tự nối khoá. `U+001F` (unit separator) — ký tự điều khiển, không xuất hiện
 * trong nhãn menu của người thật. Dùng `/` hay `>` thì một nhãn chứa ký tự đó sẽ
 * tạo ra hai group có cùng khoá.
 */
const NAV_KEY_SEP = '\u001f'

/**
 * Khoá nhận dạng một group: **đường dẫn** từ gốc, không phải nhãn.
 *
 * Vì sao không dùng nhãn: nhãn trùng nhau ở hai nhánh khác nhau là chuyện bình
 * thường ("Cấu hình" nằm dưới cả Quản trị và Giám sát). Nếu khoá là nhãn thì mở
 * một cái sẽ mở luôn cái kia, và lỗi này chỉ lộ ra khi cây có từ 3 cấp — tức là
 * đúng lúc không ai còn nhớ vì sao khoá lại là nhãn.
 */
export function navKeyOf(parentKey: string, label: string): string {
  return parentKey === '' ? label : `${parentKey}${NAV_KEY_SEP}${label}`
}

/**
 * Khoá của mọi group cần mở sẵn để thấy được route đang mở.
 *
 * Khác `activeGroupLabels` ở chỗ trả về **khoá đường dẫn tích luỹ**, đúng dạng
 * mà `NavTree` so sánh. `activeGroupLabels` trả nhãn, và nhãn không định danh
 * được một group — xem `navKeyOf`.
 */
export function expandedKeysForRoute(
  items: readonly NavItem[],
  activeRouteName: string,
): string[] {
  const out: string[] = []
  let key = ''
  for (const label of activeGroupLabels(items, activeRouteName)) {
    key = navKeyOf(key, label)
    out.push(key)
  }
  return out
}

/** Mọi link trong cây, đã phẳng. Dùng để kiểm tên route lúc boot. */
export function flattenLinks(items: readonly NavItem[]): NavLink[] {
  const out: NavLink[] = []
  for (const item of items) {
    if (item.kind === 'link') out.push(item)
    else out.push(...flattenLinks(item.children))
  }
  return out
}

/**
 * Tên route trong menu mà router không biết.
 *
 * `routeName` là chuỗi, nên gõ sai là một mục menu bấm vào không đi đâu — và
 * không có gì báo. Gọi hàm này lúc boot ở dev để lỗi đó lộ ra ngay.
 */
export function unknownRouteNames(
  items: readonly NavItem[],
  knownRouteNames: readonly string[],
): string[] {
  const known = new Set(knownRouteNames)
  return flattenLinks(items)
    .map((l) => l.routeName)
    .filter((name) => !known.has(name))
}

/**
 * Kiểm tra xem `activeRouteName` có thuộc về `linkRouteName` không.
 *
 * Khớp chính xác (ví dụ `organizations === organizations`), hoặc khớp tiền tố
 * của các route con nghiệp vụ (ví dụ `organizations-create`, `organizations-edit`, `organizations-detail`
 * đều kích hoạt mục `organizations` trên sidebar).
 */
export function isRouteActiveForLink(
  linkRouteName: string,
  activeRouteName: string,
  linkQuery?: Record<string, string>,
  activeQuery?: Record<string, unknown>,
): boolean {
  if (linkQuery && Object.keys(linkQuery).length > 0) {
    if (linkRouteName !== activeRouteName) return false
    if (!activeQuery) return false
    for (const [k, v] of Object.entries(linkQuery)) {
      if (String(activeQuery[k] ?? '') !== v) return false
    }
    return true
  }
  if (linkRouteName === activeRouteName) return true
  if (activeRouteName.startsWith(`${linkRouteName}-`)) return true
  if (linkRouteName.endsWith('s') && activeRouteName.startsWith(`${linkRouteName.slice(0, -1)}-`)) return true
  return false
}

/**
 * Route đang mở nằm trong nhánh nào — để mở sẵn group đó khi vào trang.
 *
 * Trả nhãn của mọi group là tổ tiên của link đang active. Không có nó thì vào
 * một URL trực tiếp sẽ thấy sidebar đóng hết và không biết mình đang ở đâu.
 */
export function activeGroupLabels(
  items: readonly NavItem[],
  activeRouteName: string,
): string[] {
  for (const item of items) {
    if (item.kind === 'link') {
      if (isRouteActiveForLink(item.routeName, activeRouteName)) return []
      continue
    }
    const inside = activeGroupLabels(item.children, activeRouteName)
    // `inside` rỗng có hai nghĩa: "không tìm thấy" và "tìm thấy ở ngay tầng này".
    // Phân biệt bằng cách kiểm lại có link khớp trong cây con hay không.
    if (
      inside.length > 0 ||
      flattenLinks(item.children).some((l) => isRouteActiveForLink(l.routeName, activeRouteName))
    ) {
      return [item.label, ...inside]
    }
  }
  return []
}
