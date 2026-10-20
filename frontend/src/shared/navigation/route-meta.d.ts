import type { RouteAccess } from './guard'

/**
 * Mở rộng `RouteMeta` của vue-router.
 *
 * Đặt ở `shared/navigation/` chứ không ở `app/router/` vì **nhiều tầng cần nó**:
 * mỗi `features/<name>/routes.ts` khai `meta.access`, và `app/router/` cũng vậy.
 * Đặt ở `app/` thì `features/` phải phụ thuộc lên tầng trên để lấy type — trái
 * luật import một chiều.
 *
 * `RouteAccess` cũng sống ở `shared/navigation/guard.ts`, nên khai báo này nằm
 * cạnh thứ nó mô tả.
 */
declare module 'vue-router' {
  interface RouteMeta {
    /**
     * Yêu cầu truy cập. **Không khai nghĩa là cần đăng nhập** — xem luật 1 trong
     * `guard.ts`. Quên khai phải là lỗi "không vào được".
     */
    access?: RouteAccess

    /** Tiêu đề trang, ghi vào `document.title`. Phase 4 đổi thành khoá i18n. */
    title?: string
  }
}
