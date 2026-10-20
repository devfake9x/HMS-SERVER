<script setup lang="ts">
import LocaleSwitcher from '#/shared/i18n/LocaleSwitcher.vue'
import ThemeSwitcher from '#/shared/theme/ThemeSwitcher.vue'

/**
 * Khung cho trang chưa đăng nhập: login, quên mật khẩu, đặt lại mật khẩu.
 *
 * Tách khỏi `AppShell` vì trang login **không được** có sidebar và menu — hiện
 * menu cho người chưa đăng nhập là để lộ cấu trúc hệ thống, và mọi mục trong đó
 * đều bấm không được.
 */

defineProps<{
  title: string
  subtitle?: string
}>()
</script>

<template>
  <div class="auth">
    <div class="auth__toolbar">
      <LocaleSwitcher />
      <ThemeSwitcher />
    </div>

    <div class="auth__inner">
      <!-- Thương hiệu đến từ SLOT, không hard-code: `shared/` dùng cho nhiều dự
           án nên nó không được biết tên sản phẩm nào. Base architecture §9.1. -->
      <div v-if="$slots.brand" class="auth__brand">
        <slot name="brand" />
      </div>

      <main class="auth__card">
        <header class="auth__head">
          <!-- h1 vì đây là tiêu đề duy nhất của trang. Một trang không có h1 làm
               người dùng screen reader mất mốc điều hướng. -->
          <h1 class="auth__title">{{ title }}</h1>
          <p v-if="subtitle" class="auth__subtitle">{{ subtitle }}</p>
        </header>

        <slot />

        <footer v-if="$slots.footer" class="auth__foot">
          <slot name="footer" />
        </footer>
      </main>
    </div>
  </div>
</template>

<style scoped>
/*
 * Nền chuyển màu giữa ĐÚNG HAI token bề mặt đã được cổng 9 nghiệm
 * (`--surface-page` và `--surface-overlay`), không phải hai màu tự chọn.
 *
 * Lý do là đo được: cổng chỉ tính tương phản trên màu ĐẶC, nó không đánh giá
 * được một gradient. Nếu hai đầu gradient là token đã có cặp với
 * `--text-primary` thì chữ đặt trên nền này an toàn ở cả hai đầu mà không cần
 * cổng hiểu gradient. Chọn một màu ngoài bảng token là tạo một vùng chưa nghiệm.
 */
.auth {
  position: relative;
  display: grid;
  place-items: center;
  height: 100%;
  min-height: 100dvh;
  overflow-y: auto;
  padding: 1.5rem;
  box-sizing: border-box;
  background-image: linear-gradient(155deg, var(--surface-page) 0%, var(--surface-overlay) 100%);
}

.auth__toolbar {
  position: absolute;
  top: 1rem;
  right: 1rem;
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.auth__inner {
  width: 100%;
  max-width: 26rem;
}

.auth__brand {
  margin-bottom: 1.25rem;
  text-align: center;
}

.auth__card {
  padding: 2rem;
  border: 1px solid var(--border-subtle);
  border-radius: 0.75rem;
  background-color: var(--surface-panel);
  /* Hai lớp: một viền sát để tách khỏi nền, một lớp toả rộng cho chiều sâu.
     Dùng đen trong suốt nên nó đúng ở cả hai theme. */
  box-shadow:
    0 1px 2px rgb(0 0 0 / 6%),
    0 10px 28px rgb(0 0 0 / 8%);
}

.auth__head {
  margin-bottom: 1.5rem;
}

.auth__title {
  margin: 0;
  font-size: 1.25rem;
  font-weight: 600;
  letter-spacing: -0.01em;
}

.auth__subtitle {
  margin: 0.25rem 0 0;
  color: var(--text-secondary);
  font-size: 0.8125rem;
  line-height: 1.4;
}

.auth__foot {
  margin-top: 1.25rem;
  padding-top: 1rem;
  border-top: 1px solid var(--border-subtle);
  color: var(--text-secondary);
  font-size: 0.75rem;
  line-height: 1.5;
}
</style>
