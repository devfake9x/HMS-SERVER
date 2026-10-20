<script setup lang="ts">
import { onMounted, onUnmounted, ref } from 'vue'

import {
  notify,
  useNotifications,
  useNotifyPaused,
  type Notification,
  type NotifyLevel,
} from './notify'

/**
 * Nơi hiện thông báo hệ thống — clone [ĐO 2026-09-05] Datacake.
 *
 * Datacake chạy react-toastify v11 với GHI ĐÈ nền tinted theo mức: error
 * `red-100` + chữ `red-700`, success `green-100`/`green-700`, warning
 * `yellow-50`/`yellow-700`, info nền trắng. Bản "Sonner glass" cũ (kính mờ,
 * viền trái 3px, progress gradient) không tồn tại trên Datacake — đã bỏ.
 *
 * Quyết định kiến trúc & tương tác (giữ nguyên từ bản trước):
 *   - Phân biệt click thường (< 6px) và bôi đen copy mã lỗi (> 6px).
 *   - Nút đóng X đúng mark-up v11: viewBox "0 0 14 16" fill currentColor,
 *     hitbox 24×24px; opacity 0.3 → 1 khi hover/focus (đo trên Datacake).
 *   - Icon 4 mức là path FILL gốc của react-toastify v11 — trích từ
 *     react-toastify@11.0.5/dist/index.mjs, đúng bản Datacake bundle.
 *   - Thanh tiến trình ĐẶC 5px (không gradient), dừng khi hover/focus.
 *   - Vào/ra bằng keyframes bounce gốc Toastify__bounceInRight/OutRight.
 * Chi tiết màu & số đo: đầu file ToastHost.css; token ở app/styles/main.css.
 */

const notifications = useNotifications()
const isPaused = useNotifyPaused()
const theme = ref<'light' | 'dark'>('light')

let observer: MutationObserver | null = null

onMounted(() => {
  if (typeof document !== 'undefined') {
    const updateTheme = () => {
      theme.value = document.documentElement.dataset.theme === 'dark' ? 'dark' : 'light'
    }
    updateTheme()
    if (typeof MutationObserver !== 'undefined') {
      observer = new MutationObserver(updateTheme)
      observer.observe(document.documentElement, {
        attributes: true,
        attributeFilter: ['data-theme'],
      })
    }
  }
})

onUnmounted(() => {
  observer?.disconnect()
  observer = null
})

const LEVEL_LABEL: Readonly<Record<NotifyLevel, string>> = {
  success: 'Thành công',
  info: 'Thông tin',
  warning: 'Cảnh báo',
  error: 'Lỗi',
}

let startPos: { x: number; y: number } | null = null

function handleMouseDown(e: MouseEvent): void {
  startPos = { x: e.clientX, y: e.clientY }
}

function handleToastClick(n: Notification, e: MouseEvent): void {
  // Micro-drag check: Nếu kéo chuột thực sự > 6px VÀ có text được bôi đen -> giữ thẻ để người dùng copy
  if (startPos) {
    const dist = Math.hypot(e.clientX - startPos.x, e.clientY - startPos.y)
    startPos = null
    if (dist >= 6 && typeof window !== 'undefined' && window.getSelection?.()?.toString().trim()) {
      return
    }
  } else if (typeof window !== 'undefined' && window.getSelection?.()?.toString().trim()) {
    return
  }

  if (n.closeOnClick) {
    notify.dismiss(n.id)
  }
}
</script>

<template>
  <Teleport to="body">
    <div
      class="Toastify notify-host"
      role="region"
      aria-label="Thông báo hệ thống"
      @keydown.esc="notify.clear()"
    >
      <div class="Toastify__toast-container Toastify__toast-container--top-right">
        <TransitionGroup name="notify">
          <div
            v-for="(n, index) in notifications"
            :id="n.id"
            :key="n.id"
            class="Toastify__toast notify-item"
            :class="[
              `Toastify__toast-theme--${theme}`,
              `Toastify__toast--${n.level}`,
              `notify-item--${n.level}`,
              { 'Toastify__toast--close-on-click': n.closeOnClick },
            ]"
            :role="n.level === 'error' ? 'alert' : 'status'"
            :style="{ '--nth': index + 1, '--len': notifications.length }"
            @mousedown="handleMouseDown"
            @click="handleToastClick(n, $event)"
            @mouseenter="notify.pauseAll()"
            @mouseleave="notify.resumeAll()"
            @focusin="notify.pauseAll()"
            @focusout="notify.resumeAll()"
          >
            <div
              :role="n.level === 'error' ? 'alert' : 'status'"
              class="Toastify__toast-body notify-body"
            >
              <!-- Icon FILL gốc react-toastify v11 (đo trên Datacake) -->
              <div class="Toastify__toast-icon Toastify--animate-icon Toastify__zoom-enter notify-icon">
                <svg
                  v-if="n.level === 'success'"
                  viewBox="0 0 24 24"
                  aria-hidden="true"
                >
                  <path
                    d="M12 0a12 12 0 1012 12A12.014 12.014 0 0012 0zm6.927 8.2l-6.845 9.289a1.011 1.011 0 01-1.43.188l-4.888-3.908a1 1 0 111.25-1.562l4.076 3.261 6.227-8.451a1 1 0 111.61 1.183z"
                  />
                </svg>

                <svg
                  v-else-if="n.level === 'info'"
                  viewBox="0 0 24 24"
                  aria-hidden="true"
                >
                  <path
                    d="M12 0a12 12 0 1012 12A12.013 12.013 0 0012 0zm.25 5a1.5 1.5 0 11-1.5 1.5 1.5 1.5 0 011.5-1.5zm2.25 13.5h-4a1 1 0 010-2h.75a.25.25 0 00.25-.25v-4.5a.25.25 0 00-.25-.25h-.75a1 1 0 010-2h1a2 2 0 012 2v4.75a.25.25 0 00.25.25h.75a1 1 0 110 2z"
                  />
                </svg>

                <svg
                  v-else-if="n.level === 'warning'"
                  viewBox="0 0 24 24"
                  aria-hidden="true"
                >
                  <path
                    d="M23.32 17.191L15.438 2.184C14.728.833 13.416 0 11.996 0c-1.42 0-2.733.833-3.443 2.184L.533 17.448a4.744 4.744 0 000 4.368C1.243 23.167 2.555 24 3.975 24h16.05C22.22 24 24 22.044 24 19.632c0-.904-.251-1.746-.68-2.44zm-9.622 1.46c0 1.033-.724 1.823-1.698 1.823s-1.698-.79-1.698-1.822v-.043c0-1.028.724-1.822 1.698-1.822s1.698.79 1.698 1.822v.043zm.039-12.285l-.84 8.06c-.057.581-.408.943-.897.943-.49 0-.84-.367-.896-.942l-.84-8.065c-.057-.624.25-1.095.779-1.095h1.91c.528.005.84.476.784 1.1z"
                  />
                </svg>

                <svg
                  v-else-if="n.level === 'error'"
                  viewBox="0 0 24 24"
                  aria-hidden="true"
                >
                  <path
                    d="M11.983 0a12.206 12.206 0 00-8.51 3.653A11.8 11.8 0 000 12.207 11.779 11.779 0 0011.8 24h.214A12.111 12.111 0 0024 11.791 11.766 11.766 0 0011.983 0zM10.5 16.542a1.476 1.476 0 011.449-1.53h.027a1.527 1.527 0 011.523 1.47 1.475 1.475 0 01-1.449 1.53h-.027a1.529 1.529 0 01-1.523-1.47zM11 12.5v-6a1 1 0 012 0v6a1 1 0 11-2 0z"
                  />
                </svg>
              </div>

              <!-- Nội dung thông báo -->
              <div class="Toastify__content-wrap">
                <p class="Toastify__message notify-message">
                  <span class="sr-only">{{ LEVEL_LABEL[n.level] }}: </span>{{ n.message }}
                  <span v-if="n.count > 1" class="Toastify__count notify-count">
                    <span aria-hidden="true">×{{ n.count }}</span>
                    <span class="sr-only">(lặp {{ n.count }} lần)</span>
                  </span>
                </p>
                <p v-if="n.detail" class="Toastify__detail notify-detail">{{ n.detail }}</p>
              </div>
            </div>

            <!-- Nút đóng X Hitbox chuẩn 24×24px -->
            <button
              type="button"
              class="Toastify__close-button notify-close"
              :class="`Toastify__close-button--${theme}`"
              :aria-label="`Đóng thông báo: ${n.message}`"
              @click.stop="notify.dismiss(n.id)"
            >
              <svg aria-hidden="true" viewBox="0 0 14 16">
                <path
                  fill-rule="evenodd"
                  d="M7.71 8.23l3.75 3.75-1.48 1.48-3.75-3.75-3.75 3.75L1 11.98l3.75-3.75L1 4.48 2.48 3l3.75 3.75L9.98 3l1.48 1.48-3.75 3.75z"
                />
              </svg>
            </button>

            <!-- Thanh tiến trình tự hủy siêu mảnh 2px -->
            <div
              v-if="n.timeoutMs > 0"
              :key="`${n.id}-${n.count}`"
              role="progressbar"
              aria-hidden="false"
              aria-label="notification timer"
              class="Toastify__progress-bar Toastify__progress-bar--animated"
              :class="[
                `Toastify__progress-bar-theme--${theme}`,
                `Toastify__progress-bar--${n.level}`,
              ]"
              :style="{
                animationDuration: `${n.timeoutMs}ms`,
                animationPlayState: isPaused ? 'paused' : 'running',
              }"
            />
          </div>
        </TransitionGroup>
      </div>
    </div>
  </Teleport>
</template>

<!-- CSS ra file riêng vì vượt ~50 dòng — Base architecture §2.4.
     KHÔNG `scoped` có chủ đích: selector `:root` bị scope hoá thành
     `:root[data-v-x]` — không bao giờ khớp — làm CHẾT cả block biến
     `--toastify-*` (đo thật: toast rộng 292px fit-content, min-height auto).
     Toàn bộ class trong file đều có prefix riêng (Toastify__ / notify-) nên
     global là an toàn; token màu thì nằm ở app/styles/main.css. -->
<style src="./ToastHost.css"></style>
