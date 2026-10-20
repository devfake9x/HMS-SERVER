<script setup lang="ts">
/**
 * EmptyState — Thành phần hiển thị trạng thái rỗng chuyên nghiệp chuẩn Datacake.
 *
 * Thiết kế đo đạc từ Datacake IoT Table Empty State:
 * - Icon hình tròn có dấu chấm than tinh tế (Exclamation Circle).
 * - Tiêu đề chính đậm rõ nét (1rem, font-weight: 600, text-primary).
 * - Dòng hướng dẫn/mô tả định hướng người dùng (0.875rem, text-secondary).
 * - Khoảng thở khoáng đạt (padding 3.5rem 1.5rem, min-height 220px).
 * - Hỗ trợ slots: #icon, #title, #description, #action.
 */

const props = withDefaults(
  defineProps<{
    title?: string
    description?: string
    icon?: 'info' | 'search' | 'empty'
  }>(),
  {
    title: 'Chưa có dữ liệu',
    description: undefined,
    icon: 'info',
  },
)
</script>

<template>
  <div class="empty-state" role="status">
    <div class="empty-state__icon">
      <slot name="icon">
        <!-- Icon Kính lúp khi rỗng do tìm kiếm -->
        <svg
          v-if="props.icon === 'search'"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="1.75"
          stroke-linecap="round"
          stroke-linejoin="round"
          aria-hidden="true"
        >
          <circle cx="11" cy="11" r="8" />
          <line x1="21" y1="21" x2="16.65" y2="16.65" stroke-width="2" />
          <line x1="8" y1="11" x2="14" y2="11" stroke-dasharray="2 2" />
        </svg>

        <!-- Icon Tròn Chấm Than Chuẩn Datacake (Mặc định) -->
        <svg
          v-else
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="2"
          stroke-linecap="round"
          stroke-linejoin="round"
          aria-hidden="true"
        >
          <path d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
        </svg>
      </slot>
    </div>

    <h2 class="empty-state__title">
      <slot name="title">{{ props.title }}</slot>
    </h2>

    <p v-if="props.description || $slots.description" class="empty-state__description">
      <slot name="description">{{ props.description }}</slot>
    </p>

    <div v-if="$slots.action" class="empty-state__action">
      <slot name="action" />
    </div>
  </div>
</template>

<style scoped>
.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  text-align: center;
  padding: 3.5rem 1rem;
  min-height: 220px;
  width: 100%;
  box-sizing: border-box;
}

@media (min-width: 640px) {
  .empty-state {
    padding: 3.5rem 1.5rem;
  }
}

.empty-state__icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 4rem;
  height: 4rem;
  margin-bottom: 1rem;
  color: var(--text-muted, #9ca3af);
}

.empty-state__icon svg {
  width: 100%;
  height: 100%;
}

.empty-state__title {
  margin: 0;
  font-size: 1.125rem;
  font-weight: 500;
  line-height: 1.5;
  color: var(--text-secondary, #6b7280);
}

.empty-state__description {
  margin: 0.375rem 0 0;
  font-size: 0.875rem;
  line-height: 1.5;
  color: var(--text-secondary, #6b7280);
  max-width: 32rem;
  word-break: break-word;
}

.empty-state__action {
  margin-top: 1.25rem;
  display: flex;
  align-items: center;
  gap: 0.75rem;
}
</style>
