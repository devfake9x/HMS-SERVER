<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { adminApi, type PermissionGrantSnapshot } from './adminApi'

const props = withDefaults(defineProps<{
  allowed: boolean
  csrfToken?: string | null
  sessionSignal?: AbortSignal
}>(), { csrfToken: null, sessionSignal: undefined })
const emit = defineEmits<{ changed: [snapshot: PermissionGrantSnapshot]; 'authorization-failed': [] }>()

const target = ref('')
const permission = ref('')
const organizationId = ref('')
const deviceId = ref('')
const action = ref('')
const deny = ref(false)
const resourceDeny = ref(false)
const deviceDeny = ref(false)
const snapshot = ref<PermissionGrantSnapshot | null>(null)
const status = ref<'idle' | 'loading' | 'ready' | 'error'>('idle')
const message = ref('')
let requestGeneration = 0
let abortCleanup: (() => void) | undefined

const canEdit = computed(() => props.allowed && !props.sessionSignal?.aborted && status.value !== 'loading' && target.value.trim().length > 0)
const canFeatureEdit = computed(() => canEdit.value && permission.value.trim().length > 0)
const canResourceEdit = computed(() => canEdit.value && organizationId.value.trim().length > 0)
const canDeviceEdit = computed(() => canEdit.value && deviceId.value.trim().length > 0 && action.value.trim().length > 0)
const id = (value: string | { value: string }) => typeof value === 'string' ? value : value.value

function invalidate() {
  requestGeneration += 1
  snapshot.value = null
  status.value = 'idle'
  message.value = ''
}
function watchSession(signal?: AbortSignal) {
  abortCleanup?.()
  if (!signal) return
  const handler = () => invalidate()
  signal.addEventListener('abort', handler, { once: true })
  abortCleanup = () => signal.removeEventListener('abort', handler)
}
watch(() => props.sessionSignal, watchSession, { immediate: true })
watch(target, () => { snapshot.value = null; status.value = 'idle'; message.value = '' })
onBeforeUnmount(() => abortCleanup?.())

async function load() {
  const userId = target.value.trim()
  if (!props.allowed || !userId || props.sessionSignal?.aborted) return
  const generation = ++requestGeneration
  status.value = 'loading'; message.value = ''
  try {
    const result = await adminApi.permissionGrants(userId, { signal: props.sessionSignal })
    if (generation !== requestGeneration || target.value.trim() !== userId || props.sessionSignal?.aborted) return
    snapshot.value = result; status.value = 'ready'
  } catch (error) {
    if (generation !== requestGeneration || props.sessionSignal?.aborted) return
    status.value = 'error'
    if (error instanceof Error && /403|401/.test(error.message)) emit('authorization-failed')
    message.value = 'Permission snapshot could not be loaded.'
  }
}

async function submit() {
  const userId = target.value.trim()
  if (!canEdit.value || !permission.value.trim()) return
  const generation = ++requestGeneration
  status.value = 'loading'; message.value = ''
  try {
    await adminApi.grantFeature({ userId, permission: permission.value.trim(), deny: deny.value }, { csrfToken: props.csrfToken ?? undefined, signal: props.sessionSignal })
    if (generation !== requestGeneration || props.sessionSignal?.aborted) return
    await load()
    if (snapshot.value) emit('changed', snapshot.value)
  } catch (error) {
    if (generation !== requestGeneration || props.sessionSignal?.aborted) return
    status.value = 'error'
    if (error instanceof Error && /403|401/.test(error.message)) emit('authorization-failed')
    message.value = 'Permission grant was rejected by the backend.'
  }
}

async function submitResource() {
  const userId = target.value.trim()
  if (!canResourceEdit.value) return
  const generation = ++requestGeneration
  status.value = 'loading'; message.value = ''
  try {
    await adminApi.grantResourceScope({ userId, organizationId: organizationId.value.trim(), deny: resourceDeny.value }, { csrfToken: props.csrfToken ?? undefined, signal: props.sessionSignal })
    if (generation !== requestGeneration || props.sessionSignal?.aborted) return
    await load()
    if (snapshot.value) emit('changed', snapshot.value)
  } catch (error) {
    if (generation !== requestGeneration || props.sessionSignal?.aborted) return
    status.value = 'error'
    if (error instanceof Error && /403|401/.test(error.message)) emit('authorization-failed')
    message.value = 'Resource grant was rejected by the backend.'
  }
}

async function submitDevice() {
  const userId = target.value.trim()
  if (!canDeviceEdit.value) return
  const generation = ++requestGeneration
  status.value = 'loading'; message.value = ''
  try {
    await adminApi.grantDeviceAction({ userId, deviceId: deviceId.value.trim(), action: action.value.trim(), deny: deviceDeny.value }, { csrfToken: props.csrfToken ?? undefined, signal: props.sessionSignal })
    if (generation !== requestGeneration || props.sessionSignal?.aborted) return
    await load()
    if (snapshot.value) emit('changed', snapshot.value)
  } catch (error) {
    if (generation !== requestGeneration || props.sessionSignal?.aborted) return
    status.value = 'error'
    if (error instanceof Error && /403|401/.test(error.message)) emit('authorization-failed')
    message.value = 'Device-action grant was rejected by the backend.'
  }
}
</script>

<template>
  <section class="permission-grant-editor" aria-label="Permission grant editor">
    <div class="form-grid compact-form">
      <label>Target user ID<input data-test="target" v-model="target" autocomplete="off" /></label>
      <button data-test="load" class="quiet-button" type="button" :disabled="!allowed || !target.trim() || sessionSignal?.aborted" @click="load">Load grants</button>
    </div>
    <p v-if="status === 'loading'" data-test="status">Loading permission policy...</p>
    <p v-if="message" class="empty" data-test="message">{{ message }}</p>
    <div v-if="snapshot" data-test="snapshot" class="grant-list">
      <p>Policy version {{ snapshot.policyVersion }}</p>
      <div v-for="grant in snapshot.featureGrants" :key="`feature-${grant.permission}`" class="grant-row">
        <code>{{ grant.permission }}</code><strong>{{ grant.deny ? 'Deny' : 'Allow' }}</strong>
      </div>
      <div v-for="grant in snapshot.resourceGrants" :key="`resource-${id(grant.organizationId)}`" class="grant-row">
        <code>organization:{{ id(grant.organizationId) }}</code><strong>{{ grant.deny ? 'Deny' : 'Allow' }}</strong>
      </div>
      <div v-for="grant in snapshot.deviceActionGrants" :key="`device-${id(grant.deviceId)}-${grant.action}`" class="grant-row">
        <code>{{ id(grant.deviceId) }}:{{ grant.action }}</code><strong>{{ grant.deny ? 'Deny' : 'Allow' }}</strong>
      </div>
    </div>
    <form class="form-grid compact-form" @submit.prevent="submit">
      <label>Feature permission<input data-test="permission" v-model="permission" placeholder="devices.manage" /></label>
      <label class="checkbox"><input data-test="deny" type="checkbox" v-model="deny" /> Explicit deny</label>
      <button data-test="submit" class="primary-button" type="submit" :disabled="!canFeatureEdit">Save feature grant</button>
    </form>
    <form class="form-grid compact-form" @submit.prevent="submitResource">
      <label>Organization scope<input data-test="organization" v-model="organizationId" placeholder="organization-id" /></label>
      <label class="checkbox"><input data-test="resource-deny" type="checkbox" v-model="resourceDeny" /> Explicit deny</label>
      <button data-test="resource-submit" class="quiet-button" type="submit" :disabled="!canResourceEdit">Save organization scope</button>
    </form>
    <form class="form-grid compact-form" @submit.prevent="submitDevice">
      <label>Device ID<input data-test="device" v-model="deviceId" placeholder="device-id" /></label>
      <label>Device action<input data-test="action" v-model="action" placeholder="status.read" /></label>
      <label class="checkbox"><input data-test="device-deny" type="checkbox" v-model="deviceDeny" /> Explicit deny</label>
      <button data-test="device-submit" class="quiet-button" type="submit" :disabled="!canDeviceEdit">Save device action</button>
    </form>
  </section>
</template>

<style scoped>
.permission-grant-editor {
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
}
.form-grid {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
  align-items: flex-end;
}
.form-grid label {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  font-size: 0.75rem;
  font-weight: 500;
  color: var(--text-secondary, #64748b);
}
.form-grid label input[type="text"],
.form-grid label input:not([type]) {
  padding: 0.375rem 0.625rem;
  font-size: 0.8125rem;
  border-radius: 0.375rem;
  border: 1px solid var(--border-default, #e2e8f0);
  background: var(--surface-card, #ffffff);
  color: var(--text-primary, #0f172a);
}
.form-grid label.checkbox {
  flex-direction: row;
  align-items: center;
  gap: 0.375rem;
  padding-bottom: 0.375rem;
  cursor: pointer;
}
.quiet-button,
.primary-button {
  padding: 0.375rem 0.75rem;
  font-size: 0.75rem;
  font-weight: 500;
  border-radius: 0.375rem;
  cursor: pointer;
  transition: all 0.15s ease;
}
.primary-button {
  background: var(--color-primary, #155dfc);
  color: #ffffff;
  border: 1px solid transparent;
}
.primary-button:hover:not(:disabled) {
  opacity: 0.9;
}
.primary-button:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
.quiet-button {
  background: transparent;
  color: var(--text-primary, #0f172a);
  border: 1px solid var(--border-default, #e2e8f0);
}
.quiet-button:hover:not(:disabled) {
  background: var(--surface-hover, #f8fafc);
}
.quiet-button:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
.grant-list {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  padding: 0.75rem;
  border-radius: 0.375rem;
  background: var(--surface-subtle, #f8fafc);
  border: 1px solid var(--border-default, #e2e8f0);
}
.grant-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  font-size: 0.75rem;
  padding: 0.25rem 0;
}
.grant-row code {
  font-family: monospace;
  color: var(--text-primary, #0f172a);
}
</style>
