<script setup lang="ts">
/**
 * Ô chọn một giá trị đa dạng (Select / Listbox / Combobox) chuẩn phong cách Datacake.
 *
 * ## Biến thể hỗ trợ (Variants)
 *
 *   - `variant="listbox"` (mặc định): Custom Listbox chuẩn Datacake với icon 2 chiều `↕`,
 *     menu dropdown có bóng mờ, viền 1px, thanh cuộn mỏng, mục đang chọn in đậm kem dau
 *     tich xanh `✓` o ben phai, va mau hover xanh nhat chuan Datacake (--surface-select-hover).
 *   - `variant="compact"`: Dropdown thu gon dung cho phan trang hoac thanh cong cu,
 *     dung icon mui ten xuong `⌄`.
 *   - `searchable`: Bo sung o tim kiem loc danh sach nhanh khi so luong option nhieu.
 *   - `native`: Fallback sang `<select>` truyen thong khi can tuong thich toi da.
 *
 * ## Kha nang truy cap (Accessibility)
 *
 *   - `role="combobox"` tren trigger va `role="listbox"` tren panel menu.
 *   - Ho tro day du ban phim: ArrowDown, ArrowUp, Enter, Space, Escape, Home, End.
 *   - Click-outside tu dong dong menu.
 */
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'


export type SelectOption = {
  value: string | null
  label: string
  disabled?: boolean
  dot?: 'success' | 'danger' | 'warning' | 'neutral' | 'info' | string
  description?: string
}

const props = withDefaults(
  defineProps<{
    options: readonly SelectOption[]
    size?: 'sm' | 'md'
    variant?: 'listbox' | 'compact'
    searchable?: boolean
    native?: boolean
    disabled?: boolean
    placeholder?: string
    id?: string
    align?: 'left' | 'right'
  }>(),
  {
    size: 'md',
    variant: 'listbox',
    searchable: false,
    native: false,
    disabled: false,
    placeholder: 'Chọn một mục…',
    id: undefined,
    align: 'left',
  },
)

const model = defineModel<string | null>({ default: null })

defineOptions({
  name: 'Select',
  inheritAttrs: false,
})

const isOpen = ref(false)
const searchQuery = ref('')
const searchInputRef = ref<HTMLInputElement | null>(null)
const rootRef = ref<HTMLElement | null>(null)
const triggerRef = ref<HTMLButtonElement | null>(null)
const listboxRef = ref<HTMLElement | null>(null)
const activeIndex = ref(-1)

const filteredOptions = computed(() => {
  if (!props.searchable || !searchQuery.value.trim()) {
    return props.options
  }
  const q = searchQuery.value.toLowerCase().trim()
  return props.options.filter((opt) => opt.label.toLowerCase().includes(q))
})

const selectedOption = computed(() => {
  return props.options.find((opt) => opt.value === model.value)
})

const displayLabel = computed(() => {
  if (selectedOption.value) {
    return selectedOption.value.label
  }
  return props.placeholder
})

const domNativeValue = computed<string>({
  get: () => model.value ?? '',
  set: (val: string) => {
    model.value = val === '' ? null : val
  },
})

function toggleDropdown(): void {
  if (props.disabled || props.native) return
  if (isOpen.value) {
    closeDropdown()
  } else {
    openDropdown()
  }
}

function openDropdown(): void {
  if (props.disabled || props.native) return
  isOpen.value = true
  searchQuery.value = ''
  const selectedIdx = filteredOptions.value.findIndex((opt) => opt.value === model.value)
  activeIndex.value = selectedIdx >= 0 ? selectedIdx : 0

  nextTick(() => {
    if (props.searchable && searchInputRef.value) {
      searchInputRef.value.focus()
    }
    scrollToActive()
  })
}

function closeDropdown(focusTrigger = true): void {
  isOpen.value = false
  activeIndex.value = -1
  searchQuery.value = ''
  if (focusTrigger && triggerRef.value) {
    triggerRef.value.focus()
  }
}

function selectOption(opt: SelectOption): void {
  if (opt.disabled) return
  model.value = opt.value
  closeDropdown(true)
}

function scrollToActive(): void {
  if (!listboxRef.value || activeIndex.value < 0) return
  const activeEl = listboxRef.value.children[activeIndex.value] as HTMLElement | undefined
  if (activeEl && typeof activeEl.scrollIntoView === 'function') {
    activeEl.scrollIntoView({ block: 'nearest' })
  }
}

function onKeyDown(e: KeyboardEvent): void {
  if (props.disabled || props.native) return

  if (!isOpen.value) {
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp' || e.key === ' ' || e.key === 'Enter') {
      e.preventDefault()
      openDropdown()
    }
    return
  }

  switch (e.key) {
    case 'ArrowDown': {
      e.preventDefault()
      let next = activeIndex.value + 1
      while (next < filteredOptions.value.length && filteredOptions.value[next]?.disabled) {
        next++
      }
      if (next < filteredOptions.value.length) {
        activeIndex.value = next
        scrollToActive()
      }
      break
    }
    case 'ArrowUp': {
      e.preventDefault()
      let prev = activeIndex.value - 1
      while (prev >= 0 && filteredOptions.value[prev]?.disabled) {
        prev--
      }
      if (prev >= 0) {
        activeIndex.value = prev
        scrollToActive()
      }
      break
    }
    case 'Home': {
      e.preventDefault()
      const first = filteredOptions.value.findIndex((opt) => !opt.disabled)
      if (first >= 0) {
        activeIndex.value = first
        scrollToActive()
      }
      break
    }
    case 'End': {
      e.preventDefault()
      for (let i = filteredOptions.value.length - 1; i >= 0; i--) {
        if (!filteredOptions.value[i]?.disabled) {
          activeIndex.value = i
          scrollToActive()
          break
        }
      }
      break
    }
    case 'Enter':
    case ' ': {
      // Khi dang go search input, phim Space khong dong menu
      if (e.key === ' ' && e.target === searchInputRef.value) {
        return
      }
      e.preventDefault()
      if (activeIndex.value >= 0 && activeIndex.value < filteredOptions.value.length) {
        const opt = filteredOptions.value[activeIndex.value]
        if (opt && !opt.disabled) {
          selectOption(opt)
        }
      }
      break
    }
    case 'Escape': {
      e.preventDefault()
      closeDropdown(true)
      break
    }
    case 'Tab': {
      closeDropdown(false)
      break
    }
  }
}

function onDocumentPointerDown(e: PointerEvent): void {
  if (!isOpen.value || !rootRef.value) return
  if (!rootRef.value.contains(e.target as Node)) {
    closeDropdown(false)
  }
}

watch(
  () => props.disabled,
  (isDisabled) => {
    if (isDisabled && isOpen.value) {
      closeDropdown(false)
    }
  },
)

onMounted(() => {
  document.addEventListener('pointerdown', onDocumentPointerDown)
})

onBeforeUnmount(() => {
  document.removeEventListener('pointerdown', onDocumentPointerDown)
})
</script>

<template>
  <select
    v-if="native"
    :id="id"
    v-model="domNativeValue"
    class="custom-select__native"
    :class="`custom-select__native--${size}`"
    :disabled="disabled"
    v-bind="$attrs"
  >
    <option
      v-for="opt in options"
      :key="opt.value ?? ''"
      :value="opt.value ?? ''"
      :disabled="opt.disabled"
    >
      {{ opt.label }}
    </option>
  </select>

  <div
    v-else
    ref="rootRef"
    class="custom-select"
    :class="[`custom-select--${variant}`, { 'custom-select--disabled': disabled }]"
  >
    <button
      :id="id"
      ref="triggerRef"
      type="button"
      class="custom-select__trigger"
      :class="[
        `custom-select__trigger--${size}`,
        `custom-select__trigger--${variant}`,
        { 'custom-select__trigger--open': isOpen },
      ]"
      :disabled="disabled"
      role="combobox"
      aria-haspopup="listbox"
      :aria-expanded="isOpen"
      v-bind="$attrs"
      @click="toggleDropdown"
      @keydown="onKeyDown"
    >
      <span class="custom-select__trigger-content">
        <span
          v-if="selectedOption?.dot"
          class="custom-select__dot"
          :class="`custom-select__dot--${selectedOption.dot}`"
          aria-hidden="true"
        />
        <span
          class="custom-select__label"
          :class="{ 'custom-select__label--placeholder': !selectedOption }"
        >
          {{ displayLabel }}
        </span>
      </span>

      <!-- Icon mũi tên 2 chiều (selector) cho listbox hoặc mũi tên xuống cho compact -->
      <span class="custom-select__icon-wrap">
        <svg
          v-if="variant === 'compact'"
          class="custom-select__icon custom-select__icon--chevron"
          viewBox="0 0 20 20"
          fill="currentColor"
          aria-hidden="true"
        >
          <path
            fill-rule="evenodd"
            d="M5.293 7.293a1 1 0 011.414 0L10 10.586l3.293-3.293a1 1 0 111.414 1.414l-4 4a1 1 0 01-1.414 0l-4-4a1 1 0 010-1.414z"
            clip-rule="evenodd"
          />
        </svg>

        <svg
          v-else
          class="custom-select__icon custom-select__icon--selector"
          viewBox="0 0 20 20"
          fill="currentColor"
          aria-hidden="true"
        >
          <path
            fill-rule="evenodd"
            d="M10 3a.75.75 0 01.55.24l3.25 3.5a.75.75 0 11-1.1 1.02L10 4.852 7.3 7.76a.75.75 0 01-1.1-1.02l3.25-3.5A.75.75 0 0110 3zm-3.76 9.2a.75.75 0 011.06.04l2.7 2.908 2.7-2.908a.75.75 0 111.1 1.02l-3.25 3.5a.75.75 0 01-1.1 0l-3.25-3.5a.75.75 0 01.04-1.06z"
            clip-rule="evenodd"
          />
        </svg>
      </span>
    </button>
    <input
      type="text"
      class="custom-select__hidden-input sr-only"
      :value="displayLabel"
      readonly
      tabindex="-1"
      aria-hidden="true"
    />

    <!-- Menu Panel Dropdown -->
    <div
      v-if="isOpen"
      class="custom-select__panel"
      :class="[`custom-select__panel--${variant}`, `custom-select__panel--align-${align}`]"
    >
      <!-- Ô tìm kiếm khi bật searchable -->
      <div v-if="searchable" class="custom-select__search-wrap">
        <svg
          class="custom-select__search-icon"
          viewBox="0 0 20 20"
          fill="currentColor"
          aria-hidden="true"
        >
          <path
            fill-rule="evenodd"
            d="M9 3.5a5.5 5.5 0 1 0 0 11 5.5 5.5 0 0 0 0-11ZM2 9a7 7 0 1 1 12.452 4.391l3.328 3.329a.75.75 0 1 1-1.06 1.06l-3.329-3.328A7 7 0 0 1 2 9Z"
            clip-rule="evenodd"
          />
        </svg>
        <input
          ref="searchInputRef"
          v-model="searchQuery"
          type="text"
          class="custom-select__search-input"
          :class="{ 'custom-select__search-input--has-clear': Boolean(searchQuery) }"
          placeholder="Tìm kiếm…"
          autocomplete="off"
          @keydown="onKeyDown"
        />
        <button
          v-if="Boolean(searchQuery)"
          type="button"
          class="custom-select__clear-btn"
          aria-label="Xóa tìm kiếm"
          title="Xóa tìm kiếm"
          @click.stop="searchQuery = ''; searchInputRef?.focus()"
        >
          <svg
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            stroke-linecap="round"
            stroke-linejoin="round"
            aria-hidden="true"
          >
            <line x1="18" y1="6" x2="6" y2="18" />
            <line x1="6" y1="6" x2="18" y2="18" />
          </svg>
        </button>
      </div>

      <!-- Danh sách Options -->
      <div
        ref="listboxRef"
        class="custom-select__menu"
        role="listbox"
        tabindex="-1"
      >
        <div
          v-if="filteredOptions.length === 0"
          class="custom-select__empty"
        >
          Không tìm thấy kết quả
        </div>

        <div
          v-for="(opt, idx) in filteredOptions"
          :key="opt.value ?? `null-opt-${idx}`"
          class="custom-select__option"
          :class="{
            'custom-select__option--selected': opt.value === model,
            'custom-select__option--active': idx === activeIndex,
            'custom-select__option--disabled': opt.disabled,
          }"
          role="option"
          :aria-selected="opt.value === model"
          :aria-disabled="opt.disabled"
          @mouseenter="!opt.disabled && (activeIndex = idx)"
          @click="selectOption(opt)"
        >
          <span class="custom-select__option-content">
            <span
              v-if="opt.dot"
              class="custom-select__dot"
              :class="`custom-select__dot--${opt.dot}`"
              aria-hidden="true"
            />
            <span class="custom-select__option-text">
              {{ opt.label }}
            </span>
          </span>

          <!-- Ghi chú / Tóm tắt phụ đặt ở cuối dòng để giữ cân đối -->
          <span
            v-if="opt.description"
            class="custom-select__option-desc"
            :title="opt.description"
          >
            {{ opt.description }}
          </span>

          <!-- Checkmark SVG màu xanh chuẩn Datacake -->
          <span
            v-if="opt.value === model"
            class="custom-select__check-wrap"
          >
            <svg
              class="custom-select__check"
              viewBox="0 0 20 20"
              fill="currentColor"
              aria-hidden="true"
            >
              <path
                fill-rule="evenodd"
                d="M16.707 5.293a1 1 0 010 1.414l-8 8a1 1 0 01-1.414 0l-4-4a1 1 0 011.414-1.414L8 12.586l7.293-7.293a1 1 0 011.414 0z"
                clip-rule="evenodd"
              />
            </svg>
          </span>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
/* Fallback Native */
.custom-select__native {
  appearance: none;
  -webkit-appearance: none;
  -moz-appearance: none;
  -webkit-print-color-adjust: exact;
  print-color-adjust: exact;
  background-image: url("data:image/svg+xml,%3csvg xmlns='http://www.w3.org/2000/svg' fill='none' viewBox='0 0 20 20'%3e%3cpath stroke='%236b7280' stroke-linecap='round' stroke-linejoin='round' stroke-width='1.5' d='M6 8l4 4 4-4'/%3e%3c/svg%3e");
  background-position: right 0.625rem center;
  background-repeat: no-repeat;
  background-size: 1.25em 1.25em;
  width: 100%;
  border: 1px solid var(--border-subtle, #e2e8f0);
  border-radius: 0.375rem;
  background-color: var(--surface-panel);
  color: var(--text-secondary);
  font-family: inherit;
  line-height: 1.43;
  box-shadow: var(--shadow-control);
  cursor: pointer;
  outline: none;
  transition: border-color 120ms ease, box-shadow 120ms ease;
}

.custom-select__native:hover:not(:disabled) {
  border-color: #cbd5e1;
}

.custom-select__native:focus,
.custom-select__native:focus-visible {
  outline: none;
  border-color: var(--input-border-focus, #3b82f6);
  box-shadow: var(--input-focus-ring, 0 0 0 2px rgba(59, 130, 246, 0.15));
}

.custom-select__native:disabled {
  cursor: not-allowed;
  opacity: 0.55;
  background-color: var(--surface-overlay, #e5e7eb);
}

.custom-select__native--sm {
  height: 32px;
  padding: 0.25rem 2rem 0.25rem 0.5rem;
  font-size: 0.8125rem;
  line-height: 1.35;
}

.custom-select__native--md {
  height: 38px;
  padding: 0.5rem 2.25rem 0.5rem 0.75rem;
  font-size: 0.875rem;
  line-height: 1.43;
}


/* Custom Select Container */
.custom-select {
  position: relative;
  width: 100%;
}

.custom-select--disabled {
  cursor: not-allowed;
}

/* Trigger Button */
.custom-select__trigger {
  display: flex;
  align-items: center;
  justify-content: space-between;
  width: 100%;
  gap: 0.5rem;
  border: 1px solid var(--border-subtle, #e2e8f0);
  border-radius: 0.375rem;
  background-color: var(--surface-panel);
  color: var(--text-secondary);
  font-family: inherit;
  font-weight: 400;
  text-align: left;
  line-height: 1.43;
  box-shadow: var(--shadow-control, rgb(0 0 0 / 5%) 0 1px 2px 0);
  cursor: pointer;
  transition: border-color 120ms ease, box-shadow 120ms ease;
}

.custom-select__trigger:hover:not(:disabled) {
  border-color: #cbd5e1;
}

.custom-select__trigger:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.custom-select__trigger--sm {
  padding: 0.35rem 0.5rem;
  font-size: 0.8125rem;
  min-height: 2rem;
}

.custom-select__trigger--md {
  padding: 0.5rem 0.75rem;
  font-size: 0.875rem;
  min-height: 2.375rem;
}

.custom-select__trigger:focus-visible,
.custom-select__trigger--open {
  outline: none;
  border-color: var(--input-border-focus, #3b82f6);
  box-shadow: var(--input-focus-ring, 0 0 0 2px rgba(59, 130, 246, 0.15));
}

.custom-select__trigger[aria-invalid='true'] {
  border-color: var(--status-danger);
}

.custom-select__trigger[aria-invalid='true']:focus-visible {
  outline: none;
  border-color: var(--status-danger);
  box-shadow: var(--input-focus-ring-danger, 0 0 0 2px rgba(239, 68, 68, 0.18));
}

.custom-select__trigger-content,
.custom-select__option-content {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  min-width: 0;
  overflow: hidden;
}

.custom-select__dot {
  display: inline-block;
  width: 0.5rem;
  height: 0.5rem;
  border-radius: 9999px;
  flex: none;
}

.custom-select__dot--success,
.custom-select__dot--green {
  background-color: var(--status-success-dot, var(--status-success));
}

.custom-select__dot--danger,
.custom-select__dot--red {
  background-color: var(--status-danger-dot, var(--status-danger));
}

.custom-select__dot--warning,
.custom-select__dot--yellow {
  background-color: var(--status-warning-dot, var(--status-warning));
}

.custom-select__dot--neutral,
.custom-select__dot--gray {
  background-color: var(--status-neutral-dot, var(--status-neutral));
}

.custom-select__dot--info,
.custom-select__dot--blue {
  background-color: var(--status-info);
}

.custom-select__label {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.custom-select__label--placeholder {
  color: var(--text-hint);
}

.custom-select__icon-wrap {
  display: flex;
  align-items: center;
  flex: none;
}

.custom-select__icon {
  width: 1.125rem;
  height: 1.125rem;
  flex: none;
  color: var(--text-secondary);
}

/* Dropdown Panel */
.custom-select__panel {
  position: absolute;
  top: calc(100% + 4px);
  left: 0;
  z-index: 50;
  min-width: 100%;
  width: max-content;
  max-width: min(520px, 90vw);
  border: 1px solid var(--border-default);
  border-radius: 0.375rem;
  background-color: var(--surface-panel);
  box-shadow: 0 10px 15px -3px rgb(0 0 0 / 0.1), 0 4px 6px -4px rgb(0 0 0 / 0.1);
  overflow: hidden;
}

.custom-select__panel--align-right {
  left: auto;
  right: 0;
}

/* Search wrap */
.custom-select__search-wrap {
  position: relative;
  padding: 0.5rem;
  border-bottom: 1px solid var(--border-default);
}

.custom-select__search-icon {
  position: absolute;
  top: 50%;
  left: 0.875rem;
  width: 0.875rem;
  height: 0.875rem;
  transform: translateY(-50%);
  color: var(--text-secondary);
  pointer-events: none;
}

.custom-select__search-input {
  width: 100%;
  padding: 0.35rem 0.5rem 0.35rem 1.75rem;
  border: 1px solid var(--border-subtle);
  border-radius: 0.25rem;
  background-color: var(--surface-panel);
  color: var(--text-secondary);
  font-family: inherit;
  font-size: 0.8125rem;
  outline: none;
}

.custom-select__search-input:focus {
  border-color: var(--input-border-focus, #3b82f6);
  box-shadow: var(--input-focus-ring, 0 0 0 2px rgba(59, 130, 246, 0.18));
}

.custom-select__search-input--has-clear {
  padding-right: 1.75rem;
}

.custom-select__clear-btn {
  position: absolute;
  top: 50%;
  right: 0.75rem;
  transform: translateY(-50%);
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 2px;
  border: none;
  background: transparent;
  color: var(--text-secondary);
  cursor: pointer;
  border-radius: 0.25rem;
  transition: color 120ms ease;
}

.custom-select__clear-btn:hover {
  color: var(--text-primary);
}

.custom-select__clear-btn svg {
  width: 0.75rem;
  height: 0.75rem;
}

/* Options Menu */
.custom-select__menu {
  max-height: 15rem;
  overflow-y: auto;
  overflow-x: hidden;
  padding: 0.25rem 0;
  scrollbar-width: thin;
  scrollbar-color: var(--border-subtle) transparent;
}

.custom-select__menu::-webkit-scrollbar {
  width: 6px;
}

.custom-select__menu::-webkit-scrollbar-track {
  background: transparent;
}

.custom-select__menu::-webkit-scrollbar-thumb {
  background-color: var(--border-subtle);
  border-radius: 9999px;
}

.custom-select__empty {
  padding: 0.75rem;
  font-size: 0.8125rem;
  text-align: center;
  color: var(--text-secondary);
}

.custom-select__option {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.5rem;
  padding: 0.5rem 0.75rem;
  font-size: 0.875rem;
  line-height: 1.25rem;
  color: var(--text-secondary);
  cursor: pointer;
  user-select: none;
  transition: background-color 75ms ease;
}

/* Màu hover chuẩn Datacake (blue-200 / surface-select-hover) */
.custom-select__option--active {
  background-color: var(--surface-select-hover);
}

.custom-select__option--selected {
  font-weight: 600;
}

.custom-select__option--disabled {
  opacity: 0.4;
  cursor: not-allowed;
}

.custom-select__option-text {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.custom-select__option-content {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  min-width: 0;
  flex: 1;
}

.custom-select__option-desc {
  margin-left: auto;
  padding-left: 0.75rem;
  font-size: 0.75rem;
  font-weight: 400;
  color: var(--text-muted);
  white-space: nowrap;
  text-overflow: ellipsis;
  overflow: hidden;
  max-width: 220px;
  flex-shrink: 0;
}

.custom-select__check-wrap {
  display: flex;
  align-items: center;
  flex: none;
  margin-left: 0.5rem;
}

.custom-select__check {
  width: 1.25rem;
  height: 1.25rem;
  flex: none;
  color: var(--status-info);
}
</style>
