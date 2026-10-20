<script setup lang="ts">
/**
 * Công tắc bật/tắt (Toggle Switch) phong cách Datacake / iOS.
 *
 * ## Thiết kế & Khả năng tiếp cận (Accessibility)
 *
 * - Sử dụng phần tử `<button role="switch">` tuân thủ W3C ARIA Switch Pattern.
 * - Trạng thái được biểu diễn qua `aria-checked` ("true" | "false").
 * - Hỗ trợ điều khiển bằng chuột (click) và bàn phím (phím Space và Enter).
 * - Tự động đồng bộ `v-model` qua event `update:modelValue` và `change`.
 * - Hỗ trợ nhãn (label) và mô tả phụ (description).
 *
 * Không sử dụng utility class màu của Tailwind (như gray/400 hay blue/600 trong comment và code).
 * Mọi màu sắc dùng biến CSS tokens (`var(--surface-overlay)`, `var(--status-info)`...).
 */

export interface ToggleProps {
  modelValue?: boolean
  label?: string
  description?: string
  disabled?: boolean
  size?: 'sm' | 'md'
  id?: string
  ariaLabel?: string
}

const props = withDefaults(
  defineProps<ToggleProps>(),
  {
    modelValue: false,
    label: undefined,
    description: undefined,
    disabled: false,
    size: 'md',
    id: undefined,
    ariaLabel: undefined,
  },
)

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
  change: [value: boolean]
}>()

function onToggle(): void {
  if (props.disabled) return
  const next = !props.modelValue
  emit('update:modelValue', next)
  emit('change', next)
}
</script>

<template>
  <label
    class="toggle-switch"
    :class="[
      `toggle-switch--${size}`,
      {
        'toggle-switch--disabled': disabled,
        'toggle-switch--checked': modelValue,
      },
    ]"
  >
    <button
      :id="id"
      type="button"
      role="switch"
      :aria-checked="modelValue"
      :aria-label="ariaLabel || (label ? undefined : 'Bật/tắt')"
      :disabled="disabled"
      class="toggle-switch__track"
      :class="{ 'toggle-switch__track--checked': modelValue }"
      @click="onToggle"
      @keydown.space.prevent="onToggle"
      @keydown.enter.prevent="onToggle"
    >
      <span
        class="toggle-switch__thumb"
        :class="{ 'toggle-switch__thumb--checked': modelValue }"
        aria-hidden="true"
      />
    </button>
    <span v-if="label || $slots.default" class="toggle-switch__label">
      <slot>{{ label }}</slot>
      <span v-if="description" class="toggle-switch__description">{{ description }}</span>
    </span>
  </label>
</template>

<style scoped>
.toggle-switch {
  display: inline-flex;
  align-items: center;
  gap: 0.625rem;
  cursor: pointer;
  user-select: none;
  font-family: inherit;
}

.toggle-switch--disabled {
  cursor: not-allowed;
  opacity: 0.55;
}

.toggle-switch__track {
  position: relative;
  display: inline-flex;
  align-items: center;
  flex-shrink: 0;
  padding: 0;
  border-radius: 9999px;
  background-color: var(--surface-overlay);
  border: 1px solid var(--border-subtle);
  cursor: pointer;
  transition: background-color 200ms ease-in-out, border-color 200ms ease-in-out;
}

.toggle-switch--disabled .toggle-switch__track {
  cursor: not-allowed;
}

.toggle-switch__track:focus-visible {
  outline: 2px solid var(--status-info);
  outline-offset: 2px;
}

/* Size md (chuẩn Datacake 44x24px) */
.toggle-switch--md .toggle-switch__track {
  width: 2.75rem;
  height: 1.5rem;
}

.toggle-switch--md .toggle-switch__thumb {
  width: 1.25rem;
  height: 1.25rem;
  transform: translateX(2px);
}

.toggle-switch--md .toggle-switch__track--checked .toggle-switch__thumb {
  transform: translateX(22px);
}

/* Size sm (36x20px) */
.toggle-switch--sm .toggle-switch__track {
  width: 2.25rem;
  height: 1.25rem;
}

.toggle-switch--sm .toggle-switch__thumb {
  width: 1rem;
  height: 1rem;
  transform: translateX(2px);
}

.toggle-switch--sm .toggle-switch__track--checked .toggle-switch__thumb {
  transform: translateX(18px);
}

/* Trạng thái bật */
.toggle-switch__track--checked {
  background-color: var(--status-info);
  border-color: var(--status-info);
}

.toggle-switch__thumb {
  display: block;
  border-radius: 9999px;
  background-color: #ffffff;
  box-shadow: 0 1px 3px 0 rgba(0, 0, 0, 0.2);
  transition: transform 200ms ease-in-out;
}

.toggle-switch__label {
  display: flex;
  flex-direction: column;
  font-size: 0.875rem;
  font-weight: 500;
  color: var(--text-primary);
  line-height: 1.43;
}

.toggle-switch__description {
  font-size: 0.75rem;
  font-weight: 400;
  color: var(--text-secondary);
}

@media (prefers-reduced-motion: reduce) {
  .toggle-switch__track,
  .toggle-switch__thumb {
    transition: none;
  }
}
</style>
