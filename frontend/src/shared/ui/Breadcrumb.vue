<script setup lang="ts">
/**
 * Đường dẫn phân cấp trên đầu trang.
 *
 * ## Vì sao mục cuối KHÔNG phải link
 *
 * Mục cuối là trang đang mở. Một link trỏ về chính nó là cái bẫy cho người dùng
 * bàn phím: nó nhận được focus, đọc lên như một đích đến, rồi không đi đâu cả. Nên
 * mục cuối render thành `<span aria-current="page">`, và `items` cuối cùng **không**
 * cần `routeName`.
 *
 * ## Vì sao `<nav aria-label>` chứ không chỉ một hàng div
 *
 * Screen reader liệt kê được các landmark `nav` để nhảy tới. Không có nhãn thì hai
 * `nav` trên cùng trang (sidebar và breadcrumb) đọc ra giống nhau.
 *
 * Dấu phân cách là `aria-hidden`: nó là hình, không phải nghĩa. Không ẩn thì screen
 * reader đọc "chevron right" giữa mỗi mục.
 *
 * ## Cái nó KHÔNG làm
 *
 * Không tự suy đường dẫn từ route. Suy tự động cần một cây quan hệ cha-con của
 * route mà `routes.ts` không khai — và đoán từ path segment sẽ sinh ra mục trung
 * gian không tới được (`/quan-ly` không phải một trang). Caller truyền tường minh.
 */
import { inject } from 'vue'
import { routerKey } from 'vue-router'


export type BreadcrumbItem = {
  label: string
  /** Tên route. Bỏ trống ở mục cuối, hoặc ở mục không phải trang thật. */
  routeName?: string
  /** Tham số route (tuỳ chọn), dùng khi route đòi hỏi params như :id */
  routeParams?: Record<string, string | number>
}

const props = withDefaults(
  defineProps<{
    items: readonly BreadcrumbItem[]
    /**
     * Dấu phân cách: 'chevron' (>) hoặc 'slash' (/). Mặc định 'chevron' để tương thích ngược 100%.
     */
    separator?: 'chevron' | 'slash'
  }>(),
  {
    separator: 'chevron',
  },
)

const router = inject(routerKey, null)

function isRouteResolvable(name?: string): boolean {
  if (!name) return false
  if (!router) return true
  try {
    return router.hasRoute(name)
  } catch {
    return true
  }
}
</script>

<template>
  <nav class="crumbs" aria-label="Đường dẫn">
    <ol class="crumbs__list">
      <li v-for="(item, i) in items" :key="`${item.label}-${i}`" class="crumbs__item">
        <!-- Dấu phân cách dạng gạch chéo '/' chuẩn ảnh mockup Datacake Header -->
        <span
          v-if="props.separator === 'slash' && i > 0"
          class="crumbs__sep crumbs__sep--slash"
          aria-hidden="true"
        >
          /
        </span>

        <!-- Dấu phân cách dạng Chevron '>' Solid 20x20 -->
        <svg
          v-else-if="i > 0"
          class="crumbs__sep"
          viewBox="0 0 20 20"
          fill="currentColor"
          aria-hidden="true"
        >
          <path
            fill-rule="evenodd"
            d="M7.293 14.707a1 1 0 010-1.414L10.586 10 7.293 6.707a1 1 0 011.414-1.414l4 4a1 1 0 010 1.414l-4 4a1 1 0 01-1.414 0z"
            clip-rule="evenodd"
          />
        </svg>

        <RouterLink
          v-if="item.routeName && i < items.length - 1 && isRouteResolvable(item.routeName)"
          class="crumbs__link"
          :to="{ name: item.routeName, params: item.routeParams }"
          :title="item.label"
        >
          {{ item.label }}
        </RouterLink>

        <!-- Mục cuối: trang hiện tại. Không phải link. -->
        <span
          v-else-if="i === items.length - 1"
          class="crumbs__current"
          aria-current="page"
          :title="item.label"
        >
          {{ item.label }}
        </span>

        <!-- Mục trung gian không có route (ví dụ tên nhóm "Quản lý"). Không phải link, không phải trang hiện tại. -->
        <span
          v-else
          class="crumbs__static"
          :title="item.label"
        >
          {{ item.label }}
        </span>
      </li>
    </ol>
  </nav>
</template>

<style scoped>
.crumbs {
  overflow-x: auto;
  scrollbar-width: none;
}

.crumbs::-webkit-scrollbar {
  display: none;
}

.crumbs__list {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  margin: 0;
  padding: 0;
  list-style: none;
}

.crumbs__item {
  display: inline-flex;
  align-items: center;
  font-size: 0.875rem;
  line-height: 1.25rem;
  font-weight: 500;
}

.crumbs__sep {
  inline-size: 1.25rem;
  block-size: 1.25rem;
  margin-inline: 0.25rem;
  color: var(--color-gray-400, #9ca3af);
  flex-shrink: 0;
}

.crumbs__sep--slash {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  inline-size: auto;
  block-size: auto;
  font-size: 0.875rem;
  font-weight: 400;
  line-height: 1;
  color: var(--color-gray-400, #9ca3af);
  margin-inline: 0.5rem;
  user-select: none;
}

.crumbs__link {
  color: var(--color-gray-500, #6b7280);
  font-weight: 500;
  text-decoration: none;
  border-radius: 0.1875rem;
  max-width: 14rem;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  transition: color 150ms ease-in-out;
}

.crumbs__link:hover {
  color: var(--color-gray-700, #374151);
  text-decoration: none;
}

.crumbs__link:focus-visible {
  outline: 2px solid var(--status-info);
  outline-offset: 2px;
}

.crumbs__static {
  color: var(--color-gray-500, #6b7280);
  font-weight: 500;
  max-width: 14rem;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.crumbs__current {
  color: var(--color-gray-700, #374151);
  font-weight: 500;
  max-width: 16rem;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

:global(html[data-theme='dark']) .crumbs__sep {
  color: var(--text-hint, #71717a);
}

:global(html[data-theme='dark']) .crumbs__link,
:global(html[data-theme='dark']) .crumbs__static {
  color: var(--text-secondary, #a1a1aa);
}

:global(html[data-theme='dark']) .crumbs__link:hover {
  color: var(--text-heading, #ffffff);
}

:global(html[data-theme='dark']) .crumbs__current {
  color: var(--text-primary, #f4f4f5);
}
</style>

