<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import Button from '@/shared/ui/primitives/Button.vue'
import { Pencil, Power } from '@lucide/vue'
import { adcRows, adcUnavailable, channelRecord } from './adcData'
import ConfigWriteConfirm from './ConfigWriteConfirm.vue'
import ConfigWriteResult from './ConfigWriteResult.vue'
import { configGate } from './configGate'
import { isValidAi, validateChannelName } from './configRules'
import { adcRenameSpec, adcToggleSpec } from './configWriteSpecs'
import { showValue } from './ReadFieldList.vue'
import type { ConfigEditorProps } from './readCard'
import { useConfigWrite } from './useConfigWrite'

/**
 * ADC channels joined to their values, with a rename and an on/off control per channel. A control
 * exists only for a channel that get_channels reported with a valid `ai`, and never when the ADC
 * has no hardware. The user edits only the name or the switch; type and enabled go back unchanged.
 */
const props = defineProps<ConfigEditorProps>()

const flow = useConfigWrite({
  deviceId: () => props.deviceId,
  context: () => ({ csrfToken: props.csrfToken, catalog: props.catalog }),
  refresh: () => props.refresh,
  lockReads: () => props.lockReads,
  states: () => props.states,
  configChangedAt: () => props.configChangedAt,
})

const rows = computed(() => adcRows(props.states))
const controllable = computed(() => !adcUnavailable(props.states))
const hasControls = computed(() => controllable.value && rows.value.some(r => r.inChannels && isValidAi(r.ai)))

function gateFor(writeKey: string) {
  return configGate({
    allowed: props.allowedCommands,
    writeKey,
    rereadKeys: ['adc.get_channels', 'adc.get_values'],
    readKey: 'adc.get_channels',
    catalogVerified: props.catalogVerified,
    catalogTrusted: props.catalog?.trusted === true,
    link: props.link,
    states: props.states,
    now: props.now,
    configChangedAt: props.configChangedAt,
    writing: flow.active.value,
  })
}
const renameGate = computed(() => gateFor('adc.set_channel'))
const toggleGate = computed(() => gateFor('adc.toggle_channel'))

const renaming = ref<number | null>(null)
const draft = ref('')
const draftError = computed(() => renaming.value === null ? null : validateChannelName(draft.value))

const canControl = (r: { ai: number; inChannels: boolean }) => controllable.value && r.inChannels && isValidAi(r.ai)

function startRename(ai: number, name: unknown) {
  draft.value = typeof name === 'string' ? name : ''
  renaming.value = ai
}

function submitRename() {
  const ai = renaming.value
  const before = ai === null ? null : channelRecord(props.states['adc.get_channels'], ai)
  if (ai === null || !before || draftError.value !== null || !renameGate.value.enabled || draft.value === before.name) return
  renaming.value = null
  flow.request(adcRenameSpec(before, ai, draft.value))
}

function toggle(ai: number) {
  const before = channelRecord(props.states['adc.get_channels'], ai)
  if (!before || typeof before.enabled !== 'boolean' || !toggleGate.value.enabled) return
  flow.request(adcToggleSpec(before, ai, !before.enabled))
}

watch(() => props.deviceId, () => { renaming.value = null })
</script>

<template>
  <div class="space-y-1.5">
    <table v-if="rows.length" class="w-full text-[11px] font-mono" data-test="adc-table">
      <thead class="text-[10px] text-[var(--text-hint)] text-left">
        <tr>
          <th>AI</th><th>Tên</th><th>Loại</th><th>Bật</th><th>Giá trị</th><th>Chất lượng</th>
          <th v-if="hasControls"><span class="sr-only">Thao tác</span></th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="r in rows" :key="r.ai" :data-test="`adc-row-${r.ai}`" class="border-t border-[var(--border-subtle)]">
          <td>{{ r.ai }}</td>
          <td class="font-sans">{{ showValue(r.name) }}</td>
          <td>{{ showValue(r.type) }}</td>
          <td>{{ showValue(r.enabled) }}</td>
          <td>{{ showValue(r.value, typeof r.unit === 'string' ? r.unit : undefined) }}</td>
          <td>{{ showValue(r.quality) }}</td>
          <td v-if="hasControls" class="whitespace-nowrap text-right">
            <template v-if="canControl(r)">
              <Button
                variant="ghost"
                size="xs"
                :disabled="!renameGate.enabled"
                :aria-label="`Đổi tên kênh AI-${r.ai}`"
                :data-test="`adc-rename-${r.ai}`"
                @click="startRename(r.ai, r.name)"
              >
                <Pencil class="w-3 h-3" />
              </Button>
              <Button
                v-if="typeof r.enabled === 'boolean'"
                variant="ghost"
                size="xs"
                role="switch"
                :aria-checked="r.enabled"
                :disabled="!toggleGate.enabled"
                :aria-label="`${r.enabled ? 'Tắt' : 'Bật'} kênh AI-${r.ai}`"
                :data-test="`adc-toggle-${r.ai}`"
                @click="toggle(r.ai)"
              >
                <Power class="w-3 h-3" />
              </Button>
            </template>
          </td>
        </tr>
      </tbody>
    </table>

    <div v-if="renaming !== null" class="flex flex-wrap items-center gap-1.5">
      <label :for="`adc-name-${renaming}`" class="text-[11px] text-[var(--text-secondary)]">Tên kênh AI-{{ renaming }}:</label>
      <input
        :id="`adc-name-${renaming}`"
        v-model="draft"
        type="text"
        class="px-2 py-1 text-[11px] rounded border border-[var(--border-default)] bg-[var(--surface-panel)] text-[var(--text-primary)]"
        :data-test="`adc-name-input-${renaming}`"
        @keydown.enter.prevent="submitRename"
      />
      <Button variant="primary" size="xs" :disabled="draftError !== null || !renameGate.enabled" :data-test="`adc-name-submit-${renaming}`" @click="submitRename">Lưu</Button>
      <Button variant="ghost" size="xs" @click="renaming = null">Huỷ</Button>
      <p v-if="draftError" class="basis-full text-[10px] text-rose-500" data-test="adc-name-error">{{ draftError }}</p>
    </div>

    <template v-if="hasControls">
      <p v-if="!renameGate.enabled" class="text-[10px] text-[var(--text-hint)]" data-test="lock-reason-adc-rename">Đổi tên: {{ renameGate.reason }}</p>
      <p v-if="!toggleGate.enabled" class="text-[10px] text-[var(--text-hint)]" data-test="lock-reason-adc-toggle">Bật/tắt kênh: {{ toggleGate.reason }}</p>
    </template>

    <ConfigWriteConfirm
      v-if="flow.pending.value"
      v-bind="flow.pending.value.confirm"
      @confirm="flow.confirm()"
      @cancel="flow.dismiss()"
    />
    <ConfigWriteResult v-if="hasControls || flow.result.value" :result="flow.result.value" :clock="flow.clock.value" />
  </div>
</template>
