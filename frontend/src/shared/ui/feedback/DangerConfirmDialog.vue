<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { AlertTriangle } from '@lucide/vue'
import Modal from './Modal.vue'
import Button from '../primitives/Button.vue'

defineOptions({
  inheritAttrs: false,
})

const props = withDefaults(
  defineProps<{
    modelValue: boolean
    title: string
    message: string
    confirmPhrase?: string
    dangerActionText?: string
    loading?: boolean
  }>(),
  {
    confirmPhrase: '',
    dangerActionText: 'Confirm Delete',
    loading: false,
  }
)

const emit = defineEmits<{
  (e: 'update:modelValue', value: boolean): void
  (e: 'confirm'): void
  (e: 'cancel'): void
}>()

const userInput = ref('')

watch(
  () => props.modelValue,
  (val) => {
    if (val) {
      userInput.value = ''
    }
  }
)

const isConfirmedValid = computed(() => {
  if (!props.confirmPhrase) return true
  return userInput.value.trim() === props.confirmPhrase.trim()
})

function onCancel() {
  emit('update:modelValue', false)
  emit('cancel')
}

function onConfirm() {
  if (isConfirmedValid.value && !props.loading) {
    emit('confirm')
  }
}
</script>

<template>
  <Modal
    :model-value="modelValue"
    @update:model-value="emit('update:modelValue', $event)"
    max-width="md"
    data-test="danger-confirm-dialog"
  >
    <div class="flex items-start gap-4">
      <div class="flex-shrink-0 w-10 h-10 rounded-full bg-rose-500/10 text-rose-600 dark:text-rose-400 flex items-center justify-center">
        <AlertTriangle class="w-5 h-5" />
      </div>
      <div class="flex-1">
        <h3 class="text-base font-semibold text-zinc-900 dark:text-zinc-100">
          {{ title }}
        </h3>
        <p class="mt-2 text-xs text-zinc-600 dark:text-zinc-400 leading-relaxed">
          {{ message }}
        </p>

        <div v-if="confirmPhrase" class="mt-4 p-3 bg-zinc-100/80 dark:bg-zinc-800/60 rounded-lg border border-zinc-200 dark:border-zinc-700/60 text-xs">
          <p class="text-zinc-600 dark:text-zinc-400 font-medium">
            Please type <span class="font-mono font-bold text-rose-600 dark:text-rose-400 select-all">{{ confirmPhrase }}</span> to confirm:
          </p>
          <input
            v-model="userInput"
            type="text"
            class="mt-2 w-full px-3 py-1.5 text-xs rounded-md bg-white dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 focus:outline-none focus:ring-2 focus:ring-rose-500/40 font-mono text-zinc-900 dark:text-zinc-100"
            placeholder="Type confirmation phrase..."
            data-test="danger-confirm-input"
            @keydown.enter.prevent="onConfirm"
          />
        </div>
      </div>
    </div>

    <template #footer>
      <Button
        variant="ghost"
        @click="onCancel"
        :disabled="loading"
        data-test="danger-cancel-btn"
      >
        Cancel
      </Button>
      <Button
        variant="danger"
        :loading="loading"
        :disabled="!isConfirmedValid || loading"
        @click="onConfirm"
        data-test="danger-confirm-btn"
      >
        {{ dangerActionText }}
      </Button>
    </template>
  </Modal>
</template>
