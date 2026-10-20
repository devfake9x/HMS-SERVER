<script setup lang="ts">
/**
 * Thanh điều phối các thao tác đầu trang chuẩn Datacake.
 *
 * ## Đặc tính cốt lõi
 * - Khóa cứng trên 1 hàng ngang duy nhất (`flex-wrap: nowrap`).
 * - Tích hợp CSS Container Queries (`container-type: inline-size`) để điều phối
 *   các nút con co về dạng icon khi không gian hẹp.
 * - Chiều cao chuẩn hóa 38px cho toàn bộ các phần tử con.
 */
defineOptions({
  name: 'ActionToolbar',
})
</script>

<template>
  <div class="action-toolbar" role="toolbar" aria-label="Thanh công cụ thao tác dữ liệu">
    <div v-if="$slots.search" class="action-toolbar__search">
      <slot name="search" />
    </div>

    <div v-if="$slots.columns" class="action-toolbar__item">
      <slot name="columns" />
    </div>

    <div v-if="$slots.default || $slots.actions" class="action-toolbar__item action-toolbar__item--primary">
      <slot name="actions">
        <slot />
      </slot>
    </div>
  </div>
</template>

<style scoped>
.action-toolbar {
  container-type: inline-size;
  container-name: actiontoolbar;
  display: flex;
  flex-direction: row;
  flex-wrap: nowrap;
  align-items: center;
  justify-content: flex-end;
  gap: 0.5rem;
  flex-shrink: 0;
  min-width: 0;
}

.action-toolbar__search {
  flex: 1 1 15rem;
  min-inline-size: 7.5rem;  /* 120px */
  max-inline-size: 17.5rem; /* 280px */
}

.action-toolbar__item {
  display: inline-flex;
  align-items: center;
  flex-shrink: 0;
}

/* Quy tắc co gọn đồng bộ theo Container Queries */
@container actiontoolbar (max-width: 460px) {
  .action-toolbar__search {
    flex: 1 1 8rem;
    min-inline-size: 7rem;
  }
}
</style>
