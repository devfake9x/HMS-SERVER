<script setup lang="ts">
import { computed } from 'vue'

export interface FieldSpec {
  /** A top-level key, or a dotted path into a nested object ("ota.state"). */
  key: string
  label: string
  unit?: string
  /** 'epoch': seconds since 1970, shown as local date and time. */
  format?: 'epoch'
}

/**
 * Reply data of one read: known fields (from spec §6 and the replies seen on the lab gateway)
 * with labels first, every other top-level field under "Khác". Values of an unexpected type are
 * shown as they are, never dropped and never fatal.
 */
const props = defineProps<{ data: unknown; fields: FieldSpec[] }>()

const record = computed(() =>
  props.data !== null && typeof props.data === 'object' && !Array.isArray(props.data)
    ? props.data as Record<string, unknown>
    : null)

function at(path: string): { found: boolean; value: unknown } {
  let node: unknown = record.value
  for (const part of path.split('.')) {
    if (node === null || typeof node !== 'object' || !(part in (node as Record<string, unknown>)))
      return { found: false, value: undefined }
    node = (node as Record<string, unknown>)[part]
  }
  return { found: true, value: node }
}

const known = computed(() => record.value
  ? props.fields.map(f => ({ ...f, ...at(f.key) })).filter(f => f.found)
  : [])

const extra = computed(() => {
  if (!record.value) return []
  const listed = new Set(props.fields.map(f => f.key.split('.')[0]))
  return Object.entries(record.value).filter(([key]) => !listed.has(key))
})
</script>

<script lang="ts">
/** Display form of a reply value: booleans in words, long fractions to 4 significant digits. */
export function showValue(value: unknown, unit?: string, format?: 'epoch'): string {
  if (value === null || value === undefined || value === '') return '—'
  if (format === 'epoch' && typeof value === 'number' && value > 0)
    return new Date(value * 1000).toLocaleString()
  if (typeof value === 'boolean') return value ? 'Có' : 'Không'
  if (typeof value === 'object') return JSON.stringify(value)
  const text = typeof value === 'number' && !Number.isInteger(value) ? String(Number(value.toPrecision(4))) : String(value)
  return unit ? `${text} ${unit}` : text
}
</script>

<template>
  <pre v-if="!record" class="text-[10px] font-mono whitespace-pre-wrap break-all text-[var(--text-primary)]">{{ JSON.stringify(data) }}</pre>
  <div v-else class="space-y-1">
    <dl v-if="known.length" class="grid grid-cols-[auto_1fr] gap-x-3 gap-y-0.5 text-[11px]">
      <template v-for="field in known" :key="field.key">
        <dt class="text-[var(--text-hint)]">{{ field.label }}</dt>
        <dd class="font-mono text-[var(--text-primary)] break-all">{{ showValue(field.value, field.unit, field.format) }}</dd>
      </template>
    </dl>
    <div v-if="extra.length" data-test="fields-extra">
      <p class="text-[10px] uppercase tracking-wide text-[var(--text-hint)]">Khác</p>
      <dl class="grid grid-cols-[auto_1fr] gap-x-3 gap-y-0.5 text-[11px]">
        <template v-for="[key, value] in extra" :key="key">
          <dt class="font-mono text-[var(--text-hint)]">{{ key }}</dt>
          <dd class="font-mono text-[var(--text-primary)] break-all">{{ showValue(value) }}</dd>
        </template>
      </dl>
    </div>
  </div>
</template>
