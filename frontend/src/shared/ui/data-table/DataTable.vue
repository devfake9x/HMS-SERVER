<script setup lang="ts">
import { ref, computed } from 'vue'
import { ChevronUp, ChevronDown, ChevronsUpDown, ChevronLeft, ChevronRight } from '@lucide/vue'
import Checkbox from '@/shared/ui/Checkbox.vue'

export interface TableColumn {
  key: string
  label: string
  sortable?: boolean
  align?: 'left' | 'center' | 'right'
  width?: string
}

const props = withDefaults(
  defineProps<{
    columns: TableColumn[]
    items: Record<string, any>[]
    loading?: boolean
    emptyMessage?: string
    pageSize?: number
    selectable?: boolean
    selectedIds?: (string | number)[]
    idKey?: string
  }>(),
  {
    loading: false,
    emptyMessage: 'No data available',
    pageSize: 10,
    selectable: false,
    selectedIds: () => [],
    idKey: 'id',
  }
)

const emit = defineEmits<{
  (e: 'update:selectedIds', ids: (string | number)[]): void
  (e: 'row-click', item: Record<string, any>): void
}>()

const sortKey = ref<string | null>(null)
const sortOrder = ref<'asc' | 'desc'>('asc')
const currentPage = ref(1)

function toggleSort(col: TableColumn) {
  if (!col.sortable) return
  if (sortKey.value === col.key) {
    if (sortOrder.value === 'asc') {
      sortOrder.value = 'desc'
    } else {
      sortKey.value = null
      sortOrder.value = 'asc'
    }
  } else {
    sortKey.value = col.key
    sortOrder.value = 'asc'
  }
}

const sortedItems = computed(() => {
  if (!sortKey.value) return props.items
  const k = sortKey.value
  const order = sortOrder.value === 'asc' ? 1 : -1

  return [...props.items].sort((a, b) => {
    const valA = a[k]
    const valB = b[k]
    if (valA === valB) return 0
    if (valA == null) return 1
    if (valB == null) return -1
    if (typeof valA === 'number' && typeof valB === 'number') {
      return (valA - valB) * order
    }
    return String(valA).localeCompare(String(valB)) * order
  })
})

const totalPages = computed(() => {
  if (props.pageSize <= 0) return 1
  return Math.max(1, Math.ceil(sortedItems.value.length / props.pageSize))
})

const paginatedItems = computed(() => {
  if (props.pageSize <= 0) return sortedItems.value
  const start = (currentPage.value - 1) * props.pageSize
  return sortedItems.value.slice(start, start + props.pageSize)
})

const isAllSelected = computed(() => {
  if (paginatedItems.value.length === 0) return false
  return paginatedItems.value.every((item) =>
    props.selectedIds.includes(item[props.idKey])
  )
})

const isIndeterminate = computed(() => {
  if (props.selectedIds.length === 0) return false
  return !isAllSelected.value && paginatedItems.value.some((item) => props.selectedIds.includes(item[props.idKey]))
})

function toggleSelectAll(checked?: boolean) {
  const shouldSelectAll = checked !== undefined ? checked : !isAllSelected.value
  if (!shouldSelectAll) {
    const pageItemIds = new Set(paginatedItems.value.map((i) => i[props.idKey]))
    const newSelected = props.selectedIds.filter((id) => !pageItemIds.has(id))
    emit('update:selectedIds', newSelected)
  } else {
    const pageItemIds = paginatedItems.value.map((i) => i[props.idKey])
    const newSelected = Array.from(new Set([...props.selectedIds, ...pageItemIds]))
    emit('update:selectedIds', newSelected)
  }
}

function toggleSelectItem(id: string | number, checked?: boolean) {
  const isCurrentlySelected = props.selectedIds.includes(id)
  const shouldSelect = checked !== undefined ? checked : !isCurrentlySelected

  if (shouldSelect && !isCurrentlySelected) {
    emit('update:selectedIds', [...props.selectedIds, id])
  } else if (!shouldSelect && isCurrentlySelected) {
    emit(
      'update:selectedIds',
      props.selectedIds.filter((item) => item !== id)
    )
  }
}

function prevPage() {
  if (currentPage.value > 1) {
    currentPage.value--
  }
}

function nextPage() {
  if (currentPage.value < totalPages.value) {
    currentPage.value++
  }
}
</script>

<template>
  <div class="data-table-card w-full flex flex-col bg-[var(--surface-card)] border border-[var(--border-default)] rounded-lg overflow-hidden shadow-[var(--shadow-control)]" data-test="data-table">
    <!-- Table Container with Horizontal Scroll -->
    <div class="data-table__scroll overflow-x-auto">
      <table class="data-table w-full text-left border-collapse text-xs">
        <!-- Header -->
        <thead>
          <tr class="bg-[var(--surface-table-header)] border-b border-[var(--border-default)] text-[var(--text-secondary)] font-semibold select-none text-[11px] uppercase tracking-wider">
            <th v-if="selectable" class="data-table__th-checkbox w-12 px-4 py-3">
              <Checkbox
                :model-value="isAllSelected"
                :indeterminate="isIndeterminate"
                label="Select all"
                label-hidden
                data-test="table-select-all"
                @update:model-value="toggleSelectAll"
              />
            </th>
            <th
              v-for="col in columns"
              :key="col.key"
              class="data-table__th px-6 py-3 text-[var(--text-secondary)] font-semibold"
              :class="[
                col.align === 'center' ? 'text-center' : col.align === 'right' ? 'text-right' : 'text-left',
                col.sortable ? 'cursor-pointer hover:text-[var(--text-heading)] transition-colors' : '',
              ]"
              :style="{ width: col.width }"
              @click="toggleSort(col)"
              :data-test="`table-header-${col.key}`"
            >
              <div
                class="inline-flex items-center gap-1.5"
                :class="[col.align === 'right' ? 'justify-end' : col.align === 'center' ? 'justify-center' : 'justify-start']"
              >
                <span>{{ col.label }}</span>
                <span v-if="col.sortable" class="text-[var(--text-hint)]">
                  <ChevronUp v-if="sortKey === col.key && sortOrder === 'asc'" class="w-3.5 h-3.5 text-[var(--color-primary)]" />
                  <ChevronDown v-else-if="sortKey === col.key && sortOrder === 'desc'" class="w-3.5 h-3.5 text-[var(--color-primary)]" />
                  <ChevronsUpDown v-else class="w-3.5 h-3.5 opacity-40 hover:opacity-100" />
                </span>
              </div>
            </th>
          </tr>
        </thead>

        <!-- Body -->
        <tbody class="divide-y divide-[var(--border-table-row)]">
          <!-- Loading State -->
          <tr v-if="loading">
            <td :colspan="columns.length + (selectable ? 1 : 0)" class="px-6 py-12 text-center text-[var(--text-hint)]">
              <div class="flex items-center justify-center gap-2">
                <svg class="animate-spin h-5 w-5 text-[var(--color-primary)]" viewBox="0 0 24 24" fill="none">
                  <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4" />
                  <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
                </svg>
                <span>Loading records...</span>
              </div>
            </td>
          </tr>

          <!-- Empty State -->
          <tr v-else-if="paginatedItems.length === 0">
            <td :colspan="columns.length + (selectable ? 1 : 0)" class="px-6 py-12 text-center text-[var(--text-hint)]" data-test="table-empty">
              {{ emptyMessage }}
            </td>
          </tr>

          <!-- Rows -->
          <tr
            v-else
            v-for="(item, idx) in paginatedItems"
            :key="item[idKey] ?? idx"
            class="hover:bg-[var(--surface-card-hover)] transition-colors cursor-pointer group bg-[var(--surface-card)]"
            @click="emit('row-click', item)"
            data-test="table-row"
          >
            <td v-if="selectable" class="data-table__td-checkbox w-12 px-4 py-3.5" @click.stop>
              <Checkbox
                :model-value="selectedIds.includes(item[idKey])"
                :label="`Select ${item[idKey]}`"
                label-hidden
                data-test="table-row-select"
                @update:model-value="(val) => toggleSelectItem(item[idKey], val)"
              />
            </td>
            <td
              v-for="col in columns"
              :key="col.key"
              class="data-table__td px-6 py-3.5 text-[var(--text-secondary)] whitespace-nowrap"
              :class="[
                col.align === 'center' ? 'text-center' : col.align === 'right' ? 'text-right' : 'text-left',
              ]"
            >
              <slot :name="`cell-${col.key}`" :item="item" :value="item[col.key]" :index="idx">
                {{ item[col.key] !== undefined && item[col.key] !== null ? item[col.key] : '-' }}
              </slot>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- Pagination Footer -->
    <div
      v-if="!loading && items.length > 0"
      class="flex items-center justify-between px-6 py-3 border-t border-[var(--border-table-row)] bg-[var(--surface-card)] text-xs text-[var(--text-secondary)]"
    >
      <div>
        Showing
        <span class="font-medium text-[var(--text-heading)]">{{ (currentPage - 1) * pageSize + 1 }}</span>
        to
        <span class="font-medium text-[var(--text-heading)]">{{ Math.min(currentPage * pageSize, items.length) }}</span>
        of
        <span class="font-medium text-[var(--text-heading)]">{{ items.length }}</span>
        entries
      </div>

      <div class="flex items-center gap-1">
        <button
          type="button"
          class="p-1.5 rounded-md hover:bg-[var(--surface-overlay)] disabled:opacity-30 disabled:pointer-events-none transition-colors border border-[var(--border-subtle)] text-[var(--text-secondary)]"
          :disabled="currentPage <= 1"
          @click="prevPage"
          data-test="table-prev-page"
        >
          <ChevronLeft class="w-4 h-4" />
        </button>
        <span class="px-2 font-mono text-[11px] text-[var(--text-primary)]">
          {{ currentPage }} / {{ totalPages }}
        </span>
        <button
          type="button"
          class="p-1.5 rounded-md hover:bg-[var(--surface-overlay)] disabled:opacity-30 disabled:pointer-events-none transition-colors border border-[var(--border-subtle)] text-[var(--text-secondary)]"
          :disabled="currentPage >= totalPages"
          @click="nextPage"
          data-test="table-next-page"
        >
          <ChevronRight class="w-4 h-4" />
        </button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.data-table-card {
  display: flex;
  flex-direction: column;
  background-color: var(--surface-card);
  border: 1px solid var(--border-default);
  border-radius: 0.5rem;
  box-shadow: var(--shadow-control);
  overflow: hidden;
}

.data-table__scroll {
  overflow-x: auto;
  overscroll-behavior-x: contain;
  -webkit-overflow-scrolling: touch;
}

.data-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.8125rem;
  color: var(--text-primary);
}

.data-table th {
  padding: 0.75rem 1.5rem;
  font-size: 0.75rem;
  font-weight: 500;
  letter-spacing: 0.05em;
  line-height: 1.25rem;
  text-align: left;
  text-transform: uppercase;
  color: var(--text-secondary);
  background-color: var(--surface-table-header);
  white-space: nowrap;
  border-bottom: 1px solid var(--border-default);
}

.data-table td {
  padding: 0.875rem 1.5rem;
  vertical-align: middle;
  background-color: var(--surface-card);
  border-bottom: 1px solid var(--border-table-row);
  color: var(--text-secondary);
  font-size: 0.8125rem;
  line-height: 1.4;
  transition: background-color 150ms ease, color 150ms ease;
}

.data-table tbody tr:last-child td {
  border-bottom: 0 !important;
}

.data-table tbody tr:hover > td {
  background-color: var(--surface-card-hover);
  color: var(--text-primary);
}

.data-table__th-checkbox,
.data-table__td-checkbox {
  padding-left: 1.25rem !important;
  padding-right: 0.75rem !important;
  width: 3.25rem;
  text-align: center;
}

/* Dark Mode — Nuxt UI v3 / Datacake Table Header Capsule Parity */
:root[data-theme='dark'] .data-table table,
html[data-theme='dark'] .data-table table,
:root[data-theme='dark'] .data-table,
html[data-theme='dark'] .data-table {
  border-collapse: separate;
  border-spacing: 0;
}

:root[data-theme='dark'] .data-table thead tr,
html[data-theme='dark'] .data-table thead tr {
  background-color: rgba(38, 38, 42, 0.5);
}

:root[data-theme='dark'] .data-table th,
html[data-theme='dark'] .data-table th {
  color: var(--text-heading, #ffffff);
  background-color: rgba(38, 38, 42, 0.5);
  font-weight: 600;
  border-top: 1px solid var(--border-default, #27272a);
  border-bottom: 1px solid var(--border-default, #27272a);
  border-left: none;
  border-right: none;
}

:root[data-theme='dark'] .data-table th:first-child,
html[data-theme='dark'] .data-table th:first-child {
  border-left: 1px solid var(--border-default, #27272a);
  border-top-left-radius: 8px;
  border-bottom-left-radius: 8px;
}

:root[data-theme='dark'] .data-table th:last-child,
html[data-theme='dark'] .data-table th:last-child {
  border-right: 1px solid var(--border-default, #27272a);
  border-top-right-radius: 8px;
  border-bottom-right-radius: 8px;
}
</style>
