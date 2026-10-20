<script setup lang="ts">
/**
 * Combobox — combo giữa Select (dropdown) và Input (free text).
 *
 * User có thể chọn từ danh sách gợi ý HOẶC gõ tự do. Khi gõ, dropdown filter
 * theo ký tự đã nhập. Khi blur, giá trị user nhập được giữ nguyên bất kể nó
 * có nằm trong options hay không.
 *
 * Keyboard: ArrowDown/Up navigate, Enter chọn, Escape đóng dropdown.
 * Accessibility: role=combobox, aria-expanded, aria-activedescendant, role=listbox.
 */

import { ref, computed, watch, nextTick, useId } from 'vue'

const props = withDefaults(
  defineProps<{
    /** Danh sách gợi ý cho dropdown. */
    options: readonly { value: string; label: string }[]
    /** Kích cỡ input — ảnh hưởng padding và font-size. */
    size?: 'sm' | 'md'
    placeholder?: string
  }>(),
  {
    size: 'md',
    placeholder: undefined,
  },
)

const model = defineModel<string>({ default: '' })

/* ─── Internal state ────────────────────────────────────── */

const listboxId = `combobox-listbox-${useId()}`

const isOpen = ref(false)
const activeIndex = ref(-1)
const inputRef = ref<HTMLInputElement | null>(null)
const listRef = ref<HTMLUListElement | null>(null)

/* ─── Filtered options ──────────────────────────────────── */

const filteredOptions = computed(() => {
  const query = model.value.toLowerCase().trim()
  if (!query) return props.options
  return props.options.filter((o) =>
    o.label.toLowerCase().includes(query) || o.value.toLowerCase().includes(query),
  )
})

/* Reset activeIndex khi danh sách filter thay đổi */
watch(filteredOptions, () => {
  activeIndex.value = -1
})

/* ─── Computed helpers ──────────────────────────────────── */

const activeDescendantId = computed(() => {
  if (activeIndex.value < 0 || activeIndex.value >= filteredOptions.value.length) {
    return undefined
  }
  return `${listboxId}-option-${activeIndex.value}`
})

/* ─── Event handlers ────────────────────────────────────── */

function openDropdown() {
  if (filteredOptions.value.length > 0) {
    isOpen.value = true
  }
}

function closeDropdown() {
  isOpen.value = false
  activeIndex.value = -1
}

function onInput() {
  openDropdown()
}

function onFocus() {
  openDropdown()
}

function onBlur(e: FocusEvent) {
  /* Nếu focus chuyển sang listbox item thì không đóng */
  const related = e.relatedTarget as HTMLElement | null
  if (related && listRef.value?.contains(related)) {
    return
  }
  closeDropdown()
}

function selectOption(option: { value: string; label: string }) {
  model.value = option.value
  closeDropdown()
  nextTick(() => inputRef.value?.focus())
}

function onKeydown(e: KeyboardEvent) {
  switch (e.key) {
    case 'ArrowDown': {
      e.preventDefault()
      if (!isOpen.value) {
        openDropdown()
        return
      }
      if (activeIndex.value < filteredOptions.value.length - 1) {
        activeIndex.value++
      } else {
        activeIndex.value = 0
      }
      scrollActiveIntoView()
      break
    }
    case 'ArrowUp': {
      e.preventDefault()
      if (!isOpen.value) {
        openDropdown()
        return
      }
      if (activeIndex.value > 0) {
        activeIndex.value--
      } else {
        activeIndex.value = filteredOptions.value.length - 1
      }
      scrollActiveIntoView()
      break
    }
    case 'Enter': {
      if (isOpen.value && activeIndex.value >= 0 && activeIndex.value < filteredOptions.value.length) {
        e.preventDefault()
        selectOption(filteredOptions.value[activeIndex.value])
      }
      break
    }
    case 'Escape': {
      if (isOpen.value) {
        e.preventDefault()
        closeDropdown()
      }
      break
    }
  }
}

function scrollActiveIntoView() {
  nextTick(() => {
    const activeEl = listRef.value?.querySelector(`#${CSS.escape(activeDescendantId.value ?? '')}`)
    if (typeof activeEl?.scrollIntoView === 'function') {
      activeEl.scrollIntoView({ block: 'nearest' })
    }
  })
}

function onOptionMousedown(e: MouseEvent) {
  /* Ngăn blur trên input trước khi click option xử lý xong */
  e.preventDefault()
}
</script>

<template>
  <div class="combobox">
    <input
      ref="inputRef"
      v-model="model"
      role="combobox"
      autocomplete="off"
      :aria-expanded="isOpen"
      :aria-controls="listboxId"
      :aria-activedescendant="activeDescendantId"
      class="combobox__input"
      :class="`combobox__input--${size}`"
      :placeholder="placeholder"
      @input="onInput"
      @focus="onFocus"
      @blur="onBlur"
      @keydown="onKeydown"
    />
    <Transition name="combobox-dropdown">
      <ul
        v-show="isOpen && filteredOptions.length > 0"
        :id="listboxId"
        ref="listRef"
        role="listbox"
        class="combobox__listbox"
      >
        <li
          v-for="(option, index) in filteredOptions"
          :id="`${listboxId}-option-${index}`"
          :key="option.value"
          role="option"
          :aria-selected="model === option.value"
          class="combobox__option"
          :class="{
            'combobox__option--active': index === activeIndex,
            'combobox__option--selected': model === option.value,
          }"
          @mousedown="onOptionMousedown"
          @click="selectOption(option)"
          @mouseenter="activeIndex = index"
        >
          {{ option.label }}
        </li>
      </ul>
    </Transition>
  </div>
</template>

<style scoped>
.combobox {
  position: relative;
  width: 100%;
}

/* ─── Input ─────────────────────────────────────────────── */

.combobox__input {
  width: 100%;
  border: 1px solid var(--border-control, #d1d5db);
  border-radius: 0.375rem;
  background-color: var(--surface-panel);
  color: var(--text-secondary);
  font-family: inherit;
  line-height: 1.43;
  transition: border-color 120ms ease;
}

.combobox__input--sm {
  padding: 0.3rem 0.5rem;
  font-size: 0.8125rem;
}

.combobox__input--md {
  padding: 0.5rem 0.75rem;
  font-size: 0.875rem;
}

.combobox__input::placeholder {
  color: var(--text-secondary);
}

.combobox__input:focus-visible {
  outline: none;
  border-color: var(--input-border-focus, #3b82f6);
  box-shadow: var(--input-focus-ring, 0 0 0 2px rgba(59, 130, 246, 0.18));
}

.combobox__input[aria-invalid='true'] {
  border-color: var(--status-danger);
}

.combobox__input[aria-invalid='true']:focus-visible {
  outline: none;
  border-color: var(--status-danger);
  box-shadow: var(--input-focus-ring-danger, 0 0 0 2px rgba(239, 68, 68, 0.18));
}

/* ─── Dropdown listbox ──────────────────────────────────── */

.combobox__listbox {
  position: absolute;
  top: calc(100% + 4px);
  left: 0;
  right: 0;
  z-index: 50;
  margin: 0;
  padding: 0.25rem 0;
  list-style: none;
  background-color: var(--surface-panel, #fff);
  border: 1px solid var(--border-control, #d1d5db);
  border-radius: 0.375rem;
  box-shadow:
    0 4px 6px -1px rgba(0, 0, 0, 0.1),
    0 2px 4px -2px rgba(0, 0, 0, 0.1);
  max-height: 12rem;
  overflow-y: auto;
}

/* ─── Option items ──────────────────────────────────────── */

.combobox__option {
  padding: 0.375rem 0.75rem;
  font-size: 0.875rem;
  font-family: inherit;
  color: var(--text-secondary);
  cursor: pointer;
  transition: background-color 80ms ease;
}

.combobox__option:hover,
.combobox__option--active {
  background-color: var(--surface-select-hover, var(--surface-hover, #27272a));
}

.combobox__option--selected {
  background-color: var(--surface-active, #e0f2fe);
}

.combobox__option--selected.combobox__option--active {
  background-color: var(--surface-active, #e0f2fe);
}

/* ─── Dropdown transition ───────────────────────────────── */

.combobox-dropdown-enter-active,
.combobox-dropdown-leave-active {
  transition:
    opacity 120ms ease,
    transform 120ms ease;
}

.combobox-dropdown-enter-from,
.combobox-dropdown-leave-to {
  opacity: 0;
  transform: translateY(-4px);
}

/* ─── Reduced motion ────────────────────────────────────── */

@media (prefers-reduced-motion: reduce) {
  .combobox__input {
    transition: none;
  }

  .combobox__option {
    transition: none;
  }

  .combobox-dropdown-enter-active,
  .combobox-dropdown-leave-active {
    transition: none;
  }
}
</style>
