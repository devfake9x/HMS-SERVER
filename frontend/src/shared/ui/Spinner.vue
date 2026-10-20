<script setup lang="ts">
/**
 * Chỉ báo đang tải.
 *
 * File phẳng, không có folder — Base architecture §2.3 rule of two: chỉ tạo
 * folder khi có file thứ hai cùng chủ đề.
 *
 * Hai điểm về khả năng truy cập:
 *   - `role="status"` + chữ `.sr-only`: một hình xoay không nói được gì với
 *     screen reader. Không có nhãn chữ thì người dùng screen reader thấy trang
 *     im lặng và không biết là đang tải hay đã xong mà rỗng.
 *   - `prefers-reduced-motion` thì **làm chậm, không dừng**. Spinner dừng hẳn
 *     là spinner vô nghĩa. Xem style bên dưới.
 */

withDefaults(
  defineProps<{
    size?: 'sm' | 'md' | 'lg'
    /** Nhãn cho screen reader. Đặt rỗng khi phần tử cha đã có `aria-busy`. */
    label?: string
  }>(),
  {
    size: 'md',
    label: 'Đang tải',
  },
)
</script>

<template>
  <span class="spinner" :class="`spinner--${size}`" role="status">
    <svg viewBox="0 0 24 24" fill="none" aria-hidden="true">
      <circle class="spinner__track" cx="12" cy="12" r="9" stroke="currentColor" stroke-width="2.5" />
      <path
        class="spinner__head"
        d="M21 12a9 9 0 0 0-9-9"
        stroke="currentColor"
        stroke-width="2.5"
        stroke-linecap="round"
      />
    </svg>
    <span v-if="label" class="sr-only">{{ label }}</span>
  </span>
</template>

<style scoped>
.spinner {
  display: inline-flex;
  flex: none;
  color: currentcolor;
}

.spinner svg {
  width: 100%;
  height: 100%;
  animation: spinner-rotate 800ms linear infinite;
}

.spinner--sm {
  width: 0.875rem;
  height: 0.875rem;
}
.spinner--md {
  width: 1.125rem;
  height: 1.125rem;
}
.spinner--lg {
  width: 1.75rem;
  height: 1.75rem;
}

.spinner__track {
  opacity: 0.22;
}

@keyframes spinner-rotate {
  to {
    transform: rotate(360deg);
  }
}

/* Làm CHẬM, không dừng: spinner đứng im không còn là chỉ báo tải. */
@media (prefers-reduced-motion: reduce) {
  .spinner svg {
    animation-duration: 2.4s;
  }
}
</style>
