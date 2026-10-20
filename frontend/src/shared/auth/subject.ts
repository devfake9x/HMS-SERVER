import { computed, inject, provide, type ComputedRef, type InjectionKey } from 'vue'

import type { AuthSubject } from '#/core/auth/can'

export type { AuthSubject }

/**
 * Cấp `AuthSubject` xuống cây component qua provide/inject.
 *
 * ## Vì sao cần lớp này thay vì gọi store trực tiếp
 *
 * `useSessionStore` sống ở `features/auth`. Một view ở `features/hierarchy` import
 * nó là vi phạm luật gate `cross-feature`, và luật đó đúng: hai feature biết nhau
 * thì không tách được cái nào ra khỏi cái nào. Nhưng **quyền** thì mọi feature đều
 * cần — nó không phải nghiệp vụ của `auth`, nó là ngữ cảnh của cả ứng dụng.
 *
 * Nên hợp đồng ở `shared/` (file này), dữ liệu do `app/` cấp (`App.vue` là chỗ duy
 * nhất được biết cả `features/auth` lẫn `shared/`), và feature chỉ tiêu thụ. Cùng
 * hình dạng với cách `AppShell` nhận `subject` qua props — chỉ khác là provide đi
 * xuyên qua `RouterView`, nơi props không đi qua được.
 *
 * ## Fail-closed khi không ai provide
 *
 * `useSubject()` trả subject có `permissions: null` nếu không tìm thấy provider.
 * `can()` với `permissions === null` trả `false` (Base §7.2 — chưa biết thì CHẶN).
 * Nên một component render ngoài `App.vue` — trang đăng nhập, hoặc một test không
 * dựng provider — thấy đúng "không có quyền gì", không phải "có mọi quyền".
 *
 * Đây là điểm dễ làm sai theo chiều tai hại: một default `permissions: new Set()`
 * cũng chặn, nhưng nó nói "đã biết và không có quyền" thay vì "chưa biết", và
 * `subjectReady` ở tầng trên sẽ không phân biệt được hai trạng thái đó nữa.
 */
export const SUBJECT_KEY = Symbol('auth-subject') as InjectionKey<ComputedRef<AuthSubject>>

const UNKNOWN: AuthSubject = { permissions: null, roles: Object.freeze([]) as any }

export function provideSubject(subject: ComputedRef<AuthSubject>): void {
  provide(SUBJECT_KEY, subject)
}

export function useSubject(): ComputedRef<AuthSubject> {
  return inject(SUBJECT_KEY, computed(() => UNKNOWN))
}
