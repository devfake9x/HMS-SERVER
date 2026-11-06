import { TIMEZONES } from '@/core/devices/timezones'

/**
 * Client-side mirror of the server's payload rules for the config writes
 * (src/Hms.Server.Application/DeviceLink/ConfigCommandPolicy.cs). It only spares a round trip;
 * the server stays the judge and answers COMMAND_PAYLOAD_INVALID. Each validator returns the
 * message to show, or null when the value is acceptable.
 */

// JS `$` (no `m` flag) only matches at the very end, so a trailing newline is rejected as on the server.
const HOSTNAME = /^[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?$/
const ADC_TYPE = /^[A-Za-z0-9_.-]+$/
// Control, format, line/paragraph separator, private use, surrogate and unassigned characters.
const FORBIDDEN_NAME_CHARS = /[\p{Cc}\p{Cf}\p{Zl}\p{Zp}\p{Co}\p{Cs}\p{Cn}]/u
// Letters, symbols and marks that render as nothing (same list as ConfigCommandPolicy.IsInvisible).
const INVISIBLE_NAME_CHARS = /[͏ᅟᅠ឴឵⠀ㅤﾠ᠋-᠏︀-️\u{E0100}-\u{E01EF}]/u

export const HOSTNAME_MAX_BYTES = 63
export const ADC_NAME_MAX_BYTES = 32
export const ADC_TYPE_MAX_BYTES = 32

export function utf8Bytes(text: string): number {
  return new TextEncoder().encode(text).length
}

export function validateHostname(value: string): string | null {
  if (value === '' || utf8Bytes(value) > HOSTNAME_MAX_BYTES) return 'Hostname dài 1 đến 63 ký tự'
  return HOSTNAME.test(value)
    ? null
    : 'Hostname chỉ gồm chữ, số và dấu gạch ngang, không bắt đầu hoặc kết thúc bằng dấu gạch ngang'
}

export function validateTimezone(value: string): string | null {
  return TIMEZONES.includes(value) ? null : 'Múi giờ không nằm trong danh sách được hỗ trợ'
}

export function validateChannelName(value: string): string | null {
  if (value.trim() === '') return 'Tên kênh không được để trống'
  if (utf8Bytes(value) > ADC_NAME_MAX_BYTES) return 'Tên kênh tối đa 32 byte (UTF-8)'
  if (FORBIDDEN_NAME_CHARS.test(value) || INVISIBLE_NAME_CHARS.test(value))
    return 'Tên kênh chứa ký tự điều khiển hoặc ký tự không hiển thị'
  return null
}

export function validateChannelType(value: string): string | null {
  if (value === '' || utf8Bytes(value) > ADC_TYPE_MAX_BYTES || !ADC_TYPE.test(value)) return 'Loại kênh không hợp lệ'
  return null
}

export function isValidAi(value: unknown): value is number {
  return typeof value === 'number' && Number.isInteger(value) && value >= 1 && value <= 16
}
