<script setup lang="ts">
import { computed, watch, onMounted, onUnmounted } from 'vue'
import { X } from '@lucide/vue'

defineOptions({
  inheritAttrs: false,
})

const props = withDefaults(
  defineProps<{
    modelValue: boolean
    title?: string
    subtitle?: string
    maxWidth?: 'sm' | 'md' | 'lg' | 'xl' | '2xl' | '4xl'
    showClose?: boolean
  }>(),
  {
    title: '',
    subtitle: '',
    maxWidth: 'lg',
    showClose: true,
  }
)

const emit = defineEmits<{
  (e: 'update:modelValue', value: boolean): void
  (e: 'close'): void
}>()

function close() {
  emit('update:modelValue', false)
  emit('close')
}

function handleKeydown(e: KeyboardEvent) {
  if (e.key === 'Escape' && props.modelValue) {
    close()
  }
}

watch(
  () => props.modelValue,
  (val) => {
    if (typeof document !== 'undefined') {
      if (val) {
        document.body.style.overflow = 'hidden'
      } else {
        document.body.style.overflow = ''
      }
    }
  }
)

onMounted(() => {
  if (typeof window !== 'undefined') {
    window.addEventListener('keydown', handleKeydown)
  }
})

onUnmounted(() => {
  if (typeof window !== 'undefined') {
    window.removeEventListener('keydown', handleKeydown)
  }
  if (typeof document !== 'undefined') {
    document.body.style.overflow = ''
  }
})

const maxWidthClass = computed(() => {
  switch (props.maxWidth) {
    case 'sm':
      return 'max-w-sm'
    case 'md':
      return 'max-w-md'
    case 'xl':
      return 'max-w-xl'
    case '2xl':
      return 'max-w-2xl'
    case '4xl':
      return 'max-w-4xl'
    case 'lg':
    default:
      return 'max-w-lg'
  }
})
</script>

<template>
  <Teleport to="body">
    <div
      v-if="modelValue"
      class="fixed inset-0 z-50 flex items-center justify-center p-4 sm:p-6 overflow-y-auto"
      role="dialog"
      aria-modal="true"
      data-test="modal"
      v-bind="$attrs"
    >
      <!-- Backdrop -->
      <div
        class="fixed inset-0 bg-black/60 backdrop-blur-xs transition-opacity"
        @click="close"
        data-test="modal-backdrop"
      />

      <!-- Dialog Card -->
      <div
        class="relative w-full rounded-lg shadow-2xl transition-all z-10 flex flex-col max-h-[90vh]"
        :class="maxWidthClass"
        style="background-color: var(--surface-card, #ffffff); border: 1px solid var(--border-default, #e5e7eb); color: var(--text-primary, #18181b);"
      >
        <!-- Header -->
        <div
          v-if="title || $slots.header"
          class="flex items-start justify-between p-5 border-b"
          style="border-color: var(--border-default, #e5e7eb);"
        >
          <slot name="header">
            <div>
              <h3 class="text-base font-semibold" style="color: var(--text-heading, var(--text-primary));">
                {{ title }}
              </h3>
              <p
                v-if="subtitle"
                class="mt-1 text-xs"
                style="color: var(--text-secondary, #71717a);"
              >
                {{ subtitle }}
              </p>
            </div>
          </slot>
          <button
            v-if="showClose"
            type="button"
            class="p-1 rounded-md transition-colors hover:opacity-80"
            style="color: var(--text-secondary, #71717a);"
            @click="close"
            data-test="modal-close"
          >
            <X class="w-4 h-4" />
          </button>
        </div>

        <!-- Body -->
        <div class="p-5 overflow-y-auto flex-1">
          <slot />
        </div>

        <!-- Footer -->
        <div
          v-if="$slots.footer"
          class="flex items-center justify-end gap-3 px-5 py-3.5 border-t rounded-b-lg"
          style="background-color: var(--surface-subtle, rgba(0, 0, 0, 0.02)); border-color: var(--border-default, #e5e7eb);"
        >
          <slot name="footer" />
        </div>
      </div>
    </div>
  </Teleport>
</template>
