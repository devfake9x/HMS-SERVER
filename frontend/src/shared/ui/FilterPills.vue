<script setup lang="ts">
/**
 * Danh sách các tag tiêu chí lọc (Filter Pills / Badges) đang hoạt động.
 *
 * ## Trải nghiệm người dùng
 *
 *   - Hiển thị từng tiêu chí dạng badge bo tròn nhỏ gọn (ví dụ: `Đơn vị: Công ty A`, `Trạng thái: Hoạt động`).
 *   - Mỗi badge có nút `✕` để gỡ nhanh tiêu chí đơn lẻ.
 *   - Nút "Xóa tất cả" xuất hiện khi có từ 2 tiêu chí trở lên.
 *   - Tuân thủ chuẩn tương phản WCAG 2.2 AA và không dùng utility class cho màu sắc.
 */

export type FilterPillItem = {
  key: string
  label: string
  valueLabel: string
}

defineProps<{
  items: readonly FilterPillItem[]
}>()

const emit = defineEmits<{
  (e: 'remove', key: string): void
  (e: 'clearAll'): void
}>()
</script>

<template>
  <div v-if="items.length > 0" class="filter-pills" role="region" aria-label="Các tiêu chí lọc đang áp dụng">
    <ul class="filter-pills__list">
      <li v-for="item in items" :key="item.key" class="filter-pills__item">
        <span class="filter-pills__name">{{ item.label }}:</span>
        <strong class="filter-pills__val">{{ item.valueLabel }}</strong>

        <button
          type="button"
          class="filter-pills__remove"
          :aria-label="`Xóa bộ lọc ${item.label}: ${item.valueLabel}`"
          @click="emit('remove', item.key)"
        >
          <svg
            class="filter-pills__remove-icon"
            viewBox="0 0 20 20"
            fill="currentColor"
            aria-hidden="true"
          >
            <path
              fill-rule="evenodd"
              d="M4.293 4.293a1 1 0 011.414 0L10 8.586l4.293-4.293a1 1 0 111.414 1.414L11.414 10l4.293 4.293a1 1 0 01-1.414 1.414L10 11.414l-4.293 4.293a1 1 0 01-1.414-1.414L8.586 10 4.293 5.707a1 1 0 010-1.414z"
              clip-rule="evenodd"
            />
          </svg>
        </button>
      </li>
    </ul>

    <button
      v-if="items.length >= 2"
      type="button"
      class="filter-pills__clear"
      @click="emit('clearAll')"
    >
      Xóa tất cả
    </button>
  </div>
</template>

<style scoped>
.filter-pills {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.5rem;
  margin-block-end: 0.75rem;
  font-size: 0.8125rem;
  line-height: 1.4;
}

.filter-pills__list {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.375rem;
  margin: 0;
  padding: 0;
  list-style: none;
}

.filter-pills__item {
  display: inline-flex;
  align-items: center;
  gap: 0.25rem;
  padding: 0.2rem 0.5rem;
  border-radius: 9999px;
  border: 1px solid var(--border-subtle);
  background-color: var(--surface-overlay);
  color: var(--text-primary);
  font-size: 0.75rem;
  line-height: 1.25;
}

.filter-pills__name {
  color: var(--text-secondary);
  font-weight: 400;
}

.filter-pills__val {
  color: var(--text-primary);
  font-weight: 600;
}

.filter-pills__remove {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 0.875rem;
  height: 0.875rem;
  margin-inline-start: 0.125rem;
  padding: 0;
  border: none;
  border-radius: 9999px;
  background-color: transparent;
  color: var(--text-secondary);
  cursor: pointer;
  transition: background-color 100ms ease, color 100ms ease;
}

.filter-pills__remove:hover {
  background-color: var(--border-subtle);
  color: var(--text-primary);
}

.filter-pills__remove:focus-visible {
  outline: 2px solid var(--status-info);
  outline-offset: 1px;
}

.filter-pills__remove-icon {
  width: 0.75rem;
  height: 0.75rem;
  flex: none;
}

.filter-pills__clear {
  padding: 0.125rem 0.375rem;
  border: none;
  border-radius: 0.25rem;
  background-color: transparent;
  color: var(--status-info);
  font-size: 0.75rem;
  font-weight: 500;
  cursor: pointer;
  text-decoration: underline;
  text-underline-offset: 2px;
  transition: opacity 100ms ease;
}

.filter-pills__clear:hover {
  opacity: 0.8;
}

.filter-pills__clear:focus-visible {
  outline: 2px solid var(--status-info);
  outline-offset: 1px;
}
</style>
