<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { type CommandCatalogSnapshot } from '@/core/api/adminApi'
import { canRenderCommand, findCatalogAction, type CatalogAction } from '@/core/commands/commandAvailability'
import { useDeviceCommand } from '@/core/commands/useDeviceCommand'
import { can as canPermission, type PermissionSnapshot } from '@/core/auth/permissions'
import Modal from '@/shared/ui/feedback/Modal.vue'
import DangerConfirmDialog from '@/shared/ui/feedback/DangerConfirmDialog.vue'
import Button from '@/shared/ui/primitives/Button.vue'
import Badge from '@/shared/ui/primitives/Badge.vue'
import Select, { type SelectOption } from '@/shared/ui/Select.vue'
import { Terminal, ShieldAlert, CheckCircle2, Play, Lock } from '@lucide/vue'

const props = defineProps<{
  modelValue: boolean
  deviceId: string
  deviceName: string
  catalog: CommandCatalogSnapshot | null
  catalogProof: { version: string; hash: string } | null
  catalogFresh: boolean
  permissions: PermissionSnapshot | null
  csrfToken: string | null
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', val: boolean): void
  (e: 'command-executed', result: any): void
}>()

const selectedType = ref<string | null>(null)
const selectedAction = ref<string | null>(null)
const correlationId = ref('')
const executionError = ref('')

// The device answers on MQTT after the API call returns "Published"; the composable reads the
// outcome back until the device replies or the server records a timeout.
const command = useDeviceCommand(() => props.deviceId, () => props.csrfToken)
const executing = computed(() => command.phase.value === 'sending')
const executionResult = computed(() => command.dispatch.value)
const awaitingDevice = computed(() => command.phase.value === 'waiting')
const deviceOutcome = computed(() => command.outcome.value?.final ?? null)
const clientTimedOut = computed(() => command.phase.value === 'timeout')

const dispatchAccepted = computed(() => {
  const status = executionResult.value?.status
  return status === 'Published' || status === 'Acknowledged' || status === 'Executed' || status === 'Recorded' || status === 'MqttDeferred'
})

const outcomeStatus = computed(() => deviceOutcome.value?.result?.status ?? null)
const replyJson = computed(() => {
  const data = deviceOutcome.value?.reply?.data
  return data === undefined || data === null ? null : JSON.stringify(data, null, 2)
})

watch(() => props.modelValue, open => {
  if (!open) command.cancel()
})
watch(() => command.dispatch.value, sent => {
  if (sent) emit('command-executed', sent)
})
const showDangerConfirm = ref(false)
const rawParamsJson = ref('{}')
const paramsJsonError = ref('')

// Relay outputs are driven only from the DO panel, which locks the channel and reads the state
// back after every command; this generic dialog does neither.
const PANEL_ONLY_ACTIONS = new Set(['control.set_output', 'control.pulse', 'control.set_all'])

const catalogActions = computed<CatalogAction[]>(() =>
  (props.catalog?.actions ?? []).filter(a => !PANEL_ONLY_ACTIONS.has(`${a.type}.${a.action}`)))

const availableTypes = computed(() => {
  const types = new Set<string>()
  for (const act of catalogActions.value) {
    if (act.type) types.add(act.type)
  }
  return Array.from(types)
})

const typeOptions = computed<SelectOption[]>(() =>
  availableTypes.value.map(t => ({ value: t, label: t }))
)

const availableActionsForType = computed(() => {
  if (!selectedType.value) return []
  return catalogActions.value.filter(a => a.type === selectedType.value)
})

const actionOptions = computed<SelectOption[]>(() =>
  availableActionsForType.value.map(a => ({ value: a.action, label: a.action }))
)

const currentAction = computed(() => {
  return findCatalogAction(catalogActions.value, selectedType.value || '', selectedAction.value || '')
})

const isDestructive = computed(() => {
  const act = currentAction.value
  if (!act) return false
  const name = `${act.type}.${act.action}`.toLowerCase()
  return name.includes('reboot') || name.includes('reset') || name.includes('wipe') || name.includes('format') || name.includes('revoke')
})

// The catalog API names the spec §11 permission `requiredPermission`; older snapshots used
// `featurePermission`. The server re-checks it against the device either way.
const requiredPermission = computed(() =>
  currentAction.value?.requiredPermission ?? currentAction.value?.featurePermission ?? '')

const hasPermission = computed(() => {
  if (!currentAction.value) return false
  return canPermission(props.permissions, requiredPermission.value)
})

const isCatalogVerified = computed(() => {
  if (!props.catalog?.trusted || !currentAction.value) return false
  return (
    props.catalogFresh &&
    props.catalogProof !== null &&
    canRenderCommand(currentAction.value, props.catalogProof, {
      version: props.catalog.catalogVersion,
      hash: props.catalog.catalogHash,
    })
  )
})

watch([selectedType, selectedAction], () => {
  const t = selectedType.value
  const a = selectedAction.value
  if (!t || !a) {
    rawParamsJson.value = '{}'
    return
  }
  if (t === 'system' && a === 'reboot') {
    rawParamsJson.value = JSON.stringify({ confirm_token: `REBOOT-${props.deviceId}` }, null, 2)
  } else if (t === 'system' && a === 'factory_reset') {
    rawParamsJson.value = JSON.stringify({ confirm_token: `FACTORY-RESET-${props.deviceId}` }, null, 2)
  } else if (t === 'ota' && a === 'apply') {
    rawParamsJson.value = JSON.stringify({ confirm_token: `OTA-APPLY-${props.deviceId}` }, null, 2)
  } else if (t === 'sms' && a === 'list') {
    rawParamsJson.value = JSON.stringify({ page: 1, limit: 10 }, null, 2)
  } else {
    rawParamsJson.value = '{}'
  }
  paramsJsonError.value = ''
})

const parsedParams = computed<Record<string, unknown>>(() => {
  try {
    paramsJsonError.value = ''
    return JSON.parse(rawParamsJson.value || '{}')
  } catch {
    paramsJsonError.value = 'Invalid JSON parameter format'
    return {}
  }
})

const canSubmit = computed(() => {
  return currentAction.value !== undefined && hasPermission.value && isCatalogVerified.value && !executing.value && !paramsJsonError.value
})

function generateCorrelationId() {
  correlationId.value = `c2-${Date.now().toString(36)}-${Math.random().toString(36).substring(2, 7)}`
}

function handleTrigger() {
  if (!canSubmit.value) return
  if (!correlationId.value) {
    generateCorrelationId()
  }

  if (isDestructive.value) {
    showDangerConfirm.value = true
  } else {
    executeCommand()
  }
}

async function executeCommand() {
  showDangerConfirm.value = false
  executionError.value = ''
  const id = correlationId.value || undefined
  // A new id per dispatch: reusing one returns the earlier result instead of sending again.
  correlationId.value = ''
  try {
    await command.run({
      type: selectedType.value || '',
      action: selectedAction.value || '',
      payload: parsedParams.value,
      correlationId: id,
      catalogVersion: props.catalog?.catalogVersion ?? '',
      catalogHash: props.catalog?.catalogHash ?? '',
    })
  } catch (err: any) {
    command.cancel()
    executionError.value = err?.message || 'Command dispatch failed. The server rejected the request.'
  }
}
</script>

<template>
  <Modal
    :model-value="modelValue"
    @update:model-value="emit('update:modelValue', $event)"
    max-width="xl"
    data-test="device-command-modal"
  >
    <template #header>
      <div class="flex items-center gap-2.5">
        <div class="w-8 h-8 rounded-lg bg-amber-500/10 text-amber-500 flex items-center justify-center">
          <Terminal class="w-4 h-4" />
        </div>
        <div>
          <h3 class="text-sm font-semibold text-zinc-900 dark:text-zinc-100">
            Dispatch C2 Command
          </h3>
          <p class="text-xs text-zinc-500 dark:text-zinc-400">
            Target: <span class="font-mono font-medium text-zinc-800 dark:text-zinc-200">{{ deviceName }}</span> ({{ deviceId }})
          </p>
        </div>
      </div>
    </template>

    <div class="space-y-4">
      <!-- Cryptographic verification alert banner -->
      <div
        v-if="!isCatalogVerified"
        class="p-3 rounded-lg bg-amber-500/10 border border-amber-500/20 text-xs text-amber-700 dark:text-amber-400 flex items-start gap-2.5"
        data-test="catalog-unverified-warning"
      >
        <ShieldAlert class="w-4 h-4 flex-shrink-0 mt-0.5" />
        <div>
          <p class="font-semibold">Cryptographic Catalog Pre-flight Warning</p>
          <p class="mt-0.5 text-[11px] leading-relaxed">
            The command catalog hash has not been verified against the secure enclave or permissions are unconfirmed. Execution will be guarded.
          </p>
        </div>
      </div>

      <!-- Command Type & Action Selectors -->
      <div class="grid grid-cols-1 sm:grid-cols-2 gap-3">
        <div>
          <label class="block text-xs font-medium mb-1 text-[var(--text-secondary)]">
            Command Subsystem
          </label>
          <Select
            v-model="selectedType"
            :options="typeOptions"
            placeholder="Select subsystem..."
            size="sm"
            data-test="command-type-select"
          />
        </div>

        <div>
          <label class="block text-xs font-medium mb-1 text-[var(--text-secondary)]">
            Action
          </label>
          <Select
            v-model="selectedAction"
            :disabled="!selectedType"
            :options="actionOptions"
            placeholder="Select action..."
            size="sm"
            data-test="command-action-select"
          />
        </div>
      </div>
      <p
        v-if="selectedType === 'control'"
        class="text-[11px] text-[var(--text-hint)]"
        data-test="do-panel-only-note"
      >
        Điều khiển DO trên panel DO (tab Telemetry): panel khoá kênh và đọc lại trạng thái sau mỗi lệnh.
      </p>

      <!-- Action Details & Permissions Check -->
      <div v-if="currentAction" class="p-3 rounded-md space-y-2 text-xs" style="background-color: var(--surface-subtle, #f8fafc); border: 1px solid var(--border-default, #e2e8f0);">
        <div class="flex items-center justify-between">
          <span style="color: var(--text-secondary, #64748b);">Required Permission:</span>
          <Badge :variant="hasPermission ? 'success' : 'danger'" size="xs">
            {{ requiredPermission || '—' }}
          </Badge>
        </div>
        <div class="flex items-center justify-between">
          <span style="color: var(--text-secondary, #64748b);">Risk Profile:</span>
          <Badge :variant="isDestructive ? 'danger' : 'neutral'" size="xs">
            {{ isDestructive ? 'Destructive (2-Step Confirm)' : 'Normal Operational' }}
          </Badge>
        </div>
      </div>

      <!-- Command Parameters Input (JSON) -->
      <div v-if="currentAction" class="space-y-1">
        <div class="flex items-center justify-between">
          <label class="block text-xs font-medium" style="color: var(--text-secondary, #64748b);">
            Command Parameters (JSON Payload)
          </label>
          <span v-if="paramsJsonError" class="text-[11px] text-rose-500 font-medium">
            {{ paramsJsonError }}
          </span>
        </div>
        <textarea
          v-model="rawParamsJson"
          rows="3"
          class="w-full px-3 py-2 text-xs font-mono rounded-md focus:outline-none focus:ring-2 focus:ring-amber-500/30 resize-y"
          style="background-color: var(--surface-card, #ffffff); border: 1px solid var(--border-default, #e2e8f0); color: var(--text-primary, #0f172a);"
          data-test="command-params-input"
          placeholder="{}"
        ></textarea>
      </div>

      <!-- Correlation ID -->
      <div>
        <div class="flex items-center justify-between mb-1">
          <label class="block text-xs font-medium" style="color: var(--text-secondary, #64748b);">
            Correlation Tracking ID
          </label>
          <button
            type="button"
            class="text-[11px] text-amber-600 dark:text-amber-400 hover:underline"
            @click="generateCorrelationId"
          >
            Auto-generate
          </button>
        </div>
        <input
          v-model="correlationId"
          type="text"
          placeholder="e.g. c2-m09-abcde"
          class="w-full px-3 py-2 text-xs font-mono rounded-md focus:outline-none focus:ring-2 focus:ring-amber-500/30"
          style="background-color: var(--surface-card, #ffffff); border: 1px solid var(--border-default, #e2e8f0); color: var(--text-primary, #0f172a);"
          data-test="command-correlation-input"
        />
      </div>

      <!-- Execution Result / Error Display -->
      <div
        v-if="executionResult && dispatchAccepted"
        class="p-3 rounded-lg bg-emerald-500/10 border border-emerald-500/20 text-xs text-emerald-800 dark:text-emerald-300 space-y-1"
        data-test="command-success-result"
      >
        <div class="flex items-center gap-1.5 font-semibold">
          <CheckCircle2 class="w-4 h-4 text-emerald-500" />
          <span>Command Dispatched Successfully</span>
        </div>
        <div class="font-mono text-[11px] pl-5 space-y-0.5 text-zinc-700 dark:text-zinc-300">
          <p>Status: {{ executionResult.status }}</p>
          <p>Code: {{ executionResult.code }}</p>
          <p>Correlation: {{ executionResult.correlationId }}</p>
        </div>
      </div>

      <div
        v-if="executionResult && !dispatchAccepted"
        class="p-3 rounded-lg bg-amber-500/10 border border-amber-500/20 text-xs text-amber-800 dark:text-amber-300 space-y-1"
        data-test="command-not-sent-result"
      >
        <div class="flex items-center gap-1.5 font-semibold">
          <ShieldAlert class="w-4 h-4 text-amber-500" />
          <span>Command Not Sent to Device</span>
        </div>
        <div class="font-mono text-[11px] pl-5 space-y-0.5 text-zinc-700 dark:text-zinc-300">
          <p>Status: {{ executionResult.status }}</p>
          <p>Code: {{ executionResult.code }}</p>
          <p v-if="executionResult.detail">Detail: {{ executionResult.detail }}</p>
        </div>
      </div>

      <div
        v-if="awaitingDevice || outcomeStatus || clientTimedOut"
        class="p-3 rounded-lg text-xs space-y-1 border"
        :class="outcomeStatus === 'Executed'
          ? 'bg-emerald-500/10 border-emerald-500/20 text-emerald-800 dark:text-emerald-300'
          : outcomeStatus === 'Failed'
            ? 'bg-rose-500/10 border-rose-500/20 text-rose-700 dark:text-rose-400'
            : 'bg-sky-500/10 border-sky-500/20 text-sky-800 dark:text-sky-300'"
        data-test="command-device-outcome"
      >
        <p class="font-semibold">
          {{ clientTimedOut
            ? 'No device outcome yet. It may still run: read device state before retrying.'
            : awaitingDevice && (!outcomeStatus || outcomeStatus === 'Published') ? 'Waiting for device reply…' : `Device outcome: ${outcomeStatus}` }}
        </p>
        <div v-if="deviceOutcome?.result && outcomeStatus !== 'Published'" class="font-mono text-[11px] space-y-0.5 text-zinc-700 dark:text-zinc-300">
          <p>Code: {{ deviceOutcome.result.code }}</p>
          <p v-if="deviceOutcome.result.detail">Detail: {{ deviceOutcome.result.detail }}</p>
          <p v-if="deviceOutcome.reply?.error">Device error: {{ deviceOutcome.reply.error }}</p>
        </div>
        <pre
          v-if="replyJson"
          class="mt-1 max-h-56 overflow-auto rounded p-2 text-[11px] font-mono bg-white/60 dark:bg-zinc-900/60 text-zinc-800 dark:text-zinc-200"
          data-test="command-reply-data"
        >{{ replyJson }}</pre>
      </div>

      <div
        v-if="executionError"
        class="p-3 rounded-lg bg-rose-500/10 border border-rose-500/20 text-xs text-rose-700 dark:text-rose-400"
        data-test="command-error-result"
      >
        {{ executionError }}
      </div>
    </div>

    <template #footer>
      <Button variant="ghost" @click="emit('update:modelValue', false)">
        Close
      </Button>
      <Button
        :variant="isDestructive ? 'danger' : 'primary'"
        :loading="executing"
        :disabled="!canSubmit"
        @click="handleTrigger"
        data-test="command-submit-btn"
        class="!bg-orange-500 hover:!bg-orange-600 !text-white !border-orange-500"
      >
        <template #icon-left>
          <Play class="w-3.5 h-3.5" />
        </template>
        {{ isDestructive ? 'Execute Destructive Command' : 'Dispatch Command' }}
      </Button>
    </template>
  </Modal>

  <!-- 2-Step Danger Confirmation for destructive operations -->
  <DangerConfirmDialog
    v-model="showDangerConfirm"
    title="Confirm Destructive Action"
    :message="`You are about to execute '${selectedType}.${selectedAction}' on device '${deviceName}'. This action can disrupt ongoing telemetry or reset configurations.`"
    :confirm-phrase="deviceId"
    danger-action-text="Confirm and Execute"
    :loading="executing"
    @confirm="executeCommand"
  />
</template>
