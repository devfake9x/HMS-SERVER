<script setup lang="ts">
import { onMounted, ref } from 'vue'
import Button from '@/shared/ui/primitives/Button.vue'

/** Inline confirmation of one config write: the old and the new value, then Xác nhận or Huỷ (Esc). */
defineProps<{ title: string; from: string; to: string }>()
const emit = defineEmits<{ confirm: []; cancel: [] }>()

const root = ref<HTMLElement | null>(null)
onMounted(() => root.value?.focus())
</script>

<template>
  <div
    ref="root"
    role="alertdialog"
    tabindex="-1"
    :aria-label="`Xác nhận: ${title}`"
    class="p-2 rounded-md border border-amber-500/40 bg-amber-500/5 space-y-1.5 outline-none"
    data-test="config-confirm"
    @keydown.esc.stop="emit('cancel')"
  >
    <p class="text-[11px] font-semibold text-[var(--text-heading)]">{{ title }}</p>
    <p class="text-[11px] font-mono text-[var(--text-primary)] break-all">
      <span data-test="config-confirm-from">{{ from }}</span>
      <span class="mx-1 text-[var(--text-hint)]" aria-hidden="true">→</span>
      <span class="sr-only">thành</span>
      <span data-test="config-confirm-to" class="font-semibold">{{ to }}</span>
    </p>
    <div class="flex items-center gap-1.5">
      <Button variant="primary" size="xs" data-test="config-confirm-ok" @click="emit('confirm')">Xác nhận</Button>
      <Button variant="ghost" size="xs" data-test="config-confirm-cancel" @click="emit('cancel')">Huỷ</Button>
    </div>
  </div>
</template>
