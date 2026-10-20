<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink } from 'vue-router'

import { iconPath } from '#/shared/ui/icons'
import { isRouteActiveForLink, navKeyOf, type NavBadge, type NavItem } from './nav'

/**
 * Cây điều hướng — **một component cho mọi cấp**, gọi lại chính nó.
 *
 * Trước đây `AppShell.vue` viết cứng hai tầng `v-for` lồng nhau. Cái giá của nó
 * không phải là dài dòng mà là **một giới hạn không ai khai ra**: cấp thứ ba
 * trong `nav.config.ts` sẽ đơn giản không hiện, và không có gì báo lỗi. Đệ quy
 * làm số cấp thành một con số kiểm được (gate `nav-audit`), không phải một hằng
 * số ẩn trong template.
 *
 * ## Mô hình a11y: disclosure, KHÔNG phải `role="tree"`
 *
 * `<ul>` lồng nhau + `<button aria-expanded>` cho mỗi nhóm. Không dùng
 * `role="tree"` / `role="treeitem"` (research R-2): `role="tree"` là một lời hứa
 * với trình đọc màn hình rằng mũi tên lên/xuống/trái/phải, Home/End và
 * type-ahead đều hoạt động. Hứa mà không cài đủ thì **tệ hơn** là không hứa —
 * người dùng mất luôn cách điều hướng mặc định của danh sách link.
 *
 * `<nav>` bọc ngoài do `AppShell` cung cấp; component này chỉ trả về `<ul>` để
 * bản thân nó lồng vào chính nó được.
 *
 * ## Trạng thái mở nằm ở NGOÀI
 *
 * `expandedKeys` là prop, không phải state nội bộ. Vì component tự lồng chính
 * nó, state nội bộ sẽ nằm rải ở mỗi cấp và không ai đọc được "cây đang mở những
 * nhánh nào". Chủ sở hữu duy nhất là `AppShell`.
 */

const props = withDefaults(
  defineProps<{
    items: readonly NavItem[]
    /** Tên route đang mở, để đánh dấu `aria-current`. */
    activeRouteName: string
    /** Tham số truy vấn của route đang mở để so khớp link có query. */
    activeRouteQuery?: Record<string, unknown>
    /** Khoá đường dẫn của các nhóm đang mở. Xem `navKeyOf`. */
    expandedKeys: ReadonlySet<string>
    /** Khoá của nhóm cha. Rỗng ở cấp gốc. */
    parentKey?: string
    /** Cấp hiện tại, 1 ở gốc. Chỉ dùng để thụt lề. */
    depth?: number
    /** Trạng thái thanh bên đang thu gọn (Collapsed Rail). */
    isCollapsed?: boolean
  }>(),
  { parentKey: '', depth: 1, activeRouteQuery: () => ({}), isCollapsed: false },
)

const emit = defineEmits<{ toggle: [key: string] }>()

function resolveBadge(item: NavItem): NavBadge | null {
  if (item.kind !== 'link') return null
  if (typeof item.badge === 'function') {
    return item.badge()
  }
  return null
}

/**
 * Tên tường minh để component tham chiếu được chính nó trong template.
 *
 * Vue suy tên từ tên file nên đệ quy vẫn chạy nếu bỏ dòng này — nhưng lúc đó
 * việc "component này tự gọi chính nó" phụ thuộc vào **tên file**, và đổi tên
 * file sẽ làm nó vỡ ở runtime chứ không phải lúc build.
 *
 * Đặt dưới `defineProps`/`defineEmits` vì rule `vue/define-macros-order` đòi hai
 * macro đó ở ngay đầu block.
 */
defineOptions({ name: 'NavTree' })

/**
 * Nhóm không còn con nào thì **bỏ hẳn**, không render tiêu đề.
 *
 * `visibleNav()` đã lọc theo quyền trước khi tới đây, nhưng lọc ở cả hai nơi là
 * có chủ đích: một `children: []` viết thẳng trong `nav.config.ts` sẽ tạo ra một
 * nút bấm vào không ra gì, và đó không phải chuyện của quyền.
 */
const visibleItems = computed(() =>
  props.items.filter((item) => item.kind === 'link' || item.children.length > 0),
)

/**
 * Khoá song song với `visibleItems`, tra theo chỉ số.
 *
 * Tách thành mảng riêng thay vì gói `{ item, key }`: `v-for="(item, i)"` cho
 * `item` là một binding trực tiếp, nên `v-if="item.kind === 'link'"` thu hẹp
 * được kiểu. Gói vào object thì điều kiện thành `row.item.kind` và việc thu hẹp
 * kiểu qua chuỗi thuộc tính là thứ dễ vỡ khi đổi phiên bản trình biên dịch.
 */
const keys = computed(() =>
  visibleItems.value.map((item) => navKeyOf(props.parentKey, item.label)),
)
</script>

<template>
  <ul class="navlist" :class="{ 'navlist--nested': depth > 1 && !props.isCollapsed, 'navlist--collapsed': props.isCollapsed }">
    <li v-for="(item, i) in visibleItems" :key="keys[i]">
      <!-- Nút vỏ siêu dữ liệu (Ancestor Shell): chỉ đọc, không điều hướng -->
      <div
        v-if="item.kind === 'link' && item.isShell"
        class="navlink navlink--shell"
        :title="isCollapsed ? item.label : undefined"
      >
        <svg
          v-if="iconPath(item.icon)"
          class="navicon"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="2"
          stroke-linecap="round"
          stroke-linejoin="round"
          aria-hidden="true"
        >
          <path :d="iconPath(item.icon) ?? ''" />
        </svg>
        <span class="navlink__label">{{ item.label }}</span>
        <span class="navbadge navbadge--shell">Chỉ đọc</span>
      </div>

      <RouterLink
        v-else-if="item.kind === 'link'"
        class="navlink"
        :to="{ name: item.routeName, query: item.query }"
        :title="isCollapsed ? item.label : undefined"
        :aria-current="isRouteActiveForLink(item.routeName, activeRouteName, item.query, activeRouteQuery) ? 'page' : undefined"
      >
        <svg
          v-if="iconPath(item.icon)"
          class="navicon"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="2"
          stroke-linecap="round"
          stroke-linejoin="round"
          aria-hidden="true"
        >
          <path :d="iconPath(item.icon) ?? ''" />
        </svg>
        <span class="navlink__label">{{ item.label }}</span>
        <span
          v-if="resolveBadge(item)"
          class="navbadge"
          :class="[
            `navbadge--${resolveBadge(item)?.level ?? 'info'}`,
            { 'navbadge--pulse': resolveBadge(item)?.pulse },
          ]"
          aria-live="polite"
        >
          {{ (resolveBadge(item)?.count ?? 0) > 99 ? '99+' : resolveBadge(item)?.count }}
          <span class="sr-only">, {{ resolveBadge(item)?.label ?? `${resolveBadge(item)?.count} cảnh báo` }}</span>
        </span>
      </RouterLink>

      <template v-else>
        <!--
          Nhóm TĨNH: nhãn phân vùng, không phải control.
          Là `<div>` chứ không `<button aria-disabled>`: một button không làm gì vẫn
          nhận focus và vẫn được screen reader đọc là "nút", tức nó hứa một hành động
          không tồn tại. `<div>` không có hứa nào để phá.
          Không `role="heading"`: các con nằm trong `<ul>` ngay sau, và quan hệ đó đã
          rõ về cấu trúc; thêm một cấp heading giả vào giữa cây điều hướng làm danh
          sách heading của trang có mục không dẫn tới đâu.
        -->
        <div v-if="item.collapsible === false" class="navsection" :title="isCollapsed ? item.label : undefined">{{ item.label }}</div>

        <button
          v-else
          type="button"
          class="navgroup"
          :class="{ 'navgroup--shell': item.isShell }"
          :aria-expanded="expandedKeys.has(keys[i])"
          :title="isCollapsed ? item.label : undefined"
          @click="emit('toggle', keys[i])"
        >
          <svg
            v-if="iconPath(item.icon)"
            class="navicon"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            stroke-linecap="round"
            stroke-linejoin="round"
            aria-hidden="true"
          >
            <path :d="iconPath(item.icon) ?? ''" />
          </svg>
          <span>{{ item.label }}</span>
          <span v-if="item.isShell" class="navbadge navbadge--shell">Chỉ đọc</span>
          <svg
            class="navgroup__chevron"
            :class="{ 'navgroup__chevron--open': expandedKeys.has(keys[i]) }"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            stroke-linecap="round"
            aria-hidden="true"
          >
            <path d="m9 18 6-6-6-6" />
          </svg>
        </button>

        <!--
          `v-if` chứ không `v-show`. Hai lý do, cả hai đều thật:
          1. Cây đóng thì không tạo node — menu 3 cấp của một dự án lớn là vài
             trăm node vô ích trên mọi lần render.
          2. `v-show` để lại node kèm `display:none`, và jsdom không có layout
             engine nên test **không phân biệt được** ẩn với hiện. Một gate
             không phân biệt được đúng với sai thì không phải gate.
        -->
        <!--
          Nhóm tĩnh luôn hiện con — nó không có trạng thái đóng, nên `expandedKeys`
          không áp cho nó. Không có vế thứ hai này thì nhóm tĩnh hiện một nhãn rồi
          không có gì bên dưới cho tới khi ai đó bấm — mà nhãn thì không bấm được.
        -->
        <NavTree
          v-if="expandedKeys.has(keys[i]) || item.collapsible === false"
          :items="item.children"
          :active-route-name="activeRouteName"
          :active-route-query="activeRouteQuery"
          :expanded-keys="expandedKeys"
          :parent-key="keys[i]"
          :depth="depth + 1"
          :is-collapsed="props.isCollapsed"
          @toggle="emit('toggle', $event)"
        />
      </template>
    </li>
  </ul>
</template>

<style scoped src="./NavTree.css"></style>
