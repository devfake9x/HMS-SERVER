import { describe, it, expect } from 'vitest'
import type { DeviceLinkDto } from '@/core/api/adminApi'
import {
  checkAfterReboot,
  isVerifiedReboot,
  rebootGate,
  rebootMessage,
  rebootPhrase,
  rebootResultText,
  relaysBeforeReboot,
} from './rebootPlan'

const channel = (n: number, state: boolean, extra: Record<string, unknown> = {}) =>
  ({ channel: n, state, mode: 'toggle', enabled: true, gpioOk: true, forced: false, ...extra })

const link = (extra: Partial<DeviceLinkDto> = {}): DeviceLinkDto => ({
  binding: { topicPrefix: 'lab', mqttDeviceId: 'gateway_870c', keyVersion: 1 },
  keyGeneration: 1, commandKeyProvisioned: true, connectivity: 'online', connectivityAt: null,
  lastHeartbeatAt: null, online: true, capabilitiesAt: null, capabilityCount: 0, brokerConnected: true,
  allowedOutputChannels: [], canControlOutputs: false, canViewOutputs: true, ...extra,
})

describe('relaysBeforeReboot', () => {
  it('splits the channels by what a reboot does to them (spec 6.4)', () => {
    const plan = relaysBeforeReboot({
      channels: [
        channel(1, true),
        channel(2, true, { forced: true }),
        channel(3, false),
        channel(4, false, { forced: true }),
        channel(5, true),
        channel(6, false, { pulseActive: true }),
      ],
    })

    expect(plan.readable).toBe(true)
    expect(plan.turnOff).toEqual([1, 5, 6])
    expect(plan.forcedOff).toEqual([4])
    expect(plan.forcedOn).toEqual([2])
  })

  it('reads the channels one level deeper when the device wraps its reply', () => {
    const plan = relaysBeforeReboot({ success: true, data: { channels: [channel(1, true)] } })

    expect(plan.readable).toBe(true)
    expect(plan.turnOff).toEqual([1])
  })

  it.each([undefined, null, 'x', 5, {}, { channels: 'no' }, { channels: [{ state: true }] }])(
    'is not readable for %j', data => {
      const plan = relaysBeforeReboot(data)

      expect(plan.readable).toBe(false)
      expect(plan.turnOff).toEqual([])
      expect(plan.forcedOn).toEqual([])
    })
})

describe('checkAfterReboot', () => {
  const before = relaysBeforeReboot({
    channels: [channel(1, true), channel(2, true, { forced: true }), channel(4, false, { forced: true })],
  })

  it('has no deviation when forced channels hold their level and the rest is off', () => {
    const result = checkAfterReboot(before, {
      channels: [channel(1, false), channel(2, true, { forced: true }), channel(4, false, { forced: true })],
    })

    expect(result.deviations).toEqual([])
    expect(result.notes).toEqual([])
  })

  it('flags a forced-on channel that is off afterwards', () => {
    const result = checkAfterReboot(before, {
      channels: [channel(1, false), channel(2, false, { forced: true }), channel(4, false, { forced: true })],
    })

    expect(result.deviations).toHaveLength(1)
    expect(result.deviations[0]).toContain('DO2')
  })

  it('flags a forced-off channel that is on afterwards', () => {
    const result = checkAfterReboot(before, {
      channels: [channel(1, false), channel(2, true, { forced: true }), channel(4, true, { forced: true })],
    })

    expect(result.deviations).toHaveLength(1)
    expect(result.deviations[0]).toContain('DO4')
  })

  it('reports an unforced channel that is on as information about DO rules, not as a fault', () => {
    const result = checkAfterReboot(before, {
      channels: [channel(1, true), channel(2, true, { forced: true }), channel(4, false, { forced: true })],
    })

    expect(result.deviations).toEqual([])
    expect(result.notes).toHaveLength(1)
    expect(result.notes[0]).toContain('DO1')
    expect(result.notes[0]).toContain('SET_HIGH/SET_LOW')
  })
})

describe('isVerifiedReboot', () => {
  it('is true only when the signed uptime after is smaller than before', () => {
    expect(isVerifiedReboot(500, 4)).toBe(true)
    expect(isVerifiedReboot(500, 600)).toBe(false)
    expect(isVerifiedReboot(500, 500)).toBe(false)
  })

  it.each([[null, 4], [500, null], [undefined, 4], [500, undefined], ['500', 4], [500, 'x'], [null, null]])(
    'is false when a value is missing or not a number (%j, %j)', (before, after) => {
      expect(isVerifiedReboot(before, after)).toBe(false)
    })
})

describe('rebootGate', () => {
  const input = { allowedCommands: ['system.reboot'] as string[] | null, catalogVerified: true, link: link(), running: false }

  it('is open when everything holds', () => {
    expect(rebootGate(input)).toEqual({ visible: true, enabled: true, reason: null })
  })

  it.each([null, [], ['system.get_info']])('hides the control when system.reboot is not allowed (%j)', allowed => {
    expect(rebootGate({ ...input, allowedCommands: allowed }).visible).toBe(false)
  })

  it('locks with a reason when the catalog is not verified', () => {
    expect(rebootGate({ ...input, catalogVerified: false })).toEqual({ visible: true, enabled: false, reason: 'Catalog lệnh chưa được xác minh' })
  })

  it('locks when the device or the server broker link is offline', () => {
    expect(rebootGate({ ...input, link: link({ online: false }) }).reason).toBe('Thiết bị offline')
    expect(rebootGate({ ...input, link: link({ brokerConnected: false }) }).reason).toBe('Thiết bị offline')
    expect(rebootGate({ ...input, link: null }).reason).toBe('Thiết bị offline')
  })

  it('locks when the link has no MQTT device id', () => {
    expect(rebootGate({ ...input, link: link({ binding: null }) }).reason).toBe('Chưa có định danh MQTT')
    expect(rebootGate({ ...input, link: link({ binding: { topicPrefix: 'lab', mqttDeviceId: '', keyVersion: 1 } }) }).reason).toBe('Chưa có định danh MQTT')
  })

  it('locks while a reboot runs', () => {
    expect(rebootGate({ ...input, running: true })).toEqual({ visible: true, enabled: false, reason: 'Đang khởi động lại…' })
  })
})

describe('rebootPhrase', () => {
  it('is built from the MQTT device id of the link, never the server device id', () => {
    expect(rebootPhrase(link())).toBe('REBOOT-gateway_870c')
  })

  it('is empty without a binding, so nothing can match', () => {
    expect(rebootPhrase(link({ binding: null }))).toBe('')
    expect(rebootPhrase(null)).toBe('')
  })
})

describe('rebootMessage', () => {
  it.each([
    ['DEVICE_REBOOT_SCHEDULED', 'Đang khởi động lại…'],
    ['DEVICE_REBOOT_NOT_OBSERVED', 'Chưa thấy thiết bị lên lại sau 2 phút; server vẫn chờ thêm 10 phút. Không gửi lại'],
    ['DEVICE_REBOOT_OUTCOME_UNKNOWN', 'Chưa rõ kết quả (server mất theo dõi)'],
    ['DEVICE_REBOOT_IN_PROGRESS', 'Thiết bị đang khởi động lại, chờ kết quả'],
    ['DEVICE_REBOOT_TOO_SOON', 'Chưa đủ 60 s từ lần khởi động lại trước; thử lại sau'],
    ['HIGH_RISK_REQUIRES_MASTER_ADMIN', 'Chỉ master-admin được khởi động lại thiết bị'],
    ['CONFIRM_MISMATCH', 'Mã xác nhận không khớp định danh MQTT của thiết bị'],
    ['CONFIRM_REQUIRED', 'Thiếu mã xác nhận'],
    ['COMMAND_NOT_FOUND', 'Server đã khởi động lại và mất bản ghi lệnh; chưa rõ kết quả'],
    ['REBOOT_SEND_UNCONFIRMED', 'Không rõ lệnh có tới server không; chưa rõ kết quả'],
  ])('%s', (code, text) => {
    expect(rebootMessage(code, null)).toBe(text)
  })

  it('says rebooted only when the signed uptime confirmed it', () => {
    expect(rebootMessage('DEVICE_REBOOT_OBSERVED', null, true)).toBe('Đã khởi động lại')
    expect(rebootMessage('DEVICE_REBOOT_OBSERVED', null, false)).toBe('Thiết bị báo đã lên lại (chưa xác minh)')
    expect(rebootMessage('DEVICE_REBOOT_OBSERVED', null)).toBe('Thiết bị báo đã lên lại (chưa xác minh)')
  })

  it('tells the device refusals apart by their reason', () => {
    expect(rebootMessage('DEVICE_REJECTED', 'UNAUTHORIZED')).toBe('Thiết bị từ chối (UNAUTHORIZED: chống lặp, safe mode hoặc giới hạn 60 s)')
    expect(rebootMessage('DEVICE_REJECTED', 'confirm_required')).toBe('Thiết bị từ chối mã xác nhận')
  })

  it.each(['DEVICE_WRITE_BUSY', 'DEVICE_WRITE_RATE_LIMITED', 'COMMAND_PAYLOAD_INVALID', 'CATALOG_STALE', 'DEVICE_PERMISSION_REQUIRED'])(
    'gives %s a sentence of its own', code => {
      const text = rebootMessage(code, null)

      expect(text).not.toBe(code)
      expect(text).not.toContain('_')
    })

  it('falls back to the code and detail for a code it does not know', () => {
    expect(rebootMessage('SOMETHING_NEW', 'why')).toBe('SOMETHING_NEW: why')
    expect(rebootMessage('SOMETHING_NEW', null)).toBe('SOMETHING_NEW')
  })
})

describe('rebootResultText', () => {
  it('names the final outcome for the operator', () => {
    expect(rebootResultText('observed', 'DEVICE_REBOOT_OBSERVED', null, true)).toBe('Đã khởi động lại')
    expect(rebootResultText('observed', 'DEVICE_REBOOT_OBSERVED', null, false)).toBe('Thiết bị báo đã lên lại (chưa xác minh)')
    expect(rebootResultText('relay_unchecked', 'DEVICE_REBOOT_OBSERVED', null, true)).toBe('Đã khởi động lại. Chưa kiểm được trạng thái relay')
    expect(rebootResultText('relay_unchecked', 'DEVICE_REBOOT_OBSERVED', null, false)).toBe('Thiết bị báo đã lên lại (chưa xác minh). Chưa kiểm được trạng thái relay')
    expect(rebootResultText('not_observed', 'DEVICE_REBOOT_NOT_OBSERVED', null, false)).toBe(rebootMessage('DEVICE_REBOOT_NOT_OBSERVED', null))
    expect(rebootResultText('server_restarted', 'COMMAND_NOT_FOUND', null, false)).toBe(rebootMessage('COMMAND_NOT_FOUND', null))
    expect(rebootResultText('tracking_timeout', 'DEVICE_REBOOT_SCHEDULED', null, false)).toBe('Chưa có kết quả; server vẫn theo dõi, xem lại sau')
    expect(rebootResultText('untracked', 'DEVICE_EXECUTED', null, false)).toBe('Thiết bị đã nhận lệnh nhưng server không theo dõi được việc lên lại')
  })

  it('says a failed dry run is that, with the reason', () => {
    const text = rebootResultText('dry_run_failed', 'DEVICE_REJECTED', 'UNAUTHORIZED', false)

    expect(text).toContain('Chạy thử không đạt')
    expect(text).toContain('UNAUTHORIZED')
  })

  it('gives refusals the message of their code', () => {
    expect(rebootResultText('not_sent', 'DEVICE_REBOOT_TOO_SOON', null, false)).toBe(rebootMessage('DEVICE_REBOOT_TOO_SOON', null))
    expect(rebootResultText('rejected', 'DEVICE_REJECTED', 'confirm_required', false)).toBe('Thiết bị từ chối mã xác nhận')
  })
})
