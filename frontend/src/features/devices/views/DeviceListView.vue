<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import {
  adminApi,
  getCsrf,
  type DeviceDto,
  type OrganizationDto,
  type ProjectDto,
  type PackageDto,
  type AreaDto,
} from '@/core/api/adminApi'
import { createSessionFence } from '@/core/auth/sessionFence'
import { createRequestRevision } from '@/core/utils/requestRevision'
import { filterDevices } from '@/core/devices/deviceFilters'
import { writeDeviceInventoryQuery, readDeviceInventoryQuery } from '@/core/devices/deviceInventoryState'
import DataTable, { type TableColumn } from '@/shared/ui/data-table/DataTable.vue'
import TreeDataTable, { type TreeNode } from '@/shared/ui/data-table/TreeDataTable.vue'
import FilterBar, { type FilterTab } from '@/shared/ui/navigation/FilterBar.vue'
import StatusDot from '@/shared/ui/primitives/StatusDot.vue'
import Badge from '@/shared/ui/primitives/Badge.vue'
import Button from '@/shared/ui/primitives/Button.vue'
import Modal from '@/shared/ui/feedback/Modal.vue'
import DangerConfirmDialog from '@/shared/ui/feedback/DangerConfirmDialog.vue'
import Select, { type SelectOption } from '@/shared/ui/Select.vue'
import Input from '@/shared/ui/Input.vue'
import {
  HardDrive,
  Table,
  Network,
  Plus,
  Trash2,
  ExternalLink,
  RefreshCw,
  SlidersHorizontal,
} from '@lucide/vue'

const router = useRouter()
const route = useRoute()
const sessionFence = createSessionFence()
const requestRevision = createRequestRevision()

// View mode: 'table' or 'tree'
const viewMode = ref<'table' | 'tree'>('table')

interface DeviceRow extends DeviceDto {
  organization: string
  area: string | null
}

// State
const loading = ref(true)
const devices = ref<DeviceRow[]>([])
const organizations = ref<OrganizationDto[]>([])
const projects = ref<ProjectDto[]>([])
const packages = ref<PackageDto[]>([])
const areas = ref<AreaDto[]>([])

// Query / Filter state
const search = ref('')
const activeStatusTab = ref('all')
const selectedOrg = ref<string | null>(null)
const selectedProject = ref<string | null>(null)
const selectedPackage = ref<string | null>(null)
const selectedArea = ref<string | null>(null)

// Dropdown options for Select component
const orgOptions = computed<SelectOption[]>(() => [
  { value: null, label: 'All Organizations' },
  ...organizations.value.map(o => ({ value: o.id, label: o.name })),
])

const projectOptions = computed<SelectOption[]>(() => [
  { value: null, label: 'All Projects' },
  ...projects.value.map(p => ({ value: p.id, label: p.name })),
])

const packageOptions = computed<SelectOption[]>(() => [
  { value: null, label: 'All Packages' },
  ...packages.value.map(pkg => ({ value: pkg.id, label: pkg.name })),
])

const areaOptions = computed<SelectOption[]>(() => [
  { value: null, label: 'All Areas' },
  ...areas.value.map(a => ({ value: a.id, label: a.name })),
])

const createOrgOptions = computed<SelectOption[]>(() => [
  { value: null, label: 'Select organization...' },
  ...organizations.value.map(o => ({ value: o.id, label: o.name })),
])

const createProjectOptions = computed<SelectOption[]>(() => [
  { value: null, label: 'None' },
  ...projects.value.map(p => ({ value: p.id, label: p.name })),
])

const createPackageOptions = computed<SelectOption[]>(() => [
  { value: null, label: 'None' },
  ...packages.value.map(pkg => ({ value: pkg.id, label: pkg.name })),
])

const createAreaOptions = computed<SelectOption[]>(() => [
  { value: null, label: 'None' },
  ...areas.value.map(a => ({ value: a.id, label: a.name })),
])

// Selection
const selectedDeviceIds = ref<(string | number)[]>([])
const showRevokeDialog = ref(false)
const revoking = ref(false)
const revokeTargetDeviceId = ref('')

// Device enrollment modal state
const showCreateModal = ref(false)
const createName = ref('')
const createOrgId = ref('')
const createProjectId = ref('')
const createPackageId = ref('')
const createAreaId = ref('')
const creating = ref(false)
const createError = ref('')

// Columns
const columns: TableColumn[] = [
  { key: 'id', label: 'Device ID', width: '220px', sortable: true },
  { key: 'name', label: 'Device Name', sortable: true },
  { key: 'state', label: 'State', width: '130px', sortable: true },
  { key: 'ownerOrganizationId', label: 'Organization', width: '180px', sortable: true },
  { key: 'areaId', label: 'Area', width: '140px' },
  { key: 'keyGeneration', label: 'Key Gen', width: '90px', align: 'center' },
  { key: 'actions', label: 'Actions', width: '110px', align: 'right' },
]

// Status tabs calculation
const tabs = computed<FilterTab[]>(() => [
  { key: 'all', label: 'All Fleet', count: devices.value.length },
  {
    key: 'active',
    label: 'Active',
    count: devices.value.filter(d => d.state?.toLowerCase() === 'active').length,
  },
  {
    key: 'provisioning',
    label: 'Provisioning',
    count: devices.value.filter(d => d.state?.toLowerCase() === 'provisioning').length,
  },
  {
    key: 'revoked',
    label: 'Revoked',
    count: devices.value.filter(d => d.state?.toLowerCase() === 'revoked').length,
  },
])

// Filtered Devices
const filteredDevices = computed(() => {
  let list = devices.value

  // Status Tab
  if (activeStatusTab.value !== 'all') {
    list = list.filter(d => d.state?.toLowerCase() === activeStatusTab.value)
  }

  // Dropdown hierarchy filters
  if (selectedOrg.value) {
    list = list.filter(d => d.ownerOrganizationId === selectedOrg.value)
  }
  if (selectedProject.value) {
    list = list.filter(d => d.projectId === selectedProject.value)
  }
  if (selectedPackage.value) {
    list = list.filter(d => d.packageId === selectedPackage.value)
  }
  if (selectedArea.value) {
    list = list.filter(d => d.areaId === selectedArea.value)
  }

  // Text search
  return filterDevices(list, search.value)
})

// Build hierarchical tree nodes
const treeNodes = computed<TreeNode[]>(() => {
  const result: TreeNode[] = []

  for (const org of organizations.value) {
    const orgProjects = projects.value.filter(p => p.organizationId === org.id)
    const orgAreas = areas.value.filter(a => a.organizationId === org.id)
    const orgDevices = devices.value.filter(d => d.ownerOrganizationId === org.id && !d.projectId)

    const projectNodes: TreeNode[] = orgProjects.map(proj => {
      const projPackages = packages.value.filter(pkg => pkg.projectId === proj.id)
      const projDevices = devices.value.filter(d => d.projectId === proj.id && !d.packageId)

      const packageNodes: TreeNode[] = projPackages.map(pkg => {
        const pkgDevices = devices.value.filter(d => d.packageId === pkg.id)
        return {
          id: `pkg-${pkg.id}`,
          name: pkg.name,
          type: 'package',
          children: pkgDevices.map(d => ({
            id: d.id,
            name: d.name,
            type: 'device',
            status: d.state?.toLowerCase(),
          })),
        }
      })

      return {
        id: `proj-${proj.id}`,
        name: proj.name,
        type: 'project',
        children: [
          ...packageNodes,
          ...projDevices.map(d => ({
            id: d.id,
            name: d.name,
            type: 'device',
            status: d.state?.toLowerCase(),
          })),
        ],
      }
    })

    result.push({
      id: `org-${org.id}`,
      name: org.name,
      type: 'org',
      children: [
        ...projectNodes,
        ...orgAreas.map(a => ({
          id: `area-${a.id}`,
          name: a.name,
          type: 'area',
          children: devices.value
            .filter(d => d.areaId === a.id)
            .map(d => ({
              id: d.id,
              name: d.name,
              type: 'device',
              status: d.state?.toLowerCase(),
            })),
        })),
        ...orgDevices.map(d => ({
          id: d.id,
          name: d.name,
          type: 'device',
          status: d.state?.toLowerCase(),
        })),
      ],
    })
  }

  return result
})

async function loadData() {
  loading.value = true
  const lease = sessionFence.rotate()
  const revision = requestRevision.next()

  try {
    const query = writeDeviceInventoryQuery({
      q: search.value || undefined,
      organizationId: selectedOrg.value || undefined,
      projectId: selectedProject.value || undefined,
      packageId: selectedPackage.value || undefined,
      areaId: selectedArea.value || undefined,
    })

    const [deviceList, orgList, projList, pkgList, areaList] = await Promise.all([
      adminApi.listDevices(query, { signal: lease.signal }).catch(() => []),
      adminApi.listOrganizations({ signal: lease.signal }).catch(() => []),
      adminApi.listProjects({ signal: lease.signal }).catch(() => []),
      adminApi.listPackages({ signal: lease.signal }).catch(() => []),
      adminApi.listAreas({ signal: lease.signal }).catch(() => []),
    ])

    if (!sessionFence.isCurrent(lease) || !requestRevision.isCurrent(revision)) return

    devices.value = deviceList.map((d) => ({
      ...d,
      organization: d.ownerOrganizationId,
      area: d.areaId,
    }))
    organizations.value = orgList
    projects.value = projList
    packages.value = pkgList
    areas.value = areaList
  } finally {
    if (sessionFence.isCurrent(lease)) {
      loading.value = false
    }
  }
}

function handleRowClick(device: any) {
  router.push(`/devices/${device.id}`)
}

function handleTreeNodeSelect(node: TreeNode) {
  if (node.type === 'device') {
    router.push(`/devices/${node.id}`)
  }
}

function openRevokeDialog(deviceId: string) {
  revokeTargetDeviceId.value = deviceId
  showRevokeDialog.value = true
}

async function confirmRevoke() {
  if (!revokeTargetDeviceId.value) return
  revoking.value = true
  const lease = sessionFence.capture()
  try {
    const csrf = await getCsrf({ signal: lease.signal }).catch(() => null)
    await adminApi.revokeDevice(revokeTargetDeviceId.value, {
      csrfToken: csrf?.requestToken,
      signal: lease.signal,
    })
    const dev = devices.value.find(d => d.id === revokeTargetDeviceId.value)
    if (dev) dev.state = 'Revoked'
    showRevokeDialog.value = false
  } catch (err) {
    console.error('Revoke failed:', err)
  } finally {
    revoking.value = false
  }
}

async function handleCreateDevice() {
  if (!createName.value || !createOrgId.value) {
    createError.value = 'Device name and organization are required.'
    return
  }

  creating.value = true
  createError.value = ''
  const lease = sessionFence.capture()

  try {
    const csrf = await getCsrf({ signal: lease.signal }).catch(() => null)
    await adminApi.createDevice({
      name: createName.value,
      ownerOrganizationId: createOrgId.value,
      projectId: createProjectId.value || undefined,
      packageId: createPackageId.value || undefined,
      areaId: createAreaId.value || undefined,
    }, {
      csrfToken: csrf?.requestToken,
      signal: lease.signal,
    })

    showCreateModal.value = false
    createName.value = ''
    await loadData()
  } catch (err: any) {
    createError.value = err?.message || 'Failed to enroll device.'
  } finally {
    creating.value = false
  }
}

onMounted(() => {
  const query = readDeviceInventoryQuery(window.location.search)
  search.value = query.q ?? ''
  selectedOrg.value = query.organizationId ?? ''
  selectedProject.value = query.projectId ?? ''
  selectedPackage.value = query.packageId ?? ''
  selectedArea.value = query.areaId ?? ''
  void loadData()
})

watch([search, selectedOrg, selectedProject, selectedPackage, selectedArea], () => {
  void loadData()
})
</script>

<template>
  <div class="space-y-4" data-test="device-list-view">
    <!-- Header: Title + Actions -->
    <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
      <div>
        <h1 class="text-xl font-bold tracking-tight text-[var(--text-heading)]">
          Edge Device Fleet
        </h1>
        <p class="mt-1 text-xs text-[var(--text-hint)]">
          Manage RTU units, inspect telemetry heartbeats, and control security tokens.
        </p>
      </div>

      <div class="flex items-center gap-2">
        <!-- View Mode Toggle -->
        <div class="inline-flex rounded-md bg-[var(--surface-overlay)] p-0.5 border border-[var(--border-default)]">
          <button
            type="button"
            class="px-2.5 py-1 text-xs font-medium rounded transition-colors cursor-pointer flex items-center gap-1.5"
            :class="[
              viewMode === 'table'
                ? 'bg-[var(--surface-card)] text-[var(--text-heading)] font-semibold shadow-xs'
                : 'text-[var(--text-secondary)] hover:text-[var(--text-primary)]'
            ]"
            @click="viewMode = 'table'"
            data-test="toggle-table-mode"
          >
            <Table class="w-3.5 h-3.5" />
            <span>Table</span>
          </button>
          <button
            type="button"
            class="px-2.5 py-1 text-xs font-medium rounded transition-colors cursor-pointer flex items-center gap-1.5"
            :class="[
              viewMode === 'tree'
                ? 'bg-[var(--surface-card)] text-[var(--text-heading)] font-semibold shadow-xs'
                : 'text-[var(--text-secondary)] hover:text-[var(--text-primary)]'
            ]"
            @click="viewMode = 'tree'"
            data-test="toggle-tree-mode"
          >
            <Network class="w-3.5 h-3.5" />
            <span>Hierarchy</span>
          </button>
        </div>

        <Button
          variant="outline"
          size="sm"
          :loading="loading"
          @click="loadData"
          data-test="devices-refresh-btn"
        >
          <template #icon-left>
            <RefreshCw class="w-3.5 h-3.5" />
          </template>
        </Button>

        <Button
          variant="primary"
          size="sm"
          @click="showCreateModal = true"
          data-test="enroll-device-btn"
        >
          <template #icon-left>
            <Plus class="w-3.5 h-3.5" />
          </template>
          Enroll Device
        </Button>
      </div>
    </div>

    <!-- Filter Bar (Search + Status Tabs) -->
    <FilterBar
      v-model:search="search"
      v-model:activeTab="activeStatusTab"
      :tabs="tabs"
      search-placeholder="Search devices by name or ID..."
    >
      <template #actions>
        <!-- Filters dropdown toggle or indicators -->
        <div v-if="selectedOrg || selectedProject" class="hidden sm:flex items-center gap-1.5">
          <Badge variant="neutral" size="xs">
            Filters Active
          </Badge>
        </div>
      </template>
    </FilterBar>

    <!-- Hierarchy Filter Dropdowns Bar -->
    <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 p-3 bg-[var(--surface-card)] border border-[var(--border-default)] rounded-lg text-xs shadow-[var(--shadow-control)]">
      <div>
        <label class="block text-[11px] font-medium text-[var(--text-secondary)] mb-1">Organization</label>
        <Select
          v-model="selectedOrg"
          :options="orgOptions"
          placeholder="All Organizations"
          size="sm"
          data-test="filter-select-org"
        />
      </div>

      <div>
        <label class="block text-[11px] font-medium text-[var(--text-secondary)] mb-1">Project</label>
        <Select
          v-model="selectedProject"
          :options="projectOptions"
          placeholder="All Projects"
          size="sm"
          data-test="filter-select-project"
        />
      </div>

      <div>
        <label class="block text-[11px] font-medium text-[var(--text-secondary)] mb-1">Package</label>
        <Select
          v-model="selectedPackage"
          :options="packageOptions"
          placeholder="All Packages"
          size="sm"
          data-test="filter-select-package"
        />
      </div>

      <div>
        <label class="block text-[11px] font-medium text-[var(--text-secondary)] mb-1">Area</label>
        <Select
          v-model="selectedArea"
          :options="areaOptions"
          placeholder="All Areas"
          size="sm"
          data-test="filter-select-area"
        />
      </div>
    </div>

    <!-- Table View -->
    <div v-if="viewMode === 'table'">
      <DataTable
        :columns="columns"
        :items="filteredDevices"
        :loading="loading"
        empty-message="No devices match your filter criteria."
        :page-size="10"
        selectable
        v-model:selected-ids="selectedDeviceIds"
        @row-click="handleRowClick"
      >
        <template #cell-id="{ value }">
          <code class="font-mono text-[11px] font-semibold text-[var(--text-primary)] bg-[var(--surface-overlay)] px-1.5 py-0.5 rounded border border-[var(--border-subtle)]">
            {{ value }}
          </code>
        </template>

        <template #cell-name="{ value }">
          <div class="flex items-center gap-2">
            <HardDrive class="w-3.5 h-3.5 text-[var(--text-hint)] group-hover:text-[var(--color-primary)] transition-colors" />
            <span class="font-semibold text-[var(--text-heading)] group-hover:text-[var(--color-primary)] transition-colors">{{ value }}</span>
          </div>
        </template>

        <template #cell-state="{ value }">
          <div class="flex items-center gap-1.5">
            <StatusDot
              :status="value?.toLowerCase() === 'active' ? 'online' : value?.toLowerCase() === 'provisioning' ? 'warning' : 'offline'"
              size="sm"
            />
            <Badge
              :variant="value?.toLowerCase() === 'active' ? 'success' : value?.toLowerCase() === 'provisioning' ? 'warning' : 'neutral'"
              size="xs"
            >
              {{ value || 'Unknown' }}
            </Badge>
          </div>
        </template>

        <template #cell-ownerOrganizationId="{ value }">
          <span class="text-xs text-[var(--text-secondary)] font-mono">{{ value }}</span>
        </template>

        <template #cell-areaId="{ value }">
          <span class="text-xs text-[var(--text-secondary)]">{{ value || '-' }}</span>
        </template>

        <template #cell-keyGeneration="{ value }">
          <span class="font-mono text-[11px] text-[var(--text-hint)]">
            gen-{{ value }}
          </span>
        </template>

        <template #cell-actions="{ item }">
          <div class="flex items-center justify-end gap-1.5" @click.stop>
            <button
              type="button"
              class="p-1 text-[var(--text-hint)] hover:text-[var(--color-primary)] rounded transition-colors"
              title="Open Device Detail"
              @click="router.push(`/devices/${item.id}`)"
            >
              <ExternalLink class="w-3.5 h-3.5" />
            </button>
            <button
              type="button"
              class="p-1 text-[var(--text-hint)] hover:text-[var(--status-danger)] rounded transition-colors"
              title="Revoke device access"
              @click="openRevokeDialog(item.id)"
            >
              <Trash2 class="w-3.5 h-3.5" />
            </button>
          </div>
        </template>
      </DataTable>
    </div>

    <!-- Tree View -->
    <div v-else>
      <TreeDataTable
        :nodes="treeNodes"
        :loading="loading"
        default-expanded-all
        @select-node="handleTreeNodeSelect"
        @action="(_, node) => handleTreeNodeSelect(node)"
      />
    </div>

    <!-- Enroll Device Modal -->
    <Modal
      v-model="showCreateModal"
      title="Enroll New Edge Device"
      subtitle="Register an RTU unit and initialize cryptographic key generation."
      max-width="md"
    >
      <form @submit.prevent="handleCreateDevice" class="space-y-3.5">
        <div v-if="createError" class="p-2.5 rounded-lg bg-rose-500/10 border border-rose-500/20 text-xs text-rose-600 dark:text-rose-400">
          {{ createError }}
        </div>

        <div>
          <label class="block text-xs font-medium text-[var(--text-secondary)] mb-1">
            Device Display Name *
          </label>
          <Input
            v-model="createName"
            type="text"
            required
            placeholder="e.g. RTU-Bridge-North-01"
            size="md"
          />
        </div>

        <div>
          <label class="block text-xs font-medium text-[var(--text-secondary)] mb-1">
            Owner Organization *
          </label>
          <Select
            v-model="createOrgId"
            :options="createOrgOptions"
            placeholder="Select organization..."
            size="md"
          />
        </div>

        <div class="grid grid-cols-2 gap-3">
          <div>
            <label class="block text-xs font-medium text-[var(--text-secondary)] mb-1">
              Project (Optional)
            </label>
            <Select
              v-model="createProjectId"
              :options="createProjectOptions"
              placeholder="None"
              size="md"
            />
          </div>

          <div>
            <label class="block text-xs font-medium text-[var(--text-secondary)] mb-1">
              Area (Optional)
            </label>
            <Select
              v-model="createAreaId"
              :options="createAreaOptions"
              placeholder="None"
              size="md"
            />
          </div>
        </div>

        <div class="flex items-center justify-end gap-2 pt-3">
          <Button variant="ghost" @click="showCreateModal = false">
            Cancel
          </Button>
          <Button variant="primary" type="submit" :loading="creating">
            Enroll & Generate Keys
          </Button>
        </div>
      </form>
    </Modal>

    <!-- Revoke Confirmation Dialog -->
    <DangerConfirmDialog
      v-model="showRevokeDialog"
      title="Revoke Device Access"
      :message="`Revoking device ${revokeTargetDeviceId} immediately invalidates its MQTT credentials and session tokens.`"
      :confirm-phrase="revokeTargetDeviceId"
      danger-action-text="Revoke Permanently"
      :loading="revoking"
      @confirm="confirmRevoke"
    />
  </div>
</template>
