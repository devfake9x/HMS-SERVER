import { describe, expect, it } from 'vitest'
import { redactSecrets } from './redact'

describe('redactSecrets', () => {
  it('redacts nested secret fields without changing safe metadata', () => {
    const redacted = redactSecrets({
      deviceId: 'd-1',
      bundleHash: 'hash',
      credentials: { password: 'secret', mqtt_username: 'user' },
      raw_envelope: 'opaque-bytes',
      items: [{ token: 'token-value', name: 'safe' }],
    })

    expect(redacted).toEqual({
      deviceId: 'd-1',
      bundleHash: 'hash',
      credentials: { password: '[REDACTED]', mqtt_username: 'user' },
      raw_envelope: '[REDACTED]',
      items: [{ token: '[REDACTED]', name: 'safe' }],
    })
  })
})
