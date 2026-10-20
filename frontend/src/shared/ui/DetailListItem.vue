<script setup lang="ts">
/**
 * Một mục label–giá trị của `DetailList`. Giá trị đặt qua slot default để
 * chèn Badge/StatusIndicator tự nhiên; mã định danh (EUI, code, IP…) bật
 * `mono` như Datacake dùng font mono cho các trường ID.
 */
defineProps<{
  label: string
  mono?: boolean
}>()
</script>

<template>
  <div class="detail-item">
    <dt class="detail-item__label">{{ label }}</dt>
    <dd class="detail-item__value" :class="{ 'detail-item__value--mono': mono }">
      <slot />
    </dd>
  </div>
</template>

<style scoped>
.detail-item__label {
  font-size: 0.875rem;
  font-weight: 600;
  color: var(--text-secondary);
}

.detail-item__value {
  margin: 0.25rem 0 0 0;
  font-size: 0.875rem;
  color: var(--text-primary);
  overflow-wrap: anywhere;
}

.detail-item__value--mono {
  font-family: var(--font-mono);
  font-size: 0.8125rem;
}
</style>
