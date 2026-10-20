<script lang="ts">
export type DotStatus = 'online' | 'offline' | 'warning' | 'critical' | 'unknown' | 'neutral' | 'danger' | 'success' | 'info'
export type StatusDotType = DotStatus
</script>

<script setup lang="ts">

withDefaults(defineProps<{
  status?: DotStatus
}>(), {
  status: 'unknown',
})

const SR_TEXT: Readonly<Record<DotStatus, string>> = {
  online: 'Hoạt động',
  offline: 'Ngoại tuyến',
  warning: 'Cảnh báo',
  critical: 'Nguy hiểm',
  unknown: 'Không rõ',
  neutral: 'Bình thường',
  danger: 'Nguy hiểm',
  success: 'Hoạt động',
  info: 'Thông tin',
}
</script>

<template>
  <span class="status-dot" :class="`status-dot--${status}`" role="img" :aria-label="SR_TEXT[status]">
    <span class="sr-only">{{ SR_TEXT[status] }}</span>
  </span>
</template>

<style scoped>
.status-dot {
  display: inline-block;
  width: 0.5rem;
  height: 0.5rem;
  border-radius: 9999px;
  flex: none;
}

.status-dot--online,
.status-dot--success {
  background-color: var(--status-success-dot, var(--status-success));
}

.status-dot--offline {
  background-color: var(--status-neutral-dot, var(--status-neutral));
}

.status-dot--warning {
  background-color: var(--status-warning-dot, var(--status-warning));
}

.status-dot--critical {
  background-color: var(--status-danger-dot, var(--status-danger));
}

.status-dot--unknown,
.status-dot--neutral {
  background-color: var(--status-neutral-dot, var(--status-neutral));
}

.status-dot--danger {
  background-color: var(--status-danger-dot, var(--status-danger));
}

.status-dot--info {
  background-color: var(--status-info-dot, var(--status-info));
}
</style>
