<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  LayoutDashboard,
  Gauge,
  MapPin,
  Cpu,
  FolderTree,
  Bell,
  ClipboardList,
  Users,
  ShieldCheck,
  Key,
  PanelLeftClose,
  PanelLeftOpen,
} from '@lucide/vue'
import { navigationConfig, type NavLinkItem } from '@/app/config/nav.config'
import { useSidebarCollapse } from '@/shared/composables/useSidebarCollapse'

const iconMap: Record<string, any> = {
  LayoutDashboard,
  Gauge,
  MapPin,
  Cpu,
  FolderTree,
  BellRing: Bell,
  Bell,
  ClipboardList,
  Users,
  ShieldAlert: ShieldCheck,
  KeyRound: Key,
  Key,
}

const { isCollapsed, toggle } = useSidebarCollapse()
const route = useRoute()
const router = useRouter()

function isActive(path: string) {
  if (path === '/monitoring/overview' && (route.path === '/' || route.path === '/monitoring/overview')) {
    return true
  }
  return route.path.startsWith(path) && path !== '/'
}

function navigate(path: string) {
  router.push(path)
}
</script>

<template>
  <aside
    class="sidebar-rail flex flex-col justify-between border-r border-[var(--border-subtle)] bg-[var(--surface-card)] transition-all duration-200 select-none z-30 shrink-0"
    :class="isCollapsed ? 'w-16' : 'w-64'"
    aria-label="Sidebar Navigation"
  >
    <!-- Top Brand & Header -->
    <div class="flex flex-col">
      <div class="h-16 flex items-center px-4 border-b border-[var(--border-subtle)]" :class="isCollapsed ? 'justify-center' : 'justify-between'">
        <div class="flex items-center gap-3 overflow-hidden cursor-pointer" @click="navigate('/monitoring/overview')">
          <div class="w-9 h-9 rounded-xl bg-gradient-to-br from-emerald-500 to-cyan-600 flex items-center justify-center text-white font-black text-lg shadow-md shadow-emerald-500/20 shrink-0">
            H
          </div>
          <div v-if="!isCollapsed" class="flex flex-col leading-none truncate">
            <span class="font-extrabold text-sm text-[var(--text-heading)] tracking-tight">HMS-SERVER</span>
            <span class="text-[10px] text-emerald-400 font-semibold mt-0.5 tracking-wider uppercase">SCADA & IoT</span>
          </div>
        </div>

        <button
          v-if="!isCollapsed"
          type="button"
          @click="toggle"
          class="p-1.5 rounded-lg border border-[var(--border-subtle)] text-[var(--text-secondary)] hover:text-[var(--text-primary)] hover:bg-[var(--surface-hover)] transition-colors"
          title="Thu gọn menu (Ctrl+[)"
        >
          <PanelLeftClose class="w-4 h-4" />
        </button>
      </div>

      <!-- Nav Items -->
      <nav class="p-2 space-y-5 overflow-y-auto max-h-[calc(100vh-8rem)]">
        <div v-for="group in navigationConfig" :key="group.key" class="space-y-1">
          <!-- Group Title (Expanded mode only) -->
          <div
            v-if="!isCollapsed"
            class="px-3 py-1 text-[10px] font-bold uppercase tracking-wider text-[var(--text-hint)]"
          >
            {{ group.label }}
          </div>

          <!-- Group Divider in Collapsed Mode -->
          <div v-else class="my-1.5 border-t border-[var(--border-subtle)]" />

          <!-- Links -->
          <div class="space-y-0.5">
            <button
              v-for="item in group.children"
              :key="item.key"
              type="button"
              @click="navigate(item.path)"
              class="w-full flex items-center gap-3 px-3 py-2 rounded-xl text-xs font-semibold transition-all relative group"
              :class="[
                isActive(item.path)
                  ? 'bg-emerald-500/10 text-emerald-400 border border-emerald-500/20 shadow-sm'
                  : 'text-[var(--text-secondary)] hover:text-[var(--text-primary)] hover:bg-[var(--surface-hover)]',
                isCollapsed ? 'justify-center px-0' : '',
              ]"
              :title="isCollapsed ? item.label : undefined"
            >
              <component
                :is="iconMap[item.icon] || Cpu"
                class="w-4 h-4 shrink-0 transition-colors"
                :class="isActive(item.path) ? 'text-emerald-400' : 'text-[var(--text-secondary)] group-hover:text-[var(--text-primary)]'"
              />

              <span v-if="!isCollapsed" class="truncate">{{ item.label }}</span>

              <!-- Active Indicator Bar -->
              <span
                v-if="isActive(item.path) && !isCollapsed"
                class="absolute right-2 w-1.5 h-1.5 rounded-full bg-emerald-400"
              />
            </button>
          </div>
        </div>
      </nav>
    </div>

    <!-- Bottom Rail Controls (Expand button when collapsed) -->
    <div class="p-2 border-t border-[var(--border-subtle)] flex items-center" :class="isCollapsed ? 'justify-center' : 'justify-between'">
      <div v-if="!isCollapsed" class="text-[10px] text-[var(--text-hint)] px-2 font-mono">
        v2.0 • Datacake Parity
      </div>

      <button
        type="button"
        @click="toggle"
        class="p-2 rounded-xl border border-[var(--border-subtle)] bg-[var(--surface-overlay)] text-[var(--text-secondary)] hover:text-[var(--text-primary)] hover:border-emerald-500/30 transition-all"
        :title="isCollapsed ? 'Mở rộng thanh menu (Ctrl+[)' : 'Thu gọn thanh menu (Ctrl+[)'"
      >
        <PanelLeftOpen v-if="isCollapsed" class="w-4 h-4" />
        <PanelLeftClose v-else class="w-4 h-4" />
      </button>
    </div>
  </aside>
</template>
