import { describe, it, expect } from 'vitest'
import { outputWriteLock, outputOwnerNote, stateRefusal } from './outputLocks'

describe('outputWriteLock', () => {
  it.each([
    [{ gpioOk: false }, 'GPIO không dùng được'],
    [{ enabled: false }, 'Kênh tắt trong cấu hình'],
    [{ forced: true }, 'Đang force'],
    [{ owner: 'force' as const }, 'Đang force'],
    [{ owner: 'level_rule' as const, ownerId: '7' }, 'Luật #7 giữ kênh'],
    [{ owner: 'level_rule' as const, ownerId: null }, 'Luật giữ kênh'],
  ])('locks %j with its reason', (channel, reason) => {
    expect(outputWriteLock(channel)).toBe(reason)
  })

  it('takes the reasons in the order gpio, config, force, rule', () => {
    expect(outputWriteLock({ gpioOk: false, enabled: false, forced: true, owner: 'level_rule' })).toBe('GPIO không dùng được')
    expect(outputWriteLock({ enabled: false, forced: true })).toBe('Kênh tắt trong cấu hình')
    expect(outputWriteLock({ forced: true, owner: 'level_rule' })).toBe('Đang force')
  })

  it.each([
    [{ owner: 'none' as const }],
    [{ owner: 'event_rule' as const }],
    [{ owner: 'trigger' as const }],
    [{ owner: 'unknown' as const }],
    [{ owner: null }],
    [{}],
    [{ enabled: true, gpioOk: true, forced: false }],
  ])('does not lock %j', channel => {
    expect(outputWriteLock(channel)).toBeNull()
  })
})

describe('outputOwnerNote', () => {
  it('notes a rule or a trigger that may change the channel', () => {
    expect(outputOwnerNote({ owner: 'event_rule' })).toBe('Luật/trigger có thể đổi kênh sau lệnh tay')
    expect(outputOwnerNote({ owner: 'trigger' })).toBe('Luật/trigger có thể đổi kênh sau lệnh tay')
  })

  it('says nothing for the other owners', () => {
    for (const owner of ['none', 'force', 'level_rule', 'unknown', null] as const)
      expect(outputOwnerNote({ owner })).toBeNull()
  })
})

describe('stateRefusal', () => {
  it.each([
    ['gpio_unavailable', 'GPIO không dùng được: thiết bị không đổi relay'],
    ['channel_disabled', 'Kênh tắt trong cấu hình: thiết bị không đổi relay'],
    ['channel_forced', 'Đang force: thiết bị không đổi relay'],
    ['owned_by_rule', 'Luật giữ kênh: thiết bị không đổi relay'],
  ])('translates %s', (reason, sentence) => {
    expect(stateRefusal(`ESP_ERR_INVALID_STATE: ${reason}`, null)).toBe(sentence)
  })

  it('names the rule from the reply data, as a number or a string', () => {
    expect(stateRefusal('ESP_ERR_INVALID_STATE: owned_by_rule', { rule_id: 7 })).toBe('Luật #7 giữ kênh: thiết bị không đổi relay')
    expect(stateRefusal('ESP_ERR_INVALID_STATE: owned_by_rule', { rule_id: '7' })).toBe('Luật #7 giữ kênh: thiết bị không đổi relay')
  })

  it('says a rule holds the channel when the reply names none', () => {
    expect(stateRefusal('ESP_ERR_INVALID_STATE: owned_by_rule', { error: 'owned_by_rule', channel: 6 }))
      .toBe('Luật giữ kênh: thiết bị không đổi relay')
  })

  it('reads a bare reason', () => {
    expect(stateRefusal('owned_by_rule', { rule_id: 3 })).toBe('Luật #3 giữ kênh: thiết bị không đổi relay')
  })

  it.each([
    ['ESP_ERR_INVALID_STATE: busy'],
    ['LANE_FULL'],
    ['UNAUTHORIZED'],
    ['ESP_ERR_INVALID_ARG: channel must be an integer >= 1'],
    ['ESP_ERR_INVALID_STATE: owned_by_rule_x'],
    [null],
    [''],
  ])('leaves %j alone', detail => {
    expect(stateRefusal(detail, { rule_id: 7 })).toBeNull()
  })
})
