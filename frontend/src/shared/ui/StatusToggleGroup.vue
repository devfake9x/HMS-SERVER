<script setup lang="ts">
/**
 * Nhóm nút chọn trạng thái (Segmented Radio Group).
 * Chuẩn hóa cách biểu diễn trạng thái (Active / Suspended / ...) trong các form.
 *
 * ## Vấn đề nó giải quyết (từ Scope kiểm tra)
 * 1. Các form tự chế nút bấm (.area-form__status-cards, .gateway-form__status-cards):
 *    - Thiếu hoàn toàn accessibility (không role, không aria-checked, screen reader đọc là nút trơn).
 *    - Độ tương phản kém, chỉ có viền mỏng xanh, không có dot hoặc checkmark phân biệt trực quan.
 *    - Không hỗ trợ phím mũi tên theo WAI-ARIA Radio Group pattern.
 *    - Không xử lý disabled khi submitting.
 * 2. Đem lại sự đồng bộ 100% về UI và hành vi trên toàn bộ hệ thống.
 */
import { computed, ref } from 'vue'

export interface StatusToggleOption {
  readonly value: string
  readonly label: string
  readonly description?: string
  readonly tone?: 'success' | 'neutral' | 'warning' | 'info' | 'danger'
}

const props = withDefaults(
  defineProps<{
    modelValue: string
    options: readonly StatusToggleOption[]
    disabled?: boolean
    ariaLabel?: string
    name?: string
  }>(),
  {
    disabled: false,
    ariaLabel: 'Trạng thái',
    name: undefined,
  },
)

const emit = defineEmits<{
  'update:modelValue': [value: string]
}>()

const itemRefs = ref<HTMLElement[]>([])

function selectOption(val: string) {
  if (props.disabled || props.modelValue === val) return
  emit('update:modelValue', val)
}

function handleKeydown(e: KeyboardEvent, currentIndex: number) {
  if (props.disabled) return
  const len = props.options.length
  if (len <= 1) return

  let targetIndex = -1
  if (e.key === 'ArrowRight' || e.key === 'ArrowDown') {
    e.preventDefault()
    targetIndex = (currentIndex + 1) % len
  } else if (e.key === 'ArrowLeft' || e.key === 'ArrowUp') {
    e.preventDefault()
    targetIndex = (currentIndex - 1 + len) % len
  }

  if (targetIndex >= 0) {
    const nextVal = props.options[targetIndex]?.value
    if (nextVal !== undefined) {
      emit('update:modelValue', nextVal)
      itemRefs.value[targetIndex]?.focus()
    }
  }
}

function resolveDotTone(opt: StatusToggleOption): string {
  if (opt.tone) return opt.tone
  if (opt.value === 'active' || opt.value === 'online') return 'success'
  if (opt.value === 'suspended' || opt.value === 'inactive' || opt.value === 'offline') return 'neutral'
  if (opt.value === 'warning' || opt.value === 'maintenance') return 'warning'
  return 'info'
}

const selectedOption = computed(() => props.options.find((opt) => opt.value === props.modelValue))
</script>

<template>
  <div
    class="status-toggle-group"
    role="radiogroup"
    :aria-label="ariaLabel"
    :aria-disabled="disabled ? 'true' : undefined"
  >
    <button
      v-for="(opt, index) in options"
      :key="opt.value"
      ref="itemRefs"
      type="button"
      role="radio"
      class="status-toggle-group__item"
      :class="{
        'status-toggle-group__item--selected': modelValue === opt.value,
        'status-toggle-group__item--disabled': disabled,
      }"
      :aria-checked="modelValue === opt.value ? 'true' : 'false'"
      :tabindex="modelValue === opt.value ? 0 : -1"
      :disabled="disabled"
      @click="selectOption(opt.value)"
      @keydown="handleKeydown($event, index)"
    >
      <span
        class="status-toggle-group__dot"
        :class="`status-toggle-group__dot--${resolveDotTone(opt)}`"
        aria-hidden="true"
      />
      <span class="status-toggle-group__label">{{ opt.label }}</span>
      <span
        v-if="modelValue === opt.value"
        class="status-toggle-group__check"
        aria-hidden="true"
      >
        ✓
      </span>
    </button>
    <input
      type="text"
      class="status-toggle-group__hidden-input sr-only"
      :value="selectedOption?.label ?? modelValue"
      readonly
      tabindex="-1"
      aria-hidden="true"
    />
  </div>
</template>

<style scoped>
.status-toggle-group {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 0.5rem;
  width: 100%;
}

.status-toggle-group__item {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 0.5rem;
  padding: 0.5rem 0.75rem;
  min-height: 2.25rem;
  border-radius: 0.375rem;
  border: 1px solid var(--border-subtle);
  background: var(--surface-panel);
  color: var(--text-secondary);
  font-size: 0.8125rem;
  font-weight: 500;
  cursor: pointer;
  text-align: center;
  transition: all 120ms ease;
  user-select: none;
}

.status-toggle-group__item:hover:not(:disabled) {
  border-color: var(--border-hover, #94a3b8);
  background: var(--surface-hover, var(--surface-overlay));
  color: var(--text-primary);
}

.status-toggle-group__item:focus-visible {
  outline: 2px solid var(--status-info);
  outline-offset: 1px;
}

.status-toggle-group__item--selected {
  border-color: var(--status-info);
  border-width: 1.5px;
  background: var(--surface-overlay);
  color: var(--text-primary);
  font-weight: 600;
}

.status-toggle-group__item--disabled {
  opacity: 0.5;
  cursor: default;
}

.status-toggle-group__item--disabled.status-toggle-group__item--selected {
  opacity: 0.95;
  cursor: default;
  background: var(--surface-overlay);
  border-color: var(--status-info);
  color: var(--text-primary);
}

.status-toggle-group__dot {
  display: inline-block;
  width: 0.5rem;
  height: 0.5rem;
  border-radius: 9999px;
  flex: none;
}

.status-toggle-group__dot--success {
  background-color: var(--status-success-dot, var(--status-success));
}

.status-toggle-group__dot--neutral {
  background-color: var(--status-neutral-dot, var(--status-neutral));
}

.status-toggle-group__dot--warning {
  background-color: var(--status-warning-dot, var(--status-warning));
}

.status-toggle-group__dot--info {
  background-color: var(--status-info);
}

.status-toggle-group__dot--danger {
  background-color: var(--status-danger-dot, var(--status-danger));
}

.status-toggle-group__label {
  white-space: nowrap;
}

.status-toggle-group__check {
  margin-left: 0.25rem;
  font-size: 0.75rem;
  color: var(--status-info);
  font-weight: bold;
}
</style>
