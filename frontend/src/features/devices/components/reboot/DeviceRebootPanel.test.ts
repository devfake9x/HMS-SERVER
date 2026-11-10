// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { effectScope, type EffectScope } from 'vue'
import { mount, type VueWrapper } from '@vue/test-utils'
import { useDeviceReboot } from '@/core/commands/useDeviceReboot'
import { adminApi, type DeviceLinkDto } from '@/core/api/adminApi'
import { fakeDevice, type Scenario, type SentCommand } from '@/core/commands/testing/fakeDevice'
import DeviceRebootPanel from './DeviceRebootPanel.vue'

const REBOOT = 'system.reboot'
const INFO = 'system.get_info'
const STATUS = 'control.get_status'
const TIME = 'system.get_time'
const allowed = [REBOOT, INFO, STATUS, TIME]
const catalog = { catalogVersion: 'v', catalogHash: 'h', trusted: true, actions: [] }
const link = (extra: Partial<DeviceLinkDto> = {}): DeviceLinkDto => ({
  binding: { topicPrefix: 'lab', mqttDeviceId: 'gateway_870c', keyVersion: 1 },
  keyGeneration: 1, commandKeyProvisioned: true, connectivity: 'online', connectivityAt: null,
  lastHeartbeatAt: null, online: true, capabilitiesAt: null, capabilityCount: 0, brokerConnected: true,
  allowedOutputChannels: [], canControlOutputs: false, canViewOutputs: true, ...extra,
})
const props = (extra: Record<string, unknown> = {}) => ({
  deviceId: 'dev-1', csrfToken: 'csrf', catalog, catalogVerified: true, allowedCommands: allowed,
  link: link(), commandEvent: null, outputsKey: 0, ...extra,
})

const channel = (n: number, state: boolean, forced = false) => ({ channel: n, state, mode: 'toggle', enabled: true, gpioOk: true, forced })
const relaysNow = { channels: [channel(1, true), channel(2, true, true), channel(3, true), channel(4, false, true), channel(5, false)] }
const scheduled = { status: 'Executed', code: 'DEVICE_REBOOT_SCHEDULED' }
const observed = { status: 'Executed', code: 'DEVICE_REBOOT_OBSERVED' }

interface Rig {
  dry?: Scenario
  real?: Scenario
  uptimeBefore?: number
  uptimeAfter?: number
  status?: (n: number) => Scenario | undefined
  before?: unknown
  after?: unknown
}

let sent: SentCommand[] = []
function startDevice(rig: Rig = {}) {
  let infoReads = 0
  let statusReads = 0
  sent = fakeDevice((key, _payload, n) => {
    if (key === REBOOT) return n === 1 ? (rig.dry ?? { data: { would_apply: true } }) : (rig.real ?? { results: [scheduled, observed] })
    if (key === INFO) return { data: { uptime_seconds: ++infoReads === 1 ? (rig.uptimeBefore ?? 500) : (rig.uptimeAfter ?? 4), firmware: '1.0.108' } }
    if (key === STATUS) {
      const n = ++statusReads
      return rig.status?.(n) ?? { data: n === 1 ? (rig.before ?? relaysNow) : (rig.after ?? { channels: [channel(1, false), channel(2, true, true), channel(3, false), channel(4, false, true), channel(5, false)] }) }
    }
    if (key === TIME) return { data: { epoch: Math.floor(Date.now() / 1000), ntp_synced: true } }
    return undefined
  })
}

const wrappers: VueWrapper[] = []
const scopes: EffectScope[] = []
// The page owns the reboot run, so a panel that is unmounted and mounted again finds it going on.
function makeReboot(extra: Record<string, unknown> = {}) {
  const p = props(extra)
  const scope = effectScope()
  scopes.push(scope)
  return scope.run(() => useDeviceReboot(() => p.deviceId, () => ({ csrfToken: p.csrfToken, catalog: p.catalog, allowedCommands: p.allowedCommands })))!
}
function mountPanel(extra: Record<string, unknown> = {}, reboot = makeReboot(extra)) {
  const wrapper = mount(DeviceRebootPanel, { attachTo: document.body, props: { ...props(extra), reboot } })
  wrappers.push(wrapper)
  return wrapper
}
const sends = (key: string) => sent.filter(s => s.key === key)
const body = () => document.body.querySelector('[data-test="danger-confirm-dialog"]')
const confirmButton = () => document.body.querySelector('[data-test="danger-confirm-btn"]') as HTMLButtonElement | null
const text = (selector: string, wrapper?: VueWrapper) =>
  (wrapper?.find(selector).text() ?? document.body.querySelector(selector)?.textContent ?? '').replace(/\s+/g, ' ').trim()

async function type(value: string) {
  const input = document.body.querySelector('[data-test="danger-confirm-input"]') as HTMLInputElement
  input.value = value
  input.dispatchEvent(new Event('input'))
  await vi.advanceTimersByTimeAsync(0)
}
async function openDialog(wrapper: VueWrapper) {
  await wrapper.find('[data-test="reboot-btn"]').trigger('click')
  await vi.advanceTimersByTimeAsync(6000)
}
async function confirmReboot(wrapper: VueWrapper) {
  await openDialog(wrapper)
  await type('REBOOT-gateway_870c')
  confirmButton()!.click()
  await vi.advanceTimersByTimeAsync(0)
}

describe('DeviceRebootPanel', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-11-10T00:00:00Z'))
  })
  afterEach(() => {
    while (wrappers.length) wrappers.pop()!.unmount()
    while (scopes.length) scopes.pop()!.stop()
    vi.restoreAllMocks()
    vi.useRealTimers()
  })

  describe('the button', () => {
    it('is absent when system.reboot is not in the allowed commands', () => {
      const wrapper = mountPanel({ allowedCommands: [INFO, STATUS] })

      expect(wrapper.find('[data-test="reboot-btn"]').exists()).toBe(false)
    })

    it('is locked with the reason when the device is offline', () => {
      const wrapper = mountPanel({ link: link({ online: false }) })
      const button = wrapper.find('[data-test="reboot-btn"]')

      expect(button.exists()).toBe(true)
      expect((button.element as HTMLButtonElement).disabled).toBe(true)
      expect(wrapper.find('[data-test="reboot-lock-reason"]').text()).toBe('Thiết bị offline')
    })

    it('is locked when the catalog is not verified or the link has no MQTT device id', () => {
      expect(text('[data-test="reboot-lock-reason"]', mountPanel({ catalogVerified: false }))).toBe('Catalog lệnh chưa được xác minh')
      expect(text('[data-test="reboot-lock-reason"]', mountPanel({ link: link({ binding: null }) }))).toBe('Chưa có định danh MQTT')
    })
  })

  describe('before the dialog', () => {
    it('reads the signed uptime and the relay table first, and sends no reboot yet', async () => {
      startDevice()
      const wrapper = mountPanel()
      await openDialog(wrapper)

      expect(sent.map(s => s.key)).toEqual([INFO, STATUS])
      expect(body()).not.toBeNull()
      expect(sends(REBOOT)).toHaveLength(0)
    })

    it('tells what each relay does: unforced on goes off, forced off stays, forced on may drop then return, DO rules may turn one on', async () => {
      startDevice()
      await openDialog(mountPanel())

      expect(text('[data-test="reboot-line-turn-off"]')).toBe('DO1, DO3 đang BẬT (không force) sẽ TẮT')
      expect(text('[data-test="reboot-line-forced-off"]')).toBe('DO4 force TẮT, giữ TẮT')
      expect(text('[data-test="reboot-line-forced-on"]')).toBe('DO2 force BẬT: có thể TẮT trong lúc khởi động, BẬT lại khi thiết bị lên')
      expect(text('[data-test="reboot-line-rules"]')).toBe('Luật DO SET_HIGH/SET_LOW (nếu có) chạy ngay sau khởi động và có thể BẬT relay')
      expect(document.body.querySelector('[data-test="reboot-read-at"]')).not.toBeNull()
      expect(body()?.textContent).toContain('mất kết nối')
    })

    it('says no relay is on when none is', async () => {
      startDevice({ before: { channels: [channel(1, false), channel(2, false, true)] } })
      await openDialog(mountPanel())

      expect(text('[data-test="reboot-line-none"]')).toBe('Không có relay nào đang BẬT')
      expect(document.body.querySelector('[data-test="reboot-line-turn-off"]')).toBeNull()
    })

    it('warns in general words when the relay table cannot be read', async () => {
      startDevice({ before: { channels: 'garbage' } })
      await openDialog(mountPanel())

      expect(text('[data-test="reboot-line-unreadable"]')).toBe('Không đọc được trạng thái relay; mọi relay không bị force sẽ TẮT')
      expect(body()).not.toBeNull()
    })

    it('does not read the relay table without permission, and warns in general words', async () => {
      startDevice()
      await openDialog(mountPanel({ allowedCommands: [REBOOT, INFO] }))

      expect(sent.map(s => s.key)).toEqual([INFO])
      expect(text('[data-test="reboot-line-unreadable"]')).toContain('Không đọc được trạng thái relay')
    })
  })

  describe('leaving', () => {
    it('stops the reads before the dialog when the panel is unmounted', async () => {
      startDevice({ status: () => ({ result: { status: 'Failed', code: 'DEVICE_REJECTED', detail: 'LANE_FULL' } }) })
      const wrapper = mountPanel()
      await wrapper.find('[data-test="reboot-btn"]').trigger('click')
      await vi.advanceTimersByTimeAsync(5000)
      expect(sends(STATUS).length).toBeGreaterThan(0)

      wrapper.unmount()
      wrappers.splice(wrappers.indexOf(wrapper), 1)
      const count = sent.length
      await vi.advanceTimersByTimeAsync(90_000)

      expect(sent.length).toBe(count)
    })
  })

  describe('the confirmation', () => {
    it('enables the button only for REBOOT-<mqttDeviceId>, never for the server device id', async () => {
      startDevice()
      await openDialog(mountPanel())

      expect(confirmButton()!.disabled).toBe(true)
      await type('REBOOT-dev-1')
      expect(confirmButton()!.disabled).toBe(true)
      await type('REBOOT-gateway')
      expect(confirmButton()!.disabled).toBe(true)
      await type('REBOOT-gateway_870c')
      expect(confirmButton()!.disabled).toBe(false)
      expect(body()?.textContent).toContain('REBOOT-gateway_870c')
      expect(body()?.textContent).toContain('Gõ')
      expect(body()?.textContent).toContain('Huỷ')
    })

    // The server's verified state for the relays of relaysNow, with the changes a test wants.
    const verified = (change: (rows: Array<Record<string, unknown>>) => void = () => undefined, extra: Record<string, unknown> = {}) => {
      const rows = relaysNow.channels.map(c => ({ channel: c.channel, state: c.state, mode: c.mode, pulseActive: false }))
      change(rows)
      vi.spyOn(adminApi, 'deviceOutputs').mockResolvedValue({
        kind: 'ok', state: { channels: rows, readAt: '2026-11-10T00:00:00Z', stale: false, staleReason: null, ...extra },
      } as never)
    }
    const outputsEvent = async (wrapper: VueWrapper) => {
      await wrapper.setProps({ outputsKey: 1 })
      await vi.advanceTimersByTimeAsync(0)
    }
    const CHANGED = 'Trạng thái relay vừa đổi; mở lại để xem danh sách mới'

    it('stays open when an outputs event arrives but the relays are as the dialog shows them', async () => {
      startDevice()
      const wrapper = mountPanel()
      await openDialog(wrapper)
      verified()

      await outputsEvent(wrapper)

      expect(body()).not.toBeNull()
      expect(wrapper.find('[data-test="reboot-notice"]').exists()).toBe(false)
    })

    it('closes and says why when a relay changed since the dialog was built', async () => {
      startDevice()
      const wrapper = mountPanel()
      await openDialog(wrapper)
      verified(rows => { rows[0].state = false })

      await outputsEvent(wrapper)

      expect(body()).toBeNull()
      expect(text('[data-test="reboot-notice"]', wrapper)).toBe(CHANGED)
      expect(sends(REBOOT)).toHaveLength(0)
    })

    it.each([
      ['a relay mode', (rows: Array<Record<string, unknown>>) => { rows[1].mode = 'pulse' }],
      ['a pulse running', (rows: Array<Record<string, unknown>>) => { rows[2].pulseActive = true }],
      ['a channel added', (rows: Array<Record<string, unknown>>) => { rows.push({ channel: 6, state: false, mode: 'toggle', pulseActive: false }) }],
      ['a channel removed', (rows: Array<Record<string, unknown>>) => { rows.pop() }],
    ])('closes for %s', async (_name, change) => {
      startDevice()
      const wrapper = mountPanel()
      await openDialog(wrapper)
      verified(change)

      await outputsEvent(wrapper)

      expect(body()).toBeNull()
      expect(text('[data-test="reboot-notice"]', wrapper)).toBe(CHANGED)
    })

    it('closes when the state is marked stale, since the list is no longer trustworthy', async () => {
      startDevice()
      const wrapper = mountPanel()
      await openDialog(wrapper)
      verified(() => undefined, { stale: true, staleReason: 'device_reboot_scheduled' })

      await outputsEvent(wrapper)

      expect(body()).toBeNull()
      expect(text('[data-test="reboot-notice"]', wrapper)).toContain('không còn đáng tin')
    })

    it('closes when the verified state cannot be fetched to compare', async () => {
      startDevice()
      const wrapper = mountPanel()
      await openDialog(wrapper)
      vi.spyOn(adminApi, 'deviceOutputs').mockRejectedValue(new Error('Request failed (500)'))

      await outputsEvent(wrapper)

      expect(body()).toBeNull()
      expect(text('[data-test="reboot-notice"]', wrapper)).toContain('không còn đáng tin')
    })

    it('does not close for a change of the relay state while no dialog is open', async () => {
      startDevice()
      const wrapper = mountPanel()
      await wrapper.setProps({ outputsKey: 1 })

      expect(wrapper.find('[data-test="reboot-notice"]').exists()).toBe(false)
    })

    it('waits for a relay comparison in flight before sending, and sends nothing if it finds a change', async () => {
      startDevice()
      const wrapper = mountPanel()
      await openDialog(wrapper)
      let finish!: () => void
      const rows = relaysNow.channels.map(c => ({ channel: c.channel, state: c.state, mode: c.mode, pulseActive: false }))
      rows[0].state = false
      vi.spyOn(adminApi, 'deviceOutputs').mockImplementation(() => new Promise(resolve => {
        finish = () => resolve({ kind: 'ok', state: { channels: rows, readAt: 'x', stale: false, staleReason: null } } as never)
      }))
      await wrapper.setProps({ outputsKey: 1 })
      await vi.advanceTimersByTimeAsync(0)
      await type('REBOOT-gateway_870c')

      confirmButton()!.click()
      await vi.advanceTimersByTimeAsync(0)
      expect(sends(REBOOT)).toHaveLength(0)
      finish()
      await vi.advanceTimersByTimeAsync(0)

      expect(sends(REBOOT)).toHaveLength(0)
      expect(body()).toBeNull()
      expect(text('[data-test="reboot-notice"]', wrapper)).toBe('Trạng thái relay vừa đổi; mở lại để xem danh sách mới')
    })

    it('sends nothing and reads again when the readings are older than 30 s at confirmation', async () => {
      startDevice()
      const wrapper = mountPanel()
      await openDialog(wrapper)
      await vi.advanceTimersByTimeAsync(31_000)
      await type('REBOOT-gateway_870c')

      confirmButton()!.click()
      await vi.advanceTimersByTimeAsync(6000)

      expect(sends(REBOOT)).toHaveLength(0)
      expect(sends(INFO)).toHaveLength(2)
      expect(sends(STATUS)).toHaveLength(2)
      expect(body()).not.toBeNull()
      expect(confirmButton()!.disabled).toBe(true)
    })

    it('sends nothing and says why when the gate locked while the dialog was open', async () => {
      startDevice()
      const wrapper = mountPanel()
      await openDialog(wrapper)
      await wrapper.setProps({ link: link({ online: false }) })
      await type('REBOOT-gateway_870c')

      confirmButton()!.click()
      await vi.advanceTimersByTimeAsync(0)

      expect(sends(REBOOT)).toHaveLength(0)
      expect(body()).toBeNull()
      expect(text('[data-test="reboot-notice"]', wrapper)).toContain('Thiết bị offline')
    })
  })

  describe('while it runs', () => {
    it('shows each phase, locks the button and offers no resend', async () => {
      startDevice({ real: { results: [scheduled] } })
      const wrapper = mountPanel()
      await confirmReboot(wrapper)

      expect(body()).toBeNull()
      expect(text('[data-test="reboot-status"]', wrapper)).toBe('Đang chạy thử…')
      expect((wrapper.find('[data-test="reboot-btn"]').element as HTMLButtonElement).disabled).toBe(true)

      await vi.advanceTimersByTimeAsync(1500)
      expect(text('[data-test="reboot-status"]', wrapper)).toBe('Đang gửi lệnh…')

      await vi.advanceTimersByTimeAsync(2000)
      expect(text('[data-test="reboot-status"]', wrapper)).toBe('Đang khởi động lại…')
      expect(wrapper.find('[data-test="reboot-status"]').attributes('aria-live')).toBe('polite')
      expect(wrapper.text()).not.toMatch(/gửi lại/i)
      expect(wrapper.find('[data-test="reboot-btn"]').attributes('disabled')).toBeDefined()
      expect(sends(REBOOT)).toHaveLength(2)

      await vi.advanceTimersByTimeAsync(400_000)
      expect(sends(REBOOT)).toHaveLength(2)
    })

    it('goes on when the panel is unmounted and mounted again, and shows the live phase and then the result', async () => {
      startDevice()
      const reboot = makeReboot()
      const first = mountPanel({}, reboot)
      await confirmReboot(first)
      await vi.advanceTimersByTimeAsync(3000)
      expect(text('[data-test="reboot-status"]', first)).toBe('Đang khởi động lại…')

      first.unmount()
      wrappers.splice(wrappers.indexOf(first), 1)
      await vi.advanceTimersByTimeAsync(500)
      const second = mountPanel({}, reboot)
      await vi.advanceTimersByTimeAsync(0)

      expect(text('[data-test="reboot-status"]', second)).toBe('Đang khởi động lại…')
      expect(second.find('[data-test="reboot-btn"]').attributes('disabled')).toBeDefined()

      await vi.advanceTimersByTimeAsync(60_000)
      expect(text('[data-test="reboot-status"]', second)).toBe('Đã khởi động lại')
      expect(sends(REBOOT)).toHaveLength(2)
    })

    it('polls at once when the page hears the result of the reboot it follows', async () => {
      startDevice({ real: { results: [scheduled] } })
      const wrapper = mountPanel()
      await confirmReboot(wrapper)
      await vi.advanceTimersByTimeAsync(3000)
      const id = sends(REBOOT)[1].correlationId
      const polls = () => vi.mocked(adminApi.commandResult).mock.calls.filter(c => c[1] === id).length
      const before = polls()

      await wrapper.setProps({ commandEvent: { correlationId: 'other' } })
      await vi.advanceTimersByTimeAsync(0)
      expect(polls()).toBe(before)

      await wrapper.setProps({ commandEvent: { correlationId: id } })
      await vi.advanceTimersByTimeAsync(0)
      expect(polls()).toBe(before + 1)
    })
  })

  describe('the result', () => {
    async function finishRun(rig: Rig = {}) {
      startDevice(rig)
      const wrapper = mountPanel()
      await confirmReboot(wrapper)
      await vi.advanceTimersByTimeAsync(60_000)
      return wrapper
    }

    it('says "Đã khởi động lại" only with the signed uptime lower than before, and shows both numbers', async () => {
      const wrapper = await finishRun({ uptimeBefore: 500, uptimeAfter: 4 })

      expect(text('[data-test="reboot-status"]', wrapper)).toBe('Đã khởi động lại')
      expect(text('[data-test="reboot-uptime"]', wrapper)).toContain('500')
      expect(text('[data-test="reboot-uptime"]', wrapper)).toContain('4')
      expect(text('[data-test="reboot-firmware"]', wrapper)).toContain('1.0.108')
      expect(text('[data-test="reboot-relays"]', wrapper)).toContain('DO2')
      expect(wrapper.find('[data-test="reboot-btn"]').attributes('disabled')).toBeUndefined()
      expect(wrapper.emitted('refresh-link')).toHaveLength(1)
    })

    it('says the device reported coming back, unverified, when the signed uptime did not drop', async () => {
      const wrapper = await finishRun({ uptimeBefore: 500, uptimeAfter: 900 })

      expect(text('[data-test="reboot-status"]', wrapper)).toBe('Thiết bị báo đã lên lại (chưa xác minh)')
    })

    it('lists the faults of forced channels as warnings and unforced channels that are on as information', async () => {
      const wrapper = await finishRun({ after: { channels: [channel(1, true), channel(2, false, true), channel(3, false), channel(4, false, true), channel(5, false)] } })

      expect(text('[data-test="reboot-deviations"]', wrapper)).toContain('DO2')
      expect(text('[data-test="reboot-notes"]', wrapper)).toContain('DO1')
      expect(text('[data-test="reboot-notes"]', wrapper)).toContain('SET_HIGH/SET_LOW')
    })

    it('says whether the reboot was verified, and that the relay table could not be checked', async () => {
      const wrapper = await finishRun({ after: { channels: 'garbage' } })

      expect(text('[data-test="reboot-status"]', wrapper)).toBe('Đã khởi động lại. Chưa kiểm được trạng thái relay')
      expect(wrapper.find('[data-test="reboot-relays"]').exists()).toBe(false)
    })

    it('keeps the unverified wording when the relay table could not be checked either', async () => {
      const wrapper = await finishRun({ uptimeBefore: 500, uptimeAfter: 900, after: { channels: 'garbage' } })

      expect(text('[data-test="reboot-status"]', wrapper)).toBe('Thiết bị báo đã lên lại (chưa xác minh). Chưa kiểm được trạng thái relay')
    })

    it('shows the message of a refusal and sends no second reboot', async () => {
      const wrapper = await finishRun({ real: { dispatch: { status: 'ContractUnavailable', code: 'DEVICE_REBOOT_TOO_SOON' } } })

      expect(text('[data-test="reboot-status"]', wrapper)).toBe('Chưa đủ 60 s từ lần khởi động lại trước; thử lại sau')
      expect(sends(REBOOT)).toHaveLength(2)
      expect(wrapper.emitted('refresh-link')).toBeUndefined()
    })

    it('clears the previous result and notice when the next attempt starts', async () => {
      const wrapper = await finishRun({ real: { dispatch: { status: 'ContractUnavailable', code: 'DEVICE_REBOOT_TOO_SOON' } } })
      expect(text('[data-test="reboot-status"]', wrapper)).toContain('Chưa đủ 60 s')

      await wrapper.find('[data-test="reboot-btn"]').trigger('click')
      await vi.advanceTimersByTimeAsync(0)
      expect(wrapper.find('[data-test="reboot-status"]').text()).toBe('Đang đọc trạng thái thiết bị…')
      expect(wrapper.text()).not.toContain('Chưa đủ 60 s')
      await vi.advanceTimersByTimeAsync(6000)

      expect(body()).not.toBeNull()
      expect(wrapper.find('[data-test="reboot-status"]').exists()).toBe(false)
      expect(wrapper.find('[data-test="reboot-notice"]').exists()).toBe(false)
    })

    it('shows a failed dry run as that, and sends no real command', async () => {
      startDevice({ dry: { result: { status: 'Failed', code: 'DEVICE_REJECTED', detail: 'UNAUTHORIZED' } } })
      const wrapper = mountPanel()
      await confirmReboot(wrapper)
      await vi.advanceTimersByTimeAsync(10_000)

      expect(text('[data-test="reboot-status"]', wrapper)).toContain('Chạy thử không đạt')
      expect(text('[data-test="reboot-status"]', wrapper)).toContain('UNAUTHORIZED')
      expect(sends(REBOOT)).toHaveLength(1)
    })

    it('says the server restarted when the record is gone', async () => {
      const wrapper = await finishRun({ real: { results: [scheduled, new Error('Request failed (404)')] } })

      expect(text('[data-test="reboot-status"]', wrapper)).toBe('Server đã khởi động lại và mất bản ghi lệnh; chưa rõ kết quả')
    })
  })
})
