<script setup lang="ts">
import { computed, useId } from 'vue'

import { LOCALE_PREFERENCES, useLocaleStore, type LocalePreference } from './locale-store'

/**
 * Bộ chuyển đổi ngôn ngữ giao diện (VI / EN).
 *
 * Dùng <fieldset> + radio native để có sẵn keyboard navigation (ArrowLeft/Right, Tab)
 * và a11y đầy đủ từ trình duyệt.
 */

import { ref } from 'vue'
import { readStoredLocale } from './locale-store'

let locale: any
try {
  locale = useLocaleStore()
} catch {
  const fallback = ref(readStoredLocale())
  locale = {
    current: fallback,
    setLocale: (next: LocalePreference) => { fallback.value = next },
  }
}
const groupName = useId()

const selected = computed<LocalePreference>({
  get: () => locale.current,
  set: (next) => locale.setLocale(next),
})

const LABELS: Readonly<Record<LocalePreference, string>> = {
  vi: 'VI',
  en: 'EN',
}

const FULL_TITLES: Readonly<Record<LocalePreference, string>> = {
  vi: 'Tiếng Việt',
  en: 'English',
}
</script>

<template>
  <fieldset class="localeswitch">
    <legend class="sr-only">Ngôn ngữ giao diện</legend>

    <label
      v-for="option in LOCALE_PREFERENCES"
      :key="option"
      class="localeswitch__option"
      :title="FULL_TITLES[option]"
    >
      <input
        v-model="selected"
        class="localeswitch__input"
        type="radio"
        :name="groupName"
        :value="option"
      />
      <span class="localeswitch__label">{{ LABELS[option] }}</span>
    </label>
  </fieldset>
</template>

<style scoped src="./LocaleSwitcher.css"></style>
