<script setup lang="ts">
/**
 * Khung màn hình danh sách chuẩn hoá (StandardListPage).
 *
 * Đóng vai trò là "khung xương" 5 tầng chuẩn mực cho toàn bộ các trang quản trị
 * và danh sách dữ liệu trong hệ thống:
 * 1. PageContainer (định vị bề rộng full-width)
 * 2. PageHeader (Tiêu đề, mô tả và toolbar actions)
 * 3. FilterBar slot (Thanh tìm kiếm và bộ lọc command grid)
 * 4. Default slot (Khung bảng dữ liệu trung tâm hoặc Split layout)
 * 5. Modals slot (Hộp thoại xác nhận xoá và modal thêm/sửa)
 */

import { onBeforeUnmount, ref } from 'vue'
import PageContainer from '#/shared/ui/PageContainer.vue'
import PageHeader from '#/shared/ui/PageHeader.vue'
import type { StandardListPageProps } from './page-types'

const props = withDefaults(
  defineProps<StandardListPageProps>(),
  {
    description: undefined,
    singleLine: false,
    splitLayout: false,
    width: 'full',
    breadcrumb: false,
    resizableSplit: false,
    sidebarWidth: 300,
    minSidebarWidth: 240,
    maxSidebarWidth: 520,
    storageKey: 'standard_list_page_sidebar_width',
  },
)

// Khởi tạo độ rộng sidebar từ localStorage nếu hợp lệ
function getInitialWidth(): number {
  if (typeof window !== 'undefined' && props.resizableSplit && props.storageKey) {
    try {
      const saved = localStorage.getItem(props.storageKey)
      if (saved) {
        const val = parseInt(saved, 10)
        if (!isNaN(val) && val >= props.minSidebarWidth && val <= props.maxSidebarWidth) {
          return val
        }
      }
    } catch {
      // Ignore
    }
  }
  return props.sidebarWidth
}

const currentSidebarWidth = ref<number>(getInitialWidth())
const isResizing = ref(false)
let startX = 0
let startWidth = 0

function onPointerMove(e: PointerEvent) {
  if (!isResizing.value) return
  const delta = e.clientX - startX
  const newWidth = Math.min(Math.max(startWidth + delta, props.minSidebarWidth), props.maxSidebarWidth)
  currentSidebarWidth.value = newWidth
}

function onPointerUp() {
  if (!isResizing.value) return
  isResizing.value = false
  if (typeof window !== 'undefined' && props.storageKey) {
    try {
      localStorage.setItem(props.storageKey, String(currentSidebarWidth.value))
    } catch {
      // Ignore
    }
  }
  window.removeEventListener('pointermove', onPointerMove)
  window.removeEventListener('pointerup', onPointerUp)
  window.removeEventListener('pointercancel', onPointerUp)
}

function startResize(e: PointerEvent) {
  if (!props.resizableSplit) return
  isResizing.value = true
  startX = e.clientX
  startWidth = currentSidebarWidth.value

  window.addEventListener('pointermove', onPointerMove)
  window.addEventListener('pointerup', onPointerUp)
  window.addEventListener('pointercancel', onPointerUp)
}

function resetSidebarWidth() {
  currentSidebarWidth.value = props.sidebarWidth
  if (typeof window !== 'undefined' && props.storageKey) {
    try {
      localStorage.removeItem(props.storageKey)
    } catch {
      // Ignore
    }
  }
}

function handleSplitterKeyDown(e: KeyboardEvent) {
  const step = 16
  let updated = false
  if (e.key === 'ArrowLeft') {
    e.preventDefault()
    currentSidebarWidth.value = Math.max(currentSidebarWidth.value - step, props.minSidebarWidth)
    updated = true
  } else if (e.key === 'ArrowRight') {
    e.preventDefault()
    currentSidebarWidth.value = Math.min(currentSidebarWidth.value + step, props.maxSidebarWidth)
    updated = true
  } else if (e.key === 'Home') {
    e.preventDefault()
    currentSidebarWidth.value = props.minSidebarWidth
    updated = true
  } else if (e.key === 'End') {
    e.preventDefault()
    currentSidebarWidth.value = props.maxSidebarWidth
    updated = true
  } else if (e.key === 'Enter') {
    e.preventDefault()
    resetSidebarWidth()
  }

  if (updated && typeof window !== 'undefined' && props.storageKey) {
    try {
      localStorage.setItem(props.storageKey, String(currentSidebarWidth.value))
    } catch {}
  }
}

onBeforeUnmount(() => {
  if (typeof window !== 'undefined') {
    window.removeEventListener('pointermove', onPointerMove)
    window.removeEventListener('pointerup', onPointerUp)
    window.removeEventListener('pointercancel', onPointerUp)
  }
})
</script>

<template>
  <PageContainer :width="props.width" :breadcrumb="props.breadcrumb">
    <div class="standard-list-page" :class="{ 'standard-list-page--split': props.splitLayout }">
      <!-- TẦNG 1: PAGE HEADER & ACTION TOOLBAR -->
      <PageHeader
        :title="props.title"
        :description="props.description"
        :single-line="props.singleLine"
      >
        <template v-if="$slots.actions" #actions>
          <div class="standard-list-page__toolbar">
            <slot name="actions" />
          </div>
        </template>
      </PageHeader>

      <!-- TẦNG 2: COMMAND FILTER BAR -->
      <div v-if="$slots.filters" class="standard-list-page__filters">
        <slot name="filters" />
      </div>

      <!-- TẦNG 3: NỘI DUNG CHÍNH (TABLE HOẶC SPLIT TREE+TABLE) -->
      <div
        v-if="props.splitLayout"
        class="standard-list-page__split-container"
        :class="{
          'standard-list-page__split-container--resizable': props.resizableSplit,
          'standard-list-page__split-container--resizing': isResizing,
        }"
        :style="{ '--split-sidebar-width': `${currentSidebarWidth}px` }"
      >
        <aside v-if="$slots.sidebar" class="standard-list-page__sidebar">
          <slot name="sidebar" />
        </aside>

        <!-- Resizable Splitter Handle -->
        <div
          v-if="props.resizableSplit && $slots.sidebar"
          class="standard-list-page__splitter"
          role="separator"
          tabindex="0"
          aria-orientation="vertical"
          aria-label="Thanh kéo phân cách độ rộng khung cây phân cấp"
          :aria-valuenow="currentSidebarWidth"
          :aria-valuemin="props.minSidebarWidth"
          :aria-valuemax="props.maxSidebarWidth"
          title="Kéo sang trái/phải để chỉnh độ rộng. Nhấp đúp để đặt lại mặc định."
          @pointerdown="startResize"
          @dblclick="resetSidebarWidth"
          @keydown="handleSplitterKeyDown"
        >
          <div class="standard-list-page__splitter-handle"></div>
        </div>

        <main class="standard-list-page__main-content">
          <slot />
        </main>
      </div>

      <div v-else class="standard-list-page__content">
        <slot />
      </div>

      <!-- TẦNG 4: FOOTER PHÂN TRANG PHỤ TRỢ (NẾU KHÔNG GỘP TRONG TABLE) -->
      <div v-if="$slots.footer" class="standard-list-page__footer">
        <slot name="footer" />
      </div>

      <!-- TẦNG 5: HỘP THOẠI VÀ MODAL -->
      <slot name="modals" />
    </div>
  </PageContainer>
</template>

<style scoped>
.standard-list-page {
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
  width: 100%;
}

.standard-list-page__toolbar {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.standard-list-page__filters {
  width: 100%;
}

.standard-list-page__content {
  width: 100%;
}

.standard-list-page__split-container {
  display: grid;
  grid-template-columns: var(--split-sidebar-width, 280px) 1fr;
  gap: 1.25rem;
  align-items: start;
  width: 100%;
}

.standard-list-page__split-container--resizable {
  display: flex;
  gap: 0;
  align-items: stretch;
}

.standard-list-page__split-container--resizing {
  user-select: none;
  cursor: col-resize;
}

.standard-list-page__sidebar {
  position: sticky;
  top: 1rem;
}

.standard-list-page__split-container--resizable .standard-list-page__sidebar {
  flex: 0 0 var(--split-sidebar-width, 300px);
  width: var(--split-sidebar-width, 300px);
  min-width: 0;
}

.standard-list-page__splitter {
  flex: 0 0 16px;
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: col-resize;
  user-select: none;
  touch-action: none;
  z-index: 10;
  transition: background-color 0.15s ease;
}

.standard-list-page__splitter:hover,
.standard-list-page__splitter:focus-visible {
  outline: none;
}

.standard-list-page__splitter-handle {
  width: 4px;
  height: 48px;
  border-radius: 9999px;
  background-color: var(--border-default, #e5e7eb);
  transition: all 0.2s ease;
}

.standard-list-page__splitter:hover .standard-list-page__splitter-handle,
.standard-list-page__splitter:focus-visible .standard-list-page__splitter-handle,
.standard-list-page__split-container--resizing .standard-list-page__splitter-handle {
  background-color: var(--color-primary, #155dfc);
  height: 64px;
  width: 5px;
}

.standard-list-page__main-content {
  min-width: 0;
  width: 100%;
}

.standard-list-page__split-container--resizable .standard-list-page__main-content {
  flex: 1 1 0%;
}

.standard-list-page__footer {
  width: 100%;
}

@media (max-width: 960px) {
  .standard-list-page__split-container {
    grid-template-columns: 1fr;
  }
  .standard-list-page__split-container--resizable {
    flex-direction: column;
    gap: 1.25rem;
  }
  .standard-list-page__split-container--resizable .standard-list-page__sidebar {
    flex: 1 1 auto;
    width: 100%;
    position: static;
  }
  .standard-list-page__splitter {
    display: none;
  }
}
</style>
