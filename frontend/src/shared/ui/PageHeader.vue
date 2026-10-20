<script setup lang="ts">
/**
 * Đầu trang: tiêu đề + mô tả tuỳ chọn + vùng hành động bên phải. Spec 007 §B.1.
 *
 * ## `<h1>` sống ở đây, không sống ở view
 *
 * `[SUY LUẬN]` Trước spec này **năm** view mỗi cái tự viết `<h1>` với một class
 * riêng (`home__title`, `ph__title`, `err__title` ×2, `audit-log-view__title`).
 * Một trang không có `h1` làm người dùng screen reader mất mốc điều hướng, và
 * năm chỗ tự viết là năm chỗ có thể quên. Đưa nó vào đây thì "mỗi trang đúng một
 * `h1`" thành thứ khó phá hơn là thứ phải nhớ.
 *
 * `AuthLayout` vẫn tự render `h1` của nó — nó là layout của trang chưa đăng nhập,
 * không dùng khung trang này. Xem `PageContainer`.
 *
 * ## Breadcrumb KHÔNG ở trong này
 *
 * Spec 007 A5: trang lỗi và trang chưa đăng nhập cần đầu trang nhưng **không** có
 * breadcrumb. Gộp cứng thì hai trang đó nhận một slot rỗng và một khoảng trắng
 * không ai đặt. `Breadcrumb` là component riêng, và nó **chưa được dựng** vì
 * chưa có page nào ở cấp thứ hai — rule-of-two.
 */

defineProps<{
  title: string
  /** Câu giải thích ngắn dưới tiêu đề. Bỏ trống thì không render gì. */
  description?: string
  /**
   * Chế độ đồng hàng tuyệt đối chuẩn Datacake:
   * Tiêu đề và toàn bộ cụm actions nằm trên 1 đường thẳng duy nhất, không bao giờ gãy dòng.
   */
  singleLine?: boolean
}>()
</script>

<template>
  <header class="pagehead" :class="{ 'pagehead--single-line': singleLine }">
    <div class="pagehead__text">
      <h1 class="pagehead__title" :title="title">{{ title }}</h1>
      <p v-if="description" class="pagehead__desc">{{ description }}</p>
    </div>

    <!-- Vùng hành động: nút chính, bộ lọc. Chỉ render khi có nội dung — một div
         rỗng vẫn chiếm chỗ trong flex và đẩy tiêu đề lệch. -->
    <div v-if="$slots.actions" class="pagehead__actions">
      <slot name="actions" />
    </div>
  </header>
</template>

<style scoped>
.pagehead {
  container-type: inline-size;
  container-name: pagehead;
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
  align-items: flex-start;
  justify-content: space-between;
}

/* Chế độ Datacake: 1 hàng ngang duy nhất, tuyệt đối không gãy dòng */
.pagehead--single-line {
  flex-wrap: nowrap;
  align-items: center;
  min-width: 0;
}

.pagehead__text {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  /* Cho phép co lại để vùng hành động không bị đẩy xuống quá sớm. */
  min-width: 0;
  flex-shrink: 1;
}

.pagehead--single-line .pagehead__text {
  gap: 0;
  justify-content: center;
}

.pagehead__title {
  margin: 0;
  /* [ĐO 2026-09-05 dashboard Datacake] tiêu đề widget/trang 20px/600, màu
   * gray-800 #1e293b — đậm và tối hơn body text #4a4a4a (xem --text-heading). */
  font-size: 1.25rem;
  font-weight: 600;
  line-height: 1.25;
  letter-spacing: -0.01em;
  color: var(--text-heading);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.pagehead__desc {
  margin: 0;
  font-size: 0.8125rem;
  line-height: 1.5;
  color: var(--text-secondary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.pagehead__actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  align-items: center;
  flex-shrink: 0;
}

.pagehead--single-line .pagehead__actions {
  flex-wrap: nowrap;
}

@container pagehead (max-width: 680px) {
  .pagehead--single-line .pagehead__desc {
    display: none;
  }
}
</style>
