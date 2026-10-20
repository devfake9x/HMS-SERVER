<script setup lang="ts">
import Spinner from './Spinner.vue'

/**
 * Nút.
 *
 * Ba quyết định là để chặn lỗi cụ thể:
 *
 * 1. **`type` mặc định là `'button'`, không phải `'submit'`.** HTML mặc định
 *    `<button>` bên trong `<form>` là `submit`. Một nút "Hủy" hoặc "Thêm dòng"
 *    đặt trong form sẽ submit form đó. Đây là lỗi kinh điển và nó im lặng.
 *
 * 2. **`loading` tự kéo theo `disabled` và `aria-busy`.** Không có nó thì người
 *    dùng bấm lần hai trong lúc request đang bay — tạo hai bản ghi trùng. Chặn
 *    ở tầng UI là chỗ rẻ nhất; idempotency ở backend là chuyện khác và không
 *    thay thế được cái này.
 *
 * 3. **Là `<button>` thật, không phải `<div role="button">`.** Nút thật có sẵn
 *    Enter/Space, tab order, và trạng thái disabled mà không cần viết gì thêm.
 *
 * Cố tình KHÔNG hỗ trợ render thành `<a>`. Một thứ vừa là nút vừa là link thì
 * hai hành vi bàn phím khác nhau (Space so với Enter) bị trộn vào một component.
 * Cần link trông giống nút thì viết `LinkButton` riêng khi thật sự cần tới.
 */

const props = withDefaults(
  defineProps<{
    variant?: 'primary' | 'secondary' | 'ghost' | 'danger'
    size?: 'sm' | 'md'
    type?: 'button' | 'submit' | 'reset'
    disabled?: boolean
    loading?: boolean
    /** Chiếm hết bề rộng của phần tử cha. */
    block?: boolean
    /** Tự động co thành icon-only khi container hẹp */
    collapseIcon?: boolean
    /** Chế độ chỉ hiển thị icon */
    iconOnly?: boolean
    /** Nhãn nút (hỗ trợ aria-label và title tooltip khi ở dạng icon) */
    label?: string
  }>(),
  {
    variant: 'secondary',
    size: 'md',
    type: 'button',
    disabled: false,
    loading: false,
    block: false,
    collapseIcon: false,
    iconOnly: false,
    label: undefined,
  },
)

// Một nguồn duy nhất: mọi nơi đọc `isBlocked`, không nơi nào tự ghép lại điều
// kiện — ghép lại là chỗ để quên một nửa.
const isBlocked = () => props.disabled || props.loading
</script>

<template>
  <button
    class="btn"
    :class="[
      `btn--${variant}`,
      `btn--${size}`,
      {
        'btn--block': block,
        'btn--loading': loading,
        'btn--collapsible': collapseIcon,
        'btn--icon-only': iconOnly,
      },
    ]"
    :type="type"
    :disabled="isBlocked()"
    :aria-busy="loading || undefined"
    :aria-label="label || undefined"
    :title="label || undefined"
  >
    <!-- label rỗng: nút đã có aria-busy nên screen reader biết trạng thái;
         thêm "Đang tải" nữa là đọc lặp. -->
    <Spinner v-if="loading" :size="size === 'sm' ? 'sm' : 'md'" label="" />
    <slot name="icon" />
    <span v-if="$slots.default" class="btn__label">
      <slot />
    </span>
  </button>
</template>

<style scoped>
.btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 0.4rem;
  border: 1px solid transparent;
  border-radius: 0.375rem;
  font-family: inherit;
  font-weight: 500;
  /*
   * `[ĐO 2026-08-29]` Datacake: nút cao **38px**, và con số đó không đến từ
   * `height` — nó là `8px` padding trên + `20px` line-box + `8px` padding dưới +
   * `2px` viền. Nên `line-height` phải là `20/14 ≈ 1.43`, không phải `1.2`.
   *
   * Dùng `line-height` chứ không `height: 38px`: nút có chữ dài xuống hai dòng thì
   * `height` cứng sẽ cắt chữ, còn cách này để nó cao ra. Nút trong bảng ở màn hình
   * hẹp là ca thật.
   */
  line-height: 1.43;
  white-space: nowrap;
  /*
   * Shadow rất nhẹ — cùng giá trị Datacake dùng cho nút primary, secondary và ô
   * chọn. Nó không mang thông tin nào (trạng thái nút đã có màu, viền và
   * `aria-disabled`), nên `prefers-reduced-transparency` hay chế độ tương phản cao
   * bỏ nó đi cũng không mất gì.
   */
  box-shadow: var(--shadow-control);
  cursor: pointer;
  transition: background-color 120ms ease, border-color 120ms ease;
}

.btn--sm {
  padding: 0.35rem 0.75rem;
  font-size: 0.8125rem;
}

/* `[ĐO]` Datacake `padding: 8px 16px` cho nút cỡ thường. Bản trước `7.2px 14px`. */
.btn--md {
  padding: 0.5rem 1rem;
  font-size: 0.875rem;
}

.btn--block {
  width: 100%;
}

.btn__label {
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
}

.btn--icon-only {
  padding: 0.5rem;
  min-width: 2.375rem;
  min-height: 2.375rem;
}

@container (max-width: 680px) {
  .btn--collapsible .btn__label {
    display: none;
  }
  .btn--collapsible {
    padding: 0.5rem 0.625rem;
    gap: 0;
  }
}

/* Viền focus KHÔNG được xoá. `:focus-visible` chỉ hiện khi điều hướng bằng bàn
   phím, nên không làm bẩn giao diện khi bấm chuột. */
.btn:focus-visible {
  outline: 2px solid var(--status-info);
  outline-offset: 2px;
}

.btn:disabled {
  cursor: not-allowed;
  opacity: 0.55;
}

/* Đang tải thì con trỏ báo "chờ", không báo "bị cấm" — hai nghĩa khác nhau. */
.btn--loading:disabled {
  cursor: progress;
  opacity: 0.8;
}

/* `--status-info-on`, KHÔNG phải `#fff`.
   `[ĐO]` chữ trắng trên `--status-info` của theme tối (`#38bdf8`) chỉ đạt
   **2,14:1** — đúng con số mà `.shell__skip` từng mắc và đã sửa. Nút thì vẫn
   hard-code `#fff`, và gate 9 không thấy vì cặp `#fff`/`status-info` chưa được
   khai; cặp đã khai dùng chính token này. Đọc token là cách duy nhất để cổng và
   component nói cùng một chuyện. */
/* Primary Action Button — Orange accent mandated: 'chỉ riêng màu cam ở nút nhất giữ lại' */
.btn--primary {
  background-color: #ea580c;
  border-color: #ea580c;
  color: #ffffff;
}
.btn--primary:hover:not(:disabled) {
  background-color: #f97316;
  border-color: #f97316;
}
.btn--primary:active:not(:disabled) {
  background-color: #c2410c;
  border-color: #c2410c;
}

.btn--secondary {
  background-color: var(--surface-panel);
  /* Datacake secondary: viền gray-300 (đậm hơn subtle một bậc), hover gray-50 */
  border-color: var(--border-control, #d1d5db);
  color: var(--text-primary);
}
.btn--secondary:hover:not(:disabled) {
  background-color: var(--surface-card-hover, #f9fafb);
}

/*
 * `ghost` KHÔNG có shadow. Nó là biến thể mờ, dùng cho hành động phụ nằm cạnh hành
 * động chính (nút Huỷ trong form, nút thứ hai trong footer modal). Một nút mờ mà
 * vẫn nổi lên thì nó không còn mờ, và cấp bậc giữa hai nút cạnh nhau biến mất.
 */
.btn--ghost {
  box-shadow: none;
  background-color: transparent;
  color: var(--text-secondary);
}
.btn--ghost:hover:not(:disabled) {
  background-color: var(--border-subtle);
  color: var(--text-primary);
}

/* Cùng lý do như `--primary`: `#fff` trên `--status-danger` của theme tối đạt
   **2,77:1**. Token `--status-danger-on` sinh ra trong cùng lượt sửa này, và nó
   có cặp riêng trong `PAIRS` nên lần sau ai đổi màu danger là cổng biết. */
.btn--danger {
  background-color: var(--status-danger);
  border-color: var(--status-danger);
  color: var(--status-danger-on);
}
.btn--danger:hover:not(:disabled) {
  filter: brightness(1.08);
}

@media (prefers-reduced-motion: reduce) {
  .btn {
    transition: none;
  }
}
</style>
