import { describe, expect, it } from 'vitest'

// RTU Wire Protocol and Validation Contracts
export interface RtuDeviceResponse {
  id?: string
  type?: string
  action?: string
  success: boolean
  ts?: number
  data?: any
  error?: string | null
}

export function validateDoCommand(channel: number, value: boolean | number, mode: 'toggle' | 'pulse' = 'toggle', durationMs?: number) {
  if (!Number.isInteger(channel) || channel < 1 || channel > 6) {
    throw new Error('Invalid channel: must be between 1 and 6')
  }
  if (typeof value !== 'boolean' && typeof value !== 'number') {
    throw new Error('Invalid value: must be boolean or number')
  }
  if (mode === 'pulse') {
    if (!durationMs || durationMs <= 0 || durationMs > 60000) {
      throw new Error('Invalid pulse duration: must be between 1 and 60000ms')
    }
  }
  return {
    channel,
    value: typeof value === 'boolean' ? value : value !== 0,
    mode,
    durationMs: mode === 'pulse' ? durationMs : undefined
  }
}

export function parseRtuResponse(payload: unknown): { ok: boolean; error?: string; state?: boolean } {
  if (!payload || typeof payload !== 'object') {
    return { ok: false, error: 'Malformed response' }
  }
  const res = payload as RtuDeviceResponse
  if (res.success !== true) {
    return { ok: false, error: res.error || 'Command execution failed' }
  }
  const state = res.data?.state ?? res.data?.value
  return { ok: true, state: Boolean(state) }
}

export function buildMqttTopics(prefix: string, deviceId: string) {
  if (!prefix || !deviceId) throw new Error('Prefix and deviceId required')
  return {
    commandTopic: `${prefix}/${deviceId}/cmd`,
    responseTopic: `${prefix}/${deviceId}/cmd/res`,
    auditTopic: `${prefix}/${deviceId}/audit`,
    capabilitiesTopic: `${prefix}/${deviceId}/capabilities`
  }
}

describe('RTU Hardware-in-the-Loop Contract Verification', () => {
  describe('DO Command Validation', () => {
    it('accepts valid toggle command for channels 1 through 6', () => {
      for (let ch = 1; ch <= 6; ch++) {
        const cmd = validateDoCommand(ch, true, 'toggle')
        expect(cmd.channel).toBe(ch)
        expect(cmd.value).toBe(true)
        expect(cmd.mode).toBe('toggle')
      }
    })

    it('rejects out-of-boundary channels', () => {
      expect(() => validateDoCommand(0, true)).toThrow('Invalid channel')
      expect(() => validateDoCommand(7, true)).toThrow('Invalid channel')
      expect(() => validateDoCommand(-1, true)).toThrow('Invalid channel')
      expect(() => validateDoCommand(1.5, true)).toThrow('Invalid channel')
    })

    it('validates pulse mode and duration', () => {
      const pulseCmd = validateDoCommand(1, true, 'pulse', 500)
      expect(pulseCmd.durationMs).toBe(500)
      expect(() => validateDoCommand(1, true, 'pulse', 0)).toThrow('Invalid pulse duration')
      expect(() => validateDoCommand(1, true, 'pulse', 70000)).toThrow('Invalid pulse duration')
    })
  })

  describe('RTU Response Envelope Parsing', () => {
    it('correctly parses successful wire response from firmware', () => {
      const wirePayload = {
        id: 'c2-870c-1790789566',
        type: 'control',
        action: 'control.set_output',
        success: true,
        ts: 1790789566,
        data: { channel: 1, state: true, mode: 'toggle', source: 'http' },
        error: null
      }
      const parsed = parseRtuResponse(wirePayload)
      expect(parsed.ok).toBe(true)
      expect(parsed.state).toBe(true)
      expect(parsed.error).toBeUndefined()
    })

    it('handles failure response with firmware error message', () => {
      const errorPayload = {
        id: 'c2-870c-1790789566',
        success: false,
        error: 'GPIO not available (PSRAM conflict)'
      }
      const parsed = parseRtuResponse(errorPayload)
      expect(parsed.ok).toBe(false)
      expect(parsed.error).toBe('GPIO not available (PSRAM conflict)')
    })

    it('safely handles malformed non-JSON or null payloads', () => {
      expect(parseRtuResponse(null).ok).toBe(false)
      expect(parseRtuResponse('invalid').ok).toBe(false)
      expect(parseRtuResponse(undefined).ok).toBe(false)
    })
  })

  describe('MQTT Topic Derivation', () => {
    it('generates exact hardware topic contracts for ESP32 RTU', () => {
      const topics = buildMqttTopics('hms_toan_lab', 'gateway_870c')
      expect(topics.commandTopic).toBe('hms_toan_lab/gateway_870c/cmd')
      expect(topics.responseTopic).toBe('hms_toan_lab/gateway_870c/cmd/res')
      expect(topics.auditTopic).toBe('hms_toan_lab/gateway_870c/audit')
      expect(topics.capabilitiesTopic).toBe('hms_toan_lab/gateway_870c/capabilities')
    })
  })
})
