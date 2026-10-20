<script setup lang="ts">
/**
 * Chân trang Giám sát Kỹ thuật (Technical Monitoring Footer).
 *
 * Hiển thị nhịp Gateway, chu kỳ lấy mẫu 1.0s, độ trễ và bản quyền.
 */
</script>

<template>
  <footer class="shell__footer">
    <div class="shell__footer-left">
      <span class="shell__footer-pulse" aria-hidden="true" />
      <span class="shell__footer-station">Trạm Gateway #01</span>
      <span class="shell__footer-sep">•</span>
      <span class="shell__footer-status">Đang kết nối</span>
      <span class="shell__footer-sep">•</span>
      <span class="shell__footer-meta">Chu kỳ: 1.0s</span>
      <span class="shell__footer-sep">•</span>
      <span class="shell__footer-meta">Độ trễ: 12ms</span>
    </div>

    <div class="shell__footer-right">
      <span class="shell__footer-brand">Concrete Stress Monitoring v2.0</span>
      <span class="shell__footer-sep">•</span>
      <span class="shell__footer-copy">© 2026</span>
    </div>
  </footer>
</template>

<style src="./AppFooter.css"></style>
