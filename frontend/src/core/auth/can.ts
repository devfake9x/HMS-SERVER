import { ROLE, type PermissionCode } from './permissions'

/**
 * Kiểm quyền. Base architecture §7.
 *
 * Cố tình đơn giản: `Set<string>` + role bypass. Không có lớp abstraction
 * `(action, subject)` nào — backend trả flat string, và abstraction lệch shape
 * sẽ không map được rồi bị comment lại, để lại một hệ ACL chết trong repo.
 */
export type AuthSubject = {
  /** null = chưa biết. Base §7.2: chưa biết thì CHẶN, không phải cho qua. */
  permissions: ReadonlySet<PermissionCode> | null
  roles: readonly string[]
  companyId?: string | null
  organizationId?: string | null
}

export function isSuperAdmin(subject: AuthSubject): boolean {
  return (
    subject.roles.includes(ROLE.SUPER_ADMIN) ||
    subject.roles.includes('super_admin') ||
    subject.roles.includes(ROLE.ADMIN) ||
    subject.roles.includes('admin')
  )
}

/**
 * `true` chỉ khi biết chắc chủ thể CÓ quyền.
 *
 * Fail closed: `permissions === null` (chưa fetch xong, hoặc fetch lỗi) trả `false`.
 * Đây là điểm sửa trực tiếp lỗi của bản cũ — guard cũ bỏ qua cả check khi
 * mảng permission rỗng, nên user không quyền vẫn vào được route được gate.
 */
export function can(subject: AuthSubject, code: PermissionCode): boolean {
  if (isSuperAdmin(subject)) return true
  if (subject.permissions === null) return false
  return subject.permissions.has(code)
}

/** Có ÍT NHẤT một trong các quyền. */
export function canAny(subject: AuthSubject, codes: readonly PermissionCode[]): boolean {
  if (isSuperAdmin(subject)) return true
  if (subject.permissions === null) return false
  return codes.some((c) => subject.permissions!.has(c))
}

/** Có TẤT CẢ các quyền. */
export function canAll(subject: AuthSubject, codes: readonly PermissionCode[]): boolean {
  if (isSuperAdmin(subject)) return true
  if (subject.permissions === null) return false
  return codes.every((c) => subject.permissions!.has(c))
}

/**
 * Kiểm tra phân quyền 2 chiều (WHAT x WHERE):
 * - Chiều 1: Quyền tính năng (PermissionCode)
 * - Chiều 2: Cờ phạm vi dữ liệu (AccessMask bitmask)
 */
export function canAccess(
  subject: AuthSubject,
  code: PermissionCode,
  nodeAccessMask?: number | null,
  requiredFlag?: number | null,
): boolean {
  if (!can(subject, code)) return false
  if (isSuperAdmin(subject)) return true
  if (requiredFlag == null || requiredFlag === 0) return true
  if (nodeAccessMask == null) return false
  return (nodeAccessMask & requiredFlag) === requiredFlag
}

/**
 * Lọc danh sách permission code lạ từ server thành `PermissionCode` đã biết.
 * Code server trả mà frontend không biết bị bỏ — và đó là hành vi đúng:
 * frontend không thể gate theo thứ nó không hiểu.
 */
export function toPermissionSet(
  raw: readonly string[],
  known: readonly PermissionCode[],
): ReadonlySet<PermissionCode> {
  const knownSet = new Set<string>(known)
  const out = new Set<PermissionCode>()
  for (const r of raw) {
    if (knownSet.has(r)) out.add(r as PermissionCode)
  }
  return out
}
