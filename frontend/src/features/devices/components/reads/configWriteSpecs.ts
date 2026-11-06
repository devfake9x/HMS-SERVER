import type { ReadState } from '@/core/commands/useDeviceReads'
import type { WriteOutcome } from '@/core/commands/useDeviceWrite'
import { channelRecord, differingFields } from './adcData'
import type { Verification, WriteSpec } from './useConfigWrite'

const unavailable: Verification = { status: 'unavailable', text: 'Không đọc lại được' }

const show = (value: unknown): string => value === undefined ? '—' : typeof value === 'string' ? value : JSON.stringify(value)

function dataOf(state: ReadState | undefined): Record<string, unknown> | null {
  return state?.kind === 'ok' && state.data && typeof state.data === 'object' && !Array.isArray(state.data)
    ? state.data as Record<string, unknown>
    : null
}

/** The write's own reply data (spec §6.1: applied_immediately, reboot_recommended). */
function replyOf(outcome: WriteOutcome): Record<string, unknown> {
  return outcome.data && typeof outcome.data === 'object' ? outcome.data as Record<string, unknown> : {}
}

export function hostnameSpec(from: string, to: string): WriteSpec {
  return {
    command: { type: 'system', action: 'set_hostname', payload: { hostname: to } },
    confirm: { title: 'Đổi hostname', from, to },
    rereadSteps: [{ type: 'system', action: 'get_hostname' }],
    verify: (states, outcome) => {
      const actual = dataOf(states['system.get_hostname'])?.hostname
      if (typeof actual !== 'string') return unavailable
      if (actual === to) return { status: 'match', text: `Hostname hiện tại: ${actual}` }
      // A device that saved the name without applying it still reports the old one: not a mismatch.
      return { status: replyOf(outcome).applied_immediately === false ? 'pending_apply' : 'mismatch', text: actual }
    },
    notes: outcome => replyOf(outcome).reboot_recommended === true
      ? ['Thiết bị khuyên khởi động lại; reboot chưa mở trên server']
      : [],
  }
}

export function timezoneSpec(from: string, to: string): WriteSpec {
  return {
    command: { type: 'system', action: 'set_timezone', payload: { timezone: to } },
    confirm: { title: 'Đổi múi giờ', from, to },
    rereadSteps: [{ type: 'system', action: 'get_time' }],
    checkClock: true,
    verify: states => {
      const actual = dataOf(states['system.get_time'])?.timezone
      if (typeof actual !== 'string') return unavailable
      return actual === to
        ? { status: 'match', text: `Múi giờ hiện tại: ${actual}` }
        : { status: 'mismatch', text: actual }
    },
  }
}

export function ntpSyncSpec(currentSynced: unknown): WriteSpec {
  return {
    command: { type: 'system', action: 'sync_ntp', payload: {} },
    confirm: {
      title: 'Đồng bộ giờ qua NTP',
      from: currentSynced === true ? 'Đã đồng bộ NTP' : currentSynced === false ? 'Chưa đồng bộ NTP' : 'Chưa rõ',
      to: 'Đồng bộ lại ngay',
    },
    rereadSteps: [{ type: 'system', action: 'get_time' }],
    checkClock: true,
    verify: states => {
      const synced = dataOf(states['system.get_time'])?.ntp_synced
      if (typeof synced !== 'boolean') return unavailable
      return synced
        ? { status: 'match', text: 'Đã đồng bộ NTP: Có' }
        : { status: 'mismatch', text: 'chưa đồng bộ NTP (ntp_synced: Không)' }
    },
  }
}

const ADC_REREAD = [{ type: 'adc', action: 'get_channels' }, { type: 'adc', action: 'get_values' }]

/** Compares the whole channel record: the written field must be new, every other field unchanged. */
function verifyChannel(ai: number, expected: Record<string, unknown>, states: Record<string, ReadState>): Verification {
  const state = states['adc.get_channels']
  if (state?.kind !== 'ok') return unavailable
  const actual = channelRecord(state, ai)
  if (!actual) return { status: 'mismatch', text: `kênh AI-${ai} không còn trong lần đọc lại` }
  const fields = differingFields(expected, actual)
  return fields.length === 0
    ? { status: 'match', text: `AI-${ai}: ${show(actual.name)}, ${actual.enabled === true ? 'bật' : 'tắt'}` }
    : { status: 'mismatch', text: `AI-${ai} ${fields.map(f => `${f}: ${show(expected[f])} → ${show(actual[f])}`).join('; ')}` }
}

/** `before` is the channel's record at the last read; type and enabled are sent back unchanged. */
export function adcRenameSpec(before: Record<string, unknown>, ai: number, name: string): WriteSpec {
  const payload: Record<string, unknown> = { ai, name }
  if (typeof before.type === 'string') payload.type = before.type
  if (typeof before.enabled === 'boolean') payload.enabled = before.enabled
  return {
    command: { type: 'adc', action: 'set_channel', payload },
    confirm: { title: `Đổi tên kênh AI-${ai}`, from: show(before.name), to: name },
    rereadSteps: ADC_REREAD,
    verify: states => verifyChannel(ai, { ...before, name }, states),
  }
}

export function adcToggleSpec(before: Record<string, unknown>, ai: number, enabled: boolean): WriteSpec {
  return {
    command: { type: 'adc', action: 'toggle_channel', payload: { ai, enabled } },
    confirm: { title: `${enabled ? 'Bật' : 'Tắt'} kênh AI-${ai}`, from: before.enabled === true ? 'Bật' : 'Tắt', to: enabled ? 'Bật' : 'Tắt' },
    rereadSteps: ADC_REREAD,
    verify: states => verifyChannel(ai, { ...before, enabled }, states),
  }
}
