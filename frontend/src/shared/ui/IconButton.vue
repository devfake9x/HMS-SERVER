<script setup lang="ts">
/**
 * Nút chỉ có icon.
 *
 * ## Vì sao là component riêng, không phải một prop của `Button`
 *
 * Nút chỉ-icon có **một yêu cầu bắt buộc** mà nút có chữ không có: nhãn cho screen
 * reader. Nếu đây là `<Button icon-only>` thì `label` phải thành prop tuỳ chọn của
 * `Button`, và một nút icon quên nhãn sẽ compile được — screen reader đọc ra "nút",
 * hết. Tách ra thì `label` là prop **bắt buộc** và trình biên dịch bắt được ca đó.
 *
 * Nhãn đi vào cả `aria-label` và `title`: `aria-label` cho screen reader,
 * `title` cho tooltip của trình duyệt với người dùng chuột không đoán được hình vẽ
 * nghĩa là gì. Cùng một chuỗi, nên chúng không lệch được.
 *
 * ## Vùng bấm 32×32 chứ không bằng kích thước icon
 *
 * Icon 16px nhưng vùng bấm 32px. WCAG 2.5.8 đòi tối thiểu 24×24; 32 cho biên an
 * toàn trên màn hình cảm ứng mà vẫn vừa một hàng bảng cao 44px.
 *
 * ## Cái nó KHÔNG làm
 *
 * Không có `variant`. Nút icon trong bảng luôn là dạng mờ; một nút icon màu đặc
 * giữa hàng dữ liệu hút mắt hơn chính dữ liệu. Cần nhấn mạnh thì dùng `Button` có
 * chữ.
 */
import { computed } from 'vue'

import { iconPath } from './icons'

const props = withDefaults(
  defineProps<{
    /** Tên icon trong `icons.ts`. Tên lạ thì không vẽ gì — không ném lỗi. */
    name: string
    /** Nhãn cho screen reader và tooltip. **Bắt buộc** — xem giải thích ở trên. */
    label: string
    disabled?: boolean
    /** Dùng cho hành động phá hoại (xoá): icon chuyển màu cảnh báo khi hover. */
    danger?: boolean
  }>(),
  { disabled: false, danger: false },
)

defineOptions({
  name: 'IconButton',
})

const path = computed(() => iconPath(props.name))
</script>

<template>
  <button
    type="button"
    class="icon-btn"
    :class="{ 'icon-btn--danger': danger }"
    :disabled="disabled"
    :aria-label="label"
    :title="label"
  >
    <svg
      v-if="path"
      class="icon-btn__svg"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      stroke-width="1.75"
      stroke-linecap="round"
      stroke-linejoin="round"
      aria-hidden="true"
    >
      <path :d="path" />
    </svg>
  </button>
</template>

<style scoped>
.icon-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  /* Vùng bấm 2rem = 32px. Xem giải thích ở đầu file. */
  inline-size: 2rem;
  block-size: 2rem;
  padding: 0;
  border: 1px solid transparent;
  border-radius: 0.375rem;
  background: transparent;
  color: var(--text-secondary);
  cursor: pointer;
  transition:
    background-color 120ms ease,
    color 120ms ease;
}

.icon-btn:hover:not(:disabled) {
  background-color: var(--surface-overlay);
  color: var(--text-primary);
}

.icon-btn--danger:hover:not(:disabled) {
  color: var(--status-danger);
}

.icon-btn:focus-visible {
  outline: 2px solid var(--status-info);
  outline-offset: 1px;
}

.icon-btn:disabled {
  cursor: not-allowed;
  opacity: 0.5;
}

.icon-btn__svg {
  inline-size: 1rem;
  block-size: 1rem;
}

@media (prefers-reduced-motion: reduce) {
  .icon-btn {
    transition: none;
  }
}
</style>
