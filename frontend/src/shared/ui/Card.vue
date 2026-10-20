<script setup lang="ts">
/**
 * Bề mặt panel: nền `--surface-panel`, viền, bo góc. Spec 007 §B.2.
 *
 * ## Nó sửa một giả định đang SAI, không chỉ để trông đẹp
 *
 * `[SUY LUẬN]` `main.css` đặt `body { background-color: var(--surface-page) }`, và
 * `AppShell.css` `.shell__main` **không** khai `background`. Truy cả chuỗi tổ tiên
 * của bảng audit-log (`body` → `.shell` → `.shell__main` → view → `.data-table`)
 * thì không một cái nào đặt nền. Nhưng `DataTable.css` tự khai bất biến của nó là
 * *"mọi chữ vẫn nằm trên `--surface-panel` — đúng cặp cổng 9 đã nghiệm"*.
 *
 * Hai câu đó không thể cùng đúng. Không có lỗi tương phản (cặp `text-*` trên
 * `--surface-page` **cũng** có trong `PAIRS`), nhưng **giả định ghi trong file thì
 * sai** — và một bất biến sai là thứ người sau sẽ dựa vào. Component này làm câu
 * đó thành đúng, thay vì đi nới câu đó cho khớp hiện trạng.
 *
 * ## KHÔNG khai `overflow` — luật gate `card-no-overflow` giữ điều này
 *
 * `[SUY LUẬN]` `DataTable.css` ghi rõ cơ chế cuộn ngang: *"`overflow-x: auto` một
 * mình KHÔNG cuộn — nó chỉ bóp cột. Phải có cái gì cưỡng bức bề rộng, và ở đây là
 * `white-space: nowrap` trên `<th>`"*. Một `overflow: hidden` ở tổ tiên tạo scroll
 * container mới và **giết** cơ chế đó: cột bị bóp, cột cuối biến mất, và **không
 * có dấu hiệu nào** cho biết có nội dung bị cắt.
 *
 * Đường dẫn tới đó rất ngắn và trông hợp lý: bo góc + bảng vuông ⇒ góc bảng chọc
 * ra ⇒ thêm `overflow: hidden` ⇒ review qua. jsdom không có layout engine nên
 * **không test nào đỏ**. Vì vậy nó bị chặn bằng cổng tĩnh, không bằng lời nhắc.
 *
 * Bo góc cho bảng giải bằng `border-radius` trên chính bảng, không bằng cắt ở
 * tổ tiên.
 */

withDefaults(
  defineProps<{
    /**
     * Bỏ padding trong. Dùng khi nội dung tự quản khoảng cách của nó — bảng là ca
     * chính: `td` có padding riêng, và padding của card làm viền `td` không chạm
     * biên card.
     */
    flush?: boolean
    /**
     * `comfortable` = py-6 px-4 sm:p-6 của Datacake (24px, cho form/settings);
     * mặc định 1rem giữ nguyên cho các card bảng/danh sách đã cân.
     */
    padding?: 'default' | 'comfortable'
  }>(),
  { flush: false, padding: 'default' },
)
</script>

<template>
  <div class="card" :class="[flush ? 'card--flush' : '', padding === 'comfortable' ? 'card--comfortable' : '']">
    <slot />
  </div>
</template>

<style scoped>
/* Khung Datacake: card có viền 4 cạnh 1px solid var(--border-default) + bo góc 0.5rem (8px),
 * bóng mỏng nhẹ nhàng, đảm bảo khung thông tin tách biệt rõ ràng và sắc nét. */
.card {
  background-color: var(--surface-panel);
  border: 1px solid var(--border-default, #e5e7eb);
  border-radius: 0.5rem;
  box-shadow: 0 1px 3px 0 rgb(0 0 0 / 0.05), 0 1px 2px -1px rgb(0 0 0 / 0.05);
  padding: 1rem;
}

/* flush: nội dung tự quản khung riêng (bảng có border riêng) */
.card--flush {
  padding: 0;
}

.card--comfortable {
  padding: 1.25rem 1rem;
}

@media (min-width: 40rem) {
  .card--comfortable {
    padding: 1.5rem;
  }
}

@media (min-width: 64rem) {
  .card--comfortable {
    padding: 1.75rem;
  }
}
</style>
