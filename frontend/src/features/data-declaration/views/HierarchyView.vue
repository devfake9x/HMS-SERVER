<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import {
  adminApi,
  getCsrf,
  type OrganizationDto,
  type ProjectDto,
  type PackageDto,
  type AreaDto,
} from '@/core/api/adminApi'
import { createSessionFence } from '@/core/auth/sessionFence'
import TreeDataTable, { type TreeNode } from '@/shared/ui/data-table/TreeDataTable.vue'
import Button from '@/shared/ui/primitives/Button.vue'
import Modal from '@/shared/ui/feedback/Modal.vue'
import Select, { type SelectOption } from '@/shared/ui/Select.vue'
import Input from '@/shared/ui/Input.vue'
import {
  FolderTree,
  Building2,
  Briefcase,
  Layers,
  Folder,
  Plus,
  RefreshCw,
  Edit2,
} from '@lucide/vue'

const sessionFence = createSessionFence()
const loading = ref(true)

const organizations = ref<OrganizationDto[]>([])
const projects = ref<ProjectDto[]>([])
const packages = ref<PackageDto[]>([])
const areas = ref<AreaDto[]>([])

// Create modal state
const showCreateModal = ref(false)
const createType = ref<'org' | 'project' | 'package' | 'area'>('org')
const createName = ref('')
const createParentOrgId = ref<string | null>(null)
const createParentProjId = ref<string | null>(null)
const creating = ref(false)
const createError = ref('')

const orgOptions = computed<SelectOption[]>(() =>
  organizations.value.map((org) => ({ value: org.id, label: org.name }))
)

const projOptions = computed<SelectOption[]>(() =>
  projects.value.map((p) => ({ value: p.id, label: p.name }))
)

// Tree Nodes computation
const treeNodes = computed<TreeNode[]>(() => {
  return organizations.value.map((org) => {
    const orgProjects = projects.value.filter((p) => p.organizationId === org.id)
    const orgAreas = areas.value.filter((a) => a.organizationId === org.id)

    const projectNodes: TreeNode[] = orgProjects.map((proj) => {
      const projPackages = packages.value.filter((pkg) => pkg.projectId === proj.id)
      return {
        id: `proj-${proj.id}`,
        name: proj.name,
        type: 'project',
        children: projPackages.map((pkg) => ({
          id: `pkg-${pkg.id}`,
          name: pkg.name,
          type: 'package',
        })),
      }
    })

    return {
      id: `org-${org.id}`,
      name: org.name,
      type: 'org',
      children: [
        ...projectNodes,
        ...orgAreas.map((a) => ({
          id: `area-${a.id}`,
          name: a.name,
          type: 'area',
        })),
      ],
    }
  })
})

async function loadHierarchy() {
  loading.value = true
  const lease = sessionFence.rotate()
  try {
    const [orgList, projList, pkgList, areaList] = await Promise.all([
      adminApi.listOrganizations({ signal: lease.signal }).catch(() => []),
      adminApi.listProjects({ signal: lease.signal }).catch(() => []),
      adminApi.listPackages({ signal: lease.signal }).catch(() => []),
      adminApi.listAreas({ signal: lease.signal }).catch(() => []),
    ])

    if (!sessionFence.isCurrent(lease)) return

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

function openCreateModal(type: 'org' | 'project' | 'package' | 'area') {
  createType.value = type
  createName.value = ''
  createParentOrgId.value = organizations.value[0]?.id || ''
  createParentProjId.value = projects.value[0]?.id || ''
  createError.value = ''
  showCreateModal.value = true
}

async function handleCreateNode() {
  if (!createName.value.trim()) {
    createError.value = 'Name is required.'
    return
  }

  creating.value = true
  createError.value = ''
  const lease = sessionFence.capture()

  try {
    const csrf = await getCsrf({ signal: lease.signal }).catch(() => null)
    const csrfToken = csrf?.requestToken

    if (createType.value === 'org') {
      await adminApi.createOrganization(createName.value, null, { csrfToken, signal: lease.signal })
    } else if (createType.value === 'project') {
      await adminApi.createProject(createName.value, createParentOrgId.value || '', { csrfToken, signal: lease.signal })
    } else if (createType.value === 'package') {
      await adminApi.createPackage(createName.value, createParentProjId.value || '', { csrfToken, signal: lease.signal })
    } else if (createType.value === 'area') {
      await adminApi.createArea(createName.value, createParentOrgId.value || '', { csrfToken, signal: lease.signal })
    }

    showCreateModal.value = false
    await loadHierarchy()
  } catch (err: any) {
    createError.value = err?.message || 'Failed to create hierarchy node.'
  } finally {
    creating.value = false
  }
}

onMounted(() => {
  void loadHierarchy()
})
</script>

<template>
  <div class="space-y-4" data-test="hierarchy-view">
    <!-- Header -->
    <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
      <div>
        <h1 class="text-xl font-bold tracking-tight text-[var(--text-heading)]">
          Infrastructure Data Declaration
        </h1>
        <p class="mt-1 text-xs text-[var(--text-hint)]">
          Hierarchical structure: Organization &rarr; Project &rarr; Work Package &rarr; Area &rarr; Devices.
        </p>
      </div>

      <div class="flex items-center gap-2">
        <Button variant="outline" size="sm" :loading="loading" @click="loadHierarchy">
          <template #icon-left>
            <RefreshCw class="w-3.5 h-3.5" />
          </template>
          Sync
        </Button>

        <Button variant="primary" size="sm" @click="openCreateModal('org')">
          <template #icon-left>
            <Plus class="w-3.5 h-3.5" />
          </template>
          Add Organization
        </Button>
      </div>
    </div>

    <!-- Quick Actions Pills -->
    <div class="flex items-center gap-2 overflow-x-auto pb-1">
      <button
        type="button"
        class="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-md text-xs font-medium bg-[var(--surface-card)] text-[var(--text-secondary)] hover:text-[var(--text-primary)] hover:bg-[var(--surface-overlay)] border border-[var(--border-default)] transition-colors cursor-pointer shadow-xs"
        @click="openCreateModal('project')"
      >
        <Briefcase class="w-3.5 h-3.5 text-amber-500" />
        <span>+ Add Project</span>
      </button>

      <button
        type="button"
        class="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-md text-xs font-medium bg-[var(--surface-card)] text-[var(--text-secondary)] hover:text-[var(--text-primary)] hover:bg-[var(--surface-overlay)] border border-[var(--border-default)] transition-colors cursor-pointer shadow-xs"
        @click="openCreateModal('package')"
      >
        <Layers class="w-3.5 h-3.5 text-sky-500" />
        <span>+ Add Package</span>
      </button>

      <button
        type="button"
        class="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-md text-xs font-medium bg-[var(--surface-card)] text-[var(--text-secondary)] hover:text-[var(--text-primary)] hover:bg-[var(--surface-overlay)] border border-[var(--border-default)] transition-colors cursor-pointer shadow-xs"
        @click="openCreateModal('area')"
      >
        <Folder class="w-3.5 h-3.5 text-emerald-500" />
        <span>+ Add Area</span>
      </button>
    </div>

    <!-- Tree View Table -->
    <TreeDataTable
      :nodes="treeNodes"
      :loading="loading"
      default-expanded-all
      empty-message="No hierarchy nodes declared yet."
    />

    <!-- Create Node Modal -->
    <Modal
      v-model="showCreateModal"
      :title="`Create ${createType.toUpperCase()} Node`"
      subtitle="Expand the operational infrastructure hierarchy."
      max-width="md"
    >
      <form @submit.prevent="handleCreateNode" class="space-y-4">
        <div v-if="createError" class="p-2.5 rounded-lg bg-rose-500/10 border border-rose-500/20 text-xs text-rose-600 dark:text-rose-400">
          {{ createError }}
        </div>

        <div>
          <label class="block text-xs font-medium text-[var(--text-secondary)] mb-1">
            Node Name *
          </label>
          <Input
            v-model="createName"
            type="text"
            required
            placeholder="e.g. Metro Construction Package A"
            size="md"
          />
        </div>

        <!-- Parent Org if Project or Area -->
        <div v-if="createType === 'project' || createType === 'area'">
          <label class="block text-xs font-medium text-[var(--text-secondary)] mb-1">
            Parent Organization *
          </label>
          <Select
            v-model="createParentOrgId"
            :options="orgOptions"
            placeholder="Select parent organization..."
            size="md"
          />
        </div>

        <!-- Parent Project if Package -->
        <div v-if="createType === 'package'">
          <label class="block text-xs font-medium text-[var(--text-secondary)] mb-1">
            Parent Project *
          </label>
          <Select
            v-model="createParentProjId"
            :options="projOptions"
            placeholder="Select parent project..."
            size="md"
          />
        </div>

        <div class="flex items-center justify-end gap-2 pt-3">
          <Button variant="ghost" @click="showCreateModal = false">
            Cancel
          </Button>
          <Button variant="primary" type="submit" :loading="creating">
            Create Node
          </Button>
        </div>
      </form>
    </Modal>
  </div>
</template>
