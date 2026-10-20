<script setup lang="ts">
withDefaults(
  defineProps<{
    type?: 'info' | 'warning' | 'success' | 'danger'
    title?: string
  }>(),
  {
    type: 'info',
    title: undefined,
  },
)
</script>

<template>
  <div class="callout" :class="`callout--${type}`" role="region" :aria-label="title ?? type">
    <div class="callout__icon-wrapper">
      <!-- Icon Info -->
      <svg
        v-if="type === 'info'"
        class="callout__icon"
        viewBox="0 0 20 20"
        fill="currentColor"
        aria-hidden="true"
      >
        <path
          fill-rule="evenodd"
          d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7-4a1 1 0 11-2 0 1 1 0 012 0zM9 9a1 1 0 000 2v3a1 1 0 001 1h1a1 1 0 100-2v-3a1 1 0 00-1-1H9z"
          clip-rule="evenodd"
        />
      </svg>

      <!-- Icon Warning -->
      <svg
        v-else-if="type === 'warning'"
        class="callout__icon"
        viewBox="0 0 20 20"
        fill="currentColor"
        aria-hidden="true"
      >
        <path
          fill-rule="evenodd"
          d="M8.485 2.495c.673-1.167 2.357-1.167 3.03 0l6.28 10.875c.673 1.167-.17 2.625-1.516 2.625H3.72c-1.347 0-2.189-1.458-1.515-2.625L8.485 2.495zM10 5a.75.75 0 01.75.75v3.5a.75.75 0 01-1.5 0v-3.5A.75.75 0 0110 5zm0 9a1 1 0 100-2 1 1 0 000 2z"
          clip-rule="evenodd"
        />
      </svg>

      <!-- Icon Success -->
      <svg
        v-else-if="type === 'success'"
        class="callout__icon"
        viewBox="0 0 20 20"
        fill="currentColor"
        aria-hidden="true"
      >
        <path
          fill-rule="evenodd"
          d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.857-9.809a.75.75 0 00-1.214-.882l-3.483 4.79-1.88-1.88a.75.75 0 10-1.06 1.061l2.5 2.5a.75.75 0 001.137-.089l4-5.5z"
          clip-rule="evenodd"
        />
      </svg>

      <!-- Icon Danger -->
      <svg
        v-else-if="type === 'danger'"
        class="callout__icon"
        viewBox="0 0 20 20"
        fill="currentColor"
        aria-hidden="true"
      >
        <path
          fill-rule="evenodd"
          d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.28 7.22a.75.75 0 00-1.06 1.06L8.94 10l-1.72 1.72a.75.75 0 101.06 1.06L10 11.06l1.72 1.72a.75.75 0 101.06-1.06L11.06 10l1.72-1.72a.75.75 0 00-1.06-1.06L10 8.94 8.28 7.22z"
          clip-rule="evenodd"
        />
      </svg>
    </div>

    <div class="callout__content">
      <h3 v-if="title" class="callout__title">{{ title }}</h3>
      <div class="callout__body">
        <slot />
      </div>
    </div>
  </div>
</template>

<style scoped>
.callout {
  display: flex;
  align-items: flex-start;
  gap: 0.75rem;
  padding: 0.875rem 1rem;
  border-radius: 0.375rem;
  border: 1px solid var(--border-subtle, #e5e7eb);
  background-color: var(--surface-subtle, #f9fafb);
  line-height: 1.45;
}

.callout__icon-wrapper {
  flex-shrink: 0;
  margin-top: 0.125rem;
}

.callout__icon {
  width: 1.125rem;
  height: 1.125rem;
}

.callout__content {
  flex: 1;
  min-width: 0;
}

.callout__title {
  margin: 0 0 0.25rem 0;
  font-size: 0.875rem;
  font-weight: 600;
  line-height: 1.3;
}

.callout__body {
  font-size: 0.8125rem;
}

.callout__body :deep(p) {
  margin: 0;
}

.callout__body :deep(p + p) {
  margin-top: 0.375rem;
}

/* Info Variant */
.callout--info {
  background-color: var(--surface-info-subtle, #eff6ff);
  border-color: var(--border-info-subtle, #bfdbfe);
  color: var(--text-info-body, #1d4ed8);
}
.callout--info .callout__icon {
  color: var(--status-info, #2563eb);
}
.callout--info .callout__title {
  color: var(--text-info-title, #1e40af);
}

/* Warning Variant */
.callout--warning {
  background-color: var(--surface-warning-subtle, #fefce8);
  border-color: var(--border-warning-subtle, #fef08a);
  color: var(--text-warning-body, #854d0e);
}
.callout--warning .callout__icon {
  color: var(--status-warning, #ca8a04);
}
.callout--warning .callout__title {
  color: var(--text-warning-title, #713f12);
}

/* Success Variant */
.callout--success {
  background-color: var(--surface-success-subtle, #f0fdf4);
  border-color: var(--border-success-subtle, #bbf7d0);
  color: var(--text-success-body, #15803d);
}
.callout--success .callout__icon {
  color: var(--status-success, #16a34a);
}
.callout--success .callout__title {
  color: var(--text-success-title, #14532d);
}

/* Danger Variant */
.callout--danger {
  background-color: var(--surface-danger-subtle, #fef2f2);
  border-color: var(--border-danger-subtle, #fecaca);
  color: var(--text-danger-body, #b91c1c);
}
.callout--danger .callout__icon {
  color: var(--status-danger, #dc2626);
}
.callout--danger .callout__title {
  color: var(--text-danger-title, #7f1d1d);
}
</style>
