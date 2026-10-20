// @vitest-environment jsdom
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import DataTable from './DataTable.vue'

describe('DataTable', () => {
  const columns = [
    { key: 'id', label: 'ID', sortable: true },
    { key: 'name', label: 'Name', sortable: true },
    { key: 'status', label: 'Status' },
  ]

  const items = [
    { id: 1, name: 'Sensor Alpha', status: 'online' },
    { id: 2, name: 'Gateway Beta', status: 'offline' },
    { id: 3, name: 'Controller Gamma', status: 'online' },
  ]

  it('renders table headers and rows correctly', () => {
    const wrapper = mount(DataTable, {
      props: { columns, items },
    })

    expect(wrapper.find('[data-test="data-table"]').exists()).toBe(true)
    const rows = wrapper.findAll('[data-test="table-row"]')
    expect(rows.length).toBe(3)
    expect(wrapper.text()).toContain('Sensor Alpha')
    expect(wrapper.text()).toContain('Gateway Beta')
  })

  it('shows empty message when no items', () => {
    const wrapper = mount(DataTable, {
      props: { columns, items: [], emptyMessage: 'Zero devices found' },
    })
    expect(wrapper.find('[data-test="table-empty"]').text()).toContain('Zero devices found')
  })

  it('handles row selection when selectable is enabled', async () => {
    const wrapper = mount(DataTable, {
      props: {
        columns,
        items,
        selectable: true,
        selectedIds: [],
      },
    })

    const selectAllCheckbox = wrapper.find('[data-test="table-select-all"]')
    expect(selectAllCheckbox.exists()).toBe(true)

    const rowCheckboxes = wrapper.findAll('[data-test="table-row-select"]')
    expect(rowCheckboxes.length).toBe(3)

    await rowCheckboxes[0].setValue(true)
    expect(wrapper.emitted('update:selectedIds')?.[0]).toEqual([[1]])
  })

  it('handles select-all toggle', async () => {
    const wrapper = mount(DataTable, {
      props: {
        columns,
        items,
        selectable: true,
        selectedIds: [],
      },
    })

    const selectAll = wrapper.find('[data-test="table-select-all"]')
    await selectAll.setValue(true)
    expect(wrapper.emitted('update:selectedIds')?.[0]).toEqual([[1, 2, 3]])
  })

  it('reflects indeterminate property on select all checkbox when partial rows selected', async () => {
    const wrapper = mount(DataTable, {
      props: {
        columns,
        items,
        selectable: true,
        selectedIds: [1],
      },
    })

    await nextTick()
    const selectAllEl = wrapper.find<HTMLInputElement>('input[data-test="table-select-all"]').element
    expect(selectAllEl.indeterminate).toBe(true)
  })

  it('sorts columns when sortable header is clicked', async () => {
    const wrapper = mount(DataTable, {
      props: { columns, items },
    })

    const nameHeader = wrapper.find('[data-test="table-header-name"]')
    await nameHeader.trigger('click')

    // Ascending: Controller Gamma, Gateway Beta, Sensor Alpha
    const rows = wrapper.findAll('[data-test="table-row"]')
    expect(rows[0].text()).toContain('Controller Gamma')
  })
})
