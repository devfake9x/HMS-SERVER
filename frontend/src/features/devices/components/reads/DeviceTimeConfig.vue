<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { TIMEZONES } from '@/core/devices/timezones'
import Button from '@/shared/ui/primitives/Button.vue'
import { Pencil, RefreshCw } from '@lucide/vue'
import ConfigWriteConfirm from './ConfigWriteConfirm.vue'
import ConfigWriteResult from './ConfigWriteResult.vue'
import { configGate } from './configGate'
import { validateHostname } from './configRules'
import { hostnameSpec, ntpSyncSpec, timezoneSpec } from './configWriteSpecs'
import type { ConfigEditorProps } from './readCard'
import { useConfigWrite } from './useConfigWrite'

/**
 * Write controls of the "Thiết bị & thời gian" card: hostname, timezone and an NTP sync. NTP
 * servers are shown only; the server keeps system.set_ntp_servers closed. Each control is on only
 * while its gate allows it, and says why when it is not.
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

function gateFor(writeKey: string, readKey: string) {
  return configGate({
    allowed: props.allowedCommands,
    writeKey,
    rereadKeys: [readKey],
    readKey,
    catalogVerified: props.catalogVerified,
    catalogTrusted: props.catalog?.trusted === true,
    link: props.link,
    states: props.states,
    now: props.now,
    configChangedAt: props.configChangedAt,
    writing: flow.active.value,
  })
}
const hostnameGate = computed(() => gateFor('system.set_hostname', 'system.get_hostname'))
const timezoneGate = computed(() => gateFor('system.set_timezone', 'system.get_time'))
const ntpGate = computed(() => gateFor('system.sync_ntp', 'system.get_time'))

const timeData = computed(() => {
  const state = props.states['system.get_time']
  return state?.kind === 'ok' && state.data && typeof state.data === 'object' ? state.data as Record<string, unknown> : null
})
const currentHostname = computed(() => {
  const state = props.states['system.get_hostname']
  const hostname = state?.kind === 'ok' && state.data && typeof state.data === 'object'
    ? (state.data as Record<string, unknown>).hostname : null
  return typeof hostname === 'string' ? hostname : null
})
const currentTimezone = computed(() => typeof timeData.value?.timezone === 'string' ? timeData.value.timezone : null)

const ntpServers = computed(() => {
  const servers = timeData.value?.ntp_servers
  return Array.isArray(servers) && servers.length > 0 ? servers.map(s => typeof s === 'string' ? s : JSON.stringify(s)).join(', ') : '—'
})

// Hostname
const editing = ref(false)
const hostnameDraft = ref('')
const hostnameError = computed(() => editing.value ? validateHostname(hostnameDraft.value) : null)
const hostnameCanSubmit = computed(() =>
  hostnameGate.value.enabled && hostnameError.value === null && currentHostname.value !== null && hostnameDraft.value !== currentHostname.value)

function startEdit() {
  hostnameDraft.value = currentHostname.value ?? ''
  editing.value = true
}
function submitHostname() {
  if (!hostnameCanSubmit.value || currentHostname.value === null) return
  editing.value = false
  flow.request(hostnameSpec(currentHostname.value, hostnameDraft.value))
}

// Timezone
const timezoneDraft = ref('')
watch(currentTimezone, value => { timezoneDraft.value = value ?? '' }, { immediate: true })
const timezoneOptions = computed(() =>
  currentTimezone.value && !TIMEZONES.includes(currentTimezone.value) ? [currentTimezone.value, ...TIMEZONES] : [...TIMEZONES])
const timezoneCanSubmit = computed(() =>
  timezoneGate.value.enabled && currentTimezone.value !== null && TIMEZONES.includes(timezoneDraft.value) && timezoneDraft.value !== currentTimezone.value)

function submitTimezone() {
  if (!timezoneCanSubmit.value || currentTimezone.value === null) return
  flow.request(timezoneSpec(currentTimezone.value, timezoneDraft.value))
}

function syncNtp() {
  if (ntpGate.value.enabled) flow.request(ntpSyncSpec(timeData.value?.ntp_synced))
}

// A different device starts from a clean form.
watch(() => props.deviceId, () => { editing.value = false })
</script>

<template>
  <div class="space-y-2 pt-1 border-t border-[var(--border-subtle)]" data-test="time-config">
    <h4 class="text-[11px] font-semibold text-[var(--text-heading)]">Cấu hình</h4>

    <div class="space-y-1" data-test="hostname-config">
      <div class="flex items-center justify-between gap-2 text-[11px]">
        <span class="text-[var(--text-secondary)]">Hostname: <span class="font-mono text-[var(--text-primary)]">{{ currentHostname ?? '—' }}</span></span>
        <Button
          v-if="!editing"
          variant="outline"
          size="xs"
          :disabled="!hostnameGate.enabled || currentHostname === null"
          aria-label="Sửa hostname"
          data-test="hostname-edit"
          @click="startEdit"
        >
          <Pencil class="w-3 h-3" />
          <span class="ml-1">Sửa</span>
        </Button>
      </div>
      <div v-if="editing" class="flex flex-wrap items-center gap-1.5">
        <input
          v-model="hostnameDraft"
          type="text"
          maxlength="63"
          aria-label="Hostname mới"
          class="px-2 py-1 text-[11px] font-mono rounded border border-[var(--border-default)] bg-[var(--surface-panel)] text-[var(--text-primary)]"
          data-test="hostname-input"
          @keydown.enter.prevent="submitHostname"
        />
        <Button variant="primary" size="xs" :disabled="!hostnameCanSubmit" data-test="hostname-submit" @click="submitHostname">Lưu</Button>
        <Button variant="ghost" size="xs" data-test="hostname-cancel" @click="editing = false">Huỷ</Button>
        <p v-if="hostnameError" class="basis-full text-[10px] text-rose-500" data-test="hostname-error">{{ hostnameError }}</p>
      </div>
      <p v-if="!hostnameGate.enabled" class="text-[10px] text-[var(--text-hint)]" data-test="lock-reason-hostname">{{ hostnameGate.reason }}</p>
    </div>

    <div class="space-y-1" data-test="timezone-config">
      <div class="flex flex-wrap items-center gap-1.5 text-[11px]">
        <label for="timezone-select" class="text-[var(--text-secondary)]">Múi giờ:</label>
        <select
          id="timezone-select"
          v-model="timezoneDraft"
          :disabled="!timezoneGate.enabled || currentTimezone === null"
          class="px-2 py-1 text-[11px] font-mono rounded border border-[var(--border-default)] bg-[var(--surface-panel)] text-[var(--text-primary)]"
          data-test="timezone-select"
        >
          <option v-for="zone in timezoneOptions" :key="zone" :value="zone">{{ zone }}</option>
        </select>
        <Button variant="outline" size="xs" :disabled="!timezoneCanSubmit" data-test="timezone-submit" @click="submitTimezone">Đổi múi giờ</Button>
      </div>
      <p v-if="!timezoneGate.enabled" class="text-[10px] text-[var(--text-hint)]" data-test="lock-reason-timezone">{{ timezoneGate.reason }}</p>
    </div>

    <div class="space-y-1" data-test="ntp-config">
      <div class="flex flex-wrap items-center justify-between gap-1.5 text-[11px]">
        <span class="text-[var(--text-secondary)]" data-test="ntp-servers">
          NTP server: <span class="font-mono text-[var(--text-primary)]">{{ ntpServers }}</span>
          <template v-if="timeData?.ntp_mode"> · chế độ <span class="font-mono text-[var(--text-primary)]">{{ timeData.ntp_mode }}</span></template>
        </span>
        <Button variant="outline" size="xs" :disabled="!ntpGate.enabled" aria-label="Đồng bộ NTP" data-test="ntp-sync" @click="syncNtp">
          <RefreshCw class="w-3 h-3" />
          <span class="ml-1">Đồng bộ NTP</span>
        </Button>
      </div>
      <p v-if="!ntpGate.enabled" class="text-[10px] text-[var(--text-hint)]" data-test="lock-reason-ntp">{{ ntpGate.reason }}</p>
    </div>

    <ConfigWriteConfirm
      v-if="flow.pending.value"
      v-bind="flow.pending.value.confirm"
      @confirm="flow.confirm()"
      @cancel="flow.dismiss()"
    />
    <ConfigWriteResult :result="flow.result.value" :clock="flow.clock.value" />
  </div>
</template>
