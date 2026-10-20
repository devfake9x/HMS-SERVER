<script setup lang="ts">
/**
 * Khung nội dung của một trang **bên trong `AppShell`**. Spec 007 §B.1.
 *
 * ## Vì sao nó tồn tại
 *
 * `[SUY LUẬN]` Trước spec này có **bốn** chủ của bề rộng khung trang, mỗi chủ một
 * câu trả lời: `HomeView` 48rem · `PlaceholderView` 32rem · `error-page.css`
 * 32rem · `AuditLogListView` không khai gì. Bốn quyết định độc lập cho cùng một
 * câu hỏi — đúng class defect F3 ("convention song song") mà base ra đời để chặn,
 * tái sinh ở tầng layout trong khi tầng component thì được canh gác.
 *
 * ## Nó KHÔNG khai `padding`, và đó là quyết định
 *
 * `AppShell.css` `.shell__main` đã khai `padding: 1.25rem`. Nếu component này
 * cũng khai padding thì có **hai chủ** của cùng một khoảng cách, và tổng là một
 * con số không ai chọn. Mỗi thuộc tính một chủ: `.shell__main` giữ khoảng cách
 * ngoài, file này giữ **bề rộng + căn giữa + khoảng cách dọc giữa các section**.
 *
 * ## `AuthLayout` KHÔNG dùng nó, và đó cũng là quyết định
 *
 * `AuthLayout` có `max-width: 26rem` riêng. Nó **không** phải chủ thứ năm của
 * cùng ý niệm: nó là card căn giữa cho người **chưa** đăng nhập, nằm ngoài
 * `AppShell`, và bề rộng của nó là bề rộng của một form. Hai ý niệm khác nhau thì
 * hai định nghĩa là đúng. Luật gate `view-no-page-layout` cố tình **không** phủ
 * `shared/layouts/`, và lý do nằm ở đây chứ không nằm trong một allow-list mà sau
 * này không ai biết vì sao có.
 */

import { computed, inject } from 'vue'
import { routeLocationKey } from 'vue-router'

import { SHELL_HEADER_KEY, useBreadcrumbs } from '#/shared/navigation/useBreadcrumbs'
import Breadcrumb, { type BreadcrumbItem } from './Breadcrumb.vue'

const props = withDefaults(
  defineProps<{
    /**
     * `full` — tràn viền 100%, chuẩn Datacake cho trang bảng dữ liệu.
     * `default` — tràn viền 100% diện tích body khả dụng.
     * `form` — 58rem, cho các trang tạo / sửa dữ liệu (CRUD).
     * `prose` — 48rem, cho trang chủ yếu là chữ. Dòng chữ dài quá ~75 ký tự thì
     * mắt mất dòng khi xuống hàng.
     */
    width?: 'full' | 'default' | 'prose' | 'form'
    /**
     * Bật / tắt thanh breadcrumb cục bộ trong PageContainer.
     * Mặc định không render vì Breadcrumb đã chuyển lên Header của AppShell.
     */
    breadcrumb?: boolean
    /**
     * Tuỳ chọn truyền chuỗi breadcrumb thủ công thay vì tự động phân giải từ menu.
     */
    breadcrumbItems?: readonly BreadcrumbItem[]
  }>(),
  {
    width: 'default',
    breadcrumb: undefined,
    breadcrumbItems: undefined,
  },
)

const route = inject(routeLocationKey, null)
const hasShellHeader = inject(SHELL_HEADER_KEY, false)

const { breadcrumbs } = useBreadcrumbs()

const itemsToRender = computed(() => {
  if (props.breadcrumbItems) {
    return props.breadcrumbItems
  }
  return breadcrumbs.value
})

const shouldRenderBreadcrumb = computed(() => {
  // 1. Nếu caller chủ động tắt: không render
  if (props.breadcrumb === false) return false
  // 2. Nếu truyền breadcrumbItems thủ công: luôn render
  if (props.breadcrumbItems && props.breadcrumbItems.length > 0) return true
  // 3. Nếu đang chạy bên trong AppShell đã có Header Breadcrumb: không render đúp trong container
  if (hasShellHeader && props.breadcrumb !== true) return false
  // 4. Mặc định: render khi có mục và route hợp lệ
  return itemsToRender.value.length > 0 && Boolean(route?.name)
})
</script>

<template>
  <div class="page" :class="`page--${width}`">
    <Breadcrumb
      v-if="shouldRenderBreadcrumb"
      class="page__breadcrumb"
      :items="itemsToRender"
    />
    <slot />
  </div>
</template>

<style scoped>
.page {
  display: flex;
  flex-direction: column;
  /* Khoảng cách dọc giữa các section của trang. Đây là thứ trước đây mỗi view tự
     khai (`gap: 1rem` ở AuditLogListView, `padding: 3rem 0` ở error-page). */
  gap: 1.25rem;
  width: 100%;
  margin-inline: auto;
  padding-bottom: 2rem;
}

/* Bề rộng bảng Datacake: tràn trọn vẹn bề rộng body khả dụng */
.page--full,
.page--default {
  max-width: 100%;
}

.page--form {
  max-width: min(100%, 58rem);
}

.page--prose {
  max-width: 48rem;
}
</style>
