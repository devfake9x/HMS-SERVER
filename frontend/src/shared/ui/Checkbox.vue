<script setup lang="ts">
/**
 * Ô đánh dấu — `<input type="checkbox">` thật, không giả lập bằng `div`.
 *
 * ## Vì sao KHÔNG dùng `Toggle.vue`
 *
 * `Toggle` là `<button role="switch">` theo W3C ARIA Switch Pattern. Switch và
 * checkbox khác nhau ở ngữ nghĩa, không phải ở hình dáng: switch bật/tắt một
 * trạng thái **có hiệu lực ngay**; checkbox chọn một phần tử trong tập hợp để
 * **hành động sau**. Chọn dòng trong bảng là loại thứ hai. Dùng switch cho nó làm
 * screen reader đọc sai bản chất thao tác.
 *
 * ## Vì sao bọc `<input>` thay vì tự vẽ
 *
 * Trình duyệt đã cho sẵn: Space bật/tắt, thuộc nhóm form, `:disabled` chặn tương
 * tác, và `indeterminate` — thứ không tự dựng lại được bằng `div` + `aria-checked`
 * nếu không viết lại toàn bộ điều khiển bàn phím.
 *
 * ## `indeterminate` là PROPERTY, không phải attribute
 *
 * `<input indeterminate>` trong template **không có tác dụng** — HTML không có
 * attribute đó. Nó chỉ tồn tại như property trên DOM node, nên phải set qua ref.
 * Đây là chỗ dễ xanh giả nhất của component này: một ca test assert prop của
 * component sẽ xanh cả khi ô vuông không bao giờ hiện dấu gạch. Ca phải đọc
 * `input.indeterminate` trên phần tử thật.
 *
 * ## Nhãn là prop BẮT BUỘC
 *
 * Cùng lý do `IconButton.label` bắt buộc: một ô vuông trần không có tên nào để
 * đọc. Ca dùng phổ biến nhất — cột chọn dòng trong bảng — lại chính là ca không
 * có nhãn nhìn thấy được, tức ca dễ quên nhất. Nên `label` bắt buộc và
 * `labelHidden` giấu nó về mặt thị giác, thay vì `label` tuỳ chọn: trình biên
 * dịch bắt được ca quên, còn một quy ước thì không.
 *
 * `labelHidden` đi cùng `.sr-only` của `app/styles/main.css` — cùng cơ chế
 * `VirtualColumn.headerSrOnly` đã dùng cho header cột.
 *
 * Màu qua CSS token (`var(--status-info)` cho dấu check/gạch ngang và focus ring,
 * `var(--border-checkbox)` cho viền, `var(--surface-card)` cho nền chuẩn Datacake),
 * không qua utility class — luật `no-utility-color`.
 */

import { ref, watchEffect } from 'vue'

export interface CheckboxProps {
  modelValue?: boolean
  /** Tên đọc được của ô. Bắt buộc — xem docblock. */
  label: string
  /** Giấu nhãn về mặt thị giác, vẫn để screen reader đọc. */
  labelHidden?: boolean
  /** Ô "chọn tất cả" khi mới chọn một phần. Xem docblock về property vs attribute. */
  indeterminate?: boolean
  disabled?: boolean
}

defineOptions({
  inheritAttrs: false,
})

const props = withDefaults(defineProps<CheckboxProps>(), {
  modelValue: false,
  labelHidden: false,
  indeterminate: false,
  disabled: false,
})

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
  change: [value: boolean]
}>()

const inputRef = ref<HTMLInputElement | null>(null)

// Phải set qua property. Xem docblock — đây không phải một lựa chọn phong cách.
//
// `flush: 'post'` là bắt buộc, không phải tuỳ chọn. Mặc định của `watchEffect` là
// `'pre'` — chạy TRƯỚC khi DOM cập nhật, mà lúc setup thì `inputRef` còn `null`,
// nên lượt chạy đầu không set được gì. `[ĐO]` với `'pre'`, ba ca của nhóm
// `indeterminate` đỏ với `expected false to be true`: ô vuông không có gạch ngang
// ở lần render đầu. `'post'` chạy sau khi template ref đã được gán.
watchEffect(
  () => {
    if (inputRef.value) {
      inputRef.value.indeterminate = props.indeterminate
    }
  },
  { flush: 'post' },
)

function onChange(event: Event): void {
  const next = (event.target as HTMLInputElement).checked
  emit('update:modelValue', next)
  emit('change', next)
}
</script>

<template>
  <label class="checkbox" :class="{ 'checkbox--disabled': disabled }">
    <input
      ref="inputRef"
      type="checkbox"
      class="checkbox__native"
      :checked="modelValue"
      :disabled="disabled"
      v-bind="$attrs"
      @change="onChange"
    />
    <span
      class="checkbox__box"
      :class="{
        'checkbox__box--checked': modelValue,
        'checkbox__box--indeterminate': indeterminate,
      }"
      aria-hidden="true"
    >
      <svg
        v-if="modelValue && !indeterminate"
        class="checkbox__icon"
        viewBox="0 0 20 20"
        fill="none"
      >
        <path
          d="M5 11L8 14L14.5 7.5"
          stroke="currentColor"
          stroke-width="2"
          stroke-linecap="round"
          stroke-linejoin="round"
        />
      </svg>
      <svg
        v-else-if="indeterminate"
        class="checkbox__icon"
        viewBox="0 0 20 20"
        fill="none"
      >
        <path
          d="M5 10H15"
          stroke="currentColor"
          stroke-width="2"
          stroke-linecap="round"
        />
      </svg>
    </span>
    <span :class="labelHidden ? 'sr-only' : 'checkbox__label'">{{ label }}</span>
  </label>
</template>

<style scoped>
.checkbox {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  cursor: pointer;
  user-select: none;
  position: relative;
}

.checkbox--disabled {
  cursor: not-allowed;
}

/* [ĐO 2 ảnh user so sánh editable vs disabled + source Datacake] Disabled
 * KHÔNG đổi màu — box vẫn trắng + tick xanh, CHỈ FADE (~45% opacity) toàn bộ
 * box. Cách cũ (nền xám cho ô chưa check, giữ đậm cho ô đã check) làm disabled
 * đã check nhìn không khác gì editable — đúng thứ user bắt lỗi. */
.checkbox--disabled .checkbox__box {
  cursor: not-allowed;
  opacity: 0.45;
}

.checkbox__native {
  position: absolute;
  opacity: 0;
  width: 0;
  height: 0;
  margin: 0;
  padding: 0;
  pointer-events: none;
}

/* [Thiết kế tinh tế] Viền mờ mờ (var(--border-subtle, #e2e8f0)), nền trắng, tick xanh #0284c7 */
.checkbox__box {
  inline-size: 1.25rem;
  block-size: 1.25rem;
  box-sizing: border-box;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border: 1px solid var(--border-subtle, #e2e8f0);
  border-radius: 0.25rem;
  background-color: var(--surface-card, #ffffff);
  color: var(--brand-primary, #0284c7);
  transition: background-color 120ms ease, border-color 120ms ease, box-shadow 120ms ease;
  flex-shrink: 0;
}

:root[data-theme='dark'] .checkbox__box,
html[data-theme='dark'] .checkbox__box {
  background-color: var(--surface-card, #18181b);
  border-color: var(--border-subtle, #27272a);
  color: var(--status-info, #38bdf8);
}

.checkbox:hover .checkbox__box:not(.checkbox--disabled *) {
  border-color: var(--border-control, #cbd5e1);
}

.checkbox__native:focus-visible + .checkbox__box {
  outline: none;
  box-shadow: 0 0 0 2px var(--surface-card), 0 0 0 4px rgba(2, 132, 199, 0.25);
}

.checkbox__icon {
  width: 100%;
  height: 100%;
  display: block;
}

.checkbox__label {
  font-size: 0.875rem;
  font-weight: 500;
  color: var(--text-primary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

/* Checked & Indeterminate: nền vẫn trắng tinh, viền mờ mờ, dấu tick xanh nổi bật */
.checkbox__box.checkbox__box--checked,
.checkbox__box.checkbox__box--indeterminate {
  background-color: var(--surface-card, #ffffff);
  border-color: var(--border-control, #cbd5e1);
  color: var(--brand-primary, #0284c7);
}

:root[data-theme='dark'] .checkbox__box.checkbox__box--checked,
html[data-theme='dark'] .checkbox__box.checkbox__box--checked,
:root[data-theme='dark'] .checkbox__box.checkbox__box--indeterminate,
html[data-theme='dark'] .checkbox__box.checkbox__box--indeterminate {
  background-color: var(--surface-card, #18181b);
  border-color: var(--border-control, #3f3f46);
  color: var(--status-info, #38bdf8);
}
</style>
