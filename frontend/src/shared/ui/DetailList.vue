<script setup lang="ts">
/**
 * DetailList — bảng thông tin chi tiết kiểu Datacake ("General / Connection
 * Info" panel): các mục label–giá trị xếp lưới responsive, mỗi mục là một
 * `DetailListItem` đặt trong slot default.
 *
 * Giải phẫu đo từ Datacake (specs/012 datacake-shared-components-reference):
 * section title 14px bold gray-500; lưới 1 cột (mobile) → 2 (≥768px) → 3 (≥1280px).
 * Panel bên ngoài do `Card` đảm nhiệm — component này chỉ là nội dung.
 */
defineProps<{
  /** Tiêu đề section — bỏ qua nếu truyền rỗng. */
  title?: string
}>()
</script>

<template>
  <section class="detail-list">
    <h3 v-if="title" class="detail-list__title">{{ title }}</h3>
    <dl class="detail-list__grid">
      <slot />
    </dl>
  </section>
</template>

<style scoped>
.detail-list__title {
  margin: 0 0 0.75rem 0;
  font-size: 0.875rem;
  font-weight: 700;
  color: var(--text-secondary);
}

.detail-list__grid {
  display: grid;
  grid-template-columns: 1fr;
  gap: 1rem 1.5rem;
  margin: 0;
}

@media (min-width: 48rem) {
  .detail-list__grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (min-width: 80rem) {
  .detail-list__grid {
    grid-template-columns: repeat(3, minmax(0, 1fr));
  }
}
</style>
