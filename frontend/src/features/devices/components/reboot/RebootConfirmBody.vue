<script setup lang="ts">
import { computed } from 'vue'
import { AlertTriangle } from '@lucide/vue'
import { DRY_RUN_NOTE, type RelaysBefore } from '@/core/commands/rebootPlan'

/** What the operator reads before typing the phrase: what each relay will do (spec 6.4), and the limits of the check. */
const props = defineProps<{ plan: RelaysBefore; readAt: number; uptimeKnown: boolean }>()

const names = (channels: number[]) => channels.map(n => `DO${n}`).join(', ')
const readTime = computed(() => new Date(props.readAt).toLocaleTimeString('vi-VN'))
const noneOn = computed(() => props.plan.turnOff.length === 0 && props.plan.forcedOn.length === 0)
</script>

<template>
  <div class="mt-3 space-y-1.5 text-xs text-[var(--text-secondary)]" data-test="reboot-dialog-body">
    <p v-if="!plan.readable" class="flex items-start gap-1.5 text-amber-600 dark:text-amber-400" data-test="reboot-line-unreadable">
      <AlertTriangle class="w-3.5 h-3.5 mt-px shrink-0" />
      <span>Không đọc được trạng thái relay; mọi relay không bị force sẽ TẮT</span>
    </p>
    <template v-else>
      <p v-if="plan.turnOff.length" data-test="reboot-line-turn-off">{{ names(plan.turnOff) }} đang BẬT (không force) sẽ TẮT</p>
      <p v-if="plan.forcedOff.length" data-test="reboot-line-forced-off">{{ names(plan.forcedOff) }} force TẮT, giữ TẮT</p>
      <p v-if="plan.forcedOn.length" data-test="reboot-line-forced-on">
        {{ names(plan.forcedOn) }} force BẬT: có thể TẮT trong lúc khởi động, BẬT lại khi thiết bị lên
      </p>
      <p v-if="noneOn" data-test="reboot-line-none">Không có relay nào đang BẬT</p>
    </template>
    <p data-test="reboot-line-rules">Luật DO SET_HIGH/SET_LOW (nếu có) chạy ngay sau khởi động và có thể BẬT relay</p>
    <p v-if="!uptimeKnown" class="text-amber-600 dark:text-amber-400" data-test="reboot-line-uptime">
      Không đọc được uptime đã ký; kết quả sẽ không được xác minh.
    </p>
    <p data-test="reboot-read-at">
      Đọc lúc {{ readTime }}. Thiết bị sẽ mất kết nối trong lúc khởi động; dữ liệu ở các thẻ sẽ cũ cho tới khi đọc lại.
    </p>
    <p class="text-[11px] text-[var(--text-hint)]" data-test="reboot-line-dry-run">
      Server gửi một lệnh chạy thử trước lệnh thật. {{ DRY_RUN_NOTE }}.
    </p>
  </div>
</template>
