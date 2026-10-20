<script setup lang="ts">
/**
 * Component ô nhập ngày chuẩn hoá (DateInput).
 *
 * Chuẩn Datacake IoT:
 * - Chiều cao chuẩn 38px (size: 'md') đồng bộ hoàn hảo với SearchInput và Select.
 * - Hỗ trợ nhãn prefix nội tuyến ("Từ:", "Đến:").
 * - Viền focus kích hoạt thông qua :focus-within với viền xanh var(--input-border-focus).
 * - Hoàn toàn không chứa logic nghiệp vụ để tuân thủ bất biến I2 của shared/ui.
 */

interface Props {
  modelValue?: string | number | null
  label?: string
  placeholder?: string
  ariaLabel?: string
  min?: string
  max?: string
  size?: 'sm' | 'md'
  disabled?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  modelValue: '',
  label: undefined,
  placeholder: undefined,
  ariaLabel: undefined,
  min: undefined,
  max: undefined,
  size: 'md',
  disabled: false,
})

const emit = defineEmits<{
  'update:modelValue': [value: string]
  change: [value: string]
}>()

function onInput(e: Event): void {
  const val = (e.target as HTMLInputElement).value
  emit('update:modelValue', val)
}

function onChange(e: Event): void {
  const val = (e.target as HTMLInputElement).value
  emit('change', val)
}
</script>

<template>
  <div
    class="date-input"
    :class="[
      `date-input--${props.size}`,
      { 'date-input--disabled': props.disabled },
    ]"
  >
    <!-- Nhãn prefix hiển thị phía trước -->
    <span v-if="props.label" class="date-input__label" aria-hidden="true">
      {{ props.label }}
    </span>

    <!-- Biểu tượng lịch native -->
    <svg
      class="date-input__icon"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      stroke-width="1.75"
      stroke-linecap="round"
      stroke-linejoin="round"
      aria-hidden="true"
    >
      <rect x="3" y="4" width="18" height="18" rx="2" ry="2" />
      <line x1="16" y1="2" x2="16" y2="6" />
      <line x1="8" y1="2" x2="8" y2="6" />
      <line x1="3" y1="10" x2="21" y2="10" />
    </svg>

    <!-- Thẻ nhập ngày native HTML5 -->
    <input
      type="date"
      class="date-input__field"
      :value="props.modelValue ?? ''"
      :min="props.min || undefined"
      :max="props.max || undefined"
      :placeholder="props.placeholder"
      :disabled="props.disabled"
      :aria-label="props.ariaLabel || props.label || 'Chọn ngày'"
      @input="onInput"
      @change="onChange"
    />
  </div>
</template>

<style scoped>
.date-input {
  display: inline-flex;
  align-items: center;
  gap: 0.375rem;
  background-color: var(--surface-panel, #ffffff);
  border: 1px solid var(--border-control, #d1d5db);
  border-radius: 0.375rem;
  box-sizing: border-box;
  box-shadow: var(--shadow-control);
  transition: border-color 120ms ease, box-shadow 120ms ease;
  min-width: 0;
  width: 100%;
}

.date-input--md {
  height: 2.375rem; /* 38px chuẩn Datacake */
  padding: 0 0.625rem;
}

.date-input--sm {
  height: 2rem; /* 32px */
  padding: 0 0.5rem;
}

.date-input:focus-within {
  border-color: var(--input-border-focus, #3b82f6);
  box-shadow: var(--input-focus-ring, 0 0 0 2px rgba(59, 130, 246, 0.18));
}

.date-input--disabled {
  background-color: var(--surface-control-disabled, #f9fafb);
  opacity: 0.6;
  cursor: not-allowed;
}

.date-input__label {
  font-size: 0.8125rem;
  font-weight: 500;
  color: var(--text-secondary, #6b7280);
  white-space: nowrap;
  user-select: none;
  flex-shrink: 0;
}

.date-input__icon {
  width: 1rem;
  height: 1rem;
  color: var(--text-secondary, #9ca3af);
  flex-shrink: 0;
}

.date-input__field {
  border: none;
  background: transparent;
  color: var(--text-primary, #111827);
  font-family: inherit;
  font-size: 0.875rem;
  outline: none;
  width: 100%;
  min-width: 0;
  padding: 0;
  cursor: pointer;
}

.date-input--disabled .date-input__field {
  cursor: not-allowed;
}

/* Tinh chỉnh nút lịch picker indicator của WebKit */
.date-input__field::-webkit-calendar-picker-indicator {
  cursor: pointer;
  opacity: 0.55;
  filter: invert(0.3);
  transition: opacity 120ms ease;
}

.date-input__field::-webkit-calendar-picker-indicator:hover {
  opacity: 1;
}

@media (forced-colors: active) {
  .date-input:focus-within {
    outline: 2px solid Highlight;
    outline-offset: 1px;
  }
}
</style>
