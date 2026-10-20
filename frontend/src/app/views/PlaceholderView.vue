<script setup lang="ts">
defineProps<{
  title: string
  description?: string
}>()
</script>

<template>
  <div class="space-y-4">
    <div class="flex items-center justify-between pb-4 border-b border-[var(--border-subtle)]">
      <div>
        <h1 class="text-xl font-bold text-[var(--text-heading)] tracking-tight">{{ title }}</h1>
        <p v-if="description" class="text-xs text-[var(--text-secondary)] mt-1">{{ description }}</p>
      </div>
    </div>

    <div class="p-12 text-center border border-dashed border-[var(--border-subtle)] rounded-2xl bg-[var(--surface-card)]">
      <div class="w-12 h-12 rounded-2xl bg-emerald-500/10 text-emerald-400 flex items-center justify-center mx-auto mb-3 text-lg font-bold">
        ⚡
      </div>
      <h3 class="text-sm font-bold text-[var(--text-heading)]">{{ title }}</h3>
      <p class="text-xs text-[var(--text-hint)] max-w-sm mx-auto mt-1">
        Module đang được nạp theo kiến trúc Feature-Sliced Design.
      </p>
    </div>
  </div>
</template>
