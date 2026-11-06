import { describe, it, expect } from 'vitest'
import { isValidAi, validateChannelName, validateHostname, validateTimezone } from './configRules'

// Code points are built at run time so the source file holds no invisible characters.
const ch = (codePoint: number) => String.fromCharCode(codePoint)

describe('validateHostname', () => {
  it.each(['a', 'gw-1', 'Maitech-RTU', 'a'.repeat(63), '1abc', 'a1'])('accepts %s', v => {
    expect(validateHostname(v)).toBeNull()
  })

  it.each([
    ['', 'empty'],
    ['a'.repeat(64), '64 chars'],
    ['-a', 'leading dash'],
    ['a-', 'trailing dash'],
    ['a_b', 'underscore'],
    ['a.b', 'dot'],
    ['a b', 'space'],
    ['gw\n', 'trailing newline'],
    ['gwé', 'non-ascii'],
    [`gw${ch(0x202e)}`, 'bidi override'],
  ])('rejects %j (%s)', v => {
    expect(validateHostname(v)).not.toBeNull()
  })
})

describe('validateTimezone', () => {
  it('accepts exactly the supported values', () => {
    expect(validateTimezone('ICT-7')).toBeNull()
    expect(validateTimezone('UTC0')).toBeNull()
    expect(validateTimezone('utc0')).not.toBeNull()
    expect(validateTimezone('Asia/Ho_Chi_Minh')).not.toBeNull()
    expect(validateTimezone('')).not.toBeNull()
  })
})

describe('validateChannelName', () => {
  it.each(['AI-1', 'Áp suất', 'Nhiệt độ bồn 2', 'a', 'x'.repeat(32), 'ạ'.repeat(10)])('accepts %s', v => {
    expect(validateChannelName(v)).toBeNull()
  })

  it.each([
    ['', 'empty'],
    ['   ', 'whitespace only'],
    [`${ch(0xa0)}\t`, 'nbsp and tab'],
    ['x'.repeat(33), '33 bytes'],
    ['ạ'.repeat(11), '33 UTF-8 bytes in 11 chars'],
    [`a${ch(0x202e)}b`, 'right-to-left override'],
    [`a${ch(0x200b)}b`, 'zero-width space'],
    [`a${ch(0)}b`, 'NUL'],
    ['a\nb', 'newline'],
    [`a${ch(0x2028)}b`, 'line separator'],
    [`a${ch(0xe000)}b`, 'private use'],
    [`a${ch(0xd800)}b`, 'lone surrogate'],
    [`a${ch(0x0378)}b`, 'unassigned'],
    [ch(0x3164), 'Hangul filler'],
    [`AI${ch(0x115f)}1`, 'Hangul choseong filler'],
    [`${ch(0x2800)}${ch(0x2800)}`, 'braille blank'],
    [`A${ch(0x034f)}I`, 'combining grapheme joiner'],
    [`AI${ch(0xfe0f)}`, 'variation selector'],
    [`${ch(0xffa0)}x`, 'halfwidth Hangul filler'],
  ])('rejects %j (%s)', v => {
    expect(validateChannelName(v)).not.toBeNull()
  })
})

describe('isValidAi', () => {
  it('accepts integers 1..16 only', () => {
    expect(isValidAi(1)).toBe(true)
    expect(isValidAi(16)).toBe(true)
    for (const bad of [0, 17, 1.5, '1', null, undefined, NaN]) expect(isValidAi(bad)).toBe(false)
  })
})
