<script setup lang="ts">
import { nextTick, ref } from 'vue'
import { iconPath } from './icons'

export interface VerticalTabItem {
  value: string
  label: string
  description?: string
  icon?: string
  badge?: string | number
  disabled?: boolean
}

const props = defineProps<{
  tabs: readonly VerticalTabItem[]
  modelValue: string
}>()

const emit = defineEmits<{
  'update:modelValue': [value: string]
}>()

const tabButtons = ref<HTMLButtonElement[]>([])

function selectTab(value: string, index?: number): void {
  emit('update:modelValue', value)
  if (typeof index === 'number' && tabButtons.value[index]) {
    nextTick(() => {
      tabButtons.value[index]?.focus()
    })
  }
}

function handleKeydown(event: KeyboardEvent, currentIndex: number): void {
  const enabledTabs = props.tabs
    .map((t, idx) => ({ ...t, originalIndex: idx }))
    .filter((t) => !t.disabled)

  if (enabledTabs.length === 0) return

  const currentPos = enabledTabs.findIndex((t) => t.originalIndex === currentIndex)
  let nextPos = -1

  if (event.key === 'ArrowDown') {
    event.preventDefault()
    nextPos = currentPos < enabledTabs.length - 1 ? currentPos + 1 : 0
  } else if (event.key === 'ArrowUp') {
    event.preventDefault()
    nextPos = currentPos > 0 ? currentPos - 1 : enabledTabs.length - 1
  } else if (event.key === 'Home') {
    event.preventDefault()
    nextPos = 0
  } else if (event.key === 'End') {
    event.preventDefault()
    nextPos = enabledTabs.length - 1
  }

  if (nextPos !== -1) {
    const target = enabledTabs[nextPos]
    selectTab(target.value, target.originalIndex)
  }
}
</script>

<template>
  <div
    class="vtab-list"
    role="tablist"
    aria-orientation="vertical"
    data-testid="vertical-tab-bar"
  >
    <button
      v-for="(tab, idx) in tabs"
      :ref="(el) => { if (el) tabButtons[idx] = el as HTMLButtonElement }"
      :key="tab.value"
      type="button"
      role="tab"
      class="vtab-item"
      :class="{
        'vtab-item--active': tab.value === modelValue,
        'vtab-item--disabled': tab.disabled,
      }"
      :aria-selected="tab.value === modelValue"
      :tabindex="tab.value === modelValue ? 0 : -1"
      :disabled="tab.disabled"
      :data-testid="`vtab-${tab.value}`"
      @click="!tab.disabled && selectTab(tab.value, idx)"
      @keydown="handleKeydown($event, idx)"
    >
      <svg
        v-if="iconPath(tab.icon)"
        class="vtab-item__icon"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        stroke-width="2"
        stroke-linecap="round"
        stroke-linejoin="round"
        aria-hidden="true"
      >
        <path :d="iconPath(tab.icon) ?? ''" />
      </svg>
      <div class="vtab-item__content">
        <div class="vtab-item__header">
          <span class="vtab-item__title">{{ tab.label }}</span>
          <span v-if="tab.badge !== undefined" class="vtab-item__badge">{{ tab.badge }}</span>
        </div>
        <span v-if="tab.description" class="vtab-item__desc">{{ tab.description }}</span>
      </div>
    </button>
  </div>
</template>

<style scoped>
.vtab-list {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  width: 100%;
}

.vtab-item {
  display: flex;
  align-items: flex-start;
  gap: 0.75rem;
  padding: 0.75rem 1rem;
  border-radius: 8px;
  border: 1px solid transparent;
  background: transparent;
  text-align: left;
  cursor: pointer;
  width: 100%;
  font-family: inherit;
  transition: background-color 0.15s ease, border-color 0.15s ease;
}

.vtab-item:hover:not(:disabled) {
  background-color: var(--surface-card-hover, #f9fafb);
}

.vtab-item:focus-visible {
  outline: 2px solid var(--status-info, #155dfc);
  outline-offset: -2px;
}

.vtab-item--active {
  background-color: rgba(37, 99, 235, 0.08);
  border-color: rgba(37, 99, 235, 0.25);
}

html[data-theme='dark'] .vtab-item--active {
  background-color: rgba(16, 185, 129, 0.12);
  border-color: rgba(16, 185, 129, 0.3);
}

.vtab-item--disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.vtab-item__icon {
  flex-shrink: 0;
  width: 1.25rem;
  height: 1.25rem;
  margin-top: 0.125rem;
  color: var(--text-secondary);
}

.vtab-item--active .vtab-item__icon {
  color: var(--status-info, #155dfc);
}

html[data-theme='dark'] .vtab-item--active .vtab-item__icon {
  color: var(--color-primary, #10b981);
}

.vtab-item__content {
  display: flex;
  flex-direction: column;
  min-width: 0;
  flex: 1;
}

.vtab-item__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.5rem;
}

.vtab-item__title {
  font-size: 0.875rem;
  font-weight: 600;
  color: var(--text-heading);
  line-height: 1.25rem;
}

.vtab-item--active .vtab-item__title {
  color: var(--status-info, #155dfc);
}

html[data-theme='dark'] .vtab-item--active .vtab-item__title {
  color: var(--color-primary, #10b981);
}

.vtab-item__desc {
  display: block;
  font-size: 0.75rem;
  color: var(--text-secondary);
  margin-top: 0.125rem;
  line-height: 1.35;
}

.vtab-item__badge {
  display: inline-flex;
  align-items: center;
  padding: 0.125rem 0.375rem;
  border-radius: 9999px;
  font-size: 0.6875rem;
  font-weight: 500;
  background-color: var(--surface-overlay);
  color: var(--text-primary);
}

@media (prefers-reduced-motion: reduce) {
  .vtab-item {
    transition: none;
  }
}
</style>
