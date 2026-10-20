<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue'
import {
  adminApi,
  getCsrf,
  type DevicePermissionMatrixDto,
  type DevicePermissionDefinitionDto,
  type DeviceUserPermissionRowDto,
} from '@/core/api/adminApi'
import Checkbox from '@/shared/ui/Checkbox.vue'
import Button from '@/shared/ui/primitives/Button.vue'
import Badge from '@/shared/ui/primitives/Badge.vue'
import { RefreshCw, Save, CheckCircle2, AlertCircle, Info, ShieldCheck } from '@lucide/vue'

export interface DevicePermissionsTabProps {
  deviceId: string
  csrfToken?: string | null
}

const props = defineProps<DevicePermissionsTabProps>()

// State
const loading = ref(true)
const saving = ref(false)
const error = ref<string | null>(null)
const successMessage = ref<string | null>(null)
const matrix = ref<DevicePermissionMatrixDto | null>(null)
const internalCsrf = ref<string | null>(null)

// Local draft state for grants: map of userId -> map of action -> boolean
const draftGrants = ref<Record<string, Record<string, boolean>>>({})

const definitions = computed<DevicePermissionDefinitionDto[]>(() => {
  return matrix.value?.permissions ?? [
    { key: 'view', title: 'View', description: 'Can view the device and its data' },
    { key: 'edit_basics', title: 'Metadata', description: 'Can edit device metadata' },
    { key: 'edit_product', title: 'Definition', description: 'Can edit the device definition' },
    { key: 'record_measurements', title: 'Measurements', description: 'Can record measurements' },
  ]
})

const users = computed<DeviceUserPermissionRowDto[]>(() => {
  return matrix.value?.users ?? []
})

// Initialize drafts from matrix
function initDrafts(data: DevicePermissionMatrixDto) {
  const drafts: Record<string, Record<string, boolean>> = {}
  for (const user of data.users) {
    drafts[user.userId] = {}
    for (const def of definitions.value) {
      drafts[user.userId][def.key] = user.grants[def.key]?.granted ?? false
    }
  }
  draftGrants.value = drafts
}

async function loadData() {
  loading.value = true
  error.value = null
  successMessage.value = null
  try {
    if (!props.csrfToken && !internalCsrf.value) {
      const csrf = await getCsrf().catch(() => null)
      if (csrf) internalCsrf.value = csrf.requestToken
    }
    const data = await adminApi.devicePermissions(props.deviceId)
    matrix.value = data
    initDrafts(data)
  } catch (err: unknown) {
    const msg = err instanceof Error ? err.message : String(err)
    error.value = `Failed to load device permissions: ${msg}`
  } finally {
    loading.value = false
  }
}

// Compute unsaved changes count
const unsavedChangesCount = computed(() => {
  if (!matrix.value) return 0
  let count = 0
  for (const user of matrix.value.users) {
    for (const def of definitions.value) {
      const original = user.grants[def.key]?.granted ?? false
      const current = draftGrants.value[user.userId]?.[def.key] ?? false
      if (original !== current) {
        count++
      }
    }
  }
  return count
})

const hasUnsavedChanges = computed(() => unsavedChangesCount.value > 0)

function isInherited(user: DeviceUserPermissionRowDto, permKey: string): boolean {
  return user.grants[permKey]?.inherited ?? false
}

function getInheritedReason(user: DeviceUserPermissionRowDto, permKey: string): string {
  return user.grants[permKey]?.inheritedReason || 'The user has set this permission for all devices in this workspace.'
}

function getPermissionValue(userId: string, permKey: string): boolean {
  return draftGrants.value[userId]?.[permKey] ?? false
}

function setPermissionValue(userId: string, permKey: string, value: boolean) {
  if (!draftGrants.value[userId]) {
    draftGrants.value[userId] = {}
  }
  draftGrants.value[userId][permKey] = value
}

// Column select-all calculations
function getColumnSelectAllState(permKey: string): { checked: boolean; indeterminate: boolean } {
  const configurableUsers = users.value.filter(u => !isInherited(u, permKey))
  if (configurableUsers.length === 0) {
    return { checked: true, indeterminate: false }
  }

  let checkedCount = 0
  for (const u of configurableUsers) {
    if (getPermissionValue(u.userId, permKey)) {
      checkedCount++
    }
  }

  if (checkedCount === configurableUsers.length) {
    return { checked: true, indeterminate: false }
  } else if (checkedCount > 0) {
    return { checked: false, indeterminate: true }
  }
  return { checked: false, indeterminate: false }
}

function toggleColumnSelectAll(permKey: string) {
  const state = getColumnSelectAllState(permKey)
  const target = !state.checked
  const configurableUsers = users.value.filter(u => !isInherited(u, permKey))
  for (const u of configurableUsers) {
    setPermissionValue(u.userId, permKey, target)
  }
}

async function saveChanges() {
  if (!matrix.value) return
  saving.value = true
  error.value = null
  successMessage.value = null

  try {
    const activeCsrf = props.csrfToken || internalCsrf.value
    if (!activeCsrf) {
      const csrf = await getCsrf()
      internalCsrf.value = csrf.requestToken
    }

    const grantOverrides: Array<{ userId: string; action: string; granted: boolean }> = []

    for (const user of matrix.value.users) {
      for (const def of definitions.value) {
        if (isInherited(user, def.key)) continue
        const original = user.grants[def.key]?.granted ?? false
        const current = draftGrants.value[user.userId]?.[def.key] ?? false
        if (original !== current) {
          grantOverrides.push({
            userId: user.userId,
            action: def.key,
            granted: current,
          })
        }
      }
    }

    if (grantOverrides.length === 0) {
      saving.value = false
      return
    }

    const updated = await adminApi.updateDevicePermissions(
      props.deviceId,
      {
        expectedPolicyVersion: matrix.value.policyVersion,
        grants: grantOverrides,
      },
      { csrfToken: props.csrfToken || internalCsrf.value || undefined }
    )

    matrix.value = updated
    initDrafts(updated)
    successMessage.value = `Permissions saved successfully (Policy v${updated.policyVersion}).`
  } catch (err: unknown) {
    const msg = err instanceof Error ? err.message : String(err)
    if (msg.includes('409') || msg.includes('POLICY_VERSION_STALE')) {
      error.value = 'Conflict: Permissions were updated by another session. Please reload to see the latest rules.'
    } else {
      error.value = `Failed to save permissions: ${msg}`
    }
  } finally {
    saving.value = false
  }
}

onMounted(() => {
  loadData()
})

watch(() => props.deviceId, () => {
  loadData()
})
</script>

<template>
  <div class="space-y-4" data-test="device-permissions-tab">
    <!-- Card Container matching Datacake Parity -->
    <div class="card bg-[var(--surface-card)] border border-[var(--border-default)] rounded-lg shadow-[var(--shadow-control)] overflow-hidden">
      <!-- Card Header -->
      <div class="card-header bg-[var(--surface-card)] border-b border-[var(--border-default)] px-6 py-4 flex flex-wrap items-center justify-between gap-3">
        <div class="flex items-center gap-2">
          <h2 class="card-header-title text-base font-semibold text-[var(--text-heading)]">Permissions</h2>
          <Badge v-if="hasUnsavedChanges" variant="warning" size="sm" class="font-mono">
            {{ unsavedChangesCount }} unsaved
          </Badge>
        </div>

        <div class="flex items-center gap-2">
          <Button
            variant="outline"
            size="sm"
            @click="loadData()"
            :loading="loading"
            data-test="reload-permissions-btn"
          >
            <template #icon-left>
              <RefreshCw class="w-3.5 h-3.5" />
            </template>
            Reload
          </Button>

          <!-- Primary Save Button - Retains Brand Orange Color -->
          <Button
            variant="primary"
            size="sm"
            @click="saveChanges()"
            :disabled="!hasUnsavedChanges || saving"
            :loading="saving"
            data-test="save-permissions-btn"
          >
            <template #icon-left>
              <Save class="w-3.5 h-3.5" />
            </template>
            Save Changes
          </Button>
        </div>
      </div>

      <!-- Feedback Alerts -->
      <div v-if="successMessage" class="px-6 py-3 bg-emerald-50 dark:bg-emerald-950/40 border-b border-emerald-200 dark:border-emerald-800 text-xs text-emerald-800 dark:text-emerald-200 flex items-center justify-between">
        <div class="flex items-center gap-2">
          <CheckCircle2 class="w-4 h-4 text-emerald-600 dark:text-emerald-400" />
          <span>{{ successMessage }}</span>
        </div>
        <button type="button" @click="successMessage = null" class="text-xs hover:underline cursor-pointer">Dismiss</button>
      </div>

      <div v-if="error" class="px-6 py-3 bg-red-50 dark:bg-red-950/40 border-b border-red-200 dark:border-red-800 text-xs text-red-800 dark:text-red-200 flex items-center justify-between">
        <div class="flex items-center gap-2">
          <AlertCircle class="w-4 h-4 text-red-600 dark:text-red-400" />
          <span>{{ error }}</span>
        </div>
        <Button v-if="error.includes('Conflict')" variant="outline" size="sm" @click="loadData()">Reload Latest</Button>
        <button v-else type="button" @click="error = null" class="text-xs hover:underline cursor-pointer">Dismiss</button>
      </div>

      <!-- Table Content -->
      <div class="card-content overflow-x-auto">
        <div v-if="loading && !matrix" class="p-8 text-center text-xs text-[var(--text-secondary)]">
          <RefreshCw class="w-5 h-5 mx-auto animate-spin mb-2 text-[var(--text-hint)]" />
          Loading device permission matrix...
        </div>

        <table v-else class="table is-fullwidth padding-pls is-striped w-full text-left text-xs border-collapse">
          <thead>
            <tr class="border-b border-[var(--border-default)] bg-[var(--surface-table-header,#f8fafc)] dark:bg-[var(--surface-table-header,#0f172a)]">
              <!-- User Column -->
              <th scope="col" class="px-6 py-4 font-semibold text-xs text-[var(--text-secondary)] uppercase tracking-wider w-1/4">
                User
              </th>

              <!-- 4 Datacake Permission Columns with Checkbox + Title + Description -->
              <th
                v-for="perm in definitions"
                :key="perm.key"
                scope="col"
                class="px-6 py-4 align-top w-[18.75%]"
              >
                <div class="flex items-start gap-2.5">
                  <div class="pt-0.5">
                    <Checkbox
                      :id="`all-perm-${perm.key}`"
                      :label="`Select all ${perm.title}`"
                      label-hidden
                      :model-value="getColumnSelectAllState(perm.key).checked"
                      :indeterminate="getColumnSelectAllState(perm.key).indeterminate"
                      @change="toggleColumnSelectAll(perm.key)"
                      data-test="select-all-checkbox"
                    />
                  </div>
                  <div>
                    <div class="font-semibold text-xs text-[var(--text-primary)]">
                      {{ perm.title }}
                    </div>
                    <p class="help has-text-grey text-[11px] text-[var(--text-hint)] font-normal leading-snug mt-0.5">
                      {{ perm.description }}
                    </p>
                  </div>
                </div>
              </th>
            </tr>
          </thead>

          <tbody class="divide-y divide-[var(--border-default)]">
            <tr v-if="users.length === 0">
              <td colspan="5" class="px-6 py-8 text-center text-xs text-[var(--text-hint)]">
                No users found for this workspace/organization.
              </td>
            </tr>

            <tr
              v-for="user in users"
              :key="user.userId"
              class="hover:bg-[var(--surface-hover)] transition-colors odd:bg-[var(--surface-card)] even:bg-[var(--surface-subtle,#f8fafc)] dark:even:bg-[var(--surface-subtle,#090d16)]"
              :data-test="`user-row-${user.userId}`"
            >
              <!-- User Name & Email -->
              <td class="px-6 py-4 whitespace-nowrap">
                <div>
                  <span class="has-text-weight-bold font-semibold text-xs text-[var(--text-primary)] block">
                    {{ user.displayName }}
                  </span>
                  <span class="has-text-grey text-[11px] text-[var(--text-hint)] block mt-0.5 font-mono">
                    {{ user.email }}
                  </span>
                </div>
              </td>

              <!-- 4 Permission Checkbox Cells -->
              <td
                v-for="perm in definitions"
                :key="perm.key"
                class="px-6 py-4 whitespace-nowrap align-middle"
              >
                <!-- Case 1: Inherited Permission from Workspace -->
                <div
                  v-if="isInherited(user, perm.key)"
                  class="tooltip has-tooltip-left has-tooltip-multiline inline-flex items-center"
                  :title="getInheritedReason(user, perm.key)"
                  :data-tooltip="getInheritedReason(user, perm.key)"
                  data-test="inherited-permission-cell"
                >
                  <Checkbox
                    :id="`${user.userId}-perm-${perm.key}`"
                    :label="`${user.displayName} - ${perm.title}`"
                    label-hidden
                    :model-value="true"
                    :disabled="true"
                  />
                </div>

                <!-- Case 2: Configurable Device Permission -->
                <div v-else class="inline-flex items-center">
                  <Checkbox
                    :id="`${user.userId}-perm-${perm.key}`"
                    :label="`${user.displayName} - ${perm.title}`"
                    label-hidden
                    :model-value="getPermissionValue(user.userId, perm.key)"
                    @update:model-value="(val) => setPermissionValue(user.userId, perm.key, val)"
                    :data-test="`perm-checkbox-${user.userId}-${perm.key}`"
                  />
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  </div>
</template>

<style scoped>
/* Tooltip styling matching Datacake tooltip */
.tooltip {
  position: relative;
  cursor: help;
}

[data-tooltip] {
  position: relative;
}

[data-tooltip]:hover::after {
  content: attr(data-tooltip);
  position: absolute;
  bottom: calc(100% + 6px);
  left: 50%;
  transform: translateX(-50%);
  background-color: var(--surface-tooltip, #18181b);
  color: #ffffff;
  padding: 6px 10px;
  font-size: 11px;
  line-height: 1.3;
  border-radius: 4px;
  white-space: normal;
  width: max-content;
  max-width: 240px;
  box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.1), 0 2px 4px -1px rgba(0, 0, 0, 0.06);
  z-index: 50;
  pointer-events: none;
}
</style>
