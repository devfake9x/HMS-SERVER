<script setup lang="ts">
import { ChevronRight, ChevronLeft } from '@lucide/vue'
import { useRouter } from 'vue-router'
import type { BreadcrumbItem } from '../../composables/useBreadcrumbs'

defineProps<{
  items: BreadcrumbItem[]
  canGoBack?: boolean
}>()

const emit = defineEmits<{
  back: []
}>()

const router = useRouter()

function navigate(to?: string) {
  if (to) {
    router.push(to)
  }
}
</script>

<template>
  <nav class="flex items-center gap-2 text-xs" aria-label="Breadcrumb">
    <!-- Back Button -->
    <button
      v-if="canGoBack"
      type="button"
      @click="emit('back')"
      class="p-1.5 rounded-lg border border-[var(--border-subtle)] bg-[var(--surface-card)] hover:bg-[var(--surface-hover)] text-[var(--text-secondary)] hover:text-[var(--text-primary)] transition-colors mr-1"
      title="Quay lại trang trước"
    >
      <ChevronLeft class="w-3.5 h-3.5" />
    </button>

    <!-- Items -->
    <div class="flex items-center gap-1.5 flex-wrap">
      <template v-for="(item, index) in items" :key="index">
        <button
          v-if="item.to && index < items.length - 1"
          type="button"
          @click="navigate(item.to)"
          class="font-medium text-[var(--text-secondary)] hover:text-[var(--color-primary)] transition-colors cursor-pointer"
        >
          {{ item.label }}
        </button>
        <span
          v-else
          class="font-semibold"
          :class="index === items.length - 1 ? 'text-[var(--text-heading)]' : 'text-[var(--text-secondary)]'"
        >
          {{ item.label }}
        </span>

        <ChevronRight
          v-if="index < items.length - 1"
          class="w-3.5 h-3.5 text-[var(--text-hint)] shrink-0"
        />
      </template>
    </div>
  </nav>
</template>
