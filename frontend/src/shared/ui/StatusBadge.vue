<script setup lang="ts">
/**
 * StatusBadge — Hiển thị trạng thái vận hành và kết nối chuẩn Datacake dạng Soft Pill Badge.
 *
 * ## Đặc điểm kỹ thuật:
 *   - Không dùng dấu chấm tròn (loại bỏ StatusDot gây chật chội).
 *   - Kiểu dáng Soft Pill Badge: bo góc mềm 6px, nền mờ 10-15%, viền ring inset tinh tế.
 *   - Tự động chuẩn hóa ngữ nghĩa và đa ngôn ngữ dựa trên shared/i18n/status:
 *     + operational: "Hoạt động" / "Dừng hoạt động"
 *     + connectivity: "Kết nối" / "Mất kết nối"
 *   - Tương thích tốt trên cả nền Sáng và Tối (WCAG 2.1 AA).
 */
import { computed } from 'vue'
import {
  useStatusTranslation,
  type StatusCategory,
  type BadgeTone,
} from '#/shared/i18n/status'

const props = withDefaults(
  defineProps<{
    /** Mã trạng thái (vd: active, suspended, online, offline, degraded...) */
    status?: string | null
    /** Phân loại ngữ cảnh hiển thị trạng thái */
    type?: StatusCategory
    /** Tùy chọn chỉ định tone màu trực tiếp (success, danger, warning, neutral) */
    tone?: BadgeTone
    /** Nhãn trợ năng cho screen readers */
    srLabel?: string
  }>(),
  {
    status: '',
    type: 'auto',
    tone: undefined,
    srLabel: undefined,
  },
)

const { tStatus, getOperationalLabel, getConnectivityLabel, resolveBadgeTone } = useStatusTranslation()

const resolvedTone = computed<BadgeTone>(() => {
  if (props.tone) return props.tone
  return resolveBadgeTone(props.status, props.type)
})

const resolvedText = computed<string>(() => {
  if (!props.status) return '—'
  if (props.type === 'operational') {
    return getOperationalLabel(props.status)
  }
  if (props.type === 'connectivity') {
    return getConnectivityLabel(props.status)
  }
  return tStatus(props.status)
})
</script>

<template>
  <span
    class="status-badge"
    :class="`status-badge--${resolvedTone}`"
    :data-status="status || 'unknown'"
    :data-type="type"
    :title="resolvedText"
  >
    <span v-if="srLabel" class="sr-only">{{ srLabel }}: </span>
    <span class="status-badge__label"><slot>{{ resolvedText }}</slot></span>
  </span>
</template>

<style scoped>
.status-badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  padding: 0.25rem 0.5rem;
  font-size: 0.75rem;
  line-height: 1rem;
  font-weight: 500;
  border-radius: 0.375rem;
  white-space: nowrap;
  letter-spacing: 0.01em;
  transition: background-color 0.15s ease, color 0.15s ease, border-color 0.15s ease;
  user-select: none;
}

.status-badge__label {
  display: inline-block;
}

/* Tone 1: Success (Hoạt động / Kết nối) */
.status-badge--success {
  background-color: rgba(16, 185, 129, 0.12);
  color: #065f46;
  box-shadow: inset 0 0 0 1px rgba(16, 185, 129, 0.28);
}

/* Tone 2: Danger (Mất kết nối / Lỗi / Khóa) */
.status-badge--danger {
  background-color: rgba(239, 68, 68, 0.12);
  color: #991b1b;
  box-shadow: inset 0 0 0 1px rgba(239, 68, 68, 0.28);
}

/* Tone 3: Warning (Cảnh báo / Suy giảm / Đang kết nối) */
.status-badge--warning {
  background-color: rgba(245, 158, 11, 0.12);
  color: #92400e;
  box-shadow: inset 0 0 0 1px rgba(245, 158, 11, 0.28);
}

/* Tone 4: Neutral (Dừng hoạt động / Không rõ / Chưa kích hoạt) */
.status-badge--neutral {
  background-color: rgba(100, 116, 139, 0.12);
  color: #475569;
  box-shadow: inset 0 0 0 1px rgba(100, 116, 139, 0.25);
}

.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border-width: 0;
}
</style>

<style>
/* Dark theme overrides — Nuxt UI v3 Parity (10% tint, 25% ring, text phát quang) */
html[data-theme='dark'] .status-badge--success,
[data-theme='dark'] .status-badge--success,
.dark .status-badge--success {
  background-color: rgba(52, 211, 153, 0.1);
  color: #34d399;
  box-shadow: inset 0 0 0 1px rgba(52, 211, 153, 0.25);
}

html[data-theme='dark'] .status-badge--danger,
[data-theme='dark'] .status-badge--danger,
.dark .status-badge--danger {
  background-color: rgba(248, 113, 113, 0.1);
  color: #f87171;
  box-shadow: inset 0 0 0 1px rgba(248, 113, 113, 0.25);
}

html[data-theme='dark'] .status-badge--warning,
[data-theme='dark'] .status-badge--warning,
.dark .status-badge--warning {
  background-color: rgba(251, 191, 36, 0.1);
  color: #fbbf24;
  box-shadow: inset 0 0 0 1px rgba(251, 191, 36, 0.25);
}

html[data-theme='dark'] .status-badge--neutral,
[data-theme='dark'] .status-badge--neutral,
.dark .status-badge--neutral {
  background-color: rgba(161, 161, 170, 0.1);
  color: #cbd5e1;
  box-shadow: inset 0 0 0 1px rgba(161, 161, 170, 0.25);
}
</style>
