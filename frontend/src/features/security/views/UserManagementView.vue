<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import {
  adminApi,
  getCsrf,
  type ManagedUserDto,
  type OrganizationDto,
} from '@/core/api/adminApi'
import { createSessionFence } from '@/core/auth/sessionFence'
import DataTable, { type TableColumn } from '@/shared/ui/data-table/DataTable.vue'
import FilterBar from '@/shared/ui/navigation/FilterBar.vue'
import Badge from '@/shared/ui/primitives/Badge.vue'
import Button from '@/shared/ui/primitives/Button.vue'
import Modal from '@/shared/ui/feedback/Modal.vue'
import Select, { type SelectOption } from '@/shared/ui/Select.vue'
import Input from '@/shared/ui/Input.vue'
import { Users, UserPlus, RefreshCw, Mail, Shield } from '@lucide/vue'

const sessionFence = createSessionFence()
const loading = ref(true)

const users = ref<ManagedUserDto[]>([])
const organizations = ref<OrganizationDto[]>([])
const search = ref('')

// Add user modal
const showAddModal = ref(false)
const newUserEmail = ref('')
const newUserPassword = ref('')
const newUserOrg = ref<string | null>(null)
const creating = ref(false)
const createError = ref('')

const orgOptions = computed<SelectOption[]>(() => [
  { value: null, label: 'Global (All Orgs)' },
  ...organizations.value.map((org) => ({ value: org.id, label: org.name })),
])

const columns: TableColumn[] = [
  { key: 'email', label: 'User Email', sortable: true },
  { key: 'id', label: 'User ID', width: '220px', sortable: true },
  { key: 'organization', label: 'Organization', width: '180px' },
  { key: 'roles', label: 'Assigned Roles', width: '200px' },
  { key: 'actions', label: 'Actions', width: '100px', align: 'right' },
]

async function loadUsers() {
  loading.value = true
  const lease = sessionFence.rotate()
  try {
    const [userList, orgList] = await Promise.all([
      adminApi.listUsers({ signal: lease.signal }).catch(() => []),
      adminApi.listOrganizations({ signal: lease.signal }).catch(() => []),
    ])

    if (!sessionFence.isCurrent(lease)) return

    users.value = userList
    organizations.value = orgList
  } finally {
    if (sessionFence.isCurrent(lease)) {
      loading.value = false
    }
  }
}

async function handleCreateUser() {
  if (!newUserEmail.value || !newUserPassword.value) {
    createError.value = 'Email and initial password are required.'
    return
  }

  creating.value = true
  createError.value = ''
  const lease = sessionFence.capture()

  try {
    const csrf = await getCsrf({ signal: lease.signal }).catch(() => null)
    await adminApi.createUser({
      email: newUserEmail.value,
      password: newUserPassword.value,
      organizationId: newUserOrg.value || undefined,
    }, {
      csrfToken: csrf?.requestToken,
      signal: lease.signal,
    })

    showAddModal.value = false
    newUserEmail.value = ''
    newUserPassword.value = ''
    await loadUsers()
  } catch (err: any) {
    createError.value = err?.message || 'Failed to create user.'
  } finally {
    creating.value = false
  }
}

onMounted(() => {
  void loadUsers()
})
</script>

<template>
  <div class="space-y-4" data-test="user-management-view">
    <!-- Header -->
    <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
      <div>
        <h1 class="text-xl font-bold tracking-tight text-[var(--text-heading)]">
          User & Identity Management
        </h1>
        <p class="mt-1 text-xs text-[var(--text-hint)]">
          Manage system operators, assign organizational boundaries, and enforce MFA.
        </p>
      </div>

      <div class="flex items-center gap-2">
        <Button variant="outline" size="sm" :loading="loading" @click="loadUsers">
          <template #icon-left>
            <RefreshCw class="w-3.5 h-3.5" />
          </template>
        </Button>
        <Button variant="primary" size="sm" @click="showAddModal = true">
          <template #icon-left>
            <UserPlus class="w-3.5 h-3.5" />
          </template>
          Add User
        </Button>
      </div>
    </div>

    <!-- Filter Bar -->
    <FilterBar
      v-model:search="search"
      search-placeholder="Search users by email or ID..."
    />

    <!-- Users DataTable -->
    <DataTable
      :columns="columns"
      :items="users"
      :loading="loading"
      empty-message="No system users registered."
      :page-size="10"
    >
      <template #cell-email="{ value }">
        <div class="flex items-center gap-2">
          <div class="w-7 h-7 rounded-full bg-[var(--surface-overlay)] text-[var(--color-primary)] border border-[var(--border-subtle)] flex items-center justify-center font-medium text-xs">
            {{ (value || 'U')[0].toUpperCase() }}
          </div>
          <div>
            <p class="font-medium text-[var(--text-heading)]">{{ value }}</p>
          </div>
        </div>
      </template>

      <template #cell-id="{ value }">
        <span class="font-mono text-[11px] text-[var(--text-hint)]">{{ value }}</span>
      </template>

      <template #cell-organization="{ item }">
        <span class="text-xs text-[var(--text-secondary)]">
          {{ item.organizationId || 'Global Enterprise' }}
        </span>
      </template>

      <template #cell-roles="{ item }">
        <div class="flex flex-wrap gap-1">
          <Badge variant="neutral" size="xs">
            {{ item.role || 'Operator' }}
          </Badge>
        </div>
      </template>

      <template #cell-actions="{ item }">
        <Button variant="ghost" size="xs">
          Edit
        </Button>
      </template>
    </DataTable>

    <!-- Create User Modal -->
    <Modal
      v-model="showAddModal"
      title="Create New System User"
      subtitle="Provision access credentials for an operator or administrator."
      max-width="md"
    >
      <form @submit.prevent="handleCreateUser" class="space-y-3.5">
        <div v-if="createError" class="p-2.5 rounded-lg bg-rose-500/10 border border-rose-500/20 text-xs text-rose-600 dark:text-rose-400">
          {{ createError }}
        </div>

        <div>
          <label class="block text-xs font-medium text-[var(--text-secondary)] mb-1">
            Email Address *
          </label>
          <Input
            v-model="newUserEmail"
            type="email"
            required
            placeholder="operator@company.com"
            size="md"
          />
        </div>

        <div>
          <label class="block text-xs font-medium text-[var(--text-secondary)] mb-1">
            Initial Temporary Password *
          </label>
          <Input
            v-model="newUserPassword"
            type="password"
            required
            placeholder="••••••••••••"
            size="md"
          />
        </div>

        <div>
          <label class="block text-xs font-medium text-[var(--text-secondary)] mb-1">
            Organization Scope (Optional)
          </label>
          <Select
            v-model="newUserOrg"
            :options="orgOptions"
            placeholder="Global (All Orgs)"
            size="md"
          />
        </div>

        <div class="flex items-center justify-end gap-2 pt-3">
          <Button variant="ghost" @click="showAddModal = false">
            Cancel
          </Button>
          <Button variant="primary" type="submit" :loading="creating">
            Provision Account
          </Button>
        </div>
      </form>
    </Modal>
  </div>
</template>
