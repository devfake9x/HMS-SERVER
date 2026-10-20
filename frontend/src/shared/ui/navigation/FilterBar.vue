<script setup lang="ts">
import { ref, watch } from 'vue'
import { Search, X } from '@lucide/vue'

export interface FilterTab {
  key: string
  label: string
  count?: number
}

const props = withDefaults(
  defineProps<{
    search?: string
    searchPlaceholder?: string
    tabs?: FilterTab[]
    activeTab?: string
  }>(),
  {
    search: '',
    searchPlaceholder: 'Search...',
    tabs: () => [],
    activeTab: 'all',
  }
)

const emit = defineEmits<{
  (e: 'update:search', val: string): void
  (e: 'update:activeTab', tabKey: string): void
}>()

const localSearch = ref(props.search)
let debounceTimeout: ReturnType<typeof setTimeout> | null = null

watch(
  () => props.search,
  (val) => {
    localSearch.value = val
  }
)

function onSearchInput(e: Event) {
  const target = e.target as HTMLInputElement
  localSearch.value = target.value
  if (debounceTimeout) clearTimeout(debounceTimeout)
  debounceTimeout = setTimeout(() => {
    emit('update:search', localSearch.value)
  }, 250)
}

function clearSearch() {
  localSearch.value = ''
  if (debounceTimeout) clearTimeout(debounceTimeout)
  emit('update:search', '')
}
</script>

<template>
  <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-3 p-3 bg-[var(--surface-card)] border border-[var(--border-default)] rounded-lg shadow-[var(--shadow-control)]">
    <!-- Left: Tabs -->
    <div v-if="tabs.length > 0" class="flex items-center gap-1.5 overflow-x-auto pb-1 sm:pb-0 scrollbar-none">
      <button
        v-for="tab in tabs"
        :key="tab.key"
        type="button"
        class="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-md text-xs font-medium transition-colors cursor-pointer select-none whitespace-nowrap"
        :class="[
          activeTab === tab.key
            ? 'bg-[var(--surface-overlay)] text-[var(--text-heading)] font-semibold shadow-xs border border-[var(--border-default)]'
            : 'text-[var(--text-secondary)] hover:text-[var(--text-primary)] hover:bg-[var(--surface-overlay)] border border-transparent'
        ]"
        @click="emit('update:activeTab', tab.key)"
        :data-test="`filter-tab-${tab.key}`"
      >
        <span>{{ tab.label }}</span>
        <span
          v-if="tab.count !== undefined"
          class="text-[10px] px-1.5 py-0.2 rounded-full font-mono"
          :class="[
            activeTab === tab.key
              ? 'bg-[var(--surface-card)] text-[var(--text-heading)]'
              : 'bg-[var(--surface-overlay)] text-[var(--text-hint)]'
          ]"
        >
          {{ tab.count }}
        </span>
      </button>
    </div>

    <!-- Right: Search + Extra actions slot -->
    <div class="flex items-center gap-2.5 flex-1 sm:flex-initial justify-end">
      <!-- Search Box -->
      <div class="relative w-full sm:w-64">
        <div class="absolute inset-y-0 left-0 pl-2.5 flex items-center pointer-events-none text-[var(--text-hint)]">
          <Search class="w-3.5 h-3.5" />
        </div>
        <input
          :value="localSearch"
          @input="onSearchInput"
          type="text"
          :placeholder="searchPlaceholder"
          class="w-full pl-8 pr-7 py-1.5 text-xs rounded-md bg-[var(--surface-panel)] border border-[var(--border-default)] text-[var(--text-primary)] placeholder-[var(--text-hint)] focus:outline-none focus:border-[var(--color-primary)] transition-colors"
          data-test="filter-search-input"
        />
        <button
          v-if="localSearch"
          type="button"
          @click="clearSearch"
          class="absolute inset-y-0 right-0 pr-2 flex items-center text-[var(--text-hint)] hover:text-[var(--text-primary)]"
          data-test="filter-search-clear"
        >
          <X class="w-3.5 h-3.5" />
        </button>
      </div>

      <!-- Action slot -->
      <slot name="actions" />
    </div>
  </div>
</template>
