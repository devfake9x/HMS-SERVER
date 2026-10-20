<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import { Bell, CheckCircle2, AlertTriangle, Info } from '@lucide/vue'

export interface AlarmItem {
  id: string
  title: string
  time: string
  severity: 'info' | 'warning' | 'danger'
}

withDefaults(
  defineProps<{
    alarms?: AlarmItem[]
    unreadCount?: number
  }>(),
  {
    alarms: () => [
      {
        id: '1',
        title: 'Trạm RTU-ESP32-S3-001 kết nối thành công',
        time: 'Vừa xong',
        severity: 'info',
      },
    ],
    unreadCount: 1,
  }
)

const isOpen = ref(false)
const containerRef = ref<HTMLElement | null>(null)

function toggle() {
  isOpen.value = !isOpen.value
}

function close() {
  isOpen.value = false
}

function handleClickOutside(event: MouseEvent) {
  if (containerRef.value && !containerRef.value.contains(event.target as Node)) {
    close()
  }
}

onMounted(() => {
  document.addEventListener('click', handleClickOutside)
})

onUnmounted(() => {
  document.removeEventListener('click', handleClickOutside)
})
</script>

<template>
  <div ref="containerRef" class="relative">
    <button
      type="button"
      @click.stop="toggle"
      class="relative p-2 rounded-xl border border-[var(--border-subtle)] bg-[var(--surface-card)] hover:bg-[var(--surface-hover)] text-[var(--text-secondary)] hover:text-[var(--text-primary)] transition-all focus:outline-none focus:ring-2 focus:ring-[var(--color-primary)]"
      title="Cảnh báo & Sự kiện"
      :aria-expanded="isOpen"
    >
      <Bell class="w-4 h-4" />
      <span
        v-if="unreadCount > 0"
        class="absolute -top-1 -right-1 w-4 h-4 rounded-full bg-red-500 text-white text-[10px] font-bold flex items-center justify-center border-2 border-[var(--surface-card)] animate-pulse"
      >
        {{ unreadCount > 9 ? '9+' : unreadCount }}
      </span>
    </button>

    <!-- Popover -->
    <div
      v-if="isOpen"
      class="absolute right-0 mt-2 w-80 rounded-2xl border border-[var(--border-subtle)] bg-[var(--surface-card)] shadow-[var(--shadow-card)] p-4 z-50 animate-in fade-in zoom-in-95 duration-100"
    >
      <div class="flex items-center justify-between pb-3 border-b border-[var(--border-subtle)] mb-3">
        <div class="text-xs font-bold text-[var(--text-heading)] uppercase tracking-wider flex items-center gap-1.5">
          <Bell class="w-3.5 h-3.5 text-emerald-400" />
          <span>Sự kiện & Báo động</span>
        </div>
        <span class="text-[10px] text-[var(--text-hint)]">{{ alarms.length }} sự kiện</span>
      </div>

      <div class="space-y-2 max-h-60 overflow-y-auto pr-1">
        <div
          v-for="item in alarms"
          :key="item.id"
          class="p-2.5 rounded-xl border border-[var(--border-subtle)] bg-[var(--surface-overlay)] hover:border-emerald-500/30 transition-colors"
        >
          <div class="flex items-start gap-2">
            <span
              class="w-2 h-2 rounded-full mt-1.5 shrink-0"
              :class="{
                'bg-emerald-400': item.severity === 'info',
                'bg-amber-400': item.severity === 'warning',
                'bg-red-400': item.severity === 'danger',
              }"
            />
            <div class="flex-1">
              <div class="text-xs font-medium text-[var(--text-primary)] leading-snug">{{ item.title }}</div>
              <div class="text-[10px] text-[var(--text-hint)] mt-1">{{ item.time }}</div>
            </div>
          </div>
        </div>

        <div v-if="alarms.length === 0" class="text-center py-6 text-xs text-[var(--text-hint)]">
          Không có sự kiện báo động mới
        </div>
      </div>
    </div>
  </div>
</template>
