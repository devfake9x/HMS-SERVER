<script setup lang="ts">
import { useRouter } from 'vue-router'
import { Home } from '@lucide/vue'

const router = useRouter()
</script>

<template>
  <div class="min-h-[60vh] flex flex-col items-center justify-center text-center p-6">
    <div class="text-6xl font-black text-emerald-400 mb-2 font-mono">404</div>
    <h1 class="text-xl font-bold text-[var(--text-heading)] mb-2">Trang Không Tồn Tại</h1>
    <p class="text-xs text-[var(--text-secondary)] max-w-sm mb-6">
      Đường dẫn bạn yêu cầu không tồn tại hoặc đã được chuyển sang phân khu khác trong hệ thống SCADA.
    </p>
    <button
      type="button"
      @click="router.push('/monitoring/overview')"
      class="inline-flex items-center gap-2 px-4 py-2 rounded-xl bg-emerald-500 hover:bg-emerald-600 text-zinc-950 font-bold text-xs shadow-lg shadow-emerald-500/20 transition-all cursor-pointer"
    >
      <Home class="w-4 h-4" />
      <span>Về Tổng Quan Hệ Thống</span>
    </button>
  </div>
</template>
