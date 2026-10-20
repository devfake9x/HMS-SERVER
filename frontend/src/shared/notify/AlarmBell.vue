<script setup lang="ts">
import {
  DropdownMenuContent,
  DropdownMenuPortal,
  DropdownMenuRoot,
  DropdownMenuTrigger,
} from 'reka-ui'

import {
  acknowledgeAlarm,
  acknowledgeAllAlarms,
  activeAlarms,
  unackCount,
  type AlarmItem,
} from '#/shared/notify/alarm'
import { iconPath } from '#/shared/ui/icons'

/**
 * Quả chuông cảnh báo sự cố & thông báo thời gian thực.
 *
 * Kết nối trực tiếp tới hàng đợi `shared/notify/alarm.ts`.
 * Hiển thị badge số lượng sự cố chưa xử lý và mở Popover danh sách nhanh.
 */

defineEmits<{
  viewAll: []
}>()

function formatRelativeTime(timestamp: number): string {
  const diffSec = Math.max(0, Math.floor((Date.now() - timestamp) / 1000))
  if (diffSec < 60) return 'Vừa xong'
  const diffMin = Math.floor(diffSec / 60)
  if (diffMin < 60) return `${diffMin} phút trước`
  const diffHour = Math.floor(diffMin / 60)
  if (diffHour < 24) return `${diffHour} giờ trước`
  const diffDay = Math.floor(diffHour / 24)
  return `${diffDay} ngày trước`
}

function handleAck(item: AlarmItem): void {
  acknowledgeAlarm(item.id, 'Kỹ thuật viên')
}

function handleAckAll(): void {
  acknowledgeAllAlarms('Kỹ thuật viên')
}
</script>

<template>
  <DropdownMenuRoot>
    <DropdownMenuTrigger
      class="alarmbell__trigger"
      type="button"
      :aria-label="`Thông báo: ${unackCount} cảnh báo chưa xử lý`"
    >
      <svg
        class="alarmbell__icon"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        stroke-width="2"
        aria-hidden="true"
      >
        <path v-if="iconPath('bell')" :d="iconPath('bell')!" />
      </svg>
      <span v-if="unackCount > 0" class="alarmbell__badge">
        {{ unackCount > 99 ? '99+' : unackCount }}
      </span>
    </DropdownMenuTrigger>

    <DropdownMenuPortal>
      <DropdownMenuContent
        class="alarmbell__content"
        :side-offset="6"
        align="end"
      >
        <div class="alarmbell__header">
          <div class="alarmbell__header-title">
            <span class="alarmbell__title-text">Cảnh báo hệ thống</span>
            <span v-if="unackCount > 0" class="alarmbell__count-tag">{{ unackCount }} mới</span>
          </div>
          <button
            v-if="unackCount > 0"
            type="button"
            class="alarmbell__mark-read"
            @click="handleAckAll"
          >
            Tiếp nhận tất cả
          </button>
        </div>

        <div v-if="activeAlarms.length === 0" class="alarmbell__empty" style="padding: 1.5rem; text-align: center; color: var(--text-muted); font-size: 0.875rem;">
          Không có cảnh báo chưa xử lý
        </div>

        <div v-else class="alarmbell__list">
          <div
            v-for="item in activeAlarms"
            :key="item.id"
            class="alarmbell__item"
            :class="[
              `alarmbell__item--${item.severity}`,
              { 'alarmbell__item--acked': item.state === 'active_ack' }
            ]"
          >
            <span class="alarmbell__dot" :class="`alarmbell__dot--${item.severity}`" aria-hidden="true" />
            <div class="alarmbell__item-body">
              <div class="alarmbell__item-head" style="display: flex; justify-content: space-between; align-items: flex-start; gap: 0.5rem;">
                <span class="alarmbell__item-title">{{ item.title }}</span>
                <span v-if="item.state === 'active_ack'" class="alarmbell__ack-badge" style="font-size: 0.6875rem; color: var(--text-muted); flex: none;">
                  Đã nhận
                </span>
                <button
                  v-else
                  type="button"
                  class="alarmbell__item-ack-btn"
                  style="font-size: 0.6875rem; padding: 0.125rem 0.375rem; border-radius: 0.25rem; border: 1px solid var(--border-subtle); background: var(--surface-overlay); color: var(--text-primary); cursor: pointer; flex: none;"
                  @click.stop="handleAck(item)"
                >
                  Tiếp nhận
                </button>
              </div>
              <div class="alarmbell__item-meta">
                <span>{{ item.location }}</span>
                <span>•</span>
                <span>{{ formatRelativeTime(item.occurredAt) }}</span>
              </div>
            </div>
          </div>
        </div>

        <div class="alarmbell__footer">
          <RouterLink
            class="alarmbell__view-all"
            style="display: block; width: 100%; text-align: center; text-decoration: none;"
            :to="{ name: 'gateway-devices', query: { connectionStatus: 'offline' } }"
          >
            Xem trạm mất kết nối
          </RouterLink>
        </div>
      </DropdownMenuContent>
    </DropdownMenuPortal>
  </DropdownMenuRoot>
</template>

<style src="./AlarmBell.css"></style>
