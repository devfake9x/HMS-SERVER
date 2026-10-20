<script setup lang="ts">
/**
 * Nhãn trạng thái. Nhận **mức ngữ nghĩa**, không nhận màu.
 * `specs/006-audit-log-slice` FR-7, FR-8, A1/B.3.
 *
 * ## Vì sao không nền, chữ + viền cùng màu
 *
 * Ba lựa chọn đã cân (requirements A1):
 *   - nền `--status-*-soft`: dùng `color-mix` nên `check-contrast.mjs` **không**
 *     tính được — tức đặt chữ lên một nền chưa nghiệm ngưỡng.
 *   - nền `--status-*` đặc + chữ trắng: cần **4 cặp mới** kiểu `status-*-on`, mà
 *     chỉ `status-info-on` tồn tại.
 *   - **không nền, chữ màu + viền cùng màu**: giữ mọi chữ trên `surface-panel`,
 *     đúng tập cặp đã có trong `PAIRS` và đã đo đạt. Cùng lý do `<thead>` của
 *     `DataTable` không có nền.
 *
 * `border: 1px solid currentColor` để viền **tự** bằng màu chữ — không có đường
 * nào đặt viền lệch màu chữ. Viền là thành phần UI (ngưỡng 3:1), chữ là 4,5:1;
 * chữ đã đạt thì viền cùng màu đạt theo.
 *
 * ## Vì sao `srLabel`
 *
 * Màu không đọc được bằng screen reader (FR-8). Nội dung slot (vd `"POST"`,
 * `"200"`) tự nó đã là nghĩa và SR đọc được, nên màu **không bao giờ** là kênh
 * duy nhất. `srLabel` là móc thêm ngữ cảnh chỉ-đọc-màn-hình khi consumer muốn
 * (vd `"phương thức"` để SR đọc `"phương thức POST"`); nó nằm **trước** slot.
 *
 * ## Vì sao ở `shared/`, không biết nghiệp vụ
 *
 * Badge không biết HTTP method hay status code là gì — ánh xạ `POST → success`
 * sống ở `features/audit-log/`, ngoài `shared/`. Đó là bất biến I2, và luật gate
 * `ui-no-query-lib` cùng nhóm giữ `shared/ui` sạch khỏi tầng dữ liệu.
 */

export type BadgeLevel = 'neutral' | 'info' | 'success' | 'warning' | 'danger'

withDefaults(defineProps<{ level?: BadgeLevel; srLabel?: string }>(), {
  level: 'neutral',
  srLabel: undefined,
})
</script>

<template>
  <span class="badge" :class="`badge--${level}`">
    <span v-if="srLabel" class="sr-only">{{ srLabel }} </span>
    <slot />
  </span>
</template>

<style scoped>
/*
 * ## Dạng pill có nền nhạt — và vì sao QUYẾT ĐỊNH CŨ ĐÃ ĐẢO
 *
 * Docblock ở đầu file ghi ba lựa chọn và loại nền `--status-*-soft` vì nó dùng
 * `color-mix`, thứ `check-contrast.mjs` không tính được. Lý do đó **đã hết hiệu lực**:
 * bốn token soft nay là **hex đặc** khai trong từng theme (xem `main.css`), và bốn
 * cặp `status-*` trên `status-*-soft` đã vào `PAIRS`. Nền không còn là vùng chưa
 * nghiệm.
 *
 * `[ĐO]` Hình dạng lấy từ Datacake: `border-radius: 9999px`, `padding: 2px 10px`,
 * `font-size: 12px`, `font-weight: 500`, chữ `red-800` trên nền `red-100`.
 *
 * **Viền `currentColor` được GIỮ.** Nó không còn cần cho tương phản, nhưng nó là
 * thứ duy nhất phân biệt badge với nền ở theme tối — nơi bốn token soft bằng đúng
 * `--surface-panel`, tức badge ở đó vẫn là dạng viền. Bỏ viền thì badge biến mất
 * hoàn toàn trong theme tối, và cổng 9 **không bắt được** vì nó chỉ đo chữ-trên-nền,
 * không đo nền-với-nền (`DataTable.css` đã ghi đúng lỗ này).
 */
.badge {
  display: inline-flex;
  align-items: center;
  gap: 0.25rem;
  /* `2px 10px` của Datacake. */
  padding: 0.125rem 0.625rem;
  /* Viền tự bằng màu chữ — không có đường nào đặt nó lệch. */
  border: 1px solid currentcolor;
  /* Bo tròn hết, dạng viên thuốc. */
  border-radius: 9999px;
  font-size: 0.75rem;
  /* 500, không 600 — Datacake dùng 500 và chữ 12px in đậm quá thành nặng. */
  font-weight: 500;
  line-height: 1.35;
  white-space: nowrap;
}

/*
 * `neutral` KHÔNG có token soft riêng và không cần: nó dùng `--surface-overlay`, một
 * bề mặt đã có ba cặp trong `PAIRS` (`text-secondary`/`surface-overlay` là một trong
 * số đó, và đó đúng là cặp của badge này).
 */
.badge--neutral {
  color: var(--text-secondary);
  background-color: var(--surface-overlay);
}

.badge--info {
  color: var(--status-info);
  background-color: var(--status-info-soft);
}

.badge--success {
  color: var(--status-success);
  background-color: var(--status-success-soft);
}

.badge--warning {
  color: var(--status-warning);
  background-color: var(--status-warning-soft);
}

.badge--danger {
  color: var(--status-danger);
  background-color: var(--status-danger-soft);
}
</style>
