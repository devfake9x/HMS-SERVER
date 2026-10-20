<script setup lang="ts">
import { ref, computed } from 'vue'
import { ChevronRight, ChevronDown, Folder, HardDrive, Building2, Briefcase, Layers } from '@lucide/vue'

export interface TreeNode {
  id: string | number
  name: string
  type: 'org' | 'project' | 'package' | 'area' | 'device' | string
  depth?: number
  children?: TreeNode[]
  meta?: Record<string, any>
  status?: string
}

const props = withDefaults(
  defineProps<{
    nodes: TreeNode[]
    loading?: boolean
    emptyMessage?: string
    defaultExpandedAll?: boolean
  }>(),
  {
    loading: false,
    emptyMessage: 'No hierarchy data found',
    defaultExpandedAll: false,
  }
)

const emit = defineEmits<{
  (e: 'select-node', node: TreeNode): void
  (e: 'action', actionKey: string, node: TreeNode): void
}>()

const expandedIds = ref<Set<string | number>>(new Set())

function isExpanded(id: string | number) {
  return expandedIds.value.has(id)
}

function toggleExpand(node: TreeNode) {
  if (expandedIds.value.has(node.id)) {
    expandedIds.value.delete(node.id)
  } else {
    expandedIds.value.add(node.id)
  }
}

interface FlattenedRow {
  node: TreeNode
  depth: number
  hasChildren: boolean
}

function flattenTree(items: TreeNode[], depth = 0): FlattenedRow[] {
  const result: FlattenedRow[] = []
  for (const item of items) {
    const hasChildren = !!(item.children && item.children.length > 0)
    result.push({ node: item, depth, hasChildren })
    if (hasChildren && (props.defaultExpandedAll || isExpanded(item.id))) {
      result.push(...flattenTree(item.children!, depth + 1))
    }
  }
  return result
}

const visibleRows = computed(() => flattenTree(props.nodes))

function getNodeIcon(type: string) {
  switch (type.toLowerCase()) {
    case 'org':
    case 'organization':
      return Building2
    case 'project':
      return Briefcase
    case 'package':
      return Layers
    case 'area':
      return Folder
    case 'device':
      return HardDrive
    default:
      return Folder
  }
}
</script>

<template>
  <div class="w-full flex flex-col bg-white dark:bg-zinc-900 border border-zinc-200/80 dark:border-zinc-800 rounded-xl overflow-hidden shadow-xs" data-test="tree-data-table">
    <div class="overflow-x-auto">
      <table class="w-full text-left border-collapse text-xs">
        <thead>
          <tr class="bg-zinc-50/80 dark:bg-zinc-950/60 border-b border-zinc-200/80 dark:border-zinc-800 text-zinc-500 dark:text-zinc-400 font-medium select-none">
            <th class="px-4 py-3 text-zinc-600 dark:text-zinc-300 font-semibold">Hierarchy Node</th>
            <th class="px-4 py-3 text-zinc-600 dark:text-zinc-300 font-semibold w-28">Type</th>
            <th class="px-4 py-3 text-zinc-600 dark:text-zinc-300 font-semibold w-32">Status</th>
            <th class="px-4 py-3 text-zinc-600 dark:text-zinc-300 font-semibold text-right w-40">Actions</th>
          </tr>
        </thead>
        <tbody class="divide-y divide-zinc-100 dark:divide-zinc-800/80">
          <tr v-if="loading">
            <td colspan="4" class="px-6 py-12 text-center text-zinc-400">
              <div class="flex items-center justify-center gap-2">
                <svg class="animate-spin h-5 w-5 text-amber-500" viewBox="0 0 24 24" fill="none">
                  <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4" />
                  <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
                </svg>
                <span>Loading hierarchy...</span>
              </div>
            </td>
          </tr>

          <tr v-else-if="visibleRows.length === 0">
            <td colspan="4" class="px-6 py-12 text-center text-zinc-400" data-test="tree-empty">
              {{ emptyMessage }}
            </td>
          </tr>

          <tr
            v-else
            v-for="row in visibleRows"
            :key="row.node.id"
            class="hover:bg-zinc-50/70 dark:hover:bg-zinc-800/40 transition-colors cursor-pointer group"
            @click="emit('select-node', row.node)"
            data-test="tree-row"
          >
            <!-- Node Name + Indent + Expander -->
            <td class="px-4 py-3 text-zinc-900 dark:text-zinc-100 font-medium">
              <div class="flex items-center" :style="{ paddingLeft: `${row.depth * 22}px` }">
                <!-- Expander Button or spacer -->
                <button
                  v-if="row.hasChildren"
                  type="button"
                  class="p-1 -ml-1 mr-1 text-zinc-400 hover:text-zinc-700 dark:hover:text-zinc-200 rounded transition-colors"
                  @click.stop="toggleExpand(row.node)"
                  data-test="tree-toggle"
                >
                  <ChevronDown v-if="isExpanded(row.node.id) || defaultExpandedAll" class="w-3.5 h-3.5" />
                  <ChevronRight v-else class="w-3.5 h-3.5" />
                </button>
                <span v-else class="w-5 inline-block" />

                <!-- Icon -->
                <component
                  :is="getNodeIcon(row.node.type)"
                  class="w-4 h-4 mr-2 text-zinc-400 dark:text-zinc-500 group-hover:text-amber-500 transition-colors"
                />

                <span class="truncate">{{ row.node.name }}</span>
              </div>
            </td>

            <!-- Type Badge -->
            <td class="px-4 py-3">
              <span class="inline-flex px-2 py-0.5 rounded text-[10px] uppercase font-mono tracking-wider bg-zinc-100 dark:bg-zinc-800 text-zinc-600 dark:text-zinc-400 border border-zinc-200 dark:border-zinc-700">
                {{ row.node.type }}
              </span>
            </td>

            <!-- Status -->
            <td class="px-4 py-3">
              <slot name="status" :node="row.node">
                <span v-if="row.node.status" class="inline-flex items-center gap-1.5 text-xs text-zinc-600 dark:text-zinc-300">
                  <span
                    class="w-2 h-2 rounded-full"
                    :class="[
                      row.node.status === 'online' ? 'bg-emerald-500' :
                      row.node.status === 'warning' ? 'bg-amber-500' :
                      row.node.status === 'offline' ? 'bg-zinc-400' : 'bg-zinc-500'
                    ]"
                  />
                  <span class="capitalize">{{ row.node.status }}</span>
                </span>
                <span v-else class="text-zinc-400 text-xs">-</span>
              </slot>
            </td>

            <!-- Actions -->
            <td class="px-4 py-3 text-right" @click.stop>
              <slot name="actions" :node="row.node">
                <button
                  type="button"
                  class="px-2 py-1 text-[11px] font-medium text-zinc-600 dark:text-zinc-400 hover:text-amber-600 dark:hover:text-amber-400 hover:bg-zinc-100 dark:hover:bg-zinc-800 rounded transition-colors"
                  @click="emit('action', 'view', row.node)"
                >
                  View
                </button>
              </slot>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>
