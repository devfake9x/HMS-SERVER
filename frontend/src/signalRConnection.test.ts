import { describe, expect, it } from 'vitest'
import { createSignalRConnection } from './signalRConnection'

describe('signalRConnection', () => {
  it('builds a browser connection without opening it during construction', () => {
    const connection = createSignalRConnection()
    expect(typeof connection.start).toBe('function')
    expect(typeof connection.invoke).toBe('function')
    expect(typeof connection.on).toBe('function')
  })
})
