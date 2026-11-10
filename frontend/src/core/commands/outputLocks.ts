import type { OutputChannelDto } from '@/core/api/adminApi'

type LockFields = Pick<OutputChannelDto, 'enabled' | 'gpioOk' | 'forced' | 'owner' | 'ownerId'>

const GPIO = 'GPIO không dùng được'
const DISABLED = 'Kênh tắt trong cấu hình'
const FORCED = 'Đang force'
const ruleLabel = (ruleId: string | null | undefined) => (ruleId ? `Luật #${ruleId} giữ kênh` : 'Luật giữ kênh')

/**
 * Why the device will refuse a manual write on this channel, from the last read; null when it will not
 * (or when it did not say). Order: GPIO, configuration, force, level rule. Pulse is locked on a rule-held
 * channel on purpose: the rule sets the level again within a second and the spec does not say whether
 * a pulse is refused there (get_status fields: L797; refusals: L827-831).
 */
export function outputWriteLock(ch: LockFields): string | null {
  if (ch.gpioOk === false) return GPIO
  if (ch.enabled === false) return DISABLED
  if (ch.forced === true || ch.owner === 'force') return FORCED
  if (ch.owner === 'level_rule') return ruleLabel(ch.ownerId)
  return null
}

/** A rule or trigger that does not refuse manual writes but may change the channel afterwards. */
export function outputOwnerNote(ch: Pick<OutputChannelDto, 'owner'>): string | null {
  return ch.owner === 'event_rule' || ch.owner === 'trigger' ? 'Luật/trigger có thể đổi kênh sau lệnh tay' : null
}

const REFUSAL = /(?:^|:\s*)(gpio_unavailable|channel_disabled|channel_forced|owned_by_rule)$/

function ruleIdOf(replyData: unknown): string | null {
  if (!replyData || typeof replyData !== 'object') return null
  const id = (replyData as Record<string, unknown>).rule_id
  if (typeof id === 'string' && id !== '') return id
  return typeof id === 'number' && Number.isFinite(id) ? String(id) : null
}

/**
 * The sentence for a device refusal that says the relay did not change (`error` or `error: data.error`
 * of the result detail); null for any other detail, which stays as the raw text.
 */
export function stateRefusal(detail: string | null | undefined, replyData: unknown): string | null {
  const reason = detail ? REFUSAL.exec(detail)?.[1] : undefined
  if (!reason) return null
  const label = reason === 'gpio_unavailable' ? GPIO
    : reason === 'channel_disabled' ? DISABLED
      : reason === 'channel_forced' ? FORCED
        : ruleLabel(ruleIdOf(replyData))
  return `${label}: thiết bị không đổi relay`
}
