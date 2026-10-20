<script setup lang="ts">
import { ref, onMounted } from 'vue'
import {
  adminApi,
  getCsrf,
  type PermissionSnapshot,
  type ManagedUserDto,
  type OrganizationDto,
} from '@/core/api/adminApi'
import { createSessionFence } from '@/core/auth/sessionFence'
import PermissionGrantEditor from '@/PermissionGrantEditor.vue'
import Badge from '@/shared/ui/primitives/Badge.vue'
import Button from '@/shared/ui/primitives/Button.vue'
import { ShieldCheck, RefreshCw, Key, ShieldAlert } from '@lucide/vue'

const sessionFence = createSessionFence()
const loading = ref(true)

const permissions = ref<PermissionSnapshot | null>(null)
const users = ref<ManagedUserDto[]>([])
const organizations = ref<OrganizationDto[]>([])
const actionMessage = ref('')
const csrfToken = ref<string | null>(null)
const sessionSignal = ref<AbortSignal>(sessionFence.capture().signal)

async function loadData() {
  loading.value = true
  const lease = sessionFence.rotate()
  try {
    const [perms, userList, orgList] = await Promise.all([
      adminApi.permissions({ signal: lease.signal }).catch(() => null),
      adminApi.listUsers({ signal: lease.signal }).catch(() => []),
      adminApi.listOrganizations({ signal: lease.signal }).catch(() => []),
    ])

    if (!sessionFence.isCurrent(lease)) return

    permissions.value = perms
    users.value = userList
    organizations.value = orgList
  } finally {
    if (sessionFence.isCurrent(lease)) {
      loading.value = false
    }
  }
}

async function handleGrantSaved(_snapshot: any) {
  actionMessage.value = 'Permission grant snapshot persisted successfully.'
  await loadData()
}

onMounted(() => {
  void loadData()
})
</script>

<template>
  <div class="space-y-6" data-test="role-permission-view">
    <!-- Header -->
    <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
      <div>
        <h1 class="text-xl font-bold tracking-tight" style="color: var(--text-heading, #0f172a);">
          Role-Based Access Control & Permissions
        </h1>
        <p class="mt-1 text-xs" style="color: var(--text-secondary, #64748b);">
          Cryptographically signed policy snapshots, fine-grained grants, and scope enforcement.
        </p>
      </div>

      <div class="flex items-center gap-2">
        <Button variant="outline" size="sm" :loading="loading" @click="loadData">
          <template #icon-left>
            <RefreshCw class="w-3.5 h-3.5" />
          </template>
          Sync Policies
        </Button>
      </div>
    </div>

    <!-- Active Policy Snapshot Banner -->
    <div
      class="p-4 rounded-lg shadow-xs flex flex-col sm:flex-row sm:items-center justify-between gap-4"
      style="background-color: var(--surface-card, #ffffff); border: 1px solid var(--border-default, #e2e8f0);"
    >
      <div class="flex items-center gap-3">
        <div class="w-10 h-10 rounded-lg bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 flex items-center justify-center">
          <ShieldCheck class="w-5 h-5" />
        </div>
        <div>
          <div class="flex items-center gap-2">
            <h3 class="text-sm font-semibold" style="color: var(--text-heading, #0f172a);">
              Active Authorization Policy
            </h3>
            <Badge variant="success" size="xs">
              Version {{ permissions?.policyVersion ?? 1 }}
            </Badge>
          </div>
          <p class="text-[11px]" style="color: var(--text-secondary, #64748b);">
            Expires: {{ permissions?.expiresAt ? new Date(permissions.expiresAt).toLocaleString() : 'Session based' }}
          </p>
        </div>
      </div>

      <div class="flex items-center gap-3 text-xs">
        <div>
          <span style="color: var(--text-secondary, #64748b);">Granted:</span>
          <span class="font-bold ml-1 text-emerald-600 dark:text-emerald-400">{{ permissions?.permissions?.length ?? 0 }}</span>
        </div>
        <div>
          <span style="color: var(--text-secondary, #64748b);">Explicit Denied:</span>
          <span class="font-bold ml-1 text-rose-600 dark:text-rose-400">{{ permissions?.deniedPermissions?.length ?? 0 }}</span>
        </div>
      </div>
    </div>

    <div v-if="actionMessage" class="p-3 rounded-lg bg-emerald-500/10 border border-emerald-500/20 text-xs text-emerald-700 dark:text-emerald-400">
      {{ actionMessage }}
    </div>

    <!-- Permission Grant Editor Slice -->
    <div
      class="rounded-lg p-5 shadow-xs"
      style="background-color: var(--surface-card, #ffffff); border: 1px solid var(--border-default, #e2e8f0);"
    >
      <h2 class="text-sm font-semibold mb-4" style="color: var(--text-heading, #0f172a);">
        Grant Fine-Grained Authorization Policy
      </h2>
      <PermissionGrantEditor
        :allowed="true"
        :csrf-token="csrfToken"
        :session-signal="sessionSignal"
        @changed="handleGrantSaved"
      />
    </div>
  </div>
</template>
